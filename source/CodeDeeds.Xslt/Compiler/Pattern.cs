using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// Where the step written to the left of a pattern step has to hold, relative to what this one matched.
    /// </summary>
    /// <remarks>
    /// One number that folds together two things written separately: the connector before the step
    /// (<c>/</c> or <c>//</c>) and the step's own axis. <c>a/descendant::b</c> and <c>a//b</c> say the same
    /// thing, and saying it either way should reach the same matcher rather than a second one.
    /// </remarks>
    internal enum PatternReach : byte
    {
        /// <summary>The immediate parent, which is what <c>/</c> before a <c>child::</c> step means.</summary>
        Parent = 0,

        /// <summary>Any ancestor: written <c>//</c>, or as the <c>descendant::</c> axis.</summary>
        AnyAncestor = 1,

        /// <summary>The very same node, which is what a <c>self::</c> step means.</summary>
        Self = 2,

        /// <summary>The node or any ancestor of it: <c>descendant-or-self::</c>, or <c>//self::</c>.</summary>
        AnyAncestorOrSelf = 3,
    }

    /// <summary>One step of a match pattern, together with where the step to its left has to hold.</summary>
    internal sealed class PatternStep
    {
        /// <summary>Initializes a step.</summary>
        public PatternStep(
            Axis axis, NodeTest test, Expr[] predicates, PatternReach reach, bool explicitAxis = false)
        {
            Axis = axis;
            Test = test;
            Predicates = predicates;
            Reach = reach;
            ExplicitAxis = explicitAxis;
            CountsPosition = Pattern.AnyMayBePositional(predicates, 0);
            RecountsBetweenPredicates = predicates.Length > 1 && Pattern.AnyMayBePositional(predicates, 1);
            AskedForBoolean = Pattern.NeverNumbers(predicates);

            // Asked once here, each being a walk of the predicates that was made for every candidate.
            bool[] filters = new bool[predicates.Length];
            bool filtersReadOnlyTheTree = true;
            bool allReadOnlyTheTree = true;

            for (int i = 0; i < predicates.Length; i++)
            {
                bool readsOnlyTheTree = Pattern.ReadsOnlyTheTree(predicates[i]);
                filters[i] = i + 1 < predicates.Length && Pattern.AnyMayBePositional(predicates, i + 1);
                filtersReadOnlyTheTree &= !filters[i] || readsOnlyTheTree;
                allReadOnlyTheTree &= readsOnlyTheTree;
            }

            FiltersBeforeCounting = filters;
            RemembersSurvivors = RecountsBetweenPredicates
                && filtersReadOnlyTheTree
                && axis is not (Axis.Descendant or Axis.DescendantOrSelf);
            MatchesNothingElseMeanwhile = allReadOnlyTheTree;
        }

        /// <summary>
        /// For each predicate, whether what it leaves has to be enumerated before the next is asked,
        /// some later predicate being one that could select by position.
        /// </summary>
        public bool[] FiltersBeforeCounting { get; }

        /// <summary>
        /// Whether what the predicates leave, of everything the step selects from an anchor, can be
        /// worked out once for the anchor and kept: see <see cref="StepSurvivors"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// True where the step recounts between predicates and every predicate it filters by reads
        /// nothing but the tree (<see cref="Pattern.ReadsOnlyTheTree"/>), so that which nodes it keeps
        /// is the same whichever candidate is asking. <c>item[@type='a'][2]</c> is such a step. A
        /// predicate that reads <c>current()</c> or a variable is not, and that step is counted afresh
        /// for every candidate as it always was.
        /// </para>
        /// <para>
        /// Not on the two descendant axes, where the anchor changes as the search climbs and one
        /// candidate asks at every ancestor: what one anchor's predicates left would be replaced by the
        /// next anchor's before the next candidate came for it.
        /// </para>
        /// </remarks>
        public bool RemembersSurvivors { get; }

        /// <summary>
        /// Whether no predicate of this step can cause another pattern to be matched while this one is,
        /// every one of them reading only the tree.
        /// </summary>
        /// <remarks>
        /// A predicate may call a function that applies templates, and a template's pattern may be this
        /// very step under another anchor. Where that cannot happen, what is remembered for the step is
        /// written over in place; where it can, it is replaced by a new one, so that a match under way
        /// keeps reading the lists it began with.
        /// </remarks>
        public bool MatchesNothingElseMeanwhile { get; }

        /// <summary>
        /// For each predicate, whether it is asked for a boolean outright, its value being known never to
        /// be the number that would select by position.
        /// </summary>
        /// <remarks>
        /// A predicate is evaluated for every candidate the step is tried against, and its value is looked
        /// at for one thing: whether it is a number. Where it cannot be, the value need not be made.
        /// <c>product[@type]</c> built a node-set for each product only to be asked whether it was empty,
        /// where the path asked for a boolean answers from the list its steps filled. Settled here, once,
        /// so that a candidate pays for reading a flag and not for asking the expression what it is. Such
        /// a predicate may still <em>read</em> the position, as <c>position() &gt; 1</c> does, which is
        /// <see cref="CountsPosition"/>'s business and not this one's.
        /// </remarks>
        public bool[] AskedForBoolean { get; }

        /// <summary>
        /// Whether a predicate after the first could select by position, so that it counts among what the
        /// predicates before it left rather than among everything the step selects.
        /// </summary>
        /// <remarks>
        /// <c>foo[@a='c'][2]</c> is the second <c>foo</c> with the attribute. What the earlier predicates
        /// left is found by evaluating them against every node the step selects, and what they answer may
        /// depend on more than the node: on <c>current()</c>, and in an <c>xsl:number</c> or an
        /// <c>xsl:for-each-group</c> on local variables that differ from one call to the next. Where it
        /// may, that list is worked out afresh for each candidate, as it always was. Where the earlier
        /// predicates read nothing but the tree it is worked out once for the anchor and kept: see
        /// <see cref="RemembersSurvivors"/>. A step that never needs such a list has only its selection
        /// remembered; see <see cref="StepSelection"/>.
        /// </remarks>
        public bool RecountsBetweenPredicates { get; }

        /// <summary>
        /// Whether any predicate of this step could select by position, so that matching has to know where
        /// the candidate stands among the nodes the step selects.
        /// </summary>
        /// <remarks>
        /// Settled once, here, because it is asked every time the step is tried against a node, and
        /// because of what the answer saves. Finding a candidate's position means enumerating everything
        /// the step selects from its anchor — its siblings, on the child axis — and doing that for each
        /// candidate in turn is the square of the list: <c>item[@type='a']</c> over sixteen thousand
        /// siblings took two thirds of a second where the same test in an <c>xsl:choose</c> took five
        /// milliseconds. A predicate that is a comparison or a path can read no position, which is most
        /// predicates anyone writes, and for those nothing need be counted at all.
        /// </remarks>
        public bool CountsPosition { get; }

        /// <summary>
        /// Whether the axis was written out, <c>child::</c> rather than nothing.
        /// </summary>
        /// <remarks>
        /// The one place it matters: a bare <c>document-node()</c> stands on the self axis, so that a
        /// document node matches it, and <c>child::document-node()</c> stands on the child axis it named,
        /// which no document node is on. Everything else about the two is the same.
        /// </remarks>
        public bool ExplicitAxis { get; }

        /// <summary>The axis this step selects on, which decides what an unqualified name test admits.</summary>
        public Axis Axis { get; }

        /// <summary>The test the candidate node must satisfy.</summary>
        public NodeTest Test { get; }

        /// <summary>The predicates that must hold for the candidate.</summary>
        public Expr[] Predicates { get; }

        /// <summary>Where the step written to the left of this one has to hold.</summary>
        public PatternReach Reach { get; }
    }

    /// <summary>
    /// The nodes a pattern step last selected from an anchor, kept so that the next candidate under the
    /// same anchor can be found among them without selecting them again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A positional predicate needs the candidate's place among the nodes its step selects, and the
    /// candidates arrive one after another from the same parent: <c>xsl:apply-templates</c> hands over an
    /// element's children in order, and <c>item[1]</c> is asked of each. Selecting the siblings for every
    /// one of them is the square of the list. Selected once and kept, they are selected once per parent,
    /// and the candidate is found by a binary search, the selection being in document order.
    /// </para>
    /// <para>
    /// Only what depends on nothing but the tree is kept: the nodes an axis and a node test select from
    /// an anchor. No predicate has been evaluated to arrive at them, so nothing a predicate can read —
    /// <c>current()</c>, a variable — can make them stale. One selection is kept per step and the newest
    /// replaces it, which is all that candidates arriving in document order need; candidates that
    /// alternate between two parents fill it each time, and cost what they did before there was one.
    /// </para>
    /// <para>
    /// One belongs to one transformation, which holds it: see <c>XsltRuntime.SelectionOf</c>.
    /// </para>
    /// </remarks>
    internal sealed class StepSelection
    {
        private readonly List<int> m_nodes = new List<int>();
        private XdmTree? m_tree;
        private int m_anchor;
        private bool m_ascending;

        // The ids a descendant step was last selected between, first and last inclusive; first is past
        // last where what is held was not selected on a descendant axis.
        private int m_first = 1;
        private int m_last;

        /// <summary>How many nodes were selected, which is the context size a predicate sees.</summary>
        public int Count => m_nodes.Count;

        /// <summary>Whether what is held was selected from this anchor in this tree.</summary>
        public bool IsFrom(XdmTree tree, int anchor)
        {
            return ReferenceEquals(m_tree, tree) && m_anchor == anchor;
        }

        /// <summary>
        /// Whether what is held includes everything a descendant step selects between two ids of this
        /// tree, so that the selection from an anchor inside the one held can be read out of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A node's descendants are the ids after it up to the end of its subtree, and a step on a
        /// descendant axis selects those of them that pass its test. So what it selects from an anchor
        /// is a run of what it selects from any ancestor of that anchor, and where the run begins and
        /// ends is two searches.
        /// </para>
        /// <para>
        /// That is what keeps <c>list/descendant::item[2]</c> from being the square of the list. A
        /// candidate is asked about at every ancestor in turn, a pattern being matched wherever some
        /// anchor selects the node, and one selection kept for the last anchor asked was replaced at
        /// every ancestor of every candidate: the selection from the <c>list</c>, then from each
        /// element above it, then from the document, and the same again for the next
        /// <c>item</c>. Kept from the highest anchor asked, it answers for all of them.
        /// </para>
        /// </remarks>
        /// <param name="tree">The tree the anchor is in.</param>
        /// <param name="first">The first id the anchor's selection may hold.</param>
        /// <param name="last">The last.</param>
        public bool Spans(XdmTree tree, int first, int last)
        {
            return ReferenceEquals(m_tree, tree) && m_ascending && m_first <= m_last
                && m_first <= first && last <= m_last;
        }

        /// <summary>
        /// The one-based position of a node among those held that lie between two ids, and how many
        /// of them there are; zero where the node is not one of them.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="first">The first id of the run.</param>
        /// <param name="last">The last.</param>
        /// <param name="size">How many of the nodes held lie in the run.</param>
        public int PositionWithin(int node, int first, int last, out int size)
        {
            int from = LowerBound(first);
            int to = LowerBound(last + 1);
            size = to - from;

            int index = m_nodes.BinarySearch(from, size, node, null);
            return index < 0 ? 0 : index - from + 1;
        }

        /// <summary>The index of the first node held whose id is not below a given one.</summary>
        private int LowerBound(int id)
        {
            int low = 0;
            int high = m_nodes.Count;

            while (low < high)
            {
                int middle = (int)(((uint)low + (uint)high) >> 1);

                if (m_nodes[middle] < id)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        /// <summary>Selects afresh, replacing whatever was held.</summary>
        public void Fill(XdmTree tree, int anchor, PatternStep step, int[] fingerprintMap)
        {
            // Held by nothing while it is being filled, so that a walk that fails leaves no selection
            // behind claiming to be one.
            m_tree = null;
            m_nodes.Clear();

            AxisWalker.Collect(tree, anchor, step.Axis, step.Test, fingerprintMap, m_nodes);

            // Every axis a pattern may use is a forward one, and a forward axis is walked in document
            // order, which is ascending order of node id. Checked rather than taken on trust, since a
            // binary search over anything else answers "not there" about a node that is.
            bool ascending = true;

            for (int i = 1; i < m_nodes.Count; i++)
            {
                if (m_nodes[i] <= m_nodes[i - 1])
                {
                    ascending = false;
                    break;
                }
            }

            // What a descendant axis walked, which is what Spans answers from: the anchor's subtree,
            // the anchor itself left out or not as the axis leaves it.
            if (step.Axis is Axis.Descendant or Axis.DescendantOrSelf)
            {
                m_first = step.Axis == Axis.Descendant ? anchor + 1 : anchor;
                m_last = tree.SubtreeEndOf(anchor);
            }
            else
            {
                m_first = 1;
                m_last = 0;
            }

            m_ascending = ascending;
            m_anchor = anchor;
            m_tree = tree;
        }

        /// <summary>The one-based position of a node among those selected, or zero where it is not one.</summary>
        public int PositionOf(int node)
        {
            int index = m_ascending ? m_nodes.BinarySearch(node) : m_nodes.IndexOf(node);
            return index < 0 ? 0 : index + 1;
        }
    }

    /// <summary>
    /// What a pattern step selected from an anchor, and what each of its predicates left of that, kept
    /// so that the next candidate under the same anchor is looked up and nothing is evaluated again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a step that counts among what an earlier predicate left: <c>item[@type='a'][2]</c> is the
    /// second of the items that have the type, so matching one item means knowing which of its siblings
    /// have it. Evaluating the predicate against every sibling for every candidate is the square of the
    /// list, and with a larger constant than selecting them was: two thousand items took 170
    /// milliseconds and eight thousand 2.7 seconds.
    /// </para>
    /// <para>
    /// A <see cref="StepSelection"/> keeps only what depends on nothing but the tree, no predicate
    /// having been evaluated to arrive at it. This keeps what predicates left, and so is used only for
    /// a step whose filtering predicates themselves read nothing but the tree:
    /// <see cref="PatternStep.RemembersSurvivors"/>. What a predicate left is worked out the first time
    /// a candidate gets that far, and not before, so a predicate is evaluated against the siblings only
    /// where it always was, after the candidate has passed it itself.
    /// </para>
    /// <para>
    /// One belongs to one transformation, which holds it: see <c>XsltRuntime.SurvivorsOf</c>.
    /// </para>
    /// </remarks>
    internal sealed class StepSurvivors
    {
        private readonly List<int> m_selected = new List<int>();
        private readonly List<int>?[] m_left;
        private readonly bool[] m_known;
        private XdmTree? m_tree;
        private int m_anchor;
        private bool m_ascending;

        /// <summary>Initializes an empty one for a step.</summary>
        /// <param name="step">The step, for how many predicates it has.</param>
        public StepSurvivors(PatternStep step)
        {
            m_left = new List<int>?[step.Predicates.Length];
            m_known = new bool[step.Predicates.Length];
        }

        /// <summary>Everything the step selects from the anchor, before any predicate.</summary>
        public List<int> Selected => m_selected;

        /// <summary>Whether what is held was selected from this anchor in this tree.</summary>
        public bool IsFrom(XdmTree tree, int anchor)
        {
            return ReferenceEquals(m_tree, tree) && m_anchor == anchor;
        }

        /// <summary>Selects afresh from an anchor, forgetting what the predicates left of the last.</summary>
        public void Fill(XdmTree tree, int anchor, PatternStep step, int[] fingerprintMap)
        {
            // Held by nothing while it is being filled, as a StepSelection is.
            m_tree = null;
            m_selected.Clear();
            Array.Clear(m_known);

            AxisWalker.Collect(tree, anchor, step.Axis, step.Test, fingerprintMap, m_selected);

            bool ascending = true;

            for (int i = 1; i < m_selected.Count; i++)
            {
                if (m_selected[i] <= m_selected[i - 1])
                {
                    ascending = false;
                    break;
                }
            }

            m_ascending = ascending;
            m_anchor = anchor;
            m_tree = tree;
        }

        /// <summary>What a predicate left, or null where no candidate has yet got as far as asking.</summary>
        /// <param name="predicate">Which predicate, counted from zero.</param>
        public List<int>? LeftBy(int predicate)
        {
            return m_known[predicate] ? m_left[predicate] : null;
        }

        /// <summary>A list to work out what a predicate leaves into, empty.</summary>
        /// <param name="predicate">Which predicate.</param>
        public List<int> ListFor(int predicate)
        {
            List<int> list = m_left[predicate] ??= new List<int>();
            list.Clear();
            return list;
        }

        /// <summary>Marks what a predicate left as worked out, the list for it having been filled.</summary>
        /// <param name="predicate">Which predicate.</param>
        public void Keep(int predicate)
        {
            m_known[predicate] = true;
        }

        /// <summary>The one-based position of a node in one of the lists held, or zero where it is not there.</summary>
        /// <param name="nodes">The list: the selection, or what a predicate left of it.</param>
        /// <param name="node">The node.</param>
        public int PositionIn(List<int> nodes, int node)
        {
            // What a predicate leaves is in the order of what it was given, so one answer does for all.
            int index = m_ascending ? nodes.BinarySearch(node) : nodes.IndexOf(node);
            return index < 0 ? 0 : index + 1;
        }
    }

    /// <summary>
    /// A compiled <c>match</c> pattern.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Patterns are matched <em>bottom-up</em>, which is what separates them from ordinary location paths.
    /// Asking "does <c>chapter/title</c> match this node?" does not mean evaluating the path and searching the
    /// result; it means testing whether the node is a <c>title</c> and then whether its parent is a
    /// <c>chapter</c>. Steps are therefore stored innermost-first, and matching walks towards the root.
    /// </para>
    /// <para>
    /// A <c>//</c> connector makes the step before it match any ancestor rather than the immediate parent, so
    /// matching backtracks over the ancestor chain at that point.
    /// </para>
    /// </remarks>
    internal sealed class Pattern
    {
        private readonly PatternStep[] m_steps;
        private readonly bool m_anchoredAtRoot;
        private readonly bool m_rootAnchorAllowsAnyDepth;

        /// <summary>The key the outermost step hangs from, where the pattern is anchored on one.</summary>
        private readonly Expr? m_keyAnchor;

        /// <summary>The pattern read as an ordinary expression, for the shapes that cannot be walked upwards.</summary>
        private readonly Expr? m_selection;

        /// <summary>
        /// Whether only a node that is a child of something can match — an attribute and a document node
        /// being children of nothing.
        /// </summary>
        /// <remarks>
        /// A test that pins a kind settles this for itself, so what this is really about is <c>node()</c>,
        /// which pins none. XSLT says it matches every node except an attribute and the document node, and
        /// that is what leaves the document node to the built-in rule rather than to a stylesheet's
        /// catch-all — the difference between a result and no result at all.
        /// </remarks>
        private readonly bool m_childrenOnly;

        /// <summary>Whether only an attribute can match, the innermost step being on that axis.</summary>
        private readonly bool m_attributesOnly;

        private Pattern(
            PatternStep[] steps,
            bool anchoredAtRoot,
            bool rootAnchorAllowsAnyDepth,
            double priority,
            Expr? keyAnchor = null)
        {
            m_steps = steps;
            m_anchoredAtRoot = anchoredAtRoot;
            m_rootAnchorAllowsAnyDepth = rootAnchorAllowsAnyDepth;
            m_keyAnchor = keyAnchor;
            Priority = priority;

            m_childrenOnly = steps.Length != 0
                && steps[0].Axis is Axis.Child or Axis.Descendant
                && DetermineRequiredKind(steps[0]) is null;
            m_attributesOnly = steps.Length != 0 && steps[0].Axis == Axis.Attribute;
        }

        /// <summary>Gets the default priority derived from the pattern's shape.</summary>
        public double Priority { get; }

        /// <summary>
        /// Gets the name slot the innermost step tests for, or -1 when that step is not a name test. Patterns
        /// with a name slot can be indexed so that dispatch never considers them for other names.
        /// </summary>
        public int NameSlot { get; private init; } = -1;

        /// <summary>
        /// The name slot of the outermost step, where that step names an element on an axis whose
        /// principal node kind is element, and -1 otherwise.
        /// </summary>
        /// <remarks>
        /// What <c>xsl:mode typed="strict"</c> holds a template rule to: the first step of the pattern as
        /// it was written names an element the schemas must declare (<c>XTSE3105</c>). The steps are kept
        /// innermost-first, so the first step written is the last of them.
        /// </remarks>
        public int OutermostElementNameSlot
        {
            get
            {
                if (m_steps.Length == 0)
                {
                    return -1;
                }

                PatternStep outermost = m_steps[^1];

                return outermost.Axis is Axis.Child or Axis.Descendant or Axis.DescendantOrSelf or Axis.Self
                    && outermost.Test is NameNodeTest named
                        ? named.Slot
                        : -1;
            }
        }

        /// <summary>
        /// Gets the node kind the innermost step requires, or <see langword="null"/> if it matches any kind.
        /// </summary>
        public NodeKind? RequiredKind { get; private init; }

        /// <summary>Returns whether any of a set of patterns matches a node.</summary>
        /// <param name="patterns">The patterns, as parsed from one attribute.</param>
        /// <param name="node">The node to test.</param>
        /// <param name="context">The context, positioned on the node.</param>
        public static bool MatchesAny(Pattern[] patterns, int node, ref DynamicContext context)
        {
            foreach (Pattern pattern in patterns)
            {
                if (pattern.Matches(node, ref context))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether a node matches this pattern.
        /// </summary>
        /// <param name="node">The candidate node.</param>
        /// <param name="context">The context used to evaluate any predicates.</param>
        public bool Matches(int node, ref DynamicContext context)
        {
            // Counted while the pattern is matched: a pattern is not output, temporary or otherwise, so
            // current-output-uri() in one is the empty sequence, wherever the pattern is written.
            XsltRuntime? runtime = context.Runtime;

            if (runtime is not null)
            {
                runtime.PatternDepth++;
            }

            try
            {
                return MatchesUnguarded(node, ref context);
            }
            catch (XsltException failed) when (!IsReported(failed))
            {
                return false;
            }
            finally
            {
                if (runtime is not null)
                {
                    runtime.PatternDepth--;
                }
            }
        }

        /// <summary>
        /// Whether an error raised while a pattern is being matched is one the transformation must hear.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 §5.5.4: a dynamic error or a type error in evaluating a pattern against an item means
        /// the item does not match, and nothing more — a stylesheet cannot predict which predicates of which
        /// patterns will be evaluated against a node, so it cannot be held to what happens when one is. The
        /// specification excepts a circularity, which it requires reported wherever it is found; a message
        /// that terminates is excepted here too, being the stylesheet's own decision to stop, and so is a
        /// key the pattern names that nothing declares — a mistake in the stylesheet rather than in the
        /// data, which the specification lets a processor report statically and this one reports the first
        /// time the pattern is asked.
        /// </remarks>
        private static bool IsReported(XsltException failed)
        {
            // And a refusal to go on for want of stack, which is about the pattern and not about the item:
            // taken for no match, it would be a rule quietly not matching on one stack and matching on
            // another, and the stylesheet the wiser only by what it did not produce.
            return failed.Code is "XTDE0640" or "XTDE1260" or "XTMM9000" or "XTMM9001" or "XPDY0130";
        }

        private bool MatchesUnguarded(int node, ref DynamicContext context)
        {
            // A node is an item too, so a predicate pattern is asked about it in the ordinary way — which is
            // what lets one rule cover a node and an atomic value together.
            if (ItemPredicates is not null)
            {
                return MatchesItemUnguarded(XPathValue.FromNode(context.Tree, node), ref context);
            }

            if (m_selection is not null)
            {
                return Selects(node, ref context);
            }

            if (m_steps.Length == 0)
            {
                return KeyHolds(node, ref context);
            }

            PatternStep innermost = m_steps[0];

            // An attribute is on no child axis, whatever the test after it admits: child::attribute() is a
            // pattern that matches nothing. And the document node is on no child axis written out — a bare
            // document-node() stands on the self axis by the specification's adjustment, child::document-node()
            // on the axis it named.
            if (innermost.Axis is Axis.Child or Axis.Descendant or Axis.DescendantOrSelf
                && XdmTree.IsAttribute(node))
            {
                return false;
            }

            if (innermost.ExplicitAxis
                && innermost.Axis == Axis.Child
                && context.Tree.KindOf(node) == NodeKind.Root)
            {
                return false;
            }

            // The innermost step names the axis the matched node is reached along, and the node matches only
            // if it is reachable along that axis from somewhere.
            if (m_childrenOnly && (XdmTree.IsAttribute(node) || context.Tree.ParentOf(node) < 0))
            {
                return false;
            }

            if (m_attributesOnly && !XdmTree.IsAttribute(node))
            {
                return false;
            }

            return MatchStep(node, 0, ref context);
        }

        /// <summary>
        /// Whether a node is one of those the anchoring key selects.
        /// </summary>
        /// <remarks>
        /// The key is looked up from the root of the tree the candidate is in, not from wherever the
        /// transformation happens to be standing: a pattern says which nodes it describes, and that cannot
        /// depend on which node is being processed when the question is asked.
        /// </remarks>
        private bool KeyHolds(int node, ref DynamicContext context)
        {
            if (node < 0)
            {
                return false;
            }

            DynamicContext lookup = context;
            lookup.Node = XdmTree.RootNode;
            lookup.AtomicItem = default;
            lookup.Position = 1;
            lookup.Size = 1;

            List<int> selected = NodeListPool.Rent();

            try
            {
                m_keyAnchor!.EvaluateNodes(ref lookup, selected);
                return selected.Contains(node);
            }
            finally
            {
                NodeListPool.Return(selected);
            }
        }

        /// <summary>
        /// Whether a node satisfies one step and everything written to the left of it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The loop is over <em>anchor</em> nodes: the node the step hangs from. XSLT defines a match
        /// existentially — a node matches a pattern where there is some node from which the pattern selects
        /// it — and the anchor is that node. For <c>child::</c> there is exactly one candidate, the parent,
        /// which is why the question usually has no search in it at all.
        /// </para>
        /// <para>
        /// The predicates are asked inside the loop and not before it, because what a positional predicate
        /// counts within is the sequence the anchor selects. <c>chapter/descendant::foo[2]</c> asks whether
        /// the node is the second <c>foo</c> under <em>the chapter</em>, and the chapter is not reached
        /// until an anchor is chosen. Asking first, against the parent, answered a different question.
        /// </para>
        /// <para>
        /// A step with one anchor is followed by moving to it, in the loop here, so a pattern of ordinary
        /// steps costs no stack however long it is. A step that climbs is a search, one anchor tried
        /// after another until the rest of the pattern holds at one, and the rest is asked recursively;
        /// a pattern of ten thousand such steps over a document that deep would ask ten thousand levels
        /// of it, so the stack is asked first and the pattern refused rather than the process ended. A
        /// pattern of ten thousand ordinary steps did the same before the loop, in <c>MatchStep</c>
        /// calling <c>AnchorHolds</c> calling <c>MatchStep</c>.
        /// </para>
        /// </remarks>
        /// <param name="node">The candidate for this step.</param>
        /// <param name="stepIndex">Which step, counting inwards from the matched node.</param>
        /// <param name="context">The context used to evaluate any predicates.</param>
        private bool MatchStep(int node, int stepIndex, ref DynamicContext context)
        {
            while (true)
            {
                if (node < 0)
                {
                    return false;
                }

                PatternStep step = m_steps[stepIndex];
                NodeKind principal = step.Axis.PrincipalNodeKind();

                if (!step.Test.Matches(context.Tree, node, principal, context.FingerprintMap))
                {
                    return false;
                }

                bool last = stepIndex + 1 == m_steps.Length;

                // Where the step to the left of this one has to hold. child:: and attribute:: look at the
                // parent and self:: does not move at all; '//' and descendant:: widen either of those from
                // one candidate to every ancestor of it.
                int start = step.Reach is PatternReach.Self or PatternReach.AnyAncestorOrSelf
                    ? node
                    : context.Tree.ParentOf(node);

                bool wide = step.Reach is PatternReach.AnyAncestor or PatternReach.AnyAncestorOrSelf
                    || (last && m_rootAnchorAllowsAnyDepth);

                // Whether the sequence a predicate counts within changes as the search climbs. A '//'
                // written before an ordinary step is a step of its own — a//b is
                // a/descendant-or-self::node()/child::b — so what a predicate on that step counts within
                // is still the parent's children, and only what is written to the left of it climbs.
                // Written as descendant:: there is no step in between, and then the sequence really is
                // whatever the anchor selects.
                bool varies = step.Axis is Axis.Descendant or Axis.DescendantOrSelf;

                if (!varies && !PredicatesHold(node, step, start, ref context))
                {
                    return false;
                }

                if (wide)
                {
                    return MatchClimbing(node, stepIndex, start, ref context);
                }

                // One anchor, and the rest of the pattern has to hold there: the next step, asked by
                // going round again, or what stands beyond the outermost step.
                if (last)
                {
                    return Anchored(start, wide: false, ref context);
                }

                node = start;
                stepIndex++;
            }
        }

        /// <summary>
        /// Whether a step that climbs is satisfied at some ancestor, and the rest of the pattern with it.
        /// </summary>
        /// <remarks>
        /// The search, kept out of <see cref="MatchStep"/> so that the loop there stays the small thing
        /// nearly every pattern needs and nothing else. The recursion is here, and only a pattern that
        /// climbs pays for it. The step's test has been passed and its anchor-independent predicates
        /// asked by the time this is reached.
        /// </remarks>
        /// <param name="node">The candidate for the step.</param>
        /// <param name="stepIndex">Which step.</param>
        /// <param name="start">The first anchor to try, from which the search climbs.</param>
        /// <param name="context">The context used to evaluate any predicates.</param>
        private bool MatchClimbing(int node, int stepIndex, int start, ref DynamicContext context)
        {
            PatternStep step = m_steps[stepIndex];
            bool last = stepIndex + 1 == m_steps.Length;
            bool varies = step.Axis is Axis.Descendant or Axis.DescendantOrSelf;

            // The search is a recursion, and a recursion that runs short of stack goes on upon another
            // (FreshStack) rather than being refused: what ends it is the pattern, which is finite.
            if (!last && !System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                return MatchClimbingOnFreshStack(node, stepIndex, start, ref context);
            }

            for (int anchor = start; ; anchor = context.Tree.ParentOf(anchor))
            {
                // A step on a descendant axis hangs from a node, and above the topmost node there is
                // none. A pattern is adjusted so that its first step can match a node with no parent
                // only where that step is on the child, attribute or namespace axis (XSLT 3.0, 5.5.3),
                // which is what the anchor of nothing stands for elsewhere: the one sequence a
                // parentless node is in, itself. Taken for an anchor here it made every x the first
                // and the last of the one x there is, once the search had climbed past the document,
                // so that descendant::x[1] matched every x in it; and it let an x with no parent
                // match descendant::x, which selects only what is beneath something.
                if (varies && anchor < 0)
                {
                    return false;
                }

                if ((!varies || PredicatesHold(node, step, anchor, ref context))
                    && (last
                        ? Anchored(anchor, wide: true, ref context)
                        : MatchStep(anchor, stepIndex + 1, ref context)))
                {
                    return true;
                }

                if (anchor < 0)
                {
                    return false;
                }
            }
        }

        /// <summary>Goes on with the search upon a new stack, the one in use being nearly spent.</summary>
        /// <remarks>
        /// Where no new stack can be had the pattern is refused, with the code <see cref="IsReported"/>
        /// lets through, so that <see cref="Matches"/> does not take the refusal for a node the pattern
        /// does not match: taken so, it would be a rule quietly not matching on one machine and matching
        /// on another.
        /// </remarks>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private bool MatchClimbingOnFreshStack(int node, int stepIndex, int start, ref DynamicContext context)
        {
            ClimbOnFreshStack search = new ClimbOnFreshStack(this, node, stepIndex, start, context.Hold());

            try
            {
                search.RunToCompletion("no further stack could be had to continue on");
            }
            catch (XsltException failed) when (failed.InnerException is PlatformNotSupportedException or OutOfMemoryException)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPDY0130,
                    "The pattern has more steps that climb ('//' or descendant::) than there is stack to "
                    + "match, and " + failed.Message + ": each is a search that asks the rest of the "
                    + "pattern of every ancestor, a level of stack apiece.");
            }

            return search.Result;
        }

        /// <summary>The search for a climbing step's anchor, waiting to go on upon a new stack.</summary>
        private sealed class ClimbOnFreshStack : FreshStack
        {
            private readonly Pattern m_pattern;
            private readonly int m_node;
            private readonly int m_stepIndex;
            private readonly int m_start;
            private readonly DynamicContext.Held m_context;

            public ClimbOnFreshStack(Pattern pattern, int node, int stepIndex, int start, DynamicContext.Held context)
            {
                m_pattern = pattern;
                m_node = node;
                m_stepIndex = stepIndex;
                m_start = start;
                m_context = context;
            }

            public bool Result { get; private set; }

            /// <inheritdoc/>
            protected override void Run()
            {
                DynamicContext context = m_context.Restore();
                Result = m_pattern.MatchClimbing(m_node, m_stepIndex, m_start, ref context);
            }
        }

        /// <summary>Whether what is beyond the outermost step holds at a chosen anchor.</summary>
        /// <param name="anchor">The node the outermost step hangs from, which may be none.</param>
        /// <param name="wide">Whether the anchor was reached by climbing rather than by one move.</param>
        /// <param name="context">The context used to evaluate a key.</param>
        private bool Anchored(int anchor, bool wide, ref DynamicContext context)
        {
            // The outermost step, so what is above it is the pattern's anchor rather than another step: a
            // key, the root, or nothing at all.
            if (m_keyAnchor is not null)
            {
                return KeyHolds(anchor, ref context);
            }

            if (!m_anchoredAtRoot)
            {
                return true;
            }

            // '/step' requires the matched node to be a child of the root, and '//step' that the root is
            // somewhere above it — a document node, that is. A tree of parentless nodes has no such root
            // for a pattern beginning at one to hang from, which is what keeps '//a' from matching an
            // element a variable's as declaration built.
            return wide
                ? anchor >= 0 && context.Tree.KindOf(anchor) == NodeKind.Root
                : anchor == XdmTree.RootNode;
        }

        /// <summary>
        /// Evaluates a step's predicates against the candidate, counted within what the anchor selects.
        /// </summary>
        /// <remarks>
        /// A positional predicate in a pattern counts the candidate's position among the nodes the step
        /// selects from its anchor, so those have to be enumerated to establish the context position and
        /// size. On the child axis that is the candidate's siblings, which is the reading everyone knows;
        /// on <c>descendant::</c> it is every descendant of the anchor, which is why the anchor is a
        /// parameter rather than the parent read off the node.
        /// </remarks>
        /// <param name="node">The candidate.</param>
        /// <param name="step">The step it satisfied.</param>
        /// <param name="anchor">The node the step hangs from, or -1 where there is nothing above it.</param>
        /// <param name="context">The context to evaluate the predicates in.</param>
        private static bool PredicatesHold(int node, PatternStep step, int anchor, ref DynamicContext context)
        {
            if (step.Predicates.Length == 0)
            {
                return true;
            }

            if (!step.CountsPosition)
            {
                return HoldWithoutPosition(node, step, ref context);
            }

            if (!step.RecountsBetweenPredicates && anchor >= 0 && context.Runtime is XsltRuntime runtime)
            {
                return HoldAtRememberedPosition(node, step, anchor, runtime, ref context);
            }

            if (step.RemembersSurvivors && anchor >= 0 && context.Runtime is XsltRuntime remembering)
            {
                return HoldAmongRememberedSurvivors(node, step, anchor, remembering, ref context);
            }

            List<int> selected = NodeListPool.Rent();

            try
            {
                if (anchor < 0)
                {
                    // Nothing above the node to have selected it, so the sequence it belongs to is itself.
                    // A parentless element is still the first of the one node there is.
                    selected.Add(node);
                }
                else
                {
                    AxisWalker.Collect(
                        context.Tree, anchor, step.Axis, step.Test, context.FingerprintMap, selected);
                }

                // Each predicate filters what the one before it left, and a positional predicate counts
                // within that: foo[@a='c'][2] is the second foo among those with the attribute, not the
                // second foo that happens to have it. So the survivors are enumerated between predicates —
                // but only where a later predicate could read a position, which most cannot.
                List<int> current = selected;
                int position = current.IndexOf(node) + 1;

                for (int i = 0; i < step.Predicates.Length; i++)
                {
                    if (position == 0)
                    {
                        return false;
                    }

                    DynamicContext inner = context;
                    inner.Node = node;
                    inner.Position = position;
                    inner.Size = current.Count;

                    if (!PredicateHolds(step.Predicates[i], step.AskedForBoolean[i], ref inner))
                    {
                        return false;
                    }

                    if (!step.FiltersBeforeCounting[i])
                    {
                        continue;
                    }

                    DynamicContext over = context;
                    current = PredicateFilter.Apply(step.Predicates[i], current, ref over);
                    position = current.IndexOf(node) + 1;
                }

                return true;
            }
            finally
            {
                NodeListPool.Return(selected);
            }
        }

        /// <summary>
        /// Evaluates a step's predicates where a later one counts among what an earlier one left, reading
        /// what each left from what the transformation remembers for the step.
        /// </summary>
        /// <remarks>
        /// The same questions in the same order as the walk in <see cref="PredicatesHold"/> that it
        /// stands in for: the candidate is asked each predicate itself, at its place among what the one
        /// before left, and what a predicate left is only looked for once the candidate has passed it.
        /// What differs is that the answer is kept for the anchor. A predicate that raises an error
        /// against some sibling raises it for every candidate that gets that far, as it did, nothing
        /// being kept of a list that was not finished.
        /// </remarks>
        private static bool HoldAmongRememberedSurvivors(
            int node, PatternStep step, int anchor, XsltRuntime runtime, ref DynamicContext context)
        {
            StepSurvivors survivors = runtime.SurvivorsOf(step);

            if (!survivors.IsFrom(context.Tree, anchor))
            {
                // Written over where it stands unless a predicate of this step could have another
                // match of it under way, which would then be reading the lists this empties.
                if (!step.MatchesNothingElseMeanwhile)
                {
                    survivors = runtime.NewSurvivorsOf(step);
                }

                survivors.Fill(context.Tree, anchor, step, context.FingerprintMap);
            }

            List<int> current = survivors.Selected;
            int position = survivors.PositionIn(current, node);

            for (int i = 0; i < step.Predicates.Length; i++)
            {
                if (position == 0)
                {
                    return false;
                }

                DynamicContext inner = context;
                inner.Node = node;
                inner.Position = position;
                inner.Size = current.Count;

                if (!PredicateHolds(step.Predicates[i], step.AskedForBoolean[i], ref inner))
                {
                    return false;
                }

                if (!step.FiltersBeforeCounting[i])
                {
                    continue;
                }

                List<int>? left = survivors.LeftBy(i);

                if (left is null)
                {
                    left = survivors.ListFor(i);
                    DynamicContext over = context;
                    PredicateFilter.Apply(step.Predicates[i], current, left, ref over);
                    survivors.Keep(i);
                }

                current = left;
                position = survivors.PositionIn(current, node);
            }

            return true;
        }

        /// <summary>
        /// Evaluates a step's predicates where none of them can read a position, without finding one.
        /// </summary>
        /// <remarks>
        /// The candidate has already passed the step's node test and the anchor is where the step hangs
        /// from, so it is among the nodes the step selects; what is left unknown is only which of them it
        /// is, and these predicates cannot ask. They are given a position and size of one, which is what a
        /// node with nothing above it has always been given, and each is evaluated in a context of its own
        /// as it is when positions are counted.
        /// </remarks>
        private static bool HoldWithoutPosition(int node, PatternStep step, ref DynamicContext context)
        {
            for (int i = 0; i < step.Predicates.Length; i++)
            {
                DynamicContext inner = context;
                inner.Node = node;
                inner.Position = 1;
                inner.Size = 1;

                if (!PredicateHolds(step.Predicates[i], step.AskedForBoolean[i], ref inner))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Evaluates a step's predicates at the candidate's position among what the step selects, reading
        /// that from the selection the transformation remembers for the step.
        /// </summary>
        /// <remarks>
        /// For a step whose first predicate alone may be positional, so that every predicate sees the one
        /// position and size. The two are read before any predicate is evaluated and the selection is not
        /// touched again: a predicate may call a function that applies templates, which may match this
        /// very step under another anchor and leave the selection holding that one instead.
        /// </remarks>
        private static bool HoldAtRememberedPosition(
            int node, PatternStep step, int anchor, XsltRuntime runtime, ref DynamicContext context)
        {
            StepSelection selection = runtime.SelectionOf(step);
            int position;
            int size;

            if (step.Axis is Axis.Descendant or Axis.DescendantOrSelf)
            {
                // The anchor changes as the search climbs, and what it selects is a run of what any
                // anchor above it selects: read out of the selection held where that spans it, so that
                // the one kept from the highest anchor asked answers for every anchor under it.
                int first = step.Axis == Axis.Descendant ? anchor + 1 : anchor;
                int last = context.Tree.SubtreeEndOf(anchor);

                if (!selection.Spans(context.Tree, first, last) && !selection.IsFrom(context.Tree, anchor))
                {
                    selection.Fill(context.Tree, anchor, step, context.FingerprintMap);
                }

                if (selection.Spans(context.Tree, first, last))
                {
                    position = selection.PositionWithin(node, first, last, out size);
                }
                else
                {
                    position = selection.PositionOf(node);
                    size = selection.Count;
                }
            }
            else
            {
                if (!selection.IsFrom(context.Tree, anchor))
                {
                    selection.Fill(context.Tree, anchor, step, context.FingerprintMap);
                }

                position = selection.PositionOf(node);
                size = selection.Count;
            }

            if (position == 0)
            {
                return false;
            }

            for (int i = 0; i < step.Predicates.Length; i++)
            {
                DynamicContext inner = context;
                inner.Node = node;
                inner.Position = position;
                inner.Size = size;

                if (!PredicateHolds(step.Predicates[i], step.AskedForBoolean[i], ref inner))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether a predicate's answer for a node depends on nothing but the tree the node is in and
        /// the node's place in the sequence it is asked among.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What decides whether the nodes a predicate keeps can be worked out once and kept: see
        /// <see cref="PatternStep.RemembersSurvivors"/>. A predicate that reads <c>current()</c>, a
        /// variable, the current group or anything else of the transformation may keep different nodes
        /// from one candidate to the next, and is counted afresh each time.
        /// </para>
        /// <para>
        /// A list of what is known to be so, and anything not on it is a no: literals and constants, the context
        /// item, the root, paths and their own predicates, unions, arithmetic, comparisons and the
        /// logical operators, and the functions XPath 1.0 has, none of which reads anything but its
        /// arguments, the context node and its position. That is most predicates anyone writes, and a
        /// no costs what the step cost before.
        /// </para>
        /// </remarks>
        /// <param name="predicate">The predicate, or any expression inside one.</param>
        internal static bool ReadsOnlyTheTree(Expr predicate)
        {
            NestingGuard.DescendExpression();
            Expr expression = predicate.Unwrapped;

            switch (expression)
            {
                case StringLiteralExpr or NumberLiteralExpr or BooleanLiteralExpr or TypedLiteralExpr
                    or ConstantExpr or EmptySequenceExpr or ContextItemExpr or RootExpr:
                    return true;

                case PathExpr path:
                    foreach (AxisStep step in path.Steps)
                    {
                        foreach (Expr inner in step.Predicates)
                        {
                            if (!ReadsOnlyTheTree(inner))
                            {
                                return false;
                            }
                        }
                    }

                    break;

                case BinaryExpr or ValueComparisonExpr or UnionExpr or NegateExpr or FunctionCallExpr:
                    break;

                default:
                    return false;
            }

            foreach (Expr child in expression.Children)
            {
                if (!ReadsOnlyTheTree(child))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether any predicate from the one given onwards could select by position.</summary>
        internal static bool AnyMayBePositional(Expr[] predicates, int from)
        {
            for (int i = from; i < predicates.Length; i++)
            {
                if (MayBePositional(predicates[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a predicate could depend on the position of the candidate: by being a number, or by
        /// reading <c>position()</c> or <c>last()</c> anywhere in it.
        /// </summary>
        /// <remarks>
        /// Conservative on purpose. A shape known to yield a boolean and to read no position — a
        /// comparison, a logical operator, <c>not()</c> — is the common case and is settled as no; anything
        /// less certain is a yes, which costs one enumeration of the siblings and never a wrong answer.
        /// <para>
        /// A path is settled as no as well, and a union of them: <c>[@type]</c> and <c>[child]</c> answer
        /// with nodes, and nodes are never the number that would make them positional. A path ending in
        /// something that is not a node is a different expression and is not among these.
        /// </para>
        /// <para>
        /// What reads the focus is asked of <see cref="Expr.DependsOnFocusPosition"/>, which knows that
        /// <c>position#0</c> and <c>function-lookup()</c> read it as <c>position()</c> does. Asking only
        /// whether a call to <c>position()</c> or <c>last()</c> was written missed those two.
        /// </para>
        /// </remarks>
        private static bool MayBePositional(Expr predicate)
        {
            return Expr.DependsOnFocusPosition(predicate.Unwrapped) || MayBeANumber(predicate);
        }

        /// <summary>
        /// Whether a predicate's value could be the number that selects by position, which is the one
        /// thing its value is looked at for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Read from the shape, and only from shapes that cannot answer otherwise whatever the version. It
        /// is deliberately not <see cref="Expr.ReturnsNodeSet"/>, which a path's own predicates go by,
        /// and which has twice been a promise made on the strength of the version that a 1.0 stylesheet
        /// on this processor could break: by a filter expression and by <c>current()</c>, until each
        /// stopped making it. <c>x[(2, 5)[1]]</c> is the second <c>x</c>, the filter's value being the
        /// number 2, and a pattern taking the promise would have asked that 2 for a boolean and matched
        /// every <c>x</c>. Neither shape is here, so both are evaluated and looked at, as anything
        /// unrecognised is, and a list that names what it knows cannot be broken from elsewhere.
        /// </para>
        /// <para>
        /// A pattern's predicates are never compiled: a pattern is parsed by <see cref="PatternParser"/>
        /// and does not pass through the expression compiler, so on both backends what stands here is the
        /// interpreted node and says for itself what it is. The shape is read through
        /// <see cref="Expr.Unwrapped"/> all the same, so that a wrapper would cost nothing but the emitted
        /// code it wraps: asked for a boolean, it hands the question to the node it was compiled from.
        /// </para>
        /// </remarks>
        private static bool MayBeANumber(Expr predicate)
        {
            // As it was written: the compiled backend may hand over a wrapper, whose shape says nothing.
            return predicate.Unwrapped switch
            {
                BinaryExpr binary => !binary.IsBooleanValued,
                FunctionCallExpr call => !call.IsBooleanValued,
                ValueComparisonExpr or NodeComparisonExpr or InstanceOfExpr or QuantifiedExpr
                    or BooleanLiteralExpr => false,
                PathExpr or UnionExpr => false,
                _ => true,
            };
        }

        /// <summary>
        /// For each predicate, whether it can be asked for a boolean outright: see
        /// <see cref="PatternStep.AskedForBoolean"/>.
        /// </summary>
        internal static bool[] NeverNumbers(Expr[] predicates)
        {
            if (predicates.Length == 0)
            {
                return Array.Empty<bool>();
            }

            bool[] never = new bool[predicates.Length];

            for (int i = 0; i < predicates.Length; i++)
            {
                never[i] = !MayBeANumber(predicates[i]);
            }

            return never;
        }

        /// <summary>
        /// Evaluates one predicate against a context already positioned on the candidate, and says whether
        /// it keeps that candidate.
        /// </summary>
        /// <remarks>
        /// A predicate that could be a number goes through the rule every predicate follows, which
        /// evaluates it and selects by position where a number is what came back: <c>x[$n]</c> with
        /// <c>$n</c> of 2 is the second <c>x</c>. One that could not is asked for its effective boolean
        /// value and nothing else, which is what that rule would have made of its value, errors included —
        /// every expression that answers the question for itself is held to its value by
        /// <c>BooleanRouteTests</c>.
        /// </remarks>
        /// <param name="predicate">The predicate.</param>
        /// <param name="askedForBoolean">What was settled for it when the pattern was built.</param>
        /// <param name="context">The context, positioned on the candidate.</param>
        private static bool PredicateHolds(Expr predicate, bool askedForBoolean, ref DynamicContext context)
        {
            return askedForBoolean
                ? predicate.EvaluateAsBoolean(ref context)
                : PredicateFilter.Holds(predicate, ref context);
        }

        /// <summary>
        /// Parses a <c>match</c> attribute into its alternatives. A pattern separated by <c>|</c> behaves as
        /// several independent patterns that happen to share a template.
        /// </summary>
        /// <param name="pattern">The pattern text.</param>
        /// <param name="context">The static context supplying namespace bindings.</param>
        /// <returns>One compiled pattern per alternative.</returns>
        /// <exception cref="XsltException">The pattern is not valid.</exception>
        public static Pattern[] Parse(string pattern, IXPathStaticContext context)
        {
            return PatternParser.Parse(pattern, context);
        }

        /// <summary>
        /// Creates a pattern anchored on a key rather than on the root.
        /// </summary>
        /// <remarks>
        /// XSLT gives every id or key pattern the default priority 0.5, whatever its shape: it says which
        /// nodes it means by naming them, which is as specific as a pattern gets.
        /// </remarks>
        /// <param name="stepsOutermostFirst">The steps below the key, which may be none.</param>
        /// <param name="keyAnchor">The key or id call the outermost step hangs from.</param>
        /// <param name="allowsAnyDepth">Whether the key was joined to the steps by <c>//</c>.</param>
        internal static Pattern CreateKeyAnchored(
            List<PatternStep> stepsOutermostFirst,
            Expr keyAnchor,
            bool allowsAnyDepth)
        {
            List<PatternStep> reversed = new List<PatternStep>(stepsOutermostFirst);
            reversed.Reverse();
            PatternStep[] steps = reversed.ToArray();

            return new Pattern(steps, false, allowsAnyDepth, 0.5, keyAnchor)
            {
                NameSlot = steps.Length != 0 && steps[0].Axis != Axis.Namespace && steps[0].Test is NameNodeTest named ? named.Slot : -1,
                RequiredKind = steps.Length != 0 ? DetermineRequiredKind(steps[0]) : null,
            };
        }

        internal static Pattern Create(
            List<PatternStep> stepsOutermostFirst,
            bool anchoredAtRoot,
            bool rootAnchorAllowsAnyDepth)
        {
            List<PatternStep> reversed = new List<PatternStep>(stepsOutermostFirst);
            reversed.Reverse();
            PatternStep[] steps = reversed.ToArray();

            PatternStep innermost = steps[0];
            double priority = ComputePriority(steps, anchoredAtRoot, innermost);

            return new Pattern(steps, anchoredAtRoot, rootAnchorAllowsAnyDepth, priority)
            {
                NameSlot = innermost.Axis != Axis.Namespace && innermost.Test is NameNodeTest named ? named.Slot : -1,
                RequiredKind = DetermineRequiredKind(innermost),
            };
        }

        /// <summary>
        /// Computes the default priority: a test naming both halves of what it wants scores 0.25, a plain
        /// name test 0, a wildcard on one half of a name -0.25, a bare kind test -0.5, and anything more
        /// specific than a single step 0.5 (XSLT 3.0 §6.5).
        /// </summary>
        /// <remarks>
        /// Both half-wildcards score the same, and they have to: <c>p:*</c> and <c>*:a</c> each fix one half
        /// of a name and leave the other open, so neither is the more specific and the specification gives
        /// them one number. Scoring <c>*:a</c> as a bare wildcard instead put it level with <c>*</c>, where
        /// the rule written later won and the one that named something lost.
        /// </remarks>
        private static double ComputePriority(PatternStep[] steps, bool anchoredAtRoot, PatternStep innermost)
        {
            bool isSingleSimpleStep = steps.Length == 1
                && !anchoredAtRoot
                && innermost.Predicates.Length == 0;

            if (!isSingleSimpleStep)
            {
                return 0.5;
            }

            return innermost.Test switch
            {
                NameNodeTest => 0.0,
                NamespaceWildcardNodeTest or LocalNameNodeTest => -0.25,
                ProcessingInstructionNodeTest pi => pi.NamesATarget ? 0.0 : -0.5,
                KindNodeTest kind => PriorityOfKindTest(kind),
                _ => -0.5,
            };
        }

        /// <summary>
        /// The default priority of one of the kind tests that can say more than which kind it wants.
        /// </summary>
        /// <remarks>
        /// A kind test naming nothing is a wildcard over its kind and scores as one. Naming one of the two
        /// things it could — the element's name, or the type it was validated against — is as specific as a
        /// name test, and naming both is more specific than anything else a single step can be. A
        /// <c>document-node(…)</c> is as specific as the element test inside it, that being the whole of
        /// what it says beyond the kind.
        /// </remarks>
        /// <param name="test">The kind test.</param>
        private static double PriorityOfKindTest(KindNodeTest test)
        {
            if (test.Content is KindNodeTest inside)
            {
                return PriorityOfKindTest(inside);
            }

            if (test.NamesAName && test.NamesAType)
            {
                return 0.25;
            }

            return test.NamesAName || test.NamesAType ? 0.0 : -0.5;
        }

        private static NodeKind? DetermineRequiredKind(PatternStep innermost)
        {
            if (innermost.Axis == Axis.Attribute)
            {
                return NodeKind.Attribute;
            }

            if (innermost.Axis == Axis.Namespace)
            {
                return NodeKind.Namespace;
            }

            // All four name tests admit only the axis's principal kind, which on the child axis is an
            // element. Leaving the '*:local' form out said it matched any kind, which cost it the indexing
            // the others get — and, once a pattern could be asked about a parentless node, made it look like
            // a test that needs one.
            if (innermost.Test is NameNodeTest or NamespaceWildcardNodeTest or LocalNameNodeTest
                || ReferenceEquals(innermost.Test, NodeTest.Wildcard))
            {
                return NodeKind.Element;
            }

            if (ReferenceEquals(innermost.Test, NodeTest.AnyText))
            {
                return NodeKind.Text;
            }

            if (ReferenceEquals(innermost.Test, NodeTest.AnyComment))
            {
                return NodeKind.Comment;
            }

            if (innermost.Test is ProcessingInstructionNodeTest)
            {
                return NodeKind.ProcessingInstruction;
            }

            // A kind test written out says which kind it wants, document-node() included — and that one has
            // to be known, because it is the pattern that names the node nothing is a child of.
            return innermost.Test is KindNodeTest written ? written.Kind : null;
        }

        /// <summary>
        /// Builds a pattern that is matched by selecting rather than by walking upwards.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Most patterns are answered bottom-up: test the node, then look above it for the steps written to
        /// its left. Some shapes XSLT 3.0 allows have no "left" to look at — a pattern rooted on a variable
        /// or on <c>doc()</c>, a parenthesised union with a predicate over the whole of it,
        /// <c>intersect</c> and <c>except</c>. For those the definition is taken literally instead: a node
        /// matches where the pattern, read as an expression and evaluated from somewhere, selects it.
        /// </para>
        /// <para>
        /// "Somewhere" is what the caller has already folded in. A rooted pattern is evaluated from the root
        /// and nowhere else; a relative one is handed in already prefixed with
        /// <c>descendant-or-self::node()/</c>, which is every node it could have been written relative to.
        /// The cost is one evaluation of the pattern per candidate node, which is the same bargain the key
        /// anchor has always struck, and it buys correctness on shapes that have none of the structure the
        /// fast path relies on.
        /// </para>
        /// </remarks>
        /// <param name="selection">The pattern read as an expression, evaluated from the root of the tree.</param>
        internal static Pattern CreateSelection(Expr selection)
        {
            return new Pattern(Array.Empty<PatternStep>(), false, false, 0.5)
            {
                Selection = selection,
            };
        }

        /// <summary>The expression a selecting pattern is read as, or null for an ordinary one.</summary>
        /// <remarks>
        /// Read by the template index, which cannot bucket one of these by name or kind any more than it can
        /// bucket a predicate pattern: nothing about the shape says what it will select.
        /// </remarks>
        public Expr? Selection
        {
            get => m_selection;
            private init => m_selection = value;
        }

        /// <summary>Whether the pattern, evaluated as an expression, selects the candidate.</summary>
        /// <remarks>
        /// From the root of the candidate's own tree rather than from wherever the transformation is
        /// standing. A pattern says which nodes it describes, and that cannot depend on which node happened
        /// to be in focus when the question was asked — the same rule the key anchor follows.
        /// </remarks>
        /// <param name="node">The candidate node.</param>
        /// <param name="context">The context to evaluate in.</param>
        private bool Selects(int node, ref DynamicContext context)
        {
            if (node < 0)
            {
                return false;
            }

            // From root(N) of the candidate rather than from node zero of its tree: an element built by a
            // variable's as declaration has no parent, so its root is itself, and node zero is a document
            // node it does not belong under.
            DynamicContext lookup = context;
            lookup.Node = context.Tree.RootOf(node);
            lookup.AtomicItem = default;
            lookup.Position = 1;
            lookup.Size = 1;

            // Evaluated as a value rather than as a node-set: a parenthesised union or a variable may give
            // back a sequence, and one holding things that are not nodes among the nodes is no error here —
            // the nodes are what is being asked about, and the rest select nothing.
            XPathValue selected = m_selection!.Evaluate(ref lookup);

            foreach (XPathValue item in XdmSequence.Items(selected))
            {
                // The tree as well as the node. A pattern may name another document — doc('other.xml')//foo
                // is one of the shapes that brought this method into being — and node ids are per tree, so
                // comparing the number alone would let a node of one document match a pattern about another.
                if (item.Kind == XPathValueKind.Node
                    && item.NodeId == node
                    && ReferenceEquals(item.NodeTree, context.Tree))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A pattern matching only the root node, used for the built-in root template rule.</summary>
        /// <summary>
        /// Builds the XSLT 3.0 predicate pattern, <c>.[…]</c>, which matches any <em>item</em> the
        /// predicates hold for.
        /// </summary>
        /// <remarks>
        /// The one pattern form that is not about nodes. Every other pattern names an axis and a node test,
        /// so it can only ever match a node; this one asks a question about the item itself, which is what
        /// lets a template rule match an atomic value, a map or an array — and so what lets
        /// <c>xsl:apply-templates</c> be used over something that is not a tree.
        /// </remarks>
        /// <param name="predicates">The predicates, evaluated with the candidate item in focus.</param>
        public static Pattern PredicatePattern(Expr[] predicates)
        {
            // Default priority 1, not the 0.5 a node test with a predicate gets: a predicate pattern says
            // nothing about a name or a kind and everything about the item itself, so the specification puts
            // it above the patterns that only narrowed a kind. With no predicates it says nothing at all and
            // matches every item, and then it is -1 (§6.5), below every pattern that narrowed anything.
            return new Pattern(Array.Empty<PatternStep>(), false, false, predicates.Length == 0 ? -1.0 : 1.0)
            {
                ItemPredicates = predicates,
                ItemPredicatesAskedForBoolean = NeverNumbers(predicates),
            };
        }

        /// <summary>
        /// For each of <see cref="ItemPredicates"/>, what <see cref="PatternStep.AskedForBoolean"/> is for
        /// a step's.
        /// </summary>
        private bool[] ItemPredicatesAskedForBoolean { get; init; } = Array.Empty<bool>();

        /// <summary>
        /// The predicates of a <c>.[…]</c> pattern, or null where this is an ordinary pattern.
        /// </summary>
        /// <remarks>
        /// Read by the template index, which cannot bucket a predicate pattern by name or kind: it matches
        /// items rather than nodes, so it has to be considered for every candidate in its mode.
        /// </remarks>
        public Expr[]? ItemPredicates { get; private init; }

        /// <summary>
        /// Returns whether an item that is not a node matches this pattern.
        /// </summary>
        /// <remarks>
        /// Only a predicate pattern can: everything else names an axis, and an atomic value is not reachable
        /// along one.
        /// </remarks>
        /// <param name="item">The candidate item.</param>
        /// <param name="context">The context the predicates are evaluated in.</param>
        public bool MatchesItem(XPathValue item, ref DynamicContext context)
        {
            XsltRuntime? runtime = context.Runtime;

            if (runtime is not null)
            {
                runtime.PatternDepth++;
            }

            try
            {
                return MatchesItemUnguarded(item, ref context);
            }
            catch (XsltException failed) when (!IsReported(failed))
            {
                return false;
            }
            finally
            {
                if (runtime is not null)
                {
                    runtime.PatternDepth--;
                }
            }
        }

        private bool MatchesItemUnguarded(XPathValue item, ref DynamicContext context)
        {
            if (ItemPredicates is null)
            {
                return false;
            }

            // Position 1 of 1: a pattern is asked about one item at a time, so there is nothing else for a
            // position to count among.
            DynamicContext inner = context.WithItem(item);
            inner.Position = 1;
            inner.Size = 1;

            for (int i = 0; i < ItemPredicates.Length; i++)
            {
                // Through the same rule an ordinary predicate follows, which is why this is not a plain
                // boolean test: a predicate whose value is a number selects by position. So '.[$n]' with $n
                // of 2 does not match, where reading 2 as true would have matched everything. Only a
                // predicate that could never be a number is asked for a boolean and nothing else.
                if (!PredicateHolds(ItemPredicates[i], ItemPredicatesAskedForBoolean[i], ref inner))
                {
                    return false;
                }
            }

            return true;
        }

        public static Pattern RootPattern { get; } = CreateRootPattern();

        private static Pattern CreateRootPattern()
        {
            PatternStep step = new PatternStep(Axis.Self, new RootOnlyNodeTest(), Array.Empty<Expr>(), PatternReach.Self);
            return new Pattern(new[] { step }, false, false, -0.5)
            {
                RequiredKind = NodeKind.Root,
            };
        }

        private sealed class RootOnlyNodeTest : NodeTest
        {
            public override bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap)
            {
                return tree.KindOf(node) == NodeKind.Root;
            }

            public override string ToString() => "/";
        }
    }
}
