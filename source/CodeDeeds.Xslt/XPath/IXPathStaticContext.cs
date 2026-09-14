namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// The compile-time information an XPath expression is resolved against: namespace bindings, variable
    /// slots, and the name-slot table shared with the rest of the stylesheet.
    /// </summary>
    public interface IXPathStaticContext
    {
        /// <summary>Gets the table assigning slots to the names this expression tests against.</summary>
        NameSlotTable Names { get; }

        /// <summary>
        /// Gets the XSLT version in force where the expression is written.
        /// </summary>
        /// <remarks>
        /// XPath 2.0 redefined several things XPath 1.0 had settled — how <c>&lt;</c> compares two strings,
        /// whether arithmetic on integers stays integral — and XSLT selects between the two readings by the
        /// version in scope. Since that is known when the expression is compiled, an expression can be built
        /// for one set of rules and never test for the other while it runs.
        /// </remarks>
        XsltVersion Version => XsltVersion.V10;

        /// <summary>
        /// Gets whether expressions are read with XPath 1.0's own grammar and function library.
        /// </summary>
        /// <remarks>
        /// Not the same question as <see cref="XsltVersion.IsBackwardsCompatible"/>, and the difference is
        /// the whole of what backwards compatibility means. A stylesheet saying <c>version="1.0"</c> is still
        /// being run by a 2.0 processor: it gets the 2.0 grammar and the 2.0 function library, and what
        /// changes is how the values are read — <c>'10' &lt; '9'</c>, a sequence where one item was wanted,
        /// an argument of the wrong type. So <c>1 to 5</c> parses there, and <c>xsl:value-of</c> writes the
        /// first item of what it gives.
        /// <para>
        /// True only where there is no 2.0 processor above the expression: a bare static context compiling
        /// XPath 1.0 on its own, which is what makes this engine's 1.0 half testable against a 1.0 oracle.
        /// </para>
        /// </remarks>
        bool LegacySyntax => Version.IsBackwardsCompatible;

        /// <summary>
        /// Gets the version whose grammar and function library are available, which is not always the version
        /// whose behaviour is in force.
        /// </summary>
        /// <remarks>
        /// The same distinction <see cref="LegacySyntax"/> draws, carried up a version. A stylesheet saying
        /// <c>version="2.0"</c> and run by a 3.0 processor is asking for 2.0 <em>behaviour</em> — how
        /// <c>&lt;</c> compares, what happens to a sequence where one item was wanted — and not for a smaller
        /// language. So the 3.0 additions to the regular expression syntax are available in it, and the W3C
        /// suite says so directly: its whole regex test set declares <c>XSLT30+</c> and uses a
        /// <c>version="2.0"</c> stylesheet.
        /// <para>
        /// Defaults to <see cref="Version"/>, which is right for a bare static context compiling XPath on its
        /// own: there the version asked for is the whole of what is being offered.
        /// </para>
        /// </remarks>
        XsltVersion SyntaxVersion => Version;

        /// <summary>
        /// Gets the collation strings are compared by where the expression names none, as a URI.
        /// </summary>
        /// <remarks>
        /// XSLT's <c>default-collation</c> puts one in scope for everything written below it, and it is
        /// static: an expression is compiled against the collation it stands among rather than looking one
        /// up while it runs. The default is the code point collation, which is what the specifications make
        /// the fallback and what a context with nothing to say answers.
        /// </remarks>
        string DefaultCollation => "http://www.w3.org/2005/xpath-functions/collation/codepoint";

        /// <summary>
        /// Gets the collations of the caller's own, which a collation URI the engine does not provide is
        /// looked up in, or <see langword="null"/> where there are none.
        /// </summary>
        IXsltCollationResolver? CollationResolver => null;

        /// <summary>
        /// Gets the namespace bindings in scope where the expression is written, by prefix.
        /// </summary>
        /// <remarks>
        /// <see cref="ResolvePrefix"/> answers for one prefix and is what almost everything wants. This is
        /// for the expression that has to carry the bindings with it: casting a computed string to
        /// <c>xs:QName</c> resolves the prefix it finds there against the namespaces where the cast was
        /// written, and the cast runs long after the compiler has moved on. A context with nothing to
        /// enumerate answers with nothing, and such a cast then resolves only a name with no prefix.
        /// </remarks>
        IReadOnlyDictionary<string, string> InScopeNamespaces
            => new Dictionary<string, string>(0, StringComparer.Ordinal);

        /// <summary>
        /// Resolves a namespace prefix as it is bound where the expression appears.
        /// </summary>
        /// <param name="prefix">The prefix to resolve. Never an empty string; see the remarks on
        /// <see cref="XPathParser"/> for why unprefixed names bypass this.</param>
        /// <returns>The namespace URI, or <see langword="null"/> if the prefix is not bound.</returns>
        string? ResolvePrefix(string prefix);

        /// <summary>
        /// Gets the namespace an unprefixed element or type name is in, empty for no namespace.
        /// </summary>
        /// <remarks>
        /// The default <em>element/type</em> namespace, which is a different thing from the default
        /// namespace an unprefixed element name in the document has: XPath deliberately does not read
        /// <c>xmlns</c> for this, so a stylesheet has to say so. XSLT says so with
        /// <c>xpath-default-namespace</c>. It reaches element names and type names and nothing else — an
        /// attribute has no default namespace, and neither does a variable or a function.
        /// </remarks>
        string DefaultElementNamespace => string.Empty;

        /// <summary>
        /// Resolves a variable reference to the slot holding its value.
        /// </summary>
        /// <param name="namespaceUri">The variable's namespace URI, usually empty.</param>
        /// <param name="localName">The variable's local name.</param>
        /// <param name="slot">On success, the slot index.</param>
        /// <param name="isGlobal">On success, whether the slot is in global storage rather than the current frame.</param>
        /// <returns><see langword="true"/> if the variable is in scope.</returns>
        bool TryResolveVariable(string namespaceUri, string localName, out int slot, out bool isGlobal);

        /// <summary>
        /// Resolves a variable whose value is already settled where the expression is compiled.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0's <c>static</c> variables and parameters. Their values are fixed while the stylesheet is
        /// still being read, which is what lets a <c>use-when</c> ask about one — so they have no slot and
        /// resolve to the value itself. Asked before <see cref="TryResolveVariable"/>, because a static
        /// variable is the same variable in an ordinary expression and there is nothing to gain by looking
        /// it up twice at run time.
        /// </remarks>
        /// <param name="namespaceUri">The variable's namespace URI, usually empty.</param>
        /// <param name="localName">The variable's local name.</param>
        /// <returns>The value it stands for, or <see langword="null"/> if no static variable has that name.</returns>
        XPathValue? TryResolveStaticVariable(string namespaceUri, string localName) => null;

        /// <summary>
        /// Creates a function that is not part of the XPath core library.
        /// </summary>
        /// <remarks>
        /// XSLT adds functions of its own — <c>key()</c> among them — which need access to the stylesheet
        /// rather than only to the expression. This lets the host supply them without the XPath parser having
        /// to know anything about XSLT. Returning <see langword="null"/> leaves the name to the core library.
        /// </remarks>
        /// <param name="name">The function name as written.</param>
        /// <param name="arguments">The compiled argument expressions.</param>
        /// <returns>The compiled call, or <see langword="null"/> if the host does not provide this function.</returns>
        Expr? TryCreateFunction(string name, Expr[] arguments) => null;

        /// <summary>
        /// Resolves the decimal format that <c>fn:format-number()</c>'s third argument names.
        /// </summary>
        /// <remarks>
        /// XPath 3.0 moved <c>format-number</c> into the core library and made the decimal formats part of
        /// the static context, so a host that declares none still has the one the specification names as the
        /// default. Returning <see langword="null"/> is what makes a name <c>FODF1280</c>.
        /// </remarks>
        /// <param name="name">The expanded name, empty in both parts for the unnamed format.</param>
        /// <returns>The format, or <see langword="null"/> if none is declared under that name.</returns>
        DecimalFormat? ResolveDecimalFormat(ExpandedName name)
        {
            return name.LocalName.Length == 0 ? DecimalFormat.Default : null;
        }
    }

    /// <summary>
    /// A straightforward <see cref="IXPathStaticContext"/> backed by dictionaries, for evaluating expressions
    /// outside a stylesheet.
    /// </summary>
    public sealed class XPathStaticContext : IXPathStaticContext, ISchemaTypeProvider
    {
        private readonly Dictionary<string, string> m_namespaces = new(StringComparer.Ordinal);
        private readonly Dictionary<ExpandedName, int> m_variables = new();
        private readonly Dictionary<ExpandedName, DecimalFormat> m_decimalFormats = new();
        private Compiler.SchemaComponents? m_schemas;

        /// <summary>
        /// Gets or sets the schemas whose types an expression may name: in <c>instance of</c>, a cast, a
        /// constructor function or a sequence type. Set before the first expression is parsed.
        /// </summary>
        public System.Xml.Schema.XmlSchemaSet? Schemas { get; set; }

        XdmSchemaType? ISchemaTypeProvider.ResolveSchemaType(string namespaceUri, string localName)
        {
            if (Schemas is null && namespaceUri != XdmType.SchemaNamespace)
            {
                return null;
            }

            m_schemas ??= new Compiler.SchemaComponents(Schemas, null);
            return m_schemas.FindType(namespaceUri, localName);
        }

        XdmSchemaDeclaration? ISchemaTypeProvider.ResolveElementDeclaration(string namespaceUri, string localName)
        {
            if (Schemas is null)
            {
                return null;
            }

            m_schemas ??= new Compiler.SchemaComponents(Schemas, null);
            return m_schemas.FindElement(namespaceUri, localName);
        }

        XdmSchemaDeclaration? ISchemaTypeProvider.ResolveAttributeDeclaration(string namespaceUri, string localName)
        {
            if (Schemas is null)
            {
                return null;
            }

            m_schemas ??= new Compiler.SchemaComponents(Schemas, null);
            return m_schemas.FindAttribute(namespaceUri, localName);
        }

        /// <summary>Initializes a context.</summary>
        /// <param name="names">The name-slot table to use, or <see langword="null"/> to create one.</param>
        public XPathStaticContext(NameSlotTable? names = null)
        {
            Names = names ?? new NameSlotTable();
        }

        /// <inheritdoc/>
        public NameSlotTable Names { get; }

        /// <summary>
        /// Gets or sets the version whose rules expressions are compiled against. Defaults to
        /// <see cref="XsltVersion.V10"/>.
        /// </summary>
        /// <remarks>
        /// Settable here, where it is fixed by the stylesheet in <c>StylesheetCompiler</c>, so that XPath 2.0
        /// semantics can be exercised on their own while the XSLT layer still reports itself as 1.0.
        /// </remarks>
        public XsltVersion Version { get; set; } = XsltVersion.V10;

        /// <summary>
        /// Gets or sets the collation strings are compared by where an expression names none. Defaults to
        /// the code point collation.
        /// </summary>
        public string DefaultCollation { get; set; } = "http://www.w3.org/2005/xpath-functions/collation/codepoint";

        /// <summary>Gets or sets the collations of the caller's own, or <see langword="null"/> for none.</summary>
        public IXsltCollationResolver? CollationResolver { get; set; }

        /// <summary>
        /// Gets or sets the namespace an unprefixed element or type name is in, empty for none. This is
        /// the default element/type namespace of the static context and not what <c>xmlns</c> declares:
        /// binding the empty prefix here would say where unprefixed names in a document live, which is a
        /// different question and one XPath does not read that binding for.
        /// </summary>
        public string DefaultElementNamespace { get; set; } = string.Empty;

        /// <summary>Binds a prefix to a namespace URI for the expressions compiled against this context.</summary>
        /// <param name="prefix">The prefix to bind.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        public void DeclarePrefix(string prefix, string namespaceUri)
        {
            m_namespaces[prefix] = namespaceUri;
        }

        /// <summary>
        /// Declares a decimal format that <c>fn:format-number()</c> may name, or the unnamed one.
        /// </summary>
        /// <param name="name">The expanded name, empty in both parts for the unnamed format.</param>
        /// <param name="format">The symbols it declares.</param>
        public void DeclareDecimalFormat(ExpandedName name, DecimalFormat format)
        {
            m_decimalFormats[name] = format;
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

        /// <summary>Declares a global variable and assigns it a slot.</summary>
        /// <param name="localName">The variable's local name.</param>
        /// <param name="slot">The slot in global storage that holds its value.</param>
        public void DeclareGlobalVariable(string localName, int slot)
        {
            m_variables[new ExpandedName(string.Empty, localName)] = slot;
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> InScopeNamespaces => m_namespaces;

        /// <inheritdoc/>
        public string? ResolvePrefix(string prefix)
        {
            if (prefix == "xml")
            {
                return Model.XdmTree.XmlNamespaceUri;
            }

            if (m_namespaces.TryGetValue(prefix, out string? uri))
            {
                return uri;
            }

            // XPath 2.0 declares xs and fn for you, so an expression may use them without saying so. A
            // stylesheet that binds either prefix to something else has already been answered above.
            return Version.IsBackwardsCompatible ? null : PredeclaredPrefix(prefix, Version);
        }

        /// <summary>
        /// Resolves the prefixes XPath 2.0 binds without being asked.
        /// </summary>
        /// <param name="prefix">The prefix to resolve.</param>
        /// <returns>The namespace URI, or <see langword="null"/> if the prefix is not one of them.</returns>
        public static string? PredeclaredPrefix(string prefix) => PredeclaredPrefix(prefix, XsltVersion.V20);

        /// <summary>
        /// Resolves the prefixes a version of XPath binds without being asked.
        /// </summary>
        /// <remarks>
        /// XPath 3.0 adds three to the two 2.0 has, and they are not offered below it: at 2.0 a stylesheet
        /// using <c>map</c> for a namespace of its own and forgetting to declare it should be told the prefix
        /// is unbound, not quietly given a library it cannot have meant.
        /// </remarks>
        /// <param name="prefix">The prefix to resolve.</param>
        /// <param name="version">The version in force.</param>
        /// <returns>The namespace URI, or <see langword="null"/> if the prefix is not one of them.</returns>
        public static string? PredeclaredPrefix(string prefix, XsltVersion version)
        {
            switch (prefix)
            {
                case "xs":
                    return XdmType.SchemaNamespace;

                case "fn":
                    return XdmType.FunctionNamespace;

                case "map" when version.CompareTo(XsltVersion.V30) >= 0:
                    return MapArrayFunctionExpr.MapNamespace;

                case "array" when version.CompareTo(XsltVersion.V30) >= 0:
                    return MapArrayFunctionExpr.ArrayNamespace;

                case "math" when version.CompareTo(XsltVersion.V30) >= 0:
                    return Xpath30FunctionExpr.MathNamespace;

                default:
                    return null;
            }
        }

        /// <inheritdoc/>
        public bool TryResolveVariable(string namespaceUri, string localName, out int slot, out bool isGlobal)
        {
            isGlobal = true;
            return m_variables.TryGetValue(new ExpandedName(namespaceUri, localName), out slot);
        }
    }
}
