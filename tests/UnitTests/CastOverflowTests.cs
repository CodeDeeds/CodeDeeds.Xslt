namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the difference between text that names no value and text that names one this engine
    /// cannot hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two complaints that are easy to run together and that the specification deliberately keeps apart.
    /// <c>xs:gYear('2004-13')</c> names no year and never will, whatever the processor is built on;
    /// <c>xs:gYear('99999999999')</c> names one perfectly well and this engine holds years in an
    /// <see cref="int"/>. The first is <c>FORG0001</c>, a value outside the type; the second is
    /// <c>FODT0001</c>, an overflow — and a reader who is told the wrong one goes looking for a typo that
    /// is not there, or files a bug against a limit that is documented.
    /// </para>
    /// <para>
    /// The same split runs through the durations, where <c>FODT0002</c> is the overflow. The integers
    /// no longer have one: <c>xs:integer</c> is unbounded here, so the only way a cast to one fails is
    /// by naming a value the target type excludes, which is <c>FORG0001</c> whatever its size.
    /// Every case here is drawn from the W3C QT3 suite.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class CastOverflowTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private static string Writes(string expression, string attributes = "")
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl} {attributes}>"
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

        /// <summary>Asserts that an expression is refused, and under the code named.</summary>
        private static void Refuses(string code, string expression)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression));
            Assert.AreEqual(code, error.Code, $"'{expression}': {error.Message}");
        }

        // ---- The Gregorian types ---------------------------------------------------------------------------

        [TestMethod]
        public void AYearTooBigToHoldIsAnOverflowAndNotATypo()
        {
            // Digits and nothing but digits, and more of them than an int holds. The text says which year it
            // means, so the complaint is this engine's range and not the lexical space.
            Refuses("FODT0001", "xs:gYear('99999999999')");
            Refuses("FODT0001", "xs:gYearMonth('99999999999-05')");
            Refuses("FODT0001", "'-99999999999' cast as xs:gYear");
        }

        [TestMethod]
        public void AYearTooBigInTextThatIsWrongAnywayIsStillATypo()
        {
            // 'XX' is no month, so this text would not be a gYearMonth whatever year stood in front of it.
            // Reporting the year's size would send a reader after the wrong half of the problem.
            Refuses("FORG0001", "'99999999999999999999999999999-XX' cast as xs:gYearMonth");
        }

        [TestMethod]
        public void ThereIsNoYearZero()
        {
            // XSD 1.0 counts 1 BCE as -0001, so 0000 names no year at all; XSD 1.1 renumbered the era and
            // this engine reads values by 1.0 rules. Not an overflow: nothing was too big.
            Refuses("FORG0001", "xs:gYear('0000')");
            Refuses("FORG0001", "xs:gYearMonth('0000-05')");
            Assert.AreEqual("false", Writes("'0000' castable as xs:gYear"));
            Assert.AreEqual("-0001", Writes("xs:gYear('-0001')"));
        }

        // ---- The durations ---------------------------------------------------------------------------------

        [TestMethod]
        public void ADurationTooBigToHoldIsAnOverflowAndNotATypo()
        {
            // A duration carries its months in an int here, and P100000000000Y is a duration that says so
            // plainly. FODT0002 is the durations' own overflow, as FODT0001 is the dates'.
            Refuses("FODT0002", "xs:yearMonthDuration('P100000000000Y')");
            Refuses("FODT0002", "xs:yearMonthDuration('P200000000Y6M')");

            // Where the text is not a duration at all, its size is beside the point.
            Refuses("FORG0001", "xs:yearMonthDuration('P100000000000')");
        }

        // ---- The integers ----------------------------------------------------------------------------------

        [TestMethod]
        public void AnIntegerOutsideItsTypeIsInvalidAndNotAnOverflow()
        {
            // 9223372036854775808 is one past xs:long's own maximum, so no processor holds it as an xs:long
            // however it is built. That is a value outside the type, which is what FORG0001 says.
            Refuses("FORG0001", "xs:long('9223372036854775808')");
            Refuses("FORG0001", "xs:long('-9223372036854775809')");
            Refuses("FORG0001", "xs:unsignedLong('18446744073709551616')");
        }

        [TestMethod]
        public void AnIntegerInsideItsTypeIsHeldHoweverWideItIs()
        {
            // 18446744073709551615 is exactly xs:unsignedLong's maximum, so it is a value of the type.
            // It was FOAR0002 here for as long as an xs:integer was a 64-bit one and no wider; now that
            // it is held, the only thing left for a cast to refuse is a value the type itself excludes.
            Assert.AreEqual(
                "18446744073709551615", Writes("xs:unsignedLong('18446744073709551615')"));

            Assert.AreEqual(
                "10000000000000000000", Writes("xs:unsignedLong('10000000000000000000')"));
        }

        [TestMethod]
        public void TextThatIsNotAnIntegerIsStillInvalid()
        {
            // A decimal or an exponent names an integer often enough, but neither is in xs:integer's lexical
            // space, and the size of what it names changes nothing.
            Refuses("FORG0001", "xs:integer('1.5')");
            Refuses("FORG0001", "xs:integer('1e400')");
            Refuses("FORG0001", "xs:integer('')");
        }

        // ---- Constructor arity -----------------------------------------------------------------------------

        [TestMethod]
        public void AConstructorTakesAtMostOneValue()
        {
            // Every constructor is declared xs:anyAtomicType? -> T?, so two values in is the argument being
            // of the wrong type rather than a value that will not convert: XPTY0004, not FORG0001.
            Refuses("XPTY0004", "xs:integer((1, 2))");
            Refuses("XPTY0004", "xs:string(('a', 'b'))");

            // One and none are both fine, and none comes back as none.
            Assert.AreEqual("1", Writes("xs:integer((1))"));
            Assert.AreEqual(string.Empty, Writes("xs:integer(())"));
        }

        // ---- Names -----------------------------------------------------------------------------------------

        [TestMethod]
        public void AnUnprefixedNameCastToAQNameTakesTheDefaultElementNamespace()
        {
            // The default element/type namespace, which a stylesheet declares with xpath-default-namespace.
            // Not what xmlns says: XPath deliberately does not read that binding for this.
            const string Declared = "xpath-default-namespace=\"http://example.com/defelementns\"";

            Assert.AreEqual(
                "http://example.com/defelementns",
                Writes("namespace-uri-from-QName(xs:QName('ncname'))", Declared));

            Assert.AreEqual(
                "http://example.com/defelementns",
                Writes("namespace-uri-from-QName('ncname' cast as xs:QName)", Declared));

            // And with none declared, an unprefixed name is in no namespace.
            Assert.AreEqual(string.Empty, Writes("namespace-uri-from-QName(xs:QName('ncname'))"));
        }
    }
}
