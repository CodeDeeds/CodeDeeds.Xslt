using System.Xml;

namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// Builds an <see cref="XdmTree"/> incrementally, in document order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The builder exposes a writer-style surface — start an element, add its attributes and namespace
    /// declarations, add children, end the element — so that trees can be produced from any source. XML input
    /// goes through <see cref="FromXml(System.IO.TextReader, NameTable?)"/>; JSON input uses the same surface
    /// from a separate builder, which is why the API is not specific to <see cref="XmlReader"/>.
    /// </para>
    /// <para>
    /// Attributes and namespace declarations are stored contiguously per element, so
    /// <see cref="AddAttribute"/> and <see cref="AddNamespaceDeclaration"/> must be called before any child of
    /// the element being built. Doing otherwise throws rather than silently corrupting the ranges.
    /// </para>
    /// <para>
    /// Each tree owns its own <see cref="Model.NameTable"/> unless one is supplied. A compiled stylesheet
    /// deliberately does <em>not</em> share its name table with the trees it transforms: sharing would mean
    /// mutating the stylesheet's table during a transform, which would make a compiled stylesheet unsafe to use
    /// from more than one thread. Compiled name tests instead resolve against the source tree's table once per
    /// transform.
    /// </para>
    /// </remarks>
    public sealed class XdmTreeBuilder
    {
        private const string XmlnsNamespaceUri = "http://www.w3.org/2000/xmlns/";
        private const int InitialNodeCapacity = 64;
        private const int InitialAttributeCapacity = 32;

        // Sized by the constructor, from an estimate where the caller has one: the arrays double as the
        // document grows, and every doubling copies all of them, so a document that fits its estimate is
        // built without a single copy and finished without a trim.
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

        // Allocated by the first Locate, so that a tree never asked to record where its nodes stand pays
        // nothing for the option.
        private int[]? m_line;
        private int[]? m_column;
        private int[] m_namespaceCount;
        private int m_nodeCount;

        private int[] m_attributeNameCode;
        private string[] m_attributeValue;
        private int[] m_attributeOwner;
        private int m_attributeCount;

        // How many of the attribute entries are parentless namespace nodes, which come last.
        private int m_parentlessNamespaceNodes;

        private string[] m_namespacePrefix = new string[InitialAttributeCapacity];
        private string[] m_namespaceUri = new string[InitialAttributeCapacity];
        private int m_namespaceDeclarationCount;

        private int[] m_openNodes = new int[16];
        private int[] m_openLastChild = new int[16];
        private bool[] m_preserveSpace = new bool[16];
        private int m_openDepth;

        private bool m_finished;

        // What the document type declaration said, where the document had one: the attributes it typed as
        // ID, recorded by entry as they arrive, and the unparsed entities it declared.
        private DocumentTypeDeclaration? m_documentType;
        private List<int>? m_idAttributeEntries;
        private List<int>? m_idrefAttributeEntries;

        // The unparsed entities of a document node copied into this tree, which the copy keeps (§5.7.1).
        private IReadOnlyDictionary<string, UnparsedEntity>? m_inheritedEntities;

        // The base URI of each node parsed out of an external entity, where it differs from its parent's.
        private Dictionary<int, string>? m_entityBase;

        // The type each element and each attribute was validated as, and the elements found nilled;
        // allocated by the first annotation, so that a tree nothing validates pays nothing.
        private ushort[]? m_nodeType;
        private ushort[]? m_attributeType;

        // What settled the annotations above, kept so the tree can keep it: the numbers in those arrays
        // are read back through a table that holds each type weakly.
        private object? m_schemaAnchor;
        private HashSet<int>? m_nilled;

        /// <summary>How many elements are open, the document node itself not counting as one.</summary>
        public int OpenElementDepth => m_openDepth < 1 ? 0 : m_openDepth - 1;

        /// <summary>
        /// Whether an element is open, so a caller can tell a document's content from what surrounds it.
        /// </summary>
        /// <remarks>
        /// The document node counts as open from the moment the builder exists, so the depth is one outside
        /// the document element and two inside it. Asking the question this way rather than exposing the
        /// depth keeps that off-by-one where it belongs.
        /// </remarks>
        public bool InsideElement => m_openDepth > 1;

        /// <summary>
        /// The name cache holds two names per set, so that two names landing in the same set both stay
        /// resident instead of evicting each other. Powers of two, so the set is a mask rather than a division.
        /// </summary>
        private const int NameCacheSets = 32;
        private const int NameCacheSize = NameCacheSets * 2;

        private readonly string?[] m_cachePrefix = new string?[NameCacheSize];
        private readonly string?[] m_cacheUri = new string?[NameCacheSize];
        private readonly string?[] m_cacheLocal = new string?[NameCacheSize];
        private readonly int[] m_cacheNameCode = new int[NameCacheSize];

        /// <summary>
        /// Initializes a new builder.
        /// </summary>
        /// <param name="nameTable">
        /// The table used to intern names, or <see langword="null"/> to create one for this tree alone.
        /// </param>
        public XdmTreeBuilder(NameTable? nameTable = null)
            : this(nameTable, InitialNodeCapacity)
        {
        }

        /// <summary>Initializes a builder with room for about as many nodes as the caller expects.</summary>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <param name="nodeCapacity">
        /// How many nodes to make room for before the storage has to grow. An estimate, not a limit: a tree
        /// that outgrows it grows as it always did, and one that falls short of it is trimmed if the slack
        /// is worth reclaiming.
        /// </param>
        public XdmTreeBuilder(NameTable? nameTable, int nodeCapacity)
            : this(nameTable, nodeCapacity, InitialAttributeCapacity)
        {
        }

        /// <summary>
        /// Initializes a builder with room for about as many nodes and attributes as the caller expects.
        /// </summary>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <param name="nodeCapacity">How many nodes to make room for; an estimate, not a limit.</param>
        /// <param name="attributeCapacity">How many attributes to make room for; likewise an estimate.</param>
        public XdmTreeBuilder(NameTable? nameTable, int nodeCapacity, int attributeCapacity)
            : this(nameTable, nodeCapacity, attributeCapacity, pooledStorage: false)
        {
        }

        /// <summary>
        /// Initializes a builder whose node storage may be taken from the shared array pool, for a tree
        /// that will be given back once a transformation is done with it.
        /// </summary>
        /// <remarks>
        /// A document's node arrays are the largest things a transformation allocates, and large enough
        /// to land on the large object heap, where every allocation counts towards the next full
        /// collection. Taken from the pool and returned after the run, they are allocated once and reused
        /// across runs, and the collections that came with them do not happen. The tree that results must
        /// be released — <see cref="XdmTree.ReleaseStorage"/> — and not looked at afterwards, which is why
        /// this is only for trees the engine builds for itself and never for one a caller keeps.
        /// </remarks>
        internal XdmTreeBuilder(NameTable? nameTable, int nodeCapacity, int attributeCapacity, bool pooledStorage)
        {
            NameTable = nameTable ?? new NameTable();
            m_pooled = pooledStorage;

            int attributes = Math.Max(attributeCapacity, InitialAttributeCapacity);
            m_attributeNameCode = new int[attributes];
            m_attributeValue = new string[attributes];
            m_attributeOwner = new int[attributes];

            int capacity = Math.Max(nodeCapacity, InitialNodeCapacity);

            if (m_pooled)
            {
                // A power of two, so that every pool gives back arrays of exactly this length and the
                // twelve of them stay the same size, which the growth check relies on.
                capacity = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)capacity);
            }

            m_kind = RentOrNew<NodeKind>(capacity);
            m_nameCode = RentOrNew<int>(capacity);
            m_parent = RentOrNew<int>(capacity);
            m_firstChild = RentOrNew<int>(capacity);
            m_next = RentOrNew<int>(capacity);
            m_subtreeEnd = RentOrNew<int>(capacity);
            m_depth = RentOrNew<int>(capacity);
            m_value = RentOrNew<string?>(capacity);
            m_attrStart = RentOrNew<int>(capacity);
            m_attrCount = RentOrNew<int>(capacity);
            m_namespaceStart = RentOrNew<int>(capacity);
            m_namespaceCount = RentOrNew<int>(capacity);

            // The root node exists from the outset and stays open until Finish.
            int root = AllocateNode();
            m_kind[root] = NodeKind.Root;
            m_nameCode[root] = NameTable.NoNameCode;
            m_parent[root] = -1;
            m_depth[root] = 0;
            Push(root);
        }

        /// <summary>Gets the name table interning the names in the tree under construction.</summary>
        public NameTable NameTable { get; }

        /// <summary>
        /// Builds a document node with nothing in it.
        /// </summary>
        /// <remarks>
        /// What a transformation with no source document runs against. Everything that resolves a name needs
        /// a tree to resolve it in, so there is always one; whether it is the document being transformed or
        /// a stand-in is a separate question, and one the runtime answers rather than the tree.
        /// </remarks>
        public static XdmTree Empty() => new XdmTreeBuilder().Finish();

        /// <summary>
        /// Gets or sets which elements have their whitespace-only text stripped as the tree is built.
        /// </summary>
        /// <remarks>
        /// Applies only to text that is entirely whitespace. An <c>xml:space="preserve"</c> anywhere above the
        /// text overrides the declaration, as the XML specification requires.
        /// </remarks>
        public WhitespaceControl? Whitespace { get; set; }

        /// <summary>
        /// Returns whether a run of text is whitespace that the current element's declaration discards.
        /// </summary>
        private bool ShouldStripWhitespace(string text)
        {
            if (Whitespace is null || Whitespace.IsEmpty || m_openDepth < 2 || m_preserveSpace[m_openDepth - 1])
            {
                return false;
            }

            foreach (char c in text)
            {
                if (c is not (' ' or '\t' or '\r' or '\n'))
                {
                    return false;
                }
            }

            int element = m_openNodes[m_openDepth - 1];

            if (HoldsSimpleContent(element))
            {
                return false;
            }

            int fingerprint = NameTable.GetFingerprintOfNameCode(m_nameCode[element]);

            return Whitespace.ShouldStrip(
                NameTable.GetNamespaceUri(fingerprint),
                NameTable.GetLocalName(fingerprint));
        }

        /// <summary>
        /// Whether an element's type annotation is one whose content is a value rather than elements.
        /// </summary>
        /// <remarks>
        /// §4.4.2: "If an element in a source document has a type annotation that is a simple type or a
        /// complex type with simple content, then any whitespace text nodes among its children are
        /// preserved, regardless of any <c>xsl:strip-space</c> declarations. The reason for this is that
        /// stripping a whitespace text node from an element with simple content could make the element
        /// invalid: for example, it could cause the <c>minLength</c> facet to be violated." The annotation
        /// is what is asked, not the declaration, which is why the same specification adds that "stripping
        /// of type annotations happens before stripping of whitespace text nodes, so this situation will
        /// not occur if <c>input-type-annotations="strip"</c> is specified" — a tree read that way has
        /// no annotation here to find, and the declarations apply as they would to any other document.
        /// </remarks>
        /// <param name="element">The open element the text is being added to.</param>
        private bool HoldsSimpleContent(int element)
        {
            if (m_nodeType is null)
            {
                return false;
            }

            XPath.XdmSchemaType? type = XPath.XdmSchemaType.ById(m_nodeType[element]);

            // Mixed content is not simple content: an element that admits both text and elements is one
            // whose whitespace the declarations are free to strip.
            return type is not null
                && (type.Variety != XPath.XdmSchemaVariety.Complex
                    || type.Content == System.Xml.Schema.XmlSchemaContentType.TextOnly);
        }

        /// <summary>
        /// Builds a tree from XML text.
        /// </summary>
        /// <param name="reader">The XML to parse. The caller retains ownership and must dispose it.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <param name="whitespace">Which whitespace-only text nodes to strip, or <see langword="null"/> to keep all.</param>
        /// <param name="locations">Whether to record where each node starts, as a stylesheet asks.</param>
        /// <param name="entityResolver">
        /// What fetches the external subset of a document type declaration and the external entities it
        /// declares, or <see langword="null"/>, the default, to fetch nothing: the declaration's internal
        /// subset is still read, and everything it names outside the document is left unread.
        /// </param>
        /// <param name="baseUri">The document's base URI, which a declaration's relative references resolve against.</param>
        /// <returns>The parsed tree.</returns>
        /// <remarks>
        /// <para>
        /// Nothing outside the document is read unless a resolver is given to read it, so a document cannot
        /// on its own be used to read local files or reach the network. A document type declaration is read
        /// for what it holds — entities are expanded, default attributes supplied, ID attributes and unparsed
        /// entities noted — and an external entity it declares expands to nothing where there is no resolver.
        /// </para>
        /// <para>
        /// The text has already been decoded by the time it arrives here, so any <c>encoding</c> in the
        /// document's XML declaration is ignored — whoever opened the reader has already decided. Use the
        /// <see cref="Stream"/> overload to let the document say what it is.
        /// </para>
        /// </remarks>
        public static XdmTree FromXml(
            TextReader reader,
            NameTable? nameTable = null,
            WhitespaceControl? whitespace = null,
            bool locations = false,
            IXsltResolver? entityResolver = null,
            string? baseUri = null)
        {
            EntityResolverAdapter? entities = entityResolver is null ? null : new EntityResolverAdapter(entityResolver, baseUri);
            using XmlReader xmlReader = XmlReader.Create(reader, HardenedSettings(entities));
            return Build(xmlReader, nameTable, whitespace, locations, entities, baseUri, InitialNodeCapacity, InitialAttributeCapacity, trackEntityBases: entities is not null);
        }

        /// <summary>
        /// Builds a tree from XML text, validating the document against schemas as it is read and
        /// annotating each element and attribute with the type validation settled on.
        /// </summary>
        /// <param name="reader">The XML to parse. The caller retains ownership and must dispose it.</param>
        /// <param name="whitespace">Which whitespace-only text nodes to strip, or <see langword="null"/> to keep all.</param>
        /// <param name="entityResolver">What fetches what a document type declaration names, or null for nothing.</param>
        /// <param name="baseUri">The document's base URI.</param>
        /// <param name="validation">What to validate against, and how.</param>
        /// <returns>The parsed tree, its nodes annotated.</returns>
        /// <exception cref="XsltException">The document is not valid; see <see cref="TreeValidation"/>.</exception>
        internal static XdmTree FromXmlValidated(
            TextReader reader,
            WhitespaceControl? whitespace,
            IXsltResolver? entityResolver,
            string? baseUri,
            TreeValidation validation)
        {
            EntityResolverAdapter? entities = entityResolver is null ? null : new EntityResolverAdapter(entityResolver, baseUri);
            using XmlReader xmlReader = XmlReader.Create(reader, HardenedSettings(entities, validation));
            return Build(
                xmlReader, null, whitespace, false, entities, baseUri,
                InitialNodeCapacity, InitialAttributeCapacity, trackEntityBases: entities is not null, validation: validation);
        }

        /// <summary>
        /// Builds a tree from XML bytes.
        /// </summary>
        /// <param name="stream">The XML to parse. The caller retains ownership and must dispose it.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <param name="whitespace">Which whitespace-only text nodes to strip, or <see langword="null"/> to keep all.</param>
        /// <returns>The parsed tree.</returns>
        /// <remarks>
        /// <para>
        /// Preferred over the <see cref="TextReader"/> overload where the bytes are what the caller has. An
        /// XML document is self-describing about its encoding, through a byte-order mark or the
        /// <c>encoding</c> of its declaration, and only the bytes can carry that: a document declaring
        /// <c>ISO-8859-1</c> is read as such here, where decoding it first would have settled the question
        /// before this engine ever saw the declaration.
        /// </para>
        /// <para>
        /// What is read outside the document is what the <see cref="TextReader"/> overload says, and the
        /// stream is read forwards only and is never sought.
        /// </para>
        /// </remarks>
        public static XdmTree FromXml(
            Stream stream,
            NameTable? nameTable = null,
            WhitespaceControl? whitespace = null,
            bool locations = false,
            IXsltResolver? entityResolver = null,
            string? baseUri = null)
        {
            EntityResolverAdapter? entities = entityResolver is null ? null : new EntityResolverAdapter(entityResolver, baseUri);
            (int nodes, int attributes) = EstimateFromStream(stream);
            using XmlReader xmlReader = XmlReader.Create(stream, HardenedSettings(entities));
            return Build(xmlReader, nameTable, whitespace, locations, entities, baseUri, nodes, attributes, trackEntityBases: entities is not null);
        }

        /// <summary>How much of a stream is looked at to size the tree before it is read.</summary>
        private const int StreamScanLimit = 1 << 20;

        /// <summary>
        /// Guesses how many nodes and attributes a stream's document will make, by counting tags and
        /// attributes in as much of it as is cheap to look at and scaling by its length.
        /// </summary>
        /// <remarks>
        /// Only a stream that can be rewound is looked at, and only its first megabyte: the count over
        /// that much says what the rest is likely to hold, at the cost of one pass over bytes the reader
        /// is about to read anyway. The bytes are counted rather than decoded, which is right for the
        /// encodings a document is nearly always in and near enough for the rest — this is an estimate,
        /// and an estimate that is off costs a doubling or a trim, which is what every document paid
        /// before there was one.
        /// </remarks>
        private static (int Nodes, int Attributes) EstimateFromStream(Stream stream)
        {
            if (!stream.CanSeek)
            {
                return (InitialNodeCapacity, InitialAttributeCapacity);
            }

            long start = stream.Position;
            long remaining = stream.Length - start;

            if (remaining <= 0)
            {
                return (InitialNodeCapacity, InitialAttributeCapacity);
            }

            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);

            try
            {
                long scanned = 0;
                long tags = 0;
                long equals = 0;

                while (scanned < StreamScanLimit)
                {
                    int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, StreamScanLimit - scanned));
                    if (read <= 0)
                    {
                        break;
                    }

                    ReadOnlySpan<byte> bytes = buffer.AsSpan(0, read);
                    tags += bytes.Count((byte)'<');
                    equals += bytes.Count((byte)'=');
                    scanned += read;
                }

                if (scanned == 0)
                {
                    return (InitialNodeCapacity, InitialAttributeCapacity);
                }

                double scale = (double)remaining / scanned;
                tags = (long)(tags * scale);
                equals = (long)(equals * scale);

                return (
                    (int)Math.Min(tags + tags / 2 + 16, int.MaxValue / 2),
                    (int)Math.Min(equals + 8, int.MaxValue / 2));
            }
            finally
            {
                stream.Position = start;
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Builds a tree from XML held in a string, either a whole document or a fragment.
        /// </summary>
        /// <remarks>
        /// A fragment is what an external general parsed entity may be: several top-level elements, or text
        /// with no element at all. The resulting document node then has more children than a document node
        /// out of a file ever does, which is exactly what <c>fn:parse-xml-fragment()</c> answers with.
        /// </remarks>
        /// <param name="xml">The XML to parse.</param>
        /// <param name="fragment">Whether to accept a fragment rather than to require one root element.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <returns>The parsed tree.</returns>
        /// <exception cref="XmlException">The text is not well-formed.</exception>
        public static XdmTree FromXml(string xml, bool fragment, NameTable? nameTable = null)
        {
            XmlReaderSettings settings = HardenedSettings(null);
            settings.ConformanceLevel = fragment ? ConformanceLevel.Fragment : ConformanceLevel.Document;

            using XmlReader reader = XmlReader.Create(new StringReader(xml), settings);
            return Build(reader, nameTable, null, false, null, null, EstimateNodeCount(xml), EstimateAttributeCount(xml), trackEntityBases: false);
        }

        /// <summary>
        /// Builds a tree from XML held in a string, with the storage sized from the text before it is read.
        /// </summary>
        /// <remarks>
        /// The same as reading it through a <see cref="StringReader"/>, but for the sizing: the text is in
        /// hand, so how many nodes it will make can be estimated from it, and a tree that fits its estimate
        /// is built without the storage ever being copied.
        /// </remarks>
        /// <param name="xml">The XML to parse.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <param name="whitespace">Which whitespace-only text nodes to strip, or <see langword="null"/> to keep all.</param>
        /// <param name="entityResolver">What fetches the external subset of a document type declaration.</param>
        /// <param name="baseUri">The document's base URI.</param>
        /// <returns>The parsed tree.</returns>
        public static XdmTree FromXmlText(
            string xml,
            NameTable? nameTable = null,
            WhitespaceControl? whitespace = null,
            IXsltResolver? entityResolver = null,
            string? baseUri = null)
        {
            EntityResolverAdapter? entities = entityResolver is null ? null : new EntityResolverAdapter(entityResolver, baseUri);
            using XmlReader xmlReader = XmlReader.Create(new StringReader(xml), HardenedSettings(entities));
            return Build(xmlReader, nameTable, whitespace, false, entities, baseUri, EstimateNodeCount(xml), EstimateAttributeCount(xml), trackEntityBases: entities is not null);
        }

        /// <summary>
        /// Builds a tree from a string for a transformation that will release it, taking the node storage
        /// from the shared pool; see <see cref="XdmTree.ReleaseStorage"/>.
        /// </summary>
        internal static XdmTree FromXmlPooled(
            string xml,
            WhitespaceControl? whitespace,
            IXsltResolver? entityResolver,
            string? baseUri,
            TreeValidation? validation = null)
        {
            EntityResolverAdapter? entities = entityResolver is null ? null : new EntityResolverAdapter(entityResolver, baseUri);
            using XmlReader xmlReader = XmlReader.Create(new StringReader(xml), HardenedSettings(entities, validation));
            return Build(
                xmlReader, null, whitespace, false, entities, baseUri,
                EstimateNodeCount(xml), EstimateAttributeCount(xml), pooledStorage: true, trackEntityBases: entities is not null,
                validation: validation);
        }

        /// <summary>Builds a tree from a reader for a transformation that will release it.</summary>
        internal static XdmTree FromXmlPooled(
            TextReader reader,
            WhitespaceControl? whitespace,
            IXsltResolver? entityResolver,
            string? baseUri,
            TreeValidation? validation = null)
        {
            EntityResolverAdapter? entities = entityResolver is null ? null : new EntityResolverAdapter(entityResolver, baseUri);
            using XmlReader xmlReader = XmlReader.Create(reader, HardenedSettings(entities, validation));
            return Build(
                xmlReader, null, whitespace, false, entities, baseUri,
                InitialNodeCapacity, InitialAttributeCapacity, pooledStorage: true, trackEntityBases: entities is not null,
                validation: validation);
        }

        /// <summary>Builds a tree from a stream for a transformation that will release it.</summary>
        internal static XdmTree FromXmlPooled(
            Stream stream,
            WhitespaceControl? whitespace,
            IXsltResolver? entityResolver,
            string? baseUri,
            TreeValidation? validation = null)
        {
            EntityResolverAdapter? entities = entityResolver is null ? null : new EntityResolverAdapter(entityResolver, baseUri);
            (int nodes, int attributes) = EstimateFromStream(stream);
            using XmlReader xmlReader = XmlReader.Create(stream, HardenedSettings(entities, validation));
            return Build(
                xmlReader, null, whitespace, false, entities, baseUri, nodes, attributes, pooledStorage: true,
                trackEntityBases: entities is not null, validation: validation);
        }

        /// <summary>
        /// Guesses how many nodes a document will make from its text, before it is parsed.
        /// </summary>
        /// <remarks>
        /// Every tag starts with a <c>&lt;</c>, and what a start tag begets is one element and, more often
        /// than not, one text node after it; an end tag begets at most the text node after it. So the count
        /// of <c>&lt;</c> is a floor of sorts and twice it a ceiling, and one and a half times it lands
        /// close for the indented documents that are the common case. An estimate that is too small costs
        /// a doubling, and one too large a trim — either being what every document paid before.
        /// </remarks>
        private static int EstimateNodeCount(string xml)
        {
            long tags = xml.AsSpan().Count('<');
            return (int)Math.Min(tags + tags / 2 + 16, int.MaxValue / 2);
        }

        /// <summary>
        /// Guesses how many attributes a document carries from its text: every attribute has an equals
        /// sign, and outside of attributes the character is rare.
        /// </summary>
        private static int EstimateAttributeCount(string xml)
        {
            return (int)Math.Min((long)xml.AsSpan().Count('=') + 8, int.MaxValue / 2);
        }

        /// <summary>
        /// The reader settings this engine builds its own readers with.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The resolver is the lock, and it is why a caller who supplies their own <see cref="XmlReader"/>
        /// is supplying its security posture along with it: the resolver is the one channel through which a
        /// document can cause anything to be read, and with none an external entity reference is not a way
        /// to read a local file or reach the network from inside a document — it expands to nothing, and an
        /// external subset is left unread. The declaration itself is parsed, so that a document with an
        /// internal subset, which reads nothing outside itself, is read as written rather than refused.
        /// </para>
        /// <para>
        /// Entity expansion is capped at the platform's default, which is what stops a declaration that
        /// expands a few entities into a billion characters.
        /// </para>
        /// </remarks>
        /// <summary>
        /// Reads far enough into a document to see whether it begins as XML, and no further.
        /// </summary>
        /// <remarks>
        /// What <c>fn:stream-available()</c> asks. Reading stops at the first element, so a document that
        /// is well formed for its first mile is available and one that is not XML at all is not: whatever
        /// is wrong with a truncated document, or with one that has two top-level elements, lies past the
        /// point a streamed read would have reached before it had to answer.
        /// </remarks>
        /// <param name="reader">The document text. The caller retains ownership and must dispose it.</param>
        /// <param name="entityResolver">What a document type declaration may reach, or null for nothing.</param>
        /// <param name="baseUri">What a reference inside the document resolves against.</param>
        /// <returns>Whether an element was reached without the document failing to parse.</returns>
        public static bool StartsAsXml(TextReader reader, IXsltResolver? entityResolver, string? baseUri)
        {
            EntityResolverAdapter? entities =
                entityResolver is null ? null : new EntityResolverAdapter(entityResolver, baseUri);

            try
            {
                using XmlReader xmlReader = XmlReader.Create(reader, HardenedSettings(entities));

                while (xmlReader.Read())
                {
                    if (xmlReader.NodeType == XmlNodeType.Element)
                    {
                        return true;
                    }
                }
            }
            catch (Exception error) when (error is XmlException or IOException or XsltException)
            {
                return false;
            }

            // The end of the document with no element in it: a document type declaration and nothing else
            // is not a document anything could be streamed from.
            return false;
        }

        private static XmlReaderSettings HardenedSettings(EntityResolverAdapter? entities, TreeValidation? validation = null)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Parse,
                XmlResolver = entities,
                MaxCharactersFromEntities = 10_000_000,
                IgnoreWhitespace = false,
                IgnoreComments = false,
                IgnoreProcessingInstructions = false,
                CloseInput = false,
            };

            if (validation is not null)
            {
                // Against the schemas in scope and those alone: neither an inline schema nor an
                // xsi:schemaLocation in the document adds to the set, which is shared by every
                // transformation over the stylesheet and would be changed under them. The xml:*
                // attributes are allowed on any element, as XSD has them, and identity constraints are
                // checked, being part of what valid means.
                settings.ValidationType = ValidationType.Schema;
                settings.Schemas = validation.Schemas;
                settings.ValidationFlags = System.Xml.Schema.XmlSchemaValidationFlags.ProcessIdentityConstraints
                    | System.Xml.Schema.XmlSchemaValidationFlags.AllowXmlAttributes;
                settings.ValidationEventHandler += validation.Report;
            }

            return settings;
        }

        /// <summary>
        /// Builds a tree by consuming an existing <see cref="XmlReader"/>.
        /// </summary>
        /// <param name="reader">The reader, positioned before the content to consume.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <param name="whitespace">Which whitespace-only text nodes to strip, or <see langword="null"/> to keep all.</param>
        /// <param name="locations">
        /// Whether to record the line and column each node starts at, where the reader knows them. A
        /// stylesheet asks, so that its instructions can say where they stand when they fail; a document
        /// does not.
        /// </param>
        /// <param name="entityResolver">
        /// What fetches the external subset of a document type declaration, so that the attributes it
        /// types as ID and the unparsed entities it declares are known; the reader's own settings decide
        /// what the reader itself fetches. <see langword="null"/>, the default, reads the internal subset alone.
        /// </param>
        /// <param name="baseUri">The document's base URI, which a declaration's relative references resolve against.</param>
        /// <returns>The parsed tree.</returns>
        public static XdmTree FromXml(
            XmlReader reader,
            NameTable? nameTable = null,
            WhitespaceControl? whitespace = null,
            bool locations = false,
            IXsltResolver? entityResolver = null,
            string? baseUri = null)
        {
            EntityResolverAdapter? entities = entityResolver is null ? null : new EntityResolverAdapter(entityResolver, baseUri);
            return Build(reader, nameTable, whitespace, locations, entities, baseUri, InitialNodeCapacity, InitialAttributeCapacity);
        }

        private static XdmTree Build(
            XmlReader reader,
            NameTable? nameTable,
            WhitespaceControl? whitespace,
            bool locations,
            EntityResolverAdapter? entities,
            string? baseUri,
            int nodeCapacity,
            int attributeCapacity,
            bool pooledStorage = false,
            bool trackEntityBases = true,
            TreeValidation? validation = null)
        {
            XdmTreeBuilder builder = new XdmTreeBuilder(nameTable, nodeCapacity, attributeCapacity, pooledStorage)
            {
                Whitespace = whitespace,
            };
            IXmlLineInfo? lines = locations && reader is IXmlLineInfo info && info.HasLineInfo() ? info : null;

            // For each open element of a validated document, whether its type admits elements and no
            // text: the whitespace in such an element is element content whitespace, which the data
            // model leaves out (XDM §6.7.3), as it does for an element a declaration says the same of.
            List<bool>? openElementOnly = validation is null ? null : new List<bool>();

            // The open elements' names as written, which a declaration's content models are keyed by, and
            // the base URI in force in each — the entity it was parsed out of, which the reader knows. Both
            // are kept only where something can ask: the names once a declaration has been read, and the
            // bases where the reader can reach an external entity at all. A reader this engine made with
            // nothing to resolve one never leaves the document, and asking it per node was a call and a
            // compare for every node of a document that could not have answered otherwise.
            List<string> openNames = new List<string>();
            List<string> openBases = new List<string>();

            // Whitespace-only text is read into this scratch space and matched against the last string seen
            // of the same length, so that an indented document's newline-and-indent, repeated once per
            // element, is one string per depth rather than one per occurrence.
            char[] scratch = new char[WhitespaceScratchSize];
            string?[] recentWhitespace = new string?[WhitespaceScratchSize + 1];

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        builder.StartElement(reader.Prefix, reader.NamespaceURI, reader.LocalName);
                        if (lines is not null)
                        {
                            // Before the attributes are copied, which moves the reader off the element.
                            builder.Locate(lines.LineNumber, lines.LinePosition);
                        }

                        // A node parsed out of an external entity has that entity's URI as its base (XDM
                        // §6.2.3), which is what a relative reference inside the entity means.
                        string entityBase = trackEntityBases
                            ? NoteEntityBase(builder, reader, openBases)
                            : string.Empty;

                        bool elementOnly = validation is not null && builder.AnnotateElement(reader, validation);

                        builder.CopyAttributes(reader, validation);
                        if (reader.IsEmptyElement)
                        {
                            builder.EndElement();
                        }
                        else
                        {
                            if (builder.HasDocumentType)
                            {
                                openNames.Add(reader.Name);
                            }

                            if (trackEntityBases)
                            {
                                openBases.Add(entityBase);
                            }

                            openElementOnly?.Add(elementOnly);
                        }

                        break;

                    case XmlNodeType.EndElement:
                        builder.EndElement();

                        if (openNames.Count > 0)
                        {
                            openNames.RemoveAt(openNames.Count - 1);
                        }

                        if (trackEntityBases)
                        {
                            openBases.RemoveAt(openBases.Count - 1);
                        }

                        openElementOnly?.RemoveAt(openElementOnly.Count - 1);
                        break;

                    case XmlNodeType.Whitespace:
                        // Whitespace outside the document element is not part of the document: XML's grammar
                        // allows comments and processing instructions around it and no character data at
                        // all. Keeping it made a document node with a text child, which the built-in rule
                        // then copied — a newline appearing in the result because the input file ended in one.
                        //
                        // A fragment has no document element to be outside of, and whitespace at its top is
                        // character data like any other: fn:parse-xml-fragment('  ') is a document node
                        // whose string value is two spaces, which the suite's parse-xml-fragment-008 asks
                        // for in as many words.
                        if (!builder.InsideElement
                            && reader.Settings?.ConformanceLevel != ConformanceLevel.Fragment)
                        {
                            break;
                        }

                        // Whitespace in an element the declaration gives element content only is element
                        // content whitespace, which the data model excludes (XDM §6.7.3).
                        if (openNames.Count > 0 && builder.HasElementOnlyContent(openNames[^1]))
                        {
                            break;
                        }

                        if (openElementOnly is { Count: > 0 } && openElementOnly[^1])
                        {
                            break;
                        }

                        builder.AddText(ReadWhitespace(reader, scratch, recentWhitespace));
                        if (trackEntityBases)
                        {
                            NoteEntityBase(builder, reader, openBases);
                        }

                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                        builder.AddText(reader.Value);
                        if (trackEntityBases)
                        {
                            NoteEntityBase(builder, reader, openBases);
                        }

                        break;

                    case XmlNodeType.Comment:
                        builder.AddComment(reader.Value);
                        if (trackEntityBases)
                        {
                            NoteEntityBase(builder, reader, openBases);
                        }

                        break;

                    case XmlNodeType.ProcessingInstruction:
                        builder.AddProcessingInstruction(reader.LocalName, reader.Value);
                        if (trackEntityBases)
                        {
                            NoteEntityBase(builder, reader, openBases);
                        }

                        break;

                    case XmlNodeType.DocumentType:
                        // The declaration is not a node of the data model, and what it declares the reader
                        // has already applied; what it says about attribute types and unparsed entities is
                        // read here, since the reader says nothing about either.
                        builder.ReadDocumentType(
                            reader.GetAttribute("SYSTEM"),
                            reader.Value,
                            entities is null ? null : entities.TextOf,
                            baseUri);
                        break;
                }
            }

            return builder.Finish();
        }

        /// <summary>
        /// Records the base URI of the node just added where the reader parsed it out of an external
        /// entity — where its base differs from that of the element it is in — and answers the base in force.
        /// </summary>
        private const int WhitespaceScratchSize = 128;

        /// <summary>
        /// Reads a whitespace-only text node without making a new string of it where the same whitespace
        /// was seen before.
        /// </summary>
        /// <remarks>
        /// The reader would hand the text over as a fresh string every time, and an indented document has
        /// one such node per element — the same newline and indentation, at each depth, thousands of times
        /// over. Reading the characters into scratch space instead costs a copy of a few characters, and
        /// what they match is kept by length, which is what tells one depth's indentation from another's.
        /// Whitespace longer than the scratch space, or a reader that cannot hand characters over, is read
        /// as it was.
        /// </remarks>
        private static string ReadWhitespace(XmlReader reader, char[] scratch, string?[] recent)
        {
            if (!reader.CanReadValueChunk)
            {
                return reader.Value;
            }

            int count = reader.ReadValueChunk(scratch, 0, scratch.Length);

            if (count == scratch.Length)
            {
                // Longer than the scratch space holds: read the rest into a builder rather than ask the
                // reader for the value it has been partly read out of.
                System.Text.StringBuilder rest = new System.Text.StringBuilder(count * 2);
                rest.Append(scratch, 0, count);

                int more;
                while ((more = reader.ReadValueChunk(scratch, 0, scratch.Length)) > 0)
                {
                    rest.Append(scratch, 0, more);
                }

                return rest.ToString();
            }

            ReadOnlySpan<char> read = scratch.AsSpan(0, count);
            string? kept = recent[count];

            if (kept is not null && read.SequenceEqual(kept))
            {
                return kept;
            }

            string fresh = new string(read);
            recent[count] = fresh;
            return fresh;
        }

        private static string NoteEntityBase(XdmTreeBuilder builder, XmlReader reader, List<string> openBases)
        {
            string entityBase = reader.BaseURI;
            string outerBase = openBases.Count == 0 ? string.Empty : openBases[^1];

            if (entityBase.Length != 0 && entityBase != outerBase)
            {
                builder.NoteEntityBase(entityBase);
            }

            return entityBase;
        }

        /// <summary>
        /// Reads a document type declaration for the attributes it types as ID and the unparsed entities it
        /// declares, which the attributes copied after it and the finished tree then carry.
        /// </summary>
        /// <param name="systemId">The system identifier of the external subset, or null where none was named.</param>
        /// <param name="internalSubset">The text of the internal subset, or an empty string.</param>
        /// <param name="fetch">What fetches an external entity by reference and base, or null to read nothing external.</param>
        /// <param name="baseUri">The document's base URI.</param>
        public void ReadDocumentType(
            string? systemId,
            string internalSubset,
            Func<string, string?, string?>? fetch,
            string? baseUri)
        {
            m_documentType = DocumentTypeDeclaration.Read(internalSubset, systemId, baseUri, fetch);
        }

        /// <summary>Whether the document type declaration gives an element element content only.</summary>
        /// <param name="element">The element's name as written.</param>
        public bool HasElementOnlyContent(string element)
        {
            return m_documentType is not null && m_documentType.ElementOnlyContent.Contains(element);
        }

        /// <summary>
        /// Records that the node most recently added was parsed out of an external entity with the given
        /// base URI, which is then the base of it and of everything under it that says nothing else.
        /// </summary>
        /// <param name="baseUri">The entity's URI.</param>
        public void NoteEntityBase(string baseUri)
        {
            (m_entityBase ??= new Dictionary<int, string>())[m_nodeCount - 1] = baseUri;
        }

        /// <summary>
        /// Gives this tree the unparsed entities of a document node copied into it, which a copy of a
        /// document node keeps (§5.7.1). The first document copied in settles them.
        /// </summary>
        /// <param name="source">The tree whose document node was copied.</param>
        public void InheritUnparsedEntities(XdmTree source)
        {
            if (source.UnparsedEntities is not null)
            {
                m_inheritedEntities ??= source.UnparsedEntities;
            }
        }

        /// <summary>
        /// Records where the node last appended starts in the text being parsed.
        /// </summary>
        /// <remarks>
        /// The arrays exist from the first record, so a tree never asked to record anything pays nothing
        /// for the option, and a node before the first record reads as line 0.
        /// </remarks>
        /// <param name="line">The line, counted from 1.</param>
        /// <param name="column">The column, counted from 1.</param>
        public void Locate(int line, int column)
        {
            if (m_line is null)
            {
                m_line = new int[m_kind.Length];
                m_column = new int[m_kind.Length];
            }

            m_line[m_nodeCount - 1] = line;
            m_column![m_nodeCount - 1] = column;
        }

        /// <summary>
        /// Starts an element and makes it the current parent.
        /// </summary>
        /// <param name="prefix">The prefix, or an empty string if unprefixed.</param>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <returns>The node id of the new element.</returns>
        public int StartElement(string prefix, string namespaceUri, string localName)
        {
            int id = AppendNode(NodeKind.Element, InternName(prefix, namespaceUri, localName), null);
            Push(id);
            m_startTagOpen = true;
            return id;
        }

        /// <summary>
        /// Whether an element's namespaces are completed when its start tag closes: a declaration for its
        /// own name and for each prefixed attribute where nothing in scope binds the prefix to that
        /// namespace, and, under a parent that does not pass its namespaces on, an undeclaration of each
        /// namespace the element did not declare for itself.
        /// </summary>
        /// <remarks>
        /// On for a tree an instruction builds, whose elements arrive with whatever namespace nodes the
        /// instruction chose to give them — none, for a copy with <c>copy-namespaces="no"</c> — and whose
        /// in-scope namespaces the data model still requires to cover every name in them. Off for a
        /// document being parsed, which is consistent as read and would pay a walk up the ancestors per
        /// element for nothing.
        /// </remarks>
        public bool FixesUpNamespaces { get; set; }

        /// <summary>Whether the element most recently started has had no content yet.</summary>
        private bool m_startTagOpen;

        /// <summary>The elements built with <c>inherit-namespaces="no"</c>, whose children take none of theirs.</summary>
        private HashSet<int>? m_noInherit;

        /// <summary>
        /// Records that the element being built does not pass its namespaces on to the children built
        /// inside it, which is what <c>inherit-namespaces="no"</c> asks.
        /// </summary>
        public void MarkNoInheritedNamespaces()
        {
            (m_noInherit ??= new HashSet<int>()).Add(RequireOpenElement());
        }

        /// <summary>Where a copied element takes its namespaces from, where that is not its parent.</summary>
        /// <remarks>
        /// Every element of a copy below the top of it. The value is the element the copy was attached
        /// to, which is where they all take them from.
        /// </remarks>
        private Dictionary<int, int>? m_attachedAt;

        /// <summary>
        /// Records that the element being built came from a copy, and so carries its own namespace nodes
        /// rather than adding to its parent's.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 §11.9.2: a copy takes the namespaces of the element it is attached to, and so do all
        /// of its descendants — from the point of attachment and not from the copied parent. So an element
        /// that undeclared the default namespace keeps that undeclaration through a copy of its parent,
        /// and a namespace the copied parent acquired at the copy — from fixup under
        /// <c>copy-namespaces="no"</c>, say — reaches nothing beneath it.
        /// </remarks>
        /// <param name="root">Whether this element is where the copy started, which takes its parent's.</param>
        public void MarkCopiedNamespaces(bool root)
        {
            if (root)
            {
                return;
            }

            int element = RequireOpenElement();
            int parent = m_parent[element];

            if (parent < 0)
            {
                return;
            }

            // The parent is the copy above this one: either the top of the copy, whose own parent is
            // where it was attached, or another element of it, which has been told already.
            m_attachedAt ??= new Dictionary<int, int>();
            m_attachedAt[element] = m_attachedAt.TryGetValue(parent, out int attachment)
                ? attachment
                : m_parent[parent];
        }

        /// <summary>
        /// Settles the namespaces of the element whose start tag is open, once anything follows it.
        /// </summary>
        /// <remarks>
        /// Called before any child is appended and before the element ends, so that what is added here
        /// stays inside the element's own run of declarations: a child's run begins where the count stands
        /// when the child is allocated.
        /// </remarks>
        private void CloseStartTag()
        {
            if (!m_startTagOpen)
            {
                return;
            }

            m_startTagOpen = false;

            if (FixesUpNamespaces)
            {
                FixUpNamespaces(m_openNodes[m_openDepth - 1]);
            }
        }

        private void FixUpNamespaces(int element)
        {
            int nameCode = m_nameCode[element];
            int fingerprint = NameTable.GetFingerprintOfNameCode(nameCode);
            string prefix = NameTable.GetPrefix(nameCode);
            string uri = NameTable.GetNamespaceUri(fingerprint);

            // A namespace node put on the element can have claimed the prefix the element's own name was
            // built with. The node stands and the name gives way to a prefix nothing is using, which is
            // what the specification asks for where the two collide (XSLT 3.0 §11.7).
            if (prefix.Length != 0
                && DeclaredHere(element, prefix) is string claimed
                && !string.Equals(claimed, uri, StringComparison.Ordinal))
            {
                prefix = UnusedPrefix(element, prefix);
                m_nameCode[element] = NameTable.GetNameCode(
                    prefix, uri, NameTable.GetLocalName(fingerprint));
            }

            RequireBinding(element, prefix, uri);

            int start = m_attrStart[element];
            for (int i = 0; i < m_attrCount[element]; i++)
            {
                int attributeCode = m_attributeNameCode[start + i];
                string attributePrefix = NameTable.GetPrefix(attributeCode);

                if (attributePrefix.Length != 0)
                {
                    int attributeName = NameTable.GetFingerprintOfNameCode(attributeCode);
                    string attributeUri = NameTable.GetNamespaceUri(attributeName);

                    // The same collision as the element's own name, and the same answer. An attribute's
                    // prefix is a spelling of its namespace and nothing more, and the namespace is what
                    // xsl:attribute was told to put it in — so where the prefix is spoken for on this
                    // element, the attribute takes one nothing is using rather than the element binding
                    // one prefix to two namespaces.
                    if (DeclaredHere(element, attributePrefix) is string bound
                        && !string.Equals(bound, attributeUri, StringComparison.Ordinal))
                    {
                        attributePrefix = UnusedPrefix(element, attributePrefix);
                        m_attributeNameCode[start + i] = NameTable.GetNameCode(
                            attributePrefix, attributeUri, NameTable.GetLocalName(attributeName));
                    }

                    RequireBinding(element, attributePrefix, attributeUri);
                }
            }

            int parent = m_parent[element];

            if (parent >= 0 && m_noInherit is not null && m_noInherit.Contains(parent))
            {
                // Each namespace the parent has in scope and the element did not declare for itself is
                // undeclared on the element, which is how a tree says that a child inherited nothing.
                foreach ((string above, string bound) in InScopeWhileBuilding(parent))
                {
                    if (bound.Length != 0 && !DeclaresPrefix(element, above))
                    {
                        AppendDeclaration(element, above, string.Empty);
                    }
                }
            }
            else if (m_attachedAt is not null && m_attachedAt.TryGetValue(element, out int attachment))
            {
                // An element of a copy takes its namespaces from where the copy was attached, so what
                // the copied parent declared for itself and the attachment does not have is undeclared
                // here. Read into a list first: appending to this element's run moves the arrays the
                // parent's is read from.
                List<(string Prefix, string Uri)> own = new List<(string, string)>();
                int first = m_namespaceStart[parent];

                for (int i = first; i < first + m_namespaceCount[parent]; i++)
                {
                    own.Add((m_namespacePrefix[i], m_namespaceUri[i]));
                }

                foreach ((string above, string bound) in own)
                {
                    if (bound.Length != 0
                        && !DeclaresPrefix(element, above)
                        && ResolveWhileBuilding(attachment, above) != bound)
                    {
                        AppendDeclaration(element, above, string.Empty);
                    }
                }
            }
        }

        /// <summary>What a prefix is bound to by the element's own declarations, or null where it has none.</summary>
        private string? DeclaredHere(int element, string prefix)
        {
            int start = m_namespaceStart[element];
            int end = start + m_namespaceCount[element];

            for (int i = start; i < end; i++)
            {
                if (string.Equals(m_namespacePrefix[i], prefix, StringComparison.Ordinal))
                {
                    return m_namespaceUri[i];
                }
            }

            return null;
        }

        /// <summary>A prefix in the shape of the one asked for that nothing in scope has taken.</summary>
        /// <summary>
        /// A prefix nothing has at an element, derived from the one that was asked for.
        /// </summary>
        /// <remarks>
        /// Keeping what was written as the stem rather than inventing something unrelated: the prefix is
        /// arbitrary either way, and one that still reads as the author's is easier to follow in the result.
        /// </remarks>
        /// <param name="element">The element the prefix has to be free at.</param>
        /// <param name="taken">The prefix something else has claimed.</param>
        private string UnusedPrefix(int element, string taken)
        {
            for (int suffix = 1; ; suffix++)
            {
                string candidate = taken + "_"
                    + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);

                if (ResolveWhileBuilding(element, candidate) is null)
                {
                    return candidate;
                }
            }
        }

        private void RequireBinding(int element, string prefix, string uri)
        {
            if (prefix == "xml" || ResolveWhileBuilding(element, prefix) == uri)
            {
                return;
            }

            AppendDeclaration(element, prefix, uri);
        }

        /// <summary>What a prefix is bound to at an element under construction, walking its ancestors.</summary>
        /// <remarks>
        /// Ancestors as the namespaces run rather than as the tree does. A parent that passes none of
        /// them on ends the walk; an element of a copy continues it at the element the copy was attached
        /// to, that being where every element of the copy takes its namespaces from.
        /// </remarks>
        /// <returns>The namespace, an empty string for the default namespace when there is none, or null for a prefix nothing binds.</returns>
        private string? ResolveWhileBuilding(int element, string prefix)
        {
            for (int node = element; node >= 0; node = Above(node))
            {
                int start = m_namespaceStart[node];
                int end = start + m_namespaceCount[node];

                for (int i = start; i < end; i++)
                {
                    if (string.Equals(m_namespacePrefix[i], prefix, StringComparison.Ordinal))
                    {
                        string uri = m_namespaceUri[i];
                        return uri.Length == 0 && prefix.Length != 0 ? null : uri;
                    }
                }

                if (m_noInherit is not null && m_parent[node] >= 0 && m_noInherit.Contains(m_parent[node]))
                {
                    break;
                }
            }

            return prefix.Length == 0 ? string.Empty : null;
        }

        /// <summary>Where the namespaces in scope at an element continue, which is not always its parent.</summary>
        /// <param name="element">The element.</param>
        private int Above(int element)
        {
            return m_attachedAt is not null && m_attachedAt.TryGetValue(element, out int attachment)
                ? attachment
                : m_parent[element];
        }

        private IEnumerable<(string Prefix, string Uri)> InScopeWhileBuilding(int element)
        {
            Dictionary<string, string> bindings = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int node = element; node >= 0; node = m_parent[node])
            {
                int start = m_namespaceStart[node];
                int end = start + m_namespaceCount[node];

                for (int i = start; i < end; i++)
                {
                    bindings.TryAdd(m_namespacePrefix[i], m_namespaceUri[i]);
                }
            }

            foreach (KeyValuePair<string, string> binding in bindings)
            {
                yield return (binding.Key, binding.Value);
            }
        }

        private bool DeclaresPrefix(int element, string prefix)
        {
            int start = m_namespaceStart[element];
            int end = start + m_namespaceCount[element];

            for (int i = start; i < end; i++)
            {
                if (string.Equals(m_namespacePrefix[i], prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void AppendDeclaration(int element, string prefix, string uri)
        {
            if (m_namespaceDeclarationCount == m_namespacePrefix.Length)
            {
                int capacity = m_namespaceDeclarationCount * 2;
                Array.Resize(ref m_namespacePrefix, capacity);
                Array.Resize(ref m_namespaceUri, capacity);
            }

            m_namespacePrefix[m_namespaceDeclarationCount] = prefix;
            m_namespaceUri[m_namespaceDeclarationCount] = uri;
            m_namespaceDeclarationCount++;
            m_namespaceCount[element]++;
        }

        /// <summary>
        /// Adds an attribute to the element being built.
        /// </summary>
        /// <param name="prefix">The prefix, or an empty string if unprefixed.</param>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <param name="value">The attribute value.</param>
        /// <exception cref="InvalidOperationException">
        /// No element is open, or a child has already been added to it.
        /// </exception>
        /// <summary>
        /// Adds an attribute that belongs to no element, and returns the id it will have.
        /// </summary>
        /// <remarks>
        /// XSLT 2.0 lets a sequence hold a parentless attribute: <c>&lt;xsl:variable as="attribute()"&gt;</c>
        /// with an <c>xsl:attribute</c> inside it produces exactly one, and the whole point of declaring the
        /// type is to say that the attribute is the value rather than something to attach. Its owner is
        /// recorded as -1, which is what <see cref="XdmTree.ParentOf"/> then answers: no parent, as the data
        /// model says.
        /// </remarks>
        /// <param name="prefix">The preferred prefix, or an empty string.</param>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <param name="value">The attribute's value.</param>
        /// <summary>The first node built directly under the document node, or -1 where none has been.</summary>
        public int FirstTopLevelNode => m_firstChild[XdmTree.RootNode];

        /// <summary>
        /// Takes the nodes built so far out of the document node, leaving each of them a root of its own.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What an <c>as</c> declaration asks for. A sequence constructor without one builds a document and
        /// everything it makes is inside it; with one it produces a <em>sequence</em>, and the specification
        /// says the nodes in that sequence are parentless. So <c>$e/..</c> is empty and <c>root($e)</c> is
        /// <c>$e</c>, where before both answered about a document node the stylesheet never asked for.
        /// </para>
        /// <para>
        /// The document node stays in the arrays as node zero, unreferenced. Renumbering to remove it would
        /// mean rewriting every parent, sibling and subtree index in the tree to save one slot, and nothing
        /// reaches it once it has no children.
        /// </para>
        /// </remarks>
        public void Detach()
        {
            for (int node = m_firstChild[XdmTree.RootNode]; node >= 0;)
            {
                int next = m_next[node];

                m_parent[node] = -1;
                m_next[node] = -1;
                m_depth[node] = 0;
                node = next;
            }

            m_firstChild[XdmTree.RootNode] = -1;
            m_openLastChild[0] = -1;
        }

        public int AddParentlessAttribute(string prefix, string namespaceUri, string localName, string value)
        {
            if (m_attributeCount == m_attributeNameCode.Length)
            {
                int capacity = m_attributeCount * 2;
                Array.Resize(ref m_attributeNameCode, capacity);
                Array.Resize(ref m_attributeValue, capacity);
                Array.Resize(ref m_attributeOwner, capacity);

                if (m_attributeType is not null)
                {
                    Array.Resize(ref m_attributeType, capacity);
                }
            }

            m_attributeNameCode[m_attributeCount] = InternName(prefix, namespaceUri, localName);
            m_attributeValue[m_attributeCount] = value;
            m_attributeOwner[m_attributeCount] = -1;

            if (m_attributeType is not null)
            {
                m_attributeType[m_attributeCount] = 0;
            }

            return XdmTree.AttributeIdBase + m_attributeCount++;
        }

        /// <summary>
        /// Adds a namespace node with no parent, which is what an <c>xsl:namespace</c> or a copied namespace
        /// node becomes where no element is open: an item of the sequence, in a tree of its own.
        /// </summary>
        /// <remarks>
        /// Held in the attribute arrays, as every namespace node is, and told apart by standing beyond the
        /// count of attributes — so it has to be the last entry, which a tree holding only it makes sure of.
        /// </remarks>
        /// <param name="prefix">The prefix, or an empty string for the default namespace.</param>
        /// <param name="uri">The namespace URI, which is the node's string value.</param>
        /// <returns>The node id.</returns>
        public int AddParentlessNamespaceNode(string prefix, string uri)
        {
            int id = AddParentlessAttribute(string.Empty, string.Empty, prefix, uri);
            m_parentlessNamespaceNodes++;
            return id;
        }

        public void AddAttribute(string prefix, string namespaceUri, string localName, string value)
        {
            m_lastWasAtomic = false;
            int element = RequireOpenElement();
            if (m_attrStart[element] + m_attrCount[element] != m_attributeCount)
            {
                throw new InvalidOperationException(
                    "Attributes must be added before any child of the element they belong to.");
            }

            if (m_attributeCount == m_attributeNameCode.Length)
            {
                int capacity = m_attributeCount * 2;
                Array.Resize(ref m_attributeNameCode, capacity);
                Array.Resize(ref m_attributeValue, capacity);
                Array.Resize(ref m_attributeOwner, capacity);

                if (m_attributeType is not null)
                {
                    Array.Resize(ref m_attributeType, capacity);
                }
            }

            if (localName == "space" && namespaceUri == XdmTree.XmlNamespaceUri)
            {
                m_preserveSpace[m_openDepth - 1] = value == "preserve";
            }

            int nameCode = InternName(prefix, namespaceUri, localName);

            // A second attribute of one name on an element replaces the first (§5.7.1): what an xsl:copy-of
            // writes after an xsl:attribute of the same name is the value that stands, and the element has
            // one attribute of the name either way. The first is taken out and the second goes last, which
            // is the order the serializer keeps for the same case.
            int fingerprint = NameTable.GetFingerprintOfNameCode(nameCode);
            int end = m_attrStart[element] + m_attrCount[element];

            for (int i = m_attrStart[element]; i < end; i++)
            {
                if (NameTable.GetFingerprintOfNameCode(m_attributeNameCode[i]) != fingerprint)
                {
                    continue;
                }

                for (int j = i + 1; j < end; j++)
                {
                    m_attributeNameCode[j - 1] = m_attributeNameCode[j];
                    m_attributeValue[j - 1] = m_attributeValue[j];

                    if (m_attributeType is not null)
                    {
                        m_attributeType[j - 1] = m_attributeType[j];
                    }
                }

                m_attributeCount--;
                m_attrCount[element]--;
                break;
            }

            m_attributeNameCode[m_attributeCount] = nameCode;
            m_attributeValue[m_attributeCount] = value;
            m_attributeOwner[m_attributeCount] = element;

            if (m_attributeType is not null)
            {
                m_attributeType[m_attributeCount] = 0;
            }

            m_attributeCount++;
            m_attrCount[element]++;
        }

        /// <summary>
        /// Records a namespace declaration made on the element being built.
        /// </summary>
        /// <param name="prefix">The prefix being bound, or an empty string for the default namespace.</param>
        /// <param name="uri">The namespace URI, or an empty string to un-declare the prefix.</param>
        /// <exception cref="InvalidOperationException">
        /// No element is open, or a child has already been added to it.
        /// </exception>
        public void AddNamespaceDeclaration(string prefix, string uri)
        {
            m_lastWasAtomic = false;
            int element = RequireOpenElement();
            if (m_namespaceStart[element] + m_namespaceCount[element] != m_namespaceDeclarationCount)
            {
                throw new InvalidOperationException(
                    "Namespace declarations must be added before any child of the element they belong to.");
            }

            if (m_namespaceDeclarationCount == m_namespacePrefix.Length)
            {
                int capacity = m_namespaceDeclarationCount * 2;
                Array.Resize(ref m_namespacePrefix, capacity);
                Array.Resize(ref m_namespaceUri, capacity);
            }

            m_namespacePrefix[m_namespaceDeclarationCount] = prefix;
            m_namespaceUri[m_namespaceDeclarationCount] = uri;
            m_namespaceDeclarationCount++;
            m_namespaceCount[element]++;
        }

        /// <summary>
        /// Ends the element currently being built.
        /// </summary>
        /// <exception cref="InvalidOperationException">No element is open.</exception>
        public void EndElement()
        {
            m_lastWasAtomic = false;
            CloseStartTag();
            int element = RequireOpenElement();
            m_subtreeEnd[element] = m_nodeCount - 1;
            m_openDepth--;
        }

        /// <summary>
        /// Adds character data. Text added immediately after other text is merged into a single node, as the
        /// XPath data model requires.
        /// </summary>
        /// <param name="text">The characters to add. An empty string is ignored.</param>
        /// <param name="keepEmpty">
        /// Whether a zero-length text node is added rather than dropped, which a sequence may hold where a
        /// tree may not.
        /// </param>
        /// <summary>The text nodes added with output escaping disabled.</summary>
        private HashSet<int>? m_rawText;

        /// <summary>Adds a text node written with output escaping disabled, and remembers that it was.</summary>
        /// <param name="text">The text.</param>
        public void AddRawText(string text)
        {
            int before = m_nodeCount;
            AddText(text, keepEmpty: true);

            if (m_nodeCount > before)
            {
                (m_rawText ??= new HashSet<int>()).Add(m_nodeCount - 1);
            }
        }

        public void AddText(string text, bool keepEmpty = false)
        {
            if (text.Length == 0 && !keepEmpty)
            {
                m_lastWasAtomic = false;
                return;
            }

            if (ShouldStripWhitespace(text))
            {
                return;
            }

            // Any text node, a zero-length one included, ends a run of atomic values: the space between two
            // of them is for values with nothing at all between, which the suite's on-empty-113a settles.
            m_lastWasAtomic = false;

            int lastChild = m_openLastChild[m_openDepth - 1];
            if (lastChild >= 0 && m_kind[lastChild] == NodeKind.Text)
            {
                m_value[lastChild] += text;
                return;
            }

            AppendNode(NodeKind.Text, NameTable.NoNameCode, text);
        }

        /// <summary>Whether the last thing added was an atomic value, with nothing after it yet.</summary>
        private bool m_lastWasAtomic;

        /// <summary>
        /// Adds an atomic value as text, a single space before it where an atomic value came just before.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 §5.7.1: adjacent atomic values in what a sequence constructor produces become one text
        /// node, their strings joined with single spaces — so two empty strings make a text node holding one
        /// space, and <c>1 to 3</c> reads <c>1 2 3</c>. Any node between them breaks the run, except a
        /// zero-length text node, which is discarded first.
        /// </remarks>
        /// <param name="text">The value as a string.</param>
        public void AddAtomic(string text)
        {
            if (m_lastWasAtomic)
            {
                AddText(" ");
            }

            AddText(text);
            m_lastWasAtomic = true;
        }

        /// <summary>
        /// Ends a run of adjacent atomic values, an item having come between them that adds nothing to
        /// the tree.
        /// </summary>
        /// <remarks>
        /// A document node copied in is such an item: its children are what it contributes, and where it
        /// has none it contributes nothing at all — but it stood between the two values, so they were
        /// never adjacent and no space goes between them.
        /// </remarks>
        public void EndAtomicRun()
        {
            m_lastWasAtomic = false;
        }

        /// <summary>Adds a comment node.</summary>
        /// <param name="text">The comment's character data.</param>
        public void AddComment(string text)
        {
            m_lastWasAtomic = false;
            AppendNode(NodeKind.Comment, NameTable.NoNameCode, text);
        }

        /// <summary>Adds a processing instruction node.</summary>
        /// <param name="target">The target, held as the local part of the node's name.</param>
        /// <param name="data">The instruction's character data.</param>
        public void AddProcessingInstruction(string target, string data)
        {
            m_lastWasAtomic = false;
            AppendNode(
                NodeKind.ProcessingInstruction,
                InternName(string.Empty, string.Empty, target),
                data);
        }

        /// <summary>Where the copied nodes came from, or null while no node has been noted as copied.</summary>
        private Dictionary<int, (XdmTree Tree, int Node)>? m_copiedFrom;

        /// <summary>The node most recently noted as copied, so that a copy adding no node is not noted twice.</summary>
        private int m_lastNoted = -1;

        /// <summary>
        /// Records that the node most recently added was copied, with its accumulator values, from a node
        /// of another tree.
        /// </summary>
        /// <param name="source">The tree it was copied from.</param>
        /// <param name="node">The node it was copied from.</param>
        public void NoteCopiedFrom(XdmTree source, int node)
        {
            int copy = m_nodeCount - 1;

            // A copy that added no node — whitespace this builder stripped, text that merged into the text
            // before it — has nothing of its own to remember, and must not overwrite what the node before it
            // remembered.
            if (copy < 0 || copy == m_lastNoted)
            {
                return;
            }

            m_lastNoted = copy;
            (m_copiedFrom ??= new Dictionary<int, (XdmTree, int)>())[copy] = (source, node);
        }

        /// <summary>
        /// Completes the tree. The builder cannot be used afterwards.
        /// </summary>
        /// <returns>The finished, immutable tree.</returns>
        /// <exception cref="InvalidOperationException">An element was left unclosed, or the builder was already finished.</exception>
        public XdmTree Finish()
        {
            if (m_finished)
            {
                throw new InvalidOperationException("This builder has already produced a tree.");
            }

            if (m_openDepth != 1)
            {
                throw new InvalidOperationException(
                    $"{m_openDepth - 1} element(s) were left unclosed when the tree was finished.");
            }

            m_finished = true;
            m_subtreeEnd[XdmTree.RootNode] = m_nodeCount - 1;
            m_openDepth = 0;

            // Trimming copies every array again — on a large document that is a second pass over the whole
            // tree. The arrays are only ever indexed below their logical count, so slack is harmless; reclaim
            // it only when there is enough of it to be worth the copy. Pooled storage is never trimmed: the
            // slack goes back to the pool with the rest when the tree is released.
            if (!m_pooled && IsWorthTrimming(m_nodeCount, m_kind.Length))
            {
                Array.Resize(ref m_kind, m_nodeCount);
                Array.Resize(ref m_nameCode, m_nodeCount);
                Array.Resize(ref m_parent, m_nodeCount);
                Array.Resize(ref m_firstChild, m_nodeCount);
                Array.Resize(ref m_next, m_nodeCount);
                Array.Resize(ref m_subtreeEnd, m_nodeCount);
                Array.Resize(ref m_depth, m_nodeCount);
                Array.Resize(ref m_value, m_nodeCount);
                Array.Resize(ref m_attrStart, m_nodeCount);
                Array.Resize(ref m_attrCount, m_nodeCount);
                Array.Resize(ref m_namespaceStart, m_nodeCount);
                Array.Resize(ref m_namespaceCount, m_nodeCount);

                if (m_line is not null)
                {
                    Array.Resize(ref m_line, m_nodeCount);
                    Array.Resize(ref m_column, m_nodeCount);
                }

                if (m_nodeType is not null)
                {
                    Array.Resize(ref m_nodeType, m_nodeCount);
                }
            }

            if (IsWorthTrimming(m_attributeCount, m_attributeNameCode.Length))
            {
                Array.Resize(ref m_attributeNameCode, m_attributeCount);
                Array.Resize(ref m_attributeValue, m_attributeCount);
                Array.Resize(ref m_attributeOwner, m_attributeCount);

                if (m_attributeType is not null)
                {
                    Array.Resize(ref m_attributeType, m_attributeCount);
                }
            }

            if (IsWorthTrimming(m_namespaceDeclarationCount, m_namespacePrefix.Length))
            {
                Array.Resize(ref m_namespacePrefix, m_namespaceDeclarationCount);
                Array.Resize(ref m_namespaceUri, m_namespaceDeclarationCount);
            }

            XdmTree tree = new XdmTree(
                NameTable,
                m_nodeCount,
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
                m_attributeCount,
                m_attributeNameCode,
                m_attributeValue,
                m_attributeOwner,
                m_namespacePrefix,
                m_namespaceUri,
                m_line,
                m_column,
                m_parentlessNamespaceNodes,
                m_pooled)
            {
                CopiedFrom = m_copiedFrom,
                RawText = m_rawText,
                DeclaredIdAttributes = m_idAttributeEntries?.ToArray() ?? Array.Empty<int>(),
                DeclaredIdrefAttributes = m_idrefAttributeEntries?.ToArray() ?? Array.Empty<int>(),
                UnparsedEntities = m_documentType is { UnparsedEntities.Count: > 0 }
                    ? m_documentType.UnparsedEntities
                    : m_inheritedEntities,
                EntityBases = m_entityBase,
                NodeTypes = m_nodeType,
                AttributeTypes = m_attributeType,
                NilledNodes = m_nilled,
                SchemaAnchor = m_schemaAnchor,
                IdElements = m_idElements,
                IdrefElements = m_idrefElements,
            };

            return tree;
        }

        /// <summary>
        /// Returns whether reclaiming an array's unused tail is worth copying the array to do it. Growth
        /// doubles, so the expected waste is under a third; only a large absolute saving justifies the copy.
        /// </summary>
        private static bool IsWorthTrimming(int used, int capacity)
        {
            return capacity - used > 1024 && capacity > used * 5 / 4;
        }

        /// <summary>Whether a document type declaration has been read, so that its lists can be asked.</summary>
        internal bool HasDocumentType => m_documentType is not null;

        /// <summary>
        /// Reads what the validating reader settled for the element just started, and annotates the
        /// element with it.
        /// </summary>
        /// <remarks>
        /// The reader knows the element's declaration, its type and whether it is nilled at the start
        /// tag, which is where this is called; whether its content turns out valid it knows only at the
        /// end tag, and says through the validation event handler.
        /// </remarks>
        /// <param name="reader">The reader, positioned on the element.</param>
        /// <param name="validation">What the document is validated against.</param>
        /// <returns>Whether the element's type admits elements and no text.</returns>
        private bool AnnotateElement(XmlReader reader, TreeValidation validation)
        {
            m_schemaAnchor ??= validation;

            System.Xml.Schema.IXmlSchemaInfo? info = reader.SchemaInfo;

            if (info is null)
            {
                return false;
            }

            System.Xml.Schema.XmlSchemaType? type = info.SchemaType;

            // Strict validation is of the document element against a top-level declaration, and an
            // element with none is the error the specification names; below the top, what a declaration
            // admits is the schema's own business, and an undeclared element is what a wildcard let in.
            if (type is null && info.SchemaElement is null && validation.Strict && OpenElementDepth == 1)
            {
                throw TreeValidation.Undeclared(reader.NamespaceURI, reader.LocalName);
            }

            if (validation.Annotate && (type is not null || info.IsNil))
            {
                AnnotateElement(type is null ? (ushort)0 : validation.TypeIds(type), info.IsNil);
            }
            else if (!validation.Annotate && type is not null && XPath.XdmSchemaType.ById(validation.TypeIds(type)) is XPath.XdmSchemaType stripped)
            {
                // Stripped of its annotation, an element whose content is an ID, or a reference to one,
                // keeps being one (XSLT 3.0 §4.4), as an attribute does.
                XPath.XdmSchemaType? content = stripped.Variety == XPath.XdmSchemaVariety.Complex
                    ? stripped.Content == System.Xml.Schema.XmlSchemaContentType.TextOnly ? stripped.SimpleContent : null
                    : stripped;

                if (content is { IsIdType: true })
                {
                    (m_idElements ??= new HashSet<int>()).Add(m_nodeCount - 1);
                }
                else if (content is { IsIdrefType: true })
                {
                    (m_idrefElements ??= new HashSet<int>()).Add(m_nodeCount - 1);
                }
            }

            return type is System.Xml.Schema.XmlSchemaComplexType { ContentType: System.Xml.Schema.XmlSchemaContentType.ElementOnly };
        }

        /// <summary>
        /// Records that the element most recently started is an ID, or a reference to one, without
        /// annotating it with the type that says so.
        /// </summary>
        /// <remarks>What a copy made under a validation of strip leaves behind; see
        /// <see cref="MarkLastAttributeAsId"/>, which does the same for an attribute.</remarks>
        /// <param name="reference">Whether it is a reference to an ID rather than an ID.</param>
        internal void MarkOpenElementAsId(bool reference)
        {
            int element = RequireOpenElement();

            if (reference)
            {
                (m_idrefElements ??= new HashSet<int>()).Add(element);
            }
            else
            {
                (m_idElements ??= new HashSet<int>()).Add(element);
            }
        }

        /// <summary>The elements a schema typed as an ID or a reference, kept where the annotations were not.</summary>
        private HashSet<int>? m_idElements;
        private HashSet<int>? m_idrefElements;

        /// <summary>
        /// Annotates the element most recently started with the type it was validated as, and whether
        /// it is nilled.
        /// </summary>
        /// <param name="typeId">The type's number, from <c>XdmSchemaType.Id</c>, or 0 for none.</param>
        /// <param name="nilled">Whether the element is nilled.</param>
        internal void AnnotateElement(ushort typeId, bool nilled)
        {
            int element = RequireOpenElement();

            if (typeId != 0)
            {
                m_nodeType ??= new ushort[m_kind.Length];
                m_nodeType[element] = typeId;
            }

            if (nilled)
            {
                (m_nilled ??= new HashSet<int>()).Add(element);
            }
        }

        /// <summary>Annotates the attribute most recently added with the type it was validated as.</summary>
        /// <param name="typeId">The type's number, from <c>XdmSchemaType.Id</c>, or 0 for none.</param>
        internal void AnnotateLastAttribute(ushort typeId)
        {
            if (typeId == 0 || m_attributeCount == 0)
            {
                return;
            }

            m_attributeType ??= new ushort[m_attributeNameCode.Length];
            m_attributeType[m_attributeCount - 1] = typeId;
        }

        /// <summary>
        /// Records that the attribute most recently added is an ID, or a reference to one, without
        /// annotating it with the type that says so.
        /// </summary>
        /// <remarks>
        /// What <c>validation="strip"</c> leaves behind. §25.4.1 replaces the type annotation of everything
        /// inside a stripped element, and then says "The values of the <c>is-id</c> and <c>is-idrefs</c>
        /// properties are unchanged" — a stylesheet can no longer ask what type an attribute was validated
        /// as, and can still find it with <c>id()</c>. Everywhere else the two properties are read off the
        /// annotation; with no annotation left to read them off they are recorded the way a document type
        /// declaration's are.
        /// </remarks>
        /// <param name="reference">Whether it is a reference to an ID rather than an ID.</param>
        internal void MarkLastAttributeAsId(bool reference)
        {
            if (m_attributeCount == 0)
            {
                return;
            }

            // Appended in the order the attributes are added, which is the ascending order the lists are
            // searched in.
            List<int> entries = reference
                ? m_idrefAttributeEntries ??= new List<int>()
                : m_idAttributeEntries ??= new List<int>();

            entries.Add(m_attributeCount - 1);
        }

        private void CopyAttributes(XmlReader reader, TreeValidation? validation = null)
        {
            // The element's name as written, which is what a declaration's attribute list is keyed by —
            // and wanted only where there is a declaration to key by it.
            string? element = m_documentType is null ? null : reader.Name;

            if (!reader.MoveToFirstAttribute())
            {
                return;
            }

            bool annotate = validation is { Annotate: true };

            do
            {
                if (string.Equals(reader.NamespaceURI, XmlnsNamespaceUri, StringComparison.Ordinal))
                {
                    // xmlns="..." arrives with an empty prefix and the local name "xmlns";
                    // xmlns:p="..." arrives with the prefix "xmlns" and the local name "p".
                    string declaredPrefix = reader.Prefix.Length == 0 ? string.Empty : reader.LocalName;
                    AddNamespaceDeclaration(declaredPrefix, reader.Value);
                }
                else
                {
                    AddAttribute(reader.Prefix, reader.NamespaceURI, reader.LocalName, reader.Value);

                    if (validation is not null && reader.SchemaInfo is { SchemaType: System.Xml.Schema.XmlSchemaType attributeType })
                    {
                        ushort typeId = validation.TypeIds(attributeType);

                        if (annotate)
                        {
                            AnnotateLastAttribute(typeId);
                        }
                        else if (XPath.XdmSchemaType.ById(typeId) is XPath.XdmSchemaType type)
                        {
                            // Stripped of its annotation, an attribute keeps whether it is an ID or a
                            // reference to one (XSLT 3.0 §4.4): the properties are the tree's, not the
                            // annotation's, and are kept the way a declaration's are.
                            if (type.IsIdType)
                            {
                                (m_idAttributeEntries ??= new List<int>()).Add(m_attributeCount - 1);
                            }
                            else if (type.IsIdrefType)
                            {
                                (m_idrefAttributeEntries ??= new List<int>()).Add(m_attributeCount - 1);
                            }
                        }
                    }

                    if (m_documentType is not null)
                    {
                        if (m_documentType.IdAttributes.Contains((element!, reader.Name)))
                        {
                            (m_idAttributeEntries ??= new List<int>()).Add(m_attributeCount - 1);
                        }
                        else if (m_documentType.IdrefAttributes.Contains((element!, reader.Name)))
                        {
                            (m_idrefAttributeEntries ??= new List<int>()).Add(m_attributeCount - 1);
                        }
                    }
                }
            }
            while (reader.MoveToNextAttribute());

            reader.MoveToElement();
        }

        private int AppendNode(NodeKind kind, int nameCode, string? value)
        {
            // Whatever is appended is content of the element open, so its start tag is over.
            CloseStartTag();

            int parentSlot = m_openDepth - 1;
            int parentId = m_openNodes[parentSlot];

            int id = AllocateNode();
            m_kind[id] = kind;
            m_nameCode[id] = nameCode;
            m_value[id] = value;
            m_parent[id] = parentId;
            m_depth[id] = m_openDepth;

            int lastChild = m_openLastChild[parentSlot];
            if (lastChild < 0)
            {
                m_firstChild[parentId] = id;
            }
            else
            {
                m_next[lastChild] = id;
            }

            m_openLastChild[parentSlot] = id;
            return id;
        }

        private readonly bool m_pooled;

        /// <summary>Takes an array from the shared pool where the storage is pooled, or makes one.</summary>
        private T[] RentOrNew<T>(int length)
        {
            return m_pooled ? System.Buffers.ArrayPool<T>.Shared.Rent(length) : new T[length];
        }

        /// <summary>
        /// Replaces an array with one of a new length holding the same first elements, giving a pooled one
        /// back to the pool.
        /// </summary>
        private void Resize<T>(ref T[] array, int length)
        {
            if (!m_pooled)
            {
                Array.Resize(ref array, length);
                return;
            }

            T[] replacement = System.Buffers.ArrayPool<T>.Shared.Rent(length);
            Array.Copy(array, replacement, Math.Min(array.Length, length));
            System.Buffers.ArrayPool<T>.Shared.Return(array, clearArray: !typeof(T).IsValueType);
            array = replacement;
        }

        private int AllocateNode()
        {
            if (m_nodeCount == m_kind.Length)
            {
                int capacity = m_nodeCount * 2;
                Resize(ref m_kind, capacity);
                Resize(ref m_nameCode, capacity);
                Resize(ref m_parent, capacity);
                Resize(ref m_firstChild, capacity);
                Resize(ref m_next, capacity);
                Resize(ref m_subtreeEnd, capacity);
                Resize(ref m_depth, capacity);
                Resize(ref m_value, capacity);
                Resize(ref m_attrStart, capacity);
                Resize(ref m_attrCount, capacity);
                Resize(ref m_namespaceStart, capacity);
                Resize(ref m_namespaceCount, capacity);

                if (m_line is not null)
                {
                    Array.Resize(ref m_line, capacity);
                    Array.Resize(ref m_column, capacity);
                }

                if (m_nodeType is not null)
                {
                    Array.Resize(ref m_nodeType, capacity);
                }
            }

            int id = m_nodeCount++;
            m_firstChild[id] = -1;
            m_next[id] = -1;
            m_subtreeEnd[id] = id;
            m_attrStart[id] = m_attributeCount;
            m_attrCount[id] = 0;
            m_namespaceStart[id] = m_namespaceDeclarationCount;
            m_namespaceCount[id] = 0;
            return id;
        }

        private void Push(int nodeId)
        {
            if (m_openDepth == m_openNodes.Length)
            {
                Array.Resize(ref m_openNodes, m_openDepth * 2);
                Array.Resize(ref m_openLastChild, m_openDepth * 2);
                Array.Resize(ref m_preserveSpace, m_openDepth * 2);
            }

            m_openNodes[m_openDepth] = nodeId;
            m_openLastChild[m_openDepth] = -1;

            // xml:space is inherited until an element overrides it.
            m_preserveSpace[m_openDepth] = m_openDepth > 0 && m_preserveSpace[m_openDepth - 1];
            m_openDepth++;
        }

        private int RequireOpenElement()
        {
            if (m_openDepth < 2)
            {
                throw new InvalidOperationException("No element is currently open.");
            }

            return m_openNodes[m_openDepth - 1];
        }

        /// <summary>
        /// Interns a name, through a small cache keyed on the identity of the strings rather than on their
        /// contents.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Interning through <see cref="Model.NameTable"/> alone costs two string hashes and two dictionary
        /// probes per name, and it happens once for every element and every attribute in the document. The
        /// namespace URI is usually the expensive part: hashing 46 characters of
        /// <c>urn:iso:std:iso:20022:...</c> for each of twenty thousand elements is most of the work, and all
        /// of it is repeated, because a document uses very few distinct names over and over.
        /// </para>
        /// <para>
        /// <see cref="XmlReader"/> hands back the identical string instance each time it reports a name it has
        /// seen before, so the repeat can be recognised by comparing references — no hashing, no character
        /// comparison. That is what this cache exploits, and it takes interning from about 43 ns per name to
        /// about 17 ns.
        /// </para>
        /// <para>
        /// A hit requires all three strings to be the very same instances, and a miss simply falls through to
        /// the table, whose answer is then remembered. Where a name lands is only a guess: a poor guess costs a
        /// miss, never a wrong name. Callers that build a tree from strings assembled at run time will miss
        /// more often and are no worse off than they were without the cache.
        /// </para>
        /// </remarks>
        private int InternName(string prefix, string namespaceUri, string localName)
        {
            int first = SetOf(localName) << 1;

            if (ReferenceEquals(m_cacheLocal[first], localName)
                && ReferenceEquals(m_cacheUri[first], namespaceUri)
                && ReferenceEquals(m_cachePrefix[first], prefix))
            {
                return m_cacheNameCode[first];
            }

            int second = first + 1;

            if (ReferenceEquals(m_cacheLocal[second], localName)
                && ReferenceEquals(m_cacheUri[second], namespaceUri)
                && ReferenceEquals(m_cachePrefix[second], prefix))
            {
                // Deliberately not promoted to the first way. Two names alternating in one set then settle,
                // one in each way, and both keep hitting; swapping them on every access would not.
                return m_cacheNameCode[second];
            }

            int nameCode = NameTable.GetNameCode(prefix, namespaceUri, localName);

            m_cacheLocal[second] = m_cacheLocal[first];
            m_cacheUri[second] = m_cacheUri[first];
            m_cachePrefix[second] = m_cachePrefix[first];
            m_cacheNameCode[second] = m_cacheNameCode[first];

            m_cacheLocal[first] = localName;
            m_cacheUri[first] = namespaceUri;
            m_cachePrefix[first] = prefix;
            m_cacheNameCode[first] = nameCode;
            return nameCode;
        }

        /// <summary>
        /// Chooses a cache set from a local name's length and three of its characters.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reading the whole name would defeat the purpose, so the set comes from a sample. What matters is
        /// how the sample is mixed rather than how much of it there is: a first version added the parts
        /// together with small multipliers, and the names of an ordinary CAMT document collided in pairs —
        /// <c>NtryDtls</c> against <c>RmtInf</c>, <c>Id</c> against <c>Amt</c> — so a quarter of all lookups
        /// missed. Multiplying by a large odd constant and taking the <em>high</em> bits mixes far better,
        /// because multiplication carries low-bit differences upwards.
        /// </para>
        /// <para>
        /// No sampled hash is collision-free, though, and the second arrangement merely moved the collisions
        /// onto a different set of names. That is why the cache holds two names per set rather than one: the
        /// failure that actually occurs is a <em>pair</em> of names evicting each other, and two ways makes
        /// that pair cost one extra reference comparison instead of a miss on every element.
        /// </para>
        /// </remarks>
        private static int SetOf(string localName)
        {
            int length = localName.Length;
            if (length == 0)
            {
                return 0;
            }

            // Three positions, so that names sharing a first and last character are still told apart.
            uint mixed = (uint)((length * 31) + localName[0] + (localName[length - 1] << 8)
                + (localName[length >> 1] << 16));

            // Knuth's multiplicative constant; the top bits carry the mixing.
            return (int)((mixed * 2654435761u) >> 27) & (NameCacheSets - 1);
        }
    }
}
