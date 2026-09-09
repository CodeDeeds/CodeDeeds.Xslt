namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// The symbols a <c>format-number()</c> picture is written with and that its output uses, which
    /// <c>xsl:decimal-format</c> declares inside a stylesheet and
    /// <see cref="XPathStaticContext.DeclareDecimalFormat"/> declares outside one.
    /// </summary>
    /// <remarks>
    /// XPath 3.0 moved <c>fn:format-number</c> into the core library and made the decimal formats part of the
    /// static context, which is why this is a public type and not an XSLT-only one: a host evaluating a bare
    /// expression has the same right to declare them that a stylesheet has.
    /// <para>
    /// Every symbol is a code point rather than a <see cref="char"/>, because one may be named outside the
    /// basic plane — the QT3 suite declares a grouping separator of U+1EDB1 and a zero digit of U+104A0, and
    /// half a surrogate pair matches nothing.
    /// </para>
    /// </remarks>
    public sealed class DecimalFormat
    {
        /// <summary>The symbols in force where nothing declares otherwise.</summary>
        public static DecimalFormat Default { get; } = new DecimalFormat();

        /// <summary>The character separating the integer and fraction parts. Default <c>.</c>.</summary>
        public int DecimalSeparator { get; set; } = '.';

        /// <summary>The character separating groups of digits. Default <c>,</c>.</summary>
        public int GroupingSeparator { get; set; } = ',';

        /// <summary>
        /// The character separating a mantissa from its exponent, which XPath 3.1 added. Default <c>e</c>.
        /// </summary>
        public int ExponentSeparator { get; set; } = 'e';

        /// <summary>The string used for an infinite value. Default <c>Infinity</c>.</summary>
        public string Infinity { get; set; } = "Infinity";

        /// <summary>The character prefixing a negative value. Default <c>-</c>.</summary>
        public int MinusSign { get; set; } = '-';

        /// <summary>The string used for a value that is not a number. Default <c>NaN</c>.</summary>
        public string NaN { get; set; } = "NaN";

        /// <summary>The character marking a percentage in a picture. Default <c>%</c>.</summary>
        public int Percent { get; set; } = '%';

        /// <summary>The character marking a per-mille value in a picture. Default <c>‰</c>.</summary>
        public int PerMille { get; set; } = '‰';

        /// <summary>The character standing for zero, which also fixes the other nine digits.</summary>
        public int ZeroDigit { get; set; } = '0';

        /// <summary>The character standing for an optional digit in a picture. Default <c>#</c>.</summary>
        public int Digit { get; set; } = '#';

        /// <summary>The character separating the positive and negative sub-pictures. Default <c>;</c>.</summary>
        public int PatternSeparator { get; set; } = ';';

        /// <summary>
        /// Reads the name of a decimal format as an expression may write it, which is a lexical QName, an
        /// <c>EQName</c>, or nothing at all.
        /// </summary>
        /// <remarks>
        /// Formats are keyed by expanded name, so two prefixes bound to one namespace name the same format
        /// and one prefix rebound names a different one. Surrounding whitespace is not part of the name.
        /// </remarks>
        /// <param name="name">The name as written, or an empty string for the unnamed format.</param>
        /// <param name="resolvePrefix">Resolves a prefix as it is bound where the name is written.</param>
        /// <param name="resolved">On success, the expanded name.</param>
        /// <returns><see langword="true"/> if the name is well formed and its prefix is bound.</returns>
        public static bool TryReadName(
            string name, Func<string, string?> resolvePrefix, out ExpandedName resolved)
        {
            name = name.Trim();
            resolved = default;

            if (name.Length == 0)
            {
                resolved = new ExpandedName(string.Empty, string.Empty);
                return true;
            }

            if (name.StartsWith("Q{", StringComparison.Ordinal))
            {
                int close = name.IndexOf('}');

                if (close < 0)
                {
                    return false;
                }

                resolved = new ExpandedName(name[2..close], name[(close + 1)..]);
                return resolved.LocalName.Length > 0;
            }

            int colon = name.IndexOf(':');

            if (colon < 0)
            {
                resolved = new ExpandedName(string.Empty, name);
                return true;
            }

            string? uri = resolvePrefix(name[..colon]);

            if (uri is null)
            {
                return false;
            }

            resolved = new ExpandedName(uri, name[(colon + 1)..]);
            return resolved.LocalName.Length > 0;
        }
    }
}
