using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Numbers spelled out in one language: the words for a count and for a rank, and the mark that makes
    /// digits a rank.
    /// </summary>
    /// <remarks>
    /// One instance per language, found through <see cref="Languages"/>. Everything is written in lower case
    /// and the picture raises it afterwards, so a language that capitalises its numbers by convention is
    /// still spelled here as its grammar has them.
    /// </remarks>
    internal abstract class LanguageWords
    {
        /// <summary>Writes a number out as a count: <c>twenty-one</c>.</summary>
        /// <param name="value">The number.</param>
        public abstract string Cardinal(ulong value);

        /// <summary>Writes a number out as a rank: <c>twenty-first</c>.</summary>
        /// <param name="value">The number.</param>
        /// <param name="variation">
        /// The variation the picture named, which a language may read as a gender or an inflection, or
        /// <see langword="null"/> where none was.
        /// </param>
        public abstract string Ordinal(ulong value, string? variation);

        /// <summary>What follows digits to make them a rank: <c>st</c> in 1st, a full stop in 1.</summary>
        /// <param name="value">The number.</param>
        /// <param name="variation">The variation the picture named, if any.</param>
        public abstract string OrdinalSuffix(ulong value, string? variation);

        /// <summary>
        /// Whether a variation asks for the feminine form, in a language whose ordinals have one.
        /// </summary>
        /// <remarks>
        /// Two ways of asking, as with German's inflections. An ending written after a hyphen is the scheme
        /// called <em>inflection</em>, and is what the <c>ordinal</c> attribute of <c>xsl:number</c> holds:
        /// <c>-a</c> in Spanish, <c>-e</c> in French. A CLDR rule set is named after a per cent sign, and the
        /// feminine ones say so in their name.
        /// </remarks>
        /// <param name="variation">The variation the picture named, if any.</param>
        /// <param name="endings">The feminine endings, written without the hyphen.</param>
        protected static bool IsFeminine(string? variation, params string[] endings)
        {
            if (variation is null)
            {
                return false;
            }

            if (variation.Contains("feminine", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (string ending in endings)
            {
                if (variation.Equals("-" + ending, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Replaces the last word of a phrase, which is how a rank is made of a count in most languages.</summary>
        /// <param name="phrase">The words, separated by spaces.</param>
        /// <param name="replace">What to make of the last one.</param>
        protected static string ReplaceLastWord(string phrase, Func<string, string> replace)
        {
            int last = phrase.LastIndexOf(' ') + 1;
            return string.Concat(phrase.AsSpan(0, last), replace(phrase[last..]));
        }

        /// <summary>Turns a final <c>o</c> into an <c>a</c> in every word, which is the feminine of a Romance ordinal.</summary>
        /// <param name="words">The masculine form.</param>
        protected static string Feminine(string words)
        {
            StringBuilder builder = new StringBuilder(words);

            for (int i = 0; i < builder.Length; i++)
            {
                if (builder[i] == 'o' && (i + 1 == builder.Length || builder[i + 1] == ' '))
                {
                    builder[i] = 'a';
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// The names a calendar is written with in one language: months, days, eras and the halves of the day.
    /// </summary>
    internal sealed class DateNames
    {
        /// <summary>Initializes the names of a language.</summary>
        /// <param name="months">The twelve months, January first, as the language writes them in running text.</param>
        /// <param name="days">The seven days, Monday first.</param>
        /// <param name="beforeChrist">The era before the year 1.</param>
        /// <param name="annoDomini">The era from the year 1.</param>
        /// <param name="am">The marker for the morning, in its shortest form.</param>
        /// <param name="pm">The marker for the afternoon, in its shortest form.</param>
        /// <param name="amLong">The morning marker where there is room for a longer one, or null for the same.</param>
        /// <param name="pmLong">The afternoon marker where there is room, or null for the same.</param>
        /// <param name="monthAbbreviations">Conventional short forms of the months, or null where the language has none this engine knows.</param>
        /// <param name="dayAbbreviations">Conventional short forms of the days, or null.</param>
        public DateNames(
            string[] months,
            string[] days,
            string beforeChrist,
            string annoDomini,
            string am,
            string pm,
            string? amLong = null,
            string? pmLong = null,
            string[]? monthAbbreviations = null,
            string[]? dayAbbreviations = null)
        {
            Months = months;
            Days = days;
            BeforeChrist = beforeChrist;
            AnnoDomini = annoDomini;
            m_am = am;
            m_pm = pm;
            m_amLong = amLong ?? am;
            m_pmLong = pmLong ?? pm;
            MonthAbbreviations = monthAbbreviations;
            DayAbbreviations = dayAbbreviations;
        }

        private readonly string m_am;
        private readonly string m_pm;
        private readonly string m_amLong;
        private readonly string m_pmLong;

        /// <summary>The months, January first.</summary>
        public string[] Months { get; }

        /// <summary>The days, Monday first.</summary>
        public string[] Days { get; }

        /// <summary>Conventional short forms of the months, or <see langword="null"/> where a name is cut instead.</summary>
        public string[]? MonthAbbreviations { get; }

        /// <summary>Conventional short forms of the days, or <see langword="null"/>.</summary>
        public string[]? DayAbbreviations { get; }

        /// <summary>The era before the year 1.</summary>
        public string BeforeChrist { get; }

        /// <summary>The era from the year 1.</summary>
        public string AnnoDomini { get; }

        /// <summary>
        /// The am/pm marker in the width asked for.
        /// </summary>
        /// <remarks>
        /// The marker has no one spelling, so the width chooses between the spellings rather than padding or
        /// cutting one of them: English is <c>a.m.</c> where there is room for four and <c>am</c> where there
        /// is not.
        /// </remarks>
        /// <param name="morning">Whether the time is before noon.</param>
        /// <param name="maximum">The width to stay within.</param>
        public string HalfDay(bool morning, int maximum)
        {
            return maximum < 4 ? (morning ? m_am : m_pm) : (morning ? m_amLong : m_pmLong);
        }
    }

    /// <summary>
    /// The languages this engine spells numbers and writes dates in, found by the primary subtag of a
    /// language tag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// English is what every other request falls back to. <c>fn:format-integer</c> asks for exactly that: a
    /// processor that does not have the language wanted uses one it does have, and must not raise an error
    /// over it. The date functions say so as well, with the <c>[Language: en]</c> prefix §9.8.4.8 asks for.
    /// </para>
    /// <para>
    /// The primary subtag decides, so <c>de-AT</c> is German and <c>fr-CA</c> is French. Two tags reach
    /// something more particular: <c>pt-PT</c> is European Portuguese, which counts on the long scale and
    /// spells three of its teens differently, and <c>nn</c> is Nynorsk, which differs from Bokmål in a few
    /// numbers, its ordinals and its days.
    /// </para>
    /// </remarks>
    internal static class Languages
    {
        private static readonly EnglishWords s_english = new EnglishWords();
        private static readonly GermanWords s_german = new GermanWords();
        private static readonly FrenchNumbers s_french = new FrenchNumbers();
        private static readonly SpanishNumbers s_spanish = new SpanishNumbers();
        private static readonly PortugueseNumbers s_portuguese = new PortugueseNumbers(european: false);
        private static readonly PortugueseNumbers s_europeanPortuguese = new PortugueseNumbers(european: true);
        private static readonly ItalianNumbers s_italian = new ItalianNumbers();
        private static readonly NorwegianNumbers s_bokmaal = new NorwegianNumbers(nynorsk: false);
        private static readonly NorwegianNumbers s_nynorsk = new NorwegianNumbers(nynorsk: true);
        private static readonly SwedishNumbers s_swedish = new SwedishNumbers();
        private static readonly DanishNumbers s_danish = new DanishNumbers();

        /// <summary>The English names, which are also what an unknown language is answered with.</summary>
        public static DateNames English { get; } = new DateNames(
            new[]
            {
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December",
            },
            new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" },
            "BC",
            "AD",
            "am",
            "pm",
            "a.m.",
            "p.m.",

            // The conventional short forms, used before a name is cut to fit a width (§9.8.4.2): [FNn,3-5]
            // is Thurs and [MNn,3-4] is Sept, and only where the abbreviation is too long is it cut.
            new[] { "Jan", "Feb", "Mar", "Apr", "May", "June", "July", "Aug", "Sept", "Oct", "Nov", "Dec" },
            new[] { "Mon", "Tues", "Weds", "Thurs", "Fri", "Sat", "Sun" });

        private static readonly DateNames s_germanNames = new DateNames(
            new[]
            {
                "Januar", "Februar", "März", "April", "Mai", "Juni",
                "Juli", "August", "September", "Oktober", "November", "Dezember",
            },
            new[] { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" },
            "v. Chr.",
            "n. Chr.",
            "vorm.",
            "nachm.");

        private static readonly DateNames s_frenchNames = new DateNames(
            new[]
            {
                "janvier", "février", "mars", "avril", "mai", "juin",
                "juillet", "août", "septembre", "octobre", "novembre", "décembre",
            },
            new[] { "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche" },
            "av. J.-C.",
            "ap. J.-C.",
            "AM",
            "PM");

        private static readonly DateNames s_spanishNames = new DateNames(
            new[]
            {
                "enero", "febrero", "marzo", "abril", "mayo", "junio",
                "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
            },
            new[] { "lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo" },
            "a. C.",
            "d. C.",
            "a. m.",
            "p. m.");

        private static readonly DateNames s_portugueseNames = new DateNames(
            new[]
            {
                "janeiro", "fevereiro", "março", "abril", "maio", "junho",
                "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
            },
            new[] { "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado", "domingo" },
            "a.C.",
            "d.C.",
            "AM",
            "PM");

        private static readonly DateNames s_italianNames = new DateNames(
            new[]
            {
                "gennaio", "febbraio", "marzo", "aprile", "maggio", "giugno",
                "luglio", "agosto", "settembre", "ottobre", "novembre", "dicembre",
            },
            new[] { "lunedì", "martedì", "mercoledì", "giovedì", "venerdì", "sabato", "domenica" },
            "a.C.",
            "d.C.",
            "AM",
            "PM");

        private static readonly string[] s_norwegianMonths =
        {
            "januar", "februar", "mars", "april", "mai", "juni",
            "juli", "august", "september", "oktober", "november", "desember",
        };

        private static readonly DateNames s_bokmaalNames = new DateNames(
            s_norwegianMonths,
            new[] { "mandag", "tirsdag", "onsdag", "torsdag", "fredag", "lørdag", "søndag" },
            "f.Kr.",
            "e.Kr.",
            "a.m.",
            "p.m.");

        private static readonly DateNames s_nynorskNames = new DateNames(
            s_norwegianMonths,
            new[] { "måndag", "tysdag", "onsdag", "torsdag", "fredag", "laurdag", "sundag" },
            "f.Kr.",
            "e.Kr.",
            "a.m.",
            "p.m.");

        private static readonly DateNames s_swedishNames = new DateNames(
            new[]
            {
                "januari", "februari", "mars", "april", "maj", "juni",
                "juli", "augusti", "september", "oktober", "november", "december",
            },
            new[] { "måndag", "tisdag", "onsdag", "torsdag", "fredag", "lördag", "söndag" },
            "f.Kr.",
            "e.Kr.",
            "fm",
            "em");

        private static readonly DateNames s_danishNames = new DateNames(
            new[]
            {
                "januar", "februar", "marts", "april", "maj", "juni",
                "juli", "august", "september", "oktober", "november", "december",
            },
            new[] { "mandag", "tirsdag", "onsdag", "torsdag", "fredag", "lørdag", "søndag" },
            "f.Kr.",
            "e.Kr.",
            "AM",
            "PM");

        /// <summary>The number words of a language, or <see langword="null"/> where this engine has none for it.</summary>
        /// <param name="language">The language tag asked for, if any.</param>
        public static LanguageWords? WordsIn(string? language)
        {
            return Primary(language) switch
            {
                "en" => s_english,
                "de" => s_german,
                "fr" => s_french,
                "es" => s_spanish,
                "pt" => IsRegion(language, "PT") ? s_europeanPortuguese : s_portuguese,
                "it" => s_italian,
                "nb" or "no" => s_bokmaal,
                "nn" => s_nynorsk,
                "sv" => s_swedish,
                "da" => s_danish,
                _ => null,
            };
        }

        /// <summary>The number words of a language, or English's where this engine has none for it.</summary>
        /// <param name="language">The language tag asked for, if any.</param>
        public static LanguageWords Words(string? language) => WordsIn(language) ?? s_english;

        /// <summary>The calendar names of a language, or <see langword="null"/> where this engine has none for it.</summary>
        /// <param name="language">The language tag asked for, if any.</param>
        public static DateNames? NamesIn(string? language)
        {
            return Primary(language) switch
            {
                "en" => English,
                "de" => s_germanNames,
                "fr" => s_frenchNames,
                "es" => s_spanishNames,
                "pt" => s_portugueseNames,
                "it" => s_italianNames,
                "nb" or "no" => s_bokmaalNames,
                "nn" => s_nynorskNames,
                "sv" => s_swedishNames,
                "da" => s_danishNames,
                _ => null,
            };
        }

        /// <summary>Whether this engine spells numbers in a language.</summary>
        /// <param name="language">The language tag asked for, if any.</param>
        public static bool Spells(string? language) => WordsIn(language) is not null;

        /// <summary>The primary subtag of a language tag, in lower case, or an empty string for none.</summary>
        private static string Primary(string? language)
        {
            if (language is null)
            {
                return string.Empty;
            }

            int hyphen = language.IndexOf('-');
            return (hyphen < 0 ? language : language[..hyphen]).ToLowerInvariant();
        }

        /// <summary>Whether a language tag's second subtag names a region.</summary>
        private static bool IsRegion(string? language, string region)
        {
            if (language is null)
            {
                return false;
            }

            string[] subtags = language.Split('-');
            return subtags.Length > 1 && subtags[1].Equals(region, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>English, through <see cref="EnglishNumbers"/>.</summary>
        private sealed class EnglishWords : LanguageWords
        {
            public override string Cardinal(ulong value) => EnglishNumbers.Cardinal(value);

            public override string Ordinal(ulong value, string? variation) => EnglishNumbers.Ordinal(value);

            public override string OrdinalSuffix(ulong value, string? variation) => EnglishNumbers.OrdinalSuffix(value);
        }

        /// <summary>German, through <see cref="GermanNumbers"/>.</summary>
        private sealed class GermanWords : LanguageWords
        {
            public override string Cardinal(ulong value) => GermanNumbers.Cardinal(value);

            public override string Ordinal(ulong value, string? variation) => GermanNumbers.Ordinal(value, variation);

            // An ordinal written in digits is a full stop in German: 3. against 3rd.
            public override string OrdinalSuffix(ulong value, string? variation) => ".";
        }
    }
}
