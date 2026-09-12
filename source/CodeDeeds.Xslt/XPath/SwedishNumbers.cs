using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>Numbers written out in Swedish words.</summary>
    /// <remarks>
    /// <para>
    /// Swedish runs everything below a thousand into one word with the multipliers written out —
    /// <c>etthundratjugoett</c> — and writes the thousands as a word of their own before it:
    /// <c>tvåtusen trehundrafyrtiofem</c>. A thousand of one is <c>ettusen</c>, the two t's having merged.
    /// The scales are the long ones and stand apart: <c>en miljon</c>, <c>två miljarder</c>.
    /// </para>
    /// <para>
    /// An ordinal replaces the last piece of the compound, <c>tjugoförsta</c>, and a scale of one is dropped
    /// from an ordinal, so a hundredth is <c>hundrade</c>. An ordinal written in digits takes <c>:a</c> after
    /// a one or a two and <c>:e</c> otherwise — <c>1:a</c>, <c>2:a</c>, <c>3:e</c>, but <c>11:e</c> and
    /// <c>12:e</c>, which are not spoken as ending in one or two.
    /// </para>
    /// </remarks>
    internal sealed class SwedishNumbers : LanguageWords
    {
        private static readonly string[] s_units =
        {
            "noll", "ett", "två", "tre", "fyra", "fem", "sex", "sju", "åtta", "nio", "tio",
            "elva", "tolv", "tretton", "fjorton", "femton", "sexton", "sjutton", "arton", "nitton",
        };

        private static readonly string[] s_tens =
        {
            string.Empty, string.Empty, "tjugo", "trettio", "fyrtio", "femtio", "sextio", "sjuttio", "åttio",
            "nittio",
        };

        private static readonly string[] s_ordinals =
        {
            "nollte", "första", "andra", "tredje", "fjärde", "femte", "sjätte", "sjunde", "åttonde", "nionde",
            "tionde", "elfte", "tolfte", "trettonde", "fjortonde", "femtonde", "sextonde", "sjuttonde",
            "artonde", "nittonde",
        };

        private static readonly (ulong Scale, string Singular, string Plural)[] s_scales =
        {
            (1_000_000_000_000_000_000UL, "triljon", "triljoner"),
            (1_000_000_000_000_000UL, "biljard", "biljarder"),
            (1_000_000_000_000UL, "biljon", "biljoner"),
            (1_000_000_000UL, "miljard", "miljarder"),
            (1_000_000UL, "miljon", "miljoner"),
        };

        /// <inheritdoc/>
        public override string Cardinal(ulong value) => Compose(value, ordinal: false);

        /// <inheritdoc/>
        public override string Ordinal(ulong value, string? variation) => Compose(value, ordinal: true);

        /// <inheritdoc/>
        public override string OrdinalSuffix(ulong value, string? variation)
        {
            return value % 10 is 1 or 2 && value % 100 is not (11 or 12) ? ":a" : ":e";
        }

        private static string Compose(ulong value, bool ordinal)
        {
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
                bool last = ordinal && rest == 0;
                Separate(builder);

                if (part == 1)
                {
                    if (!last)
                    {
                        builder.Append("en ");
                    }
                }
                else
                {
                    AppendBelowMillion(builder, part, ordinal: false);
                    builder.Append(' ');
                }

                builder.Append(last ? singular + "te" : part == 1 ? singular : plural);
            }

            if (rest > 0 || builder.Length == 0)
            {
                Separate(builder);
                AppendBelowMillion(builder, rest, ordinal);
            }

            return builder.ToString();
        }

        private static void Separate(StringBuilder builder)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
        }

        private static void AppendBelowMillion(StringBuilder builder, ulong value, bool ordinal)
        {
            ulong thousands = value / 1000;
            ulong rest = value % 1000;

            if (thousands > 0)
            {
                bool last = ordinal && rest == 0;

                if (thousands == 1)
                {
                    // Ett and tusen merge their t's; and the one is not written before a thousandth.
                    builder.Append(last ? "tusende" : "ettusen");
                }
                else
                {
                    AppendBelowThousand(builder, thousands, ordinal: false);
                    builder.Append(last ? "tusende" : "tusen");
                }

                if (rest > 0)
                {
                    builder.Append(' ');
                }
            }

            if (rest > 0 || thousands == 0)
            {
                AppendBelowThousand(builder, rest, ordinal);
            }
        }

        private static void AppendBelowThousand(StringBuilder builder, ulong value, bool ordinal)
        {
            ulong hundreds = value / 100;
            ulong rest = value % 100;

            if (hundreds > 0)
            {
                bool last = ordinal && rest == 0;

                if (!(last && hundreds == 1))
                {
                    builder.Append(s_units[hundreds]);
                }

                builder.Append(last ? "hundrade" : "hundra");
            }

            if (rest > 0 || hundreds == 0)
            {
                AppendBelowHundred(builder, rest, ordinal);
            }
        }

        private static void AppendBelowHundred(StringBuilder builder, ulong value, bool ordinal)
        {
            if (value < 20)
            {
                builder.Append(ordinal ? s_ordinals[value] : s_units[value]);
                return;
            }

            ulong unit = value % 10;
            builder.Append(s_tens[value / 10]);

            if (unit == 0)
            {
                if (ordinal)
                {
                    builder.Append("nde");
                }

                return;
            }

            builder.Append(ordinal ? s_ordinals[unit] : s_units[unit]);
        }
    }
}
