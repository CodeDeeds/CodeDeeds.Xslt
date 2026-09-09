using System.Globalization;
using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Reads the date formats that email and HTTP headers are written in, which
    /// <c>fn:parse-ietf-date</c> accepts.
    /// </summary>
    /// <remarks>
    /// The specification gives a grammar rather than a list of formats, and it is wider than RFC 5322's:
    /// the day name is optional and may be spelled out, the comma after it is optional, the separators
    /// between day, month and year may be hyphens, the seconds are optional, the year may be two digits,
    /// and the whole thing may instead be written in <c>asctime</c> order with the year last. This reads the
    /// grammar directly rather than trying a list of patterns, because the patterns would number in the
    /// hundreds and the errors would say nothing about which part went wrong.
    /// <para>
    /// The day name is read and discarded without being checked against the date: <c>Sun, 20 Aug 2014</c> is
    /// a Wednesday and the specification still asks for it to be accepted.
    /// </para>
    /// </remarks>
    internal static class IetfDate
    {
        private static readonly string[] Months =
        {
            "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec",
        };

        private static readonly string[] DayNames =
        {
            "mon", "tue", "wed", "thu", "fri", "sat", "sun",
            "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
        };

        /// <summary>
        /// The named timezones the grammar allows, with the offsets they stand for.
        /// </summary>
        /// <remarks>
        /// This list and no other: <c>CET</c> is a perfectly good timezone name and is not one of these, so
        /// a date carrying it is rejected rather than guessed at.
        /// </remarks>
        private static readonly (string Name, int Hours)[] Zones =
        {
            ("ut", 0), ("utc", 0), ("gmt", 0),
            ("est", -5), ("edt", -4),
            ("cst", -6), ("cdt", -5),
            ("mst", -7), ("mdt", -6),
            ("pst", -8), ("pdt", -7),
        };

        /// <summary>
        /// Reads a date, or reports that the text is not one.
        /// </summary>
        /// <param name="text">The text to read.</param>
        /// <param name="result">On success, the moment it names.</param>
        /// <returns><see langword="true"/> if the text was read.</returns>
        public static bool TryParse(string text, out XdmDateTime result)
        {
            result = default;

            int at = 0;
            SkipSpace(text, ref at);
            SkipDayName(text, ref at);

            int day;
            int month;
            int year;
            TimeOfDay time;

            if (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                // The day first, which is how a mail header writes it.
                if (!ReadNumber(text, ref at, 1, 2, out day)
                    || !ReadSeparator(text, ref at)
                    || !ReadMonth(text, ref at, out month)
                    || !ReadSeparator(text, ref at)
                    || !ReadYear(text, ref at, out year)
                    || !SkipSpace(text, ref at)
                    || !ReadTime(text, ref at, out time))
                {
                    return false;
                }
            }
            else
            {
                // The asctime order, which puts the year at the end and the month at the front.
                if (!ReadMonth(text, ref at, out month)
                    || !ReadSeparator(text, ref at)
                    || !ReadNumber(text, ref at, 1, 2, out day)
                    || !SkipSpace(text, ref at)
                    || !ReadTime(text, ref at, out time)
                    || !SkipSpace(text, ref at)
                    || !ReadYear(text, ref at, out year))
                {
                    return false;
                }
            }

            SkipSpace(text, ref at);

            if (at != text.Length)
            {
                return false;
            }

            return Build(year, month, day, time, out result);
        }

        /// <summary>The time of day, with whatever the text said about its timezone.</summary>
        private struct TimeOfDay
        {
            public int Hour;
            public int Minute;
            public int Second;
            public string Fraction;
            public TimeSpan? Offset;
        }

        /// <summary>
        /// Assembles the parts, letting <see cref="XdmDateTime"/> judge whether they name a moment.
        /// </summary>
        /// <remarks>
        /// The lexical form is written out and read back rather than the components being checked here, so
        /// that the twenty-ninth of February in a common year is refused by the one piece of code that
        /// already knows which years have one.
        /// </remarks>
        private static bool Build(int year, int month, int day, TimeOfDay time, out XdmDateTime result)
        {
            StringBuilder lexical = new StringBuilder(40);

            // Two digits mean the twentieth century, which is what the grammar says and is a decision the
            // specification made rather than one left open.
            lexical.Append((year < 100 ? year + 1900 : year).ToString("D4", CultureInfo.InvariantCulture));
            lexical.Append('-').Append(month.ToString("D2", CultureInfo.InvariantCulture));
            lexical.Append('-').Append(day.ToString("D2", CultureInfo.InvariantCulture));
            lexical.Append('T').Append(time.Hour.ToString("D2", CultureInfo.InvariantCulture));
            lexical.Append(':').Append(time.Minute.ToString("D2", CultureInfo.InvariantCulture));
            lexical.Append(':').Append(time.Second.ToString("D2", CultureInfo.InvariantCulture));

            if (time.Fraction.Length > 0)
            {
                lexical.Append('.').Append(time.Fraction);
            }

            // A date with nothing said about its timezone is read as UTC, not as having no timezone: the
            // function answers xs:dateTime and the specification gives it Z.
            TimeSpan offset = time.Offset ?? TimeSpan.Zero;
            lexical.Append(offset < TimeSpan.Zero ? '-' : '+');
            lexical.Append(Math.Abs(offset.Hours).ToString("D2", CultureInfo.InvariantCulture));
            lexical.Append(':').Append(Math.Abs(offset.Minutes).ToString("D2", CultureInfo.InvariantCulture));

            return XdmDateTime.Read(lexical.ToString(), XdmTypeCode.DateTime, out result)
                == XdmDateTime.Reading.Value;
        }

        /// <summary>Steps over a day name and the comma that may follow it, if one is there.</summary>
        private static void SkipDayName(string text, ref int at)
        {
            int mark = at;
            string name = ReadLetters(text, ref at);

            if (name.Length > 0 && Array.IndexOf(DayNames, name) >= 0)
            {
                if (at < text.Length && text[at] == ',')
                {
                    at++;
                }

                // Whitespace has to follow, which is what makes 'Wed,20 Aug' a mistake.
                if (SkipSpace(text, ref at))
                {
                    return;
                }
            }

            at = mark;
        }

        /// <summary>
        /// Reads what stands between the day, the month and the year, which is space, a hyphen, or both.
        /// </summary>
        private static bool ReadSeparator(string text, ref int at)
        {
            bool spaced = SkipSpace(text, ref at);

            if (at < text.Length && text[at] == '-')
            {
                at++;
                SkipSpace(text, ref at);
                return true;
            }

            return spaced;
        }

        private static bool ReadMonth(string text, ref int at, out int month)
        {
            month = Array.IndexOf(Months, ReadLetters(text, ref at)) + 1;
            return month > 0;
        }

        /// <summary>Reads a year, which the grammar writes with two digits or four and nothing between.</summary>
        private static bool ReadYear(string text, ref int at, out int year)
        {
            int start = at;
            year = 0;

            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
            }

            int digits = at - start;

            return (digits == 2 || digits == 4)
                && int.TryParse(text.AsSpan(start, digits), NumberStyles.None, CultureInfo.InvariantCulture, out year);
        }

        private static bool ReadTime(string text, ref int at, out TimeOfDay time)
        {
            time = new TimeOfDay { Fraction = string.Empty };

            if (!ReadNumber(text, ref at, 1, 2, out time.Hour)
                || at >= text.Length
                || text[at] != ':')
            {
                return false;
            }

            at++;

            if (!ReadNumber(text, ref at, 2, 2, out time.Minute))
            {
                return false;
            }

            if (at < text.Length && text[at] == ':')
            {
                at++;

                if (!ReadNumber(text, ref at, 2, 2, out time.Second))
                {
                    return false;
                }

                if (at < text.Length && text[at] == '.')
                {
                    at++;
                    int start = at;

                    while (at < text.Length && char.IsAsciiDigit(text[at]))
                    {
                        at++;
                    }

                    if (at == start)
                    {
                        return false;
                    }

                    time.Fraction = text[start..at];
                }
            }

            // A timezone may follow, and may not: where it does not, the space before whatever comes next
            // belongs to whatever comes next, so nothing is consumed unless a timezone is actually read.
            int mark = at;
            SkipSpace(text, ref at);

            if (ReadTimezone(text, ref at, out TimeSpan offset))
            {
                time.Offset = offset;
            }
            else
            {
                at = mark;
            }

            return true;
        }

        private static bool ReadTimezone(string text, ref int at, out TimeSpan offset)
        {
            offset = default;

            if (at >= text.Length)
            {
                return false;
            }

            if (text[at] is '+' or '-')
            {
                return ReadOffset(text, ref at, out offset);
            }

            string name = ReadLetters(text, ref at);

            foreach ((string zone, int hours) in Zones)
            {
                if (zone == name)
                {
                    offset = TimeSpan.FromHours(hours);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads a numeric timezone offset, whose digits may be grouped in any of several ways.
        /// </summary>
        /// <remarks>
        /// The digits are counted before they are read, because the alternative is a greedy scan that gets
        /// <c>-500</c> wrong: two digits of hours would leave one of minutes, where the grammar wants one and
        /// two. A colon separates the two groups where it appears, and may appear with nothing after it.
        /// </remarks>
        private static bool ReadOffset(string text, ref int at, out TimeSpan offset)
        {
            offset = default;
            int sign = text[at] == '-' ? -1 : 1;
            at++;

            int start = at;
            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
            }

            int digits = at - start;
            int hours;
            int minutes = 0;

            if (at < text.Length && text[at] == ':')
            {
                if (digits is not (1 or 2))
                {
                    return false;
                }

                at++;
                hours = Number(text, start, digits);

                int minuteStart = at;
                while (at < text.Length && char.IsAsciiDigit(text[at]))
                {
                    at++;
                }

                // Two digits or none. One is a mistake rather than a shorthand.
                if (at - minuteStart == 2)
                {
                    minutes = Number(text, minuteStart, 2);
                }
                else if (at != minuteStart)
                {
                    return false;
                }
            }
            else
            {
                switch (digits)
                {
                    case 1: hours = Number(text, start, 1); break;
                    case 2: hours = Number(text, start, 2); break;
                    case 3: hours = Number(text, start, 1); minutes = Number(text, start + 1, 2); break;
                    case 4: hours = Number(text, start, 2); minutes = Number(text, start + 2, 2); break;
                    default: return false;
                }
            }

            if (minutes > 59 || hours > 14 || (hours == 14 && minutes > 0))
            {
                return false;
            }

            offset = new TimeSpan(sign * hours, sign * minutes, 0);

            // A name in brackets may follow an offset, and says nothing the offset has not already said.
            int mark = at;
            SkipSpace(text, ref at);

            if (at < text.Length && text[at] == '(')
            {
                at++;
                SkipSpace(text, ref at);
                string name = ReadLetters(text, ref at);
                SkipSpace(text, ref at);

                if (at >= text.Length || text[at] != ')' || !IsZoneName(name))
                {
                    return false;
                }

                at++;
            }
            else
            {
                at = mark;
            }

            return true;
        }

        private static bool IsZoneName(string name)
        {
            foreach ((string zone, int _) in Zones)
            {
                if (zone == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static int Number(string text, int start, int length)
        {
            int value = 0;

            for (int i = 0; i < length; i++)
            {
                value = (value * 10) + (text[start + i] - '0');
            }

            return value;
        }

        /// <summary>Reads a run of digits of a given width, and fails on a run of any other width.</summary>
        private static bool ReadNumber(string text, ref int at, int least, int most, out int value)
        {
            int start = at;
            value = 0;

            while (at < text.Length && at - start < most && char.IsAsciiDigit(text[at]))
            {
                at++;
            }

            if (at - start < least)
            {
                return false;
            }

            value = Number(text, start, at - start);
            return true;
        }

        /// <summary>Reads a run of ASCII letters, folded to lower case for comparison.</summary>
        private static string ReadLetters(string text, ref int at)
        {
            int start = at;

            while (at < text.Length && char.IsAsciiLetter(text[at]))
            {
                at++;
            }

            return text[start..at].ToLowerInvariant();
        }

        /// <summary>Steps over whitespace, reporting whether there was any.</summary>
        private static bool SkipSpace(string text, ref int at)
        {
            int start = at;

            while (at < text.Length && text[at] is ' ' or '\t' or '\r' or '\n')
            {
                at++;
            }

            return at > start;
        }
    }
}
