using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>Numbers written out in Italian words.</summary>
    /// <remarks>
    /// <para>
    /// Italian writes everything below a million as a single word — <c>millenovecentonovantanove</c> — and
    /// elides where two vowels would meet: <c>ventuno</c> and <c>ventotto</c> lose the <c>i</c> of
    /// <c>venti</c>, and <c>centotto</c> the <c>o</c> of <c>cento</c>. A three that ends a compound takes an
    /// accent, <c>ventitré</c>, and a one before <c>mila</c> loses its vowel, <c>ventunmila</c>. The scales
    /// are separate words on the long scale: <c>un milione</c>, <c>due miliardi</c>.
    /// </para>
    /// <para>
    /// The first ten ordinals are words of their own; from eleven an ordinal is the cardinal with its final
    /// vowel replaced by <c>esimo</c>, except that <c>tré</c> and <c>sei</c> keep theirs — <c>ventitreesimo</c>,
    /// <c>ventiseiesimo</c>. The feminine turns the final <c>o</c> into an <c>a</c>.
    /// </para>
    /// </remarks>
    internal sealed class ItalianNumbers : LanguageWords
    {
        private static readonly string[] s_units =
        {
            "zero", "uno", "due", "tre", "quattro", "cinque", "sei", "sette", "otto", "nove", "dieci",
            "undici", "dodici", "tredici", "quattordici", "quindici", "sedici", "diciassette", "diciotto",
            "diciannove",
        };

        private static readonly string[] s_tens =
        {
            string.Empty, string.Empty, "venti", "trenta", "quaranta", "cinquanta", "sessanta", "settanta",
            "ottanta", "novanta",
        };

        private static readonly string[] s_ordinals =
        {
            "zero", "primo", "secondo", "terzo", "quarto", "quinto", "sesto", "settimo", "ottavo", "nono",
            "decimo",
        };

        private static readonly (ulong Scale, string Singular, string Plural)[] s_scales =
        {
            (1_000_000_000_000_000_000UL, "trilione", "trilioni"),
            (1_000_000_000_000_000UL, "biliardo", "biliardi"),
            (1_000_000_000_000UL, "bilione", "bilioni"),
            (1_000_000_000UL, "miliardo", "miliardi"),
            (1_000_000UL, "milione", "milioni"),
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
                    AppendBelowMillion(builder, part);
                    builder.Append(' ').Append(plural);
                }
            }

            if (rest > 0)
            {
                Separate(builder);
                AppendBelowMillion(builder, rest);
            }

            return builder.ToString();
        }

        /// <inheritdoc/>
        public override string Ordinal(ulong value, string? variation)
        {
            string words;

            if (value <= 10)
            {
                words = s_ordinals[value];
            }
            else
            {
                // The ending replaces the last word's final vowel. A scale of one drops its article, so the
                // millionth is milionesimo rather than un milionesimo, which would be the fraction.
                string cardinal = Cardinal(value);

                if (cardinal.StartsWith("un ", StringComparison.Ordinal))
                {
                    cardinal = cardinal[3..];
                }

                words = ReplaceLastWord(cardinal, Stem) + "esimo";
            }

            return IsFeminine(variation, "a") ? Feminine(words) : words;
        }

        /// <inheritdoc/>
        public override string OrdinalSuffix(ulong value, string? variation)
        {
            return IsFeminine(variation, "a") ? "ª" : "º";
        }

        /// <summary>The stem an ordinal ending goes on.</summary>
        private static string Stem(string word)
        {
            if (word.EndsWith("tré", StringComparison.Ordinal))
            {
                return string.Concat(word.AsSpan(0, word.Length - 1), "e");
            }

            if (word.EndsWith("sei", StringComparison.Ordinal))
            {
                return word;
            }

            if (word.EndsWith("mila", StringComparison.Ordinal))
            {
                return string.Concat(word.AsSpan(0, word.Length - 1), "l");
            }

            return word[^1] is 'a' or 'e' or 'i' or 'o' ? word[..^1] : word;
        }

        private static void Separate(StringBuilder builder)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
        }

        private static void AppendBelowMillion(StringBuilder builder, ulong value)
        {
            ulong thousands = value / 1000;
            ulong rest = value % 1000;

            if (thousands > 0)
            {
                if (thousands == 1)
                {
                    builder.Append("mille");
                }
                else
                {
                    AppendBelowThousand(builder, thousands, apocope: true, compound: true);
                    builder.Append("mila");
                }
            }

            if (rest > 0 || thousands == 0)
            {
                AppendBelowThousand(builder, rest, apocope: false, compound: thousands > 0);
            }
        }

        /// <param name="apocope">Whether a final one loses its vowel, as it does before mila.</param>
        /// <param name="compound">Whether something already stands before this in the word.</param>
        private static void AppendBelowThousand(StringBuilder builder, ulong value, bool apocope, bool compound)
        {
            ulong hundreds = value / 100;
            ulong rest = value % 100;

            if (hundreds > 0)
            {
                if (hundreds > 1)
                {
                    builder.Append(s_units[hundreds]);
                }

                builder.Append("cento");
                compound = true;
            }

            if (rest > 0 || hundreds == 0)
            {
                int start = builder.Length;
                AppendBelowHundred(builder, rest, apocope, compound);

                // Cento loses its o before a word beginning with one: centotto, centottanta.
                if (hundreds > 0 && builder[start] == 'o')
                {
                    builder.Remove(start - 1, 1);
                }
            }
        }

        private static void AppendBelowHundred(StringBuilder builder, ulong value, bool apocope, bool compound)
        {
            if (value < 20)
            {
                builder.Append(value == 1 && apocope ? "un" : value == 3 && compound ? "tré" : s_units[value]);
                return;
            }

            ulong unit = value % 10;
            string tens = s_tens[value / 10];

            // The tens lose their final vowel before uno and otto: ventuno, ventotto.
            builder.Append(unit is 1 or 8 ? tens.AsSpan(0, tens.Length - 1) : tens);

            if (unit != 0)
            {
                builder.Append(unit == 1 && apocope ? "un" : unit == 3 ? "tré" : s_units[unit]);
            }
        }
    }
}
