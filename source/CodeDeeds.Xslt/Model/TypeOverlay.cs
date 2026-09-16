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

        /// <summary>An attribute a schema supplies a value for where the element wrote none.</summary>
        /// <param name="NamespaceUri">Its namespace, usually none.</param>
        /// <param name="LocalName">Its local name.</param>
        /// <param name="Value">The default or fixed value the schema declares.</param>
        /// <param name="TypeId">The type it is annotated with.</param>
        public readonly record struct SuppliedAttribute(
            string NamespaceUri, string LocalName, string Value, ushort TypeId);

        private Dictionary<int, List<SuppliedAttribute>>? m_supplied;

        /// <summary>Whether validation supplied any attribute that was not written.</summary>
        public bool SuppliesAttributes => m_supplied is not null;

        /// <summary>
        /// Records an attribute the schema supplies a default or fixed value for.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 §25.4.1: "If default values for elements or attributes are defined in the schema, the
        /// validation process will where necessary create new nodes containing these default values." So
        /// validating is not only a check: an element that declared none of them comes out of it carrying
        /// the attributes its declaration says it has.
        /// </remarks>
        /// <param name="element">The element the attribute belongs to.</param>
        /// <param name="attribute">What to add.</param>
        public void Supply(int element, SuppliedAttribute attribute)
        {
            m_supplied ??= new Dictionary<int, List<SuppliedAttribute>>();

            if (!m_supplied.TryGetValue(element, out List<SuppliedAttribute>? held))
            {
                m_supplied[element] = held = new List<SuppliedAttribute>();
            }

            held.Add(attribute);
        }

        /// <summary>The attributes validation supplied for an element, in declaration order.</summary>
        /// <param name="element">The element.</param>
        public IReadOnlyList<SuppliedAttribute> SuppliedFor(int element)
        {
            return m_supplied is not null && m_supplied.TryGetValue(element, out List<SuppliedAttribute>? held)
                ? held
                : Array.Empty<SuppliedAttribute>();
        }
    }
}
