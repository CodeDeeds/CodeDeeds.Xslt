using System.Globalization;
using System.Numerics;
using System.Text;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// A parsed <c>format-number()</c> picture.
    /// </summary>
    /// <remarks>
    /// Two languages share this class, and the version decides which is read. XSLT 1.0 hands the job to Java's
    /// <c>DecimalFormat</c>, where only the zero-digit itself is a digit sign, a picture may say anything at
    /// all outside its digits, and one grouping interval repeats. From 2.0 the specification gives its own
    /// picture language: every member of the zero digit's family is a mandatory digit sign, the characters a
    /// picture may contain are constrained and a picture breaking those constraints is <c>FODF1310</c>, and
    /// 3.1 adds an exponent part. The differences are recorded in <c>ConformanceNotes.md</c>; they are
    /// deliberate, and <c>XslCompiledTransform</c> was asked about every picture that tells the two apart.
    /// <para>
    /// Pictures are parsed once when the stylesheet is compiled where the picture is a literal, which it almost
    /// always is.
    /// </para>
    /// </remarks>
    internal sealed class NumberPattern
    {
        private readonly SubPicture m_positive;
        private readonly SubPicture m_negative;
        private readonly bool m_legacy;

        private NumberPattern(SubPicture positive, SubPicture negative, bool legacy)
        {
            m_positive = positive;
            m_negative = negative;
            m_legacy = legacy;
        }

        /// <summary>
        /// Parses a picture against a set of symbols.
        /// </summary>
        /// <param name="picture">The picture text.</param>
        /// <param name="format">The symbols the picture is written with.</param>
        /// <param name="version">The version in force, which chooses between the two picture languages.</param>
        /// <param name="syntaxVersion">
        /// The version whose library defines the function, which decides whether the picture may carry an
        /// exponent: that part of the language arrived with XPath 3.1, and which language is read is a
        /// question about the processor rather than about what the stylesheet says of itself. Defaults to
        /// <paramref name="version"/>, which is right wherever the two are not being told apart.
        /// </param>
        /// <returns>The parsed picture.</returns>
        /// <exception cref="XsltException">The picture is not a usable one — <c>FODF1310</c>.</exception>
        public static NumberPattern Parse(
            string picture, DecimalFormat format, XsltVersion version, XsltVersion? syntaxVersion = null)
        {
            bool legacy = version.IsBackwardsCompatible;
            bool exponents = (syntaxVersion ?? version).CompareTo(XsltVersion.V30) >= 0;

            int[] code = ToCodePoints(picture);
            List<int[]> parts = Split(code, format.PatternSeparator);

            if (parts.Count > 2 && !legacy)
            {
                throw Invalid(picture, "a picture has at most two sub-pictures, one for each sign");
            }

            SubPicture positive = SubPicture.Analyse(parts[0], picture, format, legacy, exponents);

            // The negative sub-picture stands on its own: it supplies the digits, the grouping and the
            // rounding as well as the text around them, and no minus sign is added to what it says. Only
            // where there is no second sub-picture does the minus sign appear, in front of the first's prefix.
            SubPicture negative = parts.Count > 1
                ? SubPicture.Analyse(parts[1], picture, format, legacy, exponents)
                : positive.WithMinusSign(format);

            return new NumberPattern(positive, negative, legacy);
        }

        /// <summary>
        /// Formats a number through this picture.
        /// </summary>
        /// <param name="value">The value to format, whose type decides which digits it has.</param>
        /// <param name="format">The symbols to write the result with.</param>
        public string Format(XPathValue value, DecimalFormat format)
        {
            double number = value.ToNumber();

            if (double.IsNaN(number))
            {
                // NaN carries no sign, so it never picks up the negative sub-picture.
                return format.NaN;
            }

            bool negative = number < 0 || (number == 0 && double.IsNegative(number));
            SubPicture picture = negative ? m_negative : m_positive;

            // A percent sign scales the number before anything else looks at it, and for a double that is
            // double arithmetic: a percentage of 1e308 overflows to infinity, and is reported as one.
            double scaled = Math.Abs(number) * Power(picture.Multiplier);

            if (double.IsInfinity(scaled))
            {
                return picture.Prefix + format.Infinity + picture.Suffix;
            }

            return picture.Render(Digitize(value, scaled, picture.Multiplier, m_legacy), format);
        }

        private static double Power(int tens)
        {
            return tens switch { 2 => 100.0, 3 => 1000.0, _ => 1.0 };
        }

        /// <summary>
        /// Chooses the digits a value is to be printed from.
        /// </summary>
        /// <remarks>
        /// XSLT 1.0 has one numeric type and reaches this through a <see cref="decimal"/>, which carries
        /// fifteen significant digits and is what <c>XslCompiledTransform</c> agrees with. From 2.0 the value
        /// has a type, and an <c>xs:integer</c> of eighteen digits or an <c>xs:decimal</c> of twenty-eight is
        /// printed as written rather than as the nearest double — including when a percent sign has scaled it,
        /// which for those two is moving the point rather than arithmetic.
        /// </remarks>
        private static DecimalDigits Digitize(XPathValue value, double scaled, int multiplier, bool legacy)
        {
            if (legacy)
            {
                return scaled < 7.9e28 ? DecimalDigits.Of((decimal)scaled) : DecimalDigits.Of(scaled);
            }

            return value.TypeCode switch
            {
                XdmTypeCode.Integer => (value.IsWideInteger
                    ? DecimalDigits.Of(value.ToBigInteger())
                    : DecimalDigits.Of(value.ToInteger())).Shift(multiplier),
                XdmTypeCode.Decimal => DecimalDigits.Of(value.ToDecimal()).Shift(multiplier),
                _ => DecimalDigits.Of(scaled),
            };
        }

        private static int[] ToCodePoints(string text)
        {
            List<int> code = new List<int>(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    code.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                    i++;
                }
                else
                {
                    code.Add(text[i]);
                }
            }

            return code.ToArray();
        }

        private static List<int[]> Split(int[] code, int separator)
        {
            List<int[]> parts = new List<int[]>();
            int start = 0;

            for (int i = 0; i < code.Length; i++)
            {
                if (code[i] == separator)
                {
                    parts.Add(code[start..i]);
                    start = i + 1;
                }
            }

            parts.Add(code[start..]);
            return parts;
        }

        private static XsltException Invalid(string picture, string why)
        {
            return XsltErrors.Error(
                XsltErrorCode.FODF1310, $"'{picture}' is not a format-number picture, because {why}.");
        }

        /// <summary>
        /// Appends a code point, which may be a pair of chars.
        /// </summary>
        private static void AppendCodePoint(StringBuilder builder, int code)
        {
            if (code > 0xFFFF)
            {
                builder.Append(char.ConvertFromUtf32(code));
            }
            else
            {
                builder.Append((char)code);
            }
        }

        /// <summary>
        /// One sign's worth of a picture: the text around the number, how many digits stand where, and where
        /// the separators go.
        /// </summary>
        private sealed class SubPicture
        {
            private string m_prefix = string.Empty;
            private string m_suffix = string.Empty;
            private int m_minimumIntegerDigits;
            private int m_maximumIntegerDigits;
            private int m_minimumFractionDigits;
            private int m_maximumFractionDigits;

            /// <summary>
            /// The interval at which the integer separator repeats leftwards, or zero where the picture's
            /// separators are irregular and stand only where they were written.
            /// </summary>
            private int m_groupingInterval;

            /// <summary>Where the integer separators stand, counted as digits to the right of each.</summary>
            private int[] m_integerGrouping = Array.Empty<int>();

            /// <summary>
            /// Where the fraction separators stand, counted as digits to the left of each. These never repeat:
            /// a fraction is read outwards from the point, so there is no leftward habit to carry on.
            /// </summary>
            private int[] m_fractionGrouping = Array.Empty<int>();

            /// <summary>How many places to move the point, from a percent or per-mille sign.</summary>
            private int m_multiplier;

            /// <summary>The exponent's least width, or zero where the picture has no exponent part.</summary>
            private int m_exponentDigits;

            private bool m_hasExponent;

            /// <summary>
            /// The zero of the ten digits this picture is written in, which are also the ten it writes.
            /// </summary>
            private int m_family = '0';

            /// <summary>How many of the ten count as digit signs: ten from 2.0, and one at 1.0.</summary>
            private int m_span = 1;

            public string Prefix => m_prefix;

            public string Suffix => m_suffix;

            public int Multiplier => m_multiplier;

            /// <summary>
            /// A copy that writes the minus sign in front, which is what a picture with no negative
            /// sub-picture does with a negative number.
            /// </summary>
            public SubPicture WithMinusSign(DecimalFormat format)
            {
                StringBuilder builder = new StringBuilder();
                AppendCodePoint(builder, format.MinusSign);
                builder.Append(m_prefix);

                return new SubPicture
                {
                    m_prefix = builder.ToString(),
                    m_suffix = m_suffix,
                    m_minimumIntegerDigits = m_minimumIntegerDigits,
                    m_maximumIntegerDigits = m_maximumIntegerDigits,
                    m_minimumFractionDigits = m_minimumFractionDigits,
                    m_maximumFractionDigits = m_maximumFractionDigits,
                    m_groupingInterval = m_groupingInterval,
                    m_integerGrouping = m_integerGrouping,
                    m_fractionGrouping = m_fractionGrouping,
                    m_multiplier = m_multiplier,
                    m_exponentDigits = m_exponentDigits,
                    m_hasExponent = m_hasExponent,
                    m_family = m_family,
                    m_span = m_span,
                };
            }

            /// <summary>
            /// Reads one sub-picture, raising <c>FODF1310</c> where the specification's constraints are
            /// broken. At 1.0 none of them are checked, that language having none.
            /// </summary>
            public static SubPicture Analyse(
                int[] code, string picture, DecimalFormat format, bool legacy, bool exponents)
            {
                SubPicture result = new SubPicture
                {
                    m_family = legacy ? format.ZeroDigit : FamilyOf(code, format.ZeroDigit),
                    m_span = legacy ? 1 : 10,
                };

                int exponentAt = exponents ? result.ExponentSeparatorAt(code, format) : -1;
                int exponentEnd = exponentAt < 0 ? -1 : result.LastDigitAfter(code, exponentAt);

                int first = -1;
                int last = -1;

                for (int i = 0; i < code.Length; i++)
                {
                    if (result.IsMantissaCharacter(code[i], format))
                    {
                        first = first < 0 ? i : first;
                        last = i;
                    }
                }

                if (first < 0 || (!legacy && !result.HasDigitSign(code, format)))
                {
                    // A decimal separator on its own does not make a number: 'fred.ginger' and '.e99' are
                    // both pictures with nowhere to put a digit.
                    throw Invalid(picture, "it has no digit and no optional-digit sign");
                }

                int mantissaEnd = exponentAt < 0 ? last + 1 : exponentAt;
                last = exponentAt < 0 ? last : exponentEnd;

                result.m_prefix = Text(code, 0, first);
                result.m_suffix = Text(code, last + 1, code.Length);
                result.m_hasExponent = exponentAt >= 0;
                result.m_exponentDigits = exponentAt < 0 ? 0 : exponentEnd - exponentAt;
                result.ReadMantissa(code, first, mantissaEnd, picture, format, legacy);

                if (!legacy)
                {
                    // Only the number may stand between the first digit and the last: text belongs on the
                    // outside of it, which is what makes '9.9999E999' an error where '9.9999eDog' is a
                    // number with 'eDog' written after it.
                    for (int i = first; i <= last; i++)
                    {
                        bool active = result.IsMantissaCharacter(code[i], format)
                            || (exponentAt >= 0 && i >= exponentAt);

                        if (!active)
                        {
                            throw Invalid(
                                picture,
                                "everything between its first digit and its last belongs to the number, and "
                                    + $"'{char.ConvertFromUtf32(code[i])}' does not");
                        }
                    }

                    for (int i = last + 1; i < code.Length; i++)
                    {
                        if (result.IsMantissaCharacter(code[i], format))
                        {
                            throw Invalid(picture, "the exponent's digits are the last of its number");
                        }
                    }
                }

                result.ReadMultiplier(code, picture, format, legacy);
                result.Settle(legacy);
                return result;
            }

            /// <summary>
            /// Writes a value out through this sub-picture.
            /// </summary>
            public string Render(DecimalDigits digits, DecimalFormat format)
            {
                int exponent = 0;

                if (m_hasExponent && !digits.IsZero)
                {
                    // The mantissa is scaled to show exactly as many integer digits as the picture insists
                    // on. Where it insists on none it lands between a tenth and one, and rounding is free to
                    // carry it back over one without the exponent being reconsidered.
                    exponent = digits.Point - m_minimumIntegerDigits;
                    digits = digits.Shift(-exponent);
                }

                digits = digits.Round(m_maximumFractionDigits);

                string integer = digits.IntegerPart();
                string fraction = digits.FractionPart();

                if (fraction.Length < m_minimumFractionDigits)
                {
                    fraction = fraction.PadRight(m_minimumFractionDigits, '0');
                }

                // An exponential form always shows an integer digit where the picture has an integer part to
                // show it in, so '#.#e0' writes 0.2e0 where '.#e0' writes .2e0.
                int leastInteger = m_hasExponent && m_maximumIntegerDigits > 0
                    ? Math.Max(m_minimumIntegerDigits, 1)
                    : m_minimumIntegerDigits;

                if (integer.Length < leastInteger)
                {
                    integer = integer.PadLeft(leastInteger, '0');
                }

                // One builder per thread, reused: a number is formatted once per element written, and the
                // builder and its first chunk were two allocations per call for a string a few characters long.
                StringBuilder builder = t_render ??= new StringBuilder(64);
                builder.Clear();
                builder.Append(m_prefix);
                AppendInteger(builder, integer, format);

                if (fraction.Length > 0)
                {
                    AppendCodePoint(builder, format.DecimalSeparator);
                    AppendFraction(builder, fraction, format);
                }

                if (m_hasExponent)
                {
                    AppendCodePoint(builder, format.ExponentSeparator);

                    if (exponent < 0)
                    {
                        AppendCodePoint(builder, format.MinusSign);
                    }

                    string magnitude = Math.Abs(exponent).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    AppendDigits(builder, magnitude.PadLeft(m_exponentDigits, '0'));
                }

                builder.Append(m_suffix);
                return builder.ToString();
            }

            [ThreadStatic]
            private static StringBuilder? t_render;

            private void ReadMantissa(
                int[] code, int from, int to, string picture, DecimalFormat format, bool legacy)
            {
                List<int> integerGrouping = new List<int>();
                List<int> fractionGrouping = new List<int>();
                int integerDigits = 0;
                int fractionDigits = 0;
                bool inFraction = false;
                bool mandatorySeen = false;
                bool optionalSeen = false;

                for (int i = from; i < to; i++)
                {
                    int c = code[i];

                    if (c == format.GroupingSeparator)
                    {
                        if (!legacy)
                        {
                            bool neighbouring = i + 1 == to
                                || code[i + 1] == format.GroupingSeparator
                                || code[i + 1] == format.DecimalSeparator
                                || (i > from && code[i - 1] == format.DecimalSeparator);

                            if (neighbouring)
                            {
                                throw Invalid(
                                    picture,
                                    "a grouping separator stands between digits, so it may not end the "
                                        + "number or sit beside the decimal separator or another of itself");
                            }
                        }

                        if (inFraction)
                        {
                            fractionGrouping.Add(fractionDigits);
                        }
                        else
                        {
                            integerGrouping.Add(integerDigits);
                        }
                    }
                    else if (c == format.DecimalSeparator)
                    {
                        if (inFraction && !legacy)
                        {
                            throw Invalid(picture, "it has more than one decimal separator");
                        }

                        inFraction = true;
                        mandatorySeen = false;
                        optionalSeen = false;
                    }
                    else if (IsDigit(c))
                    {
                        if (inFraction)
                        {
                            if (optionalSeen && !legacy)
                            {
                                throw Invalid(
                                    picture,
                                    "a digit that has to appear may not follow one that need not, after the "
                                        + "decimal separator");
                            }

                            m_minimumFractionDigits++;
                            fractionDigits++;
                        }
                        else
                        {
                            m_minimumIntegerDigits++;
                            integerDigits++;
                        }

                        mandatorySeen = true;
                    }
                    else if (c == format.Digit)
                    {
                        if (!inFraction && mandatorySeen && !legacy)
                        {
                            throw Invalid(
                                picture,
                                "a digit that need not appear may not follow one that has to, before the "
                                    + "decimal separator");
                        }

                        if (inFraction)
                        {
                            fractionDigits++;
                        }
                        else
                        {
                            integerDigits++;
                        }

                        optionalSeen = true;
                    }
                }

                m_maximumIntegerDigits = integerDigits;
                m_maximumFractionDigits = fractionDigits;

                // Recorded left to right as digits seen so far; wanted as digits standing to the right, which
                // is the only way a position means the same thing for a number longer than the picture.
                for (int i = 0; i < integerGrouping.Count; i++)
                {
                    integerGrouping[i] = integerDigits - integerGrouping[i];
                }

                if (legacy)
                {
                    // Java's DecimalFormat lets the rightmost separator set one interval that then repeats,
                    // ignores any others outright, and groups nothing after the decimal separator.
                    m_groupingInterval = integerGrouping.Count > 0 ? integerGrouping[^1] : 0;
                }
                else
                {
                    m_integerGrouping = integerGrouping.ToArray();
                    m_groupingInterval = DigitGrouping.IntervalOf(integerGrouping, integerDigits);
                    m_fractionGrouping = fractionGrouping.ToArray();
                }
            }

            private void ReadMultiplier(int[] code, string picture, DecimalFormat format, bool legacy)
            {
                int percent = 0;
                int perMille = 0;

                foreach (int c in code)
                {
                    if (c == format.Percent)
                    {
                        percent++;
                    }
                    else if (c == format.PerMille)
                    {
                        perMille++;
                    }
                }

                if (!legacy && (percent + perMille > 1))
                {
                    throw Invalid(picture, "it may scale a number once, by a percent sign or a per-mille sign");
                }

                if (!legacy && m_hasExponent && percent + perMille > 0)
                {
                    throw Invalid(picture, "a number written with an exponent is not also scaled");
                }

                m_multiplier = percent > 0 ? 2 : perMille > 0 ? 3 : 0;
            }

            /// <summary>
            /// Settles the digit counts a picture leaves open, which is where a picture asking for no digit at
            /// all is given one.
            /// </summary>
            private void Settle(bool legacy)
            {
                if (legacy || m_minimumIntegerDigits > 0)
                {
                    return;
                }

                if (m_maximumFractionDigits == 0)
                {
                    // The picture asks for no digit at all, so it is given one. Which side of the point it
                    // lands on turns on the exponent: without one there is nowhere to put a fraction, so the
                    // integer place is made mandatory; with one the mantissa is scaled below one and needs a
                    // fraction digit or it would say nothing.
                    if (m_hasExponent)
                    {
                        m_minimumFractionDigits = 1;
                        m_maximumFractionDigits = 1;
                    }
                    else
                    {
                        m_minimumIntegerDigits = 1;
                    }
                }
                else if (m_minimumFractionDigits == 0 && (!m_hasExponent || m_maximumIntegerDigits == 0))
                {
                    // Nothing has to appear on either side of the point, so the first fraction place is made
                    // to: format-number(0, '#.#') is '.0'. An exponential picture with an integer part is left
                    // alone, because its integer digit is written whether the value has one or not.
                    m_minimumFractionDigits = 1;
                }
            }

            private void AppendInteger(StringBuilder builder, string integer, DecimalFormat format)
            {
                if (m_groupingInterval <= 0 && m_integerGrouping.Length == 0)
                {
                    AppendDigits(builder, integer);
                    return;
                }

                for (int i = 0; i < integer.Length; i++)
                {
                    int remaining = integer.Length - i;

                    // A regular picture repeats its separator leftwards; an irregular one puts separators only
                    // where it wrote them, and one that would land in front of the number is dropped.
                    bool separated = m_groupingInterval > 0
                        ? remaining % m_groupingInterval == 0
                        : DigitGrouping.Contains(m_integerGrouping, remaining);

                    if (i > 0 && separated)
                    {
                        AppendCodePoint(builder, format.GroupingSeparator);
                    }

                    AppendCodePoint(builder, Translate(integer[i]));
                }
            }

            private void AppendFraction(StringBuilder builder, string fraction, DecimalFormat format)
            {
                for (int i = 0; i < fraction.Length; i++)
                {
                    if (i > 0 && DigitGrouping.Contains(m_fractionGrouping, i))
                    {
                        AppendCodePoint(builder, format.GroupingSeparator);
                    }

                    AppendCodePoint(builder, Translate(fraction[i]));
                }
            }

            private void AppendDigits(StringBuilder builder, string digits)
            {
                foreach (char c in digits)
                {
                    AppendCodePoint(builder, Translate(c));
                }
            }

            private int Translate(char digit)
            {
                return m_family + (digit - '0');
            }

            /// <summary>
            /// The zero of the digit family a picture is written in.
            /// </summary>
            /// <remarks>
            /// Taken from the picture rather than from <c>zero-digit</c>, because a picture may be written in
            /// Osmanya digits with the property left at its default and the suite expects Osmanya out. The
            /// property still names the family for a picture that writes no digit of its own, which is any
            /// picture made only of optional-digit signs.
            /// </remarks>
            private static int FamilyOf(int[] code, int zeroDigit)
            {
                foreach (int c in code)
                {
                    int digit = DigitValueOf(c);

                    if (digit >= 0)
                    {
                        return c - digit;
                    }
                }

                return zeroDigit;
            }

            private static int DigitValueOf(int code)
            {
                return code <= 0xFFFF
                    ? CharUnicodeInfo.GetDecimalDigitValue((char)code)
                    : CharUnicodeInfo.GetDecimalDigitValue(char.ConvertFromUtf32(code), 0);
            }

            /// <summary>
            /// Where the exponent separator stands, or -1 where the picture has no exponent part.
            /// </summary>
            /// <remarks>
            /// The separator is only itself where a mantissa precedes it and digits follow it; elsewhere the
            /// character is ordinary text, which is what lets '9.9999eDog' end in a passive 'eDog'.
            /// </remarks>
            private int ExponentSeparatorAt(int[] code, DecimalFormat format)
            {
                bool mantissa = false;

                for (int i = 0; i < code.Length; i++)
                {
                    if (code[i] == format.ExponentSeparator
                        && mantissa
                        && i + 1 < code.Length
                        && IsDigit(code[i + 1]))
                    {
                        return i;
                    }

                    mantissa |= IsDigit(code[i]) || code[i] == format.Digit;
                }

                return -1;
            }

            private int LastDigitAfter(int[] code, int separator)
            {
                int at = separator + 1;
                while (at < code.Length && IsDigit(code[at]))
                {
                    at++;
                }

                return at - 1;
            }

            /// <summary>
            /// Whether a character marks out where the number itself begins and ends. A percent sign is active
            /// but is not one of these: it stands in the text around the number, not among its digits.
            /// </summary>
            private bool IsMantissaCharacter(int c, DecimalFormat format)
            {
                return IsDigit(c)
                    || c == format.Digit
                    || c == format.DecimalSeparator
                    || c == format.GroupingSeparator;
            }

            /// <summary>Whether the picture has anywhere at all to put a digit.</summary>
            private bool HasDigitSign(int[] code, DecimalFormat format)
            {
                foreach (int c in code)
                {
                    if (IsDigit(c) || c == format.Digit)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Whether a character is a digit. From 2.0 that means any of the ten in the family, so a picture
            /// may be written '9,999.99'; at 1.0 only the zero digit itself counts and a '9' is ordinary text,
            /// which is what <c>XslCompiledTransform</c> does with it.
            /// </summary>
            private bool IsDigit(int c)
            {
                return (uint)(c - m_family) < (uint)m_span;
            }

            private static string Text(int[] code, int from, int to)
            {
                StringBuilder builder = new StringBuilder();

                for (int i = from; i < to; i++)
                {
                    AppendCodePoint(builder, code[i]);
                }

                return builder.ToString();
            }
        }
    }
}
