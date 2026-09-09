namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:format-integer</c>.
    /// </summary>
    /// <remarks>
    /// The picture language here is not the one <c>xsl:number</c> uses, and the difference is worth keeping
    /// in view while reading these: <c>xsl:number</c> renders a list of numbers, so its format alternates
    /// tokens with the literal text between them, while this renders one number, so every character in the
    /// picture belongs to it and the punctuation between digits means grouping.
    /// </remarks>
    [TestClass]
    public sealed class FormatIntegerTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private static string Writes(string expression, string version = "3.0")
        {
            string stylesheet = $"<xsl:stylesheet version=\"{version}\" {Xsl}>"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) =>
                new XsltOptions { Backend = backend, OmitXmlDeclaration = true };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        /// <summary>The error code raised by an expression, so a picture can be checked for being refused.</summary>
        private static string Refuses(string expression)
        {
            return Assert.ThrowsExactly<XsltException>(() => Writes(expression)).Code ?? string.Empty;
        }

        /// <summary>Renders one number through one picture, which is what most of these ask.</summary>
        private static string Format(long value, string picture)
        {
            return Writes($"format-integer({value}, '{picture}')");
        }

        [TestMethod]
        public void WordsAreSpelledInTheLanguageTheThirdArgumentNames()
        {
            Assert.AreEqual("einundzwanzig", Writes("format-integer(21, 'w', 'de')"));

            // The variation inside the parentheses is the ordinal ending German inflects for, and it is the
            // same thing the ordinal attribute of xsl:number holds.
            Assert.AreEqual("Erster", Writes("format-integer(1, 'Ww;o(-er)', 'de')"));
            Assert.AreEqual("Zwanzigste", Writes("format-integer(20, 'Ww;o(%spellout-ordinal)', 'de')"));

            // A language this engine does not spell falls back to English rather than being refused, which
            // is what the specification asks of a processor that does not have the one wanted.
            Assert.AreEqual("twenty-one", Writes("format-integer(21, 'w', 'fr')"));
            Assert.AreEqual("twenty-one", Writes("format-integer(21, 'w', ())"));
        }

        [TestMethod]
        public void DigitsArePaddedToThePicturesWidth()
        {
            Assert.AreEqual("123", Format(123, "1"));
            Assert.AreEqual("123", Format(123, "001"));
            Assert.AreEqual("00123", Format(123, "00001"));

            // The picture asks for a minimum, not a maximum, so a number wider than it comes out whole.
            Assert.AreEqual("123456", Format(123456, "001"));

            Assert.AreEqual("0", Format(0, "0"));
            Assert.AreEqual("000", Format(0, "000"));
            Assert.AreEqual("00000", Format(0, "00000"));
        }

        [TestMethod]
        public void TheSignGoesInFrontOfThePadding()
        {
            // Not "00-123": the padding is part of writing the number, and the sign is written before it.
            Assert.AreEqual("-123", Format(-123, "9"));
            Assert.AreEqual("-123", Format(-123, "999"));
            Assert.AreEqual("-00123", Format(-123, "99999"));
        }

        [TestMethod]
        public void ThePictureNamesANumberingSequence()
        {
            Assert.AreEqual("a|b|c|d", Writes("string-join(for $i in 1 to 4 return format-integer($i, 'a'), '|')"));
            Assert.AreEqual("A|B|C|D", Writes("string-join(for $i in 1 to 4 return format-integer($i, 'A'), '|')"));

            Assert.AreEqual(
                "i|ii|iii|iv|v|vi|vii|viii|ix|x",
                Writes("string-join(for $i in 1 to 10 return format-integer($i, 'i'), '|')"));

            Assert.AreEqual(
                "I|II|III|IV|V|VI|VII|VIII|IX|X",
                Writes("string-join(for $i in 1 to 10 return format-integer($i, 'I'), '|')"));

            // Neither sequence can write zero, and roman numerals stop short of 5000. Both fall back to
            // digits rather than refusing, which is what the specification asks for.
            Assert.AreEqual("0", Format(0, "i"));
            Assert.AreEqual("5000", Format(5000, "I"));
        }

        [TestMethod]
        public void WordsComeInThreeCases()
        {
            Assert.AreEqual(
                "one|two|three|four|five|six|seven|eight|nine|ten",
                Writes("string-join(for $i in 1 to 10 return format-integer($i, 'w'), '|')"));

            Assert.AreEqual(
                "ONE|TWO|THREE|FOUR|FIVE|SIX|SEVEN|EIGHT|NINE|TEN",
                Writes("string-join(for $i in 1 to 10 return format-integer($i, 'W'), '|')"));

            Assert.AreEqual(
                "One|Two|Three|Four|Five|Six|Seven|Eight|Nine|Ten",
                Writes("string-join(for $i in 1 to 10 return format-integer($i, 'Ww'), '|')"));

            Assert.AreEqual("Zero", Format(0, "Ww"));
            Assert.AreEqual("Eleven", Format(11, "Ww"));
        }

        [TestMethod]
        public void WordsScaleUpThroughHundredsAndThousands()
        {
            Assert.AreEqual("one hundred", Format(100, "w"));
            Assert.AreEqual("one hundred and twenty-three", Format(123, "w"));
            Assert.AreEqual("one thousand two hundred and thirty-four", Format(1234, "w"));

            // A remainder under a hundred joins what came before it with "and"; a larger one starts a phrase
            // of its own.
            Assert.AreEqual("one million and one", Format(1000001, "w"));
            Assert.AreEqual("one million one hundred", Format(1000100, "w"));

            Assert.AreEqual("Twenty-First", Format(21, "Ww;o"));
            Assert.AreEqual(
                "nine quintillion two hundred and twenty-three quadrillion three hundred and seventy-two "
                + "trillion thirty-six billion eight hundred and fifty-four million seven hundred and "
                + "seventy-five thousand eight hundred and seven",
                Format(long.MaxValue, "w"));
        }

        [TestMethod]
        public void OrdinalsAreAskedForWithASemicolonAndAnO()
        {
            Assert.AreEqual(
                "1st|2nd|3rd|4th|5th|6th|7th|8th|9th|10th|11th|12th|13th",
                Writes("string-join(for $i in 1 to 13 return format-integer($i, '1;o'), '|')"));

            // Eleven through thirteen take "th" against what their last digit suggests, and so does every
            // number that ends in them.
            Assert.AreEqual("21st|22nd|23rd", Writes(
                "string-join(for $i in 21 to 23 return format-integer($i, '1;o'), '|')"));
            Assert.AreEqual("111th", Format(111, "1;o"));
            Assert.AreEqual("101st", Format(101, "1;o"));

            Assert.AreEqual("-85th", Format(-85, "1;o"));
        }

        [TestMethod]
        public void OrdinalWordsChangeOnlyTheLastWord()
        {
            Assert.AreEqual("zeroth", Format(0, "w;o"));
            Assert.AreEqual("one hundredth", Format(100, "w;o"));
            Assert.AreEqual("one thousandth", Format(1000, "w;o"));
            Assert.AreEqual("one hundred and twenty-first", Format(121, "w;o"));
            Assert.AreEqual("twelfth", Format(12, "w;o"));
            Assert.AreEqual("fortieth", Format(40, "w;o"));
            Assert.AreEqual("ninth", Format(9, "w;o"));

            Assert.AreEqual("SECOND", Format(2, "W;o"));
            Assert.AreEqual("-Fifth", Format(-5, "Ww;o"));
        }

        [TestMethod]
        public void RegularSeparatorsRepeatAndIrregularOnesStayPut()
        {
            // One separator sitting where a regular pattern would put it describes a habit, so it carries on
            // leftwards however long the number is.
            Assert.AreEqual("1,500,000", Format(1500000, "0,000"));
            Assert.AreEqual("1,500,000", Format(1500000, "#,###,000"));
            Assert.AreEqual("12 345 678 901", Format(12345678901, "# 000"));
            Assert.AreEqual("1,23,45,67,89", Format(123456789, "00,00,00"));

            // These do not: a regular pattern of two would also have wanted a separator after the sixth
            // digit, and this one has none, so its separators stay where they were written.
            Assert.AreEqual("12345,67,89", Format(123456789, "000,00,00"));
            Assert.AreEqual("12345,6,78,9", Format(123456789, "0,0,00,0"));

            // Different separators are irregular by definition, and one that would land at the very front of
            // the number is dropped rather than left dangling.
            Assert.AreEqual("602)347-826", Format(602347826, "#(000)000-000"));
        }

        [TestMethod]
        public void ASemicolonIsAlsoAGroupingSeparator()
        {
            // Which is why the modifier is separated by the *last* semicolon: this picture groups with
            // semicolons and has no modifier at all.
            Assert.AreEqual("1;234", Format(1234, "#;##1;"));
            Assert.AreEqual("001", Format(1, "001;"));
        }

        [TestMethod]
        public void ThePicturesDigitsChooseTheFamilyToWriteIn()
        {
            // Arabic-Indic digits: the picture is written with one of them and the output uses all of them.
            Assert.AreEqual("١٠", Format(10, "١"));
            Assert.AreEqual("٢٠", Format(20, "٩"));

            // Osmanya digits are outside the basic plane, so the picture and the result are both read and
            // written a code point at a time rather than a char at a time.
            Assert.AreEqual(
                "\U000104A1,\U000104A2\U000104A3\U000104A4", Format(1234, "#,\U000104A0\U000104A0\U000104A0"));
        }

        [TestMethod]
        public void AnUnrecognisedTokenFallsBackToPlainDigits()
        {
            // The specification leaves the set of numbering sequences to the processor and says an unknown
            // one is not an error, so these produce numbers rather than messages.
            Assert.AreEqual("1500000", Format(1500000, "#"));
            Assert.AreEqual("1500000", Format(1500000, "#a"));
            Assert.AreEqual("1234", Format(1234, "bb"));
            Assert.AreEqual("1234", Format(1234, "&#10;"));

            // The modifier still applies to the fallback.
            Assert.AreEqual("1234th", Format(1234, "()Ww;o"));
        }

        [TestMethod]
        public void AnIllFormedPictureIsRefused()
        {
            Assert.AreEqual("FODF1310", Refuses("format-integer(1, '')"));
            Assert.AreEqual("FODF1310", Refuses("format-integer(1, ';')"));

            // A grouping separator may not start or end the pattern, nor sit beside another.
            Assert.AreEqual("FODF1310", Refuses("format-integer(1500000, ',123')"));
            Assert.AreEqual("FODF1310", Refuses("format-integer(1500000, '0,000,')"));
            Assert.AreEqual("FODF1310", Refuses("format-integer(1500000, '0,00,,000')"));

            // An optional digit says "no wider than this unless you must be", which means nothing once the
            // digits that have to appear have started.
            Assert.AreEqual("FODF1310", Refuses("format-integer(1500000, '11#0,000')"));
            Assert.AreEqual("FODF1310", Refuses("format-integer(123, '0#')"));

            // A letter is not a grouping separator, so this is the mistake it looks like rather than a
            // number grouped by the letter o.
            Assert.AreEqual("FODF1310", Refuses("format-integer(-1, '1o')"));

            // Every digit in the pattern has to name the same family.
            Assert.AreEqual("FODF1310", Refuses("format-integer(1234, '123١')"));
        }

        [TestMethod]
        public void AnIllFormedModifierIsRefused()
        {
            Assert.AreEqual("FODF1310", Refuses("format-integer(1, '1;o(-er)z')"));
            Assert.AreEqual("FODF1310", Refuses("format-integer(1234, 'Ww;o()(')"));
            Assert.AreEqual("FODF1310", Refuses("format-integer(1234, 'Ww;o(')"));
            Assert.AreEqual("FODF1310", Refuses("format-integer(1234, 'Ww;x')"));
        }

        [TestMethod]
        public void APictureIsReadWhereItIsUsedRatherThanWhereItIsWritten()
        {
            // FODF1310 is a dynamic error, so a picture that will never be read must not stop the
            // expression compiling — even though this one is a literal and is read at compile time when it
            // parses.
            Assert.AreEqual("ok", Writes("if (false()) then format-integer(1, ';') else 'ok'"));

            // And a computed picture is read per call, which is the only way it could be.
            Assert.AreEqual("00042", Writes("format-integer(42, concat('0000', '0'))"));
        }

        [TestMethod]
        public void TheModifiersThatMakeNoDifferenceHereAreStillAccepted()
        {
            // 'a' and 't' choose between alphabetic and traditional numbering in languages that have both.
            // Neither language here does, so they are accepted and change nothing.
            Assert.AreEqual("One", Format(1, "Ww;t"));
            Assert.AreEqual("One", Format(1, "Ww;a"));
            Assert.AreEqual("One", Format(1, "Ww;c"));

            // English and German are the languages available, and the specification wants a processor
            // without the one asked for to use a language it has rather than refuse — including where what
            // was asked for is not a language at all.
            Assert.AreEqual("Eleven", Writes("format-integer(11, 'Ww', 'en')"));
            Assert.AreEqual("Elf", Writes("format-integer(11, 'Ww', 'de')"));
            Assert.AreEqual("Eleven", Writes("format-integer(11, 'Ww', 'hu')"));
            Assert.AreEqual("Eleven", Writes("format-integer(11, 'Ww', '@*!+%')"));
        }

        [TestMethod]
        public void NothingInIsAnEmptyStringOut()
        {
            // An empty sequence, not an empty sequence back: the function is declared to return one string.
            Assert.AreEqual("", Writes("format-integer((), 'Ww')"));
            Assert.AreEqual("1", Writes("count(format-integer((), 'Ww'))"));
        }

        [TestMethod]
        public void ItIsNotAvailableInATwoPointZeroStylesheet()
        {
            // A 2.0 stylesheet calling it means an extension function of its own, and should be told the
            // name is unknown rather than quietly given this one.
            Assert.AreEqual(
                "XPST0017",
                Assert.ThrowsExactly<XsltException>(
                    () => Writes("format-integer(1, '1')", version: "2.0")).Code);
        }
    }
}
