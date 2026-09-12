using System.Globalization;
using System.Text;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>A place in a stylesheet module: which module, and the line and column in it.</summary>
    internal sealed record SourceLocation(string? Module, int Line, int Column);

    /// <summary>
    /// A compiled XSLT instruction.
    /// </summary>
    /// <remarks>
    /// Instructions form the second half of the intermediate representation, alongside <see cref="Expr"/>.
    /// The interpreter implements <see cref="Execute"/>; the IL backend will add emission to the same types.
    /// </remarks>
    internal abstract class Instruction
    {
        /// <summary>Runs this instruction.</summary>
        /// <param name="context">The current context. Instructions may copy it, but must not disturb the caller's.</param>
        /// <param name="runtime">The transformation in progress.</param>
        public abstract void Execute(ref DynamicContext context, XsltRuntime runtime);

        /// <summary>
        /// Gets or sets where this instruction stands in its stylesheet module, or null where that was not
        /// recorded.
        /// </summary>
        /// <remarks>
        /// What <c>$err:module</c>, <c>$err:line-number</c> and <c>$err:column-number</c> report in an
        /// <c>xsl:catch</c>: the element the instruction was compiled from. Set by the compiler after the
        /// instruction is built, since every instruction has one and no constructor should have to ask.
        /// </remarks>
        internal SourceLocation? Location { get; set; }

        /// <summary>
        /// Tells this instruction that it is the last thing its template does, so that a call it makes can
        /// be made in place of the template's own invocation rather than beneath it.
        /// </summary>
        /// <remarks>
        /// Most instructions have nothing to do with the news. A call passes it to the call, and a
        /// conditional passes it to the last instruction of each branch, because whichever branch runs, that
        /// instruction is still the last thing the template does. Anything that has work left after its body
        /// — an element to close, a loop to continue, a value to capture — stops it here.
        /// </remarks>
        internal virtual void MarkTailPosition()
        {
        }

        /// <summary>Marks the last instruction of a sequence constructor as the last thing its template does.</summary>
        /// <param name="body">The sequence constructor.</param>
        internal static void MarkTailPosition(Instruction[] body)
        {
            if (body.Length > 0)
            {
                body[body.Length - 1].MarkTailPosition();
            }
        }

        /// <summary>Runs a sequence of instructions in order.</summary>
        /// <param name="body">The instructions to run.</param>
        /// <param name="context">The context to run them in.</param>
        /// <param name="runtime">The transformation in progress.</param>
        public static void ExecuteAll(Instruction[] body, ref DynamicContext context, XsltRuntime runtime)
        {
            int at = 0;

            try
            {
                for (; at < body.Length; at++)
                {
                    body[at].Execute(ref context, runtime);

                    // xsl:next-iteration and xsl:break end the sequence constructor they stand in and every
                    // one between them and the xsl:iterate. A flag read here unwinds through however many
                    // that is, which an exception would also do — at a cost paid per iteration rather than
                    // per stylesheet that never uses the feature. The branch predicts perfectly for every
                    // stylesheet that does not, this being a field that is never written in one.
                    if (runtime.LoopSignal != LoopSignal.None)
                    {
                        return;
                    }
                }
            }
            catch (XsltException error) when (error.Line == 0 && at < body.Length && body[at].Location is not null)
            {
                // The innermost sequence constructor the error came through knows which of its instructions
                // was running, and that is where the error stands: an error in a select expression is at
                // the instruction carrying it. Filled once, on the way out, and left alone by every sequence
                // constructor above — which is what the filter checks, so that the common case of an error
                // with its place already known does not even enter the handler.
                SourceLocation location = body[at].Location!;
                error.Module = location.Module;
                error.Line = location.Line;
                error.Column = location.Column;
                throw;
            }
        }

        /// <summary>Whether the text is a name XML would allow, with no colon in it.</summary>
        /// <remarks>
        /// Asked wherever a name is computed while the transformation runs and so cannot have been checked
        /// when the stylesheet was read: the prefix an <c>xsl:namespace</c> binds, the error code an
        /// <c>xsl:message</c> names.
        /// </remarks>
        /// <param name="name">The text to check.</param>
        protected static bool IsNCName(string name)
        {
            if (name.Length == 0 || !System.Xml.XmlConvert.IsStartNCNameChar(name[0]))
            {
                return false;
            }

            for (int i = 1; i < name.Length; i++)
            {
                if (!System.Xml.XmlConvert.IsNCNameChar(name[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Runs a sequence of instructions and returns the text they produce, discarding any markup. This is
        /// the string-value of the content, which is what <c>xsl:attribute</c> and <c>xsl:comment</c> need.
        /// </summary>
        protected static string CaptureText(
            Instruction[] body,
            ref DynamicContext context,
            XsltRuntime runtime,
            string separator = " ")
        {
            StringCaptureTarget capture = new StringCaptureTarget(separator);
            OutputTarget previous = runtime.Output;
            runtime.Output = capture;

            try
            {
                ExecuteAll(body, ref context, runtime);
            }
            finally
            {
                runtime.Output = previous;
            }

            return capture.ToString();
        }

        /// <summary>
        /// Renders a value as the text an instruction writes: every item of it, separated.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The separator defaults to a space wherever XSLT 2.0 lets a <c>select</c> stand in for content,
        /// which is what makes a multi-item selection read as a list rather than as a run-on string.
        /// </para>
        /// <para>
        /// The value is <b>atomized</b> first, which is what the specification says for all four instructions
        /// that use this — <c>xsl:value-of</c>, <c>xsl:attribute</c>, <c>xsl:comment</c> and
        /// <c>xsl:processing-instruction</c>. For a node it changes nothing, its typed value here being its
        /// string value. What it settles is an <b>array</b>: XPath 3.1 made arrays atomizable, so
        /// <c>[1, 2]</c> contributes two values rather than being a thing with no string-value. A map still
        /// has none, and still says so.
        /// </para>
        /// </remarks>
        /// <param name="value">The value to render.</param>
        /// <param name="separator">What to put between items.</param>
        protected static string Join(XPathValue value, string separator)
        {
            return XdmSequence.SimpleContent(value, separator);
        }
    }

    /// <summary>A literal result element — an element written directly in the stylesheet.</summary>
    internal sealed class LiteralElementInstruction : Instruction
    {
        /// <summary>Whether the element passes its namespaces on to the children built inside it.</summary>
        private readonly bool m_inheritNamespaces;

        private readonly string m_prefix;
        private readonly string m_namespaceUri;
        private readonly string m_localName;
        private readonly (string Prefix, string Uri)[] m_namespaces;
        private readonly LiteralAttribute[] m_attributes;
        private readonly ExpandedName[] m_attributeSets;
        private readonly Instruction[] m_body;

        /// <summary>Initializes a literal result element.</summary>
        public LiteralElementInstruction(
            string prefix,
            string namespaceUri,
            string localName,
            (string Prefix, string Uri)[] namespaces,
            LiteralAttribute[] attributes,
            Instruction[] body,
            ExpandedName[]? attributeSets = null,
            bool inheritNamespaces = true)
        {
            m_inheritNamespaces = inheritNamespaces;
            m_prefix = prefix;
            m_namespaceUri = namespaceUri;
            m_localName = localName;
            m_namespaces = namespaces;
            m_attributes = attributes;
            m_body = body;
            m_attributeSets = attributeSets ?? Array.Empty<ExpandedName>();
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            runtime.Output.StartElement(m_prefix, m_namespaceUri, m_localName);

            if (!m_inheritNamespaces)
            {
                runtime.Output.MarkNoInheritedNamespaces();
            }

            // Namespaces in scope on the element in the stylesheet become namespace nodes in the result,
            // unless excluded; the writer drops any that are already in scope.
            foreach ((string prefix, string uri) in m_namespaces)
            {
                runtime.Output.WriteNamespaceDeclaration(prefix, uri);
            }

            // Attribute sets come first, so the element's own attributes override anything they supplied.
            AttributeSetApplier.Apply(m_attributeSets, ref context, runtime);

            foreach (LiteralAttribute attribute in m_attributes)
            {
                runtime.Output.WriteAttribute(
                    attribute.Prefix,
                    attribute.NamespaceUri,
                    attribute.LocalName,
                    attribute.Value.Evaluate(ref context));
            }

            ExecuteAll(m_body, ref context, runtime);
            runtime.Output.EndElement();
        }
    }

    /// <summary>An attribute written on a literal result element, whose value may be a template.</summary>
    internal sealed class LiteralAttribute
    {
        /// <summary>Initializes a literal attribute.</summary>
        public LiteralAttribute(string prefix, string namespaceUri, string localName, AttributeValueTemplate value)
        {
            Prefix = prefix;
            NamespaceUri = namespaceUri;
            LocalName = localName;
            Value = value;
        }

        /// <summary>The prefix as written.</summary>
        public string Prefix { get; }

        /// <summary>The namespace URI.</summary>
        public string NamespaceUri { get; }

        /// <summary>The local part of the name.</summary>
        public string LocalName { get; }

        /// <summary>The value template.</summary>
        public AttributeValueTemplate Value { get; }
    }

    /// <summary>Literal text, from <c>xsl:text</c> or from character data in the stylesheet.</summary>
    internal sealed class TextInstruction : Instruction
    {
        private readonly string m_text;
        private readonly bool m_disableEscaping;

        /// <summary>Initializes a text instruction.</summary>
        public TextInstruction(string text, bool disableEscaping)
        {
            m_text = text;
            m_disableEscaping = disableEscaping;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            if (m_disableEscaping)
            {
                runtime.Output.WriteRawText(m_text);
            }
            else
            {
                runtime.Output.WriteText(m_text);
            }
        }
    }

    /// <summary>
    /// Text written in a stylesheet where <c>expand-text</c> is in force, so that the braces in it are
    /// expressions rather than characters.
    /// </summary>
    /// <remarks>
    /// XSLT 3.0's text value templates. Separate from <see cref="TextInstruction"/> rather than folded into
    /// it, because text with nothing to expand is the overwhelmingly common case and compiles back to that
    /// one: what reaches here is only text that actually has an expression in it.
    /// </remarks>
    internal sealed class TextValueTemplateInstruction : Instruction
    {
        private readonly AttributeValueTemplate m_template;
        private readonly bool m_disableEscaping;

        /// <summary>Initializes a text value template.</summary>
        /// <param name="template">The text and the expressions in it.</param>
        /// <param name="disableEscaping">Whether to write the result without escaping it.</param>
        public TextValueTemplateInstruction(AttributeValueTemplate template, bool disableEscaping)
        {
            m_template = template;
            m_disableEscaping = disableEscaping;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            string text = m_template.Evaluate(ref context);

            if (m_disableEscaping)
            {
                runtime.Output.WriteRawText(text);
            }
            else
            {
                runtime.Output.WriteText(text);
            }
        }
    }

    /// <summary><c>xsl:value-of</c>.</summary>
    internal sealed class ValueOfInstruction : Instruction
    {
        private readonly Expr? m_select;
        private readonly Instruction[]? m_body;
        private readonly bool m_disableEscaping;
        private readonly AttributeValueTemplate? m_separator;
        private readonly bool m_wholeSequence;

        /// <summary>Initializes a value-of instruction under a particular version's rules.</summary>
        /// <param name="select">The value to write, or <see langword="null"/> where the content says.</param>
        /// <param name="body">The content, where there is no <c>select</c>.</param>
        /// <param name="disableEscaping">Whether to write the text without escaping it.</param>
        /// <param name="separator">What to put between items, when several are written.</param>
        /// <param name="wholeSequence">
        /// Whether every item is written rather than only the first. XSLT 1.0 takes the string-value of the
        /// first node and discards the rest; XSLT 2.0 atomizes the whole sequence and joins it. This is the
        /// most visible of the incompatibilities between the two, so it follows the version in scope.
        /// </param>
        public ValueOfInstruction(
            Expr? select,
            Instruction[]? body,
            bool disableEscaping,
            AttributeValueTemplate? separator,
            bool wholeSequence)
        {
            m_select = select;
            m_body = body;
            m_disableEscaping = disableEscaping;
            m_separator = separator;
            m_wholeSequence = wholeSequence;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            // XSLT 2.0 lets the content stand in for the select, and the content is a sequence constructor
            // rather than a tree: <xsl:value-of><xsl:sequence select="1 to 3"/></xsl:value-of> writes three
            // items joined, not the text of a fragment.
            string text;

            if (m_select is not null && m_separator is null && m_select.ReturnsNodeSet
                && !m_select.MaySpanDocuments)
            {
                text = SelectedNodesText(ref context);
            }
            else
            {
                XPathValue value = m_select is not null
                    ? m_select.Evaluate(ref context)
                    : VariableInstruction.CaptureSequence(m_body, ref context, runtime, becomesAString: true);

                // Under 1.0's rules the first item is the whole answer: this wrote the string-value of the
                // first node of a node-set there, and a 2.0 expression yielding a sequence is read the same
                // way. How that item is written is not part of what the mode restores, so it is the 2.0
                // lexical form either way.
                text = m_wholeSequence
                    ? JoinItems(value, ref context)
                    : XdmSequence.FirstItem(value).ToCanonicalString();
            }

            if (m_disableEscaping)
            {
                runtime.Output.WriteRawText(text);
            }
            else
            {
                runtime.Output.WriteText(text);
            }
        }

        /// <remarks>
        /// The default separator differs between the two forms, and the specification says so in as many
        /// words: a space where a <c>select</c> produced the sequence, and nothing where the content did.
        /// The second is what makes an instruction whose content is <c>a</c> then <c>b</c> write <c>ab</c>
        /// rather than <c>a b</c>.
        /// </remarks>
        private string JoinItems(XPathValue value, ref DynamicContext context)
        {
            string separator = m_separator is not null
                ? m_separator.Evaluate(ref context)
                : m_select is not null ? " " : string.Empty;

            return Join(value, separator);
        }

        /// <summary>
        /// The text of a select known to yield nodes, read from the nodes themselves rather than from a
        /// node-set built to hold them.
        /// </summary>
        /// <remarks>
        /// A value-of selecting one child element is the commonest instruction in a stylesheet, run once per
        /// element written. Going through a value costs a node-set and its array, the list of items and
        /// the list of their atomized values, all for the one string the node carries; the list the nodes
        /// are collected into here is pooled, and the string is taken straight from the tree. Only where
        /// there is more than one node, and every one of them is wanted, is the general path worth building.
        /// </remarks>
        private string SelectedNodesText(ref DynamicContext context)
        {
            List<int> nodes = NodeListPool.Rent();

            try
            {
                XdmTree tree = m_select!.EvaluateNodes(ref context, nodes);

                if (nodes.Count == 0)
                {
                    return string.Empty;
                }

                if (nodes.Count == 1 || !m_wholeSequence)
                {
                    return tree.StringValueOf(nodes[0]);
                }

                return Join(XPathValue.FromNodeSet(NodeSet.FromOrderedNodes(tree, nodes)), " ");
            }
            finally
            {
                NodeListPool.Return(nodes);
            }
        }
    }

    /// <summary><c>xsl:if</c>.</summary>
    internal sealed class IfInstruction : Instruction
    {
        private readonly Expr m_test;
        private readonly Instruction[] m_body;

        /// <summary>Initializes an if instruction.</summary>
        public IfInstruction(Expr test, Instruction[] body)
        {
            m_test = test;
            m_body = body;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            if (m_test.EvaluateAsBoolean(ref context))
            {
                ExecuteAll(m_body, ref context, runtime);
            }
        }

        /// <inheritdoc/>
        internal override void MarkTailPosition()
        {
            MarkTailPosition(m_body);
        }
    }

    /// <summary><c>xsl:choose</c>, with its <c>xsl:when</c> branches and optional <c>xsl:otherwise</c>.</summary>
    internal sealed class ChooseInstruction : Instruction
    {
        private readonly Expr[] m_tests;
        private readonly Instruction[][] m_branches;
        private readonly Instruction[]? m_otherwise;

        /// <summary>Initializes a choose instruction.</summary>
        public ChooseInstruction(Expr[] tests, Instruction[][] branches, Instruction[]? otherwise)
        {
            m_tests = tests;
            m_branches = branches;
            m_otherwise = otherwise;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            for (int i = 0; i < m_tests.Length; i++)
            {
                if (m_tests[i].EvaluateAsBoolean(ref context))
                {
                    ExecuteAll(m_branches[i], ref context, runtime);
                    return;
                }
            }

            if (m_otherwise is not null)
            {
                ExecuteAll(m_otherwise, ref context, runtime);
            }
        }

        /// <inheritdoc/>
        internal override void MarkTailPosition()
        {
            foreach (Instruction[] branch in m_branches)
            {
                MarkTailPosition(branch);
            }

            if (m_otherwise is not null)
            {
                MarkTailPosition(m_otherwise);
            }
        }
    }

    /// <summary>Where upper and lower case fall relative to one another when text is otherwise equal.</summary>
    internal enum SortCaseOrder : byte
    {
        /// <summary>Leave case to the comparison in force; <c>case-order</c> was not written.</summary>
        Unspecified = 0,

        /// <summary>Upper case sorts before lower case.</summary>
        UpperFirst = 1,

        /// <summary>Lower case sorts before upper case.</summary>
        LowerFirst = 2,
    }

    /// <summary>
    /// The ordering attributes of one <c>xsl:sort</c> — <c>order</c>, <c>data-type</c>, <c>lang</c>,
    /// <c>case-order</c> and <c>collation</c> — where any of them is an attribute value template.
    /// </summary>
    /// <remarks>
    /// Evaluated once per sort, with the focus of the instruction that sorts, which is what the specification
    /// says: the ordering is a property of the sort and not of the items. A key none of whose attributes is
    /// computed carries no ordering and is used as it was compiled.
    /// </remarks>
    internal sealed record SortOrdering(
        AttributeValueTemplate? Order,
        AttributeValueTemplate? DataType,
        AttributeValueTemplate? Language,
        AttributeValueTemplate? CaseOrder,
        AttributeValueTemplate? Collation);

    /// <summary>A sort key from <c>xsl:sort</c>.</summary>
    /// <remarks>
    /// <para>
    /// A key is one value per item — atomized, a node giving its string value as an untyped value — and the
    /// values are compared by what they are: numbers as numbers, dates as dates, text by the collation or
    /// the language and case order asked for. Two that cannot be compared, an untyped value and a date say,
    /// are <c>XTDE1030</c>. <c>data-type="number"</c> reads every key as a number and <c>data-type="text"</c>
    /// every key as text, and XSLT 1.0, which had no types to sort by, reads every key as text unless told
    /// otherwise. The empty sequence sorts before everything.
    /// </para>
    /// <para>
    /// Every ordering attribute may be an attribute value template, settled by <see cref="Resolve"/> once per
    /// sort; a value that is not one the attribute may take is <c>XTDE0030</c> then, where a written one
    /// was refused when the stylesheet was read.
    /// </para>
    /// </remarks>
    internal sealed class SortKey
    {
        private readonly CultureInfo? m_culture;
        private readonly SortCaseOrder m_caseOrder;
        private readonly bool m_firstItemOnly;
        private readonly bool m_asText;
        private readonly Collation? m_collation;
        private readonly SortOrdering? m_ordering;

        /// <summary>Initializes a sort key.</summary>
        /// <param name="select">The expression producing the value to sort on.</param>
        /// <param name="numeric">Whether values are compared as numbers, which is <c>data-type="number"</c>.</param>
        /// <param name="descending">Whether the ordering is reversed.</param>
        /// <param name="culture">The language to collate text in, or <see langword="null"/> for ordinal.</param>
        /// <param name="caseOrder">Where case falls when text is otherwise equal.</param>
        /// <param name="firstItemOnly">
        /// Whether a sequence contributes only its first item, as 1.0 has it. A key that takes only its
        /// first item is a key in a backwards-compatible stylesheet, where <c>data-type</c> defaults to
        /// text — so it settles the comparison as well as the value.
        /// </param>
        /// <param name="collation">The collation text is compared by, where one was named.</param>
        /// <param name="ordering">The ordering attributes still to be computed, where any is.</param>
        /// <param name="asText">
        /// Whether values are compared as text whatever they are, which is <c>data-type="text"</c> and every
        /// key under XSLT 1.0.
        /// </param>
        public SortKey(
            Expr select,
            bool numeric,
            bool descending,
            CultureInfo? culture = null,
            SortCaseOrder caseOrder = SortCaseOrder.Unspecified,
            bool firstItemOnly = false,
            Collation? collation = null,
            SortOrdering? ordering = null,
            bool asText = false)
        {
            Select = select;
            Numeric = numeric;
            Descending = descending;
            m_culture = culture;
            m_caseOrder = caseOrder;
            m_firstItemOnly = firstItemOnly;
            m_collation = collation;
            m_ordering = ordering;
            m_asText = asText || firstItemOnly;
        }

        /// <summary>Gets the expression producing the value to sort on.</summary>
        public Expr Select { get; }

        /// <summary>Gets whether values are compared as numbers.</summary>
        public bool Numeric { get; }

        /// <summary>Gets whether the ordering is reversed.</summary>
        public bool Descending { get; }

        /// <summary>
        /// Finds the culture a <c>lang</c> attribute names, or <see langword="null"/> where there is none or
        /// the platform does not know it — in which case text is ordered by code point, which is an answer
        /// the specification allows.
        /// </summary>
        /// <param name="lang">The language tag, or nothing.</param>
        public static CultureInfo? CultureFor(string? lang)
        {
            if (string.IsNullOrEmpty(lang))
            {
                return null;
            }

            try
            {
                return CultureInfo.GetCultureInfo(lang);
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        /// <summary>Whether text is a language tag as <c>xs:language</c> has it: letters, then dashed runs.</summary>
        /// <param name="lang">The text.</param>
        public static bool IsLanguage(string lang)
        {
            int run = 0;
            bool first = true;

            foreach (char c in lang)
            {
                if (c == '-')
                {
                    if (run == 0)
                    {
                        return false;
                    }

                    run = 0;
                    first = false;
                    continue;
                }

                bool allowed = first ? char.IsAsciiLetter(c) : char.IsAsciiLetterOrDigit(c);
                if (!allowed || ++run > 8)
                {
                    return false;
                }
            }

            return run != 0;
        }

        /// <summary>The collation a URI names, or <c>XTDE1035</c> where this processor has none by it.</summary>
        /// <param name="uri">The collation URI.</param>
        public static Collation ResolveCollation(string uri)
        {
            try
            {
                return Collation.Resolve(uri);
            }
            catch (XsltException failed)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE1035,
                    $"'{uri}' is not a collation this processor can sort by.",
                    failed);
            }
        }

        /// <summary>
        /// The same key with another ordering, which is what an ordering attribute written as an attribute
        /// value template comes to once the template is evaluated.
        /// </summary>
        /// <param name="numeric">Whether values are compared as numbers.</param>
        /// <param name="descending">Whether the ordering is reversed.</param>
        /// <param name="culture">The language to collate text in, or <see langword="null"/> for ordinal.</param>
        /// <param name="caseOrder">Where case falls when text is otherwise equal.</param>
        /// <param name="collation">The collation text is compared by, where one was named.</param>
        public SortKey Ordered(
            bool numeric,
            bool descending,
            CultureInfo? culture,
            SortCaseOrder caseOrder,
            Collation? collation = null)
        {
            return numeric == Numeric
                && descending == Descending
                && ReferenceEquals(culture, m_culture)
                && caseOrder == m_caseOrder
                && ReferenceEquals(collation, m_collation)
                    ? this
                    : new SortKey(
                        Select, numeric, descending, culture, caseOrder, m_firstItemOnly, collation, null, m_asText);
        }

        /// <summary>The key with its computed ordering attributes settled, for one sort.</summary>
        /// <param name="context">The focus of the instruction that sorts.</param>
        public SortKey Resolve(ref DynamicContext context)
        {
            if (m_ordering is null)
            {
                return this;
            }

            bool numeric = Numeric;
            bool asText = m_asText;
            bool descending = Descending;
            CultureInfo? culture = m_culture;
            SortCaseOrder caseOrder = m_caseOrder;
            Collation? collation = m_collation;

            if (m_ordering.Order?.Evaluate(ref context) is string order)
            {
                descending = order.Trim() switch
                {
                    "ascending" => false,
                    "descending" => true,
                    _ => throw Refused("order", order),
                };
            }

            if (m_ordering.DataType?.Evaluate(ref context) is string dataType)
            {
                string trimmed = dataType.Trim();
                numeric = trimmed == "number";
                asText = trimmed == "text" || m_firstItemOnly;

                if (!numeric && !asText && !trimmed.Contains(':'))
                {
                    throw Refused("data-type", dataType);
                }
            }

            if (m_ordering.Language?.Evaluate(ref context) is string lang)
            {
                if (!IsLanguage(lang.Trim()))
                {
                    throw Refused("lang", lang);
                }

                culture = CultureFor(lang.Trim());
            }

            if (m_ordering.CaseOrder?.Evaluate(ref context) is string written)
            {
                caseOrder = written.Trim() switch
                {
                    "upper-first" => SortCaseOrder.UpperFirst,
                    "lower-first" => SortCaseOrder.LowerFirst,
                    _ => throw Refused("case-order", written),
                };
            }

            if (m_ordering.Collation?.Evaluate(ref context) is string uri)
            {
                collation = ResolveCollation(uri.Trim());
            }

            return new SortKey(
                Select, numeric, descending, culture, caseOrder, m_firstItemOnly, collation, null, asText);
        }

        private static XsltException Refused(string attribute, string value)
        {
            return XsltErrors.Error(
                XsltErrorCode.XTDE0030,
                $"'{value}' is not a value the {attribute} of xsl:sort may take. It was computed, so the "
                + "stylesheet could not have been told sooner.");
        }

        /// <summary>Every key of a sort with its computed ordering settled.</summary>
        internal static SortKey[] Resolved(SortKey[] keys, ref DynamicContext context)
        {
            SortKey[] resolved = keys;

            for (int k = 0; k < keys.Length; k++)
            {
                SortKey key = keys[k].Resolve(ref context);

                if (!ReferenceEquals(key, keys[k]))
                {
                    if (ReferenceEquals(resolved, keys))
                    {
                        resolved = (SortKey[])keys.Clone();
                    }

                    resolved[k] = key;
                }
            }

            return resolved;
        }

        /// <summary>
        /// Evaluates the key for one item: one atomic value, or the empty sequence.
        /// </summary>
        /// <remarks>
        /// A sort key yielding several values is <c>XTTE1020</c>, and under 1.0 the first is taken. A node
        /// is atomized here, so what is compared is the value and not the node.
        /// </remarks>
        /// <param name="context">The context, positioned on the item.</param>
        public XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue value = Select.Evaluate(ref context);

            if (m_firstItemOnly)
            {
                return XdmSequence.FirstItem(value);
            }

            List<XPathValue> items = XdmSequence.Atomize(XdmSequence.Items(value));

            if (items.Count > 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE1020,
                    "A sort key gave more than one value for one item of what is being sorted. One key is "
                    + "one value per item; several values would give several orderings.");
            }

            return items.Count == 0 ? XPathValue.FromSequence(XdmSequence.Empty) : items[0];
        }

        /// <summary>Compares two key values as this key orders them.</summary>
        /// <param name="left">One value.</param>
        /// <param name="right">The other.</param>
        public int Compare(XPathValue left, XPathValue right)
        {
            bool leftEmpty = Xpath2FunctionExpr.IsEmptySequence(left);
            bool rightEmpty = Xpath2FunctionExpr.IsEmptySequence(right);

            if (leftEmpty || rightEmpty)
            {
                return leftEmpty && rightEmpty ? 0 : leftEmpty ? -1 : 1;
            }

            if (Numeric)
            {
                return CompareNumbers(left.ToNumber(), right.ToNumber());
            }

            if (m_asText || (Xpath2FunctionExpr.IsText(left) && Xpath2FunctionExpr.IsText(right)))
            {
                return CompareText(left.ToStringValue(), right.ToStringValue());
            }

            // Typed, and by type: a double beside anything numeric is compared as doubles, NaN first, and
            // the rest as XPath's lt orders them. Text beside a date, or a duration beside a number, is not
            // an ordering at all.
            if (IsFloating(left.TypeCode) && IsNumber(right.TypeCode)
                || IsFloating(right.TypeCode) && IsNumber(left.TypeCode))
            {
                return CompareNumbers(left.ToNumber(), right.ToNumber());
            }

            if (!Xpath2FunctionExpr.IsText(left) && !Xpath2FunctionExpr.IsText(right)
                && XdmComparison.TryOrder(left, right, out int order))
            {
                return order;
            }

            throw XsltErrors.Error(
                XsltErrorCode.XTDE1030,
                $"Two sort key values cannot be compared: one is {XdmTypeConversion.Describe(left)} and the "
                + $"other {XdmTypeConversion.Describe(right)}. A key orders values of one type, or of types "
                + "that order against each other; data-type=\"text\" would compare them as strings.");
        }

        private static bool IsNumber(XdmTypeCode type)
        {
            return type is XdmTypeCode.Integer or XdmTypeCode.Decimal or XdmTypeCode.Float or XdmTypeCode.Double;
        }

        private static bool IsFloating(XdmTypeCode type)
        {
            return type is XdmTypeCode.Float or XdmTypeCode.Double;
        }

        /// <summary>Sorts nodes of one tree in place.</summary>
        /// <param name="nodes">The nodes.</param>
        /// <param name="keys">The keys, first the most significant.</param>
        /// <param name="context">The context the keys are evaluated in.</param>
        public static void Sort(List<int> nodes, SortKey[] keys, ref DynamicContext context)
        {
            if (keys.Length == 0 || nodes.Count < 2)
            {
                return;
            }

            keys = Resolved(keys, ref context);
            int count = nodes.Count;
            XPathValue[][] values = new XPathValue[keys.Length][];

            for (int k = 0; k < keys.Length; k++)
            {
                values[k] = new XPathValue[count];

                for (int i = 0; i < count; i++)
                {
                    DynamicContext inner = context;
                    inner.Node = nodes[i];
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    inner.Position = i + 1;
                    inner.Size = count;

                    values[k][i] = keys[k].Evaluate(ref inner);
                }
            }

            int[] order = Order(keys, values, count);
            int[] originalNodes = nodes.ToArray();

            for (int i = 0; i < count; i++)
            {
                nodes[i] = originalNodes[order[i]];
            }
        }

        /// <summary>Sorts items in place.</summary>
        /// <param name="items">The items.</param>
        /// <param name="keys">The keys, first the most significant.</param>
        /// <param name="context">The context the keys are evaluated in.</param>
        public static void Sort(List<XPathValue> items, SortKey[] keys, ref DynamicContext context)
        {
            if (keys.Length == 0 || items.Count < 2)
            {
                return;
            }

            keys = Resolved(keys, ref context);
            int count = items.Count;
            XPathValue[][] values = new XPathValue[keys.Length][];

            XsltRuntime? runtime = context.Runtime;
            XPathValue? outerCurrent = runtime?.CurrentAtomicItem;

            try
            {
                for (int k = 0; k < keys.Length; k++)
                {
                    values[k] = new XPathValue[count];

                    for (int i = 0; i < count; i++)
                    {
                        DynamicContext inner = context.WithItem(items[i]);
                        inner.CurrentNode = inner.Node;
                        inner.CurrentTree = inner.Tree;
                        inner.Position = i + 1;
                        inner.Size = count;

                        if (runtime is not null)
                        {
                            runtime.CurrentAtomicItem = inner.Node >= 0 ? null : items[i];
                        }

                        values[k][i] = keys[k].Evaluate(ref inner);
                    }
                }
            }
            finally
            {
                if (runtime is not null)
                {
                    runtime.CurrentAtomicItem = outerCurrent;
                }
            }

            int[] order = Order(keys, values, count);
            XPathValue[] original = items.ToArray();

            for (int i = 0; i < count; i++)
            {
                items[i] = original[order[i]];
            }
        }

        /// <summary>Sorts nodes that may belong to several trees, in place.</summary>
        /// <param name="entries">The nodes, each with its tree.</param>
        /// <param name="keys">The keys, first the most significant.</param>
        /// <param name="context">The context the keys are evaluated in.</param>
        public static void SortAcrossTrees(
            (int Node, XdmTree Tree)[] entries,
            SortKey[] keys,
            ref DynamicContext context)
        {
            if (keys.Length == 0 || entries.Length < 2)
            {
                return;
            }

            keys = Resolved(keys, ref context);
            int count = entries.Length;
            XPathValue[][] values = new XPathValue[keys.Length][];

            for (int k = 0; k < keys.Length; k++)
            {
                values[k] = new XPathValue[count];

                for (int i = 0; i < count; i++)
                {
                    DynamicContext inner = context.SwitchTree(entries[i].Tree, entries[i].Node);
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    inner.Position = i + 1;
                    inner.Size = count;

                    values[k][i] = keys[k].Evaluate(ref inner);
                }
            }

            int[] order = Order(keys, values, count);
            (int Node, XdmTree Tree)[] original = ((int Node, XdmTree Tree)[])entries.Clone();

            for (int i = 0; i < count; i++)
            {
                entries[i] = original[order[i]];
            }
        }

        internal static int[] Order(SortKey[] keys, XPathValue[][] values, int count)
        {
            int[] order = new int[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }

            try
            {
                Array.Sort(order, (left, right) =>
                {
                    for (int k = 0; k < keys.Length; k++)
                    {
                        int comparison = keys[k].Compare(values[k][left], values[k][right]);

                        if (comparison != 0)
                        {
                            return keys[k].Descending ? -comparison : comparison;
                        }
                    }

                    // Equal keys keep their original relative order, making the sort stable.
                    return left.CompareTo(right);
                });
            }
            catch (InvalidOperationException wrapped) when (wrapped.InnerException is XsltException refused)
            {
                // Array.Sort wraps what its comparison throws, and what it threw is the answer.
                throw refused;
            }

            return order;
        }

        /// <summary>Compares two strings as this key orders text.</summary>
        /// <param name="left">One string.</param>
        /// <param name="right">The other.</param>
        public int CompareText(string left, string right)
        {
            // A named collation decides everything, case order included: the specification has case-order
            // apply only where no collation was named.
            if (m_collation is not null)
            {
                return m_collation.Compare(left, right);
            }

            if (m_caseOrder != SortCaseOrder.Unspecified)
            {
                int ignoringCase = m_culture is null
                    ? string.Compare(left, right, StringComparison.OrdinalIgnoreCase)
                    : m_culture.CompareInfo.Compare(left, right, CompareOptions.IgnoreCase);

                return ignoringCase != 0 ? ignoringCase : CompareByCase(left, right);
            }

            return m_culture is null
                ? string.CompareOrdinal(left, right)
                : m_culture.CompareInfo.Compare(left, right, CompareOptions.None);
        }

        /// <summary>Orders two strings that differ only in case, as the case order says.</summary>
        private int CompareByCase(string left, string right)
        {
            int shared = Math.Min(left.Length, right.Length);

            for (int i = 0; i < shared; i++)
            {
                if (left[i] == right[i])
                {
                    continue;
                }

                bool leftUpper = char.IsUpper(left[i]);
                if (leftUpper != char.IsUpper(right[i]))
                {
                    bool upperFirst = m_caseOrder == SortCaseOrder.UpperFirst;
                    return leftUpper == upperFirst ? -1 : 1;
                }

                return left[i].CompareTo(right[i]);
            }

            return left.Length.CompareTo(right.Length);
        }

        private static int CompareNumbers(double left, double right)
        {
            bool leftIsNaN = double.IsNaN(left);
            bool rightIsNaN = double.IsNaN(right);

            if (leftIsNaN || rightIsNaN)
            {
                return leftIsNaN && rightIsNaN ? 0 : leftIsNaN ? -1 : 1;
            }

            return left.CompareTo(right);
        }
    }

    /// <summary>
    /// A sequence constructor where an expression is wanted: the key of an <c>xsl:sort</c> written as
    /// content, or what an <c>xsl:perform-sort</c> without a <c>select</c> sorts.
    /// </summary>
    internal sealed class SequenceConstructorExpr : Expr
    {
        private readonly Instruction[] m_body;

        /// <summary>Initializes the expression.</summary>
        /// <param name="body">The sequence constructor.</param>
        public SequenceConstructorExpr(Instruction[] body)
        {
            m_body = body;
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            DynamicContext inner = context;

            return VariableInstruction.CaptureSequence(
                m_body,
                ref inner,
                context.Runtime ?? throw new InvalidOperationException(
                    "A sequence constructor can only be evaluated inside a transformation."));
        }
    }

    /// <summary><c>xsl:for-each</c>.</summary>
    internal sealed class ForEachInstruction : Instruction
    {
        private readonly Expr m_select;
        private readonly SortKey[] m_sortKeys;
        private readonly Instruction[] m_body;

        /// <summary>Initializes a for-each instruction.</summary>
        public ForEachInstruction(Expr select, SortKey[] sortKeys, Instruction[] body)
        {
            m_select = select;
            m_sortKeys = sortKeys;
            m_body = body;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// XSLT 2.0 iterates a <em>sequence</em>, and a sequence holds atomic values as readily as nodes:
        /// <c>select="1 to 5"</c> and <c>select="('a', 'b')"</c> are both ordinary. The node-set path below
        /// is kept whole rather than folded into the general one, because a selection of nodes is what
        /// almost every stylesheet writes and it is the path everything was measured on.
        /// </remarks>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            XPathValue value = m_select.Evaluate(ref context);

            // The focus moves to something the rules did not choose, so the rule that was running does not
            // come in with it: xsl:apply-imports inside here has no current template rule to continue.
            TemplateRule? rule = runtime.SuspendCurrentRule();

            try
            {
                if (value.Kind != XPathValueKind.NodeSet)
                {
                    ExecuteOverItems(value, ref context, runtime);
                    return;
                }

                ExecuteOverNodes(value.AsNodeSet(), ref context, runtime);
            }
            finally
            {
                runtime.ResumeCurrentRule(rule);
            }
        }

        /// <summary>
        /// Iterates a sequence that is not already a node-set.
        /// </summary>
        /// <remarks>
        /// A sequence of nothing but nodes is handed to the node-set path, which is the same work done
        /// faster — and, more to the point, is the same code, so <c>(a, b)</c> behaves exactly as
        /// <c>a | b</c> does rather than nearly so. Only a sequence that actually holds an atomic value
        /// takes the general path.
        /// </remarks>
        private void ExecuteOverItems(XPathValue value, ref DynamicContext context, XsltRuntime runtime)
        {
            List<XPathValue> items = XdmSequence.Items(value);

            bool allNodes = true;
            foreach (XPathValue item in items)
            {
                if (item.Kind != XPathValueKind.Node)
                {
                    allNodes = false;
                    break;
                }
            }

            if (allNodes)
            {
                ExecuteOverNodes(
                    NodeSet.Of(value, context.Tree, XsltErrorCode.XPTY0004, "xsl:for-each"),
                    ref context,
                    runtime);

                return;
            }

            SortKey.Sort(items, m_sortKeys, ref context);

            // An atomic item is current while its body runs, for current() to answer with; a node is
            // recorded in the context as it always was.
            XPathValue? outerCurrent = runtime.CurrentAtomicItem;

            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    DynamicContext inner = context.WithItem(items[i]);
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    inner.Position = i + 1;
                    inner.Size = items.Count;
                    runtime.CurrentAtomicItem = inner.Node >= 0 ? null : items[i];
                    ExecuteAll(m_body, ref inner, runtime);
                }
            }
            finally
            {
                runtime.CurrentAtomicItem = outerCurrent;
            }
        }

        private void ExecuteOverNodes(NodeSet selected, ref DynamicContext context, XsltRuntime runtime)
        {
            DynamicContext iteration = ReferenceEquals(context.Tree, selected.Tree)
                ? context
                : context.SwitchTree(selected.Tree, context.Node);

            // Without sort keys the selection is already in the order it will be visited, so it can be walked
            // directly rather than copied into a list first.
            if (m_sortKeys.Length == 0)
            {
                int size = selected.Count;
                XdmTree walked = iteration.Tree;

                for (int i = 0; i < size; i++)
                {
                    // A selection spanning documents is ordered by document, so this changes at most once per
                    // document rather than once per node. It costs one reference comparison otherwise.
                    XdmTree tree = selected.TreeAt(i);
                    if (!ReferenceEquals(tree, walked))
                    {
                        iteration = context.SwitchTree(tree, context.Node);
                        walked = tree;
                    }

                    DynamicContext inner = iteration;
                    inner.Node = selected[i];
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    inner.Position = i + 1;
                    inner.Size = size;
                    ExecuteAll(m_body, ref inner, runtime);
                }

                return;
            }

            if (selected.SpansDocuments)
            {
                // Sorting compares string-values drawn from whichever document each node belongs to, so the
                // nodes have to carry their trees through the sort rather than being a list of ids.
                SortAcrossDocuments(selected, ref context, runtime);
                return;
            }

            List<int> nodes = NodeListPool.Rent();
            try
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    nodes.Add(selected[i]);
                }

                SortKey.Sort(nodes, m_sortKeys, ref iteration);

                for (int i = 0; i < nodes.Count; i++)
                {
                    DynamicContext inner = iteration;
                    inner.Node = nodes[i];
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    inner.Position = i + 1;
                    inner.Size = nodes.Count;
                    ExecuteAll(m_body, ref inner, runtime);
                }
            }
            finally
            {
                NodeListPool.Return(nodes);
            }
        }

        /// <summary>
        /// Sorts and iterates a selection whose nodes come from more than one document.
        /// </summary>
        /// <remarks>
        /// Kept apart from the ordinary path so that the common case still sorts a plain list of ids. Here
        /// each node is sorted one at a time against a context positioned in its own document, which is what
        /// makes a sort key evaluate against the right tree.
        /// </remarks>
        private void SortAcrossDocuments(NodeSet selected, ref DynamicContext context, XsltRuntime runtime)
        {
            int count = selected.Count;
            (int Node, XdmTree Tree)[] entries = new (int, XdmTree)[count];

            for (int i = 0; i < count; i++)
            {
                entries[i] = (selected[i], selected.TreeAt(i));
            }

            SortKey.SortAcrossTrees(entries, m_sortKeys, ref context);

            for (int i = 0; i < count; i++)
            {
                DynamicContext inner = context.SwitchTree(entries[i].Tree, entries[i].Node);
                inner.CurrentNode = inner.Node;
                inner.CurrentTree = inner.Tree;
                inner.Position = i + 1;
                inner.Size = count;
                ExecuteAll(m_body, ref inner, runtime);
            }
        }
    }

    /// <summary>A parameter supplied at a call site by <c>xsl:with-param</c>.</summary>
    internal sealed class WithParameter
    {
        /// <summary>Initializes a with-param.</summary>
        public WithParameter(
            ExpandedName name, Expr? select, Instruction[]? body, bool tunnel, XdmSequenceType? type = null)
        {
            Name = name;
            Select = select;
            Body = body;
            Tunnel = tunnel;
            Type = type;
        }

        /// <summary>
        /// The <c>as</c> written on the <c>xsl:with-param</c>, or null where it declared none.
        /// </summary>
        /// <remarks>
        /// The value is converted to it where it is evaluated, before the callee sees it, and a value that
        /// cannot be is <c>XTTE0590</c> — the caller's promise, checked at the caller. The callee's own
        /// <c>as</c> is a second, separate check.
        /// </remarks>
        public XdmSequenceType? Type { get; }

        /// <summary>The parameter's expanded name.</summary>
        public ExpandedName Name { get; }

        /// <summary>The value expression, if written with a <c>select</c>.</summary>
        public Expr? Select { get; }

        /// <summary>The value content, if written with a body.</summary>
        public Instruction[]? Body { get; }

        /// <summary>
        /// Whether the value was supplied with <c>tunnel="yes"</c>.
        /// </summary>
        /// <remarks>
        /// A tunnel value binds only to a declaration that is itself marked <c>tunnel="yes"</c>, so the two
        /// kinds never see each other even when they share a name.
        /// </remarks>
        public bool Tunnel { get; }

        /// <summary>The base URI of the declaration, which a tree built from its content takes.</summary>
        public string? BaseUri { get; init; }

        /// <summary>
        /// Evaluates every parameter of one call site.
        /// </summary>
        /// <remarks>
        /// The values belong to the calling instruction rather than to the nodes it goes on to process:
        /// <c>xsl:apply-templates</c> evaluates each <c>xsl:with-param</c> exactly once however many nodes are
        /// selected, so <c>select="position()"</c> there means the position of the node the instruction was
        /// written for. Evaluating per selected node would be wrong as well as slower.
        /// </remarks>
        /// <param name="parameters">The parameters written on the call site.</param>
        /// <param name="context">The calling instruction's context.</param>
        /// <param name="runtime">The transformation in progress.</param>
        public static ParameterValue[] EvaluateAll(
            WithParameter[] parameters,
            ref DynamicContext context,
            XsltRuntime runtime)
        {
            if (parameters.Length == 0)
            {
                return Array.Empty<ParameterValue>();
            }

            ParameterValue[] values = new ParameterValue[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                WithParameter parameter = parameters[i];
                values[i] = new ParameterValue(
                    parameter.Name,
                    VariableInstruction.Evaluate(
                        parameter.Select,
                        parameter.Body,
                        ref context,
                        runtime,
                        parameter.Type,
                        XsltErrorCode.XTTE0590,
                        parameter.BaseUri),
                    parameter.Tunnel);
            }

            return values;
        }
    }

    /// <summary>A parameter value, evaluated at the call site and waiting to be bound by the callee.</summary>
    internal readonly struct ParameterValue
    {
        /// <summary>Initializes a parameter value.</summary>
        public ParameterValue(ExpandedName name, XPathValue value, bool tunnel)
        {
            Name = name;
            Value = value;
            Tunnel = tunnel;
        }

        /// <summary>The name this value binds to.</summary>
        public ExpandedName Name { get; }

        /// <summary>The value itself.</summary>
        public XPathValue Value { get; }

        /// <summary>Whether it was supplied with <c>tunnel="yes"</c>.</summary>
        public bool Tunnel { get; }
    }

    /// <summary><c>xsl:apply-templates</c>.</summary>
    internal sealed class ApplyTemplatesInstruction : Instruction
    {
        private readonly Expr? m_select;
        private readonly int m_mode;
        private readonly SortKey[] m_sortKeys;
        private readonly WithParameter[] m_parameters;

        /// <summary>Whether a selection may hold things that are not nodes, which is 3.0's rule.</summary>
        private readonly bool m_allowsItems;

        /// <summary>Initializes an apply-templates instruction.</summary>
        public ApplyTemplatesInstruction(
            Expr? select,
            int mode,
            SortKey[] sortKeys,
            WithParameter[] parameters,
            bool allowsItems = false)
        {
            m_select = select;
            m_mode = mode;
            m_sortKeys = sortKeys;
            m_parameters = parameters;
            m_allowsItems = allowsItems;
        }

        /// <summary>Whether every item of a value is a node, which is the case the node path can take.</summary>
        private static bool IsAllNodes(XPathValue value)
        {
            if (value.Kind is XPathValueKind.NodeSet or XPathValueKind.Node)
            {
                return true;
            }

            foreach (XPathValue item in XdmSequence.Items(value))
            {
                if (item.Kind is not (XPathValueKind.Node or XPathValueKind.NodeSet))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            // Evaluated here, before anything is selected, because the values are the instruction's and not
            // each processed node's; see WithParameter.EvaluateAll.
            ParameterValue[] parameters = WithParameter.EvaluateAll(m_parameters, ref context, runtime);

            int mode = m_mode == CompiledStylesheet.CurrentMode ? runtime.CurrentMode : m_mode;

            DynamicContext iteration = context;
            NodeSet? selected = null;

            if (m_select is not null)
            {
                XPathValue value = m_select.Evaluate(ref context);

                // Before 3.0 this really did want nodes, there being no template that could match an atomic
                // value. The predicate pattern is one, so a sequence holding anything else is now something
                // to walk rather than something to refuse — and it is walked item by item, because a
                // NodeSet cannot hold the parts of it that are not nodes.
                if (m_allowsItems && !IsAllNodes(value))
                {
                    runtime.ApplyTemplatesToSequence(
                        XdmSequence.Items(value), mode, parameters, ref context);

                    return;
                }

                // A sequence of nodes is nodes, and refusing (a, b) for not being a | b was reading XPath
                // 1.0's value model into an XSLT 2.0 instruction.
                selected = NodeSet.Of(
                    value,
                    context.Tree,
                    XsltErrorCode.XTTE0520,
                    "xsl:apply-templates");

                if (!ReferenceEquals(context.Tree, selected.Tree))
                {
                    iteration = context.SwitchTree(selected.Tree, context.Node);
                }
            }

            if (m_sortKeys.Length == 0)
            {
                if (selected is null)
                {
                    // With no select, the children of the context node are processed in document order.
                    runtime.ApplyTemplatesToChildren(context.Node, mode, parameters, ref iteration);
                    return;
                }

                int size = selected.Count;
                XdmTree walked = iteration.Tree;

                for (int i = 0; i < size; i++)
                {
                    XdmTree tree = selected.TreeAt(i);
                    if (!ReferenceEquals(tree, walked))
                    {
                        iteration = context.SwitchTree(tree, context.Node);
                        walked = tree;
                    }

                    DynamicContext inner = iteration;
                    inner.Node = selected[i];
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    inner.Position = i + 1;
                    inner.Size = size;
                    runtime.ApplyTemplates(selected[i], mode, parameters, ref inner);
                }

                return;
            }

            if (selected is not null && selected.SpansDocuments)
            {
                int count = selected.Count;
                (int Node, XdmTree Tree)[] entries = new (int, XdmTree)[count];

                for (int i = 0; i < count; i++)
                {
                    entries[i] = (selected[i], selected.TreeAt(i));
                }

                SortKey.SortAcrossTrees(entries, m_sortKeys, ref context);

                for (int i = 0; i < count; i++)
                {
                    DynamicContext inner = context.SwitchTree(entries[i].Tree, entries[i].Node);
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    inner.Position = i + 1;
                    inner.Size = count;
                    runtime.ApplyTemplates(entries[i].Node, mode, parameters, ref inner);
                }

                return;
            }

            List<int> nodes = NodeListPool.Rent();
            try
            {
                if (selected is null)
                {
                    for (int child = context.Tree.FirstChildOf(context.Node); child >= 0;
                        child = context.Tree.NextSiblingOf(child))
                    {
                        nodes.Add(child);
                    }
                }
                else
                {
                    for (int i = 0; i < selected.Count; i++)
                    {
                        nodes.Add(selected[i]);
                    }
                }

                SortKey.Sort(nodes, m_sortKeys, ref iteration);

                for (int i = 0; i < nodes.Count; i++)
                {
                    DynamicContext inner = iteration;
                    inner.Node = nodes[i];
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    inner.Position = i + 1;
                    inner.Size = nodes.Count;
                    runtime.ApplyTemplates(nodes[i], mode, parameters, ref inner);
                }
            }
            finally
            {
                NodeListPool.Return(nodes);
            }
        }
    }

    /// <summary><c>xsl:apply-imports</c>.</summary>
    internal sealed class ApplyImportsInstruction : Instruction
    {
        private readonly WithParameter[] m_parameters;

        /// <summary>Initializes an apply-imports instruction.</summary>
        /// <param name="parameters">
        /// Parameters written on the instruction. XSLT 1.0 allowed none here; 2.0 does, which is what lets an
        /// override pass a tunnel parameter down to the template it is wrapping.
        /// </param>
        public ApplyImportsInstruction(WithParameter[] parameters)
        {
            m_parameters = parameters;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            runtime.ApplyImports(WithParameter.EvaluateAll(m_parameters, ref context, runtime), ref context);
        }
    }

    /// <summary><c>xsl:call-template</c>.</summary>
    internal sealed class CallTemplateInstruction : Instruction
    {
        private readonly Template m_target;
        private readonly WithParameter[] m_parameters;
        private bool m_last;

        /// <summary>Initializes a call-template instruction.</summary>
        public CallTemplateInstruction(Template target, WithParameter[] parameters)
        {
            m_target = target;
            m_parameters = parameters;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            // The parameters are evaluated here either way: they mean what they mean where they were
            // written, in the calling template's frame, which a call made in its place no longer has.
            ParameterValue[] parameters = WithParameter.EvaluateAll(m_parameters, ref context, runtime);

            // A call that is the last thing its template does is handed back to the invocation running that
            // template, to make once the template has returned — in the template's own place on the stack
            // rather than beneath it. That is what lets a template loop by calling itself, for as many
            // iterations as it likes: the 1.0 idiom for walking a sequence one item at a time.
            if (m_last)
            {
                runtime.DeferTailCall(m_target, parameters);
                return;
            }

            // Calling by name leaves the mode exactly as it was: what a named template does to the mode is
            // nothing, which is what lets a helper be called from several modes and still behave.
            runtime.InvokeTemplate(m_target, parameters, runtime.CurrentMode, ref context);
        }

        /// <inheritdoc/>
        internal override void MarkTailPosition()
        {
            m_last = true;
        }
    }

    /// <summary><c>xsl:variable</c> and <c>xsl:param</c> declared inside a template.</summary>
    internal sealed class VariableInstruction : Instruction
    {
        private readonly int m_slot;
        private readonly Expr? m_select;
        private readonly Instruction[]? m_body;
        private readonly XdmSequenceType? m_type;
        private readonly string? m_baseUri;

        /// <summary>Initializes a variable declaration.</summary>
        /// <param name="slot">Where the value goes in the frame.</param>
        /// <param name="select">The value expression, if written with a <c>select</c>.</param>
        /// <param name="body">The content, otherwise.</param>
        /// <param name="type">The declared type from an <c>as</c> attribute, if there was one.</param>
        /// <param name="baseUri">The base URI of the declaration, which a tree built from its content takes.</param>
        public VariableInstruction(
            int slot, Expr? select, Instruction[]? body, XdmSequenceType? type = null, string? baseUri = null)
        {
            m_slot = slot;
            m_select = select;
            m_body = body;
            m_type = type;
            m_baseUri = baseUri;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            context.Locals[context.FrameBase + m_slot] =
                Evaluate(m_select, m_body, ref context, runtime, m_type, baseUri: m_baseUri);
        }

        /// <summary>
        /// Produces a variable's value. A <c>select</c> yields an ordinary XPath value; content instead yields
        /// a result tree fragment, captured as a tree so that it can be copied or navigated later.
        /// </summary>
        /// <remarks>
        /// A declared type changes what the content amounts to. Without one, everything a constructor
        /// produces is built into a single document node — three integers become the text <c>1 2 3</c>. With
        /// one, the constructor's result is a sequence, and the three integers stay three integers.
        /// </remarks>
        /// <param name="select">The value expression, if written with a <c>select</c>.</param>
        /// <param name="body">The content, otherwise.</param>
        /// <param name="context">The evaluation context.</param>
        /// <param name="runtime">The transformation in progress.</param>
        /// <param name="type">The declared type, if the declaration had an <c>as</c> attribute.</param>
        /// <param name="code">The error where the value does not fit the type.</param>
        /// <param name="baseUri">
        /// The base URI of the declaration, which a tree built from its content takes (§9.4): with no
        /// declared type, the document node's; with one, that of each node built at the top.
        /// </param>
        public static XPathValue Evaluate(
            Expr? select,
            Instruction[]? body,
            ref DynamicContext context,
            XsltRuntime runtime,
            XdmSequenceType? type = null,
            XsltErrorCode code = XsltErrorCode.XTTE0570,
            string? baseUri = null)
        {
            if (select is not null)
            {
                return XdmTypeConversion.Apply(select.Evaluate(ref context), type, code);
            }

            if (type is not null)
            {
                return XdmTypeConversion.Apply(
                    CaptureSequence(body, ref context, runtime, baseUri: baseUri), type, code);
            }

            if (body is null || body.Length == 0)
            {
                return XPathValue.FromString(string.Empty);
            }

            ResultTreeBuilder builder = new ResultTreeBuilder { BaseUri = baseUri };
            OutputTarget previous = runtime.Output;
            runtime.Output = builder;

            try
            {
                ExecuteAll(body, ref context, runtime);
            }
            finally
            {
                runtime.Output = previous;
            }

            XdmTree fragment = builder.Finish();
            return XPathValue.FromNodeSet(NodeSet.Singleton(fragment, XdmTree.RootNode));
        }

        /// <summary>Runs a sequence constructor and keeps what it produced as a sequence.</summary>
        /// <param name="body">The constructor.</param>
        /// <param name="context">The context to run it in.</param>
        /// <param name="runtime">The transformation in progress.</param>
        /// <param name="becomesAString">
        /// Whether the sequence is on its way to being a string. Only <c>xsl:value-of</c> says yes, and only
        /// <c>xsl:result-document</c> asks: 3.0 lets one be written there and not inside a temporary tree.
        /// </param>
        /// <param name="baseUri">The base URI a node built at the top of the sequence takes.</param>
        internal static XPathValue CaptureSequence(
            Instruction[]? body,
            ref DynamicContext context,
            XsltRuntime runtime,
            bool becomesAString = false,
            string? baseUri = null,
            bool finalOutput = false)
        {
            if (body is null || body.Length == 0)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            SequenceCaptureTarget capture = new SequenceCaptureTarget(becomesAString, baseUri)
            {
                StandsForFinalOutput = finalOutput,
            };
            OutputTarget previous = runtime.Output;
            runtime.Output = capture;

            try
            {
                ExecuteAll(body, ref context, runtime);
            }
            finally
            {
                runtime.Output = previous;
            }

            return capture.Finish();
        }
    }

    /// <summary><c>xsl:copy-of</c>, which copies selected nodes and their subtrees.</summary>
    internal sealed class CopyOfInstruction : Instruction
    {
        private readonly Expr m_select;
        private readonly bool m_copyNamespaces;
        private readonly bool m_copyAccumulators;

        /// <summary>Initializes a copy-of instruction.</summary>
        /// <param name="select">What to copy.</param>
        /// <param name="copyNamespaces">
        /// Whether an element's namespace nodes come with it. XSLT 2.0's <c>copy-namespaces="no"</c> leaves
        /// behind the declarations nothing in the copy uses.
        /// </param>
        /// <param name="copyAccumulators">
        /// Whether each copied node answers for the accumulators as the node it was copied from does, which
        /// is XSLT 3.0's <c>copy-accumulators="yes"</c>. Without it a copy is a fresh tree and the
        /// accumulators, all of them, are computed over the copy from the beginning.
        /// </param>
        public CopyOfInstruction(Expr select, bool copyNamespaces = true, bool copyAccumulators = false)
        {
            m_select = select;
            m_copyNamespaces = copyNamespaces;
            m_copyAccumulators = copyAccumulators;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            XPathValue value = m_select.Evaluate(ref context);

            if (value.Kind == XPathValueKind.NodeSet)
            {
                NodeSet nodes = value.AsNodeSet();
                for (int i = 0; i < nodes.Count; i++)
                {
                    NodeCopier.CopyDeep(
                        nodes.TreeAt(i), nodes[i], runtime.Output, m_copyNamespaces, m_copyAccumulators, runtime);
                }

                return;
            }

            if (value.Kind is not (XPathValueKind.Sequence or XPathValueKind.Node or XPathValueKind.Array))
            {
                runtime.Output.WriteAtomic(value.ToStringValue());
                return;
            }

            // A sequence may hold nodes and atomic values together — which is what an as declaration lets a
            // variable be — so each item is copied as its own kind requires. An array contributes its
            // members: a result tree has no way to hold one, and its members are what it was built out of.
            List<XPathValue> items = XdmSequence.ContentItems(value);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Kind == XPathValueKind.Node)
                {
                    NodeCopier.CopyDeep(
                        items[i].NodeTree,
                        items[i].NodeId,
                        runtime.Output,
                        m_copyNamespaces,
                        m_copyAccumulators,
                        runtime);

                    continue;
                }

                // Written as an atomic value, which is what puts a single space between it and an atomic value
                // written just before — by this instruction or by the one before it, and never after a node.
                runtime.Output.WriteAtomic(items[i].ToStringValue());
            }
        }
    }

    /// <summary>
    /// <c>xsl:document</c>, which builds a document node from its content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The content is captured into a tree of its own rather than written straight through, and that is what
    /// makes the instruction a boundary: an attribute or a namespace created directly inside it belongs to
    /// the document node, which can carry neither, rather than attaching itself to whatever element happens
    /// to enclose the instruction.
    /// </para>
    /// <para>
    /// Written into a result, a document node contributes its children and nothing else — no document node
    /// can be a child of an element — so what reaches the output is what the same content would have
    /// produced in place. The difference shows only where a document node is a value rather than output,
    /// which needs the sequence-valued variables that <c>as</c> declarations bring; see
    /// ConformanceNotes.md.
    /// </para>
    /// </remarks>
    internal sealed class DocumentInstruction : Instruction
    {
        private readonly Instruction[] m_body;
        private readonly string? m_baseUri;

        /// <summary>Initializes a document instruction.</summary>
        /// <param name="baseUri">The base URI of the instruction, which the document node takes (§11.4.3).</param>
        public DocumentInstruction(Instruction[] body, string? baseUri = null)
        {
            m_body = body;
            m_baseUri = baseUri;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            if (m_body.Length == 0)
            {
                // An empty document node contributes nothing; building one would prove that at some cost.
                return;
            }

            XPathValue value = VariableInstruction.Evaluate(null, m_body, ref context, runtime, baseUri: m_baseUri);

            // Where the target holds a sequence, the document node itself is what this instruction produced
            // and what a declared document-node() asks for. Copying its content out instead would contribute
            // the children and lose the one node the instruction exists to make.
            if (runtime.Output.TryAppendValue(value))
            {
                return;
            }

            NodeSet document = value.AsNodeSet();
            NodeCopier.CopyDeep(document.TreeAt(0), document[0], runtime.Output);
        }
    }

    /// <summary><c>xsl:copy</c>, which copies the current node without its children.</summary>
    internal sealed class CopyInstruction : Instruction
    {
        /// <summary>Whether the copied element passes its namespaces on to the children built inside it.</summary>
        private readonly bool m_inheritNamespaces;

        private readonly Instruction[] m_body;
        private readonly ExpandedName[] m_attributeSets;
        private readonly bool m_copyNamespaces;
        private readonly Expr? m_select;
        private readonly bool m_copiesItems;
        private readonly bool m_copyAccumulators;

        /// <summary>Initializes a copy instruction.</summary>
        /// <param name="body">What fills the copied element.</param>
        /// <param name="attributeSets">Attribute sets to apply to it.</param>
        /// <param name="copyNamespaces">Whether the element's namespace nodes come with it.</param>
        /// <param name="select">What to copy, where XSLT 3.0's <c>select</c> named something else.</param>
        /// <param name="copiesItems">
        /// Whether an item that is not a node is copied as itself rather than refused, which is XSLT 3.0's
        /// reading and not 2.0's.
        /// </param>
        /// <param name="copyAccumulators">
        /// Whether the copy answers for the accumulators as the node it was copied from does, which is
        /// XSLT 3.0's <c>copy-accumulators="yes"</c>.
        /// </param>
        public CopyInstruction(
            Instruction[] body,
            ExpandedName[]? attributeSets = null,
            bool copyNamespaces = true,
            Expr? select = null,
            bool copiesItems = false,
            bool copyAccumulators = false,
            bool inheritNamespaces = true)
        {
            m_inheritNamespaces = inheritNamespaces;
            m_body = body;
            m_attributeSets = attributeSets ?? Array.Empty<ExpandedName>();
            m_copyNamespaces = copyNamespaces;
            m_select = select;
            m_copiesItems = copiesItems;
            m_copyAccumulators = copyAccumulators;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            // XSLT 3.0's select names what to copy, in place of the context item. It also gives the body a
            // focus of its own — the selected item, at position 1 of 1 — which is what lets xsl:copy be
            // written inside a stylesheet function, where there is otherwise no focus at all.
            DynamicContext focus = context;

            if (m_select is not null)
            {
                List<XPathValue> chosen = XdmSequence.Items(m_select.Evaluate(ref context));

                // Nothing selected copies nothing, and more than one item is a type error of its own.
                if (chosen.Count == 0)
                {
                    return;
                }

                if (chosen.Count > 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTTE3180,
                        $"The select of xsl:copy yielded {chosen.Count} items, and xsl:copy copies one.");
                }

                XPathValue selected = chosen[0];

                focus = context.WithItem(selected);
                focus.Position = 1;
                focus.Size = 1;
            }

            XdmTree tree = focus.Tree;
            int node = focus.Node;

            if (node < 0)
            {
                // An atomic value or a function item has nothing to shallow-copy, so from 3.0 the copy of it
                // is the value itself and the body is not run — there is no element for it to fill. XSLT 2.0
                // has no such reading and calls it a type error.
                if (m_copiesItems && focus.HasContextItem)
                {
                    XPathValue item = focus.AtomicItem;

                    if (!runtime.Output.TryAppendValue(item))
                    {
                        runtime.Output.WriteAtomic(item.ToStringValue());
                    }

                    return;
                }

                throw XsltErrors.Error(
                    XsltErrorCode.XTTE0945,
                    "xsl:copy copies the context item, and "
                    + (focus.HasContextItem
                        ? "here the context item is an atomic value rather than a node."
                        : "here there is no context item at all."));
            }

            // Choosing the item choosen makes the focus one the rules did not choose, so — as for xsl:for-each
            // — the rule that was running is no longer the current one inside. Without this an xsl:next-match
            // in the body matches against whatever the select moved to, which for select=".." is the parent,
            // and a rule that matches the parent as well runs forever.
            TemplateRule? rule = m_select is null ? null : runtime.SuspendCurrentRule();

            try
            {
                CopyNode(tree, node, ref focus, runtime);
            }
            finally
            {
                if (m_select is not null)
                {
                    runtime.ResumeCurrentRule(rule);
                }
            }
        }

        private void CopyNode(XdmTree tree, int node, ref DynamicContext focus, XsltRuntime runtime)
        {
            switch (tree.KindOf(node))
            {
                case NodeKind.Root:
                {
                    // A document node contributes its children and nothing else wherever it is copied to, so
                    // the content runs straight into whatever is already open rather than being built into a
                    // document node of its own and copied out again. The one thing that separates the two is
                    // an attribute or namespace node: a document node cannot carry one, and inline execution
                    // would otherwise put it on the element outside.
                    // The target is told all the same, so that one collecting a sequence can make the
                    // document node an item where a variable declared document-node() wants one.
                    int floor = runtime.DocumentFloor;
                    OutputTarget? floorTarget = runtime.DocumentFloorTarget;
                    if (runtime.Output.OpenElementDepth == 0)
                    {
                        runtime.Output.NoteSourceOfNext(tree, node, runtime, withAttributes: false);
                    }

                    runtime.Output.StartDocumentCopy();
                    runtime.Output.NoteDocumentCopiedFrom(tree);
                    runtime.DocumentFloor = runtime.Output.OpenElementDepth;
                    runtime.DocumentFloorTarget = runtime.Output;

                    try
                    {
                        ExecuteAll(m_body, ref focus, runtime);
                    }
                    finally
                    {
                        runtime.DocumentFloor = floor;
                        runtime.DocumentFloorTarget = floorTarget;
                        runtime.Output.EndDocumentCopy();
                    }

                    return;
                }

                case NodeKind.Element:
                {
                    int nameCode = tree.NameCodeOf(node);
                    int fingerprint = tree.FingerprintOf(node);

                    // A copy made where nothing is open may be parentless, and then keeps the base URI of
                    // the node it copies (§11.9.1); under a parent it takes the parent's, as any node does.
                    if (runtime.Output.OpenElementDepth == 0)
                    {
                        runtime.Output.NoteSourceOfNext(tree, node, runtime, withAttributes: false);
                    }

                    runtime.Output.StartElement(
                        tree.NameTable.GetPrefix(nameCode),
                        tree.NameTable.GetNamespaceUri(fingerprint),
                        tree.NameTable.GetLocalName(fingerprint));

                    runtime.Output.MarkOwnNamespaces(root: true);

                    if (m_copyAccumulators)
                    {
                        runtime.Output.NoteCopiedFrom(tree, node);
                    }

                    if (m_copyNamespaces)
                    {
                        foreach ((string prefix, string uri) in tree.InScopeNamespacesOf(node))
                        {
                            runtime.Output.WriteNamespaceDeclaration(prefix, uri);
                        }
                    }

                    if (!m_inheritNamespaces)
                    {
                        runtime.Output.MarkNoInheritedNamespaces();
                    }

                    AttributeSetApplier.Apply(m_attributeSets, ref focus, runtime);
                    ExecuteAll(m_body, ref focus, runtime);
                    runtime.Output.EndElement();
                    return;
                }

                default:
                    NodeCopier.CopyShallow(tree, node, runtime.Output, m_copyAccumulators);
                    return;
            }
        }
    }

    /// <summary><c>xsl:element</c>, whose name is computed.</summary>
    internal sealed class ElementInstruction : Instruction
    {
        /// <summary>Whether the element passes its namespaces on to the children built inside it.</summary>
        private readonly bool m_inheritNamespaces;

        private readonly AttributeValueTemplate m_name;
        private readonly AttributeValueTemplate? m_namespaceUri;
        private readonly Instruction[] m_body;
        private readonly ExpandedName[] m_attributeSets;

        /// <summary>
        /// The namespace declarations in scope on the <c>xsl:element</c> itself, which are what the prefix
        /// of the computed name is read against.
        /// </summary>
        private readonly IReadOnlyDictionary<string, string> m_inScope;

        /// <summary>Initializes an element instruction.</summary>
        public ElementInstruction(
            AttributeValueTemplate name,
            AttributeValueTemplate? namespaceUri,
            Instruction[] body,
            IReadOnlyDictionary<string, string> inScope,
            ExpandedName[]? attributeSets = null,
            bool inheritNamespaces = true)
        {
            m_inheritNamespaces = inheritNamespaces;
            m_name = name;
            m_namespaceUri = namespaceUri;
            m_body = body;
            m_inScope = inScope;
            m_attributeSets = attributeSets ?? Array.Empty<ExpandedName>();
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            (string prefix, string namespaceUri, string localName) = ComputedName.ForElement(
                m_name.Evaluate(ref context),
                m_namespaceUri?.Evaluate(ref context),
                m_inScope);

            runtime.Output.StartElement(prefix, namespaceUri, localName);

            if (!m_inheritNamespaces)
            {
                runtime.Output.MarkNoInheritedNamespaces();
            }
            AttributeSetApplier.Apply(m_attributeSets, ref context, runtime);
            ExecuteAll(m_body, ref context, runtime);
            runtime.Output.EndElement();
        }
    }

    /// <summary><c>xsl:attribute</c>, whose name is computed and whose value is its text content.</summary>
    internal sealed class AttributeInstruction : Instruction
    {
        private readonly AttributeValueTemplate m_name;
        private readonly AttributeValueTemplate? m_namespaceUri;
        private readonly Instruction[] m_body;
        private readonly Expr? m_select;
        private readonly AttributeValueTemplate? m_separator;

        /// <summary>
        /// The namespace declarations in scope on the <c>xsl:attribute</c> itself, which are what the prefix
        /// of the computed name is read against.
        /// </summary>
        private readonly IReadOnlyDictionary<string, string> m_inScope;

        /// <summary>Initializes an attribute instruction.</summary>
        /// <param name="name">The attribute's name, which may be a value template.</param>
        /// <param name="namespaceUri">The namespace to put it in, if any.</param>
        /// <param name="body">The content giving its value.</param>
        /// <param name="inScope">The namespace declarations in scope where the instruction was written.</param>
        /// <param name="select">The expression giving its value instead, which XSLT 2.0 allows.</param>
        /// <param name="separator">What to put between items when the value has several.</param>
        public AttributeInstruction(
            AttributeValueTemplate name,
            AttributeValueTemplate? namespaceUri,
            Instruction[] body,
            IReadOnlyDictionary<string, string> inScope,
            Expr? select = null,
            AttributeValueTemplate? separator = null)
        {
            m_name = name;
            m_namespaceUri = namespaceUri;
            m_body = body;
            m_inScope = inScope;
            m_select = select;
            m_separator = separator;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            (string prefix, string namespaceUri, string localName) = ComputedName.ForAttribute(
                m_name.Evaluate(ref context),
                m_namespaceUri?.Evaluate(ref context),
                m_inScope);

            // The default separator differs between the two forms, as it does for xsl:value-of: a single
            // space between the items of a select, and nothing between what the content produced.
            string value = m_select is null
                ? CaptureText(
                    m_body,
                    ref context,
                    runtime,
                    m_separator is null ? string.Empty : m_separator.Evaluate(ref context))
                : Join(
                    m_select.Evaluate(ref context),
                    m_separator is null ? " " : m_separator.Evaluate(ref context));

            runtime.RequireAnElementForAttribute("An attribute");
            runtime.Output.WriteAttribute(prefix, namespaceUri, localName, value);
        }
    }

    /// <summary>
    /// <c>xsl:namespace</c>, which puts a namespace node on the element being built.
    /// </summary>
    /// <remarks>
    /// Rarely written, because a literal result element already carries the bindings that were in scope where
    /// it was written. It earns its place when the prefix or the URI is not known until the transformation
    /// runs — generating a document whose namespaces come from its input, most of all.
    /// </remarks>
    internal sealed class NamespaceInstruction : Instruction
    {
        /// <summary>The namespace of the <c>xml</c> prefix, which is bound in advance and cannot be re-bound.</summary>
        private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

        private readonly AttributeValueTemplate m_name;
        private readonly Expr? m_select;
        private readonly Instruction[] m_body;

        /// <summary>Initializes a namespace instruction.</summary>
        /// <param name="name">The prefix to bind, which may be a value template.</param>
        /// <param name="select">The expression giving the URI, if written with a <c>select</c>.</param>
        /// <param name="body">The content giving the URI otherwise.</param>
        public NamespaceInstruction(AttributeValueTemplate name, Expr? select, Instruction[] body)
        {
            m_name = name;
            m_select = select;
            m_body = body;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            string prefix = m_name.Evaluate(ref context);
            string namespaceUri = m_select is not null
                ? m_select.Evaluate(ref context).ToStringValue()
                : CaptureText(m_body, ref context, runtime);

            Check(prefix, namespaceUri);
            runtime.RequireAnElementForAttribute("A namespace node");
            runtime.Output.CreateNamespace(prefix, namespaceUri);
        }

        /// <summary>
        /// Refuses a binding that XML itself would not allow.
        /// </summary>
        /// <remarks>
        /// Checked here rather than left to the writer because the name and the URI are only known once the
        /// instruction runs, and a result carrying <c>xmlns:xmlns</c> or a prefix that is not a name is not
        /// XML at all — the failure would otherwise surface as a parse error in whatever reads the result.
        /// </remarks>
        private static void Check(string prefix, string namespaceUri)
        {
            if (prefix.Length != 0 && !IsNCName(prefix))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0920,
                    $"An xsl:namespace asked to bind '{prefix}', which is not a name. Give a prefix, or an "
                    + "empty name for the default namespace.");
            }

            if (prefix == "xmlns")
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0920, "An xsl:namespace cannot bind the prefix 'xmlns'.");
            }

            if (namespaceUri.Length == 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0930,
                    $"An xsl:namespace bound {(prefix.Length == 0 ? "the default namespace" : $"'{prefix}'")} "
                    + "to nothing. XML has no way to undeclare a namespace, so the URI cannot be empty.");
            }

            // The declarations' own namespace is refused, and so is text no escaping could make a URI of —
            // more than one fragment marker. A space is not that: the suite's on-empty-115b binds a prefix
            // to one, and every character an anyURI may carry escaped is allowed here as it is there.
            if (namespaceUri == ComputedName.XmlnsNamespace || namespaceUri.Count(c => c == '#') > 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0905,
                    $"An xsl:namespace cannot bind a prefix to '{namespaceUri}': it is "
                    + (namespaceUri == ComputedName.XmlnsNamespace
                        ? "the namespace the declarations themselves belong to."
                        : "not a URI."));
            }

            if ((prefix == "xml") != (namespaceUri == XmlNamespace))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0925,
                    "The prefix 'xml' and the namespace it names are bound to each other and to nothing "
                    + $"else, so binding '{prefix}' to '{namespaceUri}' is not allowed.");
            }
        }
    }

    /// <summary><c>xsl:comment</c>.</summary>
    internal sealed class CommentInstruction : Instruction
    {
        private readonly Instruction[] m_body;
        private readonly Expr? m_select;

        /// <summary>Initializes a comment instruction.</summary>
        /// <param name="body">The content giving the comment's text.</param>
        /// <param name="select">The expression giving it instead, which XSLT 2.0 allows.</param>
        public CommentInstruction(Instruction[] body, Expr? select = null)
        {
            m_body = body;
            m_select = select;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            runtime.Output.WriteComment(Spaced(m_select is null
                ? CaptureText(m_body, ref context, runtime)
                : Join(m_select.Evaluate(ref context), " ")));
        }

        /// <summary>
        /// A comment's text, with a space after any hyphen that would otherwise run into the next one or
        /// into the comment's own end.
        /// </summary>
        /// <remarks>
        /// XML has no escaping inside a comment, so <c>--</c> cannot be written in one and a comment cannot
        /// end with a hyphen. XSLT does not refuse the content for that: it says the processor inserts the
        /// space (§11.8), which keeps what the stylesheet wrote legible and the result well formed. So
        /// <c>--Valid comment--</c> is written <c>- -Valid comment- - </c>.
        /// </remarks>
        /// <param name="text">What the content or the select came to.</param>
        private static string Spaced(string text)
        {
            if (text.IndexOf('-') < 0)
            {
                return text;
            }

            StringBuilder written = new StringBuilder(text.Length + 4);

            for (int i = 0; i < text.Length; i++)
            {
                written.Append(text[i]);

                if (text[i] == '-' && (i + 1 == text.Length || text[i + 1] == '-'))
                {
                    written.Append(' ');
                }
            }

            return written.ToString();
        }
    }

    /// <summary><c>xsl:processing-instruction</c>.</summary>
    internal sealed class ProcessingInstructionInstruction : Instruction
    {
        private readonly AttributeValueTemplate m_name;
        private readonly Instruction[] m_body;
        private readonly Expr? m_select;

        /// <summary>Initializes a processing-instruction instruction.</summary>
        /// <param name="name">The target, which may be a value template.</param>
        /// <param name="body">The content giving the instruction's data.</param>
        /// <param name="select">The expression giving it instead, which XSLT 2.0 allows.</param>
        public ProcessingInstructionInstruction(
            AttributeValueTemplate name,
            Instruction[] body,
            Expr? select = null)
        {
            m_name = name;
            m_body = body;
            m_select = select;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            runtime.Output.WriteProcessingInstruction(
                ComputedName.ForProcessingInstruction(m_name.Evaluate(ref context)),
                Spaced(m_select is null
                    ? CaptureText(m_body, ref context, runtime)
                    : Join(m_select.Evaluate(ref context), " ")));
        }

        /// <summary>
        /// A processing instruction's data, with a space between any <c>?</c> and <c>&gt;</c> that would
        /// otherwise end the instruction where it stands.
        /// </summary>
        /// <remarks>
        /// The same arrangement XSLT makes for a comment's hyphens, and for the same reason (§11.7): a
        /// processing instruction has no escaping either, so those two characters cannot stand together in
        /// one, and the specification puts a space between them rather than refusing what was written. Data
        /// ending in a single <c>?</c> needs nothing: XML ends the instruction at the first <c>?&gt;</c>,
        /// and there is none.
        /// </remarks>
        /// <param name="data">What the content or the select came to.</param>
        private static string Spaced(string data)
        {
            return data.Replace("?>", "? >", StringComparison.Ordinal);
        }
    }

    /// <summary><c>xsl:message</c>, which writes to the runtime's message sink rather than the result.</summary>
    internal sealed class MessageInstruction : Instruction
    {
        /// <summary>
        /// The name an error-code attribute comes to, as a caller reads codes: the local part for the
        /// standard namespace, and <see langword="null"/> for text that is no name at all.
        /// </summary>
        /// <remarks>
        /// The attribute is a value template, so what it says is known only when the message is reached, and
        /// an expression may produce something no more a QName than a number is. The specification gives
        /// that no code of its own because the instruction already has one: a message naming no usable error
        /// raises the error a message raises when it names none.
        /// </remarks>
        /// <param name="written">The attribute as it came out.</param>
        private string? CodeOf(string written)
        {
            const string ErrorNamespace = "http://www.w3.org/2005/xqt-errors";
            string uri;
            string local;

            if (written.StartsWith("Q{", StringComparison.Ordinal) && written.IndexOf('}') is int close && close > 0)
            {
                uri = written[2..close];
                local = written[(close + 1)..];
            }
            else
            {
                int colon = written.IndexOf(':');
                local = colon < 0 ? written : written[(colon + 1)..];
                uri = colon < 0
                    ? ErrorNamespace
                    : Prefixes is not null && Prefixes.TryGetValue(written[..colon], out string? bound)
                        ? bound
                        : ErrorNamespace;
            }

            if (!IsNCName(local))
            {
                return null;
            }

            return uri == ErrorNamespace ? local : $"Q{{{uri}}}{local}";
        }

        private readonly Instruction[] m_body;
        private readonly bool m_terminate;

        /// <summary>
        /// Whether the processor reading this stylesheet implements XSLT 3.0.
        /// </summary>
        /// <remarks>
        /// Two things here follow the processor rather than the stylesheet's claim, and both are settled
        /// when the stylesheet is compiled: which spellings a computed <c>terminate</c> may use, the element
        /// table being unable to see one; and whether a message that cannot be built is reported or passed
        /// over, which is a guarantee XSLT 3.0 made and 2.0 did not.
        /// </remarks>
        private readonly bool m_implements30;

        /// <summary>The code a terminating message raises, where the stylesheet named one.</summary>
        public AttributeValueTemplate? ErrorCode { get; set; }

        /// <summary>The prefixes in scope where the message was written, for reading the code's.</summary>
        public IReadOnlyDictionary<string, string>? Prefixes { get; set; }
        private readonly AttributeValueTemplate? m_computedTerminate;

        /// <summary>Initializes a message instruction whose <c>terminate</c> was written out.</summary>
        /// <param name="body">What the message is built from.</param>
        /// <param name="terminate">Whether the message stops the transformation.</param>
        /// <param name="implements30">Whether the processor implements XSLT 3.0.</param>
        public MessageInstruction(Instruction[] body, bool terminate, bool implements30)
        {
            m_body = body;
            m_terminate = terminate;
            m_implements30 = implements30;
        }

        /// <summary>Initializes a message instruction whose <c>terminate</c> is computed when it is reached.</summary>
        /// <param name="body">What the message is built from.</param>
        /// <param name="terminate">The value template saying whether the message stops the transformation.</param>
        /// <param name="implements30">Whether the processor implements XSLT 3.0.</param>
        public MessageInstruction(
            Instruction[] body, AttributeValueTemplate terminate, bool implements30)
        {
            m_body = body;
            m_computedTerminate = terminate;
            m_implements30 = implements30;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            bool terminate = m_computedTerminate is null
                ? m_terminate
                : ReadTerminate(m_computedTerminate.Evaluate(ref context), m_implements30);

            XPathValue content = XPathValue.FromSequence(XdmSequence.Empty);
            string message;

            try
            {
                // A document node, built from the content by the rules a variable's content is built by
                // (§5.7.1): adjacent atomic values separated by a space, adjacent text merged, nodes
                // copied. That is what a message is — an xsl:message may be asked for the elements in it.
                content = VariableInstruction.Evaluate(null, m_body, ref context, runtime);

                message = Join(content, " ");
            }
            catch (XsltException failed) when (m_implements30)
            {
                // A message is a diagnostic, and one that cannot be produced is not itself a reason to
                // stop: XSLT 3.0 says in as many words that the transformation must not fail because the
                // content of an xsl:message could not be evaluated. What stands in its place is left open,
                // and saying why there is no message is more use than saying nothing. Rendering is inside
                // the same guard as evaluation, a map being a value the instruction may be given and no
                // value a message can be made of.
                //
                // The guarantee is 3.0's, and a 2.0 processor reports the error as it would anywhere else.
                // The suite holds both sides of that: message-0404 is an XSLT30+ test whose transformation
                // must survive a division by zero in a message, and result-document-1108 an XSLT20 one
                // whose xsl:result-document inside a message must still be XTDE1480.
                message = $"[this xsl:message could not be reported: {failed.Message}]";
            }

            runtime.WriteMessage(message);

            if (terminate)
            {
                // The same code xsl:assert raises, and for the same reason: both are a stylesheet stopping
                // the transformation deliberately, which is a different thing from a transformation that
                // went wrong. A caller distinguishing the two has to be able to tell them apart — unless the
                // stylesheet named the error itself, in which case that name is the code.
                string described = $"Transformation terminated by xsl:message: {message}";

                // The content travels with the error, so that an xsl:catch reads what the message said as
                // $err:value rather than only as text. It is the sequence the instruction built and not a
                // rendering of it: a message may carry a whole element, and the catch may go into it.
                if (ErrorCode is null || CodeOf(ErrorCode.Evaluate(ref context).Trim()) is not string named)
                {
                    throw new XsltException(XsltErrorCode.XTMM9000, described) { Value = content };
                }

                throw new XsltException(described, named) { Value = content };
            }
        }

        /// <summary>
        /// Reads what a computed <c>terminate</c> came to, refusing anything that is not yes or no.
        /// </summary>
        /// <remarks>
        /// An attribute written with curly brackets is checked when it is reached rather than when it is
        /// compiled, so the fixed set of values it may take is a dynamic error here where it is a static one
        /// on a value written out. Reading anything else as "no" would let a stylesheet ask to stop and be
        /// quietly carried on.
        /// </remarks>
        /// <param name="value">What the attribute came out as.</param>
        /// <param name="widened">Whether the four spellings XSLT 3.0 added are among the answers.</param>
        private static bool ReadTerminate(string value, bool widened)
        {
            return value.Trim() switch
            {
                "yes" => true,
                "no" => false,
                "true" or "1" when widened => true,
                "false" or "0" when widened => false,
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XTDE0030,
                    $"'{value}' is not one of the values terminate may take: "
                    + (widened ? "yes, no, true, false, 1, 0." : "yes, no.")),
            };
        }
    }

    /// <summary>
    /// An instruction this engine does not implement, standing in for it along with whatever
    /// <c>xsl:fallback</c> the stylesheet supplied.
    /// </summary>
    /// <remarks>
    /// The error waits until the instruction is reached, which is the whole point: a stylesheet may well
    /// contain an instruction meant for a different processor, guarded so that this one never runs it. Only if
    /// it is reached, and offered nothing to fall back to, is there anything to complain about.
    /// </remarks>
    internal sealed class FallbackInstruction : Instruction
    {
        private readonly Instruction[] m_fallback;
        private readonly bool m_given;
        private readonly string m_message;
        private readonly XsltErrorCode? m_code;

        /// <summary>Initializes a stand-in for an unimplemented instruction.</summary>
        /// <param name="fallback">What the instruction's <c>xsl:fallback</c> children compiled to.</param>
        /// <param name="given">
        /// Whether there was an <c>xsl:fallback</c> child at all, which is a different question from
        /// whether it compiled to anything: an empty one says to do nothing, and doing nothing is what it
        /// then does. Only the absence of one makes reaching the instruction an error.
        /// </param>
        /// <param name="message">What to report if the instruction is reached with nothing to fall back to.</param>
        /// <param name="code">The code to report it under, where the specification gives one.</param>
        public FallbackInstruction(
            Instruction[] fallback, bool given, string message, XsltErrorCode? code = null)
        {
            m_fallback = fallback;
            m_given = given;
            m_message = message;
            m_code = code;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            if (!m_given)
            {
                throw m_code is XsltErrorCode code
                    ? XsltErrors.Error(code, m_message)
                    : new XsltException(m_message);
            }

            ExecuteAll(m_fallback, ref context, runtime);
        }
    }

    /// <summary>Copies nodes from a tree to an output target.</summary>
    internal static class NodeCopier
    {
        /// <summary>Copies a node and everything beneath it.</summary>
        /// <param name="tree">The tree the node belongs to.</param>
        /// <param name="node">The node to copy.</param>
        /// <param name="output">The destination.</param>
        /// <param name="copyNamespaces">Whether an element's namespace nodes come with it.</param>
        /// <param name="copyAccumulators">
        /// Whether each copied node is to answer for the accumulators as the node it was copied from does,
        /// which is <c>copy-accumulators="yes"</c>.
        /// </param>
        /// <param name="runtime">
        /// The transformation, where the copy is to keep the node's base URI if it ends up parentless;
        /// null where that does not arise.
        /// </param>
        public static void CopyDeep(
            XdmTree tree,
            int node,
            OutputTarget output,
            bool copyNamespaces = true,
            bool copyAccumulators = false,
            XsltRuntime? runtime = null)
        {
            if (runtime is not null && output.OpenElementDepth == 0)
            {
                output.NoteSourceOfNext(tree, node, runtime, withAttributes: true);
            }

            // The walk is driven by the tree's own links — first child, next sibling, parent — rather than
            // by recursing once per level. The depth of a document is the one thing about its shape that a
            // caller cannot bound, and a stack overflow is not an error that can be caught: it takes the
            // process with it. The tree already records where each node's parent is, so climbing back out
            // of a subtree costs nothing that the stack would not have cost.
            int current = node;

            while (true)
            {
                NodeKind kind = tree.KindOf(current);
                int child = -1;

                if (kind is NodeKind.Element or NodeKind.Root)
                {
                    if (kind == NodeKind.Element)
                    {
                        StartElementCopy(
                            tree, current, output, copyNamespaces, copyAccumulators, root: current == node);
                    }
                    else
                    {
                        output.StartDocumentCopy();
                        output.NoteDocumentCopiedFrom(tree);
                    }

                    child = tree.FirstChildOf(current);
                }
                else
                {
                    CopyShallow(tree, current, output, copyAccumulators);
                }

                if (child >= 0)
                {
                    current = child;
                    continue;
                }

                // Nothing beneath this node, or nothing left: close it, and climb until there is a sibling
                // to go on to. The node the copy started from is where the climb stops, whatever is beside it.
                while (true)
                {
                    if (kind == NodeKind.Element)
                    {
                        output.EndElement();
                    }
                    else if (kind == NodeKind.Root)
                    {
                        output.EndDocumentCopy();
                    }

                    if (current == node)
                    {
                        return;
                    }

                    int next = tree.NextSiblingOf(current);
                    if (next >= 0)
                    {
                        current = next;
                        break;
                    }

                    current = tree.ParentOf(current);
                    kind = tree.KindOf(current);
                }
            }
        }

        /// <summary>Copies a node without its children.</summary>
        /// <param name="tree">The tree the node belongs to.</param>
        /// <param name="node">The node to copy.</param>
        /// <param name="output">The destination.</param>
        /// <param name="copyAccumulators">
        /// Whether the copy is to answer for the accumulators as the node it was copied from does.
        /// </param>
        public static void CopyShallow(
            XdmTree tree, int node, OutputTarget output, bool copyAccumulators = false)
        {
            NameTable names = tree.NameTable;

            switch (tree.KindOf(node))
            {
                case NodeKind.Text:
                    output.WriteText(tree.StringValueOf(node));
                    break;

                case NodeKind.Comment:
                    output.WriteComment(tree.StringValueOf(node));
                    break;

                case NodeKind.ProcessingInstruction:
                    output.WriteProcessingInstruction(
                        names.GetLocalName(tree.FingerprintOf(node)), tree.StringValueOf(node));
                    break;

                case NodeKind.Namespace:
                    // Goes back out as the declaration it stands for: its name is the prefix, its value the
                    // URI. Not noted either, for the reason an attribute is not.
                    output.WriteNamespaceDeclaration(
                        names.GetLocalName(tree.FingerprintOf(node)), tree.StringValueOf(node));
                    return;

                case NodeKind.Attribute:
                {
                    // Not noted: an attribute has no place of its own in the walk an accumulator makes, and
                    // its values are its element's.
                    int nameCode = tree.NameCodeOf(node);
                    int fingerprint = tree.FingerprintOf(node);

                    output.WriteAttribute(
                        names.GetPrefix(nameCode),
                        names.GetNamespaceUri(fingerprint),
                        names.GetLocalName(fingerprint),
                        tree.StringValueOf(node));
                    return;
                }

                case NodeKind.Element:
                    StartElementCopy(tree, node, output, copyNamespaces: true, copyAccumulators);
                    output.EndElement();
                    return;

                default:
                    return;
            }

            if (copyAccumulators)
            {
                output.NoteCopiedFrom(tree, node);
            }
        }

        private static void StartElementCopy(
            XdmTree tree,
            int element,
            OutputTarget output,
            bool copyNamespaces = true,
            bool copyAccumulators = false,
            bool root = true)
        {
            NameTable names = tree.NameTable;
            int nameCode = tree.NameCodeOf(element);
            int fingerprint = tree.FingerprintOf(element);

            output.StartElement(
                names.GetPrefix(nameCode),
                names.GetNamespaceUri(fingerprint),
                names.GetLocalName(fingerprint));

            output.MarkOwnNamespaces(root);

            if (copyAccumulators)
            {
                output.NoteCopiedFrom(tree, element);
            }

            // The element's namespace nodes are copied too, so a declaration survives even when nothing in the
            // copied subtree happens to use its prefix. copy-namespaces="no" asks for the opposite: only the
            // declarations the copied names actually need, which the writer supplies as it goes.
            if (copyNamespaces)
            {
                foreach ((string prefix, string uri) in tree.InScopeNamespacesOf(element))
                {
                    output.WriteNamespaceDeclaration(prefix, uri);
                }
            }

            int attributeCount = tree.AttributeCountOf(element);
            for (int i = 0; i < attributeCount; i++)
            {
                CopyShallow(tree, tree.AttributeAt(element, i), output);
            }
        }
    }

    /// <summary>
    /// Captures the string an instruction's content comes to: what <c>xsl:attribute</c>, <c>xsl:comment</c>,
    /// <c>xsl:processing-instruction</c> and <c>xsl:namespace</c> make of a sequence constructor.
    /// </summary>
    /// <remarks>
    /// XSLT 3.0 §5.7.2. The content is a sequence of items, and the string is their string values joined with
    /// the separator — a single space, or for xsl:attribute nothing unless it says otherwise — after zero-length text nodes have been discarded and adjacent text nodes merged, which
    /// is what makes two <c>xsl:value-of</c> in a row read <c>12</c> while <c>xsl:sequence select="1, 2"</c>
    /// reads <c>1 2</c>, and a document node copied in read as one item however many text nodes it holds.
    /// An element written here is one item, its string value being what atomizing it gives; a comment or a
    /// processing instruction at the top is one item too, and inside an element is no part of its value.
    /// </remarks>
    internal sealed class StringCaptureTarget : OutputTarget
    {
        private readonly List<string> m_items = new();
        private readonly StringBuilder m_text = new();
        private readonly string m_separator;
        private bool m_hasText;
        private bool m_lastWasAtomic;
        private int m_depth;

        /// <summary>Initializes the target.</summary>
        /// <param name="separator">What goes between two items' strings.</param>
        public StringCaptureTarget(string separator)
        {
            m_separator = separator;
        }

        /// <inheritdoc/>
        public override bool BecomesAString => true;

        /// <inheritdoc/>
        public override int OpenElementDepth => m_depth;

        /// <inheritdoc/>
        public override void StartElement(string prefix, string namespaceUri, string localName)
        {
            if (m_depth++ == 0)
            {
                EndText();
            }

            m_lastWasAtomic = false;
        }

        /// <inheritdoc/>
        public override void StartDocumentCopy()
        {
            // One item, its string value being all the text beneath it — the same shape an element has.
            StartElement(string.Empty, string.Empty, string.Empty);
        }

        /// <inheritdoc/>
        public override void EndDocumentCopy()
        {
            EndElement();
        }

        /// <inheritdoc/>
        public override void WriteAttribute(string prefix, string namespaceUri, string localName, string value)
        {
            m_lastWasAtomic = false;

            // An attribute of an element written here is no part of the element's string value. One written
            // at the top is an item, whose string value is its value.
            if (m_depth == 0)
            {
                EndText();
                m_items.Add(value);
            }
        }

        /// <inheritdoc/>
        public override void WriteNamespaceDeclaration(string prefix, string namespaceUri)
        {
            m_lastWasAtomic = false;
        }

        /// <inheritdoc/>
        public override void EndElement()
        {
            m_lastWasAtomic = false;

            if (--m_depth == 0)
            {
                // The element is one item, and its string value is all the text beneath it.
                m_items.Add(m_text.ToString());
                m_text.Clear();
                m_hasText = false;
            }
        }

        /// <inheritdoc/>
        public override void WriteText(string text)
        {
            // A zero-length text node is discarded before an item is made of it, but it still ends a run of
            // atomic values: the suite's on-empty-113a has an empty text between two of them and no space.
            m_lastWasAtomic = false;

            if (text.Length == 0)
            {
                return;
            }

            m_text.Append(text);
            m_hasText = true;
        }

        /// <inheritdoc/>
        public override void WriteRawText(string text) => WriteText(text);

        /// <inheritdoc/>
        public override void WriteAtomic(string text)
        {
            if (m_depth != 0)
            {
                // Text of the element, a space before it where an atomic value came just before, as in a tree.
                if (m_lastWasAtomic)
                {
                    m_text.Append(' ');
                }

                m_text.Append(text);
                m_lastWasAtomic = true;
                return;
            }

            EndText();
            m_items.Add(text);
        }

        /// <inheritdoc/>
        public override void WriteComment(string text)
        {
            m_lastWasAtomic = false;

            if (m_depth == 0)
            {
                EndText();
                m_items.Add(text);
            }
        }

        /// <inheritdoc/>
        public override void WriteProcessingInstruction(string target, string data)
        {
            m_lastWasAtomic = false;

            if (m_depth == 0)
            {
                EndText();
                m_items.Add(data);
            }
        }

        /// <summary>Closes a run of adjacent text nodes at the top, which is one item.</summary>
        private void EndText()
        {
            if (!m_hasText)
            {
                return;
            }

            m_items.Add(m_text.ToString());
            m_text.Clear();
            m_hasText = false;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            EndText();
            return string.Join(m_separator, m_items);
        }
    }
}
