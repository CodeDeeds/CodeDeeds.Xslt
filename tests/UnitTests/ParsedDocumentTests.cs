using CodeDeeds.Xslt;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for a resolver handing back a document already parsed, which <c>document()</c> and
    /// <c>doc()</c> then read without parsing it again.
    /// </summary>
    /// <remarks>
    /// The DocBook stylesheets read their localization file and their title-page templates for every
    /// document, 1.8 milliseconds of the 10.8 a short one took; a caller whose documents do not change
    /// can parse each once with <see cref="Xslt.ParseDocument(TextReader, string)"/> and give the tree
    /// back through <see cref="ResolvedResource(XdmTree, string)"/>. See <c>ConformanceNotes.md</c>,
    /// "A document parsed once and read by every transformation".
    /// </remarks>
    [TestClass]
    public sealed class ParsedDocumentTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"#all\">";

        /// <summary>A resolver of texts, or of trees where it was given one, counting what it was asked.</summary>
        private sealed class Documents : IXsltResolver
        {
            private readonly Dictionary<string, string> m_texts = new(StringComparer.Ordinal);
            private readonly Dictionary<string, XdmTree> m_trees = new(StringComparer.Ordinal);

            public int Asked { get; private set; }

            public Documents Text(string name, string text)
            {
                m_texts[name] = text;
                return this;
            }

            public Documents Tree(string name, XdmTree tree)
            {
                m_trees[name] = tree;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                Asked++;

                return m_trees.TryGetValue(href, out XdmTree? tree) ? new ResolvedResource(tree, "urn:" + href)
                    : m_texts.TryGetValue(href, out string? text) ? new ResolvedResource(new StringReader(text), "urn:" + href)
                    : null;
            }
        }

        private static Xslt Compiled(string body, IXsltResolver? documents, XsltBackend backend = XsltBackend.Interpreted)
        {
            return new Xslt(
                Head + body + "</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Backend = backend,
                    DocumentResolver = documents,
                    StylesheetResolver = documents,
                });
        }

        /// <summary>What a stylesheet writes of a document, on both backends, which have to agree.</summary>
        private static string Run(string body, IXsltResolver? documents, string input = "<r/>")
        {
            string interpreted = Compiled(body, documents, XsltBackend.Interpreted).TransformXml(input);
            string compiled = Compiled(body, documents, XsltBackend.Compiled).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "The backends disagree.");
            return interpreted;
        }

        [TestMethod]
        public void ADocumentGivenParsedIsTheDocumentDocReads()
        {
            // The same answer from a tree as from the text it was parsed from, and the tree's nodes are
            // the ones read: the base URI is the tree's own, and a second transformation reads the same
            // tree again.
            string body =
                "<xsl:template match=\"/\"><out n=\"{count(doc('d.xml')//x)}\" base=\"{base-uri(doc('d.xml'))}\">"
                + "<xsl:value-of select=\"doc('d.xml')/r/x[2]\"/></out></xsl:template>";
            string text = "<r><x>one</x><x>two</x></r>";

            Documents texts = new Documents().Text("d.xml", text);
            string fromText = Run(body, texts);

            Xslt parser = Compiled(body, null);
            XdmTree tree = parser.ParseDocument(text, "urn:d.xml");
            Documents trees = new Documents().Tree("d.xml", tree);

            Assert.AreEqual("<out n=\"2\" base=\"urn:d.xml\">two</out>", fromText);
            Assert.AreEqual(fromText, Run(body, trees));
            Assert.AreEqual(fromText, Compiled(body, trees).TransformXml("<r/>"));
            Assert.AreEqual(fromText, Compiled(body, trees).TransformXml("<r/>"));
        }

        [TestMethod]
        public void AParsedDocumentIsReadTheWayTheStylesheetReads()
        {
            // Xslt.ParseDocument applies the stylesheet's whitespace stripping, so the tree is what
            // document() would have built from the text; a tree parsed any other way is taken as it is.
            string body =
                "<xsl:strip-space elements=\"r\"/>"
                + "<xsl:template match=\"/\"><out n=\"{count(doc('d.xml')/r/node())}\"/></xsl:template>";
            string text = "<r> <x/> <x/> </r>";

            Assert.AreEqual("<out n=\"2\"/>", Run(body, new Documents().Text("d.xml", text)));

            Xslt stylesheet = Compiled(body, null);
            XdmTree stripped = stylesheet.ParseDocument(text, "urn:d.xml");
            XdmTree kept = XdmTreeBuilder.FromXml(new StringReader(text), baseUri: "urn:d.xml");

            Assert.AreEqual("<out n=\"2\"/>", Run(body, new Documents().Tree("d.xml", stripped)));
            Assert.AreEqual("<out n=\"5\"/>", Run(body, new Documents().Tree("d.xml", kept)));
        }

        [TestMethod]
        public void OneParsedDocumentServesManyTransformationsAtOnce()
        {
            // The tree is never changed by a transformation, so one may be read by any number at once,
            // each applying templates to it and matching them by name in a table of its own.
            string body =
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"doc('d.xml')/r/*\"/></out></xsl:template>"
                + "<xsl:template match=\"x\"><xsl:value-of select=\"@n\"/>,</xsl:template>";
            Xslt parser = Compiled(body, null);
            XdmTree tree = parser.ParseDocument(
                "<r>" + string.Concat(Enumerable.Range(1, 40).Select(n => $"<x n=\"{n}\"/>")) + "</r>", "urn:d.xml");
            Xslt transformation = Compiled(body, new Documents().Tree("d.xml", tree));
            string expected = "<out>" + string.Concat(Enumerable.Range(1, 40).Select(n => n + ",")) + "</out>";

            string[] results = new string[16];
            Parallel.For(0, results.Length, i => results[i] = transformation.TransformXml("<r/>"));

            foreach (string result in results)
            {
                Assert.AreEqual(expected, result);
            }
        }

        [TestMethod]
        public void DocAvailableSeesAParsedDocument()
        {
            string body =
                "<xsl:template match=\"/\"><out a=\"{doc-available('d.xml')}\" b=\"{doc-available('none.xml')}\"/></xsl:template>";
            Xslt parser = Compiled(body, null);

            Assert.AreEqual(
                "<out a=\"true\" b=\"false\"/>",
                Run(body, new Documents().Tree("d.xml", parser.ParseDocument("<r/>", "urn:d.xml"))));
        }

        [TestMethod]
        public void AStaticExpressionReadsAParsedDocument()
        {
            // A static variable is evaluated while compiling, through the compiler's own reading of a
            // document, which takes a tree the same way.
            string body =
                "<xsl:variable name=\"n\" static=\"yes\" select=\"count(doc('d.xml')//x)\"/>"
                + "<xsl:template match=\"/\"><out n=\"{$n}\"/></xsl:template>";
            Xslt parser = Compiled("<xsl:template match=\"/\"/>", null);

            Assert.AreEqual(
                "<out n=\"3\"/>",
                Run(body, new Documents().Tree("d.xml", parser.ParseDocument("<r><x/><x/><x/></r>", "urn:d.xml"))));
        }

        [TestMethod]
        public void AResourceGivenParsedIsRefusedWhereTextIsRead()
        {
            // Only document() and doc() read a tree. unparsed-text() reads text and a stylesheet module is
            // compiled from text, and a resolver that gives a tree for either is told so, by the resource.
            Xslt parser = Compiled("<xsl:template match=\"/\"/>", null);
            XdmTree tree = parser.ParseDocument("<r/>", "urn:d.xml");

            XsltException text = Assert.ThrowsExactly<XsltException>(() => Compiled(
                "<xsl:template match=\"/\"><out><xsl:value-of select=\"unparsed-text('d.xml')\"/></out></xsl:template>",
                new Documents().Tree("d.xml", tree)).TransformXml("<r/>"));
            StringAssert.Contains(text.Message, "already parsed");

            XsltException module = Assert.ThrowsExactly<XsltException>(() => Compiled(
                "<xsl:include href=\"d.xml\"/><xsl:template match=\"/\"/>",
                new Documents().Tree("d.xml", tree)));
            StringAssert.Contains(module.Message, "already parsed");

            XsltException reader = Assert.ThrowsExactly<XsltException>(() => new ResolvedResource(tree, "urn:d.xml").Reader);
            StringAssert.Contains(reader.Message, "urn:d.xml");
            Assert.AreSame(tree, new ResolvedResource(tree, "urn:d.xml").Document);
            Assert.IsNull(new ResolvedResource(new StringReader("<r/>"), "urn:t.xml").Document);
        }

        [TestMethod]
        public void TwoSpellingsOfOneParsedDocumentAreOneDocument()
        {
            // Resolved to the same URI, two references read one tree, so a node of the one is a node of
            // the other: what the resolver gave is the identity.
            string body =
                "<xsl:template match=\"/\"><out same=\"{doc('d.xml')/r is doc('./d.xml')/r}\"/></xsl:template>";
            Xslt parser = Compiled(body, null);
            XdmTree tree = parser.ParseDocument("<r/>", "urn:d.xml");
            Documents documents = new Documents().Tree("d.xml", tree).Tree("./d.xml", tree);

            Assert.AreEqual("<out same=\"true\"/>", Run(body, documents));
        }

        [TestMethod]
        public void AParsedDocumentCostsNoParse()
        {
            // A document of two thousand elements read by every transformation: parsed each time it is
            // most of what the transformation allocates, and given parsed it is a mapping of the
            // stylesheet's names, a few hundred bytes.
            string body = "<xsl:template match=\"/\"><out n=\"{count(doc('d.xml')//x)}\"/></xsl:template>";
            string text = "<r>" + string.Concat(Enumerable.Repeat("<x a=\"1\">text</x>", 2000)) + "</r>";
            Xslt parsing = Compiled(body, new Documents().Text("d.xml", text));
            Xslt parser = Compiled(body, null);
            Xslt keeping = Compiled(body, new Documents().Tree("d.xml", parser.ParseDocument(text, "urn:d.xml")));

            Assert.AreEqual("<out n=\"2000\"/>", parsing.TransformXml("<r/>"));
            Assert.AreEqual("<out n=\"2000\"/>", keeping.TransformXml("<r/>"));

            long parsed = Least(parsing);
            long kept = Least(keeping);

            Assert.IsTrue(
                kept * 10 < parsed,
                $"Reading the document parsed allocated {kept / 1024:N0} KB against {parsed / 1024:N0} parsing it.");
        }

        private static long Least(Xslt transformation)
        {
            long least = long.MaxValue;

            for (int i = 0; i < 12; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                transformation.TransformXml("<r/>", TextWriter.Null);
                least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            return least;
        }
    }
}
