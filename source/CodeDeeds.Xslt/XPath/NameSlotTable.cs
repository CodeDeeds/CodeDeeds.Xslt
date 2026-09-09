using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>An expanded name — the (namespace URI, local name) pair that XPath name tests compare.</summary>
    /// <param name="NamespaceUri">The namespace URI, or an empty string for no namespace.</param>
    /// <param name="LocalName">The local part of the name.</param>
    public readonly record struct ExpandedName(string NamespaceUri, string LocalName);

    /// <summary>
    /// Assigns a compile-time slot to each distinct expanded name a stylesheet tests against, and resolves
    /// those slots to fingerprints for a particular input tree at run time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fingerprints are only meaningful within one <see cref="Model.NameTable"/>, so a compiled name test
    /// cannot simply hold a fingerprint from the stylesheet's own table. Sharing a single table between the
    /// stylesheet and every document it transforms would fix that, but it would also mean mutating the
    /// stylesheet's table on every transform — which would make a compiled stylesheet unsafe to use from more
    /// than one thread, defeating the purpose of compiling it once.
    /// </para>
    /// <para>
    /// Instead each name test holds a slot index. At the start of a transform, <see cref="BuildFingerprintMap"/>
    /// resolves every slot against the input tree's table in one pass, and name tests then read a fingerprint
    /// out of that array. The comparison stays an integer compare, at the cost of one array indirection.
    /// </para>
    /// <para>
    /// The table does grow after compilation, in one case: a target expression of <c>xsl:evaluate</c> may
    /// test a name the stylesheet never wrote, and it is given a slot then. Slots are only ever added, so
    /// one handed out stays good, and the lock is what lets two transformations sharing the stylesheet ask
    /// at once. A mapping built before the table grew is short rather than wrong, and the runtime builds it
    /// again when it sees that.
    /// </para>
    /// </remarks>
    public sealed class NameSlotTable
    {
        private readonly Dictionary<ExpandedName, int> m_slots = new();
        private readonly List<ExpandedName> m_names = new();

        /// <summary>Gets the number of distinct names that have been assigned slots.</summary>
        public int Count
        {
            get
            {
                lock (m_slots)
                {
                    return m_names.Count;
                }
            }
        }

        /// <summary>
        /// Returns the slot for an expanded name, assigning one if the name has not been seen before.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <returns>A slot index for use with <see cref="BuildFingerprintMap"/>.</returns>
        public int GetSlot(string namespaceUri, string localName)
        {
            ExpandedName name = new ExpandedName(namespaceUri, localName);

            lock (m_slots)
            {
                if (m_slots.TryGetValue(name, out int existing))
                {
                    return existing;
                }

                int slot = m_names.Count;
                m_names.Add(name);
                m_slots.Add(name, slot);
                return slot;
            }
        }

        /// <summary>Returns the expanded name occupying a slot.</summary>
        /// <param name="slot">A slot previously returned by <see cref="GetSlot"/>.</param>
        public ExpandedName GetName(int slot)
        {
            lock (m_slots)
            {
                return m_names[slot];
            }
        }

        /// <summary>
        /// Resolves every slot against a tree's name table.
        /// </summary>
        /// <param name="tree">The tree about to be transformed.</param>
        /// <returns>
        /// An array indexed by slot. A name that never occurs in the tree maps to
        /// <see cref="NameTable.NoFingerprint"/>, which cannot equal any node's fingerprint and so correctly
        /// matches nothing.
        /// </returns>
        public int[] BuildFingerprintMap(XdmTree tree)
        {
            lock (m_slots)
            {
                int[] map = new int[m_names.Count];
                for (int i = 0; i < map.Length; i++)
                {
                    ExpandedName name = m_names[i];
                    map[i] = tree.NameTable.LookupFingerprint(name.NamespaceUri, name.LocalName);
                }

                return map;
            }
        }
    }
}
