namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// The type annotations validation settled for the nodes of a tree, kept beside the tree rather than
    /// in it: what a validated copy of the nodes is given, where the tree itself is shared and stays as
    /// it was.
    /// </summary>
    /// <remarks>
    /// An <c>xsl:copy-of</c> with <c>validation="strict"</c> validates the nodes it was given and writes
    /// copies of them carrying what validation found; the originals, which a variable or the input may
    /// hold, are not annotated by it. The copier reads the overlay for each node it writes, and a node
    /// the overlay says nothing about is untyped, as an element validation found no declaration for is.
    /// </remarks>
    internal sealed class TypeOverlay
    {
        private readonly Dictionary<int, ushort> m_types = new();
        private HashSet<int>? m_nilled;

        /// <summary>Initializes an overlay over the components whose types it will record.</summary>
        /// <param name="schemas">What holds those types, so a tree made from this overlay can hold it too.</param>
        public TypeOverlay(object? schemas = null)
        {
            Schemas = schemas;
        }

        /// <summary>
        /// What holds the types the numbers below stand for. A number is not a reference and the table
        /// it is read back through holds each type weakly, so a tree carrying these annotations keeps
        /// this alongside them.
        /// </summary>
        public object? Schemas { get; }

        /// <summary>Records the type a node was validated as; 0 records nothing.</summary>
        /// <param name="node">The node, an element or an attribute.</param>
        /// <param name="typeId">The type's number, from <c>XdmSchemaType.Id</c>.</param>
        public void Set(int node, ushort typeId)
        {
            if (typeId != 0)
            {
                m_types[node] = typeId;
            }
        }

        /// <summary>Records that an element was validated as nilled.</summary>
        /// <param name="node">The element.</param>
        public void MarkNilled(int node)
        {
            (m_nilled ??= new HashSet<int>()).Add(node);
        }

        /// <summary>The number of the type a node was validated as, or 0 for none.</summary>
        /// <param name="node">The node.</param>
        public ushort TypeIdOf(int node)
        {
            return m_types.TryGetValue(node, out ushort typeId) ? typeId : (ushort)0;
        }

        /// <summary>Whether an element was validated as nilled.</summary>
        /// <param name="node">The element.</param>
        public bool IsNilled(int node)
        {
            return m_nilled is not null && m_nilled.Contains(node);
        }

        /// <summary>Every node with a type, and the type's number.</summary>
        public IEnumerable<KeyValuePair<int, ushort>> Types => m_types;

        /// <summary>Every element found nilled.</summary>
        public IEnumerable<int> Nilled => m_nilled ?? (IEnumerable<int>)Array.Empty<int>();
    }
}
