using System.Buffers;
using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Unicode's full case mappings, which <c>fn:upper-case</c> and <c>fn:lower-case</c> are defined
    /// against and which change the length of a string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// F&amp;O 5.4.7 asks for the mappings described in Unicode's <em>default case operations</em>, which are
    /// the full mappings without tailoring for any language. .NET's <c>ToUpperInvariant</c> applies the
    /// <em>simple</em> mappings, which are one character to one character and cannot say that
    /// <c>&#x00DF;</c> uppercases to <c>SS</c> or that <c>&#xFB03;</c> uppercases to <c>FFI</c>.
    /// </para>
    /// <para>
    /// What is here is the unconditional part of the Unicode Character Database's <c>SpecialCasing</c>
    /// table: every character whose full mapping is more than one character and does not depend on
    /// context. The conditional entries are either language-specific, which this function is defined not
    /// to be, or the Greek final sigma, which depends on what stands either side of it.
    /// </para>
    /// <para>
    /// Each replacement is text the simple mapping then leaves alone, so the two are applied in that
    /// order rather than woven together: substitute what the table names, then let .NET map the rest,
    /// which is also what keeps a surrogate pair a pair.
    /// </para>
    /// </remarks>
    internal static class CaseMapping
    {
        /// <summary>The characters whose full upper-case mapping is more than one character.</summary>
        private static readonly Dictionary<char, string> s_upper = new Dictionary<char, string>
        {
            ['\u00df'] = "\u0053\u0053", // LATIN SMALL LETTER SHARP S
            ['\ufb00'] = "\u0046\u0046", // LATIN SMALL LIGATURE FF
            ['\ufb01'] = "\u0046\u0049", // LATIN SMALL LIGATURE FI
            ['\ufb02'] = "\u0046\u004c", // LATIN SMALL LIGATURE FL
            ['\ufb03'] = "\u0046\u0046\u0049", // LATIN SMALL LIGATURE FFI
            ['\ufb04'] = "\u0046\u0046\u004c", // LATIN SMALL LIGATURE FFL
            ['\ufb05'] = "\u0053\u0054", // LATIN SMALL LIGATURE LONG S T
            ['\ufb06'] = "\u0053\u0054", // LATIN SMALL LIGATURE ST
            ['\u0587'] = "\u0535\u0552", // ARMENIAN SMALL LIGATURE ECH YIWN
            ['\ufb13'] = "\u0544\u0546", // ARMENIAN SMALL LIGATURE MEN NOW
            ['\ufb14'] = "\u0544\u0535", // ARMENIAN SMALL LIGATURE MEN ECH
            ['\ufb15'] = "\u0544\u053b", // ARMENIAN SMALL LIGATURE MEN INI
            ['\ufb16'] = "\u054e\u0546", // ARMENIAN SMALL LIGATURE VEW NOW
            ['\ufb17'] = "\u0544\u053d", // ARMENIAN SMALL LIGATURE MEN XEH
            ['\u0149'] = "\u02bc\u004e", // LATIN SMALL LETTER N PRECEDED BY APOSTROPHE
            ['\u0390'] = "\u0399\u0308\u0301", // GREEK SMALL LETTER IOTA WITH DIALYTIKA AND TONOS
            ['\u03b0'] = "\u03a5\u0308\u0301", // GREEK SMALL LETTER UPSILON WITH DIALYTIKA AND TONOS
            ['\u01f0'] = "\u004a\u030c", // LATIN SMALL LETTER J WITH CARON
            ['\u1e96'] = "\u0048\u0331", // LATIN SMALL LETTER H WITH LINE BELOW
            ['\u1e97'] = "\u0054\u0308", // LATIN SMALL LETTER T WITH DIAERESIS
            ['\u1e98'] = "\u0057\u030a", // LATIN SMALL LETTER W WITH RING ABOVE
            ['\u1e99'] = "\u0059\u030a", // LATIN SMALL LETTER Y WITH RING ABOVE
            ['\u1e9a'] = "\u0041\u02be", // LATIN SMALL LETTER A WITH RIGHT HALF RING
            ['\u1f50'] = "\u03a5\u0313", // GREEK SMALL LETTER UPSILON WITH PSILI
            ['\u1f52'] = "\u03a5\u0313\u0300", // GREEK SMALL LETTER UPSILON WITH PSILI AND VARIA
            ['\u1f54'] = "\u03a5\u0313\u0301", // GREEK SMALL LETTER UPSILON WITH PSILI AND OXIA
            ['\u1f56'] = "\u03a5\u0313\u0342", // GREEK SMALL LETTER UPSILON WITH PSILI AND PERISPOMENI
            ['\u1fb6'] = "\u0391\u0342", // GREEK SMALL LETTER ALPHA WITH PERISPOMENI
            ['\u1fc6'] = "\u0397\u0342", // GREEK SMALL LETTER ETA WITH PERISPOMENI
            ['\u1fd2'] = "\u0399\u0308\u0300", // GREEK SMALL LETTER IOTA WITH DIALYTIKA AND VARIA
            ['\u1fd3'] = "\u0399\u0308\u0301", // GREEK SMALL LETTER IOTA WITH DIALYTIKA AND OXIA
            ['\u1fd6'] = "\u0399\u0342", // GREEK SMALL LETTER IOTA WITH PERISPOMENI
            ['\u1fd7'] = "\u0399\u0308\u0342", // GREEK SMALL LETTER IOTA WITH DIALYTIKA AND PERISPOMENI
            ['\u1fe2'] = "\u03a5\u0308\u0300", // GREEK SMALL LETTER UPSILON WITH DIALYTIKA AND VARIA
            ['\u1fe3'] = "\u03a5\u0308\u0301", // GREEK SMALL LETTER UPSILON WITH DIALYTIKA AND OXIA
            ['\u1fe4'] = "\u03a1\u0313", // GREEK SMALL LETTER RHO WITH PSILI
            ['\u1fe6'] = "\u03a5\u0342", // GREEK SMALL LETTER UPSILON WITH PERISPOMENI
            ['\u1fe7'] = "\u03a5\u0308\u0342", // GREEK SMALL LETTER UPSILON WITH DIALYTIKA AND PERISPOMENI
            ['\u1ff6'] = "\u03a9\u0342", // GREEK SMALL LETTER OMEGA WITH PERISPOMENI
            ['\u1f80'] = "\u1f08\u0399", // GREEK SMALL LETTER ALPHA WITH PSILI AND YPOGEGRAMMENI
            ['\u1f81'] = "\u1f09\u0399", // GREEK SMALL LETTER ALPHA WITH DASIA AND YPOGEGRAMMENI
            ['\u1f82'] = "\u1f0a\u0399", // GREEK SMALL LETTER ALPHA WITH PSILI AND VARIA AND YPOGEGRAMMENI
            ['\u1f83'] = "\u1f0b\u0399", // GREEK SMALL LETTER ALPHA WITH DASIA AND VARIA AND YPOGEGRAMMENI
            ['\u1f84'] = "\u1f0c\u0399", // GREEK SMALL LETTER ALPHA WITH PSILI AND OXIA AND YPOGEGRAMMENI
            ['\u1f85'] = "\u1f0d\u0399", // GREEK SMALL LETTER ALPHA WITH DASIA AND OXIA AND YPOGEGRAMMENI
            ['\u1f86'] = "\u1f0e\u0399", // GREEK SMALL LETTER ALPHA WITH PSILI AND PERISPOMENI AND YPOGEGRAMMENI
            ['\u1f87'] = "\u1f0f\u0399", // GREEK SMALL LETTER ALPHA WITH DASIA AND PERISPOMENI AND YPOGEGRAMMENI
            ['\u1f88'] = "\u1f08\u0399", // GREEK CAPITAL LETTER ALPHA WITH PSILI AND PROSGEGRAMMENI
            ['\u1f89'] = "\u1f09\u0399", // GREEK CAPITAL LETTER ALPHA WITH DASIA AND PROSGEGRAMMENI
            ['\u1f8a'] = "\u1f0a\u0399", // GREEK CAPITAL LETTER ALPHA WITH PSILI AND VARIA AND PROSGEGRAMMENI
            ['\u1f8b'] = "\u1f0b\u0399", // GREEK CAPITAL LETTER ALPHA WITH DASIA AND VARIA AND PROSGEGRAMMENI
            ['\u1f8c'] = "\u1f0c\u0399", // GREEK CAPITAL LETTER ALPHA WITH PSILI AND OXIA AND PROSGEGRAMMENI
            ['\u1f8d'] = "\u1f0d\u0399", // GREEK CAPITAL LETTER ALPHA WITH DASIA AND OXIA AND PROSGEGRAMMENI
            ['\u1f8e'] = "\u1f0e\u0399", // GREEK CAPITAL LETTER ALPHA WITH PSILI AND PERISPOMENI AND PROSGEGRAMMENI
            ['\u1f8f'] = "\u1f0f\u0399", // GREEK CAPITAL LETTER ALPHA WITH DASIA AND PERISPOMENI AND PROSGEGRAMMENI
            ['\u1f90'] = "\u1f28\u0399", // GREEK SMALL LETTER ETA WITH PSILI AND YPOGEGRAMMENI
            ['\u1f91'] = "\u1f29\u0399", // GREEK SMALL LETTER ETA WITH DASIA AND YPOGEGRAMMENI
            ['\u1f92'] = "\u1f2a\u0399", // GREEK SMALL LETTER ETA WITH PSILI AND VARIA AND YPOGEGRAMMENI
            ['\u1f93'] = "\u1f2b\u0399", // GREEK SMALL LETTER ETA WITH DASIA AND VARIA AND YPOGEGRAMMENI
            ['\u1f94'] = "\u1f2c\u0399", // GREEK SMALL LETTER ETA WITH PSILI AND OXIA AND YPOGEGRAMMENI
            ['\u1f95'] = "\u1f2d\u0399", // GREEK SMALL LETTER ETA WITH DASIA AND OXIA AND YPOGEGRAMMENI
            ['\u1f96'] = "\u1f2e\u0399", // GREEK SMALL LETTER ETA WITH PSILI AND PERISPOMENI AND YPOGEGRAMMENI
            ['\u1f97'] = "\u1f2f\u0399", // GREEK SMALL LETTER ETA WITH DASIA AND PERISPOMENI AND YPOGEGRAMMENI
            ['\u1f98'] = "\u1f28\u0399", // GREEK CAPITAL LETTER ETA WITH PSILI AND PROSGEGRAMMENI
            ['\u1f99'] = "\u1f29\u0399", // GREEK CAPITAL LETTER ETA WITH DASIA AND PROSGEGRAMMENI
            ['\u1f9a'] = "\u1f2a\u0399", // GREEK CAPITAL LETTER ETA WITH PSILI AND VARIA AND PROSGEGRAMMENI
            ['\u1f9b'] = "\u1f2b\u0399", // GREEK CAPITAL LETTER ETA WITH DASIA AND VARIA AND PROSGEGRAMMENI
            ['\u1f9c'] = "\u1f2c\u0399", // GREEK CAPITAL LETTER ETA WITH PSILI AND OXIA AND PROSGEGRAMMENI
            ['\u1f9d'] = "\u1f2d\u0399", // GREEK CAPITAL LETTER ETA WITH DASIA AND OXIA AND PROSGEGRAMMENI
            ['\u1f9e'] = "\u1f2e\u0399", // GREEK CAPITAL LETTER ETA WITH PSILI AND PERISPOMENI AND PROSGEGRAMMENI
            ['\u1f9f'] = "\u1f2f\u0399", // GREEK CAPITAL LETTER ETA WITH DASIA AND PERISPOMENI AND PROSGEGRAMMENI
            ['\u1fa0'] = "\u1f68\u0399", // GREEK SMALL LETTER OMEGA WITH PSILI AND YPOGEGRAMMENI
            ['\u1fa1'] = "\u1f69\u0399", // GREEK SMALL LETTER OMEGA WITH DASIA AND YPOGEGRAMMENI
            ['\u1fa2'] = "\u1f6a\u0399", // GREEK SMALL LETTER OMEGA WITH PSILI AND VARIA AND YPOGEGRAMMENI
            ['\u1fa3'] = "\u1f6b\u0399", // GREEK SMALL LETTER OMEGA WITH DASIA AND VARIA AND YPOGEGRAMMENI
            ['\u1fa4'] = "\u1f6c\u0399", // GREEK SMALL LETTER OMEGA WITH PSILI AND OXIA AND YPOGEGRAMMENI
            ['\u1fa5'] = "\u1f6d\u0399", // GREEK SMALL LETTER OMEGA WITH DASIA AND OXIA AND YPOGEGRAMMENI
            ['\u1fa6'] = "\u1f6e\u0399", // GREEK SMALL LETTER OMEGA WITH PSILI AND PERISPOMENI AND YPOGEGRAMMENI
            ['\u1fa7'] = "\u1f6f\u0399", // GREEK SMALL LETTER OMEGA WITH DASIA AND PERISPOMENI AND YPOGEGRAMMENI
            ['\u1fa8'] = "\u1f68\u0399", // GREEK CAPITAL LETTER OMEGA WITH PSILI AND PROSGEGRAMMENI
            ['\u1fa9'] = "\u1f69\u0399", // GREEK CAPITAL LETTER OMEGA WITH DASIA AND PROSGEGRAMMENI
            ['\u1faa'] = "\u1f6a\u0399", // GREEK CAPITAL LETTER OMEGA WITH PSILI AND VARIA AND PROSGEGRAMMENI
            ['\u1fab'] = "\u1f6b\u0399", // GREEK CAPITAL LETTER OMEGA WITH DASIA AND VARIA AND PROSGEGRAMMENI
            ['\u1fac'] = "\u1f6c\u0399", // GREEK CAPITAL LETTER OMEGA WITH PSILI AND OXIA AND PROSGEGRAMMENI
            ['\u1fad'] = "\u1f6d\u0399", // GREEK CAPITAL LETTER OMEGA WITH DASIA AND OXIA AND PROSGEGRAMMENI
            ['\u1fae'] = "\u1f6e\u0399", // GREEK CAPITAL LETTER OMEGA WITH PSILI AND PERISPOMENI AND PROSGEGRAMMENI
            ['\u1faf'] = "\u1f6f\u0399", // GREEK CAPITAL LETTER OMEGA WITH DASIA AND PERISPOMENI AND PROSGEGRAMMENI
            ['\u1fb3'] = "\u0391\u0399", // GREEK SMALL LETTER ALPHA WITH YPOGEGRAMMENI
            ['\u1fbc'] = "\u0391\u0399", // GREEK CAPITAL LETTER ALPHA WITH PROSGEGRAMMENI
            ['\u1fc3'] = "\u0397\u0399", // GREEK SMALL LETTER ETA WITH YPOGEGRAMMENI
            ['\u1fcc'] = "\u0397\u0399", // GREEK CAPITAL LETTER ETA WITH PROSGEGRAMMENI
            ['\u1ff3'] = "\u03a9\u0399", // GREEK SMALL LETTER OMEGA WITH YPOGEGRAMMENI
            ['\u1ffc'] = "\u03a9\u0399", // GREEK CAPITAL LETTER OMEGA WITH PROSGEGRAMMENI
            ['\u1fb2'] = "\u1fba\u0399", // GREEK SMALL LETTER ALPHA WITH VARIA AND YPOGEGRAMMENI
            ['\u1fb4'] = "\u0386\u0399", // GREEK SMALL LETTER ALPHA WITH OXIA AND YPOGEGRAMMENI
            ['\u1fc2'] = "\u1fca\u0399", // GREEK SMALL LETTER ETA WITH VARIA AND YPOGEGRAMMENI
            ['\u1fc4'] = "\u0389\u0399", // GREEK SMALL LETTER ETA WITH OXIA AND YPOGEGRAMMENI
            ['\u1ff2'] = "\u1ffa\u0399", // GREEK SMALL LETTER OMEGA WITH VARIA AND YPOGEGRAMMENI
            ['\u1ff4'] = "\u038f\u0399", // GREEK SMALL LETTER OMEGA WITH OXIA AND YPOGEGRAMMENI
            ['\u1fb7'] = "\u0391\u0342\u0399", // GREEK SMALL LETTER ALPHA WITH PERISPOMENI AND YPOGEGRAMMENI
            ['\u1fc7'] = "\u0397\u0342\u0399", // GREEK SMALL LETTER ETA WITH PERISPOMENI AND YPOGEGRAMMENI
            ['\u1ff7'] = "\u03a9\u0342\u0399", // GREEK SMALL LETTER OMEGA WITH PERISPOMENI AND YPOGEGRAMMENI
        };

        /// <summary>The characters whose full lower-case mapping is more than one character.</summary>
        private static readonly Dictionary<char, string> s_lower = new Dictionary<char, string>
        {
            ['\u0130'] = "\u0069\u0307", // LATIN CAPITAL LETTER I WITH DOT ABOVE
        };

        private static readonly SearchValues<char> s_upperKeys = SearchValues.Create(Keys(s_upper));

        private static readonly SearchValues<char> s_lowerKeys = SearchValues.Create(Keys(s_lower));

        /// <summary>Converts to upper case, as <c>fn:upper-case</c> is defined to.</summary>
        /// <param name="value">The text.</param>
        public static string ToUpper(string value)
        {
            return Expand(value, s_upperKeys, s_upper).ToUpperInvariant();
        }

        /// <summary>Converts to lower case, as <c>fn:lower-case</c> is defined to.</summary>
        /// <param name="value">The text.</param>
        public static string ToLower(string value)
        {
            return Expand(value, s_lowerKeys, s_lower).ToLowerInvariant();
        }

        /// <summary>
        /// Replaces every character the table names with the text it maps to, and returns the string
        /// unchanged where it names none — which is nearly every string there is.
        /// </summary>
        private static string Expand(string value, SearchValues<char> keys, Dictionary<char, string> table)
        {
            int at = value.AsSpan().IndexOfAny(keys);

            if (at < 0)
            {
                return value;
            }

            StringBuilder builder = new StringBuilder(value.Length + 8);
            builder.Append(value, 0, at);

            for (int i = at; i < value.Length; i++)
            {
                if (table.TryGetValue(value[i], out string? full))
                {
                    builder.Append(full);
                }
                else
                {
                    builder.Append(value[i]);
                }
            }

            return builder.ToString();
        }

        private static char[] Keys(Dictionary<char, string> table)
        {
            char[] keys = new char[table.Count];
            table.Keys.CopyTo(keys, 0);
            return keys;
        }
    }
}
