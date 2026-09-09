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
    /// </remarks>
    internal static class NodeListPool
    {
        /// <summary>
        /// The most buffers kept per thread. Depth beyond this is rare enough that letting the extras be
        /// collected is cheaper than holding them for the life of the thread.
        /// </summary>
        private const int MaximumRetained = 32;

        /// <summary>Buffers larger than this are dropped rather than retained, to bound the pool's footprint.</summary>
        private const int MaximumRetainedCapacity = 4096;

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
                return;
            }

            Stack<List<int>> pool = s_pool ??= new Stack<List<int>>();
            if (pool.Count < MaximumRetained)
            {
                pool.Push(list);
            }
        }
    }
}
