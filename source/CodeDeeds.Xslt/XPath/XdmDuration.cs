using System.Globalization;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A duration: a number of months and a number of seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two components rather than one, because months and seconds cannot be reconciled. A month is between 28
    /// and 31 days, so <c>P1M</c> and <c>P30D</c> are neither equal nor orderable — which is why XPath 2.0
    /// gives the two halves types of their own, <c>xs:yearMonthDuration</c> and <c>xs:dayTimeDuration</c>, and
    /// why most of what you can do with a duration is only defined on one of them.
    /// </para>
    /// <para>
    /// The sign belongs to the duration as a whole, so both components always agree on it.
    /// </para>
    /// </remarks>
    public readonly struct XdmDuration : IEquatable<XdmDuration>
    {
        /// <summary>Initializes a duration.</summary>
        /// <param name="months">The number of months, negative for a negative duration.</param>
        /// <param name="seconds">The number of seconds, negative for a negative duration.</param>
        /// <param name="type">Which of the three duration types this is.</param>
        public XdmDuration(int months, decimal seconds, XdmTypeCode type)
        {
            Months = months;
            Seconds = seconds;
            Type = type;
        }

        /// <summary>Gets the months component.</summary>
        public int Months { get; }

        /// <summary>Gets the seconds component.</summary>
        public decimal Seconds { get; }

        /// <summary>Gets which duration type this is.</summary>
        public XdmTypeCode Type { get; }

        /// <summary>Gets whether the duration is negative.</summary>
        public bool IsNegative => Months < 0 || Seconds < 0m;

        /// <summary>
        /// The largest number of seconds a duration can be written as, and so the largest it can hold.
        /// </summary>
        /// <remarks>
        /// A day-time duration is spelt as a count of days and a time of day, and the count of days is a
        /// 64-bit integer here. More seconds than this has no spelling at all, which makes it out of range
        /// rather than merely large — <c>FODT0002</c>, the overflow the specification provides for, and what
        /// the suite's cbcl-divide-dayTimeDuration-003 allows in place of an answer.
        /// </remarks>
        public static readonly decimal MaxSeconds = long.MaxValue * 86400m;

        /// <summary>Whether a number of seconds is one a duration can hold.</summary>
        /// <param name="seconds">The number of seconds.</param>
        public static bool CanHold(decimal seconds) =>
            seconds >= -MaxSeconds && seconds <= MaxSeconds;

        /// <summary>How reading a duration turned out.</summary>
        public enum Reading : byte
        {
            /// <summary>A value was read.</summary>
            Value,

            /// <summary>The text is not in the type lexical space.</summary>
            NotLexical,

            /// <summary>The text names a duration too large for this engine to hold.</summary>
            Overflow,
        }

        /// <summary>Whether a run of text is digits and nothing else.</summary>
        private static bool AllDigits(ReadOnlySpan<char> text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Parses the lexical form of a duration.
        /// </summary>
        /// <remarks>
        /// The subtypes accept less than the general type: <c>xs:yearMonthDuration</c> takes no day or time
        /// components, and <c>xs:dayTimeDuration</c> takes no years or months. A value in the wrong half is
        /// rejected rather than silently truncated.
        /// </remarks>
        /// <param name="text">The text to parse.</param>
        /// <param name="type">Which duration type to expect.</param>
        /// <param name="result">On success, the duration.</param>
        /// <returns><see langword="true"/> if the text is in the type's lexical space.</returns>
        public static bool TryParse(string text, XdmTypeCode type, out XdmDuration result)
        {
            return Read(text, type, out result) == Reading.Value;
        }

        /// <summary>Reads a duration, saying which way it failed where it did.</summary>
        /// <remarks>
        /// A count of years or of days that no number here can hold is not a malformed duration: the text
        /// says what it means and this engine cannot hold it, which is the overflow the specification
        /// names rather than the lexical error it gives to text that says nothing.
        /// </remarks>
        /// <param name="text">The text to read.</param>
        /// <param name="type">Which of the three duration types is wanted.</param>
        /// <param name="result">The duration read, where one was.</param>
        public static Reading Read(string text, XdmTypeCode type, out XdmDuration result)
        {
            result = default;
            ReadOnlySpan<char> span = text.AsSpan().Trim();

            bool negative = span.Length > 0 && span[0] == '-';
            if (negative)
            {
                span = span[1..];
            }

            if (span.Length < 2 || span[0] != 'P')
            {
                return Reading.NotLexical;
            }

            span = span[1..];

            long years = 0, months = 0, days = 0, hours = 0, minutes = 0;
            decimal seconds = 0m;
            bool any = false;
            bool inTime = false;

            while (span.Length > 0)
            {
                if (span[0] == 'T')
                {
                    if (inTime || span.Length == 1)
                    {
                        return Reading.NotLexical;
                    }

                    inTime = true;
                    span = span[1..];
                    continue;
                }

                int digits = 0;
                while (digits < span.Length && (char.IsAsciiDigit(span[digits]) || span[digits] == '.'))
                {
                    digits++;
                }

                if (digits == 0 || digits == span.Length)
                {
                    return Reading.NotLexical;
                }

                ReadOnlySpan<char> number = span[..digits];
                char designator = span[digits];
                span = span[(digits + 1)..];
                any = true;

                // Only the seconds may be fractional, and only when it is the last component.
                if (designator == 'S' && inTime)
                {
                    if (!IsSecondsLexical(number)
                        || !decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds))
                    {
                        return Reading.NotLexical;
                    }

                    continue;
                }

                if (number.IndexOf('.') >= 0)
                {
                    return Reading.NotLexical;
                }

                if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
                {
                    // Digits and nothing else, and still too many of them: the text says what it means
                    // and no number here holds it, which is an overflow rather than a malformed duration.
                    return AllDigits(number) ? Reading.Overflow : Reading.NotLexical;
                }

                switch (designator)
                {
                    case 'Y' when !inTime:
                        years = value;
                        break;

                    case 'M' when !inTime:
                        months = value;
                        break;

                    case 'D' when !inTime:
                        days = value;
                        break;

                    case 'H' when inTime:
                        hours = value;
                        break;

                    case 'M' when inTime:
                        minutes = value;
                        break;

                    default:
                        return Reading.NotLexical;
                }
            }

            if (!any)
            {
                return Reading.NotLexical;
            }

            long totalMonths;

            try
            {
                totalMonths = checked((years * 12) + months);
            }
            catch (OverflowException)
            {
                return Reading.Overflow;
            }
            decimal totalSeconds = (days * 86400m) + (hours * 3600m) + (minutes * 60m) + seconds;

            if (type == XdmTypeCode.YearMonthDuration && totalSeconds != 0m)
            {
                return Reading.NotLexical;
            }

            if (type == XdmTypeCode.DayTimeDuration && totalMonths != 0)
            {
                return Reading.NotLexical;
            }

            if (totalMonths > int.MaxValue)
            {
                return Reading.Overflow;
            }

            result = negative
                ? new XdmDuration((int)-totalMonths, -totalSeconds, type)
                : new XdmDuration((int)totalMonths, totalSeconds, type);

            return Reading.Value;
        }

        /// <summary>
        /// Whether the seconds of a duration are written as XML Schema requires: <c>[0-9]+(\.[0-9]+)?</c>.
        /// </summary>
        /// <remarks>
        /// A digit is needed on each side of the point. <c>decimal.TryParse</c> accepts <c>.5</c> and
        /// <c>30.</c> and would let both through as durations that cannot be written back out in the form
        /// they came in.
        /// </remarks>
        private static bool IsSecondsLexical(ReadOnlySpan<char> number)
        {
            int point = number.IndexOf('.');

            if (point < 0)
            {
                return number.Length != 0;
            }

            // One point, with at least one digit on each side of it. The caller has already established that
            // every character is a digit or the point itself.
            return point > 0
                && point < number.Length - 1
                && number[(point + 1)..].IndexOf('.') < 0;
        }

        /// <summary>Writes the canonical lexical form.</summary>
        public override string ToString()
        {
            int months = Math.Abs(Months);
            decimal seconds = Math.Abs(Seconds);

            // Every component at its widest is under sixty characters, so this is written in one piece.
            CharStringBuilder builder = new CharStringBuilder(stackalloc char[64]);
            if (IsNegative)
            {
                builder.Append('-');
            }

            builder.Append('P');

            if (Type != XdmTypeCode.DayTimeDuration)
            {
                if (months / 12 != 0)
                {
                    builder.Append(months / 12);
                    builder.Append('Y');
                }

                if (months % 12 != 0)
                {
                    builder.Append(months % 12);
                    builder.Append('M');
                }
            }

            if (Type == XdmTypeCode.YearMonthDuration)
            {
                // A zero-length year-month duration still has to say something.
                return months == 0 ? (IsNegative ? "-P0M" : "P0M") : builder.ToString();
            }

            long days = (long)(seconds / 86400m);
            decimal rest = seconds - (days * 86400m);
            long hours = (long)(rest / 3600m);
            rest -= hours * 3600m;
            long minutes = (long)(rest / 60m);
            rest -= minutes * 60m;

            if (days != 0)
            {
                builder.Append(days);
                builder.Append('D');
            }

            if (hours != 0 || minutes != 0 || rest != 0m)
            {
                builder.Append('T');

                if (hours != 0)
                {
                    builder.Append(hours);
                    builder.Append('H');
                }

                if (minutes != 0)
                {
                    builder.Append(minutes);
                    builder.Append('M');
                }

                if (rest != 0m)
                {
                    builder.Append(WithoutTrailingZeros(rest));
                    builder.Append('S');
                }
            }

            if (builder.Length == (IsNegative ? 2 : 1))
            {
                // Nothing was written after the P, so this is a zero duration, and one of no length is
                // written in seconds whichever half it might have counted in: only xs:yearMonthDuration,
                // which counts in months and has left above, says P0M (XSD 1.0 §3.2.6.2).
                builder.Append("T0S");
            }

            return builder.ToString();
        }

        /// <summary>
        /// The same number with no trailing zeros in its fraction, the canonical form having none:
        /// <c>PT10.02S</c> halved is <c>PT5.01S</c> and not the <c>PT5.010S</c> that the scale the
        /// arithmetic arrived at would otherwise carry through.
        /// </summary>
        /// <param name="value">The number to shorten.</param>
        private static decimal WithoutTrailingZeros(decimal value)
        {
            // The scale is the fourth word of the representation, in bits 16 to 23.
            int scale = (decimal.GetBits(value)[3] >> 16) & 0xFF;

            while (scale > 0 && decimal.Round(value, scale - 1) == value)
            {
                value = decimal.Round(value, --scale);
            }

            return value;
        }

        /// <summary>
        /// Compares two durations, which is only defined when both are of the same half.
        /// </summary>
        /// <exception cref="XsltException">One duration is in months and the other in seconds.</exception>
        public int CompareTo(XdmDuration other)
        {
            if (Months != 0 && other.Seconds != 0m || Seconds != 0m && other.Months != 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"'{this}' and '{other}' cannot be ordered: a month is not a fixed number of days.");
            }

            return Months != 0 || other.Months != 0
                ? Months.CompareTo(other.Months)
                : Seconds.CompareTo(other.Seconds);
        }

        /// <inheritdoc/>
        public bool Equals(XdmDuration other) => Months == other.Months && Seconds == other.Seconds;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is XdmDuration other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Months, Seconds);
    }
}
