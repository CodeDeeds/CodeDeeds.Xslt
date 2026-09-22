using System.Runtime.CompilerServices;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A per-thread pool of scratch node lists.
    /// </summary>
    /// <remarks>
    /// Evaluating a location path needs a couple of working lists, and a path inside a predicate is evaluated
    /// once per candidate node — often millions of times in a single transformation. Allocating those lists
    /// fresh each time dominated the actual work of walking the axis.
    /// <para>
    /// Callers rent and return in a strict stack discipline, always through <see langword="try"/>/
    /// <see langword="finally"/>, so nested and recursive evaluation each get their own buffers. The pool is
    /// <see cref="ThreadStaticAttribute"/> so that concurrent transformations never share one.
    /// </para>
    /// <para>
    /// A few large buffers are kept as well as the many small ones. A step that holds every node of a
    /// document — <c>//node()</c>, or the <c>descendant-or-self::node()</c> a positional <c>//x[P]</c>
    /// cannot fold away — grows a list past the small limit, and dropping it on return meant growing one
    /// again from that limit at the next call: a quarter of a megabyte a call over a document of
    /// twenty-four thousand nodes, and nothing else the call allocated came near it. The large ones are
    /// bounded in number and in size, so the footprint is bounded still.
    /// </para>
    /// </remarks>
    internal static class NodeListPool
    {
        /// <summary>
        /// The most buffers kept per thread. Depth beyond this is rare enough that letting the extras be
        /// collected is cheaper than holding them for the life of the thread.
        /// </summary>
        private const int MaximumRetained = 32;

        /// <summary>Buffers larger than this are large ones, of which only a few are kept.</summary>
        private const int MaximumRetainedCapacity = 4096;

        /// <summary>
        /// How many large buffers are kept. A path holds two at a time and the count of its result a third,
        /// so two is what a document-wide step converges on and four leaves room for a path inside it.
        /// </summary>
        private const int MaximumLargeRetained = 4;

        /// <summary>
        /// Buffers larger than this are dropped rather than retained whatever their number: four megabytes,
        /// which is a step over a million nodes, and past which a document is no longer something whose
        /// working lists are worth holding on to between transformations.
        /// </summary>
        private const int LargestRetainedCapacity = 1 << 20;

        [ThreadStatic]
        private static Stack<List<int>>? s_pool;

        /// <summary>Takes a cleared list from the pool, or creates one.</summary>
        public static List<int> Rent()
        {
            Stack<List<int>>? pool = s_pool;
            if (pool is null || pool.Count == 0)
            {
                return new List<int>(16);
            }

            // Clearing a list of integers sets its count and touches nothing else, so a large list rented
            // for a small step costs what a small one would.
            List<int> list = pool.Pop();
            list.Clear();
            return list;
        }

        /// <summary>Returns a list to the pool.</summary>
        /// <param name="list">The list to return. It must not be used again by the caller.</param>
        public static void Return(List<int> list)
        {
            if (list.Capacity > MaximumRetainedCapacity)
            {
                ReturnLarge(list);
                return;
            }

            Stack<List<int>> pool = s_pool ??= new Stack<List<int>>();
            if (pool.Count < MaximumRetained)
            {
                pool.Push(list);
            }
        }

        /// <summary>Returns a large list, keeping it where the pool has room for one.</summary>
        /// <remarks>
        /// Out of line, so that the rent and the return every step makes stay the few instructions they
        /// were: a path inside a predicate rents twice for every candidate node. How many large lists the
        /// pool holds is counted here, over the thirty-two it can hold at most, rather than kept in a
        /// count that every rent would have to keep honest; a large list comes back once per large step,
        /// and a small one two thousand times a call.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReturnLarge(List<int> list)
        {
            if (list.Capacity > LargestRetainedCapacity)
            {
                return;
            }

            Stack<List<int>> pool = s_pool ??= new Stack<List<int>>();
            if (pool.Count >= MaximumRetained)
            {
                return;
            }

            int large = 0;
            foreach (List<int> held in pool)
            {
                if (held.Capacity > MaximumRetainedCapacity && ++large >= MaximumLargeRetained)
                {
                    return;
                }
            }

            pool.Push(list);
        }
    }
}
