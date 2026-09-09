namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// The rule deciding whether a picture's grouping separators repeat.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shared by <c>fn:format-number</c> and <c>fn:format-integer</c>, which state it in the same words. A
    /// picture whose separators are all one character, all at multiples of one interval, with none of those
    /// multiples missing, describes a <em>habit</em>: <c>#,##0</c> groups every three digits however long the
    /// number turns out to be. Anything else describes a <em>shape</em>, and its separators stay exactly
    /// where they were written while a longer number runs off the left.
    /// </para>
    /// <para>
    /// The distinction is what separates <c>00,00,00</c> — which gives <c>1,23,45,67,89</c> — from
    /// <c>000,00,00</c>, where a regular pattern of two would also have wanted a separator after the sixth
    /// digit. Having none there, that one gives <c>12345,67,89</c>.
    /// </para>
    /// </remarks>
    internal static class DigitGrouping
    {
        /// <summary>
        /// Works out the interval at which a picture's separators repeat.
        /// </summary>
        /// <param name="positions">
        /// Where the separators are, counted as the number of digit signs standing to the right of each.
        /// </param>
        /// <param name="signs">How many digit signs the picture has in total.</param>
        /// <returns>The interval, or zero where the separators are irregular and stay where they were put.</returns>
        public static int IntervalOf(IReadOnlyList<int> positions, int signs)
        {
            if (positions.Count == 0)
            {
                return 0;
            }

            // The rightmost separator sets the candidate, there being no smaller interval every position
            // could be a multiple of.
            int interval = positions[^1];

            if (interval <= 0)
            {
                return 0;
            }

            foreach (int position in positions)
            {
                if (position % interval != 0)
                {
                    return 0;
                }
            }

            // And a regular picture leaves none of the intervening multiples out.
            for (int position = interval; position < signs; position += interval)
            {
                if (!Contains(positions, position))
                {
                    return 0;
                }
            }

            return interval;
        }

        /// <summary>Whether a separator stands this many digits from the right.</summary>
        /// <remarks>
        /// Linear, because a picture has a handful of separators at most and the alternative allocates a set
        /// per picture to save comparisons that were never going to be counted.
        /// </remarks>
        public static bool Contains(IReadOnlyList<int> positions, int position)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i] == position)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
