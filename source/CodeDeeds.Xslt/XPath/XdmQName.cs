namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// An <c>xs:QName</c>: a name in a namespace, together with the prefix it was written with.
    /// </summary>
    /// <remarks>
    /// The prefix is carried but takes no part in identity. Two QNames are the same name when their namespace
    /// and local part agree, however they were spelled — which is why <c>prefix-from-QName</c> exists at all:
    /// the spelling is recoverable but means nothing to a comparison.
    /// </remarks>
    /// <param name="Prefix">The prefix as written, empty where the name had none.</param>
    /// <param name="NamespaceUri">The namespace the prefix stood for.</param>
    /// <param name="LocalName">The local part.</param>
    public readonly record struct XdmQName(string Prefix, string NamespaceUri, string LocalName)
    {
        /// <summary>Returns the name as it would be written, with its prefix if it has one.</summary>
        public override string ToString()
        {
            return Prefix.Length == 0 ? LocalName : $"{Prefix}:{LocalName}";
        }

        /// <summary>Whether two QNames denote the same name, which the prefix has no part in.</summary>
        /// <param name="other">The name to compare with.</param>
        public bool Denotes(XdmQName other)
        {
            return string.Equals(NamespaceUri, other.NamespaceUri, StringComparison.Ordinal)
                && string.Equals(LocalName, other.LocalName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Splits a lexical QName into its prefix and local part, checking both are names.
        /// </summary>
        /// <param name="text">The name as written.</param>
        /// <param name="prefix">The prefix, or an empty string.</param>
        /// <param name="localName">The local part.</param>
        /// <returns><see langword="true"/> if the text is a lexical QName.</returns>
        public static bool TrySplit(string text, out string prefix, out string localName)
        {
            prefix = string.Empty;
            localName = text.Trim();

            int colon = localName.IndexOf(':');
            if (colon >= 0)
            {
                prefix = localName[..colon];
                localName = localName[(colon + 1)..];

                if (!IsNCName(prefix))
                {
                    return false;
                }
            }

            return IsNCName(localName);
        }

        private static bool IsNCName(string name)
        {
            if (name.Length == 0 || !System.Xml.XmlConvert.IsStartNCNameChar(name[0]))
            {
                return false;
            }

            for (int i = 1; i < name.Length; i++)
            {
                if (!System.Xml.XmlConvert.IsNCNameChar(name[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// A value of one of the binary types, <c>xs:hexBinary</c> and <c>xs:base64Binary</c>.
    /// </summary>
    /// <remarks>
    /// Held as the bytes rather than as the text, because the two types are two spellings of one value: the
    /// same bytes written as hex or as base 64 are equal, and casting between them is a re-spelling and not a
    /// conversion. Keeping the text would make <c>xs:hexBinary('FF')</c> and <c>xs:hexBinary('ff')</c> look
    /// different, which they are not.
    /// </remarks>
    public sealed class XdmBinary
    {
        /// <summary>Initializes a binary value.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <param name="type">Which of the two types spells them.</param>
        public XdmBinary(byte[] bytes, XdmTypeCode type)
        {
            Bytes = bytes;
            Type = type;
        }

        /// <summary>The bytes this value holds.</summary>
        public byte[] Bytes { get; }

        /// <summary>Which of the two binary types this is.</summary>
        public XdmTypeCode Type { get; }

        /// <summary>Reads the lexical form of either binary type.</summary>
        /// <param name="text">The text to read.</param>
        /// <param name="type">Which type's lexical form to expect.</param>
        /// <param name="result">On success, the value.</param>
        public static bool TryParse(string text, XdmTypeCode type, out XdmBinary? result)
        {
            result = null;
            string trimmed = text.Trim();

            if (type == XdmTypeCode.Base64Binary && !IsCanonicalBase64(trimmed))
            {
                return false;
            }

            try
            {
                byte[] bytes = type == XdmTypeCode.HexBinary
                    ? Convert.FromHexString(trimmed)
                    : Convert.FromBase64String(trimmed);

                result = new XdmBinary(bytes, type);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Whether base64 text has the padding bits XML Schema requires, which .NET does not check.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A final group of two characters carries one byte and a final group of three carries two, so the
        /// last character of each holds bits that no byte uses. XML Schema's lexical space admits only the
        /// spelling where those bits are zero: <c>AP8=</c> is two bytes and <c>AP9=</c> is not a value at
        /// all, because the two spellings would otherwise decode alike and one of them could never be
        /// written back out.
        /// </para>
        /// <para>
        /// <c>Convert.FromBase64String</c> accepts both and discards the surplus bits, so a value read that
        /// way would not survive a round trip through its own canonical form.
        /// </para>
        /// </remarks>
        private static bool IsCanonicalBase64(string text)
        {
            // Whitespace is permitted between the characters and is not part of the grouping.
            string packed = text.Length == 0 ? text : Compact(text);

            if (packed.Length % 4 != 0)
            {
                return false;
            }

            if (packed.EndsWith("==", StringComparison.Ordinal))
            {
                // One byte, which is eight bits: six from the first character of the group and two from the
                // second, whose remaining four must be zero.
                return (Sextet(packed[^3]) & 0x0F) == 0;
            }

            if (packed.EndsWith('='))
            {
                // Two bytes, sixteen bits: six, six, and four from the third character, whose remaining two
                // must be zero.
                return (Sextet(packed[^2]) & 0x03) == 0;
            }

            return true;
        }

        /// <summary>The six bits a base64 character stands for, or -1 if it stands for none.</summary>
        private static int Sextet(char character)
        {
            return character switch
            {
                >= 'A' and <= 'Z' => character - 'A',
                >= 'a' and <= 'z' => character - 'a' + 26,
                >= '0' and <= '9' => character - '0' + 52,
                '+' => 62,
                '/' => 63,
                _ => -1,
            };
        }

        private static string Compact(string text)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(text.Length);

            foreach (char character in text)
            {
                if (character is not (' ' or '\t' or '\n' or '\r'))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        /// <summary>Returns the value in its type's lexical form.</summary>
        public override string ToString()
        {
            return Type == XdmTypeCode.HexBinary
                ? Convert.ToHexString(Bytes)
                : Convert.ToBase64String(Bytes);
        }

        /// <summary>Whether two binary values hold the same bytes, whichever way each is spelled.</summary>
        /// <param name="other">The value to compare with.</param>
        public bool SameBytes(XdmBinary other)
        {
            return Bytes.AsSpan().SequenceEqual(other.Bytes);
        }

        /// <summary>
        /// Orders two binary values by their octets, which XPath 3.1 defines and 2.0 left undefined.
        /// </summary>
        /// <remarks>
        /// Octet by octet, unsigned, and a value that is a prefix of another comes first — the ordering of a
        /// dictionary, applied to bytes. Only ever asked of two values of one type: <c>xs:hexBinary</c> and
        /// <c>xs:base64Binary</c> are distinct types with no comparison between them, however alike their
        /// contents look.
        /// </remarks>
        /// <param name="other">The value to compare with.</param>
        /// <returns>Negative, zero or positive as this value sorts before, with, or after the other.</returns>
        public int CompareTo(XdmBinary other)
        {
            return Bytes.AsSpan().SequenceCompareTo(other.Bytes);
        }
    }
}
