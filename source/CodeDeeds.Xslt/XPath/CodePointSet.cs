namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A set of Unicode code points, held as the sorted disjoint ranges that spell it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because the two regular expression languages disagree about what a character is. XPath's
    /// is a code point, so <c>[&#x10000;-&#x10005;]</c> is six of them and <c>.</c> matches any one; .NET's
    /// is a UTF-16 unit, so the same range is a pair of surrogates against a pair of surrogates and reads as
    /// nothing anyone wrote. A character class therefore cannot be handed across as written — it has to be
    /// worked out as a set of code points and spelled again in the smaller alphabet.
    /// </para>
    /// <para>
    /// Ranges are inclusive at both ends and are kept sorted and merged, adjacency included, so that a set
    /// has exactly one representation and comparing two of them is comparing their ranges.
    /// </para>
    /// </remarks>
    internal sealed class CodePointSet
    {
        /// <summary>The highest code point there is.</summary>
        public const int Max = 0x10FFFF;

        /// <summary>The first code point above the basic plane, where surrogate pairs begin.</summary>
        public const int FirstSupplementary = 0x10000;

        private const int FirstSurrogate = 0xD800;
        private const int LastSurrogate = 0xDFFF;

        private readonly List<(int Low, int High)> m_ranges = new List<(int Low, int High)>();
        private bool m_ordered = true;

        /// <summary>Whether the set holds nothing.</summary>
        public bool IsEmpty
        {
            get
            {
                Order();
                return m_ranges.Count == 0;
            }
        }

        /// <summary>The ranges the set is made of, sorted, disjoint, and not adjacent to one another.</summary>
        public IReadOnlyList<(int Low, int High)> Ranges
        {
            get
            {
                Order();
                return m_ranges;
            }
        }

        /// <summary>Adds one code point.</summary>
        public void Add(int code)
        {
            Add(code, code);
        }

        /// <summary>Adds an inclusive range, which may overlap anything already there.</summary>
        public void Add(int low, int high)
        {
            if (low > high)
            {
                return;
            }

            if (m_ranges.Count > 0 && low < m_ranges[m_ranges.Count - 1].Low)
            {
                m_ordered = false;
            }

            m_ranges.Add((low, high));
        }

        /// <summary>Adds everything another set holds.</summary>
        public void Add(CodePointSet other)
        {
            foreach ((int low, int high) in other.Ranges)
            {
                Add(low, high);
            }
        }

        /// <summary>Takes away everything another set holds.</summary>
        public void Subtract(CodePointSet other)
        {
            Order();
            IReadOnlyList<(int Low, int High)> cuts = other.Ranges;

            if (cuts.Count == 0 || m_ranges.Count == 0)
            {
                return;
            }

            List<(int Low, int High)> kept = new List<(int Low, int High)>(m_ranges.Count);

            foreach ((int low, int high) in m_ranges)
            {
                int from = low;

                foreach ((int cutLow, int cutHigh) in cuts)
                {
                    if (cutHigh < from)
                    {
                        continue;
                    }

                    if (cutLow > high)
                    {
                        break;
                    }

                    if (cutLow > from)
                    {
                        kept.Add((from, cutLow - 1));
                    }

                    from = cutHigh + 1;

                    if (from > high)
                    {
                        break;
                    }
                }

                if (from <= high)
                {
                    kept.Add((from, high));
                }
            }

            m_ranges.Clear();
            m_ranges.AddRange(kept);
            m_ordered = true;
        }

        /// <summary>
        /// Turns the set into every code point it does not hold.
        /// </summary>
        /// <remarks>
        /// The surrogate code points are left out of the answer. They are not characters — they are how
        /// UTF-16 spells the ones above the basic plane — so nothing in XPath matches one, and letting a
        /// complement hold them would let a pattern match half of a character and stop there.
        /// </remarks>
        public void Complement()
        {
            Order();
            List<(int Low, int High)> gaps = new List<(int Low, int High)>(m_ranges.Count + 1);
            int from = 0;

            foreach ((int low, int high) in m_ranges)
            {
                if (low > from)
                {
                    gaps.Add((from, low - 1));
                }

                from = high + 1;
            }

            if (from <= Max)
            {
                gaps.Add((from, Max));
            }

            m_ranges.Clear();
            m_ranges.AddRange(gaps);
            m_ordered = true;

            RemoveSurrogates();
        }

        /// <summary>Takes out the surrogate code points, which stand for characters but are not any.</summary>
        public void RemoveSurrogates()
        {
            CodePointSet surrogates = new CodePointSet();
            surrogates.Add(FirstSurrogate, LastSurrogate);
            Subtract(surrogates);
        }

        /// <summary>Whether every code point the set holds lies inside the basic plane.</summary>
        public bool IsBasicPlaneOnly()
        {
            Order();
            return m_ranges.Count == 0 || m_ranges[m_ranges.Count - 1].High < FirstSupplementary;
        }

        /// <summary>Whether the set holds exactly one code point, and which.</summary>
        public bool IsSingle(out int code)
        {
            Order();

            if (m_ranges.Count == 1 && m_ranges[0].Low == m_ranges[0].High)
            {
                code = m_ranges[0].Low;
                return true;
            }

            code = 0;
            return false;
        }

        /// <summary>Sorts and merges the ranges, so that the set has one representation.</summary>
        private void Order()
        {
            if (m_ordered && m_ranges.Count <= 1)
            {
                return;
            }

            if (!m_ordered)
            {
                m_ranges.Sort(static (left, right) => left.Low.CompareTo(right.Low));
                m_ordered = true;
            }

            int kept = 0;

            for (int i = 1; i < m_ranges.Count; i++)
            {
                // Adjacent ranges are merged as well as overlapping ones: [a-b] and [c-d] where c is b + 1
                // is one range, and leaving it as two would spell the same set two ways.
                if (m_ranges[i].Low <= m_ranges[kept].High + 1)
                {
                    if (m_ranges[i].High > m_ranges[kept].High)
                    {
                        m_ranges[kept] = (m_ranges[kept].Low, m_ranges[i].High);
                    }

                    continue;
                }

                m_ranges[++kept] = m_ranges[i];
            }

            m_ranges.RemoveRange(kept + 1, m_ranges.Count - kept - 1);
        }
    }
}
