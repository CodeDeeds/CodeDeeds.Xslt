using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>One <c>xsl:accumulator-rule</c>: which nodes it fires at, when, and what it makes of them.</summary>
    /// <param name="Patterns">The nodes this rule applies to.</param>
    /// <param name="AtEnd">Whether it fires when the node ends rather than when it starts.</param>
    /// <param name="Select">The new value, where given as an expression.</param>
    /// <param name="Body">The new value, where given as content.</param>
    /// <param name="ValueSlot">The frame slot <c>$value</c> is bound to while the rule runs.</param>
    internal sealed record AccumulatorRule(
        Pattern[] Patterns, bool AtEnd, Expr? Select, Instruction[]? Body, int ValueSlot);

    /// <summary>
    /// A compiled <c>xsl:accumulator</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An accumulator is a value that exists at <em>every node</em> of a document: it starts at an initial
    /// value and is rewritten by a rule each time the walk reaches a node the rule matches. What it buys is
    /// the running total, the section number, the "which chapter is this in" — every question whose answer
    /// depends on everything that came before, and which otherwise costs a backwards axis or a second pass.
    /// </para>
    /// <para>
    /// The value is computed for a whole document at once, on first use, and kept. Streaming is what
    /// accumulators were designed around and this engine does not stream, so the trade is the opposite one:
    /// a preorder sweep of the document, and two <see cref="XPathValue"/> per node for as long as the
    /// transformation lasts. That is affordable because the flat tree numbers nodes in preorder already —
    /// the sweep is a loop over an integer range, not a traversal.
    /// </para>
    /// </remarks>
    internal sealed class AccumulatorDefinition
    {
        /// <summary>Initializes an accumulator.</summary>
        /// <param name="name">Its expanded name.</param>
        /// <param name="index">Its position among the stylesheet's accumulators, which addresses its values.</param>
        public AccumulatorDefinition(ExpandedName name, int index)
        {
            Name = name;
            Index = index;
        }

        /// <summary>The accumulator's expanded name.</summary>
        public ExpandedName Name { get; }

        /// <summary>The index its computed values are held under at run time.</summary>
        public int Index { get; }

        /// <summary>The package that declared it; an accumulator is local to its package.</summary>
        public int Package { get; init; }

        /// <summary>The value before any rule has fired.</summary>
        public Expr? InitialValue { get; set; }

        /// <summary>The type every value it takes must fit.</summary>
        public XdmSequenceType? Type { get; set; }

        /// <summary>The rules, in the order they were declared.</summary>
        public AccumulatorRule[] Rules { get; set; } = Array.Empty<AccumulatorRule>();

        /// <summary>The number of frame slots a rule body needs.</summary>
        public int FrameSize { get; set; }
    }

    /// <summary>
    /// Which accumulators apply to a document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An accumulator is a value at every node of a document, but it is not a value at every node of every
    /// document. What applies to one is fixed when the document is made available, by the
    /// <c>use-accumulators</c> attribute of whatever made it available — the initial mode for the source
    /// document, and the <c>xsl:source-document</c> or <c>xsl:merge-source</c> that read any other — and
    /// saying nothing there means none. What <c>doc()</c> reads and what the stylesheet builds for itself
    /// are the exceptions, and carry every accumulator without being asked.
    /// </para>
    /// <para>
    /// The rule exists for streaming: an accumulator has to be computed during the one pass that reads the
    /// document, so a processor has to know before it starts which ones it will be asked for. This engine
    /// builds them on demand from a tree it is already holding and would happily build any of them, which is
    /// exactly why the restriction has to be <em>enforced</em> rather than merely obeyed — a stylesheet
    /// relying on an accumulator it never asked for would work here and fail where it mattered.
    /// </para>
    /// </remarks>
    internal sealed class AccumulatorSet : IEquatable<AccumulatorSet?>
    {
        /// <summary>The indices, ascending, or null for every accumulator there is.</summary>
        private readonly int[]? m_indices;

        private AccumulatorSet(int[]? indices)
        {
            m_indices = indices;
        }

        /// <summary>Every accumulator the stylesheet declares.</summary>
        public static AccumulatorSet All { get; } = new AccumulatorSet(null);

        /// <summary>No accumulator at all, which is what a <c>use-accumulators</c> left unwritten means.</summary>
        public static AccumulatorSet None { get; } = new AccumulatorSet(Array.Empty<int>());

        /// <summary>The set holding just these accumulators.</summary>
        /// <param name="indices">Their indices, in any order.</param>
        public static AccumulatorSet Of(IEnumerable<int> indices)
        {
            int[] sorted = new HashSet<int>(indices).ToArray();
            Array.Sort(sorted);

            return new AccumulatorSet(sorted);
        }

        /// <summary>Whether one accumulator applies.</summary>
        /// <param name="index">Its index among the stylesheet's accumulators.</param>
        public bool Contains(int index) => m_indices is null || Array.IndexOf(m_indices, index) >= 0;

        /// <inheritdoc/>
        public bool Equals(AccumulatorSet? other)
        {
            return other is not null
                && (m_indices is null
                    ? other.m_indices is null
                    : other.m_indices is not null && m_indices.AsSpan().SequenceEqual(other.m_indices));
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as AccumulatorSet);

        /// <inheritdoc/>
        public override int GetHashCode() => m_indices?.Length ?? -1;
    }

    /// <summary>
    /// The value of one accumulator at every node of one document.
    /// </summary>
    /// <remarks>
    /// Two values per node rather than one, because the two questions are different: <c>accumulator-before</c>
    /// asks what the value was when the node began and <c>accumulator-after</c> what it was when the node
    /// ended, and everything in between — the node's own start rule, and every descendant — happened between
    /// the two answers.
    /// </remarks>
    internal sealed class AccumulatorValues
    {
        private readonly XPathValue[] m_before;
        private readonly XPathValue[] m_after;

        /// <summary>Initializes the values for a document.</summary>
        public AccumulatorValues(int nodeCount)
        {
            m_before = new XPathValue[nodeCount];
            m_after = new XPathValue[nodeCount];
        }

        /// <summary>The value as the node began.</summary>
        public XPathValue Before(int node) => m_before[node];

        /// <summary>The value as the node ended.</summary>
        public XPathValue After(int node) => m_after[node];

        /// <summary>Records the value as a node began.</summary>
        public void SetBefore(int node, XPathValue value) => m_before[node] = value;

        /// <summary>Records the value as a node ended.</summary>
        public void SetAfter(int node, XPathValue value) => m_after[node] = value;
    }

    /// <summary>
    /// A call to <c>accumulator-before()</c> or <c>accumulator-after()</c>.
    /// </summary>
    /// <remarks>
    /// A name written out is resolved where the call is written, so a stylesheet that names an accumulator it
    /// has not declared hears about it at compile time rather than on whichever document first reaches the
    /// expression. A computed name can only be looked up once it is known, against the same declarations and
    /// with the prefixes that were in scope where the call was written.
    /// </remarks>
    internal sealed class AccumulatorExpr : Expr
    {
        private readonly int m_index;
        private readonly bool m_after;
        private readonly string m_name;
        private readonly Expr? m_computed;
        private readonly IReadOnlyDictionary<string, string>? m_namespaces;
        private readonly IReadOnlyList<AccumulatorDefinition>? m_accumulators;
        private readonly int m_package;

        /// <summary>Initializes a call.</summary>
        /// <param name="index">The accumulator's index.</param>
        /// <param name="after">Whether the call asks for the value after the node rather than before it.</param>
        /// <param name="name">The name as written, for messages.</param>
        public AccumulatorExpr(int index, bool after, string name)
        {
            m_index = index;
            m_after = after;
            m_name = name;
        }

        /// <summary>Initializes a call whose name is computed.</summary>
        /// <param name="computed">The expression the name comes from.</param>
        /// <param name="after">Whether the call asks for the value after the node rather than before it.</param>
        /// <param name="namespaces">The prefixes in scope where the call was written.</param>
        /// <param name="accumulators">The accumulators the stylesheet declares.</param>
        public AccumulatorExpr(
            Expr computed,
            bool after,
            IReadOnlyDictionary<string, string> namespaces,
            IReadOnlyList<AccumulatorDefinition> accumulators,
            int package)
        {
            m_package = package;
            m_index = -1;
            m_after = after;
            m_name = "…";
            m_computed = computed;
            m_namespaces = namespaces;
            m_accumulators = accumulators;
        }

        /// <summary>Finds the accumulator a computed name refers to.</summary>
        private int Resolve(ref DynamicContext context)
        {
            string written = XdmSequence.StringValueOf(XdmSequence.RequireSingleItem(
                m_computed!.Evaluate(ref context), "the name of an accumulator"));

            ExpandedName name;
            int close = written.StartsWith("Q{", StringComparison.Ordinal) ? written.IndexOf('}') : -1;

            if (close > 0)
            {
                // Written with its namespace in it, which needs no prefix to have been bound anywhere.
                name = new ExpandedName(written[2..close], written[(close + 1)..]);
            }
            else if (XdmQName.TrySplit(written, out string prefix, out string localName)
                && (prefix.Length == 0 || m_namespaces!.ContainsKey(prefix)))
            {
                name = new ExpandedName(prefix.Length == 0 ? string.Empty : m_namespaces![prefix], localName);
            }
            else
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE3340,
                    $"'{written}' does not name an accumulator: it is not a name, or its prefix is not bound "
                    + "where the call was written.");
            }

            foreach (AccumulatorDefinition accumulator in m_accumulators!)
            {
                if (accumulator.Name == name && accumulator.Package == m_package)
                {
                    return accumulator.Index;
                }
            }

            throw XsltErrors.Error(XsltErrorCode.XTDE3340, $"No xsl:accumulator is named '{written}'.");
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            int index = m_computed is null ? m_index : Resolve(ref context);

            if (context.Node < 0)
            {
                throw XsltErrors.Error(
                    context.HasContextItem ? XsltErrorCode.XTTE3360 : XsltErrorCode.XPDY0002,
                    $"accumulator-{(m_after ? "after" : "before")}('{m_name}') is about the node being "
                    + "processed, and here there is "
                    + (context.HasContextItem ? "an atomic value rather than a node." : "no context item."));
            }

            XdmTree tree = context.Tree;

            // An attribute is not in the preorder sequence, so it has no place of its own in the walk, and
            // the specification makes asking there a type error rather than answering for the element.
            if (XdmTree.IsAttribute(context.Node))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE3360,
                    $"accumulator-{(m_after ? "after" : "before")}('{m_name}') is about the node being "
                    + "processed, and an attribute has no place of its own in the walk an accumulator makes.");
            }

            int node = context.Node;

            // A node copied with its accumulator values answers with the values of the node it was copied
            // from, in the document it was copied from — which is also the document whose applicable
            // accumulators decide whether there is an answer at all. Followed as far as it goes, a copy of a
            // copy being a copy.
            while (tree.CopiedFrom is { } copied && copied.TryGetValue(node, out (XdmTree Tree, int Node) origin))
            {
                (tree, node) = origin;
            }

            if (!context.Runtime!.AppliesTo(index, tree))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE3362,
                    $"The accumulator '{m_name}' does not apply to this document. Which accumulators apply "
                    + "is fixed when a document is made available, by the use-accumulators attribute of "
                    + "whatever made it available, and this one was not among them.");
            }

            AccumulatorValues values = context.Runtime!.AccumulatorValuesFor(index, tree);

            return m_after ? values.After(node) : values.Before(node);
        }
    }
}
