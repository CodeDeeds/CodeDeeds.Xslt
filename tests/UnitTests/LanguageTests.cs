namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the languages this engine spells numbers and writes dates in beyond English and German:
    /// French, Spanish, Portuguese, Italian, Norwegian, Swedish and Danish.
    /// </summary>
    /// <remarks>
    /// Each language is checked at the numbers where its grammar turns: where a one changes form before a
    /// noun, where a hundred is one word and a hundred and one another, where a plural appears, where an
    /// <c>and</c> goes and where it does not, and where the ordinal is a word of its own rather than an
    /// ending. Reached through <c>fn:format-integer</c>, <c>xsl:number</c> and <c>fn:format-date</c>, since
    /// each is a different way in to the same words.
    /// </remarks>
    [TestClass]
    public sealed class LanguageTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private static string Writes(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\" separator=\"|\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            string result = new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>");
            return result == "<out/>" ? string.Empty : result["<out>".Length..^"</out>".Length];
        }

        /// <summary>Spells a number in a language, as a cardinal.</summary>
        private static string Cardinal(long value, string language)
        {
            return Writes($"format-integer({value}, 'w', '{language}')");
        }

        /// <summary>Spells a number in a language, as an ordinal, with the variation the picture names.</summary>
        private static string Ordinal(long value, string language, string variation = "")
        {
            string modifier = variation.Length == 0 ? "o" : $"o({variation})";
            return Writes($"format-integer({value}, 'w;{modifier}', '{language}')");
        }

        /// <summary>Writes digits as an ordinal in a language: what follows the digits is the language's mark.</summary>
        private static string Digits(long value, string language, string variation = "")
        {
            string modifier = variation.Length == 0 ? "o" : $"o({variation})";
            return Writes($"format-integer({value}, '1;{modifier}', '{language}')");
        }

        private static string Date(string picture, string language, string date = "2026-09-12")
        {
            return Writes($"format-date(xs:date('{date}'), '{picture}', '{language}', (), ())");
        }

        private static string Time(string picture, string language, string time = "09:05:00")
        {
            return Writes($"format-time(xs:time('{time}'), '{picture}', '{language}', (), ())");
        }

        // ---- French --------------------------------------------------------------------------------------

        [TestMethod]
        public void FrenchCountsInTwentiesAndKeepsItsPluralsStraight()
        {
            Assert.AreEqual("zéro", Cardinal(0, "fr"));
            Assert.AreEqual("un", Cardinal(1, "fr"));
            Assert.AreEqual("dix-sept", Cardinal(17, "fr"));
            Assert.AreEqual("vingt-et-un", Cardinal(21, "fr"));
            Assert.AreEqual("vingt-deux", Cardinal(22, "fr"));
            Assert.AreEqual("soixante-dix", Cardinal(70, "fr"));
            Assert.AreEqual("soixante-et-onze", Cardinal(71, "fr"));
            Assert.AreEqual("soixante-douze", Cardinal(72, "fr"));
            Assert.AreEqual("quatre-vingts", Cardinal(80, "fr"));
            Assert.AreEqual("quatre-vingt-un", Cardinal(81, "fr"));
            Assert.AreEqual("quatre-vingt-dix", Cardinal(90, "fr"));
            Assert.AreEqual("quatre-vingt-onze", Cardinal(91, "fr"));
            Assert.AreEqual("quatre-vingt-dix-neuf", Cardinal(99, "fr"));
            Assert.AreEqual("cent", Cardinal(100, "fr"));
            Assert.AreEqual("cent un", Cardinal(101, "fr"));
            Assert.AreEqual("deux cents", Cardinal(200, "fr"));
            Assert.AreEqual("deux cent un", Cardinal(201, "fr"));
            Assert.AreEqual("deux cent quatre-vingts", Cardinal(280, "fr"));
            Assert.AreEqual("mille", Cardinal(1000, "fr"));
            Assert.AreEqual("mille un", Cardinal(1001, "fr"));
            Assert.AreEqual("mille neuf cent quatre-vingt-dix-neuf", Cardinal(1999, "fr"));
            Assert.AreEqual("deux mille", Cardinal(2000, "fr"));
            Assert.AreEqual("quatre-vingt mille", Cardinal(80000, "fr"));
            Assert.AreEqual("un million", Cardinal(1_000_000, "fr"));
            Assert.AreEqual("deux millions un", Cardinal(2_000_001, "fr"));
            Assert.AreEqual("un milliard", Cardinal(1_000_000_000, "fr"));

            Assert.AreEqual("premier", Ordinal(1, "fr"));
            Assert.AreEqual("première", Ordinal(1, "fr", "-e"));
            Assert.AreEqual("deuxième", Ordinal(2, "fr"));
            Assert.AreEqual("quatrième", Ordinal(4, "fr"));
            Assert.AreEqual("cinquième", Ordinal(5, "fr"));
            Assert.AreEqual("neuvième", Ordinal(9, "fr"));
            Assert.AreEqual("dix-septième", Ordinal(17, "fr"));
            Assert.AreEqual("vingt-et-unième", Ordinal(21, "fr"));
            Assert.AreEqual("soixante-et-onzième", Ordinal(71, "fr"));
            Assert.AreEqual("quatre-vingtième", Ordinal(80, "fr"));
            Assert.AreEqual("centième", Ordinal(100, "fr"));
            Assert.AreEqual("deux centième", Ordinal(200, "fr"));
            Assert.AreEqual("millième", Ordinal(1000, "fr"));
            Assert.AreEqual("millionième", Ordinal(1_000_000, "fr"));

            Assert.AreEqual("1er", Digits(1, "fr"));
            Assert.AreEqual("1re", Digits(1, "fr", "-e"));
            Assert.AreEqual("2e", Digits(2, "fr"));
            Assert.AreEqual("21e", Digits(21, "fr"));
        }

        // ---- Spanish -------------------------------------------------------------------------------------

        [TestMethod]
        public void SpanishShortensItsOneBeforeANounAndItsHundredOnItsOwn()
        {
            Assert.AreEqual("cero", Cardinal(0, "es"));
            Assert.AreEqual("uno", Cardinal(1, "es"));
            Assert.AreEqual("dieciséis", Cardinal(16, "es"));
            Assert.AreEqual("veintiuno", Cardinal(21, "es"));
            Assert.AreEqual("veintidós", Cardinal(22, "es"));
            Assert.AreEqual("treinta", Cardinal(30, "es"));
            Assert.AreEqual("treinta y uno", Cardinal(31, "es"));
            Assert.AreEqual("noventa y nueve", Cardinal(99, "es"));
            Assert.AreEqual("cien", Cardinal(100, "es"));
            Assert.AreEqual("ciento uno", Cardinal(101, "es"));
            Assert.AreEqual("doscientos", Cardinal(200, "es"));
            Assert.AreEqual("quinientos", Cardinal(500, "es"));
            Assert.AreEqual("novecientos noventa y nueve", Cardinal(999, "es"));
            Assert.AreEqual("mil", Cardinal(1000, "es"));
            Assert.AreEqual("mil uno", Cardinal(1001, "es"));
            Assert.AreEqual("dos mil", Cardinal(2000, "es"));
            Assert.AreEqual("veintiún mil", Cardinal(21000, "es"));
            Assert.AreEqual("treinta y un mil", Cardinal(31000, "es"));
            Assert.AreEqual("ciento un mil", Cardinal(101000, "es"));
            Assert.AreEqual("un millón", Cardinal(1_000_000, "es"));
            Assert.AreEqual("dos millones uno", Cardinal(2_000_001, "es"));
            Assert.AreEqual("mil millones", Cardinal(1_000_000_000, "es"));
            Assert.AreEqual("un billón", Cardinal(1_000_000_000_000, "es"));

            Assert.AreEqual("primero", Ordinal(1, "es"));
            Assert.AreEqual("primera", Ordinal(1, "es", "-a"));
            Assert.AreEqual("tercero", Ordinal(3, "es"));
            Assert.AreEqual("décimo", Ordinal(10, "es"));
            Assert.AreEqual("undécimo", Ordinal(11, "es"));
            Assert.AreEqual("decimotercero", Ordinal(13, "es"));
            Assert.AreEqual("vigésimo", Ordinal(20, "es"));
            Assert.AreEqual("vigésimo primero", Ordinal(21, "es"));
            Assert.AreEqual("vigésima primera", Ordinal(21, "es", "-a"));
            Assert.AreEqual("centésimo", Ordinal(100, "es"));
            Assert.AreEqual("centésimo vigésimo primero", Ordinal(121, "es"));
            Assert.AreEqual("quingentésimo", Ordinal(500, "es"));
            Assert.AreEqual("milésimo", Ordinal(1000, "es"));
            Assert.AreEqual("dosmilésimo", Ordinal(2000, "es"));
            Assert.AreEqual("millonésimo", Ordinal(1_000_000, "es"));

            Assert.AreEqual("1º", Digits(1, "es"));
            Assert.AreEqual("1ª", Digits(1, "es", "-a"));
            Assert.AreEqual("21º", Digits(21, "es"));
        }

        // ---- Portuguese ----------------------------------------------------------------------------------

        [TestMethod]
        public void PortugueseJoinsWithEAndCountsDifferentlyOnEachSideOfTheAtlantic()
        {
            Assert.AreEqual("zero", Cardinal(0, "pt"));
            Assert.AreEqual("um", Cardinal(1, "pt"));
            Assert.AreEqual("dezesseis", Cardinal(16, "pt"));
            Assert.AreEqual("vinte e um", Cardinal(21, "pt"));
            Assert.AreEqual("cem", Cardinal(100, "pt"));
            Assert.AreEqual("cento e um", Cardinal(101, "pt"));
            Assert.AreEqual("duzentos", Cardinal(200, "pt"));
            Assert.AreEqual("duzentos e trinta e um", Cardinal(231, "pt"));
            Assert.AreEqual("mil", Cardinal(1000, "pt"));
            Assert.AreEqual("mil e um", Cardinal(1001, "pt"));
            Assert.AreEqual("mil e cem", Cardinal(1100, "pt"));
            Assert.AreEqual("mil duzentos e trinta e um", Cardinal(1231, "pt"));
            Assert.AreEqual("dois mil", Cardinal(2000, "pt"));
            Assert.AreEqual("um milhão", Cardinal(1_000_000, "pt"));
            Assert.AreEqual("dois milhões e um", Cardinal(2_000_001, "pt"));
            Assert.AreEqual("um bilhão", Cardinal(1_000_000_000, "pt"));

            // Portugal: three teens spelled differently, and the long scale.
            Assert.AreEqual("dezasseis", Cardinal(16, "pt-PT"));
            Assert.AreEqual("mil milhões", Cardinal(1_000_000_000, "pt-PT"));
            Assert.AreEqual("um bilião", Cardinal(1_000_000_000_000, "pt-PT"));
            Assert.AreEqual("um bilhão", Cardinal(1_000_000_000, "pt-BR"));

            Assert.AreEqual("primeiro", Ordinal(1, "pt"));
            Assert.AreEqual("primeira", Ordinal(1, "pt", "-a"));
            Assert.AreEqual("segundo", Ordinal(2, "pt"));
            Assert.AreEqual("décimo primeiro", Ordinal(11, "pt"));
            Assert.AreEqual("vigésimo", Ordinal(20, "pt"));
            Assert.AreEqual("vigésimo primeiro", Ordinal(21, "pt"));
            Assert.AreEqual("centésimo", Ordinal(100, "pt"));
            Assert.AreEqual("centésimo vigésimo primeiro", Ordinal(121, "pt"));
            Assert.AreEqual("milésimo", Ordinal(1000, "pt"));
            Assert.AreEqual("segundo milésimo", Ordinal(2000, "pt"));
            Assert.AreEqual("milionésimo", Ordinal(1_000_000, "pt"));

            Assert.AreEqual("1º", Digits(1, "pt"));
            Assert.AreEqual("2ª", Digits(2, "pt", "-a"));
        }

        // ---- Italian -------------------------------------------------------------------------------------

        [TestMethod]
        public void ItalianRunsItsWordsTogetherAndElidesWhereVowelsMeet()
        {
            Assert.AreEqual("zero", Cardinal(0, "it"));
            Assert.AreEqual("uno", Cardinal(1, "it"));
            Assert.AreEqual("tre", Cardinal(3, "it"));
            Assert.AreEqual("diciassette", Cardinal(17, "it"));
            Assert.AreEqual("ventuno", Cardinal(21, "it"));
            Assert.AreEqual("ventitré", Cardinal(23, "it"));
            Assert.AreEqual("ventotto", Cardinal(28, "it"));
            Assert.AreEqual("trentuno", Cardinal(31, "it"));
            Assert.AreEqual("cento", Cardinal(100, "it"));
            Assert.AreEqual("centouno", Cardinal(101, "it"));
            Assert.AreEqual("centotré", Cardinal(103, "it"));
            Assert.AreEqual("centotto", Cardinal(108, "it"));
            Assert.AreEqual("centottanta", Cardinal(180, "it"));
            Assert.AreEqual("duecento", Cardinal(200, "it"));
            Assert.AreEqual("mille", Cardinal(1000, "it"));
            Assert.AreEqual("milleuno", Cardinal(1001, "it"));
            Assert.AreEqual("millenovecentonovantanove", Cardinal(1999, "it"));
            Assert.AreEqual("duemila", Cardinal(2000, "it"));
            Assert.AreEqual("ventunmila", Cardinal(21000, "it"));
            Assert.AreEqual("un milione", Cardinal(1_000_000, "it"));
            Assert.AreEqual("due milioni trecento", Cardinal(2_000_300, "it"));
            Assert.AreEqual("un miliardo", Cardinal(1_000_000_000, "it"));

            Assert.AreEqual("primo", Ordinal(1, "it"));
            Assert.AreEqual("prima", Ordinal(1, "it", "-a"));
            Assert.AreEqual("terzo", Ordinal(3, "it"));
            Assert.AreEqual("decimo", Ordinal(10, "it"));
            Assert.AreEqual("undicesimo", Ordinal(11, "it"));
            Assert.AreEqual("ventesimo", Ordinal(20, "it"));
            Assert.AreEqual("ventunesimo", Ordinal(21, "it"));
            Assert.AreEqual("ventitreesimo", Ordinal(23, "it"));
            Assert.AreEqual("ventiseiesimo", Ordinal(26, "it"));
            Assert.AreEqual("centesimo", Ordinal(100, "it"));
            Assert.AreEqual("millesimo", Ordinal(1000, "it"));
            Assert.AreEqual("duemillesimo", Ordinal(2000, "it"));
            Assert.AreEqual("milionesimo", Ordinal(1_000_000, "it"));

            Assert.AreEqual("1º", Digits(1, "it"));
            Assert.AreEqual("1ª", Digits(1, "it", "-a"));
        }

        // ---- Norwegian -----------------------------------------------------------------------------------

        [TestMethod]
        public void NorwegianCountsTensFirstAndPutsOgBeforeTheLastPiece()
        {
            Assert.AreEqual("null", Cardinal(0, "nb"));
            Assert.AreEqual("en", Cardinal(1, "nb"));
            Assert.AreEqual("sju", Cardinal(7, "nb"));
            Assert.AreEqual("tjueen", Cardinal(21, "nb"));
            Assert.AreEqual("ett hundre", Cardinal(100, "nb"));
            Assert.AreEqual("ett hundre og en", Cardinal(101, "nb"));
            Assert.AreEqual("ett hundre og tjueen", Cardinal(121, "nb"));
            Assert.AreEqual("to hundre", Cardinal(200, "nb"));
            Assert.AreEqual("ett tusen", Cardinal(1000, "nb"));
            Assert.AreEqual("ett tusen og en", Cardinal(1001, "nb"));
            Assert.AreEqual("ett tusen ett hundre", Cardinal(1100, "nb"));
            Assert.AreEqual("ett tusen ett hundre og tjueen", Cardinal(1121, "nb"));
            Assert.AreEqual("to tusen", Cardinal(2000, "nb"));
            Assert.AreEqual("en million", Cardinal(1_000_000, "nb"));
            Assert.AreEqual("to millioner og en", Cardinal(2_000_001, "nb"));
            Assert.AreEqual("en milliard", Cardinal(1_000_000_000, "nb"));

            Assert.AreEqual("første", Ordinal(1, "nb"));
            Assert.AreEqual("andre", Ordinal(2, "nb"));
            Assert.AreEqual("tredje", Ordinal(3, "nb"));
            Assert.AreEqual("sjuende", Ordinal(7, "nb"));
            Assert.AreEqual("tjuende", Ordinal(20, "nb"));
            Assert.AreEqual("tjueførste", Ordinal(21, "nb"));
            Assert.AreEqual("hundrede", Ordinal(100, "nb"));
            Assert.AreEqual("ett hundre og første", Ordinal(101, "nb"));
            Assert.AreEqual("tusende", Ordinal(1000, "nb"));
            Assert.AreEqual("millionte", Ordinal(1_000_000, "nb"));
            Assert.AreEqual("21.", Digits(21, "nb"));

            // 'no' is Bokmål too; Nynorsk differs in a few words and its ordinal endings.
            Assert.AreEqual("tjueen", Cardinal(21, "no"));
            Assert.AreEqual("ein", Cardinal(1, "nn"));
            Assert.AreEqual("tjueein", Cardinal(21, "nn"));
            Assert.AreEqual("eitt hundre", Cardinal(100, "nn"));
            Assert.AreEqual("sjuande", Ordinal(7, "nn"));
            Assert.AreEqual("tjuande", Ordinal(20, "nn"));
            Assert.AreEqual("hundrade", Ordinal(100, "nn"));
        }

        // ---- Swedish -------------------------------------------------------------------------------------

        [TestMethod]
        public void SwedishRunsItsHundredsTogetherAndMarksOrdinalDigitsWithAColon()
        {
            Assert.AreEqual("noll", Cardinal(0, "sv"));
            Assert.AreEqual("ett", Cardinal(1, "sv"));
            Assert.AreEqual("tjugoett", Cardinal(21, "sv"));
            Assert.AreEqual("etthundra", Cardinal(100, "sv"));
            Assert.AreEqual("etthundraett", Cardinal(101, "sv"));
            Assert.AreEqual("etthundratjugoett", Cardinal(121, "sv"));
            Assert.AreEqual("tvåhundra", Cardinal(200, "sv"));
            Assert.AreEqual("ettusen", Cardinal(1000, "sv"));
            Assert.AreEqual("ettusen ett", Cardinal(1001, "sv"));
            Assert.AreEqual("ettusen etthundratjugoett", Cardinal(1121, "sv"));
            Assert.AreEqual("tvåtusen trehundrafyrtiofem", Cardinal(2345, "sv"));
            Assert.AreEqual("en miljon", Cardinal(1_000_000, "sv"));
            Assert.AreEqual("två miljoner ett", Cardinal(2_000_001, "sv"));
            Assert.AreEqual("en miljard", Cardinal(1_000_000_000, "sv"));

            Assert.AreEqual("första", Ordinal(1, "sv"));
            Assert.AreEqual("andra", Ordinal(2, "sv"));
            Assert.AreEqual("tredje", Ordinal(3, "sv"));
            Assert.AreEqual("fjärde", Ordinal(4, "sv"));
            Assert.AreEqual("åttonde", Ordinal(8, "sv"));
            Assert.AreEqual("tjugonde", Ordinal(20, "sv"));
            Assert.AreEqual("tjugoförsta", Ordinal(21, "sv"));
            Assert.AreEqual("hundrade", Ordinal(100, "sv"));
            Assert.AreEqual("etthundratjugoförsta", Ordinal(121, "sv"));
            Assert.AreEqual("tusende", Ordinal(1000, "sv"));
            Assert.AreEqual("miljonte", Ordinal(1_000_000, "sv"));

            Assert.AreEqual("1:a", Digits(1, "sv"));
            Assert.AreEqual("2:a", Digits(2, "sv"));
            Assert.AreEqual("3:e", Digits(3, "sv"));
            Assert.AreEqual("11:e", Digits(11, "sv"));
            Assert.AreEqual("12:e", Digits(12, "sv"));
            Assert.AreEqual("21:a", Digits(21, "sv"));
            Assert.AreEqual("23:e", Digits(23, "sv"));
        }

        // ---- Danish --------------------------------------------------------------------------------------

        [TestMethod]
        public void DanishCountsUnitsFirstAndInTwenties()
        {
            Assert.AreEqual("nul", Cardinal(0, "da"));
            Assert.AreEqual("en", Cardinal(1, "da"));
            Assert.AreEqual("syv", Cardinal(7, "da"));
            Assert.AreEqual("enogtyve", Cardinal(21, "da"));
            Assert.AreEqual("toogtredive", Cardinal(32, "da"));
            Assert.AreEqual("halvtreds", Cardinal(50, "da"));
            Assert.AreEqual("femoghalvtreds", Cardinal(55, "da"));
            Assert.AreEqual("et hundrede", Cardinal(100, "da"));
            Assert.AreEqual("et hundrede og en", Cardinal(101, "da"));
            Assert.AreEqual("et hundrede og enogtyve", Cardinal(121, "da"));
            Assert.AreEqual("to hundrede", Cardinal(200, "da"));
            Assert.AreEqual("et tusind", Cardinal(1000, "da"));
            Assert.AreEqual("et tusind og en", Cardinal(1001, "da"));
            Assert.AreEqual("et tusind et hundrede og enogtyve", Cardinal(1121, "da"));
            Assert.AreEqual("to tusind", Cardinal(2000, "da"));
            Assert.AreEqual("en million", Cardinal(1_000_000, "da"));
            Assert.AreEqual("to millioner og en", Cardinal(2_000_001, "da"));
            Assert.AreEqual("en milliard", Cardinal(1_000_000_000, "da"));

            Assert.AreEqual("første", Ordinal(1, "da"));
            Assert.AreEqual("anden", Ordinal(2, "da"));
            Assert.AreEqual("tredje", Ordinal(3, "da"));
            Assert.AreEqual("syvende", Ordinal(7, "da"));
            Assert.AreEqual("tyvende", Ordinal(20, "da"));
            Assert.AreEqual("enogtyvende", Ordinal(21, "da"));
            Assert.AreEqual("tredivte", Ordinal(30, "da"));
            Assert.AreEqual("halvtredsindstyvende", Ordinal(50, "da"));
            Assert.AreEqual("hundrede", Ordinal(100, "da"));
            Assert.AreEqual("et hundrede og første", Ordinal(101, "da"));
            Assert.AreEqual("tusinde", Ordinal(1000, "da"));
            Assert.AreEqual("millionte", Ordinal(1_000_000, "da"));
            Assert.AreEqual("21.", Digits(21, "da"));
        }

        // ---- Through xsl:number and the case the picture asks for ------------------------------------------

        [TestMethod]
        public void XslNumberSpellsInTheSameLanguages()
        {
            static string Number(string attributes)
            {
                string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                    + $"<xsl:template match=\"/\"><out><xsl:number {attributes}/></out></xsl:template></xsl:stylesheet>";

                return new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true })
                    .TransformXml("<r/>")["<out>".Length..^"</out>".Length];
            }

            Assert.AreEqual("vingt-et-un", Number("value=\"21\" format=\"w\" lang=\"fr\""));
            Assert.AreEqual("Vingt-Et-Unième", Number("value=\"21\" format=\"Ww\" lang=\"fr\" ordinal=\"yes\""));
            Assert.AreEqual("VEINTIUNO", Number("value=\"21\" format=\"W\" lang=\"es\""));
            Assert.AreEqual("Vigésima Primera", Number("value=\"21\" format=\"Ww\" lang=\"es\" ordinal=\"-a\""));
            Assert.AreEqual("21ª", Number("value=\"21\" format=\"1\" lang=\"es\" ordinal=\"-a\""));
            Assert.AreEqual("Tjueførste", Number("value=\"21\" format=\"Ww\" lang=\"nb\" ordinal=\"yes\""));
            Assert.AreEqual("21:a", Number("value=\"21\" format=\"1\" lang=\"sv\" ordinal=\"yes\""));
            Assert.AreEqual("ETTHUNDRATJUGOETT", Number("value=\"121\" format=\"W\" lang=\"sv\""));
            Assert.AreEqual("enogtyvende", Number("value=\"21\" format=\"w\" lang=\"da\" ordinal=\"yes\""));

            // The region and the standard reach through xsl:number as well.
            Assert.AreEqual("dezasseis", Number("value=\"16\" format=\"w\" lang=\"pt-PT\""));
            Assert.AreEqual("sjuande", Number("value=\"7\" format=\"w\" lang=\"nn\" ordinal=\"yes\""));
        }

        // ---- Dates ---------------------------------------------------------------------------------------

        [TestMethod]
        public void MonthsDaysErasAndHalfDaysAreNamedInEachLanguage()
        {
            // 12 September 2026 is a Saturday, and 09:05 is in the morning.
            Assert.AreEqual("septembre|samedi", Date("[Mn]|[Fn]", "fr"));
            Assert.AreEqual("septiembre|sábado", Date("[Mn]|[Fn]", "es"));
            Assert.AreEqual("setembro|sábado", Date("[Mn]|[Fn]", "pt"));
            Assert.AreEqual("settembre|sabato", Date("[Mn]|[Fn]", "it"));
            Assert.AreEqual("september|lørdag", Date("[Mn]|[Fn]", "nb"));
            Assert.AreEqual("september|laurdag", Date("[Mn]|[Fn]", "nn"));
            Assert.AreEqual("september|lördag", Date("[Mn]|[Fn]", "sv"));
            Assert.AreEqual("september|lørdag", Date("[Mn]|[Fn]", "da"));

            // German capitalises its nouns, which the title-case presentation gives back; 'n' is lower case
            // by definition, whatever the language writes.
            Assert.AreEqual("September|Samstag", Date("[MNn]|[FNn]", "de"));
            Assert.AreEqual("september", Date("[Mn]", "de"));

            // The region does not change the names, and the presentation still decides the case.
            Assert.AreEqual("Septembre|SAMEDI", Date("[MNn]|[FN]", "fr-CA"));
            Assert.AreEqual("Segunda-Feira", Date("[FNn]", "pt", "2026-09-14"));

            // A name longer than the width is cut, since no conventional short form is known.
            Assert.AreEqual("sept", Date("[Mn,4-4]", "fr"));
            Assert.AreEqual("Sáb", Date("[FNn,3-3]", "es"));

            Assert.AreEqual("ap. J.-C.", Date("[E]", "fr"));
            Assert.AreEqual("d. C.", Date("[E]", "es"));
            Assert.AreEqual("e.Kr.", Date("[E]", "sv"));
            Assert.AreEqual("n. Chr.", Date("[E]", "de"));

            Assert.AreEqual("a. m.", Time("[P]", "es"));
            Assert.AreEqual("P. M.", Time("[PN]", "es", "15:00:00"));
            Assert.AreEqual("fm", Time("[P]", "sv"));
            Assert.AreEqual("em", Time("[P]", "sv", "15:00:00"));
            Assert.AreEqual("vorm.", Time("[P]", "de"));
        }

        [TestMethod]
        public void DaysInWordsAndOrdinalDigitsFollowTheLanguageToo()
        {
            Assert.AreEqual("douze septembre", Date("[Dw] [Mn]", "fr"));
            Assert.AreEqual("12e", Date("[D1o]", "fr"));
            Assert.AreEqual("1er septembre", Date("[D1o] [Mn]", "fr", "2026-09-01"));
            Assert.AreEqual("douzième", Date("[Dwo]", "fr"));
            Assert.AreEqual("12º de septiembre", Date("[D1o] de [Mn]", "es"));
            Assert.AreEqual("duodécimo", Date("[Dwo]", "es"));
            Assert.AreEqual("12. september", Date("[D1o] [Mn]", "nb"));
            Assert.AreEqual("tolfte", Date("[Dwo]", "sv"));
            Assert.AreEqual("12:e", Date("[D1o]", "sv"));
            Assert.AreEqual("tolvte", Date("[Dwo]", "da"));
            Assert.AreEqual("zwölfte|12.", Date("[Dwo]|[D1o]", "de"));

            // A language this engine does not have is still answered in English, and says so.
            Assert.AreEqual("[Language: en]twelfth September", Date("[Dwo] [MNn]", "hu"));
        }
    }
}
