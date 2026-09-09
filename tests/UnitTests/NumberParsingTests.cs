using System.Globalization;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for XPath number conversion.
    /// </summary>
    /// <remarks>
    /// <see cref="XPathValue.ParseNumber"/> carries a fast path that reconstructs the value from the digits it
    /// scanned rather than deferring to the framework parser. That is only sound while the mantissa and the
    /// power of ten involved are both exactly representable, so these tests check it agrees with
    /// <see cref="double.Parse(string, IFormatProvider)"/> bit for bit — including on the inputs that must fall
    /// off the fast path.
    /// </remarks>
    [TestClass]
    public sealed class NumberParsingTests
    {
        private static void AssertMatchesFrameworkParse(string text)
        {
            double expected = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            double actual = XPathValue.ParseNumber(text);

            Assert.AreEqual(
                BitConverter.DoubleToInt64Bits(expected),
                BitConverter.DoubleToInt64Bits(actual),
                $"\"{text}\" parsed as {actual:R}, but the framework gives {expected:R}.");
        }

        [TestMethod]
        public void FastPathAgreesWithTheFrameworkParser()
        {
            string[] values =
            {
                "0", "1", "-1", "42", "-42",
                "0.5", "-0.5", "1250.00", "3.14159", "0.1", "0.2", "0.3",
                "00042", "1.10", "0.0000001",
                "123456789", "123456789012345",
                "9007199254740992",      // exactly 2^53, the last integer the fast path may claim
                "0.000000000000000001",  // eighteen fraction digits, still within the exact powers of ten
            };

            foreach (string value in values)
            {
                AssertMatchesFrameworkParse(value);
            }
        }

        [TestMethod]
        public void ValuesBeyondTheExactRangeFallBackAndStayCorrect()
        {
            string[] values =
            {
                "9007199254740993",                  // 2^53 + 1: no longer exactly representable
                "12345678901234567890",              // far past the mantissa limit
                "123456789012345678901234567890",
                "0.12345678901234567890123456789",   // more fraction digits than there are exact powers of ten
                "1.7976931348623157",
                "0.1000000000000000055511151231257827",
            };

            foreach (string value in values)
            {
                AssertMatchesFrameworkParse(value);
            }
        }

        [TestMethod]
        public void RandomValuesAtManyPrecisionsAgreeWithTheFrameworkParser()
        {
            // A generated sweep catches boundary cases a hand-written list would not think to include.
            Random random = new Random(20260821);

            for (int i = 0; i < 20000; i++)
            {
                long mantissa = (long)(random.NextDouble() * long.MaxValue);
                int fractionDigits = random.Next(0, 25);

                string digits = mantissa.ToString(CultureInfo.InvariantCulture);
                string text = fractionDigits == 0 || fractionDigits >= digits.Length
                    ? digits
                    : digits.Insert(digits.Length - fractionDigits, ".");

                AssertMatchesFrameworkParse(text);
                AssertMatchesFrameworkParse("-" + text);
            }
        }

        [TestMethod]
        public void NegativeZeroKeepsItsSignButPrintsAsZero()
        {
            double parsed = XPathValue.ParseNumber("-0");

            Assert.IsTrue(double.IsNegative(parsed), "the sign must survive parsing");
            Assert.AreEqual("0", XPathValue.NumberToString(parsed), "but XPath prints it without the sign");
        }

        [TestMethod]
        public void SyntaxXPathDoesNotAcceptStillYieldsNaN()
        {
            // The fast path must not accidentally accept forms the grammar excludes.
            string[] invalid =
            {
                "", "   ", "-", ".", "-.", "+1", "1e3", "1E3", "0x10",
                "1.2.3", "1,000", "NaN", "Infinity", "abc", "1 2", "--1", "1-",
            };

            foreach (string text in invalid)
            {
                Assert.IsTrue(
                    double.IsNaN(XPathValue.ParseNumber(text)),
                    $"\"{text}\" is not a valid XPath number and must convert to NaN.");
            }
        }

        [TestMethod]
        public void SurroundingWhitespaceIsAllowed()
        {
            Assert.AreEqual(1.5, XPathValue.ParseNumber("  1.5  "));
            Assert.AreEqual(-1.5, XPathValue.ParseNumber("\t-1.5\r\n"));
            Assert.AreEqual(42.0, XPathValue.ParseNumber("\n42"));
        }

        [TestMethod]
        public void ExtremeMagnitudesAreWrittenOutInFull()
        {
            // XPath 1.0 has no exponent notation, so these really are three hundred digits — far past the
            // buffer a short string is built in, which is the case that has to reach the heap instead.
            string large = XPathValue.NumberToString(1e300);
            Assert.AreEqual(301, large.Length);
            Assert.AreEqual("1", large.TrimEnd('0'), "a power of ten is a one followed by zeros");

            Assert.AreEqual("0." + new string('0', 299) + "1", XPathValue.NumberToString(1e-300));
            Assert.AreEqual("-" + large, XPathValue.NumberToString(-1e300));

            // And whatever was written has to read back as the same value.
            Assert.AreEqual(1e300, XPathValue.ParseNumber(large));
            Assert.AreEqual(1e-300, XPathValue.ParseNumber(XPathValue.NumberToString(1e-300)));
        }

        [TestMethod]
        public void FormattingRoundTripsThroughParsing()
        {
            Random random = new Random(981);

            for (int i = 0; i < 5000; i++)
            {
                double original = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-15, 16));
                string formatted = XPathValue.NumberToString(original);

                // XPath's own output must always be readable as an XPath number again.
                double reparsed = XPathValue.ParseNumber(formatted);
                Assert.AreEqual(
                    original,
                    reparsed,
                    $"{original:R} formatted as \"{formatted}\" reparsed as {reparsed:R}.");
            }
        }
    }
}
