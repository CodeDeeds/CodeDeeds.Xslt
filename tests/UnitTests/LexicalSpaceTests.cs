namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what each built-in type will and will not read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A schema type is a value space and a <em>lexical space</em> — the set of texts that denote a value of
    /// it — and the second is as much a part of the type as the first. A type that admits anything is not
    /// the type it says it is: <c>xs:language</c> exists to mean "a language tag and nothing else", and if it
    /// takes <c>a*a</c> then a stylesheet asking for one has been told a lie.
    /// </para>
    /// <para>
    /// Most of these were accepted because .NET's own parsers are more generous than XML Schema.
    /// <c>double.TryParse</c> reads <c>nan</c> and <c>Infinity</c>; <c>decimal.TryParse</c> reads <c>.5</c>
    /// and <c>30.</c>; <c>Convert.FromBase64String</c> discards padding bits that the lexical space requires
    /// to be zero. Each one would have let this engine invent a value from text the specification says is
    /// not one. Every case here is drawn from the W3C QT3 suite.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class LexicalSpaceTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private static string Writes(string expression, string input = "<r/>")
        {
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        /// <summary>Asserts that a value is refused, and that the refusal names the lexical space.</summary>
        private static void Refuses(string expression)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression));
            Assert.AreEqual("FORG0001", error.Code, $"'{expression}': {error.Message}");
        }

        // ---- The string-derived types ----------------------------------------------------------------------

        [TestMethod]
        [DataRow("xs:language('')")]
        [DataRow("xs:language('abcdefjhl')")]
        [DataRow("xs:language('1')")]
        [DataRow("xs:language('a1a')")]
        [DataRow("xs:language('a.a')")]
        [DataRow("xs:language('abc-')")]
        [DataRow("xs:language('abc--ab')")]
        [DataRow("xs:language('abc-abcdefikl')")]
        public void ALanguageTagIsLettersAndThenShortGroups(string expression)
        {
            // The pattern is [a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*, and the length limit is the part most easily
            // lost: 'abcdefjhl' is nine letters, so it is not a language however much it looks like one.
            Refuses(expression);
        }

        [TestMethod]
        [DataRow("xs:language('en')", "en")]
        [DataRow("xs:language('en-GB')", "en-GB")]
        [DataRow("xs:language('es-419')", "es-419")]
        [DataRow("xs:language('abcdefgh-12345678')", "abcdefgh-12345678")]
        public void ARealLanguageTagIsStillAccepted(string expression, string expected)
        {
            // Digits are allowed after the first group, which is how the UN M.49 region codes are written.
            Assert.AreEqual(expected, Writes(expression));
        }

        [TestMethod]
        [DataRow("xs:Name('1abc')")]
        [DataRow("xs:Name('a c')")]
        [DataRow("xs:Name('')")]
        [DataRow("xs:NCName('')")]
        [DataRow("xs:NCName('a:b')")]
        [DataRow("xs:ID('')")]
        [DataRow("xs:IDREF('')")]
        [DataRow("xs:NMTOKEN('')")]
        [DataRow("xs:NMTOKEN(' ')")]
        [DataRow("xs:NMTOKEN(';')")]
        public void ANameIsAnXmlName(string expression)
        {
            Refuses(expression);
        }

        [TestMethod]
        public void ANameThatIsOneIsAccepted()
        {
            // A Name may carry a colon and an NCName may not, which is the whole difference between them.
            Assert.AreEqual("a:b", Writes("xs:Name('a:b')"));
            Assert.AreEqual("ab", Writes("xs:NCName('ab')"));
            Assert.AreEqual("1a", Writes("xs:NMTOKEN('1a')"));
        }

        [TestMethod]
        public void WhitespaceIsCollapsedBeforeTheTextIsJudged()
        {
            // These types all derive from xs:token, so the text is collapsed first and then matched. ' f f'
            // collapses to 'f f', which still holds a space and so is still not one token.
            Assert.AreEqual("ab", Writes("xs:NMTOKEN('  ab  ')"));
            Refuses("xs:NMTOKEN(' f f')");

            Assert.AreEqual("a b", Writes("xs:token('  a   b  ')"));
            Assert.AreEqual("a b", Writes("xs:normalizedString('a\tb')"));
        }

        [TestMethod]
        [DataRow("xs:anyURI('%gg')")]
        [DataRow("xs:anyURI('http://www.example.com/file%GF.html')")]
        [DataRow("xs:anyURI(':/cut.jpg')")]
        [DataRow("xs:anyURI(':/')")]
        public void AUriWithABadEscapeOrAnEmptySchemeIsRefused(string expression)
        {
            Refuses(expression);
        }

        [TestMethod]
        [DataRow("xs:anyURI('')", "")]
        [DataRow("xs:anyURI('http://example.com/a%20b')", "http://example.com/a%20b")]
        [DataRow("xs:anyURI('../relative/path')", "../relative/path")]
        public void AUriThatIsOneIsAccepted(string expression, string expected)
        {
            // The check is deliberately narrow: XML Schema admits very nearly any text here, and the empty
            // string and a relative reference are both values of this type.
            Assert.AreEqual(expected, Writes(expression));
        }

        // ---- Numbers ---------------------------------------------------------------------------------------

        [TestMethod]
        [DataRow("xs:double('nan')")]
        [DataRow("xs:double('naN')")]
        [DataRow("xs:float('nan')")]
        [DataRow("xs:double('Infinity')")]
        [DataRow("xs:double('1,000')")]
        [DataRow("xs:decimal('1e5')")]
        [DataRow("xs:double('1e')")]
        [DataRow("xs:double('e5')")]
        public void ANumberIsSpelledTheWaySchemaSpellsIt(string expression)
        {
            // The special values are NaN, INF and -INF, exactly so. Everything else .NET would read here
            // — 'Infinity', a thousands separator, a lone exponent — is text, not a number.
            Refuses(expression);
        }

        [TestMethod]
        public void TheInfinitiesAreSpelledTwoWays()
        {
            Assert.AreEqual("INF", Writes("xs:double('INF')"));
            Assert.AreEqual("-INF", Writes("xs:double('-INF')"));
            Assert.AreEqual("INF", Writes("xs:float('INF')"));

            // '+INF' is the third XML Schema 1.1 adds, and this engine reads values by 1.0 rules, so a
            // leading plus on an infinity is text rather than a number. The suite asks about it only
            // under an xsd-version 1.1 dependency, which the drivers skip.
            Refuses("xs:double('+INF')");
            Refuses("xs:float('+INF')");

            // And nothing else spells one at all.
            Refuses("xs:double('+Infinity')");
            Refuses("xs:double('inf')");
        }

        [TestMethod]
        [DataRow("xs:double('NaN')", "NaN")]
        [DataRow("xs:double('1e5')", "100000")]
        [DataRow("xs:double('.5')", "0.5")]
        [DataRow("xs:double('5.')", "5")]
        [DataRow("xs:double('-1.5E-3')", "-0.0015")]
        [DataRow("xs:decimal('5.')", "5")]
        public void ANumberSpelledThatWayIsStillRead(string expression, string expected)
        {
            Assert.AreEqual(expected, Writes(expression));
        }

        // ---- Durations, dates and the gregorian types -------------------------------------------------------

        [TestMethod]
        [DataRow("xs:duration('PT.5S')")]
        [DataRow("xs:duration('PT30.S')")]
        [DataRow("xs:dayTimeDuration('PT10M.5S')")]
        public void SecondsNeedADigitEachSideOfThePoint(string expression)
        {
            Refuses(expression);
        }

        [TestMethod]
        public void ADurationWithRealSecondsIsStillRead()
        {
            Assert.AreEqual("PT1.5S", Writes("xs:duration('PT1.5S')"));
            Assert.AreEqual("PT30S", Writes("xs:duration('PT30S')"));
        }

        [TestMethod]
        public void ACanonicalFormIsWrittenBackAsItWasRead()
        {
            // These forms are built a piece at a time into a fixed buffer, so what is worth pinning is the
            // longest each type can be: a dateTime carrying fractional seconds and an offset, and a duration
            // using every component at once.
            Assert.AreEqual(
                "2026-08-25T14:30:00.125+02:00", Writes("xs:dateTime('2026-08-25T14:30:00.125+02:00')"));

            Assert.AreEqual("2026-08-25T14:30:00Z", Writes("xs:dateTime('2026-08-25T14:30:00Z')"));
            Assert.AreEqual("2026-08-25T14:30:00", Writes("xs:dateTime('2026-08-25T14:30:00')"), "no timezone");
            Assert.AreEqual("14:30:00.125", Writes("xs:time('14:30:00.125')"));
            Assert.AreEqual("2026-08-25", Writes("xs:date('2026-08-25')"));

            Assert.AreEqual("P1Y2M", Writes("xs:yearMonthDuration('P1Y2M')"));
            Assert.AreEqual("P3DT4H5M6S", Writes("xs:dayTimeDuration('P3DT4H5M6S')"));
            Assert.AreEqual("-P400DT23H59M59.999S", Writes("xs:dayTimeDuration('-P400DT23H59M59.999S')"));

            // A duration with nothing in it still has to say something, and it says it in seconds unless it
            // is the one type that counts in months and cannot.
            Assert.AreEqual("PT0S", Writes("xs:dayTimeDuration('PT0S')"));
            Assert.AreEqual("P0M", Writes("xs:yearMonthDuration('P0M')"));
            Assert.AreEqual("PT0S", Writes("xs:duration('P0M')"));
            Assert.AreEqual("PT0S", Writes("xs:duration('PT0M')"));
        }

        [TestMethod]
        public void MidnightAtTheEndOfADayIsATimeOfNoneOfIt()
        {
            // 24:00:00 is the last moment of a day, and an xs:time has no day to carry it into: the hour
            // comes back to zero where it stands, so it is before 23:59:59 and not a second after it.
            Assert.AreEqual("00:00:00", Writes("xs:time('24:00:00')"));
            Assert.AreEqual("-PT23H59M59S", Writes("xs:time('24:00:00') - xs:time('23:59:59')"));
            Assert.AreEqual("false", Writes("xs:time('24:00:00') gt xs:time('23:59:59')"));

            // Which is also why a timezone can put one on the other side of the reference day from another:
            // 19:00 in -05:00 is the next midnight, and this one is not.
            Assert.AreEqual("false", Writes("xs:time('24:00:00Z') = xs:time('19:00:00-05:00')"));

            // An xs:dateTime does have a day, and there the same hour moves on to the next one.
            Assert.AreEqual("2000-01-01T00:00:00", Writes("xs:dateTime('1999-12-31T24:00:00')"));
            Assert.AreEqual("PT1S", Writes(
                "xs:dateTime('1999-12-31T24:00:00') - xs:dateTime('1999-12-31T23:59:59')"));
        }

        [TestMethod]
        [DataRow("xs:gYear('02004')")]
        [DataRow("xs:gYearMonth('02004-08')")]
        [DataRow("xs:date('02004-08-01')")]
        [DataRow("xs:date('00004-08-01')")]
        [DataRow("xs:dateTime('02004-08-01T12:44:05')")]
        public void AYearLongerThanFourDigitsCannotStartWithAZero(string expression)
        {
            // 02004 is not 2004 written differently; it is outside the lexical space, and reading it as 2004
            // would take a typo for a date.
            Refuses(expression);
        }

        [TestMethod]
        [DataRow("xs:gMonthDay('--02-31')")]
        [DataRow("xs:gMonthDay('--04-31')")]
        [DataRow("xs:gMonthDay('--11-31')")]
        public void ADayHasToExistInItsMonth(string expression)
        {
            Refuses(expression);
        }

        [TestMethod]
        public void FebruaryTakesTwentyNineDays()
        {
            // There is no year named, and the type means that day of every year — one in four of which has it.
            Assert.AreEqual("--02-29", Writes("xs:gMonthDay('--02-29')"));
            Assert.AreEqual("2004", Writes("xs:gYear('2004')"));
        }

        // ---- Binary ----------------------------------------------------------------------------------------

        [TestMethod]
        [DataRow("xs:base64Binary('AP9=')")]
        [DataRow("xs:base64Binary('Ay==')")]
        [DataRow("xs:base64Binary('frfhforlksid745323==')")]
        public void Base64PaddingBitsHaveToBeZero(string expression)
        {
            // A final group shorter than four characters carries bits no byte uses, and only the spelling
            // with those bits zero is a value. Otherwise two spellings would decode alike and one of them
            // could never be written back out.
            Refuses(expression);
        }

        [TestMethod]
        [DataRow("xs:base64Binary('AP8=')", "AP8=")]
        [DataRow("xs:base64Binary('/w==')", "/w==")]
        [DataRow("xs:base64Binary('SGVsbG8=')", "SGVsbG8=")]
        [DataRow("xs:base64Binary('1111')", "1111")]
        public void CanonicalBase64IsStillRead(string expression, string expected)
        {
            Assert.AreEqual(expected, Writes(expression));
        }

        // ---- Untyped values reaching a number ---------------------------------------------------------------

        [TestMethod]
        public void TextThatIsNotANumberSaysSoRatherThanBecomingNaN()
        {
            // Document content is untyped, and every place wanting a number casts it to xs:double. XPath 1.0
            // turns text that is not a number into NaN and carries on; 2.0 raises, on the grounds that
            // nothing downstream will make sense of NaN either.
            foreach (string expression in new[]
            {
                "/r/@n + 1",
                "/r/@n = 1",
                "avg(/r/@n)",
                "max(/r/@n)",
                "abs(/r/@n)",
            })
            {
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => Writes(expression, "<r n='three'/>"));

                Assert.AreEqual("FORG0001", error.Code, expression);
            }
        }

        [TestMethod]
        public void TextThatIsANumberStillWorksEverywhere()
        {
            Assert.AreEqual("4", Writes("/r/@n + 1", "<r n='3'/>"));
            Assert.AreEqual("true", Writes("/r/@n = 3", "<r n='3'/>"));
            Assert.AreEqual("3", Writes("avg(/r/@n)", "<r n='3'/>"));
        }

        // ---- Scaling a duration -----------------------------------------------------------------------------

        [TestMethod]
        public void ADurationDividedBySomethingEnormousHasNoLength()
        {
            // Dividing by an infinity leaves no duration, which is a value; multiplying by one leaves a
            // duration no representation can hold, which is an error.
            Assert.AreEqual("PT0S", Writes("xs:dayTimeDuration('P3D') div xs:double('INF')"));
            Assert.AreEqual("P0M", Writes("xs:yearMonthDuration('P3Y') div xs:double('-INF')"));
            Assert.AreEqual("PT0S", Writes("xs:dayTimeDuration('P3D') div xs:double('1.7976931348623157E308')"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("xs:dayTimeDuration('P3D') * xs:double('INF')"));

            Assert.AreEqual("FODT0002", error.Code);
        }

        [TestMethod]
        public void ADurationOfNoLengthScalesToItself()
        {
            // Nothing overflows because nothing grows, so a factor that would be a question about range for
            // any other duration is not one here.
            Assert.AreEqual("PT0S", Writes("xs:dayTimeDuration('PT0S') * xs:double('1.7976931348623157E308')"));
            Assert.AreEqual("PT0S", Writes("xs:dayTimeDuration('PT0S') * xs:double('-1.7976931348623157E308')"));
            Assert.AreEqual("P0M", Writes("xs:yearMonthDuration('P0M') * xs:double('INF')"));
        }

        [TestMethod]
        public void ScalingKeepsFractionalSecondsExact()
        {
            // The reason the arithmetic stays in decimal wherever it can: 3.1 * 3 through a double is
            // 9.299999999999999.
            Assert.AreEqual("PT9.3S", Writes("xs:dayTimeDuration('PT3.1S') * 3"));
        }

        [TestMethod]
        public void ADurationIsDividedRatherThanMultipliedByTheReciprocal()
        {
            // A third is 0.3333333333333333333333333333 in a decimal and not a third, so multiplying by it
            // leaves PT16H3M19.999999999999999999999994S where dividing leaves the answer.
            Assert.AreEqual("PT16H3M20S", Writes(
                "avg((xs:dayTimeDuration('P1DT12H'), xs:dayTimeDuration('PT12H30M'),"
                + " xs:dayTimeDuration('-PT20M')))"));

            Assert.AreEqual("PT5.01S", Writes("xs:dayTimeDuration('PT10.02S') div 2"));
        }

        [TestMethod]
        public void TheScaleTheArithmeticArrivedAtIsNotWritten()
        {
            // 10.02 halved is 5.010 in a decimal, which carries the scale of both operands. The canonical
            // form has no trailing zeros in it, so the zero the multiplication introduced is not written.
            Assert.AreEqual("PT5.01S", Writes("0.5 * xs:dayTimeDuration('PT10.02S')"));
            Assert.AreEqual("PT5.015S", Writes("0.5 * xs:dayTimeDuration('PT10.03S')"));
            Assert.AreEqual("PT30.06S", Writes("xs:dayTimeDuration('PT10.02S') * 3"));
        }

        [TestMethod]
        public void MovingAMomentByAFractionOfASecondIsExact()
        {
            // The seconds are added as ticks and not through AddSeconds, which takes a double: 446400.3 is
            // not one, and the ticks it truncates to would land three ten-millionths short.
            Assert.AreEqual(
                "04:12:00.3Z", Writes("xs:time('00:12:00Z') + xs:dayTimeDuration('P5DT4H0M0.3S')"));

            Assert.AreEqual(
                "20:11:59.7Z", Writes("xs:time('00:12:00Z') - xs:dayTimeDuration('P5DT4H0M0.3S')"));
        }

        // ---- Writing a number, which is the lexical space in the other direction --------------------------

        [TestMethod]
        [DataRow("xs:double('INF')", "INF")]
        [DataRow("xs:double('-INF')", "-INF")]
        [DataRow("xs:double('NaN')", "NaN")]
        [DataRow("xs:double('0')", "0")]
        [DataRow("xs:double('-0')", "-0")]
        public void TheSpecialValuesAreSpelledAsSchemaSpellsThem(string expression, string expected)
        {
            // XPath 1.0 writes Infinity and has one zero to write. These are the 2.0 spellings, and they are
            // also the ones the type will read back: 'Infinity' is not in xs:double's lexical space at all,
            // so writing it would produce text this engine itself refuses.
            Assert.AreEqual(expected, Writes(expression));
        }

        [TestMethod]
        [DataRow("xs:double('1')", "1")]
        [DataRow("xs:double('1.5')", "1.5")]
        [DataRow("xs:double('-1.5')", "-1.5")]
        [DataRow("xs:double('999999')", "999999")]
        [DataRow("xs:double('0.000001')", "0.000001")]
        public void AReadableMagnitudeIsWrittenWithoutAnExponent(string expression, string expected)
        {
            // The window is 0.000001 inclusive to 1000000 exclusive, and inside it a double is written as a
            // plain decimal number.
            Assert.AreEqual(expected, Writes(expression));
        }

        [TestMethod]
        [DataRow("xs:double('1000000')", "1.0E6")]
        [DataRow("xs:double('0.0000001')", "1.0E-7")]
        [DataRow("xs:double('1.7976931348623157E308')", "1.7976931348623157E308")]
        [DataRow("xs:double('-1.7976931348623157E308')", "-1.7976931348623157E308")]
        public void OutsideItTheNumberIsWrittenWithOne(string expression, string expected)
        {
            // The mantissa is between one and ten and always carries a decimal point, so a whole one is
            // written 1.0 rather than 1; and the exponent has no plus sign and no leading zeros, which is
            // where .NET's own E+06 would have differed.
            Assert.AreEqual(expected, Writes(expression));
        }

        [TestMethod]
        public void AFloatIsWrittenAsAFloatAndNotAsTheDoubleHoldingIt()
        {
            // An xs:float is held here as a double that has been through float, so asking the double for its
            // shortest round trip answers with the digits of a double: 3.3 comes back 3.299999952316284.
            Assert.AreEqual("3.3", Writes("xs:float('3.3')"));
            Assert.AreEqual("3.4028235E38", Writes("xs:float('3.4028235E38')"));
            Assert.AreEqual("-3.4028235E38", Writes("xs:float('-3.4028235E38')"));
        }

        // ---- The list types --------------------------------------------------------------------------------

        [TestMethod]
        public void TheThreeListTypesAreCastFromTheTokensInTheText()
        {
            // XPath 3.0 defines a cast to the three list types a processor without a schema has: the text is
            // split on whitespace and each token cast to the item type, so what comes back is a sequence
            // rather than a single value — the one cast that produces more than one item.
            Assert.AreEqual("a b c", Writes("string-join('a b c' cast as xs:NMTOKENS, ' ')"));
            Assert.AreEqual("3", Writes("count(' a b c ' cast as xs:NMTOKENS)"));
            Assert.AreEqual("true", Writes("'1 2 3 ' castable as xs:NMTOKENS"));

            // An xs:IDREF and an xs:ENTITY are each an NCName, so a token beginning with a digit is neither
            // — where an xs:NMTOKEN takes name characters with nothing required of the first.
            Assert.AreEqual("true", Writes("' aa bb c1.2 ' castable as xs:IDREFS"));
            Assert.AreEqual("false", Writes("'1 2 3 ' castable as xs:IDREFS"));
            Assert.AreEqual("false", Writes("' 1/2 ' castable as xs:ENTITIES"));

            // All three have a minimum length of one, so text holding no tokens is a value of none of them.
            Assert.AreEqual("false", Writes("'' castable as xs:NMTOKENS"));
            Assert.AreEqual("false", Writes("' ' castable as xs:IDREFS"));
            Assert.AreEqual("false", Writes("' &#10; ' castable as xs:ENTITIES"));

            // And a list type is made from text and from nothing else: there is no reading of a number as a
            // list of names.
            Assert.AreEqual("false", Writes("1 castable as xs:NMTOKENS"));
        }
    }
}
