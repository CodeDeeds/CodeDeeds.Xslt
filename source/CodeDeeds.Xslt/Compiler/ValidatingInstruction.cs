using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>What an instruction that validates its result produces: an element, a document, or an attribute.</summary>
    internal enum ValidationShape
    {
        /// <summary>An element and its content: <c>xsl:element</c>, a literal result element.</summary>
        Element,

        /// <summary>A document node: <c>xsl:document</c>, which is validated even when empty.</summary>
        Document,

        /// <summary>
        /// A copy of nodes: <c>xsl:copy</c> and <c>xsl:copy-of</c>, where validation has an effect only when
        /// the copied content holds an element or attribute (XSLT 3.0 §5.6.2) and is document-level then.
        /// </summary>
        Copy,

        /// <summary>One attribute: <c>xsl:attribute</c>.</summary>
        Attribute,
    }

    /// <summary>
    /// Wraps an instruction that constructs nodes and carries <c>validation="strict"</c>, <c>"lax"</c> or a
    /// <c>type</c>: it builds the result, validates it against the schemas, and writes it out carrying the
    /// types validation settled on.
    /// </summary>
    /// <remarks>
    /// Only strict, lax and a named type reach here. <c>validation="preserve"</c> and <c>"strip"</c> ask for
    /// no validation — the constructed content is untyped either way — and are left to the instruction, which
    /// for a copy keeps or drops the annotations of what it copies and for a fresh element has none to keep.
    /// </remarks>
    internal sealed class ValidatingInstruction : Instruction
    {
        private readonly Instruction m_inner;
        private readonly ValidationShape m_shape;
        private readonly bool m_strict;
        private readonly XdmSchemaType? m_type;
        private readonly string? m_baseUri;
        private readonly IReadOnlyDictionary<string, string>? m_namespaces;

        /// <summary>Initializes a validating wrapper.</summary>
        /// <param name="inner">The instruction whose result is validated.</param>
        /// <param name="shape">What the instruction produces.</param>
        /// <param name="strict">Whether validation is strict rather than lax; ignored where a type is named.</param>
        /// <param name="type">The type a <c>type</c> attribute named, or null for validation by mode.</param>
        /// <param name="baseUri">The base URI the captured tree takes.</param>
        /// <param name="namespaces">The namespaces in scope, for validating an attribute value that is a name.</param>
        public ValidatingInstruction(
            Instruction inner,
            ValidationShape shape,
            bool strict,
            XdmSchemaType? type,
            string? baseUri,
            IReadOnlyDictionary<string, string>? namespaces)
        {
            m_inner = inner;
            m_shape = shape;
            m_strict = strict;
            m_type = type;
            m_baseUri = baseUri;
            m_namespaces = namespaces;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            SchemaComponents schemas = runtime.Schemas
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XTSE1660, "Validation was asked for, but the stylesheet imported no schema.");

            if (m_shape == ValidationShape.Attribute)
            {
                ValidateAttribute(schemas, ref context, runtime);
                return;
            }

            if (m_shape == ValidationShape.Copy)
            {
                ValidateCopy(schemas, ref context, runtime);
                return;
            }

            // Build the result into a tree of its own, then validate that tree and write it out annotated.
            ResultTreeBuilder builder = new ResultTreeBuilder { BaseUri = m_baseUri };
            OutputTarget previous = runtime.Output;
            runtime.Output = builder;

            try
            {
                m_inner.Execute(ref context, runtime);
            }
            finally
            {
                runtime.Output = previous;
            }

            XdmTree built = builder.Finish();
            NodeValidator validator = new NodeValidator(schemas);

            // A document node is held to its own shape whether it is validated by a mode or against a
            // named type: one element child and no text, or XTTE1550.
            if (m_shape == ValidationShape.Document)
            {
                validator.RequireDocumentShape(built);
            }

            TypeOverlay overlay = m_type is not null
                ? validator.ValidateElementAgainstType(built, m_type)
                : m_shape == ValidationShape.Element
                    ? validator.ValidateElement(built, m_strict)
                    : validator.ValidateDocument(built, m_strict);

            XdmTree annotated = built.WithTypeAnnotations(overlay);

            // A document node produced by xsl:document is one item where the target holds a sequence, and its
            // children elsewhere; other shapes contribute the constructed content, which is the doc's children.
            if (m_shape == ValidationShape.Document
                && runtime.Output.TryAppendValue(Validated(annotated, XdmTree.RootNode, overlay)))
            {
                return;
            }

            // The overlay goes with the tree: it holds the annotations, which the tree carries already,
            // and the attributes validation supplied, which only it knows about.
            for (int child = annotated.FirstChildOf(XdmTree.RootNode); child >= 0; child = annotated.NextSiblingOf(child))
            {
                NodeCopier.CopyDeep(annotated, child, runtime.Output, runtime: runtime, types: overlay);
            }
        }

        /// <summary>
        /// Validates what <c>xsl:copy</c> or <c>xsl:copy-of</c> copied. The copied nodes are captured as the
        /// sequence they are — a document node stays a document node, an element an element — so that what
        /// comes out is shaped as the unvalidated copy would be; text and atomic values are left untouched,
        /// validation having no effect on them (XSLT 3.0 §5.6.2).
        /// </summary>
        private void ValidateCopy(SchemaComponents schemas, ref DynamicContext context, XsltRuntime runtime)
        {
            SequenceCaptureTarget capture = new SequenceCaptureTarget();
            OutputTarget previous = runtime.Output;
            runtime.Output = capture;

            try
            {
                m_inner.Execute(ref context, runtime);
            }
            finally
            {
                runtime.Output = previous;
            }

            NodeValidator validator = new NodeValidator(schemas);

            foreach (XPathValue item in XdmSequence.Items(capture.Finish()))
            {
                if (item.Kind != XPathValueKind.Node)
                {
                    SequenceWriter.Write(item, runtime);
                    continue;
                }

                XdmTree tree = item.NodeTree;
                int node = item.NodeId;
                NodeKind kind = tree.KindOf(node);

                if (kind == NodeKind.Root)
                {
                    // A copied document node: its element child is validated against the named type, or the
                    // whole node as a document by mode.
                    TypeOverlay overlay = m_type is not null
                        ? validator.ValidateElementAgainstType(tree, m_type)
                        : validator.ValidateDocument(tree, m_strict);

                    SequenceWriter.Write(Validated(tree.WithTypeAnnotations(overlay), node, overlay), runtime);
                }
                else if (kind == NodeKind.Element)
                {
                    TypeOverlay overlay = validator.ValidateElementNode(tree, node, m_strict, m_type);

                    SequenceWriter.Write(Validated(tree.WithTypeAnnotations(overlay), node, overlay), runtime);
                }
                else if (kind == NodeKind.Attribute)
                {
                    WriteValidatedAttribute(schemas, tree, node, runtime);
                }
                else
                {
                    // A text, comment, processing instruction or namespace node: validation has no effect
                    // on it, so it is written as it was copied.
                    SequenceWriter.Write(item, runtime);
                }
            }
        }

        /// <summary>
        /// A validated node as the one item it is, an element or a document node, carrying what validation
        /// settled for it and for everything beneath it.
        /// </summary>
        /// <remarks>
        /// Usually the node itself, in the tree that carries the overlay's annotations: nothing is copied.
        /// But §25.4.1 has validation "where necessary create new nodes containing these default values",
        /// and an attribute a schema supplied is a node the tree does not have and cannot be given without
        /// renumbering every attribute after it. So where validation supplied one, the item is a copy of
        /// the node made through the overlay, which is how the copier writes a validated tree anywhere: the
        /// supplied attributes after the element's own, annotated as their declarations say. The copy is
        /// taken at the top of a sequence, so a document node is still a document node and an element a
        /// parentless element, and it keeps the tree's base URI, a document node's unparsed entities and —
        /// where the copy being validated kept them — what each node answers for the accumulators.
        /// </remarks>
        /// <param name="annotated">The tree the node is in, carrying the overlay's annotations.</param>
        /// <param name="node">The node validated: the tree's document node, or an element in it.</param>
        /// <param name="overlay">What validation settled.</param>
        private static XPathValue Validated(XdmTree annotated, int node, TypeOverlay overlay)
        {
            if (!overlay.SuppliesAttributes)
            {
                return XPathValue.FromNodeSet(NodeSet.Singleton(annotated, node));
            }

            SequenceCaptureTarget copy = new SequenceCaptureTarget(baseUri: annotated.BaseUri);
            NodeCopier.CopyDeep(
                annotated, node, copy, copyAccumulators: annotated.CopiedFrom is not null, types: overlay);
            return copy.Finish();
        }

        /// <summary>Validates a copied attribute against its declaration or the named type, and writes it annotated.</summary>
        private void WriteValidatedAttribute(SchemaComponents schemas, XdmTree tree, int attribute, XsltRuntime runtime)
        {
            int fingerprint = tree.FingerprintOf(attribute);
            string uri = tree.NameTable.GetNamespaceUri(fingerprint);
            string local = tree.NameTable.GetLocalName(fingerprint);
            string prefix = tree.NameTable.GetPrefix(tree.NameCodeOf(attribute));
            string value = tree.StringValueOf(attribute);

            XdmSchemaType? type = m_type;

            if (type is null)
            {
                XdmSchemaDeclaration? declaration = schemas.FindAttribute(uri, local);

                if (declaration is null)
                {
                    if (m_strict)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTTE1512,
                            $"Strict validation was asked for, and the schemas in scope declare no top-level "
                            + $"attribute '{(uri.Length == 0 ? local : "Q{" + uri + "}" + local)}'.");
                    }

                    runtime.Output.WriteAttribute(prefix, uri, local, value);
                    return;
                }

                type = declaration.Type;
            }

            ushort typeId = NodeValidator.ValidateAttributeValue(
                type, value, m_namespaces, byType: m_type is not null, m_strict);

            runtime.Output.WriteAttribute(prefix, uri, local, value, typeId);
        }

        private void ValidateAttribute(SchemaComponents schemas, ref DynamicContext context, XsltRuntime runtime)
        {
            SequenceCaptureTarget capture = new SequenceCaptureTarget();
            OutputTarget previous = runtime.Output;
            runtime.Output = capture;

            try
            {
                m_inner.Execute(ref context, runtime);
            }
            finally
            {
                runtime.Output = previous;
            }

            List<XPathValue> items = XdmSequence.Items(capture.Finish());

            foreach (XPathValue item in items)
            {
                if (item.Kind != XPathValueKind.Node
                    || item.NodeTree.KindOf(item.NodeId) != NodeKind.Attribute)
                {
                    continue;
                }

                XdmTree tree = item.NodeTree;
                int attribute = item.NodeId;
                int fingerprint = tree.FingerprintOf(attribute);
                string uri = tree.NameTable.GetNamespaceUri(fingerprint);
                string local = tree.NameTable.GetLocalName(fingerprint);
                string prefix = tree.NameTable.GetPrefix(tree.NameCodeOf(attribute));
                string value = tree.StringValueOf(attribute);

                XdmSchemaType? type = m_type;

                if (type is null)
                {
                    // Validation by mode: against the top-level attribute declaration of the name, where
                    // there is one. A name with none is untyped under lax and unknown under strict.
                    XdmSchemaDeclaration? declaration = schemas.FindAttribute(uri, local);

                    if (declaration is null)
                    {
                        if (m_strict)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTTE1512,
                                $"Strict validation was asked for, and the schemas in scope declare no "
                                + $"top-level attribute '{(uri.Length == 0 ? local : "Q{" + uri + "}" + local)}'.");
                        }

                        runtime.Output.WriteAttribute(prefix, uri, local, value);
                        continue;
                    }

                    type = declaration.Type;
                }

                ushort typeId = NodeValidator.ValidateAttributeValue(
                    type, value, m_namespaces, byType: m_type is not null, m_strict);

                runtime.Output.WriteAttribute(prefix, uri, local, value, typeId);
            }
        }
    }
}
