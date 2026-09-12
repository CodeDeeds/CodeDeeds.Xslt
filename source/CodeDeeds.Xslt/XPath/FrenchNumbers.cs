using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>Numbers written out in French words.</summary>
    /// <remarks>
    /// <para>
    /// French counts in twenties from seventy up — <c>soixante-dix</c> is sixty-ten and <c>quatre-vingts</c>
    /// four twenties — and joins a one to its tens with <c>et</c> up to seventy-one, but not after:
    /// <c>vingt-et-un</c>, <c>soixante-et-onze</c>, <c>quatre-vingt-un</c>. The compounds below a hundred are
    /// hyphenated throughout, which is what the 1990 rectifications ask and what CLDR writes; the hundreds and
    /// thousands stand as separate words. <c>cent</c> and <c>vingt</c> take a plural <c>s</c> when they are
    /// multiplied and end the number — <c>deux cents</c>, <c>quatre-vingts</c> — and lose it when anything
    /// follows: <c>deux cent un</c>, <c>quatre-vingt mille</c>.
    /// </para>
    /// <para>
    /// An ordinal is the cardinal's last word with <c>ième</c> in place of a final <c>e</c>, apart from
    /// <c>premier</c> for one on its own, <c>cinquième</c> and <c>neuvième</c>. The long scale applies, so a
    /// <c>milliard</c> stands between a million and a <c>billion</c>.
    /// </para>
    /// </remarks>
    internal sealed class FrenchNumbers : LanguageWords
    {
        private static readonly string[] s_units =
        {
            "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
            "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf",
        };

        private static readonly string[] s_tens =
        {
            string.Empty, string.Empty, "vingt", "trente", "quarante", "cinquante", "soixante",
        };

        private static readonly (ulong Scale, string Singular, string Plural)[] s_scales =
        {
            (1_000_000_000_000_000_000UL, "trillion", "trillions"),
            (1_000_000_000_000_000UL, "billiard", "billiards"),
            (1_000_000_000_000UL, "billion", "billions"),
            (1_000_000_000UL, "milliard", "milliards"),
            (1_000_000UL, "million", "millions"),
        };

        /// <inheritdoc/>
        public override string Cardinal(ulong value)
        {
            if (value == 0)
            {
                return s_units[0];
            }

            StringBuilder builder = new StringBuilder();
            ulong rest = value;

            foreach ((ulong scale, string singular, string plural) in s_scales)
            {
                ulong part = rest / scale;

                if (part == 0)
                {
                    continue;
                }

                rest -= part * scale;
                Separate(builder);

                if (part == 1)
                {
                    builder.Append("un ").Append(singular);
                }
                else
                {
                    AppendBelowMillion(builder, part, terminal: false);
                    builder.Append(' ').Append(plural);
                }
            }

            if (rest > 0)
            {
                Separate(builder);
                AppendBelowMillion(builder, rest, terminal: true);
            }

            return builder.ToString();
        }

        /// <inheritdoc/>
        public override string Ordinal(ulong value, string? variation)
        {
            bool feminine = IsFeminine(variation, "e", "re");

            if (value == 1)
            {
                return feminine ? "première" : "premier";
            }

            // The ending goes on the last word of the cardinal, hyphenated pieces counting as words: dix-sept
            // is dix-septième and quatre-vingts is quatre-vingtième. A scale of one drops its article, so the
            // millionth is millionième rather than un millionième, which would be the fraction.
            string cardinal = Cardinal(value);

            if (cardinal.StartsWith("un ", StringComparison.Ordinal))
            {
                cardinal = cardinal[3..];
            }

            int last = cardinal.LastIndexOfAny(new[] { ' ', '-' }) + 1;
            return string.Concat(cardinal.AsSpan(0, last), Stem(cardinal[last..]), "ième");
        }

        /// <inheritdoc/>
        public override string OrdinalSuffix(ulong value, string? variation)
        {
            // 1er and 1re, then 2e onwards: only the first has a gender.
            return value == 1 ? (IsFeminine(variation, "e", "re") ? "re" : "er") : "e";
        }

        /// <summary>The stem an ordinal ending goes on.</summary>
        private static string Stem(string word)
        {
            // A plural s comes off first, then the e that the ending replaces; cinq and neuf change to keep
            // the consonant sounding as it did.
            if (word.EndsWith('s') && word is not "trois")
            {
                word = word[..^1];
            }

            return word switch
            {
                "cinq" => "cinqu",
                "neuf" => "neuv",
                _ => word.EndsWith('e') ? word[..^1] : word,
            };
        }

        private static void Separate(StringBuilder builder)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
        }

        private static void AppendBelowMillion(StringBuilder builder, ulong value, bool terminal)
        {
            ulong thousands = value / 1000;
            ulong rest = value % 1000;

            if (thousands > 0)
            {
                // Mille is invariable and takes no article: mille, deux mille, quatre-vingt mille.
                if (thousands > 1)
                {
                    AppendBelowThousand(builder, thousands, terminal: false);
                    builder.Append(' ');
                }

                builder.Append("mille");

                if (rest > 0)
                {
                    builder.Append(' ');
                }
            }

            if (rest > 0 || thousands == 0)
            {
                AppendBelowThousand(builder, rest, terminal);
            }
        }

        private static void AppendBelowThousand(StringBuilder builder, ulong value, bool terminal)
        {
            ulong hundreds = value / 100;
            ulong rest = value % 100;

            if (hundreds > 0)
            {
                if (hundreds > 1)
                {
                    builder.Append(s_units[hundreds]).Append(' ');
                }

                builder.Append("cent");

                if (hundreds > 1 && rest == 0 && terminal)
                {
                    builder.Append('s');
                }

                if (rest > 0)
                {
                    builder.Append(' ');
                }
            }

            if (rest > 0 || hundreds == 0)
            {
                AppendBelowHundred(builder, rest, terminal);
            }
        }

        private static void AppendBelowHundred(StringBuilder builder, ulong value, bool terminal)
        {
            if (value < 20)
            {
                builder.Append(s_units[value]);
                return;
            }

            if (value < 70)
            {
                ulong unit = value % 10;
                builder.Append(s_tens[value / 10]);

                if (unit == 1)
                {
                    builder.Append("-et-un");
                }
                else if (unit != 0)
                {
                    builder.Append('-').Append(s_units[unit]);
                }

                return;
            }

            if (value < 80)
            {
                // Sixty-ten to sixty-nineteen, with the et that seventy-one alone keeps.
                builder.Append("soixante").Append(value == 71 ? "-et-" : "-").Append(s_units[value - 60]);
                return;
            }

            builder.Append("quatre-vingt");

            if (value == 80)
            {
                if (terminal)
                {
                    builder.Append('s');
                }

                return;
            }

            builder.Append('-').Append(s_units[value - 80]);
        }
    }
}
