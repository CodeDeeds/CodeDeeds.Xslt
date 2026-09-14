using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// Where a transformation's instructions send their result.
    /// </summary>
    /// <remarks>
    /// Instructions never know whether they are writing the final output or the body of an
    /// <c>xsl:variable</c>. <see cref="OutputWriter"/> serializes to text; <see cref="ResultTreeBuilder"/>
    /// captures into a tree so that a variable's content can later be navigated or copied.
    /// </remarks>
    public abstract class OutputTarget
    {
        /// <summary>
        /// Gets whether anything has been written here.
        /// </summary>
        /// <remarks>
        /// What says whether a transformation built a result tree of its own. <c>xsl:result-document</c> may
        /// name the base output URI, which is where the transformation's own result goes, and doing both is
        /// two final result trees with one URI between them — <c>XTDE1490</c>.
        /// </remarks>
        public bool HasContent { get; protected set; }

        /// <summary>
        /// Gets how many times something has been written here: a count that only rises, for telling
        /// whether anything was written between two moments, which <see cref="HasContent"/> cannot once it
        /// is true.
        /// </summary>
        public int Writes { get; private set; }

        /// <summary>
        /// Gets whether what is written here is a final result tree, or stands for one: a variable's tree
        /// and a captured sequence are temporary output state (§2.3.2), a serializer is not, and neither is
        /// a buffer that will be written to one once it is complete.
        /// </summary>
        public virtual bool IsFinalOutput => false;

        /// <summary>Records that something was written.</summary>
        protected void Touch()
        {
            HasContent = true;
            Writes++;
        }

        /// <summary>
        /// How many elements are open here.
        /// </summary>
        /// <remarks>
        /// What says whether an attribute has anything to attach to. An <c>xsl:copy</c> of a document node
        /// runs its content straight into whatever is already being written — a document node contributes its
        /// children and nothing else, so there is no reason to build one and copy it out — but that leaves
        /// nothing to stop an attribute in it from landing on the element outside. Comparing this against the
        /// depth the copy began at is what tells the two apart.
        /// </remarks>
        public virtual int OpenElementDepth => 0;

        /// <summary>
        /// Gets whether what is written here is on its way to being a string.
        /// </summary>
        /// <remarks>
        /// What XSLT 3.0 uses to draw the line under <c>xsl:result-document</c>. Writing a result document
        /// while a <em>temporary tree</em> is being built is refused, because whether the file appears would
        /// then depend on whether a variable was ever read; building the content of an attribute, a comment,
        /// a processing instruction, a namespace, an <c>xsl:value-of</c> or an <c>xsl:message</c> is not
        /// that, and 3.0 allows it. The two look alike from inside the instruction, and this is what tells
        /// them apart.
        /// </remarks>
        public virtual bool BecomesAString => false;

        /// <summary>
        /// What separates two adjacent atomic values written into this result.
        /// </summary>
        /// <remarks>
        /// A single space, which is what atomization does, unless <c>item-separator</c> said otherwise. It is
        /// asked of the target rather than settled by the instruction because it is a property of the result
        /// document: one sequence written to two result documents may be separated differently in each.
        /// </remarks>
        public virtual string ItemSeparator => " ";

        /// <summary>Writes the start of an element. Attributes may follow until content begins.</summary>
        /// <param name="prefix">The preferred prefix, or an empty string.</param>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        public abstract void StartElement(string prefix, string namespaceUri, string localName);

        /// <summary>Writes an attribute on the element currently being started.</summary>
        /// <param name="prefix">The preferred prefix, or an empty string.</param>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <param name="value">The attribute value.</param>
        public abstract void WriteAttribute(string prefix, string namespaceUri, string localName, string value);

        /// <summary>
        /// Adds an attribute carrying the type validation settled on, which a target building a tree
        /// records and a target writing text has no use for.
        /// </summary>
        /// <param name="prefix">The prefix, or an empty string.</param>
        /// <param name="namespaceUri">The namespace URI, or an empty string.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <param name="value">The attribute's value.</param>
        /// <param name="typeId">The type's number, from <c>XdmSchemaType.Id</c>, or 0 for untyped.</param>
        internal virtual void WriteAttribute(string prefix, string namespaceUri, string localName, string value, ushort typeId)
        {
            WriteAttribute(prefix, namespaceUri, localName, value);

            if (typeId != 0)
            {
                AnnotateAttribute(typeId);
            }
        }

        /// <summary>
        /// Annotates the element most recently started with the type validation settled on it, and
        /// whether it is nilled. Nothing, for a target that writes text rather than building a tree.
        /// </summary>
        /// <param name="typeId">The type's number, from <c>XdmSchemaType.Id</c>, or 0 for untyped.</param>
        /// <param name="nilled">Whether the element is nilled.</param>
        internal virtual void AnnotateElement(ushort typeId, bool nilled)
        {
        }

        /// <summary>Annotates the attribute most recently written with the type validation settled on it.</summary>
        /// <param name="typeId">The type's number, from <c>XdmSchemaType.Id</c>, or 0 for untyped.</param>
        internal virtual void AnnotateAttribute(ushort typeId)
        {
        }

        /// <summary>
        /// Marks the element most recently started as one whose content is untyped, which is what
        /// <c>validation="strip"</c> asks: whatever annotations are written inside it, until it ends, are
        /// dropped (XSLT 3.0 §27.4).
        /// </summary>
        internal virtual void StripContent()
        {
        }

        /// <summary>
        /// Declares a namespace on the element currently being started.
        /// </summary>
        /// <remarks>
        /// XSLT copies namespace nodes to the result, so a declaration can survive even when nothing in the
        /// output uses its prefix. Targets are free to drop a declaration that is already in scope.
        /// </remarks>
        /// <param name="prefix">The prefix being bound, or an empty string for the default namespace.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        public abstract void WriteNamespaceDeclaration(string prefix, string namespaceUri);

        /// <summary>
        /// Creates a namespace node because the stylesheet asked for one, which is <c>xsl:namespace</c>.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="WriteNamespaceDeclaration"/>, which carries bindings that came from
        /// somewhere else — a literal result element, or a node being copied — and may quietly drop one that
        /// is already in scope or that arrives too late to be written. Here the stylesheet said so in as many
        /// words, so getting it wrong is reported rather than absorbed.
        /// </remarks>
        /// <param name="prefix">The prefix being bound, or an empty string for the default namespace.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        public virtual void CreateNamespace(string prefix, string namespaceUri)
        {
            WriteNamespaceDeclaration(prefix, namespaceUri);
        }

        /// <summary>Closes the innermost open element.</summary>
        public abstract void EndElement();

        /// <summary>Writes character data, escaped as the target requires.</summary>
        /// <param name="text">The characters to write.</param>
        public abstract void WriteText(string text);

        /// <summary>Writes character data without escaping, implementing <c>disable-output-escaping</c>.</summary>
        /// <param name="text">The characters to write verbatim.</param>
        public abstract void WriteRawText(string text);

        /// <summary>
        /// Marks the start of a document node being copied in, whose children follow. A tree takes them as
        /// they come; simple content takes the whole document as one item, which is what makes a copied
        /// document beside a text node two items and not one merged text.
        /// </summary>
        public virtual void StartDocumentCopy()
        {
        }

        /// <summary>
        /// Says which tree the document node being copied in came from, so that a target building a tree
        /// can keep what a copy of a document node keeps: its unparsed entities (§5.7.1). A target that
        /// builds nothing has nowhere to keep them.
        /// </summary>
        /// <param name="source">The tree the document node was copied from.</param>
        public virtual void NoteDocumentCopiedFrom(XdmTree source)
        {
        }

        /// <summary>Marks the end of a document node being copied in.</summary>
        public virtual void EndDocumentCopy()
        {
        }

        /// <summary>
        /// Records that the element just started does not pass its namespaces on to the children built
        /// inside it: <c>inherit-namespaces="no"</c>.
        /// </summary>
        public virtual void MarkNoInheritedNamespaces()
        {
        }

        /// <summary>
        /// Records that the element just started carries its own namespace nodes: what is declared for it
        /// here is the whole of what it has, rather than an addition to what surrounds it.
        /// </summary>
        /// <remarks>
        /// What a copy says about itself, and the difference between the two ways a copied element comes to
        /// be somewhere. A copy takes the namespaces of the element it is attached to, and so do all of its
        /// descendants — from the point of attachment, not from the copied parent, which is why an element
        /// that undeclared the default namespace keeps that undeclaration through
        /// <c>xsl:copy-of</c> of its parent and loses it through an <c>xsl:copy</c> that rebuilt the parent
        /// (XSLT 3.0 §11.9.2). Called immediately after <see cref="StartElement"/> and before anything is
        /// declared for the element.
        /// </remarks>
        /// <param name="root">
        /// Whether this element is where the copy started, and so takes its namespaces from the element it
        /// is attached to rather than from the copy above it.
        /// </param>
        public virtual void MarkOwnNamespaces(bool root)
        {
        }

        /// <summary>
        /// Writes an atomic value where nodes are being built, which makes it text — separated from an atomic
        /// value written just before it by a single space, as the specification has adjacent atomic values in
        /// what a sequence constructor produces become one text node with a space between each pair.
        /// </summary>
        /// <remarks>
        /// Adjacent means nothing else came between. A text node with characters in it, an element, a comment
        /// or an attribute breaks the run; a zero-length text node does not, being discarded before adjacency
        /// is looked at. A target that builds nothing may take the value as text, which is the default.
        /// </remarks>
        /// <param name="text">The value as a string.</param>
        public virtual void WriteAtomic(string text)
        {
            WriteText(text);
        }

        /// <summary>Writes a comment.</summary>
        /// <param name="text">The comment's character data.</param>
        public abstract void WriteComment(string text);

        /// <summary>Writes a processing instruction.</summary>
        /// <param name="target">The instruction's target.</param>
        /// <param name="data">The instruction's character data.</param>
        public abstract void WriteProcessingInstruction(string target, string data);

        /// <summary>
        /// Contributes a value, which <c>xsl:sequence</c> produces.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="false"/> where the target cannot hold a value as a value — everywhere the
        /// result is markup rather than a sequence — and the caller writes it out instead. Only a target
        /// capturing a sequence, which is what an <c>as</c> declaration asks for, keeps the item itself:
        /// there an <c>xs:integer</c> stays an integer rather than becoming the text of one.
        /// </remarks>
        /// <param name="value">The value to contribute.</param>
        public virtual bool TryAppendValue(XPath.XPathValue value)
        {
            return false;
        }

        /// <summary>
        /// Records that the node most recently written was copied, with its accumulator values, from a node
        /// of another tree.
        /// </summary>
        /// <remarks>
        /// What <c>copy-accumulators="yes"</c> asks of a target. Only a target building a tree has anything
        /// to record it against — a serializer writes the node out and there is no node left to ask — so the
        /// default is to take no notice.
        /// </remarks>
        /// <param name="source">The tree the node was copied from.</param>
        /// <param name="node">The node it was copied from.</param>
        public virtual void NoteCopiedFrom(XdmTree source, int node)
        {
        }

        /// <summary>
        /// Says that the next top-level node written is a copy of a node of another tree, whose base URI
        /// the copy keeps.
        /// </summary>
        /// <remarks>
        /// What a parentless copy is entitled to (§11.9.1): a node copied into a sequence has no parent to
        /// take a base URI from, so it keeps the one it had. Only a target collecting a sequence has such a
        /// node to give it to — under a parent, the copy inherits the parent's — so the default takes no
        /// notice, and a caller asks only when nothing is open.
        /// </remarks>
        /// <param name="source">The tree the node is copied from.</param>
        /// <param name="node">The node.</param>
        /// <param name="runtime">The transformation, which knows where the source tree came from.</param>
        /// <param name="withAttributes">
        /// Whether the copy carries the node's attributes, as a deep copy does and <c>xsl:copy</c> does not:
        /// a copied <c>xml:base</c> is resolved afresh, against the copying instruction's base URI (§11.9.2).
        /// </param>
        public virtual void NoteSourceOfNext(XdmTree source, int node, XsltRuntime runtime, bool withAttributes)
        {
        }
    }

    /// <summary>
    /// Captures what a sequence constructor produces as a sequence, rather than as a tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The difference an <c>as</c> declaration makes. Without one, everything a constructor produces is built
    /// into one document node, so three integers arrive as the text <c>1 2 3</c>; with one, they arrive as
    /// three integers.
    /// </para>
    /// <para>
    /// Nodes are still built — a literal result element inside such a constructor is an element node, not
    /// text — so this holds a tree builder alongside the items and files each completed top-level node into
    /// the sequence as it finishes.
    /// </para>
    /// </remarks>
    public sealed class SequenceCaptureTarget : OutputTarget
    {
        private readonly List<XPath.XPathValue> m_items = new();
        private readonly System.Text.StringBuilder m_text = new();
        private readonly bool m_becomesAString;
        private readonly string? m_baseUri;
        private XdmTreeBuilder? m_builder;
        private int m_depth;

        /// <summary>The base URI of the top-level node being built, settled when it started.</summary>
        private string? m_nodeBaseUri;

        /// <summary>The base URI noted for the next top-level node, and whether one was noted.</summary>
        private string? m_pendingBaseUri;
        private bool m_hasPendingBaseUri;

        /// <summary>Initializes a capture.</summary>
        /// <param name="becomesAString">
        /// Whether what is captured is on its way to being a string rather than kept as a tree or a
        /// sequence. Only <c>xsl:value-of</c> says yes: it collects a sequence so that a separator can be
        /// put between the items, and then joins them.
        /// </param>
        /// <param name="baseUri">
        /// The base URI a node built at the top takes, which is that of the instruction whose content this
        /// is; a node copied to the top keeps its own instead.
        /// </param>
        public SequenceCaptureTarget(bool becomesAString = false, string? baseUri = null)
        {
            m_becomesAString = becomesAString;
            m_baseUri = baseUri;
        }

        /// <inheritdoc/>
        public override bool BecomesAString => m_becomesAString;

        /// <summary>
        /// Gets whether this buffer stands in for final output: the body of an <c>xsl:try</c> is captured so
        /// that it can be rolled back, and a result document written from inside it is written from
        /// wherever the try stands.
        /// </summary>
        public bool StandsForFinalOutput { get; init; }

        /// <inheritdoc/>
        public override bool IsFinalOutput => StandsForFinalOutput;

        /// <inheritdoc/>
        public override int OpenElementDepth => m_depth;

        /// <inheritdoc/>
        public override bool TryAppendValue(XPath.XPathValue value)
        {
            // A value contributed while an element is open belongs to that element's content rather than to
            // the sequence, so the caller writes it out as an element's child instead.
            if (m_depth != 0)
            {
                return false;
            }

            FlushText();

            foreach (XPath.XPathValue item in XPath.XdmSequence.Items(value))
            {
                m_items.Add(item);
            }

            return true;
        }

        /// <inheritdoc/>
        public override void NoteCopiedFrom(XdmTree source, int node)
        {
            m_builder?.NoteCopiedFrom(source, node);
        }

        /// <inheritdoc/>
        public override void NoteDocumentCopiedFrom(XdmTree source)
        {
            m_builder?.InheritUnparsedEntities(source);
        }

        /// <inheritdoc/>
        public override void StartElement(string prefix, string namespaceUri, string localName)
        {
            if (m_depth == 0)
            {
                FlushText();

                // Each top-level node gets a tree of its own, so that it can be referred to as soon as it is
                // complete rather than waiting for the whole constructor to finish.
                m_builder = new XdmTreeBuilder { FixesUpNamespaces = true };
                m_nodeBaseUri = TakeBaseUri();
            }

            m_builder!.StartElement(prefix, namespaceUri, localName);
            m_depth++;
        }

        /// <inheritdoc/>
        public override void WriteAttribute(string prefix, string namespaceUri, string localName, string value)
        {
            if (m_depth != 0)
            {
                m_builder!.AddAttribute(prefix, namespaceUri, localName, value);
                return;
            }

            // An attribute written where no element is open is an attribute that is the value rather than
            // something to attach — which is a thing a sequence may hold, and the reason for declaring
            // as="attribute()" in the first place. It used to be dropped, leaving an empty sequence where the
            // stylesheet had plainly produced something.
            FlushText();

            XdmTreeBuilder builder = new XdmTreeBuilder();
            int node = builder.AddParentlessAttribute(prefix, namespaceUri, localName, value);
            m_items.Add(XPath.XPathValue.FromNode(builder.Finish(), node));
        }

        /// <inheritdoc/>
        internal override void WriteAttribute(string prefix, string namespaceUri, string localName, string value, ushort typeId)
        {
            if (m_depth != 0 || typeId == 0)
            {
                base.WriteAttribute(prefix, namespaceUri, localName, value, typeId);
                return;
            }

            // A parentless attribute is a tree of its own, annotated before the tree is finished.
            FlushText();

            XdmTreeBuilder builder = new XdmTreeBuilder();
            int node = builder.AddParentlessAttribute(prefix, namespaceUri, localName, value);
            builder.AnnotateLastAttribute(typeId);
            m_items.Add(XPath.XPathValue.FromNode(builder.Finish(), node));
        }

        /// <summary>The depth of the element whose content is being stripped of annotations, or -1.</summary>
        private int m_stripDepth = -1;

        /// <inheritdoc/>
        internal override void AnnotateElement(ushort typeId, bool nilled)
        {
            if (m_depth > 0 && m_stripDepth < 0)
            {
                m_builder!.AnnotateElement(typeId, nilled);
            }
        }

        /// <inheritdoc/>
        internal override void AnnotateAttribute(ushort typeId)
        {
            if (m_depth > 0 && m_stripDepth < 0)
            {
                m_builder!.AnnotateLastAttribute(typeId);
            }
        }

        /// <inheritdoc/>
        internal override void StripContent()
        {
            if (m_depth > 0 && m_stripDepth < 0)
            {
                m_stripDepth = m_depth;
            }
        }

        /// <inheritdoc/>
        public override void WriteNamespaceDeclaration(string prefix, string namespaceUri)
        {
            if (m_depth != 0)
            {
                m_builder!.AddNamespaceDeclaration(prefix, namespaceUri);
                return;
            }

            // A namespace written where no element is open is a namespace node that is the value — an item
            // of the sequence, which is what makes an xsl:namespace beside an xsl:on-empty count as content.
            FlushText();

            XdmTreeBuilder builder = new XdmTreeBuilder();
            int node = builder.AddParentlessNamespaceNode(prefix, namespaceUri);
            m_items.Add(XPath.XPathValue.FromNode(builder.Finish(), node));
        }

        /// <inheritdoc/>
        public override void EndElement()
        {
            if (m_depth == m_stripDepth)
            {
                m_stripDepth = -1;
            }

            m_builder!.EndElement();

            if (--m_depth == 0)
            {
                FinishNode();
            }
        }

        public override void MarkNoInheritedNamespaces()
        {
            if (m_depth != 0)
            {
                m_builder!.MarkNoInheritedNamespaces();
            }
        }

        /// <summary>Whether each document copy under way began at the top, where it is an item of its own.</summary>
        private readonly Stack<bool> m_documentCopies = new();

        /// <inheritdoc/>
        /// <remarks>
        /// A document node copied at the top is one item of the sequence, a document node with the copied
        /// content as its children — which is what a variable declared <c>document-node()*</c> and filled
        /// by <c>xsl:copy</c> over documents is asking for. Inside an element it contributes its children
        /// and nothing else, as it does anywhere.
        /// </remarks>
        public override void StartDocumentCopy()
        {
            bool top = m_depth == 0;
            m_documentCopies.Push(top);

            if (!top)
            {
                return;
            }

            FlushText();
            m_builder = new XdmTreeBuilder { FixesUpNamespaces = true };
            m_nodeBaseUri = TakeBaseUri();
            m_depth++;
        }

        /// <inheritdoc/>
        public override void EndDocumentCopy()
        {
            if (!m_documentCopies.Pop())
            {
                return;
            }

            m_depth--;

            XdmTree document = m_builder!.Finish();
            document.BaseUri = m_nodeBaseUri;
            m_items.Add(XPath.XPathValue.FromNode(document, XdmTree.RootNode));
            m_builder = null;
        }

        /// <inheritdoc/>
        public override void WriteText(string text)
        {
            if (m_depth != 0)
            {
                m_builder!.AddText(text);
                return;
            }

            if (m_becomesAString)
            {
                // On the way to a string, adjacent text nodes merge and a zero-length one is discarded — the
                // first two steps of making simple content — so text is gathered until something else arrives.
                m_text.Append(text);
                return;
            }

            // Otherwise each text node written is an item of its own, as the instruction that wrote it made
            // it: two xsl:text are two text nodes, and an empty one is a zero-length text node, which a
            // sequence may hold though a tree may not. Merging adjacent text nodes is what building a tree
            // does, not this.
            WriteLeaf(builder => builder.AddText(text, keepEmpty: true));
        }

        public override void WriteAtomic(string text)
        {
            if (m_depth != 0)
            {
                m_builder!.AddAtomic(text);
                return;
            }

            // At the top an atomic value is an item, and the caller normally appends it as one; this is the
            // value having lost its type on the way, which a string carries as well as anything.
            FlushText();
            m_items.Add(XPath.XPathValue.FromString(text));
        }

        /// <inheritdoc/>
        public override void WriteRawText(string text)
        {
            if (m_depth != 0 || m_becomesAString)
            {
                WriteText(text);
                return;
            }

            // A text node of its own, and one that remembers it was written raw: what a template with a
            // declared type hands back keeps the flag, and is written raw again where it lands.
            WriteLeaf(builder => builder.AddRawText(text));
        }

        /// <inheritdoc/>
        public override void WriteComment(string text)
        {
            WriteLeaf(builder => builder.AddComment(text));
        }

        /// <inheritdoc/>
        public override void WriteProcessingInstruction(string target, string data)
        {
            WriteLeaf(builder => builder.AddProcessingInstruction(target, data));
        }

        /// <inheritdoc/>
        public override void NoteSourceOfNext(XdmTree source, int node, XsltRuntime runtime, bool withAttributes)
        {
            if (m_depth != 0)
            {
                return;
            }

            // An element copied with an xml:base of its own resolves it against the base URI of the
            // instruction copying it (§11.9.2), which is this target's; one without — or one copied without
            // its attributes — keeps the base URI it had.
            m_pendingBaseUri =
                withAttributes
                && source.KindOf(node) == NodeKind.Element
                && XPath.Xpath2FunctionExpr.XmlBaseOf(source, node) is not null
                    ? m_baseUri
                    : XPath.Xpath2FunctionExpr.BaseUriOf(source, node, runtime.BaseUriOf(source));
            m_hasPendingBaseUri = true;
        }

        /// <summary>
        /// The base URI for a top-level node that is starting: the one noted for it as a copy, otherwise
        /// the target's own.
        /// </summary>
        private string? TakeBaseUri()
        {
            if (!m_hasPendingBaseUri)
            {
                return m_baseUri;
            }

            m_hasPendingBaseUri = false;
            return m_pendingBaseUri;
        }

        /// <summary>Returns everything the constructor produced, in order.</summary>
        public XPath.XPathValue Finish()
        {
            FlushText();
            return XPath.XdmSequence.Concatenate(m_items);
        }

        private void WriteLeaf(Action<XdmTreeBuilder> add)
        {
            if (m_depth != 0)
            {
                add(m_builder!);
                return;
            }

            FlushText();
            m_builder = new XdmTreeBuilder();
            m_nodeBaseUri = TakeBaseUri();
            add(m_builder);
            FinishNode();
        }

        private void FlushText()
        {
            if (m_text.Length == 0)
            {
                return;
            }

            string text = m_text.ToString();
            m_text.Clear();

            m_builder = new XdmTreeBuilder();
            m_nodeBaseUri = TakeBaseUri();
            m_builder.AddText(text);
            FinishNode();
        }

        /// <summary>Completes the tree holding one top-level node and files that node as an item.</summary>
        /// <remarks>
        /// The node is detached before the tree is finished, so it is a root rather than a document's child.
        /// That is what an <c>as</c> declaration asks for: the constructor produces a sequence, and the nodes
        /// in a sequence are parentless. Which node it is has to be read before detaching, since afterwards
        /// the document node has no children to ask about.
        /// </remarks>
        private void FinishNode()
        {
            int node = m_builder!.FirstTopLevelNode;
            m_builder.Detach();

            XdmTree tree = m_builder.Finish();
            tree.BaseUri = m_nodeBaseUri;
            m_builder = null;

            if (node >= 0)
            {
                m_items.Add(XPath.XPathValue.FromNode(tree, node));
            }
        }
    }

    /// <summary>
    /// Captures instruction output into an <see cref="XdmTree"/>, which is how a result tree fragment — the
    /// value of an <c>xsl:variable</c> with content rather than a <c>select</c> — is represented.
    /// </summary>
    public sealed class ResultTreeBuilder : OutputTarget
    {
        private readonly XdmTreeBuilder m_builder = new() { FixesUpNamespaces = true };

        /// <summary>
        /// Gets the base URI of the document node built, which is that of the instruction whose content
        /// the tree is (§9.4): what a relative reference inside the tree resolves against, and what
        /// <c>base-uri()</c> of the document node answers.
        /// </summary>
        public string? BaseUri { get; init; }

        /// <summary>
        /// Gets whether this tree is a final result of the transformation rather than a temporary one.
        /// </summary>
        /// <remarks>
        /// A result document delivered as a document node is built here, and an <c>xsl:result-document</c>
        /// inside it is writing a result of the transformation, not writing into a variable. Everything else
        /// built here is temporary, which is why this is off unless the builder is told.
        /// </remarks>
        public bool StandsForFinalOutput { get; init; }

        /// <inheritdoc/>
        public override bool IsFinalOutput => StandsForFinalOutput;

        /// <inheritdoc/>
        public override int OpenElementDepth => m_builder.OpenElementDepth;

        /// <inheritdoc/>
        public override void MarkNoInheritedNamespaces()
        {
            m_builder.MarkNoInheritedNamespaces();
        }

        /// <inheritdoc/>
        public override void StartElement(string prefix, string namespaceUri, string localName)
        {
            m_builder.StartElement(prefix, namespaceUri, localName);
        }

        /// <inheritdoc/>
        public override void WriteAttribute(string prefix, string namespaceUri, string localName, string value)
        {
            // As with CreateNamespace below: the builder guards its own ordering and reports a misuse of the
            // builder, where what the stylesheet author needs to hear is that an attribute cannot go where
            // this one was put — the top of a captured tree, which is a document node and carries none.
            try
            {
                m_builder.AddAttribute(prefix, namespaceUri, localName, value);
            }
            catch (InvalidOperationException error)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0420,
                    $"An attribute ('{localName}') cannot be added here. {error.Message}",
                    error);
            }
        }

        /// <inheritdoc/>
        public override void WriteNamespaceDeclaration(string prefix, string namespaceUri)
        {
            // A copied namespace node where no element is open — the top of a tree, which is a document
            // node and carries none — is the same misplacement as an attribute there.
            try
            {
                m_builder.AddNamespaceDeclaration(prefix, namespaceUri);
            }
            catch (InvalidOperationException error)
            {
                string named = prefix.Length == 0 ? "the default namespace" : $"the prefix '{prefix}'";

                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0420,
                    $"A namespace node ({named}) cannot be added here. {error.Message}",
                    error);
            }
        }

        /// <inheritdoc/>
        public override void CreateNamespace(string prefix, string namespaceUri)
        {
            // The builder guards its own ordering, since adding a namespace after a child would corrupt the
            // ranges it keeps. What it reports is a misuse of the builder; what the stylesheet author needs to
            // hear is which instruction was in the wrong place, so the two are said together.
            try
            {
                m_builder.AddNamespaceDeclaration(prefix, namespaceUri);
            }
            catch (InvalidOperationException error)
            {
                string named = prefix.Length == 0 ? "the default namespace" : $"the prefix '{prefix}'";

                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0420,
                    $"A namespace declaration ({named}) cannot be added here. {error.Message}",
                    error);
            }
        }

        /// <inheritdoc/>
        public override void EndElement()
        {
            if (m_builder.OpenElementDepth == m_stripDepth)
            {
                m_stripDepth = -1;
            }

            m_builder.EndElement();
        }

        /// <summary>The depth of the element whose content is being stripped of annotations, or -1.</summary>
        private int m_stripDepth = -1;

        /// <inheritdoc/>
        internal override void AnnotateElement(ushort typeId, bool nilled)
        {
            if (m_stripDepth < 0 && m_builder.OpenElementDepth > 0)
            {
                m_builder.AnnotateElement(typeId, nilled);
            }
        }

        /// <inheritdoc/>
        internal override void AnnotateAttribute(ushort typeId)
        {
            if (m_stripDepth < 0)
            {
                m_builder.AnnotateLastAttribute(typeId);
            }
        }

        /// <inheritdoc/>
        internal override void StripContent()
        {
            if (m_stripDepth < 0 && m_builder.OpenElementDepth > 0)
            {
                m_stripDepth = m_builder.OpenElementDepth;
            }
        }

        /// <inheritdoc/>
        public override void WriteText(string text)
        {
            m_builder.AddText(text);
        }

        /// <inheritdoc/>
        public override void WriteRawText(string text)
        {
            // Escaping is a serialization concern; a captured tree simply holds the characters.
            m_builder.AddText(text);
        }

        public override void WriteAtomic(string text)
        {
            m_builder.AddAtomic(text);
        }

        /// <inheritdoc/>
        public override void WriteComment(string text)
        {
            m_builder.AddComment(text);
        }

        /// <inheritdoc/>
        public override void WriteProcessingInstruction(string target, string data)
        {
            m_builder.AddProcessingInstruction(target, data);
        }

        /// <inheritdoc/>
        public override void NoteCopiedFrom(XdmTree source, int node)
        {
            m_builder.NoteCopiedFrom(source, node);
        }

        /// <inheritdoc/>
        public override void NoteDocumentCopiedFrom(XdmTree source)
        {
            m_builder.InheritUnparsedEntities(source);
        }

        /// <summary>Completes the captured tree.</summary>
        /// <returns>The tree holding everything written to this target.</returns>
        public XdmTree Finish()
        {
            XdmTree tree = m_builder.Finish();
            tree.BaseUri = BaseUri;
            return tree;
        }
    }
}
