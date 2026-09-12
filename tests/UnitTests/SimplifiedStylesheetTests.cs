using System.Xml;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for simplified stylesheets and for <c>system-property()</c>.
    /// </summary>
    [TestClass]
    public sealed class SimplifiedStylesheetTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private static string Run(string stylesheet, string input)
        {
            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
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
                XmlComparison.Normalize(output.ToString()),
                XmlComparison.Normalize(Run(stylesheet, input)),
                "output differed from XslCompiledTransform");
        }

        // ---- Simplified stylesheets ----------------------------------------------------------------------

        [TestMethod]
        public void TheDocumentElementBecomesTheWholeTemplate()
        {
            AssertMatchesReference(
                $"<html {Xsl} xsl:version=\"1.0\">"
                + "<body><h1><xsl:value-of select=\"/page/title\"/></h1></body>"
                + "</html>",
                "<page><title>Hello</title></page>");
        }

        [TestMethod]
        public void InstructionsWorkInsideASimplifiedStylesheet()
        {
            AssertMatchesReference(
                $"<ul {Xsl} xsl:version=\"1.0\">"
                + "<xsl:for-each select=\"/r/i\">"
                + "<li n=\"{position()}\"><xsl:value-of select=\".\"/></li>"
                + "</xsl:for-each>"
                + "</ul>",
                "<r><i>a</i><i>b</i></r>");
        }

        [TestMethod]
        public void AttributeValueTemplatesWorkOnTheDocumentElementItself()
        {
            AssertMatchesReference(
                $"<page {Xsl} xsl:version=\"1.0\" title=\"{{/r/@t}}\"/>",
                "<r t=\"Report\"/>");
        }

        [TestMethod]
        public void TheVersionAttributeIsNotCopiedToTheResult()
        {
            // xsl:version is a directive to the processor, not part of the result.
            string result = Run($"<out {Xsl} xsl:version=\"1.0\"/>", "<r/>");

            Assert.AreEqual("<out/>", result);
        }

        [TestMethod]
        public void ADocumentElementWithoutVersionIsStillRejected()
        {
            // Without xsl:version this is not a stylesheet at all, and saying so beats transforming to itself.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt("<html><body/></html>"));

            StringAssert.Contains(error.Message, "xsl:version");
        }

        [TestMethod]
        public void ASimplifiedStylesheetCanExcludePrefixes()
        {
            string result = Run(
                $"<out {Xsl} xmlns:d=\"urn:demo\" xsl:version=\"1.0\" xsl:exclude-result-prefixes=\"d\">"
                + "<xsl:value-of select=\"/r\"/>"
                + "</out>",
                "<r>text</r>");

            Assert.AreEqual("<out>text</out>", result);
        }

        // ---- system-property() ---------------------------------------------------------------------------

        [TestMethod]
        public void SystemPropertyReportsTheVersion()
        {
            // The processor's version, which is 3.0 for a caller who names none, read as a number because a
            // 1.0 stylesheet is asking.
            Assert.AreEqual(
                "<out>3</out>",
                Run($"<xsl:stylesheet version=\"1.0\" {Xsl}>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"system-property('xsl:version')\"/>"
                    + "</out></xsl:template></xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void TheVersionIsANumberNotAString()
        {
            // Stylesheets branch on this, so it has to compare as a number.
            Assert.AreEqual(
                "<out>true</out>",
                Run($"<xsl:stylesheet version=\"1.0\" {Xsl}>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"system-property('xsl:version') &gt;= 1\"/>"
                    + "</out></xsl:template></xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void SystemPropertyReportsTheVendor()
        {
            Assert.AreEqual(
                "<out>CodeDeeds</out>",
                Run($"<xsl:stylesheet version=\"1.0\" {Xsl}>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"system-property('xsl:vendor')\"/>"
                    + "</out></xsl:template></xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void AnUnknownPropertyIsAnEmptyStringRatherThanAnError()
        {
            // What makes the function usable for probing: asking about something unsupported is safe.
            // Both lookups produce nothing, so the element ends up empty.
            Assert.AreEqual(
                "<out/>",
                Run($"<xsl:stylesheet version=\"1.0\" {Xsl}>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"system-property('xsl:no-such-thing')\"/>"
                    + "<xsl:value-of select=\"system-property('unprefixed')\"/>"
                    + "</out></xsl:template></xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void ThePropertyNameResolvesThroughWhicheverPrefixIsBound()
        {
            // The prefix is arbitrary; only the namespace it resolves to matters.
            Assert.AreEqual(
                "<out>CodeDeeds</out>",
                Run("<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                    + "xmlns:t=\"http://www.w3.org/1999/XSL/Transform\">"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"system-property('t:vendor')\"/>"
                    + "</out></xsl:template></xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void ANonLiteralPropertyNameIsResolvedAtRunTime()
        {
            // Rare, but the argument is an ordinary expression and prefixes must still mean what they meant
            // where the call was written.
            Assert.AreEqual(
                "<out>CodeDeeds</out>",
                Run($"<xsl:stylesheet version=\"1.0\" {Xsl}>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"system-property(concat('xsl',':','vendor'))\"/>"
                    + "</out></xsl:template></xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void SystemPropertyWorksInsideASimplifiedStylesheet()
        {
            Assert.AreEqual(
                "<out>3</out>",
                Run($"<out {Xsl} xsl:version=\"1.0\">"
                    + "<xsl:value-of select=\"system-property('xsl:version')\"/></out>",
                    "<r/>"));
        }

        [TestMethod]
        public void TheVersionReportedIsTheProcessorsAndNotTheStylesheetsOwn()
        {
            // XSLT 3.0 18.2.2 asks for "the version of XSLT implemented by the processor". This engine
            // holds both languages and is told which to be, so one stylesheet gets two answers -- and
            // neither of them is the version the stylesheet writes of itself. That is what a use-when
            // guarding a 3.0 construct is asking, and answering it with 2.0 on a 3.0 processor closes the
            // guard over a construct that would have worked.
            const string sheet =
                "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"system-property('xsl:version')\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>2.0</out>",
                new Xslt(sheet, new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V20 })
                    .TransformXml("<r/>"));

            Assert.AreEqual(
                "<out>3.0</out>",
                new Xslt(sheet, new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                    .TransformXml("<r/>"));

            // And 3.0 is what a caller who names no version gets.
            Assert.AreEqual(
                "<out>3.0</out>",
                new Xslt(sheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));

            // What the stylesheet says of itself decides how the answer is read rather than what it is: a
            // version="1.0" stylesheet gets the same version as a number, that being 1.0's type for it.
            const string legacy =
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"system-property('xsl:version') + 0\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>3</out>",
                new Xslt(legacy, new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                    .TransformXml("<r/>"));
        }
    }
}
