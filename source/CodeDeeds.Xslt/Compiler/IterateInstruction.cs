using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>Why a sequence constructor stopped short.</summary>
    /// <remarks>
    /// Read by <see cref="Instruction.ExecuteAll"/> after each instruction, which is what carries the signal
    /// out through however many <c>xsl:if</c> and <c>xsl:choose</c> stand between the instruction that
    /// raised it and the <c>xsl:iterate</c> that acts on it.
    /// </remarks>
    internal enum LoopSignal : byte
    {
        /// <summary>Nothing has interrupted anything, which is every instruction in most stylesheets.</summary>
        None,

        /// <summary>An <c>xsl:next-iteration</c>: end this iteration and start the next.</summary>
        NextIteration,

        /// <summary>An <c>xsl:break</c>: end the whole iteration.</summary>
        Break,
    }

    /// <summary>A loop-carried parameter of an <c>xsl:iterate</c>.</summary>
    /// <param name="Name">Its name, which <c>xsl:with-param</c> matches.</param>
    /// <param name="Slot">The frame slot holding its value.</param>
    /// <param name="Select">The initial value, where given as an expression.</param>
    /// <param name="Body">The initial value, where given as content.</param>
    /// <param name="Type">The declared type every value bound here must fit.</param>
    internal sealed record IterationParameter(
        ExpandedName Name, int Slot, Expr? Select, Instruction[]? Body, XdmSequenceType? Type);

    /// <summary>
    /// <c>xsl:iterate</c>, which walks a sequence carrying values from one item to the next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What <c>xsl:for-each</c> cannot do: each iteration of a for-each is independent, so a running total
    /// has to be expressed as a recursive template or not at all. Here the parameters declared at the top are
    /// rebound by <c>xsl:next-iteration</c>, which is the loop's only way of talking to the next round —
    /// there is still no assignment, and a parameter not mentioned keeps the value it had.
    /// </para>
    /// <para>
    /// <c>xsl:break</c> ends the whole thing, and <c>xsl:on-completion</c> runs only where it did not: that
    /// asymmetry is the point of having both, and it is what makes "the first item satisfying P, or a
    /// message saying there is none" one instruction rather than two passes.
    /// </para>
    /// </remarks>
    internal sealed class IterateInstruction : Instruction
    {
        private readonly Expr m_select;
        private readonly IterationParameter[] m_parameters;
        private readonly Instruction[] m_onCompletion;
        private readonly Instruction[] m_body;

        /// <summary>Initializes an iterate instruction.</summary>
        public IterateInstruction(
            Expr select,
            IterationParameter[] parameters,
            Instruction[] onCompletion,
            Instruction[] body)
        {
            m_select = select;
            m_parameters = parameters;
            m_onCompletion = onCompletion;
            m_body = body;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            List<XPathValue> items = XdmSequence.Items(m_select.Evaluate(ref context));

            foreach (IterationParameter parameter in m_parameters)
            {
                context.Locals[context.FrameBase + parameter.Slot] = VariableInstruction.Evaluate(
                    parameter.Select, parameter.Body, ref context, runtime, parameter.Type);
            }

            bool broke = false;

            for (int i = 0; i < items.Count; i++)
            {
                DynamicContext inner = context.WithItem(items[i]);
                inner.Position = i + 1;
                inner.Size = items.Count;
                inner.CurrentNode = inner.Node;
                inner.CurrentTree = inner.Tree;

                ExecuteAll(m_body, ref inner, runtime);

                if (runtime.LoopSignal == LoopSignal.Break)
                {
                    runtime.LoopSignal = LoopSignal.None;
                    broke = true;
                    break;
                }

                // Whether the iteration ended by running out of body or by saying so, what happens next is
                // the same — which is why xsl:next-iteration needs no marker beyond having stopped.
                runtime.LoopSignal = LoopSignal.None;
            }

            if (!broke)
            {
                // The focus is absent in xsl:on-completion (§8.3): the iteration has finished, so there is
                // no item it is standing on, and letting the one the loop was entered with show through
                // would let an xsl:number there number a node the instruction is not about.
                DynamicContext completed = context;
                completed.Node = DynamicContext.NotANode;
                completed.AtomicItem = default;
                completed.Position = 0;
                completed.Size = 0;

                ExecuteAll(m_onCompletion, ref completed, runtime);
            }
        }
    }

    /// <summary>
    /// <c>xsl:next-iteration</c>, which ends the current iteration after saying what the next one carries.
    /// </summary>
    /// <remarks>
    /// The values are computed before any is bound, so they all read the parameters as this iteration had
    /// them: <c>$a</c> in the expression for <c>$b</c> is the old <c>$a</c>, whichever order the
    /// <c>xsl:with-param</c> elements were written in. Binding as it went would make the order of the
    /// stylesheet's lines part of the meaning.
    /// </remarks>
    internal sealed class NextIterationInstruction : Instruction
    {
        private readonly (int Slot, Expr? Select, Instruction[]? Body, XdmSequenceType? Supplied,
            XdmSequenceType? Declared)[] m_bindings;

        /// <summary>Initializes a next-iteration instruction.</summary>
        /// <param name="bindings">
        /// The parameters it rebinds, resolved to slots. Each carries two types where the stylesheet wrote
        /// two: what the <c>xsl:with-param</c> declares the value it supplies to be, and what the
        /// <c>xsl:param</c> declares the parameter to hold.
        /// </param>
        public NextIterationInstruction(
            (int Slot, Expr? Select, Instruction[]? Body, XdmSequenceType? Supplied,
                XdmSequenceType? Declared)[] bindings)
        {
            m_bindings = bindings;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            XPathValue[] values = new XPathValue[m_bindings.Length];

            for (int i = 0; i < m_bindings.Length; i++)
            {
                // The type given to Evaluate decides what content amounts to as well as what it is checked
                // against — with one, a constructor yields a sequence rather than a document node — so the
                // one type case has to reach it as the only type, exactly as it always did.
                values[i] = VariableInstruction.Evaluate(
                    m_bindings[i].Select,
                    m_bindings[i].Body,
                    ref context,
                    runtime,
                    m_bindings[i].Supplied ?? m_bindings[i].Declared);

                if (m_bindings[i].Supplied is not null && m_bindings[i].Declared is not null)
                {
                    values[i] = XdmTypeConversion.Apply(
                        values[i], m_bindings[i].Declared, XsltErrorCode.XTTE0590);
                }
            }

            for (int i = 0; i < m_bindings.Length; i++)
            {
                context.Locals[context.FrameBase + m_bindings[i].Slot] = values[i];
            }

            runtime.LoopSignal = LoopSignal.NextIteration;
        }
    }

    /// <summary><c>xsl:break</c>, which ends the iteration it stands in after producing its content.</summary>
    internal sealed class BreakInstruction : Instruction
    {
        private readonly Expr? m_select;
        private readonly Instruction[] m_body;

        /// <summary>Initializes a break instruction.</summary>
        /// <param name="select">What it produces, where it says so with an expression.</param>
        /// <param name="body">What it produces otherwise.</param>
        public BreakInstruction(Expr? select, Instruction[] body)
        {
            m_select = select;
            m_body = body;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            if (m_select is not null)
            {
                new SequenceInstruction(m_select).Execute(ref context, runtime);
            }
            else
            {
                ExecuteAll(m_body, ref context, runtime);
            }

            runtime.LoopSignal = LoopSignal.Break;
        }
    }
}
