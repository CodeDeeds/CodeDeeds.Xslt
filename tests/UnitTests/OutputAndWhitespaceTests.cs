using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:strip-space</c>, <c>xsl:preserve-space</c>, and the <c>xsl:output</c> options beyond
    /// the serialization method.
    /// </summary>
    [TestClass]
    public sealed class OutputAndWhitespaceTests
    {
        private static string Sheet(string body)
        {
            return "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + body
                + "</xsl:stylesheet>";
        }

        /// <summary>Compiles a stylesheet with the XML declaration suppressed, since these tests compare fragments.</summary>
        private static Xslt Compile(string stylesheet, XsltBackend backend = XsltBackend.Interpreted)
        {
            return new Xslt(stylesheet, new XsltOptions { Backend = backend, OmitXmlDeclaration = true });
        }

        private static string Run(string stylesheet, string input)
        {
            string interpreted = Compile(stylesheet, XsltBackend.Interpreted).TransformXml(input);
            string compiled = Compile(stylesheet, XsltBackend.Compiled).TransformXml(input);

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

        // ---- Whitespace stripping ------------------------------------------------------------------------

        private const string Indented = "<r>\n  <a>text</a>\n  <b/>\n</r>";

        [TestMethod]
        public void StripSpaceRemovesWhitespaceOnlyTextNodes()
        {
            AssertMatchesReference(
                Sheet("<xsl:strip-space elements=\"r\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<n><xsl:value-of select=\"count(/r/node())\"/></n>"
                    + "</out></xsl:template>"),
                Indented);
        }

        [TestMethod]
        public void StripSpaceWithAWildcardAppliesEverywhere()
        {
            AssertMatchesReference(
                Sheet("<xsl:strip-space elements=\"*\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<n><xsl:value-of select=\"count(//text())\"/></n>"
                    + "</out></xsl:template>"),
                Indented);
        }

        [TestMethod]
        public void PreserveSpaceOverridesAMoreGeneralStripSpace()
        {
            // The more specific declaration wins, whichever order they are written in.
            AssertMatchesReference(
                Sheet("<xsl:strip-space elements=\"*\"/>"
                    + "<xsl:preserve-space elements=\"r\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<n><xsl:value-of select=\"count(/r/node())\"/></n>"
                    + "</out></xsl:template>"),
                Indented);
        }

        [TestMethod]
        public void NonWhitespaceTextIsNeverStripped()
        {
            AssertMatchesReference(
                Sheet("<xsl:strip-space elements=\"*\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"/r/a\"/></out></xsl:template>"),
                Indented);
        }

        [TestMethod]
        public void XmlSpacePreserveOverridesStripSpace()
        {
            // xml:space is an XML-level instruction and outranks the stylesheet's declaration.
            string input = "<r>\n  <keep xml:space=\"preserve\">\n  <inner/>\n</keep>\n</r>";

            string result = Run(
                Sheet("<xsl:strip-space elements=\"*\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<kept><xsl:value-of select=\"count(/r/keep/node())\"/></kept>"
                    + "<stripped><xsl:value-of select=\"count(/r/node())\"/></stripped>"
                    + "</out></xsl:template>"),
                input);

            // <keep> retains its two whitespace text nodes plus <inner>; <r> keeps only <keep>.
            Assert.AreEqual("<out><kept>3</kept><stripped>1</stripped></out>", result);
        }

        [TestMethod]
        public void StripSpaceMatchesOnNamespaceNotPrefix()
        {
            string input = "<d:r xmlns:d=\"urn:d\">\n  <d:a/>\n</d:r>";

            AssertMatchesReference(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:p=\"urn:d\">"
                + "<xsl:strip-space elements=\"p:r\"/>"
                + "<xsl:template match=\"/\"><out>"
                + "<n><xsl:value-of select=\"count(/p:r/node())\"/></n>"
                + "</out></xsl:template>"
                + "</xsl:stylesheet>",
                input);
        }

        [TestMethod]
        public void AStripSpaceNameTestMayLeaveEitherHalfOpen()
        {
            // The elements attribute is a list of name tests and not of names, so '*:a' is the element of
            // that local name in any namespace at all — as much a name test as 'p:*' and not a prefix the
            // stylesheet forgot to declare. Both are less specific than a name test that gives both halves
            // and more specific than a bare star, which is the order of the priorities a name test carries
            // as a pattern.
            string input = "<r xmlns:a=\"urn:a\" xmlns:b=\"urn:b\">"
                + "<a:x> </a:x><b:x> </b:x><a:y> </a:y></r>";

            Assert.AreEqual(
                "<out><n>0</n><n>0</n><n>1</n></out>",
                Run(
                    Sheet("<xsl:strip-space elements=\"*:x\"/>"
                        + "<xsl:template match=\"/\"><out>"
                        + "<xsl:for-each select=\"/r/*\">"
                        + "<n><xsl:value-of select=\"count(node())\"/></n></xsl:for-each>"
                        + "</out></xsl:template>"),
                    input));

            // The namespace in braces says the same thing without a prefix in scope to say it with, which
            // is the name test 3.0 writes wherever a lexical one is written.
            Assert.AreEqual(
                "<out><n>0</n><n>1</n><n>0</n></out>",
                new Xslt(
                    Sheet("<xsl:strip-space elements=\"Q{urn:a}x Q{urn:a}y\"/>"
                        + "<xsl:template match=\"/\"><out>"
                        + "<xsl:for-each select=\"/r/*\">"
                        + "<n><xsl:value-of select=\"count(node())\"/></n></xsl:for-each>"
                        + "</out></xsl:template>"),
                    new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 }).TransformXml(input));
        }

        [TestMethod]
        public void AnUnprefixedStripSpaceNameTestTakesTheDefaultNamespaceForNameTests()
        {
            // Not a name, so not in no namespace: xpath-default-namespace says which namespace an
            // unprefixed name test is in here as it does anywhere else one is written.
            Assert.AreEqual(
                "<out><n>0</n><n>1</n></out>",
                Run(
                    "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                    + "<xsl:strip-space elements=\"x\" xpath-default-namespace=\"urn:a\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"/r/*\">"
                    + "<n><xsl:value-of select=\"count(node())\"/></n></xsl:for-each>"
                    + "</out></xsl:template></xsl:stylesheet>",
                    "<r xmlns:a=\"urn:a\"><a:x> </a:x><x> </x></r>"));
        }

        // ---- Output options ------------------------------------------------------------------------------

        [TestMethod]
        public void IndentNestsElementsButLeavesMixedContentAlone()
        {
            Assert.AreEqual(
                "<a>\n  <b>text</b>\n  <c/>\n</a>",
                Run(Sheet("<xsl:output method=\"xml\" indent=\"yes\"/>"
                    + "<xsl:template match=\"/\"><a><b>text</b><c/></a></xsl:template>"),
                    "<r/>"));

            // An element holding text must not gain whitespace, which would change its content.
            Assert.AreEqual(
                "<p>Hello <b>world</b>!</p>",
                Run(Sheet("<xsl:output method=\"xml\" indent=\"yes\"/>"
                    + "<xsl:template match=\"/\"><p><xsl:text>Hello </xsl:text><b>world</b>"
                    + "<xsl:text>!</xsl:text></p></xsl:template>"),
                    "<r/>"));
        }

        [TestMethod]
        public void TheMethodIsInferredFromTheDocumentElement()
        {
            // XSLT selects the HTML method when the result's document element is html in no namespace, so a
            // stylesheet producing a web page needs no xsl:output to get void elements written correctly.
            //
            // Compared by behaviour rather than against the reference: exactly where an HTML serializer puts
            // its line breaks is left to the processor, and the framework's writer suppresses them around
            // certain elements. What must hold is that the HTML method was chosen at all.
            string html = Run(
                Sheet("<xsl:template match=\"/\"><html><body><br/><p>hi</p></body></html></xsl:template>"),
                "<r/>");

            StringAssert.Contains(html, "<br>", "a void element must not be self-closed");
            Assert.DoesNotContain("<br/>", html);
            Assert.DoesNotContain("<br/>", html);
            StringAssert.Contains(html, "\n", "the HTML method indents by default");

            // Anything else stays XML: self-closing, and not indented.
            Assert.AreEqual(
                "<doc><body><br/><p>hi</p></body></doc>",
                Run(Sheet("<xsl:template match=\"/\"><doc><body><br/><p>hi</p></body></doc></xsl:template>"),
                    "<r/>"));
        }

        [TestMethod]
        public void IndentDefaultsToTheMethodButAnExplicitSettingWins()
        {
            // Indenting is on for HTML and off for XML unless the stylesheet says otherwise.
            AssertMatchesReference(
                Sheet("<xsl:output method=\"html\"/>"
                    + "<xsl:template match=\"/\"><html><body><p>hi</p></body></html></xsl:template>"),
                "<r/>");

            Assert.AreEqual(
                "<html><body><p>hi</p></body></html>",
                Run(Sheet("<xsl:output method=\"html\" indent=\"no\"/>"
                    + "<xsl:template match=\"/\"><html><body><p>hi</p></body></html></xsl:template>"),
                    "<r/>"));
        }

        [TestMethod]
        public void IndentIsOffByDefault()
        {
            Assert.AreEqual(
                "<a><b>text</b></a>",
                Run(Sheet("<xsl:template match=\"/\"><a><b>text</b></a></xsl:template>"), "<r/>"));
        }

        [TestMethod]
        public void XmlDeclarationIsWrittenByDefaultAsTheSpecificationRequires()
        {
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><a/>",
                new Xslt(Sheet("<xsl:template match=\"/\"><a/></xsl:template>")).TransformXml("<r/>"));

            Assert.AreEqual(
                "<a/>",
                new Xslt(Sheet("<xsl:output omit-xml-declaration=\"yes\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>")).TransformXml("<r/>"));

            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"iso-8859-1\"?><a/>",
                new Xslt(Sheet("<xsl:output encoding=\"iso-8859-1\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>")).TransformXml("<r/>"));
        }

        [TestMethod]
        public void OptionsOverrideWhateverTheStylesheetAskedFor()
        {
            const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><a/>";
            string plain = Sheet("<xsl:template match=\"/\"><a/></xsl:template>");

            Assert.AreEqual(
                "<a/>",
                new Xslt(plain, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));

            // And in the other direction: the option wins over an explicit request in the stylesheet.
            string suppressed = Sheet("<xsl:output omit-xml-declaration=\"yes\"/>"
                + "<xsl:template match=\"/\"><a/></xsl:template>");

            Assert.AreEqual("<a/>", new Xslt(suppressed).TransformXml("<r/>"));
            Assert.AreEqual(
                Declaration,
                new Xslt(suppressed, new XsltOptions { OmitXmlDeclaration = false }).TransformXml("<r/>"));
        }

        [TestMethod]
        public void WithReconfiguresWithoutCompilingAgain()
        {
            // The reason the settings live in options rather than in the stylesheet: one compiled stylesheet
            // serving both a standalone document and an embedded fragment.
            Xslt standalone = new Xslt(Sheet("<xsl:template match=\"/\"><a/></xsl:template>"));
            Xslt embedded = standalone.With(standalone.Options.WithOmitXmlDeclaration(true));

            Assert.AreEqual("<?xml version=\"1.0\" encoding=\"UTF-8\"?><a/>", standalone.TransformXml("<r/>"));
            Assert.AreEqual("<a/>", embedded.TransformXml("<r/>"));

            // The original is untouched, because neither instance can be reconfigured after construction.
            Assert.AreEqual("<?xml version=\"1.0\" encoding=\"UTF-8\"?><a/>", standalone.TransformXml("<r/>"));
            Assert.IsNull(standalone.Options.OmitXmlDeclaration);
        }

        [TestMethod]
        public void WithRefusesToChangeTheBackend()
        {
            // The backend decides what the stylesheet was compiled to, so it cannot be swapped afterwards.
            Xslt stylesheet = new Xslt(Sheet("<xsl:template match=\"/\"><a/></xsl:template>"));

            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(
                () => stylesheet.With(new XsltOptions { Backend = XsltBackend.Compiled }));

            StringAssert.Contains(error.Message, "compiled");
        }

        [TestMethod]
        public void NonXmlMethodsNeverWriteADeclaration()
        {
            // The declaration belongs to the XML method; asking for it elsewhere must not produce one.
            Assert.AreEqual(
                "text",
                new Xslt(Sheet("<xsl:output method=\"text\" omit-xml-declaration=\"no\"/>"
                    + "<xsl:template match=\"/\"><xsl:text>text</xsl:text></xsl:template>"))
                    .TransformXml("<r/>"));

            Assert.AreEqual(
                "<p></p>",
                new Xslt(Sheet("<xsl:output method=\"html\" omit-xml-declaration=\"no\"/>"
                    + "<xsl:template match=\"/\"><p/></xsl:template>")).TransformXml("<r/>"));
        }

        [TestMethod]
        public void DoctypeIsWrittenBeforeTheDocumentElement()
        {
            // A document element named html selects the HTML method, which indents and never self-closes.
            Assert.AreEqual(
                "<!DOCTYPE html SYSTEM \"about:legacy-compat\">\n<html></html>",
                Run(Sheet("<xsl:output doctype-system=\"about:legacy-compat\"/>"
                    + "<xsl:template match=\"/\"><html/></xsl:template>"),
                    "<r/>"));

            Assert.AreEqual(
                "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"x.dtd\">\n<html></html>",
                Run(Sheet("<xsl:output doctype-public=\"-//W3C//DTD XHTML 1.0 Strict//EN\" "
                    + "doctype-system=\"x.dtd\"/>"
                    + "<xsl:template match=\"/\"><html/></xsl:template>"),
                    "<r/>"));
        }

        [TestMethod]
        public void CdataSectionElementsWrapTheirTextInsteadOfEscapingIt()
        {
            Assert.AreEqual(
                "<out><script><![CDATA[if (a < b) { }]]></script><other>if (a &lt; b) { }</other></out>",
                Run(Sheet("<xsl:output cdata-section-elements=\"script\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<script><xsl:value-of select=\"/r\"/></script>"
                    + "<other><xsl:value-of select=\"/r\"/></other>"
                    + "</out></xsl:template>"),
                    "<r>if (a &lt; b) { }</r>"));
        }

        [TestMethod]
        public void CdataContainingTheTerminatorIsSplitAcrossSections()
        {
            // "]]>" cannot appear inside a CDATA section, so the text is split around it and stays intact.
            string result = Run(
                Sheet("<xsl:output cdata-section-elements=\"d\"/>"
                    + "<xsl:template match=\"/\"><d><xsl:value-of select=\"/r\"/></d></xsl:template>"),
                "<r>a]]&gt;b</r>");

            Assert.AreEqual("<d><![CDATA[a]]]]><![CDATA[>b]]></d>", result);

            // The round trip is what actually matters: a parser must read back the original text.
            Assert.AreEqual("a]]>b", XDocument.Parse(result).Root!.Value);
        }

        [TestMethod]
        public void CdataSectionElementsNamesAnElementSoTheDefaultNamespaceApplies()
        {
            // Unlike the QName of a template or a variable, where it deliberately does not. A stylesheet that
            // declares a default namespace and asks for cdata-section-elements="t" means the t it is
            // producing; reading the name as no-namespace matched nothing at all. Confirmed against
            // XslCompiledTransform, which agrees.
            AssertMatchesReference(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns=\"urn:x\">"
                + "<xsl:output cdata-section-elements=\"t\"/>"
                + "<xsl:template match=\"/\"><out><t>a&lt;b</t></out></xsl:template>"
                + "</xsl:stylesheet>",
                "<r/>");
        }

        [TestMethod]
        public void TheXmlPrefixIsNeverDeclared()
        {
            // It is bound everywhere by definition, so xmlns:xml="…" is not a redundant declaration but an
            // ill-formed document.
            Assert.AreEqual(
                "<out xml:lang=\"en\">x</out>",
                Run(Sheet("<xsl:template match=\"/\"><out xml:lang=\"en\">x</out></xsl:template>"), "<r/>"));
        }

        // ---- The XHTML output method -----------------------------------------------------------------------

        private static string Xhtml(string body)
        {
            return "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns=\"http://www.w3.org/1999/xhtml\">"
                + "<xsl:output method=\"xhtml\" indent=\"no\" include-content-type=\"no\"/>"
                + body
                + "</xsl:stylesheet>";
        }

        [TestMethod]
        public void XhtmlCollapsesOnlyTheElementsHtmlCallsEmpty()
        {
            // The whole of the method: a browser reading <p/> as HTML sees an unclosed paragraph, and one
            // reading <br></br> sees two line breaks.
            Assert.AreEqual(
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><br /><img src=\"x\" /><p></p><span>t</span></html>",
                Run(Xhtml("<xsl:template match=\"/\"><html><br/><img src=\"x\"/><p/><span>t</span></html>"
                    + "</xsl:template>"), "<r/>"));
        }

        [TestMethod]
        public void XhtmlLeavesAnElementOutsideItsNamespaceToTheXmlRules()
        {
            Assert.AreEqual(
                "<out><p /></out>",
                Run(Xhtml("<xsl:template match=\"/\"><out xmlns=\"\"><p/></out></xsl:template>"), "<r/>"));
        }

        [TestMethod]
        public void XhtmlIsStillXmlAndSaysSo()
        {
            string result = new Xslt(Xhtml("<xsl:template match=\"/\"><html><br/></html></xsl:template>"))
                .TransformXml("<r/>");

            StringAssert.StartsWith(result, "<?xml version=\"1.0\"");
        }

        [TestMethod]
        public void TheContentTypeMetaGoesFirstInTheHead()
        {
            Assert.AreEqual(
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head>"
                + "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\" />"
                + "<title>t</title></head></html>",
                Run("<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                    + "xmlns=\"http://www.w3.org/1999/xhtml\">"
                    + "<xsl:output method=\"xhtml\" indent=\"no\"/>"
                    + "<xsl:template match=\"/\"><html><head><title>t</title></head></html></xsl:template>"
                    + "</xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void AnEmptyHeadStillGetsItsMeta()
        {
            Assert.AreEqual(
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head>"
                + "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\" />"
                + "</head></html>",
                Run("<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                    + "xmlns=\"http://www.w3.org/1999/xhtml\">"
                    + "<xsl:output method=\"xhtml\" indent=\"no\"/>"
                    + "<xsl:template match=\"/\"><html><head/></html></xsl:template>"
                    + "</xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void TheHtmlMethodPutsAMetaInItsHeadToo()
        {
            // Not checked against XslCompiledTransform, though it does the same thing: it writes into a
            // StringWriter, so the charset it names is the writer's utf-16 rather than the stylesheet's, and
            // the comparison would be about the harness. What it does agree on is that the meta is there.
            Assert.AreEqual(
                "<HTML><HEAD><meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\">h</HEAD>"
                + "<BODY>b</BODY></HTML>",
                Run(Sheet("<xsl:output method=\"html\" indent=\"no\"/>"
                    + "<xsl:template match=\"/\"><HTML><HEAD>h</HEAD><BODY>b</BODY></HTML></xsl:template>"),
                    "<r/>"));
        }

        // ---- What the HTML and XHTML methods owe a browser -------------------------------------------------

        /// <summary>Wraps a body in an XHTML-producing stylesheet, meta and all.</summary>
        private static string Page(string body, string output = "method=\"xhtml\" indent=\"no\"")
        {
            return "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + $"xmlns=\"http://www.w3.org/1999/xhtml\"><xsl:output {output}/>"
                + $"<xsl:template match=\"/\">{body}</xsl:template></xsl:stylesheet>";
        }

        [TestMethod]
        public void AUriAttributeIsPercentEscapedAndOthersAreNot()
        {
            // href on a is a URI; accesskey is not, and neither is a name HTML has never heard of. Escaping
            // one that merely looks like a URI would corrupt it.
            Assert.AreEqual(
                "<a xmlns=\"http://www.w3.org/1999/xhtml\" href=\"%C2%A1\" accesskey=\"¡\" other=\"¡\"></a>",
                Run(Page("<a href=\"&#xA1;\" accesskey=\"&#xA1;\" other=\"&#xA1;\"/>",
                    "method=\"xhtml\" indent=\"no\" include-content-type=\"no\""),
                    "<r/>"));
        }

        [TestMethod]
        public void PrintableAsciiSurvivesUriEscapingExactly()
        {
            // A space, a quotation mark and an already-escaped %20 all stand: re-escaping an escape would
            // change where the link points, and the markup escaping still protects what markup needs.
            Assert.AreEqual(
                "<a xmlns=\"http://www.w3.org/1999/xhtml\" href=\"a b %20 &quot; &amp; ~\"></a>",
                Run(Page("<a href=\"a b %20 &#34; &amp; ~\"/>",
                    "method=\"xhtml\" indent=\"no\" include-content-type=\"no\""),
                    "<r/>"));
        }

        [TestMethod]
        public void AUriIsComposedBeforeItIsEscaped()
        {
            // One letter written two ways: a with a ring, and a followed by a combining ring. Escaping them
            // as they came would give two different URIs for one address.
            Assert.AreEqual(
                "<a xmlns=\"http://www.w3.org/1999/xhtml\" href=\"%C3%A5%C3%A5\"></a>",
                Run(Page("<a href=\"&#xE5;a&#x30A;\"/>",
                    "method=\"xhtml\" indent=\"no\" include-content-type=\"no\""),
                    "<r/>"));
        }

        [TestMethod]
        public void EscapeUriAttributesCanBeSwitchedOff()
        {
            Assert.AreEqual(
                "<a xmlns=\"http://www.w3.org/1999/xhtml\" href=\"¡\"></a>",
                Run(Page("<a href=\"&#xA1;\"/>",
                    "method=\"xhtml\" indent=\"no\" include-content-type=\"no\" escape-uri-attributes=\"no\""),
                    "<r/>"));
        }

        [TestMethod]
        public void AControlCharacterBecomesAReferenceInHtml()
        {
            // #x7F to #x9F are unassigned controls in Unicode and Windows-1252 characters to a browser, so a
            // literal one means different things in different readers. UTF-8 can carry it; that is not the
            // point of the rule.
            Assert.AreEqual(
                "<a xmlns=\"http://www.w3.org/1999/xhtml\" accesskey=\"&#150;\">&#150;</a>",
                Run(Page("<a accesskey=\"&#x96;\">&#x96;</a>",
                    "method=\"xhtml\" indent=\"no\" include-content-type=\"no\""),
                    "<r/>"));
        }

        [TestMethod]
        public void TheSerializersMetaReplacesOneAlreadyInTheHead()
        {
            // Otherwise the page claims two encodings, and a reader has to guess which one it meant.
            Assert.AreEqual(
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head>"
                + "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\" />"
                + "<title>t</title></head></html>",
                Run(Page("<html><head><meta http-equiv=\"CONTENT-TYPE\" content=\"text/html; charset=UTF-16\"/>"
                    + "<title>t</title></head></html>"),
                    "<r/>"));
        }

        [TestMethod]
        public void AMetaThatIsNotAContentTypeIsLeftAlone()
        {
            Assert.AreEqual(
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head>"
                + "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\" />"
                + "<meta name=\"author\" content=\"nobody\" /></head></html>",
                Run(Page("<html><head><meta name=\"author\" content=\"nobody\"/></head></html>"), "<r/>"));
        }

        [TestMethod]
        public void AnUnstatedMethodIsInferredFromAnXhtmlDocumentElement()
        {
            // The meta is the visible sign of it: no xsl:output at all, and the result is XHTML because of
            // what the document element turned out to be.
            StringAssert.Contains(
                Run("<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                    + "xmlns=\"http://www.w3.org/1999/xhtml\">"
                    + "<xsl:template match=\"/\"><html><head/></html></xsl:template></xsl:stylesheet>",
                    "<r/>"),
                "<meta http-equiv=\"Content-Type\"");
        }

        [TestMethod]
        public void AOnePointZeroStylesheetIsNotInferredToBeXhtml()
        {
            // XSLT 1.0 had no XHTML method and no meta of its own to add, so a stylesheet written against it
            // gets the XML method it would have got then — escaping and all.
            Assert.AreEqual(
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><a href=\"¡\"/></html>",
                Run("<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                    + "xmlns=\"http://www.w3.org/1999/xhtml\">"
                    + "<xsl:template match=\"/\"><html><a href=\"&#xA1;\"/></html></xsl:template>"
                    + "</xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void CDataSectionElementsAccumulateAcrossDeclarations()
        {
            // The one output attribute that is a list rather than a choice, so several declarations amount
            // to their union rather than the last one winning.
            Assert.AreEqual(
                "<out><a><![CDATA[<1>]]></a><b><![CDATA[<2>]]></b></out>",
                Run(Sheet("<xsl:output cdata-section-elements=\"a\" indent=\"no\"/>"
                    + "<xsl:output cdata-section-elements=\"b\"/>"
                    + "<xsl:template match=\"/\"><out><a>&lt;1&gt;</a><b>&lt;2&gt;</b></out></xsl:template>"),
                    "<r/>"));
        }

        [TestMethod]
        public void APublicIdentifierWithoutASystemOneWritesNoDoctypeInXml()
        {
            // The external subset a declaration points at is the system identifier, and there is nothing to
            // point at without it. The HTML method, which has a document type it can name on its own, does
            // write one.
            Assert.AreEqual(
                "<a/>",
                Run(Sheet("<xsl:output method=\"xml\" doctype-public=\"-//X//DTD Y//EN\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>"),
                    "<r/>"));

            StringAssert.Contains(
                Run(Sheet("<xsl:output method=\"html\" doctype-public=\"-//X//DTD Y//EN\" indent=\"no\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>"),
                    "<r/>"),
                "<!DOCTYPE a PUBLIC \"-//X//DTD Y//EN\">");
        }

        [TestMethod]
        public void AnIdentifierHoldingAQuoteIsDelimitedWithApostrophes()
        {
            // Neither identifier is something character references reach, so the only thing a serializer may
            // vary is which quote it uses.
            StringAssert.Contains(
                Run(Sheet("<xsl:output doctype-system=\"AB&#34;CD\" doctype-public=\"AB'CD\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>"),
                    "<r/>"),
                "<!DOCTYPE a PUBLIC \"AB'CD\" 'AB\"CD'>");
        }

        [TestMethod]
        public void ADoctypePublicThatIsNotAPublicIdentifierIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile(Sheet("<xsl:output doctype-public=\"£[~]\" doctype-system=\"s\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>")));

            Assert.AreEqual("XTSE0020", error.Code);
        }
        [TestMethod]
        public void ACommentInAStylesheetDoesNotDivideTheTextAroundIt()
        {
            // A stylesheet is prepared by removing its comments and processing instructions and only then
            // stripping whitespace-only text (XSLT 3.0 §4.2). A tree holds no two adjacent text nodes, so
            // by then what stood on either side of a removed comment is one text node, and whitespace
            // beside something that is not whitespace is kept along with it. Which is the point: writing a
            // comment in the middle of some text changes nothing about the text.
            Assert.AreEqual(
                "<out><e>   h   </e><e>   h   </e></out>",
                Run(
                    Sheet("<xsl:template match=\"/\"><out>"
                        + "<e>   h<!--c-->   </e><e>   <!--c-->h   </e>"
                        + "</out></xsl:template>"),
                    "<r/>"));

            // A processing instruction is removed at the same point and does the same nothing.
            Assert.AreEqual(
                "<out><e>   h</e><e>h   </e></out>",
                Run(
                    Sheet("<xsl:template match=\"/\"><out>"
                        + "<e>   <?pi?>h</e><e>h<?pi?>   </e>"
                        + "</out></xsl:template>"),
                    "<r/>"));

            // An element does divide one run from the next, being a node the preparation leaves standing,
            // so the whitespace on either side of it goes as it always did.
            Assert.AreEqual(
                "<out><e>h<b/></e></out>",
                Run(
                    Sheet("<xsl:template match=\"/\"><out>"
                        + "<e>h<b/>   </e>"
                        + "</out></xsl:template>"),
                    "<r/>"));
        }
    }
}
