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

            // A set written to use itself is there in the text for its author to see, and is the static
            // error it has always been. A cycle that exists only once an xsl:override has been bound is
            // in neither package on its own, and is XTDE0640; see OverrideTests.
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

        [TestMethod]
        public void ApplyingTemplatesToTheChildrenOfAnAtomicValueIsRefused()
        {
            // With no select, xsl:apply-templates processes the children of the context item. An atomic
            // value has none, and reaching for them walked off the end of the node arrays.
            string Walking(string body) =>
                $"<xsl:stylesheet version=\"3.0\" {Xsl}><xsl:template match=\"/\">"
                + "<xsl:for-each select=\"1 to 3\">" + body + "</xsl:for-each>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual("XTTE0510", Refuses(Walking("<xsl:apply-templates/>")));

            // Sorting the children reaches them by a second route, and is held to the same rule.
            Assert.AreEqual(
                "XTTE0510",
                Refuses(Walking(
                    "<xsl:apply-templates><xsl:sort select=\".\"/></xsl:apply-templates>")));

            // A node context item still works, which is what says the guard is about the kind of item and
            // not about apply-templates having no select.
            Assert.AreEqual(
                "<out/>",
                new Xslt(
                    Sheet("<xsl:template match=\"doc\"><xsl:apply-templates/></xsl:template>"),
                    new XsltOptions { OmitXmlDeclaration = true })
                    .TransformXml("<doc/>"));
        }

        [TestMethod]
        public void AMissingRequiredAttributeSaysWhichElementWantedIt()
        {
            Assert.AreEqual("XTSE0010", Refuses(Sheet("<xsl:include/>")));
            Assert.AreEqual("XTSE0010", Refuses(Sheet("<xsl:import/>")));

            Assert.AreEqual(
                "XTSE0010",
                Refuses(Sheet("<xsl:character-map name=\"m\"><xsl:output-character string=\"x\"/></xsl:character-map>")));

        }

        [TestMethod]
        public void AnInstructionThisEngineHasNotGotIsRefusedWhenItIsReached()
        {
            // Under forwards-compatible processing a stylesheet may hold instructions from a later version,
            // and each is an error only if reached without an xsl:fallback to stand in for it.
            const string Later =
                "<xsl:stylesheet version=\"22.0\" " + Xsl + ">"
                + "<xsl:template match=\"/\"><out><xsl:banana/></out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual("XTSE0010", Refuses(Later));

            // With an xsl:fallback it is not an error at all, and the fallback is what runs.
            const string Guarded =
                "<xsl:stylesheet version=\"22.0\" " + Xsl + ">"
                + "<xsl:template match=\"/\"><out><xsl:banana><xsl:fallback>instead</xsl:fallback>"
                + "</xsl:banana></out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>instead</out>",
                new Xslt(Guarded, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<doc/>"));
        }

        [TestMethod]
        public void ATunnelParameterOnAFunctionIsRefused()
        {
            // A function takes its arguments and nothing else, so tunnel="yes" on one of its parameters is
            // a value the attribute may not take.
            Assert.AreEqual(
                "XTSE0020",
                Refuses(Sheet(
                    "<xsl:function name=\"f:x\" xmlns:f=\"urn:f\">"
                    + "<xsl:param name=\"p\" tunnel=\"yes\"/><xsl:sequence select=\"$p\"/></xsl:function>")));
        }
    }
}
