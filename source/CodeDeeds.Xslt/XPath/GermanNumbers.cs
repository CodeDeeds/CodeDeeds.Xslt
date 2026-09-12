using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>Numbers written out in German words.</summary>
    /// <remarks>
    /// <para>
    /// The second language this engine learned to spell numbers in, and the one the conformance suite asks
    /// for by name besides English; the others are found through <see cref="Languages"/>. A language none
    /// of them covers falls back to <see cref="EnglishNumbers"/>, which is what the specification asks a
    /// processor to do for a language it does not have: use one it does have, and do not raise an error
    /// over it.
    /// </para>
    /// <para>
    /// German writes everything below a million as a single word, with the units before the tens —
    /// <c>einhundertvierunddreißigtausendachthundertsechzehn</c> — and starts a new word at each million:
    /// <c>zwei millionen …</c>. Case is the picture's business rather than this class's, so everything here
    /// is written in lower case and <c>W</c> or <c>Ww</c> raises it afterwards.
    /// </para>
    /// <para>
    /// An ordinal is not a suffix on the cardinal: 201 is <c>zweihunderteins</c> where the ordinal is
    /// <c>zweihunderterste</c>, and 1 is <c>eins</c> against <c>erste</c>. So the ordinal is built rather
    /// than patched, by telling each step whether it is writing the last piece of the number — the piece
    /// that carries the ending.
    /// </para>
    /// </remarks>
    internal static class GermanNumbers
    {
        /// <summary>The numbers below twenty, each of which has a word to itself.</summary>
        private static readonly string[] s_units =
        {
            "null", "eins", "zwei", "drei", "vier", "fünf", "sechs", "sieben", "acht", "neun", "zehn",
            "elf", "zwölf", "dreizehn", "vierzehn", "fünfzehn", "sechzehn", "siebzehn", "achtzehn",
            "neunzehn",
        };

        /// <summary>
        /// The ordinal stems below twenty, which an ending is added to.
        /// </summary>
        /// <remarks>
        /// Mostly the cardinal and a <c>t</c>, with four that are not: <c>erst</c> against <c>eins</c>,
        /// <c>dritt</c> against <c>drei</c>, <c>siebt</c> against <c>sieben</c>, and <c>acht</c>, which
        /// already ends in the letter the rest are given.
        /// </remarks>
        private static readonly string[] s_ordinals =
        {
            "nullt", "erst", "zweit", "dritt", "viert", "fünft", "sechst", "siebt", "acht", "neunt",
            "zehnt", "elft", "zwölft", "dreizehnt", "vierzehnt", "fünfzehnt", "sechzehnt", "siebzehnt",
            "achtzehnt", "neunzehnt",
        };

        /// <summary>The tens, indexed by the tens digit.</summary>
        private static readonly string[] s_tens =
        {
            string.Empty, string.Empty, "zwanzig", "dreißig", "vierzig", "fünfzig", "sechzig", "siebzig",
            "achtzig", "neunzig",
        };

        /// <summary>
        /// The scales, largest first, each named in the singular and the plural.
        /// </summary>
        /// <remarks>
        /// German counts on the long scale, so a <c>Milliarde</c> stands between a million and a
        /// <c>Billion</c> and the false friends are all one step out: what English calls a billion is a
        /// <c>Milliarde</c>, and a German <c>Billion</c> is an English trillion.
        /// </remarks>
        private static readonly (ulong Scale, string Singular, string Plural)[] s_scales =
        {
            (1_000_000_000_000_000_000UL, "trillion", "trillionen"),
            (1_000_000_000_000_000UL, "billiarde", "billiarden"),
            (1_000_000_000_000UL, "billion", "billionen"),
            (1_000_000_000UL, "milliarde", "milliarden"),
            (1_000_000UL, "million", "millionen"),
        };

        /// <summary>
        /// Whether a language code asks for German.
        /// </summary>
        /// <remarks>
        /// The primary subtag alone, so that <c>de-AT</c> and <c>de-CH</c> are German too. Swiss German
        /// writes <c>ss</c> where the others write <c>ß</c>, which is a distinction this engine does not
        /// make.
        /// </remarks>
        /// <param name="language">The language code asked for, if any.</param>
        public static bool Speaks(string? language)
        {
            if (language is null)
            {
                return false;
            }

            int hyphen = language.IndexOf('-');
            ReadOnlySpan<char> primary = hyphen < 0 ? language : language.AsSpan(0, hyphen);
            return primary.Equals("de", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Writes a number out in lower-case words.</summary>
        /// <param name="value">The number.</param>
        public static string Cardinal(ulong value)
        {
            StringBuilder builder = new StringBuilder();
            Append(builder, value, null);
            return builder.ToString();
        }

        /// <summary>Writes a number out as a lower-case ordinal.</summary>
        /// <param name="value">The number.</param>
        /// <param name="variation">The variation the picture named, which chooses the ending.</param>
        public static string Ordinal(ulong value, string? variation)
        {
            StringBuilder builder = new StringBuilder();
            Append(builder, value, EndingOf(variation));
            return builder.ToString();
        }

        /// <summary>
        /// The ending a German ordinal takes, which the picture chooses.
        /// </summary>
        /// <remarks>
        /// Two ways of asking, and the suite uses both. A variation beginning with a hyphen is the ending
        /// itself, which is the scheme called <em>inflection</em>: <c>o(-er)</c> gives <c>erster</c> and
        /// <c>o(-es)</c> gives <c>erstes</c>, because German inflects an ordinal for the case, number and
        /// gender of what it stands before and no processor can know those. One beginning with a per cent
        /// sign names a CLDR rule set instead, and the only spelled-ordinal set German has makes no such
        /// distinction — so that and an absent variation both give the bare <c>-e</c>.
        /// </remarks>
        /// <param name="variation">The variation the picture named, if any.</param>
        private static string EndingOf(string? variation)
        {
            return variation is not null && variation.StartsWith('-') ? variation[1..] : "e";
        }

        /// <summary>Writes a number, scales first.</summary>
        /// <param name="builder">Where to write it.</param>
        /// <param name="value">The number.</param>
        /// <param name="ending">The ordinal ending, or <see langword="null"/> for a cardinal.</param>
        private static void Append(StringBuilder builder, ulong value, string? ending)
        {
            ulong rest = value;

            foreach ((ulong scale, string singular, string plural) in s_scales)
            {
                ulong part = rest / scale;

                if (part == 0)
                {
                    continue;
                }

                rest -= part * scale;
                bool last = rest == 0 && ending is not null;

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                if (part == 1)
                {
                    // One of a scale is "eine Million" — feminine, and with the article written out. As an
                    // ordinal the article goes, which is what makes the millionth "millionste" rather than
                    // "eine millionste".
                    if (!last)
                    {
                        builder.Append("eine ");
                    }

                    builder.Append(singular);
                }
                else
                {
                    AppendBelowMillion(builder, part, null, terminal: false);
                    builder.Append(' ').Append(last ? singular : plural);
                }

                if (last)
                {
                    builder.Append("st").Append(ending);
                }
            }

            if (rest > 0 || builder.Length == 0)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                AppendBelowMillion(builder, rest, ending, terminal: true);
            }
        }

        /// <summary>Writes a number below a million, which is one word however long it is.</summary>
        /// <param name="builder">Where to write it.</param>
        /// <param name="value">The number.</param>
        /// <param name="ending">The ordinal ending, or <see langword="null"/> for a cardinal.</param>
        /// <param name="terminal">Whether nothing follows this in the word.</param>
        private static void AppendBelowMillion(
            StringBuilder builder, ulong value, string? ending, bool terminal)
        {
            ulong thousands = value / 1000;
            ulong rest = value % 1000;

            if (thousands > 0)
            {
                AppendBelowThousand(builder, thousands, null, terminal: false);
                builder.Append("tausend");

                if (rest == 0 && ending is not null)
                {
                    builder.Append("st").Append(ending);
                }
            }

            if (rest > 0 || thousands == 0)
            {
                AppendBelowThousand(builder, rest, ending, terminal);
            }
        }

        /// <summary>Writes a number below a thousand.</summary>
        /// <param name="builder">Where to write it.</param>
        /// <param name="value">The number.</param>
        /// <param name="ending">The ordinal ending, or <see langword="null"/> for a cardinal.</param>
        /// <param name="terminal">Whether nothing follows this in the word.</param>
        private static void AppendBelowThousand(
            StringBuilder builder, ulong value, string? ending, bool terminal)
        {
            ulong hundreds = value / 100;
            ulong rest = value % 100;

            if (hundreds > 0)
            {
                // "einhundert" rather than the bare "hundert", which is how the suite spells it and the
                // form that keeps a hundred and a thousand written the same way.
                builder.Append(hundreds == 1 ? "ein" : s_units[hundreds]).Append("hundert");

                if (rest == 0 && ending is not null)
                {
                    builder.Append("st").Append(ending);
                }
            }

            if (rest > 0 || hundreds == 0)
            {
                AppendBelowHundred(builder, rest, ending, terminal);
            }
        }

        /// <summary>Writes a number below a hundred, units before tens.</summary>
        /// <param name="builder">Where to write it.</param>
        /// <param name="value">The number.</param>
        /// <param name="ending">The ordinal ending, or <see langword="null"/> for a cardinal.</param>
        /// <param name="terminal">Whether nothing follows this in the word.</param>
        private static void AppendBelowHundred(
            StringBuilder builder, ulong value, string? ending, bool terminal)
        {
            if (value < 20)
            {
                if (ending is not null)
                {
                    builder.Append(s_ordinals[value]).Append(ending);
                }
                else
                {
                    // One is "eins" only where the word ends there: 1 is "eins", 201 is "zweihunderteins",
                    // but 21 is "einundzwanzig" and 1000 is "eintausend".
                    builder.Append(value == 1 && !terminal ? "ein" : s_units[value]);
                }

                return;
            }

            if (value % 10 != 0)
            {
                builder.Append(value % 10 == 1 ? "ein" : s_units[value % 10]).Append("und");
            }

            builder.Append(s_tens[value / 10]);

            if (ending is not null)
            {
                // From twenty up the ordinal is built with "st" rather than the "t" the smaller numbers
                // take, and it goes on the tens: 21st is "einundzwanzigste".
                builder.Append("st").Append(ending);
            }
        }
    }
}
