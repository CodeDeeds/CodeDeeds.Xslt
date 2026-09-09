namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:stream-available()</c>, which answers whether a document could be read as a stream.
    /// </summary>
    /// <remarks>
    /// The answer is about the document rather than about the processor. This engine streams nothing — it
    /// builds a tree and transforms that — but the question is whether the document is there and begins as
    /// XML, so that a stylesheet can pick a route before committing to one, and reading stops at the first
    /// element: whatever is wrong further in is past the point a streamed read would have reached before it
    /// had to answer.
    /// </remarks>
    [TestClass]
    public sealed class StreamAvailableTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_documents = new(StringComparer.Ordinal);

            public MapResolver Add(string name, string text)
            {
                m_documents[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_documents.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static string Asks(string expression, IXsltResolver? resolver)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template name=\"xsl:initial-template\"><out>"
                + $"<xsl:value-of select=\"{expression}\" separator=\",\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            string written = new Xslt(
                stylesheet,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    DocumentResolver = resolver,
                }).Transform();

            return written == "<out/>" ? string.Empty : written["<out>".Length..^"</out>".Length];
        }

        [TestMethod]
        public void AvailabilityIsAboutTheDocumentAndNotTheProcessor()
        {
            MapResolver resolver = new MapResolver()
                .Add("good.xml", "<r><a/></r>")
                .Add("prose.txt", "Kirche, Kinder, Kuche");

            Assert.AreEqual(
                "true,false,false",
                Asks(
                    "stream-available('good.xml'), stream-available('prose.txt'),"
                    + " stream-available('absent.xml')",
                    resolver));
        }

        [TestMethod]
        public void ADocumentThatGoesWrongFurtherInIsStillAvailable()
        {
            // Reading stops at the first element, so a document that is well formed for its first mile is
            // available: a streamed read would have started on it and only failed later.
            MapResolver resolver = new MapResolver()
                .Add("truncated.xml", "<?xml version=\"1.0\"?><r><a>half a docum")
                .Add("two-roots.xml", "<a/><b/>")
                .Add("declaration-only.xml", "<?xml version=\"1.0\"?><!DOCTYPE d [<!ELEMENT d (#PCDATA)>]>");

            Assert.AreEqual(
                "true,true,false",
                Asks(
                    "stream-available('truncated.xml'), stream-available('two-roots.xml'),"
                    + " stream-available('declaration-only.xml')",
                    resolver));
        }

        [TestMethod]
        public void NothingIsAvailableWithoutSomewhereToReadItFrom()
        {
            // The same posture as everything else that reads: with no document resolver a stylesheet cannot
            // reach the file system at all, so nothing is there to be streamed.
            Assert.AreEqual("false", Asks("stream-available('good.xml')", null));

            // And no URI at all is not a document either.
            Assert.AreEqual("false", Asks("stream-available(())", new MapResolver().Add("good.xml", "<r/>")));
        }
    }
}
