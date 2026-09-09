namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// The substitutions declared by an <c>xsl:character-map</c>, applied as the result is serialized.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A character map replaces a character with a string that is then written exactly as it stands, markup
    /// and all: mapping a no-break space to <c>&amp;nbsp;</c> produces that entity reference rather than
    /// <c>&amp;amp;nbsp;</c>. That makes it XSLT 2.0's answer to <c>disable-output-escaping</c>, and a better
    /// one — it is declared once, where it can be read, instead of at every place the text is written, and it
    /// cannot be lost when the result is handed to something other than a serializer.
    /// </para>
    /// <para>
    /// A map is consulted once per character written, so the answer "not mapped" has to be nearly free. One
    /// bit per code point answers it with a single array access, and the array spans only the range the map
    /// actually covers rather than the whole of Unicode — a map of one character occupies one word. Only a
    /// character whose bit is set goes on to the dictionary.
    /// </para>
    /// </remarks>
    public sealed class CharacterMap
    {
        private readonly Dictionary<int, string> m_entries;
        private readonly ulong[] m_present;
        private readonly int m_firstWord;

        /// <summary>Initializes a character map.</summary>
        /// <param name="mappings">The substitutions, from code point to the string replacing it.</param>
        public CharacterMap(IEnumerable<KeyValuePair<int, string>> mappings)
        {
            m_entries = new Dictionary<int, string>();

            foreach (KeyValuePair<int, string> mapping in mappings)
            {
                m_entries[mapping.Key] = mapping.Value;
            }

            if (m_entries.Count == 0)
            {
                m_present = Array.Empty<ulong>();
                return;
            }

            int lowest = int.MaxValue;
            int highest = int.MinValue;

            foreach (int codePoint in m_entries.Keys)
            {
                int unit = IndexUnit(codePoint);
                lowest = Math.Min(lowest, unit);
                highest = Math.Max(highest, unit);
            }

            m_firstWord = lowest >> 6;
            m_present = new ulong[(highest >> 6) - m_firstWord + 1];

            foreach (int codePoint in m_entries.Keys)
            {
                int unit = IndexUnit(codePoint);
                m_present[(unit >> 6) - m_firstWord] |= 1UL << (unit & 63);
            }
        }

        /// <summary>Gets the number of characters this map substitutes.</summary>
        public int Count => m_entries.Count;

        /// <summary>Every mapping, by code point.</summary>
        public IReadOnlyDictionary<int, string> Entries => m_entries;

        /// <summary>Returns the replacement for a code point, or <see langword="null"/> if it is not mapped.</summary>
        /// <param name="codePoint">The Unicode code point to look up.</param>
        public string? Find(int codePoint)
        {
            return m_entries.TryGetValue(codePoint, out string? replacement) ? replacement : null;
        }

        /// <summary>
        /// Looks up the character at one position of a string.
        /// </summary>
        /// <remarks>
        /// Takes the string rather than a character because a mapped character may be one that does not fit in
        /// a single <see cref="char"/>, and the pair encoding it can only be recognized in context.
        /// </remarks>
        /// <param name="text">The text being written.</param>
        /// <param name="index">The position to examine.</param>
        /// <param name="replacement">The string to write in its place, when the result is true.</param>
        /// <param name="length">How many <see cref="char"/> values the mapped character occupies.</param>
        internal bool TryMap(string text, int index, out string? replacement, out int length)
        {
            char unit = text[index];
            int word = (unit >> 6) - m_firstWord;

            if ((uint)word >= (uint)m_present.Length || (m_present[word] & (1UL << (unit & 63))) == 0)
            {
                replacement = null;
                length = 0;
                return false;
            }

            if (char.IsHighSurrogate(unit) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                length = 2;
                return m_entries.TryGetValue(char.ConvertToUtf32(unit, text[index + 1]), out replacement);
            }

            length = 1;
            return m_entries.TryGetValue(unit, out replacement);
        }

        /// <summary>
        /// The code unit a code point is indexed by: itself where it fits in one, and otherwise the leading
        /// surrogate of the pair encoding it.
        /// </summary>
        /// <remarks>
        /// A string is scanned one code unit at a time, so indexing a supplementary character by its leading
        /// surrogate lets the same single test serve both kinds. A map holding no supplementary character
        /// never reaches the surrogate range and pays nothing for this.
        /// </remarks>
        private static int IndexUnit(int codePoint)
        {
            return codePoint <= 0xFFFF ? codePoint : ((codePoint - 0x10000) >> 10) + 0xD800;
        }
    }
}
