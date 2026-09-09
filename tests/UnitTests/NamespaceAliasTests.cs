using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:namespace-alias</c> and the <c>xsl:output</c> attributes that describe the result
    /// rather than shape it.
    /// </summary>
    [TestClass]
    public sealed class NamespaceAliasTests
    {
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_modules = new(StringComparer.Ordinal);

            public MapResolver Add(string name, string text)
            {
                m_modules[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_modules.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static string Run(string stylesheet, string input, IXsltResolver? resolver = null)
        {
            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                StylesheetResolver = resolver,
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

        // ---- xsl:namespace-alias -------------------------------------------------------------------------

        /// <summary>
        /// The reason the feature exists: a stylesheet that writes a stylesheet. A literal
        /// <c>&lt;xsl:template&gt;</c> would be obeyed rather than copied, so it is written under a stand-in
        /// namespace and aliased back on the way out.
        /// </summary>
        [TestMethod]
        public void AStylesheetCanGenerateAStylesheet()
        {
            AssertMatchesReference(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:out=\"urn:placeholder\">"
                + "<xsl:namespace-alias stylesheet-prefix=\"out\" result-prefix=\"xsl\"/>"
                + "<xsl:template match=\"/\">"
                + "<out:stylesheet version=\"1.0\">"
                + "<out:template match=\"{/r/@match}\"><hit/></out:template>"
                + "</out:stylesheet>"
                + "</xsl:template>"
                + "</xsl:stylesheet>",
                "<r match=\"item\"/>");
        }

        [TestMethod]
        public void TheStandInNamespaceDoesNotAppearInTheResult()
        {
            string result = Run(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:out=\"urn:placeholder\">"
                + "<xsl:namespace-alias stylesheet-prefix=\"out\" result-prefix=\"xsl\"/>"
                + "<xsl:template match=\"/\"><out:thing/></xsl:template>"
                + "</xsl:stylesheet>",
                "<r/>");

            Assert.DoesNotContain("urn:placeholder", result, "the stand-in namespace must be aliased away");
            StringAssert.Contains(result, "http://www.w3.org/1999/XSL/Transform");
        }

        [TestMethod]
        public void AliasingAppliesToAttributesToo()
        {
            AssertMatchesReference(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:a=\"urn:from\" xmlns:b=\"urn:to\">"
                + "<xsl:namespace-alias stylesheet-prefix=\"a\" result-prefix=\"b\"/>"
                + "<xsl:template match=\"/\"><e a:marked=\"yes\" plain=\"1\"/></xsl:template>"
                + "</xsl:stylesheet>",
                "<r/>");
        }

        [TestMethod]
        public void AliasingCanTargetTheDefaultNamespace()
        {
            string result = Run(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:s=\"urn:from\" xmlns=\"urn:to\">"
                + "<xsl:namespace-alias stylesheet-prefix=\"s\" result-prefix=\"#default\"/>"
                + "<xsl:template match=\"/\"><s:e/></xsl:template>"
                + "</xsl:stylesheet>",
                "<r/>");

            Assert.DoesNotContain("urn:from", result);
            StringAssert.Contains(result, "urn:to");
        }

        [TestMethod]
        public void AliasesAreOverriddenByTheImportingStylesheet()
        {
            MapResolver resolver = new MapResolver().Add(
                "base.xsl",
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:s=\"urn:from\" xmlns:low=\"urn:imported\">"
                + "<xsl:namespace-alias stylesheet-prefix=\"s\" result-prefix=\"low\"/>"
                + "</xsl:stylesheet>");

            string result = Run(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:s=\"urn:from\" xmlns:high=\"urn:importing\">"
                + "<xsl:import href=\"base.xsl\"/>"
                + "<xsl:namespace-alias stylesheet-prefix=\"s\" result-prefix=\"high\"/>"
                + "<xsl:template match=\"/\"><s:e/></xsl:template>"
                + "</xsl:stylesheet>",
                "<r/>",
                resolver);

            StringAssert.Contains(result, "urn:importing");
            Assert.DoesNotContain("urn:imported", result);
        }

        [TestMethod]
        public void AnUnboundAliasPrefixIsReported()
        {
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(() => new Xslt(
                    "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                    + "<xsl:namespace-alias stylesheet-prefix=\"nope\" result-prefix=\"xsl\"/>"
                    + "<xsl:template match=\"/\"><e/></xsl:template>"
                    + "</xsl:stylesheet>")).Message,
                "nope");
        }

        [TestMethod]
        public void WithoutAnAliasNothingIsRewritten()
        {
            AssertMatchesReference(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:a=\"urn:kept\">"
                + "<xsl:template match=\"/\"><a:e a:x=\"1\"/></xsl:template>"
                + "</xsl:stylesheet>",
                "<r/>");
        }

        // ---- xsl:output standalone and media-type --------------------------------------------------------

        private static string Sheet(string body)
        {
            return "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + body
                + "</xsl:stylesheet>";
        }

        [TestMethod]
        public void StandaloneAppearsInTheXmlDeclaration()
        {
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><a/>",
                new Xslt(Sheet("<xsl:output standalone=\"yes\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>")).TransformXml("<r/>"));

            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?><a/>",
                new Xslt(Sheet("<xsl:output standalone=\"no\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>")).TransformXml("<r/>"));
        }

        [TestMethod]
        public void StandaloneIsLeftOutWhenTheStylesheetDoesNotAskForIt()
        {
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><a/>",
                new Xslt(Sheet("<xsl:template match=\"/\"><a/></xsl:template>")).TransformXml("<r/>"));
        }

        [TestMethod]
        public void StandaloneIsSuppressedAlongWithTheDeclaration()
        {
            Assert.AreEqual(
                "<a/>",
                new Xslt(
                    Sheet("<xsl:output standalone=\"yes\"/><xsl:template match=\"/\"><a/></xsl:template>"),
                    new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }

        [TestMethod]
        public void MediaTypeIsReportedForTheCallerToUse()
        {
            // The stylesheet is the only thing that knows this; it exists to be put in a Content-Type header.
            Assert.AreEqual(
                "application/xhtml+xml",
                new Xslt(Sheet("<xsl:output media-type=\"application/xhtml+xml\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>")).OutputMediaType);
        }

        [TestMethod]
        public void MediaTypeFallsBackToTheOneImpliedByTheMethod()
        {
            Assert.AreEqual(
                "text/xml",
                new Xslt(Sheet("<xsl:template match=\"/\"><a/></xsl:template>")).OutputMediaType);

            Assert.AreEqual(
                "text/html",
                new Xslt(Sheet("<xsl:output method=\"html\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>")).OutputMediaType);

            Assert.AreEqual(
                "text/plain",
                new Xslt(Sheet("<xsl:output method=\"text\"/>"
                    + "<xsl:template match=\"/\"><a/></xsl:template>")).OutputMediaType);
        }

        [TestMethod]
        public void OutputMethodAndEncodingAreReported()
        {
            Xslt stylesheet = new Xslt(Sheet("<xsl:output method=\"html\" encoding=\"iso-8859-1\"/>"
                + "<xsl:template match=\"/\"><a/></xsl:template>"));

            Assert.AreEqual(Runtime.OutputMethod.Html, stylesheet.OutputMethod);
            Assert.AreEqual("iso-8859-1", stylesheet.OutputEncoding);
        }
    }
}
