using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A way of comparing two strings, named by a URI.
    /// </summary>
    /// <remarks>
    /// Three are provided. The code point collation is the one every processor must have, and it compares by
    /// code point rather than by UTF-16 unit — those disagree above the basic plane, where a supplementary
    /// character is written as a surrogate pair beginning below U+FFFF. The HTML ASCII case-insensitive one
    /// that 3.1 adds folds <c>A-Z</c> onto <c>a-z</c> and nothing else, so it is not the invariant culture's
    /// idea of case either.
    /// <para>
    /// The third is the UCA collation URI, which asks for the Unicode Collation Algorithm with parameters.
    /// .NET's <see cref="CompareInfo"/> is backed by ICU, which is an implementation of that algorithm, so
    /// what is left is reading the parameters and refusing the ones it cannot honour. The specification calls
    /// this the *simple* fallback: with <c>fallback=yes</c>, the default, a processor may ignore a parameter
    /// it does not implement; with <c>fallback=no</c> it must raise <c>FOCH0002</c> instead, which is what
    /// makes the distinction observable.
    /// </para>
    /// <para>
    /// A fourth kind is the caller's: an <see cref="XsltCollation"/> handed in under a URI of the caller's
    /// choosing through <see cref="IXsltCollationResolver"/>, which is asked for whatever the three above
    /// do not answer. It is wrapped here so that everything downstream sees one shape, and what it cannot
    /// do — make a key, match a substring — is refused with <c>FOCH0004</c> where it is asked for.
    /// </para>
    /// </remarks>
    internal sealed class Collation
    {
        /// <summary>The collation URI that compares by code point, which every processor must provide.</summary>
        public const string CodepointUri = "http://www.w3.org/2005/xpath-functions/collation/codepoint";

        /// <summary>The collation URI that folds ASCII letters, which XPath 3.1 added.</summary>
        public const string HtmlAsciiCaseInsensitiveUri =
            "http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive";

        /// <summary>The base of the URI that asks for the Unicode Collation Algorithm.</summary>
        public const string UcaUri = "http://www.w3.org/2013/collation/UCA";

        private static readonly Dictionary<string, Collation> s_cache = new(StringComparer.Ordinal);

        /// <summary>
        /// What each caller's resolver has answered, kept as long as the resolver is: a URI is resolved on
        /// every function call that names one, and a resolver building a collation each time would pay for
        /// that on every item of a sort.
        /// </summary>
        private static readonly ConditionalWeakTable<IXsltCollationResolver, Dictionary<string, Collation>> s_callers = new();

        /// <summary>How the algorithm gets at the strings, which is the one thing the three do differently.</summary>
        private readonly CompareInfo? m_compare;

        private readonly CompareOptions m_options;

        /// <summary>Whether ASCII letters are folded before anything else looks at the string.</summary>
        private readonly bool m_foldAscii;

        /// <summary>
        /// Whether two strings that compare equal are then separated by code point.
        /// </summary>
        /// <remarks>
        /// What <c>strength=identical</c> asks for: every difference matters, including one the algorithm
        /// itself weighs as nothing.
        /// </remarks>
        private readonly bool m_identical;

        /// <summary>The caller's collation, where this wraps one, and the URI it was supplied under.</summary>
        private readonly XsltCollation? m_custom;
        private readonly string? m_uri;

        private Collation(CompareInfo? compare, CompareOptions options, bool foldAscii, bool identical)
        {
            m_compare = compare;
            m_options = options;
            m_foldAscii = foldAscii;
            m_identical = identical;
        }

        private Collation(string uri, XsltCollation custom)
        {
            m_uri = uri;
            m_custom = custom;
        }

        /// <summary>The code point collation, which is also the default where none is named.</summary>
        public static Collation Codepoint { get; } = new Collation(null, CompareOptions.None, false, false);

        /// <summary>
        /// Finds the collation a URI names among the ones this engine provides.
        /// </summary>
        /// <param name="uri">The URI as written. An empty one names the default, which is the code point one.</param>
        /// <returns>The collation.</returns>
        /// <exception cref="XsltException">The URI names no collation this engine has — <c>FOCH0002</c>.</exception>
        public static Collation Resolve(string uri)
        {
            return Resolve(uri, (IXsltCollationResolver?)null);
        }

        /// <summary>
        /// Finds the collation a URI names: one this engine provides, or failing that one the caller does.
        /// </summary>
        /// <param name="uri">The URI as written. An empty one names the default, which is the code point one.</param>
        /// <param name="caller">The caller's resolver, or null where there is none.</param>
        /// <returns>The collation.</returns>
        /// <exception cref="XsltException">The URI names no collation either has — <c>FOCH0002</c>.</exception>
        public static Collation Resolve(string uri, IXsltCollationResolver? caller)
        {
            if (uri.Length == 0 || uri == CodepointUri)
            {
                return Codepoint;
            }

            if (IsProvided(uri))
            {
                lock (s_cache)
                {
                    if (s_cache.TryGetValue(uri, out Collation? cached))
                    {
                        return cached;
                    }
                }

                Collation resolved = Build(uri);

                lock (s_cache)
                {
                    s_cache[uri] = resolved;
                }

                return resolved;
            }

            if (caller is not null && FromCaller(caller, uri) is Collation supplied)
            {
                return supplied;
            }

            throw Unknown(
                uri,
                caller is null
                    ? "no collation of that name is provided here, and no collation resolver is configured "
                        + "(XsltOptions.CollationResolver) to supply one"
                    : "no collation of that name is provided here, and the collation resolver has none by it");
        }

        /// <summary>
        /// Finds the collation a URI names where an expression is being evaluated: the caller's resolver
        /// is the running transformation's, or the context's own where nothing is running.
        /// </summary>
        /// <param name="uri">The URI as written.</param>
        /// <param name="context">The evaluation context.</param>
        public static Collation Resolve(string uri, ref DynamicContext context)
        {
            return Resolve(uri, ResolverOf(ref context));
        }

        /// <summary>The caller's resolver in force where an expression is being evaluated, if any.</summary>
        public static IXsltCollationResolver? ResolverOf(ref DynamicContext context)
        {
            return context.Runtime?.CollationResolver ?? context.Collations;
        }

        /// <summary>Whether a URI names one of the collations this engine provides itself.</summary>
        private static bool IsProvided(string uri)
        {
            return uri == HtmlAsciiCaseInsensitiveUri
                || uri == UcaUri
                || uri.StartsWith(UcaUri + "?", StringComparison.Ordinal);
        }

        private static Collation? FromCaller(IXsltCollationResolver caller, string uri)
        {
            Dictionary<string, Collation> known = s_callers.GetValue(
                caller, _ => new Dictionary<string, Collation>(StringComparer.Ordinal));

            lock (known)
            {
                if (known.TryGetValue(uri, out Collation? cached))
                {
                    return cached;
                }
            }

            XsltCollation? supplied = caller.Resolve(uri);

            if (supplied is null)
            {
                return null;
            }

            Collation wrapped = new Collation(uri, supplied);

            lock (known)
            {
                known[uri] = wrapped;
            }

            return wrapped;
        }

        /// <summary>Compares two strings, answering -1, 0 or 1.</summary>
        public int Compare(string first, string second)
        {
            if (m_custom is not null)
            {
                int answer = m_custom.Compare(first, second);
                return answer < 0 ? -1 : answer > 0 ? 1 : 0;
            }

            if (m_compare is null)
            {
                return CompareByCodePoint(Fold(first), Fold(second));
            }

            int sign = m_compare.Compare(first, second, m_options);

            if (sign == 0 && m_identical)
            {
                sign = CompareByCodePoint(first, second);
            }

            return sign < 0 ? -1 : sign > 0 ? 1 : 0;
        }

        /// <summary>Whether two strings are the same string under this collation.</summary>
        public bool AreEqual(string first, string second)
        {
            if (m_custom is not null)
            {
                return m_custom.AreEqual(first, second);
            }

            return m_compare is null
                ? string.Equals(Fold(first), Fold(second), StringComparison.Ordinal)
                : Compare(first, second) == 0;
        }

        /// <summary>
        /// A key on which two strings agree exactly when this collation calls them the same string.
        /// </summary>
        /// <remarks>
        /// What <c>distinct-values</c> needs, and the only way to answer it in one pass: under a collation
        /// that ignores case, <c>DATA</c> and <c>data</c> have to land in the same bucket rather than be
        /// compared with everything already seen.
        /// </remarks>
        public string Key(string value)
        {
            if (m_custom is not null)
            {
                return m_custom.Key(value)
                    ?? throw XsltErrors.Error(
                        XsltErrorCode.FOCH0004,
                        $"The collation '{m_uri}' makes no collation key, and one is needed here: grouping, "
                        + "keys, distinct-values() and fn:collation-key() file strings under a key the "
                        + "collation supplies. A collation of the caller's own supplies one by overriding "
                        + "XsltCollation.Key.");
            }

            return m_compare is null
                ? Fold(value)
                : Convert.ToBase64String(m_compare.GetSortKey(value, m_options).KeyData);
        }

        /// <summary>
        /// The same key as bytes, which is what <c>fn:collation-key</c> hands back.
        /// </summary>
        /// <remarks>
        /// The bytes must sort as the strings do, since the whole point of the function is to answer
        /// comparisons without the collation. UTF-8 does that for the code point collation — its byte order
        /// is code point order — and ICU builds its sort keys to be compared as bytes. Where the strength is
        /// <c>identical</c> the code points follow the sort key, since that is the tie the key cannot break.
        /// </remarks>
        public byte[] KeyBytes(string value)
        {
            if (m_custom is not null)
            {
                // The caller's key, as UTF-8: it orders as the strings do where the caller kept to what
                // XsltCollation.Key asks, which is all that can be done from here.
                return Encoding.UTF8.GetBytes(Key(value));
            }

            if (m_compare is null)
            {
                return Encoding.UTF8.GetBytes(Fold(value));
            }

            byte[] key = m_compare.GetSortKey(value, m_options).KeyData;

            if (!m_identical)
            {
                return key;
            }

            byte[] text = Encoding.UTF8.GetBytes(value);
            byte[] both = new byte[key.Length + 1 + text.Length];

            key.CopyTo(both, 0);
            text.CopyTo(both, key.Length + 1);
            return both;
        }

        /// <summary>
        /// Whether one string begins with another.
        /// </summary>
        /// <remarks>
        /// The specification anchors this on what the characters weigh, and ICU anchors it on where they
        /// stand, which differ under a collation that weighs some of them as nothing: <c>-abcdefghi</c>
        /// begins with <c>abc</c> when the hyphen counts for nothing, and <c>IsPrefix</c> says otherwise
        /// because the match does not start at the first character. So a refusal is checked again, against
        /// where the match does start and whether what precedes it weighs anything.
        /// </remarks>
        public bool StartsWith(string subject, string prefix)
        {
            if (m_custom is not null)
            {
                return RequireSubstrings().StartsWith(subject, prefix);
            }

            if (m_compare is null)
            {
                return Fold(subject).StartsWith(Fold(prefix), StringComparison.Ordinal);
            }

            if (m_compare.IsPrefix(subject, prefix, m_options))
            {
                return true;
            }

            int at = m_compare.IndexOf(subject, prefix, m_options, out int _);
            return at > 0 && m_compare.Compare(subject[..at], string.Empty, m_options) == 0;
        }

        /// <summary>Whether one string ends with another, read the same way round.</summary>
        public bool EndsWith(string subject, string suffix)
        {
            if (m_custom is not null)
            {
                return RequireSubstrings().EndsWith(subject, suffix);
            }

            if (m_compare is null)
            {
                return Fold(subject).EndsWith(Fold(suffix), StringComparison.Ordinal);
            }

            if (m_compare.IsSuffix(subject, suffix, m_options))
            {
                return true;
            }

            int at = m_compare.LastIndexOf(subject, suffix, m_options, out int length);
            return at >= 0
                && at + length < subject.Length
                && m_compare.Compare(subject[(at + length)..], string.Empty, m_options) == 0;
        }

        /// <summary>Whether one string appears within another.</summary>
        public bool Contains(string subject, string sought)
        {
            return IndexOf(subject, sought, out _) >= 0;
        }

        /// <summary>
        /// Where one string appears within another, and how much of the subject the match took.
        /// </summary>
        /// <remarks>
        /// The length is not the sought string's: under a collation that ignores diacritics, four characters
        /// may match five. <c>substring-after</c> resumes from the end of what actually matched, which is the
        /// only place that difference can be seen. Folding ASCII does not move anything, one character for
        /// one, so an index into the folded string is an index into the original.
        /// </remarks>
        /// <param name="subject">The string to search.</param>
        /// <param name="sought">The string to find.</param>
        /// <param name="length">On a match, how many characters of the subject it covered.</param>
        /// <returns>The index of the match, or -1.</returns>
        public int IndexOf(string subject, string sought, out int length)
        {
            if (m_custom is not null)
            {
                return RequireSubstrings().IndexOf(subject, sought, out length);
            }

            if (m_compare is null)
            {
                length = sought.Length;
                return Fold(subject).IndexOf(Fold(sought), StringComparison.Ordinal);
            }

            return m_compare.IndexOf(subject, sought, m_options, out length);
        }

        /// <summary>
        /// The caller's collation, where it matches substrings; the specification's <c>FOCH0004</c> where
        /// it does not, that being the code for a collation the substring functions cannot use.
        /// </summary>
        private XsltCollation RequireSubstrings()
        {
            if (!m_custom!.SupportsSubstringMatching)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOCH0004,
                    $"The collation '{m_uri}' does not match substrings, which contains(), starts-with(), "
                    + "ends-with(), substring-before() and substring-after() need. A collation of the "
                    + "caller's own supports them by overriding XsltCollation.SupportsSubstringMatching and "
                    + "the three matching methods.");
            }

            return m_custom;
        }

        /// <summary>Folds ASCII letters where the collation asks for it, and otherwise does nothing.</summary>
        private string Fold(string value)
        {
            if (!m_foldAscii)
            {
                return value;
            }

            return string.Create(value.Length, value, static (buffer, source) =>
            {
                for (int i = 0; i < source.Length; i++)
                {
                    char c = source[i];
                    buffer[i] = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
                }
            });
        }

        /// <summary>
        /// Compares by code point, which above the basic plane is not comparing by UTF-16 unit: U+10001 is
        /// written as a surrogate pair starting at U+D800, and so sorts before U+FFF0 the wrong way round.
        /// </summary>
        private static int CompareByCodePoint(string first, string second)
        {
            int i = 0;
            int j = 0;

            while (i < first.Length && j < second.Length)
            {
                int left = CodePointAt(first, ref i);
                int right = CodePointAt(second, ref j);

                if (left != right)
                {
                    return left < right ? -1 : 1;
                }
            }

            return i < first.Length ? 1 : j < second.Length ? -1 : 0;
        }

        private static int CodePointAt(string text, ref int index)
        {
            if (char.IsHighSurrogate(text[index])
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1]))
            {
                int code = char.ConvertToUtf32(text[index], text[index + 1]);
                index += 2;
                return code;
            }

            return text[index++];
        }

        private static Collation Build(string uri)
        {
            if (uri == HtmlAsciiCaseInsensitiveUri)
            {
                return new Collation(null, CompareOptions.None, true, false);
            }

            if (uri == UcaUri)
            {
                return new Collation(CultureInfo.InvariantCulture.CompareInfo, CompareOptions.None, false, false);
            }

            if (uri.StartsWith(UcaUri + "?", StringComparison.Ordinal))
            {
                return Uca(uri, uri[(UcaUri.Length + 1)..]);
            }

            throw Unknown(uri, "no collation of that name is provided here");
        }

        /// <summary>
        /// Reads the parameters on a UCA collation URI.
        /// </summary>
        /// <remarks>
        /// Every parameter is either honoured or refused, and a refusal only becomes an error where the URI
        /// says <c>fallback=no</c>. Honoured: <c>lang</c>, <c>strength</c>, <c>caseLevel</c>,
        /// <c>normalization</c>, and <c>caseFirst=lower</c>, which is what ICU already does for a language
        /// with no rule of its own. Refused — <c>alternate</c>, <c>backwards</c>, <c>caseFirst=upper</c>,
        /// <c>maxVariable</c>, <c>numeric</c>, <c>reorder</c>, <c>version</c>, quaternary strength — are
        /// things <see cref="CompareOptions"/> cannot express.
        /// </remarks>
        private static Collation Uca(string uri, string query)
        {
            CultureInfo culture = CultureInfo.InvariantCulture;
            int strength = 3;
            bool caseLevel = false;
            bool blankVariables = false;
            bool fallback = true;
            string? refused = null;

            foreach (string parameter in query.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = parameter.IndexOf('=');

                if (equals < 0)
                {
                    throw Unknown(uri, $"'{parameter}' is not a name and a value");
                }

                string key = parameter[..equals];
                string value = parameter[(equals + 1)..];

                switch (key)
                {
                    case "fallback":
                        fallback = value switch
                        {
                            "yes" => true,
                            "no" => false,
                            _ => throw Unknown(uri, $"'fallback' is yes or no, not '{value}'"),
                        };

                        break;

                    case "lang":
                        try
                        {
                            culture = CultureInfo.GetCultureInfo(value);
                        }
                        catch (CultureNotFoundException)
                        {
                            refused ??= $"the language '{value}'";
                        }

                        break;

                    case "strength":
                        switch (value)
                        {
                            case "primary" or "1": strength = 1; break;
                            case "secondary" or "2": strength = 2; break;
                            case "tertiary" or "3": strength = 3; break;
                            case "identical" or "5": strength = 5; break;
                            case "quaternary" or "4": strength = 3; refused ??= "quaternary strength"; break;
                            default: throw Unknown(uri, $"'{value}' is not a collation strength");
                        }

                        break;

                    case "caseLevel":
                        caseLevel = value == "yes";
                        break;

                    case "normalization":
                        // Comparison here is canonical-equivalence-aware whichever way this is set, and 'no'
                        // only says the processor need not bother.
                        break;

                    case "caseFirst":
                        if (value != "lower")
                        {
                            refused ??= $"'caseFirst={value}'";
                        }

                        break;

                    case "alternate":
                        // 'blanked' says the variable characters — punctuation, spaces, symbols — weigh
                        // nothing at all, which is what IgnoreSymbols does. 'shifted' weighs them at the
                        // fourth level, and .NET has no way to say that.
                        switch (value)
                        {
                            case "blanked": blankVariables = true; break;
                            case "non-ignorable": break;
                            default: refused ??= $"'alternate={value}'"; break;
                        }

                        break;

                    default:
                        refused ??= $"'{key}'";
                        break;
                }
            }

            if (refused is not null && !fallback)
            {
                throw Unknown(
                    uri,
                    $"{refused} is beyond the collation this engine can build, and the URI says fallback=no");
            }

            CompareOptions options = strength switch
            {
                1 => CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace
                    | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType,
                2 => CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType,
                _ => CompareOptions.None,
            };

            if (caseLevel)
            {
                // Case is weighed again on top of the chosen strength, which is what the parameter is for.
                options &= ~CompareOptions.IgnoreCase;
            }

            if (blankVariables)
            {
                options |= CompareOptions.IgnoreSymbols;
            }

            return new Collation(culture.CompareInfo, options, false, strength == 5);
        }

        private static XsltException Unknown(string uri, string why)
        {
            return XsltErrors.Error(
                XsltErrorCode.FOCH0002,
                $"'{uri}' is not a collation this engine has, because {why}. It provides the code point "
                + $"collation '{CodepointUri}', the HTML ASCII case-insensitive one, and the Unicode "
                + $"Collation Algorithm at '{UcaUri}' with its lang and strength parameters; a caller "
                + "supplies others through XsltOptions.CollationResolver.");
        }
    }
}
