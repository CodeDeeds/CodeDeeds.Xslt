namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the base URI a built document carries, and where it comes from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>fn:json-to-xml()</c> hands back a document that came from nowhere: no file was read and no
    /// parser saw a location, so the only base URI it can have is the one the call was written at. The
    /// specification says so, and it is what lets a relative reference inside the JSON be resolved the
    /// way one written in the stylesheet beside it would be.
    /// </para>
    /// <para>
    /// That is the <em>static</em> base URI, which is a property of where an expression stands rather
    /// than of the transformation running it: a module read from elsewhere, or an <c>xml:base</c>, moves
    /// the first and leaves the second alone.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class StaticBaseUriTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private static string Writes(string expression, string? baseUri = null, string attributes = "")
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl} {attributes}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                BaseUri = baseUri,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        // ---- What json-to-xml stamps -----------------------------------------------------------------------

        [TestMethod]
        public void TheDocumentJsonToXmlBuildsCarriesTheBaseUriOfTheCall()
        {
            const string Where = "http://example.com/stylesheets/main.xsl";

            Assert.AreEqual(Where, Writes("base-uri(json-to-xml('true'))", Where));
            Assert.AreEqual("true", Writes("static-base-uri() eq base-uri(json-to-xml('true'))", Where));

            // Every node of it, a base URI being inherited from the document it is in.
            Assert.AreEqual(Where, Writes("base-uri(json-to-xml('true')/*)", Where));
        }

        [TestMethod]
        public void WithNoBaseUriThereIsNoneToCarry()
        {
            // A transformation that was never told where it stands gives an empty sequence rather than
            // an invented URI, and json-to-xml's result says the same.
            Assert.AreEqual("0", Writes("count(static-base-uri())"));
            Assert.AreEqual("0", Writes("count(base-uri(json-to-xml('true')))"));
        }

        [TestMethod]
        public void AnXmlBaseMovesTheStaticBaseUriAndTheResultWithIt()
        {
            // xml:base is a property of where the expression is written, so both answers move together
            // while the transformation's own base URI stays where it was.
            const string Where = "http://example.com/stylesheets/main.xsl";

            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + "<xsl:template match=\"/\" xml:base=\"http://example.com/elsewhere/\">"
                + "<out><xsl:value-of select=\"base-uri(json-to-xml('true'))\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            string written = new Xslt(
                stylesheet,
                new XsltOptions { OmitXmlDeclaration = true, BaseUri = Where }).TransformXml("<r/>");

            Assert.AreEqual("<out>http://example.com/elsewhere/</out>", written);
        }

        // ---- The type it comes back as ---------------------------------------------------------------------

        [TestMethod]
        public void BothAnswerWithAnAnyUri()
        {
            const string Where = "http://example.com/stylesheets/main.xsl";

            Assert.AreEqual("true", Writes("static-base-uri() instance of xs:anyURI", Where));
            Assert.AreEqual("true", Writes("base-uri(json-to-xml('true')) instance of xs:anyURI", Where));
        }
    }
}
