namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:transform()</c>, which runs a second transformation and hands back a map of what it
    /// produced: the delivery format that decides what each result document is, the key each goes under, and
    /// the codes for a stylesheet that cannot be found and an option that is not one.
    /// </summary>
    [TestClass]
    public sealed class TransformFunctionTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Serves stylesheets from a dictionary, under absolute URIs so the keys are settled.</summary>
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_modules = new(StringComparer.Ordinal);

            public MapResolver Add(string uri, string stylesheet)
            {
                m_modules[uri] = stylesheet;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_modules.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static string Sheet(string body)
        {
            return $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + "<xsl:template name=\"xsl:initial-template\">" + body + "</xsl:template></xsl:stylesheet>";
        }

        private static string Run(string body, IXsltResolver resolver)
        {
            return new Xslt(
                Sheet(body),
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Version = XsltVersion.V30,
                    StylesheetResolver = resolver,
                }).Transform();
        }

        private static string Refuses(string body, IXsltResolver resolver)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, resolver)).Code ?? string.Empty;
        }

        [TestMethod]
        public void ATransformationIsRunAndComesBackAsAMap()
        {
            // The principal result goes under the key "output", and by default it is a document node: the
            // result tree the transformation built, which the caller can then navigate.
            MapResolver resolver = new MapResolver().Add(
                "file:///s/double.xsl",
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + "<xsl:template match=\".[. instance of xs:integer]\"><in><xsl:value-of select=\". * 2\"/></in>"
                + "</xsl:template></xsl:stylesheet>");

            Assert.AreEqual(
                "<out><in>84</in></out>",
                Run(
                    "<out><xsl:sequence select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/double.xsl',"
                    + " 'initial-match-selection': 42 })?output\"/></out>",
                    resolver));
        }

        [TestMethod]
        public void TheDeliveryFormatDecidesWhatEachResultIs()
        {
            MapResolver resolver = new MapResolver()
                .Add(
                    "file:///s/answer.xsl",
                    $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\""
                    + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                    + "<xsl:template name=\"xsl:initial-template\"><xsl:sequence select=\"xs:integer(7)\"/>"
                    + "</xsl:template></xsl:stylesheet>")
                .Add(
                    "file:///s/wrapped.xsl",
                    $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                    + "<xsl:template name=\"xsl:initial-template\"><n>7</n></xsl:template></xsl:stylesheet>");

            string Deliver(string module, string format, string read)
            {
                return Run(
                    "<out><xsl:sequence select=\"transform(map {"
                    + $" 'stylesheet-location': 'file:///s/{module}.xsl',"
                    + $" 'delivery-format': '{format}' }})?output{read}\"/></out>",
                    resolver);
            }

            // Raw is the sequence the transformation returned, so an integer arrives as an integer and can
            // be told from the string of it.
            Assert.AreEqual("<out>true</out>", Deliver("answer", "raw", " instance of xs:integer"));

            // A document is what the serializer would have been given: the result tree, which the caller
            // navigates rather than reads.
            Assert.AreEqual("<out>true</out>", Deliver("wrapped", "document", " instance of document-node()"));
            Assert.AreEqual("<out><n>7</n></out>", Deliver("wrapped", "document", string.Empty));

            // And serialized is what the serializer wrote, declaration included, as one string.
            Assert.AreEqual("<out>true</out>", Deliver("wrapped", "serialized", " instance of xs:string"));
            Assert.AreEqual(
                "<out>&lt;?xml version=\"1.0\" encoding=\"UTF-8\"?&gt;&lt;n&gt;7&lt;/n&gt;</out>",
                Deliver("wrapped", "serialized", string.Empty));
        }

        [TestMethod]
        public void AResultDocumentComesBackKeyedByItsUri()
        {
            // A transformation run this way writes nowhere: its result documents are collected and handed
            // back in the map, each under the URI its href resolved to, so no result resolver is needed.
            MapResolver resolver = new MapResolver().Add(
                "file:///s/two.xsl",
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template name=\"xsl:initial-template\">"
                + "<xsl:result-document href=\"aside.xml\"><aside>b</aside></xsl:result-document>"
                + "<main>a</main></xsl:template></xsl:stylesheet>");

            Assert.AreEqual(
                "<out><main>a</main><aside>b</aside></out>",
                Run(
                    "<xsl:variable name=\"r\" select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/two.xsl' })\"/>"
                    + "<out><xsl:sequence select=\"$r?output\"/>"
                    + "<xsl:sequence select=\"$r('file:///s/aside.xml')\"/></out>",
                    resolver));
        }

        [TestMethod]
        public void WhatCannotBeRunIsReportedAsItsOwnKindOfFailure()
        {
            MapResolver resolver = new MapResolver();

            // A stylesheet the resolver will not supply is a retrieval failure and nothing more.
            Assert.AreEqual(
                "FOXT0002",
                Refuses(
                    "<out><xsl:sequence select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/absent.xsl' })?output\"/></out>",
                    resolver));

            // An option that is not one, and no stylesheet named at all, are the caller's mistake.
            Assert.AreEqual(
                "FOXT0004",
                Refuses(
                    "<out><xsl:sequence select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/absent.xsl', 'stylesheet-locator': 'x' })?output\"/></out>",
                    resolver));

            Assert.AreEqual(
                "FOXT0004",
                Refuses(
                    "<out><xsl:sequence select=\"transform(map { 'delivery-format': 'raw' })?output\"/></out>",
                    resolver));

            // An entry point this engine has no way to start at is refused as a transformation it cannot
            // carry out, rather than run as though the option had not been written.
            Assert.AreEqual(
                "FOXT0001",
                Refuses(
                    "<out><xsl:sequence select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/absent.xsl',"
                    + " 'initial-function': QName('', 'f') })?output\"/></out>",
                    resolver));
        }

        [TestMethod]
        public void AFunctionItemCarriesTheTransformationItWasMadeIn()
        {
            // What raw delivery may hand back is a function item, and calling it happens after the
            // transformation that made it has ended — so the item carries that transformation with it, or
            // the body could not be run at all.
            MapResolver resolver = new MapResolver().Add(
                "file:///s/negative.xsl",
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\" xmlns:f=\"urn:f\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs f\">"
                + "<xsl:template name=\"xsl:initial-template\"><xsl:sequence select=\"f:negative#1\"/>"
                + "</xsl:template>"
                + "<xsl:function name=\"f:negative\" as=\"xs:boolean\"><xsl:param name=\"in\" as=\"xs:integer\"/>"
                + "<xsl:sequence select=\"$in lt 0\"/></xsl:function></xsl:stylesheet>");

            Assert.AreEqual(
                "<out>true false</out>",
                Run(
                    "<xsl:variable name=\"f\" select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/negative.xsl',"
                    + " 'delivery-format': 'raw' })?output\"/>"
                    + "<out><xsl:value-of select=\"$f(-1), $f(1)\"/></out>",
                    resolver));
        }
    }
}
