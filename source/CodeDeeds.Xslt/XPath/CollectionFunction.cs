using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A call to <c>fn:collection()</c> or <c>fn:uri-collection()</c>: a set of resources a caller has
    /// configured under a name, reached through <see cref="XsltOptions.CollectionResolver"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two functions ask the same question and differ in what they do with the answer. The collection
    /// resolver turns a collection URI, or the absence of one, into the URIs of what the collection holds;
    /// <c>uri-collection()</c> returns those, and <c>collection()</c> reads each through the document
    /// resolver, the way <c>doc()</c> reads one. One class, so that the two cannot resolve a name
    /// differently.
    /// </para>
    /// <para>
    /// A relative collection URI resolves against the base URI where the call is written, which the
    /// stylesheet compiler hands over as it does for <c>doc()</c>, and the documents are stripped of the
    /// whitespace the calling package strips. Without a transformation running there is no resolver to
    /// ask, so there is no collection, and the answer is the one a transformation without a resolver gives.
    /// </para>
    /// </remarks>
    internal sealed class CollectionFunctionExpr : Expr
    {
        private readonly Expr? m_uri;
        private readonly bool m_urisOnly;

        private CollectionFunctionExpr(Expr? uri, bool urisOnly)
        {
            m_uri = uri;
            m_urisOnly = urisOnly;
        }

        /// <summary>
        /// The base URI where the call was written, which a relative collection URI resolves against.
        /// </summary>
        /// <remarks>
        /// Set by the stylesheet compiler, as it is on <c>doc()</c>, and for the same reason: the library
        /// builds a call from values alone and has no way to know where it was written. Without it a
        /// relative reference resolves against wherever the transformation started.
        /// </remarks>
        internal string? StaticBaseUri { get; set; }

        /// <summary>
        /// The package the call is written in, whose whitespace declarations strip what it reads.
        /// </summary>
        internal int Package { get; set; }

        /// <summary>The name the call was written with, for what it reports.</summary>
        private string Name => m_urisOnly ? "uri-collection" : "collection";

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_uri is null ? Array.Empty<Expr>() : new[] { m_uri };

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => !m_urisOnly;

        /// <summary>
        /// <c>collection()</c> introduces other documents, as <c>document()</c> does, and so is the root of a
        /// node-set that spans them. <c>uri-collection()</c> returns no node at all.
        /// </summary>
        public override bool MaySpanDocuments => !m_urisOnly;

        /// <summary>Creates the call.</summary>
        /// <param name="arguments">The compiled arguments: nothing, or the collection URI.</param>
        /// <param name="version">The version in force, which settles how the argument is converted.</param>
        /// <param name="urisOnly">
        /// <see langword="true"/> for <c>fn:uri-collection()</c>, which answers with the URIs alone.
        /// </param>
        /// <exception cref="XsltException"><c>XPST0017</c> where there is more than one argument.</exception>
        public static Expr Create(Expr[] arguments, XsltVersion version, bool urisOnly)
        {
            string name = urisOnly ? "uri-collection" : "collection";

            if (arguments.Length > 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'{name}()' takes at most one argument, and was given {arguments.Length}.");
            }

            Expr[] uri = CheckedArgumentExpr.Wrap(
                arguments, FunctionParameter.Parse("xs:string?"), name, version);

            return new CollectionFunctionExpr(uri.Length == 0 ? null : uri[0], urisOnly);
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            // No argument and an empty one are the same question: both ask for the default collection, and
            // the specification says so in as many words, so collection(()) is not a collection named by
            // the empty URI. The argument is evaluated before any complaint about the resolver: an error
            // in it is the caller's, and it happened first.
            XPathValue named = m_uri is null
                ? XPathValue.FromSequence(XdmSequence.Empty)
                : m_uri.Evaluate(ref context);

            string? uri = Xpath2FunctionExpr.IsEmptySequence(named) ? null : XdmSequence.StringValueOf(named);

            if (context.Runtime is not XsltRuntime runtime)
            {
                // A static expression, or an expression evaluated on its own: nothing configured a
                // collection resolver here, so there is no collection, default or named.
                throw XsltRuntime.CollectionNotFound(Name, uri, "no transformation is running to hold one");
            }

            string? baseUri = StaticBaseUri ?? runtime.BaseUri;

            if (m_urisOnly)
            {
                IReadOnlyList<string> uris = runtime.UrisOfCollection(uri, baseUri, Name);
                XPathValue[] items = new XPathValue[uris.Count];

                for (int index = 0; index < items.Length; index++)
                {
                    items[index] = XPathValue.FromAnyUri(uris[index]);
                }

                return XdmSequence.Concatenate(items);
            }

            return XPathValue.FromNodeSet(
                runtime.LoadCollection(uri, baseUri, Package) ?? new NodeSet(context.Tree, 0));
        }
    }
}
