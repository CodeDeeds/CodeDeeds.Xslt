using System.Globalization;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// The version of a package, as XSLT 3.0 §3.5.1 defines it: integers separated by dots, and after
    /// them, at most, a hyphen and a name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two versions are ordered portion by portion, trailing zero integers first discarded, so that
    /// <c>1</c> and <c>1.0</c> are one version. A name sorts before an integer where the two meet, which
    /// is what makes <c>2.0-rc1</c> earlier than <c>2.0</c>, and a version that is a prefix of another is
    /// earlier where the other goes on with an integer and later where it goes on with a name:
    /// <c>1.2</c> is before <c>1.2.5</c>, and <c>1.0.3-rc1</c> is before <c>1.0.3</c>.
    /// </para>
    /// <para>
    /// The name portion is one NCName, hyphens included: <c>1-alpha-2</c> has two portions, and the second
    /// hyphen is part of the name. The specification is explicit about it.
    /// </para>
    /// </remarks>
    internal sealed class PackageVersion : IComparable<PackageVersion>
    {
        private readonly long[] m_numbers;
        private readonly string? m_name;

        private PackageVersion(long[] numbers, string? name, string written)
        {
            m_numbers = numbers;
            m_name = name;
            Written = written;
        }

        /// <summary>The version as written, whitespace trimmed.</summary>
        public string Written { get; }

        /// <summary>Parses a version, or returns null where the text is not one.</summary>
        /// <param name="text">The version as written.</param>
        public static PackageVersion? TryParse(string text)
        {
            text = text.Trim();

            if (text.Length == 0)
            {
                return null;
            }

            string numeric = text;
            string? name = null;
            int hyphen = text.IndexOf('-');

            if (hyphen >= 0)
            {
                numeric = text[..hyphen];
                name = text[(hyphen + 1)..];

                if (!IsNcName(name))
                {
                    return null;
                }
            }

            string[] parts = numeric.Split('.');
            long[] numbers = new long[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0
                    || !parts[i].All(char.IsAsciiDigit)
                    || !long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    return null;
                }
            }

            return new PackageVersion(numbers, name, text);
        }

        /// <summary>Whether a version's portions begin with this version's, which is what <c>V.*</c> asks.</summary>
        /// <param name="other">The version being tested.</param>
        public bool IsPrefixOf(PackageVersion other)
        {
            if (m_name is not null)
            {
                // A name ends a version, so nothing can follow it: only the same version has it as a prefix.
                return CompareTo(other) == 0;
            }

            long[] mine = Trimmed(m_numbers);
            long[] theirs = other.m_numbers;

            if (theirs.Length < mine.Length)
            {
                return false;
            }

            for (int i = 0; i < mine.Length; i++)
            {
                if (theirs[i] != mine[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public int CompareTo(PackageVersion? other)
        {
            if (other is null)
            {
                return 1;
            }

            long[] mine = Trimmed(m_numbers);
            long[] theirs = Trimmed(other.m_numbers);
            int shared = Math.Min(mine.Length, theirs.Length);

            for (int i = 0; i < shared; i++)
            {
                int order = mine[i].CompareTo(theirs[i]);

                if (order != 0)
                {
                    return order;
                }
            }

            // The integers agree as far as both go. What comes next on either side decides: an integer
            // makes the longer one later, a name makes it earlier, and two names are compared as strings.
            if (mine.Length != theirs.Length)
            {
                bool mineLonger = mine.Length > theirs.Length;
                string? shorterName = mineLonger ? other.m_name : m_name;

                // The shorter side goes on with a name or ends; the longer side goes on with an integer.
                // An integer beats a name and beats an end, so the longer side is later either way.
                return shorterName is null || true ? (mineLonger ? 1 : -1) : 0;
            }

            if (m_name is null && other.m_name is null)
            {
                return 0;
            }

            if (m_name is null)
            {
                return 1;
            }

            if (other.m_name is null)
            {
                return -1;
            }

            return string.CompareOrdinal(m_name, other.m_name) switch
            {
                < 0 => -1,
                > 0 => 1,
                _ => 0,
            };
        }

        private static long[] Trimmed(long[] numbers)
        {
            int length = numbers.Length;

            while (length > 1 && numbers[length - 1] == 0)
            {
                length--;
            }

            return length == numbers.Length ? numbers : numbers[..length];
        }

        internal static bool IsNcName(string text)
        {
            // The name part is an NCName (§3.5.1), by XML 1.0's name characters in their fifth edition —
            // which is the edition the suite leans on: the ideographic description characters and the CJK
            // punctuation it writes into a version are name characters there and not in the tables .NET
            // keeps, and a character of a private-use plane is a name character in neither.
            if (text.Length == 0)
            {
                return false;
            }

            for (int at = 0; at < text.Length;)
            {
                if (char.IsSurrogate(text[at]) && !char.IsSurrogatePair(text, at))
                {
                    return false;
                }

                int code = char.ConvertToUtf32(text, at);

                if (!(at == 0 ? IsNameStartChar(code) : IsNameChar(code)))
                {
                    return false;
                }

                at += code > 0xFFFF ? 2 : 1;
            }

            return true;
        }

        /// <summary>XML 1.0 fifth edition's NameStartChar, less the colon an NCName may not hold.</summary>
        private static bool IsNameStartChar(int code)
        {
            return code is (>= 'A' and <= 'Z') or '_' or (>= 'a' and <= 'z')
                or (>= 0xC0 and <= 0xD6) or (>= 0xD8 and <= 0xF6) or (>= 0xF8 and <= 0x2FF)
                or (>= 0x370 and <= 0x37D) or (>= 0x37F and <= 0x1FFF) or (>= 0x200C and <= 0x200D)
                or (>= 0x2070 and <= 0x218F) or (>= 0x2C00 and <= 0x2FEF) or (>= 0x3001 and <= 0xD7FF)
                or (>= 0xF900 and <= 0xFDCF) or (>= 0xFDF0 and <= 0xFFFD) or (>= 0x10000 and <= 0xEFFFF);
        }

        /// <summary>XML 1.0 fifth edition's NameChar, less the colon.</summary>
        private static bool IsNameChar(int code)
        {
            return IsNameStartChar(code)
                || code is '-' or '.' or (>= '0' and <= '9') or 0xB7
                    or (>= 0x300 and <= 0x36F) or (>= 0x203F and <= 0x2040);
        }

        /// <inheritdoc/>
        public override string ToString() => Written;
    }

    /// <summary>
    /// The versions an <c>xsl:use-package</c> will take, as its <c>package-version</c> writes them: one
    /// version, a prefix, a lower bound, an upper bound, both, any of those separated by commas, or
    /// <c>*</c> for whatever there is.
    /// </summary>
    internal sealed class PackageVersionRange
    {
        private readonly List<(PackageVersion? From, PackageVersion? To, bool ToIsPrefix, bool Exact, bool Prefix)> m_ranges = new();
        private readonly bool m_any;

        private PackageVersionRange(bool any)
        {
            m_any = any;
        }

        /// <summary>The range that takes any version, which is what an absent attribute means.</summary>
        public static PackageVersionRange Any { get; } = new PackageVersionRange(true);

        /// <summary>
        /// Chooses the version of a package to load: the highest of those on offer that this range takes.
        /// </summary>
        /// <remarks>
        /// The specification leaves the choice to the processor, and the latest is what a caller naming a
        /// range means. A resolver that knows none of its versions offers nothing, and is then asked with
        /// null — what the package it supplies declares of itself is checked against the range once read.
        /// </remarks>
        /// <param name="resolver">The resolver, or null where it cannot say what it holds.</param>
        /// <param name="name">The package name being asked about.</param>
        /// <param name="offered">Whether the resolver named any version at all.</param>
        /// <returns>The version to ask for, or null to ask without naming one.</returns>
        public string? Best(IXsltPackageResolver? resolver, string name, out bool offered)
        {
            PackageVersion? best = null;
            offered = false;

            if (resolver is null)
            {
                return null;
            }

            foreach (string version in resolver.VersionsOf(name))
            {
                offered = true;
                PackageVersion? candidate = PackageVersion.TryParse(version);

                if (candidate is not null && Matches(candidate) && (best is null || candidate.CompareTo(best) > 0))
                {
                    best = candidate;
                }
            }

            return best?.Written;
        }

        /// <summary>Parses a range, or returns null where the text is not one.</summary>
        /// <param name="text">The range as written.</param>
        public static PackageVersionRange? TryParse(string text)
        {
            text = text.Trim();

            if (text == "*")
            {
                return Any;
            }

            PackageVersionRange range = new PackageVersionRange(false);

            foreach (string piece in text.Split(','))
            {
                string part = piece.Trim();

                if (part.Length == 0 || !range.AddRange(part))
                {
                    return null;
                }
            }

            return range;
        }

        private bool AddRange(string part)
        {
            // "to V" and "V1 to V2", the word set off by whitespace on either side.
            int to = IndexOfTo(part);

            if (part.StartsWith("to ", StringComparison.Ordinal) || part.StartsWith("to\t", StringComparison.Ordinal))
            {
                return AddBounded(null, part[2..].Trim());
            }

            if (to > 0)
            {
                string from = part[..to].Trim();
                PackageVersion? lower = PackageVersion.TryParse(from);

                return lower is not null && AddBounded(lower, part[(to + 2)..].Trim());
            }

            if (part.EndsWith('+'))
            {
                PackageVersion? lower = PackageVersion.TryParse(part[..^1]);

                if (lower is null)
                {
                    return false;
                }

                m_ranges.Add((lower, null, false, false, false));
                return true;
            }

            if (part.EndsWith(".*", StringComparison.Ordinal))
            {
                PackageVersion? prefix = PackageVersion.TryParse(part[..^2]);

                if (prefix is null)
                {
                    return false;
                }

                m_ranges.Add((prefix, null, false, false, true));
                return true;
            }

            PackageVersion? exact = PackageVersion.TryParse(part);

            if (exact is null)
            {
                return false;
            }

            m_ranges.Add((exact, null, false, true, false));
            return true;
        }

        private bool AddBounded(PackageVersion? lower, string upper)
        {
            bool prefix = upper.EndsWith(".*", StringComparison.Ordinal);
            PackageVersion? bound = PackageVersion.TryParse(prefix ? upper[..^2] : upper);

            if (bound is null)
            {
                return false;
            }

            m_ranges.Add((lower, bound, prefix, false, false));
            return true;
        }

        private static int IndexOfTo(string part)
        {
            for (int i = 1; i + 3 < part.Length; i++)
            {
                if (char.IsWhiteSpace(part[i - 1])
                    && part[i] == 't'
                    && part[i + 1] == 'o'
                    && char.IsWhiteSpace(part[i + 2]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Whether a version is one the range takes.</summary>
        /// <param name="version">The version a package declares.</param>
        public bool Matches(PackageVersion version)
        {
            if (m_any)
            {
                return true;
            }

            foreach ((PackageVersion? from, PackageVersion? to, bool toIsPrefix, bool exact, bool prefix) in m_ranges)
            {
                if (exact)
                {
                    if (from!.CompareTo(version) == 0)
                    {
                        return true;
                    }

                    continue;
                }

                if (prefix)
                {
                    if (from!.IsPrefixOf(version))
                    {
                        return true;
                    }

                    continue;
                }

                if (from is not null && from.CompareTo(version) > 0)
                {
                    continue;
                }

                // "to V" takes anything up to V; "to V.*" anything up to some version V is a prefix of,
                // which is V itself, or a version below it, or any version that begins with V.
                if (to is null
                    || to.CompareTo(version) >= 0
                    || (toIsPrefix && to.IsPrefixOf(version)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
