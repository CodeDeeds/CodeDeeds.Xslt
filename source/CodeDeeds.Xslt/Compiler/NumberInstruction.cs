using System.Globalization;
using System.Numerics;
using System.Text;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>How <c>xsl:number</c> works out which number a node gets.</summary>
    internal enum NumberLevel : byte
    {
        /// <summary>Count the node's position among its matching siblings.</summary>
        Single = 0,

        /// <summary>Produce one number per matching ancestor, outermost first, as in <c>2.4.1</c>.</summary>
        Multiple = 1,

        /// <summary>Count every matching node before this one anywhere in the document.</summary>
        Any = 2,
    }

    /// <summary>
    /// A parsed <c>xsl:number</c> format string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A format alternates between tokens that say how to render a number — <c>1</c>, <c>01</c>, <c>a</c>,
    /// <c>i</c> — and the literal separators between them, so <c>1.1</c> and <c>A-1</c> both work. When there
    /// are more numbers than tokens the last token and separator repeat, which is what makes a single format
    /// serve any depth of nesting.
    /// </para>
    /// <para>
    /// Parsed once, and held by the instruction where the format is a literal — which it nearly always is.
    /// It used to be taken apart on every execution, so numbering a thousand nodes read <c>"1"</c> a thousand
    /// times and built two lists each time.
    /// </para>
    /// </remarks>
    internal sealed class NumberFormat
    {
        /// <summary>The format used when none is given.</summary>
        public static NumberFormat Default { get; } = Parse("1");

        private readonly string[] m_tokens;
        private readonly string[] m_separators;

        /// <summary>
        /// The picture for each token that the span path cannot render, or <see langword="null"/> where it
        /// can.
        /// </summary>
        /// <remarks>
        /// Words and the digit families other than the Latin one are already implemented, for
        /// <c>fn:format-integer</c>, so a token naming one of those is handed to that reader rather than
        /// given a second implementation here. Held alongside the tokens because it is derived from the
        /// stylesheet text, and the format is parsed once for the same reason.
        /// </remarks>
        private readonly IntegerPicture?[] m_pictures;

        private NumberFormat(string prefix, string[] tokens, string[] separators, string suffix)
        {
            Prefix = prefix;
            m_tokens = tokens;
            m_separators = separators;
            Suffix = suffix;

            m_pictures = new IntegerPicture?[tokens.Length];
            for (int at = 0; at < tokens.Length; at++)
            {
                m_pictures[at] = NumberInstruction.RendersInSpan(tokens[at])
                    ? null
                    : IntegerPicture.Parse(tokens[at], modifiers: false);
            }
        }

        /// <summary>Gets the text before the first token.</summary>
        public string Prefix { get; }

        /// <summary>Gets the text after the last token.</summary>
        public string Suffix { get; }

        /// <summary>Returns the token for the number at a position, repeating the last one.</summary>
        /// <param name="index">Which number is being rendered.</param>
        public string TokenAt(int index) => m_tokens[Math.Min(index, m_tokens.Length - 1)];

        /// <summary>
        /// Returns the picture for the number at a position, or <see langword="null"/> where the token is
        /// one the span path renders.
        /// </summary>
        /// <param name="index">Which number is being rendered.</param>
        public IntegerPicture? PictureAt(int index) => m_pictures[Math.Min(index, m_pictures.Length - 1)];

        /// <summary>Returns the separator before the number at a position, repeating the last one.</summary>
        /// <param name="index">Which gap between numbers is being written.</param>
        public string SeparatorAt(int index)
        {
            return m_separators.Length == 0 ? "." : m_separators[Math.Min(index, m_separators.Length - 1)];
        }

        /// <summary>Parses a format string.</summary>
        /// <param name="format">The format as written.</param>
        public static NumberFormat Parse(string format)
        {
            List<string> tokens = new List<string>();
            List<string> separators = new List<string>();
            string suffix = string.Empty;

            // Anything before the first alphanumeric run is a prefix rather than a separator.
            int index = EndOfRun(format, 0, alphanumeric: false);
            string prefix = format[..index];

            while (index < format.Length)
            {
                int tokenStart = index;
                index = EndOfRun(format, index, alphanumeric: true);
                tokens.Add(format[tokenStart..index]);

                int separatorStart = index;
                index = EndOfRun(format, index, alphanumeric: false);

                if (index < format.Length)
                {
                    separators.Add(format[separatorStart..index]);
                }
                else
                {
                    suffix = format[separatorStart..];
                }
            }

            if (tokens.Count == 0)
            {
                // A format made only of punctuation has a single token, and that one token is both the first
                // and the last: the string starts with the first when it is not alphanumeric and ends with
                // the last. So "*" brackets the number rather than merely preceding it.
                suffix = prefix;
                tokens.Add("1");
            }

            return new NumberFormat(prefix, tokens.ToArray(), separators.ToArray(), suffix);
        }

        /// <summary>
        /// Returns where a run of alphanumeric characters, or of everything else, ends.
        /// </summary>
        /// <remarks>
        /// Alphanumeric as XSLT defines it for a format string: a letter or a number of any Unicode kind,
        /// which is wider than <see cref="char.IsLetterOrDigit(char)"/>. That test leaves out the numbers
        /// that are not decimal digits — a circled digit, a Roman numeral letter, a vulgar fraction — and so
        /// read <c>①</c> as punctuation bracketing the number rather than as a token naming a sequence, which
        /// wrote <c>①3①</c> where the specification's fallback for an unknown token gives <c>3</c>. Read by
        /// code point, so that a digit outside the basic plane is one character and not two halves.
        /// </remarks>
        private static int EndOfRun(string format, int index, bool alphanumeric)
        {
            while (index < format.Length)
            {
                int code = char.IsSurrogatePair(format, index) ? char.ConvertToUtf32(format, index) : format[index];

                if (IsAlphanumeric(code) != alphanumeric)
                {
                    break;
                }

                index += code > char.MaxValue ? 2 : 1;
            }

            return index;
        }

        private static bool IsAlphanumeric(int code)
        {
            return CharUnicodeInfo.GetUnicodeCategory(code) is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.LetterNumber
                or UnicodeCategory.OtherNumber;
        }
    }

    /// <summary>
    /// <c>xsl:number</c> — inserts a formatted number, either given outright or worked out from the node's
    /// position in the document.
    /// </summary>
    internal sealed class NumberInstruction : Instruction
    {
        private readonly Expr? m_value;
        private readonly NumberLevel m_level;
        private readonly Pattern[]? m_count;
        private readonly Pattern[]? m_from;
        private readonly AttributeValueTemplate? m_format;

        private readonly AttributeValueTemplate? m_groupingSeparator;
        private readonly AttributeValueTemplate? m_groupingSize;
        private readonly bool m_alphabetic;
        private readonly Expr? m_select;
        private readonly AttributeValueTemplate? m_ordinal;
        private readonly AttributeValueTemplate? m_language;
        private readonly bool m_legacy;
        private readonly AttributeValueTemplate? m_startAt;

        /// <summary>
        /// The parsed format, where the format attribute is a literal, otherwise <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// Held rather than recomputed because it cannot change: an instruction is compiled once and shared,
        /// so anything derived from the stylesheet text belongs here. A computed format has to be parsed on
        /// each execution, and caching that would be state on a shared object.
        /// </remarks>
        private readonly NumberFormat? m_constantFormat;

        /// <summary>Initializes a number instruction.</summary>
        public NumberInstruction(
            Expr? value,
            NumberLevel level,
            Pattern[]? count,
            Pattern[]? from,
            AttributeValueTemplate? format,
            AttributeValueTemplate? groupingSeparator = null,
            AttributeValueTemplate? groupingSize = null,
            bool alphabetic = false,
            Expr? select = null,
            AttributeValueTemplate? ordinal = null,
            AttributeValueTemplate? language = null,
            bool legacy = false,
            AttributeValueTemplate? startAt = null)
        {
            m_language = language;
            m_legacy = legacy;
            m_startAt = startAt;
            m_value = value;
            m_level = level;
            m_count = count;
            m_from = from;
            m_format = format;
            m_groupingSeparator = groupingSeparator;
            m_groupingSize = groupingSize;
            m_alphabetic = alphabetic;
            m_select = select;
            m_ordinal = ordinal;

            m_constantFormat = format is null
                ? NumberFormat.Default
                : format.ConstantValue is string literal ? NumberFormat.Parse(literal) : null;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            List<BigInteger> numbers = new List<BigInteger>();

            if (m_value is not null)
            {
                if (CollectValues(numbers, ref context) is string plain)
                {
                    runtime.Output.WriteText(plain);
                    return;
                }
            }
            else if (m_select is not null)
            {
                // XSLT 2.0 lets the node being numbered be chosen rather than being the context node, which
                // is what makes it possible to number something other than where the instruction stands.
                NodeSet selected = NodeSet.Of(
                    m_select.Evaluate(ref context), context.Tree, XsltErrorCode.XTTE1000, "xsl:number select");

                if (selected.Count != 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTTE1000,
                        "The select of xsl:number names the one node to number, and this one selected "
                        + $"{selected.Count} nodes.");
                }

                DynamicContext inner = context.SwitchTree(selected.TreeAt(0), selected[0]);
                CollectNumbers(numbers, ref inner);
            }
            else if (context.Node < 0)
            {
                // With neither value nor select, xsl:number numbers the context item, and only a node has a
                // position in a document to be numbered by.
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE0990,
                    "xsl:number numbers the context item where it is given neither a value nor a select, and "
                    + (context.HasContextItem
                        ? "here the context item is an atomic value rather than a node."
                        : "here there is no context item at all."));
            }
            else
            {
                CollectNumbers(numbers, ref context);
            }

            string? language = m_language?.Evaluate(ref context);

            if (language is not null && m_language is { ConstantValue: null } && !IsLanguage(language))
            {
                // A literal lang was checked when the stylesheet was compiled; a computed one can only be
                // checked here, and the specification gives the two occasions different codes.
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0030,
                    $"'{language}' is not a language code, which the lang of xsl:number has to be even "
                    + "where the language it names is one this engine does not spell numbers in.");
            }

            NumberFormat format = m_constantFormat ?? NumberFormat.Parse(m_format!.Evaluate(ref context));

            // Grouping only takes effect when both the separator and the size are given; one without the
            // other is meaningless, and the specification says to ignore it.
            string? separator = null;
            int groupingSize = 0;

            if (m_groupingSeparator is not null && m_groupingSize is not null)
            {
                string separatorText = m_groupingSeparator.Evaluate(ref context);
                double size = XPathValue.ParseNumber(m_groupingSize.Evaluate(ref context));

                if (separatorText.Length > 0 && size >= 1 && !double.IsNaN(size))
                {
                    // One character, which is not always one char: a separator outside the basic plane is a
                    // surrogate pair, and taking the first half of it produces an unpaired surrogate rather
                    // than the character asked for.
                    separator = char.IsHighSurrogate(separatorText[0]) && separatorText.Length > 1
                        ? separatorText[..2]
                        : separatorText[..1];

                    groupingSize = (int)size;
                }
            }

            // The specification leaves what the attribute's value means to the language. English has one
            // ordinal form, so any non-empty value asks for it and the value itself says nothing; German
            // inflects, and the value is which ending to put on. So the value travels, not a flag made
            // from it.
            string? ordinal = m_ordinal?.Evaluate(ref context) is string asked && asked.Length != 0
                ? asked
                : null;

            if (m_startAt is not null)
            {
                ShiftToStart(numbers, m_startAt.Evaluate(ref context));
            }

            runtime.Output.WriteText(
                Format(numbers, format, separator, groupingSize, m_alphabetic, ordinal, language));
        }

        /// <summary>
        /// Applies <c>start-at</c>, which says what the first number at each level is instead of one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It moves the whole sequence rather than replacing the first of it: the number at each level is
        /// shifted by the same amount, so a list beginning at zero still counts up by one. That is why it is a
        /// shift and not a starting value — <c>start-at="0"</c> on a numbering that reached 5 gives 4, not 0.
        /// </para>
        /// <para>
        /// One integer per level, and where there are more levels than integers the last integer serves for
        /// the rest. Which is the reading that makes <c>start-at="0"</c> mean "count from zero, at every
        /// level" rather than "at the first level only".
        /// </para>
        /// </remarks>
        /// <param name="numbers">The numbers to shift, in place.</param>
        /// <param name="written">The attribute's value, whitespace-separated integers.</param>
        private static void ShiftToStart(List<BigInteger> numbers, string written)
        {
            string[] tokens = written.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
            {
                return;
            }

            int[] starts = new int[tokens.Length];
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!int.TryParse(
                        tokens[i],
                        System.Globalization.NumberStyles.AllowLeadingSign,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out starts[i]))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE0030,
                        $"The start-at of xsl:number is '{written}', and '{tokens[i]}' in it is not an "
                        + "integer. It is one integer per level, saying what each level counts from.");
                }
            }

            for (int i = 0; i < numbers.Count; i++)
            {
                numbers[i] += starts[Math.Min(i, starts.Length - 1)] - 1;
            }
        }

        /// <summary>
        /// Reads the <c>value</c> attribute as the numbers to render.
        /// </summary>
        /// <remarks>
        /// XSLT 2.0 takes a sequence here, so <c>value="5 to 8"</c> numbers four levels at once — the same
        /// shape a <c>level="multiple"</c> count produces, and rendered through the same format.
        /// </remarks>
        /// <param name="numbers">Where to put the numbers.</param>
        /// <param name="context">The focus to evaluate the expression in.</param>
        /// <returns>
        /// Text to write instead of any formatted number, which only backwards-compatible processing
        /// produces; <see langword="null"/> where <paramref name="numbers"/> is the answer.
        /// </returns>
        private string? CollectValues(List<BigInteger> numbers, ref DynamicContext context)
        {
            XPathValue value = m_value!.Evaluate(ref context);

            if (m_legacy)
            {
                // XSLT 1.0 converts the value with number() and formats whatever comes back, so a value that
                // is not a number at all reads as NaN rather than being refused. Where a 2.0 expression
                // gives a sequence, that conversion sees its first item and nothing else.
                double single = XdmSequence.FirstItem(value).ToNumber();

                if (double.IsNaN(single) || double.IsInfinity(single))
                {
                    return XPathValue.NumberToString(single);
                }

                numbers.Add(Rounded(single));
                return null;
            }

            foreach (XPathValue item in XdmSequence.Atomize(XdmSequence.Items(value)))
            {
                // An integer is taken as one: a double holds fifteen digits and xsl:number is given
                // values that run past that, whose last digits are exactly what is being numbered.
                if (item.TypeCode == XdmTypeCode.Integer)
                {
                    BigInteger whole = item.ToBigInteger();

                    if (whole.Sign < 0)
                    {
                        throw NotANumberToCountBy(item);
                    }

                    numbers.Add(whole);
                    continue;
                }

                double number = item.ToNumber();

                if (double.IsNaN(number) || double.IsInfinity(number) || number < -0.5)
                {
                    throw NotANumberToCountBy(item);
                }

                numbers.Add(Rounded(number));
            }

            return null;
        }

        private static XsltException NotANumberToCountBy(XPathValue item)
        {
            return XsltErrors.Error(
                XsltErrorCode.XTDE0980,
                $"The value of xsl:number holds '{XdmSequence.StringValueOf(item)}', and every item "
                + "in it has to be an integer that is not negative.");
        }

        /// <summary>Rounds a number to the integer that is going to be rendered.</summary>
        private static BigInteger Rounded(double number)
        {
            return new BigInteger(Math.Floor(number + 0.5));
        }

        /// <summary>
        /// Whether a string is a language code — <c>en</c>, <c>en-GB</c>, <c>i-navajo</c>.
        /// </summary>
        /// <remarks>
        /// The grammar of <c>xs:language</c>, which is all that can be checked here: whether a well-formed
        /// code names a language anyone speaks is not something this engine knows, and the specification
        /// asks only that the attribute be a valid code.
        /// </remarks>
        /// <param name="language">The value written.</param>
        internal static bool IsLanguage(string language)
        {
            int part = 0;

            foreach (string piece in language.Split('-'))
            {
                // The first subtag is the language itself and is letters; the rest may be digits too.
                if (piece.Length is 0 or > 8)
                {
                    return false;
                }

                foreach (char character in piece)
                {
                    if (!(part == 0 ? char.IsAsciiLetter(character) : char.IsAsciiLetterOrDigit(character)))
                    {
                        return false;
                    }
                }

                part++;
            }

            return true;
        }

        private void CollectNumbers(List<BigInteger> numbers, ref DynamicContext context)
        {
            switch (m_level)
            {
                case NumberLevel.Any:
                {
                    int total = CountAnywhereBefore(ref context);
                    if (total > 0)
                    {
                        numbers.Add(total);
                    }

                    return;
                }

                case NumberLevel.Multiple:
                {
                    // Walk to the root gathering a position per matching ancestor, then reverse so the
                    // outermost comes first.
                    for (int node = context.Node; node >= 0; node = context.Tree.ParentOf(node))
                    {
                        if (MatchesCount(node, ref context))
                        {
                            numbers.Add(PositionAmongSiblings(node, ref context));
                        }

                        if (MatchesFrom(node, ref context))
                        {
                            break;
                        }
                    }

                    numbers.Reverse();
                    return;
                }

                default:
                {
                    int target = NearestMatchingSelfOrAncestor(ref context);
                    if (target >= 0)
                    {
                        numbers.Add(PositionAmongSiblings(target, ref context));
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// Finds the node actually being numbered: the context node if it matches, otherwise the nearest
        /// ancestor that does, stopping at any <c>from</c> boundary.
        /// </summary>
        private int NearestMatchingSelfOrAncestor(ref DynamicContext context)
        {
            for (int node = context.Node; node >= 0; node = context.Tree.ParentOf(node))
            {
                if (MatchesCount(node, ref context))
                {
                    return node;
                }

                if (MatchesFrom(node, ref context))
                {
                    return -1;
                }
            }

            return -1;
        }

        /// <summary>Counts a node's position among the preceding siblings that also match.</summary>
        private int PositionAmongSiblings(int node, ref DynamicContext context)
        {
            int parent = context.Tree.ParentOf(node);
            if (parent < 0 || XdmTree.IsAttribute(node))
            {
                return 1;
            }

            int position = 0;
            for (int sibling = context.Tree.FirstChildOf(parent); sibling >= 0;
                sibling = context.Tree.NextSiblingOf(sibling))
            {
                if (MatchesCount(sibling, ref context))
                {
                    position++;
                }

                if (sibling == node)
                {
                    break;
                }
            }

            return Math.Max(position, 1);
        }

        /// <summary>
        /// Counts every matching node at or before the context node in document order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The set counted is the current node together with everything on its preceding and ancestor axes,
        /// which in preorder is every node whose id is below its own — a following node's is above it and a
        /// descendant's is inside its range. Neither axis holds an attribute or a namespace node, so one is
        /// counted where it is the node being numbered and nowhere else (XSLT 3.0 §12.4.2). That is also
        /// why the scan stops at the element an attribute hangs off: its own id is numbered outside the
        /// preorder sequence and says nothing about where it stands.
        /// </para>
        /// <para>
        /// A <c>from</c> pattern restarts the count at the last node matching it, which is counted itself
        /// where it also matches <c>count</c>: <c>from</c> says where the numbering begins again rather than
        /// which node to leave out. Found on the way rather than by a scan backwards, since the last match
        /// before the context node is simply the last one this walk meets.
        /// </para>
        /// </remarks>
        private int CountAnywhereBefore(ref DynamicContext context)
        {
            XdmTree tree = context.Tree;
            bool detached = XdmTree.IsAttribute(context.Node);
            int last = detached ? tree.ParentOf(context.Node) : context.Node;
            int total = 0;

            for (int node = 0; node <= last && node < tree.NodeCount; node++)
            {
                Consider(node, ref total, ref context);
            }

            if (detached)
            {
                Consider(context.Node, ref total, ref context);
            }

            return total;
        }

        /// <summary>Takes one node of the walk into account, restarting the count where it says to.</summary>
        /// <param name="node">The node reached.</param>
        /// <param name="total">The count so far.</param>
        /// <param name="context">The context the patterns are matched in.</param>
        private void Consider(int node, ref int total, ref DynamicContext context)
        {
            if (MatchesFrom(node, ref context))
            {
                total = 0;
            }

            if (MatchesCount(node, ref context))
            {
                total++;
            }
        }

        private bool MatchesCount(int node, ref DynamicContext context)
        {
            if (m_count is not null)
            {
                return AnyPatternMatches(m_count, node, ref context);
            }

            // With no count pattern, the default is "nodes of the same kind and name as the current node".
            XdmTree tree = context.Tree;
            if (tree.KindOf(node) != tree.KindOf(context.Node))
            {
                return false;
            }

            return tree.FingerprintOf(node) == tree.FingerprintOf(context.Node);
        }

        private bool MatchesFrom(int node, ref DynamicContext context)
        {
            return m_from is not null && AnyPatternMatches(m_from, node, ref context);
        }

        /// <summary>Whether a node matches any of a set of patterns.</summary>
        /// <remarks>
        /// The node being tested is the current node for the duration, not just the context node, so that
        /// <c>current()</c> inside a <c>count</c> reads the candidate rather than the node the instruction
        /// is numbering. That is what makes <c>count="*[name()=name(current())]/*"</c> mean "an element
        /// whose parent has the same name" — a pattern is asked about a node, and its own subject is the
        /// node it is asked about.
        /// </remarks>
        private static bool AnyPatternMatches(Pattern[] patterns, int node, ref DynamicContext context)
        {
            foreach (Pattern pattern in patterns)
            {
                DynamicContext inner = context;
                inner.Node = node;
                inner.AtomicItem = default;
                inner.CurrentNode = node;
                inner.CurrentTree = context.Tree;

                if (pattern.Matches(node, ref inner))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Renders a list of numbers through a format string.
        /// </summary>
        /// <remarks>
        /// A format alternates between tokens that say how to render a number — <c>1</c>, <c>01</c>, <c>a</c>,
        /// <c>i</c> — and the literal separators between them, so <c>1.1</c> and <c>A-1</c> both work. When
        /// there are more numbers than tokens the last token and separator repeat, which is what makes a
        /// single format serve any depth of nesting.
        /// </remarks>
        /// <param name="numbers">The numbers to render.</param>
        /// <param name="format">The format string.</param>
        /// <param name="groupingSeparator">The character to separate digit groups with, if any.</param>
        /// <param name="groupingSize">How many digits go in a group; zero disables grouping.</param>
        /// <param name="alphabetic">
        /// Whether a token such as <c>i</c> means the alphabetic sequence rather than roman numerals, as
        /// <c>letter-value</c> selects.
        /// </param>
        /// <param name="ordinal">The value of the <c>ordinal</c> attribute, or null for cardinal numbering.</param>
        /// <param name="language">The language to spell words in.</param>
        public static string Format(
            List<BigInteger> numbers,
            string format,
            string? groupingSeparator = null,
            int groupingSize = 0,
            bool alphabetic = false,
            string? ordinal = null,
            string? language = null)
        {
            return Format(
                numbers,
                NumberFormat.Parse(format),
                groupingSeparator,
                groupingSize,
                alphabetic,
                ordinal,
                language);
        }

        /// <summary>Renders a list of numbers through a format that has already been parsed.</summary>
        /// <param name="numbers">The numbers to render.</param>
        /// <param name="format">The parsed format.</param>
        /// <param name="groupingSeparator">The character to separate digit groups with, if any.</param>
        /// <param name="groupingSize">How many digits go in a group; zero disables grouping.</param>
        /// <param name="alphabetic">Whether a token such as <c>i</c> means letters rather than roman numerals.</param>
        /// <param name="ordinal">The value of the <c>ordinal</c> attribute, or null for cardinal numbering.</param>
        /// <param name="language">The language to spell words in.</param>
        public static string Format(
            List<BigInteger> numbers,
            NumberFormat format,
            string? groupingSeparator = null,
            int groupingSize = 0,
            bool alphabetic = false,
            string? ordinal = null,
            string? language = null)
        {
            if (numbers.Count == 0)
            {
                // The prefix and the suffix belong to the format rather than to any number, so they are
                // written even with nothing between them: a format of "A." over no numbers is ".". That is
                // what makes an xsl:number over a node with no matching ancestors show its punctuation.
                return string.Concat(format.Prefix, format.Suffix);
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(format.Prefix);

            Span<char> scratch = stackalloc char[64];

            for (int i = 0; i < numbers.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(format.SeparatorAt(i - 1));
                }

                ReadOnlySpan<char> token = format.TokenAt(i);

                // An ordinal is asked for by an attribute rather than written into the token, so the picture
                // it needs cannot be the one the format was parsed into and is built here instead. The
                // attribute's value goes in as the variation, which is where fn:format-integer writes the
                // same thing: ordinal="-er" here and 'Ww;o(-er)' there ask for one word.
                IntegerPicture? picture = ordinal is null
                    ? format.PictureAt(i)
                    : IntegerPicture.Parse(format.TokenAt(i), modifiers: true, ordinal: true, ordinal);

                BigInteger number = numbers[i];

                if (picture is not null)
                {
                    // grouping-separator and grouping-size are attributes rather than picture text, and
                    // they group a digit token whatever family its digits are from.
                    builder.Append(
                        picture.Grouped(groupingSize, CodePointOf(groupingSeparator))
                            .Format(number, language));
                }
                else if (number < int.MinValue || number > int.MaxValue)
                {
                    builder.Append(FormatWide(number, token, groupingSeparator, groupingSize));
                }
                else if (TryFormatOne((int)number, token, groupingSeparator, groupingSize, alphabetic,
                    scratch, out int written))
                {
                    builder.Append(scratch[..written]);
                }
                else
                {
                    builder.Append(FormatOne((int)number, token, groupingSeparator, groupingSize, alphabetic));
                }
            }

            builder.Append(format.Suffix);
            return builder.ToString();
        }

        /// <summary>
        /// Whether a format token is one the span path renders, rather than one for the picture reader.
        /// </summary>
        /// <remarks>
        /// The four lettered sequences and the Latin digits are what a stylesheet nearly always asks for,
        /// and they are written here so that they can be rendered into a caller's buffer. Everything else —
        /// words, and digits of another family — is a sequence <c>fn:format-integer</c> already reads, and
        /// is handed to that reader rather than implemented twice. Grouping is the one thing lost on that
        /// route: <c>grouping-separator</c> applies to the Latin digits.
        /// </remarks>
        /// <param name="token">The format token.</param>
        internal static bool RendersInSpan(ReadOnlySpan<char> token)
        {
            if (token.Length == 1 && token[0] is 'a' or 'A' or 'i' or 'I')
            {
                return true;
            }

            foreach (char character in token)
            {
                if (!char.IsAsciiDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The code point a separator begins with, or a negative number where there is none.</summary>
        /// <param name="separator">The separator as written.</param>
        private static int CodePointOf(string? separator)
        {
            if (string.IsNullOrEmpty(separator))
            {
                return -1;
            }

            return char.IsSurrogatePair(separator, 0) ? char.ConvertToUtf32(separator, 0) : separator[0];
        }

        /// <summary>Renders one number too wide to hold in an <see cref="int"/>.</summary>
        /// <remarks>
        /// Only digits can present a number of this size: the alphabetic sequence would run to millions of
        /// letters and the roman one stops at 4999, and the specification's answer for a sequence that
        /// cannot render a value is the digits. The token's own width and the grouping attributes apply as
        /// they do to any other number.
        /// </remarks>
        /// <param name="number">The number to render.</param>
        /// <param name="token">The token saying how to render it.</param>
        /// <param name="groupingSeparator">The characters to separate digit groups with, if any.</param>
        /// <param name="groupingSize">How many digits go in a group; zero disables grouping.</param>
        internal static string FormatWide(
            BigInteger number,
            ReadOnlySpan<char> token,
            string? groupingSeparator,
            int groupingSize)
        {
            string plain = BigInteger.Abs(number).ToString(CultureInfo.InvariantCulture);

            if (token.Length > plain.Length && token.Length > 1 && char.IsDigit(token[0]))
            {
                plain = plain.PadLeft(token.Length, '0');
            }

            if (groupingSeparator is string separator && separator.Length != 0 && groupingSize > 0
                && plain.Length > groupingSize)
            {
                StringBuilder grouped = new StringBuilder(plain.Length * 2);

                for (int i = 0; i < plain.Length; i++)
                {
                    if (i > 0 && (plain.Length - i) % groupingSize == 0)
                    {
                        grouped.Append(separator);
                    }

                    grouped.Append(plain[i]);
                }

                plain = grouped.ToString();
            }

            return number.Sign < 0 ? "-" + plain : plain;
        }

        /// <summary>Renders one number through one format token.</summary>
        /// <remarks>
        /// The token is a span because <c>format-date</c> takes its tokens out of the middle of a picture
        /// string, where cutting one out to pass it would allocate for every component of every call.
        /// </remarks>
        /// <param name="number">The number to render.</param>
        /// <param name="token">The token saying how to render it.</param>
        /// <param name="groupingSeparator">The character to separate digit groups with, if any.</param>
        /// <param name="groupingSize">How many digits go in a group; zero disables grouping.</param>
        /// <param name="alphabetic">Whether a token such as <c>i</c> means letters rather than roman numerals.</param>
        internal static string FormatOne(
            int number,
            ReadOnlySpan<char> token,
            string? groupingSeparator,
            int groupingSize,
            bool alphabetic)
        {
            // Sized to what this token can need rather than to a round number, because the buffer is zeroed
            // before it is used and a token that wants sixteen characters should not pay to clear sixty-four.
            int width = MaximumWidth(token, groupingSize);

            Span<char> destination = width <= 128 ? stackalloc char[width] : new char[width];
            TryFormatOne(number, token, groupingSeparator, groupingSize, alphabetic, destination, out int written);
            return new string(destination[..written]);
        }

        /// <summary>
        /// Renders one number through one format token, into a buffer the caller owns.
        /// </summary>
        /// <remarks>
        /// The result nearly always goes straight into something larger — a StringBuilder, or a picture being
        /// assembled — so writing it where the caller wants it saves the string that only existed to be
        /// copied and dropped.
        /// </remarks>
        /// <param name="number">The number to render.</param>
        /// <param name="token">The token saying how to render it.</param>
        /// <param name="groupingSeparator">The character to separate digit groups with, if any.</param>
        /// <param name="groupingSize">How many digits go in a group; zero disables grouping.</param>
        /// <param name="alphabetic">Whether a token such as <c>i</c> means letters rather than roman numerals.</param>
        /// <param name="destination">Where to write, at least <see cref="MaximumWidth"/> wide.</param>
        /// <param name="written">How many characters were written.</param>
        /// <returns><see langword="false"/> if the destination was too small, having written nothing.</returns>
        internal static bool TryFormatOne(
            int number,
            ReadOnlySpan<char> token,
            string? groupingSeparator,
            int groupingSize,
            bool alphabetic,
            Span<char> destination,
            out int written)
        {
            switch (token[^1])
            {
                case 'a':
                    return TryAlphabetic(number, 'a', destination, out written);

                case 'A':
                    return TryAlphabetic(number, 'A', destination, out written);

                // letter-value="alphabetic" asks for the letter sequence starting at this token's letter
                // rather than the roman numerals the token would otherwise select.
                case 'i':
                    return alphabetic
                        ? TryAlphabetic(number, 'i', destination, out written)
                        : TryRoman(number, lower: true, destination, out written);

                case 'I':
                    return alphabetic
                        ? TryAlphabetic(number, 'I', destination, out written)
                        : TryRoman(number, lower: false, destination, out written);

                default:
                    return TryDigits(number, token, groupingSeparator, groupingSize, destination, out written);
            }
        }

        /// <summary>The most room a token could need, so a caller can size a buffer that always fits.</summary>
        /// <param name="token">The token saying how the number is rendered.</param>
        /// <param name="groupingSize">How many digits go in a group; zero disables grouping.</param>
        internal static int MaximumWidth(ReadOnlySpan<char> token, int groupingSize)
        {
            // A token that pads asks for its own width. Below that, the widest any presentation gets is the
            // roman numeral for 4888, MMMMDCCCLXXXVIII, at sixteen — wider than an int with its sign at
            // eleven, and than the seven letters the alphabetic sequence needs for int.MaxValue.
            int digits = Math.Max(16, token.Length);

            // Two chars per group boundary rather than one: a separator outside the basic plane is a
            // surrogate pair. Sizing for one and being handed the other is a buffer that does not fit, which
            // the writer reports by producing nothing at all — so the few bytes are worth not depending on
            // the caller having asked for a separator this method never sees.
            return groupingSize > 0 ? digits + (2 * ((digits - 1) / groupingSize)) : digits;
        }

        /// <summary>Writes the number as digits, padded to the token's width and grouped, both in place.</summary>
        private static bool TryDigits(
            int number,
            ReadOnlySpan<char> token,
            string? groupingSeparator,
            int groupingSize,
            Span<char> destination,
            out int written)
        {
            if (!number.TryFormat(destination, out written, default, CultureInfo.InvariantCulture))
            {
                return false;
            }

            // A token of digits pads to its own width, so "001" numbers from 001. The digits are already
            // written, so they are shifted up and the room in front filled — which is what PadLeft did, sign
            // and all: a padded "-5" really is "0-5".
            if (token.Length > written && token.Length > 1 && char.IsDigit(token[0]))
            {
                int padding = token.Length - written;
                if (written + padding > destination.Length)
                {
                    return false;
                }

                destination[..written].CopyTo(destination[padding..]);
                destination[..padding].Fill('0');
                written += padding;
            }

            if (groupingSeparator is not string separator
                || separator.Length == 0
                || groupingSize <= 0
                || written <= groupingSize)
            {
                return true;
            }

            // The separator is one character, which may be two chars where it is outside the basic plane, so
            // the room a group boundary needs is its length rather than one.
            int total = written + (((written - 1) / groupingSize) * separator.Length);
            if (total > destination.Length)
            {
                return false;
            }

            // Spread the digits out from the right, dropping a separator in after every group.
            int from = written - 1;
            int to = total - 1;
            int inGroup = 0;

            while (from >= 0)
            {
                if (inGroup == groupingSize)
                {
                    to -= separator.Length;
                    separator.CopyTo(destination[(to + 1)..(to + 1 + separator.Length)]);
                    inGroup = 0;
                }

                destination[to--] = destination[from--];
                inGroup++;
            }

            written = total;
            return true;
        }

        /// <summary>Renders 1 as a, 26 as z, 27 as aa, and so on.</summary>
        private static bool TryAlphabetic(int number, char start, Span<char> destination, out int written)
        {
            if (number <= 0)
            {
                return number.TryFormat(destination, out written, default, CultureInfo.InvariantCulture);
            }

            // The least significant letter comes out first, so it is built at the end and copied down.
            Span<char> letters = stackalloc char[8];
            int at = letters.Length;

            while (number > 0)
            {
                number--;
                letters[--at] = (char)(start + (number % 26));
                number /= 26;
            }

            ReadOnlySpan<char> produced = letters[at..];
            written = 0;

            if (produced.Length > destination.Length)
            {
                return false;
            }

            produced.CopyTo(destination);
            written = produced.Length;
            return true;
        }

        private static readonly (int Value, string Symbol)[] s_romanNumerals =
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        };

        /// <summary>
        /// Writes roman numerals, in either case.
        /// </summary>
        /// <remarks>
        /// The case is chosen as each symbol is written rather than by lowering the finished numeral, which
        /// built the string twice.
        /// </remarks>
        private static bool TryRoman(int number, bool lower, Span<char> destination, out int written)
        {
            // Roman numerals have no zero or negatives; anything outside the range falls back to digits.
            if (number <= 0 || number > 4999)
            {
                return number.TryFormat(destination, out written, default, CultureInfo.InvariantCulture);
            }

            written = 0;

            foreach ((int value, string symbol) in s_romanNumerals)
            {
                while (number >= value)
                {
                    if (written + symbol.Length > destination.Length)
                    {
                        written = 0;
                        return false;
                    }

                    foreach (char character in symbol)
                    {
                        destination[written++] = lower ? char.ToLowerInvariant(character) : character;
                    }

                    number -= value;
                }
            }

            return true;
        }
    }
}
