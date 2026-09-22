using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A long run of one kind of operator, <c>a + b + c + …</c>, evaluated in a loop rather than as
    /// operators nested one inside the next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parser reads such a run in a loop, so nothing in it passes the guard that refuses an expression
    /// nested too deeply to read, and what the loop built was a tree as deep as the run is long: every
    /// operator's left operand the whole of the run before it. Evaluating that is a call per operator, and
    /// <c>1+1+…+1</c> with five thousand terms ended the process with a stack overflow, which cannot be
    /// caught. So did compiling it, the walks over an expression's children being recursions too.
    /// </para>
    /// <para>
    /// Here the run is flat. The value so far is kept in a range-variable slot of the run's own, and each
    /// further operator is built as the ordinary expression it would have been with a reference to that
    /// slot as its left operand, so <c>head + b + c</c> is <c>let $v := head, $v := $v + b, $v := $v + c
    /// return $v</c> with the rebinding XPath has no syntax for. What each operator means is therefore
    /// decided where it always was, by the operator's own expression, and this loop knows nothing about
    /// any of them: it serves arithmetic, <c>!</c>, <c>=&gt;</c>, <c>intersect</c> and <c>except</c>,
    /// lookups, dynamic calls and the steps of a path that are expressions alike.
    /// </para>
    /// <para>
    /// A reference to a slot promises less than the expression it stands for — not that it is nodes, nor
    /// that it is of one document — so an operator after the first few dozen takes its general route
    /// where it has a quicker one. Only runs longer than <see cref="XPathParser"/>'s threshold are built
    /// this way, and an expression anybody wrote by hand is not one of them. <c>or</c>, <c>and</c> and
    /// <c>|</c> are not built this way at all: their operands may be grouped as the parser pleases, so a
    /// long run of those is a balanced tree of the same operators, no deeper than the logarithm of its
    /// length, and is still emitted by the compiled backend.
    /// </para>
    /// </remarks>
    internal sealed class ChainExpr : Expr
    {
        private readonly int m_slot;
        private readonly Expr m_head;
        private readonly Expr[] m_links;

        /// <summary>Initializes a run.</summary>
        /// <param name="slot">The range-variable slot the value so far is kept in.</param>
        /// <param name="head">The start of the run, built the ordinary way.</param>
        /// <param name="links">
        /// The operators after it in order, each reading <paramref name="slot"/> as its left operand.
        /// </param>
        internal ChainExpr(int slot, Expr head, Expr[] links)
        {
            m_slot = slot;
            m_head = head;
            m_links = links;
        }

        /// <inheritdoc/>
        /// <remarks>Not known, and the answer that is always safe is that it might.</remarks>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children
        {
            get
            {
                Expr[] children = new Expr[m_links.Length + 1];
                children[0] = m_head;
                m_links.CopyTo(children, 1);
                return children;
            }
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue saved = BindingExpr.ReserveSlot(ref context, m_slot);

            try
            {
                context.RangeVariables![m_slot] = m_head.Evaluate(ref context);

                // The array is read afresh each time round: an operand that binds a variable of its own
                // may have had to make room, and making room replaces it.
                foreach (Expr link in m_links)
                {
                    context.RangeVariables![m_slot] = link.Evaluate(ref context);
                }

                return context.RangeVariables![m_slot];
            }
            finally
            {
                context.RangeVariables![m_slot] = saved;
            }
        }
    }

    /// <summary>
    /// A long <c>let $a := …, $b := …, … return …</c>, its bindings made in a loop rather than each inside
    /// the one before.
    /// </summary>
    /// <remarks>
    /// The clauses of one <c>let</c> nest right to left, each later one the body of the one before, and the
    /// parser reads them in a loop, so a long one is as deep as it is long for the reason
    /// <see cref="ChainExpr"/> gives. A <c>let</c> is how XPath names an intermediate result, which makes a
    /// long one the ordinary shape of an expression written by a program. The bindings are independent
    /// slots, so there is nothing to the loop: each value is evaluated with the ones before it in place,
    /// the body with all of them, and every slot is given back on the way out, as the nested form gave each
    /// back as it was left.
    /// </remarks>
    internal sealed class LetChainExpr : Expr
    {
        private readonly int[] m_slots;
        private readonly Expr[] m_values;
        private readonly Expr m_body;

        /// <summary>Initializes a run of bindings.</summary>
        /// <param name="slots">The slot each variable occupies, in the order the clauses were written.</param>
        /// <param name="values">The expression bound to each.</param>
        /// <param name="body">The return clause.</param>
        internal LetChainExpr(int[] slots, Expr[] values, Expr body)
        {
            m_slots = slots;
            m_values = values;
            m_body = body;
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children
        {
            get
            {
                Expr[] children = new Expr[m_values.Length + 1];
                m_values.CopyTo(children, 0);
                children[^1] = m_body;
                return children;
            }
        }

        /// <inheritdoc/>
        internal override void MarkTailPosition()
        {
            // As a single let: the return clause's value is handed on as it is.
            m_body.MarkTailPosition();
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue[] saved = new XPathValue[m_slots.Length];
            int bound = 0;

            try
            {
                Bind(ref context, saved, ref bound);
                return m_body.Evaluate(ref context);
            }
            finally
            {
                Release(ref context, saved, bound);
            }
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            XPathValue[] saved = new XPathValue[m_slots.Length];
            int bound = 0;

            try
            {
                Bind(ref context, saved, ref bound);
                return m_body.EvaluateAsBoolean(ref context);
            }
            finally
            {
                Release(ref context, saved, bound);
            }
        }

        private void Bind(ref DynamicContext context, XPathValue[] saved, ref int bound)
        {
            for (int i = 0; i < m_slots.Length; i++)
            {
                // Reserved before the value is evaluated and written after, so that the bound expression
                // cannot see the variable it is being bound to: 'let $x := $x' means the outer one.
                saved[i] = BindingExpr.ReserveSlot(ref context, m_slots[i]);
                bound = i + 1;
                context.RangeVariables![m_slots[i]] = m_values[i].Evaluate(ref context);
            }
        }

        private void Release(ref DynamicContext context, XPathValue[] saved, int bound)
        {
            for (int i = bound - 1; i >= 0; i--)
            {
                context.RangeVariables![m_slots[i]] = saved[i];
            }
        }
    }
}
