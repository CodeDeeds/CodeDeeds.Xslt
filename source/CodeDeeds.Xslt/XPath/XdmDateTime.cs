using System.Globalization;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A date, a time or a date and time, with a timezone that may be absent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The absent timezone is why <see cref="DateTimeOffset"/> will not do. XML Schema distinguishes
    /// <c>2020-01-01T00:00:00</c> from <c>2020-01-01T00:00:00Z</c>: the first names a local time somewhere
    /// unstated, the second an instant. They are different values, they format differently, and comparing one
    /// with the other has to reach for a timezone that neither of them carries.
    /// </para>
    /// <para>
    /// That timezone — the <em>implicit</em> one — is UTC here. The specification leaves it to the
    /// implementation, and the alternative is the machine's own, which would make the same expression over the
    /// same input give different answers on different machines. This engine already made that choice for
    /// <c>xsl:sort</c> without <c>lang</c>, and makes it again for the same reason.
    /// </para>
    /// </remarks>
    public readonly struct XdmDateTime : IEquatable<XdmDateTime>
    {
        /// <summary>
        /// What reading a lexical form arrived at.
        /// </summary>
        /// <remarks>
        /// Two ways of failing, and they are not the same complaint. <c>xs:date('2004-02-30')</c> names no
        /// day and never will; <c>xs:date('-1999-05-31')</c> names one perfectly well and this engine
        /// cannot hold it, <see cref="DateTime"/> starting at the common era. The first is a value outside
        /// the type's lexical space, the second an overflow, and the specification gives them different
        /// codes — <c>FORG0001</c> and <c>FODT0001</c> — precisely so a reader can tell a typo from a limit.
        /// </remarks>
        public enum Reading : byte
        {
            /// <summary>A value was read.</summary>
            Value,

            /// <summary>The text is not in the type's lexical space.</summary>
            NotLexical,

            /// <summary>The text names a moment this engine cannot hold.</summary>
            OutOfRange,
        }

        /// <summary>The timezone assumed for a value that carries none.</summary>
        public static readonly TimeSpan ImplicitTimezone = TimeSpan.Zero;

        private XdmDateTime(DateTime value, TimeSpan? offset, XdmTypeCode type)
        {
            Value = value;
            Offset = offset;
            Type = type;
        }

        /// <summary>Gets the local date and time, without reference to any timezone.</summary>
        public DateTime Value { get; }

        /// <summary>Gets the timezone offset, or <see langword="null"/> when the value carries none.</summary>
        public TimeSpan? Offset { get; }

        /// <summary>Gets which of the three types this value is.</summary>
        public XdmTypeCode Type { get; }

        /// <summary>Gets the instant this value denotes, using the implicit timezone if it carries none.</summary>
        public DateTime Instant => Value - (Offset ?? ImplicitTimezone);

        /// <summary>
        /// Returns this value seen from another timezone: the same instant, written differently.
        /// </summary>
        /// <param name="offset">The timezone to move into.</param>
        public XdmDateTime WithTimezone(TimeSpan offset)
        {
            // A value that carries no timezone denotes no instant, so there is no instant to preserve: the
            // timezone is attached and the clock reading left alone. 2002-03-07 in −10:00 is
            // 2002-03-07−10:00 and not the previous day, which is what converting would have made of it.
            return Offset is null
                ? new XdmDateTime(Value, offset, Type)
                : new XdmDateTime(Instant + offset, offset, Type);
        }

        /// <summary>
        /// Returns this value moved to another local time, keeping its type and timezone.
        /// </summary>
        /// <param name="value">The local date and time to move to.</param>
        public XdmDateTime WithValue(DateTime value)
        {
            return new XdmDateTime(value, Offset, Type);
        }

        /// <summary>
        /// Returns this value as one of the three types, keeping the components that type carries.
        /// </summary>
        /// <remarks>
        /// A dateTime holds both halves and gives up whichever the target does not want. A date has no time
        /// of day, so becoming a dateTime puts it at midnight — the only moment a date can be said to name.
        /// A time keeps its own fixed reference date, since nothing ever reads it.
        /// </remarks>
        /// <param name="type">The type to become: <see cref="XdmTypeCode.Date"/>,
        /// <see cref="XdmTypeCode.Time"/> or <see cref="XdmTypeCode.DateTime"/>.</param>
        public XdmDateTime As(XdmTypeCode type)
        {
            DateTime value = type switch
            {
                XdmTypeCode.Date => Value.Date,
                XdmTypeCode.Time => new DateTime(1972, 12, 31) + Value.TimeOfDay,
                _ => Value,
            };

            return new XdmDateTime(value, Offset, type);
        }

        /// <summary>
        /// Returns this value with its timezone removed, keeping the local time it showed.
        /// </summary>
        /// <remarks>
        /// The one adjustment that does not preserve the instant, and cannot: a value with no timezone does
        /// not denote one. What it keeps is what the clock said.
        /// </remarks>
        public XdmDateTime WithoutTimezone()
        {
            return new XdmDateTime(Value, null, Type);
        }

        /// <summary>
        /// Parses the lexical form of <c>xs:date</c>, <c>xs:time</c> or <c>xs:dateTime</c>.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <param name="type">Which of the three to expect.</param>
        /// <param name="result">On success, the value.</param>
        /// <returns><see langword="true"/> if the text is in the type's lexical space.</returns>
        public static bool TryParse(string text, XdmTypeCode type, out XdmDateTime result)
        {
            return Read(text, type, out result) == Reading.Value;
        }

        /// <summary>
        /// Parses the lexical form of <c>xs:date</c>, <c>xs:time</c> or <c>xs:dateTime</c>, saying which of
        /// the two ways it failed if it did.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <param name="type">Which of the three to expect.</param>
        /// <param name="result">On success, the value.</param>
        public static Reading Read(string text, XdmTypeCode type, out XdmDateTime result)
        {
            result = default;
            ReadOnlySpan<char> span = text.AsSpan().Trim();

            if (span.Length == 0 || !TrySplitTimezone(ref span, out TimeSpan? offset))
            {
                return Reading.NotLexical;
            }

            switch (type)
            {
                case XdmTypeCode.Date:
                {
                    Reading reading = ReadDate(span, out DateTime date);
                    if (reading != Reading.Value)
                    {
                        return reading;
                    }

                    result = new XdmDateTime(date, offset, type);
                    return Reading.Value;
                }

                case XdmTypeCode.Time:
                {
                    if (!TryParseTime(span, out TimeSpan time))
                    {
                        return Reading.NotLexical;
                    }

                    // A time has no date, so it is held against a fixed one; only the time of day is ever
                    // read. 24:00:00 is that fixed day's own midnight and not the next day's: an xs:time has
                    // no day to roll over into, so the hour comes back to zero where it stands. An
                    // xs:dateTime does have a day, and there 24:00:00 does move on to the next one.
                    result = new XdmDateTime(
                        new DateTime(1972, 12, 31).AddTicks(time.Ticks % TimeSpan.TicksPerDay), offset, type);
                    return Reading.Value;
                }

                default:
                {
                    int t = span.IndexOf('T');
                    if (t < 0)
                    {
                        return Reading.NotLexical;
                    }

                    Reading reading = ReadDate(span[..t], out DateTime day);
                    if (reading != Reading.Value)
                    {
                        return reading;
                    }

                    if (!TryParseTime(span[(t + 1)..], out TimeSpan clock))
                    {
                        return Reading.NotLexical;
                    }

                    // A day plus a time of day can pass the last moment DateTime holds, which the addition
                    // would otherwise report as an argument being wrong rather than as the overflow it is.
                    if (clock > DateTime.MaxValue - day)
                    {
                        return Reading.OutOfRange;
                    }

                    result = new XdmDateTime(day + clock, offset, type);
                    return Reading.Value;
                }
            }
        }

        /// <summary>
        /// Removes a trailing timezone, which is <c>Z</c> or an offset of hours and minutes, and reports
        /// whether there was one.
        /// </summary>
        private static bool TrySplitTimezone(ref ReadOnlySpan<char> span, out TimeSpan? offset)
        {
            offset = null;

            if (span.Length == 0)
            {
                return false;
            }

            if (span[^1] == 'Z')
            {
                offset = TimeSpan.Zero;
                span = span[..^1];
                return true;
            }

            // An offset is exactly six characters, and the sign must not be mistaken for a negative year.
            if (span.Length < 7 || span[^3] != ':' || (span[^6] != '+' && span[^6] != '-'))
            {
                return true;
            }

            ReadOnlySpan<char> zone = span[^6..];

            if (!int.TryParse(zone[1..3], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
                || !int.TryParse(zone[4..6], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
                || hours > 14 || minutes > 59 || (hours == 14 && minutes != 0))
            {
                return false;
            }

            TimeSpan magnitude = new TimeSpan(hours, minutes, 0);
            offset = zone[0] == '-' ? -magnitude : magnitude;
            span = span[..^6];
            return true;
        }

        private static Reading ReadDate(ReadOnlySpan<char> span, out DateTime date)
        {
            date = default;

            // A year before the common era is in the lexical space, and outside DateTime's range. The sign is
            // taken off here so that the rest of the form is still checked: '-2004-13-01' names no month,
            // whatever era it claims, and that is a different complaint from the era itself.
            bool negative = span.Length != 0 && span[0] == '-';
            if (negative)
            {
                span = span[1..];
            }

            // YYYY-MM-DD, with at least four digits of year and no upper limit on them. A year longer than
            // four digits must not start with a zero: 02004 is not 2004 written differently, it is outside
            // the lexical space, and reading it as 2004 would take a typo for a date.
            int firstDash = span.IndexOf('-');
            if (firstDash < 4 || span.Length != firstDash + 6 || span[firstDash + 3] != '-'
                || (firstDash > 4 && span[0] == '0'))
            {
                return Reading.NotLexical;
            }

            ReadOnlySpan<char> years = span[..firstDash];

            foreach (char digit in years)
            {
                if (digit is < '0' or > '9')
                {
                    return Reading.NotLexical;
                }
            }

            if (!int.TryParse(span.Slice(firstDash + 1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month)
                || !int.TryParse(span.Slice(firstDash + 4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int day)
                || month is < 1 or > 12 || day < 1)
            {
                return Reading.NotLexical;
            }

            // A year of more digits than an int holds is still a year, and whether the day exists in it is
            // the only thing left to decide. The leap rule turns on the year modulo 400, and 400 divides
            // 10000, so the last four digits settle it however many there are.
            if (!int.TryParse(years, NumberStyles.None, CultureInfo.InvariantCulture, out int year))
            {
                int tail = int.Parse(years[^4..], NumberStyles.None, CultureInfo.InvariantCulture);
                return day > DaysIn(tail, month) ? Reading.NotLexical : Reading.OutOfRange;
            }

            if (year == 0 || day > DaysIn(year, month))
            {
                return Reading.NotLexical;
            }

            if (negative || year > 9999)
            {
                return Reading.OutOfRange;
            }

            date = new DateTime(year, month, day);
            return Reading.Value;
        }

        /// <summary>
        /// The number of days in a month of a given year.
        /// </summary>
        /// <remarks>
        /// Its own arithmetic rather than <see cref="DateTime.DaysInMonth"/>, which refuses a year outside
        /// the range it can hold — and a year outside that range is exactly the case this has to decide,
        /// since whether the day exists says which of the two errors to raise.
        /// </remarks>
        private static int DaysIn(int year, int month)
        {
            if (month == 2)
            {
                return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0) ? 29 : 28;
            }

            return month is 4 or 6 or 9 or 11 ? 30 : 31;
        }

        private static bool TryParseTime(ReadOnlySpan<char> span, out TimeSpan time)
        {
            time = default;

            if (span.Length < 8 || span[2] != ':' || span[5] != ':')
            {
                return false;
            }

            if (!int.TryParse(span[..2], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
                || !int.TryParse(span.Slice(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
                || !int.TryParse(span.Slice(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
            {
                return false;
            }

            decimal fraction = 0m;
            if (span.Length > 8)
            {
                if (span[8] != '.' || span.Length == 9
                    || !decimal.TryParse(span[8..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out fraction))
                {
                    return false;
                }
            }

            if (minutes > 59 || seconds > 59 || hours > 24 || (hours == 24 && (minutes != 0 || seconds != 0 || fraction != 0m)))
            {
                return false;
            }

            // 24:00:00 is midnight at the end of the day, and the only hour above 23 the lexical space allows.
            time = new TimeSpan(hours, minutes, seconds) + TimeSpan.FromTicks((long)(fraction * TimeSpan.TicksPerSecond));
            return true;
        }

        /// <summary>Writes the canonical lexical form.</summary>
        public override string ToString()
        {
            // The longest form is a dateTime with fractional seconds and an offset, at 33 characters, so this
            // is written in one piece and never grows. Each part formats straight into the buffer rather than
            // being rendered to a string and copied in.
            CharStringBuilder builder = new CharStringBuilder(stackalloc char[48]);

            if (Type != XdmTypeCode.Time)
            {
                builder.Append(Value, "yyyy-MM-dd");
            }

            if (Type == XdmTypeCode.DateTime)
            {
                builder.Append('T');
            }

            if (Type != XdmTypeCode.Date)
            {
                builder.Append(Value, "HH:mm:ss");

                // Fractional seconds appear only when there are any, and never with trailing zeros. The
                // format writes the point itself, there being no digit placeholder before it.
                long fraction = Value.Ticks % TimeSpan.TicksPerSecond;
                if (fraction != 0)
                {
                    builder.Append(fraction / (double)TimeSpan.TicksPerSecond, ".#######");
                }
            }

            if (Offset is TimeSpan offset)
            {
                if (offset == TimeSpan.Zero)
                {
                    builder.Append('Z');
                }
                else
                {
                    builder.Append(offset < TimeSpan.Zero ? '-' : '+');
                    builder.Append(offset.Duration(), "hh\\:mm");
                }
            }

            return builder.ToString();
        }

        /// <summary>Compares two values by the instants they denote.</summary>
        /// <param name="other">The value to compare against.</param>
        public int CompareTo(XdmDateTime other) => Instant.CompareTo(other.Instant);

        /// <inheritdoc/>
        public bool Equals(XdmDateTime other) => Instant == other.Instant && Type == other.Type;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is XdmDateTime other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Instant.GetHashCode();
    }
}
