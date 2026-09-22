using System.Reflection.Emit;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Emit
{
    /// <summary>
    /// Compiles an expression tree into a <see cref="DynamicMethod"/>.
    /// </summary>
    /// <remarks>
    /// One method is produced per top-level expression — the <c>select</c> or <c>test</c> an instruction holds.
    /// Everything nested inside it is emitted into that same method, so the only indirect call left at run time
    /// is the single delegate invocation at the top; the virtual dispatch between nodes disappears.
    /// </remarks>
    internal static class ExpressionCompiler
    {
        /// <summary>
        /// Compiles an expression, returning a node that evaluates through the emitted code.
        /// </summary>
        /// <param name="expression">The expression to compile.</param>
        /// <param name="description">A description used in code-generation diagnostics.</param>
        /// <returns>
        /// A compiled replacement, or the original expression if compiling it would gain nothing.
        /// </returns>
        public static Expr Compile(Expr expression, string description)
        {
            // Predicates are compiled in their own right, wherever they sit in the tree. A path this compiler
            // cannot emit inline still falls back to the interpreter, and without this its predicates would
            // fall back with it — leaving exactly the code that runs once per candidate node uncompiled.
            CompilePredicates(expression, description);

            // A bare literal or variable read is already a field access; wrapping it in a delegate call would
            // make it slower, not faster.
            if (expression is StringLiteralExpr or NumberLiteralExpr or VariableReferenceExpr)
            {
                return expression;
            }

            // One method for the whole expression means one stack frame for the whole expression, every
            // local of every operand in it, and a frame is taken in one piece when the method is entered
            // whatever part of it a call goes on to use. Twenty thousand comparisons joined by 'or' emitted
            // a method whose frame alone was more than a megabyte, and entering it was a stack overflow
            // with no recursion anywhere. An expression that size was written by a program; it is left to
            // the interpreter, which takes stack for the operand it is on and no other.
            if (CountUpTo(expression, LargestEmitted + 1) > LargestEmitted)
            {
                return expression;
            }

            DynamicMethod method = new DynamicMethod(
                $"xslt<{description}>",
                typeof(XPathValue),
                new[] { typeof(DynamicContext).MakeByRefType(), typeof(object[]) },
                typeof(ExpressionCompiler).Module,
                skipVisibility: true);

            ILBuilder il = new ILBuilder(method.GetILGenerator(), description);
            EmitContext context = new EmitContext(il);

            expression.Emit(context);
            il.Return(returnsValue: true);

            CompiledExpressionDelegate compiled = method.CreateDelegate<CompiledExpressionDelegate>();
            return new CompiledExpr(compiled, context.Constants, expression);
        }

        /// <summary>
        /// How many nodes an expression may have and still be emitted as one method. Ten thousand
        /// comparisons of a path with a value joined by <c>or</c>, four nodes apiece, ran on a megabyte of
        /// stack and twenty thousand did not, so a comparison costs between fifty and a hundred bytes of
        /// frame and this keeps a frame within some tens of kilobytes.
        /// </summary>
        private const int LargestEmitted = 2048;

        /// <summary>Counts the nodes of an expression, predicates apart, and stops counting at a limit.</summary>
        private static int CountUpTo(Expr expression, int limit)
        {
            NestingGuard.DescendExpression();
            int count = 1;

            foreach (Expr child in expression.Children)
            {
                if (count >= limit)
                {
                    break;
                }

                count += CountUpTo(child, limit - count);
            }

            return count;
        }

        /// <summary>
        /// Replaces every predicate in an expression tree with a compiled equivalent, in place.
        /// </summary>
        /// <remarks>
        /// Predicate arrays are the one part of the tree that is rewritten rather than rebuilt, which keeps
        /// this a single pass and leaves the surrounding nodes untouched.
        /// </remarks>
        private static void CompilePredicates(Expr expression, string description)
        {
            NestingGuard.DescendExpression();

            switch (expression)
            {
                case PathExpr path:
                    foreach (AxisStep step in path.Steps)
                    {
                        CompileInPlace(step.Predicates, description);
                    }

                    break;

                case FilterExpr filter:
                    CompileInPlace(filter.Predicates, description);
                    break;
            }

            foreach (Expr child in expression.Children)
            {
                CompilePredicates(child, description);
            }
        }

        private static void CompileInPlace(Expr[] predicates, string description)
        {
            for (int i = 0; i < predicates.Length; i++)
            {
                if (predicates[i] is not CompiledExpr)
                {
                    predicates[i] = Compile(predicates[i], $"{description} [predicate]");
                }
            }
        }
    }

    /// <summary>
    /// An expression that evaluates by invoking emitted IL.
    /// </summary>
    /// <remarks>
    /// The interpreted node it was compiled from is retained, and the entry points that the emitted code does
    /// not yet provide — walking the result as nodes, or testing it as a boolean without materialising a
    /// node-set — are still answered by it. That keeps the specialised, allocation-free interpreter paths in
    /// play instead of regressing to <see cref="Evaluate"/> and building a
    /// <see cref="NodeSet"/> that the caller would immediately throw away.
    /// </remarks>
    internal sealed class CompiledExpr : Expr
    {
        private readonly CompiledExpressionDelegate m_invoke;
        private readonly object[] m_constants;
        private readonly Expr m_source;

        /// <summary>Initializes a compiled expression.</summary>
        /// <param name="invoke">The emitted method.</param>
        /// <param name="constants">The constant pool it was compiled against.</param>
        /// <param name="source">The expression it was compiled from.</param>
        public CompiledExpr(CompiledExpressionDelegate invoke, object[] constants, Expr source)
        {
            m_invoke = invoke;
            m_constants = constants;
            m_source = source;
        }

        /// <summary>Gets the interpreted expression this was compiled from.</summary>
        public Expr Source => m_source;

        /// <inheritdoc/>
        internal override Expr Unwrapped => m_source;

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => m_source.ReturnsNodeSet;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => m_source.MaySpanDocuments;

        /// <inheritdoc/>
        internal override bool UsuallyReturnsNodeSet => m_source.UsuallyReturnsNodeSet;

        /// <inheritdoc/>
        internal override XdmTree? TryEvaluateNodes(ref DynamicContext context, List<int> output)
        {
            return m_source.TryEvaluateNodes(ref context, output);
        }

        /// <inheritdoc/>
        internal override void MarkTailPosition()
        {
            // The emitted code holds the very nodes it was compiled from, and calls back into the ones it
            // could not emit inline, so marking the source is marking what runs.
            m_source.MarkTailPosition();
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return m_invoke(ref context, m_constants);
        }

        /// <inheritdoc/>
        public override XdmTree EvaluateNodes(ref DynamicContext context, List<int> output)
        {
            return m_source.EvaluateNodes(ref context, output);
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            return m_source.EvaluateAsBoolean(ref context);
        }

        /// <inheritdoc/>
        public override string ToString() => m_source.ToString() ?? nameof(CompiledExpr);
    }
}
