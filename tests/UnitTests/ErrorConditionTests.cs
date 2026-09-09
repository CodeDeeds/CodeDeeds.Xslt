namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for three conditions the specifications name and this engine had let pass: an attribute an
    /// element does not have, an invocation naming two ways in at once, and one transformation both reading
    /// and writing a document.
    /// </summary>
    [TestClass]
    public sealed class ErrorConditionTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Initial = "{http://www.w3.org/1999/XSL/Transform}initial-template";

        /// <summary>A store standing for a file system, readable and writable under the same URIs.</summary>
        private sealed class Documents : IXsltResolver, IXsltResultResolver
        {
            private readonly Dictionary<string, string> m_documents = new(StringComparer.Ordinal);

            public Documents Add(string uri, string text)
            {
                m_documents[uri] = text;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_documents.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }

            TextWriter IXsltResultResolver.Resolve(string href, string? baseUri) => new StringWriter();
        }

        /// <summary>
        /// Runs a stylesheet and answers with the error code it raised, or the empty string where it ran.
        /// </summary>
        /// <remarks>
        /// Answering rather than asserting, because half of these tests are about a condition one version
        /// raises and the next does not, and "no error" is as much a result as a code is.
        /// </remarks>
        private static string CodeFrom(
            string body,
            string declared = "3.0",
            XsltVersion version = default,
            Documents? documents = null,
            string initialTemplate = Initial,
            string? initialMode = null)
        {
            string stylesheet = "<xsl:stylesheet version=\"" + declared + "\" xmlns:xsl=\"" + Xsl + "\">"
                + body + "</xsl:stylesheet>";

            try
            {
                new Xslt(
                    stylesheet,
                    new XsltOptions
                    {
                        Version = version.CompareTo(XsltVersion.V10) < 0 ? XsltVersion.V30 : version,
                        OmitXmlDeclaration = true,
                        DocumentResolver = documents,
                        ResultResolver = documents,
                        BaseOutputUri = "file:///s/out.xml",
                        InitialTemplate = initialTemplate,
                        InitialMode = initialMode,
                    }).Transform();
            }
            catch (XsltException error)
            {
                return error.Code ?? string.Empty;
            }

            return string.Empty;
        }

        [TestMethod]
        public void AStylesheetParameterHasNoVisibilityAttribute()
        {
            // A package parameter is a component like any other, but what it is visible as is said by an
            // xsl:expose and not on the declaration, so xsl:param has no visibility attribute to write.
            Assert.AreEqual(
                "XTSE0090",
                CodeFrom(
                    "<xsl:param name=\"p\" visibility=\"private\"/>"
                    + "<xsl:template name=\"xsl:initial-template\"><out/></xsl:template>"));

            // It is the element that decides and not the attribute name: xsl:variable does have one, and
            // the same spelling on it is taken.
            Assert.AreEqual(
                string.Empty,
                CodeFrom(
                    "<xsl:variable name=\"v\" visibility=\"private\" select=\"1\"/>"
                    + "<xsl:template name=\"xsl:initial-template\"><out/></xsl:template>"));
        }

        [TestMethod]
        public void TwoWaysInAtOnceAreRefusedByTwoPointZero()
        {
            const string Body =
                "<xsl:template name=\"main\"><out/></xsl:template>"
                + "<xsl:template match=\"*\" mode=\"m\"><in/></xsl:template>";

            // XSLT 2.0 took one way in or the other and settled no order between them, so naming both is
            // an error rather than a choice.
            Assert.AreEqual(
                "XTDE0047",
                CodeFrom(Body, "2.0", XsltVersion.V20, initialTemplate: "main", initialMode: "m"));

            // 3.0 dropped the error: a mode is useful there for what the named template's own
            // xsl:apply-templates does with it, so the two together are a transformation, not a mistake.
            Assert.AreEqual(
                string.Empty,
                CodeFrom(Body, "3.0", XsltVersion.V30, initialTemplate: "main", initialMode: "m"));
        }

        [TestMethod]
        public void OneTransformationDoesNotBothReadAndWriteADocument()
        {
            // Whether the read saw what the write put there would depend on an order nothing settles, so
            // the pair is refused rather than answered.
            Documents documents = new Documents().Add("file:///s/data.xml", "<r/>");

            Assert.AreEqual(
                "XTDE1500",
                CodeFrom(
                    "<xsl:template name=\"xsl:initial-template\">"
                    + "<xsl:variable name=\"d\" select=\"doc('file:///s/data.xml')\"/>"
                    + "<out><xsl:copy-of select=\"$d\"/></out>"
                    + "<xsl:result-document href=\"file:///s/data.xml\"><boo/></xsl:result-document>"
                    + "</xsl:template>",
                    documents: documents));

            // And the other way about, which is the same pair seen from the other side.
            Assert.AreEqual(
                "XTDE1500",
                CodeFrom(
                    "<xsl:template name=\"xsl:initial-template\">"
                    + "<xsl:result-document href=\"file:///s/data.xml\"><boo/></xsl:result-document>"
                    + "<out><xsl:copy-of select=\"doc('file:///s/data.xml')\"/></out>"
                    + "</xsl:template>",
                    documents: documents));

            // A document only read, and one only written, are both left alone.
            Assert.AreEqual(
                string.Empty,
                CodeFrom(
                    "<xsl:template name=\"xsl:initial-template\">"
                    + "<out><xsl:copy-of select=\"doc('file:///s/data.xml')\"/></out>"
                    + "<xsl:result-document href=\"file:///s/other.xml\"><boo/></xsl:result-document>"
                    + "</xsl:template>",
                    documents: documents));
        }
    }
}
