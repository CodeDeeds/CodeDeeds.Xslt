using System.Runtime.CompilerServices;
using System.Text;

namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// An immutable XML document represented as parallel arrays rather than as an object graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A node is an <see cref="System.Int32"/>, not an object. Nodes other than attributes are numbered in
    /// document (preorder) sequence starting at zero, which is always the root node. Three properties follow
    /// from that numbering, and together they account for most of this engine's performance:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// The subtree of a node occupies a contiguous range of ids, so the <c>descendant</c> axis is a linear
    /// sweep from <c>node + 1</c> to <see cref="SubtreeEndOf"/> rather than a recursive walk.
    /// </item>
    /// <item>
    /// Document order is integer order, so ordering a node-set is an integer sort and de-duplication is
    /// trivial. XPath 1.0 requires node-sets to be document-ordered and duplicate-free throughout, so this
    /// removes a pervasive cost.
    /// </item>
    /// <item>
    /// Names are interned to integers by <see cref="Model.NameTable"/>, so a name test is an integer comparison.
    /// </item>
    /// </list>
    /// <para>
    /// Attributes are held outside the preorder sequence, in their own arrays, so that a subtree sweep never
    /// has to step over them. An attribute is addressed by a tagged id at or above <see cref="AttributeIdBase"/>;
    /// <see cref="IsAttribute"/> distinguishes the two id spaces. Because attribute ids are not in preorder,
    /// document order must be compared with <see cref="DocumentOrderKeyOf"/> rather than by comparing ids.
    /// </para>
    /// <para>
    /// Instances are immutable once built and may be read concurrently from multiple threads. The one
    /// thing made after building is an element's namespace nodes, on first use and under a lock; a reader
    /// holds such a node's id only once it has been made.
    /// </para>
    /// <para>
    /// Namespace nodes share the attribute id space, beyond the attributes themselves: an element's set is
    /// made all at once — the <c>xml</c> namespace first, then every namespace in scope — so that a tree
    /// never asked for them pays nothing, and every accessor that already knows what to do with an
    /// attribute's id does the same for a namespace node's. Only the kind and the document order tell the
    /// two apart.
    /// </para>
    /// </remarks>
    public sealed class XdmTree
    {
        /// <summary>
        /// The first node id in the attribute id space. Ids below this denote preorder nodes.
        /// </summary>
        public const int AttributeIdBase = 0x40000000;

        /// <summary>
        /// Gets or sets the URI the document was read from, for <c>base-uri()</c> and <c>document-uri()</c>
        /// where no transformation is there to remember it. A transformation records the URIs of the
        /// documents it loads for itself, and consults this only for a tree it did not load.
        /// </summary>
        public string? DocumentUri { get; set; }

        /// <summary>
        /// Gets or sets the base URI of a tree the stylesheet built, before any <c>xml:base</c> inside it:
        /// that of the instruction that built it, or of the node its top was copied from (§5.7.1). Null
        /// for a tree read from somewhere, whose own URI is its base.
        /// </summary>
        public string? BaseUri { get; set; }

        /// <summary>
        /// Gets or sets the unparsed entities the document's type declaration declared, by name, or
        /// <see langword="null"/> where it declared none. What <c>unparsed-entity-uri()</c> answers from.
        /// </summary>
        public IReadOnlyDictionary<string, UnparsedEntity>? UnparsedEntities { get; set; }

        /// <summary>
        /// The attributes the document's type declaration typed as ID, as entries of the attribute arrays
        /// in document order; empty where it typed none. An <c>xml:id</c> is an ID without being listed.
        /// </summary>
        internal int[] DeclaredIdAttributes { get; set; } = Array.Empty<int>();

        /// <summary>The attributes the declaration typed as IDREF or IDREFS, in the same form.</summary>
        internal int[] DeclaredIdrefAttributes { get; set; } = Array.Empty<int>();

        /// <summary>
        /// The elements a schema typed as an ID, in a tree read with its annotations stripped; null
        /// where none was, or where the annotations are there to ask.
        /// </summary>
        internal HashSet<int>? IdElements { get; init; }

        /// <summary>The elements a schema typed as a reference to an ID, in the same form.</summary>
        internal HashSet<int>? IdrefElements { get; init; }

        /// <summary>
        /// The base URI of each node parsed out of an external entity, where it differs from its parent's;
        /// null where no node was. Nothing under such a node has a base of its own unless it says so.
        /// </summary>
        internal IReadOnlyDictionary<int, string>? EntityBases { get; set; }

        /// <summary>The base URI a node took from the external entity it was parsed out of, if it did.</summary>
        /// <param name="nodeId">The node.</param>
        internal string? EntityBaseOf(int nodeId)
        {
            return EntityBases is not null && EntityBases.TryGetValue(nodeId, out string? baseUri) ? baseUri : null;
        }

        /// <summary>The namespace URI bound to the <c>xml</c> prefix, which is always in scope.</summary>
        public const string XmlNamespaceUri = "http://www.w3.org/XML/1998/namespace";

        private NodeKind[] m_kind;
        private int[] m_nameCode;
        private int[] m_parent;
        private int[] m_firstChild;
        private int[] m_next;
        private int[] m_subtreeEnd;
        private int[] m_depth;
        private string?[] m_value;
        private int[] m_attrStart;
        private int[] m_attrCount;
        private int[] m_namespaceStart;
        private int[] m_namespaceCount;

        // Not readonly: the namespace nodes made after building are appended to these, beyond the attributes.
        private int[] m_attributeNameCode;
        private string[] m_attributeValue;
        private int[] m_attributeOwner;

        // How many entries the three arrays above hold, attributes and namespace nodes together, and where
        // each element's namespace nodes start and how many there are, once made.
        private readonly object m_namespaceNodeLock = new object();
        private int m_attributeEntryCount;
        private Dictionary<int, (int Start, int Count)>? m_namespaceNodesOf;

        private readonly string[] m_namespacePrefix;
        private readonly string[] m_namespaceUri;

        /// <summary>
        /// For each node, the nearest ancestor-or-self that declares a namespace, or -1 where none does.
        /// </summary>
        /// <remarks>
        /// What a node's in-scope namespaces are is settled by the ancestors that declare one, and in most
        /// documents that is the document element and nothing else. Walking every ancestor to find that
        /// out costs the depth of the node, and copying a document costs that once per element — the
        /// square of the depth, which a document nested a few tens of thousands deep turned into half a
        /// minute. The link skips the ancestors that have nothing to say, so the walk is as long as the
        /// number of declaring ancestors and no longer.
        /// </remarks>
        private int[] m_namespaceScope;

        // Where each node starts in the text it was parsed from, or null where that was not recorded.
        private readonly int[]? m_line;
        private readonly int[]? m_column;

        // The type each element and each attribute was validated as, by the number XdmSchemaType hands
        // out, with 0 for none, and the elements that are nilled; all null for a tree nothing validated,
        // which is what every tree was until schema awareness and what most still are.
        private ushort[]? m_nodeType;
        private ushort[]? m_attributeType;
        private HashSet<int>? m_nilled;

        internal XdmTree(
            NameTable nameTable,
            int nodeCount,
            NodeKind[] kind,
            int[] nameCode,
            int[] parent,
            int[] firstChild,
            int[] next,
            int[] subtreeEnd,
            int[] depth,
            string?[] value,
            int[] attributeStart,
            int[] attributeCount,
            int[] namespaceStart,
            int[] namespaceCount,
            int attributeNodeCount,
            int[] attributeNameCode,
            string[] attributeValue,
            int[] attributeOwner,
            string[] namespacePrefix,
            string[] namespaceUri,
            int[]? line = null,
            int[]? column = null,
            int parentlessNamespaceNodes = 0,
            bool pooledStorage = false)
        {
            m_pooled = pooledStorage;
            m_line = line;
            m_column = column;
            NameTable = nameTable;
            NodeCount = nodeCount;

            // A parentless namespace node the builder made is the last entry of the attribute arrays, and is a
            // namespace node by standing beyond the count of attributes, as the ones made later do.
            AttributeNodeCount = attributeNodeCount - parentlessNamespaceNodes;
            m_attributeEntryCount = attributeNodeCount;
            m_kind = kind;
            m_nameCode = nameCode;
            m_parent = parent;
            m_firstChild = firstChild;
            m_next = next;
            m_subtreeEnd = subtreeEnd;
            m_depth = depth;
            m_value = value;
            m_attrStart = attributeStart;
            m_attrCount = attributeCount;
            m_namespaceStart = namespaceStart;
            m_namespaceCount = namespaceCount;
            m_attributeNameCode = attributeNameCode;
            m_attributeValue = attributeValue;
            m_attributeOwner = attributeOwner;
            m_namespacePrefix = namespacePrefix;
            m_namespaceUri = namespaceUri;
            DocumentOrdinal = Interlocked.Increment(ref s_nextDocumentOrdinal);

            // Ids are assigned in document order, so a parent's link is settled before its children ask
            // for it, and one pass in id order settles them all.
            m_namespaceScope = pooledStorage
                ? System.Buffers.ArrayPool<int>.Shared.Rent(nodeCount)
                : new int[nodeCount];
            for (int node = 0; node < nodeCount; node++)
            {
                int above = parent[node];
                m_namespaceScope[node] = namespaceCount[node] > 0
                    ? node
                    : above < 0 ? -1 : m_namespaceScope[above];
            }
        }

        /// <summary>
        /// Returns the nearest ancestor-or-self of a node that declares a namespace, or -1 where none does.
        /// An attribute's scope is its owning element's.
        /// </summary>
        /// <summary>Whether the node arrays came from the shared pool and are to be given back.</summary>
        private bool m_pooled;

        /// <summary>
        /// Gives pooled node storage back to the shared pool, after which the tree must not be read.
        /// </summary>
        /// <remarks>
        /// Called by a transformation on the tree it built for its own input once the run is over, which
        /// is the one moment at which nothing can still be holding a node of it. The arrays are the
        /// largest things a transformation allocates, and returning them is what keeps the large object
        /// heap, and the full collections its growth brings on, out of a run. A tree that was not built
        /// this way is unaffected, so the call is safe on any tree; the arrays of a released tree are
        /// replaced by empty ones, so a use after release fails on a bounds check rather than reading
        /// whatever the pool has since handed the arrays to.
        /// </remarks>
        internal void ReleaseStorage()
        {
            if (!m_pooled)
            {
                return;
            }

            m_pooled = false;

            Give(ref m_kind);
            Give(ref m_nameCode);
            Give(ref m_parent);
            Give(ref m_firstChild);
            Give(ref m_next);
            Give(ref m_subtreeEnd);
            Give(ref m_depth);
            Give(ref m_value);
            Give(ref m_attrStart);
            Give(ref m_attrCount);
            Give(ref m_namespaceStart);
            Give(ref m_namespaceCount);
            Give(ref m_namespaceScope);

            // Not pooled, being rare; dropped so that nothing reads them after the nodes are gone.
            m_nodeType = null;
            m_attributeType = null;
            m_nilled = null;

            static void Give<T>(ref T[] array)
            {
                T[] given = array;
                array = Array.Empty<T>();
                System.Buffers.ArrayPool<T>.Shared.Return(given, clearArray: !typeof(T).IsValueType);
            }
        }

        /// <summary>The type each element was validated as, by number; set by the builder.</summary>
        internal ushort[]? NodeTypes
        {
            init => m_nodeType = value;
        }

        /// <summary>The type each attribute was validated as, by number; set by the builder.</summary>
        internal ushort[]? AttributeTypes
        {
            init => m_attributeType = value;
        }

        /// <summary>The elements validation found nilled; set by the builder.</summary>
        internal HashSet<int>? NilledNodes
        {
            init => m_nilled = value;
        }

        /// <summary>
        /// What holds the schema the annotations above were settled against, so that the types their
        /// numbers stand for outlive nothing this tree still needs; set by whatever validated it.
        /// </summary>
        /// <remarks>
        /// A number is not a reference, and the table it is read back through holds each type weakly so
        /// that a schema set nothing wants any more can be collected along with the numbers it spent.
        /// This is the tree saying it still wants one. Null on every tree nothing validated, which is
        /// almost all of them.
        /// </remarks>
        internal object? SchemaAnchor { get; init; }

        /// <summary>
        /// Whether any node of this tree carries a type annotation, which only a validated tree does.
        /// </summary>
        /// <remarks>
        /// The question every typed path asks first, so that a tree nothing validated — every tree, until
        /// a caller asks for validation — takes the path it always took and pays nothing for the option.
        /// </remarks>
        internal bool HasTypeAnnotations => m_nodeType is not null || m_attributeType is not null;

        /// <summary>
        /// The number of the type a node was validated as, or 0 for a node that carries no annotation:
        /// every node of an unvalidated tree, every node that is not an element or an attribute, and an
        /// element or attribute validation found no declaration for.
        /// </summary>
        /// <param name="nodeId">The node.</param>
        internal ushort TypeIdOf(int nodeId)
        {
            if (IsAttribute(nodeId))
            {
                int index = nodeId - AttributeIdBase;
                return m_attributeType is ushort[] attributes && index < attributes.Length ? attributes[index] : (ushort)0;
            }

            return m_nodeType is ushort[] nodes && nodeId < nodes.Length ? nodes[nodeId] : (ushort)0;
        }

        /// <summary>
        /// The type a node was validated as, or null for the annotation the data model gives an
        /// unvalidated node: <c>xs:untyped</c> for an element, <c>xs:untypedAtomic</c> for an attribute.
        /// </summary>
        /// <param name="nodeId">The node.</param>
        internal XPath.XdmSchemaType? TypeAnnotationOf(int nodeId)
        {
            return XPath.XdmSchemaType.ById(TypeIdOf(nodeId));
        }

        /// <summary>Whether an element was validated as nilled: <c>xsi:nil="true"</c> under a nillable declaration.</summary>
        /// <param name="nodeId">The node.</param>
        internal bool IsNilled(int nodeId)
        {
            return m_nilled is not null && !IsAttribute(nodeId) && m_nilled.Contains(nodeId);
        }

        /// <summary>
        /// This tree without its type annotations, which is what <c>input-type-annotations="strip"</c>
        /// asks for: the same nodes, every element <c>xs:untyped</c> and every attribute
        /// <c>xs:untypedAtomic</c> and nothing nilled.
        /// </summary>
        /// <remarks>
        /// The tree itself where it carries none. Otherwise a second tree over the same node arrays,
        /// since a tree is immutable and may be in use elsewhere with its annotations; the attribute
        /// arrays are copied, being the ones a tree appends namespace nodes to on demand, and the rest
        /// are shared.
        /// </remarks>
        internal XdmTree WithoutTypeAnnotations()
        {
            if (!HasTypeAnnotations && m_nilled is null)
            {
                return this;
            }

            return Sibling(null, null, null);
        }

        /// <summary>
        /// This tree with the annotations validation settled, in place of whatever it carried: what an
        /// <c>xsl:document</c> with <c>validation="strict"</c> produces from the tree its content built.
        /// </summary>
        /// <param name="types">What validation settled, by node.</param>
        internal XdmTree WithTypeAnnotations(TypeOverlay types)
        {
            ushort[]? nodeTypes = null;
            ushort[]? attributeTypes = null;

            foreach ((int node, ushort typeId) in types.Types)
            {
                if (IsAttribute(node))
                {
                    attributeTypes ??= new ushort[Math.Max(m_attributeEntryCount, 1)];
                    attributeTypes[node - AttributeIdBase] = typeId;
                }
                else
                {
                    nodeTypes ??= new ushort[NodeCount];
                    nodeTypes[node] = typeId;
                }
            }

            HashSet<int>? nilled = null;

            foreach (int node in types.Nilled)
            {
                (nilled ??= new HashSet<int>()).Add(node);
            }

            return Sibling(nodeTypes, attributeTypes, nilled, types.Schemas);
        }

        /// <summary>A second tree over the same node arrays, carrying the annotations given and nothing else.</summary>
        private XdmTree Sibling(ushort[]? nodeTypes, ushort[]? attributeTypes, HashSet<int>? nilled, object? anchor = null)
        {
            lock (m_namespaceNodeLock)
            {
                return new XdmTree(
                    NameTable,
                    NodeCount,
                    m_kind,
                    m_nameCode,
                    m_parent,
                    m_firstChild,
                    m_next,
                    m_subtreeEnd,
                    m_depth,
                    m_value,
                    m_attrStart,
                    m_attrCount,
                    m_namespaceStart,
                    m_namespaceCount,
                    m_attributeEntryCount,
                    (int[])m_attributeNameCode.Clone(),
                    (string[])m_attributeValue.Clone(),
                    (int[])m_attributeOwner.Clone(),
                    m_namespacePrefix,
                    m_namespaceUri,
                    m_line,
                    m_column,
                    m_attributeEntryCount - AttributeNodeCount)
                {
                    DocumentUri = DocumentUri,
                    BaseUri = BaseUri,
                    UnparsedEntities = UnparsedEntities,
                    DeclaredIdAttributes = DeclaredIdAttributes,
                    DeclaredIdrefAttributes = DeclaredIdrefAttributes,
                    EntityBases = EntityBases,
                    CopiedFrom = CopiedFrom,
                    RawText = RawText,
                    NodeTypes = nodeTypes,
                    AttributeTypes = attributeTypes,
                    NilledNodes = nilled,
                    SchemaAnchor = anchor ?? SchemaAnchor,
                };
            }
        }

        /// <summary>
        /// Appends every element in a range of ids whose name has a fingerprint, in id order.
        /// </summary>
        /// <remarks>
        /// The whole of a descendant step with a name test, which is what <c>//x</c> comes to, and the
        /// hottest loop in a stylesheet that asks about a document as a whole. It reads the two arrays
        /// directly rather than going through the accessors and a node test's virtual call per node: an
        /// id below the attribute base is never an attribute, so the checks the accessors make for one
        /// have nothing to find here.
        /// </remarks>
        /// <param name="first">The first id to look at.</param>
        /// <param name="last">The last id to look at, inclusive.</param>
        /// <param name="fingerprint">The fingerprint the element's name must have.</param>
        /// <param name="output">Where matching ids go.</param>
        internal void CollectElementsByFingerprint(int first, int last, int fingerprint, List<int> output)
        {
            NodeKind[] kinds = m_kind;
            int[] nameCodes = m_nameCode;
            NameTable names = NameTable;

            for (int node = first; node <= last; node++)
            {
                if (kinds[node] == NodeKind.Element && names.GetFingerprintOfNameCode(nameCodes[node]) == fingerprint)
                {
                    output.Add(node);
                }
            }
        }

        /// <summary>Appends the child elements of a node whose name has a fingerprint, in order.</summary>
        /// <param name="parent">The node whose children are looked at.</param>
        /// <param name="fingerprint">The fingerprint the element's name must have.</param>
        /// <param name="output">Where matching ids go.</param>
        internal void CollectChildElementsByFingerprint(int parent, int fingerprint, List<int> output)
        {
            if (IsAttribute(parent))
            {
                return;
            }

            NodeKind[] kinds = m_kind;
            int[] nameCodes = m_nameCode;
            int[] next = m_next;
            NameTable names = NameTable;

            for (int child = m_firstChild[parent]; child >= 0; child = next[child])
            {
                if (kinds[child] == NodeKind.Element && names.GetFingerprintOfNameCode(nameCodes[child]) == fingerprint)
                {
                    output.Add(child);
                }
            }
        }

        private int NamespaceScopeOf(int nodeId)
        {
            int element = IsAttribute(nodeId) ? m_attributeOwner[nodeId - AttributeIdBase] : nodeId;
            return element < 0 ? -1 : m_namespaceScope[element];
        }

        /// <summary>
        /// Returns the nearest ancestor of a declaring node that declares a namespace itself, or -1 where
        /// none does — the next scope out.
        /// </summary>
        private int NamespaceScopeAbove(int declaringNode)
        {
            int above = m_parent[declaringNode];
            return above < 0 ? -1 : m_namespaceScope[above];
        }

        private static long s_nextDocumentOrdinal;

        /// <summary>
        /// Gets a number ordering this tree against every other tree.
        /// </summary>
        /// <remarks>
        /// XPath 1.0 requires that nodes from different documents have a consistent document order, but leaves
        /// the order itself to the processor. Assigning each tree a number as it is built gives one: it is
        /// stable for as long as the trees exist, so a node-set spanning documents sorts the same way every
        /// time it is sorted. Comparing object identity would not do, because it gives no ordering, and the
        /// order a document happens to be loaded in is as good an answer as the specification asks for.
        /// </remarks>
        public long DocumentOrdinal { get; }

        /// <summary>Gets the name table used to intern every name in this tree.</summary>
        public NameTable NameTable { get; }

        /// <summary>Gets the number of preorder nodes, excluding attributes.</summary>
        public int NodeCount { get; }

        /// <summary>Gets the number of attribute nodes.</summary>
        public int AttributeNodeCount { get; }

        /// <summary>
        /// Where each node of this tree was copied from, for the nodes copied with their accumulator values;
        /// null where none was.
        /// </summary>
        /// <remarks>
        /// What <c>copy-accumulators="yes"</c> leaves behind. A copied node answers for the accumulators with
        /// the values of the node it was copied from, and rather than copy every value of every accumulator
        /// at the moment of copying — most of which nothing will ever ask for — the copy remembers its source
        /// and the question is put there when it is asked. Only the copied nodes are listed; a node built
        /// beside them belongs to this tree alone.
        /// </remarks>
        internal IReadOnlyDictionary<int, (XdmTree Tree, int Node)>? CopiedFrom { get; set; }

        /// <summary>
        /// The text nodes written with output escaping disabled, where any were: a text node a template
        /// hands back as its value keeps the flag it was written with, and the copier writes it raw again.
        /// </summary>
        internal HashSet<int>? RawText { get; set; }

        /// <summary>Whether a text node was written with output escaping disabled.</summary>
        /// <param name="node">The node.</param>
        internal bool IsRawText(int node) => RawText is not null && RawText.Contains(node);

        /// <summary>Gets the id of the root node, which is always zero.</summary>
        public static int RootNode => 0;

        /// <summary>
        /// Returns whether a node id denotes an attribute rather than a preorder node.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAttribute(int nodeId)
        {
            return nodeId >= AttributeIdBase;
        }

        /// <summary>Returns the kind of a node.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NodeKind KindOf(int nodeId)
        {
            return IsAttribute(nodeId)
                ? nodeId - AttributeIdBase >= AttributeNodeCount ? NodeKind.Namespace : NodeKind.Attribute
                : m_kind[nodeId];
        }

        /// <summary>Whether an id denotes a namespace node, which lives in the attribute id space.</summary>
        public bool IsNamespaceNode(int nodeId)
        {
            return IsAttribute(nodeId) && nodeId - AttributeIdBase >= AttributeNodeCount;
        }

        /// <summary>
        /// Whether a node is an attribute of type ID: an <c>xml:id</c>, which is one by its own
        /// specification wherever it is written, or one the document's type declaration typed so.
        /// </summary>
        /// <param name="nodeId">The node.</param>
        public bool IsIdAttribute(int nodeId)
        {
            if (!IsAttribute(nodeId) || nodeId - AttributeIdBase >= AttributeNodeCount)
            {
                return false;
            }

            if (Array.BinarySearch(DeclaredIdAttributes, nodeId - AttributeIdBase) >= 0)
            {
                return true;
            }

            // Or one a schema typed so, which validation annotated it with.
            if (m_attributeType is not null && TypeAnnotationOf(nodeId) is { IsIdType: true })
            {
                return true;
            }

            int fingerprint = FingerprintOf(nodeId);
            return NameTable.GetLocalName(fingerprint) == "id"
                && NameTable.GetNamespaceUri(fingerprint) == XmlNamespaceUri;
        }

        /// <summary>
        /// Whether a node is an attribute the document's type declaration typed as IDREF or IDREFS, or a
        /// schema did.
        /// </summary>
        /// <param name="nodeId">The node.</param>
        public bool IsIdrefAttribute(int nodeId)
        {
            if (!IsAttribute(nodeId) || nodeId - AttributeIdBase >= AttributeNodeCount)
            {
                return false;
            }

            return Array.BinarySearch(DeclaredIdrefAttributes, nodeId - AttributeIdBase) >= 0
                || (m_attributeType is not null && TypeAnnotationOf(nodeId) is { IsIdrefType: true });
        }

        /// <summary>
        /// Whether an element's typed value is an ID, or a reference to one: an element a schema gave a
        /// simple type of <c>xs:ID</c>, <c>xs:IDREF</c> or <c>xs:IDREFS</c>, or a type restricting one.
        /// </summary>
        /// <param name="nodeId">The node.</param>
        /// <param name="reference">Whether to ask about IDREF rather than ID.</param>
        internal bool IsIdTypedElement(int nodeId, bool reference)
        {
            if (IsAttribute(nodeId) || m_kind[nodeId] != NodeKind.Element)
            {
                return false;
            }

            // A tree read with its annotations stripped keeps which elements were IDs and references.
            if (m_nodeType is null)
            {
                HashSet<int>? kept = reference ? IdrefElements : IdElements;
                return kept is not null && kept.Contains(nodeId);
            }

            XPath.XdmSchemaType? type = TypeAnnotationOf(nodeId);

            // A complex type with simple content holds its text as the simple type does.
            if (type is { Variety: XPath.XdmSchemaVariety.Complex })
            {
                type = type.Content == System.Xml.Schema.XmlSchemaContentType.TextOnly ? type.SimpleContent : null;
            }

            return type is not null && (reference ? type.IsIdrefType : type.IsIdType);
        }

        /// <summary>
        /// The line a node starts on in the text it was parsed from, or 0 where that was not recorded.
        /// </summary>
        /// <remarks>
        /// Recorded for a stylesheet, whose instructions say where they stand when they fail, and not for a
        /// document, which never asks and would pay two words a node for the answer.
        /// </remarks>
        public int LineOf(int nodeId) => m_line is null || IsAttribute(nodeId) ? 0 : m_line[nodeId];

        /// <summary>The column a node starts at on its line, or 0 where that was not recorded.</summary>
        public int ColumnOf(int nodeId) => m_column is null || IsAttribute(nodeId) ? 0 : m_column[nodeId];

        /// <summary>
        /// Returns the name code of a node, or <see cref="NameTable.NoNameCode"/> for nodes that have no name.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NameCodeOf(int nodeId)
        {
            return IsAttribute(nodeId)
                ? m_attributeNameCode[nodeId - AttributeIdBase]
                : m_nameCode[nodeId];
        }

        /// <summary>
        /// Returns the fingerprint of a node's expanded name, or <see cref="NameTable.NoFingerprint"/> for
        /// nodes that have no name. This is the value to compare when performing a name test.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FingerprintOf(int nodeId)
        {
            return NameTable.GetFingerprintOfNameCode(NameCodeOf(nodeId));
        }

        /// <summary>
        /// Returns the parent of a node, or -1 for the root node. The parent of an attribute is its owning
        /// element, even though an attribute is not among that element's children.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ParentOf(int nodeId)
        {
            return IsAttribute(nodeId)
                ? m_attributeOwner[nodeId - AttributeIdBase]
                : m_parent[nodeId];
        }

        /// <summary>
        /// Returns the node at the top of the one this belongs to: the ancestor-or-self that has no parent.
        /// </summary>
        /// <remarks>
        /// Usually the document node, and answering with that outright would be quicker — but not every tree
        /// here is rooted at one. A sequence constructor with an <c>as</c> declaration builds parentless
        /// nodes, so the root of such a node is the node itself, and a parentless attribute is its own root
        /// too. Walking is what makes those answer correctly, and it costs one step per level on the one
        /// function that asks.
        /// </remarks>
        /// <param name="nodeId">The node to start from.</param>
        public int RootOf(int nodeId)
        {
            int node = nodeId;

            for (int parent = ParentOf(node); parent >= 0; parent = ParentOf(node))
            {
                node = parent;
            }

            return node;
        }

        /// <summary>Returns the first child of a node, or -1 if it has none. Attributes have no children.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FirstChildOf(int nodeId)
        {
            return IsAttribute(nodeId) ? -1 : m_firstChild[nodeId];
        }

        /// <summary>
        /// Returns the next sibling of a node, or -1 if it is the last. Attributes are not siblings of one
        /// another in this sense and always return -1; use <see cref="AttributeAt"/> to enumerate them.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextSiblingOf(int nodeId)
        {
            return IsAttribute(nodeId) ? -1 : m_next[nodeId];
        }

        /// <summary>
        /// Returns the largest preorder id contained in a node's subtree, inclusive of the node itself. The
        /// descendants of <paramref name="nodeId"/> are therefore the ids in <c>(nodeId, SubtreeEndOf(nodeId)]</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SubtreeEndOf(int nodeId)
        {
            return IsAttribute(nodeId) ? nodeId : m_subtreeEnd[nodeId];
        }

        /// <summary>Returns the depth of a node, with the root at zero. An attribute is one deeper than its element.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int DepthOf(int nodeId)
        {
            if (!IsAttribute(nodeId))
            {
                return m_depth[nodeId];
            }

            // A parentless attribute has nothing above it, so it is at the top of its own tree.
            int owner = m_attributeOwner[nodeId - AttributeIdBase];
            return owner < 0 ? 0 : m_depth[owner] + 1;
        }

        /// <summary>Returns the number of attributes on an element, or zero for any other node.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AttributeCountOf(int nodeId)
        {
            return IsAttribute(nodeId) ? 0 : m_attrCount[nodeId];
        }

        /// <summary>
        /// Returns the node id of an element's attribute by position.
        /// </summary>
        /// <param name="elementId">The owning element.</param>
        /// <param name="index">A zero-based index below <see cref="AttributeCountOf"/>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AttributeAt(int elementId, int index)
        {
            return AttributeIdBase + m_attrStart[elementId] + index;
        }

        /// <summary>
        /// Finds the namespace nodes of an element: one for each namespace in scope, the <c>xml</c> namespace
        /// among them (XDM §6.4).
        /// </summary>
        /// <remarks>
        /// Made on first use, the element's whole set at once and in one order, and the same ids from then
        /// on — which is what lets <c>is</c> and <c>generate-id()</c> answer for them as for any node.
        /// Nothing but an element has any.
        /// </remarks>
        /// <param name="elementId">The element.</param>
        /// <param name="first">The id of the first namespace node.</param>
        /// <param name="count">How many there are; the rest follow the first in id order.</param>
        /// <returns>Whether the node is an element, and so has namespace nodes.</returns>
        public bool TryGetNamespaceNodes(int elementId, out int first, out int count)
        {
            if (IsAttribute(elementId) || m_kind[elementId] != NodeKind.Element)
            {
                first = -1;
                count = 0;
                return false;
            }

            (int start, int made) = NamespaceNodeRange(elementId);
            first = AttributeIdBase + start;
            count = made;
            return true;
        }

        /// <summary>
        /// The namespace node of an element for one prefix, or -1 where no namespace with that prefix is in
        /// scope. The empty prefix names the default namespace's node.
        /// </summary>
        public int NamespaceNodeOf(int elementId, string prefix)
        {
            if (!TryGetNamespaceNodes(elementId, out int first, out int count))
            {
                return -1;
            }

            for (int i = 0; i < count; i++)
            {
                int fingerprint = NameTable.GetFingerprintOfNameCode(m_attributeNameCode[first - AttributeIdBase + i]);

                if (string.Equals(NameTable.GetLocalName(fingerprint), prefix, StringComparison.Ordinal))
                {
                    return first + i;
                }
            }

            return -1;
        }

        private (int Start, int Count) NamespaceNodeRange(int element)
        {
            lock (m_namespaceNodeLock)
            {
                m_namespaceNodesOf ??= new Dictionary<int, (int Start, int Count)>();

                if (m_namespaceNodesOf.TryGetValue(element, out (int Start, int Count) range))
                {
                    return range;
                }

                int start = m_attributeEntryCount;
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { "xml" };
                AddNamespaceNode(element, "xml", XmlNamespaceUri);

                // One node per prefix: a prefix declared twice on one element, which a copied namespace
                // node beside a copied element's own declarations can make, is one binding.
                foreach ((string prefix, string uri) in InScopeNamespacesOf(element))
                {
                    if (seen.Add(prefix))
                    {
                        AddNamespaceNode(element, prefix, uri);
                    }
                }

                // A tree built without recording its declarations — one parsed from JSON, say — still has
                // the namespaces its names are in (XDM §6.2.1): the element's own, and its attributes'.
                AddNamedNamespace(element, m_nameCode[element], seen);

                for (int i = 0; i < m_attrCount[element]; i++)
                {
                    AddNamedNamespace(element, m_attributeNameCode[m_attrStart[element] + i], seen);
                }

                range = (start, m_attributeEntryCount - start);
                m_namespaceNodesOf[element] = range;
                return range;
            }
        }

        private void AddNamedNamespace(int owner, int nameCode, HashSet<string> seen)
        {
            string uri = NameTable.GetNamespaceUri(NameTable.GetFingerprintOfNameCode(nameCode));

            if (uri.Length != 0)
            {
                string prefix = NameTable.GetPrefix(nameCode);

                if (seen.Add(prefix))
                {
                    AddNamespaceNode(owner, prefix, uri);
                }
            }
        }

        private void AddNamespaceNode(int owner, string prefix, string uri)
        {
            if (m_attributeEntryCount == m_attributeNameCode.Length)
            {
                int capacity = Math.Max(4, m_attributeEntryCount * 2);
                Array.Resize(ref m_attributeNameCode, capacity);
                Array.Resize(ref m_attributeValue, capacity);
                Array.Resize(ref m_attributeOwner, capacity);
            }

            // A namespace node's name is its prefix, in no namespace (XDM §6.4): local-name() answers the
            // prefix, namespace-uri() nothing, and the default namespace's node has no name at all.
            m_attributeNameCode[m_attributeEntryCount] = NameTable.GetNameCode(string.Empty, string.Empty, prefix);
            m_attributeValue[m_attributeEntryCount] = uri;
            m_attributeOwner[m_attributeEntryCount] = owner;
            m_attributeEntryCount++;
        }

        /// <summary>
        /// Finds an element's attribute by expanded name.
        /// </summary>
        /// <param name="elementId">The owning element.</param>
        /// <param name="fingerprint">The fingerprint of the attribute name being sought.</param>
        /// <returns>The attribute's node id, or -1 if the element has no such attribute.</returns>
        public int FindAttribute(int elementId, int fingerprint)
        {
            if (IsAttribute(elementId) || fingerprint == NameTable.NoFingerprint)
            {
                return -1;
            }

            int start = m_attrStart[elementId];
            int end = start + m_attrCount[elementId];
            for (int i = start; i < end; i++)
            {
                if (NameTable.GetFingerprintOfNameCode(m_attributeNameCode[i]) == fingerprint)
                {
                    return AttributeIdBase + i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns a key whose ordering is document order. Node ids alone cannot be compared directly because
        /// attributes are numbered outside the preorder sequence; this maps an attribute to a position
        /// immediately after its owning element and before that element's first child.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long DocumentOrderKeyOf(int nodeId)
        {
            if (!IsAttribute(nodeId))
            {
                return (long)nodeId << 32;
            }

            int index = nodeId - AttributeIdBase;
            int owner = m_attributeOwner[index];

            // A parentless attribute has no element to be ordered after, so it goes where the first child of
            // the root would: it is the only node its tree holds besides the root itself.
            if (owner < 0)
            {
                return ((long)RootNode << 32) | 1;
            }

            // Namespace nodes come after their element and before its attributes (XDM §2.4), in the order
            // they were made; the attributes keep their own order behind them.
            return index >= AttributeNodeCount
                ? ((long)owner << 32) | (uint)(index - m_namespaceNodesOf![owner].Start + 1)
                : ((long)owner << 32) | (uint)(0x40000000 + index - m_attrStart[owner] + 1);
        }

        /// <summary>
        /// Returns the string-value of a node as defined by XPath 1.0: for elements and the root, the
        /// concatenation of all descendant text nodes; otherwise the node's own character data.
        /// </summary>
        public string StringValueOf(int nodeId)
        {
            if (IsAttribute(nodeId))
            {
                return m_attributeValue[nodeId - AttributeIdBase];
            }

            NodeKind kind = m_kind[nodeId];
            if (kind != NodeKind.Element && kind != NodeKind.Root)
            {
                return m_value[nodeId] ?? string.Empty;
            }

            int end = m_subtreeEnd[nodeId];
            if (end == nodeId)
            {
                return string.Empty;
            }

            // The overwhelmingly common shape is an element wrapping a single text node. Return that string
            // directly rather than routing it through a StringBuilder.
            int first = m_firstChild[nodeId];
            if (first == end && m_kind[first] == NodeKind.Text)
            {
                return m_value[first] ?? string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = nodeId + 1; i <= end; i++)
            {
                if (m_kind[i] == NodeKind.Text)
                {
                    builder.Append(m_value[i]);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Resolves a namespace prefix against the declarations in scope at a node.
        /// </summary>
        /// <param name="nodeId">The node providing the scope. For an attribute, its owning element is used.</param>
        /// <param name="prefix">The prefix to resolve, or an empty string for the default namespace.</param>
        /// <returns>
        /// The namespace URI, or <see langword="null"/> if the prefix is not bound. An unbound default prefix
        /// resolves to an empty string rather than <see langword="null"/>, since it always denotes "no namespace".
        /// </returns>
        public string? ResolvePrefix(int nodeId, string prefix)
        {
            if (prefix == "xml")
            {
                return XmlNamespaceUri;
            }

            for (int current = NamespaceScopeOf(nodeId); current >= 0; current = NamespaceScopeAbove(current))
            {
                int start = m_namespaceStart[current];
                int end = start + m_namespaceCount[current];
                for (int i = start; i < end; i++)
                {
                    if (string.Equals(m_namespacePrefix[i], prefix, StringComparison.Ordinal))
                    {
                        // An empty URI un-declares the prefix rather than binding it.
                        string uri = m_namespaceUri[i];
                        return uri.Length == 0 && prefix.Length != 0 ? null : uri;
                    }
                }
            }

            return prefix.Length == 0 ? string.Empty : null;
        }

        /// <summary>
        /// Returns every namespace binding in scope at a node, including those inherited from ancestors, with
        /// the innermost declaration of a prefix winning.
        /// </summary>
        /// <remarks>
        /// XSLT copies the namespace <em>nodes</em> of an element to the result, not merely the prefixes the
        /// element's own name happens to use, so both copying and literal result elements need the full set.
        /// </remarks>
        /// <param name="nodeId">The node whose scope is wanted.</param>
        /// <returns>Prefix and URI pairs. Prefixes that have been un-declared are omitted.</returns>
        public IEnumerable<(string Prefix, string Uri)> InScopeNamespacesOf(int nodeId)
        {
            int nearest = NamespaceScopeOf(nodeId);

            if (nearest < 0)
            {
                return Array.Empty<(string, string)>();
            }

            // One declaring ancestor is the usual case — the document element — and its declarations are
            // the whole answer, with nothing to reconcile them against and nothing to allocate.
            return NamespaceScopeAbove(nearest) < 0
                ? DeclaredNamespacesOf(nearest)
                : InScopeNamespacesThrough(nearest);
        }

        /// <summary>The declarations one node makes, less the un-declarations.</summary>
        private IEnumerable<(string Prefix, string Uri)> DeclaredNamespacesOf(int declaringNode)
        {
            int start = m_namespaceStart[declaringNode];
            int end = start + m_namespaceCount[declaringNode];
            for (int i = start; i < end; i++)
            {
                if (m_namespaceUri[i].Length != 0)
                {
                    yield return (m_namespacePrefix[i], m_namespaceUri[i]);
                }
            }
        }

        /// <summary>
        /// The bindings in scope at a declaring node, reconciled across it and every declaring ancestor.
        /// </summary>
        private IEnumerable<(string Prefix, string Uri)> InScopeNamespacesThrough(int declaringNode)
        {
            Dictionary<string, string> bindings = new(StringComparer.Ordinal);

            for (int current = declaringNode; current >= 0; current = NamespaceScopeAbove(current))
            {
                int start = m_namespaceStart[current];
                int end = start + m_namespaceCount[current];
                for (int i = start; i < end; i++)
                {
                    // Walking upwards means an ancestor must not overwrite a nearer declaration.
                    bindings.TryAdd(m_namespacePrefix[i], m_namespaceUri[i]);
                }
            }

            foreach (KeyValuePair<string, string> binding in bindings)
            {
                if (binding.Value.Length != 0)
                {
                    yield return (binding.Key, binding.Value);
                }
            }
        }

        /// <summary>
        /// Returns the namespace declarations made directly on a node, not including those inherited from
        /// ancestors. Used by serialization to reproduce the original declarations.
        /// </summary>
        /// <param name="nodeId">The node to inspect.</param>
        /// <returns>A sequence of prefix and URI pairs, which may be empty.</returns>
        public IEnumerable<(string Prefix, string Uri)> NamespaceDeclarationsOf(int nodeId)
        {
            if (IsAttribute(nodeId))
            {
                yield break;
            }

            int start = m_namespaceStart[nodeId];
            int end = start + m_namespaceCount[nodeId];
            for (int i = start; i < end; i++)
            {
                yield return (m_namespacePrefix[i], m_namespaceUri[i]);
            }
        }
    }
}
