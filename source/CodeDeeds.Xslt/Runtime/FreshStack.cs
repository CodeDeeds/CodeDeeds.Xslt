using System.Runtime.ExceptionServices;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// One call of a deep recursion, made on a new thread's stack because the current one is nearly used up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How much stack one level of a recursion costs is not a number this library controls. It was measured
    /// at between two and nearly four kilobytes for a recursive <c>xsl:call-template</c>, depending on which
    /// tier the just-in-time compiler had reached for each method in the chain and on what dynamic profiling
    /// had chosen to inline — so the same stylesheet recursing five hundred deep passed or failed according
    /// to what the process had been doing beforehand. A limit like that cannot be documented, and a
    /// stylesheet cannot be written to it.
    /// </para>
    /// <para>
    /// So the stack is not the limit. Where the runtime says too little of it is left, the call is made here
    /// instead: on a thread started for the purpose, with the caller waiting for it. The question asked is
    /// the one that was always asked, <see cref="System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack"/>,
    /// so what it guaranteed is still guaranteed — there is never a real stack overflow, which cannot be
    /// caught — and what ends a recursion with no terminating case is the count of calls in progress, which
    /// is the same on every machine and every run.
    /// </para>
    /// <para>
    /// The transformation stays single-threaded in every way that matters: exactly one thread runs at a
    /// time, and starting and joining a thread are both points at which each sees what the other wrote. An
    /// error raised on the new stack is raised again on the caller's, as itself, so an <c>xsl:try</c> round
    /// a deep recursion catches what it would have caught.
    /// </para>
    /// <para>
    /// It costs nothing until it happens, and when it happens it costs one thread for some thousands of
    /// levels. <c>System.Linq.Expressions</c> does the same for a deep expression tree.
    /// </para>
    /// </remarks>
    internal abstract class FreshStack
    {
        /// <summary>
        /// How much stack each new thread is given. Address space until it is touched, so the size costs
        /// nothing a shallow continuation uses none of; large so that a deep one needs few threads.
        /// </summary>
        private const int StackSize = 16 * 1024 * 1024;

        private ExceptionDispatchInfo? m_failure;

        /// <summary>Makes the call. Runs on the new thread.</summary>
        protected abstract void Run();

        /// <summary>
        /// Makes the call on a new stack and waits for it, raising here whatever it raised there.
        /// </summary>
        /// <param name="tooDeep">
        /// What to say where no thread can be had — a platform without them, or no room for another stack —
        /// which leaves nothing to do but what was done before there was this: report the recursion as too
        /// deep.
        /// </param>
        internal void RunToCompletion(string tooDeep)
        {
            Thread thread;

            try
            {
                thread = new Thread(static state => ((FreshStack)state!).RunGuarded(), StackSize)
                {
                    IsBackground = true,
                    Name = "CodeDeeds.Xslt deep recursion",
                };

                thread.Start(this);
            }
            catch (Exception error) when (error is PlatformNotSupportedException or OutOfMemoryException)
            {
                throw new XsltException(tooDeep, error);
            }

            thread.Join();
            m_failure?.Throw();
        }

        private void RunGuarded()
        {
            try
            {
                Run();
            }
            catch (Exception error)
            {
                // Everything, because an exception that leaves a thread's first method ends the process,
                // and none of these is this thread's to keep: each belongs to the caller, who gets it.
                m_failure = ExceptionDispatchInfo.Capture(error);
            }
        }
    }
}
