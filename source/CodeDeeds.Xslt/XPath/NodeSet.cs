using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// An ordered, duplicate-free set of nodes, normally drawn from a single <see cref="XdmTree"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 1.0 requires node-sets to be in document order with duplicates removed. Because
    /// <see cref="XdmTree"/> numbers nodes in preorder, document order is normally integer order, so
    /// <see cref="SortAndDeduplicate"/> degrades to an integer sort. Only sets that contain attribute nodes
    /// need the slower comparison against <see cref="XdmTree.DocumentOrderKeyOf"/>, and that case is tracked so
    /// it is not paid for when it does not arise.
    /// </para>
    /// <para>
    /// A set may span documents — <c>document('a.xml') | document('b.xml')</c> is an ordinary XPath 1.0
    /// expression — but almost never does. So the tree of each node is recorded only once a node from a second
    /// tree is actually added: until then every node belongs to <see cref="Tree"/>, there is no parallel array
    /// to allocate or read, and <see cref="SpansDocuments"/> is <see langword="false"/>. Callers that walk a
    /// set must use <see cref="TreeAt"/> rather than <see cref="Tree"/>, which names only the first tree.
    /// </para>
    /// </remarks>
    public sealed class NodeSet
    {
        private int[] m_nodes;
        private XdmTree[]? m_nodeTrees;
        private int m_count;
        private bool m_containsAttributes;
        private bool m_ordered = true;

        /// <summary>Initializes an empty node-set over a tree.</summary>
        /// <param name="tree">The tree the nodes are drawn from.</param>
        /// <param name="capacity">The initial capacity to allocate.</param>
        public NodeSet(XdmTree tree, int capacity = 4)
        {
            Tree = tree;
            m_nodes = capacity > 0 ? new int[capacity] : Array.Empty<int>();
        }

        /// <summary>
        /// Creates a node-set from nodes that are already in document order with no duplicates, avoiding the
        /// sort a caller would otherwise pay for a second time.
        /// </summary>
        /// <param name="tree">The tree the nodes are drawn from.</param>
        /// <param name="nodes">The nodes, which the caller guarantees are ordered and distinct.</param>
        /// <returns>A node-set over <paramref name="nodes"/>.</returns>
        public static NodeSet FromOrderedNodes(XdmTree tree, List<int> nodes)
        {
            NodeSet set = new NodeSet(tree, nodes.Count);

            for (int i = 0; i < nodes.Count; i++)
            {
                int node = nodes[i];
                set.m_nodes[i] = node;
                set.m_containsAttributes |= XdmTree.IsAttribute(node);
            }

            set.m_count = nodes.Count;
            return set;
        }

        /// <summary>Creates a node-set containing exactly one node.</summary>
        /// <param name="tree">The tree the node is drawn from.</param>
        /// <param name="node">The single member.</param>
        /// <returns>A node-set of one node, already in document order.</returns>
        public static NodeSet Singleton(XdmTree tree, int node)
        {
            NodeSet set = new NodeSet(tree, 1);
            set.Add(node);
            return set;
        }

        /// <summary>
        /// Gathers a value into a node-set, refusing one that holds anything that is not a node.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XPath 2.0 has no node-set: a path yields a <em>sequence</em>, and one item is not wrapped in a
        /// sequence at all. So a value that means "these nodes" arrives in three shapes — a node-set, a bare
        /// node, or a sequence of them — and everything that works on nodes has to take all three. Written
        /// once here rather than at each of those places, which is how one of them came to take only the
        /// first shape and refuse a perfectly ordinary <c>(a, b)</c>.
        /// </para>
        /// <para>
        /// The order the value came in is kept. A sequence is not sorted into document order and duplicates
        /// are not removed, both of which XPath 1.0 would have done: <c>(b, a, a)</c> is three items and stays
        /// three items in that order, and only a path result is in document order.
        /// </para>
        /// </remarks>
        /// <param name="value">The value to read.</param>
        /// <param name="fallback">The tree an empty result belongs to, there being no node to say.</param>
        /// <param name="code">The error to raise where an item is not a node.</param>
        /// <param name="what">What wanted nodes, for the message.</param>
        /// <exception cref="XsltException">The value holds something that is not a node.</exception>
        public static NodeSet Of(XPathValue value, XdmTree fallback, XsltErrorCode code, string what)
        {
            if (value.Kind == XPathValueKind.NodeSet)
            {
                return value.AsNodeSet();
            }

            if (value.Kind == XPathValueKind.Node)
            {
                return Singleton(value.NodeTree, value.NodeId);
            }

            if (value.Kind != XPathValueKind.Sequence)
            {
                throw NotNodes(value, code, what, false);
            }

            List<XPathValue> items = XdmSequence.Items(value);

            if (items.Count == 0)
            {
                return new NodeSet(fallback, 0);
            }

            NodeSet nodes = new NodeSet(
                items[0].Kind == XPathValueKind.Node
                    ? items[0].NodeTree
                    : throw NotNodes(items[0], code, what, true),
                items.Count);

            foreach (XPathValue item in items)
            {
                if (item.Kind != XPathValueKind.Node)
                {
                    throw NotNodes(item, code, what, true);
                }

                nodes.Add(item.NodeTree, item.NodeId);
            }

            return nodes;
        }

        private static XsltException NotNodes(XPathValue value, XsltErrorCode code, string what, bool within)
        {
            string kind = value.Kind == XPathValueKind.Number
                ? "number"
                : value.Kind.ToString().ToLowerInvariant();

            return XsltErrors.Error(
                code,
                within
                    ? $"{what} works on nodes, and was given a sequence holding a {kind}."
                    : $"{what} works on nodes, and was given a {kind}.");
        }

        /// <summary>
        /// Gets the tree the set starts in. When <see cref="SpansDocuments"/> is <see langword="true"/> this
        /// is only the first of several; use <see cref="TreeAt"/> to get the tree of a particular node.
        /// </summary>
        public XdmTree Tree { get; }

        /// <summary>
        /// Gets whether the set holds nodes from more than one tree, in which case <see cref="Tree"/> does not
        /// describe all of them.
        /// </summary>
        public bool SpansDocuments => m_nodeTrees is not null;

        /// <summary>Gets the number of nodes in the set.</summary>
        public int Count => m_count;

        /// <summary>Gets the node at a position in the set's current ordering.</summary>
        /// <param name="index">A zero-based index below <see cref="Count"/>.</param>
        public int this[int index] => m_nodes[index];

        /// <summary>Gets the tree the node at a position belongs to.</summary>
        /// <param name="index">A zero-based index below <see cref="Count"/>.</param>
        public XdmTree TreeAt(int index) => m_nodeTrees is null ? Tree : m_nodeTrees[index];

        /// <summary>
        /// Appends a node of <see cref="Tree"/> without checking for duplicates or ordering. Call
        /// <see cref="SortAndDeduplicate"/> before the set is observed as an XPath value.
        /// </summary>
        /// <param name="node">The node to append.</param>
        public void Add(int node)
        {
            if (m_count == m_nodes.Length)
            {
                Grow();
            }

            if (m_count > 0 && node <= m_nodes[m_count - 1])
            {
                m_ordered = false;
            }

            m_containsAttributes |= XdmTree.IsAttribute(node);

            if (m_nodeTrees is not null)
            {
                m_nodeTrees[m_count] = Tree;
            }

            m_nodes[m_count++] = node;
        }

        /// <summary>
        /// Appends a node of a named tree, which need not be <see cref="Tree"/>.
        /// </summary>
        /// <param name="tree">The tree the node belongs to.</param>
        /// <param name="node">The node to append.</param>
        public void Add(XdmTree tree, int node)
        {
            if (m_nodeTrees is null)
            {
                if (ReferenceEquals(tree, Tree))
                {
                    Add(node);
                    return;
                }

                // The first node from a second tree: start recording where each node came from, and fill in
                // the tree of everything added so far.
                m_nodeTrees = new XdmTree[Math.Max(m_nodes.Length, 4)];
                for (int i = 0; i < m_count; i++)
                {
                    m_nodeTrees[i] = Tree;
                }

                // Two documents cannot be compared by node id, so what was ordered may no longer be.
                m_ordered = false;
            }

            if (m_count == m_nodes.Length)
            {
                Grow();
            }

            m_ordered = false;
            m_containsAttributes |= XdmTree.IsAttribute(node);
            m_nodeTrees[m_count] = tree;
            m_nodes[m_count++] = node;
        }

        /// <summary>Appends every node of another set. Ordering is not preserved; see <see cref="Add"/>.</summary>
        /// <param name="other">The set whose nodes are appended.</param>
        public void AddRange(NodeSet other)
        {
            if (other.m_nodeTrees is null && m_nodeTrees is null && ReferenceEquals(other.Tree, Tree))
            {
                for (int i = 0; i < other.m_count; i++)
                {
                    Add(other.m_nodes[i]);
                }

                return;
            }

            for (int i = 0; i < other.m_count; i++)
            {
                Add(other.TreeAt(i), other.m_nodes[i]);
            }
        }

        private void Grow()
        {
            int capacity = m_count == 0 ? 4 : m_count * 2;
            Array.Resize(ref m_nodes, capacity);

            if (m_nodeTrees is not null)
            {
                Array.Resize(ref m_nodeTrees, capacity);
            }
        }

        /// <summary>
        /// Places the set in document order and removes duplicates.
        /// </summary>
        public void SortAndDeduplicate()
        {
            if (m_nodeTrees is not null)
            {
                SortAcrossDocuments();
                return;
            }

            if (m_count > 1 && !(m_ordered && !m_containsAttributes))
            {
                if (m_containsAttributes)
                {
                    // Attribute ids fall outside the preorder sequence, so sort on the explicit ordering key.
                    long[] keys = new long[m_count];
                    for (int i = 0; i < m_count; i++)
                    {
                        keys[i] = Tree.DocumentOrderKeyOf(m_nodes[i]);
                    }

                    Array.Sort(keys, m_nodes, 0, m_count);
                }
                else
                {
                    Array.Sort(m_nodes, 0, m_count);
                }
            }

            if (m_count > 1)
            {
                int write = 1;
                for (int read = 1; read < m_count; read++)
                {
                    if (m_nodes[read] != m_nodes[write - 1])
                    {
                        m_nodes[write++] = m_nodes[read];
                    }
                }

                m_count = write;
            }

            m_ordered = true;
        }

        /// <summary>
        /// Orders a set whose nodes come from more than one tree: by document first, then by position within
        /// it. Two nodes are the same node only if they are the same node of the same tree.
        /// </summary>
        /// <remarks>
        /// Sorting pairs rather than parallel arrays, because <see cref="Array.Sort{TKey, TValue}(TKey[],
        /// TValue[], int, int)"/> can carry only one payload and both the node and its tree have to travel
        /// with the key. This path is rare enough that the allocation does not matter; the single-tree path
        /// above is the one that must stay cheap.
        /// </remarks>
        private void SortAcrossDocuments()
        {
            if (m_count > 1)
            {
                (long Document, long Order, int Node, XdmTree Tree)[] entries =
                    new (long, long, int, XdmTree)[m_count];

                for (int i = 0; i < m_count; i++)
                {
                    XdmTree tree = m_nodeTrees![i];
                    entries[i] = (tree.DocumentOrdinal, tree.DocumentOrderKeyOf(m_nodes[i]), m_nodes[i], tree);
                }

                Array.Sort(
                    entries,
                    static (left, right) => left.Document != right.Document
                        ? left.Document.CompareTo(right.Document)
                        : left.Order.CompareTo(right.Order));

                int write = 0;
                for (int read = 0; read < m_count; read++)
                {
                    if (read > 0
                        && entries[read].Node == entries[read - 1].Node
                        && ReferenceEquals(entries[read].Tree, entries[read - 1].Tree))
                    {
                        continue;
                    }

                    m_nodes[write] = entries[read].Node;
                    m_nodeTrees![write] = entries[read].Tree;
                    write++;
                }

                m_count = write;
            }

            m_ordered = true;
        }

        /// <summary>
        /// Returns the first node in document order, or -1 if the set is empty. The set must already be
        /// ordered; this does not sort.
        /// </summary>
        public int FirstNode()
        {
            return m_count == 0 ? -1 : m_nodes[0];
        }

        /// <summary>
        /// Returns the string-value of the set as defined by XPath 1.0: the string-value of the first node in
        /// document order, or an empty string when the set is empty.
        /// </summary>
        public string StringValue()
        {
            return m_count == 0 ? string.Empty : TreeAt(0).StringValueOf(m_nodes[0]);
        }
    }
}
