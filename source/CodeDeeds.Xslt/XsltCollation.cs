namespace CodeDeeds.Xslt
{
    /// <summary>
    /// A collation of the caller's own: a way of comparing strings, supplied under a URI of the caller's
    /// choosing through <see cref="IXsltCollationResolver"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine provides the code point collation, the HTML ASCII case-insensitive one, and the Unicode
    /// Collation Algorithm with its parameters, and those cover most of what a stylesheet asks for. What
    /// they do not cover is an ordering the caller already has — a database's, a legacy system's, one
    /// particular to a domain — and this is how such an ordering is handed in. Only <see cref="Compare"/>
    /// has to be written; everything else has a default that says what the collation cannot do.
    /// </para>
    /// <para>
    /// <see cref="Key"/> matters more than it looks. Sorting and comparison need only <see cref="Compare"/>,
    /// but grouping, <c>xsl:key</c>, <c>distinct-values()</c> and <c>fn:collation-key()</c> file strings
    /// under a key and find them again by it, and a collation that makes no key cannot be used for those:
    /// the engine says so (<c>FOCH0004</c>) rather than grouping by something else. The substring
    /// functions — <c>contains()</c>, <c>starts-with()</c>, <c>substring-before()</c> and their kin — need
    /// to match part of a string, which a comparison alone cannot do, so they are refused the same way
    /// unless <see cref="SupportsSubstringMatching"/> says otherwise.
    /// </para>
    /// </remarks>
    public abstract class XsltCollation
    {
        /// <summary>
        /// Compares two strings.
        /// </summary>
        /// <param name="first">The first string.</param>
        /// <param name="second">The second string.</param>
        /// <returns>Negative where the first sorts before the second, zero where they are equal, positive after.</returns>
        public abstract int Compare(string first, string second);

        /// <summary>
        /// Whether two strings are the same string under this collation. By default, whether
        /// <see cref="Compare"/> answers zero.
        /// </summary>
        /// <param name="first">The first string.</param>
        /// <param name="second">The second string.</param>
        public virtual bool AreEqual(string first, string second)
        {
            return Compare(first, second) == 0;
        }

        /// <summary>
        /// A key on which two strings agree exactly when this collation calls them the same string, and
        /// which orders by code point as the strings order under <see cref="Compare"/>; or
        /// <see langword="null"/> where the collation makes none.
        /// </summary>
        /// <remarks>
        /// A case-blind collation's key is the string in one case. A culture's is what
        /// <see cref="System.Globalization.CompareInfo.GetSortKey(string)"/> hands back, encoded as text.
        /// None by default, which keeps the collation out of grouping, keys, <c>distinct-values()</c> and
        /// <c>fn:collation-key()</c> and leaves it to sorting and comparison.
        /// </remarks>
        /// <param name="value">The string.</param>
        public virtual string? Key(string value)
        {
            return null;
        }

        /// <summary>
        /// Whether <see cref="StartsWith"/>, <see cref="EndsWith"/> and <see cref="IndexOf"/> are
        /// implemented, which is what the substring functions need. <see langword="false"/> by default.
        /// </summary>
        public virtual bool SupportsSubstringMatching => false;

        /// <summary>Whether one string begins with another, under this collation.</summary>
        /// <param name="subject">The string to look in.</param>
        /// <param name="prefix">The string to look for.</param>
        public virtual bool StartsWith(string subject, string prefix)
        {
            throw new NotSupportedException("This collation does not match substrings.");
        }

        /// <summary>Whether one string ends with another, under this collation.</summary>
        /// <param name="subject">The string to look in.</param>
        /// <param name="suffix">The string to look for.</param>
        public virtual bool EndsWith(string subject, string suffix)
        {
            throw new NotSupportedException("This collation does not match substrings.");
        }

        /// <summary>
        /// Where one string first appears within another, under this collation, and how many characters
        /// of the subject the match covered — which under a collation that ignores some characters need
        /// not be the length of what was sought.
        /// </summary>
        /// <param name="subject">The string to look in.</param>
        /// <param name="sought">The string to look for.</param>
        /// <param name="length">On a match, how many characters of the subject it covered.</param>
        /// <returns>The index of the match, or -1.</returns>
        public virtual int IndexOf(string subject, string sought, out int length)
        {
            throw new NotSupportedException("This collation does not match substrings.");
        }

        /// <summary>
        /// A collation over a comparer, which compares and tests equality and does nothing else: it makes
        /// no key and matches no substring.
        /// </summary>
        /// <param name="comparer">The comparer.</param>
        public static XsltCollation FromComparer(IComparer<string> comparer)
        {
            ArgumentNullException.ThrowIfNull(comparer);
            return new ComparerCollation(comparer);
        }

        private sealed class ComparerCollation : XsltCollation
        {
            private readonly IComparer<string> m_comparer;

            public ComparerCollation(IComparer<string> comparer)
            {
                m_comparer = comparer;
            }

            public override int Compare(string first, string second)
            {
                return m_comparer.Compare(first, second);
            }
        }
    }

    /// <summary>
    /// Supplies collations of the caller's own, by URI, for everything in a stylesheet that names one:
    /// <c>xsl:sort</c>, <c>xsl:for-each-group</c>, <c>xsl:key</c>, <c>default-collation</c>, and the
    /// functions that take a collation argument.
    /// </summary>
    /// <remarks>
    /// Asked only for a URI the engine does not provide itself: the code point collation, the HTML ASCII
    /// case-insensitive one and the UCA collation are answered before the resolver is consulted, so a
    /// resolver cannot change what those mean. A URI neither has is <c>FOCH0002</c>, or the code the
    /// instruction naming it gives. What the resolver answers for a URI is kept for as long as the
    /// resolver lives, so it may build a collation on every call without cost.
    /// </remarks>
    public interface IXsltCollationResolver
    {
        /// <summary>
        /// Returns the collation a URI names.
        /// </summary>
        /// <param name="uri">The collation URI, exactly as the stylesheet wrote it.</param>
        /// <returns>The collation, or <see langword="null"/> if this resolver has none by that name.</returns>
        XsltCollation? Resolve(string uri);
    }
}
