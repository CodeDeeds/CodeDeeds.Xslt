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

        /// <summary>
        /// Serializes a result with nothing overridden from outside, and gives back the code it was
        /// refused with.
        /// </summary>
        /// <remarks>
        /// Not through <see cref="Run"/>, which omits the XML declaration on the caller's behalf: a host
        /// application that does that is overruling the stylesheet rather than contradicting it, and the
        /// serializer is written to tell the two apart.
        /// </remarks>
        private static string RefusedBy(string output, string body = "<a/>")
        {
            return Assert.ThrowsExactly<XsltException>(() => Serializes(output, body)).Code ?? string.Empty;
        }

        /// <summary>Serializes a result with nothing overridden from outside.</summary>
        private static string Serializes(string output, string body = "<a/>")
        {
            return new Xslt(
                "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + output + "<xsl:template match=\"/\">" + body + "</xsl:template></xsl:stylesheet>",
                new XsltOptions()).TransformXml("<r/>");
        }

        /// <summary>Serializes a 3.0 result, for the output attributes 3.0 added.</summary>
        private static string SerializesAt30(string output, string body)
        {
            return new Xslt(
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + output + "<xsl:template match=\"/\">" + body + "</xsl:template></xsl:stylesheet>",
                new XsltOptions()).TransformXml("<r/>");
        }

        [TestMethod]
        public void ACharacterHtmlCannotCarryIsRefusedBelowVersionFive()
        {
            // Serialization §7.2: the control characters #x7F to #x9F are permitted in XML and in no
            // version of HTML before 5, and "it is a serialization error [err:SERE0014] to use the HTML
            // output method if such characters appear in the instance of the data model and the value of
            // the requested HTML version is less than 5.0. The serializer MUST signal the error." A
            // character reference is not a way round it: the error is about the character being in the
            // result at all.
            Assert.AreEqual(
                "SERE0014",
                RefusedBy("<xsl:output method=\"html\"/>", "<doc>&#x9f;</doc>"));

            // The requested HTML version is html-version where written and version otherwise (§7.4.1),
            // so either spelling of 5 lifts it.
            Assert.Contains(
                "&#159;",
                SerializesAt30("<xsl:output method=\"html\" html-version=\"5\"/>", "<doc>&#x9f;</doc>"));
            Assert.Contains(
                "&#159;",
                SerializesAt30("<xsl:output method=\"html\" version=\"5.0\"/>", "<doc>&#x9f;</doc>"));

            // The rule names the HTML output method. The XHTML method is defined in terms of the XML
            // method, which permits the character, and writes the reference this engine writes for a
            // control nobody agrees the meaning of.
            Assert.Contains(
                "&#159;",
                Serializes("<xsl:output method=\"xhtml\"/>", "<doc>&#x9f;</doc>"));
        }

        [TestMethod]
        public void OnlyAnElementInNoNamespaceIsAnHtmlElementBeforeVersionFive()
        {
            // §7.2: "An element node is serialized as an HTML element if the expanded QName of the
            // element has a null namespace URI, regardless of the value of the requested HTML version, or
            // the value of the requested HTML version is 5.0 or greater, and the element node is in the
            // XHTML namespace." What is not serialized as an HTML element is an XML island, written the way
            // XML writes it — so a br in no namespace has no end tag, and one in the XHTML namespace
            // has one until the version says 5.
            const string Xhtml = "xmlns=\"http://www.w3.org/1999/xhtml\"";

            Assert.AreEqual("<br>", Serializes("<xsl:output method=\"html\"/>", "<br/>"));
            Assert.AreEqual(
                $"<br {Xhtml}></br>",
                Serializes("<xsl:output method=\"html\"/>", $"<br {Xhtml}/>"));
            Assert.AreEqual(
                $"<br {Xhtml}>",
                SerializesAt30("<xsl:output method=\"html\" html-version=\"5\"/>", $"<br {Xhtml}/>"));
        }

        [TestMethod]
        public void SerializationParametersThatCannotBeHonouredTogetherAreRefused()
        {
            // Every one of these is a "the serializer MUST signal the error" in the Serialization
            // specification, so none of them is something to recover from. The suite asks for each with
            // assert-serialization-error, in decl/output.

            // §5.1.8: XML 1.0 has no syntax for undeclaring a prefix, so the two cannot both be had.
            Assert.AreEqual(
                "SEPM0010",
                RefusedBy("<xsl:output method=\"xml\" undeclare-prefixes=\"yes\" version=\"1.0\"/>"));

            // §5.1.6: a standalone document declaration is part of the XML declaration, so omitting
            // the declaration leaves nowhere to say it.
            Assert.AreEqual(
                "SEPM0009",
                RefusedBy("<xsl:output method=\"xml\" omit-xml-declaration=\"yes\" standalone=\"yes\"/>"));

            // §5.1.6 again, from the other side: both a doctype-system and a standalone describe a
            // document with one element and nothing else beside it.
            Assert.AreEqual(
                "SEPM0004",
                RefusedBy("<xsl:output method=\"xml\" standalone=\"yes\"/>", "<a/><b/>"));
            Assert.AreEqual(
                "SEPM0004",
                RefusedBy(
                    "<xsl:output method=\"xml\" doctype-system=\"x.dtd\"/>",
                    "<xsl:text>x</xsl:text><a/>"));

            // §5.1.3 and §8.1.3: an encoding nothing here can produce, whether or not the
            // destination is one that takes bytes.
            Assert.AreEqual("SESU0007", RefusedBy("<xsl:output encoding=\"XXX-xx\"/>"));
            Assert.AreEqual("SESU0007", RefusedBy("<xsl:output method=\"text\" encoding=\"XXX-xx\"/>"));

            // §5.1.1 and §7.4.1: a version of XML, or of HTML, this serializer does not write.
            Assert.AreEqual("SESU0013", RefusedBy("<xsl:output method=\"xml\" version=\"2.0\"/>"));
            Assert.AreEqual("SESU0013", RefusedBy("<xsl:output method=\"html\" version=\"0.0\"/>"));

            // §5.1.9: a normalization form it does not apply.
            Assert.AreEqual(
                "SESU0011", RefusedBy("<xsl:output normalization-form=\"fully-normalized\"/>"));

            // §7.2: the HTML method ends a processing instruction with '>' rather than '?>', and
            // there is no escaping inside one.
            Assert.AreEqual(
                "SERE0015",
                RefusedBy(
                    "<xsl:output method=\"html\"/>",
                    "<html><xsl:processing-instruction name=\"p\">a&gt;b</xsl:processing-instruction></html>"));
        }

        [TestMethod]
        public void TheSameParametersAreHonouredWhereTheyCanBe()
        {
            // Undeclaring a prefix is only impossible in XML 1.0.
            Assert.AreEqual(
                "<?xml version=\"1.1\" encoding=\"UTF-8\"?><a/>",
                Serializes("<xsl:output method=\"xml\" undeclare-prefixes=\"yes\" version=\"1.1\"/>"));

            // A result that named no version has not thereby asked for HTML 1.0, which is the whole reason
            // the serializer keeps whether the version was named at all.
            Assert.AreEqual("<html></html>", Serializes("<xsl:output method=\"html\"/>", "<html/>"));

            // And one element with nothing beside it is what a standalone describes.
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><a/>",
                Serializes("<xsl:output method=\"xml\" standalone=\"yes\"/>"));
        }

        [TestMethod]
        public void ALineEndingIsWrittenAsAReferenceSoThatItSurvivesBeingParsedBack()
        {
            // Serialization §5: "CR, NEL and LINE SEPARATOR characters in text nodes MUST be output
            // respectively as &#xD;, &#x85;, and &#x2028;, or their equivalents; while CR, NL, TAB, NEL and
            // LINE SEPARATOR characters in attribute nodes MUST be output respectively as &#xD;, &#xA;,
            // &#x9;, &#x85;, and &#x2028;, or their equivalents." A parser folds each of them into a line
            // feed, so a literal one is a character the result would not come back with.
            string result = Run(
                Sheet(
                    "<xsl:template match=\"/\"><out a=\"{/r}\"><xsl:value-of select=\"/r\"/></out>"
                    + "</xsl:template>"),
                "<r>a&#xD;b&#x85;c&#x2028;d</r>");

            Assert.AreEqual(
                "<out a=\"a&#xD;b&#x85;c&#x2028;d\">a&#xD;b&#x85;c&#x2028;d</out>", result);

            // The round trip is what it is for.
            Assert.AreEqual("a\rb\u0085c\u2028d", XDocument.Parse(result).Root!.Value);
        }

        [TestMethod]
        public void ACdataSectionStopsForALineEndingItCannotProtect()
        {
            // A CDATA section keeps text from being read as markup; it does not keep it from line ending
            // normalization, which happens to every character of a document alike. So the section stops for
            // one and starts again after, as it does for the terminator and for a character the encoding
            // cannot carry.
            string result = Run(
                Sheet(
                    "<xsl:output cdata-section-elements=\"d\"/>"
                    + "<xsl:template match=\"/\"><d><xsl:value-of select=\"/r\"/></d></xsl:template>"),
                "<r>a&#xD;b</r>");

            Assert.AreEqual("<d><![CDATA[a]]>&#xD;<![CDATA[b]]></d>", result);
            // A whitespace-only text node is one XDocument drops unless told not to, and the carriage
            // return between the two sections is exactly that.
            Assert.AreEqual(
                "a\rb", XDocument.Parse(result, LoadOptions.PreserveWhitespace).Root!.Value);
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
            // change where the link points, and the markup escaping still protects what markup needs. The
            // quotation mark is written as a numeric reference, which is what the suite's output-0102c and
            // 0103c ask for and what the characters beside it get.
            Assert.AreEqual(
                "<a xmlns=\"http://www.w3.org/1999/xhtml\" href=\"a b %20 &#34; &amp; ~\"></a>",
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
