using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>Numbers written out in Norwegian words, Bokmål or Nynorsk.</summary>
    /// <remarks>
    /// <para>
    /// Norwegian has counted tens-first since 1951, and writes a compound below a hundred as one word:
    /// <c>tjueen</c>. The hundreds and thousands are separate words with their multiplier written out —
    /// <c>ett hundre</c>, <c>to tusen</c> — and <c>og</c> introduces the last piece where it is below a
    /// hundred: <c>ett hundre og tjueen</c>, <c>to tusen og fem</c>, <c>to millioner og en</c>. The scales
    /// are the long ones, so a <c>milliard</c> stands between a million and a <c>billion</c>.
    /// </para>
    /// <para>
    /// An ordinal replaces the last piece: <c>tjueførste</c>, <c>ett hundre og første</c>. A multiplier of
    /// one is dropped from an ordinal scale, since a hundredth is <c>hundrede</c> and not <c>ett hundrede</c>.
    /// Nynorsk differs in <c>ein</c>, in the ordinals from the seventh up, which end in <c>ande</c> rather
    /// than <c>ende</c>, and in the days of the week.
    /// </para>
    /// </remarks>
    internal sealed class NorwegianNumbers : LanguageWords
    {
        private static readonly string[] s_units =
        {
            "null", "en", "to", "tre", "fire", "fem", "seks", "sju", "åtte", "ni", "ti",
            "elleve", "tolv", "tretten", "fjorten", "femten", "seksten", "sytten", "atten", "nitten",
        };

        private static readonly string[] s_tens =
        {
            string.Empty, string.Empty, "tjue", "tretti", "førti", "femti", "seksti", "sytti", "åtti", "nitti",
        };

        private static readonly string[] s_bokmaalOrdinals =
        {
            "nullte", "første", "andre", "tredje", "fjerde", "femte", "sjette", "sjuende", "åttende", "niende",
            "tiende", "ellevte", "tolvte", "trettende", "fjortende", "femtende", "sekstende", "syttende",
            "attende", "nittende",
        };

        private static readonly string[] s_nynorskOrdinals =
        {
            "nullte", "første", "andre", "tredje", "fjerde", "femte", "sjette", "sjuande", "åttande", "niande",
            "tiande", "ellevte", "tolvte", "trettande", "fjortande", "femtande", "sekstande", "syttande",
            "attande", "nittande",
        };

        private static readonly (ulong Scale, string Singular, string Plural)[] s_scales =
        {
            (1_000_000_000_000_000_000UL, "trillion", "trillioner"),
            (1_000_000_000_000_000UL, "billiard", "billiarder"),
            (1_000_000_000_000UL, "billion", "billioner"),
            (1_000_000_000UL, "milliard", "milliarder"),
            (1_000_000UL, "million", "millioner"),
        };

        private readonly bool m_nynorsk;

        /// <summary>Initializes one written standard.</summary>
        /// <param name="nynorsk">Whether to write Nynorsk rather than Bokmål.</param>
        public NorwegianNumbers(bool nynorsk)
        {
            m_nynorsk = nynorsk;
        }

        private string One => m_nynorsk ? "ein" : "en";

        private string OneNeuter => m_nynorsk ? "eitt" : "ett";

        private string[] Ordinals => m_nynorsk ? s_nynorskOrdinals : s_bokmaalOrdinals;

        private string TensEnding => m_nynorsk ? "ande" : "ende";

        private string Hundredth => m_nynorsk ? "hundrade" : "hundrede";

        private string Thousandth => m_nynorsk ? "tusande" : "tusende";

        /// <inheritdoc/>
        public override string Cardinal(ulong value) => Compose(value, ordinal: false);

        /// <inheritdoc/>
        public override string Ordinal(ulong value, string? variation) => Compose(value, ordinal: true);

        /// <inheritdoc/>
        public override string OrdinalSuffix(ulong value, string? variation) => ".";

        private string Compose(ulong value, bool ordinal)
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
                        builder.Append(One).Append(' ');
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

        private void AppendBelowMillion(StringBuilder builder, ulong value, bool ordinal)
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
                        builder.Append(OneNeuter).Append(' ');
                    }
                }
                else
                {
                    AppendBelowThousand(builder, thousands, ordinal: false);
                    builder.Append(' ');
                }

                builder.Append(last ? Thousandth : "tusen");

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

        private void AppendBelowThousand(StringBuilder builder, ulong value, bool ordinal)
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
                        builder.Append(OneNeuter).Append(' ');
                    }
                }
                else
                {
                    builder.Append(s_units[hundreds]).Append(' ');
                }

                builder.Append(last ? Hundredth : "hundre");

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

        private void AppendBelowHundred(StringBuilder builder, ulong value, bool ordinal)
        {
            if (value < 20)
            {
                builder.Append(ordinal ? Ordinals[value] : value == 1 ? One : s_units[value]);
                return;
            }

            ulong unit = value % 10;
            string tens = s_tens[value / 10];

            if (unit == 0)
            {
                if (ordinal)
                {
                    // Tjue drops its final e before the ending: tjuende, where tretti keeps its i in trettiende.
                    builder.Append(tens.EndsWith('e') ? tens.AsSpan(0, tens.Length - 1) : tens).Append(TensEnding);
                }
                else
                {
                    builder.Append(tens);
                }

                return;
            }

            builder.Append(tens).Append(ordinal ? Ordinals[unit] : unit == 1 ? One : s_units[unit]);
        }
    }
}
