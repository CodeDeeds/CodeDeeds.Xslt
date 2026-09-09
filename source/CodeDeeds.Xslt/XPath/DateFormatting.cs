using System.Globalization;
using System.Text;
using CodeDeeds.Xslt.Compiler;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// The picture-string formatting behind <c>format-dateTime</c>, <c>format-date</c> and
    /// <c>format-time</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A picture is literal text with markers in brackets: <c>[Y0001]-[M01]-[D01]</c>. Each marker names a
    /// component of the value and says how to present it, so the same value can be written as
    /// <c>2026-08-23</c> or <c>Sunday, 23 August</c> without the stylesheet taking it apart by hand.
    /// </para>
    /// <para>
    /// The numeric presentations are the ones <c>xsl:number</c> already formats — decimal with padding, roman
    /// numerals, letter sequences — so they are formatted by the same code. Only the names are new, and they
    /// are English: a call asking for another language is answered in English and says so, with the
    /// <c>[Language: en]</c> prefix the specification asks for (§9.8.4.8), and a calendar other than the
    /// Gregorian one is answered likewise with <c>[Calendar: AD]</c>.
    /// </para>
    /// </remarks>
    internal static class DateFormatting
    {
        private static readonly string[] s_months =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December",
        };

        private static readonly string[] s_days =
        {
            "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        };

        /// <summary>
        /// The conventional short forms, used before a name is cut to fit a width (§9.8.4.2).
        /// </summary>
        /// <remarks>
        /// A width narrower than the full name asks for an abbreviation, and the conventional one comes
        /// before the first few letters: <c>[FNn,3-5]</c> is <c>Thurs</c> and <c>[MNn,3-4]</c> is
        /// <c>Sept</c>. Where even the abbreviation is too long, it is cut, so <c>[FNn,3-4]</c> is
        /// <c>Thur</c> and <c>[MNn,3-3]</c> is <c>Sep</c>.
        /// </remarks>
        private static readonly string[] s_monthAbbreviations =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "June", "July", "Aug", "Sept", "Oct", "Nov", "Dec",
        };

        private static readonly string[] s_dayAbbreviations =
        {
            "Mon", "Tues", "Weds", "Thurs", "Fri", "Sat", "Sun",
        };

        /// <summary>The components each function will answer for.</summary>
        /// <remarks>
        /// Asking a date what hour it is has no answer, and the specification makes it an error rather than a
        /// zero — a picture written for the wrong function is a mistake worth hearing about.
        /// </remarks>
        private const string DateComponents = "YMDdFWwEC";

        // No 'E' here: a time has no era, that being a property of the date it did not come with.
        private const string TimeComponents = "HhPmsfC";
        private const string ZoneComponents = "Zz";

        /// <summary>Formats a value against a picture.</summary>
        /// <param name="value">The date, time or dateTime to write.</param>
        /// <param name="picture">The picture string.</param>
        /// <param name="function">The function being called, for error messages and for which components fit.</param>
        /// <param name="language">The language asked for, or null where none was.</param>
        /// <param name="calendar">The calendar asked for, or null where none was.</param>
        /// <param name="roundsFraction">
        /// Whether the fractional seconds are rounded to the digits the picture has room for, as XSLT 2.0
        /// had it, rather than cut, as F&amp;O 3.1 has it.
        /// </param>
        public static string Format(
            XdmDateTime value,
            string picture,
            string function,
            string? language = null,
            string? calendar = null,
            bool roundsFraction = false)
        {
            string allowed = function switch
            {
                "format-date" => DateComponents + ZoneComponents,
                "format-time" => TimeComponents + ZoneComponents,
                _ => DateComponents + TimeComponents + ZoneComponents,
            };

            StringBuilder result = new StringBuilder(picture.Length);

            // The only names here are English and the only calendar the Gregorian one, so a call asking for
            // another is answered with these and told so in front of the answer, which is what the
            // specification asks of a fallback (§9.8.4.8). A language is English by its first subtag, so
            // 'en-GB' and 'EN' are answered without comment.
            if (language is not null && !IsEnglish(language))
            {
                result.Append("[Language: en]");
            }

            if (calendar is not null && calendar is not ("AD" or "ISO"))
            {
                result.Append("[Calendar: AD]");
            }

            // One buffer for every component to format its number into, held here so that the frame it
            // shapes is set up once per picture rather than once per component.
            Span<char> scratch = stackalloc char[64];

            for (int i = 0; i < picture.Length; i++)
            {
                char character = picture[i];

                if (character == ']')
                {
                    // A closing bracket only stands for itself when doubled; alone it has nothing to close.
                    if (i + 1 < picture.Length && picture[i + 1] == ']')
                    {
                        result.Append(']');
                        i++;
                        continue;
                    }

                    throw XsltErrors.Error(
                        XsltErrorCode.FOFD1340,
                        $"The picture '{picture}' has a ']' that closes nothing. Write ']]' for a literal one.");
                }

                if (character != '[')
                {
                    result.Append(character);
                    continue;
                }

                if (i + 1 < picture.Length && picture[i + 1] == '[')
                {
                    result.Append('[');
                    i++;
                    continue;
                }

                int end = picture.IndexOf(']', i + 1);
                if (end < 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOFD1340,
                        $"The picture '{picture}' opens a component with '[' and never closes it.");
                }

                AppendComponent(
                    result, value, picture.AsSpan(i + 1, end - i - 1), allowed, function, scratch, roundsFraction);
                i = end;
            }

            return result.ToString();
        }

        /// <summary>Writes one bracketed component.</summary>
        /// <remarks>
        /// Appends rather than returning, so that a component which is only being trimmed or padded to width
        /// does not build a string for the caller to copy and discard. What each component says is still a
        /// string, because a name is one and a number is formatted as one.
        /// </remarks>
        /// <param name="result">The text built so far.</param>
        /// <param name="value">The value being written.</param>
        /// <param name="written">The marker as it appears between the brackets.</param>
        /// <param name="allowed">The specifiers the calling function will answer for.</param>
        /// <param name="function">The function being called, for error messages.</param>
        /// <param name="scratch">Room for a number to be formatted into, supplied by the caller.</param>
        /// <param name="roundsFraction">Whether the fractional seconds are rounded rather than cut.</param>
        private static void AppendComponent(
            StringBuilder result,
            XdmDateTime value,
            ReadOnlySpan<char> written,
            string allowed,
            string function,
            Span<char> scratch,
            bool roundsFraction)
        {
            ReadOnlySpan<char> marker = WithoutWhitespace(written);

            if (marker.Length == 0)
            {
                throw XsltErrors.Error(XsltErrorCode.FOFD1340, "A component of the picture names nothing.");
            }

            char specifier = marker[0];

            // Two ways for a specifier to be wrong, and they are different complaints. '[bla]' and '[y]'
            // name no component of any date or time — the picture is malformed, which is FOFD1340. '[Y]' in
            // format-time names a component this language has and this value has not, which is FOFD1350.
            if (Specifiers.IndexOf(specifier) < 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOFD1340,
                    $"'{specifier}' names no component of a date or a time, so '[{written}]' is not a "
                    + "picture component.");
            }

            if (allowed.IndexOf(specifier) < 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOFD1350,
                    $"{function}() cannot write the '{specifier}' component, which the value it was given "
                    + "does not have.");
            }

            ReadModifiers(marker[1..], specifier, out ReadOnlySpan<char> presentation,
                out bool ordinal, out bool traditional, out bool presentationWritten, out int minimum, out int maximum);

            DateTime instant = value.Value;

            switch (specifier)
            {
                case 'Y':
                    Fit(
                        result,
                        Number(YearWithin(instant.Year, presentation, maximum), presentation, ordinal, scratch),
                        minimum,
                        maximum,
                        presentation);
                    return;

                case 'M':
                    Fit(
                        result,
                        IsName(presentation)
                            ? Name(Abbreviated(s_months, s_monthAbbreviations, instant.Month - 1, maximum), presentation)
                            : Number(instant.Month, presentation, ordinal, scratch),
                        minimum,
                        maximum,
                        presentation);
                    return;

                case 'D':
                    Fit(result, Number(instant.Day, presentation, ordinal, scratch), minimum, maximum, presentation);
                    return;

                case 'd':
                    Fit(result, Number(instant.DayOfYear, presentation, ordinal, scratch), minimum, maximum, presentation);
                    return;

                case 'F':
                {
                    // ISO numbering, where Monday is 1 — not the .NET enumeration, which starts on Sunday.
                    int day = ((int)instant.DayOfWeek + 6) % 7;
                    Fit(
                        result,
                        IsName(presentation)
                            ? Name(Abbreviated(s_days, s_dayAbbreviations, day, maximum), presentation)
                            : Number(day + 1, presentation, ordinal, scratch),
                        minimum,
                        maximum,
                        presentation);
                    return;
                }

                case 'W':
                    Fit(result, Number(WeekOfYear(instant), presentation, ordinal, scratch), minimum, maximum, presentation);
                    return;

                case 'w':
                    Fit(
                        result,
                        Number(((instant.Day - 1) / 7) + 1, presentation, ordinal, scratch),
                        minimum,
                        maximum,
                        presentation);
                    return;

                case 'H':
                    Fit(result, Number(instant.Hour, presentation, ordinal, scratch), minimum, maximum, presentation);
                    return;

                case 'h':
                {
                    int hour = instant.Hour % 12;
                    Fit(result, Number(hour == 0 ? 12 : hour, presentation, ordinal, scratch), minimum, maximum, presentation);
                    return;
                }

                case 'P':
                    Fit(result, Name(HalfDay(instant.Hour < 12, maximum), presentation), 0, maximum, presentation);
                    return;

                case 'm':
                    Fit(result, Number(instant.Minute, presentation, ordinal, scratch), minimum, maximum, presentation);
                    return;

                case 's':
                    Fit(result, Number(instant.Second, presentation, ordinal, scratch), minimum, maximum, presentation);
                    return;

                case 'f':
                    AppendFraction(result, instant, presentation, minimum, maximum, roundsFraction);
                    return;

                case 'E':
                    result.Append(instant.Year > 0 ? "AD" : "BC");
                    return;

                case 'C':
                    result.Append("ISO");
                    return;

                default:
                    AppendTimezone(
                        result, value, specifier, presentationWritten ? presentation : default, traditional, minimum);
                    return;
            }
        }

        /// <summary>
        /// The fractional seconds, without the trailing zeros that carry no information.
        /// </summary>
        /// <remarks>
        /// Its own method because it holds a stack buffer, which shapes the frame of whatever method it sits
        /// in; a component that most pictures never ask for should not charge the rest for that.
        /// </remarks>
        /// <summary>
        /// Writes the fractional seconds, which are a decimal fraction and not a number.
        /// </summary>
        /// <remarks>
        /// Everything about this component runs the other way from the rest. Its digits are significant from
        /// the left, so it pads and truncates on the <em>right</em>: <c>[f777]</c> writes .12 as
        /// <c>120</c> and <c>[f99]</c> writes .123 as <c>12</c>, cutting rather than rounding. Its picture
        /// bounds it as well as padding it — <c>[f99]</c> asks for two digits and gets two, where every other
        /// component would take the picture as a minimum. The exception is a picture of one digit and no
        /// optional sign, <c>[f1]</c>, which is the plain form and asks for no maximum at all. Cutting is
        /// F&amp;O 3.1's rule; XSLT 2.0 rounded, and a processor claiming 2.0 still does.
        /// <para>
        /// Where a width is written too, it widens rather than narrows: <c>[f111,2-2]</c> keeps the three
        /// digits its picture asked for.
        /// </para>
        /// </remarks>
        private static void AppendFraction(
            StringBuilder result,
            DateTime instant,
            ReadOnlySpan<char> presentation,
            int minimum,
            int maximum,
            bool rounds)
        {
            Span<char> ticks = stackalloc char[7];
            (instant.Ticks % TimeSpan.TicksPerSecond).TryFormat(
                ticks, out int length, "D7", CultureInfo.InvariantCulture);

            ReadOnlySpan<char> digits = ticks[..length].TrimEnd('0');

            int family = '0';
            int mandatory = 0;
            int optional = 0;
            Span<char> separators = stackalloc char[16];
            Span<int> separatorAt = stackalloc int[16];
            int separatorCount = 0;

            foreach (char character in presentation)
            {
                int digit = CharUnicodeInfo.GetDecimalDigitValue(character);

                if (digit >= 0)
                {
                    family = character - digit;
                    mandatory++;
                }
                else if (character == '#')
                {
                    optional++;
                }
                else if (separatorCount < separators.Length)
                {
                    separators[separatorCount] = character;
                    separatorAt[separatorCount] = mandatory + optional;
                    separatorCount++;
                }
            }

            mandatory = Math.Max(mandatory, 1);

            int least = Math.Max(mandatory, minimum);
            int most = maximum != int.MaxValue
                ? Math.Max(mandatory, maximum)
                : mandatory == 1 && optional == 0 ? int.MaxValue : mandatory + optional;

            // XSLT 2.0 rounded the fraction to the digits the picture has room for and F&O 3.1 cuts it, so a
            // 2.0 processor rounds: .456 written with [f,1-1] is 5 there and 4 here. A fraction that rounds
            // up past what the digits can hold stays at the nines rather than carrying into the seconds.
            if (rounds && digits.Length > most && most < 7)
            {
                long divisor = 1;
                long cap = 1;

                for (int i = most; i < 7; i++)
                {
                    divisor *= 10;
                }

                for (int i = 0; i < most; i++)
                {
                    cap *= 10;
                }

                long kept = Math.Min((instant.Ticks % TimeSpan.TicksPerSecond + divisor / 2) / divisor, cap - 1);
                string text = kept.ToString("D" + most.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                digits = text.AsSpan().TrimEnd('0');
            }

            if (digits.Length > most)
            {
                digits = digits[..most];
            }

            for (int i = 0; i < Math.Max(digits.Length, least); i++)
            {
                for (int s = 0; s < separatorCount; s++)
                {
                    if (separatorAt[s] == i && i > 0)
                    {
                        result.Append(separators[s]);
                    }
                }

                AppendCodePoint(result, family + (i < digits.Length ? digits[i] - '0' : 0));
            }
        }

        private static void AppendCodePoint(StringBuilder result, int code)
        {
            if (code > 0xFFFF)
            {
                result.Append(char.ConvertFromUtf32(code));
            }
            else
            {
                result.Append((char)code);
            }
        }

        /// <summary>
        /// Removes the whitespace a marker may carry for readability, which means nothing.
        /// </summary>
        /// <remarks>
        /// Nearly every marker is written without any, so the text is looked at before anything is built and
        /// the marker handed back as it came. Only a marker that really has whitespace pays to lose it.
        /// </remarks>
        private static ReadOnlySpan<char> WithoutWhitespace(ReadOnlySpan<char> marker)
        {
            foreach (char character in marker)
            {
                if (char.IsWhiteSpace(character))
                {
                    // The string this returns is what keeps the span alive: a span into the heap is a
                    // reference the collector follows, so it does not matter that nothing else holds it.
                    return Squeeze(marker);
                }
            }

            return marker;
        }

        private static string Squeeze(ReadOnlySpan<char> marker)
        {
            CharStringBuilder kept = new CharStringBuilder(stackalloc char[32]);

            foreach (char character in marker)
            {
                if (!char.IsWhiteSpace(character))
                {
                    kept.Append(character);
                }
            }

            return kept.ToString();
        }

        /// <summary>
        /// Splits a marker's modifiers into how to present the component and how wide to make it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default presentation is not the same for every component: minutes and seconds pad to two
        /// digits, because <c>10:5</c> is not a time anyone writes, while the day of the week and the
        /// half-of-day marker are names rather than numbers.
        /// </para>
        /// <para>
        /// A second presentation modifier may follow the first (§9.8.4.1): <c>o</c> for an ordinal, <c>c</c>
        /// for a cardinal, <c>a</c> and <c>t</c> for alphabetic and traditional numbering. It is the last
        /// character of the presentation when that can be one and something stands before it — <c>[D1o]</c>
        /// is the day as an ordinal, <c>[Dwo]</c> the day in ordinal words, and a lone <c>[Do]</c> a first
        /// modifier that names nothing. English has one form of each number, so only <c>o</c> changes what
        /// is written.
        /// </para>
        /// </remarks>
        private static void ReadModifiers(
            ReadOnlySpan<char> modifiers,
            char specifier,
            out ReadOnlySpan<char> presentation,
            out bool ordinal,
            out bool traditional,
            out bool written,
            out int minimum,
            out int maximum)
        {
            // The comma that introduces the width is the last one that has a width behind it, because a
            // comma is also a perfectly good grouping separator: '[Y9,999,*]' groups by thousands and asks
            // for no maximum, and splitting at the first comma reads the grouping as the width.
            int comma = -1;

            for (int i = modifiers.Length - 1; i >= 0; i--)
            {
                if (modifiers[i] == ',' && IsWidth(modifiers[(i + 1)..]))
                {
                    comma = i;
                    break;
                }
            }

            presentation = comma < 0 ? modifiers : modifiers[..comma];
            ReadOnlySpan<char> width = comma < 0 ? default : modifiers[(comma + 1)..];
            ordinal = false;
            traditional = false;

            if (presentation.Length > 1 && presentation[^1] is 'a' or 't' or 'c' or 'o')
            {
                ordinal = presentation[^1] == 'o';
                traditional = presentation[^1] == 't';
                presentation = presentation[..^1];
            }

            // The timezone is the one component that tells an absent modifier from a default one.
            written = presentation.Length != 0;

            if (presentation.Length == 0)
            {
                // Literals, so the span points at text that outlives every call.
                presentation = specifier switch
                {
                    'm' or 's' => "01".AsSpan(),
                    'F' or 'P' or 'E' or 'C' => "n".AsSpan(),
                    _ => "1".AsSpan(),
                };
            }

            minimum = 0;
            maximum = int.MaxValue;

            if (width.Length != 0)
            {
                int dash = width.IndexOf('-');
                ReadOnlySpan<char> first = dash < 0 ? width : width[..dash];
                ReadOnlySpan<char> second = dash < 0 ? default : width[(dash + 1)..];

                minimum = IsUnbounded(first) ? 0 : ParseWidth(first);
                maximum = IsUnbounded(second) ? int.MaxValue : ParseWidth(second);

                // A width of nothing is not a width, and a maximum below the minimum asks for a component
                // both wider and narrower than itself.
                if ((!IsUnbounded(first) && minimum < 1) || maximum < minimum || maximum < 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOFD1340,
                        $"'{width}' is not a width a picture component can have: a component is at least "
                        + "one character wide, and no narrower than it is wide.");
                }
            }

            ValidateDigits(presentation, specifier);
        }

        /// <summary>The component specifiers this language has, whatever the value being written.</summary>
        private const string Specifiers = "YMDdFWwHhPmsfZzCE";

        /// <summary>
        /// Refuses a decimal digit pattern the picture language has no reading for.
        /// </summary>
        /// <remarks>
        /// The optional-digit signs stand on the side the component is padded from, which is the left for
        /// every component but the fractional seconds and the right for that one: <c>[H9#]</c> and
        /// <c>[f#99]</c> are each the other's mistake. All the digits come from one family, so <c>[f#9٠]</c>
        /// names two and is neither. And something has to appear, so a pattern of optional digits alone is
        /// no pattern.
        /// </remarks>
        private static void ValidateDigits(ReadOnlySpan<char> presentation, char specifier)
        {
            int family = -1;
            bool mandatory = false;
            bool optional = false;
            bool optionalAfterMandatory = false;
            bool mandatoryAfterOptional = false;

            foreach (char character in presentation)
            {
                int digit = CharUnicodeInfo.GetDecimalDigitValue(character);

                if (digit >= 0)
                {
                    int zero = character - digit;

                    if (family >= 0 && family != zero)
                    {
                        throw Malformed(presentation, "its digits come from more than one family");
                    }

                    family = zero;
                    mandatoryAfterOptional |= optional;
                    mandatory = true;
                }
                else if (character == '#')
                {
                    optionalAfterMandatory |= mandatory;
                    optional = true;
                }
                else if (char.IsAsciiLetter(character))
                {
                    // A named or alphabetic presentation — 'Nn', 'i', 'w' — which is not a digit pattern.
                    return;
                }
            }

            if (!mandatory)
            {
                if (optional)
                {
                    throw Malformed(presentation, "no digit in it has to appear");
                }

                return;
            }

            if (specifier == 'f' ? mandatoryAfterOptional : optionalAfterMandatory)
            {
                throw Malformed(
                    presentation,
                    specifier == 'f'
                        ? "the fractional seconds are padded on the right, so a digit that has to appear "
                            + "cannot follow one that need not"
                        : "a component is padded on the left, so a digit that need not appear cannot "
                            + "follow one that has to");
            }
        }

        private static XsltException Malformed(ReadOnlySpan<char> presentation, string why)
        {
            return XsltErrors.Error(
                XsltErrorCode.FOFD1340, $"'{presentation}' is not a picture component, because {why}.");
        }

        /// <summary>
        /// Whether text is a width: one or two bounds, each a number or a star, separated by a hyphen.
        /// </summary>
        private static bool IsWidth(ReadOnlySpan<char> text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            int dash = text.IndexOf('-');

            return IsBound(dash < 0 ? text : text[..dash])
                && (dash < 0 || IsBound(text[(dash + 1)..]));
        }

        private static bool IsBound(ReadOnlySpan<char> text)
        {
            if (text.Length == 1 && text[0] == '*')
            {
                return true;
            }

            foreach (char character in text)
            {
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }
            }

            return text.Length > 0;
        }

        /// <summary>Whether a width is absent or written as <c>*</c>, both of which mean "no limit".</summary>
        private static bool IsUnbounded(ReadOnlySpan<char> width)
        {
            return width.Length == 0 || (width.Length == 1 && width[0] == '*');
        }

        private static int ParseWidth(ReadOnlySpan<char> text)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int width)
                ? width
                : throw XsltErrors.Error(
                    XsltErrorCode.FOFD1340, $"'{text}' is not a width a picture component can have.");
        }

        private static bool IsName(ReadOnlySpan<char> presentation)
        {
            return presentation.Length switch
            {
                1 => presentation[0] is 'N' or 'n',
                2 => presentation[0] == 'N' && presentation[1] == 'n',
                _ => false,
            };
        }

        /// <summary>
        /// A name, or its conventional short form where the name is longer than the width allows.
        /// </summary>
        private static string Abbreviated(string[] names, string[] abbreviations, int index, int maximum)
        {
            return names[index].Length > maximum ? abbreviations[index] : names[index];
        }

        /// <summary>Whether a language tag names English, by its first subtag.</summary>
        private static bool IsEnglish(string language)
        {
            int dash = language.IndexOf('-');
            ReadOnlySpan<char> primary = dash < 0 ? language : language.AsSpan(0, dash);
            return primary.Equals("en", StringComparison.OrdinalIgnoreCase);
        }

        private static string Name(string name, ReadOnlySpan<char> presentation)
        {
            if (presentation.Length == 1)
            {
                if (presentation[0] == 'N')
                {
                    return name.ToUpperInvariant();
                }

                if (presentation[0] == 'n')
                {
                    return name.ToLowerInvariant();
                }
            }

            // 'Nn' is title case, which is the first letter of each word: 'a.m.' becomes 'A.M.', the stops
            // between the letters being what makes each of them a word.
            if (presentation.Length == 2 && presentation[0] == 'N' && presentation[1] == 'n')
            {
                char[] letters = name.ToLowerInvariant().ToCharArray();
                bool starting = true;

                for (int i = 0; i < letters.Length; i++)
                {
                    if (!char.IsLetter(letters[i]))
                    {
                        starting = true;
                        continue;
                    }

                    if (starting)
                    {
                        letters[i] = char.ToUpperInvariant(letters[i]);
                    }

                    starting = false;
                }

                return new string(letters);
            }

            return name;
        }

        /// <summary>
        /// Renders a component's number, into the caller's buffer where it fits.
        /// </summary>
        /// <param name="value">The number to render.</param>
        /// <param name="presentation">The token saying how to render it.</param>
        /// <param name="scratch">Room to write into, which the returned span points at.</param>
        /// <summary>
        /// Writes a numeric component through its presentation modifier.
        /// </summary>
        /// <remarks>
        /// The modifier is a <c>format-integer</c> picture, which the specification says outright, so a
        /// component written '๐๑' is answered in Thai digits and one written '#,##0' is grouped — neither of
        /// which the <c>xsl:number</c> formatter this used to go through has any idea about. That formatter
        /// still answers the common pictures, so it stays as the fast path and this is the fallback.
        /// </remarks>
        private static ReadOnlySpan<char> Number(
            int value,
            ReadOnlySpan<char> presentation,
            bool ordinal,
            Span<char> scratch)
        {
            if (!ordinal
                && IsPlainDigits(presentation)
                && NumberInstruction.TryFormatOne(
                    value, presentation, null, 0, alphabetic: false, scratch, out int written))
            {
                return scratch[..written];
            }

            try
            {
                return IntegerPicture.Parse(presentation.ToString(), modifiers: false, ordinal).Format(value).AsSpan();
            }
            catch (XsltException)
            {
                // A picture format-integer will not read but xsl:number will — the alphabetic and roman
                // sequences among them, which reach here through the same modifier.
                return NumberInstruction.FormatOne(value, presentation, null, 0, alphabetic: false);
            }
        }

        /// <summary>Whether a presentation is Latin digits alone, which is what the fast path handles.</summary>
        private static bool IsPlainDigits(ReadOnlySpan<char> presentation)
        {
            foreach (char character in presentation)
            {
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }
            }

            return presentation.Length > 0;
        }

        /// <summary>Writes a component, padded or truncated to the width its marker asked for.</summary>
        /// <param name="result">The text built so far.</param>
        /// <param name="text">What the component says.</param>
        /// <param name="minimum">The width to pad up to.</param>
        /// <param name="maximum">The width to cut down to.</param>
        /// <param name="presentation">How the component is presented, which decides which end is cut.</param>
        private static void Fit(
            StringBuilder result,
            ReadOnlySpan<char> text,
            int minimum,
            int maximum,
            ReadOnlySpan<char> presentation)
        {
            bool name = IsName(presentation);
            bool digits = !name && HasDigit(presentation);

            if (text.Length > maximum && (name || digits))
            {
                // A name is shortened, which is how [MNn,3-3] asks for Jan; a number keeps its low-order
                // digits, which is how [M,1-1] asks for one. A sequence — roman numerals, letters, words — is
                // never cut, there being no meaningful way to (§9.8.4.3): [Yi,4-4] writes mmiii whole.
                text = name ? text[..maximum] : text[^maximum..];
            }

            if (text.Length >= minimum)
            {
                result.Append(text);
            }
            else if (digits)
            {
                // A number is padded on the left with zeros, a name or a sequence on the right with spaces
                // (§9.8.4.2, §9.8.4.3): '0023', 'May       ' and 'miv '.
                //
                // With the zero of the picture's own family, which is the whole point of writing the picture
                // in one: '[Y๐๐๐๑,10]' asks for ten Thai digits, and padding it with U+0030 would answer
                // half in one family and half in another.
                int zero = ZeroOf(presentation);

                for (int i = text.Length; i < minimum; i++)
                {
                    AppendCodePoint(result, zero);
                }

                result.Append(text);
            }
            else
            {
                result.Append(text).Append(' ', minimum - text.Length);
            }
        }

        /// <summary>
        /// The am/pm marker in the width it is asked for.
        /// </summary>
        /// <remarks>
        /// The marker has no one spelling, so the width modifier chooses between the spellings rather than
        /// padding or cutting one of them: <c>[PN]</c> is A.M. and <c>[PN,2-2]</c> is AM, where cutting
        /// would have left A. and padding <c>[PNn,3-3]</c> a trailing space. The minimum is therefore spent
        /// on the choice and not applied again afterwards; only a maximum below two still cuts, to A.
        /// </remarks>
        /// <param name="morning">Whether the time is before noon.</param>
        /// <param name="maximum">The width to stay within.</param>
        private static string HalfDay(bool morning, int maximum)
        {
            return maximum < 4 ? (morning ? "am" : "pm") : (morning ? "a.m." : "p.m.");
        }

        /// <summary>
        /// The zero of the digit family a presentation is written in, as a code point.
        /// </summary>
        /// <remarks>
        /// A picture's digits all come from one family — <see cref="ValidateDigits"/> refuses one that mixes
        /// them — so the first digit found settles it, and its own value subtracted from it is that family's
        /// zero. A pattern of optional signs alone, <c>#</c> and nothing else, has no family to read and
        /// pads in Latin as it renders in Latin.
        /// </remarks>
        /// <param name="presentation">The presentation modifier.</param>
        private static int ZeroOf(ReadOnlySpan<char> presentation)
        {
            foreach (char character in presentation)
            {
                int digit = CharUnicodeInfo.GetDecimalDigitValue(character);

                if (digit >= 0)
                {
                    return character - digit;
                }
            }

            return '0';
        }

        /// <summary>Whether a presentation is a decimal digit pattern: a digit of any family, or '#'.</summary>
        private static bool HasDigit(ReadOnlySpan<char> presentation)
        {
            foreach (char character in presentation)
            {
                // A surrogate can only be a digit of a supplementary family: no sequence is written with one.
                if (character == '#' || char.IsSurrogate(character)
                    || CharUnicodeInfo.GetDecimalDigitValue(character) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The year reduced to what the marker has room for (§9.8.4.4): modulo ten to the maximum width
        /// where one is given, else to the number of digit signs in a decimal pattern of two or more, else
        /// the whole year — so <c>[Y01]</c> is a two-digit year and <c>[Y]</c> is all of it.
        /// </summary>
        private static int YearWithin(int year, ReadOnlySpan<char> presentation, int maximum)
        {
            int digits = maximum;

            if (maximum == int.MaxValue)
            {
                string pattern = presentation.ToString();
                int signs = 0;

                for (int at = 0; at < pattern.Length; at += char.IsSurrogatePair(pattern, at) ? 2 : 1)
                {
                    if (pattern[at] == '#' || CharUnicodeInfo.GetDecimalDigitValue(pattern, at) >= 0)
                    {
                        signs++;
                    }
                }

                if (signs < 2)
                {
                    return year;
                }

                digits = signs;
            }

            if (digits >= 10)
            {
                return year;
            }

            int modulus = 1;

            for (int i = 0; i < digits; i++)
            {
                modulus *= 10;
            }

            return year % modulus;
        }

        private static int WeekOfYear(DateTime instant)
        {
            return ISOWeek.GetWeekOfYear(instant);
        }

        /// <summary>
        /// Writes the timezone, by the rules that override every other component's (§9.8.4.6).
        /// </summary>
        /// <remarks>
        /// The first presentation modifier says the shape. One or two digits is the hours, with the minutes
        /// only where there are any (<c>-5</c>, <c>+10:30</c>); digits with a separator is hours and
        /// minutes always (<c>+5:00</c>); three or four digits is both with nothing between (<c>-0500</c>);
        /// <c>Z</c> is the military letter, where one exists; and <c>N</c>, a name, which this engine has
        /// none of, is the <c>+01:01</c> fallback the specification names for that case. No modifier at all
        /// is <c>+00:00</c>, as the specification's table has it — unless a width is written, in which case
        /// the hours take two digits and the minutes come where there are any, or always where the minimum
        /// width leaves room for them: <c>[z,2-2]</c> is <c>GMT-14</c> and <c>[z,6-6]</c> is
        /// <c>GMT-14:00</c>, which is what the suite expects and the nearest reading of XSLT 2.0 erratum E29,
        /// whose full representation was the hours with the minutes only where the offset had any. The
        /// second modifier <c>t</c> writes a zero offset as <c>Z</c>.
        /// </remarks>
        private static void AppendTimezone(
            StringBuilder result,
            XdmDateTime value,
            char specifier,
            ReadOnlySpan<char> presentation,
            bool traditional,
            int minimum)
        {
            bool military = presentation.Length == 1 && presentation[0] == 'Z';

            if (value.Offset is not TimeSpan offset)
            {
                // A value with no timezone has nothing to say about one — except in military form, where J
                // is the letter for local time.
                if (military)
                {
                    result.Append('J');
                }

                return;
            }

            bool negative = offset < TimeSpan.Zero;
            int hours = Math.Abs(offset.Hours);
            int minutes = Math.Abs(offset.Minutes);

            if (military && minutes == 0 && hours <= 12)
            {
                // Z is +00:00; A to M are +01:00 to +12:00 with J left out; N to Y are -01:00 to -12:00.
                result.Append(
                    hours == 0 ? 'Z'
                    : negative ? (char)('N' + hours - 1)
                    : hours < 10 ? (char)('A' + hours - 1)
                    : (char)('K' + hours - 10));
                return;
            }

            if (specifier == 'z')
            {
                result.Append("GMT");
            }

            if (traditional && offset == TimeSpan.Zero)
            {
                result.Append('Z');
                return;
            }

            // The shape: how many digits the hours take, what stands between them and the minutes, and
            // whether the minutes are written when there are none.
            int hourDigits = 2;
            char separator = ':';
            bool minutesAlways = true;

            if (presentation.Length == 0)
            {
                if (minimum > 0)
                {
                    hourDigits = Math.Min(minimum, 2);
                    minutesAlways = minimum > 3;
                }
            }
            else if (!military && presentation[0] != 'N')
            {
                int before = 0;
                int after = 0;
                separator = '\0';

                foreach (char character in presentation)
                {
                    if (character == '#' || CharUnicodeInfo.GetDecimalDigitValue(character) >= 0)
                    {
                        if (separator == '\0')
                        {
                            before++;
                        }
                        else
                        {
                            after++;
                        }
                    }
                    else if (separator == '\0')
                    {
                        separator = character;
                    }
                }

                if (separator != '\0')
                {
                    hourDigits = Math.Max(before, 1);
                }
                else if (before <= 2)
                {
                    hourDigits = Math.Max(before, 1);
                    separator = ':';
                    minutesAlways = false;
                }
                else
                {
                    hourDigits = before - 2;
                }
            }

            result.Append(negative ? '-' : '+');
            result.Append('0', Math.Max(0, hourDigits - (hours >= 10 ? 2 : 1)));
            result.Append(hours);

            if (minutesAlways || minutes != 0)
            {
                if (separator != '\0')
                {
                    result.Append(separator);
                }

                AppendTwoDigits(result, minutes);
            }
        }

        /// <summary>Writes a number below a hundred as exactly two digits.</summary>
        private static void AppendTwoDigits(StringBuilder result, int value)
        {
            result.Append((char)('0' + (value / 10)));
            result.Append((char)('0' + (value % 10)));
        }
    }
}
