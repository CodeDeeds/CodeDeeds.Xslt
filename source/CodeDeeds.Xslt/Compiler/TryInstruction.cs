using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// One <c>xsl:catch</c>: which errors it takes, what it does with them, and where it puts what it was
    /// told about the one it caught.
    /// </summary>
    /// <param name="Errors">
    /// The name tests an error code must match, or null for <c>*</c>, which takes everything.
    /// </param>
    /// <param name="Select">What the clause produces, where it says so with an expression.</param>
    /// <param name="Body">What the clause produces otherwise.</param>
    /// <param name="FirstSlot">
    /// The first of the six consecutive frame slots holding the <c>err:</c> variables, or -1 where the
    /// clause names none of them and there is nothing to bind.
    /// </param>
    internal sealed record CatchClause(
        CatchNameTest[]? Errors, Expr? Select, Instruction[] Body, int FirstSlot);

    /// <summary>One name test in an <c>errors</c> attribute.</summary>
    /// <remarks>
    /// The four shapes a name test takes, with null standing for the wildcard half: <c>prefix:local</c> is
    /// both, <c>*:local</c> is a local name in any namespace, <c>prefix:*</c> any name in one namespace. The
    /// bare <c>*</c> never reaches here, being the absence of any test at all.
    /// </remarks>
    /// <param name="NamespaceUri">The namespace the code must be in, or null for any.</param>
    /// <param name="LocalName">The local name the code must have, or null for any.</param>
    internal readonly record struct CatchNameTest(string? NamespaceUri, string? LocalName)
    {
        /// <summary>Whether an error code matches this test.</summary>
        public bool Matches(ExpandedName code)
        {
            return (NamespaceUri is null || NamespaceUri == code.NamespaceUri)
                && (LocalName is null || LocalName == code.LocalName);
        }
    }

    /// <summary>
    /// <c>xsl:try</c>, which runs a sequence constructor and hands a dynamic error in it to an
    /// <c>xsl:catch</c> rather than letting it end the transformation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The content is built into a buffer and written out only once it has finished, which is what
    /// <c>rollback-output</c> asks for and the only honest way to give it: an error half way through means
    /// half an element has already been written, and there is no taking that back from a serializer. So the
    /// buffer is the implementation of the guarantee rather than an optimisation of it. The buffer stands
    /// for the output it will be written to, so a result document written from inside it is written from
    /// wherever the try itself stands rather than from temporary output state. With
    /// <c>rollback-output="no"</c> there is no buffer: the body writes straight to the output, and an error
    /// after anything was written is <c>XTDE3530</c>, there being nothing to take it back with.
    /// </para>
    /// <para>
    /// Only a <em>dynamic</em> error is caught. A static one is a fault in the stylesheet and was raised
    /// before any of this ran; a type error the specification calls static is likewise not something a
    /// stylesheet gets to handle at run time.
    /// </para>
    /// </remarks>
    internal sealed class TryInstruction : Instruction
    {
        /// <summary>The namespace the <c>err:</c> variables and the standard error codes live in.</summary>
        public const string ErrorNamespace = "http://www.w3.org/2005/xqt-errors";

        private readonly Expr? m_select;
        private readonly Instruction[] m_body;
        private readonly CatchClause[] m_catches;
        private readonly bool m_rollbackOutput;

        /// <summary>Initializes a try instruction.</summary>
        /// <param name="select">What to evaluate, where the instruction says so with an expression.</param>
        /// <param name="body">What to run otherwise.</param>
        /// <param name="catches">The clauses that may take an error, in the order they were written.</param>
        /// <param name="rollbackOutput">Whether the body is buffered so that an error can take it back.</param>
        public TryInstruction(Expr? select, Instruction[] body, CatchClause[] catches, bool rollbackOutput = true)
        {
            m_select = select;
            m_body = body;
            m_catches = catches;
            m_rollbackOutput = rollbackOutput;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            if (m_select is null && !m_rollbackOutput)
            {
                ExecuteWithoutRollback(ref context, runtime);
                return;
            }

            XPathValue produced;

            try
            {
                produced = m_select is not null
                    ? m_select.Evaluate(ref context)
                    : VariableInstruction.CaptureSequence(
                        m_body, ref context, runtime, finalOutput: runtime.Output.IsFinalOutput);
            }
            catch (XsltException error)
            {
                Recover(error, ref context, runtime);
                return;
            }

            SequenceWriter.Write(produced, runtime);
        }

        /// <summary>
        /// Runs the body straight into the output, as <c>rollback-output="no"</c> asks.
        /// </summary>
        /// <remarks>
        /// Nothing is buffered, so nothing written before an error can be taken back. The specification
        /// leaves it to the processor whether a catch then succeeds (§8.3.1), and the honest line is drawn
        /// at whether anything was written: an error before the first write is caught as if the buffer had
        /// been there, and one after it is <c>XTDE3530</c>, the state of the result being what it is.
        /// </remarks>
        private void ExecuteWithoutRollback(ref DynamicContext context, XsltRuntime runtime)
        {
            OutputTarget output = runtime.Output;
            int written = output.Writes;

            try
            {
                ExecuteAll(m_body, ref context, runtime);
            }
            catch (XsltException error)
            {
                if (output.Writes != written)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE3530,
                        "xsl:try with rollback-output=\"no\" cannot recover from an error once output has been "
                        + $"written, and some was written before this one: {error.Message}");
                }

                Recover(error, ref context, runtime);
            }
        }

        /// <summary>Runs the first clause that takes this error, or lets it through if none does.</summary>
        private void Recover(XsltException error, ref DynamicContext context, XsltRuntime runtime)
        {
            ExpandedName code = CodeOf(error);

            foreach (CatchClause clause in m_catches)
            {
                if (clause.Errors is not null && !Array.Exists(clause.Errors, test => test.Matches(code)))
                {
                    continue;
                }

                if (clause.FirstSlot >= 0)
                {
                    Bind(clause.FirstSlot, code, error, ref context);
                }

                if (clause.Select is not null)
                {
                    SequenceWriter.Write(clause.Select.Evaluate(ref context), runtime);
                }
                else
                {
                    ExecuteAll(clause.Body, ref context, runtime);
                }

                return;
            }

            throw error;
        }

        /// <summary>Binds the six <c>err:</c> variables a clause may read.</summary>
        /// <remarks>
        /// Six consecutive slots in the order the compiler allocated them. The value is whatever the
        /// stylesheet attached to the error — the third argument of fn:error(), the content of a
        /// terminating xsl:message — and the empty sequence for an error this engine raised on its own
        /// account, which carries a message and nothing else. The module, line and column are those of the
        /// instruction that was running when the error was raised, where the stylesheet was read with its
        /// lines recorded; where it was not, they are the empty sequence too.
        /// </remarks>
        private static void Bind(int slot, ExpandedName code, XsltException error, ref DynamicContext context)
        {
            XPathValue empty = XPathValue.FromSequence(XdmSequence.Empty);

            context.Locals[context.FrameBase + slot] = XPathValue.FromQName(
                new XdmQName(code.NamespaceUri == ErrorNamespace ? "err" : string.Empty,
                    code.NamespaceUri,
                    code.LocalName));

            context.Locals[context.FrameBase + slot + 1] = XPathValue.FromString(error.Message);
            context.Locals[context.FrameBase + slot + 2] = error.Value ?? empty;
            context.Locals[context.FrameBase + slot + 3] = error.Module is string module
                ? XPathValue.FromString(module)
                : empty;
            context.Locals[context.FrameBase + slot + 4] = error.Line > 0
                ? XPathValue.FromInteger(error.Line)
                : empty;
            context.Locals[context.FrameBase + slot + 5] = error.Column > 0
                ? XPathValue.FromInteger(error.Column)
                : empty;
        }

        /// <summary>
        /// Reads the error's code as a name.
        /// </summary>
        /// <remarks>
        /// An error this engine raises without a code is still an error a stylesheet may want to catch, so
        /// it gets the one the specification keeps for exactly that: <c>FOER0000</c>, the code
        /// <c>fn:error()</c> raises when the caller names none. A code is in the <c>err</c> namespace unless
        /// the stylesheet raised it in another, which <c>fn:error()</c> lets it do.
        /// </remarks>
        private static ExpandedName CodeOf(XsltException error)
        {
            return new ExpandedName(error.CodeNamespace ?? ErrorNamespace, error.Code ?? "FOER0000");
        }
    }
}
