namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what <c>fn:doc()</c> is asked and what it resolves against: no URI naming no document, and
    /// a relative reference resolving against the base URI where the call is written.
    /// </summary>
    [TestClass]
    public sealed class DocumentUriTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Serves documents by their resolved URI, so what a call resolved to is what it asked for.</summary>
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_documents = new(StringComparer.Ordinal);

            public MapResolver Add(string uri, string text)
            {
                m_documents[uri] = text;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                string resolved = baseUri is not null && Uri.TryCreate(new Uri(baseUri), href, out Uri? absolute)
                    ? absolute.AbsoluteUri
                    : href;

                return m_documents.TryGetValue(resolved, out string? text)
                    ? new ResolvedResource(new StringReader(text), resolved)
                    : null;
            }
        }

        private static string Run(string body, IXsltResolver? resolver, string? baseAttribute = null)
        {
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"{Xsl}\""
                + (baseAttribute is null ? string.Empty : $" xml:base=\"{baseAttribute}\"")
                + "><xsl:template name=\"xsl:initial-template\"><out>" + body
                + "</out></xsl:template></xsl:stylesheet>";

            string written = new Xslt(
                stylesheet,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    BaseUri = "file:///s/sheet.xsl",
                    DocumentResolver = resolver,
                }).Transform();

            return written == "<out/>" ? string.Empty : written["<out>".Length..^"</out>".Length];
        }

        [TestMethod]
        public void NoUriNamesNoDocument()
        {
            // The argument is declared xs:string?, so the empty sequence is a value it may be given, and
            // the answer is the empty sequence. Reading its string value instead made doc(()) the same call
            // as doc(''), which is the module the expression was written in.
            MapResolver resolver = new MapResolver().Add("file:///s/one.xml", "<r/>");

            Assert.AreEqual("true,true", Run(
                "<xsl:value-of separator=\",\" select=\""
                + "doc(()) instance of document-node()?, doc(()) instance of empty-sequence()\"/>",
                resolver));

            Assert.AreEqual(string.Empty, Run("<xsl:copy-of select=\"doc(())\"/>", resolver));
            Assert.AreEqual("false", Run("<xsl:value-of select=\"doc-available(())\"/>", resolver));

            // A URI that names something still names it.
            Assert.AreEqual("<r/>", Run("<xsl:copy-of select=\"doc('one.xml')\"/>", resolver));
        }

        [TestMethod]
        public void ARelativeReferenceResolvesAgainstWhereItIsWritten()
        {
            // Which xml:base may have moved. Without it the reference resolved against wherever the
            // transformation started — the principal stylesheet — rather than against the place the call
            // stands in, which is the base document() has always read one against.
            MapResolver resolver = new MapResolver()
                .Add("file:///s/data.xml", "<outer/>")
                .Add("file:///s/inner/data.xml", "<inner/>");

            Assert.AreEqual("<outer/>", Run("<xsl:copy-of select=\"doc('data.xml')\"/>", resolver));
            Assert.AreEqual(
                "<inner/>",
                Run("<xsl:copy-of select=\"doc('data.xml')\"/>", resolver, baseAttribute: "inner/"));

            // doc-available() asks the same question and resolves it the same way.
            Assert.AreEqual(
                "true",
                Run("<xsl:value-of select=\"doc-available('data.xml')\"/>", resolver, baseAttribute: "inner/"));

            Assert.AreEqual(
                "false",
                Run("<xsl:value-of select=\"doc-available('absent.xml')\"/>", resolver, baseAttribute: "inner/"));
        }

        [TestMethod]
        public void ASourceDocumentFollowsAFragmentToTheElementItNames()
        {
            // A bare name names the element with that ID (XPointer §3.2), which an xml:id gives without any
            // schema or document type declaration — and that element, not the document node, is what the
            // body processes. document() has followed a fragment for a while; the instruction that reads a
            // document for its content had been refusing one.
            MapResolver resolver = new MapResolver()
                .Add("file:///s/news.xml", "<r><s xml:id=\"one\"><t>first</t></s><s xml:id=\"two\"/></r>");

            Assert.AreEqual(
                "<s xml:id=\"one\"><t>first</t></s>",
                Run(
                    "<xsl:source-document href=\"news.xml#one\"><xsl:copy-of select=\".\"/>"
                    + "</xsl:source-document>",
                    resolver));

            // Without one it is the document node, which is the ordinary case and the one the instruction
            // is named for.
            Assert.AreEqual(
                "<r><s xml:id=\"one\"><t>first</t></s><s xml:id=\"two\"/></r>",
                Run(
                    "<xsl:source-document href=\"news.xml\"><xsl:copy-of select=\".\"/>"
                    + "</xsl:source-document>",
                    resolver));

            // And a name no element carries leaves the instruction nothing at all to process.
            Assert.AreEqual(
                "XTRE1160",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(
                        "<xsl:source-document href=\"news.xml#three\"><xsl:copy-of select=\".\"/>"
                        + "</xsl:source-document>",
                        resolver)).Code);
        }
        [TestMethod]
        public void DocumentUriAnswersForADocumentNodeAndNothingElse()
        {
            // A property of the document itself and not of what it holds. An element is in a document
            // rather than being one, so the answer for one is the empty sequence and not the URI of
            // whatever encloses it.
            MapResolver resolver = new MapResolver()
                .Add("file:///s/one.xml", "<r a=\"1\"><!--c--><?p d?>text</r>");

            Assert.AreEqual(
                "file:///s/one.xml",
                Run("<xsl:value-of select=\"document-uri(doc('one.xml'))\"/>", resolver));

            Assert.AreEqual(
                "true,true,true,true,true",
                Run(
                    "<xsl:variable name=\"d\" select=\"doc('one.xml')\"/>"
                    + "<xsl:value-of separator=\",\" select=\""
                    + "document-uri($d/r) instance of empty-sequence(),"
                    + "document-uri($d/r/@a) instance of empty-sequence(),"
                    + "document-uri($d/r/comment()) instance of empty-sequence(),"
                    + "document-uri($d/r/processing-instruction()) instance of empty-sequence(),"
                    + "document-uri($d/r/text()) instance of empty-sequence()\"/>",
                    resolver));
        }

        [TestMethod]
        public void TheContextFormOfDocumentUriIsThreePointZeros()
        {
            // fn:document-uri() with no argument was added in XPath 3.0, where fn:base-uri() has had one
            // since 2.0. The vocabulary follows the processor's version rather than the stylesheet's
            // claim, so a 2.0 processor does not have the form at all — which is a static error about a
            // function that is not there, not an empty answer.
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template match=\"/\"><out><xsl:value-of select=\"document-uri()\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltVersion version) => new XsltOptions
            {
                Version = version,
                OmitXmlDeclaration = true,
                InputUri = "file:///s/in.xml",
            };

            Assert.AreEqual(
                "<out>file:///s/in.xml</out>",
                new Xslt(stylesheet, For(XsltVersion.V30)).TransformXml("<r/>"));

            Assert.AreEqual(
                "XPST0017",
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(stylesheet, For(XsltVersion.V20)).TransformXml("<r/>")).Code);
        }

        [TestMethod]
        public void TheSourceDocumentIsInThePoolUnderItsOwnUri()
        {
            // One URI names one document for a whole transformation, so asking doc() for where the source
            // came from answers with the source itself rather than reading the file again and handing back
            // a second set of nodes over the same content. The resolver here would answer with something
            // else, which is how the test can tell which of the two came back.
            MapResolver resolver = new MapResolver().Add("file:///s/in.xml", "<other/>");

            string stylesheet = $"<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of separator=\",\" select=\""
                + "doc(document-uri(.)) is ., name(doc(document-uri(.))/*)\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>true,r</out>",
                new Xslt(
                    stylesheet,
                    new XsltOptions
                    {
                        OmitXmlDeclaration = true,
                        InputUri = "file:///s/in.xml",
                        DocumentResolver = resolver,
                    }).TransformXml("<r/>"));
        }
    }
}
