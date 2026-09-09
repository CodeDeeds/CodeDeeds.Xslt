using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the XSLT-specific functions and instructions added after the core language:
    /// <c>xsl:apply-imports</c>, <c>format-number()</c>, <c>document()</c> and <c>lang()</c>.
    /// </summary>
    [TestClass]
    public sealed class XsltFunctionTests
    {
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_resources = new(StringComparer.Ordinal);

            public MapResolver Add(string name, string text)
            {
                m_resources[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_resources.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static string Sheet(string body)
        {
            return "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + body
                + "</xsl:stylesheet>";
        }

        private static string Run(string stylesheet, string input, XsltOptions? options = null)
        {
            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                StylesheetResolver = options?.StylesheetResolver,
                DocumentResolver = options?.DocumentResolver,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        private static void AssertMatchesReference(string stylesheet, string input)
        {
            XslCompiledTransform transform = new XslCompiledTransform();
            using (XmlReader reader = XmlReader.Create(new StringReader(stylesheet)))
            {
                transform.Load(reader);
            }

            StringWriter output = new StringWriter();
            XmlWriterSettings settings = transform.OutputSettings!.Clone();
            settings.OmitXmlDeclaration = true;
            settings.ConformanceLevel = ConformanceLevel.Auto;

            using (XmlWriter writer = XmlWriter.Create(output, settings))
            using (XmlReader reader = XmlReader.Create(new StringReader(input)))
            {
                transform.Transform(reader, null, writer);
            }

            Assert.AreEqual(
                Normalize(output.ToString()),
                Normalize(Run(stylesheet, input)),
                "output differed from XslCompiledTransform");
        }

        private static string Normalize(string fragment)
        {
            return XmlComparison.Normalize(fragment);
        }

        // ---- xsl:apply-imports ---------------------------------------------------------------------------

        [TestMethod]
        public void ApplyImportsCallsThroughToTheOverriddenTemplate()
        {
            // The point of apply-imports: an override that wraps what it inherited rather than replacing it.
            MapResolver resolver = new MapResolver()
                .Add("base.xsl", Sheet("<xsl:template match=\"item\"><plain><xsl:value-of select=\"@n\"/></plain>"
                    + "</xsl:template>"));

            Assert.AreEqual(
                "<out><wrapped><plain>1</plain></wrapped></out>",
                Run(Sheet("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"
                    + "<xsl:template match=\"item\"><wrapped><xsl:apply-imports/></wrapped></xsl:template>"),
                    "<r><item n=\"1\"/></r>",
                    new XsltOptions { StylesheetResolver = resolver }));
        }

        [TestMethod]
        public void ApplyImportsFallsBackToTheBuiltInRuleWhenNothingIsImported()
        {
            // With no lower-precedence template, the built-in rule applies — here copying the text.
            Assert.AreEqual(
                "<out><wrapped>text</wrapped></out>",
                Run(Sheet("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"
                    + "<xsl:template match=\"item\"><wrapped><xsl:apply-imports/></wrapped></xsl:template>"),
                    "<r><item>text</item></r>"));
        }

        [TestMethod]
        public void ApplyImportsSkipsPastEveryTemplateAtOrAboveTheCurrentPrecedence()
        {
            // Three levels: the middle one overrides the base, and the top overrides the middle. Calling
            // apply-imports from the top must reach the middle, not the base and not itself.
            MapResolver resolver = new MapResolver()
                .Add("base.xsl", Sheet("<xsl:template match=\"item\"><base/></xsl:template>"))
                .Add("middle.xsl", Sheet("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:template match=\"item\"><middle/></xsl:template>"));

            Assert.AreEqual(
                "<out><top><middle/></top></out>",
                Run(Sheet("<xsl:import href=\"middle.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"
                    + "<xsl:template match=\"item\"><top><xsl:apply-imports/></top></xsl:template>"),
                    "<r><item/></r>",
                    new XsltOptions { StylesheetResolver = resolver }));
        }

        [TestMethod]
        public void ApplyImportsKeepsTheCurrentMode()
        {
            MapResolver resolver = new MapResolver()
                .Add("base.xsl", Sheet(
                    "<xsl:template match=\"item\" mode=\"m\"><base-mode/></xsl:template>"
                    + "<xsl:template match=\"item\"><base-default/></xsl:template>"));

            Assert.AreEqual(
                "<out><over><base-mode/></over></out>",
                Run(Sheet("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:apply-templates select=\"//item\" mode=\"m\"/></out></xsl:template>"
                    + "<xsl:template match=\"item\" mode=\"m\"><over><xsl:apply-imports/></over></xsl:template>"),
                    "<r><item/></r>",
                    new XsltOptions { StylesheetResolver = resolver }));
        }

        // ---- format-number() -----------------------------------------------------------------------------

        [TestMethod]
        public void FormatNumberHandlesTheCommonPatterns()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:value-of select=\"format-number(1234567.891, '#,##0.00')\"/></a>"
                    + "<b><xsl:value-of select=\"format-number(0.5, '0.00')\"/></b>"
                    + "<c><xsl:value-of select=\"format-number(42, '000')\"/></c>"
                    + "<d><xsl:value-of select=\"format-number(1234.5, '#,###')\"/></d>"
                    + "<e><xsl:value-of select=\"format-number(0.07, '0.##%')\"/></e>"
                    + "<f><xsl:value-of select=\"format-number(12.3456, '#.##')\"/></f>"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void FormatNumberHandlesNegativesAndSpecialValues()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:value-of select=\"format-number(-1234.5, '#,##0.00')\"/></a>"
                    + "<b><xsl:value-of select=\"format-number(-1234.5, '#,##0.00;(#,##0.00)')\"/></b>"
                    + "<c><xsl:value-of select=\"format-number(0 div 0, '#,##0.00')\"/></c>"
                    + "<d><xsl:value-of select=\"format-number(1 div 0, '#,##0.00')\"/></d>"
                    + "<e><xsl:value-of select=\"format-number(-1 div 0, '#,##0.00')\"/></e>"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void FormatNumberRoundsHalvesAwayFromZero()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:value-of select=\"format-number(2.345, '0.00')\"/></a>"
                    + "<b><xsl:value-of select=\"format-number(2.355, '0.00')\"/></b>"
                    + "<c><xsl:value-of select=\"format-number(0.995, '0.00')\"/></c>"
                    + "<d><xsl:value-of select=\"format-number(9.999, '0.00')\"/></d>"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void FormatNumberUsesPrefixesAndSuffixes()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:value-of select=\"format-number(1234.5, 'NOK #,##0.00')\"/></a>"
                    + "<b><xsl:value-of select=\"format-number(1234.5, '#,##0.00 kr')\"/></b>"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void DecimalFormatRenamesTheSymbols()
        {
            // Continental European conventions: comma for the decimal point, period for grouping.
            AssertMatchesReference(
                Sheet("<xsl:decimal-format name=\"nb\" decimal-separator=\",\" grouping-separator=\".\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"format-number(1234567.89, '#.##0,00', 'nb')\"/>"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void AnUnnamedDecimalFormatChangesTheDefault()
        {
            AssertMatchesReference(
                Sheet("<xsl:decimal-format decimal-separator=\",\" grouping-separator=\" \" NaN=\"ikke tall\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<a><xsl:value-of select=\"format-number(1234.5, '# ##0,00')\"/></a>"
                    + "<b><xsl:value-of select=\"format-number(0 div 0, '#0,00')\"/></b>"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void AnUndeclaredDecimalFormatIsReportedAtCompileTime()
        {
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(() => new Xslt(Sheet(
                    "<xsl:template match=\"/\">"
                    + "<xsl:value-of select=\"format-number(1, '0', 'nope')\"/></xsl:template>"))).Message,
                "nope");
        }

        // ---- document() ----------------------------------------------------------------------------------

        [TestMethod]
        public void WithoutAResolverDocumentIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(Sheet(
                    "<xsl:template match=\"/\"><xsl:value-of select=\"document('d.xml')\"/></xsl:template>"))
                    .TransformXml("<r/>"));

            StringAssert.Contains(error.Message, "document resolver");
        }

        [TestMethod]
        public void DocumentLoadsAnExternalLookupTable()
        {
            // The classic use: codes in the input, descriptions in a separate file.
            MapResolver resolver = new MapResolver().Add(
                "codes.xml",
                "<codes><code id=\"a\">Alpha</code><code id=\"b\">Beta</code></codes>");

            Assert.AreEqual(
                "<out><i>Alpha</i><i>Beta</i></out>",
                Run(Sheet("<xsl:variable name=\"codes\" select=\"document('codes.xml')\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//item\">"
                    + "<i><xsl:value-of select=\"$codes/codes/code[@id = current()/@ref]\"/></i>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                    "<r><item ref=\"a\"/><item ref=\"b\"/></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void TheSameDocumentIsLoadedOnlyOnce()
        {
            // XSLT requires two calls naming one document to return the same nodes, so identity must hold.
            MapResolver resolver = new MapResolver().Add("d.xml", "<d><v>1</v></d>");

            Assert.AreEqual(
                "<out><same>true</same></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<same><xsl:value-of select=\"count(document('d.xml') | document('d.xml')) = 1\"/></same>"
                    + "</out></xsl:template>"),
                    "<r/>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        // ---- Node-sets spanning documents ----------------------------------------------------------------
        //
        // A node-set is normally drawn from one tree, and much of the engine is built around that. These are
        // the cases where it is not: document() given several URIs, and a union of two loaded documents.

        [TestMethod]
        public void DocumentAcceptsANodeSetNamingSeveralDocuments()
        {
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>A</v></d>")
                .Add("b.xml", "<d><v>B</v></d>")
                .Add("c.xml", "<d><v>C</v></d>");

            // The argument is a node-set of three file names, so one call loads three documents.
            Assert.AreEqual(
                "<out><v>A</v><v>B</v><v>C</v></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"document(//file)//v\">"
                    + "<v><xsl:value-of select=\".\"/></v>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                    "<r><file>a.xml</file><file>b.xml</file><file>c.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void CountSpansTheDocumentsLoaded()
        {
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>1</v><v>2</v></d>")
                .Add("b.xml", "<d><v>3</v></d>");

            Assert.AreEqual(
                "<out><roots>2</roots><values>3</values></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<roots><xsl:value-of select=\"count(document(//file))\"/></roots>"
                    + "<values><xsl:value-of select=\"count(document(//file)//v)\"/></values>"
                    + "</out></xsl:template>"),
                    "<r><file>a.xml</file><file>b.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void RepeatedUrisCollapseToOneDocument()
        {
            // Two mentions of one document must give the same nodes, so the union removes the duplicate.
            MapResolver resolver = new MapResolver().Add("a.xml", "<d><v>A</v></d>");

            Assert.AreEqual(
                "<out>1</out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"count(document(//file))\"/>"
                    + "</out></xsl:template>"),
                    "<r><file>a.xml</file><file>a.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void TwoDocumentsCanBeUnioned()
        {
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>A</v></d>")
                .Add("b.xml", "<d><v>B</v></d>");

            Assert.AreEqual(
                "<out><v>A</v><v>B</v></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"document('a.xml')//v | document('b.xml')//v\">"
                    + "<v><xsl:value-of select=\".\"/></v>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                    "<r/>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void NodesFromDifferentDocumentsAreDistinct()
        {
            // The same node id in two trees is two different nodes; the union must not collapse them.
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>A</v></d>")
                .Add("b.xml", "<d><v>B</v></d>");

            Assert.AreEqual(
                "<out>2</out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"count(document('a.xml')/d/v | document('b.xml')/d/v)\"/>"
                    + "</out></xsl:template>"),
                    "<r/>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void ApplyTemplatesMatchesInsideOneLoadedDocument()
        {
            // The single-document case, as a control for the spanning one below.
            MapResolver resolver = new MapResolver().Add("a.xml", "<d><v>A</v></d>");

            Assert.AreEqual(
                "<out>[A]</out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:apply-templates select=\"document('a.xml')//v\"/>"
                    + "</out></xsl:template>"
                    + "<xsl:template match=\"v\">[<xsl:value-of select=\".\"/>]</xsl:template>"),
                    "<r/>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void ApplyTemplatesReachesNodesInEveryDocument()
        {
            // Template matching resolves names against whichever document each node came from.
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>A</v></d>")
                .Add("b.xml", "<d><v>B</v></d>");

            Assert.AreEqual(
                "<out>[A][B]</out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:apply-templates select=\"document(//file)//v\"/>"
                    + "</out></xsl:template>"
                    + "<xsl:template match=\"v\">[<xsl:value-of select=\".\"/>]</xsl:template>"),
                    "<r><file>a.xml</file><file>b.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void SortingInterleavesDocuments()
        {
            // Each sort key is evaluated against the node's own document, so the result interleaves them
            // rather than keeping each document's nodes together.
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v n=\"3\">three</v><v n=\"1\">one</v></d>")
                .Add("b.xml", "<d><v n=\"2\">two</v><v n=\"4\">four</v></d>");

            Assert.AreEqual(
                "<out><v>one</v><v>two</v><v>three</v><v>four</v></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"document(//file)//v\">"
                    + "<xsl:sort select=\"@n\" data-type=\"number\"/>"
                    + "<v><xsl:value-of select=\".\"/></v>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                    "<r><file>a.xml</file><file>b.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void CopyOfCopiesFromEveryDocument()
        {
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>A</v></d>")
                .Add("b.xml", "<d><v>B</v></d>");

            Assert.AreEqual(
                "<out><v>A</v><v>B</v></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:copy-of select=\"document(//file)//v\"/>"
                    + "</out></xsl:template>"),
                    "<r><file>a.xml</file><file>b.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void PredicatesFilterWithinEachDocument()
        {
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v k=\"y\">A1</v><v k=\"n\">A2</v></d>")
                .Add("b.xml", "<d><v k=\"n\">B1</v><v k=\"y\">B2</v></d>");

            Assert.AreEqual(
                "<out><v>A1</v><v>B2</v></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"document(//file)//v[@k='y']\">"
                    + "<v><xsl:value-of select=\".\"/></v>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                    "<r><file>a.xml</file><file>b.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void TheStringValueComesFromTheFirstNodeInDocumentOrder()
        {
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>A</v></d>")
                .Add("b.xml", "<d><v>B</v></d>");

            Assert.AreEqual(
                "<out><first>A</first><matches>true</matches></out>",
                Run(Sheet("<xsl:variable name=\"all\" select=\"document(//file)//v\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<first><xsl:value-of select=\"$all\"/></first>"
                    + "<matches><xsl:value-of select=\"$all = 'B'\"/></matches>"
                    + "</out></xsl:template>"),
                    "<r><file>a.xml</file><file>b.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void ComparingASpanningNodeSetReadsEachNodeInItsOwnDocument()
        {
            // The optimised comparison path takes a list of node ids and one tree, which cannot describe nodes
            // from two documents — it would read a node of b.xml out of a.xml and quietly get another element's
            // text. Both of these must find their match in the second document.
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>A1</v><v>A2</v></d>")
                .Add("b.xml", "<d><v>B1</v><v>B2</v></d>");

            Assert.AreEqual(
                "<out><eq>true</eq><ne>true</ne><gt>true</gt></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<eq><xsl:value-of select=\"document(//file)//v = 'B2'\"/></eq>"
                    + "<ne><xsl:value-of select=\"document(//file)//v != 'A1'\"/></ne>"
                    + "<gt><xsl:value-of select=\"count(document(//file)//v) &gt; 3\"/></gt>"
                    + "</out></xsl:template>"),
                    "<r><file>a.xml</file><file>b.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void PredicatesAgainstASpanningSetUseTheRightDocument()
        {
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>A1</v></d>")
                .Add("b.xml", "<d><v>B1</v></d>");

            // The filter runs over nodes from both documents; only the one in b.xml has this string-value.
            Assert.AreEqual(
                "<out>1</out>",
                Run(Sheet("<xsl:variable name=\"all\" select=\"document(//file)//v\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"count($all[. = 'B1'])\"/>"
                    + "</out></xsl:template>"),
                    "<r><file>a.xml</file><file>b.xml</file></r>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void GenerateIdDistinguishesNodesInDifferentDocuments()
        {
            // Both are the same node id in their own tree, so identity has to account for the tree.
            MapResolver resolver = new MapResolver()
                .Add("a.xml", "<d><v>A</v></d>")
                .Add("b.xml", "<d><v>B</v></d>");

            Assert.AreEqual(
                "<out>false</out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"generate-id(document('a.xml')/d/v)"
                    + " = generate-id(document('b.xml')/d/v)\"/>"
                    + "</out></xsl:template>"),
                    "<r/>",
                    new XsltOptions { DocumentResolver = resolver }));
        }

        [TestMethod]
        public void AMissingDocumentIsReported()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Sheet("<xsl:template match=\"/\">"
                    + "<xsl:value-of select=\"document('gone.xml')\"/></xsl:template>"),
                    "<r/>",
                    new XsltOptions { DocumentResolver = new MapResolver() }));

            StringAssert.Contains(error.Message, "gone.xml");
        }

        [TestMethod]
        public void DocumentAndStylesheetResolversAreIndependent()
        {
            // Permitting modules must not implicitly permit data, or the other way round.
            MapResolver stylesheets = new MapResolver()
                .Add("lib.xsl", Sheet("<xsl:template match=\"x\"><from-lib/></xsl:template>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(
                    Sheet("<xsl:include href=\"lib.xsl\"/>"
                        + "<xsl:template match=\"/\"><xsl:value-of select=\"document('d.xml')\"/></xsl:template>"),
                    new XsltOptions { StylesheetResolver = stylesheets })
                    .TransformXml("<r/>"));

            StringAssert.Contains(error.Message, "document resolver");
        }

        // ---- lang() --------------------------------------------------------------------------------------

        [TestMethod]
        public void LangMatchesInheritedXmlLangAndSubLanguages()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//p\">"
                    + "<p en=\"{lang('en')}\" engb=\"{lang('en-GB')}\" no=\"{lang('no')}\"/>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                "<r xml:lang=\"en\"><p/><p xml:lang=\"en-GB\"/><p xml:lang=\"no\"/></r>");
        }

        [TestMethod]
        public void LangIsFalseWhenNoLanguageIsDeclared()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out><xsl:value-of select=\"lang('en')\"/></out></xsl:template>"),
                "<r><p/></r>");
        }
    }
}
