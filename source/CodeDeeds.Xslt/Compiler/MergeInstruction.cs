using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// What one <c>xsl:merge-key</c> says about the ordering, as written.
    /// </summary>
    /// <remarks>
    /// All five are attribute value templates, so what the stylesheet says is not settled until the merge
    /// runs — and the specification hangs a rule on their effective values, that corresponding keys of two
    /// sources agree about every one of them. Null is the attribute not being written at all, which counts
    /// as a value of its own for that comparison: present on one and absent on the other is a disagreement.
    /// </remarks>
    /// <param name="Order">Whether the ordering is reversed.</param>
    /// <param name="DataType">Whether the values are compared as numbers.</param>
    /// <param name="Language">The language to collate text in.</param>
    /// <param name="CaseOrder">Where case falls when text is otherwise equal.</param>
    /// <param name="Collation">
    /// Which collation orders text. Compared for agreement and then not used: this engine collates by code
    /// point and refuses any other collation where the key is compiled, so the only value that reaches here
    /// is the one it would have used anyway.
    /// </param>
    internal sealed record MergeKeyOrdering(
        AttributeValueTemplate? Order,
        AttributeValueTemplate? DataType,
        AttributeValueTemplate? Language,
        AttributeValueTemplate? CaseOrder,
        AttributeValueTemplate? Collation)
    {
        /// <summary>The attribute names, in the order <see cref="Effective"/> gives their values.</summary>
        public static readonly string[] Names =
        {
            "order", "data-type", "lang", "case-order", "collation",
        };

        /// <summary>Reads what the five attributes effectively say.</summary>
        /// <param name="context">The focus the templates are evaluated in.</param>
        public string?[] Effective(ref DynamicContext context)
        {
            return new[]
            {
                Order?.Evaluate(ref context),
                DataType?.Evaluate(ref context),
                Language?.Evaluate(ref context),
                CaseOrder?.Evaluate(ref context),
                Collation?.Evaluate(ref context),
            };
        }
    }

    /// <summary>
    /// One <c>xsl:merge-source</c>: where its items come from, and what orders them.
    /// </summary>
    /// <param name="Name">
    /// The name a <c>current-merge-group()</c> may ask for, or null where the source was not named.
    /// </param>
    /// <param name="ForEachItem">An expression giving one context per run of <c>Select</c>.</param>
    /// <param name="ForEachSource">An expression giving document URIs, one context per run of <c>Select</c>.</param>
    /// <param name="Select">What to take from each of those contexts.</param>
    /// <param name="Keys">The merge keys, most significant first.</param>
    /// <param name="Ordering">What each of those keys says about the ordering, one for one with them.</param>
    /// <param name="BaseUri">
    /// Where <c>ForEachSource</c>'s relative references resolve from, which is where the
    /// <c>xsl:merge-source</c> was written. Passing nothing had them resolve against nothing, so a source
    /// naming a file beside the stylesheet was never found.
    /// </param>
    /// <param name="SortBeforeMerge">
    /// Whether the caller has withdrawn the promise that the items arrive in order, and asked for them to be
    /// put in order here instead.
    /// </param>
    /// <param name="Accumulators">
    /// Which accumulators apply to a document <c>ForEachSource</c> reads. Nothing else here makes a document
    /// available, so nothing else can narrow the set.
    /// </param>
    internal sealed record MergeSource(
        ExpandedName? Name,
        Expr? ForEachItem,
        Expr? ForEachSource,
        Expr Select,
        SortKey[] Keys,
        MergeKeyOrdering[] Ordering,
        string? BaseUri = null,
        bool SortBeforeMerge = false,
        AccumulatorSet? Accumulators = null);

    /// <summary>What <c>current-merge-group()</c> and <c>current-merge-key()</c> answer.</summary>
    /// <param name="Group">Every item of the group, in source order.</param>
    /// <param name="BySource">The items each named source contributed.</param>
    /// <param name="Key">The key values this group shares.</param>
    internal sealed record MergeGroup(
        XPathValue Group, IReadOnlyDictionary<ExpandedName, XPathValue> BySource, XPathValue Key);

    /// <summary>
    /// <c>xsl:merge</c>, which walks several already-ordered sequences at once and visits each distinct key
    /// exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What separates it from concatenating the sources and using <c>xsl:for-each-group</c> is that the
    /// grouping never has to hold more than one item per source in hand: a merge of ten sorted logs reads
    /// each of them once, in order, and never has all ten in memory. That is why the instruction exists, and
    /// why the streamable form of it is the one the specification cares most about.
    /// </para>
    /// <para>
    /// This implementation is not streaming — it materialises each source and then merges — so what it
    /// delivers is the semantics rather than the memory profile. Materialising a source does not make the
    /// ordering rules go away: what each source promises about its own order is what the whole instruction
    /// rests on, and <c>sort-before-merge</c> is how a caller withdraws that promise.
    /// </para>
    /// </remarks>
    internal sealed class MergeInstruction : Instruction
    {
        private readonly MergeSource[] m_sources;
        private readonly Instruction[] m_action;

        /// <summary>Initializes a merge instruction.</summary>
        /// <param name="sources">The sources, in the order they were written.</param>
        /// <param name="action">What to do with each group.</param>
        public MergeInstruction(MergeSource[] sources, Instruction[] action)
        {
            m_sources = sources;
            m_action = action;
        }

        /// <summary>One item of one source, with the key values that place it.</summary>
        /// <param name="Item">The item itself.</param>
        /// <param name="Keys">Its key values, atomized, most significant first.</param>
        /// <param name="Position">
        /// Where it stood in the order its own sequence was read in, which is what breaks a tie: two items
        /// the keys cannot tell apart still reach the action in the order they arrived.
        /// </param>
        private readonly record struct Entry(XPathValue Item, XPathValue[] Keys, int Position);

        /// <summary>
        /// One input sequence: what a single anchor item of one source contributed, in the order it did.
        /// </summary>
        /// <remarks>
        /// A source is not always one sequence. <c>for-each-item</c> and <c>for-each-source</c> run the
        /// select once per anchor item, and each of those runs is an input sequence in its own right — which
        /// is the point of them, one <c>xsl:merge-source</c> standing for a whole collection of files, or of
        /// sections of one file, that share a shape. Each is separately in order and the merge interleaves
        /// them; running them together into one sequence would say they were in order end to end, which
        /// nothing promised and which two sorted classes of pupils are not.
        /// </remarks>
        /// <param name="Source">Which <c>xsl:merge-source</c> it came from.</param>
        /// <param name="Entries">Its items, with the key values that place them.</param>
        private readonly record struct Population(int Source, List<Entry> Entries);

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            SortKey[][] keys = ResolveKeys(ref context);
            List<Population> populations = new List<Population>();

            for (int i = 0; i < m_sources.Length; i++)
            {
                Populate(m_sources[i], i, keys[i], populations, ref context, runtime);
            }

            // Before the ordering, not after it: whether a sequence is in order is decided by comparing its
            // keys, and there is no saying whether a sequence of durations is sorted while it is still an
            // open question whether they can be compared with the integers in the source beside them.
            RequireComparableKeys(populations, keys[0].Length, m_sources.Length);

            foreach (Population population in populations)
            {
                PutInOrder(m_sources[population.Source], keys[population.Source], population.Entries);
            }

            // Every group is taken before any action runs, because the action may ask how many there are:
            // last() inside an xsl:merge-action is the number of groups, and that is not settled until the
            // last source is spent. Nothing is materialised twice — a group holds the same items the
            // populations already do.
            List<MergeGroup> groups = new List<MergeGroup>();
            int[] at = new int[populations.Count];

            for (int lowest = Lowest(populations, keys, at);
                lowest >= 0;
                lowest = Lowest(populations, keys, at))
            {
                groups.Add(
                    TakeGroup(populations, keys, at, populations[lowest].Entries[at[lowest]].Keys));
            }

            MergeGroup? saved = runtime.CurrentMerge;
            int savedDepth = runtime.CurrentMergeDepth;

            try
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    runtime.CurrentMerge = groups[i];

                    // Recorded with the group, because the two merge functions belong to this sequence
                    // constructor and to nothing it calls. Anything the action invokes runs one level
                    // deeper, and that is what the accessors compare against.
                    runtime.CurrentMergeDepth = runtime.CallDepth;

                    // The focus an action sees: the group's first item, at the group's own place among the
                    // groups. So position() counts groups rather than items, and xsl:copy in an action
                    // copies the item that opened the group — the same shape xsl:for-each-group gives its
                    // body, which is the instruction this one is written to stand beside.
                    DynamicContext inner = context.WithItem(XdmSequence.FirstItem(groups[i].Group));
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    inner.Position = i + 1;
                    inner.Size = groups.Count;

                    ExecuteAll(m_action, ref inner, runtime);
                }
            }
            finally
            {
                runtime.CurrentMerge = saved;
                runtime.CurrentMergeDepth = savedDepth;
            }
        }

        /// <summary>
        /// Reads what each source says about the ordering, refuses two that disagree, and gives back the
        /// keys this run is to order by.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The attributes saying how a key orders are attribute value templates, so this is a question with
        /// an answer only once the transformation is running — which is why the rule against two sources
        /// disagreeing is a dynamic error and why it is settled here rather than where the merge was
        /// compiled.
        /// </para>
        /// <para>
        /// The first source is compared against each of the others rather than every pair against every
        /// other, agreement being an equivalence: if they all agree with the first they all agree.
        /// </para>
        /// </remarks>
        private SortKey[][] ResolveKeys(ref DynamicContext context)
        {
            SortKey[][] keys = new SortKey[m_sources.Length][];
            string?[][]? first = null;

            for (int i = 0; i < m_sources.Length; i++)
            {
                MergeSource source = m_sources[i];
                string?[][] said = new string?[source.Keys.Length][];
                keys[i] = new SortKey[source.Keys.Length];

                for (int k = 0; k < source.Keys.Length; k++)
                {
                    said[k] = source.Ordering[k].Effective(ref context);

                    // The collation the key was compiled with, or the one its attribute computes: a merge
                    // key takes a collation as a sort key does, and dropping it here had every merge key
                    // ordering by code point whatever it said.
                    keys[i][k] = source.Keys[k].Ordered(
                        said[k][1] == "number",
                        said[k][0] == "descending",
                        SortKey.CultureFor(said[k][2]),
                        said[k][3] switch
                        {
                            "upper-first" => SortCaseOrder.UpperFirst,
                            "lower-first" => SortCaseOrder.LowerFirst,
                            _ => SortCaseOrder.Unspecified,
                        },
                        said[k][4] is string uri
                            ? SortKey.ResolveCollation(uri.Trim(), Collation.ResolverOf(ref context))
                            : source.Keys[k].KeyCollation);
                }

                if (first is null)
                {
                    first = said;
                    continue;
                }

                RequireAgreement(first, said);
            }

            return keys;
        }

        /// <summary>Refuses two sources whose corresponding keys do not describe one ordering.</summary>
        /// <param name="first">What the first source's keys said.</param>
        /// <param name="other">What another source's keys said.</param>
        private static void RequireAgreement(string?[][] first, string?[][] other)
        {
            // The counts agreeing is a static error, raised where the merge was compiled.
            for (int k = 0; k < Math.Min(first.Length, other.Length); k++)
            {
                for (int a = 0; a < MergeKeyOrdering.Names.Length; a++)
                {
                    if (string.Equals(first[k][a], other[k][a], StringComparison.Ordinal))
                    {
                        continue;
                    }

                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE2210,
                        $"Two xsl:merge-source disagree about their merge key {k + 1}: one says "
                        + $"{MergeKeyOrdering.Names[a]}='{first[k][a] ?? "(nothing)"}' and the other "
                        + $"{MergeKeyOrdering.Names[a]}='{other[k][a] ?? "(nothing)"}'. Keys in "
                        + "corresponding positions order the sources against one another, and two "
                        + "orderings are not one.");
                }
            }
        }

        /// <summary>Reads one source's input sequences, with the keys that order them.</summary>
        /// <param name="source">The source to read.</param>
        /// <param name="index">Which source it is, which the sequences it gives carry with them.</param>
        /// <param name="keys">The keys this run orders by.</param>
        /// <param name="into">Where to add the sequences, after any a previous source gave.</param>
        /// <param name="context">The focus the <c>xsl:merge</c> stands in.</param>
        /// <param name="runtime">The running transformation, which loads any documents named.</param>
        private static void Populate(
            MergeSource source,
            int index,
            SortKey[] keys,
            List<Population> into,
            ref DynamicContext context,
            XsltRuntime runtime)
        {
            List<XPathValue>? contexts = null;

            if (source.ForEachItem is not null)
            {
                contexts = XdmSequence.Items(source.ForEachItem.Evaluate(ref context));
            }
            else if (source.ForEachSource is not null)
            {
                // Each item names a document, which is what lets one xsl:merge-source stand for a whole
                // collection of files that share a shape.
                contexts = new List<XPathValue>();
                foreach (XPathValue href in XdmSequence.Items(source.ForEachSource.Evaluate(ref context)))
                {
                    // The attribute is xs:string*, read by the function conversion rules: a node is its
                    // string value and an untyped value a string, and a number is neither (XPTY0004).
                    if (href.Kind != XPathValueKind.Node
                        && href.TypeCode is not (XdmTypeCode.String or XdmTypeCode.UntypedAtomic or XdmTypeCode.AnyUri))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XPTY0004,
                            "for-each-source on xsl:merge-source names documents by URI, and produced a value "
                            + "that is not a string.");
                    }

                    XdmTree document = runtime.LoadDocument(href.ToStringValue(), source.BaseUri);
                    runtime.MakeAvailable(document, source.Accumulators ?? AccumulatorSet.None);
                    contexts.Add(XPathValue.FromNode(document, XdmTree.RootNode));
                }
            }

            // Null rather than a one-element list of nothing: with no for-each the select is evaluated once
            // in the context the xsl:merge stands in, which is not the same as evaluating it once in a
            // context with no item.
            int rounds = contexts?.Count ?? 1;

            for (int round = 0; round < rounds; round++)
            {
                DynamicContext inner = contexts is null ? context : context.WithItem(contexts[round]);
                List<XPathValue> items = XdmSequence.Items(source.Select.Evaluate(ref inner));
                List<Entry> entries = new List<Entry>(items.Count);

                for (int i = 0; i < items.Count; i++)
                {
                    // A singleton focus: the item, at position one, of one. Not the position in the
                    // sequence, which is what an xsl:sort key sees — and the difference is the whole reason
                    // xsl:merge exists. A merge is meant to be readable one item at a time, and an item
                    // arriving on its own does not know where in its source it stands or how many follow it.
                    // A key that could ask would be a key no streaming processor could evaluate.
                    DynamicContext keyed = inner.WithItem(items[i]);
                    keyed.CurrentNode = keyed.Node;
                    keyed.CurrentTree = keyed.Tree;
                    keyed.Position = 1;
                    keyed.Size = 1;

                    XPathValue[] values = new XPathValue[keys.Length];
                    for (int k = 0; k < keys.Length; k++)
                    {
                        values[k] = Atomized(keys[k].Evaluate(ref keyed));
                    }

                    entries.Add(new Entry(items[i], values, i));
                }

                into.Add(new Population(index, entries));
            }
        }

        /// <summary>
        /// Reduces a key value to the atomic value that orders by it.
        /// </summary>
        /// <remarks>
        /// A key selecting a node has to be atomized before its type says anything: an attribute node is not
        /// an atomic value, and the untyped text inside it is what a comparison reads. The ordering itself
        /// never noticed, taking the string value either way — the comparability check does.
        /// </remarks>
        private static XPathValue Atomized(XPathValue value)
        {
            List<XPathValue> items = XdmSequence.Atomize(XdmSequence.Items(value));

            return items.Count == 1 ? items[0] : XdmSequence.Concatenate(items);
        }

        /// <summary>
        /// Puts one source's items into the order its keys say, or refuses the merge because they are not in
        /// it already.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A merge takes its sources in order; it does not put them in order. That is the whole point of the
        /// instruction — walking ten sorted logs in step needs no more memory than ten items — so a source
        /// that arrives unsorted has broken the promise the instruction is built on, and <c>XTDE2220</c>
        /// says which one. Writing <c>sort-before-merge</c> withdraws the promise, and putting the items in
        /// order then becomes this instruction's own job.
        /// </para>
        /// <para>
        /// Checking before sorting also keeps items the keys cannot tell apart in the order they were read.
        /// A comparison sort is free to permute equal keys, and what the action sees is a sequence whose
        /// order a stylesheet can observe.
        /// </para>
        /// </remarks>
        private static void PutInOrder(MergeSource source, SortKey[] keys, List<Entry> entries)
        {
            if (IsInOrder(keys, entries))
            {
                return;
            }

            if (!source.SortBeforeMerge)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE2220,
                    "An xsl:merge-source's items are not in the order its xsl:merge-key children put them. "
                    + "A merge walks its sources in step and takes each of them as already sorted; write "
                    + "sort-before-merge='yes' on the source to have them sorted here instead.");
            }

            entries.Sort((left, right) =>
            {
                int order = Compare(keys, left.Keys, right.Keys);
                return order != 0 ? order : left.Position.CompareTo(right.Position);
            });
        }

        /// <summary>Whether a source's items already stand in the order its keys put them.</summary>
        private static bool IsInOrder(SortKey[] keys, List<Entry> entries)
        {
            for (int i = 1; i < entries.Count; i++)
            {
                if (Compare(keys, entries[i - 1].Keys, entries[i].Keys) > 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Refuses a merge whose sources give key values that cannot be compared with one another.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The keys are what put the sources in step, so deciding which source to take from next compares a
        /// key of one against a key of another — and XPath compares an integer to an integer and a date to a
        /// date, not an integer to a duration. Within a single source it does not arise: a merge never has
        /// to decide what a stylesheet meant by ordering its own items that way.
        /// </para>
        /// <para>
        /// Asked of the types rather than of every pair of values, because comparability follows from the
        /// pair of types alone. An ordinary merge has one type per key per source, so this is a handful of
        /// comparisons however long the sources are.
        /// </para>
        /// </remarks>
        /// <param name="populations">Every input sequence, in the order they were read.</param>
        /// <param name="keyCount">How many keys each source orders by.</param>
        /// <param name="sources">How many <c>xsl:merge-source</c> there are.</param>
        private static void RequireComparableKeys(
            List<Population> populations, int keyCount, int sources)
        {
            if (sources < 2)
            {
                return;
            }

            for (int k = 0; k < keyCount; k++)
            {
                List<XPathValue>[] used = new List<XPathValue>[sources];

                for (int i = 0; i < sources; i++)
                {
                    used[i] = new List<XPathValue>();
                }

                foreach (Population population in populations)
                {
                    CollectTypes(population.Entries, k, used[population.Source]);
                }

                for (int i = 1; i < sources; i++)
                {
                    for (int j = 0; j < i; j++)
                    {
                        foreach (XPathValue left in used[j])
                        {
                            foreach (XPathValue right in used[i])
                            {
                                RequireComparable(left, right);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Adds one value for each atomic type these items give at one key position.</summary>
        /// <param name="entries">One input sequence.</param>
        /// <param name="key">Which key position to look at.</param>
        /// <param name="found">What the source this sequence belongs to has given so far.</param>
        private static void CollectTypes(List<Entry> entries, int key, List<XPathValue> found)
        {
            foreach (Entry entry in entries)
            {
                if (key >= entry.Keys.Length)
                {
                    continue;
                }

                XPathValue value = entry.Keys[key];

                // Only a single atomic value has a type to compare with. A key that selected nothing is not
                // an item, and takes part in no comparison.
                if (value.Kind is not (XPathValueKind.Boolean or XPathValueKind.Number
                    or XPathValueKind.String))
                {
                    continue;
                }

                if (!found.Exists(seen => seen.TypeCode == value.TypeCode))
                {
                    found.Add(value);
                }
            }
        }

        /// <summary>Refuses two key values that the XPath <c>le</c> operator has no answer for.</summary>
        private static void RequireComparable(XPathValue left, XPathValue right)
        {
            try
            {
                XdmComparison.Value(left, right, BinaryOperator.LessThanOrEqual);
            }
            catch (XsltException error) when (error.Code == nameof(XsltErrorCode.XPTY0004))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE2230,
                    "Two xsl:merge-source give merge keys that cannot be compared with one another. "
                    + $"{error.Message} A merge decides which source to take from next by comparing their "
                    + "keys, so the sources have to agree about what kind of value a key is.");
            }
        }

        /// <summary>
        /// Finds the input sequence whose next unread item has the lowest key, or -1 when all are spent.
        /// </summary>
        private static int Lowest(List<Population> populations, SortKey[][] keys, int[] at)
        {
            int lowest = -1;

            for (int p = 0; p < populations.Count; p++)
            {
                List<Entry> entries = populations[p].Entries;

                if (at[p] >= entries.Count)
                {
                    continue;
                }

                if (lowest < 0
                    || Compare(
                        keys[populations[p].Source],
                        entries[at[p]].Keys,
                        populations[lowest].Entries[at[lowest]].Keys) < 0)
                {
                    lowest = p;
                }
            }

            return lowest;
        }

        /// <summary>Takes every item at one key, from every source that has one, and advances past them.</summary>
        /// <remarks>
        /// Walked source by source rather than sequence by sequence, because that is what a group is: what
        /// each <c>xsl:merge-source</c> contributed, in the order the sources were written, however many
        /// input sequences one of them stands for.
        /// </remarks>
        private MergeGroup TakeGroup(
            List<Population> populations, SortKey[][] keys, int[] at, XPathValue[] key)
        {
            List<XPathValue> all = new List<XPathValue>();
            Dictionary<ExpandedName, XPathValue> bySource = new Dictionary<ExpandedName, XPathValue>();

            for (int i = 0; i < m_sources.Length; i++)
            {
                List<XPathValue> mine = new List<XPathValue>();

                for (int p = 0; p < populations.Count; p++)
                {
                    if (populations[p].Source != i)
                    {
                        continue;
                    }

                    List<Entry> entries = populations[p].Entries;

                    while (at[p] < entries.Count && Compare(keys[i], entries[at[p]].Keys, key) == 0)
                    {
                        mine.Add(entries[at[p]].Item);
                        at[p]++;
                    }
                }

                all.AddRange(mine);

                if (m_sources[i].Name is ExpandedName named)
                {
                    bySource[named] = XdmSequence.Concatenate(mine);
                }
            }

            return new MergeGroup(XdmSequence.Concatenate(all), bySource, XdmSequence.Concatenate(key));
        }

        /// <summary>
        /// Orders two key tuples.
        /// </summary>
        /// <remarks>
        /// Which source's keys are passed in does not matter, every source having been made to agree about
        /// them: two sources ordered differently could not be merged at all, there being no single sequence
        /// for the result to be in.
        /// </remarks>
        private static int Compare(SortKey[] keys, XPathValue[] left, XPathValue[] right)
        {
            int count = Math.Min(Math.Min(keys.Length, left.Length), right.Length);

            for (int k = 0; k < count; k++)
            {
                int order = CompareValues(keys[k], left[k], right[k]);

                if (order != 0)
                {
                    return keys[k].Descending ? -order : order;
                }
            }

            return 0;
        }

        /// <summary>
        /// Orders one pair of key values.
        /// </summary>
        /// <remarks>
        /// A value that carries a type of its own is ordered by that type, because reading <c>1 to 50</c> as
        /// text puts 10 before 2 — and a merge that believes its own source is out of order refuses it.
        /// Everything untyped or textual keeps the string comparison, which is both what the specification
        /// asks for an untyped key and where the key's language, case-order and collation live.
        /// </remarks>
        private static int CompareValues(SortKey key, XPathValue left, XPathValue right)
        {
            if (key.Numeric)
            {
                return left.ToNumber().CompareTo(right.ToNumber());
            }

            if (!Xpath2FunctionExpr.IsText(left) && !Xpath2FunctionExpr.IsText(right)
                && XdmComparison.TryOrder(left, right, out int order))
            {
                return order;
            }

            return key.CompareText(left.ToStringValue(), right.ToStringValue());
        }
    }
}
