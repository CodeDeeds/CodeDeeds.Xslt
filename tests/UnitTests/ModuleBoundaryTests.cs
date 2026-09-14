namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what one stylesheet module can see of another: what an <c>xsl:apply-imports</c> reaches,
    /// what an <c>xsl:namespace-alias</c> moves, what several declarations of one key agree about, and
    /// where a module may be found inside a document that is not one.
    /// </summary>
    [TestClass]
    public sealed class ModuleBoundaryTests
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

        private static string Run(string principal, Dictionary<string, string> modules, string input)
        {
            return new Xslt(
                principal,
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    StylesheetResolver = new Modules(modules),
                    BaseUri = "urn:test:principal.xsl",
                })
                .TransformXml(input);
        }

        [TestMethod]
        public void ApplyImportsReachesWhatTheModuleImportedAndNoFurther()
        {
            // A module importing two others gives them both a lower precedence than its own, and neither of
            // them has imported the other. An xsl:apply-imports in one must not reach into the other: what
            // a rule may override is what its own module imported.
            const string Left =
                "<xsl:stylesheet version=\"3.0\" " + Xsl + ">"
                + "<xsl:template match=\"item\" priority=\"5\">L<xsl:apply-imports/></xsl:template>"
                + "</xsl:stylesheet>";

            const string Right =
                "<xsl:stylesheet version=\"3.0\" " + Xsl + ">"
                + "<xsl:template match=\"item\" priority=\"5\">R<xsl:apply-imports/></xsl:template>"
                + "</xsl:stylesheet>";

            const string Principal =
                "<xsl:stylesheet version=\"3.0\" " + Xsl + ">"
                + "<xsl:import href=\"left.xsl\"/><xsl:import href=\"right.xsl\"/>"
                + "<xsl:template match=\"item\" priority=\"5\">P<xsl:apply-imports/></xsl:template>"
                + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            Dictionary<string, string> modules = new()
            {
                ["left.xsl"] = Left,
                ["right.xsl"] = Right,
            };

            // P is the principal, then the later import wins among the two it imported, and there it stops:
            // right.xsl imported nothing, so the built-in rule writes the text and left.xsl is never asked.
            Assert.AreEqual("<out>PRx</out>", Run(Principal, modules, "<doc><item>x</item></doc>"));
        }

        [TestMethod]
        public void ANamespaceAliasDoesNotMoveAnAttributeInNoNamespace()
        {
            // Aliasing the default namespace moves a literal result element written with no prefix, the
            // default namespace being one an element can be in. An attribute written with no prefix is in
            // no namespace at all, so there is nothing there to move: a stylesheet writing a stylesheet
            // means version="1.0" and not xsl:version="1.0".
            const string Principal =
                "<xsl:stylesheet version=\"2.0\" " + Xsl + ">"
                + "<xsl:namespace-alias stylesheet-prefix=\"#default\" result-prefix=\"xsl\"/>"
                + "<xsl:template match=\"/\"><stylesheet version=\"1.0\"><template name=\"x\"/></stylesheet>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<xsl:stylesheet xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" version=\"1.0\">"
                + "<xsl:template name=\"x\"/></xsl:stylesheet>",
                Run(Principal, new Dictionary<string, string>(), "<doc/>"));
        }

        [TestMethod]
        public void AKeyDeclaredByAOnePointZeroModuleIsFiledBothWays()
        {
            // One key, declared by a 1.0 module and a 2.0 module. The 1.0 declaration files a value as the
            // string it spells and the 2.0 declaration files it as what it is, and a lookup asks the key
            // one question: whichever way it asks, it is entitled to an answer over the whole key.
            const string Old =
                "<xsl:stylesheet version=\"1.0\" " + Xsl + ">"
                + "<xsl:key name=\"k\" match=\"a\" use=\".\"/></xsl:stylesheet>";

            Dictionary<string, string> modules = new() { ["old.xsl"] = Old };

            // Asked as a 2.0 stylesheet asks, by value: the 2.0 declaration filed the number.
            const string ByValue =
                "<xsl:stylesheet version=\"2.0\" " + Xsl + ">"
                + "<xsl:import href=\"old.xsl\"/>"
                + "<xsl:key name=\"k\" match=\"b\" use=\"number(.)\"/>"
                + "<xsl:template match=\"/\"><out><xsl:copy-of select=\"key('k', 1)\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out><b>1.00</b><b>1</b></out>",
                Run(ByValue, modules, "<doc><a>1</a><b>1.00</b><b>1</b><b>2</b></doc>"));

            // Asked as a 1.0 stylesheet asks, by string: both declarations answer, since the 2.0 one files
            // the string its value spells as well.
            const string ByString =
                "<xsl:stylesheet version=\"2.0\" " + Xsl + ">"
                + "<xsl:import href=\"old.xsl\"/>"
                + "<xsl:key name=\"k\" match=\"b\" use=\"number(.)\"/>"
                + "<xsl:template match=\"/\"><out><xsl:copy-of select=\"key('k', 1)\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            // All three: the 2.0 declaration files the number its use expression produced, and the
            // number one spells 1 however the element it came from was written.
            Assert.AreEqual(
                "<out><a>1</a><b>1.00</b><b>1</b></out>",
                Run(
                    ByString.Replace("version=\"2.0\" " + Xsl, "version=\"1.0\" " + Xsl, StringComparison.Ordinal),
                    modules,
                    "<doc><a>1</a><b>1.00</b><b>1</b><b>2</b></doc>"));
        }

        [TestMethod]
        public void AModuleMayBeEmbeddedInADocumentThatIsNotOne()
        {
            // A reference may name an element inside a document rather than the document itself, which is
            // how a stylesheet is carried in something that is something else. The identifier is an
            // attribute the document type declared as an ID, so the document says which attribute that is.
            const string Embedded =
                "<?xml version=\"1.0\"?>"
                + "<!DOCTYPE holder [<!ATTLIST xsl:stylesheet id ID #REQUIRED>]>"
                + "<holder><note>not a stylesheet</note>"
                + "<xsl:stylesheet id=\"inside\" version=\"1.0\" " + Xsl + ">"
                + "<xsl:template match=\"item\">[<xsl:value-of select=\".\"/>]</xsl:template>"
                + "</xsl:stylesheet></holder>";

            const string Principal =
                "<xsl:stylesheet version=\"3.0\" " + Xsl + ">"
                + "<xsl:include href=\"holder.xml#inside\"/>"
                + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            Dictionary<string, string> modules = new() { ["holder.xml"] = Embedded };

            Assert.AreEqual("<out>[x]</out>", Run(Principal, modules, "<doc><item>x</item></doc>"));
        }

        [TestMethod]
        public void AFragmentThatNamesNothingOrSomethingElseIsRefused()
        {
            const string Embedded =
                "<?xml version=\"1.0\"?>"
                + "<!DOCTYPE holder [<!ATTLIST note id ID #REQUIRED>]>"
                + "<holder><note id=\"inside\">not a stylesheet</note></holder>";

            Dictionary<string, string> modules = new() { ["holder.xml"] = Embedded };

            string Sheet(string href) =>
                "<xsl:stylesheet version=\"3.0\" " + Xsl + ">"
                + "<xsl:include href=\"" + href + "\"/>"
                + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>";

            // The identifier names an element that is not a stylesheet module.
            XsltException notAModule = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet("holder.xml#inside"), modules, "<doc/>"));

            StringAssert.Contains(notAModule.Message, "note");

            // And one nothing carries.
            XsltException missing = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet("holder.xml#nowhere"), modules, "<doc/>"));

            StringAssert.Contains(missing.Message, "nowhere");
        }
    }
}
