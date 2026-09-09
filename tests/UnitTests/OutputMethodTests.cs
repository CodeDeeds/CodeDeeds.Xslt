namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the two output methods that write values rather than a tree — <c>json</c> and
    /// <c>adaptive</c>, asked for by <c>xsl:output</c> or <c>xsl:result-document</c> — and for
    /// <c>parameter-document</c>, which keeps serialization parameters outside the stylesheet.
    /// </summary>
    [TestClass]
    public sealed class OutputMethodTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Serves documents by name, for parameter documents and included modules.</summary>
        private sealed class Files : IXsltResolver
        {
            private readonly Dictionary<string, string> m_texts = new(StringComparer.Ordinal);

            public Files Add(string name, string text)
            {
                m_texts[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string name, string? baseUri)
            {
                return m_texts.TryGetValue(name, out string? text)
                    ? new ResolvedResource(new StringReader(text), name)
                    : null;
            }
        }

        private static string Sheet(string output, string body)
        {
            return $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + output + "<xsl:template name=\"xsl:initial-template\">" + body + "</xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, IXsltResolver? files = null, bool omitDeclaration = true)
        {
            return new Xslt(
                stylesheet,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = omitDeclaration,
                    StylesheetResolver = files,
                    ResultResolver = null,
                }).Transform();
        }

        private static string Refuses(string stylesheet)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(stylesheet)).Code ?? string.Empty;
        }

        [TestMethod]
        public void TheJsonMethodWritesMapsArraysAndAtomicValues()
        {
            string maps =
                "<xsl:variable name=\"maps\" as=\"map(*)*\"><xsl:for-each select=\"1 to 3\"><xsl:map>"
                + "<xsl:map-entry key=\"'index'\" select=\".\"/><xsl:map-entry key=\"'square'\" select=\". * .\"/>"
                + "</xsl:map></xsl:for-each></xsl:variable>";

            Assert.AreEqual(
                "[{\"index\":1,\"square\":1},{\"index\":2,\"square\":4},{\"index\":3,\"square\":9}]",
                Run(Sheet("<xsl:output method=\"json\"/>", maps + "<xsl:sequence select=\"array{$maps}\"/>")));

            // Booleans, numbers, strings and an empty sequence are the JSON tokens; a date is its string.
            Assert.AreEqual(
                "{\"t\":true,\"n\":1.5,\"s\":\"a\\\"b\",\"e\":null,\"d\":\"2020-01-01\"}",
                Run(Sheet(
                    "<xsl:output method=\"json\"/>",
                    "<xsl:map><xsl:map-entry key=\"'t'\" select=\"true()\"/><xsl:map-entry key=\"'n'\" select=\"1.5\"/>"
                    + "<xsl:map-entry key=\"'s'\" select=\"'a&quot;b'\"/><xsl:map-entry key=\"'e'\" select=\"()\"/>"
                    + "<xsl:map-entry key=\"'d'\" select=\"xs:date('2020-01-01')\"/></xsl:map>")));

            // A sequence of more than one item is not one JSON value, and a function has no JSON form.
            Assert.AreEqual("SERE0023", Refuses(Sheet("<xsl:output method=\"json\"/>", "<xsl:sequence select=\"1, 2\"/>")));
            Assert.AreEqual("SERE0021", Refuses(Sheet("<xsl:output method=\"json\"/>", "<xsl:sequence select=\"[name#1]\"/>")));
        }

        [TestMethod]
        public void DuplicateNamesAreRefusedUnlessAllowed()
        {
            // A time and a string that spell the same are two keys of the map and one name in JSON.
            string map =
                "<xsl:map><xsl:map-entry key=\"xs:time('23:00:00Z')\" select=\"'alpha'\"/>"
                + "<xsl:map-entry key=\"'23:00:00Z'\" select=\"'beta'\"/></xsl:map>";

            Assert.AreEqual("SERE0022", Refuses(Sheet("<xsl:output method=\"json\"/>", map)));
            Assert.AreEqual(
                "{\"23:00:00Z\":\"alpha\",\"23:00:00Z\":\"beta\"}",
                Run(Sheet("<xsl:output method=\"json\" allow-duplicate-names=\"yes\"/>", map)));
        }

        [TestMethod]
        public void ANodeInsideJsonIsWrittenWithTheNodeOutputMethod()
        {
            string body = "<xsl:variable name=\"e\" as=\"element()\"><p a=\"1\">x</p></xsl:variable><xsl:sequence select=\"[$e]\"/>";

            // xml by default, with no declaration; html where asked; and a solidus escaped as JSON has it.
            Assert.AreEqual("[\"<p a=\\\"1\\\">x<\\/p>\"]", Run(Sheet("<xsl:output method=\"json\"/>", body)));
            Assert.AreEqual(
                "[\"<p a=\\\"1\\\">x<\\/p>\"]",
                Run(Sheet("<xsl:output method=\"json\" json-node-output-method=\"html\"/>", body)));
            Assert.AreEqual(
                "[\"<br>\"]",
                Run(Sheet(
                    "<xsl:output method=\"json\" json-node-output-method=\"html\"/>",
                    "<xsl:variable name=\"e\" as=\"element()\"><br/></xsl:variable><xsl:sequence select=\"[$e]\"/>")));

            // A character map keeps the solidus from being escaped, the one thing it is for here.
            Assert.AreEqual(
                "{\"a\":\"http://x/y\"}",
                Run(Sheet(
                    "<xsl:output method=\"json\" use-character-maps=\"slash\"/>"
                    + "<xsl:character-map name=\"slash\"><xsl:output-character character=\"/\" string=\"/\"/></xsl:character-map>",
                    "<xsl:map><xsl:map-entry key=\"'a'\" select=\"'http://x/y'\"/></xsl:map>")));
        }

        [TestMethod]
        public void AMapReachesAStringTheAdaptiveMethodQuotes()
        {
            // Serialization §11: a character map applies to any value written as a quoted string, a map's
            // key included, and what it substitutes is written as it stands.
            Assert.AreEqual(
                "\"it&apos;s\"\nmap{\"k&apos;\":\"v\"}",
                Run(Sheet(
                    "<xsl:output method=\"adaptive\" use-character-maps=\"apos\"/>"
                    + "<xsl:character-map name=\"apos\"><xsl:output-character character=\"'\" string=\"&amp;apos;\"/></xsl:character-map>",
                    "<xsl:sequence select=\"'it''s', map { 'k''' : 'v' }\"/>")));
        }

        [TestMethod]
        public void TheAdaptiveMethodWritesAnythingAtAll()
        {
            string body =
                "<xsl:map-entry key=\"'a'\" select=\"1\"/><xsl:sequence select=\"name#1, (1 to 3), 'it''s', true()\"/>"
                + "<xsl:attribute name=\"att\" select=\"'v'\"/><xsl:variable name=\"e\" as=\"element()\"><e/></xsl:variable>"
                + "<xsl:sequence select=\"$e\"/>";

            Assert.AreEqual(
                "map{\"a\":1}\nfn:name#1\n1\n2\n3\n\"it's\"\ntrue()\natt=\"v\"\n<e/>",
                Run(Sheet("<xsl:output method=\"adaptive\"/>", body)));

            Assert.AreEqual(
                "1~2~3",
                Run(Sheet("<xsl:output method=\"adaptive\" item-separator=\"~\"/>", "<xsl:sequence select=\"1 to 3\"/>")));
        }

        [TestMethod]
        public void AResultDocumentMayUseEitherMethod()
        {
            StringWriter secondary = new StringWriter();

            string stylesheet = Sheet(
                string.Empty,
                "<xsl:result-document href=\"out.json\" method=\"json\"><xsl:map><xsl:map-entry key=\"'k'\" select=\"'v'\"/></xsl:map></xsl:result-document>"
                + "<out/>");

            string principal = new Xslt(
                stylesheet,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    ResultResolver = new Results(secondary),
                }).Transform();

            Assert.AreEqual("<out/>", principal);
            Assert.AreEqual("{\"k\":\"v\"}", secondary.ToString());
        }

        private sealed class Results : IXsltResultResolver
        {
            private readonly TextWriter m_writer;

            public Results(TextWriter writer)
            {
                m_writer = writer;
            }

            public TextWriter Resolve(string href, string? baseUri) => m_writer;
        }

        [TestMethod]
        public void AParameterDocumentTakesPrecedenceOverTheAttributes()
        {
            const string Ns = "http://www.w3.org/2010/xslt-xquery-serialization";

            Files files = new Files()
                .Add(
                    "params.xml",
                    $"<output:serialization-parameters xmlns:output=\"{Ns}\"><output:method value=\"json\"/>"
                    + "<output:use-character-maps><output:character-map character=\"a\" map-string=\"AAA\"/></output:use-character-maps>"
                    + "</output:serialization-parameters>")
                .Add(
                    "declaration.xml",
                    $"<output:serialization-parameters xmlns:output=\"{Ns}\"><output:omit-xml-declaration value=\"no\"/>"
                    + "</output:serialization-parameters>");

            // The document says json and maps 'a'; the attribute said xml, and loses.
            Assert.AreEqual(
                "{\"AAA\":\"bAAAr\"}",
                Run(
                    Sheet(
                        "<xsl:output method=\"xml\" parameter-document=\"params.xml\"/>",
                        "<xsl:map><xsl:map-entry key=\"'a'\" select=\"'bar'\"/></xsl:map>"),
                    files));

            // A named output definition reads its parameter document as well.
            StringWriter secondary = new StringWriter();
            new Xslt(
                Sheet(
                    "<xsl:output name=\"declared\" parameter-document=\"declaration.xml\"/>",
                    "<xsl:result-document href=\"x\" format=\"declared\"><r/></xsl:result-document><out/>"),
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    StylesheetResolver = files,
                    ResultResolver = new Results(secondary),
                }).Transform();

            Assert.AreEqual("<?xml version=\"1.0\" encoding=\"UTF-8\"?><r/>", secondary.ToString());

            // One that cannot be found is ignored, as the specification says; one that is not a parameter
            // document is not.
            Assert.AreEqual(
                "<out/>",
                Run(Sheet("<xsl:output parameter-document=\"missing.xml\"/>", "<out/>"), files));
            Assert.AreEqual(
                "SEPM0017",
                Assert.ThrowsExactly<XsltException>(() => Run(
                    Sheet("<xsl:output parameter-document=\"params.xml\"/>", "<out/>"),
                    new Files().Add("params.xml", "<not-parameters/>"))).Code);
        }

        [TestMethod]
        public void AnEmptyElementIsWrittenWithoutASpaceExceptInXhtml()
        {
            Assert.AreEqual("<out><a b=\"1\"/></out>", Run(Sheet(string.Empty, "<out><a b=\"1\"/></out>")));
            Assert.AreEqual(
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><br /></body></html>",
                Run(Sheet(
                    "<xsl:output method=\"xhtml\" include-content-type=\"no\"/>",
                    "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><br/></body></html>")));
        }
    }
}
