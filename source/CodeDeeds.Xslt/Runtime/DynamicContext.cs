using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// The evaluation context for an XPath expression: the context node, position and size, together with the
    /// variable storage and name resolution in force.
    /// </summary>
    /// <remarks>
    /// A <see langword="ref"/> <see langword="struct"/>, so that passing the context down an expression tree
    /// costs nothing and never allocates. Copy it and adjust the copy to establish a nested context; the
    /// original is unaffected.
    /// </remarks>
    public ref struct DynamicContext
    {
        /// <summary>The tree being queried.</summary>
        public XdmTree Tree;

        /// <summary>Slot-to-fingerprint mapping for <see cref="Tree"/>; see <see cref="NameSlotTable"/>.</summary>
        public int[] FingerprintMap;

        /// <summary>The context node, or <see cref="NotANode"/> when the context item is not one.</summary>
        public int Node;

        /// <summary>
        /// The value <see cref="Node"/> takes when the context item is not a node.
        /// </summary>
        /// <remarks>
        /// Every node is a non-negative index, so this cannot be mistaken for one — and because the thirteen
        /// places that establish a focus all write <see cref="Node"/>, moving to a node clears an atomic
        /// context item without having to remember to.
        /// </remarks>
        public const int NotANode = -1;

        /// <summary>
        /// The context item where it is an atomic value, and an empty node-set where it is not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XPath 2.0 makes the context item an <em>item</em>, which is a node or an atomic value. XPath 1.0
        /// had only the node, and <see cref="Node"/> is still where that lives; this is the other half, and
        /// it is what <c>(1 to 25)[. ge 10]</c> filters on — a node-set cannot hold the integer.
        /// </para>
        /// <para>
        /// A node-set is not an item and so can never be a context item, which is what makes it usable as
        /// the marker for absence: a context that was never given an atomic item holds the default
        /// <see cref="XPathValue"/>, which is an empty node-set.
        /// </para>
        /// </remarks>
        public XPathValue AtomicItem;

        /// <summary>Whether the context item is an atomic value rather than a node.</summary>
        public readonly bool HasAtomicItem => Node < 0 && AtomicItem.Kind != XPathValueKind.NodeSet;

        /// <summary>
        /// Whether there is a context item at all. There is none where an expression is evaluated outside any
        /// focus, which XPath makes an error rather than an empty answer.
        /// </summary>
        public readonly bool HasContextItem => Node >= 0 || AtomicItem.Kind != XPathValueKind.NodeSet;

        /// <summary>
        /// Returns a copy of this context whose context item is an atomic value.
        /// </summary>
        /// <param name="item">The value to become the context item.</param>
        public readonly DynamicContext WithAtomicItem(XPathValue item)
        {
            DynamicContext copy = this;
            copy.Node = NotANode;
            copy.AtomicItem = item;

            return copy;
        }

        /// <summary>
        /// Returns a copy of this context whose context item is the given item, node or atomic value.
        /// </summary>
        /// <remarks>
        /// Both halves are set, never one: a context positioned on a node while still holding the atomic
        /// item it had before reads correctly from <see cref="Node"/> but wrongly from anything that looks
        /// at <see cref="AtomicItem"/>, and the two halves are exactly what one stale value would let
        /// disagree. Switching trees where the item comes from another document is part of the same move,
        /// since a node means nothing without the tree it is numbered in.
        /// </remarks>
        /// <param name="item">The item to become the context item.</param>
        public readonly DynamicContext WithItem(XPathValue item)
        {
            if (item.Kind != XPathValueKind.Node)
            {
                return WithAtomicItem(item);
            }

            DynamicContext copy = ReferenceEquals(Tree, item.NodeTree)
                ? this
                : SwitchTree(item.NodeTree, item.NodeId);

            copy.Node = item.NodeId;
            copy.AtomicItem = default;

            return copy;
        }

        /// <summary>
        /// Returns the context item, whichever half it is in.
        /// </summary>
        /// <param name="what">What wanted it, for the message.</param>
        /// <exception cref="XsltException">There is no context item.</exception>
        public readonly XPathValue RequireContextItem(string what)
        {
            if (Node >= 0)
            {
                return XPathValue.FromNodeSet(NodeSet.Singleton(Tree, Node));
            }

            return AtomicItem.Kind != XPathValueKind.NodeSet
                ? AtomicItem
                : throw XsltErrors.Error(
                    XsltErrorCode.XPDY0002,
                    $"{what} reads the context item, and there is none here — this expression was evaluated "
                    + "outside any focus.");
        }

        /// <summary>
        /// Returns the context node, the context item being one.
        /// </summary>
        /// <param name="what">What wanted it, for the message.</param>
        /// <exception cref="XsltException">There is no context item, or it is an atomic value.</exception>
        public readonly int RequireContextNode(string what)
        {
            if (Node >= 0)
            {
                return Node;
            }

            // Two different complaints, and the codes say which: nothing to read at all, or something to read
            // that is not the kind of thing this asks for.
            throw AtomicItem.Kind != XPathValueKind.NodeSet
                ? XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"{what} reads the context node, and the context item here is an atomic value.")
                : XsltErrors.Error(
                    XsltErrorCode.XPDY0002,
                    $"{what} reads the context node, and there is none here — this expression was evaluated "
                    + "outside any focus.");
        }

        /// <summary>
        /// The node the innermost <c>xsl:for-each</c> or template is processing.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="Node"/>, and the reason <c>current()</c> exists. Inside a predicate the
        /// context node is whichever candidate is being tested, so <c>.</c> refers to that; the current node
        /// stays on the item being processed. It is what makes <c>other[@id = current()/@ref]</c> mean
        /// anything, since <c>.</c> there would refer to the wrong element.
        /// </remarks>
        public int CurrentNode;

        /// <summary>
        /// The tree <see cref="CurrentNode"/> belongs to.
        /// </summary>
        /// <remarks>
        /// Tracked separately from <see cref="Tree"/> because a path can move to another document —
        /// <c>$loaded/a/b[@id = current()/@ref]</c> walks the loaded document while the current node stays in
        /// the input. Without this, <c>current()</c> would index the wrong tree.
        /// </remarks>
        public XdmTree CurrentTree;

        /// <summary>The one-based context position.</summary>
        public int Position;

        /// <summary>The context size.</summary>
        public int Size;

        /// <summary>Backing store for local variables, addressed relative to <see cref="FrameBase"/>.</summary>
        public XPathValue[] Locals;

        /// <summary>The offset in <see cref="Locals"/> at which the current template's frame begins.</summary>
        public int FrameBase;

        /// <summary>Backing store for global variables and parameters.</summary>
        public XPathValue[] Globals;

        /// <summary>
        /// Backing store for the variables bound by <c>for</c>, <c>some</c> and <c>every</c>.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="Locals"/>, which belongs to the stylesheet and is sized by the compiler
        /// before an expression is ever parsed. A range variable is bound inside an expression, so the parser
        /// assigns its slot by nesting depth and the binding expression grows this on demand. Null until an
        /// expression actually binds one, which is nearly always.
        /// </remarks>
        public XPathValue[]? RangeVariables;

        /// <summary>
        /// The transformation in progress, or <see langword="null"/> when an expression is evaluated outside
        /// one. Expressions need it to resolve names against a tree other than <see cref="Tree"/>, which
        /// happens when a path navigates into a result tree fragment.
        /// </summary>
        public XsltRuntime? Runtime;

        /// <summary>
        /// What answers <c>doc()</c> where no transformation is running — a static expression at compile
        /// time — or <see langword="null"/> where nothing does. Given the reference as written and the base
        /// URI to resolve it against, or null for the module's own.
        /// </summary>
        public Func<string, string?, Model.XdmTree>? DocumentLoader;

        /// <summary>
        /// What answers <c>unparsed-text()</c> where no transformation is running, or
        /// <see langword="null"/> where nothing does. Given the reference as written and the encoding
        /// the call named, or null for none.
        /// </summary>
        /// <remarks>
        /// The companion of <see cref="DocumentLoader"/>, and there for the same reason: an
        /// expression evaluated on its own has no transformation behind it and so no resolver of its
        /// own, and a caller that means it to read something lends it one.
        /// </remarks>
        public Func<string, string?, string>? TextLoader;

        /// <summary>
        /// The caller's collations where no transformation is running to carry them — a static expression
        /// at compile time, or an expression evaluated on its own — or <see langword="null"/> where there
        /// are none. A running transformation answers from its own options instead.
        /// </summary>
        public IXsltCollationResolver? Collations;

        /// <summary>
        /// The slots the expression's name tests were assigned, or <see langword="null"/> where they were not
        /// supplied. Outside a transformation this is what lets a path into a document built while the
        /// expression ran — by <c>fn:parse-xml</c> or <c>fn:json-to-xml</c> — resolve its names at all.
        /// </summary>
        public XPath.NameSlotTable? Names;

        /// <summary>
        /// The one reading of the clock this evaluation makes, shared by every <c>current-*</c> call in it.
        /// </summary>
        /// <remarks>
        /// A reference rather than the moment itself, so that a context copied by <see cref="SwitchTree"/>
        /// or carried into a called template keeps reading the same clock. It is created on first use where
        /// nothing supplied one, which is what makes an expression evaluated outside a transformation stable
        /// within itself.
        /// </remarks>
        public Clock? Clock;

        /// <summary>
        /// Returns the clock this evaluation reads, making one where nothing has supplied it.
        /// </summary>
        /// <remarks>
        /// A transformation's clock is the runtime's, so that every template in it agrees; an expression
        /// evaluated outside one makes its own the first time it asks, which keeps it stable within itself.
        /// </remarks>
        public Clock ReadClock() => Runtime?.Clock ?? (Clock ??= new Clock());

        /// <summary>
        /// Initializes a context positioned on a single node, with no variables in scope.
        /// </summary>
        /// <param name="tree">The tree being queried.</param>
        /// <param name="node">The context node.</param>
        /// <param name="fingerprintMap">Slot-to-fingerprint mapping for <paramref name="tree"/>.</param>
        /// <param name="names">
        /// The table the mapping came from, so that a second tree can be mapped as well. Optional, and worth
        /// supplying wherever an expression may reach a tree other than <paramref name="tree"/>.
        /// </param>
        public DynamicContext(
            XdmTree tree, int node, int[] fingerprintMap, XPath.NameSlotTable? names = null)
        {
            Tree = tree;
            FingerprintMap = fingerprintMap;
            Names = names;
            Node = node;
            AtomicItem = default;
            CurrentNode = node;
            CurrentTree = tree;
            Position = 1;
            Size = 1;
            Locals = Array.Empty<XPathValue>();
            FrameBase = 0;
            Globals = Array.Empty<XPathValue>();
            RangeVariables = null;
            Runtime = null;
            Clock = null;
        }

        /// <summary>
        /// Returns a copy of this context repositioned on a node of a different tree, resolving name slots
        /// against that tree. Used when a path expression navigates into a result tree fragment.
        /// </summary>
        /// <param name="tree">The tree to switch to.</param>
        /// <param name="node">The node in <paramref name="tree"/> to become the context node.</param>
        /// <returns>A context over <paramref name="tree"/>.</returns>
        public readonly DynamicContext SwitchTree(XdmTree tree, int node)
        {
            DynamicContext copy = this;
            copy.Tree = tree;
            copy.Node = node;
            copy.FingerprintMap = Runtime is not null ? Runtime.GetFingerprintMap(tree)
                : tree.NameTable == Tree.NameTable ? FingerprintMap
                : Names is not null ? Names.BuildFingerprintMap(tree)
                : Array.Empty<int>();

            return copy;
        }

        /// <summary>
        /// Copies this context to the heap, for the one place a context has to leave the stack it is on.
        /// </summary>
        /// <remarks>
        /// A <see langword="ref"/> <see langword="struct"/> cannot be handed to another thread, and
        /// <see cref="FreshStack"/> continues a deep recursion on one. See <see cref="Held"/>.
        /// </remarks>
        internal readonly Held Hold()
        {
            return new Held
            {
                Tree = Tree,
                FingerprintMap = FingerprintMap,
                Node = Node,
                AtomicItem = AtomicItem,
                CurrentNode = CurrentNode,
                CurrentTree = CurrentTree,
                Position = Position,
                Size = Size,
                Locals = Locals,
                FrameBase = FrameBase,
                Globals = Globals,
                RangeVariables = RangeVariables,
                Runtime = Runtime,
                DocumentLoader = DocumentLoader,
                TextLoader = TextLoader,
                Collations = Collations,
                Names = Names,
                Clock = Clock,
            };
        }

        /// <summary>
        /// A context copied to the heap, field for field.
        /// </summary>
        /// <remarks>
        /// Every field of <see cref="DynamicContext"/> has one here of the same name, and a field added
        /// there is added here and to <see cref="Hold"/> and <see cref="Restore"/>: one left out would be
        /// silently absent from a context only where a recursion ran deep, which nothing but a test looking
        /// for exactly that would notice. The unit tests compare the two lists of names.
        /// </remarks>
        internal sealed class Held
        {
            public XdmTree Tree = null!;
            public int[] FingerprintMap = null!;
            public int Node;
            public XPathValue AtomicItem;
            public int CurrentNode;
            public XdmTree CurrentTree = null!;
            public int Position;
            public int Size;
            public XPathValue[] Locals = null!;
            public int FrameBase;
            public XPathValue[] Globals = null!;
            public XPathValue[]? RangeVariables;
            public XsltRuntime? Runtime;
            public Func<string, string?, Model.XdmTree>? DocumentLoader;
            public Func<string, string?, string>? TextLoader;
            public IXsltCollationResolver? Collations;
            public XPath.NameSlotTable? Names;
            public Clock? Clock;

            /// <summary>Rebuilds the context this was copied from.</summary>
            public DynamicContext Restore()
            {
                return new DynamicContext(Tree, Node, FingerprintMap, Names)
                {
                    AtomicItem = AtomicItem,
                    CurrentNode = CurrentNode,
                    CurrentTree = CurrentTree,
                    Position = Position,
                    Size = Size,
                    Locals = Locals,
                    FrameBase = FrameBase,
                    Globals = Globals,
                    RangeVariables = RangeVariables,
                    Runtime = Runtime,
                    DocumentLoader = DocumentLoader,
                    TextLoader = TextLoader,
                    Collations = Collations,
                    Clock = Clock,
                };
            }
        }
    }

    /// <summary>
    /// One reading of the clock, shared by every <c>current-*</c> call in an evaluation.
    /// </summary>
    /// <remarks>
    /// The specification requires <c>current-dateTime</c>, <c>current-date</c> and <c>current-time</c> to be
    /// stable within an execution scope, and to agree with one another — <c>current-date()</c> must be the
    /// date part of the instant <c>current-dateTime()</c> reports. Asking the operating system twice cannot
    /// give that, so the clock is read once and the reading kept.
    /// </remarks>
    public sealed class Clock
    {
        private DateTimeOffset? m_reading;

        /// <summary>Gets the moment this evaluation calls now, reading the clock the first time it is asked.</summary>
        public DateTimeOffset Now => m_reading ??= DateTimeOffset.UtcNow;
    }
}
