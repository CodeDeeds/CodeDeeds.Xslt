namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// The kinds of node defined by the XPath 1.0 data model.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="System.Byte"/> so that the per-node array in <see cref="XdmTree"/> stays compact.
    /// <see cref="Namespace"/> is present for completeness of the data model; the <c>namespace::</c> axis is
    /// not currently supported.
    /// </remarks>
    public enum NodeKind : byte
    {
        /// <summary>The document (root) node. Exactly one per tree, always node id 0.</summary>
        Root = 0,

        /// <summary>An element node.</summary>
        Element = 1,

        /// <summary>An attribute node. Stored outside the preorder sequence; see <see cref="XdmTree"/>.</summary>
        Attribute = 2,

        /// <summary>A text node.</summary>
        Text = 3,

        /// <summary>A comment node.</summary>
        Comment = 4,

        /// <summary>A processing instruction node. Its target is held as the local part of its name.</summary>
        ProcessingInstruction = 5,

        /// <summary>A namespace node.</summary>
        Namespace = 6,
    }
}
