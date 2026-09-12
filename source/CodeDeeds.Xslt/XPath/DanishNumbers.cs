using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>Numbers written out in Danish words.</summary>
    /// <remarks>
    /// <para>
    /// Danish counts units first below a hundred and runs them into one word with <c>og</c>:
    /// <c>enogtyve</c>. The tens from fifty are the remains of a count in twenties — <c>halvtreds</c> is
    /// half-third times twenty — which their ordinals still spell out in full, <c>halvtredsindstyvende</c>.
    /// The hundreds and thousands are separate words with their multiplier written out, <c>et hundrede</c>
    /// and <c>to tusind</c>, and <c>og</c> introduces the last piece where it is below a hundred:
    /// <c>et hundrede og enogtyve</c>, <c>to millioner og en</c>. The scales are the long ones.
    /// </para>
    /// <para>
    /// An ordinal replaces the last piece, <c>enogtyvende</c>, and a scale of one is dropped from it, so a
    /// hundredth is <c>hundrede</c> — the same word as the cardinal — and a thousandth <c>tusinde</c>.
    /// </para>
    /// </remarks>
    internal sealed class DanishNumbers : LanguageWords
    {
        private static readonly string[] s_units =
        {
            "nul", "en", "to", "tre", "fire", "fem", "seks", "syv", "otte", "ni", "ti",
            "elleve", "tolv", "tretten", "fjorten", "femten", "seksten", "sytten", "atten", "nitten",
        };

        private static readonly string[] s_tens =
        {
            string.Empty, string.Empty, "tyve", "tredive", "fyrre", "halvtreds", "tres", "halvfjerds", "firs",
            "halvfems",
        };

        private static readonly string[] s_ordinals =
        {
            "nulte", "første", "anden", "tredje", "fjerde", "femte", "sjette", "syvende", "ottende", "niende",
            "tiende", "ellevte", "tolvte", "trettende", "fjortende", "femtende", "sekstende", "syttende",
            "attende", "nittende",
        };

        private static readonly string[] s_ordinalTens =
        {
            string.Empty, string.Empty, "tyvende", "tredivte", "fyrretyvende", "halvtredsindstyvende",
            "tresindstyvende", "halvfjerdsindstyvende", "firsindstyvende", "halvfemsindstyvende",
        };

        private static readonly (ulong Scale, string Singular, string Plural)[] s_scales =
        {
            (1_000_000_000_000_000_000UL, "trillion", "trillioner"),
            (1_000_000_000_000_000UL, "billiard", "billiarder"),
            (1_000_000_000_000UL, "billion", "billioner"),
            (1_000_000_000UL, "milliard", "milliarder"),
            (1_000_000UL, "million", "millioner"),
        };

        /// <inheritdoc/>
        public override string Cardinal(ulong value) => Compose(value, ordinal: false);

        /// <inheritdoc/>
        public override string Ordinal(ulong value, string? variation) => Compose(value, ordinal: true);

        /// <inheritdoc/>
        public override string OrdinalSuffix(ulong value, string? variation) => ".";

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
                Separate(builder, following: part);

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
                Separate(builder, following: rest);
                AppendBelowMillion(builder, rest, ordinal);
            }

            return builder.ToString();
        }

        /// <summary>Separates the next piece from what came before, with <c>og</c> where it is under a hundred.</summary>
        private static void Separate(StringBuilder builder, ulong following)
        {
            if (builder.Length > 0)
            {
                builder.Append(following < 100 ? " og " : " ");
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
                    if (!last)
                    {
                        builder.Append("et ");
                    }
                }
                else
                {
                    AppendBelowThousand(builder, thousands, ordinal: false);
                    builder.Append(' ');
                }

                builder.Append(last ? "tusinde" : "tusind");

                if (rest > 0)
                {
                    Separate(builder, following: rest);
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

                if (hundreds == 1)
                {
                    if (!last)
                    {
                        builder.Append("et ");
                    }
                }
                else
                {
                    builder.Append(s_units[hundreds]).Append(' ');
                }

                builder.Append("hundrede");

                if (rest > 0)
                {
                    builder.Append(" og ");
                }
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

            if (unit != 0)
            {
                builder.Append(s_units[unit]).Append("og");
            }

            builder.Append(ordinal ? s_ordinalTens[value / 10] : s_tens[value / 10]);
        }
    }
}
