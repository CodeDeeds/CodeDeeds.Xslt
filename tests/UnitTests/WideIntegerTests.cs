namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for an <c>xs:integer</c> too large to hold in sixty-four bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>xs:integer</c> has no bound in the specification, and a <see cref="long"/> does. What is stored is
    /// a long wherever the value fits one, and a <see cref="System.Numerics.BigInteger"/> where it does not
    /// — narrowed back the moment it fits again, so that two equal integers are also stored alike and every
    /// key, hash and fast path can go on treating the narrow form as the only one it will meet.
    /// </para>
    /// <para>
    /// The risk in a representation with two forms is a path that knows only the narrow one. Those refuse
    /// rather than truncate, so a gap shows up as an error and never as a wrong number; the cases here walk
    /// a wide value through the operations that have been taught about it.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class WideIntegerTests
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

        private static void Refuses(string code, string expression)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression));
            Assert.AreEqual(code, error.Code, $"'{expression}': {error.Message}");
        }

        /// <summary>Ten to the twenty-first, which is four bits past what a signed long reaches.</summary>
        private const string Wide = "1000000000000000000000";

        // ---- Reading and writing ---------------------------------------------------------------------------

        [TestMethod]
        public void AWideLiteralIsReadAndWrittenBackExactly()
        {
            Assert.AreEqual(Wide, Writes(Wide));
            Assert.AreEqual("-" + Wide, Writes("-" + Wide));
            Assert.AreEqual(Wide, Writes($"xs:integer('{Wide}')"));
            Assert.AreEqual(Wide, Writes($"string({Wide})"));

            // The digits are all kept: read as a double it would come back 1E21, which is a different number.
            Assert.AreEqual("1000000000000000000001", Writes($"{Wide} + 1"));
        }

        [TestMethod]
        public void TheTypeIsStillXsInteger()
        {
            Assert.AreEqual("true", Writes($"{Wide} instance of xs:integer"));
            Assert.AreEqual("true", Writes($"{Wide} castable as xs:integer"));
            Assert.AreEqual("false", Writes($"{Wide} instance of xs:double"));
        }

        // ---- Arithmetic ------------------------------------------------------------------------------------

        [TestMethod]
        public void ArithmeticCarriesOnPastSixtyFourBits()
        {
            Assert.AreEqual("9223372036854775808", Writes("xs:integer('9223372036854775807') + 1"));
            Assert.AreEqual("12000000000000000000", Writes("xs:integer('4000000000000000000') * 3"));
            Assert.AreEqual($"2{Wide[1..]}", Writes($"{Wide} + {Wide}"));
            Assert.AreEqual("0", Writes($"{Wide} - {Wide}"));
            Assert.AreEqual("1000000000000000000000000000000000000000000", Writes($"{Wide} * {Wide}"));
        }

        [TestMethod]
        public void TheAnswerNarrowsAgainWhereItFits()
        {
            // Two forms for one type would be a bug waiting to happen if a value could sit in the wide one
            // while fitting the narrow. Each of these lands back inside 64 bits and has to be stored there,
            // which is what makes it a map key and a grouping key equal to the same number written plainly.
            Assert.AreEqual("42", Writes($"({Wide} + 42) - {Wide}"));
            Assert.AreEqual("1", Writes($"{Wide} idiv {Wide}"));
            Assert.AreEqual("0", Writes($"{Wide} mod 2"));

            // Keyed alike, which is what 'distinct' asks: two forms for one type would otherwise let
            // the same number sit in a sequence twice.
            Assert.AreEqual(
                "1", Writes($"count(distinct-values(((({Wide} + 42) - {Wide}), 42)))"));
        }

        [TestMethod]
        public void DividingTwoIntegersStillGivesADecimal()
        {
            Assert.AreEqual("500000000000000000000", Writes($"{Wide} div 2"));
            Assert.AreEqual("0.00000000000000000001", Writes("1 div 100000000000000000000"));
        }

        [TestMethod]
        public void NegationHasRoomForTheAnswer()
        {
            Assert.AreEqual("-" + Wide, Writes($"-({Wide})"));
            Assert.AreEqual(Wide, Writes($"-(-{Wide})"));

            // The one narrow value whose negation is not narrow: a long reaches one further down than up.
            Assert.AreEqual("9223372036854775808", Writes("-xs:integer('-9223372036854775808')"));
        }

        [TestMethod]
        public void TheOtherNumericFunctionsFollow()
        {
            Assert.AreEqual(Wide, Writes($"abs(-{Wide})"));
            Assert.AreEqual(Wide, Writes($"round({Wide})"));
            Assert.AreEqual(Wide, Writes($"floor({Wide})"));
            Assert.AreEqual(Wide, Writes($"ceiling({Wide})"));
            Assert.AreEqual(Wide, Writes($"max(({Wide}, 1))"));
            Assert.AreEqual("1", Writes($"min(({Wide}, 1))"));
            Assert.AreEqual($"2{Wide[1..]}", Writes($"sum(({Wide}, {Wide}))"));
        }

        // ---- Comparing -------------------------------------------------------------------------------------

        [TestMethod]
        public void ComparingKeepsEveryDigit()
        {
            // Two values a double cannot tell apart: 1e21 and 1e21 + 1 are the same double, so a comparison
            // that went by way of one would call these equal.
            Assert.AreEqual("false", Writes($"{Wide} eq {Wide} + 1"));
            Assert.AreEqual("true", Writes($"{Wide} lt {Wide} + 1"));
            Assert.AreEqual("true", Writes($"{Wide} gt 9223372036854775807"));
            Assert.AreEqual("true", Writes($"-{Wide} lt -9223372036854775808"));
            Assert.AreEqual("true", Writes($"{Wide} eq xs:integer('{Wide}')"));

            // And against the other numeric types, where the comparison is the wider of the two.
            Assert.AreEqual("true", Writes($"{Wide} gt 1.5"));
            Assert.AreEqual("true", Writes($"{Wide} lt 1e30"));
        }

        [TestMethod]
        public void SortingAndDistinctValuesSeeTheWholeNumber()
        {
            Assert.AreEqual(
                "1 9223372036854775807 " + Wide,
                Writes($"string-join(sort(({Wide}, 1, 9223372036854775807)), ' ')"));

            Assert.AreEqual("2", Writes($"count(distinct-values(({Wide}, {Wide} + 1)))"));
            Assert.AreEqual("1", Writes($"count(distinct-values(({Wide}, xs:integer('{Wide}'))))"));
        }

        // ---- The range operator ----------------------------------------------------------------------------

        [TestMethod]
        public void ARangeRunsBetweenWideBounds()
        {
            Assert.AreEqual("4", Writes($"count({Wide} to {Wide[..^1]}3)"));

            Assert.AreEqual(
                "1000000000000000000000 1000000000000000000001 1000000000000000000002",
                Writes($"string-join({Wide} to {Wide[..^1]}2, ' ')"));

            // Descending is empty rather than reversed, as it is for narrow bounds.
            Assert.AreEqual("0", Writes($"count({Wide[..^1]}3 to {Wide})"));

            // And a bound the count of items would overflow a long for is still just a bound.
            Assert.AreEqual("true", Writes($"{Wide} = ({Wide} to {Wide[..^1]}3)"));
        }

        // ---- Where it still will not go --------------------------------------------------------------------

        [TestMethod]
        public void TheNarrowerTypesHaveNoRoomAndSaySo()
        {
            // Every derived integer type but the four defined by sign alone is narrower than 64 bits, so a
            // value this size is outside the type rather than beyond this engine.
            Refuses("FORG0001", $"xs:long('{Wide}')");
            Refuses("FORG0001", $"xs:unsignedLong('{Wide}')");
            Refuses("FORG0001", $"xs:int('{Wide}')");

            // The four that keep xs:integer's lack of a bound take it, subject to their sign.
            Assert.AreEqual(Wide, Writes($"xs:nonNegativeInteger('{Wide}')"));
            Assert.AreEqual(Wide, Writes($"xs:positiveInteger('{Wide}')"));
            Assert.AreEqual("-" + Wide, Writes($"xs:negativeInteger('-{Wide}')"));
            Refuses("FORG0001", $"xs:positiveInteger('-{Wide}')");
        }

        [TestMethod]
        public void SpellingOneOutIsStillSixtyFourBitWork()
        {
            // format-integer and xsl:number render through a fixed-width number, and widening those means
            // widening the sequences they render through as well - the words for a quintillion and past it,
            // the alphabetic and roman fallbacks. That has not been done, and the path refuses rather than
            // truncating, which is the property the two forms are meant to keep: a gap is an error and
            // never a wrong number.
            Refuses("FOAR0002", $"format-integer({Wide}, '1')");
        }

        [TestMethod]
        public void UnderOnePointZeroTheSameDigitsAreStillADouble()
        {
            // XPath 1.0 had one numeric type and no integers to be wide, so the compatibility mode reads the
            // literal as a double, as it always has.
            string stylesheet = "<xsl:stylesheet version=\"1.0\" "
                + "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{Wide}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            // Written as this engine writes a double, which is its own question and settled elsewhere;
            // what matters here is that the digits were not kept, because under 1.0 they never are.
            Assert.AreEqual(
                "<out>1.0E21</out>",
                new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }
    }
}
