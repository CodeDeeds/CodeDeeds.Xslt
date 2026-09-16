using System.Text.RegularExpressions;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>How <c>xsl:for-each-group</c> decides where one group ends and the next begins.</summary>
    public enum GroupingKind : byte
    {
        /// <summary>Items sharing a key value belong together, wherever they appear.</summary>
        ByKey,

        /// <summary>Neighbouring items sharing a key value belong together; a run ends when the key changes.</summary>
        ByAdjacentKey,

        /// <summary>A new group begins at each item matching a pattern.</summary>
        StartingWith,

        /// <summary>A group ends at each item matching a pattern.</summary>
        EndingWith,
    }

    /// <summary>
    /// <c>xsl:for-each-group</c>, which is what XSLT 2.0 added in place of grouping by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XSLT 1.0 has no grouping at all. The way it was done — declare a key, select the first node for each
    /// distinct value, and compare identities with <c>generate-id()</c> — is called Muenchian grouping after
    /// the person who worked it out, which tells you how far it is from being obvious. This instruction
    /// replaces the whole technique.
    /// </para>
    /// <para>
    /// Within the body, <c>current-group()</c> is the items of the group and
    /// <c>current-grouping-key()</c> the value they share. Both are ordinary functions rather than variables,
    /// and both are meaningless outside a grouping body, so they are held on the runtime rather than passed
    /// through the context.
    /// </para>
    /// </remarks>
    internal sealed class ForEachGroupInstruction : Instruction
    {
        private readonly Expr m_select;
        private readonly Expr? m_key;
        private readonly Pattern[]? m_pattern;
        private readonly GroupingKind m_kind;
        private readonly bool m_composite;
        private readonly SortKey[] m_sortKeys;
        private readonly Instruction[] m_body;
        private readonly bool m_implements30;

        /// <summary>The collation string keys are grouped under, or null for the code point one.</summary>
        /// <remarks>
        /// Resolved when the instruction runs, whether it was written outright or as an attribute value
        /// template: a collation of the caller's own is found through the resolver in force then, and a
        /// written one the compiler has already checked is there to be found.
        /// </remarks>
        private readonly AttributeValueTemplate? m_collation;

        /// <summary>
        /// The default collation in scope where the instruction stands, as a URI, where no collation was
        /// written and the default is not the code point one; the specification has grouping use it.
        /// </summary>
        private readonly string? m_defaultCollation;

        public ForEachGroupInstruction(
            Expr select,
            Expr? key,
            Pattern[]? pattern,
            GroupingKind kind,
            bool composite,
            SortKey[] sortKeys,
            Instruction[] body,
            bool implements30,
            AttributeValueTemplate? collation = null,
            string? defaultCollation = null)
        {
            m_select = select;
            m_key = key;
            m_pattern = pattern;
            m_kind = kind;
            m_composite = composite;
            m_sortKeys = sortKeys;
            m_body = body;
            m_implements30 = implements30;
            m_collation = collation;
            m_defaultCollation = defaultCollation;
        }

        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            // The population is any sequence of items, in the order the select expression gave them: a
            // group is the items of the population with one grouping key, in population order, and a
            // group's first item is what the body sees as the context item. XSLT 2.0 let a pattern match
            // nodes only, and named a population of anything else XTTE1120; 3.0 patterns match any item.
            Collation? collation = null;
            string? uri = m_collation is not null ? m_collation.Evaluate(ref context).Trim() : m_defaultCollation;

            if (uri is not null)
            {
                try
                {
                    collation = Collation.Resolve(uri, ref context);
                }
                catch (XsltException failed)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE1110,
                        $"'{uri}' is not a collation this xsl:for-each-group can group by: {failed.Message}",
                        failed);
                }

                // Under the code point collation a string is its own key, and nothing need be done to it.
                if (ReferenceEquals(collation, Collation.Codepoint))
                {
                    collation = null;
                }
            }

            List<XPathValue> population = XdmSequence.Items(m_select.Evaluate(ref context));

            if (!m_implements30 && m_kind is GroupingKind.StartingWith or GroupingKind.EndingWith)
            {
                foreach (XPathValue item in population)
                {
                    if (item.Kind != XPathValueKind.Node)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTTE1120,
                            "xsl:for-each-group with group-starting-with or group-ending-with groups nodes, "
                            + "under XSLT 2.0, and the population holds an atomic value.");
                    }
                }
            }

            List<Group> groups = BuildGroups(population, ref context, runtime, collation);

            (XPathValue Group, XPathValue Key)? saved = runtime.CurrentGroup;

            // As for xsl:for-each: the focus becomes a group the rules did not choose, so the rule that was
            // running is not the current one inside.
            TemplateRule? rule = runtime.SuspendCurrentRule();

            try
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    Group group = groups[i];

                    // The context item is the first item of the group, and position() counts groups rather
                    // than items — which is what makes a group behave like one thing.
                    DynamicContext inner = Focus(ref context, group.Items[0], i + 1, groups.Count);

                    runtime.CurrentGroup = (group.Value, group.Key);
                    ExecuteAll(m_body, ref inner, runtime);
                }
            }
            finally
            {
                runtime.CurrentGroup = saved;
                runtime.ResumeCurrentRule(rule);
            }
        }

        private static DynamicContext Focus(ref DynamicContext context, XPathValue item, int position, int size)
        {
            DynamicContext inner = context.WithItem(item);
            inner.CurrentNode = inner.Node;
            inner.CurrentTree = inner.Tree;
            inner.Position = position;
            inner.Size = size;
            return inner;
        }

        private sealed class Group
        {
            private XPathValue? m_value;

            public List<XPathValue> Items { get; } = new();

            /// <summary>The grouping key, or the empty sequence for a group a pattern made.</summary>
            public XPathValue Key { get; set; }

            /// <summary>The group as current-group() returns it.</summary>
            /// <remarks>
            /// A group of nodes is a node set, so that a path or a predicate can be written on it directly;
            /// one holding anything else is the sequence of its items, in population order.
            /// </remarks>
            public XPathValue Value => m_value ??= Build();

            private XPathValue Build()
            {
                foreach (XPathValue item in Items)
                {
                    if (item.Kind != XPathValueKind.Node)
                    {
                        return XPathValue.FromSequence(new XdmSequence(Items.ToArray()));
                    }
                }

                NodeSet nodes = new NodeSet(Items[0].NodeTree, Items.Count);

                foreach (XPathValue item in Items)
                {
                    nodes.Add(item.NodeTree, item.NodeId);
                }

                nodes.SortAndDeduplicate();
                return XPathValue.FromNodeSet(nodes);
            }
        }

        private List<Group> BuildGroups(
            List<XPathValue> population, ref DynamicContext context, XsltRuntime runtime, Collation? collation)
        {
            List<Group> groups = m_kind switch
            {
                GroupingKind.ByKey => GroupByKey(population, ref context, collation),
                GroupingKind.ByAdjacentKey => GroupByAdjacentKey(population, ref context, collation),
                _ => GroupByPattern(population, ref context),
            };

            if (m_sortKeys.Length != 0 && groups.Count > 1)
            {
                SortGroups(groups, ref context, runtime);
            }

            return groups;
        }

        private List<Group> GroupByKey(List<XPathValue> population, ref DynamicContext context, Collation? collation)
        {
            List<Group> groups = new();
            Dictionary<string, Group> byKey = new(StringComparer.Ordinal);

            for (int i = 0; i < population.Count; i++)
            {
                foreach (XPathValue key in KeysOf(population, i, ref context))
                {
                    // A group's key is the key of its first item, and an item joins the group whose key
                    // equals its own. For nearly every key that is a lookup by identity. A float or a
                    // decimal is compared with a group's key as XPath compares the two, each promoted to
                    // the other's type, which is not transitive: the specification's own example has
                    // float 1.0 equal to a decimal, the decimal equal to a double, and the float not equal
                    // to the double — so those are compared against the groups there are.
                    Group? group = key.Kind == XPathValueKind.Number && key.TypeCode is XdmTypeCode.Float or XdmTypeCode.Decimal
                        ? FindNumericGroup(groups, key)
                        : null;

                    if (group is null)
                    {
                        string identity = KeyIdentity(key, collation);

                        if (!byKey.TryGetValue(identity, out group))
                        {
                            group = new Group { Key = key };
                            byKey.Add(identity, group);
                            groups.Add(group);
                        }
                    }

                    group.Items.Add(population[i]);
                }
            }

            return groups;
        }

        private static Group? FindNumericGroup(List<Group> groups, XPathValue key)
        {
            foreach (Group group in groups)
            {
                if (group.Key.Kind == XPathValueKind.Number && NumericallyEqual(group.Key, key))
                {
                    return group;
                }
            }

            return null;
        }

        /// <summary>Whether two numbers are equal once promoted as a value comparison promotes them.</summary>
        private static bool NumericallyEqual(XPathValue left, XPathValue right)
        {
            if (left.TypeCode == XdmTypeCode.Double || right.TypeCode == XdmTypeCode.Double)
            {
                return left.ToNumber() == right.ToNumber();
            }

            if (left.TypeCode == XdmTypeCode.Float || right.TypeCode == XdmTypeCode.Float)
            {
                return (float)left.ToNumber() == (float)right.ToNumber();
            }

            return left.ToDecimal() == right.ToDecimal();
        }

        private List<Group> GroupByAdjacentKey(
            List<XPathValue> population, ref DynamicContext context, Collation? collation)
        {
            List<Group> groups = new();
            string? previous = null;

            for (int i = 0; i < population.Count; i++)
            {
                List<XPathValue> keys = KeysOf(population, i, ref context);

                // group-by lets one item name several groups; group-adjacent cannot, because what it decides
                // is whether this item continues the run the last one began, and two answers to that decide
                // nothing. No answer at all decides nothing either. Composite never reaches this: however
                // many values it read, they are one key, so it always gives exactly one answer.
                if (keys.Count != 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTTE1100,
                        $"A group-adjacent gave {keys.Count} values for one item. It says whether an item "
                        + "continues the run before it, so it has to give exactly one value per item.");
                }

                XPathValue key = keys[0];
                string identity = KeyIdentity(key, collation);

                // A run ends the moment the key changes, so the same value returning later starts afresh.
                if (groups.Count == 0 || identity != previous)
                {
                    groups.Add(new Group { Key = key });
                    previous = identity;
                }

                groups[^1].Items.Add(population[i]);
            }

            return groups;
        }

        private List<Group> GroupByPattern(List<XPathValue> population, ref DynamicContext context)
        {
            List<Group> groups = new();
            bool startNext = true;

            for (int i = 0; i < population.Count; i++)
            {
                bool matches = MatchesAny(population[i], ref context);

                if (m_kind == GroupingKind.StartingWith)
                {
                    // Items before the first match form a group of their own, if there are any.
                    if (matches || groups.Count == 0)
                    {
                        groups.Add(new Group { Key = XPathValue.FromSequence(XdmSequence.Empty) });
                    }

                    groups[^1].Items.Add(population[i]);
                    continue;
                }

                if (startNext)
                {
                    groups.Add(new Group { Key = XPathValue.FromSequence(XdmSequence.Empty) });
                    startNext = false;
                }

                groups[^1].Items.Add(population[i]);
                startNext = matches;
            }

            return groups;
        }

        private bool MatchesAny(XPathValue item, ref DynamicContext context)
        {
            if (item.Kind == XPathValueKind.Node)
            {
                DynamicContext probe = context.SwitchTree(item.NodeTree, item.NodeId);
                return Pattern.MatchesAny(m_pattern!, item.NodeId, ref probe);
            }

            // An atomic value is matched the way a 3.0 pattern matches one: by a predicate on '.'.
            DynamicContext atomic = context.WithAtomicItem(item);

            foreach (Pattern pattern in m_pattern!)
            {
                if (pattern.MatchesItem(item, ref atomic))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A string as a collation keys it, or as it is under the code point collation.</summary>
        private static string Keyed(string text, Collation? collation)
        {
            return collation is null ? text : collation.Key(text);
        }

        private List<XPathValue> KeysOf(List<XPathValue> population, int index, ref DynamicContext context)
        {
            DynamicContext inner = Focus(ref context, population[index], index + 1, population.Count);

            // Atomized, because the values are being compared with each other rather than navigated: a
            // node compared by identity would put every item in a group of its own.
            List<XPathValue> values = XdmSequence.Atomize(XdmSequence.Items(m_key!.Evaluate(ref inner)));

            return m_composite ? new List<XPathValue> { XdmSequence.Concatenate(values) } : values;
        }

        /// <summary>
        /// A key's identity: two keys the same by the rules of deep-equal() have the same identity.
        /// </summary>
        /// <remarks>
        /// Typed, not spelled: 1 and 1.0 are one key and so are two dateTimes naming one instant in two
        /// time zones, while a date and the string that spells it are two keys and no error — a population
        /// mixing types is grouped, not refused. NaN is its own key, as deep-equal has it. A QName is its
        /// namespace and local name, whatever the prefix. A string's identity is the collation's key for
        /// it, where a collation other than the code point one is in force: under one that ignores case,
        /// 'DATA' and 'data' are one key.
        /// </remarks>
        /// <param name="key">The key value.</param>
        /// <param name="collation">The collation strings are keyed under, or null for the code point one.</param>
        internal static string KeyIdentity(XPathValue key, Collation? collation = null)
        {
            // A composite key is several values standing for one, so its identity is the identities of its
            // parts — each written with its length in front, because a delimiter alone would let ("a", "bc")
            // and ("ab", "c") reduce to the same thing. The 'q' says a sequence was read, so a one-value
            // composite key and a value that happens to spell the same cannot meet either.
            if (key.Kind is XPathValueKind.Sequence or XPathValueKind.NodeSet)
            {
                System.Text.StringBuilder joined = new System.Text.StringBuilder("q");

                foreach (XPathValue part in XdmSequence.Items(key))
                {
                    string identity = KeyIdentity(part, collation);
                    joined.Append(':').Append(identity.Length).Append(':').Append(identity);
                }

                return joined.ToString();
            }

            switch (key.Kind)
            {
                case XPathValueKind.Number:
                    return "n:" + key.ToNumber().ToString("R", System.Globalization.CultureInfo.InvariantCulture);

                case XPathValueKind.Boolean:
                    return key.ToBoolean() ? "b:1" : "b:0";

                case XPathValueKind.Node:
                {
                    // A node contributes its typed value, which in a validated tree may be a name or a
                    // number rather than the text it is written as: two names spelled with different
                    // prefixes are one key. Where nothing was validated the typed value is the text, and
                    // going straight to the string saves making the value to throw it away.
                    if (key.NodeTree.HasTypeAnnotations)
                    {
                        XPathValue typed = XdmSequence.TypedValueOf(key);

                        if (typed.Kind != XPathValueKind.Sequence)
                        {
                            return KeyIdentity(typed, collation);
                        }
                    }

                    return "s:" + Keyed(XdmSequence.StringValueOf(key), collation);
                }
            }

            switch (key.TypeCode)
            {
                case XdmTypeCode.String or XdmTypeCode.UntypedAtomic or XdmTypeCode.AnyUri or XdmTypeCode.None:
                    return "s:" + Keyed(key.ToStringValue(), collation);

                case XdmTypeCode.QName:
                {
                    XdmQName name = key.AsQName();
                    return "q:" + name.NamespaceUri + "}" + name.LocalName;
                }

                case XdmTypeCode.DateTime or XdmTypeCode.Date or XdmTypeCode.Time:
                    return "d:" + ((int)key.TypeCode).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ":" + key.AsDateTime().Key;

                default:
                    return "t:" + ((int)key.TypeCode).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ":" + key.ToStringValue();
            }
        }

        private void SortGroups(List<Group> groups, ref DynamicContext context, XsltRuntime runtime)
        {
            (XPathValue Group, XPathValue Key)? saved = runtime.CurrentGroup;
            SortKey[] keys;

            // The attribute value templates of xsl:sort are settled before any group is current: under 3.0
            // there is no current group in them, so current-group() there is XTDE1061 rather than an answer
            // that would differ from sort key to sort key.
            if (m_implements30)
            {
                runtime.CurrentGroup = null;
            }

            try
            {
                keys = SortKey.Resolved(m_sortKeys, ref context);
            }
            finally
            {
                runtime.CurrentGroup = saved;
            }

            int count = groups.Count;
            XPathValue[][] values = new XPathValue[keys.Length][];

            try
            {
                for (int k = 0; k < keys.Length; k++)
                {
                    values[k] = new XPathValue[count];

                    for (int i = 0; i < count; i++)
                    {
                        DynamicContext inner = Focus(ref context, groups[i].Items[0], i + 1, count);
                        runtime.CurrentGroup = (groups[i].Value, groups[i].Key);
                        values[k][i] = keys[k].Evaluate(ref inner);
                    }
                }
            }
            finally
            {
                runtime.CurrentGroup = saved;
            }

            int[] order = SortKey.Order(keys, values, count);
            Group[] original = groups.ToArray();

            for (int i = 0; i < count; i++)
            {
                groups[i] = original[order[i]];
            }
        }
    }

    internal sealed class AnalyzeStringInstruction : Instruction
    {
        /// <summary>What each of the regular expression library's complaints is called here.</summary>
        private static readonly Dictionary<string, XsltErrorCode> RegexCodes = new()
        {
            [nameof(XsltErrorCode.FORX0001)] = XsltErrorCode.XTDE1145,
            [nameof(XsltErrorCode.FORX0002)] = XsltErrorCode.XTDE1140,
            [nameof(XsltErrorCode.FORX0003)] = XsltErrorCode.XTDE1150,
        };

        private readonly Expr m_select;
        private readonly AttributeValueTemplate m_pattern;
        private readonly AttributeValueTemplate? m_flags;
        private readonly Instruction[] m_matching;
        private readonly Instruction[] m_nonMatching;

        /// <summary>
        /// The version this element was written at, which decides whether its pattern may use the
        /// non-capturing group and the <c>q</c> flag that XPath 3.0 adds.
        /// </summary>
        private readonly XsltVersion m_version;

        /// <summary>
        /// Whether the element stands under a 1.0 <c>version</c>, where the input is whatever the select
        /// says, read as a string, rather than a string it has to be.
        /// </summary>
        private readonly bool m_backwardsCompatible;

        /// <summary>Initializes an analyze-string instruction.</summary>
        public AnalyzeStringInstruction(
            Expr select,
            AttributeValueTemplate pattern,
            AttributeValueTemplate? flags,
            Instruction[] matching,
            Instruction[] nonMatching,
            XsltVersion version,
            bool backwardsCompatible)
        {
            m_backwardsCompatible = backwardsCompatible;
            m_select = select;
            m_pattern = pattern;
            m_flags = flags;
            m_matching = matching;
            m_nonMatching = nonMatching;
            m_version = version;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            // The regex and its flags are attribute value templates, not expressions: they are written as
            // text with {} for the parts that vary, the same as any other attribute.
            string input = InputText(ref context);
            string pattern = m_pattern.Evaluate(ref context);
            string flags = m_flags is null ? string.Empty : m_flags.Evaluate(ref context);

            Regex regex = ReadRegex(pattern, flags);

            // Every substring first, matching and not, so that position() and last() in a branch are the
            // substring's place among all of them and how many there are (§13.1) — a branch naming a
            // result document after position() writes each to a name of its own.
            List<(string Text, string[]? Groups)> substrings = new();
            int position = 0;

            foreach (Match match in regex.Matches(input))
            {
                if (match.Index > position)
                {
                    substrings.Add((input[position..match.Index], null));
                }

                string[] groups = new string[match.Groups.Count];
                for (int i = 0; i < match.Groups.Count; i++)
                {
                    groups[i] = match.Groups[i].Success ? match.Groups[i].Value : string.Empty;
                }

                substrings.Add((match.Value, groups));
                position = match.Index + match.Length;
            }

            if (position < input.Length)
            {
                substrings.Add((input[position..], null));
            }

            string[]? saved = runtime.RegexGroups;

            try
            {
                for (int i = 0; i < substrings.Count; i++)
                {
                    (string text, string[]? groups) = substrings[i];
                    runtime.RegexGroups = groups;
                    WriteText(
                        text, groups is null ? m_nonMatching : m_matching, i + 1, substrings.Count, ref context, runtime);
                }
            }
            finally
            {
                runtime.RegexGroups = saved;
            }
        }

        /// <summary>
        /// Reads the input, which the select must produce as one string.
        /// </summary>
        /// <remarks>
        /// The select is declared <c>xs:string</c> — <c>xs:string?</c> from 3.0 — and is read by the function
        /// conversion rules: a node is atomized and an untyped value is a string, but a number or a date is
        /// not, and several items are not one (<c>XPTY0004</c>). Under a 1.0 <c>version</c> the value is
        /// converted as XSLT 1.0 would have, its first item read as a string.
        /// </remarks>
        private string InputText(ref DynamicContext context)
        {
            XPathValue value = m_select.Evaluate(ref context);

            if (m_backwardsCompatible)
            {
                return value.ToStringValue();
            }

            List<XPathValue> items = XdmSequence.Atomize(XdmSequence.Items(value));

            if (items.Count == 0 && m_version >= XsltVersion.V30)
            {
                return string.Empty;
            }

            if (items.Count != 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    "xsl:analyze-string takes one string, and its select produced "
                    + (items.Count == 0 ? "nothing." : $"{items.Count} items."));
            }

            if (items[0].TypeCode is not (XdmTypeCode.String or XdmTypeCode.UntypedAtomic or XdmTypeCode.AnyUri))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    "xsl:analyze-string takes one string, and its select produced a value of another type.");
            }

            return items[0].ToStringValue();
        }

        /// <summary>
        /// Reads the pattern and its flags, giving each complaint the code XSLT gives it here.
        /// </summary>
        /// <remarks>
        /// The regular expression language is XPath's, and so are its complaints — but a stylesheet writing
        /// one on <c>xsl:analyze-string</c> is not calling a function, and XSLT names all three failures
        /// itself: an unreadable pattern is <c>XTDE1140</c>, a letter that is not a flag <c>XTDE1145</c>,
        /// and, before 3.0, a pattern matching the zero-length string <c>XTDE1150</c>. Recoded here rather
        /// than in the translator, which serves <c>matches</c>, <c>replace</c> and <c>tokenize</c> as well
        /// and would have to be told who was asking.
        /// <para>
        /// XSLT 3.0 admits a pattern that matches the zero-length string (§17.1): a zero-length match is a
        /// matching substring of its own, and the character after it goes to the next non-matching one, so
        /// the walk moves on — which is the walk .NET takes over the matches already.
        /// </para>
        /// </remarks>
        /// <param name="pattern">The <c>regex</c> as it came out of its attribute value template.</param>
        /// <param name="flags">The <c>flags</c>, likewise, or empty where none was written.</param>
        private Regex ReadRegex(string pattern, string flags)
        {
            try
            {
                Regex regex = RegexTranslator.Translate(pattern, flags, m_version);

                if (m_version < XsltVersion.V30)
                {
                    RegexTranslator.RequireWidth(regex, pattern, "xsl:analyze-string");
                }

                return regex;
            }
            catch (XsltException error) when (RegexCodes.ContainsKey(error.Code ?? string.Empty))
            {
                throw XsltErrors.Error(RegexCodes[error.Code!], error.Message);
            }
        }

        /// <summary>
        /// Runs one branch over a piece of the input.
        /// </summary>
        /// <remarks>
        /// Inside either branch the context item is the piece itself — an <c>xs:string</c>, not a node — so
        /// <c>.</c> means the substring and nothing can be navigated from it. It was a one-text-node tree
        /// here until XPath 2.0's atomic context item existed to hold it, and the difference shows: a path
        /// written in a branch used to walk that stand-in tree and quietly find nothing, where the
        /// specification says the step had no node to take.
        /// </remarks>
        private static void WriteText(
            string text,
            Instruction[] body,
            int position,
            int size,
            ref DynamicContext context,
            XsltRuntime runtime)
        {
            if (body.Length == 0)
            {
                // A branch that was not written contributes nothing, rather than the text it saw.
                return;
            }

            DynamicContext inner = context.WithAtomicItem(XPathValue.FromString(text));
            inner.CurrentNode = DynamicContext.NotANode;
            inner.Position = position;
            inner.Size = size;

            string? outer = runtime.CurrentSubstring;
            runtime.CurrentSubstring = text;

            try
            {
                ExecuteAll(body, ref inner, runtime);
            }
            finally
            {
                runtime.CurrentSubstring = outer;
            }
        }
    }

    /// <summary>
    /// <c>xsl:next-match</c>, which hands the node on to the template that would have matched had this one
    /// not existed.
    /// </summary>
    /// <remarks>
    /// The difference from <c>xsl:apply-imports</c> is which templates are skipped. <c>apply-imports</c>
    /// passes over everything at this precedence and above, so it can only reach an imported module;
    /// <c>next-match</c> passes over only the templates already tried, so it reaches the next one down by
    /// priority in the same module. That is what makes a chain of templates over one node possible.
    /// </remarks>
    internal sealed class NextMatchInstruction : Instruction
    {
        private readonly WithParameter[] m_parameters;

        /// <summary>Initializes a next-match instruction.</summary>
        /// <param name="parameters">Parameters passed to the template found.</param>
        public NextMatchInstruction(WithParameter[] parameters)
        {
            m_parameters = parameters;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            runtime.ApplyNextMatch(WithParameter.EvaluateAll(m_parameters, ref context, runtime), ref context);
        }
    }

    /// <summary>
    /// <c>xsl:result-document</c>, which sends its body to a second destination.
    /// </summary>
    /// <remarks>
    /// The one instruction that creates something outside the transformation, so it writes only where the
    /// caller has said it may: with no <see cref="IXsltResultResolver"/> configured it is an error rather
    /// than a way for a stylesheet to reach the file system. That is the same posture as the reading resolvers,
    /// applied to the direction that matters most.
    /// </remarks>
    internal sealed class ResultDocumentInstruction : Instruction
    {
        private readonly AttributeValueTemplate? m_href;
        private readonly OutputSettings m_settings;
        private readonly Instruction[] m_body;

        private readonly bool m_insideAStringIsAllowed;
        private readonly (string Name, AttributeValueTemplate Template)[] m_templated;
        private readonly AttributeValueTemplate? m_format;
        private readonly IReadOnlyDictionary<ExpandedName, OutputSettings>? m_formats;
        private readonly IReadOnlyDictionary<string, string>? m_prefixes;

        /// <summary>Initializes a result-document instruction.</summary>
        /// <param name="href">Where to write, or <see langword="null"/> for the unnamed result.</param>
        /// <param name="settings">How to serialize it.</param>
        /// <param name="body">What to write there.</param>
        /// <param name="insideAStringIsAllowed">Whether XSLT 3.0's narrower reading of temporary output
        /// state is in force.</param>
        public ResultDocumentInstruction(
            AttributeValueTemplate? href,
            OutputSettings settings,
            Instruction[] body,
            bool insideAStringIsAllowed = false,
            (string Name, AttributeValueTemplate Template)[]? templated = null,
            AttributeValueTemplate? format = null,
            IReadOnlyDictionary<ExpandedName, OutputSettings>? formats = null,
            IReadOnlyDictionary<string, string>? prefixes = null,
            bool validate = false,
            bool strictValidation = false,
            XdmSchemaType? validationType = null)
        {
            m_href = href;
            m_settings = settings;
            m_body = body;
            m_insideAStringIsAllowed = insideAStringIsAllowed;
            m_templated = templated ?? Array.Empty<(string, AttributeValueTemplate)>();
            m_format = format;
            m_formats = formats;
            m_prefixes = prefixes;
            m_validate = validate;
            m_strictValidation = strictValidation;
            m_validationType = validationType;
        }

        private readonly bool m_validate;
        private readonly bool m_strictValidation;
        private readonly XdmSchemaType? m_validationType;

        /// <summary>
        /// The settings for this run of the instruction: the compiled ones, or where the format or any
        /// serialization attribute was a template, those with the templates settled.
        /// </summary>
        private OutputSettings Settle(ref DynamicContext context)
        {
            if (m_format is null && m_templated.Length == 0)
            {
                return m_settings;
            }

            OutputSettings settings;

            if (m_format is not null)
            {
                string written = m_format.Evaluate(ref context).Trim();

                settings = m_formats!.TryGetValue(FormatName(written), out OutputSettings? named)
                    ? named.Copy()
                    : throw XsltErrors.Error(
                        XsltErrorCode.XTDE1460,
                        $"'{written}' is not an output definition this stylesheet declares, and the format "
                        + "of an xsl:result-document has to name one.");
            }
            else
            {
                settings = m_settings.Copy();
            }

            foreach ((string name, AttributeValueTemplate template) in m_templated)
            {
                SerializationAttributes.Apply(settings, name, template.Evaluate(ref context), ResolvePrefix);
            }

            return settings;
        }

        private string? ResolvePrefix(string prefix)
        {
            return m_prefixes is not null && m_prefixes.TryGetValue(prefix, out string? uri) ? uri : null;
        }

        /// <summary>A computed format name, in any of the spellings a name has.</summary>
        private ExpandedName FormatName(string written)
        {
            if (written.StartsWith("Q{", StringComparison.Ordinal) && written.IndexOf('}') is int close && close > 0)
            {
                return new ExpandedName(written[2..close], written[(close + 1)..]);
            }

            int colon = written.IndexOf(':');

            if (colon < 0)
            {
                return new ExpandedName(string.Empty, written);
            }

            return new ExpandedName(
                ResolvePrefix(written[..colon])
                    ?? throw XsltErrors.Error(
                        XsltErrorCode.XTDE1460,
                        $"'{written}' names no output definition: its prefix is not bound where the "
                        + "xsl:result-document is."),
                written[(colon + 1)..]);
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            // A result document is a document of the transformation's own, and what is being built here is
            // not one: a variable's tree, a captured sequence, a string. Writing to a file from inside one of
            // those would make what reaches the file system depend on whether a variable was ever read, so
            // the specification refuses it outright rather than leaving the order to the processor.
            //
            // XSLT 3.0 draws the line in a different place, and a narrower one. Building the string content
            // of an attribute, a comment, a processing instruction, a namespace, an xsl:value-of or an
            // xsl:message is no longer temporary output state: nothing there is a tree that might or might
            // not be read, the content is being turned into a string there and then. Building a temporary
            // tree or sequence still is — a variable, a function's result, a key value, a sort key, a merge
            // key, an accumulator — and that is what the target being a string capture tells apart. The
            // buffer an xsl:try keeps for its rollback is neither: it stands for the output the try is
            // writing to, and says so.
            if (!runtime.Output.IsFinalOutput
                && !(m_insideAStringIsAllowed && runtime.Output.BecomesAString))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE1480,
                    "xsl:result-document cannot be written while a temporary tree is being built. It "
                    + "produces a document of the transformation's own, and here the output is going into a "
                    + "variable, a function's result, or a captured sequence.");
            }

            OutputSettings settings = Settle(ref context);
            string href = m_href is null ? string.Empty : m_href.Evaluate(ref context);
            string? outerUri = runtime.CurrentOutputUri;

            try
            {
                // No href names the base output URI, which is where the transformation's own result goes,
                // and so does an href that resolves to it. That is a destination the caller has already
                // supplied, so no resolver is involved and none is needed.
                if (href.Length == 0 || runtime.IsBaseOutputUri(runtime.ResolveOutputUri(href)))
                {
                    runtime.CurrentOutputUri = runtime.BaseOutputUri;
                    OutputTarget principal = runtime.ClaimPrincipalResult(settings);

                    if (settings.Method is OutputMethod.Json or OutputMethod.Adaptive
                        && principal is OutputWriter claimed)
                    {
                        SequenceCaptureTarget capture = new SequenceCaptureTarget();
                        WriteBody(capture, settings, ref context, runtime);
                        claimed.WriteSerialized(Serializer.Text(XdmSequence.Items(capture.Finish()), settings));
                    }
                    else
                    {
                        WriteBody(principal, settings, ref context, runtime);
                    }

                    runtime.ReleasePrincipalResult();
                    return;
                }

                string resolved = runtime.ResolveOutputUri(href);
                runtime.CurrentOutputUri = resolved;

                // A transformation fn:transform() is running hands its result documents back as values, so
                // this one is built in memory and put in the map. Nothing is written anywhere, which is why
                // no result resolver is needed and none is asked for.
                if (runtime.Results is TransformResults collected)
                {
                    TransformDestination gathered = collected.Open(resolved, settings);
                    WriteBody(gathered.Target, settings, ref context, runtime);
                    collected.Close(gathered);
                    return;
                }

                ResultDestination destination = runtime.ResolveResultDocument(href);

                if (destination.Stream is Stream stream)
                {
                    // Bytes, so this document's own xsl:output encoding decides them rather than only naming
                    // itself in the declaration. The writer is this engine's and is disposed to flush it; the
                    // stream underneath is the caller's and stays open.
                    using StreamWriter streamWriter = SerializationEncoding.CreateWriter(
                        stream, settings, out OutputSettings encoded);

                    WriteBody(streamWriter, encoded, ref context, runtime);
                    return;
                }

                TextWriter writer = destination.Writer!;
                WriteBody(writer, settings, ref context, runtime);

                // Flushed but not disposed: whatever the resolver opened belongs to the caller, who knows when
                // it is finished with and this engine does not.
                writer.Flush();
            }
            finally
            {
                runtime.CurrentOutputUri = outerUri;
            }
        }

        /// <summary>Runs the body against a writer, restoring the previous output whatever happens.</summary>
        private void WriteBody(
            TextWriter writer,
            OutputSettings settings,
            ref DynamicContext context,
            XsltRuntime runtime)
        {
            // The json and adaptive methods write values rather than a tree: what the body produces is
            // gathered as the sequence it is, and written once it is all there.
            if (settings.Method is OutputMethod.Json or OutputMethod.Adaptive)
            {
                SequenceCaptureTarget capture = new SequenceCaptureTarget();
                WriteBody(capture, settings, ref context, runtime);
                writer.Write(Serializer.Text(XdmSequence.Items(capture.Finish()), settings));
                return;
            }

            OutputWriter output = new OutputWriter(writer, settings) { DeclaresWhenEmpty = true };
            WriteBody(output, settings, ref context, runtime);
            output.Flush();
        }

        /// <summary>Runs the body against a target, restoring the previous output whatever happens.</summary>
        private void WriteBody(
            OutputTarget output, OutputSettings settings, ref DynamicContext context, XsltRuntime runtime)
        {
            OutputTarget previous = runtime.Output;
            runtime.Output = output;

            try
            {
                if (m_validate)
                {
                    WriteValidatedBody(output, settings, ref context, runtime);
                }
                else
                {
                    ExecuteAll(m_body, ref context, runtime);
                }
            }
            finally
            {
                runtime.Output = previous;
            }

            if (output is OutputWriter written)
            {
                written.Flush();
            }
        }

        /// <summary>
        /// Builds the result document's content, validates it as a document node against the schemas in
        /// scope, and then writes it out. The serialized bytes do not depend on the type annotations, so
        /// what validation is for here is the error it raises on an invalid document.
        /// </summary>
        private void WriteValidatedBody(
            OutputTarget output, OutputSettings settings, ref DynamicContext context, XsltRuntime runtime)
        {
            SchemaComponents schemas = runtime.Schemas
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XTSE1660, "Validation was asked for, but the stylesheet imported no schema.");

            // §25.1: validation "is applied to the document node produced as the result of sequence
            // normalization", and §2.3.6.1 says what that process does — it reads one serialization
            // parameter, item-separator, and puts its value between every pair of items. So the tree
            // validated here is built with the separators in it, the specification saying outright that
            // "an inappropriate choice of item-separator may cause the result to become invalid".
            ResultTreeBuilder builder = new ResultTreeBuilder { NormalizedItemSeparator = settings.ItemSeparator };
            runtime.Output = builder;

            try
            {
                ExecuteAll(m_body, ref context, runtime);
            }
            finally
            {
                runtime.Output = output;
            }

            XdmTree built = builder.Finish();
            NodeValidator validator = new NodeValidator(schemas);

            TypeOverlay overlay = m_validationType is not null
                ? validator.ValidateElementAgainstType(built, m_validationType)
                : validator.ValidateDocument(built, m_strictValidation);

            XdmTree annotated = built.WithTypeAnnotations(overlay);

            // The separators are text nodes of this tree now, so the serializer must not put its own
            // between the items it is handed as well.
            bool separating = output.SuspendItemSeparation();

            try
            {
                for (int child = annotated.FirstChildOf(XdmTree.RootNode);
                    child >= 0;
                    child = annotated.NextSiblingOf(child))
                {
                    NodeCopier.CopyDeep(annotated, child, output, runtime: runtime, types: overlay);
                }
            }
            finally
            {
                output.ResumeItemSeparation(separating);
            }
        }
    }

    /// <summary>
    /// <c>xsl:sequence</c>, which contributes a value rather than writing text.
    /// </summary>
    /// <remarks>
    /// The difference from <c>xsl:copy-of</c> is what happens to atomic values: <c>xsl:sequence</c> passes
    /// them along as values, where copying turns everything into nodes and text. Inside <c>xsl:function</c>
    /// and <c>xsl:variable</c> that distinction is the whole point, since it is how a typed value is returned
    /// rather than a string of it.
    /// </remarks>
    internal sealed class SequenceInstruction : Instruction
    {
        private readonly Expr? m_select;
        private readonly Instruction[]? m_body;

        /// <summary>Initializes a sequence instruction.</summary>
        /// <param name="select">The value to contribute.</param>
        public SequenceInstruction(Expr select)
        {
            m_select = select;
        }

        /// <summary>Initializes a sequence instruction whose value is what its content produces.</summary>
        /// <param name="body">The sequence constructor.</param>
        public SequenceInstruction(Instruction[] body)
        {
            m_body = body;
        }

        /// <summary>
        /// Gets the expression, so that a caller wanting the value itself can take it unwritten — or null
        /// where the value is the content's.
        /// </summary>
        public Expr? Select => m_select;

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            if (m_select is null)
            {
                // The content writes itself where the instruction stands. xsl:sequence adds nothing to what a
                // sequence constructor produces, and 3.0 allows one as content for what can then be put
                // around it: an xsl:on-empty, an xsl:fallback.
                ExecuteAll(m_body!, ref context, runtime);
                return;
            }

            WriteValue(m_select.Evaluate(ref context), runtime);
        }

        /// <summary>Writes a value into the current output, as xsl:sequence writes what it selected.</summary>
        /// <remarks>
        /// Also what a transformation started at a named function writes: the value the function returned
        /// is the whole result, and it reaches the output the way an xsl:sequence at the top of a template
        /// would have put it there.
        /// </remarks>
        /// <param name="value">The value to write.</param>
        /// <param name="runtime">The transformation, whose current output it goes to.</param>
        internal static void WriteValue(XPathValue value, XsltRuntime runtime)
        {
            WriteValue(value, runtime.Output);
        }

        /// <summary>Writes a value into a target of the caller's choosing.</summary>
        /// <param name="value">The value to write.</param>
        /// <param name="output">Where it goes.</param>
        internal static void WriteValue(XPathValue value, Runtime.OutputTarget output)
        {
            // Where the result is a sequence rather than markup, the value goes in as it is: this is the one
            // instruction that can contribute something other than nodes and text.
            if (output.TryAppendValue(value))
            {
                return;
            }

            if (value.Kind == XPathValueKind.NodeSet)
            {
                NodeSet nodes = value.AsNodeSet();
                for (int i = 0; i < nodes.Count; i++)
                {
                    NodeCopier.CopyDeep(nodes.TreeAt(i), nodes[i], output);
                }

                return;
            }

            // An array contributes its members here too, a result tree having no way to hold one.
            List<XPathValue> items = XdmSequence.ContentItems(value);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Kind == XPathValueKind.Node)
                {
                    NodeCopier.CopyDeep(items[i].NodeTree, items[i].NodeId, output);
                    continue;
                }

                // A map or a function item cannot be part of a node's content, which is what is being built
                // once the value could not be appended as it is.
                if (items[i].Kind is XPathValueKind.Map or XPathValueKind.Function)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE0450,
                        "A map or a function item cannot be written into the content of a node.");
                }

                // Written as an atomic value, which is what puts a single space between it and an atomic value
                // written just before — by this instruction or by the one before it, and never after a node.
                output.WriteAtomic(XdmSequence.StringValueOf(items[i]));
            }
        }
    }
}
