using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>Numbers written out in Portuguese words.</summary>
    /// <remarks>
    /// <para>
    /// Portuguese joins its pieces with <c>e</c>: between tens and units, between hundreds and what follows,
    /// and after a thousand or a scale when what follows is under a hundred or a round hundred — <c>mil e
    /// um</c>, <c>mil e cem</c>, but <c>mil duzentos e trinta e um</c>. A hundred is <c>cem</c> on its own and
    /// <c>cento</c> with anything after it.
    /// </para>
    /// <para>
    /// Two varieties are told apart by the region subtag. Brazil, which is what a bare <c>pt</c> means as it
    /// does in CLDR, counts on the short scale and writes <c>dezesseis</c>; Portugal counts on the long scale
    /// — a thousand millions is <c>mil milhões</c> and a <c>bilião</c> is a million millions — and writes
    /// <c>dezasseis</c>. The ordinals are words of their own to the hundreds, one after another, with a
    /// thousandth's multiplier as an ordinal too: <c>segundo milésimo</c>. The feminine turns every final
    /// <c>o</c> into an <c>a</c>.
    /// </para>
    /// </remarks>
    internal sealed class PortugueseNumbers : LanguageWords
    {
        private static readonly string[] s_units =
        {
            "zero", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove", "dez",
            "onze", "doze", "treze", "catorze", "quinze", "dezesseis", "dezessete", "dezoito", "dezenove",
        };

        private static readonly string[] s_tens =
        {
            string.Empty, string.Empty, "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta",
            "oitenta", "noventa",
        };

        private static readonly string[] s_hundreds =
        {
            string.Empty, "cento", "duzentos", "trezentos", "quatrocentos", "quinhentos", "seiscentos",
            "setecentos", "oitocentos", "novecentos",
        };

        private static readonly string[] s_ordinalUnits =
        {
            string.Empty, "primeiro", "segundo", "terceiro", "quarto", "quinto", "sexto", "sétimo", "oitavo",
            "nono",
        };

        private static readonly string[] s_ordinalTens =
        {
            string.Empty, "décimo", "vigésimo", "trigésimo", "quadragésimo", "quinquagésimo", "sexagésimo",
            "septuagésimo", "octogésimo", "nonagésimo",
        };

        private static readonly string[] s_ordinalHundreds =
        {
            string.Empty, "centésimo", "ducentésimo", "trecentésimo", "quadringentésimo", "quingentésimo",
            "sexcentésimo", "septingentésimo", "octingentésimo", "nongentésimo",
        };

        /// <summary>Brazil's scales, a thousand apart.</summary>
        private static readonly (ulong Scale, string Singular, string Plural, string Ordinal)[] s_shortScales =
        {
            (1_000_000_000_000_000_000UL, "quintilhão", "quintilhões", "quintilionésimo"),
            (1_000_000_000_000_000UL, "quadrilhão", "quadrilhões", "quadrilionésimo"),
            (1_000_000_000_000UL, "trilhão", "trilhões", "trilionésimo"),
            (1_000_000_000UL, "bilhão", "bilhões", "bilionésimo"),
            (1_000_000UL, "milhão", "milhões", "milionésimo"),
        };

        /// <summary>Portugal's scales, a million apart, with a thousand of each in between.</summary>
        private static readonly (ulong Scale, string Singular, string Plural, string Ordinal)[] s_longScales =
        {
            (1_000_000_000_000_000_000UL, "trilião", "triliões", "trilionésimo"),
            (1_000_000_000_000UL, "bilião", "biliões", "bilionésimo"),
            (1_000_000UL, "milhão", "milhões", "milionésimo"),
        };

        private readonly bool m_european;

        /// <summary>Initializes one variety.</summary>
        /// <param name="european">Whether to write as Portugal does rather than as Brazil does.</param>
        public PortugueseNumbers(bool european)
        {
            m_european = european;
        }

        private (ulong Scale, string Singular, string Plural, string Ordinal)[] Scales =>
            m_european ? s_longScales : s_shortScales;

        /// <inheritdoc/>
        public override string Cardinal(ulong value)
        {
            if (value == 0)
            {
                return s_units[0];
            }

            CharStringBuilder builder = new CharStringBuilder(stackalloc char[128]);
            ulong rest = value;

            foreach ((ulong scale, string singular, string plural, _) in Scales)
            {
                ulong part = rest / scale;

                if (part == 0)
                {
                    continue;
                }

                rest -= part * scale;
                Join(ref builder, part);

                if (part == 1)
                {
                    builder.Append("um ");
                    builder.Append(singular);
                }
                else
                {
                    AppendBelowMillion(ref builder, part);
                    builder.Append(' ');
                    builder.Append(plural);
                }
            }

            if (rest > 0)
            {
                Join(ref builder, rest);
                AppendBelowMillion(ref builder, rest);
            }

            return builder.ToString();
        }

        /// <inheritdoc/>
        public override string Ordinal(ulong value, string? variation)
        {
            if (value == 0)
            {
                return s_units[0];
            }

            CharStringBuilder builder = new CharStringBuilder(stackalloc char[128]);
            ulong rest = value;

            foreach ((ulong scale, _, _, string ordinal) in Scales)
            {
                ulong part = rest / scale;

                if (part == 0)
                {
                    continue;
                }

                rest -= part * scale;
                Separate(ref builder);

                if (part > 1)
                {
                    AppendOrdinalBelowMillion(ref builder, part);
                    builder.Append(' ');
                }

                builder.Append(ordinal);
            }

            if (rest > 0)
            {
                Separate(ref builder);
                AppendOrdinalBelowMillion(ref builder, rest);
            }

            string words = builder.ToString();
            return IsFeminine(variation, "a") ? Feminine(words) : words;
        }

        /// <inheritdoc/>
        public override string OrdinalSuffix(ulong value, string? variation)
        {
            return IsFeminine(variation, "a") ? "ª" : "º";
        }

        private string Unit(ulong value)
        {
            if (m_european)
            {
                switch (value)
                {
                    case 16: return "dezasseis";
                    case 17: return "dezassete";
                    case 19: return "dezanove";
                }
            }

            return s_units[value];
        }

        private static void Separate(ref CharStringBuilder builder)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
        }

        /// <summary>
        /// Joins what follows a scale to what came before: with <c>e</c> where it is under a hundred or a
        /// round hundred, and with a space otherwise.
        /// </summary>
        private static void Join(ref CharStringBuilder builder, ulong following)
        {
            if (builder.Length > 0)
            {
                builder.Append(following < 100 || following % 100 == 0 ? " e " : " ");
            }
        }

        private void AppendBelowMillion(ref CharStringBuilder builder, ulong value)
        {
            ulong thousands = value / 1000;
            ulong rest = value % 1000;

            if (thousands > 0)
            {
                if (thousands > 1)
                {
                    AppendBelowThousand(ref builder, thousands);
                    builder.Append(' ');
                }

                builder.Append("mil");

                if (rest > 0)
                {
                    Join(ref builder, rest);
                }
            }

            if (rest > 0 || thousands == 0)
            {
                AppendBelowThousand(ref builder, rest);
            }
        }

        private void AppendBelowThousand(ref CharStringBuilder builder, ulong value)
        {
            if (value == 100)
            {
                builder.Append("cem");
                return;
            }

            ulong hundreds = value / 100;
            ulong rest = value % 100;

            if (hundreds > 0)
            {
                builder.Append(s_hundreds[hundreds]);

                if (rest > 0)
                {
                    builder.Append(" e ");
                }
            }

            if (rest > 0 || hundreds == 0)
            {
                AppendBelowHundred(ref builder, rest);
            }
        }

        private void AppendBelowHundred(ref CharStringBuilder builder, ulong value)
        {
            if (value < 20)
            {
                builder.Append(Unit(value));
                return;
            }

            builder.Append(s_tens[value / 10]);

            if (value % 10 != 0)
            {
                builder.Append(" e ");
                builder.Append(Unit(value % 10));
            }
        }

        private static void AppendOrdinalBelowMillion(ref CharStringBuilder builder, ulong value)
        {
            ulong thousands = value / 1000;
            ulong rest = value % 1000;

            if (thousands > 0)
            {
                if (thousands > 1)
                {
                    AppendOrdinalBelowThousand(ref builder, thousands);
                    builder.Append(' ');
                }

                builder.Append("milésimo");

                if (rest > 0)
                {
                    builder.Append(' ');
                }
            }

            if (rest > 0 || thousands == 0)
            {
                AppendOrdinalBelowThousand(ref builder, rest);
            }
        }

        private static void AppendOrdinalBelowThousand(ref CharStringBuilder builder, ulong value)
        {
            ulong hundreds = value / 100;
            ulong rest = value % 100;

            if (hundreds > 0)
            {
                builder.Append(s_ordinalHundreds[hundreds]);

                if (rest > 0)
                {
                    builder.Append(' ');
                }
            }

            if (rest == 0)
            {
                return;
            }

            if (rest < 10)
            {
                builder.Append(s_ordinalUnits[rest]);
                return;
            }

            builder.Append(s_ordinalTens[rest / 10]);

            if (rest % 10 != 0)
            {
                builder.Append(' ');
                builder.Append(s_ordinalUnits[rest % 10]);
            }
        }
    }
}
