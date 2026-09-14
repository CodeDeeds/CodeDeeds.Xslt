using System.Text;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// <c>xsl:evaluate</c>: an XPath expression that arrives as a string while the transformation runs,
    /// compiled then and evaluated against a context the instruction assembles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else in a stylesheet is compiled before the first node is read, and the whole engine is
    /// built on that: a name test is a slot resolved against the input tree's name table when the transform
    /// starts, a variable is a slot in a frame whose size is known, a function call is bound to its
    /// declaration. A target expression has none of that settled, so this instruction builds a static
    /// context of its own (<see cref="DynamicStaticContext"/>) from what the specification says the
    /// expression may see — the namespaces in scope, the parameters supplied to it, the stylesheet's public
    /// functions and decimal formats, its base URI — and hands the string to the same parser everything else
    /// went through. What comes back is an ordinary <see cref="Expr"/>, evaluated in a frame holding the
    /// parameters and nothing else.
    /// </para>
    /// <para>
    /// A name the expression tests against may be one the stylesheet never mentioned, which is a slot the
    /// stylesheet's <see cref="NameSlotTable"/> did not have. The table is allowed to grow for it — it only
    /// ever grows, so a slot handed out stays good — and <see cref="XsltRuntime.GetFingerprintMap"/> notices
    /// a mapping shorter than the table and builds it again. That, and the lock the table takes, is what
    /// keeps a compiled stylesheet shareable between threads with this instruction in it.
    /// </para>
    /// <para>
    /// The compiled form is cached, as the specification suggests, keyed by the string together with
    /// everything the compilation depended on: the base URI, the namespace bindings, the default element
    /// namespace and the parameter names in slot order. Two evaluations that differ in any of those compile
    /// separately, which is what a test that changes its parameter names between calls is checking for.
    /// </para>
    /// </remarks>
    internal sealed class EvaluateInstruction : Instruction
    {
        /// <summary>How many compiled targets one instruction keeps before starting over.</summary>
        private const int CacheLimit = 64;

        private static readonly XdmSequenceType s_string =
            XPathParser.ParseType("xs:string", new XPathStaticContext { Version = XsltVersion.V30 });

        private readonly Expr m_xpath;
        private readonly XdmSequenceType? m_resultType;
        private readonly AttributeValueTemplate? m_baseUri;
        private readonly string? m_staticBaseUri;
        private readonly Expr? m_withParams;
        private readonly Expr? m_contextItem;
        private readonly Expr? m_namespaceContext;
        private readonly AttributeValueTemplate? m_schemaAware;
        private readonly WithParameter[] m_parameters;
        private readonly Dictionary<string, string> m_namespaces;
        private readonly string m_defaultElementNamespace;
        private readonly NameSlotTable m_names;
        private readonly IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> m_functions;
        private readonly IReadOnlyDictionary<ExpandedName, DecimalFormat> m_decimalFormats;
        private readonly string m_defaultCollation;
        private readonly XsltVersion m_version;
        private readonly bool m_enabled;
        private readonly Instruction[]? m_fallback;
        private readonly Dictionary<string, Expr> m_cache = new(StringComparer.Ordinal);

        /// <summary>Initializes an <c>xsl:evaluate</c>.</summary>
        /// <param name="xpath">The expression giving the target expression, as a string.</param>
        /// <param name="resultType">The <c>as</c>, or null for <c>item()*</c>.</param>
        /// <param name="baseUri">The <c>base-uri</c> template, or null to use the instruction's own.</param>
        /// <param name="staticBaseUri">The base URI of the instruction itself.</param>
        /// <param name="withParams">The <c>with-params</c> expression, or null.</param>
        /// <param name="contextItem">The <c>context-item</c> expression, or null for no focus.</param>
        /// <param name="namespaceContext">The <c>namespace-context</c> expression, or null.</param>
        /// <param name="schemaAware">The <c>schema-aware</c> template, or null.</param>
        /// <param name="parameters">The <c>xsl:with-param</c> children.</param>
        /// <param name="namespaces">The prefixes in scope on the instruction, the default one left out.</param>
        /// <param name="defaultElementNamespace">The <c>xpath-default-namespace</c> in scope.</param>
        /// <param name="names">The stylesheet's name-slot table, which the target's name tests join.</param>
        /// <param name="functions">The stylesheet's functions, by name and arity.</param>
        /// <param name="decimalFormats">The stylesheet's decimal formats, by name.</param>
        /// <param name="defaultCollation">
        /// The default collation in scope where the instruction stands, which is the target expression's
        /// too. The specification says so in one line: the default collation of the target expression is
        /// the one defined at that point in the stylesheet.
        /// </param>
        /// <param name="version">The version in force where the instruction stands.</param>
        /// <param name="enabled">Whether dynamic evaluation is switched on for this stylesheet.</param>
        /// <param name="fallback">The <c>xsl:fallback</c> content, or null where there is none.</param>
        public EvaluateInstruction(
            Expr xpath,
            XdmSequenceType? resultType,
            AttributeValueTemplate? baseUri,
            string? staticBaseUri,
            Expr? withParams,
            Expr? contextItem,
            Expr? namespaceContext,
            AttributeValueTemplate? schemaAware,
            WithParameter[] parameters,
            Dictionary<string, string> namespaces,
            string defaultElementNamespace,
            NameSlotTable names,
            IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> functions,
            IReadOnlyDictionary<ExpandedName, DecimalFormat> decimalFormats,
            string defaultCollation,
            XsltVersion version,
            bool enabled,
            Instruction[]? fallback)
        {
            m_xpath = xpath;
            m_resultType = resultType;
            m_baseUri = baseUri;
            m_staticBaseUri = staticBaseUri;
            m_withParams = withParams;
            m_contextItem = contextItem;
            m_namespaceContext = namespaceContext;
            m_schemaAware = schemaAware;
            m_parameters = parameters;
            m_namespaces = namespaces;
            m_defaultElementNamespace = defaultElementNamespace;
            m_names = names;
            m_functions = functions;
            m_decimalFormats = decimalFormats;
            m_defaultCollation = defaultCollation;
            m_version = version;
            m_enabled = enabled;
            m_fallback = fallback;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            if (!m_enabled)
            {
                // Statically disabled, in the specification's terms: the option is fixed when the stylesheet
                // is compiled, so element-available() and system-property() have been saying no all along,
                // and reaching the instruction takes its fallback or is the error the specification names.
                if (m_fallback is null)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE3175,
                        "xsl:evaluate was reached, and dynamic evaluation is switched off for this "
                        + "transformation (XsltOptions.DynamicEvaluation). Give the instruction an "
                        + "xsl:fallback to say what to do instead.");
                }

                foreach (Instruction instruction in m_fallback)
                {
                    instruction.Execute(ref context, runtime);
                }

                return;
            }

            // The target expression: whatever xpath produced, read as one string by the function conversion
            // rules — so an attribute node holding the expression is as good as a literal.
            string target = XdmTypeConversion.Apply(m_xpath.Evaluate(ref context), s_string).ToStringValue();

            List<ExpandedName> names = new List<ExpandedName>(m_parameters.Length);
            List<XPathValue> values = new List<XPathValue>(m_parameters.Length);

            foreach (WithParameter parameter in m_parameters)
            {
                names.Add(parameter.Name);
                values.Add(VariableInstruction.Evaluate(
                    parameter.Select,
                    parameter.Body,
                    ref context,
                    runtime,
                    parameter.Type,
                    XsltErrorCode.XTTE0590,
                    parameter.BaseUri));
            }

            if (m_withParams is not null)
            {
                BindFromMap(m_withParams.Evaluate(ref context), names, values);
            }

            Dictionary<string, string> namespaces = m_namespaces;
            string defaultNamespace = m_defaultElementNamespace;

            if (m_namespaceContext is not null)
            {
                // The node's in-scope namespaces stand in for the instruction's, its default namespace
                // included: an expression read out of a document means what it meant there.
                XPathValue node = RequireOneNode(m_namespaceContext.Evaluate(ref context));
                namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
                defaultNamespace = string.Empty;

                foreach ((string prefix, string uri) in node.NodeTree.InScopeNamespacesOf(node.NodeId))
                {
                    if (prefix.Length == 0)
                    {
                        defaultNamespace = uri;
                    }
                    else
                    {
                        namespaces[prefix] = uri;
                    }
                }
            }

            string? baseUri = m_baseUri is not null ? m_baseUri.Evaluate(ref context).Trim() : m_staticBaseUri;

            // The imported schemas' types are in scope for the target only where schema-aware is yes; the
            // default is no, as the specification has it. A target that names a schema type where they are
            // not is XTDE3160, and not the XPST0051 an unknown type usually gets.
            bool schemaAware = false;

            if (m_schemaAware is not null)
            {
                string said = m_schemaAware.Evaluate(ref context).Trim();

                schemaAware = said switch
                {
                    "yes" or "true" or "1" => true,
                    "no" or "false" or "0" => false,
                    _ => throw XsltErrors.Error(
                        XsltErrorCode.XTDE0030,
                        $"'{said}' is not one of the values 'schema-aware' may take: yes, no, true, false, 1, 0."),
                };
            }

            Compiler.SchemaComponents? schemas = schemaAware ? runtime.Schemas : null;
            Expr compiled;

            try
            {
                compiled = Compile(
                    target, baseUri, namespaces, defaultNamespace, names, runtime.CollationResolver, schemas);
            }
            catch (XsltException failed)
                when (!schemaAware
                    && runtime.Schemas is not null
                    && failed.Code is "XPST0051" or "XPST0008"
                    && CompilesWithSchemas(target, baseUri, namespaces, defaultNamespace, names, runtime))
            {
                // The target named a schema type or declaration, which is in scope only with schema-aware
                // yes; naming one here is the error the specification gives that.
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE3160,
                    "The xsl:evaluate has schema-aware=\"no\", and its target names a type or declaration "
                    + "the imported schemas define, which is in scope only where schema-aware is yes.",
                    failed);
            }

            // The target's own dynamic context: the focus context-item says, or none; the parameters and no
            // other variable; and none of what XSLT adds — no current node, no current group, nothing a
            // function this context does not offer could have read anyway.
            DynamicContext inner = context;
            inner.CurrentNode = DynamicContext.NotANode;
            inner.Node = DynamicContext.NotANode;
            inner.AtomicItem = default;
            inner.Position = 0;
            inner.Size = 0;

            if (m_contextItem is not null)
            {
                List<XPathValue> items = XdmSequence.Items(m_contextItem.Evaluate(ref context));

                if (items.Count > 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTTE3210,
                        $"The context-item of xsl:evaluate selected {items.Count} items, and a focus is one item.");
                }

                if (items.Count == 1)
                {
                    inner = inner.WithItem(items[0]);
                    inner.Position = 1;
                    inner.Size = 1;
                }
            }

            inner.Locals = values.ToArray();
            inner.FrameBase = 0;
            inner.RangeVariables = null;

            // Compiling may have given a name its first slot, which the mapping this context carries does not
            // reach; the runtime's copy is rebuilt when it is behind, and this is where that is asked for.
            if (inner.Tree is not null)
            {
                inner.FingerprintMap = runtime.GetFingerprintMap(inner.Tree);
            }

            XPathValue result = compiled.Evaluate(ref inner);
            SequenceWriter.Write(XdmTypeConversion.Apply(result, m_resultType), runtime);
        }

        /// <summary>
        /// Adds the entries of a <c>with-params</c> map to the parameters, a name the map shares with an
        /// <c>xsl:with-param</c> taking the map's value.
        /// </summary>
        private static void BindFromMap(XPathValue supplied, List<ExpandedName> names, List<XPathValue> values)
        {
            List<XPathValue> items = XdmSequence.Items(supplied);

            if (items.Count != 1 || items[0].Kind != XPathValueKind.Map)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE3165,
                    "The with-params of xsl:evaluate must be one map of type map(xs:QName, item()*), and "
                    + (items.Count == 1 ? "the value given is not a map." : $"{items.Count} items were given."));
            }

            foreach (KeyValuePair<XPathValue, XPathValue> entry in items[0].AsMap().Entries)
            {
                if (entry.Key.TypeCode != XdmTypeCode.QName)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTTE3165,
                        $"The with-params map of xsl:evaluate has the key '{entry.Key.ToStringValue()}', which "
                        + "is not an xs:QName. A parameter is named by a QName, not by a string.");
                }

                XdmQName key = entry.Key.AsQName();
                ExpandedName name = new ExpandedName(key.NamespaceUri, key.LocalName);
                int existing = names.IndexOf(name);

                if (existing >= 0)
                {
                    values[existing] = entry.Value;
                }
                else
                {
                    names.Add(name);
                    values.Add(entry.Value);
                }
            }
        }

        /// <summary>Whether the target would compile with the imported schemas in scope, which tells a use of a schema type from a genuinely unknown one.</summary>
        private bool CompilesWithSchemas(
            string target,
            string? baseUri,
            Dictionary<string, string> namespaces,
            string defaultNamespace,
            List<ExpandedName> parameters,
            XsltRuntime runtime)
        {
            try
            {
                Compile(target, baseUri, namespaces, defaultNamespace, parameters, runtime.CollationResolver, runtime.Schemas);
                return true;
            }
            catch (XsltException)
            {
                return false;
            }
        }

        private static XPathValue RequireOneNode(XPathValue value)
        {
            List<XPathValue> items = XdmSequence.Items(value);

            if (items.Count != 1 || items[0].Kind != XPathValueKind.Node)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE3170,
                    "The namespace-context of xsl:evaluate must select one node, whose in-scope namespaces the "
                    + (items.Count == 1 ? "expression is read in; the value given is not a node." : $"expression is read in; {items.Count} items were given."));
            }

            return items[0];
        }

        /// <summary>
        /// Compiles the target expression against the context assembled for it, or finds it compiled.
        /// </summary>
        private Expr Compile(
            string target,
            string? baseUri,
            Dictionary<string, string> namespaces,
            string defaultNamespace,
            List<ExpandedName> parameters,
            IXsltCollationResolver? collations,
            SchemaComponents? schemas)
        {
            // The schemas are part of what the target compiles against: the same expression is a different
            // expression with the imported types in scope and without, so the two are cached apart.
            StringBuilder key = new StringBuilder(target)
                .Append('\n').Append(baseUri).Append('\n').Append(defaultNamespace)
                .Append('\n').Append(schemas is null ? '0' : '1');

            foreach (KeyValuePair<string, string> binding in namespaces.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                key.Append('\n').Append(binding.Key).Append('=').Append(binding.Value);
            }

            foreach (ExpandedName name in parameters)
            {
                key.Append("\n$").Append(name.NamespaceUri).Append(':').Append(name.LocalName);
            }

            string cacheKey = key.ToString();

            lock (m_cache)
            {
                if (m_cache.TryGetValue(cacheKey, out Expr? known))
                {
                    return known;
                }
            }

            DynamicStaticContext scope = new DynamicStaticContext(
                m_names,
                m_version,
                namespaces,
                defaultNamespace,
                baseUri,
                parameters,
                m_functions,
                m_decimalFormats,
                m_defaultCollation,
                collations,
                schemas);

            Expr compiled;

            try
            {
                compiled = XPathParser.Parse(target, scope);
            }
            catch (XsltException failed) when (failed.Code is null or "XPST0003" or "XPST0017")
            {
                // Not an expression, or one calling a function this context does not offer: the
                // specification's own code for a target that cannot be evaluated. A variable nobody supplied
                // and a prefix nothing binds keep XPath's codes, which say more precisely what was wrong.
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE3160,
                    $"The expression handed to xsl:evaluate cannot be evaluated: {failed.Message}",
                    failed);
            }

            lock (m_cache)
            {
                if (m_cache.Count >= CacheLimit)
                {
                    m_cache.Clear();
                }

                m_cache[cacheKey] = compiled;
            }

            return compiled;
        }
    }

    /// <summary>
    /// The static context a target expression of <c>xsl:evaluate</c> is compiled in: what §10.4.1 of XSLT
    /// 3.0 lists, and nothing the stylesheet around the instruction would otherwise have lent it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The variables are the parameters supplied and no other — not the stylesheet's globals, not the locals
    /// in scope where the instruction stands — each in a slot of a frame made for the evaluation. The
    /// functions are the XPath library's, the map, array and math ones, the constructors, and the
    /// stylesheet's own functions whose visibility is public or final; a private one is not there, and
    /// neither is anything XSLT adds to the function namespace — <c>current()</c>, <c>key()</c>,
    /// <c>document()</c>, <c>system-property()</c> — which is the specification's list and not an omission.
    /// A call to any of those is <c>XTDE3160</c> when the expression is compiled, which for the stylesheet
    /// is a dynamic error it can catch.
    /// </para>
    /// <para>
    /// The base URI is answered here rather than carried in the context, because that is where the compiler
    /// answers it for an ordinary expression: <c>static-base-uri()</c> folds to a constant and a
    /// one-argument <c>resolve-uri()</c> is given its second argument.
    /// </para>
    /// </remarks>
    internal sealed class DynamicStaticContext : IXPathStaticContext, ISchemaTypeProvider
    {
        private readonly XsltVersion m_version;
        private readonly Dictionary<string, string> m_namespaces;
        private readonly string m_defaultElementNamespace;
        private readonly string? m_baseUri;
        private readonly Dictionary<ExpandedName, int> m_slots = new();
        private readonly IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> m_functions;
        private readonly IReadOnlyDictionary<ExpandedName, DecimalFormat> m_decimalFormats;

        /// <summary>Initializes the context.</summary>
        public DynamicStaticContext(
            NameSlotTable names,
            XsltVersion version,
            Dictionary<string, string> namespaces,
            string defaultElementNamespace,
            string? baseUri,
            List<ExpandedName> parameters,
            IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> functions,
            IReadOnlyDictionary<ExpandedName, DecimalFormat> decimalFormats,
            string defaultCollation,
            IXsltCollationResolver? collations,
            SchemaComponents? schemas = null)
        {
            DefaultCollation = defaultCollation;
            CollationResolver = collations;
            m_schemas = schemas;
            Names = names;
            m_version = version;
            m_namespaces = namespaces;
            m_defaultElementNamespace = defaultElementNamespace;
            m_baseUri = baseUri;
            m_functions = functions;
            m_decimalFormats = decimalFormats;

            for (int i = 0; i < parameters.Count; i++)
            {
                m_slots[parameters[i]] = i;
            }
        }

        /// <inheritdoc/>
        public NameSlotTable Names { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// The collation in scope where the xsl:evaluate stands, not the code point one. A target
        /// expression is compiled somewhere other than where it was written, and everything else about its
        /// static context is taken from the instruction -- the namespaces, the base URI, the default
        /// element namespace -- so the collation is too, and the specification says so in as many words.
        /// </remarks>
        public string DefaultCollation { get; }

        /// <inheritdoc/>
        /// <remarks>The caller's, as everywhere: the transformation running the instruction has them.</remarks>
        public IXsltCollationResolver? CollationResolver { get; }

        private readonly SchemaComponents? m_schemas;

        /// <inheritdoc/>
        /// <remarks>The stylesheet's, so that a target expression names the types the stylesheet imported.</remarks>
        public XdmSchemaType? ResolveSchemaType(string namespaceUri, string localName)
        {
            return m_schemas?.FindType(namespaceUri, localName);
        }

        /// <inheritdoc/>
        public XdmSchemaDeclaration? ResolveElementDeclaration(string namespaceUri, string localName)
        {
            return m_schemas?.FindElement(namespaceUri, localName);
        }

        /// <inheritdoc/>
        public XdmSchemaDeclaration? ResolveAttributeDeclaration(string namespaceUri, string localName)
        {
            return m_schemas?.FindAttribute(namespaceUri, localName);
        }

        /// <inheritdoc/>
        public XsltVersion Version => m_version;

        /// <inheritdoc/>
        public XsltVersion SyntaxVersion => m_version;

        /// <inheritdoc/>
        /// <remarks>Never: XPath 1.0 compatibility mode is false for a target expression, whatever the stylesheet says.</remarks>
        public bool LegacySyntax => false;

        /// <inheritdoc/>
        public string DefaultElementNamespace => m_defaultElementNamespace;

        /// <inheritdoc/>
        public string? ResolvePrefix(string prefix)
        {
            if (prefix == "xml")
            {
                return XdmTree.XmlNamespaceUri;
            }

            // The namespaces are those the namespace-context option carried, or those in scope on the
            // xsl:evaluate itself, and nothing besides — the same rule the rest of the stylesheet is read
            // under, so an expression means the same thing whether it was written down or evaluated.
            return m_namespaces.TryGetValue(prefix, out string? uri) ? uri : null;
        }

        /// <inheritdoc/>
        public bool TryResolveVariable(string namespaceUri, string localName, out int slot, out bool isGlobal)
        {
            isGlobal = false;
            return m_slots.TryGetValue(new ExpandedName(namespaceUri, localName), out slot);
        }

        /// <inheritdoc/>
        public DecimalFormat? ResolveDecimalFormat(ExpandedName name)
        {
            if (m_decimalFormats.TryGetValue(name, out DecimalFormat? declared))
            {
                return declared;
            }

            return name.LocalName.Length == 0 ? DecimalFormat.Default : null;
        }

        /// <inheritdoc/>
        public Expr? TryCreateFunction(string name, Expr[] arguments)
        {
            if (name == "static-base-uri" && arguments.Length == 0)
            {
                return m_baseUri is string baseUri
                    ? new TypedLiteralExpr(XPathValue.FromAnyUri(baseUri))
                    : new EmptySequenceExpr();
            }

            if (name == "resolve-uri" && arguments.Length == 1 && m_baseUri is string against)
            {
                return Xpath2FunctionExpr.TryCreate(
                    "resolve-uri", new[] { arguments[0], new StringLiteralExpr(against) }, m_version);
            }

            // A bare name, or one in a namespace a library answers for, is the library's. XSLT's own
            // additions to the function namespace are deliberately not offered, so a bare name the library
            // does not know is one the target expression cannot call.
            if (!TryExpand(name, out ExpandedName expanded)
                || expanded.NamespaceUri is XdmType.FunctionNamespace
                    or XdmType.SchemaNamespace
                    or Xpath30FunctionExpr.MathNamespace
                    or MapArrayFunctionExpr.MapNamespace
                    or MapArrayFunctionExpr.ArrayNamespace)
            {
                return null;
            }

            if (m_functions.TryGetValue((expanded, arguments.Length), out UserFunction? declared))
            {
                if (declared.Visibility is Visibility.Public or Visibility.Final)
                {
                    return new UserFunctionCallExpr(declared, arguments);
                }

                throw XsltErrors.Error(
                    XsltErrorCode.XTDE3160,
                    $"'{name}()' is declared, and not offered to xsl:evaluate: only a function whose "
                    + "visibility is public or final can be called from a target expression.");
            }

            throw XsltErrors.Error(
                XsltErrorCode.XTDE3160,
                $"'{name}()' with {arguments.Length} argument(s) is not a function a target expression of "
                + "xsl:evaluate can call. It sees the XPath library, the map, array and math functions, and "
                + "the stylesheet's public functions.");
        }

        /// <summary>Resolves a name as written — prefixed, or braced — to its expanded form.</summary>
        /// <returns>False for a bare name, which has no namespace to resolve.</returns>
        private bool TryExpand(string name, out ExpandedName expanded)
        {
            if (name.StartsWith("Q{", StringComparison.Ordinal) && name.IndexOf('}') is int close and > 1)
            {
                expanded = new ExpandedName(name[2..close], name[(close + 1)..]);
                return true;
            }

            int colon = name.IndexOf(':');

            if (colon < 0)
            {
                expanded = default;
                return false;
            }

            string prefix = name[..colon];
            string uri = ResolvePrefix(prefix)
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XPST0081,
                    $"'{name}' uses the prefix '{prefix}', which nothing in scope for the target expression binds.");

            expanded = new ExpandedName(uri, name[(colon + 1)..]);
            return true;
        }
    }
}
