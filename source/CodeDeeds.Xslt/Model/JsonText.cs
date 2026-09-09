using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// Reads the text of a JSON string, escapes and all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Utf8JsonReader.GetString"/> would do this, and refuses one thing JSON allows: an escape
    /// naming half of a surrogate pair. <c>"\uD834"</c> on its own is well-formed JSON and denotes a
    /// character that cannot exist, so the framework throws rather than answering — with an
    /// <see cref="InvalidOperationException"/>, which would leave a transformation as a framework fault
    /// rather than as something the specification has a name for.
    /// </para>
    /// <para>
    /// What the specification says to do is replace it. <c>fn:parse-json</c> substitutes U+FFFD, the
    /// replacement character, for anything the data model cannot hold — the same answer a text decoder gives
    /// for a byte sequence it cannot read, and for the same reason: one unrepresentable character should
    /// cost that character and not the document.
    /// </para>
    /// <para>
    /// The <c>FOJS0007</c> paths below are a guard rather than the usual route. A backslash that begins no
    /// escape is refused while the string token is being scanned, so it comes back as a syntax error before
    /// anything here sees it; these say what to do if that ever stops being true.
    /// </para>
    /// </remarks>
    internal static class JsonText
    {
        /// <summary>The character that stands for one that cannot be represented.</summary>
        private const char Replacement = '�';

        /// <summary>
        /// Returns the string a token denotes, replacing what cannot be represented.
        /// </summary>
        /// <param name="reader">The reader, positioned on a string or a property name.</param>
        /// <exception cref="XsltException"><c>FOJS0007</c> where a backslash begins no escape JSON defines.</exception>
        public static string Read(ref Utf8JsonReader reader)
        {
            // Nothing escaped means nothing to disagree about, and this is the overwhelmingly common case.
            if (!reader.ValueIsEscaped)
            {
                return reader.GetString() ?? string.Empty;
            }

            return Unescape(Encoding.UTF8.GetString(reader.ValueSpan));
        }

        /// <summary>Replaces the backslash escapes in the raw text of a JSON string.</summary>
        /// <param name="raw">The text between the quotes, as it was written.</param>
        private static string Unescape(string raw)
        {
            StringBuilder text = new StringBuilder(raw.Length);

            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] != '\\')
                {
                    text.Append(raw[i]);
                    continue;
                }

                i++;

                if (i >= raw.Length)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOJS0007, "A JSON string ends with a backslash, which escapes nothing.");
                }

                switch (raw[i])
                {
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '/': text.Append('/'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;

                    case 'u':
                        i = AppendCodepoint(raw, i, text);
                        break;

                    default:
                        throw XsltErrors.Error(
                            XsltErrorCode.FOJS0007,
                            $"'\\{raw[i]}' is not an escape JSON defines.");
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// Appends what a <c>\u</c> escape denotes, joining a surrogate pair where one follows.
        /// </summary>
        /// <param name="raw">The raw text.</param>
        /// <param name="at">The index of the <c>u</c>.</param>
        /// <param name="text">Where to append.</param>
        /// <returns>The index of the last character consumed.</returns>
        private static int AppendCodepoint(string raw, int at, StringBuilder text)
        {
            if (!TryHex(raw, at + 1, out int first))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOJS0007, "A '\\u' escape needs four hexadecimal digits after it.");
            }

            at += 4;

            if (!char.IsHighSurrogate((char)first))
            {
                // A low surrogate on its own is as unrepresentable as a high one with nothing after it.
                text.Append(char.IsLowSurrogate((char)first) ? Replacement : (char)first);
                return at;
            }

            // A high surrogate is only half a character, and the other half has to be the very next escape.
            // TryHex answers false past the end, so it is the only bound the digits need.
            if (at + 2 < raw.Length
                && raw[at + 1] == '\\' && raw[at + 2] == 'u'
                && TryHex(raw, at + 3, out int second)
                && char.IsLowSurrogate((char)second))
            {
                text.Append((char)first).Append((char)second);
                return at + 6;
            }

            text.Append(Replacement);
            return at;
        }

        private static bool TryHex(string raw, int at, out int value)
        {
            value = 0;

            if (at + 4 > raw.Length)
            {
                return false;
            }

            return int.TryParse(
                raw.AsSpan(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
    }
}
