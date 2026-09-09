using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:number</c> — both the counting rules and the format strings.
    /// </summary>
    [TestClass]
    public sealed class NumberTests
    {
        private const string Document =
            "<doc>"
            + "<chapter><title>One</title><section><title>A</title></section>"
            + "<section><title>B</title></section></chapter>"
            + "<chapter><title>Two</title><section><title>C</title></section></chapter>"
            + "</doc>";

        private static string Sheet(string body)
        {
            return "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + body
                + "</xsl:stylesheet>";
        }

        /// <summary>Compiles a stylesheet with the XML declaration suppressed, since these tests compare fragments.</summary>
        private static Xslt Compile(string stylesheet, XsltBackend backend = XsltBackend.Interpreted)
        {
            return new Xslt(stylesheet, new XsltOptions { Backend = backend, OmitXmlDeclaration = true });
        }

        private static string Run(string stylesheet, string input)
        {
            string interpreted = Compile(stylesheet, XsltBackend.Interpreted).TransformXml(input);
            string compiled = Compile(stylesheet, XsltBackend.Compiled).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        private static void AssertMatchesReference(string stylesheet, string input)
        {
            XslCompiledTransform transform = new XslCompiledTransform();
            using (XmlReader reader = XmlReader.Create(new StringReader(stylesheet)))
            {
                transform.Load(reader);
            }

            StringWriter output = new StringWriter();
            XmlWriterSettings settings = transform.OutputSettings!.Clone();
            settings.OmitXmlDeclaration = true;
            settings.ConformanceLevel = ConformanceLevel.Auto;

            using (XmlWriter writer = XmlWriter.Create(output, settings))
            using (XmlReader reader = XmlReader.Create(new StringReader(input)))
            {
                transform.Transform(reader, null, writer);
            }

            Assert.AreEqual(
                Normalize(output.ToString()),
                Normalize(Run(stylesheet, input)),
                "output differed from XslCompiledTransform");
        }

        private static string Normalize(string fragment)
        {
            return XmlComparison.Normalize(fragment);
        }

        [TestMethod]
        public void ValueAttributeFormatsTheNumberOutright()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:number value=\"42\"/></a>"
                    + "<b><xsl:number value=\"3.7\"/></b>"
                    + "<c><xsl:number value=\"count(//section)\"/></c>"
                    + "</out></xsl:template>"),
                Document);
        }

        [TestMethod]
        public void SingleLevelCountsPrecedingSiblings()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//section\">"
                    + "<s><xsl:number/></s>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                Document);
        }

        [TestMethod]
        public void MultipleLevelProducesOneNumberPerMatchingAncestor()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//section\">"
                    + "<s><xsl:number level=\"multiple\" count=\"chapter|section\" format=\"1.1\"/></s>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                Document);
        }

        [TestMethod]
        public void AnyLevelCountsAcrossTheWholeDocument()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//title\">"
                    + "<t><xsl:number level=\"any\" count=\"title\"/></t>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                Document);
        }

        [TestMethod]
        public void FromLimitsWhereCountingStarts()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//title\">"
                    + "<t><xsl:number level=\"any\" count=\"title\" from=\"chapter\"/></t>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                Document);
        }

        [TestMethod]
        public void AnyLevelOverAnAttributeStopsAtItsElement()
        {
            // The set counted is the current node together with its preceding and ancestor axes, and
            // neither axis holds an attribute: one is counted where it is the node being numbered and
            // nowhere else. So an attribute is numbered by what stands before the element it hangs off,
            // and the second title's attribute is the second — not, as it was, the last of all of them.
            Assert.AreEqual(
                "<out><t>1</t><t>2</t><t>3</t></out>",
                Run(
                    Sheet("<xsl:template match=\"/\"><out>"
                        + "<xsl:for-each select=\"//title/@n\">"
                        + "<t><xsl:number level=\"any\" count=\"title\"/></t>"
                        + "</xsl:for-each></out></xsl:template>"),
                    Numbered));

            // Counted itself where the pattern takes it, which is the one place an attribute is counted.
            Assert.AreEqual(
                "<out><t>2</t><t>3</t><t>4</t></out>",
                Run(
                    Sheet("<xsl:template match=\"/\"><out>"
                        + "<xsl:for-each select=\"//title/@n\">"
                        + "<t><xsl:number level=\"any\" count=\"title|@n\"/></t>"
                        + "</xsl:for-each></out></xsl:template>"),
                    Numbered));

            // And a from pattern that the attribute itself matches restarts the count at it.
            Assert.AreEqual(
                "<out><t>1</t><t>1</t><t>1</t></out>",
                Run(
                    Sheet("<xsl:template match=\"/\"><out>"
                        + "<xsl:for-each select=\"//title/@n\">"
                        + "<t><xsl:number level=\"any\" count=\"title|@n\" from=\"@n\"/></t>"
                        + "</xsl:for-each></out></xsl:template>"),
                    Numbered));
        }

        /// <summary>The same shape as the document above, with an attribute on each title to number.</summary>
        private const string Numbered =
            "<doc><chapter><title n=\"a\">One</title><section><title n=\"b\">A</title></section>"
            + "<section><title n=\"c\">B</title></section></chapter></doc>";

        [TestMethod]
        public void CountPatternSelectsWhatIsCounted()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//chapter\">"
                    + "<c><xsl:number count=\"chapter\"/></c>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                Document);
        }

        [TestMethod]
        public void FormatTokensSelectTheNumberingStyle()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//section\">"
                    + "<a><xsl:number format=\"1\"/></a>"
                    + "<b><xsl:number format=\"01\"/></b>"
                    + "<c><xsl:number format=\"a\"/></c>"
                    + "<d><xsl:number format=\"A\"/></d>"
                    + "<e><xsl:number format=\"i\"/></e>"
                    + "<f><xsl:number format=\"I\"/></f>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                Document);
        }

        [TestMethod]
        public void SeparatorsPrefixAndSuffixAreReproduced()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//section\">"
                    + "<s><xsl:number level=\"multiple\" count=\"chapter|section\" format=\"(1.A) \"/></s>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                Document);
        }

        [TestMethod]
        public void AlphabeticNumberingCarriesPastTwentySix()
        {
            Assert.AreEqual("a", NumberFormat(1));
            Assert.AreEqual("z", NumberFormat(26));
            Assert.AreEqual("aa", NumberFormat(27));
            Assert.AreEqual("ab", NumberFormat(28));
            Assert.AreEqual("ba", NumberFormat(53));
        }

        [TestMethod]
        public void RomanNumeralsFollowSubtractiveNotation()
        {
            Assert.AreEqual("i", NumberFormat(1, "i"));
            Assert.AreEqual("iv", NumberFormat(4, "i"));
            Assert.AreEqual("ix", NumberFormat(9, "i"));
            Assert.AreEqual("xiv", NumberFormat(14, "i"));
            Assert.AreEqual("XL", NumberFormat(40, "I"));
            Assert.AreEqual("MCMXCIX", NumberFormat(1999, "I"));

            // The widest a numeral gets: subtractive notation is what keeps the others short, and these are
            // the numbers that cannot use it. A buffer sized for eleven digits would not hold either.
            Assert.AreEqual("MMMDCCCLXXXVIII", NumberFormat(3888, "I"));
            Assert.AreEqual("MMMMDCCCLXXXVIII", NumberFormat(4888, "I"));
            Assert.AreEqual("mmmmdccclxxxviii", NumberFormat(4888, "i"));
        }

        [TestMethod]
        public void ANumberWithNoNumeralIsWrittenAsDigits()
        {
            // Roman numerals have no zero and no negatives, and stop at 4999; the letter sequence has no
            // zero either. Each falls back to digits rather than to nothing.
            Assert.AreEqual("0", NumberFormat(0, "I"));
            Assert.AreEqual("-3", NumberFormat(-3, "i"));
            Assert.AreEqual("5000", NumberFormat(5000, "I"));
            Assert.AreEqual("0", NumberFormat(0, "a"));
            Assert.AreEqual("-3", NumberFormat(-3, "A"));
        }

        [TestMethod]
        public void APaddedTokenPadsToItsOwnWidth()
        {
            Assert.AreEqual("007", NumberFormat(7, "001"));
            Assert.AreEqual("1234", NumberFormat(1234, "001"), "a number wider than the token is left alone");

            // PadLeft put the zeros in front of the sign rather than the digits, and a format nobody writes
            // is not worth changing behaviour over.
            Assert.AreEqual("0-5", NumberFormat(-5, "001"));
        }

        /// <summary>Formats a single number through xsl:number's value attribute.</summary>
        private static string NumberFormat(int value, string format = "a")
        {
            return Run(
                Sheet($"<xsl:template match=\"/\"><xsl:number value=\"{value}\" format=\"{format}\"/></xsl:template>"),
                "<r/>");
        }

        // ---- lang, which chooses the words a number is spelled in ------------------------------------------

        [TestMethod]
        public void GermanWritesANumberAsOneWordWithTheUnitsBeforeTheTens()
        {
            Assert.AreEqual("drei", German(3));
            Assert.AreEqual("dreizehn", German(13));
            Assert.AreEqual("zwanzig", German(20));

            // Units before tens, joined by "und", and the whole of it one word however long it grows.
            Assert.AreEqual("einundzwanzig", German(21));
            Assert.AreEqual("sechsundneunzig", German(96));
            Assert.AreEqual("einhundertvierunddreißigtausendachthundertsechzehn", German(134816));

            // One is "eins" only where the word ends there. Before a hundred or a thousand, and inside an
            // "und" compound, it is "ein".
            Assert.AreEqual("eins", German(1));
            Assert.AreEqual("einhundert", German(100));
            Assert.AreEqual("eintausend", German(1000));
            Assert.AreEqual("zweihunderteins", German(201));

            // A million starts a word of its own, and German counts on the long scale: a Milliarde stands
            // where English puts a billion.
            Assert.AreEqual(
                "zwei millionen einhundertvierunddreißigtausendachthundertsechzehn", German(2134816));
            Assert.AreEqual("eine million", German(1000000));
            Assert.AreEqual("eine milliarde", German(1000000000));
        }

        [TestMethod]
        public void AGermanOrdinalTakesTheEndingTheAttributeNames()
        {
            // German inflects an ordinal for the case, number and gender of what it stands before, and no
            // processor can know those — so the attribute holds the ending rather than merely a yes.
            Assert.AreEqual("dritte", Words(3, "de", "-e"));
            Assert.AreEqual("zehnter", Words(10, "de", "-er"));
            Assert.AreEqual("dreizehntes", Words(13, "de", "-es"));
            Assert.AreEqual("zwanzigsten", Words(20, "de", "-en"));

            // Below twenty the stem takes a t and from twenty up an st, and either way the ending goes on
            // the last piece of the word rather than on the end of the cardinal: the 201st is
            // "zweihunderterste" where the cardinal ends "eins".
            Assert.AreEqual("einhundertste", Words(100, "de", "-e"));
            Assert.AreEqual("einhundertfünfzehnter", Words(115, "de", "-er"));
            Assert.AreEqual("einhundertvierunddreißigstes", Words(134, "de", "-es"));
            Assert.AreEqual("zweihunderterste", Words(201, "de", "-e"));
            Assert.AreEqual("eintausendste", Words(1000, "de", "-e"));

            // A variation beginning with a per cent sign names a CLDR rule set instead of an ending, and
            // the only spelled-ordinal set German has makes no distinction — so it gives the plain one.
            Assert.AreEqual("dritte", Words(3, "de", "%spellout-ordinal"));

            // Written in digits the German ordinal is a full stop, where English writes a suffix.
            Assert.AreEqual("3.", Words(3, "de", "-e", "1"));
            Assert.AreEqual("3rd", Words(3, "en", "-e", "1"));
        }

        [TestMethod]
        public void UpperCaseGermanWritesEszettAsTwoLettersOfS()
        {
            // The upper case of eszett is a pair of S, and the invariant mapping leaves the letter alone —
            // without which a shouted thirty would keep a lower-case letter in the middle of it.
            Assert.AreEqual("DREISSIG", Words(30, "de", string.Empty, "W"));
            Assert.AreEqual("EINHUNDERTFÜNFZEHN", Words(115, "de", string.Empty, "W"));

            // Title case capitalises each word, which is what makes the scale read as German writes it.
            Assert.AreEqual(
                "Zwei Millionen Einhundertvierunddreißigtausendachthundertsechzehn",
                Words(2134816, "de", string.Empty, "Ww"));
        }

        [TestMethod]
        public void ALanguageThisEngineDoesNotSpellFallsBackToEnglish()
        {
            // The specification tells a processor to use a language it does have rather than to refuse, so
            // asking for Hungarian gets English words and no message at all.
            Assert.AreEqual("twenty-one", Words(21, "hu"));
            Assert.AreEqual("twenty-one", Words(21, "en"));
            Assert.AreEqual("einundzwanzig", Words(21, "de"));

            // The primary subtag decides, so Austrian German is German.
            Assert.AreEqual("einundzwanzig", Words(21, "de-AT"));

            // Not carrying a language is not the same as the value being malformed, which is still refused:
            // statically where the attribute is written out, and at execution where a template computes it.
            Assert.AreEqual(
                "XTSE0020",
                Assert.ThrowsExactly<XsltException>(() => Words(21, "not a code")).Code);

            Assert.AreEqual(
                "XTDE0030",
                Assert.ThrowsExactly<XsltException>(() => Words(21, "{'not a code'}")).Code);
        }

        /// <summary>Spells a number through xsl:number, in the language and ordinal form asked for.</summary>
        private static string Words(int value, string language, string ordinal = "", string format = "w")
        {
            string asked = ordinal.Length == 0 ? string.Empty : $" ordinal=\"{ordinal}\"";

            return Run(
                "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:template match=\"/\">"
                + $"<xsl:number value=\"{value}\" format=\"{format}\" lang=\"{language}\"{asked}/>"
                + "</xsl:template></xsl:stylesheet>",
                "<r/>");
        }

        /// <summary>Spells a number in German words, which is the second language this engine has.</summary>
        private static string German(int value) => Words(value, "de");

        // ---- format-number, which shares its grouping rule with fn:format-integer ---------------------------

        [TestMethod]
        [DataRow("123456789", "#,##0")]
        [DataRow("123456789", "0,0,00,0")]
        [DataRow("123456789", "00,00,00")]
        [DataRow("123456789", "000,00,00")]
        [DataRow("0", "#")]
        [DataRow("0", "#.#")]
        [DataRow("1234.5", "#,##0.00")]
        [DataRow("0.25", "#0%")]
        public void AtOnePointZeroGroupingAndTheZeroPictureAgreeWithTheReference(string value, string picture)
        {
            // 1.0 hands format-number to Java's DecimalFormat, where one interval repeats and a picture with
            // no mandatory digit formats zero as nothing. 2.0 replaced that algorithm and answers
            // differently for exactly these pictures, so this is what says the old reading survives where it
            // is still the right one — and asking was not idle: the reference disagreed with all four.
            AssertMatchesReference(
                Sheet(
                    "<xsl:template match=\"/\">"
                    + $"<xsl:value-of select=\"format-number({value}, '{picture}')\"/>"
                    + "</xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void FromTwoPointZeroTheSeparatorsRepeatOnlyWhenRegular()
        {
            // The same rule fn:format-integer states, in the same words: separators all of one character, at
            // multiples of one interval, with none of those multiples missing, describe a habit that carries
            // on leftwards.
            Assert.AreEqual("123,456,789", FormatNumber("123456789", "#,##0"));
            Assert.AreEqual("1,23,45,67,89", FormatNumber("123456789", "00,00,00"));

            // And anything else describes a shape, which a longer number simply runs off the left of. A
            // regular pattern of two would also have wanted one after the sixth digit here.
            Assert.AreEqual("12345,67,89", FormatNumber("123456789", "000,00,00"));
            Assert.AreEqual("12345,6,78,9", FormatNumber("123456789", "0,0,00,0"));
        }

        [TestMethod]
        public void FromTwoPointZeroAPictureWithNoMandatoryDigitStillWritesZero()
        {
            // Which side of the decimal separator the zero lands on is decided by whether there is a
            // fractional part to put it in.
            Assert.AreEqual("0", FormatNumber("0", "#"));
            Assert.AreEqual(".0", FormatNumber("0", ".#"));

            // Including where the picture has an integer part, so long as no digit there has to appear:
            // '#.#' asks for a fraction and gets the zero put in it.
            Assert.AreEqual(".0", FormatNumber("0", "#.#"));
            Assert.AreEqual(".2", FormatNumber("0.2", "#.#"));

            // A picture that does ask for a digit is unaffected either way.
            Assert.AreEqual("0", FormatNumber("0", "0"));
            Assert.AreEqual("0.00", FormatNumber("0", "0.00"));
        }

        [TestMethod]
        public void AtOnePointZeroOnlyTheZeroDigitIsADigitSign()
        {
            // '9' is a digit of the same family as '0', and from 2.0 that makes it a digit sign asking for a
            // place that has to be filled. The reference reads it as literal text, and these say that is
            // still what happens where the stylesheet says 1.0: '001' pads to two places and then writes a
            // '1' after the number, and the separators inside a fraction are ignored rather than grouping it.
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:value-of select=\"format-number(42, '001')\"/></a>"
                    + "<b><xsl:value-of select=\"format-number(12345.6789012345, '#.#,##,#')\"/></b>"
                    + "<c><xsl:value-of select=\"format-number(1 div 3, '0.####################')\"/></c>"
                    + "</out></xsl:template>"),
                "<r/>");

            Assert.AreEqual("0,012.34", FormatNumber("12.34", "9,999.99"), "from 2.0 the '9's are digits");
            Assert.AreEqual("12.34", FormatNumber("12.34", "#,##9.99"));
            Assert.AreEqual("12345.7", FormatNumber("12345.678", ".9"));
        }

        [TestMethod]
        public void ANegativeSubPictureSuppliesTheWholeFormat()
        {
            // Not only the text around the number: its digits, its grouping and its rounding as well, and no
            // minus sign is added to what it says. Java's DecimalFormat takes only the prefix and suffix, but
            // the reference does not follow it there, so this is one rule both versions share.
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:value-of select=\"format-number(2392.14*(-36.58),"
                    + " '000,000.000###;###,###.000###')\"/></a>"
                    + "<b><xsl:value-of select=\"format-number(-5, '0000;(0)')\"/></b>"
                    + "</out></xsl:template>"),
                "<r/>");

            Assert.AreEqual("87,504.4812", FormatNumber("2392.14*(-36.58)", "000,000.000###;###,###.000###"));
            Assert.AreEqual("(5)", FormatNumber("-5", "0000;(0)"));
        }

        [TestMethod]
        public void FromTwoPointZeroSeparatorsGroupTheFractionToo()
        {
            // Counted outwards from the point rather than inwards from the end, and they do not repeat: a
            // fraction has no leftward habit to carry on.
            Assert.AreEqual("12345.6,78,9", FormatNumber("12345.6789012345", "#.#,##,#"));
            Assert.AreEqual("12345.67,89,01", FormatNumber("12345.6789012345", "#.##,##,##"));
        }

        [TestMethod]
        [DataRow("931.4857", "000.##0", "a digit that has to appear follows one that need not")]
        [DataRow("12345.678", "#,", "a grouping separator ends the number")]
        [DataRow("12345.678", "#,,###", "two grouping separators are adjacent")]
        [DataRow("12345.678", "#,.##", "a grouping separator sits beside the decimal separator")]
        [DataRow("12345.678", "#.,##", "the same, on the other side of it")]
        [DataRow("931.4857", "fred.ginger", "there is no digit and no optional-digit sign")]
        [DataRow("12345.678", "0.0.0", "there is more than one decimal separator")]
        [DataRow("12345.678", "0#0", "an optional digit follows a mandatory one before the point")]
        public void FromTwoPointZeroAPictureBreakingTheRulesIsRefused(string value, string picture, string why)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => FormatNumber(value, picture), why);

            // One complaint with two codes. XSLT 2.0 has a format-number of its own and calls this
            // XTDE1310; XPath 3.0 moved the function into the core library, where the same failure is
            // FODF1310. A stylesheet hears the code its own language names.
            Assert.AreEqual("XTDE1310", error.Code, why);
        }

        [TestMethod]
        public void FromThreePointZeroTheSamePictureCarriesTheCoreLibrarysCode()
        {
            Assert.AreEqual(
                "FODF1310",
                Assert.ThrowsExactly<XsltException>(
                    () => FormatNumber("12345.678", "#,", version: "3.0")).Code);
        }

        [TestMethod]
        public void APictureIsJudgedWhenItIsUsedRatherThanWhenItIsCompiled()
        {
            // FODF1310 is a dynamic error, so a stylesheet carrying a bad picture down a branch it never
            // takes has to compile and run.
            Assert.AreEqual(
                "fine",
                Run(
                    Sheet("<xsl:template match=\"/\"><xsl:choose>"
                        + "<xsl:when test=\"false()\">"
                        + "<xsl:value-of select=\"format-number(1, '#,')\"/></xsl:when>"
                        + "<xsl:otherwise>fine</xsl:otherwise></xsl:choose></xsl:template>")
                        .Replace("version=\"1.0\"", "version=\"2.0\""),
                    "<r/>"));
        }

        // ---- the exponent notation XPath 3.1 added ---------------------------------------------------------

        [TestMethod]
        [DataRow("12345.678", "9.9999e999", "1.2346e004")]
        [DataRow("12345.678", "999.99e99", "123.46e02")]
        [DataRow("12345.678", "#99.99e99", "12.35e03")]
        [DataRow("0.00012345678", "9.99e99", "1.23e-04")]
        [DataRow("0.2", "0e0", "2e-1")]
        [DataRow("0.2", "000.0e0", "200.0e-3")]
        [DataRow("0", "0.0e01", "0.0e00")]
        public void FromThreePointOneAPictureMayAskForAnExponent(string value, string picture, string expected)
        {
            // The mantissa is scaled to show exactly as many integer digits as the picture insists on, and
            // the exponent is padded to the width the picture writes.
            Assert.AreEqual(expected, FormatNumber(value, picture, "3.0"));
        }

        [TestMethod]
        [DataRow("0.2", "#e0", "0.2e0")]
        [DataRow("1.2", "#e0", "0.1e1")]
        [DataRow("0.2", ".#e0", ".2e0")]
        [DataRow("1.2", ".#e0", ".1e1")]
        [DataRow("0.2", "#.#e0", "0.2e0")]
        [DataRow("0.99999999", "#.#e0", "1e0")]
        [DataRow("0.99999999", ".#e0", "1.0e0")]
        [DataRow("0", "#.#e9", "0e0")]
        public void AnExponentPictureAskingForNoIntegerDigitStillWritesOne(
            string value, string picture, string expected)
        {
            // A picture insisting on no integer digit puts the mantissa between a tenth and one, and then
            // writes the leading zero anyway so long as it has an integer part to write it in — which is why
            // '#.#e0' gives 0.2e0 where '.#e0' gives .2e0. Rounding may carry the mantissa back over one, and
            // the exponent is not reconsidered when it does.
            Assert.AreEqual(expected, FormatNumber(value, picture, "3.0"));
        }

        [TestMethod]
        public void AnExponentPictureAndItsSeparatorFollowTheProcessorsVersion()
        {
            // The exponent part of the picture language arrived with XPath 3.1, and xsl:decimal-format's
            // exponent-separator with it. Which picture language is read is a question about the processor
            // rather than about what the stylesheet says of itself — as the code for an unreadable picture
            // already was — so a version="2.0" stylesheet on a 3.0 processor may write both.
            string stylesheet =
                "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:decimal-format exponent-separator=\"E\"/>"
                + "<xsl:template match=\"/\">"
                + "<xsl:value-of select=\"format-number(123.456, '0.0000E0')\"/>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "1.2346E2",
                new Xslt(
                    stylesheet,
                    new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                    .TransformXml("<r/>"));

            // A 2.0 processor has neither the attribute nor the picture, and says so about the attribute
            // first, that being what it reads first.
            Assert.AreEqual(
                "XTSE0090",
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true })
                        .TransformXml("<r/>")).Code);
        }

        [TestMethod]
        public void AnExponentSeparatorIsOnlyOneWhereDigitsFollowIt()
        {
            // Elsewhere it is ordinary text, which is what lets a picture end in 'eDog'.
            Assert.AreEqual("12345.6780eDog", FormatNumber("12345.678", "9.9999eDog", "3.0"));
            Assert.AreEqual("1.2346e04end", FormatNumber("12345.678", "9.9999e99end", "3.0"));

            // And where it is one, a second cannot follow: '999' after a passive 'E' is a digit run stranded
            // in the middle of the number.
            foreach (string picture in new[] { "9.9999E999", "9.99e99e99", "9.9999e,", "9.9999e999%" })
            {
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => FormatNumber("12345.678", picture, "3.0"), picture);

                Assert.AreEqual("FODF1310", error.Code, picture);
            }
        }

        // ---- which digits a value is printed from ----------------------------------------------------------

        [TestMethod]
        public void ANumberIsPrintedFromTheDigitsItsOwnTypeHas()
        {
            // A double carries the shortest digits that read back as itself, which for 1e30 is one followed
            // by thirty zeros rather than the 1000000000000000019884624838656 its bits actually hold.
            Assert.AreEqual("1000000000000000000000000000000", FormatNumber("1e30", "#", "3.0"));

            // An xs:integer of eighteen digits and an xs:decimal of nineteen are printed as written, both
            // being wider than a double could hold.
            Assert.AreEqual(
                "1.23456789012345678e17",
                FormatNumber("123456789012345678", "9.99999999999999999e99", "3.0"));
            Assert.AreEqual(
                "9.00001000020000345e-01",
                FormatNumber("0.900001000020000345", "9.99999999999999999e99", "3.0"));
        }

        [TestMethod]
        public void ScalingByAPercentSignOverflowsWhereTheValueIsADouble()
        {
            // A hundred times 1e308 is not a number a double has, and the specification asks for the
            // arithmetic rather than for the exact answer.
            Assert.AreEqual("Infinity%", FormatNumber("xs:double('1e308')", "0%", "3.0"));

            // Where the value is a decimal there is no arithmetic to overflow, and moving the point keeps
            // every digit the picture then asks to be shown.
            Assert.AreEqual("110.20304050607080900%", FormatNumber("1.102030405060708090", "0.00000000000000000%", "3.0"));
        }

        [TestMethod]
        public void ThePictureFixesWhichTenDigitsAreWritten()
        {
            // Taken from the picture rather than from zero-digit, so a picture written in Osmanya digits is
            // answered in Osmanya digits with no declaration anywhere.
            Assert.AreEqual("\U000104A1.\U000104A0e\U000104A0", FormatNumber("1.0", "\U000104A0.\U000104A0e\U000104A0", "3.0"));
            Assert.AreEqual("\U000104A1\U000104A2\U000104A3", FormatNumber("123", "\U000104A0\U000104A0\U000104A0", "3.0"));
        }

        // ---- the decimal formats a name chooses between -----------------------------------------------------

        [TestMethod]
        public void ADecimalFormatIsNamedByItsExpandedName()
        {
            // Not by its local part: one local name in two namespaces is two formats, and two prefixes bound
            // to one namespace name the same one. The prefix on the declaration and the prefix on the call
            // need not be the same prefix.
            string stylesheet =
                "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
                + " xmlns:a=\"urn:one\" xmlns:b=\"urn:two\" xmlns:also-a=\"urn:one\""
                + " exclude-result-prefixes=\"a b also-a\">"
                + "<xsl:decimal-format name=\"a:money\" decimal-separator=\",\" grouping-separator=\".\"/>"
                + "<xsl:decimal-format name=\"b:money\" decimal-separator=\"!\" grouping-separator=\"*\"/>"
                + "<xsl:template match=\"/\"><out>"
                + "<x><xsl:value-of select=\"format-number(1234.5, '#.##0,00', 'a:money')\"/></x>"
                + "<y><xsl:value-of select=\"format-number(1234.5, '#*##0!00', 'b:money')\"/></y>"
                + "<z><xsl:value-of select=\"format-number(1234.5, '#.##0,00', 'also-a:money')\"/></z>"
                + "</out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out><x>1.234,50</x><y>1*234!50</y><z>1.234,50</z></out>",
                Run(stylesheet, "<r/>"));
        }

        [TestMethod]
        public void NamingADecimalFormatThatIsNotDeclaredIsAnError()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                    + "<xsl:template match=\"/\">"
                    + "<xsl:value-of select=\"format-number(1, '#', 'nowhere')\"/>"
                    + "</xsl:template></xsl:stylesheet>",
                    "<r/>"));

            // XSLT 2.0 names this one itself; the same call at 3.0 hears the core library's FODF1280.
            Assert.AreEqual("XTDE1280", error.Code);
        }

        /// <summary>Formats through fn:format-number at 2.0 or later, where the rewritten algorithm applies.</summary>
        private static string FormatNumber(string value, string picture, string version = "2.0")
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                + "<xsl:template match=\"/\">"
                + $"<xsl:value-of select=\"format-number({value}, '{picture}')\"/>"
                + "</xsl:template></xsl:stylesheet>";

            // Which library defines format-number is the processor's question rather than the stylesheet's,
            // and it is what decides between XTDE1310 and FODF1310 — so a test about the code has to say
            // which processor it is asking. How the picture is read still follows the stylesheet's version.
            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                Version = version == "3.0" ? XsltVersion.V30 : XsltVersion.Implemented,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }
    }
}
