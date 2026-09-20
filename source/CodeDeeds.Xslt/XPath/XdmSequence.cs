using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// An ordered list of items — the value of every XPath 2.0 expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The difference from <see cref="NodeSet"/> is not that this holds atomic values as well as nodes,
    /// though it does. It is that a sequence keeps what it was given: <c>(3, 1, 1)</c> stays in that order
    /// with both copies of 1, where a node-set is sorted into document order and de-duplicated the moment it
    /// is built. XPath 1.0 needs that normalisation everywhere and XPath 2.0 needs it only for the result of
    /// a path, so the two cannot share one representation without one of them being wrong.
    /// </para>
    /// <para>
    /// A single item is not wrapped in a sequence. XPath 2.0 makes no distinction between an item and the
    /// sequence of length one containing it, so the common case stays an ordinary <see cref="XPathValue"/>
    /// and nothing is allocated for it.
    /// </para>
    /// </remarks>
    public sealed class XdmSequence : IReadOnlyList<XPathValue>
    {
        private readonly XPathValue[]? m_items;
        private readonly System.Numerics.BigInteger m_first;
        private readonly int m_length;

        /// <summary>Initializes a sequence over items the caller no longer owns.</summary>
        /// <param name="items">The items, in order.</param>
        public XdmSequence(XPathValue[] items)
        {
            m_items = items;
            m_length = items.Length;
        }

        private XdmSequence(System.Numerics.BigInteger first, int length)
        {
            m_items = null;
            m_first = first;
            m_length = length;
        }

        /// <summary>
        /// A run of consecutive integers, held as where it starts and how long it is.
        /// </summary>
        /// <remarks>
        /// <c>1 to 10000000</c> is ten million items and two numbers, and which of those it costs
        /// depends on what is asked of it. Counting it, or asking whether some number is among it,
        /// needs neither the items nor the memory they would take; anything that genuinely wants them
        /// one at a time gets them from the indexer, built as they are asked for.
        /// </remarks>
        /// <param name="first">The first integer.</param>
        /// <param name="length">How many there are.</param>
        internal static XdmSequence OfRange(System.Numerics.BigInteger first, int length)
        {
            return new XdmSequence(first, length);
        }

        /// <summary>Whether this is a range rather than an array of items.</summary>
        internal bool IsRange => m_items is null;

        /// <summary>
        /// The most items a range will be expanded into, where something genuinely wants them all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The limit is on laying a range out and not on having one, which is the distinction that
        /// matters: <c>count(1 to 10000000)</c> and <c>5 = (1 to 10000000)</c> are answered from the
        /// bounds, and only something like <c>for $i in 1 to 10000000</c> asks for the items.
        /// </para>
        /// <para>
        /// Two to the twenty-second, an item being sixteen bytes here, so about sixty-four megabytes
        /// held at once. It was a tenth of that while every range was built whether its items were
        /// wanted or not, where a tight bound cost nothing; now that it is reached only by a caller
        /// genuinely walking them, a million is a number a stylesheet can mean — building a name a
        /// megabyte long is an ordinary enough thing to ask, and the W3C suite asks it.
        /// </para>
        /// </remarks>
        internal const int ExpandableRange = 4_194_304;

        /// <summary>The sequence of no items, which is what <c>()</c> denotes.</summary>
        public static XdmSequence Empty { get; } = new XdmSequence(Array.Empty<XPathValue>());

        /// <summary>Gets the number of items.</summary>
        public int Count => m_length;

        /// <summary>Gets the item at a position.</summary>
        /// <param name="index">A zero-based index below <see cref="Count"/>.</param>
        public XPathValue this[int index] => m_items is not null
            ? m_items[index]
            : XPathValue.FromInteger(m_first + index);

        /// <summary>Walks the items in order.</summary>
        public IEnumerator<XPathValue> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        /// <inheritdoc/>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Builds the value denoting a list of items, collapsing the cases a sequence is not needed for.
        /// </summary>
        /// <remarks>
        /// Nested sequences do not exist in the data model — <c>(1, (2, 3))</c> is <c>(1, 2, 3)</c> — so this
        /// flattens as it goes. A node-set among the items contributes its nodes.
        /// </remarks>
        /// <param name="items">The values to concatenate, each of which may itself be several items.</param>
        public static XPathValue Concatenate(IReadOnlyList<XPathValue> items)
        {
            // Counted first and filled once: a list grown item by item and then copied out costs two
            // arrays' worth of garbage for every sequence a function returns, and a thousand atomized
            // prices go through here for every avg() over them.
            int count = 0;
            foreach (XPathValue item in items)
            {
                count += ItemCount(item);
            }

            if (count == 0)
            {
                return XPathValue.FromSequence(Empty);
            }

            XPathValue[] flat = new XPathValue[count];
            int at = 0;
            foreach (XPathValue item in items)
            {
                at = FlattenInto(item, flat, at);
            }

            return count == 1 ? flat[0] : XPathValue.FromSequence(new XdmSequence(flat));
        }

        /// <summary>How many items a value flattens to.</summary>
        /// <summary>How many items a value holds, without expanding it to find out.</summary>
        /// <param name="value">The value to count.</param>
        internal static int ItemCount(XPathValue value)
        {
            switch (value.Kind)
            {
                case XPathValueKind.Sequence:
                {
                    XdmSequence sequence = value.AsSequence();

                    // A range holds one integer at every position and nothing nested, so its length
                    // is its count. Asking each of ten million positions what it holds is the whole
                    // of what a range exists to avoid.
                    if (sequence.IsRange)
                    {
                        return sequence.Count;
                    }

                    int count = 0;
                    for (int i = 0; i < sequence.Count; i++)
                    {
                        count += ItemCount(sequence[i]);
                    }

                    return count;
                }

                case XPathValueKind.NodeSet:
                    return value.AsNodeSet().Count;

                default:
                    return 1;
            }
        }

        /// <summary>Writes a value's items into an array from a position, returning the position after them.</summary>
        private static int FlattenInto(XPathValue value, XPathValue[] output, int at)
        {
            switch (value.Kind)
            {
                case XPathValueKind.Sequence:
                {
                    XdmSequence sequence = value.AsSequence();
                    for (int i = 0; i < sequence.Count; i++)
                    {
                        at = FlattenInto(sequence[i], output, at);
                    }

                    return at;
                }

                case XPathValueKind.NodeSet:
                {
                    NodeSet nodes = value.AsNodeSet();
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        output[at++] = XPathValue.FromNode(nodes.TreeAt(i), nodes[i]);
                    }

                    return at;
                }

                default:
                    output[at++] = value;
                    return at;
            }
        }

        private static void Flatten(XPathValue value, List<XPathValue> output)
        {
            switch (value.Kind)
            {
                case XPathValueKind.Sequence:
                {
                    XdmSequence sequence = value.AsSequence();

                    // Here is where a range costs what it looks like it costs, and where the limit
                    // therefore sits: this is the one place a sequence is expanded into items that are
                    // all held at once. A caller that only counted the range, or only asked whether a
                    // number was among it, never reaches this.
                    if (sequence.IsRange && sequence.Count > ExpandableRange)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XPDY0130,
                            $"A range of {sequence.Count} items is more than this engine will lay out "
                            + "one at a time.");
                    }

                    for (int i = 0; i < sequence.Count; i++)
                    {
                        Flatten(sequence[i], output);
                    }

                    return;
                }

                case XPathValueKind.NodeSet:
                {
                    NodeSet nodes = value.AsNodeSet();
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        output.Add(XPathValue.FromNode(nodes.TreeAt(i), nodes[i]));
                    }

                    return;
                }

                default:
                    output.Add(value);
                    return;
            }
        }

        /// <summary>
        /// Reduces items to the atomic values they stand for, which is what <c>fn:data</c> does.
        /// </summary>
        /// <remarks>
        /// A node contributes its typed value: its string-value as <c>xs:untypedAtomic</c> in a tree
        /// nothing validated, and what its annotation says in a validated one, which may be several values
        /// or none. An <b>array</b> contributes the atomization of its members, which is why the count
        /// can change: XPath 3.1 made an array atomizable where 3.0 refused it, so <c>sum([1, 2, 3])</c> is
        /// six and <c>sum([[1, 2], [3, 4]])</c> is ten. A map and a function have no typed value at all.
        /// </remarks>
        /// <param name="items">The items to atomize.</param>
        /// <returns>The atomic values, which may be more or fewer than the items given.</returns>
        /// <summary>
        /// The items a value contributes when it is written into a result tree, with arrays opened out.
        /// </summary>
        /// <remarks>
        /// A result tree has no way to hold an array, so one written into it contributes its members —
        /// recursively, since a member may be another array. Nodes stay nodes, which is what separates this
        /// from <see cref="Atomize"/>: <c>xsl:copy-of</c> given <c>[copy-of(a), copy-of(b)]</c> copies two
        /// elements, where atomizing would have written their text.
        /// </remarks>
        /// <param name="value">The value being written.</param>
        public static List<XPathValue> ContentItems(XPathValue value)
        {
            List<XPathValue> items = Items(value);

            // The common case is no array at all, and the list is already the right answer.
            bool nested = false;
            foreach (XPathValue item in items)
            {
                if (item.Kind == XPathValueKind.Array)
                {
                    nested = true;
                    break;
                }
            }

            if (!nested)
            {
                return items;
            }

            List<XPathValue> opened = new List<XPathValue>(items.Count);
            foreach (XPathValue item in items)
            {
                OpenInto(item, opened);
            }

            return opened;
        }

        private static void OpenInto(XPathValue item, List<XPathValue> into)
        {
            if (item.Kind != XPathValueKind.Array)
            {
                into.Add(item);
                return;
            }

            NestingGuard.Descend("write into a result tree");

            foreach (XPathValue member in item.AsArray().Members)
            {
                foreach (XPathValue single in Items(member))
                {
                    OpenInto(single, into);
                }
            }
        }

        public static List<XPathValue> Atomize(List<XPathValue> items)
        {
            List<XPathValue> atomized = new List<XPathValue>(items.Count);

            foreach (XPathValue item in items)
            {
                AtomizeInto(item, atomized);
            }

            return atomized;
        }

        /// <summary>
        /// The typed value of a node, which in a tree nothing validated is its string content.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An <c>xs:untypedAtomic</c> for an element, an attribute, a text node and a document node, and an
        /// <c>xs:string</c> for a comment, a processing instruction and a namespace node (XDM §5). The
        /// three that answer <c>xs:string</c> are the ones whose content was never a candidate for
        /// validation: there is nothing a schema could have said about the text of a comment, so the type
        /// it has is the type it always has, and <c>data()</c> of one is a string wherever it is read.
        /// </para>
        /// <para>
        /// In a validated tree an element or attribute annotated with a type gives what the type says its
        /// text is: one value, several for a list type, none for a nilled element or an empty complex
        /// type, and <c>FOTY0012</c> for a type that holds elements and no text. So the answer may be a
        /// sequence, which every caller that wants one value has to look for.
        /// </para>
        /// </remarks>
        /// <param name="node">The node to atomize.</param>
        internal static XPathValue TypedValueOf(XPathValue node)
        {
            if (node.Kind == XPathValueKind.Node)
            {
                return TypedValueOf(node.NodeTree, node.NodeId);
            }

            NodeSet set = node.AsNodeSet();
            return TypedValueOf(set.TreeAt(0), set[0]);
        }

        /// <summary>The typed value of a node of a tree; see <see cref="TypedValueOf(XPathValue)"/>.</summary>
        /// <param name="tree">The tree.</param>
        /// <param name="node">The node.</param>
        internal static XPathValue TypedValueOf(Model.XdmTree tree, int node)
        {
            Model.NodeKind kind = tree.KindOf(node);

            switch (kind)
            {
                case Model.NodeKind.Comment:
                case Model.NodeKind.ProcessingInstruction:
                case Model.NodeKind.Namespace:
                    return XPathValue.FromString(tree.StringValueOf(node));

                case Model.NodeKind.Element:
                case Model.NodeKind.Attribute:
                    if (tree.HasTypeAnnotations)
                    {
                        if (kind == Model.NodeKind.Element && tree.IsNilled(node))
                        {
                            return XPathValue.FromSequence(Empty);
                        }

                        if (tree.TypeAnnotationOf(node) is XdmSchemaType type)
                        {
                            return type.TypedValue(
                                tree.StringValueOf(node),
                                type.UsesQNames ? NamespacesAt(tree, node) : null);
                        }
                    }

                    break;
            }

            return XPathValue.FromUntypedAtomic(tree.StringValueOf(node));
        }

        /// <summary>
        /// The typed value of a node where one value is wanted: the value, or the empty sequence where the
        /// node's typed value is empty.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="what">What wants the value, for the message.</param>
        /// <exception cref="XsltException"><c>XPTY0004</c> where the typed value is several values.</exception>
        internal static XPathValue TypedValueAsOne(XPathValue node, string what)
        {
            XPathValue typed = TypedValueOf(node);

            if (typed.Kind != XPathValueKind.Sequence)
            {
                return typed;
            }

            XdmSequence several = typed.AsSequence();

            return several.Count switch
            {
                0 => typed,
                1 => several[0],
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"{what} takes one value, and the node's typed value is {several.Count} values: its "
                    + "type is a list type."),
            };
        }

        /// <summary>Adds the typed value of a node, one value or several, to a list of atomic values.</summary>
        /// <param name="tree">The tree.</param>
        /// <param name="node">The node.</param>
        /// <param name="into">Where the values go.</param>
        internal static void AtomizeNodeInto(Model.XdmTree tree, int node, List<XPathValue> into)
        {
            if (!tree.HasTypeAnnotations)
            {
                Model.NodeKind kind = tree.KindOf(node);
                string text = tree.StringValueOf(node);

                into.Add(kind is Model.NodeKind.Comment or Model.NodeKind.ProcessingInstruction or Model.NodeKind.Namespace
                    ? XPathValue.FromString(text)
                    : XPathValue.FromUntypedAtomic(text));
                return;
            }

            XPathValue typed = TypedValueOf(tree, node);

            if (typed.Kind == XPathValueKind.Sequence)
            {
                XdmSequence several = typed.AsSequence();
                for (int i = 0; i < several.Count; i++)
                {
                    into.Add(several[i]);
                }
            }
            else
            {
                into.Add(typed);
            }
        }

        /// <summary>The namespaces in scope at a node, for a typed value that is a name.</summary>
        private static IReadOnlyDictionary<string, string> NamespacesAt(Model.XdmTree tree, int node)
        {
            Dictionary<string, string> namespaces = new(StringComparer.Ordinal)
            {
                ["xml"] = Model.XdmTree.XmlNamespaceUri,
            };

            foreach ((string prefix, string uri) in tree.InScopeNamespacesOf(node))
            {
                namespaces[prefix] = uri;
            }

            return namespaces;
        }

        private static void AtomizeInto(XPathValue item, List<XPathValue> into)
        {
            switch (item.Kind)
            {
                case XPathValueKind.Node:
                    AtomizeNodeInto(item.NodeTree, item.NodeId, into);
                    return;

                case XPathValueKind.NodeSet:
                {
                    // Walked in place: listing the nodes first made a list per node-set, and a node-set
                    // arrives here once per item of an aggregate over a thousand of them.
                    NodeSet nodes = item.AsNodeSet();
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        AtomizeNodeInto(nodes.TreeAt(i), nodes[i], into);
                    }

                    return;
                }

                case XPathValueKind.Sequence:
                {
                    XdmSequence sequence = item.AsSequence();
                    for (int i = 0; i < sequence.Count; i++)
                    {
                        AtomizeInto(sequence[i], into);
                    }

                    return;
                }

                case XPathValueKind.Array:
                    NestingGuard.Descend("atomize");

                    foreach (XPathValue member in item.AsArray().Members)
                    {
                        AtomizeInto(member, into);
                    }

                    return;

                case XPathValueKind.Map:
                case XPathValueKind.Function:
                    throw XsltErrors.Error(
                        XsltErrorCode.FOTY0013,
                        item.Kind == XPathValueKind.Map
                            ? "A map has no typed value. Ask it for an entry instead."
                            : "A function has no typed value. Call it, and atomize its result instead.");

                default:
                    into.Add(item);
                    return;
            }
        }

        /// <summary>
        /// Expands a value into the items it holds, leaving nodes as nodes.
        /// </summary>
        /// <remarks>
        /// The counterpart of <see cref="Concatenate"/>, and what every function taking a sequence starts by
        /// doing. A node-set contributes its nodes in document order; a single value is one item.
        /// </remarks>
        /// <param name="value">The value to expand.</param>
        public static List<XPathValue> Items(XPathValue value)
        {
            // Sized to fit, so that a thousand nodes go into one array rather than a doubling series.
            List<XPathValue> items = new List<XPathValue>(ItemCount(value));
            Flatten(value, items);
            return items;
        }

        /// <summary>
        /// Returns the one item a value holds, refusing anything else.
        /// </summary>
        /// <remarks>
        /// The common case costs nothing: a value that is already a single item is not expanded into a list
        /// first, which matters because this sits in the argument path of every function-item call.
        /// </remarks>
        /// <param name="value">The value, which must hold exactly one item.</param>
        /// <param name="what">What the item is for, named in the message.</param>
        /// <exception cref="XsltException"><c>XPTY0004</c> where the value is not one item.</exception>
        public static XPathValue RequireSingleItem(XPathValue value, string what)
        {
            if (value.Kind is not (XPathValueKind.Sequence or XPathValueKind.NodeSet))
            {
                return value;
            }

            List<XPathValue> items = Items(value);

            return items.Count == 1
                ? items[0]
                : throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"Exactly one item is wanted as {what}, and this is {items.Count}.");
        }

        /// <summary>
        /// Returns the first item a value holds, or the empty sequence where it holds none.
        /// </summary>
        /// <remarks>
        /// XSLT 1.0 had nothing that could carry more than one thing at a time, so wherever an expression
        /// written for it now gives a sequence, backwards compatible behaviour takes the first item and
        /// discards the rest — which is what a 1.0 processor would have done with the node-set the same
        /// expression used to produce. This is that rule, and it is applied where the specification names it
        /// rather than everywhere a sequence might appear.
        /// <para>
        /// Nothing is expanded that does not have to be. A value that is already one item is returned as it
        /// stands, and a node-set gives up its first node without being materialised — this sits in the path
        /// of every <c>xsl:value-of</c> and every attribute value template of every 1.0 stylesheet, where a
        /// node-set is exactly what the ordinary case produces.
        /// </para>
        /// </remarks>
        /// <param name="value">The value to reduce.</param>
        public static XPathValue FirstItem(XPathValue value)
        {
            if (value.Kind == XPathValueKind.NodeSet)
            {
                NodeSet nodes = value.AsNodeSet();

                return nodes.Count == 0
                    ? XPathValue.FromSequence(Empty)
                    : XPathValue.FromNode(nodes.TreeAt(0), nodes[0]);
            }

            if (value.Kind != XPathValueKind.Sequence)
            {
                return value;
            }

            List<XPathValue> items = Items(value);
            return items.Count == 0 ? XPathValue.FromSequence(Empty) : items[0];
        }

        /// <summary>
        /// Returns the string-value of an item, whether it is a node or an atomic value.
        /// </summary>
        /// <remarks>
        /// The XPath 2.0 lexical form, sequences being a 2.0 idea and this the helper that serves them. It
        /// differs from XPath 1.0's only for <c>xs:double</c> and <c>xs:float</c>, which 1.0 writes as
        /// <c>Infinity</c> and in full where 2.0 writes <c>INF</c> and in exponential notation.
        /// </remarks>
        /// <param name="item">The item.</param>
        public static string StringValueOf(XPathValue item)
        {
            return item.Kind == XPathValueKind.Node
                ? item.NodeTree.StringValueOf(item.NodeId)
                : item.ToCanonicalString();
        }

        /// <summary>
        /// The string a sequence comes to as simple content: what an attribute value template, an
        /// <c>xsl:value-of</c> or an <c>xsl:attribute</c> with a <c>select</c> makes of it.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 §5.7.2. Zero-length text nodes are discarded and adjacent text nodes merged before
        /// anything is atomized, so a function returning the three text nodes <c>[</c>, <c>0</c> and
        /// <c>]</c> reads <c>[0]</c> and not <c>[ 0 ]</c>; then every item's string value, joined with the
        /// separator.
        /// </remarks>
        /// <param name="value">The sequence.</param>
        /// <param name="separator">What goes between two items' strings.</param>
        public static string SimpleContent(XPathValue value, string separator)
        {
            // One node, or none, is what xsl:value-of select="name" gives, once per element written, and
            // in a tree nothing validated its string value is the whole answer: there is nothing to
            // merge, atomize or join, and the three lists the general way builds for it are three lists
            // per element. A validated node's typed value may read differently, and may be several.
            switch (value.Kind)
            {
                case XPathValueKind.NodeSet:
                {
                    NodeSet nodes = value.AsNodeSet();
                    if (nodes.Count == 0)
                    {
                        return string.Empty;
                    }

                    if (nodes.Count == 1 && !nodes.TreeAt(0).HasTypeAnnotations)
                    {
                        return nodes.TreeAt(0).StringValueOf(nodes[0]);
                    }

                    break;
                }

                case XPathValueKind.Node:
                    if (!value.NodeTree.HasTypeAnnotations)
                    {
                        return value.NodeTree.StringValueOf(value.NodeId);
                    }

                    break;

                case XPathValueKind.String:
                case XPathValueKind.Number:
                case XPathValueKind.Boolean:
                    // One atomic value — what format-number() or concat() gives — is its own atomization.
                    return value.ToCanonicalString();
            }

            List<XPathValue> items = Atomize(MergeAdjacentText(Items(value)));

            if (items.Count == 0)
            {
                return string.Empty;
            }

            if (items.Count == 1)
            {
                return StringValueOf(items[0]);
            }

            string[] parts = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                parts[i] = StringValueOf(items[i]);
            }

            return string.Join(separator, parts);
        }

        /// <summary>
        /// The first two steps of making simple content: zero-length text nodes discarded, and each run of
        /// adjacent text nodes merged into one item holding their text.
        /// </summary>
        /// <remarks>
        /// The merged item is an untyped atomic value rather than a text node, there being no tree to put a
        /// node in and nothing after this step that could tell the difference.
        /// </remarks>
        /// <param name="items">The items.</param>
        public static List<XPathValue> MergeAdjacentText(List<XPathValue> items)
        {
            List<XPathValue>? merged = null;
            System.Text.StringBuilder? run = null;

            for (int i = 0; i < items.Count; i++)
            {
                XPathValue item = items[i];
                bool isText = item.Kind == XPathValueKind.Node
                    && item.NodeTree.KindOf(item.NodeId) == NodeKind.Text;

                if (!isText)
                {
                    if (merged is null)
                    {
                        continue;
                    }

                    if (run is not null)
                    {
                        merged.Add(XPathValue.FromUntypedAtomic(run.ToString()));
                        run = null;
                    }

                    merged.Add(item);
                    continue;
                }

                string text = item.NodeTree.StringValueOf(item.NodeId);

                // The first text node met copies what came before it, so a sequence with no text node in it
                // costs nothing here — which is most of them.
                merged ??= new List<XPathValue>(items.GetRange(0, i));

                if (text.Length == 0)
                {
                    continue;
                }

                (run ??= new System.Text.StringBuilder()).Append(text);
            }

            if (merged is null)
            {
                return items;
            }

            if (run is not null)
            {
                merged.Add(XPathValue.FromUntypedAtomic(run.ToString()));
            }

            return merged;
        }

        /// <summary>
        /// Builds the sequence of integers from one bound to another, which is what <c>1 to 5</c> denotes.
        /// </summary>
        /// <remarks>
        /// A descending range is empty rather than reversed, as the specification defines it: <c>5 to 1</c>
        /// yields nothing at all.
        /// </remarks>
        /// <param name="from">The lower bound, included.</param>
        /// <param name="to">The upper bound, included.</param>
        public static XPathValue Range(System.Numerics.BigInteger from, System.Numerics.BigInteger to)
        {
            if (to < from)
            {
                return XPathValue.FromSequence(Empty);
            }

            // Counted in a wide integer, because the bounds are: the count of 1 to 10^21 is no more a
            // long than its last item is, and subtracting them in one would wrap round to nonsense.
            System.Numerics.BigInteger length = to - from + 1;

            if (length > int.MaxValue)
            {
                // A sequence is indexed by an int here, so a range of more items than an int counts
                // has no position to ask about past the first two billion of them. That is a limit
                // this engine sets and not one the language does, which is what XPDY0130 is for.
                throw XsltErrors.Error(
                    XsltErrorCode.XPDY0130,
                    $"The range {from} to {to} has {length} items, which is more than this engine "
                    + "will count.");
            }

            // Held as its bounds. Nothing is built here however long it is: what it costs is decided
            // by what is asked of it, and counting it or looking for a number in it costs nothing.
            return length == 1
                ? XPathValue.FromInteger(from)
                : XPathValue.FromSequence(OfRange(from, (int)length));
        }
    }

    /// <summary>Items joined by commas, such as <c>(1, 2, 3)</c>.</summary>
    public sealed class SequenceExpr : Expr
    {
        private readonly Expr[] m_items;

        /// <summary>Initializes a sequence constructor.</summary>
        /// <param name="items">The expressions whose values are concatenated in order.</param>
        public SequenceExpr(Expr[] items)
        {
            m_items = items;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_items;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref Runtime.DynamicContext context)
        {
            XPathValue[] values = new XPathValue[m_items.Length];
            for (int i = 0; i < m_items.Length; i++)
            {
                values[i] = m_items[i].Evaluate(ref context);
            }

            return XdmSequence.Concatenate(values);
        }
    }

    /// <summary>
    /// A constant of a particular XPath 2.0 type, such as the <c>xs:integer</c> that <c>3</c> denotes.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="NumberLiteralExpr"/>, which is an XPath 1.0 number and always a double. Which
    /// of the two a numeric literal compiles to is decided by the version in force where it was written.
    /// </remarks>
    public sealed class TypedLiteralExpr : Expr
    {
        private readonly XPathValue m_value;

        /// <summary>Initializes a typed constant.</summary>
        /// <param name="value">The constant.</param>
        public TypedLiteralExpr(XPathValue value)
        {
            m_value = value;
        }

        /// <summary>Gets the constant.</summary>
        public XPathValue Value => m_value;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref Runtime.DynamicContext context) => m_value;
    }

    /// <summary>The empty sequence, written <c>()</c>.</summary>
    public sealed class EmptySequenceExpr : Expr
    {
        /// <inheritdoc/>
        public override XPathValue Evaluate(ref Runtime.DynamicContext context)
        {
            return XPathValue.FromSequence(XdmSequence.Empty);
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref Runtime.DynamicContext context) => false;
    }

    /// <summary>
    /// An XPath 2.0 value comparison — <c>eq</c>, <c>ne</c>, <c>lt</c>, <c>le</c>, <c>gt</c>, <c>ge</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The difference from <c>=</c> is quantification. <c>$a = 1</c> asks whether <em>any</em> item of
    /// <c>$a</c> equals 1, so it is true for <c>(0, 1)</c> and, awkwardly, <c>$a = 1</c> and <c>$a != 1</c>
    /// can both be true at once. <c>$a eq 1</c> asks whether <c>$a</c> <em>is</em> 1, and objects if it is
    /// anything other than a single item.
    /// </para>
    /// <para>
    /// An empty operand yields the empty sequence rather than false, which is why a value comparison cannot
    /// simply be folded into the general one.
    /// </para>
    /// </remarks>
    public sealed class ValueComparisonExpr : Expr
    {
        private readonly BinaryOperator m_operator;
        private readonly Expr m_left;
        private readonly Expr m_right;

        /// <summary>Initializes a value comparison.</summary>
        /// <param name="op">Which comparison to make.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        public ValueComparisonExpr(BinaryOperator op, Expr left, Expr right)
        {
            m_operator = op;
            m_left = left;
            m_right = right;
        }

        /// <inheritdoc/>
        internal override bool IsBooleanValued => true;

        /// <summary>What the comparison was written among: the collation and the namespaces.</summary>
        internal ComparisonContext? Comparing { get; init; }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_left, m_right };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref Runtime.DynamicContext context)
        {
            if (!TryAtomizeToOne(m_left.Evaluate(ref context), out XPathValue left)
                || !TryAtomizeToOne(m_right.Evaluate(ref context), out XPathValue right))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            return XPathValue.FromBoolean(XdmComparison.Value(left, right, m_operator, Comparing));
        }

        /// <summary>
        /// Compares two dates, times or durations, which order by the moment or the length they denote rather
        /// than by how they are written.
        /// </summary>
        /// <remarks>
        /// It matters that this is not a string comparison: <c>2020-01-01T12:00:00Z</c> and
        /// <c>2020-01-01T13:00:00+01:00</c> are the same instant and read quite differently. Nor is it a
        /// numeric one, since neither has a numeric value at all.
        /// </remarks>
        internal static bool TryCompareTemporal(XPathValue left, XPathValue right, out int sign)
        {
            sign = 0;

            // A QName, a binary value and a gregorian value are equal or not; none of them has an order, so
            // the answer here is only ever 0 or 1 and an ordering operator on one is refused by its caller.
            if (left.TypeCode == XdmTypeCode.QName && right.TypeCode == XdmTypeCode.QName)
            {
                sign = left.AsQName().Denotes(right.AsQName()) ? 0 : 1;
                return true;
            }

            if (left.TypeCode is XdmTypeCode.HexBinary or XdmTypeCode.Base64Binary
                && right.TypeCode is XdmTypeCode.HexBinary or XdmTypeCode.Base64Binary)
            {
                // Ordered by octets, which XPath 3.1 defines and 2.0 left undefined. The bytes decide and
                // not the text, so the two spellings of the same value order the same way.
                sign = left.AsBinary().CompareTo(right.AsBinary());
                return true;
            }

            if (left.TypeCode == XdmTypeCode.Gregorian && right.TypeCode == XdmTypeCode.Gregorian)
            {
                sign = left.AsGregorian().SameValue(right.AsGregorian()) ? 0 : 1;
                return true;
            }

            if (left.TypeCode is XdmTypeCode.Date or XdmTypeCode.Time or XdmTypeCode.DateTime
                && right.TypeCode == left.TypeCode)
            {
                sign = left.AsDateTime().CompareTo(right.AsDateTime());
                return true;
            }

            if (left.TypeCode is XdmTypeCode.Duration or XdmTypeCode.YearMonthDuration
                    or XdmTypeCode.DayTimeDuration
                && right.TypeCode is XdmTypeCode.Duration or XdmTypeCode.YearMonthDuration
                    or XdmTypeCode.DayTimeDuration)
            {
                sign = left.AsDuration().CompareTo(right.AsDuration());
                return true;
            }

            return false;
        }

        /// <summary>
        /// The typed value of one node for a value comparison: false where it is empty, which makes the
        /// comparison empty, and <c>XPTY0004</c> where it is several values.
        /// </summary>
        private static bool TryTypedValueToOne(Model.XdmTree tree, int node, out XPathValue single)
        {
            if (!tree.HasTypeAnnotations)
            {
                single = XPathValue.FromUntypedAtomic(tree.StringValueOf(node));
                return true;
            }

            XPathValue typed = XdmSequence.TypedValueOf(tree, node);

            if (typed.Kind != XPathValueKind.Sequence)
            {
                single = typed;
                return true;
            }

            XdmSequence several = typed.AsSequence();

            if (several.Count > 1)
            {
                throw XsltErrors.Error(XsltErrorCode.XPTY0004,
                    $"A value comparison needs one value on each side, and the node's typed value is {several.Count} values.");
            }

            single = several.Count == 1 ? several[0] : default;
            return several.Count == 1;
        }

        /// <summary>
        /// Reduces an operand to the single atomic value a value comparison needs.
        /// </summary>
        /// <returns><see langword="false"/> when the operand is empty, which makes the whole comparison empty.</returns>
        private static bool TryAtomizeToOne(XPathValue value, out XPathValue single)
        {
            switch (value.Kind)
            {
                case XPathValueKind.Sequence:
                {
                    XdmSequence sequence = value.AsSequence();
                    if (sequence.Count == 0)
                    {
                        single = default;
                        return false;
                    }

                    if (sequence.Count > 1)
                    {
                        throw XsltErrors.Error(XsltErrorCode.XPTY0004,
                            $"A value comparison needs one value on each side, but was given {sequence.Count}.");
                    }

                    return TryAtomizeToOne(sequence[0], out single);
                }

                case XPathValueKind.NodeSet:
                {
                    NodeSet nodes = value.AsNodeSet();
                    if (nodes.Count == 0)
                    {
                        single = default;
                        return false;
                    }

                    if (nodes.Count > 1)
                    {
                        throw XsltErrors.Error(XsltErrorCode.XPTY0004,
                            $"A value comparison needs one value on each side, but was given {nodes.Count}.");
                    }

                    return TryTypedValueToOne(nodes.TreeAt(0), nodes[0], out single);
                }

                case XPathValueKind.Node:
                    return TryTypedValueToOne(value.NodeTree, value.NodeId, out single);

                case XPathValueKind.Array:
                {
                    // An array atomizes to its members atomized, so [3] is the integer 3 and compares
                    // as one. An array holding several is several values, which a value comparison has
                    // no more room for than any other sequence of them.
                    List<XPathValue> members = XdmSequence.Atomize(new List<XPathValue> { value });

                    if (members.Count == 0)
                    {
                        single = default;
                        return false;
                    }

                    if (members.Count > 1)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XPTY0004,
                            $"A value comparison needs one value on each side, but was given {members.Count}.");
                    }

                    single = members[0];
                    return true;
                }

                case XPathValueKind.Map:
                case XPathValueKind.Function:
                    // A value comparison atomizes each operand before comparing them, and a map or a
                    // function item has no typed value to give: FOTY0013, and not the XPTY0004 of a pair
                    // that names no comparison. What is wrong is the operand, not the pairing.
                {
                        List<XPathValue> atomized = XdmSequence.Atomize(new List<XPathValue> { value });
                        single = atomized.Count == 1 ? atomized[0] : default;
                        return atomized.Count == 1;
                    }

                default:
                    single = value;
                    return true;
            }
        }
    }

    /// <summary>The range operator, <c>1 to 5</c>.</summary>
    public sealed class RangeExpr : Expr
    {
        private readonly Expr m_from;
        private readonly Expr m_to;
        private readonly XsltVersion m_version;

        /// <summary>Initializes a range.</summary>
        /// <param name="from">The lower bound.</param>
        /// <param name="to">The upper bound.</param>
        /// <param name="version">The version whose rules the bounds are read under.</param>
        public RangeExpr(Expr from, Expr to, XsltVersion version)
        {
            m_from = from;
            m_to = to;
            m_version = version;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_from, m_to };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref Runtime.DynamicContext context)
        {
            XPathValue from = m_from.Evaluate(ref context);
            XPathValue to = m_to.Evaluate(ref context);

            if (m_version.IsBackwardsCompatible)
            {
                // A 1.0 expression had no way to give more than one thing at a time, so where a 2.0 one now
                // does, the first item is the bound and the rest are discarded.
                from = XdmSequence.FirstItem(from);
                to = XdmSequence.FirstItem(to);
            }

            // An empty operand makes the whole range empty, rather than counting as zero.
            if (IsEmpty(from) || IsEmpty(to))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            return XdmSequence.Range(ToBound(from), ToBound(to));
        }

        private static bool IsEmpty(XPathValue value)
        {
            return value.Kind == XPathValueKind.Sequence && value.AsSequence().Count == 0;
        }

        /// <summary>
        /// Reads one end of a range, which must be an integer.
        /// </summary>
        /// <remarks>
        /// A range counts, so its ends are counting numbers: <c>1.1 to 3</c> names no sequence, because there
        /// is no next value after 1.1 that the notation picks out. Truncating to 1 would answer a question
        /// that was not asked. An untyped bound is read as an integer, as everywhere else.
        /// </remarks>
        private static System.Numerics.BigInteger ToBound(XPathValue value)
        {
            if (value.TypeCode == XdmTypeCode.Integer)
            {
                return value.ToBigInteger();
            }

            if (value.TypeCode is not (XdmTypeCode.UntypedAtomic or XdmTypeCode.None))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"A range runs between two integers, and '{value.ToStringValue()}' is not one.");
            }

            string text = value.ToStringValue().Trim();

            return System.Numerics.BigInteger.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out System.Numerics.BigInteger bound)
                    ? bound
                    : throw XsltErrors.Error(
                        XsltErrorCode.FORG0001, $"'{text}' is not a valid bound for a range.");
        }
    }
}
