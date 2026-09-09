namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what backwards compatibility converts: a function's arguments by XPath 1.0's rules, and
    /// the operands of a general comparison where either of them is a number.
    /// </summary>
    /// <remarks>
    /// Backwards compatibility is XSLT 2.0's imitation of 1.0 within 2.0's data model rather than 1.0
    /// itself. Where 2.0 checks a type and refuses what does not reach it, 1.0 converts until it fits and
    /// cannot fail — a sequence becomes its first item, and a value becomes the string or the number the
    /// position asked for.
    /// </remarks>
    [TestClass]
    public sealed class BackwardsCompatibleTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private static string Writes(string expression, string version)
        {
            string stylesheet = $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\" separator=\",\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            string written = new Xslt(
                stylesheet,
                new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true })
                .TransformXml("<doc><a>one</a><a>two</a></doc>");

            return written == "<out/>" ? string.Empty : written["<out>".Length..^"</out>".Length];
        }

        [TestMethod]
        public void AnArgumentIsReducedToItsFirstItem()
        {
            // XPath 2.0 §3.1.5: where the expected type is one item or none and a sequence of more than one
            // is supplied, the value is replaced by its first item. So a call that is a type error under 2.0
            // answers under 1.0, which is what the compatibility is for.
            Assert.AreEqual("3", Writes("string-length(('abc','de'))", "1.0"));
            Assert.AreEqual("true", Writes("contains(doc/a, 'one')", "1.0"));
            Assert.AreEqual("one", Writes("string(doc/a)", "1.0"));

            // A position that takes a sequence takes the sequence: the rule is written for an expected type
            // of one item, and string-join joins what it is given.
            Assert.AreEqual("one-two", Writes("string-join(doc/a, '-')", "1.0"));

            // Under 2.0 the same calls are refused rather than answered.
            Assert.AreEqual("XPTY0004", Refuses("string-length(('abc','de'))"));
        }

        private static string Refuses(string expression)
        {
            return Assert.ThrowsExactly<XsltException>(() => Writes(expression, "2.0")).Code ?? string.Empty;
        }

        [TestMethod]
        public void AnArgumentIsConvertedToWhatThePositionAsksFor()
        {
            // fn:string() where a string was expected and fn:number() where a number was, neither of which
            // can fail. XPath 1.0 had no type error to raise here.
            Assert.AreEqual("2", Writes("string-length(12)", "1.0"));
            Assert.AreEqual("3", Writes("round('2.6')", "1.0"));
            Assert.AreEqual("true", Writes("starts-with(12.5, '12')", "1.0"));
        }

        [TestMethod]
        public void AComparisonWithANumberOnEitherSideComparesNumerically()
        {
            // And the conversion is to xs:double, whose lexical space has an exponent in it — so '5.00e0'
            // is the number five here, where XPath 1.0's own grammar for a number has no exponent and
            // number('5.00e0') is NaN.
            Assert.AreEqual("true", Writes("(1 to 5) = ('apple', 'banana', '5.00e0')", "1.0"));
            Assert.AreEqual("true", Writes("1 = '1.0e0'", "1.0"));
            Assert.AreEqual("false", Writes("1 = 'apple'", "1.0"));

            // Two strings still compare as text, there being no number on either side.
            Assert.AreEqual("false", Writes("'1' = '1.0e0'", "1.0"));

            // Under 2.0 an integer and a string are not comparable at all.
            Assert.AreEqual("XPTY0004", Refuses("1 = '1.0e0'"));
        }
    }
}
