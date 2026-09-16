using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// The test applied to each node an axis produces.
    /// </summary>
    /// <remarks>
    /// The principal node kind is supplied by the step rather than baked into the test, because <c>*</c>
    /// selects elements on most axes but attributes on the attribute axis.
    /// </remarks>
    public abstract class NodeTest
    {
        /// <summary>A test matching any node, written <c>node()</c>.</summary>
        public static readonly NodeTest AnyNode = new AnyNodeTest();

        /// <summary>A test matching any text node, written <c>text()</c>.</summary>
        public static readonly NodeTest AnyText = new KindOnlyNodeTest(NodeKind.Text);

        /// <summary>A test matching any comment, written <c>comment()</c>.</summary>
        public static readonly NodeTest AnyComment = new KindOnlyNodeTest(NodeKind.Comment);

        /// <summary>A test matching the principal node kind of the axis, written <c>*</c>.</summary>
        public static readonly NodeTest Wildcard = new WildcardNodeTest();

        /// <summary>
        /// Returns whether a node satisfies this test.
        /// </summary>
        /// <param name="tree">The tree the node belongs to.</param>
        /// <param name="node">The node to test.</param>
        /// <param name="principalKind">The principal node kind of the axis being walked.</param>
        /// <param name="fingerprintMap">Slot-to-fingerprint mapping for the tree; see <see cref="NameSlotTable"/>.</param>
        public abstract bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap);

        private sealed class AnyNodeTest : NodeTest
        {
            public override bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap)
            {
                return true;
            }

            public override string ToString() => "node()";
        }

        private sealed class KindOnlyNodeTest : NodeTest
        {
            private readonly NodeKind m_kind;

            public KindOnlyNodeTest(NodeKind kind)
            {
                m_kind = kind;
            }

            public override bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap)
            {
                return tree.KindOf(node) == m_kind;
            }

            public override string ToString() => m_kind == NodeKind.Text ? "text()" : "comment()";
        }

        private sealed class WildcardNodeTest : NodeTest
        {
            public override bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap)
            {
                return tree.KindOf(node) == principalKind;
            }

            public override string ToString() => "*";
        }
    }

    /// <summary>Matches nodes of the axis's principal kind whose expanded name equals a given name.</summary>
    public sealed class NameNodeTest : NodeTest
    {
        private readonly int m_slot;

        /// <summary>Initializes a name test.</summary>
        /// <param name="slot">The slot holding the name, from <see cref="NameSlotTable.GetSlot"/>.</param>
        /// <param name="displayName">The name as written, retained for diagnostics.</param>
        public NameNodeTest(int slot, string displayName)
        {
            m_slot = slot;
            DisplayName = displayName;
        }

        /// <summary>
        /// Gets the slot holding the name this test compares against. Template dispatch uses it to index
        /// patterns by name so that only plausible candidates are tested.
        /// </summary>
        public int Slot => m_slot;

        /// <summary>Gets the name as it was written in the expression.</summary>
        public string DisplayName { get; }

        /// <inheritdoc/>
        public override bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap)
        {
            if (tree.KindOf(node) != principalKind)
            {
                return false;
            }

            // A namespace node's name is its prefix, interned when the node was made — after the map from
            // the stylesheet's names to this tree's was built, which is why the map cannot answer for it.
            // The name as written is the prefix looked for, and a prefixed name on this axis names nothing.
            if (principalKind == NodeKind.Namespace)
            {
                return DisplayName.IndexOf(':') < 0
                    && string.Equals(
                        tree.NameTable.GetLocalName(tree.FingerprintOf(node)), DisplayName, StringComparison.Ordinal);
            }

            // A tree whose names were never mapped has no slot for anything, which is not a match and not a
            // fault: a path may reach a document built while the expression was running.
            if (m_slot >= fingerprintMap.Length)
            {
                return false;
            }

            int wanted = fingerprintMap[m_slot];

            // A name absent from the tree resolves to NoFingerprint, which must not be allowed to match the
            // NoFingerprint carried by unnamed nodes.
            return wanted != NameTable.NoFingerprint && tree.FingerprintOf(node) == wanted;
        }

        /// <inheritdoc/>
        public override string ToString() => DisplayName;
    }

    /// <summary>Matches nodes of the axis's principal kind in a given namespace, written <c>prefix:*</c>.</summary>
    public sealed class NamespaceWildcardNodeTest : NodeTest
    {
        private readonly string m_namespaceUri;

        /// <summary>Initializes a namespace wildcard test.</summary>
        /// <param name="namespaceUri">The namespace URI that matching nodes must be in.</param>
        /// <param name="displayName">The test as written, retained for diagnostics.</param>
        public NamespaceWildcardNodeTest(string namespaceUri, string displayName)
        {
            m_namespaceUri = namespaceUri;
            DisplayName = displayName;
        }

        /// <summary>Gets the test as it was written in the expression.</summary>
        public string DisplayName { get; }

        /// <inheritdoc/>
        public override bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap)
        {
            if (tree.KindOf(node) != principalKind)
            {
                return false;
            }

            int fingerprint = tree.FingerprintOf(node);
            return fingerprint != NameTable.NoFingerprint
                && string.Equals(tree.NameTable.GetNamespaceUri(fingerprint), m_namespaceUri, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Matches nodes of the axis's principal kind by local name alone, written <c>*:local</c>.
    /// </summary>
    /// <remarks>
    /// The other half of XPath 2.0's wildcard pair, and the useful half in practice: it is how a stylesheet
    /// reaches into a document whose namespace it does not know, or does not want to have to declare a prefix
    /// for. Unlike a bare name, which is in no namespace, this matches whatever namespace the node is in —
    /// including none.
    /// </remarks>
    public sealed class LocalNameNodeTest : NodeTest
    {
        private readonly string m_localName;

        /// <summary>Initializes a local-name wildcard test.</summary>
        /// <param name="localName">The local name that matching nodes must have.</param>
        public LocalNameNodeTest(string localName)
        {
            m_localName = localName;
        }

        /// <inheritdoc/>
        public override bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap)
        {
            if (tree.KindOf(node) != principalKind)
            {
                return false;
            }

            int fingerprint = tree.FingerprintOf(node);
            return fingerprint != NameTable.NoFingerprint
                && string.Equals(
                    tree.NameTable.GetLocalName(fingerprint), m_localName, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override string ToString() => "*:" + m_localName;
    }

    /// <summary>Matches processing instructions, optionally restricted to one target.</summary>
    public sealed class ProcessingInstructionNodeTest : NodeTest
    {
        private readonly string? m_target;

        /// <summary>Initializes a processing-instruction test.</summary>
        /// <param name="target">The required target, or <see langword="null"/> to match any.</param>
        public ProcessingInstructionNodeTest(string? target)
        {
            m_target = target;
        }

        /// <summary>Whether the test names a target, which is what makes it more than a kind test.</summary>
        public bool NamesATarget => m_target is not null;

        /// <inheritdoc/>
        public override bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap)
        {
            if (tree.KindOf(node) != NodeKind.ProcessingInstruction)
            {
                return false;
            }

            return m_target is null
                || string.Equals(tree.NameTable.GetLocalName(tree.FingerprintOf(node)), m_target, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return m_target is null ? "processing-instruction()" : $"processing-instruction('{m_target}')";
        }
    }

    /// <summary>
    /// One of XPath 2.0's kind tests: <c>element(name)</c>, <c>attribute(name)</c>, <c>document-node(…)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where <c>*</c> and a bare name take their meaning from the axis — <c>*</c> is elements on most axes
    /// and attributes on one — a kind test says which kind it wants, so <c>child::attribute()</c> selects
    /// nothing rather than every child. That is the point of it, and why the two cannot share a
    /// representation.
    /// </para>
    /// <para>
    /// The name is held expanded rather than as a slot in the name table, which the other tests use. A slot
    /// is resolved against a particular tree through a fingerprint map, and this test is also used in a
    /// sequence type — <c>instance of element(x)</c> — where the item names the tree and there is no map to
    /// hand. Two string comparisons against a name that is nearly always short is not worth a second
    /// mechanism to avoid.
    /// </para>
    /// </remarks>
    public sealed class KindNodeTest : NodeTest
    {
        private readonly NodeKind m_kind;
        private readonly string? m_namespaceUri;
        private readonly string? m_localName;
        private readonly KindNodeTest? m_content;
        private readonly string m_written;

        /// <summary>Initializes a kind test.</summary>
        /// <param name="kind">The kind of node it matches.</param>
        /// <param name="namespaceUri">The required namespace, or <see langword="null"/> to match any name.</param>
        /// <param name="localName">The required local name, or <see langword="null"/> to match any name.</param>
        /// <param name="content">
        /// For <c>document-node(element(…))</c>, the test its element child must satisfy.
        /// </param>
        /// <param name="written">The test as written, retained for diagnostics.</param>
        public KindNodeTest(
            NodeKind kind,
            string? namespaceUri,
            string? localName,
            KindNodeTest? content,
            string written)
        {
            m_kind = kind;
            m_namespaceUri = namespaceUri;
            m_localName = localName;
            m_content = content;
            m_written = written;
        }

        /// <summary>Gets the kind of node this test matches.</summary>
        public NodeKind Kind => m_kind;

        /// <summary>Whether the test names an element or attribute, rather than admitting any name.</summary>
        public bool NamesAName => m_localName is not null;

        /// <summary>Whether a type was written after the name, or a declaration was named.</summary>
        public bool NamesAType { get; init; }

        /// <summary>The type the test names as written, or <see langword="null"/> where it names none.</summary>
        public string? TypeName { get; init; }

        /// <summary>
        /// The type the test names, built in or from a schema, or <see langword="null"/> where it names
        /// none. A node matches where its annotation is this type or derives from it; a node nothing
        /// validated is annotated <c>xs:untyped</c> as an element and <c>xs:untypedAtomic</c> as an
        /// attribute (XDM §5.2), which is what a test naming those matches.
        /// </summary>
        internal XdmSchemaType? SchemaType { get; init; }

        /// <summary>
        /// Whether the test admits a nilled element: written with no type, or with the type followed by
        /// <c>?</c>. <c>element(e)</c> means <c>element(e, xs:anyType?)</c>, and the <c>?</c> is what
        /// admits a nilled node; <c>element(e, xs:anyType)</c> is narrower and does not.
        /// </summary>
        internal bool AdmitsNilled { get; init; } = true;

        /// <summary>
        /// The declaration <c>schema-element()</c> or <c>schema-attribute()</c> names, or null for the
        /// other kind tests. A node matches where its name is the declared one, or in the declared
        /// element's substitution group, and its annotation derives from the declared type, and it is
        /// not nilled unless the declaration allows that.
        /// </summary>
        internal XdmSchemaDeclaration? Declaration { get; init; }

        /// <summary>For <c>document-node(element(…))</c>, the test the element child must satisfy.</summary>
        public KindNodeTest? Content => m_content;

        /// <summary>
        /// Whether every node this test matches is matched by another: the subtype judgement over kind
        /// tests, which is what one function type's argument or result says against another's.
        /// </summary>
        /// <remarks>
        /// The name has to be the other's, or the other has to ask for no name. The type is the part with a
        /// wrinkle in it: <c>element(e)</c> means <c>element(e, xs:anyType?)</c>, and the <c>?</c> there is
        /// what makes it nillable — so <c>element(e, xs:anyType)</c>, which looks like the same thing
        /// written out, is narrower and <c>element(e)</c> is not within it. Two named types are compared
        /// by derivation, and two declarations by name.
        /// </remarks>
        /// <param name="other">The test this one may be within.</param>
        public bool Within(KindNodeTest other)
        {
            if (m_kind != other.m_kind)
            {
                return false;
            }

            if (other.Declaration is XdmSchemaDeclaration wanted)
            {
                return Declaration is XdmSchemaDeclaration own
                    && own.NamespaceUri == wanted.NamespaceUri
                    && own.LocalName == wanted.LocalName;
            }

            if (other.m_localName is not null
                && (m_localName != other.m_localName || m_namespaceUri != other.m_namespaceUri))
            {
                return false;
            }

            if (other.m_content is not null && (m_content is null || !m_content.Within(other.m_content)))
            {
                return false;
            }

            if (!other.NamesAType)
            {
                return true;
            }

            if (AdmitsNilled && !other.AdmitsNilled)
            {
                return false;
            }

            XdmSchemaType? mine = SchemaType ?? Declaration?.Type;

            return other.SchemaType is XdmSchemaType type
                ? mine is not null && mine.DerivesFrom(type)
                : NamesAType && TypeName == other.TypeName;
        }

        /// <summary>
        /// Returns whether a node satisfies this test, which needs nothing but the tree it is in.
        /// </summary>
        /// <param name="tree">The tree the node belongs to.</param>
        /// <param name="node">The node to test.</param>
        public bool Matches(XdmTree tree, int node)
        {
            if (tree.KindOf(node) != m_kind)
            {
                return false;
            }

            if (Declaration is XdmSchemaDeclaration declaration)
            {
                return MatchesDeclaration(tree, node, declaration);
            }

            if (m_localName is not null && !HasName(tree, node))
            {
                return false;
            }

            if (SchemaType is XdmSchemaType type && !AnnotatedAs(tree, node, type))
            {
                return false;
            }

            if (!AdmitsNilled && tree.IsNilled(node))
            {
                return false;
            }

            if (m_content is null)
            {
                return true;
            }

            // XPath 3.1 §2.5.5.2: document-node(E) "matches any document node that contains exactly one
            // element node, optionally accompanied by one or more comment and processing instruction
            // nodes, if E ... matches the element node". The list is exhaustive, so a second element or
            // any text at all is a document this does not describe — which is the difference between a
            // tree that could be serialized as a document and one that could not.
            int only = -1;

            for (int child = tree.FirstChildOf(node); child >= 0; child = tree.NextSiblingOf(child))
            {
                switch (tree.KindOf(child))
                {
                    case NodeKind.Element:
                        if (only >= 0)
                        {
                            return false;
                        }

                        only = child;
                        break;

                    case NodeKind.Comment:
                    case NodeKind.ProcessingInstruction:
                        break;

                    default:
                        return false;
                }
            }

            return only >= 0 && m_content.Matches(tree, only);
        }

        /// <summary>
        /// Whether a node is one <c>schema-element(E)</c> or <c>schema-attribute(A)</c> matches
        /// (XPath 3.1 §2.5.5.6): named as declared or substitutable for it, annotated with the declared
        /// type or one derived from it, and nilled only where the declaration allows.
        /// </summary>
        private static bool MatchesDeclaration(XdmTree tree, int node, XdmSchemaDeclaration declaration)
        {
            int fingerprint = tree.FingerprintOf(node);

            if (fingerprint == NameTable.NoFingerprint
                || !declaration.AdmitsName(
                    tree.NameTable.GetNamespaceUri(fingerprint), tree.NameTable.GetLocalName(fingerprint)))
            {
                return false;
            }

            // A node nothing validated is no instance of a declaration, whatever the declaration's type:
            // xs:untyped derives from xs:anyType, which an element declared without a type has, and the
            // test is still about what was validated against the declaration, not about the name alone.
            if (!tree.HasTypeAnnotations || tree.TypeIdOf(node) == 0)
            {
                return false;
            }

            return AnnotatedAs(tree, node, declaration.Type)
                && (declaration.Nillable || !tree.IsNilled(node));
        }

        /// <summary>
        /// Whether the node's type annotation is the type named, or one that derives from it; a node
        /// nothing validated is annotated <c>xs:untyped</c> or <c>xs:untypedAtomic</c> by its kind.
        /// </summary>
        private static bool AnnotatedAs(XdmTree tree, int node, XdmSchemaType type)
        {
            XdmSchemaType? annotation = tree.HasTypeAnnotations ? tree.TypeAnnotationOf(node) : null;

            annotation ??= tree.KindOf(node) == NodeKind.Element
                ? XdmSchemaType.BuiltInNamed("untyped")
                : XdmSchemaType.BuiltInNamed("untypedAtomic");

            return annotation is not null && annotation.DerivesFrom(type);
        }

        private bool HasName(XdmTree tree, int node)
        {
            int fingerprint = tree.FingerprintOf(node);

            if (fingerprint == NameTable.NoFingerprint)
            {
                return false;
            }

            return string.Equals(tree.NameTable.GetLocalName(fingerprint), m_localName, StringComparison.Ordinal)
                && string.Equals(
                    tree.NameTable.GetNamespaceUri(fingerprint), m_namespaceUri, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Matches(XdmTree tree, int node, NodeKind principalKind, int[] fingerprintMap)
        {
            return Matches(tree, node);
        }

        /// <inheritdoc/>
        public override string ToString() => m_written;
    }
}
