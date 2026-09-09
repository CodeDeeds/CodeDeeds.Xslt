namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The XPath 1.0 axes.</summary>
    /// <remarks>
    /// <see cref="Ancestor"/>, <see cref="AncestorOrSelf"/>, <see cref="Preceding"/> and
    /// <see cref="PrecedingSibling"/> are reverse axes: they are enumerated in reverse document order, and
    /// <c>position()</c> within a predicate counts along that reverse ordering. See
    /// <see cref="AxisExtensions.IsReverse"/>.
    /// </remarks>
    public enum Axis : byte
    {
        /// <summary>The context node itself.</summary>
        Self = 0,

        /// <summary>The children of the context node. The default axis when none is written.</summary>
        Child = 1,

        /// <summary>The parent of the context node, if any.</summary>
        Parent = 2,

        /// <summary>The attributes of the context node.</summary>
        Attribute = 3,

        /// <summary>All descendants of the context node.</summary>
        Descendant = 4,

        /// <summary>The context node together with all its descendants.</summary>
        DescendantOrSelf = 5,

        /// <summary>All ancestors of the context node, nearest first.</summary>
        Ancestor = 6,

        /// <summary>The context node together with all its ancestors, nearest first.</summary>
        AncestorOrSelf = 7,

        /// <summary>The siblings that follow the context node.</summary>
        FollowingSibling = 8,

        /// <summary>The siblings that precede the context node, nearest first.</summary>
        PrecedingSibling = 9,

        /// <summary>Every node after the context node in document order, excluding its descendants.</summary>
        Following = 10,

        /// <summary>Every node before the context node in document order, excluding its ancestors.</summary>
        Preceding = 11,

        /// <summary>The namespace nodes of the context node. Recognised but not currently supported.</summary>
        Namespace = 12,
    }

    /// <summary>Helpers describing the behaviour of the XPath axes.</summary>
    public static class AxisExtensions
    {
        /// <summary>
        /// Returns whether an axis is a reverse axis, meaning it is enumerated in reverse document order and
        /// <c>position()</c> counts along that reversed sequence.
        /// </summary>
        /// <param name="axis">The axis to test.</param>
        public static bool IsReverse(this Axis axis)
        {
            return axis is Axis.Ancestor or Axis.AncestorOrSelf or Axis.Preceding or Axis.PrecedingSibling;
        }

        /// <summary>
        /// Returns the principal node kind of an axis — the kind that an unqualified <c>*</c> name test
        /// selects. This is <see cref="Model.NodeKind.Attribute"/> on the attribute axis and
        /// <see cref="Model.NodeKind.Element"/> everywhere else.
        /// </summary>
        /// <param name="axis">The axis to test.</param>
        public static Model.NodeKind PrincipalNodeKind(this Axis axis)
        {
            return axis switch
            {
                Axis.Attribute => Model.NodeKind.Attribute,
                Axis.Namespace => Model.NodeKind.Namespace,
                _ => Model.NodeKind.Element,
            };
        }
    }
}
