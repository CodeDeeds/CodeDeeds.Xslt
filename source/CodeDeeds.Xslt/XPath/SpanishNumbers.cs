using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>Numbers written out in Spanish words.</summary>
    /// <remarks>
    /// <para>
    /// The numbers to twenty-nine each have a word of their own — <c>veintiuno</c> is one word where
    /// <c>treinta y uno</c> is three — and a hundred is <c>cien</c> on its own and <c>ciento</c> with anything
    /// after it. One loses its final vowel before a noun, which is what a scale is: <c>veintiún mil</c>,
    /// <c>un millón</c>. The scale is the long one by way of a thousand: a thousand millions is
    /// <c>mil millones</c>, and a <c>billón</c> is a million millions.
    /// </para>
    /// <para>
    /// The ordinals are words of their own up to the hundreds, written one after another — <c>centésimo
    /// vigésimo primero</c> — and a thousandth or a millionth is its multiplier run into <c>milésimo</c> or
    /// <c>millonésimo</c>. The feminine, asked for with <c>-a</c>, turns every final <c>o</c> into an
    /// <c>a</c>; an ordinal written in digits carries the ordinal indicator of the same gender, <c>1º</c> or
    /// <c>1ª</c>.
    /// </para>
    /// </remarks>
    internal sealed class SpanishNumbers : LanguageWords
    {
        private static readonly string[] s_units =
        {
            "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez",
            "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve",
            "veinte", "veintiuno", "veintidós", "veintitrés", "veinticuatro", "veinticinco", "veintiséis",
            "veintisiete", "veintiocho", "veintinueve",
        };

        private static readonly string[] s_tens =
        {
            string.Empty, string.Empty, string.Empty, "treinta", "cuarenta", "cincuenta", "sesenta", "setenta",
            "ochenta", "noventa",
        };

        private static readonly string[] s_hundreds =
        {
            string.Empty, "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos",
            "setecientos", "ochocientos", "novecientos",
        };

        private static readonly string[] s_ordinalUnits =
        {
            string.Empty, "primero", "segundo", "tercero", "cuarto", "quinto", "sexto", "séptimo", "octavo",
            "noveno", "décimo", "undécimo", "duodécimo", "decimotercero", "decimocuarto", "decimoquinto",
            "decimosexto", "decimoséptimo", "decimoctavo", "decimonoveno",
        };

        private static readonly string[] s_ordinalTens =
        {
            string.Empty, string.Empty, "vigésimo", "trigésimo", "cuadragésimo", "quincuagésimo", "sexagésimo",
            "septuagésimo", "octogésimo", "nonagésimo",
        };

        private static readonly string[] s_ordinalHundreds =
        {
            string.Empty, "centésimo", "ducentésimo", "tricentésimo", "cuadringentésimo", "quingentésimo",
            "sexcentésimo", "septingentésimo", "octingentésimo", "noningentésimo",
        };

        /// <summary>The scales above a thousand, each a million times the one before.</summary>
        private static readonly (ulong Scale, string Singular, string Plural, string Ordinal)[] s_scales =
        {
            (1_000_000_000_000_000_000UL, "trillón", "trillones", "trillonésimo"),
            (1_000_000_000_000UL, "billón", "billones", "billonésimo"),
            (1_000_000UL, "millón", "millones", "millonésimo"),
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

            foreach ((ulong scale, string singular, string plural, _) in s_scales)
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
                    AppendBelowMillion(builder, part, apocope: true);
                    builder.Append(' ').Append(plural);
                }
            }

            if (rest > 0)
            {
                Separate(builder);
                AppendBelowMillion(builder, rest, apocope: false);
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

            StringBuilder builder = new StringBuilder();
            ulong rest = value;

            foreach ((ulong scale, _, _, string ordinal) in s_scales)
            {
                ulong part = rest / scale;

                if (part == 0)
                {
                    continue;
                }

                rest -= part * scale;
                Separate(builder);

                // The multiplier runs into the scale as one word, and a multiplier of one is not written:
                // millonésimo, dosmillonésimo.
                if (part > 1)
                {
                    StringBuilder multiplier = new StringBuilder();
                    AppendBelowMillion(multiplier, part, apocope: true);
                    builder.Append(multiplier.Replace(" ", string.Empty));
                }

                builder.Append(ordinal);
            }

            ulong thousands = rest / 1000;
            rest %= 1000;

            if (thousands > 0)
            {
                Separate(builder);

                if (thousands > 1)
                {
                    StringBuilder multiplier = new StringBuilder();
                    AppendBelowThousand(multiplier, thousands, apocope: true);
                    builder.Append(multiplier.Replace(" ", string.Empty));
                }

                builder.Append("milésimo");
            }

            if (rest > 0)
            {
                Separate(builder);
                AppendOrdinalBelowThousand(builder, rest);
            }

            string words = builder.ToString();
            return IsFeminine(variation, "a") ? Feminine(words) : words;
        }

        /// <inheritdoc/>
        public override string OrdinalSuffix(ulong value, string? variation)
        {
            return IsFeminine(variation, "a") ? "ª" : "º";
        }

        private static void Separate(StringBuilder builder)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }
        }

        private static void AppendBelowMillion(StringBuilder builder, ulong value, bool apocope)
        {
            ulong thousands = value / 1000;
            ulong rest = value % 1000;

            if (thousands > 0)
            {
                // Mil takes no article and no plural: mil, dos mil, veintiún mil.
                if (thousands > 1)
                {
                    AppendBelowThousand(builder, thousands, apocope: true);
                    builder.Append(' ');
                }

                builder.Append("mil");

                if (rest > 0)
                {
                    builder.Append(' ');
                }
            }

            if (rest > 0 || thousands == 0)
            {
                AppendBelowThousand(builder, rest, apocope);
            }
        }

        private static void AppendBelowThousand(StringBuilder builder, ulong value, bool apocope)
        {
            if (value == 100)
            {
                builder.Append("cien");
                return;
            }

            ulong hundreds = value / 100;
            ulong rest = value % 100;

            if (hundreds > 0)
            {
                builder.Append(s_hundreds[hundreds]);

                if (rest > 0)
                {
                    builder.Append(' ');
                }
            }

            if (rest > 0 || hundreds == 0)
            {
                AppendBelowHundred(builder, rest, apocope);
            }
        }

        private static void AppendBelowHundred(StringBuilder builder, ulong value, bool apocope)
        {
            if (value < 30)
            {
                // Before a noun the one loses its vowel: un millón, veintiún mil.
                builder.Append(apocope && value == 1 ? "un" : apocope && value == 21 ? "veintiún" : s_units[value]);
                return;
            }

            ulong unit = value % 10;
            builder.Append(s_tens[value / 10]);

            if (unit != 0)
            {
                builder.Append(" y ").Append(apocope && unit == 1 ? "un" : s_units[unit]);
            }
        }

        private static void AppendOrdinalBelowThousand(StringBuilder builder, ulong value)
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

            if (rest < 20)
            {
                builder.Append(s_ordinalUnits[rest]);
                return;
            }

            builder.Append(s_ordinalTens[rest / 10]);

            if (rest % 10 != 0)
            {
                builder.Append(' ').Append(s_ordinalUnits[rest % 10]);
            }
        }
    }
}
