namespace CodeDeeds.Xslt
{
    /// <summary>
    /// How a document is validated against the schemas in scope, in the terms XSLT's own
    /// <c>validation</c> attribute uses.
    /// </summary>
    /// <remarks>
    /// What <see cref="XsltOptions.InputValidation"/> is set to. The four values are the specification's,
    /// so that a caller reading <c>xsl:source-document validation="strict"</c> and this option are
    /// reading one vocabulary; for a document this engine parses the first two come to the same thing,
    /// since a document read from text arrives with no annotations to keep or remove.
    /// </remarks>
    public enum XsltValidation
    {
        /// <summary>
        /// Not validated, and read untyped: every element is <c>xs:untyped</c> and every attribute
        /// <c>xs:untypedAtomic</c>, which is what a basic processor sees. The default.
        /// </summary>
        Strip,

        /// <summary>Not validated, and left as it arrives.</summary>
        Preserve,

        /// <summary>
        /// Validated against the schemas in scope where they declare the document element, and read
        /// untyped where they do not. A document that is invalid is <c>XTTE1515</c>.
        /// </summary>
        Lax,

        /// <summary>
        /// Validated against a top-level declaration of the document element. A document element the
        /// schemas do not declare is <c>XTTE1512</c>, and a document that is invalid is <c>XTTE1510</c>.
        /// </summary>
        Strict,
    }
}
