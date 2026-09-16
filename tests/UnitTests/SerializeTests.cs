namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:serialize</c>, and in particular for the two output methods a stylesheet's result
    /// never uses: <c>json</c> and <c>adaptive</c>.
    /// </summary>
    [TestClass]
    public sealed class SerializeTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        /// <summary>
        /// Evaluates an expression and returns its string value, unescaped.
        /// </summary>
        /// <remarks>
        /// The text method, because what this function returns is markup as often as not and writing it into
        /// an element would escape it a second time — the point of most of these tests is what the first
        /// escaping did. The expression itself is escaped here rather than by hand in every one of them.
        /// </remarks>
        private static string Writes(string expression)
        {
            string escaped = expression
                .Replace("&", "&amp;", System.StringComparison.Ordinal)
                .Replace("<", "&lt;", System.StringComparison.Ordinal)
                .Replace("\"", "&quot;", System.StringComparison.Ordinal);

            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + "<xsl:output method=\"text\"/>"
                + $"<xsl:template match=\"/\"><xsl:value-of select=\"{escaped}\"/></xsl:template>"
                + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) =>
                new XsltOptions { Backend = backend, OmitXmlDeclaration = true };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        private static string? CodeOf(string expression)
        {
            try
            {
                Writes(expression);
                return null;
            }
            catch (XsltException error)
            {
                return error.Code;
            }
        }

        private const string AsJson = "map{'method':'json'}";

        [TestMethod]
        [DataRow("[ ]", "[]")]
        [DataRow("[1, 2, 3, 'four', true(), false()]", "[1,2,3,\"four\",true,false]")]
        [DataRow("[1, 2, [3, 4, 5], 6]", "[1,2,[3,4,5],6]")]
        [DataRow("map{}", "{}")]
        [DataRow("map{'abc':23}", "{\"abc\":23}")]
        [DataRow("map{'abc':array{1 to 4}}", "{\"abc\":[1,2,3,4]}")]
        [DataRow("()", "null")]
        [DataRow("true()", "true")]
        [DataRow("12.5", "12.5")]
        public void TheJsonMethodWritesAMapOrAnArrayAsJson(string value, string expected)
        {
            Assert.AreEqual(expected, Writes($"serialize({value}, {AsJson})"));
        }

        [TestMethod]
        public void EverythingThatIsNotAMapAnArrayOrANumberBecomesAJsonString()
        {
            Assert.AreEqual("{\"a\":\"2011-04-06\"}", Writes($"serialize(map{{'a':xs:date('2011-04-06')}}, {AsJson})"));
            Assert.AreEqual("[0,0,\"abcd\"]", Writes($"serialize([0,0,xs:untypedAtomic('abcd')], {AsJson})"));

            // A map may be keyed by anything and JSON only by strings, so the keys are written as strings too.
            Assert.AreEqual("{\"1\":\"a\"}", Writes($"serialize(map{{1:'a'}}, {AsJson})"));
        }

        [TestMethod]
        public void AJsonStringEscapesWhatJsonCannotHoldAndAlsoTheSolidus()
        {
            Assert.AreEqual("\"abc\\\"def\"", Writes($"serialize('abc\"def', {AsJson})"));
            Assert.AreEqual("\"\\n\"", Writes($"serialize(codepoints-to-string(10), {AsJson})"));

            // The solidus needs no escape in JSON and this specification asks for one anyway.
            Assert.AreEqual(
                "{\"uri\":\"http:\\/\\/www.w3.org\\/\"}",
                Writes($"serialize(map{{'uri':xs:anyURI('http://www.w3.org/')}}, {AsJson})"));
        }

        [TestMethod]
        public void ANodeIsWrittenAsMarkupInsideAJsonString()
        {
            Assert.AreEqual(
                "{\"a\":\"<a>text<\\/a>\"}",
                Writes($"serialize(map{{'a':parse-xml('<a>text</a>')}}, {AsJson})"));
        }

        [TestMethod]
        public void JsonHoldsOneValueAndSaysSoWhenGivenMore()
        {
            // The rule reaches inwards as well: a map entry or an array member is one value too.
            Assert.AreEqual("SERE0023", CodeOf($"serialize(1 to 10, {AsJson})"));
            Assert.AreEqual("SERE0023", CodeOf($"serialize(map{{'abc':(1 to 10)}}, {AsJson})"));
            Assert.AreEqual("SERE0023", CodeOf($"serialize((map{{'a':1}}, map{{'b':2}}), {AsJson})"));
        }

        [TestMethod]
        public void JsonHasNoInfinityAndNoNotANumber()
        {
            Assert.AreEqual("SERE0020", CodeOf($"serialize([number('NaN')], {AsJson})"));
            Assert.AreEqual("SERE0020", CodeOf($"serialize([number('INF')], {AsJson})"));
        }

        [TestMethod]
        public void TwoKeysThatBecomeOneFieldNameAreRefusedUnlessAllowed()
        {
            const string Both = "map{QName('','foo'):1, 'foo':2}";

            Assert.AreEqual("SERE0022", CodeOf($"serialize({Both}, {AsJson})"));
            Assert.AreEqual(
                "{\"foo\":1,\"foo\":2}",
                Writes($"serialize({Both}, map{{'method':'json','allow-duplicate-names':true()}})"));
        }

        [TestMethod]
        [DataRow("23", "a number is not a boolean")]
        [DataRow("'true'", "and neither is the word")]
        [DataRow("(true(),false())", "nor two of them")]
        public void AParameterDeclaredBooleanHasToBeOneInAMap(string value, string why)
        {
            // In the element form every value is written as text, so 'yes' is how a boolean is spelled
            // there; in a map the value carries a type and these are the wrong ones.
            Assert.AreEqual(
                "XPTY0004",
                CodeOf($"serialize([1,2,3], map{{'method':'json','indent':{value}}})"),
                why);
        }

        [TestMethod]
        public void TheAdaptiveMethodWritesWhateverItIsGiven()
        {
            Assert.AreEqual("1;2;3", Writes("serialize((1,2,3), map{'method':'adaptive','item-separator':';'})"));

            Assert.AreEqual(
                "map{1:true(),2:false()};map{8:80,9:90}",
                Writes("serialize((map{1:true(),2:false()}, map{8:80,9:90}), "
                    + "map{'method':'adaptive','item-separator':';'})"));

            // A free-standing attribute has no serialized form under any other method, and here it is
            // written the way it stands on an element.
            Assert.AreEqual(
                "x=\"1\";y=\"2\"",
                Writes("serialize((parse-xml('<a x=\"1\"/>')/a/@x, parse-xml('<b y=\"2\"/>')/b/@y), "
                    + "map{'method':'adaptive','item-separator':';'})"));
        }

        [TestMethod]
        public void ANodeWithNoFormOfItsOwnIsRefusedByTheMarkupMethods()
        {
            Assert.AreEqual("SENR0001", CodeOf("serialize(parse-xml('<a x=\"1\"/>')/a/@x)"));
            Assert.AreEqual("SENR0001", CodeOf("serialize(map{'a':1})"));
        }

        [TestMethod]
        public void TheParametersMayBeWrittenAsAnElementInstead()
        {
            // XPath 3.0's form, which 3.1's map replaced for most purposes. Every value is text there, so
            // 'yes' is how a boolean is spelled.
            string parameters = Element("<method value='text'/>");

            Assert.AreEqual("hello", Writes($"serialize(parse-xml('<a>hello</a>'), {parameters})"));
        }

        [TestMethod]
        public void AParameterElementThatIsWrongInAnyWayIsReported()
        {
            // It is a document written by hand and read once, so a typo in it is worth reporting rather
            // than passing over. Which code it is reported under is the specification's to say, and it
            // gives two of them their own: a parameter set twice and a character mapped twice.
            foreach ((string body, string code, string why) in new[]
            {
                ("<method value='xml'/><method value='text'/>", "SEPM0019", "a parameter named twice"),
                ("<invented value='xml'/>", "SEPM0017", "a parameter that is not one"),
                ("<method/>", "SEPM0017", "a parameter with no value"),
                ("<method value='xml' value2='text'/>", "SEPM0017", "an attribute it does not take"),
                ("<indent value='maybe'/>", "SEPM0017", "a value the parameter will not take"),
                ("<use-character-maps value='yes'/>", "SEPM0017", "an attribute on use-character-maps"),
                ("<use-character-maps><character-map character='$$' map-string='x'/></use-character-maps>",
                    "SEPM0017", "a mapping of more than one character"),
                ("<use-character-maps><character-map character='$' map-string='x'/>"
                    + "<character-map character='$' map-string='y'/></use-character-maps>",
                    "SEPM0018", "a character mapped twice"),
            })
            {
                Assert.AreEqual(
                    code, CodeOf($"serialize(parse-xml('<a/>'), {Element(body)})"), why);
            }
        }

        [TestMethod]
        public void AParameterInSomebodyElsesNamespaceIsLeftAlone()
        {
            // A vendor's own parameter for a vendor that is not this one, which the specification says to
            // pass over rather than refuse — and two of them under one name is still a parameter set twice.
            StringAssert.Contains(
                Writes("serialize(parse-xml('<a/>'), " + Element(
                    "<method value=\"xml\"/>"
                    + "<v:indent-spaces value=\"2\" xmlns:v=\"http://vendor.example.com/\"/>") + ")"),
                "<a/>");

            Assert.AreEqual(
                "SEPM0019",
                CodeOf("serialize(parse-xml('<a/>'), " + Element(
                    "<v:indent-spaces value=\"3\" xmlns:v=\"http://vendor.example.com/\"/>"
                    + "<v:indent-spaces value=\"2\" xmlns:v=\"http://vendor.example.com/\"/>") + ")"));
        }

        [TestMethod]
        public void AnElementThatIsNotTheParametersIsATypeError()
        {
            // The argument is declared element(output:serialization-parameters), so an element of another
            // name does not match the declaration: that is a type error rather than a serialization one.
            Assert.AreEqual(
                "XPTY0004",
                CodeOf("serialize(parse-xml('<a/>'), parse-xml('<wrong/>')/*)"));
        }

        [TestMethod]
        public void ACharacterMapMayBeWrittenEitherWay()
        {
            // The element form, which holds one character-map child per substitution.
            StringAssert.Contains(
                Writes("serialize(parse-xml('<a>$</a>'), " + Element(
                    "<use-character-maps><character-map character=\"$\" map-string=\"USD\"/>"
                    + "</use-character-maps>") + ")"),
                "USD");

            // And the map form, where the parameter is itself a map of character to replacement.
            StringAssert.Contains(
                Writes("serialize(parse-xml('<a>$</a>'), map { 'use-character-maps': map { '$': 'USD' } })"),
                "USD");

            // A key or a value that is not a string is a type error: the option parameter conventions
            // convert an untyped value and refuse the rest, and they do not reach inside the inner map.
            Assert.AreEqual(
                "XPTY0004",
                CodeOf("serialize(parse-xml('<a/>'), map { 'use-character-maps': map { 'x': xs:QName('n') } })"));
        }

        /// <summary>Builds the expression that parses a serialization-parameters element.</summary>
        private static string Element(string body)
        {
            return "parse-xml('<serialization-parameters "
                + "xmlns=\"http://www.w3.org/2010/xslt-xquery-serialization\">"
                + body.Replace("'", "\"", System.StringComparison.Ordinal)
                + "</serialization-parameters>')/*";
        }
    }
}
