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
    /// <para>
    /// The year range is why <see cref="DateTime"/> is not stored outright either. XML Schema puts no bound
    /// on the year: <c>-1999-05-31</c> and <c>654321-01-01</c> are both dates, and <see cref="DateTime"/>
    /// begins at the common era and ends at 9999. What is stored instead is a <em>proxy</em> date whose year
    /// stands in for the real one, together with the number of whole 400-year cycles between them. The
    /// proleptic Gregorian calendar repeats exactly every 400 years — the leap rule does, and so does the
    /// day of the week, 400 years being 146,097 days and 146,097 divisible by 7 — so the proxy has the real
    /// value's month, day, time of day, day of week and day of year, and only its year is a stand-in. Every
    /// reader of those fields can go on using <see cref="DateTime"/>; only the year has to be asked for.
    /// </para>
    /// <para>
    /// Years are held <em>astronomically</em> — 0 is 1 BCE, −1 is 2 BCE — so that the timeline is continuous
    /// and arithmetic across the era boundary is ordinary arithmetic. XML Schema 1.0 spells the same years
    /// with no zero, 1 BCE being <c>-0001</c>, and that spelling is put on and taken off at the lexical
    /// edge: <see cref="Year"/> and <see cref="ToString"/> answer in it, and nothing inside uses it.
    /// </para>
    /// </remarks>
    public readonly struct XdmDateTime : IEquatable<XdmDateTime>
    {
        /// <summary>
        /// What reading a lexical form arrived at.
        /// </summary>
        /// <remarks>
        /// Two ways of failing, and they are not the same complaint. <c>xs:date('2004-02-30')</c> names no
        /// day and never will; <c>xs:date('-99999999999-05-31')</c> names one perfectly well and this engine
        /// holds the year in an <see cref="int"/>. The first is a value outside the type's lexical space, the
        /// second an overflow, and the specification gives them different codes — <c>FORG0001</c> and
        /// <c>FODT0001</c> — precisely so a reader can tell a typo from a limit.
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

        /// <summary>A moment on the timeline, as a day and the tick within it.</summary>
        /// <remarks>
        /// Not a single count of ticks, which would overflow: the years this holds run to nine digits, and
        /// that is more ticks than a <see cref="long"/> has. Kept apart, the day count needs twelve digits
        /// and the tick within the day five.
        /// </remarks>
        internal readonly struct Moment : IComparable<Moment>, IEquatable<Moment>
        {
            internal Moment(long day, long tick)
            {
                Day = day;
                Tick = tick;
            }

            /// <summary>The day, counted from 1970-01-01 in the proleptic Gregorian calendar.</summary>
            internal long Day { get; }

            /// <summary>The tick within that day, from zero to a day less one.</summary>
            internal long Tick { get; }

            /// <inheritdoc/>
            public int CompareTo(Moment other)
            {
                int day = Day.CompareTo(other.Day);
                return day != 0 ? day : Tick.CompareTo(other.Tick);
            }

            /// <inheritdoc/>
            public bool Equals(Moment other) => Day == other.Day && Tick == other.Tick;

            /// <inheritdoc/>
            public override bool Equals(object? obj) => obj is Moment other && Equals(other);

            /// <inheritdoc/>
            public override int GetHashCode() => HashCode.Combine(Day, Tick);
        }

        /// <summary>The timezone assumed for a value that carries none.</summary>
        public static readonly TimeSpan ImplicitTimezone = TimeSpan.Zero;

        /// <summary>The largest year this engine holds, written without its sign.</summary>
        /// <remarks>
        /// Nine digits, which is inside what an <see cref="int"/> holds with room to spare for the cycle
        /// arithmetic below. XML Schema puts no bound on the year at all, so there has to be one here, and
        /// this is far past any year a document is going to carry.
        /// </remarks>
        public const int MaxYear = 999_999_999;

        /// <summary>The whole of the proleptic Gregorian calendar's repetition, in years.</summary>
        private const int Cycle = 400;

        /// <summary>That same cycle in days, which is exactly how long 400 Gregorian years are.</summary>
        private const long DaysPerCycle = 146_097L;

        /// <summary>The first year of the window a proxy is moved into.</summary>
        private const int ProxyFloor = 2000;

        /// <summary>
        /// The years a value is left standing in, its proxy being itself.
        /// </summary>
        /// <remarks>
        /// Not the whole of what <see cref="DateTime"/> holds. A timezone reaches fourteen hours either way,
        /// so a value at the very first or very last day of the range would push its own instant off the end
        /// while being compared. Leaving a thousand years clear at each end costs nothing — almost every
        /// date is inside it, and the ones that are not were being refused outright until now.
        /// </remarks>
        private const int SettledFloor = 1000;

        /// <summary>The last year a value is left standing in.</summary>
        private const int SettledCeiling = 8999;

        private readonly DateTime m_proxy;
        private readonly int m_cycles;

        private XdmDateTime(DateTime proxy, int cycles, TimeSpan? offset, XdmTypeCode type)
        {
            m_proxy = proxy;
            m_cycles = cycles;
            Offset = offset;
            Type = type;
        }

        /// <summary>Gets the timezone offset, or <see langword="null"/> when the value carries none.</summary>
        public TimeSpan? Offset { get; }

        /// <summary>Gets which of the three types this value is.</summary>
        public XdmTypeCode Type { get; }

        /// <summary>
        /// Gets the date and time whose month, day, time of day, day of week and day of year are this
        /// value's own. Its <em>year</em> is a stand-in and means nothing; ask <see cref="Year"/> for that.
        /// </summary>
        internal DateTime Fields => m_proxy;

        /// <summary>Gets the year as XML Schema 1.0 spells it, where −1 is 1 BCE and there is no zero.</summary>
        public int Year => Spell(AstronomicalYear);

        /// <summary>Gets the year counted continuously, where 0 is 1 BCE and −1 is 2 BCE.</summary>
        private int AstronomicalYear => m_proxy.Year + (m_cycles * Cycle);

        /// <summary>Gets the tick within the day, from zero to a day less one.</summary>
        private long TickOfDay => m_proxy.Ticks % TimeSpan.TicksPerDay;

        /// <summary>Gets the instant this value denotes, using the implicit timezone if it carries none.</summary>
        internal Moment Instant
        {
            get
            {
                long day = DaysFromCivil(AstronomicalYear, m_proxy.Month, m_proxy.Day);
                return Normalize(day, TickOfDay - (Offset ?? ImplicitTimezone).Ticks);
            }
        }

        /// <summary>
        /// Returns this value seen from another timezone: the same instant, written differently.
        /// </summary>
        /// <param name="offset">The timezone to move into.</param>
        public XdmDateTime WithTimezone(TimeSpan offset)
        {
            // A value that carries no timezone denotes no instant, so there is no instant to preserve: the
            // timezone is attached and the clock reading left alone. 2002-03-07 in −10:00 is
            // 2002-03-07−10:00 and not the previous day, which is what converting would have made of it.
            if (Offset is null)
            {
                return new XdmDateTime(m_proxy, m_cycles, offset, Type);
            }

            Moment instant = Instant;
            Moment moved = Normalize(instant.Day, instant.Tick + offset.Ticks);

            // A value at the very edge of the range can be pushed over it by a timezone, which is an
            // overflow in a date operation and not an argument this method would not take.
            try
            {
                // Back to this value's own type, which is where a date loses the time of day the
                // timezone gave it and a time loses the day. The specification says the same thing the
                // long way round: take the date as a dateTime at midnight, move that, cast back.
                return At(moved, offset, Type).As(Type);
            }
            catch (ArgumentOutOfRangeException error)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FODT0001,
                    $"Seeing '{this}' from a timezone of {offset} leaves the range this engine holds "
                    + $"dates in, which runs to the year {MaxYear} either side of the common era.",
                    error);
            }
        }

        /// <summary>
        /// Returns this value moved by a number of months and a number of ticks, in that order.
        /// </summary>
        /// <remarks>
        /// Months first and as months, because they are not all the same length: one month after 31 January
        /// is 28 February, which no count of days would produce. A day past the end of the month it lands in
        /// comes back to that month's last day, which is what the specification's own algorithm does.
        /// </remarks>
        /// <remarks>
        /// The result carries only what its own type carries. A date moved by a duration with hours in it
        /// is a date and holds no hours afterwards, and a time moved past midnight is a time and holds no
        /// day: keeping either would give a value that prints as one thing and compares as another.
        /// </remarks>
        /// <param name="months">The months to move by, which may be negative.</param>
        /// <param name="ticks">The ticks to move by afterwards, which may be negative.</param>
        internal XdmDateTime Moved(int months, long ticks)
        {
            int year = AstronomicalYear;
            int month = m_proxy.Month;
            int day = m_proxy.Day;

            if (months != 0)
            {
                long counted = ((long)year * 12) + (month - 1) + months;
                long landed = FloorDiv(counted, 12);

                if (landed < Spell(-MaxYear) || landed > MaxYear)
                {
                    throw new ArgumentOutOfRangeException(nameof(months));
                }

                year = (int)landed;
                month = (int)(counted - (landed * 12)) + 1;
                day = Math.Min(day, DaysIn(year, month));
            }

            // The ticks are split into whole days and the remainder before being added, so that a duration
            // of many years' worth of them cannot overflow the count it is added to.
            long whole = FloorDiv(ticks, TimeSpan.TicksPerDay);
            long rest = ticks - (whole * TimeSpan.TicksPerDay);

            Moment moved = Normalize(DaysFromCivil(year, month, day) + whole, TickOfDay + rest);

            // And back to this value's own type. A date plus P23DT09H32M59S is the date twenty-three
            // days later and not that date carrying half a day: the hours move it and are then gone.
            // A time has no day to keep, so the same step is what wraps it around the 24-hour clock.
            return At(moved, Offset, Type).As(Type);
        }

        /// <summary>A text that tells this value from every other, for a map key or a grouping key.</summary>
        /// <remarks>
        /// The day is carried beside the tick rather than folded into it. Two moments four hundred years
        /// apart share a proxy, so a key taken from the proxy alone would put them in the same group.
        /// A value with no timezone denotes no instant, so it is keyed by the clock it shows instead; the
        /// prefix keeps the two kinds apart, one having an instant and the other not.
        /// </remarks>
        internal string Key
        {
            get
            {
                Moment moment = Offset is null
                    ? new Moment(DaysFromCivil(AstronomicalYear, m_proxy.Month, m_proxy.Day), TickOfDay)
                    : Instant;

                return (Offset is null ? "-" : "+") + moment.Day + ":" + moment.Tick;
            }
        }

        /// <summary>The seconds from one moment to another, which is what subtracting two of them gives.</summary>
        /// <param name="from">The moment subtracted from.</param>
        /// <param name="to">The moment subtracted.</param>
        internal static decimal SecondsBetween(XdmDateTime from, XdmDateTime to)
        {
            Moment left = from.Instant;
            Moment right = to.Instant;

            return ((left.Day - right.Day) * 86_400m)
                + ((left.Tick - right.Tick) / (decimal)TimeSpan.TicksPerSecond);
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
            if (type == XdmTypeCode.Time)
            {
                return new XdmDateTime(new DateTime(1972, 12, 31).AddTicks(TickOfDay), 0, Offset, type);
            }

            DateTime proxy = type == XdmTypeCode.Date ? m_proxy.Date : m_proxy;
            return new XdmDateTime(proxy, m_cycles, Offset, type);
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
            return new XdmDateTime(m_proxy, m_cycles, null, Type);
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
                    Reading reading = ReadDate(span, out int year, out int month, out int day);
                    if (reading != Reading.Value)
                    {
                        return reading;
                    }

                    result = At(new Moment(DaysFromCivil(year, month, day), 0), offset, type);
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
                        new DateTime(1972, 12, 31).AddTicks(time.Ticks % TimeSpan.TicksPerDay), 0, offset, type);
                    return Reading.Value;
                }

                default:
                {
                    int t = span.IndexOf('T');
                    if (t < 0)
                    {
                        return Reading.NotLexical;
                    }

                    Reading reading = ReadDate(span[..t], out int year, out int month, out int day);
                    if (reading != Reading.Value)
                    {
                        return reading;
                    }

                    if (!TryParseTime(span[(t + 1)..], out TimeSpan clock))
                    {
                        return Reading.NotLexical;
                    }

                    // 24:00:00 is the last day's own midnight written as the end of this one, so the day
                    // rolls over. That can land on the first day of the year after the last one this holds,
                    // which is the one way a well-formed dateTime still leaves the range.
                    Moment moment = Normalize(DaysFromCivil(year, month, day), clock.Ticks);

                    if (moment.Day > LastDay)
                    {
                        return Reading.OutOfRange;
                    }

                    result = At(moment, offset, type);
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

        /// <summary>Reads <c>YYYY-MM-DD</c>, answering the year counted continuously.</summary>
        private static Reading ReadDate(ReadOnlySpan<char> span, out int year, out int month, out int day)
        {
            year = 0;
            month = 0;
            day = 0;

            // The sign is taken off here so that the rest of the form is still checked: '-2004-13-01' names
            // no month, whatever era it claims, and that is a different complaint from the era itself.
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

            if (!int.TryParse(span.Slice(firstDash + 1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month)
                || !int.TryParse(span.Slice(firstDash + 4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out day)
                || month is < 1 or > 12 || day < 1)
            {
                return Reading.NotLexical;
            }

            // A year of more digits than this holds is still a year, and whether the day exists in it is the
            // only thing left to decide. The leap rule turns on the year modulo 400, and 400 divides 10000,
            // so the last four digits settle it however many there are.
            if (!int.TryParse(years, NumberStyles.None, CultureInfo.InvariantCulture, out int written)
                || written > MaxYear)
            {
                int tail = int.Parse(years[^4..], NumberStyles.None, CultureInfo.InvariantCulture);
                int stand = negative ? Count(-tail) : tail;
                return day > DaysIn(stand, month) ? Reading.NotLexical : Reading.OutOfRange;
            }

            // There is no year zero. XML Schema 1.0 counts 1 BCE as -0001, so 0000 names nothing at all.
            if (written == 0)
            {
                return Reading.NotLexical;
            }

            year = negative ? Count(-written) : written;
            return day > DaysIn(year, month) ? Reading.NotLexical : Reading.Value;
        }

        /// <summary>The number of days in a month of a year counted continuously.</summary>
        /// <remarks>
        /// Its own arithmetic rather than <see cref="DateTime.DaysInMonth"/>, which refuses a year outside
        /// the range it can hold — and years outside that range are most of what this has to decide about.
        /// </remarks>
        private static int DaysIn(int year, int month)
        {
            if (month == 2)
            {
                // The remainder of a negative year is negative in C#, so the tests are written against zero
                // rather than for equality with it: -400 is a leap year exactly as 400 is.
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

        // ---- The calendar ------------------------------------------------------------------------------

        /// <summary>The last day this holds, being the last day of the last year.</summary>
        private static readonly long LastDay = DaysFromCivil(MaxYear, 12, 31);

        /// <summary>The first day this holds, being the first day of the earliest year.</summary>
        private static readonly long FirstDay = DaysFromCivil(Count(-MaxYear), 1, 1);

        /// <summary>Turns a year counted continuously into the year XML Schema 1.0 spells, and back.</summary>
        /// <remarks>
        /// The two numberings agree from year 1 on and differ by one below it, XML Schema 1.0 having no year
        /// zero: its −1 is 1 BCE, which counted continuously is 0. The mapping is its own inverse.
        /// </remarks>
        private static int Spell(int year) => year > 0 ? year : year - 1;

        /// <summary>Turns the year XML Schema 1.0 spells into the year counted continuously.</summary>
        private static int Count(int year) => year > 0 ? year : year + 1;

        /// <summary>Divides, rounding towards negative infinity rather than towards zero.</summary>
        private static long FloorDiv(long value, long divisor)
        {
            return (value >= 0 ? value : value - divisor + 1) / divisor;
        }

        /// <summary>Carries a tick count that has left its day into the day beside it.</summary>
        private static Moment Normalize(long day, long tick)
        {
            long whole = FloorDiv(tick, TimeSpan.TicksPerDay);
            return new Moment(day + whole, tick - (whole * TimeSpan.TicksPerDay));
        }

        /// <summary>
        /// The day a date falls on, counted from 1970-01-01 in the proleptic Gregorian calendar.
        /// </summary>
        /// <remarks>
        /// Howard Hinnant's algorithm, which shifts the year to start in March so that the leap day falls at
        /// the end of it and the months before it keep fixed lengths. It is exact for every year an
        /// <see cref="int"/> holds, which is the whole point of not going through <see cref="DateTime"/>.
        /// </remarks>
        private static long DaysFromCivil(int year, int month, int day)
        {
            long shifted = year - (month <= 2 ? 1L : 0L);
            long era = FloorDiv(shifted, Cycle);
            long yearOfEra = shifted - (era * Cycle);
            long dayOfYear = (((153 * (month + (month > 2 ? -3 : 9))) + 2) / 5) + day - 1;
            long dayOfEra = (yearOfEra * 365) + (yearOfEra / 4) - (yearOfEra / 100) + dayOfYear;

            return (era * DaysPerCycle) + dayOfEra - 719_468L;
        }

        /// <summary>The date a day number falls on, which is the inverse of <see cref="DaysFromCivil"/>.</summary>
        private static void CivilFromDays(long count, out int year, out int month, out int day)
        {
            long shifted = count + 719_468L;
            long era = FloorDiv(shifted, DaysPerCycle);
            long dayOfEra = shifted - (era * DaysPerCycle);
            long yearOfEra = (dayOfEra - (dayOfEra / 1460) + (dayOfEra / 36524) - (dayOfEra / 146096)) / 365;
            long dayOfYear = dayOfEra - ((365 * yearOfEra) + (yearOfEra / 4) - (yearOfEra / 100));
            long marchMonth = ((5 * dayOfYear) + 2) / 153;

            day = (int)(dayOfYear - (((153 * marchMonth) + 2) / 5) + 1);
            month = (int)(marchMonth + (marchMonth < 10 ? 3 : -9));
            year = (int)(yearOfEra + (era * Cycle) + (month <= 2 ? 1 : 0));
        }

        /// <summary>Builds a value at a moment, moving its year into the window a proxy stands in.</summary>
        private static XdmDateTime At(Moment moment, TimeSpan? offset, XdmTypeCode type)
        {
            if (moment.Day < FirstDay || moment.Day > LastDay)
            {
                throw new ArgumentOutOfRangeException(nameof(moment));
            }

            CivilFromDays(moment.Day, out int year, out int month, out int day);

            // A year already well inside what DateTime holds stands for itself, which is what almost every
            // date does: the proxy is then the date, and nothing here has cost anything.
            int cycles = year >= SettledFloor && year <= SettledCeiling
                ? 0
                : (int)FloorDiv(year - ProxyFloor, Cycle);

            DateTime proxy = new DateTime(year - (cycles * Cycle), month, day).AddTicks(moment.Tick);
            return new XdmDateTime(proxy, cycles, offset, type);
        }

        /// <summary>Writes the canonical lexical form.</summary>
        public override string ToString()
        {
            // The longest form is a dateTime with a nine-digit year, fractional seconds and an offset, so
            // this is written in one piece and never grows. Each part formats straight into the buffer
            // rather than being rendered to a string and copied in.
            CharStringBuilder builder = new CharStringBuilder(stackalloc char[48]);

            if (Type != XdmTypeCode.Time)
            {
                // The year is written from the value rather than by the proxy's own format, which would put
                // the stand-in year on the page. Four digits at least, and its own sign.
                int year = Year;

                if (year < 0)
                {
                    builder.Append('-');
                }

                builder.Append(Math.Abs(year), "0000");
                builder.Append('-');
                builder.Append(m_proxy, "MM-dd");
            }

            if (Type == XdmTypeCode.DateTime)
            {
                builder.Append('T');
            }

            if (Type != XdmTypeCode.Date)
            {
                builder.Append(m_proxy, "HH:mm:ss");

                // Fractional seconds appear only when there are any, and never with trailing zeros. The
                // format writes the point itself, there being no digit placeholder before it.
                long fraction = m_proxy.Ticks % TimeSpan.TicksPerSecond;
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
        public bool Equals(XdmDateTime other) => Instant.Equals(other.Instant) && Type == other.Type;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is XdmDateTime other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Instant.GetHashCode();
    }
}
