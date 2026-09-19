using System.Reflection.Emit;
using CodeDeeds.Xslt.Emit;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>One step of a location path: an axis, a node test, and zero or more predicates.</summary>
    public sealed class AxisStep
    {
        /// <summary>Initializes a step.</summary>
        /// <param name="axis">The axis to walk.</param>
        /// <param name="test">The test applied to the nodes the axis produces.</param>
        /// <param name="predicates">The predicates, applied left to right.</param>
        public AxisStep(Axis axis, NodeTest test, Expr[] predicates)
        {
            Axis = axis;
            Test = test;
            Predicates = predicates;
        }

        /// <summary>Gets the axis this step walks.</summary>
        public Axis Axis { get; }

        /// <summary>Gets the node test applied to candidates.</summary>
        public NodeTest Test { get; }

        /// <summary>Gets the predicates filtering this step's result.</summary>
        public Expr[] Predicates { get; }
    }

    /// <summary>The root of the document, written as a leading <c>/</c>.</summary>
    /// <remarks>
    /// Not "the document being transformed" but "the root of the tree the context node is in", which is what
    /// makes <c>/</c> mean something different inside <c>xsl:for-each</c> over another document. It follows
    /// that it means nothing at all where there is no context node: inside a stylesheet function, or in a
    /// transformation started at a named template with no source document. Reading it as the source
    /// document's root there answered a question the expression had not asked.
    /// </remarks>
    public sealed class RootExpr : Expr
    {
        /// <inheritdoc/>
        public override bool ReturnsNodeSet => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return XPathValue.FromNodeSet(NodeSet.Singleton(context.Tree, RootOf(ref context)));
        }

        /// <inheritdoc/>
        public override XdmTree EvaluateNodes(ref DynamicContext context, List<int> output)
        {
            output.Add(RootOf(ref context));
            return context.Tree;
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            RootOf(ref context);
            return true;
        }

        /// <summary>
        /// The root of the tree the context node is in, refusing a root step that names nothing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three errors rather than one, because the three mistakes are different. Nothing at all in focus
        /// is <c>XPDY0002</c>; an atomic value in focus is <c>XPTY0020</c>, an axis step applied to
        /// something that is not a node. And a node whose root is not a document node is <c>XPDY0050</c>: a
        /// leading <c>/</c> is <c>fn:root(self::node())</c> <em>treated as</em> a document node (XPath 2.0
        /// §3.2), and that is the treat failing.
        /// </para>
        /// <para>
        /// Node zero is the document node a tree was built around, and in every ordinary tree it is the
        /// root of everything in it. A sequence constructor with a declared type detaches what it built
        /// from that document node — the nodes of a sequence are parentless — and leaves it childless,
        /// which is what tells the two apart without walking up from the context node on every step.
        /// </para>
        /// </remarks>
        private static int RootOf(ref DynamicContext context)
        {
            if (context.Node >= 0)
            {
                int root = XdmTree.RootNode;

                if (context.Node != XdmTree.RootNode && context.Tree.FirstChildOf(XdmTree.RootNode) < 0)
                {
                    root = context.Node;

                    for (int parent = context.Tree.ParentOf(root); parent >= 0;
                        parent = context.Tree.ParentOf(root))
                    {
                        root = parent;
                    }
                }

                if (context.Tree.KindOf(root) != Model.NodeKind.Root)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPDY0050,
                        "'/' is the root of the tree the context node is in, and that root is not a "
                        + "document node: this node stands in a tree of its own rather than in a document, "
                        + "which is what a sequence constructor with a declared type builds.");
                }

                return root;
            }

            throw XsltErrors.Error(
                context.HasContextItem ? XsltErrorCode.XPTY0020 : XsltErrorCode.XPDY0002,
                context.HasContextItem
                    ? "'/' is the root of the tree the context node is in, and the context item here is not "
                    + "a node."
                    : "'/' is the root of the tree the context node is in, and there is no context item "
                    + "here. A stylesheet function has none, and neither has a transformation started at a "
                    + "named template with no source document.");
        }

        /// <inheritdoc/>
        public override string ToString() => "/";
    }

    /// <summary>
    /// A path step that is an expression rather than an axis step: <c>a/(b|c)</c>, <c>a/name/string()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 2.0 lets any primary expression be a step, where 1.0 allowed only an axis. The step is
    /// evaluated once per node the left side produced, with that node as the context item, and what comes
    /// back is joined — sorted into document order and de-duplicated if it is nodes, kept in order if it is
    /// not. A mixture of the two is <c>XPTY0018</c>: there is no order that would suit both.
    /// </para>
    /// <para>
    /// Kept apart from <see cref="PathExpr"/>, which walks axes over a list of node ids and is the hottest
    /// code in the engine. This shape only appears where one was written, so nothing that could be an axis
    /// step pays for it.
    /// </para>
    /// </remarks>
    public sealed class StepMapExpr : Expr
    {
        private readonly Expr m_source;
        private readonly Expr m_step;

        /// <summary>Initializes a general path step.</summary>
        /// <param name="source">The path so far, whose nodes the step is taken from.</param>
        /// <param name="step">The step, evaluated once per node.</param>
        public StepMapExpr(Expr source, Expr step)
        {
            m_source = source;
            m_step = step;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_source, m_step };

        /// <summary>Always true: a step may reach a node of any document the left side carried in.</summary>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            List<XPathValue> sources = XdmSequence.Items(m_source.Evaluate(ref context));
            List<XPathValue> results = new List<XPathValue>();

            bool nodes = false;
            bool others = false;

            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i].Kind != XPathValueKind.Node)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0019,
                        "A path takes a step from each node it is given, and this one was given a "
                        + $"{sources[i].Kind.ToString().ToLowerInvariant()}, which is not a node.");
                }

                DynamicContext inner = context.SwitchTree(sources[i].NodeTree, sources[i].NodeId);
                inner.Position = i + 1;
                inner.Size = sources.Count;

                foreach (XPathValue item in XdmSequence.Items(m_step.Evaluate(ref inner)))
                {
                    nodes |= item.Kind == XPathValueKind.Node;
                    others |= item.Kind != XPathValueKind.Node;

                    results.Add(item);
                }
            }

            if (nodes && others)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0018,
                    "The last step of a path gave both nodes and values. One or the other can be put in "
                    + "order; a mixture cannot.");
            }

            return nodes ? Gather(results, context.Tree) : XdmSequence.Concatenate(results);
        }

        /// <summary>Puts the nodes a step produced into document order, without repeats.</summary>
        private static XPathValue Gather(List<XPathValue> items, XdmTree fallback)
        {
            NodeSet result = new NodeSet(
                items.Count == 0 ? fallback : items[0].NodeTree, items.Count);

            foreach (XPathValue item in items)
            {
                result.Add(item.NodeTree, item.NodeId);
            }

            result.SortAndDeduplicate();
            return XPathValue.FromNodeSet(result);
        }
    }

    /// <summary>
    /// A location path: an optional starting expression followed by a sequence of steps.
    /// </summary>
    /// <remarks>
    /// Each step is evaluated for every node the previous step produced. Within a single starting node the
    /// axis is walked in axis order, so that <c>position()</c> inside a predicate counts correctly on reverse
    /// axes; the results are only returned to document order once the step as a whole is complete.
    /// </remarks>
    public sealed class PathExpr : Expr
    {
        private readonly Expr? m_start;
        private readonly AxisStep[] m_steps;

        /// <summary>Initializes a location path.</summary>
        /// <param name="start">
        /// The expression the path starts from, or <see langword="null"/> to start at the context node.
        /// </param>
        /// <param name="steps">The steps to apply in order.</param>
        public PathExpr(Expr? start, AxisStep[] steps)
        {
            m_start = start;
            m_steps = Simplify(steps);
        }

        /// <summary>
        /// Folds <c>descendant-or-self::node()/child::x</c>, which is what <c>//x</c> abbreviates, into the
        /// one step <c>descendant::x</c> wherever the two select the same nodes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Written out, the abbreviation gathers every descendant of the origin into a list and then takes
        /// a child step from each of them, which visits every node twice and holds the whole document's
        /// worth of ids in between. A single descendant step visits each node once with the name test
        /// applied as it goes, and holds only what it selects. On a document of sixty thousand nodes the
        /// difference is the larger part of what <c>count(//x)</c> costs.
        /// </para>
        /// <para>
        /// The two are the same set in document order except where a predicate on the child step counts:
        /// <c>//x[1]</c> is the first <c>x</c> child of each node, and <c>descendant::x[1]</c> the first
        /// <c>x</c> anywhere. So the fold is made only where every predicate is known to be a boolean and
        /// to read nothing of the focus position, which is what makes it the same test on the same nodes
        /// whichever way they were reached.
        /// </para>
        /// </remarks>
        private static AxisStep[] Simplify(AxisStep[] steps)
        {
            bool any = false;

            for (int i = 0; i + 1 < steps.Length; i++)
            {
                if (IsAbbreviatedDescendant(steps[i], steps[i + 1]))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return steps;
            }

            List<AxisStep> folded = new List<AxisStep>(steps.Length);

            for (int i = 0; i < steps.Length; i++)
            {
                if (i + 1 < steps.Length && IsAbbreviatedDescendant(steps[i], steps[i + 1]))
                {
                    AxisStep child = steps[i + 1];
                    folded.Add(new AxisStep(Axis.Descendant, child.Test, child.Predicates));
                    i++;
                }
                else
                {
                    folded.Add(steps[i]);
                }
            }

            return folded.ToArray();
        }

        private static bool IsAbbreviatedDescendant(AxisStep first, AxisStep second)
        {
            if (first.Axis != Axis.DescendantOrSelf
                || !ReferenceEquals(first.Test, NodeTest.AnyNode)
                || first.Predicates.Length != 0
                || second.Axis != Axis.Child)
            {
                return false;
            }

            foreach (Expr predicate in second.Predicates)
            {
                if (!(predicate.ReturnsNodeSet || predicate.IsBooleanValued)
                    || Expr.DependsOnFocusPosition(predicate))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            NodeSet? initial = EvaluateStart(ref context);

            if (initial is not null && initial.SpansDocuments)
            {
                return XPathValue.FromNodeSet(EvaluateAcrossDocuments(ref context, initial));
            }

            DynamicContext walk = context;
            List<int> current = NodeListPool.Rent();
            List<int> next = NodeListPool.Rent();

            try
            {
                XdmTree tree = EvaluateSteps(ref context, ref walk, ref current, ref next, initial);
                return XPathValue.FromNodeSet(NodeSet.FromOrderedNodes(tree, current));
            }
            finally
            {
                NodeListPool.Return(current);
                NodeListPool.Return(next);
            }
        }

        /// <summary>
        /// Evaluates the expression the path starts from, or returns <see langword="null"/> for a path that
        /// starts at the context node.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Pulled out of <see cref="EvaluateSteps"/> because whether a path begins in more than one document
        /// can only be discovered by evaluating its start, and that has to be decided before the steps run.
        /// The result is handed back down so it is never evaluated twice.
        /// </para>
        /// <para>
        /// A step can only be applied to nodes, so anything else here is <c>XPTY0019</c> — the error XPath
        /// names for exactly this, and a narrower diagnosis than the general <c>XPTY0004</c>: it says that
        /// what went wrong is the step, not the value.
        /// </para>
        /// <para>
        /// XPath 2.0 lets a path start from anything that yields nodes, and a sequence is one of those:
        /// <c>for $h in /works/employee return $h/text()</c> binds <c>$h</c> to a single node item, not to a
        /// node-set. The steps below walk a node-set, so a sequence of nodes is gathered into one here —
        /// which is safe because the steps sort into document order regardless of how their input arrived.
        /// </para>
        /// </remarks>
        private NodeSet? EvaluateStart(ref DynamicContext context)
        {
            if (m_start is null)
            {
                return null;
            }

            XPathValue start = m_start.Evaluate(ref context);

            return start.Kind == XPathValueKind.NodeSet
                ? start.AsNodeSet()
                : NodeSet.Of(start, context.Tree, XsltErrorCode.XPTY0019, "A path takes a step from a node, so it");
        }

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => true;

        /// <summary>
        /// A path reaches a second document only if it starts in one: the steps themselves never leave the
        /// tree they are walking.
        /// </summary>
        public override bool MaySpanDocuments => m_start is not null && m_start.MaySpanDocuments;

        /// <summary>Gets the steps of this path, whose predicate arrays may be rewritten in place.</summary>
        internal AxisStep[] Steps => m_steps;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children =>
            m_start is null ? Array.Empty<Expr>() : new[] { m_start };

        /// <inheritdoc/>
        public override XdmTree EvaluateNodes(ref DynamicContext context, List<int> output)
        {
            DynamicContext walk = context;
            NodeSet? initial = EvaluateStart(ref context);

            if (initial is not null && initial.SpansDocuments)
            {
                NodeSet spanning = EvaluateAcrossDocuments(ref context, initial);
                for (int i = 0; i < spanning.Count; i++)
                {
                    output.Add(spanning[i]);
                }

                // Every node is here, but only one tree can be named. Callers that count the nodes or ask
                // whether there are any are unaffected; a caller reading string-values from the returned tree
                // would be, which is why comparison goes through the node-set instead.
                return spanning.Count == 0 ? context.Tree : spanning.TreeAt(0);
            }

            List<int> current = NodeListPool.Rent();
            List<int> next = NodeListPool.Rent();

            try
            {
                XdmTree tree = EvaluateSteps(ref context, ref walk, ref current, ref next, initial);
                output.AddRange(current);
                return tree;
            }
            finally
            {
                NodeListPool.Return(current);
                NodeListPool.Return(next);
            }
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            NodeSet? initial = EvaluateStart(ref context);

            if (initial is not null && initial.SpansDocuments)
            {
                return EvaluateAcrossDocuments(ref context, initial).Count != 0;
            }

            DynamicContext walk = context;
            List<int> current = NodeListPool.Rent();
            List<int> next = NodeListPool.Rent();

            try
            {
                EvaluateSteps(ref context, ref walk, ref current, ref next, initial);
                return current.Count != 0;
            }
            finally
            {
                NodeListPool.Return(current);
                NodeListPool.Return(next);
            }
        }

        /// <summary>
        /// Recognises the shape this compiler emits inline: a single unpredicated child-axis name test taken
        /// from the context node, such as <c>c:Amt</c>.
        /// </summary>
        /// <remarks>
        /// It is worth singling out because it is overwhelmingly the most common step in real stylesheets, and
        /// because it is the one evaluated over and over inside predicates. Everything else still compiles —
        /// it just compiles to a call back into the interpreter.
        /// </remarks>
        /// <param name="nameSlot">On success, the name slot the step tests for.</param>
        internal bool TryGetSimpleChildNameStep(out int nameSlot)
        {
            nameSlot = -1;

            if (m_start is not null || m_steps.Length != 1)
            {
                return false;
            }

            AxisStep step = m_steps[0];
            if (step.Axis != Axis.Child || step.Predicates.Length != 0 || step.Test is not NameNodeTest name)
            {
                return false;
            }

            nameSlot = name.Slot;
            return true;
        }

        /// <inheritdoc/>
        internal override void Emit(EmitContext context)
        {
            if (!TryGetSimpleChildNameStep(out int nameSlot))
            {
                context.EmitInterpreterFallback(this);
                return;
            }

            LocalBuilder tree = context.IL.DeclareLocal(typeof(XdmTree));
            LocalBuilder list = context.IL.DeclareLocal(typeof(List<int>));

            context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.RentList)));
            context.IL.StoreLocal(list);

            EmitChildNameLoop(context, nameSlot, tree, list);

            context.IL.LoadLocal(tree);
            context.IL.LoadLocal(list);
            context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.MakeNodeSet)));

            context.IL.LoadLocal(list);
            context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.ReturnList)));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// What an emitted <c>and</c>, <c>or</c>, <c>not()</c> or <c>boolean()</c> asks of an operand that
        /// is a path. Left to the default it was the path's value with its effective boolean value taken
        /// after: a node-set built for every candidate of a predicate such as <c>[@id and price &gt; 100]</c>
        /// only to be asked whether it was empty. The inline walk answers from the list it filled, and any
        /// other path is handed to <see cref="EvaluateAsBoolean"/>, which builds nothing either.
        /// </remarks>
        internal override void EmitAsBoolean(EmitContext context)
        {
            if (!TryGetSimpleChildNameStep(out int nameSlot))
            {
                context.EmitInterpreterBooleanFallback(this);
                return;
            }

            LocalBuilder tree = context.IL.DeclareLocal(typeof(XdmTree));
            LocalBuilder list = context.IL.DeclareLocal(typeof(List<int>));

            context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.RentList)));
            context.IL.StoreLocal(list);

            EmitChildNameLoop(context, nameSlot, tree, list);

            context.IL.LoadLocal(list);
            context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.AnyThenReturnList)));
        }

        /// <summary>
        /// Emits a walk of the context node's children, appending those matching the step's name to a list.
        /// </summary>
        /// <remarks>
        /// This is what the compiled backend actually buys. The interpreter reaches the same result through a
        /// virtual call per expression node, a loop over the step array, and a virtual
        /// <see cref="NodeTest.Matches"/> for every child inspected. Here the name test is two integer
        /// comparisons emitted straight into the loop.
        /// </remarks>
        /// <param name="context">The compilation context.</param>
        /// <param name="nameSlot">The name slot the step tests for.</param>
        /// <param name="tree">A local that receives the tree being walked.</param>
        /// <param name="list">A local holding the list to append to.</param>
        internal static void EmitChildNameLoop(
            EmitContext context,
            int nameSlot,
            LocalBuilder tree,
            LocalBuilder list)
        {
            ILBuilder il = context.IL;

            LocalBuilder wanted = il.DeclareLocal(typeof(int));
            LocalBuilder child = il.DeclareLocal(typeof(int));

            // The walk indexes the tree by the context node, so it asks first whether there is one, as the
            // interpreter's does: inside 'for-each select="(1, 2)"' there is not, and the step is an error
            // to be reported rather than an index to run off the end of an array with.
            context.LoadContext();
            il.Call(EmitHelpers.Method(nameof(EmitHelpers.RequireContextNode)));

            // wanted = context.FingerprintMap[nameSlot]
            context.LoadContextField(nameof(DynamicContext.FingerprintMap));
            il.LoadInt(nameSlot);
            il.LoadElementInt();
            il.StoreLocal(wanted);

            context.LoadContextField(nameof(DynamicContext.Tree));
            il.StoreLocal(tree);

            il.LoadLocal(tree);
            context.LoadContextField(nameof(DynamicContext.Node));
            il.Call(TreeMethod(nameof(XdmTree.FirstChildOf)));
            il.StoreLocal(child);

            Label loop = il.DefineLabel("child-loop");
            Label next = il.DefineLabel("child-next");
            Label finished = il.DefineLabel("child-done");

            il.MarkLabel(loop);

            il.LoadLocal(child);
            il.LoadInt(0);
            il.BranchComparing(OpCodes.Blt, finished, "child < 0");

            // Only elements can carry the name being tested for.
            il.LoadLocal(tree);
            il.LoadLocal(child);
            il.Call(TreeMethod(nameof(XdmTree.KindOf)));
            il.LoadInt((int)NodeKind.Element);
            il.BranchComparing(OpCodes.Bne_Un, next, "kind != element");

            il.LoadLocal(tree);
            il.LoadLocal(child);
            il.Call(TreeMethod(nameof(XdmTree.FingerprintOf)));
            il.LoadLocal(wanted);
            il.BranchComparing(OpCodes.Bne_Un, next, "name mismatch");

            il.LoadLocal(list);
            il.LoadLocal(child);
            il.Call(typeof(List<int>).GetMethod(nameof(List<int>.Add))!, virtualCall: true);

            il.MarkLabel(next);
            il.LoadLocal(tree);
            il.LoadLocal(child);
            il.Call(TreeMethod(nameof(XdmTree.NextSiblingOf)));
            il.StoreLocal(child);
            il.Branch(loop);

            il.MarkLabel(finished);
        }

        private static System.Reflection.MethodInfo TreeMethod(string name)
        {
            return typeof(XdmTree).GetMethod(name)
                ?? throw new InvalidOperationException($"XdmTree.{name} could not be located.");
        }

        /// <summary>
        /// Evaluates the path when its starting node-set spans documents, by running the steps once per
        /// document and unioning what each produces.
        /// </summary>
        /// <remarks>
        /// The steps themselves are resolutely single-tree — an axis walk indexes one <see cref="XdmTree"/>,
        /// and name tests resolve against that tree's name table — so rather than teach them to switch trees
        /// mid-walk, the origins are grouped and each group walked in its own document. Since the starting set
        /// arrives in document order, a group is a contiguous run.
        /// </remarks>
        private NodeSet EvaluateAcrossDocuments(ref DynamicContext context, NodeSet initial)
        {
            NodeSet result = new NodeSet(initial.TreeAt(0), initial.Count);

            for (int start = 0; start < initial.Count;)
            {
                XdmTree tree = initial.TreeAt(start);

                int end = start + 1;
                while (end < initial.Count && ReferenceEquals(initial.TreeAt(end), tree))
                {
                    end++;
                }

                DynamicContext walk = context.SwitchTree(tree, context.Node);
                List<int> current = NodeListPool.Rent();
                List<int> next = NodeListPool.Rent();

                try
                {
                    for (int i = start; i < end; i++)
                    {
                        current.Add(initial[i]);
                    }

                    WalkSteps(ref walk, ref current, ref next);

                    for (int i = 0; i < current.Count; i++)
                    {
                        result.Add(tree, current[i]);
                    }
                }
                finally
                {
                    NodeListPool.Return(current);
                    NodeListPool.Return(next);
                }

                start = end;
            }

            result.SortAndDeduplicate();
            return result;
        }

        /// <summary>
        /// The error a relative path raises where the context item is not a node for its first step to
        /// start from: <c>XPTY0020</c> where it is an atomic value, <c>XPDY0002</c> where there is none.
        /// </summary>
        /// <remarks>
        /// One place for it, because a step is walked from two: here, and by the code the compiled backend
        /// emits inline, which indexes the tree by the context node and has to have asked first.
        /// </remarks>
        internal static XsltException NoContextNode(ref DynamicContext context)
        {
            return context.HasAtomicItem
                ? XsltErrors.Error(
                    XsltErrorCode.XPTY0020,
                    "A step starts from the context item, and here that item is an atomic value "
                    + "rather than a node.")
                : XsltErrors.Error(
                    XsltErrorCode.XPDY0002,
                    "A relative path starts at the context item, and there is none here — this "
                    + "expression was evaluated outside any focus.");
        }

        private XdmTree EvaluateSteps(
            ref DynamicContext context,
            ref DynamicContext walk,
            ref List<int> current,
            ref List<int> next,
            NodeSet? start)
        {
            if (start is null)
            {
                // A relative path starts at the context item, and a step needs that item to be a node. It is
                // not one when a predicate is filtering atomic values — in '(1 to 25)[foo]' there is no node
                // for 'foo' to be a child of — and there is no item at all outside a focus.
                if (context.Node < 0)
                {
                    throw NoContextNode(ref context);
                }

                current.Add(context.Node);
            }
            else
            {
                NodeSet initial = start;
                for (int i = 0; i < initial.Count; i++)
                {
                    current.Add(initial[i]);
                }

                // A variable holding a result tree fragment carries nodes from a different tree, with its own
                // name table. The remaining steps have to be walked against that tree.
                if (!ReferenceEquals(initial.Tree, context.Tree))
                {
                    walk = context.SwitchTree(initial.Tree, context.Node);
                }
            }

            WalkSteps(ref walk, ref current, ref next);
            return walk.Tree;
        }

        /// <summary>
        /// Applies every step in turn to a set of origin nodes, all of them in <see cref="DynamicContext.Tree"/>.
        /// </summary>
        /// <param name="walk">The context to walk in, already positioned on the right tree.</param>
        /// <param name="current">The origin nodes on entry, the result on return.</param>
        /// <param name="next">A scratch buffer, swapped with <paramref name="current"/> between steps.</param>
        private void WalkSteps(ref DynamicContext walk, ref List<int> current, ref List<int> next)
        {
            foreach (AxisStep step in m_steps)
            {
                if (current.Count == 0)
                {
                    break;
                }

                next.Clear();

                // Walking from a single origin, an axis already yields distinct nodes in axis order. That
                // covers most steps in practice — every step of a chain like a/b/c/d after the first — and it
                // means the result needs no sort and no de-duplication, only a reversal on a reverse axis.
                bool singleOrigin = current.Count == 1;

                foreach (int origin in current)
                {
                    int mark = next.Count;
                    AxisWalker.Collect(walk.Tree, origin, step.Axis, step.Test, walk.FingerprintMap, next);
                    PredicateFilter.ApplyInPlace(step.Predicates, next, mark, ref walk);
                }

                if (singleOrigin)
                {
                    if (step.Axis.IsReverse())
                    {
                        next.Reverse();
                    }
                }
                else
                {
                    SortAndDeduplicate(next, walk.Tree);
                }

                // Reuse the previous buffer as scratch for the next step instead of allocating one.
                (current, next) = (next, current);
            }
        }

        /// <summary>
        /// Places nodes in document order and removes duplicates, needed only when a step ran from more than
        /// one origin and so could produce overlapping or interleaved results.
        /// </summary>
        private static void SortAndDeduplicate(List<int> nodes, XdmTree tree)
        {
            if (nodes.Count < 2)
            {
                return;
            }

            bool containsAttributes = false;
            bool ordered = true;

            for (int i = 0; i < nodes.Count; i++)
            {
                containsAttributes |= XdmTree.IsAttribute(nodes[i]);
                if (i > 0 && nodes[i] <= nodes[i - 1])
                {
                    ordered = false;
                }
            }

            if (containsAttributes)
            {
                // Attribute ids sit outside the preorder sequence, so they need the explicit ordering key.
                nodes.Sort((left, right) =>
                    tree.DocumentOrderKeyOf(left).CompareTo(tree.DocumentOrderKeyOf(right)));
            }
            else if (!ordered)
            {
                nodes.Sort();
            }

            int write = 1;
            for (int read = 1; read < nodes.Count; read++)
            {
                if (nodes[read] != nodes[write - 1])
                {
                    nodes[write++] = nodes[read];
                }
            }

            nodes.RemoveRange(write, nodes.Count - write);
        }
    }
}
