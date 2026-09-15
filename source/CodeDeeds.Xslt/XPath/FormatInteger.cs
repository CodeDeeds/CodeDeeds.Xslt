using System.Globalization;
using System.Numerics;
using System.Text;
using CodeDeeds.Xslt.Compiler;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The numbering sequences a <c>fn:format-integer</c> picture can name.</summary>
    internal enum IntegerSequence : byte
    {
        /// <summary>Digits, in the family the picture was written with.</summary>
        Digits,

        /// <summary><c>a</c> — a, b, c, … z, aa, ab.</summary>
        AlphabeticLower,

        /// <summary><c>A</c> — A, B, C, … Z, AA, AB.</summary>
        AlphabeticUpper,

        /// <summary><c>i</c> — roman numerals in lower case.</summary>
        RomanLower,

        /// <summary><c>I</c> — roman numerals in upper case.</summary>
        RomanUpper,

        /// <summary><c>w</c> — words, in lower case.</summary>
        WordsLower,

        /// <summary><c>W</c> — words, in upper case.</summary>
        WordsUpper,

        /// <summary><c>Ww</c> — words, in title case.</summary>
        WordsTitle,
    }

    /// <summary>
    /// A <c>fn:format-integer</c> picture, read once and then able to render any number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A picture is a primary format token — <c>1</c>, <c>001</c>, <c>#,##0</c>, <c>a</c>, <c>i</c>,
    /// <c>Ww</c> — optionally followed by a semicolon and a modifier saying whether the number is cardinal
    /// (<c>c</c>, the default) or ordinal (<c>o</c>). The semicolon that separates them is the <em>last</em>
    /// one, because a semicolon is also a perfectly good grouping separator: <c>#;##1;</c> groups with
    /// semicolons and has no modifier at all.
    /// </para>
    /// <para>
    /// This is not the same language as the <c>format</c> attribute of <c>xsl:number</c>, which alternates
    /// tokens with literal separators so that one picture can render a whole sequence of numbers. Here there
    /// is one number, so every character belongs to it, and the punctuation between digits means grouping
    /// rather than separation. The two do meet at the individual sequences — <c>a</c>, <c>A</c>, <c>i</c>,
    /// <c>I</c> are rendered by the same code, through <see cref="NumberInstruction.FormatOne"/>.
    /// </para>
    /// </remarks>
    internal sealed class IntegerPicture
    {
        /// <summary>The sequence to render with.</summary>
        private readonly IntegerSequence m_sequence;

        /// <summary>Whether the modifier asked for ordinal numbering.</summary>
        private readonly bool m_ordinal;

        /// <summary>
        /// The variation the modifier named, which chooses a form within the language.
        /// </summary>
        /// <remarks>
        /// Only a language with more than one ordinal form reads it: German inflects an ordinal for what it
        /// stands before, so <c>o(-er)</c> and <c>o(-es)</c> ask for different words. English has one form
        /// and ignores this.
        /// </remarks>
        private readonly string? m_variation;

        /// <summary>The code point of the digit family's zero, for <see cref="IntegerSequence.Digits"/>.</summary>
        private readonly int m_zero;

        /// <summary>How many digits must appear, which is what a picture of <c>001</c> asks for.</summary>
        private readonly int m_mandatory;

        /// <summary>
        /// Where the grouping separators go, counted as the number of digits standing to their right, most
        /// distant first. Empty when the picture groups nothing.
        /// </summary>
        private readonly int[] m_positions;

        /// <summary>The separator code points, parallel to <see cref="m_positions"/>.</summary>
        private readonly int[] m_separators;

        /// <summary>
        /// The interval at which a regular picture repeats its separator leftwards, or zero when the
        /// separators sit only where the picture put them.
        /// </summary>
        private readonly int m_interval;

        private IntegerPicture(
            IntegerSequence sequence,
            bool ordinal,
            int zero,
            int mandatory,
            int[] positions,
            int[] separators,
            int interval,
            string? variation)
        {
            m_sequence = sequence;
            m_ordinal = ordinal;
            m_variation = variation;
            m_zero = zero;
            m_mandatory = mandatory;
            m_positions = positions;
            m_separators = separators;
            m_interval = interval;
        }

        /// <summary>The picture <c>1</c>, which is what an unrecognised primary format token falls back to.</summary>
        private static IntegerPicture Fallback(bool ordinal, string? variation)
        {
            return new IntegerPicture(
                IntegerSequence.Digits, ordinal, '0', 1, Array.Empty<int>(), Array.Empty<int>(), 0, variation);
        }

        /// <summary>Reads a picture.</summary>
        /// <param name="picture">The picture string.</param>
        /// <exception cref="XsltException">The picture is not a valid one — <c>FODF1310</c>.</exception>
        public static IntegerPicture Parse(string picture)
        {
            return Parse(picture, modifiers: true);
        }

        /// <summary>
        /// Parses a picture, optionally without the modifier a semicolon introduces.
        /// </summary>
        /// <param name="picture">The picture text.</param>
        /// <param name="modifiers">
        /// Whether a semicolon may introduce an ordinal modifier. False where the picture is the
        /// presentation modifier of a <c>format-date</c> component, which has no modifier of its own — so
        /// <c>[Y9;999]</c> groups by semicolons rather than asking for an ordinal year.
        /// </param>
        /// <param name="ordinal">
        /// Whether ordinal numbering was asked for outside the picture, as a <c>format-date</c> component's
        /// second presentation modifier asks: <c>[D1o]</c> is the day as <c>1st</c>.
        /// </param>
        /// <param name="variation">
        /// The ordinal variation asked for outside the picture, which is where the <c>ordinal</c> attribute
        /// of <c>xsl:number</c> puts it: that attribute is the variation and not merely a flag.
        /// </param>
        public static IntegerPicture Parse(
            string picture, bool modifiers, bool ordinal = false, string? variation = null)
        {
            // The last semicolon separates the token from the modifier, so that every earlier one is free to
            // be a grouping separator.
            int semicolon = modifiers ? picture.LastIndexOf(';') : -1;
            string token = semicolon < 0 ? picture : picture[..semicolon];
            ordinal |= ParseModifier(
                semicolon < 0 ? string.Empty : picture[(semicolon + 1)..], picture, out string? written);
            variation ??= written;

            if (token.Length == 0)
            {
                throw Invalid(picture, "it has no format token");
            }

            return HasDecimalDigit(token)
                ? ParseDigitPattern(token, ordinal, picture, variation)
                : ParseSequence(token, ordinal, variation);
        }

        /// <summary>
        /// Reads the modifier, which is <c>c</c> or <c>o</c> — optionally with a parenthesised variation —
        /// then optionally <c>a</c> or <c>t</c>.
        /// </summary>
        /// <remarks>
        /// The variation names a form within the language, such as <c>o(-er)</c> for German, and is handed
        /// back for the language to read: English has one ordinal form and ignores it.
        /// <c>a</c> and <c>t</c> choose between alphabetic and traditional numbering in languages that have
        /// both; neither language here does, so they are accepted and make no difference.
        /// </remarks>
        /// <param name="modifier">The text after the last semicolon.</param>
        /// <param name="picture">The whole picture, for the error message.</param>
        /// <param name="variation">The text inside the parentheses after <c>o</c>, if any.</param>
        /// <returns>Whether ordinal numbering was asked for.</returns>
        private static bool ParseModifier(string modifier, string picture, out string? variation)
        {
            int at = 0;
            bool ordinal = false;
            variation = null;

            if (at < modifier.Length && (modifier[at] == 'c' || modifier[at] == 'o'))
            {
                ordinal = modifier[at] == 'o';
                at++;

                if (ordinal && at < modifier.Length && modifier[at] == '(')
                {
                    int close = modifier.IndexOf(')', at);

                    if (close < 0)
                    {
                        throw Invalid(picture, "the variation after 'o' is not closed");
                    }

                    variation = modifier[(at + 1)..close];
                    at = close + 1;
                }
            }

            if (at < modifier.Length && (modifier[at] == 'a' || modifier[at] == 't'))
            {
                at++;
            }

            if (at != modifier.Length)
            {
                throw Invalid(picture, $"'{modifier}' is not a format modifier");
            }

            return ordinal;
        }

        /// <summary>Whether the token holds a decimal digit, which is what makes it a digit pattern.</summary>
        private static bool HasDecimalDigit(string token)
        {
            for (int at = 0; at < token.Length; at += char.IsSurrogatePair(token, at) ? 2 : 1)
            {
                if (CharUnicodeInfo.GetDecimalDigitValue(token, at) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Reads a token that names a numbering sequence rather than spelling out digits.</summary>
        /// <remarks>
        /// Anything not recognised is not an error. The specification leaves the set of sequences to the
        /// processor and says an unrecognised one falls back to <c>1</c>, so a stylesheet asking for circled
        /// digits or Kanji gets plain numbers rather than a message.
        /// </remarks>
        private static IntegerPicture ParseSequence(string token, bool ordinal, string? variation)
        {
            IntegerSequence? sequence = token switch
            {
                "a" => IntegerSequence.AlphabeticLower,
                "A" => IntegerSequence.AlphabeticUpper,
                "i" => IntegerSequence.RomanLower,
                "I" => IntegerSequence.RomanUpper,
                "w" => IntegerSequence.WordsLower,
                "W" => IntegerSequence.WordsUpper,
                "Ww" => IntegerSequence.WordsTitle,
                _ => null,
            };

            return sequence is IntegerSequence named
                ? new IntegerPicture(
                    named, ordinal, '0', 1, Array.Empty<int>(), Array.Empty<int>(), 0, variation)
                : Fallback(ordinal, variation);
        }

        /// <summary>Reads a decimal digit pattern such as <c>#,##0</c>.</summary>
        private static IntegerPicture ParseDigitPattern(
            string token, bool ordinal, string picture, string? variation)
        {
            List<int> positions = new List<int>();
            List<int> separators = new List<int>();

            int zero = -1;
            int mandatory = 0;
            int signs = 0;
            bool afterSeparator = false;

            for (int at = 0; at < token.Length;)
            {
                int length = char.IsSurrogatePair(token, at) ? 2 : 1;
                int digit = CharUnicodeInfo.GetDecimalDigitValue(token, at);
                int code = char.ConvertToUtf32(token, at);

                if (digit >= 0)
                {
                    // Every digit in the pattern says which family the output is written in, so they must all
                    // say the same thing: '123' and '١' are each a fine pattern and '123١' is not.
                    int family = code - digit;

                    if (zero < 0)
                    {
                        zero = family;
                    }
                    else if (zero != family)
                    {
                        throw Invalid(picture, "its digits are from more than one family");
                    }

                    mandatory++;
                    signs++;
                    afterSeparator = false;
                }
                else if (code == '#')
                {
                    // An optional digit says "no wider than this if you do not need to be", which only means
                    // anything in front of the digits that must appear.
                    if (mandatory > 0)
                    {
                        throw Invalid(picture, "'#' comes after a mandatory digit");
                    }

                    signs++;
                    afterSeparator = false;
                }
                else if (IsGroupingSeparator(code))
                {
                    if (signs == 0)
                    {
                        throw Invalid(picture, "it starts with a grouping separator");
                    }

                    if (afterSeparator)
                    {
                        throw Invalid(picture, "two grouping separators are adjacent");
                    }

                    positions.Add(signs);
                    separators.Add(code);
                    afterSeparator = true;
                }
                else
                {
                    throw Invalid(picture, $"'{token.Substring(at, length)}' does not belong in a digit pattern");
                }

                at += length;
            }

            if (afterSeparator)
            {
                throw Invalid(picture, "it ends with a grouping separator");
            }

            // Recorded left to right as digits seen so far; wanted as digits standing to the right, which is
            // the only way a position means the same thing for a number longer than the pattern.
            for (int i = 0; i < positions.Count; i++)
            {
                positions[i] = signs - positions[i];
            }

            return new IntegerPicture(
                IntegerSequence.Digits,
                ordinal,
                zero,
                mandatory,
                positions.ToArray(),
                separators.ToArray(),
                IntervalOf(positions, separators, signs),
                variation);
        }

        /// <summary>
        /// Works out whether the separators repeat, and at what interval.
        /// </summary>
        /// <remarks>
        /// The rule itself is <see cref="DigitGrouping"/>, which <c>fn:format-number</c> states in the same
        /// words. What is decided here first is the part only this function has: a picture may be written
        /// with several different separator characters, and separators that are not all the same character
        /// are irregular whatever their positions.
        /// </remarks>
        /// <param name="positions">Separator positions, counted as digits to their right.</param>
        /// <param name="separators">The separator code points.</param>
        /// <param name="signs">How many digit signs the pattern has in total.</param>
        /// <returns>The interval, or zero when the separators are irregular.</returns>
        private static int IntervalOf(List<int> positions, List<int> separators, int signs)
        {
            foreach (int separator in separators)
            {
                if (separator != separators[0])
                {
                    return 0;
                }
            }

            return DigitGrouping.IntervalOf(positions, signs);
        }

        /// <summary>
        /// Whether a character may separate groups of digits.
        /// </summary>
        /// <remarks>
        /// Punctuation and spaces, which is wide enough for the comma, the full stop, the space, the Armenian
        /// hyphen and the Aegean word separator, and narrow enough that <c>1o</c> is the mistake it looks
        /// like rather than a number grouped by the letter o.
        /// </remarks>
        private static bool IsGroupingSeparator(int code)
        {
            return CharUnicodeInfo.GetUnicodeCategory(code) is UnicodeCategory.ConnectorPunctuation
                or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation
                or UnicodeCategory.ClosePunctuation
                or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.OtherPunctuation
                or UnicodeCategory.SpaceSeparator;
        }

        private static XsltException Invalid(string picture, string why)
        {
            return XsltErrors.Error(
                XsltErrorCode.FODF1310, $"'{picture}' is not a format-integer picture, because {why}.");
        }

        /// <summary>Renders one number.</summary>
        /// <param name="value">The number.</param>
        /// <param name="language">The language to spell words in, if the picture asks for words.</param>
        public string Format(long value, string? language = null)
        {
            // The sign is settled here rather than inside each sequence, so that padding, grouping and the
            // ordinal suffix all apply to the number itself: -123 through '99999' is -00123, not 00-123.
            ulong magnitude = value < 0 ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
            string rendered = m_sequence switch
            {
                IntegerSequence.Digits => Digits(magnitude, language),
                IntegerSequence.WordsLower => Words(magnitude, language),
                IntegerSequence.WordsUpper => UpperCase(Words(magnitude, language)),
                IntegerSequence.WordsTitle => TitleCase(Words(magnitude, language)),
                _ => Lettered(magnitude),
            };

            return value < 0 ? "-" + rendered : rendered;
        }

        /// <summary>Renders one number wider than a 64-bit integer.</summary>
        /// <remarks>
        /// Only the digit sequences can present a number of this size. The alphabetic one would run to
        /// tens of millions of letters, the roman one stops at 4999 and words would fill pages; the
        /// specification's answer for a sequence that cannot render a value is the digits, which is what
        /// each of them falls back to here as it already does for the values it cannot reach.
        /// </remarks>
        /// <param name="value">The number.</param>
        /// <param name="language">The language to spell words in, if the picture asks for words.</param>
        public string Format(BigInteger value, string? language = null)
        {
            if (value >= long.MinValue && value <= long.MaxValue)
            {
                return Format((long)value, language);
            }

            BigInteger magnitude = BigInteger.Abs(value);
            string plain = magnitude.ToString(CultureInfo.InvariantCulture);

            // The ordinal suffix is decided by the last digits and by nothing further up, so the tail is
            // all that has to fit the counter the languages are written against.
            string rendered = m_sequence == IntegerSequence.Digits
                ? Digits(plain, (ulong)(magnitude % 1000), language)
                : plain;

            return value.Sign < 0 ? "-" + rendered : rendered;
        }

        /// <summary>
        /// The same picture with a regular grouping separator, which is what <c>xsl:number</c> asks for
        /// in its <c>grouping-separator</c> and <c>grouping-size</c> attributes rather than in a picture.
        /// </summary>
        /// <remarks>
        /// The attributes apply to a digit token whatever family the digits are from, and a picture that
        /// places its own separators has said where they go already.
        /// </remarks>
        /// <param name="size">How many digits go in a group; zero or less asks for none.</param>
        /// <param name="separator">The separator's code point, or a negative number for none.</param>
        public IntegerPicture Grouped(int size, int separator)
        {
            if (size <= 0 || separator < 0 || m_sequence != IntegerSequence.Digits
                || m_interval > 0 || m_positions.Length != 0)
            {
                return this;
            }

            return new IntegerPicture(
                m_sequence, m_ordinal, m_zero, m_mandatory, m_positions, new[] { separator }, size,
                m_variation);
        }

        /// <summary>Renders through the alphabetic or roman sequences.</summary>
        /// <remarks>
        /// Neither sequence has a way to write zero, and roman numerals stop at 4999. Both are asked anyway
        /// and both answer with plain digits when they cannot help, which is the fallback the specification
        /// asks for. The same is true above <see cref="int.MaxValue"/>, where the answer would be tens of
        /// millions of letters long.
        /// </remarks>
        private string Lettered(ulong magnitude)
        {
            if (magnitude > int.MaxValue)
            {
                return magnitude.ToString(CultureInfo.InvariantCulture);
            }

            char token = m_sequence switch
            {
                IntegerSequence.AlphabeticLower => 'a',
                IntegerSequence.AlphabeticUpper => 'A',
                IntegerSequence.RomanLower => 'i',
                _ => 'I',
            };

            bool alphabetic = m_sequence is IntegerSequence.AlphabeticLower or IntegerSequence.AlphabeticUpper;
            return NumberInstruction.FormatOne((int)magnitude, stackalloc char[] { token }, null, 0, alphabetic);
        }

        /// <summary>Renders as digits of the picture's family, padded and grouped as the picture asks.</summary>
        private string Digits(ulong magnitude, string? language)
        {
            return Digits(magnitude.ToString(CultureInfo.InvariantCulture), magnitude, language);
        }

        /// <summary>Renders digits already written out, which is the one thing a wide value shares.</summary>
        /// <param name="plain">The magnitude in Latin digits, with no sign.</param>
        /// <param name="ordinal">The magnitude, or its last digits, for the ordinal suffix.</param>
        /// <param name="language">The language the suffix is spelled in.</param>
        private string Digits(string plain, ulong ordinal, string? language)
        {
            int width = Math.Max(plain.Length, m_mandatory);
            StringBuilder builder = new StringBuilder(width + width);

            for (int i = 0; i < width; i++)
            {
                // Position counted from the right, so that a separator the picture put after three digits
                // stays after three digits however much longer the number is than the picture.
                int position = width - i;

                if (i > 0 && SeparatorAt(position) is int separator)
                {
                    AppendCodePoint(builder, separator);
                }

                int leading = width - plain.Length;
                AppendCodePoint(builder, m_zero + (i < leading ? 0 : plain[i - leading] - '0'));
            }

            if (!m_ordinal)
            {
                return builder.ToString();
            }

            // An ordinal written in digits is a suffix in English and a full stop in German: 3rd against 3.
            return builder.Append(Languages.Words(language).OrdinalSuffix(ordinal, m_variation)).ToString();
        }

        /// <summary>The separator that belongs this many digits from the right, if any.</summary>
        private int? SeparatorAt(int position)
        {
            if (m_interval > 0)
            {
                return position % m_interval == 0 ? m_separators[0] : null;
            }

            for (int i = 0; i < m_positions.Length; i++)
            {
                if (m_positions[i] == position)
                {
                    return m_separators[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Renders in words, cardinal or ordinal, in the language asked for — or in English, where the
        /// language is one this engine does not spell.
        /// </summary>
        private string Words(ulong magnitude, string? language)
        {
            LanguageWords words = Languages.Words(language);
            return m_ordinal ? words.Ordinal(magnitude, m_variation) : words.Cardinal(magnitude);
        }

        /// <summary>
        /// Raises words to upper case, which German needs more of than the invariant mapping gives.
        /// </summary>
        /// <remarks>
        /// The upper case of eszett is a pair of S, and .NET's simple case mapping leaves the letter alone —
        /// so without this the upper-case thirtieth would keep a lower-case letter in the middle of it.
        /// Nothing else either language spells needs a mapping the invariant one does not have.
        /// </remarks>
        private static string UpperCase(string words)
        {
            return words.Replace("ß", "SS", StringComparison.Ordinal).ToUpperInvariant();
        }

        /// <summary>
        /// Capitalises every word, counting a hyphen as a word boundary so that <c>Twenty-First</c> comes out
        /// the way a heading would be written.
        /// </summary>
        private static string TitleCase(string words)
        {
            StringBuilder builder = new StringBuilder(words);

            for (int i = 0; i < builder.Length; i++)
            {
                if (i == 0 || builder[i - 1] == ' ' || builder[i - 1] == '-')
                {
                    builder[i] = char.ToUpperInvariant(builder[i]);
                }
            }

            return builder.ToString();
        }

        /// <summary>Appends a code point, which the digit families and separators may need a pair for.</summary>
        private static void AppendCodePoint(StringBuilder builder, int code)
        {
            if (code <= 0xFFFF)
            {
                builder.Append((char)code);
                return;
            }

            int offset = code - 0x10000;
            builder.Append((char)(0xD800 + (offset >> 10))).Append((char)(0xDC00 + (offset & 0x3FF)));
        }
    }

    /// <summary>Numbers written out in English words.</summary>
    /// <remarks>
    /// <para>
    /// English is what a request for a language this engine does not spell falls back to; the languages it
    /// does spell are found through <see cref="Languages"/>. <c>fn:format-integer</c> asks for exactly that:
    /// a processor that does not have the language wanted uses one it does have, and must not raise an error
    /// over it.
    /// </para>
    /// <para>
    /// The dialect is the one the specification's own examples use — <c>one hundred and twenty-three</c>,
    /// with the <c>and</c> — and groups above a thousand are joined by a space, with <c>and</c> reappearing
    /// before a final remainder under a hundred: <c>one million and one</c>.
    /// </para>
    /// </remarks>
    internal static class EnglishNumbers
    {
        private static readonly string[] s_units =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
            "nineteen",
        };

        private static readonly string[] s_tens =
        {
            string.Empty, string.Empty, "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty",
            "ninety",
        };

        /// <summary>The scales, smallest first, each a thousand times the one before.</summary>
        private static readonly string[] s_scales =
        {
            "thousand", "million", "billion", "trillion", "quadrillion", "quintillion",
        };

        /// <summary>Writes a number out in lower-case words.</summary>
        /// <param name="value">The number.</param>
        public static string Cardinal(ulong value)
        {
            if (value < 20)
            {
                return s_units[value];
            }

            StringBuilder builder = new StringBuilder();
            ulong scale = 1_000_000_000_000_000_000UL;

            for (int i = s_scales.Length - 1; i >= 0; i--, scale /= 1000UL)
            {
                ulong part = value / scale;

                if (part == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                AppendUnderThousand(builder, (int)part);
                builder.Append(' ').Append(s_scales[i]);
                value -= part * scale;
            }

            if (value > 0)
            {
                // A remainder under a hundred reads as part of what came before it — one million and one —
                // while a larger one starts a phrase of its own.
                if (builder.Length > 0)
                {
                    builder.Append(value < 100 ? " and " : " ");
                }

                AppendUnderThousand(builder, (int)value);
            }

            return builder.ToString();
        }

        /// <summary>Writes a number out as a lower-case ordinal.</summary>
        /// <remarks>
        /// Only the last word changes: <c>one hundred and twenty-first</c>, <c>one thousandth</c>. So the
        /// cardinal is written first and its final word replaced, which also settles the hyphenated ones
        /// without treating them specially.
        /// </remarks>
        /// <param name="value">The number.</param>
        public static string Ordinal(ulong value)
        {
            string cardinal = Cardinal(value);
            int last = cardinal.LastIndexOfAny(new[] { ' ', '-' }) + 1;
            return string.Concat(cardinal.AsSpan(0, last), OrdinalWord(cardinal[last..]));
        }

        /// <summary>The suffix that turns digits into an ordinal — <c>1st</c>, <c>12th</c>, <c>23rd</c>.</summary>
        /// <param name="value">The number.</param>
        public static string OrdinalSuffix(ulong value)
        {
            // Eleven, twelve and thirteen take "th" against what their last digit would suggest, and so does
            // every number ending in them.
            if (value % 100 is >= 11 and <= 13)
            {
                return "th";
            }

            return (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };
        }

        private static string OrdinalWord(string word)
        {
            switch (word)
            {
                case "one": return "first";
                case "two": return "second";
                case "three": return "third";
                case "five": return "fifth";
                case "eight": return "eighth";
                case "nine": return "ninth";
                case "twelve": return "twelfth";
            }

            // The tens all end in y and all become -ieth; everything else that is left takes -th, including
            // zero, the teens, and the scales.
            return word.EndsWith('y') ? string.Concat(word.AsSpan(0, word.Length - 1), "ieth") : word + "th";
        }

        private static void AppendUnderThousand(StringBuilder builder, int value)
        {
            if (value >= 100)
            {
                builder.Append(s_units[value / 100]).Append(" hundred");
                value %= 100;

                if (value == 0)
                {
                    return;
                }

                builder.Append(" and ");
            }

            if (value < 20)
            {
                builder.Append(s_units[value]);
                return;
            }

            builder.Append(s_tens[value / 10]);

            if (value % 10 != 0)
            {
                builder.Append('-').Append(s_units[value % 10]);
            }
        }
    }

    /// <summary><c>fn:format-integer</c>.</summary>
    /// <remarks>
    /// The picture is parsed at compile time when it is a literal, which it nearly always is, and once per
    /// call otherwise. A literal picture that will not parse is <em>not</em> refused at compile time:
    /// <c>FODF1310</c> is a dynamic error, so an invalid picture in a branch that never runs must not stop
    /// the expression compiling. It is simply left to evaluation, which reads it again and raises there.
    /// </remarks>
    internal sealed class FormatIntegerExpr : Expr
    {
        private readonly Expr[] m_arguments;
        private readonly IntegerPicture? m_picture;

        private FormatIntegerExpr(Expr[] arguments, IntegerPicture? picture)
        {
            m_arguments = arguments;
            m_picture = picture;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <summary>Creates the call.</summary>
        /// <param name="arguments">The compiled arguments.</param>
        /// <param name="version">The version in force.</param>
        /// <exception cref="XsltException">The argument count is wrong.</exception>
        public static Expr Create(Expr[] arguments, XsltVersion version)
        {
            if (arguments.Length is < 2 or > 3)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'format-integer()' takes two or three arguments, and was given {arguments.Length}.");
            }

            IntegerPicture? picture = null;

            if (arguments[1] is StringLiteralExpr literal)
            {
                try
                {
                    picture = IntegerPicture.Parse(literal.Value);
                }
                catch (XsltException)
                {
                    // Left for evaluation to raise, so that the error is dynamic as specified.
                }
            }

            FunctionParameter[] signature = FunctionParameter.Parse(
                arguments.Length == 2 ? "xs:integer?, xs:string" : "xs:integer?, xs:string, xs:string?");

            return new FormatIntegerExpr(
                CheckedArgumentExpr.Wrap(arguments, signature, "format-integer", version), picture);
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue value = m_arguments[0].Evaluate(ref context);
            string text = m_arguments[1].Evaluate(ref context).ToStringValue();

            string? language = null;

            if (m_arguments.Length > 2)
            {
                // Always evaluated, whether or not the picture asks for words: a missing context item or a
                // type error in the argument has to be raised even where the language makes no difference.
                XPathValue named = m_arguments[2].Evaluate(ref context);

                if (!Xpath2FunctionExpr.IsEmptySequence(named))
                {
                    language = named.ToStringValue();
                }
            }

            IntegerPicture picture = m_picture ?? IntegerPicture.Parse(text);

            return Xpath2FunctionExpr.IsEmptySequence(value)
                ? XPathValue.FromString(string.Empty)
                : XPathValue.FromString(picture.Format(value.ToBigInteger(), language));
        }
    }
}
