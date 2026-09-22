using System.Runtime.CompilerServices;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// What a walk over nested maps and arrays asks before it goes down a level, so that nesting deeper
    /// than the stack is an error rather than a stack overflow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An array is an item, so an array may hold an array, and nothing limits how far: folding
    /// <c>function($a, $i) { [$a] }</c> over a range nests one twenty thousand deep with no recursion in
    /// the stylesheet at all. Building it costs no stack. Reading it does, one level of the walk per level
    /// of the value, in everything that goes inside: atomizing, flattening, comparing, <c>deep-equal</c>,
    /// <c>map:find</c>, and the json and adaptive serialization methods. Each of those ended as a stack
    /// overflow, which cannot be caught and takes the process with it.
    /// </para>
    /// <para>
    /// A node tree is walked without recursion instead, because a deep document is ordinary input. A value
    /// nested thousands deep is not — JSON is refused beyond sixty-four levels before it gets here — so
    /// these walks stay the recursions they read best as, and ask the runtime how much stack is left, which
    /// is the one question whose answer does not depend on a guess about frame sizes. It is asked once per
    /// map or array and never per atomic item, so a sequence of numbers pays nothing for it.
    /// </para>
    /// </remarks>
    internal static class NestingGuard
    {
        /// <summary>Refuses to go a level further into a value where the stack is nearly spent.</summary>
        /// <param name="doing">What is being done to the value, as the message will say it: "atomize".</param>
        /// <exception cref="XsltException">Too little stack is left to go on.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Descend(string doing)
        {
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                Refuse("Maps or arrays", doing);
            }
        }

        /// <summary>
        /// The same question for the one walk over elements that is a recursion: the elements that stand
        /// for JSON, which <c>xml-to-json</c> reads a map or an array at a time with the keys of each map
        /// held while its members are written.
        /// </summary>
        /// <param name="doing">What is being done to the elements, as the message will say it.</param>
        /// <exception cref="XsltException">Too little stack is left to go on.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DescendElements(string doing)
        {
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                Refuse("Elements", doing);
            }
        }

        /// <summary>
        /// The same question for a walk over an expression's own tree, made while a stylesheet is compiled:
        /// looking for what reads the focus position, compiling the predicates inside it.
        /// </summary>
        /// <remarks>
        /// An expression is as deep as the parser's own recursion let it be and a few dozen levels for each
        /// of those, the parser building a long run of operators flat, so this is not expected to refuse
        /// anything. It is here because what it guards is a recursion over something a stylesheet supplied,
        /// and costs nothing anyone will notice where it is asked: once per node, once per stylesheet.
        /// </remarks>
        /// <exception cref="XsltException">Too little stack is left to go on.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DescendExpression()
        {
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0003,
                    "The expression is nested too deeply to compile. Every level of nesting costs stack to "
                    + "walk, and this one has more levels than there is stack for.");
            }
        }

        private static void Refuse(string what, string doing)
        {
            throw new XsltException(
                $"{what} nested too deeply to {doing}. Every level of nesting costs stack to read, and "
                + "there are more levels here than there is stack for.");
        }
    }
}
