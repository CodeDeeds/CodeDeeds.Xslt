namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// Interns expanded names so that name comparison becomes integer comparison.
    /// </summary>
    /// <remarks>
    /// Names are interned at two levels, following the scheme used by Saxon:
    /// <list type="bullet">
    /// <item>
    /// A <em>fingerprint</em> identifies the expanded name — the (namespace URI, local name) pair. Two names
    /// match under XPath name tests exactly when their fingerprints are equal, so a name test is a single
    /// <see cref="System.Int32"/> comparison.
    /// </item>
    /// <item>
    /// A <em>name code</em> identifies a (fingerprint, prefix) triple. The prefix carries no meaning for
    /// matching, but must be retained so that serialization can reproduce the original prefix.
    /// </item>
    /// </list>
    /// A <see cref="NameTable"/> is not thread-safe while names are still being added. Once a tree or a
    /// stylesheet has been built, the table is only read, and concurrent reads are safe.
    /// </remarks>
    public sealed class NameTable
    {
        /// <summary>The fingerprint of the empty name, used by nodes that have no name.</summary>
        public const int NoFingerprint = -1;

        /// <summary>The name code of the empty name, used by nodes that have no name.</summary>
        public const int NoNameCode = -1;

        private readonly Dictionary<FingerprintKey, int> m_fingerprintLookup = new();
        private string[] m_fingerprintUri = new string[64];
        private string[] m_fingerprintLocal = new string[64];
        private int m_fingerprintCount;

        private readonly Dictionary<NameKey, int> m_nameLookup = new();
        private int[] m_nameFingerprint = new int[64];
        private string[] m_namePrefix = new string[64];
        private int m_nameCount;

        /// <summary>
        /// Gets the number of distinct expanded names interned in this table.
        /// </summary>
        public int FingerprintCount => m_fingerprintCount;

        /// <summary>
        /// Gets the number of distinct (expanded name, prefix) combinations interned in this table.
        /// </summary>
        public int NameCodeCount => m_nameCount;

        /// <summary>
        /// Returns the fingerprint for an expanded name, allocating one if the name has not been seen before.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <returns>A non-negative fingerprint identifying the expanded name.</returns>
        public int GetFingerprint(string namespaceUri, string localName)
        {
            FingerprintKey key = new FingerprintKey(namespaceUri, localName);
            if (m_fingerprintLookup.TryGetValue(key, out int existing))
            {
                return existing;
            }

            if (m_fingerprintCount == m_fingerprintUri.Length)
            {
                Array.Resize(ref m_fingerprintUri, m_fingerprintCount * 2);
                Array.Resize(ref m_fingerprintLocal, m_fingerprintCount * 2);
            }

            int fingerprint = m_fingerprintCount++;
            m_fingerprintUri[fingerprint] = namespaceUri;
            m_fingerprintLocal[fingerprint] = localName;
            m_fingerprintLookup.Add(key, fingerprint);
            return fingerprint;
        }

        /// <summary>
        /// Looks up the fingerprint for an expanded name without allocating one.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <returns>
        /// The fingerprint, or <see cref="NoFingerprint"/> if the name has never been interned. A name test
        /// against an unknown name can never match any node in the tree, so callers may treat this as
        /// "matches nothing".
        /// </returns>
        public int LookupFingerprint(string namespaceUri, string localName)
        {
            return m_fingerprintLookup.TryGetValue(new FingerprintKey(namespaceUri, localName), out int fingerprint)
                ? fingerprint
                : NoFingerprint;
        }

        /// <summary>
        /// Returns the name code for a qualified name, allocating one if the combination has not been seen before.
        /// </summary>
        /// <param name="prefix">The prefix, or an empty string if the name is unprefixed.</param>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <returns>A non-negative name code identifying the (expanded name, prefix) combination.</returns>
        public int GetNameCode(string prefix, string namespaceUri, string localName)
        {
            int fingerprint = GetFingerprint(namespaceUri, localName);

            NameKey key = new NameKey(fingerprint, prefix);
            if (m_nameLookup.TryGetValue(key, out int existing))
            {
                return existing;
            }

            if (m_nameCount == m_nameFingerprint.Length)
            {
                Array.Resize(ref m_nameFingerprint, m_nameCount * 2);
                Array.Resize(ref m_namePrefix, m_nameCount * 2);
            }

            int nameCode = m_nameCount++;
            m_nameFingerprint[nameCode] = fingerprint;
            m_namePrefix[nameCode] = prefix;
            m_nameLookup.Add(key, nameCode);
            return nameCode;
        }

        /// <summary>
        /// Returns the fingerprint underlying a name code. This is the value to compare for name tests.
        /// </summary>
        /// <param name="nameCode">A name code previously returned by <see cref="GetNameCode"/>.</param>
        /// <returns>The fingerprint, or <see cref="NoFingerprint"/> if <paramref name="nameCode"/> is <see cref="NoNameCode"/>.</returns>
        public int GetFingerprintOfNameCode(int nameCode)
        {
            return nameCode == NoNameCode ? NoFingerprint : m_nameFingerprint[nameCode];
        }

        /// <summary>
        /// Returns the prefix associated with a name code, or an empty string if the name is unprefixed.
        /// </summary>
        public string GetPrefix(int nameCode)
        {
            return nameCode == NoNameCode ? string.Empty : m_namePrefix[nameCode];
        }

        /// <summary>
        /// Returns the namespace URI for a fingerprint, or an empty string if the name is in no namespace.
        /// </summary>
        public string GetNamespaceUri(int fingerprint)
        {
            return fingerprint == NoFingerprint ? string.Empty : m_fingerprintUri[fingerprint];
        }

        /// <summary>
        /// Returns the local part of the name for a fingerprint, or an empty string if there is no name.
        /// </summary>
        public string GetLocalName(int fingerprint)
        {
            return fingerprint == NoFingerprint ? string.Empty : m_fingerprintLocal[fingerprint];
        }

        /// <summary>
        /// Returns the lexical qualified name for a name code — <c>prefix:local</c>, or just <c>local</c>
        /// when the name is unprefixed.
        /// </summary>
        public string GetQualifiedName(int nameCode)
        {
            if (nameCode == NoNameCode)
            {
                return string.Empty;
            }

            string prefix = m_namePrefix[nameCode];
            string localName = m_fingerprintLocal[m_nameFingerprint[nameCode]];
            return prefix.Length == 0 ? localName : string.Concat(prefix, ":", localName);
        }

        private readonly record struct FingerprintKey(string Uri, string Local);

        private readonly record struct NameKey(int Fingerprint, string Prefix);
    }
}
