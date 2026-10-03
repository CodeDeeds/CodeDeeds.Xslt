using System.Xml.Schema;
using CodeDeeds.Xslt.Compiler;

namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// A compiled set of XML Schemas that documents are read and validated against, so that their nodes
    /// carry type annotations, outside any stylesheet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A schema-aware transformation validates what it reads and what it builds on its own account; this
    /// is for a caller who has a document, or a tree of their own making, and wants it typed: to hand to
    /// <see cref="Xslt.Transform(XdmTree)"/> as a validated input, to evaluate an XPath expression
    /// against with <see cref="XPath.XPathStaticContext.TypedSchemas"/>, or to read the types back with
    /// <see cref="XdmTree.TypeNameOf(int)"/>.
    /// </para>
    /// <para>
    /// Validation is .NET's <c>System.Xml.Schema</c> and so is XSD 1.0. It holds a date in
    /// <see cref="DateTime"/>, so a document with a year before 1 or after 9999 in an <c>xs:date</c>,
    /// <c>xs:dateTime</c>, <c>xs:gYear</c> or <c>xs:gYearMonth</c> is refused as invalid; the message says so.
    /// </para>
    /// <para>
    /// The same instance may be used from several threads at once, and the trees it produces share its type
    /// numbers, so documents validated by one instance and a stylesheet given the same
    /// <see cref="XmlSchemaSet"/> agree about what a type is.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// XmlSchemaSet set = new XmlSchemaSet();
    /// set.Add(null, "order.xsd");
    ///
    /// XdmSchemas schemas = new XdmSchemas(set);
    /// XdmTree order = schemas.Parse("&lt;order id='7'&gt;&lt;qty&gt;3&lt;/qty&gt;&lt;/order&gt;");
    ///
    /// // The first child of the document node is the order element; ask what it was validated as.
    /// var (uri, name) = order.TypeNameOf(order.FirstChildOf(XdmTree.RootNode)) ?? default;
    /// </code>
    /// </example>
    public sealed class XdmSchemas
    {
        /// <summary>Initializes the schemas from a set the caller has built.</summary>
        /// <param name="schemas">
        /// The schemas. The set is read and not changed: what is validated against is a compiled copy.
        /// </param>
        /// <param name="importNamespaces">
        /// Namespaces to import by name alone, as <c>xsl:import-schema namespace="..."</c> does: those the
        /// engine carries a schema for, the <c>xml</c> namespace and the XPath functions namespace, are put
        /// in scope; any other is left to the set.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="schemas"/> is <see langword="null"/>.</exception>
        /// <exception cref="XsltException">The schemas do not compile.</exception>
        public XdmSchemas(XmlSchemaSet schemas, IEnumerable<string>? importNamespaces = null)
        {
            ArgumentNullException.ThrowIfNull(schemas);

            Components = new SchemaComponents(schemas, null);

            foreach (string namespaceUri in importNamespaces ?? Array.Empty<string>())
            {
                Components.Import(namespaceUri, null, null, null);
            }

            Components.EnsureCompiled();
        }

        internal SchemaComponents Components { get; }

        /// <summary>Reads XML and validates it as it is read, so that its elements and attributes are typed.</summary>
        /// <param name="xml">The XML text.</param>
        /// <param name="mode"><see cref="XsltValidation.Strict"/> or <see cref="XsltValidation.Lax"/>.</param>
        /// <param name="nameTable">The table that interns names, to share one across trees; null makes one.</param>
        /// <param name="baseUri">The document's base URI, or null.</param>
        /// <returns>The tree, rooted at a document node.</returns>
        /// <exception cref="ArgumentException"><paramref name="mode"/> is neither strict nor lax.</exception>
        /// <exception cref="XsltException">
        /// The document is not valid: <c>XTTE1510</c> under strict validation and <c>XTTE1515</c> under lax,
        /// <c>XTTE1512</c> for a document element strict validation finds no declaration for.
        /// </exception>
        public XdmTree Parse(string xml, XsltValidation mode = XsltValidation.Strict, NameTable? nameTable = null, string? baseUri = null)
        {
            ArgumentNullException.ThrowIfNull(xml);

            using StringReader reader = new StringReader(xml);
            return Parse(reader, mode, nameTable, baseUri);
        }

        /// <summary>Reads XML from a reader and validates it as it is read.</summary>
        /// <param name="reader">The XML. The caller keeps ownership.</param>
        /// <param name="mode"><see cref="XsltValidation.Strict"/> or <see cref="XsltValidation.Lax"/>.</param>
        /// <param name="nameTable">The table that interns names, to share one across trees; null makes one.</param>
        /// <param name="baseUri">The document's base URI, or null.</param>
        /// <returns>The tree, rooted at a document node.</returns>
        /// <exception cref="ArgumentException"><paramref name="mode"/> is neither strict nor lax.</exception>
        /// <exception cref="XsltException">The document is not valid; see <see cref="Parse(string, XsltValidation, NameTable?, string?)"/>.</exception>
        public XdmTree Parse(TextReader reader, XsltValidation mode = XsltValidation.Strict, NameTable? nameTable = null, string? baseUri = null)
        {
            ArgumentNullException.ThrowIfNull(reader);

            return XdmTreeBuilder.FromXmlValidated(reader, null, null, baseUri, ValidationFor(mode), nameTable);
        }

        /// <summary>
        /// Validates a tree the caller built, and returns it with its elements and attributes typed.
        /// </summary>
        /// <param name="tree">A tree rooted at a document node with one element child. It is not changed.</param>
        /// <param name="mode"><see cref="XsltValidation.Strict"/> or <see cref="XsltValidation.Lax"/>.</param>
        /// <returns>
        /// A tree over the same nodes, carrying the types validation settled on. Under lax validation a
        /// document element the schemas do not declare, and everything under it, stays untyped.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="mode"/> is neither strict nor lax.</exception>
        /// <exception cref="XsltException">
        /// The tree is not valid (<c>XTTE1510</c> or <c>XTTE1515</c>), is not shaped as a document is
        /// (<c>XTTE1550</c>), or under strict validation has an undeclared document element (<c>XTTE1512</c>).
        /// </exception>
        public XdmTree Validate(XdmTree tree, XsltValidation mode = XsltValidation.Strict)
        {
            ArgumentNullException.ThrowIfNull(tree);

            bool strict = ValidationFor(mode).Strict;
            return tree.WithTypeAnnotations(new NodeValidator(Components).ValidateDocument(tree, strict));
        }

        private TreeValidation ValidationFor(XsltValidation mode)
        {
            if (mode is not (XsltValidation.Strict or XsltValidation.Lax))
            {
                throw new ArgumentException("Validation is strict or lax.", nameof(mode));
            }

            return new TreeValidation(
                Components.ValidatingSet, strict: mode == XsltValidation.Strict, Components.TypeIdOf, annotate: true);
        }
    }
}
