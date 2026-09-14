namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the whole number of months a scaled <c>xs:yearMonthDuration</c> comes back as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A year-month duration is a count of months and nothing finer, so multiplying or dividing one has to
    /// land on a whole month. The specification says which one: the nearest, and where the value falls
    /// exactly between two, the one nearer positive infinity — <c>fn:round</c>'s rule, and not the rule
    /// either of the two obvious library calls implements. <c>Math.Round</c> sends a half to its even
    /// neighbour by default, and <c>MidpointRounding.ToPositiveInfinity</c> is not a rule about halves at
    /// all but a ceiling, which carries 3.1 months up to 4 along with them.
    /// </para>
    /// <para>
    /// The table below is the W3C's own, and it is the only thing that tells the three apart: every case
    /// that is not a half agrees under all of them, and the halves are where two of the three are wrong.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class DurationRoundingTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

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

        // ---- The halves ------------------------------------------------------------------------------------

        [TestMethod]
        [DataRow("-3.9", "-P4M")]
        [DataRow("-3.5", "-P3M")]
        [DataRow("-3.1", "-P3M")]
        [DataRow("-0.9", "-P1M")]
        [DataRow("-0.5", "P0M")]
        [DataRow("-0.1", "P0M")]
        [DataRow("0.1", "P0M")]
        [DataRow("0.5", "P1M")]
        [DataRow("0.9", "P1M")]
        [DataRow("3.1", "P3M")]
        [DataRow("3.5", "P4M")]
        [DataRow("3.9", "P4M")]
        public void AMonthScaledLandsOnTheNearestMonthAndAHalfGoesUpwards(string factor, string expected)
        {
            // -3.5 months is -3 and not -4, and 0.5 is 1 and not 0: upwards means towards positive infinity
            // and not away from zero, so the two signs do not mirror one another.
            Assert.AreEqual(expected, Writes($"xs:yearMonthDuration('P1M') * {factor}"));
        }

        [TestMethod]
        [DataRow("-2", "-P2M")]
        [DataRow("-4", "-P1M")]
        [DataRow("-10", "P0M")]
        [DataRow("-50", "P0M")]
        [DataRow("50", "P0M")]
        [DataRow("10", "P1M")]
        [DataRow("4", "P1M")]
        [DataRow("2", "P3M")]
        public void TheSameRuleHoldsForDivision(string divisor, string expected)
        {
            // Five months over two is two and a half, which goes to three; over -2 it is -2.5, which goes
            // to -2. A ceiling would agree with both of these and then disagree about 5 div 4.
            Assert.AreEqual(expected, Writes($"xs:yearMonthDuration('P5M') div {divisor}"));
        }

        [TestMethod]
        public void TheSpecificationsOwnTwoExamples()
        {
            // 35 months times 2.3 is 80.5, and a half goes up: 81 months, which is P6Y9M.
            Assert.AreEqual("P6Y9M", Writes("xs:yearMonthDuration('P2Y11M') * 2.3"));

            // 35 months over 1.5 is 23.33, which is not a half and simply goes to the nearest.
            Assert.AreEqual("P1Y11M", Writes("xs:yearMonthDuration('P2Y11M') div 1.5"));
        }

        // ---- What is not rounded ---------------------------------------------------------------------------

        [TestMethod]
        public void ADayTimeDurationKeepsItsFractionOfASecond()
        {
            // The rounding is a property of the months, which are all a year-month duration has. A day-time
            // duration holds a fraction of a second and has nothing to round to, so scaling one is exact.
            Assert.AreEqual("PT9.3S", Writes("xs:dayTimeDuration('PT3.1S') * 3"));
            Assert.AreEqual("PT1.55S", Writes("xs:dayTimeDuration('PT3.1S') div 2"));
            Assert.AreEqual("PT16H3M20S", Writes("xs:dayTimeDuration('PT48H10M') div 3"));
        }
    }
}
