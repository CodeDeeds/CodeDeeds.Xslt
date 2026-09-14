using CodeDeeds.Xslt.Emit;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A compiled XPath expression.
    /// </summary>
    /// <remarks>
    /// Expressions form the intermediate representation shared by both execution backends. The interpreter
    /// implements <see cref="Evaluate"/>; the IL backend will add an emit method to the same node types rather
    /// than introducing a parallel hierarchy.
    /// </remarks>
    public abstract class Expr
    {
        /// <summary>Evaluates this expression.</summary>
        /// <param name="context">The evaluation context. Not modified by the call.</param>
        /// <returns>The resulting value.</returns>
        public abstract XPathValue Evaluate(ref DynamicContext context);

        /// <summary>
        /// Gets whether this expression is statically known to produce a node-set.
        /// </summary>
        /// <remarks>
        /// Lets consumers take the allocation-free route below without inspecting a value first. A
        /// <see langword="false"/> answer only means "not known", never "cannot be" — a variable reference may
        /// still hold a node-set at run time, and simply takes the general path.
        /// </remarks>
        public virtual bool ReturnsNodeSet => false;

        /// <summary>
        /// Gets whether this expression could produce a node-set drawn from more than one document.
        /// </summary>
        /// <remarks>
        /// Decided when the expression is compiled, and false for all but a handful of shapes: only
        /// <c>document()</c> introduces a second document, and only a union, a filter, a path or a variable can
        /// carry one outwards. It exists so that the optimised comparison path — which takes a plain list of
        /// node ids and one tree, and cannot express nodes from two — is skipped exactly when it would be
        /// wrong, at no cost to the paths where it is right.
        /// </remarks>
        public virtual bool MaySpanDocuments => false;

        /// <summary>
        /// Evaluates this expression, appending the selected nodes to a caller-supplied list instead of
        /// wrapping them in a <see cref="NodeSet"/>.
        /// </summary>
        /// <remarks>
        /// Counting, testing and comparing a node-set never need the <see cref="NodeSet"/> object itself, and
        /// a path inside a predicate is evaluated once per candidate node. Letting the caller supply a pooled
        /// list removes that allocation from the hottest loop in the engine.
        /// </remarks>
        /// <param name="context">The evaluation context.</param>
        /// <param name="output">The list to append to. Nodes are appended in document order.</param>
        /// <returns>The tree the appended nodes belong to.</returns>
        public virtual XdmTree EvaluateNodes(ref DynamicContext context, List<int> output)
        {
            NodeSet nodes = Evaluate(ref context).AsNodeSet();
            for (int i = 0; i < nodes.Count; i++)
            {
                output.Add(nodes[i]);
            }

            return nodes.Tree;
        }

        /// <summary>
        /// Evaluates this expression and converts the result to a boolean.
        /// </summary>
        /// <param name="context">The evaluation context.</param>
        public virtual bool EvaluateAsBoolean(ref DynamicContext context)
        {
            return Evaluate(ref context).ToBoolean();
        }

        /// <summary>
        /// Gets this expression's sub-expressions, excluding predicates.
        /// </summary>
        /// <remarks>
        /// Used to walk a tree looking for the predicates buried inside it. Predicates are deliberately left
        /// out so that a traversal can visit them separately, in the one place that is allowed to replace them.
        /// </remarks>
        internal virtual IEnumerable<Expr> Children => Array.Empty<Expr>();

        /// <summary>
        /// Whether evaluating this needs the transformation it was written in, and not only the values it
        /// is handed.
        /// </summary>
        /// <remarks>
        /// True of a call to a function the stylesheet declares, which is run by the transformation's own
        /// runtime. It decides one thing: a function item standing for such a call carries the transformation
        /// it was made in, because the item may be called where there is none — a static variable reading
        /// what <c>fn:transform()</c> handed back, or a <c>use-when</c> answered before anything runs.
        /// </remarks>
        internal virtual bool NeedsTheTransformation => false;

        /// <summary>
        /// The types a call this expression makes was declared with, or null where none are recorded.
        /// </summary>
        /// <remarks>
        /// Asked of the call a named function reference stands for, so that the item carries the types the
        /// stylesheet wrote for the function. Only a call to an <c>xsl:function</c> answers.
        /// </remarks>
        internal virtual XdmFunctionSignature? DeclaredSignature => null;

        /// <summary>
        /// Tells this expression that its value is the whole answer of the function body it ends, so that a
        /// call it makes can be made in place of that body's own invocation rather than beneath it.
        /// </summary>
        /// <remarks>
        /// Only a call has anything to do with the news, and only what hands a value on untouched — a
        /// conditional, a <c>let</c> — passes it along. Anything that reads the value stops it here, since a
        /// value read is not one that can be handed back for a caller to produce.
        /// </remarks>
        internal virtual void MarkTailPosition()
        {
        }

        /// <summary>
        /// Whether this expression's value is always a boolean, so that as a predicate it can never be the
        /// number that makes a predicate positional.
        /// </summary>
        /// <remarks>
        /// Conservative: an expression that does not know says no, and is then treated as one that might
        /// answer with a number.
        /// </remarks>
        internal virtual bool IsBooleanValued => false;

        /// <summary>Whether this expression itself reads the focus position or size.</summary>
        internal virtual bool ReadsFocusPosition => false;

        /// <summary>
        /// Whether an expression, or anything inside it, reads the focus position or size — so that its
        /// value depends on where the context item stands among its companions, not on the item alone.
        /// </summary>
        /// <remarks>
        /// Conservative about focus: a sub-expression that shifts the focus for its operand, such as a
        /// simple map, is walked into all the same, so an inner <c>position()</c> that is about a different
        /// focus still counts. The cost of that is only a rewrite forgone.
        /// </remarks>
        internal static bool DependsOnFocusPosition(Expr expression)
        {
            if (expression.ReadsFocusPosition)
            {
                return true;
            }

            foreach (Expr child in expression.Children)
            {
                if (DependsOnFocusPosition(child))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Emits IL that leaves this expression's value on the evaluation stack.
        /// </summary>
        /// <remarks>
        /// The default emits a call back into <see cref="Evaluate"/> on this very node. That keeps generated
        /// code correct for every expression from the start, so each node type can be given a real
        /// implementation — and checked against the interpreter — independently, instead of the whole backend
        /// having to be finished before any of it can be tested.
        /// </remarks>
        /// <param name="context">The compilation context to emit into.</param>
        internal virtual void Emit(EmitContext context)
        {
            context.EmitInterpreterFallback(this);
        }

        /// <summary>
        /// Emits IL leaving this expression's value on the stack as a raw <see cref="double"/>.
        /// </summary>
        /// <remarks>
        /// Lets arithmetic keep its intermediates as plain numbers rather than wrapping each one in an
        /// <see cref="XPathValue"/> only for the next operator to unwrap it again.
        /// </remarks>
        /// <param name="context">The compilation context to emit into.</param>
        internal virtual void EmitAsNumber(EmitContext context)
        {
            Emit(context);
            context.IL.Call(EmitHelpers.ToNumberMethod);
        }

        /// <summary>
        /// Emits IL leaving this expression's value on the stack as a raw <see cref="bool"/>.
        /// </summary>
        /// <param name="context">The compilation context to emit into.</param>
        internal virtual void EmitAsBoolean(EmitContext context)
        {
            Emit(context);
            context.IL.Call(EmitHelpers.ToBooleanMethod);
        }
    }

    /// <summary>A string literal.</summary>
    public sealed class StringLiteralExpr : Expr
    {
        private readonly XPathValue m_value;

        /// <summary>Initializes a string literal.</summary>
        /// <param name="value">The literal text.</param>
        public StringLiteralExpr(string value)
        {
            m_value = XPathValue.FromString(value);
            m_text = value;
        }

        private readonly string m_text;

        /// <summary>Gets the literal's text, for compile-time uses such as resolving a key name.</summary>
        public string Value => m_text;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context) => m_value;

        /// <inheritdoc/>
        internal override void Emit(EmitContext context)
        {
            context.IL.LoadString(m_text);
            context.IL.Call(EmitHelpers.FromString);
        }
    }

    /// <summary>A numeric literal.</summary>
    public sealed class NumberLiteralExpr : Expr
    {
        private readonly XPathValue m_value;

        /// <summary>Initializes a numeric literal.</summary>
        /// <param name="value">The literal value.</param>
        public NumberLiteralExpr(double value)
        {
            m_value = XPathValue.FromNumber(value);
            m_number = value;
        }

        private readonly double m_number;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context) => m_value;

        /// <inheritdoc/>
        internal override void Emit(EmitContext context)
        {
            context.IL.LoadDouble(m_number);
            context.IL.Call(EmitHelpers.FromNumber);
        }

        /// <inheritdoc/>
        internal override void EmitAsNumber(EmitContext context)
        {
            // The whole point of the typed path: a literal in arithmetic is just a constant.
            context.IL.LoadDouble(m_number);
        }

        /// <inheritdoc/>
        internal override void EmitAsBoolean(EmitContext context)
        {
            context.IL.LoadInt(m_number != 0.0 && !double.IsNaN(m_number) ? 1 : 0);
        }
    }

    /// <summary>A boolean constant.</summary>
    /// <remarks>
    /// XPath has no boolean literal — <c>true()</c> and <c>false()</c> are function calls — so this exists for
    /// expressions that fold to a constant during compilation, such as <c>element-available()</c> asked about
    /// a name written out in full.
    /// </remarks>
    public sealed class BooleanLiteralExpr : Expr
    {
        private readonly XPathValue m_value;
        private readonly bool m_boolean;

        /// <summary>Initializes a boolean constant.</summary>
        /// <param name="value">The constant value.</param>
        public BooleanLiteralExpr(bool value)
        {
            m_value = XPathValue.FromBoolean(value);
            m_boolean = value;
        }

        /// <summary>Gets the constant's value, for compile-time uses.</summary>
        public bool Value => m_boolean;

        /// <inheritdoc/>
        internal override bool IsBooleanValued => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context) => m_value;

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context) => m_boolean;

        /// <inheritdoc/>
        internal override void Emit(EmitContext context)
        {
            context.IL.LoadInt(m_boolean ? 1 : 0);
            context.IL.Call(EmitHelpers.FromBoolean);
        }

        /// <inheritdoc/>
        internal override void EmitAsBoolean(EmitContext context)
        {
            context.IL.LoadInt(m_boolean ? 1 : 0);
        }
    }

    /// <summary>
    /// A value already known when the expression was compiled, standing where the expression that produced
    /// it was written.
    /// </summary>
    /// <remarks>
    /// What a reference to an XSLT 3.0 <c>static</c> variable compiles to. A static variable is settled
    /// before the stylesheet it belongs to has finished being read — that is what lets a <c>use-when</c> ask
    /// about one — so there is no slot to hold it and nothing left to evaluate. Unlike the three literal
    /// nodes above it carries a whole <see cref="XPathValue"/>, a static variable being able to hold a
    /// sequence as easily as a string.
    /// </remarks>
    public sealed class ConstantExpr : Expr
    {
        private readonly XPathValue m_value;

        /// <summary>Initializes a constant.</summary>
        /// <param name="value">The value it stands for.</param>
        public ConstantExpr(XPathValue value)
        {
            m_value = value;
        }

        /// <summary>Gets the value, for compile-time uses.</summary>
        public XPathValue Value => m_value;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context) => m_value;
    }

    /// <summary>
    /// A reference to a variable or parameter, resolved to a slot at compile time so that no name lookup
    /// happens during evaluation.
    /// </summary>
    public sealed class VariableReferenceExpr : Expr
    {
        private readonly int m_slot;
        private readonly bool m_isGlobal;

        /// <summary>Initializes a variable reference.</summary>
        /// <param name="slot">The slot the variable occupies.</param>
        /// <param name="isGlobal">Whether the slot is in global storage rather than the current frame.</param>
        /// <param name="name">The variable name as written, retained for diagnostics.</param>
        public VariableReferenceExpr(int slot, bool isGlobal, string name)
        {
            m_slot = slot;
            m_isGlobal = isGlobal;
            Name = name;
        }

        /// <summary>Gets the variable name as it appeared in the stylesheet.</summary>
        public string Name { get; }

        /// <summary>
        /// Always true: what a variable holds is not known where it is read, and a variable is the usual way
        /// a loaded document travels from a <c>document()</c> call to the expression that uses it.
        /// </summary>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            if (!m_isGlobal)
            {
                return context.Locals[context.FrameBase + m_slot];
            }

            // Globals may be referenced before their declaration, so the first read forces evaluation.
            context.Runtime?.EnsureGlobal(m_slot, ref context);
            return context.Globals[m_slot];
        }

        /// <inheritdoc/>
        public override string ToString() => "$" + Name;
    }

    /// <summary>
    /// A leading <c>+</c>, which XPath 2.0 has and 1.0 does not.
    /// </summary>
    /// <remarks>
    /// It looks like nothing and is not: <c>+$x</c> takes a number and gives it back with its type intact,
    /// which means it also refuses what is not one. <c>+'a string'</c> is a type error, the same as
    /// <c>-'a string'</c>, rather than a string that happens to have survived being signed.
    /// </remarks>
    public sealed class UnaryPlusExpr : Expr
    {
        private readonly Expr m_operand;

        /// <summary>Initializes a unary plus.</summary>
        /// <param name="operand">The operand, which must be a number.</param>
        public UnaryPlusExpr(Expr operand)
        {
            m_operand = operand;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_operand };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return XdmArithmetic.RequireNumericOperand(m_operand.Evaluate(ref context), "+");
        }

        /// <inheritdoc/>
        public override string ToString() => "+" + m_operand;
    }

    /// <summary>
    /// The context item, which <c>.</c> denotes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A primary expression rather than a step. XPath 1.0 defines <c>.</c> as <c>self::node()</c>, which is
    /// the same thing for as long as the context item is always a node. XPath 2.0 lets it be an atomic value,
    /// and then the two part company: <c>(1 to 25)[. ge 10]</c> has to see the integer it is filtering, and a
    /// node-set is not something an integer can be put in.
    /// </para>
    /// <para>
    /// A node context item still yields a node-set of one, so nothing about the 1.0 half changes — including
    /// the comparison fast paths, which <see cref="ReturnsNodeSet"/> keeps open exactly where the language
    /// guarantees a node.
    /// </para>
    /// </remarks>
    public sealed class ContextItemExpr : Expr
    {
        private readonly XsltVersion m_version;


        /// <summary>Initializes a context-item reference.</summary>
        /// <param name="version">
        /// The version this was compiled against, which decides whether the item is known to be a node.
        /// </param>
        public ContextItemExpr(XsltVersion version)
        {
            m_version = version;
        }

        /// <summary>
        /// True under 1.0, where the context item is a node by definition, and unknown under 2.0, where it
        /// may be an atomic value.
        /// </summary>
        public override bool ReturnsNodeSet => m_version.IsBackwardsCompatible;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return context.HasAtomicItem
                ? context.AtomicItem
                : XPathValue.FromNodeSet(NodeSet.Singleton(context.Tree, RequireNode(ref context)));
        }

        /// <inheritdoc/>
        public override XdmTree EvaluateNodes(ref DynamicContext context, List<int> output)
        {
            if (context.HasAtomicItem)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    "Nodes were required here, and the context item is an atomic value.");
            }

            output.Add(RequireNode(ref context));
            return context.Tree;
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            // A node is always true, being one node rather than none; an atomic item has whatever effective
            // boolean value its type gives it, and some types give it none.
            return context.HasAtomicItem ? context.AtomicItem.ToBoolean() : RequireNode(ref context) >= 0;
        }

        /// <summary>Returns the context node, there being one.</summary>
        /// <exception cref="XsltException">There is no context item at all.</exception>
        private static int RequireNode(ref DynamicContext context)
        {
            return context.Node >= 0
                ? context.Node
                : throw XsltErrors.Error(
                    XsltErrorCode.XPDY0002,
                    "'.' names the context item, and there is none here — this expression was evaluated "
                    + "outside any focus.");
        }

        /// <inheritdoc/>
        public override string ToString() => ".";
    }

    /// <summary>The binary operators of XPath 1.0.</summary>
    public enum BinaryOperator : byte
    {
        /// <summary>Logical disjunction, with short-circuit evaluation.</summary>
        Or,

        /// <summary>Logical conjunction, with short-circuit evaluation.</summary>
        And,

        /// <summary>Equality.</summary>
        Equal,

        /// <summary>Inequality.</summary>
        NotEqual,

        /// <summary>Numeric less-than.</summary>
        LessThan,

        /// <summary>Numeric less-than-or-equal.</summary>
        LessThanOrEqual,

        /// <summary>Numeric greater-than.</summary>
        GreaterThan,

        /// <summary>Numeric greater-than-or-equal.</summary>
        GreaterThanOrEqual,

        /// <summary>Addition.</summary>
        Add,

        /// <summary>Subtraction.</summary>
        Subtract,

        /// <summary>Multiplication.</summary>
        Multiply,

        /// <summary>Division, written <c>div</c>. Division by zero yields an infinity, not an error.</summary>
        Divide,

        /// <summary>
        /// The XPath 2.0 <c>idiv</c> operator, which divides and truncates towards zero. Needed because
        /// <c>div</c> on two integers yields a decimal rather than an integer.
        /// </summary>
        IntegerDivide,

        /// <summary>Remainder, written <c>mod</c>, taking the sign of the dividend.</summary>
        Modulo,
    }

    /// <summary>A binary operation.</summary>
    public sealed class BinaryExpr : Expr
    {
        private readonly BinaryOperator m_operator;
        private readonly Expr m_left;
        private readonly Expr m_right;

        /// <summary>Initializes a binary operation.</summary>
        /// <param name="op">The operator.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        public BinaryExpr(BinaryOperator op, Expr left, Expr right)
            : this(op, left, right, XsltVersion.V10)
        {
        }

        /// <summary>Initializes a binary expression compiled against a particular version of XPath.</summary>
        /// <param name="op">The operator.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="version">
        /// The XSLT version in force where the expression was written, which decides how the relational
        /// operators read their operands.
        /// </param>
        public BinaryExpr(BinaryOperator op, Expr left, Expr right, XsltVersion version)
        {
            m_operator = op;
            m_left = left;
            m_right = right;
            m_version = version;
        }

        private readonly XsltVersion m_version;

        /// <summary>
        /// What the comparison was written among: the collation and the namespaces.
        /// </summary>
        /// <remarks>
        /// Null where the expression was built without a static context to ask, which is every expression
        /// this engine builds for itself. The emitted form needs nothing of it: a comparison under XPath 2.0
        /// rules already falls back to the interpreter, and one under 1.0 rules has no collation to read.
        /// </remarks>
        internal ComparisonContext? Comparing { get; init; }

        /// <summary>
        /// Whether this comparison may read one operand's nodes directly, without building a node-set.
        /// </summary>
        private bool CanTakeNodesDirectly =>
            m_version.IsBackwardsCompatible
            && IsComparison(m_operator)
            && (m_left.ReturnsNodeSet || m_right.ReturnsNodeSet)
            && !m_left.MaySpanDocuments && !m_right.MaySpanDocuments;

        /// <summary>
        /// Whether, under 2.0 rules, one operand is statically a node-set and the other is not, so that the
        /// nodes can be atomized one at a time against the other value rather than collected first.
        /// </summary>
        private bool CanTakeNodesTyped =>
            !m_version.IsBackwardsCompatible
            && IsComparison(m_operator)
            && m_left.ReturnsNodeSet != m_right.ReturnsNodeSet
            && !m_left.MaySpanDocuments && !m_right.MaySpanDocuments;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_left, m_right };

        /// <summary>
        /// Whether the operator yields a boolean — a comparison or a logical operator — rather than a number.
        /// </summary>
        /// <remarks>
        /// Read by a pattern deciding whether a predicate could select by position: one that yields a
        /// boolean and reads no position cannot, and need not have the siblings counted for it.
        /// </remarks>
        internal override bool IsBooleanValued => m_operator is BinaryOperator.Or
            or BinaryOperator.And
            or BinaryOperator.Equal
            or BinaryOperator.NotEqual
            or BinaryOperator.LessThan
            or BinaryOperator.LessThanOrEqual
            or BinaryOperator.GreaterThan
            or BinaryOperator.GreaterThanOrEqual;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            switch (m_operator)
            {
                case BinaryOperator.Or:
                    return XPathValue.FromBoolean(
                        m_left.Evaluate(ref context).ToBoolean() || m_right.Evaluate(ref context).ToBoolean());

                case BinaryOperator.And:
                    return XPathValue.FromBoolean(
                        m_left.Evaluate(ref context).ToBoolean() && m_right.Evaluate(ref context).ToBoolean());
            }

            // Comparisons where an operand is statically a node-set are the hottest expressions in most
            // stylesheets, because they appear in predicates evaluated once per candidate node. Taking the
            // node list directly avoids allocating a NodeSet for every one of those evaluations. It reads the
            // nodes under 1.0 rules, so 2.0 goes the general way instead — the conversions there depend on
            // what is on the other side, which these paths have already decided.
            if (CanTakeNodesDirectly)
            {
                return XPathValue.FromBoolean(CompareWithNodeOperand(ref context));
            }

            if (CanTakeNodesTyped)
            {
                return XPathValue.FromBoolean(CompareTypedWithNodeOperand(ref context));
            }

            XPathValue left = m_left.Evaluate(ref context);
            XPathValue right = m_right.Evaluate(ref context);

            switch (m_operator)
            {
                case BinaryOperator.Equal:
                case BinaryOperator.NotEqual:
                case BinaryOperator.LessThan:
                case BinaryOperator.LessThanOrEqual:
                case BinaryOperator.GreaterThan:
                case BinaryOperator.GreaterThanOrEqual:
                    return XPathValue.FromBoolean(
                        XPathComparison.General(left, right, m_operator, m_version, Comparing));

                case BinaryOperator.Add:
                case BinaryOperator.Subtract:
                case BinaryOperator.Multiply:
                case BinaryOperator.Divide:
                case BinaryOperator.IntegerDivide:
                case BinaryOperator.Modulo:
                    // XPath 1.0 has one numeric type and nothing to decide; 2.0 promotes the operands and the
                    // result takes the type that came out of it.
                    return m_version.IsBackwardsCompatible
                        ? XPathValue.FromNumber(BackwardsCompatibleArithmetic(m_operator, left, right))
                        : XdmArithmetic.Apply(m_operator, left, right);

                default:
                    return XPathValue.FromNumber(left.ToNumber() % right.ToNumber());
            }
        }

        private static double BackwardsCompatibleArithmetic(BinaryOperator op, XPathValue left, XPathValue right)
        {
            // One number apiece, from the first item of whatever each side gave: XPath 1.0 arithmetic took
            // number() of a node-set, which is number() of its first node, and the compatibility mode keeps
            // that where a 2.0 expression now yields a sequence.
            double a = XdmSequence.FirstItem(left).ToNumber();
            double b = XdmSequence.FirstItem(right).ToNumber();

            return op switch
            {
                BinaryOperator.Add => a + b,
                BinaryOperator.Subtract => a - b,
                BinaryOperator.Multiply => a * b,
                BinaryOperator.Divide => a / b,
                BinaryOperator.IntegerDivide => Math.Truncate(a / b),
                _ => a % b,
            };
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            return m_operator switch
            {
                BinaryOperator.Or => m_left.EvaluateAsBoolean(ref context) || m_right.EvaluateAsBoolean(ref context),
                BinaryOperator.And => m_left.EvaluateAsBoolean(ref context) && m_right.EvaluateAsBoolean(ref context),
                _ when IsComparison(m_operator) && (m_left.ReturnsNodeSet || m_right.ReturnsNodeSet)
                    && !m_left.MaySpanDocuments && !m_right.MaySpanDocuments =>
                    CompareWithNodeOperand(ref context),
                _ => Evaluate(ref context).ToBoolean(),
            };
        }

        private static bool IsComparison(BinaryOperator op)
        {
            return op is BinaryOperator.Equal
                or BinaryOperator.NotEqual
                or BinaryOperator.LessThan
                or BinaryOperator.LessThanOrEqual
                or BinaryOperator.GreaterThan
                or BinaryOperator.GreaterThanOrEqual;
        }

        private bool CompareWithNodeOperand(ref DynamicContext context)
        {
            List<int> leftNodes = NodeListPool.Rent();

            try
            {
                if (!m_right.ReturnsNodeSet)
                {
                    XdmTree tree = m_left.EvaluateNodes(ref context, leftNodes);
                    XPathValue other = m_right.Evaluate(ref context);

                    // A variable can still turn out to hold a node-set, in which case fall back.
                    return other.Kind == XPathValueKind.NodeSet
                        ? CompareAgainstNodeSet(tree, leftNodes, other.AsNodeSet(), nodesOnLeft: true)
                        : XPathComparison.NodesVersusValue(tree, leftNodes, other, true, m_operator);
                }

                if (!m_left.ReturnsNodeSet)
                {
                    XdmTree tree = m_right.EvaluateNodes(ref context, leftNodes);
                    XPathValue other = m_left.Evaluate(ref context);

                    return other.Kind == XPathValueKind.NodeSet
                        ? CompareAgainstNodeSet(tree, leftNodes, other.AsNodeSet(), nodesOnLeft: false)
                        : XPathComparison.NodesVersusValue(tree, leftNodes, other, false, m_operator);
                }

                List<int> rightNodes = NodeListPool.Rent();
                try
                {
                    XdmTree leftTree = m_left.EvaluateNodes(ref context, leftNodes);
                    XdmTree rightTree = m_right.EvaluateNodes(ref context, rightNodes);
                    return XPathComparison.NodesVersusNodes(
                        leftTree, leftNodes, rightTree, rightNodes, m_operator);
                }
                finally
                {
                    NodeListPool.Return(rightNodes);
                }
            }
            finally
            {
                NodeListPool.Return(leftNodes);
            }
        }

        /// <inheritdoc/>
        /// <summary>
        /// A 2.0 general comparison between a node-set and one atomic value, made without building
        /// anything: the nodes are read into a pooled list and each is atomized as it is compared.
        /// </summary>
        /// <remarks>
        /// The general path atomizes both sides into lists of their own and asks every pair, which for the
        /// commonest comparison of all — a child element against a literal, once per candidate node of a
        /// predicate — is a node-set, its array, two lists and the pairs, to answer one string compare. The
        /// atomized values and the pairs asked are the same here, in the same order, so what changes is
        /// what is allocated and nothing that is answered. A value that is not one atomic item — a
        /// sequence, or a node-set a variable turned out to hold — goes the general way, which knows how
        /// to atomize it.
        /// </remarks>
        private bool CompareTypedWithNodeOperand(ref DynamicContext context)
        {
            bool nodesOnLeft = m_left.ReturnsNodeSet;
            List<int> nodes = NodeListPool.Rent();

            try
            {
                XdmTree tree;
                XPathValue other;

                if (nodesOnLeft)
                {
                    tree = m_left.EvaluateNodes(ref context, nodes);
                    other = m_right.Evaluate(ref context);
                }
                else
                {
                    other = m_left.Evaluate(ref context);
                    tree = m_right.EvaluateNodes(ref context, nodes);
                }

                if (other.Kind is not (XPathValueKind.Boolean or XPathValueKind.Number or XPathValueKind.String))
                {
                    XPathValue set = XPathValue.FromNodeSet(NodeSet.FromOrderedNodes(tree, nodes));

                    return nodesOnLeft
                        ? XPathComparison.General(set, other, m_operator, m_version, Comparing)
                        : XPathComparison.General(other, set, m_operator, m_version, Comparing);
                }

                bool typed = tree.HasTypeAnnotations;

                for (int i = 0; i < nodes.Count; i++)
                {
                    // In a validated tree a node's typed value is what its type says, and may be several
                    // values, each of which is asked; in any other it is the text, untyped.
                    XPathValue atom = typed
                        ? XdmSequence.TypedValueOf(tree, nodes[i])
                        : XPathValue.FromUntypedAtomic(tree.StringValueOf(nodes[i]));

                    if (atom.Kind == XPathValueKind.Sequence)
                    {
                        XdmSequence several = atom.AsSequence();

                        for (int j = 0; j < several.Count; j++)
                        {
                            if (Holds(several[j], other, nodesOnLeft))
                            {
                                return true;
                            }
                        }

                        continue;
                    }

                    if (Holds(atom, other, nodesOnLeft))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                NodeListPool.Return(nodes);
            }
        }

        /// <summary>Whether one atomized node compares as asked against the other operand.</summary>
        private bool Holds(XPathValue atom, XPathValue other, bool nodesOnLeft)
        {
            return nodesOnLeft
                ? XdmComparison.Pair(atom, other, m_operator, Comparing)
                : XdmComparison.Pair(other, atom, m_operator, Comparing);
        }

        internal override void Emit(EmitContext context)
        {
            if (CanTakeNodesDirectly)
            {
                if (TryEmitNodeComparison(context))
                {
                    context.IL.Call(EmitHelpers.FromBoolean);
                    return;
                }

                // Any other node-set comparison has a specialised interpreter path that already avoids
                // materialising a NodeSet; emitting the generic form would be a step backwards.
                context.EmitInterpreterFallback(this);
                return;
            }

            switch (m_operator)
            {
                case BinaryOperator.Or:
                case BinaryOperator.And:
                    EmitAsBoolean(context);
                    context.IL.Call(EmitHelpers.FromBoolean);
                    return;

                case BinaryOperator.Equal:
                case BinaryOperator.NotEqual:
                case BinaryOperator.LessThan:
                case BinaryOperator.LessThanOrEqual:
                case BinaryOperator.GreaterThan:
                case BinaryOperator.GreaterThanOrEqual:
                    EmitComparison(context);
                    context.IL.Call(EmitHelpers.FromBoolean);
                    return;

                default:
                    if (!m_version.IsBackwardsCompatible)
                    {
                        // Under XPath 2.0 the operands' types decide the result's: an integer, a decimal, a
                        // date, a duration — a double is only one of the answers, and emitting one would be
                        // giving the wrong answer quickly. The interpreter already does that dispatch.
                        context.EmitInterpreterFallback(this);
                        return;
                    }

                    EmitAsNumber(context);
                    context.IL.Call(EmitHelpers.FromNumber);
                    return;
            }
        }

        /// <inheritdoc/>
        internal override void EmitAsNumber(EmitContext context)
        {
            System.Reflection.Emit.OpCode opCode;

            if (!m_version.IsBackwardsCompatible && !IsComparison(m_operator)
                && m_operator is not (BinaryOperator.Or or BinaryOperator.And))
            {
                // Same reasoning as Emit: a double is not what 2.0 arithmetic produces, and an integer wider
                // than a double can hold would lose digits on the way through one.
                context.EmitInterpreterFallback(this);
                context.IL.Call(EmitHelpers.ToNumberMethod);
                return;
            }

            switch (m_operator)
            {
                case BinaryOperator.Add:
                    opCode = System.Reflection.Emit.OpCodes.Add;
                    break;

                case BinaryOperator.Subtract:
                    opCode = System.Reflection.Emit.OpCodes.Sub;
                    break;

                case BinaryOperator.Multiply:
                    opCode = System.Reflection.Emit.OpCodes.Mul;
                    break;

                case BinaryOperator.Divide:
                    // IEEE division, so dividing by zero yields an infinity exactly as XPath requires.
                    opCode = System.Reflection.Emit.OpCodes.Div;
                    break;

                case BinaryOperator.Modulo:
                    opCode = System.Reflection.Emit.OpCodes.Rem;
                    break;

                default:
                    // Deliberately not base.EmitAsNumber: that routes through Emit, which sends arithmetic
                    // straight back here. Going to the interpreter instead cannot loop.
                    context.EmitInterpreterFallback(this);
                    context.IL.Call(EmitHelpers.ToNumberMethod);
                    return;
            }

            // Through the value rather than straight to a double, because 1.0 arithmetic takes the first
            // item of an operand that turns out to be a sequence — and whether one is cannot be known from
            // here. The interpreter does the same, and the two backends have to agree.
            EmitOperand(m_left, context);
            EmitOperand(m_right, context);
            context.IL.BinaryOperation(opCode, m_operator.ToString());
        }

        /// <summary>Emits one operand of backwards compatible arithmetic as a raw double.</summary>
        private static void EmitOperand(Expr operand, EmitContext context)
        {
            operand.Emit(context);
            context.IL.Call(EmitHelpers.ToNumberFirstItemMethod);
        }

        /// <inheritdoc/>
        internal override void EmitAsBoolean(EmitContext context)
        {
            if (m_operator is not (BinaryOperator.Or or BinaryOperator.And))
            {
                if (IsComparison(m_operator))
                {
                    if (!m_left.ReturnsNodeSet && !m_right.ReturnsNodeSet)
                    {
                        EmitComparison(context);
                        return;
                    }

                    if (TryEmitNodeComparison(context))
                    {
                        return;
                    }
                }

                base.EmitAsBoolean(context);
                return;
            }

            // Short-circuit: for 'or', a true left operand skips the right entirely.
            bool isOr = m_operator == BinaryOperator.Or;
            var shortCircuit = context.IL.DefineLabel(isOr ? "or-true" : "and-false");
            var done = context.IL.DefineLabel("logical-done");

            m_left.EmitAsBoolean(context);

            if (isOr)
            {
                context.IL.BranchIfTrue(shortCircuit);
            }
            else
            {
                context.IL.BranchIfFalse(shortCircuit);
            }

            m_right.EmitAsBoolean(context);
            context.IL.Branch(done);

            context.IL.MarkLabel(shortCircuit);
            context.IL.LoadInt(isOr ? 1 : 0);

            context.IL.MarkLabel(done);
        }

        /// <summary>
        /// Emits a comparison where one side is a simple child-axis name test, walking the children inline and
        /// testing them without ever building a node-set.
        /// </summary>
        /// <returns><see langword="false"/> if neither operand has the required shape.</returns>
        private bool TryEmitNodeComparison(EmitContext context)
        {
            bool nodesOnLeft = m_left is PathExpr leftPath && leftPath.TryGetSimpleChildNameStep(out _);
            PathExpr? path = nodesOnLeft ? (PathExpr)m_left : m_right as PathExpr;

            if (path is null || !path.TryGetSimpleChildNameStep(out int nameSlot))
            {
                return false;
            }

            Expr other = nodesOnLeft ? m_right : m_left;

            System.Reflection.Emit.LocalBuilder tree = context.IL.DeclareLocal(typeof(Model.XdmTree));
            System.Reflection.Emit.LocalBuilder list = context.IL.DeclareLocal(typeof(List<int>));

            context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.RentList)));
            context.IL.StoreLocal(list);

            PathExpr.EmitChildNameLoop(context, nameSlot, tree, list);

            context.IL.LoadLocal(tree);
            context.IL.LoadLocal(list);
            other.Emit(context);
            context.IL.LoadInt(nodesOnLeft ? 1 : 0);
            context.IL.LoadInt((int)m_operator);
            context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.CompareNodes)));

            context.IL.LoadLocal(list);
            context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.ReturnList)));

            return true;
        }

        private void EmitComparison(EmitContext context)
        {
            if (!m_version.IsBackwardsCompatible)
            {
                // Same reasoning as 2.0 arithmetic. Which comparison a pair of operands calls for — and
                // whether one is defined at all — depends on both their types, and the conversions an untyped
                // operand goes through depend on the other side. The interpreter already makes that decision;
                // emitting a call per operator would be emitting the 1.0 answer quickly.
                context.EmitInterpreterFallback(this);
                context.IL.Call(EmitHelpers.ToBooleanMethod);
                return;
            }

            m_left.Emit(context);
            m_right.Emit(context);

            switch (m_operator)
            {
                case BinaryOperator.Equal:
                    context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.AreEqual)));
                    return;

                case BinaryOperator.NotEqual:
                    context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.NotEquals)));
                    return;

                default:
                    context.IL.LoadInt((int)m_operator);
                    context.IL.Call(EmitHelpers.Method(nameof(EmitHelpers.Relational)));
                    return;
            }
        }

        private bool CompareAgainstNodeSet(XdmTree tree, List<int> nodes, NodeSet other, bool nodesOnLeft)
        {
            List<int> otherNodes = NodeListPool.Rent();

            try
            {
                for (int i = 0; i < other.Count; i++)
                {
                    otherNodes.Add(other[i]);
                }

                return nodesOnLeft
                    ? XPathComparison.NodesVersusNodes(tree, nodes, other.Tree, otherNodes, m_operator)
                    : XPathComparison.NodesVersusNodes(other.Tree, otherNodes, tree, nodes, m_operator);
            }
            finally
            {
                NodeListPool.Return(otherNodes);
            }
        }
    }

    /// <summary>Arithmetic negation.</summary>
    /// <remarks>
    /// <para>
    /// Negation keeps the operand's type, so <c>-3</c> is an <c>xs:integer</c> and not a double that happens
    /// to hold 3. It matters wherever an integer is what is asked for — <c>-3 to 3</c> is a range, and would
    /// not be if the minus sign turned its lower bound into something else.
    /// </para>
    /// <para>
    /// Backwards compatibility is the exception, and the same one the binary operators make: XPath 1.0 has
    /// one numeric type, so the operand of an arithmetic operator becomes an <c>xs:double</c>. It shows in
    /// the one value a double has and an integer has not — <c>-0</c> is a negative zero there and an
    /// integer zero here, which is a difference <c>1 div -0</c> can see.
    /// </para>
    /// </remarks>
    public sealed class NegateExpr : Expr
    {
        private readonly Expr m_operand;
        private readonly XsltVersion m_version;

        /// <summary>Initializes a negation.</summary>
        /// <param name="operand">The operand to negate.</param>
        public NegateExpr(Expr operand)
            : this(operand, XsltVersion.V10)
        {
        }

        /// <summary>Initializes a negation compiled against a particular version of XPath.</summary>
        /// <param name="operand">The operand to negate.</param>
        /// <param name="version">The version in force where it was written, which decides the result's type.</param>
        public NegateExpr(Expr operand, XsltVersion version)
        {
            m_operand = operand;
            m_version = version;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_operand };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue value = m_operand.Evaluate(ref context);

            // Under 2.0 the operand has to be a number. XPath 1.0 reads anything as one and answers NaN,
            // which turns -'a string' into a value; there is no negative of a string to be that value.
            if (m_version.IsBackwardsCompatible)
            {
                return XPathValue.FromNumber(-XdmSequence.FirstItem(value).ToNumber());
            }

            value = XdmArithmetic.RequireNumericOperand(value, "-");

            if (Xpath2FunctionExpr.IsEmptySequence(value))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            return value.TypeCode switch
            {
                XdmTypeCode.Integer => XPathValue.FromInteger(-value.ToInteger()),
                XdmTypeCode.Decimal => XPathValue.FromDecimal(-value.ToDecimal()),
                XdmTypeCode.Float => XPathValue.FromFloat(-(float)value.ToNumber()),
                _ => XPathValue.FromNumber(-value.ToNumber()),
            };
        }

        /// <inheritdoc/>
        internal override void Emit(EmitContext context)
        {
            if (!m_version.IsBackwardsCompatible)
            {
                // Same reasoning as 2.0 arithmetic: the result's type follows the operand's, and a double is
                // only one of the answers.
                context.EmitInterpreterFallback(this);
                return;
            }

            EmitAsNumber(context);
            context.IL.Call(EmitHelpers.FromNumber);
        }

        /// <inheritdoc/>
        internal override void EmitAsNumber(EmitContext context)
        {
            m_operand.EmitAsNumber(context);
            context.IL.UnaryOperation(System.Reflection.Emit.OpCodes.Neg, "neg");
        }
    }

    /// <summary>The union of two node-sets, written <c>|</c>.</summary>
    public sealed class UnionExpr : Expr
    {
        private readonly Expr m_left;
        private readonly Expr m_right;

        /// <summary>Initializes a union.</summary>
        /// <param name="left">The left operand, which must evaluate to a node-set.</param>
        /// <param name="right">The right operand, which must evaluate to a node-set.</param>
        public UnionExpr(Expr left, Expr right)
        {
            m_left = left;
            m_right = right;
        }

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => true;

        /// <inheritdoc/>
        // A union can only reach a second document if one of its operands can. Anything that is single-tree
        // but not the context tree — a loaded document, a result tree fragment — arrives through a variable or
        // through document(), and both of those say so.
        public override bool MaySpanDocuments => m_left.MaySpanDocuments || m_right.MaySpanDocuments;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_left, m_right };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            // Read through NodeSet.Of rather than AsNodeSet, because from XPath 2.0 a sequence of nodes is
            // as good an operand as a node-set: a variable holding elements is a sequence, and refusing
            // '$nodes | ()' for that reason refuses the ordinary way of asking how many distinct nodes are
            // in one. What is still refused is an operand holding something that is not a node.
            NodeSet left = NodeSet.Of(
                m_left.Evaluate(ref context), context.Tree, XsltErrorCode.XPTY0004, "the left of a union");

            NodeSet right = NodeSet.Of(
                m_right.Evaluate(ref context), context.Tree, XsltErrorCode.XPTY0004, "the right of a union");

            // The result starts in the left operand's tree, so that a union of two sets from one document
            // never records per-node trees. An empty left side would name the wrong tree, hence the check.
            NodeSet result = new NodeSet(
                left.Count > 0 ? left.Tree : right.Count > 0 ? right.Tree : context.Tree,
                left.Count + right.Count);

            result.AddRange(left);
            result.AddRange(right);
            result.SortAndDeduplicate();
            return XPathValue.FromNodeSet(result);
        }
    }

    /// <summary>
    /// A primary expression followed by predicates, such as <c>$nodes[2]</c> or <c>(a|b)[@x]</c>.
    /// </summary>
    public sealed class FilterExpr : Expr
    {
        private readonly Expr m_primary;
        private readonly Expr[] m_predicates;
        private readonly XsltVersion m_version;

        /// <summary>Initializes a filter expression.</summary>
        /// <param name="primary">The expression producing the node-set to filter.</param>
        /// <param name="predicates">The predicates, applied left to right.</param>
        /// <param name="version">The version this was compiled against.</param>
        public FilterExpr(Expr primary, Expr[] predicates, XsltVersion version)
        {
            m_primary = primary;
            m_predicates = predicates;
            m_version = version;
        }

        /// <summary>
        /// True under 1.0, where only a node-set can be filtered, and otherwise whatever the primary is
        /// known to produce.
        /// </summary>
        /// <remarks>
        /// A filter yields the kind of thing it filtered, so <c>(1, 2, 3)[. gt 1]</c> is a sequence and not a
        /// node-set. Claiming otherwise sends every caller that trusts this down a route that then asks the
        /// result for nodes it does not have.
        /// </remarks>
        public override bool ReturnsNodeSet => m_version.IsBackwardsCompatible || m_primary.ReturnsNodeSet;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => m_primary.MaySpanDocuments;

        /// <summary>Gets this filter's predicates, which may be rewritten in place.</summary>
        internal Expr[] Predicates => m_predicates;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_primary };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue primary = m_primary.Evaluate(ref context);

            // A sequence is filtered as a sequence: its items may be atomic, or nodes from several trees, and
            // neither survives being turned into a node-set. So is a lone atomic value, which is a sequence of
            // one — '(0)[1]' filters the integer, and asking for a node-set would only refuse it. Only a value
            // that already is a node-set takes the path below, which is the one every 1.0 expression goes down.
            if (primary.Kind is not XPathValueKind.NodeSet)
            {
                return FilterSequence(XdmSequence.Items(primary), ref context);
            }

            NodeSet nodes = primary.AsNodeSet();

            if (nodes.SpansDocuments)
            {
                return XPathValue.FromNodeSet(FilterAcrossDocuments(ref context, nodes));
            }

            DynamicContext walk = ReferenceEquals(nodes.Tree, context.Tree) || nodes.Count == 0
                ? context
                : context.SwitchTree(nodes.Tree, context.Node);

            List<int> current = new List<int>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                current.Add(nodes[i]);
            }

            foreach (Expr predicate in m_predicates)
            {
                current = PredicateFilter.Apply(predicate, current, ref walk);
            }

            NodeSet result = new NodeSet(walk.Tree, current.Count);
            foreach (int node in current)
            {
                result.Add(node);
            }

            result.SortAndDeduplicate();
            return XPathValue.FromNodeSet(result);
        }

        /// <summary>
        /// Filters a sequence, which may hold atomic values and nodes from different trees together.
        /// </summary>
        /// <remarks>
        /// Order and duplicates are kept, unlike the node-set path, which sorts into document order and
        /// removes repeats because XPath 1.0 requires it of every node-set. A sequence is not a node-set:
        /// <c>($b, $a, $a)</c> is three items in that order and stays so.
        /// </remarks>
        private XPathValue FilterSequence(List<XPathValue> items, ref DynamicContext context)
        {
            foreach (Expr predicate in m_predicates)
            {
                List<XPathValue> survivors = new List<XPathValue>(items.Count);

                for (int i = 0; i < items.Count; i++)
                {
                    DynamicContext inner;

                    // An item's own tree becomes the context for the predicate, so a path inside one starts
                    // where the item is rather than where the expression was written. An atomic item has no
                    // tree and is carried as itself, which is what '. ge 10' reads.
                    if (items[i].Kind == XPathValueKind.Node)
                    {
                        inner = context.SwitchTree(items[i].NodeTree, items[i].NodeId);
                    }
                    else
                    {
                        inner = context.WithAtomicItem(items[i]);
                    }

                    inner.Position = i + 1;
                    inner.Size = items.Count;

                    if (PredicateFilter.Holds(predicate, ref inner))
                    {
                        survivors.Add(items[i]);
                    }
                }

                items = survivors;
            }

            return XdmSequence.Concatenate(items);
        }

        /// <summary>
        /// Filters a set whose nodes come from more than one document, one document at a time.
        /// </summary>
        /// <remarks>
        /// A predicate has to be evaluated against the node's own tree, and <c>position()</c> counts along the
        /// sequence as a whole rather than restarting per document — so the positions are established first
        /// and each node is then tested in its own context.
        /// </remarks>
        private NodeSet FilterAcrossDocuments(ref DynamicContext context, NodeSet nodes)
        {
            List<(int Node, XdmTree Tree)> current = new List<(int, XdmTree)>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                current.Add((nodes[i], nodes.TreeAt(i)));
            }

            foreach (Expr predicate in m_predicates)
            {
                List<(int Node, XdmTree Tree)> survivors = new List<(int, XdmTree)>(current.Count);
                int size = current.Count;

                for (int i = 0; i < current.Count; i++)
                {
                    DynamicContext inner = context.SwitchTree(current[i].Tree, current[i].Node);
                    inner.Position = i + 1;
                    inner.Size = size;

                    if (PredicateFilter.Holds(predicate, ref inner))
                    {
                        survivors.Add(current[i]);
                    }
                }

                current = survivors;
            }

            NodeSet result = new NodeSet(current.Count > 0 ? current[0].Tree : context.Tree, current.Count);
            foreach ((int node, XdmTree tree) in current)
            {
                result.Add(tree, node);
            }

            result.SortAndDeduplicate();
            return result;
        }
    }

    /// <summary>Applies a predicate to a candidate sequence.</summary>
    internal static class PredicateFilter
    {
        /// <summary>
        /// Filters a sequence by a predicate.
        /// </summary>
        /// <remarks>
        /// A predicate whose value is a number selects by position; any other value is taken as a boolean.
        /// <c>position()</c> counts along the sequence as supplied, so callers must pass reverse axes in
        /// reverse document order.
        /// </remarks>
        /// <param name="predicate">The predicate expression.</param>
        /// <param name="candidates">The sequence to filter, in the order positions are counted.</param>
        /// <param name="context">The context the predicate is evaluated against.</param>
        /// <returns>The surviving nodes, in their original order.</returns>
        public static List<int> Apply(Expr predicate, List<int> candidates, ref DynamicContext context)
        {
            List<int> survivors = new List<int>(candidates.Count);
            int size = candidates.Count;

            for (int i = 0; i < candidates.Count; i++)
            {
                DynamicContext inner = context;
                inner.Node = candidates[i];
                inner.Position = i + 1;
                inner.Size = size;

                if (Holds(predicate, ref inner))
                {
                    survivors.Add(candidates[i]);
                }
            }

            return survivors;
        }

        /// <summary>
        /// Evaluates one predicate against a context already positioned on the candidate, and says whether it
        /// keeps that candidate.
        /// </summary>
        /// <remarks>
        /// A numeric predicate selects by position, so the answer depends on
        /// <see cref="DynamicContext.Position"/> having been set by the caller.
        /// </remarks>
        /// <param name="predicate">The predicate expression.</param>
        /// <param name="context">The context, positioned on the candidate.</param>
        public static bool Holds(Expr predicate, ref DynamicContext context)
        {
            XPathValue value = predicate.Evaluate(ref context);

            if (value.Kind == XPathValueKind.Number)
            {
                return value.ToNumber() == context.Position;
            }

            // A sequence of exactly one number is that number — '1 to 1' is one — and selects by position
            // as the number itself would. Any other sequence is read for its effective boolean value.
            if (value.Kind == XPathValueKind.Sequence)
            {
                List<XPathValue> items = XdmSequence.Items(value);

                if (items.Count == 1 && items[0].Kind == XPathValueKind.Number)
                {
                    return items[0].ToNumber() == context.Position;
                }
            }

            return value.ToBoolean();
        }

        /// <summary>
        /// Filters the tail of a list in place, from <paramref name="start"/> onwards, applying each predicate
        /// in turn.
        /// </summary>
        /// <remarks>
        /// A location step evaluates its predicates separately for each origin node, so the caller appends one
        /// origin's results and filters just that range. Compacting in place avoids allocating a list per
        /// origin per predicate, which on a path evaluated once per node dominated the actual work.
        /// </remarks>
        /// <param name="predicates">The predicates, applied left to right.</param>
        /// <param name="nodes">The list to filter.</param>
        /// <param name="start">The index at which this origin's results begin.</param>
        /// <param name="context">The context the predicates are evaluated against.</param>
        public static void ApplyInPlace(
            Expr[] predicates,
            List<int> nodes,
            int start,
            ref DynamicContext context)
        {
            foreach (Expr predicate in predicates)
            {
                int size = nodes.Count - start;
                if (size == 0)
                {
                    return;
                }

                // A predicate that is statically a node-set can never be a positional one, so it can take the
                // boolean route and skip building a value at all.
                bool booleanOnly = predicate.ReturnsNodeSet;

                int write = start;
                for (int i = 0; i < size; i++)
                {
                    int node = nodes[start + i];

                    DynamicContext inner = context;
                    inner.Node = node;
                    inner.Position = i + 1;
                    inner.Size = size;

                    bool keep;
                    if (booleanOnly)
                    {
                        keep = predicate.EvaluateAsBoolean(ref inner);
                    }
                    else
                    {
                        XPathValue value = predicate.Evaluate(ref inner);
                        keep = value.Kind == XPathValueKind.Number
                            ? value.ToNumber() == i + 1
                            : value.ToBoolean();
                    }

                    if (keep)
                    {
                        // write never runs ahead of the read position, so compaction is safe in place.
                        nodes[write++] = node;
                    }
                }

                nodes.RemoveRange(write, nodes.Count - write);
            }
        }
    }
}
