using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Turns an XML Schema regular expression into a .NET one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two dialects agree on most of their syntax, and .NET even supports the character-class subtraction
    /// that XSD uses. What differs is the multi-character escapes XSD adds for XML name characters — and the
    /// flags, which XPath spells as letters in a string.
    /// </para>
    /// <para>
    /// They also differ in what they <em>refuse</em>, and that is why a pattern is checked here before .NET
    /// sees it. .NET's language is the larger of the two, so a pattern XPath has no reading for is one .NET
    /// will happily compile into something else — a stylesheet that works here and fails on a conformant
    /// processor, which is the quietest kind of difference there is.
    /// </para>
    /// </remarks>
    internal static class RegexTranslator
    {
        /// <summary>
        /// The characters an XML name may begin with, which is what <c>\i</c> stands for: XML 1.0's
        /// NameStartChar in its fifth edition, which XSD 1.1 and so XPath 3.0 define the escape by, plus the
        /// colon XSD keeps in it.
        /// </summary>
        private static readonly (int Low, int High)[] NameStartRanges =
        {
            (':', ':'), ('A', 'Z'), ('_', '_'), ('a', 'z'),
            (0x00C0, 0x00D6), (0x00D8, 0x00F6), (0x00F8, 0x02FF), (0x0370, 0x037D), (0x037F, 0x1FFF),
            (0x200C, 0x200D), (0x2070, 0x218F), (0x2C00, 0x2FEF), (0x3001, 0xD7FF), (0xF900, 0xFDCF),
            (0xFDF0, 0xFFFD), (0x10000, 0xEFFFF),
        };

        /// <summary>
        /// The same two sets by XML 1.0's fourth edition, which XSD 1.0 and so XPath 2.0 defined the escapes
        /// by: the letter tables Unicode had in 2000 rather than the ranges the fifth edition drew. .NET keeps
        /// those tables, and they are read once into runs — a 2.0 stylesheet's <c>\c</c> does not match the
        /// combining bridge above, U+0346, and a 3.0 one's does, and the suite asks both.
        /// </summary>
        private static readonly Lazy<(int Low, int High)[]> s_fourthEditionStart =
            new(() => Runs(c => c == ':' || System.Xml.XmlConvert.IsStartNCNameChar(c)));

        private static readonly Lazy<(int Low, int High)[]> s_fourthEditionRest =
            new(() => Runs(c => System.Xml.XmlConvert.IsNCNameChar(c) && !System.Xml.XmlConvert.IsStartNCNameChar(c)));

        /// <summary>The runs of code units of the basic plane a test admits, in order.</summary>
        private static (int Low, int High)[] Runs(Func<char, bool> admits)
        {
            List<(int Low, int High)> runs = new List<(int Low, int High)>();
            int start = -1;

            for (int code = 0; code <= 0xFFFF; code++)
            {
                bool inRun = !char.IsSurrogate((char)code) && admits((char)code);

                if (inRun && start < 0)
                {
                    start = code;
                }
                else if (!inRun && start >= 0)
                {
                    runs.Add((start, code - 1));
                    start = -1;
                }
            }

            if (start >= 0)
            {
                runs.Add((start, 0xFFFF));
            }

            return runs.ToArray();
        }

        /// <summary>What <c>\c</c> adds to <see cref="NameStartRanges"/>: a name may not begin with these.</summary>
        private static readonly (int Low, int High)[] NameRestRanges =
        {
            ('-', '-'), ('.', '.'), ('0', '9'),
            (0x00B7, 0x00B7), (0x0300, 0x036F), (0x203F, 0x2040),
        };

        /// <summary>The category <c>\d</c> names, which is the decimal digits of every script.</summary>
        private static readonly uint DecimalDigits = UnicodeCategories.Mask("Nd")!.Value;

        /// <summary>
        /// What a word character is not: punctuation, a separator, or one of the others.
        /// </summary>
        /// <remarks>
        /// This is the definition XML Schema gives, and it is not .NET's. .NET's <c>\w</c> is the letters,
        /// the non-spacing marks, the decimal digits and the connectors — which puts <c>_</c> inside it,
        /// where Schema's puts it outside, the underscore being connector punctuation.
        /// </remarks>
        private static readonly uint NotWord =
            UnicodeCategories.Mask("P")!.Value
            | UnicodeCategories.Mask("Z")!.Value
            | UnicodeCategories.Mask("C")!.Value;

        private static readonly Dictionary<string, Regex> s_cache = new(StringComparer.Ordinal);

        /// <summary>Compiles an XSD regular expression with XPath flags.</summary>
        /// <param name="pattern">The pattern, in XML Schema syntax.</param>
        /// <param name="flags">Any of <c>s</c>, <c>m</c>, <c>i</c> and <c>x</c>, and <c>q</c> from 3.0.</param>
        /// <param name="version">
        /// The version in force. XPath 3.0 adds two things to this language and nothing else: the
        /// non-capturing group <c>(?:</c> and the <c>q</c> flag. Both are refused below it, because a pattern
        /// this engine reads and a conformant 2.0 processor does not is the quietest kind of difference there
        /// is — the stylesheet works here and fails there.
        /// </param>
        /// <exception cref="XsltException">The flags or the pattern are not ones XPath has.</exception>
        public static Regex Translate(string pattern, string flags, XsltVersion version)
        {
            bool three = version.CompareTo(XsltVersion.V30) >= 0;
            // The version is part of the key: the same pattern and flags ask a different question at
            // 3.0, where one may compile and the other be refused.
            string key = flags + "\0" + (three ? '3' : '2') + "\0" + pattern;

            lock (s_cache)
            {
                if (s_cache.TryGetValue(key, out Regex? cached))
                {
                    return cached;
                }
            }

            RegexOptions options = RegexOptions.CultureInvariant;
            bool literal = false;
            bool ignoreWhitespace = false;

            foreach (char flag in flags)
            {
                switch (flag)
                {
                    case 's':
                        options |= RegexOptions.Singleline;
                        break;

                    case 'm':
                        options |= RegexOptions.Multiline;
                        break;

                    case 'i':
                        options |= RegexOptions.IgnoreCase;
                        break;

                    case 'x':
                        // Done below rather than by RegexOptions.IgnorePatternWhitespace, which is a
                        // different rule: .NET also treats '#' as starting a comment, and leaves whitespace
                        // inside a construct like \p{ IsBasicLatin } where XPath removes it.
                        ignoreWhitespace = true;
                        break;

                    case 'q' when three:
                        literal = true;
                        break;

                    default:
                        throw XsltErrors.Error(XsltErrorCode.FORX0001, $"'{flag}' is not a valid regular expression flag.");
                }
            }

            string translated;

            if (literal)
            {
                // Nothing in a literal pattern is syntax, so there is nothing to check or rewrite.
                translated = Regex.Escape(pattern);
            }
            else
            {
                string source = ignoreWhitespace ? RemoveWhitespace(pattern) : pattern;

                Validate(source, three);
                translated = ToDotNet(source, options, three);
            }

            Regex compiled;
            try
            {
                compiled = new Regex(translated, options);
            }
            catch (ArgumentException exception)
            {
                throw XsltErrors.Error(XsltErrorCode.FORX0002, $"'{pattern}' is not a valid regular expression: {exception.Message}");
            }

            lock (s_cache)
            {
                s_cache[key] = compiled;
            }

            return compiled;
        }

        /// <summary>
        /// Refuses a pattern that matches the empty string, which <c>replace</c> and <c>tokenize</c> cannot use.
        /// </summary>
        /// <remarks>
        /// Replacing something that occupies no width would never move forward, and splitting on it would
        /// give a token between every pair of characters. <c>matches</c> has no such difficulty and is not
        /// checked: <c>matches('a', '')</c> is honestly true.
        /// </remarks>
        /// <param name="regex">The compiled pattern.</param>
        /// <param name="pattern">The pattern as written, for the message.</param>
        /// <param name="function">The function that will not take it, for the message.</param>
        /// <exception cref="XsltException">The pattern matches a zero-length string.</exception>
        public static void RequireWidth(Regex regex, string pattern, string function)
        {
            if (regex.IsMatch(string.Empty))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FORX0003,
                    $"fn:{function}() cannot use '{pattern}', which matches a zero-length string: there "
                    + "would be a match at every position and between every pair of characters.");
            }
        }

        /// <summary>
        /// Rewrites a replacement string from XPath's form to the one .NET expects, refusing what is neither.
        /// </summary>
        /// <remarks>
        /// XPath allows exactly two things: <c>$N</c> for one digit N, naming what the Nth group captured,
        /// and a backslash before a <c>\</c> or a <c>$</c> to mean that character. Everything else is
        /// <c>FORX0004</c> — where .NET would read <c>$y</c> as a literal, <c>\1</c> as the digit 1, and
        /// <c>$12</c> as the twelfth group rather than the first followed by a 2.
        /// </remarks>
        /// <param name="replacement">The replacement as the stylesheet wrote it.</param>
        /// <exception cref="XsltException">The replacement is not one XPath allows.</exception>
        /// <param name="replacement">The replacement as the stylesheet wrote it.</param>
        /// <param name="pattern">The compiled pattern, which says how many groups there are to name.</param>
        /// <exception cref="XsltException">The replacement is not one XPath allows.</exception>
        public static string TranslateReplacement(string replacement, Regex pattern)
        {
            int groups = pattern.GetGroupNumbers().Length - 1;
            StringBuilder builder = new StringBuilder(replacement.Length);

            for (int i = 0; i < replacement.Length; i++)
            {
                char character = replacement[i];

                if (character == '\\')
                {
                    char? escaped = i + 1 < replacement.Length ? replacement[i + 1] : null;

                    if (escaped is not ('\\' or '$'))
                    {
                        throw BadReplacement(
                            replacement,
                            "a backslash may only be followed by another backslash or a dollar sign");
                    }

                    // A dollar means itself here, and .NET spells that by doubling it.
                    builder.Append(escaped == '$' ? "$$" : "\\");
                    i++;
                    continue;
                }

                if (character == '$')
                {
                    if (i + 1 >= replacement.Length || replacement[i + 1] is not (>= '0' and <= '9'))
                    {
                        throw BadReplacement(
                            replacement, "a dollar sign must be followed by a digit naming a group");
                    }

                    int group = ReadGroupNumber(replacement, ref i, groups);

                    // A group the pattern has not got contributes nothing, which is what the specification
                    // says a $N naming more groups than there are comes to. .NET leaves the reference
                    // standing as text instead, so '[$2]#$5#' against a pattern of two groups would write
                    // the ${5} out.
                    if (group > groups)
                    {
                        continue;
                    }

                    // Braced, so that a digit of the replacement text is not read as part of the number:
                    // with one group, $12 is that group followed by a 2 and not the twelfth.
                    builder.Append("${").Append(group).Append('}');
                    continue;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Reads the digits naming a group, taking as many as name one that exists.
        /// </summary>
        /// <remarks>
        /// The rule both <c>\N</c> and <c>$N</c> follow, and the reason neither can be read as a single
        /// digit or as every digit going: <c>$15</c> is the fifteenth group where there are fifteen, and the
        /// first followed by a <c>5</c> where there are not.
        /// </remarks>
        /// <param name="text">The pattern or replacement being read.</param>
        /// <param name="index">The index of the first digit on entry, of the last on return.</param>
        /// <param name="groups">How many capturing groups there are to name.</param>
        private static int ReadGroupNumber(string text, ref int index, int groups)
        {
            int number = text[++index] - '0';

            while (index + 1 < text.Length && text[index + 1] is >= '0' and <= '9')
            {
                int wider = (number * 10) + (text[index + 1] - '0');
                if (wider > groups)
                {
                    break;
                }

                number = wider;
                index++;
            }

            return number;
        }

        private static XsltException BadReplacement(string replacement, string why)
        {
            return XsltErrors.Error(
                XsltErrorCode.FORX0004, $"'{replacement}' is not a valid replacement string: {why}.");
        }

        /// <summary>
        /// Checks that a pattern is one XPath has a reading for, before .NET gives it a different one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two things are checked, and both are places where .NET accepts what XPath does not rather than
        /// the other way about. A <b>back-reference</b> may only name a group that is complete to its left:
        /// <c>(a\1)</c> and <c>(a)\2(b)</c> name a group that is still open and one that does not exist, and
        /// no back-reference belongs in a character class at all, where <c>[\1]</c> would be read as an
        /// escape. A <b>hyphen</b> in a character class is either part of a range or the start of a
        /// subtraction, so the one in <c>[0-9-.]</c> is neither.
        /// </para>
        /// <para>
        /// Everything .NET's syntax has and XSD's does not — lookahead, non-capturing groups, word
        /// boundaries — is left alone here and recorded as a difference instead. Refusing them is right by
        /// the specification and would break a stylesheet that uses one and works today, which is a trade
        /// worth making deliberately rather than in passing.
        /// </para>
        /// </remarks>
        /// <param name="pattern">The pattern as written.</param>
        /// <exception cref="XsltException">The pattern is not one XPath's grammar admits.</exception>
        private static void Validate(string pattern, bool three)
        {
            // Counted first, because a back-reference takes as many digits as name a group that exists:
            // '\11' is the eleventh group where there are eleven, and the first followed by a literal 1
            // where there are not.
            int groups = CountGroups(pattern);

            Stack<int> open = new Stack<int>();
            HashSet<int> closed = new HashSet<int>();
            int assigned = 0;
            int i = 0;

            while (i < pattern.Length)
            {
                switch (pattern[i])
                {
                    case '\\':
                        ReadEscape(pattern, ref i, groups, false, closed);
                        break;

                    case '[':
                        ReadClass(pattern, ref i, groups, closed, three);
                        break;

                    case ']':
                        throw Invalid(
                            pattern,
                            "a ']' here would close a character class that was never opened; the character "
                            + "itself is written '\\]'");

                    case '{':
                        ReadQuantifier(pattern, ref i);
                        break;

                    case '}':
                        // Closes a quantifier and nothing else, so on its own it names nothing: the
                        // character itself is written '\\}'. XSD 1.0's grammar left both braces out of its
                        // list of metacharacters by mistake, which F&O corrects (§5.6.1).
                        throw Invalid(
                            pattern,
                            "a '}' closes a quantifier that was never opened; the character itself is "
                            + "written '\\}'");

                    case '(':
                        // XPath 3.0 admits exactly one of .NET's '(?' forms, the non-capturing group. The
                        // rest of the family — lookahead and lookbehind, atomic groups, inline options,
                        // comments, conditionals — is .NET's and not this language's at any version.
                        if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                        {
                            if (!three || i + 2 >= pattern.Length || pattern[i + 2] != ':')
                            {
                                throw Invalid(
                                    pattern,
                                    three
                                        ? "'(?:' is the only '(?' form in this language, which otherwise has "
                                            + "only capturing groups"
                                        : "a group here is '(' and nothing else — the '(?' forms are not in "
                                            + "this language before 3.0, which has only capturing groups");
                            }

                            // Takes no number, so a back-reference cannot name it and the groups after it
                            // keep the numbers they would have had without it. Pushed all the same, because
                            // it still has to be balanced.
                            i += 3;
                            open.Push(NotCapturing);
                            break;
                        }

                        open.Push(++assigned);
                        i++;
                        break;

                    case ')':
                        if (open.Count == 0)
                        {
                            throw Invalid(pattern, "it closes a group that was never opened");
                        }

                        int number = open.Pop();

                        if (number != NotCapturing)
                        {
                            closed.Add(number);
                        }

                        i++;
                        break;

                    default:
                        i++;
                        break;
                }
            }

            if (open.Count != 0)
            {
                throw Invalid(pattern, "a group is left open");
            }
        }

        /// <summary>
        /// Reads a character class and everything nested in it, leaving the index past the closing bracket.
        /// </summary>
        /// <remarks>
        /// The grammar is narrower than .NET's in three ways worth naming. A <b>bracket</b> inside a class is
        /// the character itself and must be written <c>\[</c>, so <c>[[abcd]-[bc]]</c> and <c>[a[:xyz:]</c>
        /// name no class here even though .NET reads both. A class must hold <b>at least one</b> character,
        /// so <c>[]]</c> is not the class of one bracket that POSIX would read. And a <b>subtraction</b> is
        /// the last thing in the class that contains it: <c>[a-c-[b]]</c> and nothing after the inner
        /// bracket.
        /// </remarks>
        private static void ReadClass(string pattern, ref int index, int groups, HashSet<int> closed, bool three)
        {
            index++;

            if (index < pattern.Length && pattern[index] == '^')
            {
                index++;
            }

            bool any = false;

            while (true)
            {
                if (index >= pattern.Length)
                {
                    throw Invalid(pattern, "a character class is left open");
                }

                char character = pattern[index];

                if (character == ']')
                {
                    if (!any)
                    {
                        throw Invalid(
                            pattern,
                            "a character class holds at least one character, and the bracket that would be "
                            + "its first is written '\\]'");
                    }

                    index++;
                    return;
                }

                if (character == '[')
                {
                    throw Invalid(
                        pattern,
                        "a '[' inside a character class is the character itself and is written '\\['");
                }

                if (character == '-')
                {
                    if (index + 1 < pattern.Length && pattern[index + 1] == '[')
                    {
                        if (!any)
                        {
                            throw Invalid(
                                pattern,
                                "a subtraction takes one character class away from another, and there is "
                                + "nothing here for it to take from");
                        }

                        index++;
                        ReadClass(pattern, ref index, groups, closed, three);

                        if (index >= pattern.Length || pattern[index] != ']')
                        {
                            throw Invalid(
                                pattern, "a subtraction is the last thing in the class that contains it");
                        }

                        index++;
                        return;
                    }

                    // A hyphen at either end of the class is the character itself; anywhere else it has
                    // already been read as part of a range, so one reaching here is neither — at 3.0,
                    // whose grammar (XSD 1.1's) says so. XSD 1.0's, which XPath 2.0 reads by, let a hyphen
                    // stand for itself anywhere: '[a-c-1-4]' is two ranges with a hyphen between, which
                    // the 2.0 suite asks for and the 3.0 one refuses. Recorded in XsltCompatibility.md.
                    if (three && any && (index + 1 >= pattern.Length || pattern[index + 1] != ']'))
                    {
                        throw Invalid(
                            pattern,
                            "a hyphen in a character class is part of a range or the start of a "
                            + "subtraction, and this one is neither");
                    }

                    any = true;
                    index++;
                    continue;
                }

                ReadClassMember(pattern, ref index, groups, closed);
                any = true;

                // A hyphen with something on both sides makes a range, and the something after it is read
                // the same way as the something before — an escape included, which is what keeps '[X-Գ]'
                // from slipping past with its invalid escape unread.
                if (index + 1 < pattern.Length
                    && pattern[index] == '-'
                    && pattern[index + 1] != '['
                    && pattern[index + 1] != ']')
                {
                    index++;
                    ReadClassMember(pattern, ref index, groups, closed);
                }
            }
        }

        private static void ReadClassMember(string pattern, ref int index, int groups, HashSet<int> closed)
        {
            if (pattern[index] == '\\')
            {
                ReadEscape(pattern, ref index, groups, true, closed);
                return;
            }

            // A character above the basic plane is one member written in two units, so a range with one at
            // either end is that range and not four members with a hyphen somewhere among them.
            index += IsSupplementaryAt(pattern, index) ? 2 : 1;
        }

        /// <summary>Reads one escape, leaving the index past it.</summary>
        private static void ReadEscape(
            string pattern, ref int index, int groups, bool inClass, HashSet<int> closed)
        {
            if (index + 1 >= pattern.Length)
            {
                throw Invalid(pattern, "it ends with a backslash, which escapes nothing");
            }

            char escaped = pattern[index + 1];

            if (escaped is >= '0' and <= '9')
            {
                ReadBackReference(pattern, ref index, groups, inClass, closed);
                index++;
                return;
            }

            if (!IsEscapable(escaped))
            {
                throw Invalid(
                    pattern,
                    $"'\\{escaped}' is not an escape this language has — it has the metacharacters, the "
                    + "whitespace and name escapes, and the character categories, and nothing beyond them");
            }

            index += 2;

            if (escaped is not ('p' or 'P'))
            {
                return;
            }

            // The braces after a category escape belong to it, so they are not a quantifier that has lost
            // its number: '\p{Nd}{4}' is a category and then a quantifier, and '\[{Nd}' is neither.
            if (index >= pattern.Length || pattern[index] != '{')
            {
                throw Invalid(pattern, "a category escape names its category in braces, as '\\p{Nd}'");
            }

            int close = pattern.IndexOf('}', index);

            if (close < 0)
            {
                throw Invalid(pattern, "a category escape is left open");
            }

            index = close + 1;
        }

        /// <summary>
        /// Reads a braced quantifier, leaving the index past it.
        /// </summary>
        /// <remarks>
        /// An unescaped brace begins one, and nothing else: .NET reads a brace that starts no quantifier as
        /// the character itself, so <c>a{,2}</c> and <c>{5</c> match there and name nothing here.
        /// </remarks>
        private static void ReadQuantifier(string pattern, ref int index)
        {
            int at = index + 1;
            int digits = at;

            while (at < pattern.Length && char.IsAsciiDigit(pattern[at]))
            {
                at++;
            }

            if (at == digits)
            {
                throw Invalid(
                    pattern,
                    "a '{' begins a quantifier and so is followed by a number; the character itself is "
                    + "written '\\{'");
            }

            if (at < pattern.Length && pattern[at] == ',')
            {
                at++;

                while (at < pattern.Length && char.IsAsciiDigit(pattern[at]))
                {
                    at++;
                }
            }

            if (at >= pattern.Length || pattern[at] != '}')
            {
                throw Invalid(pattern, "a quantifier is left open");
            }

            index = at + 1;
        }

        /// <summary>
        /// Reads the digits of a back-reference and checks that it names a group complete to its left.
        /// </summary>
        private static void ReadBackReference(
            string pattern,
            ref int index,
            int groups,
            bool inClass,
            HashSet<int> closed)
        {
            if (inClass)
            {
                throw Invalid(
                    pattern,
                    "a back-reference cannot appear inside a character class, which holds characters "
                    + "rather than what was matched elsewhere");
            }

            int group = ReadGroupNumber(pattern, ref index, groups);

            if (!closed.Contains(group))
            {
                throw Invalid(
                    pattern,
                    $"\\{group} names a group that is not complete before it — a group cannot refer to "
                    + "itself, and one that comes later has captured nothing yet");
            }
        }

        /// <summary>
        /// Whether a backslash may precede this character.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The whole of the escaping this language has: the metacharacters, so that each can mean itself;
        /// <c>n</c>, <c>r</c> and <c>t</c>; the multi-character escapes for whitespace, digits, word
        /// characters and XML name characters; and the two that introduce a Unicode category. Digits are
        /// handled apart, being back-references.
        /// </para>
        /// <para>
        /// What is refused is everything .NET adds: <c>\b</c> and <c>\B</c>, <c>\A</c>, <c>\Z</c> and
        /// <c>\z</c>, <c>\G</c>, the named back-reference <c>\k</c>, and the character escapes <c>\a</c>,
        /// <c>\e</c>, <c>\f</c>, <c>\v</c>, <c>\xNN</c>, <c>\uNNNN</c> and <c>\cX</c>. Each of those means
        /// something in .NET and nothing here, so a pattern using one was matching against a rule that no
        /// conformant processor would apply.
        /// </para>
        /// </remarks>
        private static bool IsEscapable(char character)
        {
            return character is 'n' or 'r' or 't'
                or '\\' or '|' or '.' or '?' or '*' or '+' or '(' or ')' or '{' or '}'
                or '-' or '[' or ']' or '^' or '$'
                or 's' or 'S' or 'i' or 'I' or 'c' or 'C' or 'd' or 'D' or 'w' or 'W'
                or 'p' or 'P';
        }

        /// <summary>Stands for a group that takes no number, so that the stack can still balance it.</summary>
        private const int NotCapturing = -1;

        /// <summary>Counts the capturing groups, which is how far a back-reference may name.</summary>
        /// <remarks>
        /// A non-capturing group is not one of them, and leaving it out here is what keeps the numbering this
        /// engine hands to <c>\1</c> and to <c>fn:analyze-string</c> the same as .NET's own.
        /// </remarks>
        private static int CountGroups(string pattern)
        {
            int groups = 0;
            bool inClass = false;

            for (int i = 0; i < pattern.Length; i++)
            {
                switch (pattern[i])
                {
                    case '\\':
                        i++;
                        break;

                    case '[' when !inClass:
                        inClass = true;
                        break;

                    case ']':
                        inClass = false;
                        break;

                    case '(' when !inClass:
                        if (i + 1 < pattern.Length && pattern[i + 1] == '?')
                        {
                            i++;
                            break;
                        }

                        groups++;
                        break;
                }
            }

            return groups;
        }

        private static XsltException Invalid(string pattern, string why)
        {
            return XsltErrors.Error(
                XsltErrorCode.FORX0002, $"'{pattern}' is not a valid regular expression: {why}.");
        }

        /// <summary>
        /// Removes the whitespace the <c>x</c> flag ignores, which is all of it outside a character class.
        /// </summary>
        /// <remarks>
        /// Escaping does not protect it: <c>hello\ sworld</c> becomes <c>hello\sworld</c>, the backslash
        /// joining the <c>s</c> that the space was separating it from. That is what the flag is for.
        /// </remarks>
        private static string RemoveWhitespace(string pattern)
        {
            StringBuilder builder = new StringBuilder(pattern.Length);
            bool inClass = false;

            foreach (char character in pattern)
            {
                if (character == '[')
                {
                    inClass = true;
                }
                else if (character == ']')
                {
                    inClass = false;
                }
                else if (!inClass && character is ' ' or '\t' or '\n' or '\r')
                {
                    continue;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Rewrites the constructs the two dialects spell differently or read differently.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The largest difference is that <b>a character is a code point here and a UTF-16 unit there</b>.
        /// Above the basic plane a character is written as two units, so .NET's <c>.</c> matches half of one,
        /// its <c>[^a]</c> matches half of one, and its <c>\p{Lu}</c> matches neither half — both are
        /// surrogates, and a surrogate is not a letter. Anything that names a <em>set</em> of characters is
        /// therefore worked out here as a set of code points and written again as the pairs that spell it:
        /// character classes, the category and name escapes, and the dot.
        /// </para>
        /// <para>
        /// Then the smaller ones. A <b>back-reference</b> is written <c>\k&lt;N&gt;</c> so that the digits
        /// after it stay text — <c>\11</c> where there is one group is that group followed by a <c>1</c>, and
        /// .NET would read the eleventh. A literal character above the basic plane is wrapped in a group, so
        /// that a quantifier after it reaches both of its units rather than the second alone.
        /// </para>
        /// <para>
        /// Then the anchors, where .NET is Perl's and XPath is stricter. Without <c>m</c>, .NET's <c>$</c>
        /// matches at the end of the string <em>or before a newline that ends it</em>, so
        /// <c>matches('Mary\n', 'Mary$')</c> is true there and false in XPath; both anchors become the
        /// strict <c>\A</c> and <c>\z</c>. With <c>m</c>, XPath's <c>^</c> matches after any newline
        /// <em>except one that ends the string</em>, where .NET's matches after that one too.
        /// </para>
        /// </remarks>
        /// <param name="pattern">The pattern, already stripped of ignorable whitespace.</param>
        /// <param name="options">The options the flags selected, which several rewrites turn on.</param>
        private static string ToDotNet(string pattern, RegexOptions options, bool three)
        {
            bool multiline = (options & RegexOptions.Multiline) != 0;
            bool dotAll = (options & RegexOptions.Singleline) != 0;
            bool caseless = (options & RegexOptions.IgnoreCase) != 0;

            int groups = CountGroups(pattern);
            StringBuilder builder = new StringBuilder(pattern.Length);
            int i = 0;

            while (i < pattern.Length)
            {
                char character = pattern[i];

                if (character == '\\')
                {
                    char next = pattern[i + 1];

                    if (next is >= '0' and <= '9')
                    {
                        builder.Append("\\k<").Append(ReadGroupNumber(pattern, ref i, groups)).Append('>');
                        i++;
                        continue;
                    }

                    if (IsSetEscape(next))
                    {
                        ClassSet escaped = new ClassSet();
                        AddEscape(pattern, ref i, escaped, caseless, three);
                        AppendClass(builder, escaped, caseless);
                        continue;
                    }

                    builder.Append('\\').Append(next);
                    i += 2;
                    continue;
                }

                if (character == '[')
                {
                    AppendClass(builder, ReadClassSet(pattern, ref i, caseless, three), caseless);
                    continue;
                }

                if (character == '.')
                {
                    AppendClass(builder, AnyCharacter(dotAll), caseless);
                    i++;
                    continue;
                }

                if (!multiline && character is '^' or '$')
                {
                    builder.Append(character == '^' ? "\\A" : "\\z");
                    i++;
                    continue;
                }

                // With m, a newline that ends the string does not begin a line after it, where .NET
                // would let '^' match there.
                if (multiline && character == '^')
                {
                    builder.Append("(?:\\A|(?<=\\n)(?!\\z))");
                    i++;
                    continue;
                }

                // One character, two units, and a quantifier that would otherwise reach only the second.
                if (IsSupplementaryAt(pattern, i))
                {
                    builder.Append("(?:").Append(character).Append(pattern[i + 1]).Append(')');
                    i += 2;
                    continue;
                }

                builder.Append(character);
                i++;
            }

            return builder.ToString();
        }

        /// <summary>Whether a backslash and this character introduce a set rather than one character.</summary>
        private static bool IsSetEscape(char character)
        {
            return character is 's' or 'S' or 'i' or 'I' or 'c' or 'C'
                or 'd' or 'D' or 'w' or 'W' or 'p' or 'P';
        }

        /// <summary>Whether a character above the basic plane starts at this index.</summary>
        private static bool IsSupplementaryAt(string text, int index)
        {
            return char.IsHighSurrogate(text[index])
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1]);
        }

        /// <summary>
        /// A character class being worked out, as the code points it holds.
        /// </summary>
        /// <remarks>
        /// Almost all of a class is code points. What is not is a <b>block name</b> inside the basic plane,
        /// which .NET has a name for and this engine has no ranges for, so it is carried across as the text
        /// .NET reads — see <see cref="AppendBlocks"/> for what that costs.
        /// </remarks>
        private sealed class ClassSet
        {
            /// <summary>The code points the class holds, negation and subtraction already applied.</summary>
            public readonly CodePointSet Points = new CodePointSet();

            /// <summary>Block names left for .NET to read, as the class text it reads them from.</summary>
            public StringBuilder? Blocks;

            /// <summary>What a subtraction takes away, kept where blocks mean .NET must do the taking.</summary>
            public CodePointSet? Removed;

            /// <summary>Whether the class is negated and holds a block, so it could not be complemented.</summary>
            public bool Negated;

            /// <summary>Whether a category escape contributed, which the <c>i</c> flag must not reach.</summary>
            public bool Categories;
        }

        /// <summary>The set the dot stands for: every character, and not the two that end a line.</summary>
        private static ClassSet AnyCharacter(bool dotAll)
        {
            ClassSet set = new ClassSet();
            set.Points.Add(0, CodePointSet.Max);

            if (!dotAll)
            {
                CodePointSet lines = new CodePointSet();
                lines.Add('\n');
                lines.Add('\r');
                set.Points.Subtract(lines);
            }

            return set;
        }

        /// <summary>
        /// Reads a character class, working out the code points it names.
        /// </summary>
        /// <remarks>
        /// The grammar is the one <see cref="ReadClass"/> has already checked, so this reads rather than
        /// judges: the class closes, a hyphen reaching the loop stands for itself, and a subtraction is the
        /// last thing in the class that contains it.
        /// </remarks>
        /// <param name="pattern">The pattern.</param>
        /// <param name="index">The index of the <c>[</c> on entry, of the character after <c>]</c> on return.</param>
        /// <param name="caseless">Whether the <c>i</c> flag is in force, so a literal stands for both cases.</param>
        private static ClassSet ReadClassSet(string pattern, ref int index, bool caseless, bool three)
        {
            ClassSet result = new ClassSet();
            index++;

            if (pattern[index] == '^')
            {
                result.Negated = true;
                index++;
            }

            while (true)
            {
                char character = pattern[index];

                if (character == ']')
                {
                    index++;
                    break;
                }

                if (character == '-' && index + 1 < pattern.Length && pattern[index + 1] == '[')
                {
                    index++;
                    ClassSet taken = ReadClassSet(pattern, ref index, caseless, three);

                    // A subtraction takes from the group as written, negation included: '[^cde-[ag]]' is
                    // everything but c, d and e, and then without a and g. .NET reads the same text the
                    // other way about, as the negation of what is left after the subtraction.
                    Negate(result);
                    Subtract(result, taken, pattern);
                    index++;
                    break;
                }

                if (character == '-')
                {
                    AddLiteral(result.Points, '-', caseless);
                    index++;
                    continue;
                }

                int low = ReadMember(pattern, ref index, result, caseless, three);

                if (low < 0)
                {
                    continue;
                }

                if (index + 1 < pattern.Length
                    && pattern[index] == '-'
                    && pattern[index + 1] != '['
                    && pattern[index + 1] != ']')
                {
                    index++;
                    int high = ReadMember(pattern, ref index, result, caseless, three);

                    if (high < 0)
                    {
                        throw Invalid(
                            pattern,
                            "the end of a range is one character, and a multi-character escape is a set "
                            + "of them");
                    }

                    if (high < low)
                    {
                        throw Invalid(
                            pattern, "a range runs from one character to a later one, and this one does not");
                    }

                    AddLiteral(result.Points, low, high, caseless);
                    continue;
                }

                AddLiteral(result.Points, low, caseless);
            }

            Negate(result);
            return result;
        }

        /// <summary>
        /// Applies a class's negation, where it is one this engine can apply.
        /// </summary>
        /// <remarks>
        /// A negation is a set operation wherever the class is a set, which is everywhere but a block: those
        /// are text .NET reads for itself, so the negation has to be text too and stays for
        /// <see cref="AppendBlocks"/> to write.
        /// </remarks>
        private static void Negate(ClassSet set)
        {
            if (set.Negated && set.Blocks is null)
            {
                set.Points.Complement();
                set.Negated = false;
            }
        }

        /// <summary>
        /// Reads one member of a character class, adding it where it is a set.
        /// </summary>
        /// <returns>The code point the member is, or -1 where it was a set and has been added.</returns>
        private static int ReadMember(string pattern, ref int index, ClassSet into, bool caseless, bool three)
        {
            char character = pattern[index];

            if (character == '\\')
            {
                char escaped = pattern[index + 1];

                if (IsSetEscape(escaped))
                {
                    AddEscape(pattern, ref index, into, caseless, three);
                    return -1;
                }

                index += 2;

                return escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped,
                };
            }

            if (IsSupplementaryAt(pattern, index))
            {
                int code = char.ConvertToUtf32(character, pattern[index + 1]);
                index += 2;
                return code;
            }

            index++;
            return character;
        }

        /// <summary>
        /// Adds what a multi-character escape stands for, leaving the index past it.
        /// </summary>
        /// <remarks>
        /// Each of these has a negated form spelled with the capital letter, and each is the complement of
        /// the other over the whole of Unicode — which is a set operation here, and is why <c>\C</c> and
        /// <c>\P{Lu}</c> may be members of a class where .NET has no way to negate part of one.
        /// </remarks>
        /// <param name="pattern">The pattern.</param>
        /// <param name="index">The index of the backslash on entry, of the character after it on return.</param>
        /// <param name="into">The class being built.</param>
        /// <param name="caseless">Whether the <c>i</c> flag is in force.</param>
        private static void AddEscape(string pattern, ref int index, ClassSet into, bool caseless, bool three)
        {
            char kind = pattern[index + 1];
            index += 2;

            switch (kind)
            {
                case 's' or 'S':
                    Contribute(into, Whitespace(), kind == 'S');
                    return;

                case 'i' or 'I':
                    Contribute(into, NameCharacters(true, caseless, three), kind == 'I');
                    return;

                case 'c' or 'C':
                    Contribute(into, NameCharacters(false, caseless, three), kind == 'C');
                    return;

                case 'd' or 'D':
                    into.Categories = true;
                    Contribute(into, Category(DecimalDigits), kind == 'D');
                    return;

                // A word character is named by what it is not: everything that is not punctuation, not a
                // separator and not one of the others. The capital is that set itself.
                case 'w' or 'W':
                    into.Categories = true;
                    Contribute(into, Category(NotWord), kind == 'w');
                    return;

                default:
                    AddCategoryOrBlock(pattern, ref index, into, kind);
                    return;
            }
        }

        /// <summary>Adds a set, or everything outside it, to a class.</summary>
        private static void Contribute(ClassSet into, CodePointSet points, bool complement)
        {
            if (complement)
            {
                points.Complement();
            }

            into.Points.Add(points);
        }

        /// <summary>
        /// Adds what a <c>\p{…}</c> or <c>\P{…}</c> stands for, leaving the index past the closing brace.
        /// </summary>
        private static void AddCategoryOrBlock(string pattern, ref int index, ClassSet into, char kind)
        {
            int close = pattern.IndexOf('}', index);

            if (close < 0)
            {
                throw Invalid(pattern, "a category escape is left open");
            }

            string name = pattern[(index + 1)..close];
            index = close + 1;

            if (UnicodeCategories.Mask(name) is uint categories)
            {
                into.Categories = true;
                Contribute(into, Category(categories), kind == 'P');
                return;
            }

            if (name == "IsPrivateUse")
            {
                // XSD's block table has the private use area three times over, in the basic plane and in
                // the two planes kept for it, all under the one name; .NET's block is the first alone.
                CodePointSet privateUse = new CodePointSet();
                privateUse.Add(0xE000, 0xF8FF);
                privateUse.Add(0xF0000, 0xFFFFD);
                privateUse.Add(0x100000, 0x10FFFD);
                Contribute(into, privateUse, kind == 'P');
                return;
            }

            if (SupplementaryBlock(name) is (int low, int high))
            {
                CodePointSet block = new CodePointSet();
                block.Add(low, high);
                Contribute(into, block, kind == 'P');
                return;
            }

            // A block .NET names and this engine has no ranges for, handed across as written so that it
            // keeps whatever .NET's tables say it is. Every block .NET names lies inside the basic plane,
            // so the negated form takes in the whole of Unicode above it and that part is known here.
            (into.Blocks ??= new StringBuilder())
                .Append('\\').Append(kind).Append('{').Append(name).Append('}');

            if (kind == 'P')
            {
                into.Points.Add(CodePointSet.FirstSupplementary, CodePointSet.Max);
            }
        }

        /// <summary>Takes one character class away from another.</summary>
        private static void Subtract(ClassSet from, ClassSet taken, string pattern)
        {
            if (taken.Blocks is not null)
            {
                throw Invalid(
                    pattern,
                    "a subtraction cannot take away a block name: .NET reads the block for itself, and "
                    + "this engine has no ranges to take away");
            }

            if (from.Negated)
            {
                // The negation is still to be applied, which happens only for a class naming a block —
                // and .NET reads '[^X-[Y]]' as the negation of the subtraction where XPath reads it as
                // the subtraction from the negation. Refused rather than answered the other way about.
                throw Invalid(
                    pattern,
                    "a negated class that names a block has nothing to take a subtraction from: .NET "
                    + "would negate what is left rather than take from what is negated");
            }

            from.Points.Subtract(taken.Points);
            from.Categories |= taken.Categories;

            if (from.Blocks is not null)
            {
                // The blocks are text .NET reads for itself, so what is taken from them has to be text too.
                from.Removed = taken.Points;
            }
        }

        /// <summary>Writes a class as a pattern .NET reads the way XPath reads the original.</summary>
        private static void AppendClass(StringBuilder builder, ClassSet set, bool caseless)
        {
            // The i flag reaches a literal but not a category: '\p{Lu}' is the uppercase letters whether or
            // not case is being ignored, where .NET would fold the class it expands to. Where the class
            // holds nothing but literals the fold is left to .NET, which is what it did before any of this.
            bool guard = caseless && set.Categories;

            if (guard)
            {
                builder.Append("(?-i:");
            }

            set.Points.RemoveSurrogates();

            if (set.Blocks is null)
            {
                AppendPoints(builder, set.Points);
            }
            else
            {
                AppendBlocks(builder, set);
            }

            if (guard)
            {
                builder.Append(')');
            }
        }

        /// <summary>
        /// Writes a set of code points: the basic plane as a character class, and everything above it as the
        /// surrogate pairs that spell it.
        /// </summary>
        private static void AppendPoints(StringBuilder builder, CodePointSet points)
        {
            List<(int Low, int High)> basic = new List<(int Low, int High)>();
            List<(int Low, int High)> above = new List<(int Low, int High)>();
            Divide(points, basic, above);

            if (basic.Count == 0 && above.Count == 0)
            {
                // A set holding nothing, which is what an assertion that never succeeds matches.
                builder.Append("(?!)");
                return;
            }

            if (above.Count == 0)
            {
                AppendBasicPlane(builder, basic);
                return;
            }

            builder.Append("(?:");

            if (basic.Count != 0)
            {
                AppendBasicPlane(builder, basic);
                builder.Append('|');
            }

            AppendSurrogatePairs(builder, above);
            builder.Append(')');
        }

        /// <summary>
        /// Writes a class that names a block, where the basic plane is left to .NET.
        /// </summary>
        /// <remarks>
        /// .NET has names for the blocks inside the basic plane and this engine does not, so those are
        /// handed across as the text .NET reads. That divides the class in two: the part .NET reads, which
        /// is one unit wide and so cannot reach above the basic plane, and the code points above it, which
        /// are known here because a basic-plane block contributes none of them. A negation puts the
        /// surrogates inside the negated class, so that it cannot match one half of a character and stop.
        /// </remarks>
        private static void AppendBlocks(StringBuilder builder, ClassSet set)
        {
            List<(int Low, int High)> basic = new List<(int Low, int High)>();
            List<(int Low, int High)> above = new List<(int Low, int High)>();
            Divide(set.Points, basic, above);

            if (set.Negated)
            {
                CodePointSet outside = new CodePointSet();

                foreach ((int low, int high) in above)
                {
                    outside.Add(low, high);
                }

                outside.Complement();
                outside.Subtract(BasicPlane());
                above.Clear();
                above.AddRange(outside.Ranges);
            }

            bool alternation = above.Count != 0;

            if (alternation)
            {
                builder.Append("(?:");
            }

            builder.Append('[');

            if (set.Negated)
            {
                builder.Append('^');
            }

            AppendRanges(builder, basic);
            builder.Append(set.Blocks);

            if (set.Negated)
            {
                builder.Append("\\uD800-\\uDFFF");
            }

            if (set.Removed is not null)
            {
                List<(int Low, int High)> removed = new List<(int Low, int High)>();
                Divide(set.Removed, removed, new List<(int Low, int High)>());

                if (removed.Count != 0)
                {
                    builder.Append("-[");
                    AppendRanges(builder, removed);
                    builder.Append(']');
                }
            }

            builder.Append(']');

            if (alternation)
            {
                builder.Append('|');
                AppendSurrogatePairs(builder, above);
                builder.Append(')');
            }
        }

        /// <summary>Splits a set at the top of the basic plane, cutting a range that straddles it.</summary>
        private static void Divide(
            CodePointSet points, List<(int Low, int High)> basic, List<(int Low, int High)> above)
        {
            foreach ((int low, int high) in points.Ranges)
            {
                if (low < CodePointSet.FirstSupplementary)
                {
                    basic.Add((low, Math.Min(high, 0xFFFF)));
                }

                if (high >= CodePointSet.FirstSupplementary)
                {
                    above.Add((Math.Max(low, CodePointSet.FirstSupplementary), high));
                }
            }
        }

        private static CodePointSet BasicPlane()
        {
            CodePointSet plane = new CodePointSet();
            plane.Add(0, 0xFFFF);
            return plane;
        }

        /// <summary>Writes basic-plane ranges as a class, or as the character where that is all it is.</summary>
        private static void AppendBasicPlane(StringBuilder builder, List<(int Low, int High)> ranges)
        {
            if (ranges.Count == 1 && ranges[0].Low == ranges[0].High)
            {
                Unit(builder, ranges[0].Low);
                return;
            }

            builder.Append('[');
            AppendRanges(builder, ranges);
            builder.Append(']');
        }

        /// <summary>Writes ranges as the body of a character class, every character escaped.</summary>
        private static void AppendRanges(StringBuilder builder, List<(int Low, int High)> ranges)
        {
            foreach ((int low, int high) in ranges)
            {
                Unit(builder, low);

                if (high != low)
                {
                    builder.Append('-');
                    Unit(builder, high);
                }
            }
        }

        /// <summary>
        /// Writes code points above the basic plane as the surrogate pairs that spell them.
        /// </summary>
        /// <remarks>
        /// Gathered by leading surrogate, and then leads whose trailing surrogates are the same set are
        /// written as one range of leads. That is what keeps a plane of ideographs to a single alternative
        /// rather than the thousand its leads would otherwise be.
        /// </remarks>
        private static void AppendSurrogatePairs(StringBuilder builder, List<(int Low, int High)> ranges)
        {
            const int Leads = 0x400;
            CodePointSet?[] trails = new CodePointSet?[Leads];

            foreach ((int low, int high) in ranges)
            {
                int first = Lead(low);
                int last = Lead(high);

                for (int lead = first; lead <= last; lead++)
                {
                    int from = lead == first ? Trail(low) : 0xDC00;
                    int to = lead == last ? Trail(high) : 0xDFFF;
                    (trails[lead - 0xD800] ??= new CodePointSet()).Add(from, to);
                }
            }

            string? written = null;
            int start = 0;
            bool any = false;

            for (int lead = 0; lead <= Leads; lead++)
            {
                string? trail = lead < Leads && trails[lead] is not null
                    ? TrailingSurrogates(trails[lead]!)
                    : null;

                if (string.Equals(trail, written, StringComparison.Ordinal))
                {
                    continue;
                }

                if (written is not null)
                {
                    if (any)
                    {
                        builder.Append('|');
                    }

                    AppendLeadingSurrogates(builder, 0xD800 + start, 0xD800 + lead - 1);
                    builder.Append(written);
                    any = true;
                }

                written = trail;
                start = lead;
            }
        }

        private static void AppendLeadingSurrogates(StringBuilder builder, int low, int high)
        {
            if (low == high)
            {
                Unit(builder, low);
                return;
            }

            builder.Append('[');
            Unit(builder, low);
            builder.Append('-');
            Unit(builder, high);
            builder.Append(']');
        }

        private static string TrailingSurrogates(CodePointSet trails)
        {
            StringBuilder builder = new StringBuilder();

            if (trails.IsSingle(out int only))
            {
                Unit(builder, only);
                return builder.ToString();
            }

            builder.Append('[');

            foreach ((int low, int high) in trails.Ranges)
            {
                Unit(builder, low);

                if (high != low)
                {
                    builder.Append('-');
                    Unit(builder, high);
                }
            }

            builder.Append(']');
            return builder.ToString();
        }

        /// <summary>
        /// Adds a literal range, and the other case of everything in it where the <c>i</c> flag is in force.
        /// </summary>
        /// <remarks>
        /// Where the class holds nothing but literals this fold changes nothing — .NET applies its own,
        /// which is the wider of the two, to the class this becomes. It decides the answer only in a class
        /// that also holds a category, which is written under <c>(?-i:</c> so that the category keeps
        /// meaning the set it names.
        /// </remarks>
        private static void AddLiteral(CodePointSet points, int low, int high, bool caseless)
        {
            points.Add(low, high);

            if (!caseless)
            {
                return;
            }

            for (int code = low; code <= high; code++)
            {
                if (!Rune.TryCreate(code, out Rune rune))
                {
                    continue;
                }

                points.Add(Rune.ToUpperInvariant(rune).Value);
                points.Add(Rune.ToLowerInvariant(rune).Value);
            }
        }

        private static void AddLiteral(CodePointSet points, int code, bool caseless)
        {
            AddLiteral(points, code, code, caseless);
        }

        /// <summary>
        /// The four characters <c>\s</c> stands for.
        /// </summary>
        /// <remarks>
        /// Not .NET's reading of the same escape, which is every Unicode separator and the form feed and
        /// vertical tab besides. XML Schema names these four and nothing else.
        /// </remarks>
        private static CodePointSet Whitespace()
        {
            CodePointSet points = new CodePointSet();
            points.Add(' ');
            points.Add('\t');
            points.Add('\n');
            points.Add('\r');
            return points;
        }

        /// <summary>The characters an XML name may begin with, or may hold anywhere.</summary>
        private static CodePointSet NameCharacters(bool startOnly, bool caseless, bool three)
        {
            CodePointSet points = new CodePointSet();

            foreach ((int low, int high) in three ? NameStartRanges : s_fourthEditionStart.Value)
            {
                AddLiteral(points, low, high, caseless);
            }

            if (!startOnly)
            {
                foreach ((int low, int high) in three ? NameRestRanges : s_fourthEditionRest.Value)
                {
                    AddLiteral(points, low, high, caseless);
                }
            }

            return points;
        }

        private static CodePointSet Category(uint categories)
        {
            CodePointSet points = new CodePointSet();
            UnicodeCategories.AddTo(points, categories);
            return points;
        }

        private static int Lead(int code) => 0xD800 + ((code - CodePointSet.FirstSupplementary) >> 10);

        private static int Trail(int code) => 0xDC00 + ((code - CodePointSet.FirstSupplementary) & 0x3FF);

        private static void Unit(StringBuilder builder, int unit)
        {
            builder.Append("\\u").Append(unit.ToString("X4", CultureInfo.InvariantCulture));
        }

        /// <summary>The range a block name stands for, where the block is above the basic plane.</summary>
        private static (int Low, int High)? SupplementaryBlock(string name)
        {
            foreach ((string block, int low, int high) in SupplementaryBlocks)
            {
                if (string.Equals(block, name, StringComparison.Ordinal))
                {
                    return (low, high);
                }
            }

            return null;
        }

        /// <summary>
        /// The blocks XML Schema names that lie above the basic plane, which is every block .NET has no name
        /// for. The ranges are the ones the Unicode block table gives.
        /// </summary>
        /// <remarks>
        /// The first group is XML Schema 1.0's own list, which was fixed in 2001. The second is the symbol
        /// and pictograph blocks Unicode has added since, which are here because they are the ones a
        /// stylesheet actually names — <c>\p{IsEmoticons}</c> is how you match an emoji, and there is no
        /// other way to say it. Blocks added since 2001 for scripts are not listed: a name this table does
        /// not have is refused rather than answered wrongly.
        /// </remarks>
        private static readonly (string Name, int Low, int High)[] SupplementaryBlocks =
        {
            ("IsLinearBSyllabary", 0x10000, 0x1007F),
            ("IsLinearBIdeograms", 0x10080, 0x100FF),
            ("IsAegeanNumbers", 0x10100, 0x1013F),
            ("IsOldItalic", 0x10300, 0x1032F),
            ("IsGothic", 0x10330, 0x1034F),
            ("IsUgaritic", 0x10380, 0x1039F),
            ("IsDeseret", 0x10400, 0x1044F),
            ("IsShavian", 0x10450, 0x1047F),
            ("IsOsmanya", 0x10480, 0x104AF),
            ("IsCypriotSyllabary", 0x10800, 0x1083F),
            ("IsByzantineMusicalSymbols", 0x1D000, 0x1D0FF),
            ("IsMusicalSymbols", 0x1D100, 0x1D1FF),
            ("IsTaiXuanJingSymbols", 0x1D300, 0x1D35F),
            ("IsMathematicalAlphanumericSymbols", 0x1D400, 0x1D7FF),
            ("IsCJKUnifiedIdeographsExtensionB", 0x20000, 0x2A6DF),
            ("IsCJKCompatibilityIdeographsSupplement", 0x2F800, 0x2FA1F),
            ("IsTags", 0xE0000, 0xE007F),
            ("IsSupplementaryPrivateUseArea-A", 0xF0000, 0xFFFFF),
            ("IsSupplementaryPrivateUseArea-B", 0x100000, 0x10FFFF),

            ("IsMiscellaneousSymbolsandPictographs", 0x1F300, 0x1F5FF),
            ("IsEmoticons", 0x1F600, 0x1F64F),
            ("IsOrnamentalDingbats", 0x1F650, 0x1F67F),
            ("IsTransportandMapSymbols", 0x1F680, 0x1F6FF),
            ("IsAlchemicalSymbols", 0x1F700, 0x1F77F),
            ("IsGeometricShapesExtended", 0x1F780, 0x1F7FF),
            ("IsSupplementalArrows-C", 0x1F800, 0x1F8FF),
            ("IsSupplementalSymbolsandPictographs", 0x1F900, 0x1F9FF),
            ("IsChessSymbols", 0x1FA00, 0x1FA6F),
            ("IsSymbolsandPictographsExtended-A", 0x1FA70, 0x1FAFF),
        };
    }
}
