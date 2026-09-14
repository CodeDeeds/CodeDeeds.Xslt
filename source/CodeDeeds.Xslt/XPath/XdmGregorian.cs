using System.Globalization;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A value of one of the five gregorian types: <c>xs:gYear</c>, <c>xs:gYearMonth</c>, <c>xs:gMonth</c>,
    /// <c>xs:gMonthDay</c> and <c>xs:gDay</c>.
    /// </summary>
    /// <remarks>
    /// Each names a recurring or partial piece of the calendar rather than a point in it: <c>xs:gMonth</c> is
    /// August in every year, and <c>xs:gDay</c> the 23rd of every month. They are one type here because they
    /// differ only in which components they carry and how those are written — and because none of them can be
    /// turned into a <see cref="DateTime"/> without inventing the parts it does not have.
    /// </remarks>
    public sealed class XdmGregorian
    {
        /// <summary>Initializes a gregorian value.</summary>
        /// <param name="name">The type's local name, such as <c>gYearMonth</c>.</param>
        /// <param name="year">The year, where the type carries one.</param>
        /// <param name="month">The month, where the type carries one.</param>
        /// <param name="day">The day, where the type carries one.</param>
        /// <param name="offset">The timezone, if the value carries one.</param>
        public XdmGregorian(string name, int? year, int? month, int? day, TimeSpan? offset)
        {
            Name = name;
            Year = year;
            Month = month;
            Day = day;
            Offset = offset;
        }

        /// <summary>The type's local name.</summary>
        public string Name { get; }

        /// <summary>The year, where this type has one.</summary>
        public int? Year { get; }

        /// <summary>The month, where this type has one.</summary>
        public int? Month { get; }

        /// <summary>The day, where this type has one.</summary>
        public int? Day { get; }

        /// <summary>The timezone, if the value was written with one.</summary>
        public TimeSpan? Offset { get; }

        /// <summary>How reading a Gregorian value turned out.</summary>
        public enum Reading : byte
        {
            /// <summary>A value was read.</summary>
            Value,

            /// <summary>The text is not in the type lexical space.</summary>
            NotLexical,

            /// <summary>The text names a year outside the range this engine holds.</summary>
            OutOfRange,
        }

        /// <summary>Whether the last read refused a year for its size rather than its shape.</summary>
        [ThreadStatic]
        private static bool s_yearOutOfRange;

        /// <summary>Reads a Gregorian value, saying which way it failed where it did.</summary>
        /// <param name="text">The text to read.</param>
        /// <param name="name">The type name, which says how many parts to expect.</param>
        /// <param name="result">The value read, where one was.</param>
        public static Reading Read(string text, string name, out XdmGregorian? result)
        {
            s_yearOutOfRange = false;

            if (TryParse(text, name, out result))
            {
                return Reading.Value;
            }

            // A year of nothing but digits that no number here holds is not a malformed year: the text
            // says which year it means and this engine cannot hold it. Only where the rest of the text
            // reads, though: "999999999999-XX" has a month that is no month, and a text that is not in
            // the lexical space at all is better complained about as such.
            bool yearTooBig = s_yearOutOfRange;

            return yearTooBig && TryParse(WithYear(text, "9999"), name, out _)
                ? Reading.OutOfRange
                : Reading.NotLexical;
        }

        /// <summary>The same text with the year digits swapped for others, to ask about the rest of it.</summary>
        private static string WithYear(string text, string year)
        {
            int start = text.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
            int end = start;

            while (end < text.Length && text[end] >= '0' && text[end] <= '9')
            {
                end++;
            }

            return string.Concat(text.AsSpan(0, start), year, text.AsSpan(end));
        }

        /// <summary>Reads the lexical form of one of the five types.</summary>
        /// <param name="text">The text to read.</param>
        /// <param name="name">Which type's lexical form to expect.</param>
        /// <param name="result">On success, the value.</param>
        public static bool TryParse(string text, string name, out XdmGregorian? result)
        {
            result = null;
            string body = text.Trim();
            TimeSpan? offset = null;

            if (!TrySplitTimezone(ref body, out offset))
            {
                return false;
            }

            switch (name)
            {
                case "gYear":
                    // A year may be negative and must be at least four digits, which is what stops 26 from
                    // standing for 2026.
                    return TryYear(body, out int year)
                        && Build(name, year, null, null, offset, out result);

                case "gYearMonth":
                {
                    int dash = body.LastIndexOf('-');
                    return dash > 0
                        && TryYear(body[..dash], out int yearOfMonth)
                        && TryPart(body[(dash + 1)..], 2, 1, 12, out int month)
                        && Build(name, yearOfMonth, month, null, offset, out result);
                }

                case "gMonth":
                    return body.StartsWith("--", StringComparison.Ordinal)
                        && TryPart(body[2..], 2, 1, 12, out int onlyMonth)
                        && Build(name, null, onlyMonth, null, offset, out result);

                case "gMonthDay":
                {
                    // The day has to exist in that month. There is no year to ask about, so February takes
                    // 29 — the type names a day of every year, and every fourth one has that day in it.
                    return body.StartsWith("--", StringComparison.Ordinal)
                        && body.Length == 7
                        && body[4] == '-'
                        && TryPart(body[2..4], 2, 1, 12, out int monthOfDay)
                        && TryPart(body[5..], 2, 1, DaysIn(monthOfDay), out int dayOfMonth)
                        && Build(name, null, monthOfDay, dayOfMonth, offset, out result);
                }

                default:
                    return body.StartsWith("---", StringComparison.Ordinal)
                        && TryPart(body[3..], 2, 1, 31, out int onlyDay)
                        && Build(name, null, null, onlyDay, offset, out result);
            }
        }

        /// <summary>Returns the value in its type's lexical form.</summary>
        public override string ToString()
        {
            string body = Name switch
            {
                "gYear" => FormatYear(),
                "gYearMonth" => $"{FormatYear()}-{Month:00}",
                "gMonth" => $"--{Month:00}",
                "gMonthDay" => $"--{Month:00}-{Day:00}",
                _ => $"---{Day:00}",
            };

            return body + FormatTimezone();
        }

        /// <summary>Whether two gregorian values are the same type and the same parts.</summary>
        /// <param name="other">The value to compare with.</param>
        public bool SameValue(XdmGregorian other)
        {
            return Name == other.Name
                && Year == other.Year
                && Month == other.Month
                && Day == other.Day
                && Offset == other.Offset;
        }

        private string FormatYear()
        {
            int year = Year ?? 0;
            return year < 0
                ? "-" + (-year).ToString("0000", CultureInfo.InvariantCulture)
                : year.ToString("0000", CultureInfo.InvariantCulture);
        }

        private string FormatTimezone()
        {
            if (Offset is not TimeSpan offset)
            {
                return string.Empty;
            }

            if (offset == TimeSpan.Zero)
            {
                return "Z";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1:00}:{2:00}",
                offset < TimeSpan.Zero ? '-' : '+',
                Math.Abs(offset.Hours),
                Math.Abs(offset.Minutes));
        }

        private static bool Build(
            string name,
            int? year,
            int? month,
            int? day,
            TimeSpan? offset,
            out XdmGregorian? result)
        {
            result = new XdmGregorian(name, year, month, day, offset);
            return true;
        }

        private static bool TrySplitTimezone(ref string body, out TimeSpan? offset)
        {
            offset = null;

            if (body.EndsWith("Z", StringComparison.Ordinal))
            {
                offset = TimeSpan.Zero;
                body = body[..^1];
                return true;
            }

            // A trailing offset is six characters, +hh:mm — and only a trailing one, since the year itself may
            // begin with a sign.
            if (body.Length > 6 && body[^6] is '+' or '-' && body[^3] == ':')
            {
                string zone = body[^6..];
                if (!int.TryParse(zone[1..3], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
                    || !int.TryParse(zone[4..], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
                    || hours > 14 || minutes > 59)
                {
                    return false;
                }

                TimeSpan magnitude = new TimeSpan(hours, minutes, 0);
                offset = zone[0] == '-' ? -magnitude : magnitude;
                body = body[..^6];
            }

            return true;
        }

        private static bool TryYear(string text, out int year)
        {
            year = 0;
            bool negative = text.StartsWith("-", StringComparison.Ordinal);
            string digits = negative ? text[1..] : text;

            // Four digits at least, and a longer year must not start with a zero: 02004 is not 2004 written
            // differently but outside the lexical space, and reading it as 2004 would take a typo for a year.
            if (digits.Length < 4 || (digits.Length > 4 && digits[0] == '0'))
            {
                return false;
            }

            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out year))
            {
                // Digits and nothing else, and too many of them for an int: a year this engine cannot
                // hold rather than text that is not a year at all.
                s_yearOutOfRange = AllDigits(digits);
                return false;
            }

            // There is no year zero. XSD 1.0 counts 1 BCE as -0001, so 0000 names nothing, and reading
            // it as a year would invent one.
            if (year == 0)
            {
                return false;
            }

            if (negative)
            {
                year = -year;
            }

            return true;
        }

        /// <summary>Whether a run of text is digits and nothing else.</summary>
        private static bool AllDigits(string text)
        {
            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return text.Length != 0;
        }

        /// <summary>
        /// The longest a month can be, counting February as 29 because no year is named to say otherwise.
        /// </summary>
        private static int DaysIn(int month)
        {
            return month switch
            {
                2 => 29,
                4 or 6 or 9 or 11 => 30,
                _ => 31,
            };
        }

        private static bool TryPart(string text, int width, int minimum, int maximum, out int value)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                && text.Length == width
                && value >= minimum
                && value <= maximum;
        }
    }
}
