namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// Decides which whitespace-only text nodes are dropped while a source document is built, as directed by
    /// <c>xsl:strip-space</c> and <c>xsl:preserve-space</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stripping during construction rather than afterwards means the nodes are never created at all, so a
    /// heavily indented document costs neither the memory nor the traversal.
    /// </para>
    /// <para>
    /// XSLT resolves conflicting declarations by specificity: an exact name beats <c>prefix:*</c> and
    /// <c>*:name</c>, which beat <c>*</c>. Among equally specific declarations the last one written wins,
    /// which is why entries record the order they were declared in.
    /// </para>
    /// </remarks>
    public sealed class WhitespaceControl
    {
        /// <summary>A control that preserves every whitespace-only text node, which is the default.</summary>
        public static WhitespaceControl PreserveAll { get; } = new WhitespaceControl();

        private readonly List<Entry> m_entries = new();

        /// <summary>Gets whether any declaration has been added.</summary>
        public bool IsEmpty => m_entries.Count == 0;

        /// <summary>
        /// Records a declaration for one element name test.
        /// </summary>
        /// <param name="namespaceUri">
        /// The namespace URI the test applies to, an empty string for none, or <see langword="null"/> to
        /// match any namespace at all.
        /// </param>
        /// <param name="localName">The local name, or <c>*</c> to match any name.</param>
        /// <param name="strip">Whether matching elements have their whitespace-only text stripped.</param>
        public void Declare(string? namespaceUri, string localName, bool strip)
        {
            // A test that names both halves is the most specific, one that names neither the least, and
            // 'p:*' and '*:a' are equally specific between them — which is the order of the priorities a
            // name test carries as a pattern, since these are name tests and that is what they are for.
            int specificity = (namespaceUri is null ? 0 : 1) + (localName == "*" ? 0 : 1);
            m_entries.Add(new Entry(namespaceUri, localName, specificity, strip, m_entries.Count));
        }

        /// <summary>
        /// Returns whether whitespace-only text directly inside an element should be dropped.
        /// </summary>
        /// <param name="namespaceUri">The element's namespace URI.</param>
        /// <param name="localName">The element's local name.</param>
        public bool ShouldStrip(string namespaceUri, string localName)
        {
            Entry? best = null;

            foreach (Entry entry in m_entries)
            {
                if (!entry.Matches(namespaceUri, localName))
                {
                    continue;
                }

                // More specific wins; among equals, the one declared later.
                if (best is null
                    || entry.Specificity > best.Value.Specificity
                    || (entry.Specificity == best.Value.Specificity && entry.Order > best.Value.Order))
                {
                    best = entry;
                }
            }

            return best?.Strip ?? false;
        }

        private readonly record struct Entry(
            string? NamespaceUri,
            string LocalName,
            int Specificity,
            bool Strip,
            int Order)
        {
            public bool Matches(string namespaceUri, string localName)
            {
                if (NamespaceUri is not null
                    && !string.Equals(NamespaceUri, namespaceUri, StringComparison.Ordinal))
                {
                    return false;
                }

                return LocalName == "*" || string.Equals(LocalName, localName, StringComparison.Ordinal);
            }
        }
    }
}
