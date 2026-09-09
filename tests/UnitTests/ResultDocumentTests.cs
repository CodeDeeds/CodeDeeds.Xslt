namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for where a result document goes and what it is told about it: the base output URI an
    /// <c>href</c> resolves against, <c>current-output-uri()</c>, the serialization attributes computed on
    /// the instruction, and a result document written from inside <c>xsl:analyze-string</c>.
    /// </summary>
    [TestClass]
    public sealed class ResultDocumentTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private sealed class Results : IXsltResultResolver
        {
            public Dictionary<string, StringWriter> Written { get; } = new(StringComparer.Ordinal);

            public string? LastBase { get; private set; }

            public TextWriter Resolve(string href, string? baseUri)
            {
                LastBase = baseUri;
                StringWriter writer = new StringWriter();
                Written[href] = writer;
                return writer;
            }
        }

        private static string Sheet(string declarations, string body)
        {
            return $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\" xmlns:f=\"urn:f\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"f xs\">" + declarations
                + "<xsl:template name=\"xsl:initial-template\">" + body + "</xsl:template></xsl:stylesheet>";
        }

        private static (string Principal, Results Results) Run(string stylesheet, string? baseOutputUri = "file:///tmp/out/main.xml")
        {
            Results results = new Results();

            string principal = new Xslt(
                stylesheet,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    ResultResolver = results,
                    BaseOutputUri = baseOutputUri,
                }).Transform();

            return (principal, results);
        }

        [TestMethod]
        public void AnHrefResolvesAgainstTheBaseOutputUri()
        {
            (string principal, Results results) = Run(Sheet(
                string.Empty,
                "<xsl:result-document href=\"sub/one.xml\"><one/></xsl:result-document><out/>"));

            Assert.AreEqual("<out/>", principal);
            Assert.AreEqual("<one/>", results.Written["file:///tmp/out/sub/one.xml"].ToString());
            Assert.AreEqual("file:///tmp/out/main.xml", results.LastBase);

            // With no base output URI the href reaches the resolver as written.
            (_, results) = Run(Sheet(string.Empty, "<xsl:result-document href=\"sub/one.xml\"><one/></xsl:result-document>"), baseOutputUri: null);
            Assert.IsTrue(results.Written.ContainsKey("sub/one.xml"));

            // An href that resolves to the base output URI is the principal result, which is already being
            // written.
            Assert.AreEqual(
                "XTDE1490",
                Assert.ThrowsExactly<XsltException>(() => Run(Sheet(
                    string.Empty,
                    "<out><xsl:result-document href=\"main.xml\"><two/></xsl:result-document></out>"))).Code);
        }

        [TestMethod]
        public void CurrentOutputUriIsWhereTheOutputIsGoing()
        {
            string body =
                "<out base=\"{current-output-uri()}\">"
                + "<xsl:variable name=\"inVariable\" as=\"xs:string*\"><xsl:sequence select=\"current-output-uri()\"/></xsl:variable>"
                + "<xsl:variable name=\"item\" select=\"current-output-uri#0\"/>"
                + "<v empty=\"{empty($inVariable)}\" dynamic=\"{empty($item())}\" inline=\"{empty(function() { current-output-uri() }())}\" function=\"{empty(f:get())}\"/>"
                + "<xsl:result-document href=\"sub/two.xml\"><two uri=\"{current-output-uri()}\"/></xsl:result-document>"
                + "<after uri=\"{current-output-uri()}\"/></out>";
            string function = "<xsl:function name=\"f:get\"><xsl:sequence select=\"current-output-uri()\"/></xsl:function>";

            (string principal, Results results) = Run(Sheet(function, body));

            Assert.AreEqual(
                "<out base=\"file:///tmp/out/main.xml\"><v empty=\"true\" dynamic=\"true\" inline=\"true\" function=\"true\"/>"
                + "<after uri=\"file:///tmp/out/main.xml\"/></out>",
                principal);
            Assert.AreEqual(
                "<two uri=\"file:///tmp/out/sub/two.xml\"/>",
                results.Written["file:///tmp/out/sub/two.xml"].ToString());

            // A select expression is not temporary output, where a sequence constructor is (§2.3.2).
            (principal, _) = Run(Sheet(string.Empty, "<xsl:variable name=\"s\" select=\"current-output-uri()\"/><out s=\"{$s}\"/>"));
            Assert.AreEqual("<out s=\"file:///tmp/out/main.xml\"/>", principal);

            // No base output URI, nothing to say; and a pattern is not output at all, wherever it stands.
            (principal, _) = Run(
                Sheet(
                    "<xsl:template match=\"x[current-output-uri()]\"><wrong/></xsl:template>"
                    + "<xsl:template match=\"x[empty(current-output-uri())]\"><right/></xsl:template>",
                    "<out empty=\"{empty(current-output-uri())}\"><xsl:variable name=\"d\"><x/></xsl:variable><xsl:apply-templates select=\"$d/x\"/>"
                    + "<xsl:for-each-group select=\"-1 to 1\" group-starting-with=\".[. = count(current-output-uri())]\"><g at=\"{.}\"/></xsl:for-each-group></out>"),
                baseOutputUri: null);
            Assert.AreEqual("<out empty=\"true\"><right/><g at=\"-1\"/><g at=\"0\"/></out>", principal);
        }

        [TestMethod]
        public void ComputedSerializationAttributesReachTheJsonMethod()
        {
            string map = "<xsl:map><xsl:map-entry key=\"xs:time('23:00:00Z')\" select=\"1\"/><xsl:map-entry key=\"'23:00:00Z'\" select=\"2\"/></xsl:map>";

            // The method, the duplicate-names rule and the node method may all be computed.
            (string principal, _) = Run(Sheet(
                "<xsl:param name=\"m\" select=\"'json'\"/><xsl:param name=\"d\" select=\"'yes'\"/>",
                "<xsl:result-document method=\"{$m}\" allow-duplicate-names=\"{$d}\">" + map + "</xsl:result-document>"));
            Assert.AreEqual("{\"23:00:00Z\":1,\"23:00:00Z\":2}", principal);

            Assert.AreEqual(
                "SERE0022",
                Assert.ThrowsExactly<XsltException>(() => Run(Sheet(
                    string.Empty,
                    "<xsl:result-document method=\"js{'on'}\" allow-duplicate-names=\"n{'o'}\">" + map + "</xsl:result-document>"))).Code);
        }

        [TestMethod]
        public void ItemSeparatorAndCharacterMapsFollowTheInstruction()
        {
            // "#absent" on the instruction takes back the separator the format declared; and the
            // instruction's character maps add to the format's, winning where the two map one character.
            (string principal, _) = Run(Sheet(
                "<xsl:output name=\"f\" item-separator=\"|\" omit-xml-declaration=\"yes\"/>",
                "<xsl:result-document format=\"f\" item-separator=\"#absent\" build-tree=\"no\"><xsl:comment>b</xsl:comment>"
                + "<xsl:sequence select=\"1, 2\"/><xsl:comment>e</xsl:comment></xsl:result-document>"));
            Assert.AreEqual("<!--b-->1 2<!--e-->", principal);

            (principal, _) = Run(Sheet(
                "<xsl:character-map name=\"one\"><xsl:output-character character=\"a\" string=\"A\"/><xsl:output-character character=\"c\" string=\"1\"/></xsl:character-map>"
                + "<xsl:character-map name=\"two\"><xsl:output-character character=\"b\" string=\"B\"/><xsl:output-character character=\"c\" string=\"2\"/></xsl:character-map>"
                + "<xsl:output name=\"f\" use-character-maps=\"one\" omit-xml-declaration=\"yes\"/>",
                "<xsl:result-document format=\"f\" use-character-maps=\"two\"><out>abc</out></xsl:result-document>"));
            Assert.AreEqual("<out>AB2</out>", principal);

            // A comment and items written with a separator are content: no declaration follows them.
            (principal, _) = Run(Sheet(
                string.Empty,
                "<xsl:result-document method=\"xml\" item-separator=\"~\"><xsl:comment>s</xsl:comment><xsl:sequence select=\"1 to 3\"/></xsl:result-document>"));
            Assert.AreEqual("<!--s-->~1~2~3", principal);
        }

        [TestMethod]
        public void ATemplateDeclaringItsResultTypeStillWritesResultDocuments()
        {
            // Declaring a result type does not put the transformation into temporary output state: the type
            // is a check on what the body produced, not a variable to produce it into. So an
            // xsl:result-document inside such a template writes a document of the transformation's own,
            // exactly as it would without the type — where the capture the check is made through had made
            // the body look temporary and refused it.
            (string principal, Results results) = Run(
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template name=\"xsl:initial-template\" as=\"element(main)\">"
                + "<xsl:result-document href=\"aside.xml\"><aside/></xsl:result-document>"
                + "<main/></xsl:template></xsl:stylesheet>");

            Assert.AreEqual("<main/>", principal);
            Assert.AreEqual("<aside/>", results.Written["file:///tmp/out/aside.xml"].ToString());
        }

        [TestMethod]
        public void AnalyzeStringCountsItsSubstrings()
        {
            // position() and last() in a branch are the substring's place among all substrings, matching
            // and not, so a result document named after position() is written once per match.
            (string principal, Results results) = Run(Sheet(
                string.Empty,
                "<out><xsl:analyze-string select=\"'a1b22c'\" regex=\"[0-9]+\">"
                + "<xsl:matching-substring><xsl:result-document href=\"m{position()}.xml\"><m of=\"{last()}\"><xsl:value-of select=\".\"/></m></xsl:result-document></xsl:matching-substring>"
                + "<xsl:non-matching-substring><n p=\"{position()}\"/></xsl:non-matching-substring>"
                + "</xsl:analyze-string></out>"));

            Assert.AreEqual("<out><n p=\"1\"/><n p=\"3\"/><n p=\"5\"/></out>", principal);
            Assert.AreEqual("<m of=\"5\">1</m>", results.Written["file:///tmp/out/m2.xml"].ToString());
            Assert.AreEqual("<m of=\"5\">22</m>", results.Written["file:///tmp/out/m4.xml"].ToString());
        }
    }
}
