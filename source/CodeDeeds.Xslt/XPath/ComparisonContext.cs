namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// What a comparison needs from the place it was written: the collation strings are compared by, and the
    /// namespaces a name found in an untyped operand is read against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are static — properties of the expression rather than of the values it compares — so they are
    /// settled when the expression is compiled and never looked up while it runs. XSLT's
    /// <c>default-collation</c> puts a collation in scope for everything written below it, and casting an
    /// untyped operand to <c>xs:QName</c> resolves whatever prefix it finds against the namespaces the
    /// comparison stands among, which is why <c>$u = $q</c> can answer differently in two branches of one
    /// <c>xsl:choose</c>.
    /// </para>
    /// <para>
    /// The collation is <see langword="null"/> wherever it is the code point one, which is every comparison
    /// written without a <c>default-collation</c> in scope. That keeps the ordinary path one null check away
    /// from the ordinal comparison it always was.
    /// </para>
    /// </remarks>
    internal sealed class ComparisonContext
    {
        /// <summary>Initializes a comparison's static context.</summary>
        /// <param name="collation">The collation, or <see langword="null"/> for the code point one.</param>
        /// <param name="namespaces">The namespaces in scope where the comparison is written.</param>
        public ComparisonContext(Collation? collation, IReadOnlyDictionary<string, string>? namespaces)
        {
            Collation = collation;
            Namespaces = namespaces;
        }

        /// <summary>Gets the collation, or <see langword="null"/> where strings compare by code point.</summary>
        public Collation? Collation { get; }

        /// <summary>Gets the namespaces in scope where the comparison is written.</summary>
        public IReadOnlyDictionary<string, string>? Namespaces { get; }

        /// <summary>Takes what a comparison compiled against a static context needs from it.</summary>
        /// <param name="context">The static context the expression is being compiled against.</param>
        public static ComparisonContext For(IXPathStaticContext context)
        {
            string uri = context.DefaultCollation;

            return new ComparisonContext(
                uri.Length == 0 || uri == XPath.Collation.CodepointUri
                    ? null
                    : XPath.Collation.Resolve(uri, context.CollationResolver),
                context.InScopeNamespaces);
        }
    }
}
