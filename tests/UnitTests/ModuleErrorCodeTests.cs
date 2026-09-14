namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that a refusal about a stylesheet module, or about a declaration in one, carries the code the
    /// specification names for it. A stylesheet that is wrong is refused either way; the code is what tells
    /// a caller which rule it broke, and a handler written against one cannot be written against a message.
    /// </summary>
    [TestClass]
    public sealed class ModuleErrorCodeTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        /// <summary>Serves the modules a test declares, by the name its references use.</summary>
        private sealed class Modules : IXsltResolver
        {
            private readonly Dictionary<string, string> m_files;

            public Modules(Dictionary<string, string> files)
            {
                m_files = files;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_files.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), "urn:test:" + href)
                    : null;
            }
        }

        private static string Refuses(
            string stylesheet,
            Dictionary<string, string>? modules = null,
            XsltVersion? version = null)
        {
            XsltException raised = Assert.ThrowsExactly<XsltException>(() => new Xslt(
                stylesheet,
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Version = version ?? XsltVersion.V30,
                    StylesheetResolver = new Modules(modules ?? new Dictionary<string, string>()),
                    BaseUri = "urn:test:principal.xsl",
                })
                .TransformXml("<doc/>"));

            return raised.Code ?? "(no code)";
        }

        private static string Sheet(string body, string attributes = "")
        {
            return $"<xsl:stylesheet version=\"3.0\" {Xsl} {attributes}>{body}"
                + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>";
        }

        [TestMethod]
        public void AModuleThatCannotBeReadOrReachesItselfSaysWhichItWas()
        {
            Assert.AreEqual(
                "XTSE0165",
                Refuses(Sheet("<xsl:import href=\"nowhere.xsl\"/>")),
                "a reference to a module that is not there");

            // The two kinds of reference are told apart here and nowhere else.
            Dictionary<string, string> itself = new()
            {
                ["loop.xsl"] = "<xsl:stylesheet version=\"3.0\" " + Xsl + "><xsl:include href=\"loop.xsl\"/></xsl:stylesheet>",
            };

            Assert.AreEqual(
                "XTSE0180",
                Refuses(Sheet("<xsl:include href=\"loop.xsl\"/>"), itself),
                "an xsl:include that reaches itself");

            Dictionary<string, string> imported = new()
            {
                ["loop.xsl"] = "<xsl:stylesheet version=\"3.0\" " + Xsl + "><xsl:import href=\"loop.xsl\"/></xsl:stylesheet>",
            };

            Assert.AreEqual(
                "XTSE0210",
                Refuses(Sheet("<xsl:import href=\"loop.xsl\"/>"), imported),
                "an xsl:import that reaches itself");
        }

        [TestMethod]
        public void AModuleThatIsNotAStylesheetSaysWhetherItWasTheOneHandedIn()
        {
            Assert.AreEqual(
                "XTSE0150",
                Refuses("<not-a-stylesheet/>"),
                "the module handed in");

            Dictionary<string, string> other = new() { ["other.xml"] = "<not-a-stylesheet/>" };

            Assert.AreEqual(
                "XTSE0165",
                Refuses(Sheet("<xsl:include href=\"other.xml\"/>"), other),
                "a module a reference reached");
        }

        [TestMethod]
        public void TheStaticErrorsOfADeclarationCarryTheirCodes()
        {
            Assert.AreEqual("XTSE0280", Refuses(Sheet("<xsl:template name=\"nope:x\"/>")));
            Assert.AreEqual("XTSE0350", Refuses(Sheet("<xsl:template match=\"/\"><e a=\"{1\"/></xsl:template>")));
            Assert.AreEqual("XTSE0370", Refuses(Sheet("<xsl:template match=\"/\"><e a=\"}\"/></xsl:template>")));
            Assert.AreEqual("XTSE0530", Refuses(Sheet("<xsl:template match=\"a\" priority=\"high\"/>")));
            Assert.AreEqual("XTSE0660", Refuses(Sheet("<xsl:template name=\"t\"/><xsl:template name=\"t\"/>")));
            Assert.AreEqual("XTSE0740", Refuses(Sheet("<xsl:function name=\"f\"/>")));
            Assert.AreEqual("XTSE0740", Refuses(Sheet("<xsl:function name=\"Q{}f\"/>")));

            Assert.AreEqual(
                "XTSE0720",
                Refuses(Sheet("<xsl:attribute-set name=\"a\" use-attribute-sets=\"a\"/>")));

            Assert.AreEqual(
                "XTSE1080",
                Refuses(Sheet("<xsl:template match=\"/\"><xsl:for-each-group select=\"*\"/></xsl:template>")));

            Assert.AreEqual(
                "XTSE1600",
                Refuses(Sheet("<xsl:character-map name=\"m\" use-character-maps=\"m\"/>")));
        }

        [TestMethod]
        public void AFunctionNameInANamespaceIsNotRefusedForHavingNoPrefix()
        {
            // Q{uri}local carries its namespace in the braces, so there is no prefix to look for and
            // nothing wrong with the name.
            Assert.AreEqual(
                "<out/>",
                new Xslt(
                    Sheet("<xsl:function name=\"Q{urn:f}g\"><xsl:sequence select=\"1\"/></xsl:function>"),
                    new XsltOptions { OmitXmlDeclaration = true })
                    .TransformXml("<doc/>"));
        }

        [TestMethod]
        public void AMisplacedImportIsRefusedByATwoPointZeroProcessorOnly()
        {
            // Until 3.0 the imports had to come first. 3.0 dropped the rule, and the same stylesheet is
            // read either way depending on which language it is being read in.
            const string Late =
                "<xsl:stylesheet version=\"2.0\" " + Xsl + ">"
                + "<xsl:template match=\"/\"><out/></xsl:template>"
                + "<xsl:import href=\"nowhere.xsl\"/></xsl:stylesheet>";

            Assert.AreEqual("XTSE0200", Refuses(Late, version: XsltVersion.V20));

            // And under 3.0 the placement is fine, so what is heard is that the module is not there.
            Assert.AreEqual("XTSE0165", Refuses(Late.Replace("\"2.0\"", "\"3.0\"", StringComparison.Ordinal)));
        }
    }
}
