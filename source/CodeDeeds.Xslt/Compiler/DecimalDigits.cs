using System.Globalization;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// A number written as its decimal digits, with the place of the point recorded separately.
    /// </summary>
    /// <remarks>
    /// <c>format-number</c> shifts a number's point, rounds it at a place the picture chooses and pads it out,
    /// and is asked to do so to values reaching 10^308 and to integers of eighteen digits. In
    /// <see cref="double"/> that loses the digits it was told to print; in <see cref="decimal"/> it runs out of
    /// range at 7.9×10^28. Done on the digits themselves it is exact wherever the value came from, and the only
    /// question left is which digits those are — which is a question about the value's type, answered by the
    /// <c>Of</c> overloads below and nowhere else.
    /// </remarks>
    internal readonly struct DecimalDigits
    {
        private DecimalDigits(string digits, int point)
        {
            Digits = digits;
            Point = point;
        }

        /// <summary>The significant digits, carrying no leading or trailing zero. Empty when the value is zero.</summary>
        public string Digits { get; }

        /// <summary>
        /// How many digits stand to the left of the point, so that the value is <c>0.Digits × 10^Point</c>.
        /// It may be negative, putting zeros between the point and the first digit, and it may run past the
        /// end of <see cref="Digits"/>, putting zeros between the last digit and the point.
        /// </summary>
        public int Point { get; }

        /// <summary>Gets whether the value is zero, which is the one value with no significant digits.</summary>
        public bool IsZero => Digits.Length == 0;

        // Formatted into stack space and read from there: a number is formatted once per element written,
        // and the text, its halves either side of the point and its trimmed digits were four strings per
        // number where one — the digits kept — is all that is needed.

        /// <summary>The digits of an integer, which are exact however many there are.</summary>
        public static DecimalDigits Of(long value)
        {
            Span<char> buffer = stackalloc char[24];

            // long.MinValue has no positive counterpart, so the text is negated rather than the number.
            value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture);
            ReadOnlySpan<char> text = buffer[..written];
            return Parse(text[0] == '-' ? text[1..] : text);
        }

        /// <summary>The digits of a decimal, whose own text is already exact.</summary>
        public static DecimalDigits Of(decimal value)
        {
            Span<char> buffer = stackalloc char[40];
            Math.Abs(value).TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture);
            return Parse(buffer[..written]);
        }

        /// <summary>
        /// The digits of a double, which are the shortest that read back as the same value.
        /// </summary>
        /// <remarks>
        /// Not the exact binary value, which for 1e30 is 1000000000000000019884624838656 and is not what any
        /// specification asks to be printed. .NET writes the shortest round-tripping form by default, which is
        /// the same set of digits XPath's own double-to-string conversion uses.
        /// </remarks>
        public static DecimalDigits Of(double value)
        {
            Span<char> buffer = stackalloc char[40];
            Math.Abs(value).TryFormat(buffer, out int written, "R", CultureInfo.InvariantCulture);
            return Parse(buffer[..written]);
        }

        /// <summary>
        /// Reads digits from a plain or exponential decimal numeral, which must not carry a sign.
        /// </summary>
        public static DecimalDigits Parse(string text) => Parse(text.AsSpan());

        /// <inheritdoc cref="Parse(string)"/>
        public static DecimalDigits Parse(ReadOnlySpan<char> text)
        {
            int exponent = 0;
            int e = text.IndexOfAny('e', 'E');

            if (e >= 0)
            {
                exponent = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                text = text[..e];
            }

            int point = text.IndexOf('.');
            int place = (point < 0 ? text.Length : point) + exponent;

            Span<char> digits = text.Length <= 64 ? stackalloc char[64] : new char[text.Length];
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (i != point)
                {
                    digits[count++] = text[i];
                }
            }

            // Leading zeros are not significant but do move the point; trailing ones are neither.
            int first = 0;
            while (first < count && digits[first] == '0')
            {
                first++;
                place--;
            }

            int last = count;
            while (last > first && digits[last - 1] == '0')
            {
                last--;
            }

            string kept = last == first ? string.Empty : new string(digits[first..last]);
            return new DecimalDigits(kept, kept.Length == 0 ? 0 : place);
        }

        /// <summary>Multiplies by a power of ten, which only moves the point.</summary>
        public DecimalDigits Shift(int places)
        {
            return IsZero ? this : new DecimalDigits(Digits, Point + places);
        }

        /// <summary>
        /// Rounds to a given number of digits after the point, carrying where the rounding overflows.
        /// </summary>
        /// <param name="fractionDigits">How many digits may stand after the point.</param>
        public DecimalDigits Round(int fractionDigits)
        {
            int keep = Point + fractionDigits;

            if (IsZero || keep >= Digits.Length)
            {
                return this;
            }

            if (keep < 0)
            {
                // The whole value stands below half of the last place kept, so nothing survives and no carry
                // can reach up into it.
                return default;
            }

            bool up = Digits[keep] >= '5';
            char[] kept = Digits[..keep].ToCharArray();
            int at = kept.Length - 1;

            while (up && at >= 0)
            {
                if (kept[at] == '9')
                {
                    kept[at] = '0';
                    at--;
                }
                else
                {
                    kept[at]++;
                    up = false;
                }
            }

            // Carrying off the left end lengthens the number: 99 rounded to one digit is 100, not 10.
            string digits = up ? "1" + new string(kept) : new string(kept);
            int point = up ? Point + 1 : Point;

            digits = digits.TrimEnd('0');
            return new DecimalDigits(digits, digits.Length == 0 ? 0 : point);
        }

        /// <summary>The digits standing before the point, with no padding and no leading zero.</summary>
        public string IntegerPart()
        {
            if (Point <= 0)
            {
                return string.Empty;
            }

            return Point >= Digits.Length ? Digits.PadRight(Point, '0') : Digits[..Point];
        }

        /// <summary>
        /// The digits standing after the point, carrying whatever leading zeros the point's place calls for
        /// and no trailing ones.
        /// </summary>
        public string FractionPart()
        {
            if (Point >= Digits.Length)
            {
                return string.Empty;
            }

            return Point <= 0 ? new string('0', -Point) + Digits : Digits[Point..];
        }
    }
}
