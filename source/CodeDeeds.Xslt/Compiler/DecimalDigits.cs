using System.Globalization;
using System.Numerics;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// A number written as its decimal digits, with the place of the point recorded separately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>format-number</c> shifts a number's point, rounds it at a place the picture chooses and pads it out,
    /// and is asked to do so to values reaching 10^308 and to integers of eighteen digits. In
    /// <see cref="double"/> that loses the digits it was told to print; in <see cref="decimal"/> it runs out of
    /// range at 7.9×10^28. Done on the digits themselves it is exact wherever the value came from, and the only
    /// question left is which digits those are — which is a question about the value's type, answered by the
    /// <c>Of</c> overloads below and nowhere else.
    /// </para>
    /// <para>
    /// The digits stand in space the caller lends, which for every value but a very wide integer is space
    /// on the caller's stack. A number is formatted once per element written, and held as strings it was
    /// four of them for each: the digits kept, the two halves either side of the point, and a half padded
    /// out. Read where they stand, none of those is made, and rounding changes the digits in place.
    /// </para>
    /// </remarks>
    internal readonly ref struct DecimalDigits
    {
        /// <summary>
        /// How much space holds the digits of any value but an integer too wide for 64 bits: a decimal's
        /// twenty-nine, a double's seventeen, with room to spare.
        /// </summary>
        public const int Room = 48;

        private readonly Span<char> m_digits;

        private DecimalDigits(Span<char> digits, int point)
        {
            m_digits = digits;
            Point = point;
        }

        /// <summary>The significant digits, carrying no leading or trailing zero. Empty when the value is zero.</summary>
        public ReadOnlySpan<char> Digits => m_digits;

        /// <summary>
        /// How many digits stand to the left of the point, so that the value is <c>0.Digits × 10^Point</c>.
        /// It may be negative, putting zeros between the point and the first digit, and it may run past the
        /// end of <see cref="Digits"/>, putting zeros between the last digit and the point.
        /// </summary>
        public int Point { get; }

        /// <summary>Gets whether the value is zero, which is the one value with no significant digits.</summary>
        public bool IsZero => m_digits.IsEmpty;

        /// <summary>The digits of an integer, which are exact however many there are.</summary>
        /// <param name="value">The integer.</param>
        /// <param name="space">Where the digits are to stand, at least <see cref="Room"/> long.</param>
        public static DecimalDigits Of(long value, Span<char> space)
        {
            Span<char> buffer = stackalloc char[24];

            // long.MinValue has no positive counterpart, so the text is negated rather than the number.
            value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture);
            ReadOnlySpan<char> text = buffer[..written];
            return Parse(text[0] == '-' ? text[1..] : text, space);
        }

        /// <summary>The digits of an integer too wide to hold in 64 bits, which are exact as well.</summary>
        /// <param name="value">The integer.</param>
        /// <param name="space">
        /// Where the digits are to stand if they fit. An integer of more digits than that is given an array
        /// of its own, being rare enough to be worth one.
        /// </param>
        public static DecimalDigits Of(BigInteger value, Span<char> space)
        {
            string text = BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture);
            return Parse(text, text.Length <= space.Length ? space : new char[text.Length]);
        }

        /// <summary>The digits of a decimal, whose own text is already exact.</summary>
        /// <param name="value">The decimal.</param>
        /// <param name="space">Where the digits are to stand, at least <see cref="Room"/> long.</param>
        public static DecimalDigits Of(decimal value, Span<char> space)
        {
            Span<char> buffer = stackalloc char[40];
            Math.Abs(value).TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture);
            return Parse(buffer[..written], space);
        }

        /// <summary>
        /// The digits of a double, which are the shortest that read back as the same value.
        /// </summary>
        /// <remarks>
        /// Not the exact binary value, which for 1e30 is 1000000000000000019884624838656 and is not what any
        /// specification asks to be printed. .NET writes the shortest round-tripping form by default, which is
        /// the same set of digits XPath's own double-to-string conversion uses.
        /// </remarks>
        /// <param name="value">The double.</param>
        /// <param name="space">Where the digits are to stand, at least <see cref="Room"/> long.</param>
        public static DecimalDigits Of(double value, Span<char> space)
        {
            Span<char> buffer = stackalloc char[40];
            Math.Abs(value).TryFormat(buffer, out int written, "R", CultureInfo.InvariantCulture);
            return Parse(buffer[..written], space);
        }

        /// <summary>
        /// Reads digits from a plain or exponential decimal numeral, which must not carry a sign.
        /// </summary>
        /// <param name="text">The numeral.</param>
        /// <param name="space">Where the digits are to stand, at least as long as the numeral.</param>
        public static DecimalDigits Parse(scoped ReadOnlySpan<char> text, Span<char> space)
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

            // Leading zeros are not significant but do move the point; trailing ones are neither. The
            // leading ones are dropped as they are met, and the trailing ones once all are in.
            int count = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (i == point)
                {
                    continue;
                }

                if (count == 0 && text[i] == '0')
                {
                    place--;
                    continue;
                }

                space[count++] = text[i];
            }

            while (count > 0 && space[count - 1] == '0')
            {
                count--;
            }

            return new DecimalDigits(space[..count], count == 0 ? 0 : place);
        }

        /// <summary>Multiplies by a power of ten, which only moves the point.</summary>
        public DecimalDigits Shift(int places)
        {
            return IsZero ? this : new DecimalDigits(m_digits, Point + places);
        }

        /// <summary>
        /// Rounds to a given number of digits after the point, carrying where the rounding overflows.
        /// </summary>
        /// <remarks>
        /// The digits are changed where they stand, so the value this was is not to be read afterwards.
        /// </remarks>
        /// <param name="fractionDigits">How many digits may stand after the point.</param>
        public DecimalDigits Round(int fractionDigits)
        {
            int keep = Point + fractionDigits;

            if (IsZero || keep >= m_digits.Length)
            {
                return this;
            }

            if (keep < 0)
            {
                // The whole value stands below half of the last place kept, so nothing survives and no carry
                // can reach up into it.
                return default;
            }

            bool up = m_digits[keep] >= '5';
            Span<char> kept = m_digits[..keep];
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

            if (up)
            {
                // Carrying off the left end lengthens the number: 99 rounded to one digit is 100, not 10.
                // Every digit kept was a nine and is now a zero, so the one carried is the only digit
                // left that signifies, and it stands where the digit rounded on stood, which is there to
                // be written over whether or not any digit was kept.
                m_digits[0] = '1';
                return new DecimalDigits(m_digits[..1], Point + 1);
            }

            int count = kept.Length;

            while (count > 0 && kept[count - 1] == '0')
            {
                count--;
            }

            return new DecimalDigits(kept[..count], count == 0 ? 0 : Point);
        }

        /// <summary>The digits standing before the point, with no padding and no leading zero.</summary>
        public DigitRun IntegerPart()
        {
            if (Point <= 0)
            {
                return default;
            }

            return Point >= m_digits.Length
                ? new DigitRun(0, m_digits, Point - m_digits.Length)
                : new DigitRun(0, m_digits[..Point], 0);
        }

        /// <summary>
        /// The digits standing after the point, carrying whatever leading zeros the point's place calls for
        /// and no trailing ones.
        /// </summary>
        public DigitRun FractionPart()
        {
            if (Point >= m_digits.Length)
            {
                return default;
            }

            return Point <= 0 ? new DigitRun(-Point, m_digits, 0) : new DigitRun(0, m_digits[Point..], 0);
        }
    }

    /// <summary>
    /// A run of digits read where they stand, with zeros understood before and after them.
    /// </summary>
    /// <remarks>
    /// What a number is padded out with is zeros and nothing else, so a padded run is its digits and two
    /// counts, and no string has to be made to hold the zeros.
    /// </remarks>
    internal readonly ref struct DigitRun
    {
        private readonly ReadOnlySpan<char> m_digits;
        private readonly int m_before;
        private readonly int m_after;

        /// <summary>Initializes a run.</summary>
        /// <param name="before">How many zeros stand before the digits.</param>
        /// <param name="digits">The digits.</param>
        /// <param name="after">How many zeros stand after them.</param>
        public DigitRun(int before, ReadOnlySpan<char> digits, int after)
        {
            m_before = before;
            m_digits = digits;
            m_after = after;
        }

        /// <summary>How many digits the run has, the zeros either side counted.</summary>
        public int Length => m_before + m_digits.Length + m_after;

        /// <summary>The digit at a place in the run, counted from its left.</summary>
        public char this[int index]
        {
            get
            {
                int within = index - m_before;
                return (uint)within < (uint)m_digits.Length ? m_digits[within] : '0';
            }
        }

        /// <summary>The run with zeros put before it until it is a given length.</summary>
        public DigitRun PadLeft(int length)
        {
            return Length >= length ? this : new DigitRun(m_before + length - Length, m_digits, m_after);
        }

        /// <summary>The run with zeros put after it until it is a given length.</summary>
        public DigitRun PadRight(int length)
        {
            return Length >= length ? this : new DigitRun(m_before, m_digits, m_after + length - Length);
        }
    }
}
