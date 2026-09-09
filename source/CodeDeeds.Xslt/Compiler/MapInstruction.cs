using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// <c>xsl:map</c>, which builds one map out of whatever its content produced.
    /// </summary>
    /// <remarks>
    /// The XPath map constructor <c>map { … }</c> takes a fixed list of entries written out; this takes a
    /// sequence constructor, so the entries can come from an <c>xsl:for-each</c>, an
    /// <c>xsl:apply-templates</c>, or a choice. That is the whole reason the instruction exists alongside
    /// the expression — a map whose shape depends on the document cannot be written as a literal.
    /// </remarks>
    internal sealed class MapInstruction : Instruction
    {
        private readonly Instruction[] m_body;

        /// <summary>Initializes a map instruction.</summary>
        /// <param name="body">The content, which must produce maps and nothing else.</param>
        public MapInstruction(Instruction[] body)
        {
            m_body = body;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            List<KeyValuePair<XPathValue, XPathValue>> entries = new();

            foreach (XPathValue item in XdmSequence.Items(
                VariableInstruction.CaptureSequence(m_body, ref context, runtime)))
            {
                if (item.Kind != XPathValueKind.Map)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTTE3375,
                        "The content of an xsl:map has to be maps and nothing else, and this produced "
                        + $"{item.Kind}. Every entry comes from an xsl:map-entry or from an expression that "
                        + "is already a map.");
                }

                entries.AddRange(item.AsMap().Entries);
            }

            // Two entries of one key is an error rather than a silent choice: nothing in the instruction
            // says which of them was meant, and quietly keeping one would make the answer depend on the
            // order the content happened to run in.
            runtime.Output.TryAppendValue(XPathValue.FromMap(BuildRejectingDuplicates(entries)));
        }

        /// <summary>Builds the map, naming the repeated key rather than choosing between the two values.</summary>
        private static XdmMap BuildRejectingDuplicates(
            List<KeyValuePair<XPathValue, XPathValue>> entries)
        {
            XdmMap built = XdmMap.Empty;

            foreach (KeyValuePair<XPathValue, XPathValue> entry in entries)
            {
                if (built.Contains(entry.Key))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE3365,
                        $"Two entries of this xsl:map have the key '{entry.Key.ToStringValue()}'.");
                }

                built = built.Put(entry.Key, entry.Value);
            }

            return built;
        }
    }

    /// <summary>
    /// <c>xsl:map-entry</c>, which produces a map of one entry for its <c>xsl:map</c> to absorb.
    /// </summary>
    /// <remarks>
    /// A one-entry map rather than some pair type of its own, which is what lets the entries be produced by
    /// anything that produces a sequence: <c>xsl:map</c> takes maps, and an entry is simply the smallest of
    /// them.
    /// </remarks>
    internal sealed class MapEntryInstruction : Instruction
    {
        private readonly Expr m_key;
        private readonly Expr? m_select;
        private readonly Instruction[] m_body;

        /// <summary>Initializes a map-entry instruction.</summary>
        /// <param name="key">The key, which must be a single atomic value.</param>
        /// <param name="select">The value, where given as an expression.</param>
        /// <param name="body">The value, where given as content.</param>
        public MapEntryInstruction(Expr key, Expr? select, Instruction[] body)
        {
            m_key = key;
            m_select = select;
            m_body = body;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            List<XPathValue> keys = XdmSequence.Atomize(XdmSequence.Items(m_key.Evaluate(ref context)));

            if (keys.Count != 1)
            {
                // XPTY0004 and not XTTE3375: the key attribute has a required type of xs:anyAtomicType and
                // is converted to it by the ordinary rules, so what fails is that conversion. XTTE3375 is
                // for the other thing — an xsl:map whose content is not maps.
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"The key of an xsl:map-entry is one atomic value, and this one gave {keys.Count}.");
            }

            XPathValue value = m_select is not null
                ? m_select.Evaluate(ref context)
                : VariableInstruction.CaptureSequence(m_body, ref context, runtime);

            runtime.Output.TryAppendValue(XPathValue.FromMap(XdmMap.Entry(keys[0], value)));
        }
    }
}
