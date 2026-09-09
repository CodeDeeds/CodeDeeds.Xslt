using System.Xml;
using CodeDeeds.Xslt.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what a document type declaration gives the data model: entities expanded and defaults
    /// supplied, attributes typed ID for <c>id()</c> and for patterns anchored on it, IDREF for
    /// <c>idref()</c>, unparsed entities for <c>unparsed-entity-uri()</c>, element content whitespace
    /// excluded — and what is read outside the document, which is nothing without a resolver and what the
    /// resolver allows with one.
    /// </summary>
    [TestClass]
    public sealed class DocumentTypeTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Serves entities and documents by name, and remembers what was asked for.</summary>
        private sealed class Entities : IXsltResolver
        {
            private readonly Dictionary<string, string> m_texts = new(StringComparer.Ordinal);

            public List<(string Href, string? Base)> Asked { get; } = new();

            public Entities Add(string name, string text)
            {
                m_texts[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                Asked.Add((href, baseUri));
                string identity = new Uri(new Uri(baseUri ?? "http://example.org/in/"), href).AbsoluteUri;
                return m_texts.TryGetValue(href, out string? text) ? new ResolvedResource(new StringReader(text), identity) : null;
            }
        }

        private const string Declared =
            "<!DOCTYPE doc [\n"
            + "<!ELEMENT doc (#PCDATA|item)*>\n"
            + "<!ATTLIST item key ID #IMPLIED kind CDATA \"plain\" see IDREFS #IMPLIED>\n"
            + "<!ENTITY greet \"hello\">\n"
            + "<!NOTATION gif SYSTEM \"image/gif\">\n"
            + "<!ENTITY pic SYSTEM \"pic.gif\" NDATA gif>\n"
            + "<!ENTITY pub PUBLIC \"-//X//Y  Z//EN\" \"y.gif\" NDATA gif>\n"
            + "]>\n"
            + "<doc><item key=\"a\">&greet;</item><item key=\"b\" xml:id=\"c\" see=\"a\"/><item key=\"a\" see=\"b c\"/></doc>";

        private static string Sheet(string body, string declarations = "", string version = "3.0")
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" xmlns:f=\"urn:f\" exclude-result-prefixes=\"f\">"
                + declarations + "<xsl:template match=\"/\"><out>" + body + "</out></xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, string input, XsltOptions? options = null)
        {
            return new Xslt(stylesheet, options ?? new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 }).TransformXml(input);
        }

        private static string Refuses(string stylesheet, string input, XsltOptions? options = null)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(stylesheet, input, options)).Code ?? string.Empty;
        }

        private static string Value(string expression, string input = Declared, XsltOptions? options = null)
        {
            string result = Run(Sheet($"<xsl:value-of select=\"{expression}\"/>"), input, options);
            return result == "<out/>" ? string.Empty : result[5..^6];
        }

        [TestMethod]
        public void TheDeclarationIsReadAndIsNotANode()
        {
            // The internal subset expands an entity and supplies a default attribute, and the declaration
            // itself leaves no node behind: the document node has the one child it would have had without.
            Assert.AreEqual("hello|plain|1", Value("concat(//item[1], '|', //item[1]/@kind, '|', count(/node()))"));
        }

        [TestMethod]
        public void IdAnswersForWhatTheDeclarationTypes()
        {
            // 'key' is an ID by the declaration and xml:id is one by its own specification; the two are
            // looked up together. Where two elements claim one ID the first in document order has it.
            Assert.AreEqual("hello", Value("id('a')"));
            Assert.AreEqual("b", Value("id('c')/@key"));
            Assert.AreEqual("2", Value("count(id('c a'))"));
            Assert.AreEqual("1", Value("count(id('a'))"));
            Assert.AreEqual("0", Value("count(id('zz'))"));
            Assert.AreEqual("hello", Value("element-with-id('a')"));
            Assert.AreEqual("0", Value("count(id('plain'))"));
        }

        [TestMethod]
        public void APatternMayBeAnchoredOnId()
        {
            // id('b') names the second item and nothing else; id('a')/@key is the first item's attribute
            // only, the third's being a duplicate ID and so not what id('a') selects.
            string sheet =
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item|//item/@key\"/></out></xsl:template>"
                + "<xsl:template match=\"id('b')\"><b/></xsl:template>"
                + "<xsl:template match=\"item\"><i/></xsl:template>"
                + "<xsl:template match=\"id('a')/@key\"><ak/></xsl:template>"
                + "<xsl:template match=\"@key\"><k/></xsl:template>"
                + "</xsl:stylesheet>";

            Assert.AreEqual("<out><i/><ak/><b/><k/><i/><k/></out>", Run(sheet, Declared));

            // XSLT 3.0 lets the pattern name the document it searches; 2.0 does not (XTSE0340).
            string twoArguments =
                "<xsl:template match=\"/\"><xsl:variable name=\"d\" select=\"/\"/><out><xsl:apply-templates select=\"//item\"><xsl:with-param name=\"d\" select=\"$d\" tunnel=\"yes\"/></xsl:apply-templates></out></xsl:template>"
                + "<xsl:variable name=\"x\" select=\"/\"/>"
                + "<xsl:template match=\"id('b', $x)\"><b/></xsl:template>"
                + "<xsl:template match=\"item\"><i/></xsl:template>";

            Assert.AreEqual(
                "<out><i/><b/><i/></out>",
                Run($"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">{twoArguments}</xsl:stylesheet>", Declared));
            Assert.AreEqual(
                "XTSE0340",
                Refuses($"<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"{Xsl}\">{twoArguments}</xsl:stylesheet>", Declared, new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V20 }));
        }

        [TestMethod]
        public void IdrefFindsTheReferringAttributes()
        {
            // 'see' is IDREFS by the declaration: the attributes any of whose tokens is the ID asked for,
            // in document order. Each argument is one ID and not a list, so a string that is not an NCName
            // is not an IDREF value and refers to nothing.
            Assert.AreEqual("b", Value("string-join(idref('a')/../@key, '|')"));
            Assert.AreEqual("a", Value("string-join(idref('c')/../@key, '|')"));
            Assert.AreEqual("0", Value("count(idref('a b'))"));
            Assert.AreEqual("0", Value("count(idref('zz'))"));
            Assert.AreEqual("2", Value("count(idref(('a', 'c')))"));
        }

        [TestMethod]
        public void UnparsedEntitiesAreAnswered()
        {
            // The system identifier is resolved against the document's base, the public identifier is
            // given with its whitespace normalized, and an entity that is parsed or not there answers ''.
            XsltOptions options = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30, InputUri = "file:///C:/docs/in.xml" };

            Assert.AreEqual("file:///C:/docs/pic.gif", Value("unparsed-entity-uri('pic')", options: options));
            Assert.AreEqual("file:///C:/docs/y.gif", Value("unparsed-entity-uri('pub')", options: options));
            Assert.AreEqual("-//X//Y Z//EN", Value("unparsed-entity-public-id('pub')", options: options));
            Assert.AreEqual(string.Empty, Value("unparsed-entity-public-id('pic')", options: options));
            Assert.AreEqual(string.Empty, Value("unparsed-entity-uri('greet')", options: options));
            Assert.AreEqual(string.Empty, Value("unparsed-entity-uri('nope')", options: options));

            // With nothing known of where the document came from, the identifier stands as written.
            Assert.AreEqual("pic.gif", Value("unparsed-entity-uri('pic')"));

            // The 3.0 form names the document asked about.
            Assert.AreEqual("file:///C:/docs/pic.gif", Value("unparsed-entity-uri('pic', //item[1])", options: options));
        }

        [TestMethod]
        public void UnparsedEntitiesSurviveACopyOfTheDocument()
        {
            // A copy of a document node keeps its unparsed entities (§5.7.1), whichever way it is copied.
            XsltOptions options = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30, InputUri = "file:///C:/docs/in.xml" };
            string copyOf =
                "<xsl:variable name=\"p\" as=\"document-node()\"><xsl:copy-of select=\".\"/></xsl:variable>"
                + "<xsl:value-of select=\"unparsed-entity-uri('pic', $p)\"/>";
            string copy =
                "<xsl:variable name=\"p\" as=\"document-node()\"><xsl:copy/></xsl:variable>"
                + "<xsl:for-each select=\"$p\"><xsl:value-of select=\"unparsed-entity-uri('pic')\"/></xsl:for-each>";
            string function =
                "<xsl:variable name=\"p\" as=\"document-node()\" select=\"copy-of(.)\"/>"
                + "<xsl:value-of select=\"unparsed-entity-public-id('pub', $p)\"/>";

            Assert.AreEqual("<out>file:///C:/docs/pic.gif</out>", Run(Sheet(copyOf), Declared, options));
            Assert.AreEqual("<out>file:///C:/docs/pic.gif</out>", Run(Sheet(copy), Declared, options));
            Assert.AreEqual("<out>-//X//Y Z//EN</out>", Run(Sheet(function), Declared, options));
        }

        [TestMethod]
        public void TheFunctionsHaveTheirOwnCodesForAMissingContextNode()
        {
            // A stylesheet function has no context node, and each function says so under its own code
            // rather than the general one for a missing focus.
            string declaration(string call) =>
                $"<xsl:function name=\"f:g\"><xsl:sequence select=\"{call}\"/></xsl:function>";

            Assert.AreEqual(
                "XTDE1370",
                Refuses(Sheet("<xsl:value-of select=\"f:g()\"/>", declaration("unparsed-entity-uri('pic')")), Declared));
            Assert.AreEqual(
                "XTDE1380",
                Refuses(Sheet("<xsl:value-of select=\"f:g()\"/>", declaration("unparsed-entity-public-id('pic')")), Declared));
        }

        [TestMethod]
        public void NothingOutsideTheDocumentIsReadWithoutAResolver()
        {
            // The oldest trick played on XML parsers: an entity naming a local file. With no resolver it
            // expands to nothing and no file is read; an external subset is left unread the same way, so
            // an ID it would have declared is not one, and the document still parses.
            string entity = "<!DOCTYPE r [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><r>&x;</r>";
            string subset = "<!DOCTYPE r SYSTEM \"http://127.0.0.1:1/r.dtd\"><r a=\"q\"/>";

            Assert.AreEqual("0", Value("string-length(/r)", entity));
            Assert.AreEqual("0", Value("count(id('q'))", subset));
            Assert.AreEqual(string.Empty, Value("unparsed-entity-uri('pic')", subset));
        }

        [TestMethod]
        public void AResolverReadsTheExternalSubsetAndWhatItDeclares()
        {
            // With a resolver the external subset is read: an attribute list assembled from parameter
            // entities, a conditional section included and one ignored, an external general entity's
            // content, and an unparsed entity whose identifier resolves against the subset's own URI. The
            // subset is fetched once, for the reader and for the reading of it both.
            Entities entities = new Entities()
                .Add("r.dtd",
                    "<!ENTITY % common \"key ID #IMPLIED\">\n"
                    + "<!ATTLIST item %common; other CDATA #IMPLIED>\n"
                    + "<![INCLUDE[ <!ENTITY pic SYSTEM \"pic.gif\" NDATA gif> ]]>\n"
                    + "<![IGNORE[ <!ATTLIST item other ID #IMPLIED> ]]>\n"
                    + "<!ENTITY note SYSTEM \"note.txt\">\n"
                    + "<!NOTATION gif SYSTEM \"image/gif\">\n")
                .Add("note.txt", "from outside");
            XsltOptions options = new XsltOptions
            {
                OmitXmlDeclaration = true,
                Version = XsltVersion.V30,
                EntityResolver = entities,
                InputUri = "http://example.org/in/doc.xml",
            };
            string document = "<!DOCTYPE doc SYSTEM \"r.dtd\"><doc><item key=\"a\" other=\"z\">&note;</item></doc>";

            Assert.AreEqual(
                "<out>from outside|1|0|http://example.org/in/pic.gif</out>",
                Run(
                    Sheet("<xsl:value-of select=\"concat(id('a'), '|', count(id('a')), '|', count(id('z')), '|', unparsed-entity-uri('pic'))\"/>"),
                    document,
                    options));
            CollectionAssert.Contains(entities.Asked, ("r.dtd", "http://example.org/in/doc.xml"));
            CollectionAssert.Contains(entities.Asked, ("note.txt", "http://example.org/in/r.dtd"));
            Assert.AreEqual(1, entities.Asked.Count(asked => asked.Href == "r.dtd"));
        }

        [TestMethod]
        public void AnElementFromAnExternalEntityHasTheEntitysBase()
        {
            // The base URI of an element parsed out of an external entity is the entity's, and of one
            // parsed out of the document is the document's (XDM §6.2.3).
            Entities entities = new Entities().Add("dir/data.xml", "<inner><deep/></inner>");
            XsltOptions options = new XsltOptions
            {
                OmitXmlDeclaration = true,
                Version = XsltVersion.V30,
                EntityResolver = entities,
                InputUri = "http://example.org/in/doc.xml",
            };
            string document = "<!DOCTYPE doc [<!ENTITY e SYSTEM \"dir/data.xml\">]><doc><chap>&e;</chap><chap/></doc>";

            Assert.AreEqual(
                "<out>http://example.org/in/dir/data.xml|http://example.org/in/dir/data.xml|http://example.org/in/doc.xml</out>",
                Run(
                    Sheet("<xsl:value-of select=\"concat(base-uri(//inner), '|', base-uri(//deep), '|', base-uri(//chap[2]))\"/>"),
                    document,
                    options));
        }

        [TestMethod]
        public void AMissingExternalEntityIsAnError()
        {
            // Asked to read and finding nothing is an error, not a silent nothing: the caller said the
            // declaration's references were to be followed.
            XsltOptions options = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30, EntityResolver = new Entities() };
            string document = "<!DOCTYPE doc SYSTEM \"missing.dtd\"><doc/>";

            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(Sheet("x"), document, options));
            StringAssert.Contains(error.Message, "'missing.dtd'");
        }

        [TestMethod]
        public void EntityExpansionIsCapped()
        {
            // A few declarations that expand into a billion characters are stopped well short of it.
            string laughs = "<!DOCTYPE lolz [<!ENTITY lol \"lol\">";

            for (int i = 1; i <= 9; i++)
            {
                laughs += $"<!ENTITY lol{i} \"" + string.Concat(Enumerable.Repeat($"&lol{i - 1};", 10)).Replace("lol0", "lol") + "\">";
            }

            laughs += "]><lolz>&lol9;</lolz>";

            Assert.ThrowsExactly<XmlException>(() => Run(Sheet("x"), laughs));
        }

        [TestMethod]
        public void ElementContentWhitespaceIsExcluded()
        {
            // Whitespace in an element the declaration gives element content only is not part of the data
            // model (XDM §6.7.3); in mixed content it is text like any other.
            string elementOnly = "<!DOCTYPE doc [<!ELEMENT doc (item*)><!ELEMENT item (#PCDATA)>]><doc>\n  <item> x </item>\n</doc>";
            string mixed = "<!DOCTYPE doc [<!ELEMENT doc (#PCDATA|item)*>]><doc>\n  <item> x </item>\n</doc>";
            string undeclared = "<doc>\n  <item> x </item>\n</doc>";

            Assert.AreEqual("0|1", Value("concat(count(/doc/text()), '|', count(/doc/item/text()))", elementOnly));
            Assert.AreEqual("2", Value("count(/doc/text())", mixed));
            Assert.AreEqual("2", Value("count(/doc/text())", undeclared));
        }

        [TestMethod]
        public void TheStylesheetMayCarryADeclaration()
        {
            // A stylesheet's own internal subset is read the same way: an entity it declares is expanded.
            string sheet =
                "<!DOCTYPE xsl:stylesheet [<!ENTITY who \"world\">]>"
                + $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template match=\"/\"><out>hello &who;</out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual("<out>hello world</out>", Run(sheet, "<r/>"));
        }

        [TestMethod]
        public void DocumentFollowsABareNameFragment()
        {
            // A bare name after the '#' is a shorthand pointer to the element with that ID; a name no
            // element has selects nothing; anything else is refused (XTRE1160). Two references into one
            // document are one document, whatever follows their '#'.
            Entities documents = new Entities().Add("d.xml", "<!DOCTYPE d [<!ATTLIST e n ID #IMPLIED>]><d><e n=\"q\">found</e></d>");
            XsltOptions options = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30, DocumentResolver = documents };

            Assert.AreEqual(
                "<out>found|0|1</out>",
                Run(
                    Sheet("<xsl:value-of select=\"concat(document('d.xml#q'), '|', count(document('d.xml#zz')), '|', count(document('d.xml#q')/ancestor::node()[last()] | document('d.xml')))\"/>"),
                    "<r/>",
                    options));
            Assert.AreEqual("XTRE1160", Refuses(Sheet("<xsl:value-of select=\"document('d.xml#1x')\"/>"), "<r/>", options));
            Assert.AreEqual("XTRE1160", Refuses(Sheet("<xsl:value-of select=\"document('d.xml#xpointer(/d)')\"/>"), "<r/>", options));
        }
    }
}
