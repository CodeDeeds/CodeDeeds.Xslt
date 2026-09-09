namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the four functions XPath 3.1 gives for reading and writing JSON.
    /// </summary>
    /// <remarks>
    /// There are two ways in and the specification gives both because they answer different questions.
    /// <c>parse-json()</c> yields maps and arrays, which is what you want when the JSON is your data;
    /// <c>json-to-xml()</c> yields a node tree, which is what you want when you would rather write templates
    /// than lookups. Both directions are asserted here, and so is the round trip between them.
    /// </remarks>
    [TestClass]
    public sealed class JsonFunctionTests
    {
        private const string XslOnly = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private const string Xsl =
            XslOnly + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        /// <summary>The map and array namespaces, which a stylesheet reaching those libraries declares.</summary>
        private const string Libraries =
            "xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\""
            + " xmlns:array=\"http://www.w3.org/2005/xpath-functions/array\"";

        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_resources = new(StringComparer.Ordinal);

            public MapResolver Add(string name, string content)
            {
                m_resources[name] = content;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_resources.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        /// <summary>
        /// Escapes an expression for an XML attribute, JSON being full of the quote that ends one.
        /// </summary>
        private static string Attribute(string expression)
        {
            return expression.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");
        }

        private static string Writes(string expression, string input = "<r/>", IXsltResolver? documents = null)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {XslOnly} {Libraries}"
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " exclude-result-prefixes=\"xs map array\">"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{Attribute(expression)}\" separator=\",\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                DocumentResolver = documents,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        /// <summary>Copies what an expression selects into the result, so a node structure can be seen.</summary>
        private static string Copies(string expression, string input = "<r/>")
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><xsl:copy-of select=\"{Attribute(expression)}\"/>"
                + "</xsl:template></xsl:stylesheet>";

            return new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml(input);
        }

        private static string Refuses(string expression, string input = "<r/>")
        {
            return Assert.ThrowsExactly<XsltException>(() => Writes(expression, input)).Code ?? string.Empty;
        }

        private const string Json = "'{\"a\": 1, \"b\": [true, null, \"x\"]}'";

        [TestMethod]
        public void ParseJsonYieldsMapsAndArrays()
        {
            Assert.AreEqual("1", Writes($"parse-json({Json})?a"));
            Assert.AreEqual("true", Writes($"parse-json({Json})?b?1"));
            Assert.AreEqual("x", Writes($"parse-json({Json})?b?3"));
            Assert.AreEqual("2", Writes($"map:size(parse-json({Json}))"));
            Assert.AreEqual("3", Writes($"array:size(parse-json({Json})?b)"));

            Assert.AreEqual("true", Writes($"parse-json({Json}) instance of map(*)"));
            Assert.AreEqual("true", Writes("parse-json('[1, 2]') instance of array(*)"));
        }

        [TestMethod]
        public void EveryJsonNumberIsADouble()
        {
            // JSON has one numeric type, so reading 1 as an integer would make the XPath type depend on how
            // the number happened to be written on the other side of the wire.
            Assert.AreEqual("true", Writes("parse-json('1') instance of xs:double"));
            Assert.AreEqual("false", Writes("parse-json('1') instance of xs:integer"));
            Assert.AreEqual("1.5", Writes("parse-json('1.5')"));
            Assert.AreEqual("1000", Writes("parse-json('1e3')"));

            // Past what a double holds the answer is an infinity, which is what casting the digits gives.
            Assert.AreEqual("INF", Writes("parse-json('1e400')"));
        }

        [TestMethod]
        public void JsonNullIsTheEmptySequence()
        {
            // The data model has no null, and inventing one would put a value into every sequence that a
            // stylesheet would then have to test for. The empty sequence already means "nothing here".
            Assert.AreEqual("0", Writes("count(parse-json('null'))"));
            Assert.AreEqual("0", Writes("count(parse-json('{\"a\": null}')?a)"));

            // The entry is still there, which is what tells it apart from one that was never written.
            Assert.AreEqual("true", Writes("map:contains(parse-json('{\"a\": null}'), 'a')"));
            Assert.AreEqual("false", Writes("map:contains(parse-json('{\"a\": null}'), 'b')"));
        }

        [TestMethod]
        public void ParseJsonRefusesWhatIsNotJson()
        {
            Assert.AreEqual("FOJS0001", Refuses("parse-json('{')"));
            Assert.AreEqual("FOJS0001", Refuses("parse-json('')"));
            Assert.AreEqual("FOJS0001", Refuses("parse-json('nonsense')"));

            // A JSON document is one value, and two side by side is not one.
            Assert.AreEqual("FOJS0001", Refuses("parse-json('1 2')"));

            // Comments and trailing commas are not JSON, and are accepted only when asked for.
            Assert.AreEqual("FOJS0001", Refuses("parse-json('[1, 2,]')"));
            Assert.AreEqual("2", Writes("array:size(parse-json('[1, 2,]', map { 'liberal': true() }))"));
        }

        [TestMethod]
        public void DuplicateKeysAreHandledAsAsked()
        {
            const string Twice = "'{\"a\": 1, \"a\": 2}'";

            Assert.AreEqual("1", Writes($"parse-json({Twice})?a"));
            Assert.AreEqual(
                "2", Writes($"parse-json({Twice}, map {{ 'duplicates': 'use-last' }})?a"));

            Assert.AreEqual(
                "FOJS0003",
                Refuses($"parse-json({Twice}, map {{ 'duplicates': 'reject' }})"));

            Assert.AreEqual(
                "FOJS0005",
                Refuses($"parse-json({Twice}, map {{ 'duplicates': 'nonesuch' }})"));

            // A value of the wrong type is a type error by the option parameter conventions, where a value of
            // the right type that means nothing is FOJS0005: the suite's xml-to-json-C100 draws the line.
            Assert.AreEqual("XPTY0004", Refuses("parse-json('1', map { 'liberal': 'yes' })"));
            Assert.AreEqual("XPTY0004", Refuses("parse-json('1', 'not a map')"));
        }

        [TestMethod]
        public void HalfASurrogatePairBecomesTheReplacementCharacter()
        {
            // '\uD834' alone is well-formed JSON denoting a character that cannot exist. The specification
            // says to substitute U+FFFD rather than to fail, which is the same answer a text decoder gives
            // for bytes it cannot read and for the same reason: one unrepresentable character should cost
            // that character, not the document.
            Assert.AreEqual("�", Writes(@"parse-json('""\uD834""')"));
            Assert.AreEqual("�", Writes(@"parse-json('""\uDD1E""')"));
            Assert.AreEqual("a�b", Writes(@"parse-json('""a\uDEADb""')"));

            // A complete pair is the character it names, and the escapes around it still work.
            Assert.AreEqual("\U0001D11E", Writes(@"parse-json('""𝄞""')"));
            Assert.AreEqual("a\tb", Writes(@"parse-json('""a\tb""')"));
            Assert.AreEqual("\"", Writes(@"parse-json('""\""""')"));

            // A key is read the same way as a value.
            Assert.AreEqual("1", Writes(@"parse-json('{""\uD834"": 1}')?*"));

            // A backslash beginning no escape JSON defines is refused while the string is being scanned,
            // before the escapes are read, so it comes back as a syntax error rather than as FOJS0007.
            Assert.AreEqual("FOJS0001", Refuses(@"parse-json('""\q""')"));
        }

        [TestMethod]
        public void JsonDocReadsTheResourceAndParsesIt()
        {
            MapResolver documents = new MapResolver().Add("data.json", "{\"a\": [1, 2]}");

            Assert.AreEqual("2", Writes("json-doc('data.json')?a?2", documents: documents));
            Assert.AreEqual("1", Writes("map:size(json-doc('data.json'))", documents: documents));
        }

        [TestMethod]
        public void JsonToXmlBuildsTheNodeStructure()
        {
            Assert.AreEqual(
                "<map xmlns=\"http://www.w3.org/2005/xpath-functions\">"
                + "<number key=\"a\">1</number><boolean key=\"b\">true</boolean></map>",
                Copies("json-to-xml('{\"a\": 1, \"b\": true}')"));

            Assert.AreEqual(
                "<array xmlns=\"http://www.w3.org/2005/xpath-functions\">"
                + "<string>x</string><null/></array>",
                Copies("json-to-xml('[\"x\", null]')"));

            // It is a document node, so a path steps into it the way it steps into anything doc() returns.
            Assert.AreEqual("map", Writes("name(json-to-xml('{}')/*)"));
            Assert.AreEqual("FOJS0001", Refuses("json-to-xml('{')"));
        }

        [TestMethod]
        public void XmlToJsonWritesTheStructureBackOut()
        {
            Assert.AreEqual(
                "{\"a\":1,\"b\":true}",
                Writes("xml-to-json(json-to-xml('{\"a\": 1, \"b\": true}'))"));

            Assert.AreEqual("[\"x\",null]", Writes("xml-to-json(json-to-xml('[\"x\", null]'))"));
            Assert.AreEqual("{}", Writes("xml-to-json(json-to-xml('{}'))"));

            // A string is escaped on the way out, so what comes back is JSON and not something that looks
            // like it until a quote appears.
            Assert.AreEqual(
                "[\"say \\\"so\\\"\"]", Writes("xml-to-json(json-to-xml('[\"say \\\"so\\\"\"]'))"));
        }

        [TestMethod]
        public void XmlToJsonRefusesWhatIsNotTheStructure()
        {
            Assert.AreEqual("FOJS0006", Refuses("xml-to-json(/r)", "<r/>"));
            Assert.AreEqual(string.Empty, Writes("xml-to-json(())"));
        }

        [TestMethod]
        public void TheTwoDirectionsAgreeWithEachOther()
        {
            // The round trip is the useful assertion: it holds every rule in both directions at once, and a
            // disagreement between them shows up here rather than as two tests that each look right.
            const string Original = "{\"n\":1.5,\"s\":\"a b\",\"t\":true,\"z\":null,\"a\":[1,[2]],\"m\":{\"k\":\"v\"}}";

            Assert.AreEqual(Original, Writes($"xml-to-json(json-to-xml('{Original}'))"));
        }

        [TestMethod]
        public void JsonIsNotAvailableInATwoPointZeroStylesheet()
        {
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:template match=\"/\"><out><xsl:value-of select=\"parse-json('1')\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, new XsltOptions()).TransformXml("<r/>"));

            Assert.AreEqual("XPST0017", error.Code);
        }

        /// <summary>An element in the functions namespace, as fn:json-to-xml() would have built it.</summary>
        private static string J(string markup)
        {
            return "parse-xml('" + markup.Replace("xmlns>", "NS>").Replace("xmlns ", "NS ").Replace("xmlns/>", "NS/>").Replace("NS", "xmlns=\"http://www.w3.org/2005/xpath-functions\"") + "')";
        }

        [TestMethod]
        public void XmlToJsonEscapesWhatTheSpecificationSaysAndCopiesWhatIsEscapedAlready()
        {
            // The solidus goes out escaped, as the specification's erratum has it, and the control range —
            // C0, DEL and C1 — as \u escapes in upper-case hex. Text that says it is escaped already keeps its
            // escapes, has the rest escaped as usual, and is refused where a backslash starts no escape.
            Assert.AreEqual("\"-\\/-\"", Writes("xml-to-json(" + J("<string xmlns>-/-</string>") + ")"));
            Assert.AreEqual(
                "\"-\\u007F-\"",
                Writes("xml-to-json(" + J("<string xmlns>-&#127;-</string>") + ")"));
            Assert.AreEqual(
                "\"\\u0041 \\\" \\\\\"",
                Writes("xml-to-json(" + J("<string xmlns escaped=\" 1 \">\\u0041 \" \\\\</string>") + ")"));
            Assert.AreEqual(
                "FOJS0007",
                Refuses("xml-to-json(" + J("<string xmlns escaped=\"true\">\\Q</string>") + ")"));
        }

        [TestMethod]
        public void XmlToJsonWritesANumberAsXPathWritesADouble()
        {
            // 007 is 7 and 1E6 is 1.0E6: the number is an xs:double, written as fn:string() writes one, which
            // is what json-to-xml would have made of the same text. NaN is not a number JSON has.
            Assert.AreEqual("7", Writes("xml-to-json(" + J("<number xmlns> 007 </number>") + ")"));
            Assert.AreEqual("1.0E6", Writes("xml-to-json(" + J("<number xmlns>1E6</number>") + ")"));
            Assert.AreEqual("0.001", Writes("xml-to-json(" + J("<number xmlns>.001</number>") + ")"));
            Assert.AreEqual("FOJS0006", Refuses("xml-to-json(" + J("<number xmlns>NaN</number>") + ")"));
        }

        [TestMethod]
        public void XmlToJsonChecksTheStructureAsTheSchemaHasIt()
        {
            // Two keys that unescape to the same string are one key twice; an attribute in no namespace the
            // element does not have, or one claiming the functions namespace, is not the structure; one in
            // any other namespace is none of its business. A key outside a map is allowed and ignored.
            Assert.AreEqual(
                "FOJS0006",
                Refuses("xml-to-json(" + J(
                    "<map xmlns><string key=\"1\">a</string>"
                    + "<string key=\"\\u0031\" escaped-key=\"true\">b</string></map>") + ")"));
            Assert.AreEqual(
                "FOJS0006", Refuses("xml-to-json(" + J("<number xmlns zero=\"yes\">0</number>") + ")"));
            Assert.AreEqual(
                "\"a\"",
                Writes("xml-to-json(" + J(
                    "<string xmlns key=\"k\" xsi:type=\"xs:string\" "
                    + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">a</string>") + ")"));
            Assert.AreEqual(
                "FOJS0006", Refuses("xml-to-json(" + J("<string xmlns>H<sub>2</sub>O</string>") + ")"));
        }

        [TestMethod]
        public void XmlToJsonChecksItsOptionsByTheOptionParameterConventions()
        {
            // Absent means the default. Present, it is one xs:boolean: a string is a type error, an empty
            // sequence too, and an untyped value is cast, which is what makes '2' a cast failure.
            Assert.AreEqual("null", Writes("xml-to-json(" + J("<null xmlns/>") + ")"));
            Assert.AreEqual(
                "XPTY0004", Refuses("xml-to-json(" + J("<null xmlns/>") + ", map { 'indent': 'yes' })"));
            Assert.AreEqual(
                "XPTY0004", Refuses("xml-to-json(" + J("<null xmlns/>") + ", map { 'indent': () })"));
            Assert.AreEqual(
                "FORG0001",
                Refuses("xml-to-json(" + J("<null xmlns/>") + ", map { 'indent': xs:untypedAtomic('2') })"));
        }
        // ---- What json-to-xml was told to do ------------------------------------------------------------

        [TestMethod]
        public void JsonToXmlKeepsBothEntriesOfARepeatedKeyUnlessToldOtherwise()
        {
            // The XML representation can hold two elements of one key where a map cannot hold two entries,
            // so the two functions do not admit the same answers. json-to-xml retains both by default and
            // takes 'retain' where parse-json takes 'use-last'.
            const string Repeated = "'{\"a\":1, \"b\":2, \"a\":3}'";

            Assert.AreEqual(
                "<map xmlns=\"http://www.w3.org/2005/xpath-functions\"><number key=\"a\">1</number>"
                + "<number key=\"b\">2</number><number key=\"a\">3</number></map>",
                Copies("json-to-xml(" + Repeated + ")"));

            Assert.AreEqual(
                "<map xmlns=\"http://www.w3.org/2005/xpath-functions\"><number key=\"a\">1</number>"
                + "<number key=\"b\">2</number></map>",
                Copies("json-to-xml(" + Repeated + ", map { 'duplicates': 'use-first' })"));

            Assert.AreEqual(
                "FOJS0003",
                Refuses("json-to-xml(" + Repeated + ", map { 'duplicates': 'reject' })"));

            // use-last is one of parse-json's answers and not one of these, so it is not a spelling mistake
            // to be guessed at but an option value this function does not have.
            Assert.AreEqual(
                "FOJS0005",
                Refuses("json-to-xml(" + Repeated + ", map { 'duplicates': 'use-last' })"));

            Assert.AreEqual(
                "XPTY0004",
                Refuses("json-to-xml(" + Repeated + ", map { 'duplicates': true() })"));

            // And the other way round, where retain is the one that does not apply.
            Assert.AreEqual("3", Writes("parse-json(" + Repeated + ", map { 'duplicates': 'use-last' })?a"));
            Assert.AreEqual(
                "FOJS0005",
                Refuses("parse-json(" + Repeated + ", map { 'duplicates': 'retain' })"));
        }

        [TestMethod]
        public void JsonToXmlSaysWhatBecomesOfACharacterXmlCannotHold()
        {
            // A form feed is a character JSON admits and XML does not. Asked to escape, the string keeps the
            // escape it was written with and the element says so; asked not to, the default is the
            // replacement character, and a fallback function is called with the escape sequence instead.
            const string Feed = "'[\"\\f\"]'";

            Assert.AreEqual(
                "<array xmlns=\"http://www.w3.org/2005/xpath-functions\">"
                + "<string escaped=\"true\">\\f</string></array>",
                Copies("json-to-xml(" + Feed + ", map { 'escape': true() })"));

            Assert.AreEqual(
                "<array xmlns=\"http://www.w3.org/2005/xpath-functions\"><string>\uFFFD</string></array>",
                Copies("json-to-xml(" + Feed + ")"));

            Assert.AreEqual(
                "<array xmlns=\"http://www.w3.org/2005/xpath-functions\"><string>[\\f]</string></array>",
                Copies(
                    "json-to-xml(" + Feed
                    + ", map { 'fallback': function($s) { '[' || $s || ']' } })"));

            // A key is escaped by the same rule, and says so under a name of its own.
            Assert.AreEqual(
                "<map xmlns=\"http://www.w3.org/2005/xpath-functions\">"
                + "<number escaped-key=\"true\" key=\"k\\f\">1</number></map>",
                Copies("json-to-xml('{\"k\\f\":1}', map { 'escape': true() })"));

            // The options are checked as the option parameter conventions have them.
            Assert.AreEqual("XPTY0004", Refuses("json-to-xml('[1]', map { 'escape': () })"));
            Assert.AreEqual(
                "XPTY0004", Refuses("json-to-xml('[1]', map { 'escape': (true(), true()) })"));
            Assert.AreEqual("XPTY0004", Refuses("json-to-xml('[1]', map { 'escape': 'ECMA-262' })"));
            Assert.AreEqual("XPTY0004", Refuses("json-to-xml('[1]', map { 'fallback': 'dummy' })"));
        }
    }
}
