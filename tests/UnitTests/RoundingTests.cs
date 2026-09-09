namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for rounding a double at a number of decimal places, and for the three numbers whose names are
    /// words.
    /// </summary>
    /// <remarks>
    /// What these hold down is that a double is a binary fraction and almost never the decimal it was
    /// written as. Scaling one by a power of ten and rounding answers a question about the scaled value
    /// rather than about the value, and the two differ exactly where it matters — on the halves.
    /// </remarks>
    [TestClass]
    public sealed class RoundingTests
    {
        private const string Xsl =
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private static string Writes(string expression, string version = "2.0")
        {
            string stylesheet = $"<xsl:stylesheet version=\"{version}\" {Xsl}>"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\" separator=\",\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            string written = new Xslt(
                stylesheet,
                new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true }).TransformXml("<r/>");

            return written == "<out/>" ? string.Empty : written["<out>".Length..^"</out>".Length];
        }

        [TestMethod]
        public void RoundingADoubleAsksTheValueItReallyIs()
        {
            // 13.65 is really 13.6500000000000003552…, which is above the half, so rounding it towards
            // positive infinity gives 13.7 and rounding -13.65 gives -13.7. Multiplying either by ten
            // lands it exactly on a half, the difference being smaller than the spacing of doubles there,
            // and the answer is then decided by a tie that was never in the value.
            Assert.AreEqual("13.7,-13.7", Writes("round(1.365e1, 1), round(-1.365e1, 1)"));

            // 250.025 is really 250.0250000000000056…, above the half; 150.015 is really
            // 150.0149999999999863…, below it. So half-to-even rounds the first up and the second down,
            // and neither of them is the tie it looks like.
            Assert.AreEqual(
                "180.02,150.01,250.03",
                Writes(
                    "round-half-to-even(180.0180e0, 2), round-half-to-even(150.0150e0, 2),"
                    + " round-half-to-even(250.0250e0, 2)"));

            Assert.AreEqual(
                "-120.01,-150.01,-250.03",
                Writes(
                    "round-half-to-even(-120.0120e0, 2), round-half-to-even(-150.0150e0, 2),"
                    + " round-half-to-even(-250.0250e0, 2)"));

            // A half that really is one goes to even for the one function and towards positive infinity
            // for the other, which is the whole difference between them.
            Assert.AreEqual(
                "2,-2,3,-2",
                Writes(
                    "round-half-to-even(2.5e0), round-half-to-even(-2.5e0), round(2.5e0), round(-2.5e0)"));
        }

        [TestMethod]
        public void RoundingKeepsTheSignOfAZeroAndTheTypeItCameIn()
        {
            // Both zeros are values of their own and write differently, so a negative that rounds to zero
            // keeps its sign.
            Assert.AreEqual("-0,0", Writes("round(-0.2e0), round(0.2e0)"));
            Assert.AreEqual("-0", Writes("round-half-to-even(-3.0e0, -2)"));

            // An integer rounded to the hundreds is an integer, which has one zero and not two.
            Assert.AreEqual("0,8500,-8400", Writes("round(-3, -2), round(8450, -2), round(-8450, -2)"));

            // A decimal is rounded exactly, in the type it came in.
            Assert.AreEqual("35600,1.235", Writes("round(35612.25, -2), round(1.2345, 3)"));
        }

        [TestMethod]
        public void RoundingReachesTheEndsOfTheRange()
        {
            // Places far outside the value's own scale, where scaling by a power of ten would be a
            // multiplication by something that is not itself exact.
            Assert.AreEqual("5.0E101", Writes("round(xs:double(45e100), -101)"));
            Assert.AreEqual("1.0E-98", Writes("round(xs:double(9.9e-99), 99)"));

            // Keeping more places than any double has digits changes nothing; rounding above every double
            // leaves a zero of the value's own sign.
            Assert.AreEqual("1.5,-0", Writes("round(1.5e0, 400), round(-1.5e0, -400)"));

            // The three that have no digits to round.
            Assert.AreEqual(
                "NaN,INF,-INF",
                Writes("round(xs:double('NaN'), 2), round(xs:double('INF'), 2), round(xs:double('-INF'), 2)"));
        }

        [TestMethod]
        public void TheThreeNumbersWhoseNamesAreWords()
        {
            // fn:number is a cast to xs:double from XPath 2.0 on, and the lexical space of xs:double takes
            // these three. What the cast will not read is NaN rather than an error, which is the one thing
            // the function keeps from 1.0.
            Assert.AreEqual(
                "INF,-INF,NaN,NaN",
                Writes("number('INF'), number('-INF'), number('NaN'), number('nonsense')"));

            Assert.AreEqual("INF", Writes("number('INF') + 3"));

            // XPath 1.0's own grammar for a number has a sign, digits and a point and nothing else, so
            // there the three of them are words like any other.
            Assert.AreEqual("NaN,NaN,NaN", Writes("number('INF'), number('-INF'), number('NaN')", "1.0"));
        }
    }
}
