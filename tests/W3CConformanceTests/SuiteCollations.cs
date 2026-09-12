namespace CodeDeeds.Xslt.Conformance
{
    /// <summary>
    /// The collations the two suites declare in their environments and expect the driver to supply: a
    /// case-blind one, under the URI each catalog gives it.
    /// </summary>
    /// <remarks>
    /// Everything else an environment declares — the UCA collation with parameters, the HTML ASCII
    /// case-insensitive one — the engine provides itself and answers before asking here, so declaring it
    /// costs the driver nothing. What the suites mean by <em>caseblind</em> is a collation under which
    /// case does not tell two strings apart; ordinal comparison without regard to case is that, and it
    /// makes a key and matches substrings, which the tests using it need.
    /// </remarks>
    internal sealed class SuiteCollations : IXsltCollationResolver
    {
        public static SuiteCollations Instance { get; } = new SuiteCollations();

        private static readonly XsltCollation s_caseBlind = new CaseBlind();

        public XsltCollation? Resolve(string uri)
        {
            return uri is "http://www.w3.org/2010/09/qt-fots-catalog/collation/caseblind"
                or "http://www.w3.org/xslts/collation/caseblind"
                ? s_caseBlind
                : null;
        }

        private sealed class CaseBlind : XsltCollation
        {
            public override int Compare(string first, string second)
            {
                return string.Compare(first, second, StringComparison.OrdinalIgnoreCase);
            }

            public override bool AreEqual(string first, string second)
            {
                return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
            }

            public override string? Key(string value)
            {
                return value.ToUpperInvariant();
            }

            public override bool SupportsSubstringMatching => true;

            public override bool StartsWith(string subject, string prefix)
            {
                return subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }

            public override bool EndsWith(string subject, string suffix)
            {
                return subject.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }

            public override int IndexOf(string subject, string sought, out int length)
            {
                length = sought.Length;
                return subject.IndexOf(sought, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
