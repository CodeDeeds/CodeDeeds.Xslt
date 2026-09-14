using System.Xml.Schema;

namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// What a document is validated against as it is read, and how strictly: the schemas in scope, whether
    /// an element with no declaration is an error, how a validated type is numbered for the tree, and
    /// whether the tree keeps the annotations at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Validation is done by .NET's validating reader over the same pass that builds the tree, and what
    /// the reader settles for each element and attribute — its declaration, its type, whether it is
    /// nilled — is read off the reader at the node's start and written into the tree as its annotation.
    /// The typed value is not taken from the reader; the annotation is enough, and the engine makes the
    /// value from the string value by the type when something asks.
    /// </para>
    /// <para>
    /// A document that is not valid stops the read where it fails: <c>XTTE1510</c> under strict
    /// validation and <c>XTTE1515</c> under lax, and <c>XTTE1512</c> for a document element that strict
    /// validation finds no declaration for. Lax validation leaves such an element, and everything under
    /// it, untyped, as the specification has it.
    /// </para>
    /// </remarks>
    internal sealed class TreeValidation
    {
        /// <summary>Initializes the validation a read is done under.</summary>
        /// <param name="schemas">The compiled schemas to validate against.</param>
        /// <param name="strict">Whether a document element with no declaration is an error rather than untyped.</param>
        /// <param name="typeIds">What numbers a type the validator settled on, for the tree's annotation.</param>
        /// <param name="annotate">Whether the tree records what was found, or is validated and left untyped.</param>
        public TreeValidation(XmlSchemaSet schemas, bool strict, Func<XmlSchemaType, ushort> typeIds, bool annotate)
        {
            Schemas = schemas;
            Strict = strict;
            TypeIds = typeIds;
            Annotate = annotate;
        }

        /// <summary>The compiled schemas the document is validated against.</summary>
        public XmlSchemaSet Schemas { get; }

        /// <summary>Whether validation is strict, so that an undeclared document element is an error.</summary>
        public bool Strict { get; }

        /// <summary>What numbers a type for the tree's annotation arrays.</summary>
        public Func<XmlSchemaType, ushort> TypeIds { get; }

        /// <summary>
        /// Whether the tree keeps the annotations. Off, the document is validated and read untyped, which
        /// is what a stylesheet declaring <c>input-type-annotations="strip"</c> asks for.
        /// </summary>
        public bool Annotate { get; }

        /// <summary>
        /// Turns what the validating reader reports into the error the specification names, and lets a
        /// warning pass: an undeclared element under lax validation is untyped and not wrong.
        /// </summary>
        public void Report(object? sender, ValidationEventArgs e)
        {
            if (e.Severity != XmlSeverityType.Error)
            {
                return;
            }

            string where = e.Exception is { LineNumber: > 0 } at
                ? $" (line {at.LineNumber}, column {at.LinePosition})"
                : string.Empty;

            throw XsltErrors.Error(
                Strict ? XsltErrorCode.XTTE1510 : XsltErrorCode.XTTE1515,
                $"The document is not valid against the schemas in scope{where}: {e.Message}",
                e.Exception);
        }

        /// <summary>The error for a document element strict validation finds no declaration for.</summary>
        /// <param name="namespaceUri">The element's namespace URI.</param>
        /// <param name="localName">The element's local name.</param>
        public static XsltException Undeclared(string namespaceUri, string localName)
        {
            string name = namespaceUri.Length == 0 ? localName : "Q{" + namespaceUri + "}" + localName;

            return XsltErrors.Error(
                XsltErrorCode.XTTE1512,
                $"Strict validation was asked for, and the schemas in scope declare no top-level element "
                + $"'{name}' to validate the document against.");
        }
    }
}
