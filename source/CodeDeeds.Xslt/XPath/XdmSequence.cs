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
    public sealed class XdmSequence
    {
        private readonly XPathValue[] m_items;

        /// <summary>Initializes a sequence over items the caller no longer owns.</summary>
        /// <param name="items">The items, in order.</param>
        public XdmSequence(XPathValue[] items)
        {
            m_items = items;
        }

        /// <summary>The sequence of no items, which is what <c>()</c> denotes.</summary>
        public static XdmSequence Empty { get; } = new XdmSequence(Array.Empty<XPathValue>());

        /// <summary>Gets the number of items.</summary>
        public int Count => m_items.Length;

        /// <summary>Gets the item at a position.</summary>
        /// <param name="index">A zero-based index below <see cref="Count"/>.</param>
        public XPathValue this[int index] => m_items[index];

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
        private static int ItemCount(XPathValue value)
        {
            switch (value.Kind)
            {
                case XPathValueKind.Sequence:
                {
                    XdmSequence sequence = value.AsSequence();
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
        /// A node contributes its string-value as <c>xs:untypedAtomic</c>, this engine carrying no type
        /// annotations. An <b>array</b> contributes the atomization of its members, which is why the count
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
        /// An <c>xs:untypedAtomic</c> for an element, an attribute, a text node and a document node, and an
        /// <c>xs:string</c> for a comment, a processing instruction and a namespace node (XDM §5). The
        /// three that answer <c>xs:string</c> are the ones whose content was never a candidate for
        /// validation: there is nothing a schema could have said about the text of a comment, so the type
        /// it has is the type it always has, and <c>data()</c> of one is a string wherever it is read.
        /// </remarks>
        /// <param name="node">The node to atomize.</param>
        internal static XPathValue TypedValueOf(XPathValue node)
        {
            string text = StringValueOf(node);

            Model.NodeKind kind = node.Kind == XPathValueKind.Node
                ? node.NodeTree.KindOf(node.NodeId)
                : node.AsNodeSet().Tree.KindOf(node.AsNodeSet()[0]);

            return kind is Model.NodeKind.Comment or Model.NodeKind.ProcessingInstruction
                or Model.NodeKind.Namespace
                ? XPathValue.FromString(text)
                : XPathValue.FromUntypedAtomic(text);
        }

        private static void AtomizeInto(XPathValue item, List<XPathValue> into)
        {
            switch (item.Kind)
            {
                case XPathValueKind.Node:
                    into.Add(TypedValueOf(item));
                    return;

                case XPathValueKind.NodeSet:
                {
                    // Walked in place: listing the nodes first made a list per node-set, and a node-set
                    // arrives here once per item of an aggregate over a thousand of them.
                    NodeSet nodes = item.AsNodeSet();
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        into.Add(TypedValueOf(XPathValue.FromNode(nodes.TreeAt(i), nodes[i])));
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
            // its string value is the whole answer: there is nothing to merge, atomize or join, and the
            // three lists the general way builds for it are three lists per element.
            switch (value.Kind)
            {
                case XPathValueKind.NodeSet:
                {
                    NodeSet nodes = value.AsNodeSet();
                    if (nodes.Count == 0)
                    {
                        return string.Empty;
                    }

                    if (nodes.Count == 1)
                    {
                        return nodes.TreeAt(0).StringValueOf(nodes[0]);
                    }

                    break;
                }

                case XPathValueKind.Node:
                    return value.NodeTree.StringValueOf(value.NodeId);

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
        public static XPathValue Range(long from, long to)
        {
            if (to < from)
            {
                return XPathValue.FromSequence(Empty);
            }

            long length = to - from + 1;
            if (length > 1_000_000)
            {
                // A limit this engine sets and not one the language does, which is what XPDY0130 is for: a
                // range is built as an array of items here, so a caller asking for three billion of them is
                // asking for something this processor will not do rather than something wrong. The code is
                // what lets a stylesheet tell those apart, and what lets the suite accept either answer.
                throw XsltErrors.Error(
                    XsltErrorCode.XPDY0130,
                    $"The range {from} to {to} has {length} items, which is more than this engine will build.");
            }

            XPathValue[] items = new XPathValue[length];
            for (long i = 0; i < length; i++)
            {
                items[i] = XPathValue.FromInteger(from + i);
            }

            return length == 1 ? items[0] : XPathValue.FromSequence(new XdmSequence(items));
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

    /// <summary>
    /// An integer literal beyond what this engine's <c>xs:integer</c> holds.
    /// </summary>
    /// <remarks>
    /// XPath's <c>xs:integer</c> is unbounded and this engine's is a 64-bit one, which the specification
    /// permits so long as going past the limit is reported as an overflow. Overflow is a <em>dynamic</em>
    /// error, so it is raised where the literal is evaluated rather than where it is read — a branch that
    /// never runs never raises it, and a stylesheet that only mentions such a number still compiles.
    /// </remarks>
    public sealed class OverflowingLiteralExpr : Expr
    {
        private readonly string m_written;

        /// <summary>Initializes a literal that cannot be held.</summary>
        /// <param name="written">The literal as it was written, named in the message.</param>
        public OverflowingLiteralExpr(string written)
        {
            m_written = written;
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref Runtime.DynamicContext context)
        {
            throw XsltErrors.Error(
                XsltErrorCode.FOAR0002,
                $"{m_written} is beyond the range of xs:integer this engine holds, which is that of a "
                + "64-bit signed integer.");
        }
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

                    single = XPathValue.FromUntypedAtomic(nodes.TreeAt(0).StringValueOf(nodes[0]));
                    return true;
                }

                case XPathValueKind.Node:
                    single = XPathValue.FromUntypedAtomic(XdmSequence.StringValueOf(value));
                    return true;

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
        private static long ToBound(XPathValue value)
        {
            if (value.TypeCode == XdmTypeCode.Integer)
            {
                return value.ToInteger();
            }

            if (value.TypeCode is not (XdmTypeCode.UntypedAtomic or XdmTypeCode.None))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"A range runs between two integers, and '{value.ToStringValue()}' is not one.");
            }

            string text = value.ToStringValue().Trim();

            return long.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long bound)
                    ? bound
                    : throw XsltErrors.Error(
                        XsltErrorCode.FORG0001, $"'{text}' is not a valid bound for a range.");
        }
    }
}
