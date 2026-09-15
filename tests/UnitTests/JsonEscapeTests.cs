namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the <c>escape</c> and <c>fallback</c> options the two JSON readers share.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>fn:parse-json()</c> and <c>fn:json-to-xml()</c> read the same options map and are defined to
    /// answer alike about a string; only the shape they build it into differs. They did not: json-to-xml
    /// read <c>escape</c> and <c>fallback</c>, and parse-json checked them and then never consulted them,
    /// so an option that was accepted did nothing at all.
    /// </para>
    /// <para>
    /// What <c>escape</c> asks for is a result that can be read back as JSON. A character the grammar will
    /// not carry literally — the quotation mark, the backslash, every C0 control — therefore keeps the
    /// escape it came in with, and everything else is unescaped as usual: a percent sign written as an
    /// escape is a percent sign, where a newline written as one would be a newline no JSON parser would
    /// take back.
    /// </para>
    /// <para>
    /// The JSON in these is written inside an XPath string inside an XML attribute, so its own quotation
    /// marks are entities and its backslashes are doubled by C#. What reaches the parser is what the
    /// comment beside each case says it is.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class JsonEscapeTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
            + " xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\""
            + " exclude-result-prefixes=\"xs map\"";

        private static string Writes(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        /// <summary>Wraps JSON in the quotation marks an XML attribute will carry.</summary>
        private static string Quoted(string json) => "&quot;" + json + "&quot;";

        // ---- What escape retains ---------------------------------------------------------------------------

        [TestMethod]
        public void AnEscapeJsonCannotDoWithoutIsKept()
        {
            // The whole point is a result a JSON parser would take back, and a literal newline inside a
            // JSON string is not one. So "abcd\n" keeps its \n, and comes back six characters long.
            Assert.AreEqual(
                "abcd\\n",
                Writes("parse-json('" + Quoted("abcd\\n") + "', map{'escape':true()})"));

            Assert.AreEqual(
                "a\\tb",
                Writes("parse-json('" + Quoted("a\\tb") + "', map{'escape':true()})"));

            Assert.AreEqual(
                "a\\rb",
                Writes("parse-json('" + Quoted("a\\rb") + "', map{'escape':true()})"));

            // A backslash stands for one backslash and comes back as two, which is how it was written.
            Assert.AreEqual(
                "a\\\\b",
                Writes("parse-json('" + Quoted("a\\\\b") + "', map{'escape':true()})"));

            // And a character XML could not carry at all keeps the long form it came in with.
            Assert.AreEqual(
                "a\\u0000b",
                Writes("parse-json('" + Quoted("a\\u0000b") + "', map{'escape':true()})"));
        }

        [TestMethod]
        public void AnEscapeJsonDoesNotNeedIsRead()
        {
            // A percent sign stands as itself inside a JSON string, so keeping % would change the
            // text without changing what it says.
            Assert.AreEqual(
                "a%b",
                Writes("parse-json('" + Quoted("a\\u0025b") + "', map{'escape':true()})"));

            Assert.AreEqual(
                "a/b",
                Writes("parse-json('" + Quoted("a\\/b") + "', map{'escape':true()})"));

            Assert.AreEqual(
                "aéb",
                Writes("parse-json('" + Quoted("a\\u00e9b") + "', map{'escape':true()})"));
        }

        [TestMethod]
        public void WithoutTheOptionEverythingIsRead()
        {
            Assert.AreEqual("abcd\n", Writes("parse-json('" + Quoted("abcd\\n") + "')"));
            Assert.AreEqual("a\tb", Writes("parse-json('" + Quoted("a\\tb") + "', map{'escape':false()})"));
            Assert.AreEqual("a\\b", Writes("parse-json('" + Quoted("a\\\\b") + "')"));

            // A character XML cannot carry goes to the fallback, which by default is the replacement one.
            Assert.AreEqual("a�b", Writes("parse-json('" + Quoted("a\\u0000b") + "')"));
        }

        // ---- Keys are strings too --------------------------------------------------------------------------

        [TestMethod]
        public void AKeyIsRepresentedLikeAnyOtherString()
        {
            Assert.AreEqual(
                "a\\nb",
                Writes("map:keys(parse-json('{" + Quoted("a\\nb") + ":1}', map{'escape':true()}))"));

            // Which is also what decides whether two keys are one key: the long escape for a newline
            // and the short one name a single character and arrive at a single text, so the map
            // cannot hold both.
            Assert.AreEqual(
                "1",
                Writes("map:size(parse-json('{" + Quoted("\\u000a") + ":1, " + Quoted("\\n")
                    + ":2}', map{'escape':true()}))"));

            // And these do not: a C0 control XML cannot carry keeps its \u form, where a newline takes
            // the short one, so the two texts differ and both keys stand.
            Assert.AreEqual(
                "2",
                Writes("map:size(parse-json('{" + Quoted("\\u0010") + ":1, " + Quoted("\\n")
                    + ":2}', map{'escape':true()}))"));
        }

        // ---- The fallback, which parse-json also never consulted -------------------------------------------

        [TestMethod]
        public void TheFallbackIsAskedAboutWhatXmlCannotCarry()
        {
            Assert.AreEqual(
                "a[\\u0000]b",
                Writes("parse-json('" + Quoted("a\\u0000b")
                    + "', map{'fallback': function($s) { '[' || $s || ']' }})"));

            // It is asked with the escape sequence that named the character, not with the character.
            Assert.AreEqual(
                "\\u0001",
                Writes("parse-json('" + Quoted("\\u0001") + "', map{'fallback': function($s) { $s }})"));
        }

        // ---- Nothing in, nothing out -----------------------------------------------------------------------

        [TestMethod]
        public void ReadingNothingReadsNothing()
        {
            // Declared xs:string? in each, so an empty argument is an empty answer and no resource is
            // looked for. fn:json-doc is fn:parse-json over fn:unparsed-text, and rests on that holding
            // for both of them: it was the one call that noticed unparsed-text(()) went looking.
            Assert.AreEqual("0", Writes("count(parse-json(()))"));
            Assert.AreEqual("0", Writes("count(unparsed-text(()))"));
            Assert.AreEqual("0", Writes("count(json-doc(()))"));
        }
    }
}
