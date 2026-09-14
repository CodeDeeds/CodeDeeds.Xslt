using System.Xml;
using System.Xml.Schema;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// Validates a tree an instruction constructed against the schemas in scope, and says what type each
    /// node came out as, for <c>validation="strict"</c> and <c>"lax"</c> and for a named <c>type</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tree is built first and walked into .NET's push validator, <see cref="XmlSchemaValidator"/>,
    /// which reports the declaration, the type and the validity of each element and attribute, and the
    /// document-level constraints — one ID used twice, a reference to an ID nothing declares — at the end.
    /// What it settles becomes a <see cref="TypeOverlay"/> the copier reads as it writes the nodes out;
    /// an invalid document is the error the specification names, chosen by what was being validated.
    /// </para>
    /// <para>
    /// Strict and lax differ only at the top: a document element with no top-level declaration is
    /// <c>XTTE1512</c> under strict and untyped under lax. Below the top the schema's own content models
    /// decide, and an element they do not allow is <c>XTTE1510</c> under strict and <c>XTTE1515</c> under
    /// lax. A named type validates by partial validation and its failures are <c>XTTE1540</c>.
    /// </para>
    /// </remarks>
    internal sealed class NodeValidator
    {
        private readonly SchemaComponents m_schemas;
        private readonly XmlSchemaSet m_set;

        public NodeValidator(SchemaComponents schemas)
        {
            m_schemas = schemas;
            m_set = schemas.ValidatingSet;
        }

        /// <summary>
        /// Validates a constructed document node — the tree an instruction's content built — against the
        /// schemas, strictly or laxly, and returns what each node came out as.
        /// </summary>
        /// <param name="tree">The tree, rooted at a document node.</param>
        /// <param name="strict">Whether an undeclared document element is an error rather than untyped.</param>
        /// <exception cref="XsltException">The document is not valid; see the class remarks.</exception>
        public TypeOverlay ValidateDocument(XdmTree tree, bool strict)
        {
            int root = XdmTree.RootNode;

            // Exactly one element child, no text: what a document node has to be for validation to mean
            // anything (XTTE1550). Comments and processing instructions are allowed among the children,
            // and whitespace between them is not text of the document's own.
            int elementChild = -1;

            for (int child = tree.FirstChildOf(root); child >= 0; child = tree.NextSiblingOf(child))
            {
                switch (tree.KindOf(child))
                {
                    case NodeKind.Element:
                        if (elementChild >= 0)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTTE1550,
                                "A document node was validated whose children are more than one element.");
                        }

                        elementChild = child;
                        break;

                    case NodeKind.Text:
                        if (tree.StringValueOf(child).AsSpan().Trim().Length != 0)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.XTTE1550,
                                "A document node was validated that has text among its children, which a "
                                + "validated document may not.");
                        }

                        break;
                }
            }

            if (elementChild < 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE1550,
                    "A document node was validated that has no element child, and a validated document has "
                    + "exactly one.");
            }

            return ValidateFrom(tree, elementChild, strict, identityConstraints: true);
        }

        /// <summary>
        /// Validates one constructed element against the schemas, strictly or laxly, without the
        /// document-level ID and identity constraints, which apply only to a validated document node.
        /// </summary>
        /// <param name="tree">The tree, rooted at a document node whose one element child is validated.</param>
        /// <param name="strict">Whether an undeclared element is an error rather than untyped.</param>
        public TypeOverlay ValidateElement(XdmTree tree, bool strict)
        {
            int element = FirstElement(tree);
            return element < 0 ? new TypeOverlay() : ValidateFrom(tree, element, strict, identityConstraints: false);
        }

        /// <summary>
        /// Validates one element node — a parentless element a copy produced — strictly or laxly, with the
        /// document-level identity constraints a copied sequence is subject to.
        /// </summary>
        /// <param name="tree">The tree the element belongs to.</param>
        /// <param name="element">The element node.</param>
        /// <param name="strict">Whether an undeclared element is an error rather than untyped.</param>
        /// <param name="type">A type to validate against instead of by declaration, or null.</param>
        public TypeOverlay ValidateElementNode(XdmTree tree, int element, bool strict, XdmSchemaType? type)
        {
            if (type is not null)
            {
                return type.Definition is null
                    ? new TypeOverlay()
                    : Run(tree, element, type.Definition, strict: true, XsltErrorCode.XTTE1540, XsltErrorCode.XTTE1540, identityConstraints: false);
            }

            return ValidateFrom(tree, element, strict, identityConstraints: true);
        }

        /// <summary>Finds the single element child of the constructed document node, and validates from it.</summary>
        private TypeOverlay ValidateFrom(XdmTree tree, int element, bool strict, bool identityConstraints)
        {
            // Strict validation is against a top-level declaration of the element; lax leaves an undeclared
            // one, and everything under it, untyped. An xsi:type supplies the type where there is no
            // declaration, so an element carrying one is not undeclared for this.
            int fingerprint = tree.FingerprintOf(element);
            string uri = tree.NameTable.GetNamespaceUri(fingerprint);
            string local = tree.NameTable.GetLocalName(fingerprint);

            if (m_set.GlobalElements[new XmlQualifiedName(local, uri)] is null && XsiType(tree, element) is null)
            {
                if (strict)
                {
                    throw TreeValidation.Undeclared(uri, local);
                }

                return new TypeOverlay();
            }

            return Run(tree, element, partial: null, strict, XsltErrorCode.XTTE1510, XsltErrorCode.XTTE1515, identityConstraints);
        }

        /// <summary>
        /// Validates a constructed element against a named type by partial validation, and returns what it
        /// and its content came out as.
        /// </summary>
        /// <param name="tree">The tree, rooted at a document node whose one element child is validated.</param>
        /// <param name="type">The type the <c>type</c> attribute named.</param>
        /// <exception cref="XsltException"><c>XTTE1540</c> where the element is not valid against the type.</exception>
        public TypeOverlay ValidateElementAgainstType(XdmTree tree, XdmSchemaType type)
        {
            int element = FirstElement(tree);

            if (element < 0 || type.Definition is null)
            {
                return new TypeOverlay();
            }

            return Run(
                tree, element, type.Definition, strict: true,
                XsltErrorCode.XTTE1540, XsltErrorCode.XTTE1540, identityConstraints: false);
        }

        private static int FirstElement(XdmTree tree)
        {
            for (int child = tree.FirstChildOf(XdmTree.RootNode); child >= 0; child = tree.NextSiblingOf(child))
            {
                if (tree.KindOf(child) == NodeKind.Element)
                {
                    return child;
                }
            }

            return -1;
        }

        /// <summary>
        /// Validates a constructed attribute's value against a type, and returns the number to annotate it
        /// with.
        /// </summary>
        /// <param name="type">The type named, or the declared type where validation found a declaration.</param>
        /// <param name="value">The attribute's value.</param>
        /// <param name="namespaces">The namespaces in scope where the attribute was written.</param>
        /// <param name="byType">Whether a <c>type</c> attribute named the type, so a failure is <c>XTTE1540</c>.</param>
        /// <param name="strict">Whether validation is strict, where a failure is <c>XTTE1510</c>.</param>
        /// <exception cref="XsltException">The value is not one of the type's.</exception>
        public static ushort ValidateAttributeValue(
            XdmSchemaType type, string value, IReadOnlyDictionary<string, string>? namespaces, bool byType, bool strict)
        {
            // An attribute may not be validated against a type built on xs:QName or xs:NOTATION: the
            // lexical form's meaning would depend on namespaces the copy has lost (XTTE1545).
            if (byType && type.UsesQNames)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE1545,
                    $"An attribute cannot be validated against {type.Written}, which is built on xs:QName or "
                    + "xs:NOTATION: a name's meaning depends on the namespaces in scope, which a validated "
                    + "attribute does not carry.");
            }

            if (type.Variety == XdmSchemaVariety.Complex)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE1535,
                    $"{type.Written} is a complex type, and an attribute is validated against a simple one.");
            }

            if (type.Refuses(value, namespaces) is string problem)
            {
                throw XsltErrors.Error(
                    byType ? XsltErrorCode.XTTE1540 : strict ? XsltErrorCode.XTTE1510 : XsltErrorCode.XTTE1515,
                    $"The attribute's value '{value}' is not valid against {type.Written}: {problem}");
            }

            return type.Id;
        }

        /// <summary>Walks an element subtree into the validator, annotating as it goes.</summary>
        private TypeOverlay Run(
            XdmTree tree,
            int element,
            XmlSchemaType? partial,
            bool strict,
            XsltErrorCode invalid,
            XsltErrorCode laxInvalid,
            bool identityConstraints)
        {
            TypeOverlay overlay = new TypeOverlay();
            List<(bool Id, string Message)> problems = new();

            XmlNamespaceManager namespaces = new XmlNamespaceManager(m_set.NameTable);
            XmlSchemaValidator validator = new XmlSchemaValidator(
                m_set.NameTable,
                m_set,
                namespaces,
                identityConstraints
                    ? XmlSchemaValidationFlags.ProcessIdentityConstraints
                    : XmlSchemaValidationFlags.None);

            validator.ValidationEventHandler += (_, e) =>
            {
                if (e.Severity == XmlSeverityType.Error)
                {
                    problems.Add((IsIdentityProblem(e.Message), e.Message));
                }
            };

            validator.XmlResolver = null;

            if (partial is null)
            {
                validator.Initialize();
            }
            else
            {
                validator.Initialize(partial);
            }

            Walk(tree, element, validator, namespaces, overlay);
            validator.EndValidation();

            // A document-level constraint — one ID used twice, a reference to an undeclared ID — is always
            // XTTE1555, whatever the mode; a value or content-model failure is the mode's own error.
            foreach ((bool id, string message) in problems)
            {
                if (id)
                {
                    throw XsltErrors.Error(XsltErrorCode.XTTE1555, $"The document is not valid: {message}");
                }
            }

            if (problems.Count > 0)
            {
                throw XsltErrors.Error(
                    partial is not null ? invalid : strict ? invalid : laxInvalid,
                    $"The constructed node is not valid: {problems[0].Message}");
            }

            return overlay;
        }

        private void Walk(
            XdmTree tree, int element, XmlSchemaValidator validator, XmlNamespaceManager namespaces, TypeOverlay overlay)
        {
            Model.NameTable names = tree.NameTable;
            int fingerprint = tree.FingerprintOf(element);
            string uri = names.GetNamespaceUri(fingerprint);
            string local = names.GetLocalName(fingerprint);

            // The element's own namespace declarations, so a QName-valued attribute or the xsi:type of the
            // element resolves against what the element carries.
            namespaces.PushScope();
            foreach ((string prefix, string ns) in tree.NamespaceDeclarationsOf(element))
            {
                if (prefix != "xmlns")
                {
                    namespaces.AddNamespace(prefix, ns);
                }
            }

            XmlSchemaInfo info = new XmlSchemaInfo();
            validator.ValidateElement(local, uri, info, XsiType(tree, element), null, null, null);

            int attributeCount = tree.AttributeCountOf(element);
            for (int i = 0; i < attributeCount; i++)
            {
                int attribute = tree.AttributeAt(element, i);
                int attributeName = tree.FingerprintOf(attribute);
                string attributeUri = names.GetNamespaceUri(attributeName);

                // xsi and xmlns attributes are the validator's own business, not content to validate.
                if (attributeUri == "http://www.w3.org/2001/XMLSchema-instance"
                    || attributeUri == "http://www.w3.org/2000/xmlns/")
                {
                    continue;
                }

                XmlSchemaInfo attributeInfo = new XmlSchemaInfo();
                validator.ValidateAttribute(
                    names.GetLocalName(attributeName), attributeUri, tree.StringValueOf(attribute), attributeInfo);

                if (attributeInfo.SchemaType is XmlSchemaType attributeType)
                {
                    overlay.Set(attribute, m_schemas.TypeIdOf(attributeType));
                }
            }

            validator.ValidateEndOfAttributes(info);

            bool hasElementChild = false;
            for (int child = tree.FirstChildOf(element); child >= 0; child = tree.NextSiblingOf(child))
            {
                if (tree.KindOf(child) == NodeKind.Element)
                {
                    hasElementChild = true;
                    break;
                }
            }

            // Text is fed as text, whitespace as whitespace, so that an element-only content model does not
            // trip over the indentation between its children.
            for (int child = tree.FirstChildOf(element); child >= 0; child = tree.NextSiblingOf(child))
            {
                switch (tree.KindOf(child))
                {
                    case NodeKind.Text:
                        string text = tree.StringValueOf(child);
                        if (hasElementChild && text.AsSpan().Trim().Length == 0)
                        {
                            validator.ValidateWhitespace(text);
                        }
                        else
                        {
                            validator.ValidateText(text);
                        }

                        break;

                    case NodeKind.Element:
                        Walk(tree, child, validator, namespaces, overlay);
                        break;
                }
            }

            XmlSchemaInfo end = new XmlSchemaInfo();
            validator.ValidateEndElement(end);

            if (end.SchemaType is XmlSchemaType settled)
            {
                overlay.Set(element, m_schemas.TypeIdOf(settled));
            }

            if (end.IsNil)
            {
                overlay.MarkNilled(element);
            }

            namespaces.PopScope();
        }

        /// <summary>The value of an element's <c>xsi:type</c>, which steers what it is validated against.</summary>
        private static string? XsiType(XdmTree tree, int element)
        {
            int attribute = tree.FindAttribute(
                element, tree.NameTable.GetFingerprint("http://www.w3.org/2001/XMLSchema-instance", "type"));

            return attribute < 0 ? null : tree.StringValueOf(attribute);
        }

        /// <summary>Whether a validator's message is about an ID, an IDREF or an identity constraint.</summary>
        private static bool IsIdentityProblem(string message)
        {
            return message.Contains("as an ID", StringComparison.Ordinal)
                || message.Contains("undeclared ID", StringComparison.Ordinal)
                || message.Contains("Reference to undeclared", StringComparison.Ordinal)
                || message.Contains("identity constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("KeyRef", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
        }
    }
}
