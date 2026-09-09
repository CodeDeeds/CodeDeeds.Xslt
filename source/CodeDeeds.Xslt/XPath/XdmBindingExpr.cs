using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A reference to a variable bound by <c>for</c>, <c>some</c> or <c>every</c>.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="VariableReferenceExpr"/>, which reads a variable the stylesheet declared and
    /// whose slot the compiler assigned. A range variable exists only within the expression that binds it, so
    /// the parser assigns its slot by how deeply the bindings nest and it lives in its own array.
    /// </remarks>
    public sealed class RangeVariableExpr : Expr
    {
        private readonly int m_slot;
        private readonly string m_name;

        /// <summary>Initializes a range variable reference.</summary>
        /// <param name="slot">The slot, which is the depth at which the variable was bound.</param>
        /// <param name="name">The name as written, kept for diagnostics.</param>
        public RangeVariableExpr(int slot, string name)
        {
            m_slot = slot;
            m_name = name;
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue[]? slots = context.RangeVariables;

            return slots is not null && m_slot < slots.Length
                ? slots[m_slot]
                : throw XsltErrors.Error(XsltErrorCode.XPST0008, $"Variable '${m_name}' is not bound.");
        }
    }

    /// <summary>
    /// Shared machinery for the expressions that bind a variable over a sequence.
    /// </summary>
    /// <remarks>
    /// The previous occupant of the slot is saved and put back afterwards. Sibling bindings at the same depth
    /// share a slot, which is harmless, but an expression that binds a variable can be re-entered while it is
    /// running — a <c>for</c> whose return clause calls a template that evaluates the same expression again —
    /// and without the save the outer loop would resume reading the inner one's value.
    /// </remarks>
    public abstract class BindingExpr : Expr
    {
        /// <summary>Initializes a binding expression.</summary>
        /// <param name="slot">The slot the variable occupies while the body runs.</param>
        /// <param name="sequence">The expression supplying the items to bind in turn.</param>
        /// <param name="body">The expression evaluated once per item.</param>
        protected BindingExpr(int slot, Expr sequence, Expr body)
        {
            Slot = slot;
            Sequence = sequence;
            Body = body;
        }

        /// <summary>The slot the variable occupies.</summary>
        protected int Slot { get; }

        /// <summary>The expression supplying the items.</summary>
        protected Expr Sequence { get; }

        /// <summary>The expression evaluated once per item.</summary>
        protected Expr Body { get; }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { Sequence, Body };

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <summary>Makes room for this binding's slot and returns what was in it.</summary>
        protected XPathValue Reserve(ref DynamicContext context)
        {
            if (context.RangeVariables is null)
            {
                context.RangeVariables = new XPathValue[Math.Max(Slot + 1, 4)];
                return default;
            }

            if (context.RangeVariables.Length <= Slot)
            {
                Array.Resize(ref context.RangeVariables, Slot + 1);
                return default;
            }

            return context.RangeVariables[Slot];
        }
    }

    /// <summary>The <c>for</c> expression, which evaluates its body once per item and concatenates the results.</summary>
    public sealed class ForExpr : BindingExpr
    {
        /// <summary>Initializes a for expression.</summary>
        /// <param name="slot">The slot the variable occupies.</param>
        /// <param name="sequence">The sequence to iterate.</param>
        /// <param name="body">The return clause.</param>
        public ForExpr(int slot, Expr sequence, Expr body)
            : base(slot, sequence, body)
        {
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            List<XPathValue> items = XdmSequence.Items(Sequence.Evaluate(ref context));
            XPathValue saved = Reserve(ref context);

            try
            {
                List<XPathValue> results = new List<XPathValue>(items.Count);

                foreach (XPathValue item in items)
                {
                    context.RangeVariables![Slot] = item;
                    results.Add(Body.Evaluate(ref context));
                }

                return XdmSequence.Concatenate(results);
            }
            finally
            {
                context.RangeVariables![Slot] = saved;
            }
        }
    }

    /// <summary>
    /// The <c>let</c> expression, which XPath 3.0 adds: <c>let $x := E return F</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It looks like <c>for</c> and is the opposite of it. <c>for $x in (1, 2, 3)</c> runs its body three
    /// times, once per item; <c>let $x := (1, 2, 3)</c> runs it once with the whole sequence bound. Which is
    /// why <c>let</c> is what you want for naming a subexpression you are about to use twice, and <c>for</c>
    /// never was.
    /// </para>
    /// <para>
    /// Naming it is also the only way to evaluate it once. XPath has no other place to put an intermediate
    /// result inside an expression, so before this a stylesheet either repeated the subexpression or reached
    /// for <c>xsl:variable</c> and stopped being one expression.
    /// </para>
    /// </remarks>
    public sealed class LetExpr : BindingExpr
    {
        /// <summary>Initializes a let expression.</summary>
        /// <param name="slot">The slot the variable occupies.</param>
        /// <param name="value">The expression bound to it, evaluated once.</param>
        /// <param name="body">The return clause.</param>
        public LetExpr(int slot, Expr value, Expr body)
            : base(slot, value, body)
        {
        }

        /// <inheritdoc/>
        internal override void MarkTailPosition()
        {
            // The return clause's value is handed on as it is. A call there has read the variable by the
            // time it is handed back, so the slot being given up on the way out costs it nothing.
            Body.MarkTailPosition();
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue saved = Reserve(ref context);

            try
            {
                // Evaluated after the slot is reserved and before it is written, so the bound expression
                // cannot see the variable it is being bound to: 'let $x := $x' means the outer one.
                context.RangeVariables![Slot] = Sequence.Evaluate(ref context);
                return Body.Evaluate(ref context);
            }
            finally
            {
                context.RangeVariables![Slot] = saved;
            }
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            XPathValue saved = Reserve(ref context);

            try
            {
                context.RangeVariables![Slot] = Sequence.Evaluate(ref context);
                return Body.EvaluateAsBoolean(ref context);
            }
            finally
            {
                context.RangeVariables![Slot] = saved;
            }
        }
    }

    /// <summary>
    /// The simple map operator, <c>E ! F</c>, which XPath 3.0 adds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Almost <c>/</c>, and the differences are the point. A path insists its steps select nodes, sorts the
    /// result into document order and removes duplicates; this does none of those. <c>F</c> may produce
    /// anything at all, and what comes back is one result per item of <c>E</c>, joined in that order and
    /// repeats intact.
    /// </para>
    /// <para>
    /// So <c>a/string-length()</c> is an error and <c>a ! string-length()</c> is a number per <c>a</c>. And
    /// <c>(1, 1, 1) ! .</c> is three items where a path would have refused the atomic values in the first
    /// place.
    /// </para>
    /// </remarks>
    public sealed class SimpleMapExpr : Expr
    {
        private readonly Expr m_source;
        private readonly Expr m_action;

        /// <summary>Initializes a simple map.</summary>
        /// <param name="source">The expression supplying the items.</param>
        /// <param name="action">The expression evaluated once per item, with that item as the context.</param>
        public SimpleMapExpr(Expr source, Expr action)
        {
            m_source = source;
            m_action = action;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_source, m_action };

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            List<XPathValue> items = XdmSequence.Items(m_source.Evaluate(ref context));
            List<XPathValue> results = new List<XPathValue>(items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                // A node item brings its own tree, so a path on the right starts where the item is rather
                // than where the expression was written. An atomic item has no tree and is carried as itself.
                DynamicContext inner = items[i].Kind == XPathValueKind.Node
                    ? context.SwitchTree(items[i].NodeTree, items[i].NodeId)
                    : context.WithAtomicItem(items[i]);

                inner.Position = i + 1;
                inner.Size = items.Count;

                results.AddRange(XdmSequence.Items(m_action.Evaluate(ref inner)));
            }

            return XdmSequence.Concatenate(results);
        }
    }

    /// <summary>The <c>some</c> and <c>every</c> expressions.</summary>
    /// <remarks>
    /// Both stop as soon as the answer is settled: <c>some</c> at the first item that satisfies the test,
    /// <c>every</c> at the first that does not. Over an empty sequence <c>some</c> is false and <c>every</c>
    /// is true, which is the usual convention for an empty universe and occasionally surprising.
    /// </remarks>
    public sealed class QuantifiedExpr : BindingExpr
    {
        private readonly bool m_requireAll;

        /// <summary>Initializes a quantified expression.</summary>
        /// <param name="slot">The slot the variable occupies.</param>
        /// <param name="sequence">The sequence to quantify over.</param>
        /// <param name="body">The test applied to each item.</param>
        /// <param name="requireAll"><see langword="true"/> for <c>every</c>, <see langword="false"/> for <c>some</c>.</param>
        public QuantifiedExpr(int slot, Expr sequence, Expr body, bool requireAll)
            : base(slot, sequence, body)
        {
            m_requireAll = requireAll;
        }

        /// <inheritdoc/>
        internal override bool IsBooleanValued => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return XPathValue.FromBoolean(EvaluateAsBoolean(ref context));
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            List<XPathValue> items = XdmSequence.Items(Sequence.Evaluate(ref context));
            XPathValue saved = Reserve(ref context);

            try
            {
                foreach (XPathValue item in items)
                {
                    context.RangeVariables![Slot] = item;

                    if (Body.EvaluateAsBoolean(ref context) != m_requireAll)
                    {
                        return !m_requireAll;
                    }
                }

                return m_requireAll;
            }
            finally
            {
                context.RangeVariables![Slot] = saved;
            }
        }
    }

    /// <summary>The conditional expression, <c>if (test) then a else b</c>.</summary>
    /// <remarks>
    /// The <c>else</c> is not optional in XPath, unlike in most languages: an expression must have a value
    /// whichever way the test goes, and writing <c>else ()</c> is how you say the value is nothing.
    /// </remarks>
    public sealed class IfExpr : Expr
    {
        private readonly Expr m_test;
        private readonly Expr m_then;
        private readonly Expr m_else;

        /// <summary>Initializes a conditional.</summary>
        /// <param name="test">The condition.</param>
        /// <param name="whenTrue">The value when the condition holds.</param>
        /// <param name="whenFalse">The value when it does not.</param>
        public IfExpr(Expr test, Expr whenTrue, Expr whenFalse)
        {
            m_test = test;
            m_then = whenTrue;
            m_else = whenFalse;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_test, m_then, m_else };

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return m_test.EvaluateAsBoolean(ref context)
                ? m_then.Evaluate(ref context)
                : m_else.Evaluate(ref context);
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            return m_test.EvaluateAsBoolean(ref context)
                ? m_then.EvaluateAsBoolean(ref context)
                : m_else.EvaluateAsBoolean(ref context);
        }

        /// <inheritdoc/>
        internal override void MarkTailPosition()
        {
            // Whichever branch is taken, its value is handed on as it is. The test is read, so it is not.
            m_then.MarkTailPosition();
            m_else.MarkTailPosition();
        }
    }
}
