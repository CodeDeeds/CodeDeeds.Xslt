namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// String operations counted in code points, which is the unit XPath's string functions work in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A .NET string is a sequence of UTF-16 units and a character above the basic plane is two of them, so
    /// <c>"abc𝅖def".Length</c> is 8 where <c>fn:string-length</c> of the same string is 7. The difference
    /// shows up wherever a position is counted: <c>substring(…, 5)</c> takes the second half of the pair on
    /// its own, which is not a character at all and cannot be written out.
    /// </para>
    /// <para>
    /// Every operation here has a fast path for text that holds no surrogate pair, which is nearly all text,
    /// and in that case the answer is the .NET one. The scan that decides costs one pass over a string these
    /// functions were already going to walk.
    /// </para>
    /// </remarks>
    internal static class CodePoints
    {
        /// <summary>Returns how many code points a string holds.</summary>
        /// <param name="text">The text to measure.</param>
        public static int Count(string text)
        {
            int count = text.Length;

            for (int i = 0; i + 1 < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && char.IsLowSurrogate(text[i + 1]))
                {
                    count--;
                    i++;
                }
            }

            return count;
        }

        /// <summary>
        /// Returns a run of code points, by the same arguments <see cref="string.Substring(int, int)"/> takes
        /// but counted in code points.
        /// </summary>
        /// <param name="text">The text to take from.</param>
        /// <param name="start">Where to start, counted from zero.</param>
        /// <param name="count">How many code points to take.</param>
        public static string Slice(string text, int start, int count)
        {
            if (Count(text) == text.Length)
            {
                return text.Substring(start, count);
            }

            int from = OffsetOf(text, start);
            return text[from..OffsetOf(text, start + count)];
        }

        /// <summary>Returns the code points a string holds, one at a time.</summary>
        /// <param name="text">The text to read.</param>
        public static IEnumerable<int> Of(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                yield return char.IsHighSurrogate(text[i]) && i + 1 < text.Length
                    && char.IsLowSurrogate(text[i + 1])
                        ? char.ConvertToUtf32(text[i], text[i++ + 1])
                        : text[i];
            }
        }

        /// <summary>
        /// Returns where the <paramref name="wanted"/>th code point begins, as an index into the UTF-16 units.
        /// </summary>
        /// <remarks>
        /// An index past the end of the text gives the length, so that a caller clamping in code points does
        /// not have to clamp again in units.
        /// </remarks>
        private static int OffsetOf(string text, int wanted)
        {
            int index = 0;

            while (wanted > 0 && index < text.Length)
            {
                index += char.IsHighSurrogate(text[index]) && index + 1 < text.Length
                    && char.IsLowSurrogate(text[index + 1])
                        ? 2
                        : 1;

                wanted--;
            }

            return index;
        }
    }
}
