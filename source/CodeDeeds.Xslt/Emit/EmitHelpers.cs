using System.Reflection;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Emit
{
    /// <summary>
    /// Static entry points that emitted code calls into, and cached <see cref="MethodInfo"/> handles for them.
    /// </summary>
    /// <remarks>
    /// Instance methods on a struct need a managed pointer to the receiver, which would mean spilling every
    /// intermediate <see cref="XPathValue"/> to a local before it could be converted. Static wrappers take the
    /// value straight off the evaluation stack instead, so the common conversions cost a single call.
    /// </remarks>
    internal static class EmitHelpers
    {
        /// <summary>Converts a value to a number, per XPath's rules.</summary>
        /// <param name="value">The value to convert.</param>
        public static double ToNumber(XPathValue value) => value.ToNumber();

        /// <summary>
        /// Converts a value to a number under XPath 1.0's rules, where a sequence is its first item.
        /// </summary>
        /// <remarks>
        /// The compiled counterpart of what the interpreter does for backwards compatible arithmetic. It has
        /// to be a separate entry point rather than a change to <see cref="ToNumber"/>, which serves 2.0 as
        /// well and must leave a sequence a type error there.
        /// </remarks>
        /// <param name="value">The value to convert.</param>
        public static double ToNumberFirstItem(XPathValue value)
        {
            return XdmSequence.FirstItem(value).ToNumber();
        }

        /// <summary>Converts a value to a boolean, per XPath's rules.</summary>
        /// <param name="value">The value to convert.</param>
        public static bool ToBoolean(XPathValue value) => value.ToBoolean();

        /// <summary>Converts a value to a string, per XPath's rules.</summary>
        /// <param name="value">The value to convert.</param>
        public static string ToStringValue(XPathValue value) => value.ToStringValue();

        /// <summary>Applies the <c>=</c> operator to two values.</summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        public static bool AreEqual(XPathValue left, XPathValue right) => XPathComparison.AreEqual(left, right);

        /// <summary>Applies the <c>!=</c> operator to two values.</summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        public static bool NotEquals(XPathValue left, XPathValue right) => XPathComparison.NotEquals(left, right);

        /// <summary>Applies a relational operator to two values.</summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="op">The operator, as its <see cref="BinaryOperator"/> value.</param>
        public static bool Relational(XPathValue left, XPathValue right, int op)
        {
            return XPathComparison.Relational(left, right, (BinaryOperator)op);
        }

        /// <summary>Takes a scratch node list from the pool, for emitted code to collect into.</summary>
        public static List<int> RentList() => NodeListPool.Rent();

        /// <summary>Returns a scratch node list to the pool.</summary>
        /// <param name="list">The list to return.</param>
        public static void ReturnList(List<int> list) => NodeListPool.Return(list);

        /// <summary>Wraps collected nodes as a node-set value.</summary>
        /// <param name="tree">The tree the nodes belong to.</param>
        /// <param name="nodes">The nodes, already in document order.</param>
        public static XPathValue MakeNodeSet(Model.XdmTree tree, List<int> nodes)
        {
            return XPathValue.FromNodeSet(NodeSet.FromOrderedNodes(tree, nodes));
        }

        /// <summary>Compares collected nodes against a value, for emitted comparison code.</summary>
        /// <param name="tree">The tree the nodes belong to.</param>
        /// <param name="nodes">The nodes forming one operand.</param>
        /// <param name="other">The other operand.</param>
        /// <param name="nodesOnLeft">Whether the nodes were written on the left of the operator.</param>
        /// <param name="op">The operator, as its <see cref="BinaryOperator"/> value.</param>
        public static bool CompareNodes(
            Model.XdmTree tree,
            List<int> nodes,
            XPathValue other,
            bool nodesOnLeft,
            int op)
        {
            return other.Kind == XPathValueKind.NodeSet
                ? CompareAgainstNodeSet(tree, nodes, other.AsNodeSet(), nodesOnLeft, op)
                : XPathComparison.NodesVersusValue(tree, nodes, other, nodesOnLeft, (BinaryOperator)op);
        }

        private static bool CompareAgainstNodeSet(
            Model.XdmTree tree,
            List<int> nodes,
            NodeSet other,
            bool nodesOnLeft,
            int op)
        {
            List<int> otherNodes = NodeListPool.Rent();
            try
            {
                for (int i = 0; i < other.Count; i++)
                {
                    otherNodes.Add(other[i]);
                }

                return nodesOnLeft
                    ? XPathComparison.NodesVersusNodes(tree, nodes, other.Tree, otherNodes, (BinaryOperator)op)
                    : XPathComparison.NodesVersusNodes(other.Tree, otherNodes, tree, nodes, (BinaryOperator)op);
            }
            finally
            {
                NodeListPool.Return(otherNodes);
            }
        }

        /// <summary>Looks up one of this class's methods.</summary>
        /// <param name="name">The method name.</param>
        public static MethodInfo Method(string name)
        {
            return typeof(EmitHelpers).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"EmitHelpers.{name} could not be located.");
        }

        /// <summary>Looks up one of <see cref="XPathValue"/>'s public static methods.</summary>
        /// <param name="name">The method name.</param>
        /// <param name="parameters">
        /// The parameter types, where the name alone would be ambiguous. Naming them is not optional
        /// once a factory has an overload: the lookup by name throws rather than choosing, and it
        /// throws from a static constructor, which surfaces as every compiled transformation failing
        /// to start rather than as anything to do with the method.
        /// </param>
        public static MethodInfo ValueMethod(string name)
        {
            return typeof(XPathValue).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"XPathValue.{name} could not be located.");
        }

        /// <summary>Looks up one of <see cref="XPathValue"/>'s public static methods by its signature.</summary>
        /// <param name="name">The method name.</param>
        /// <param name="parameters">The parameter types.</param>
        public static MethodInfo ValueMethod(string name, Type[] parameters)
        {
            return typeof(XPathValue).GetMethod(
                name, BindingFlags.Public | BindingFlags.Static, binder: null, parameters, modifiers: null)
                ?? throw new InvalidOperationException($"XPathValue.{name} could not be located.");
        }

        /// <summary>Wraps a number as a value.</summary>
        public static readonly MethodInfo FromNumber = ValueMethod(nameof(XPathValue.FromNumber));

        /// <summary>Wraps a boolean as a value.</summary>
        public static readonly MethodInfo FromBoolean = ValueMethod(nameof(XPathValue.FromBoolean));

        /// <summary>Wraps a 64-bit integer as an <c>xs:integer</c> value.</summary>
        public static readonly MethodInfo FromInteger =
            ValueMethod(nameof(XPathValue.FromInteger), new[] { typeof(long) });

        /// <summary>Reads <c>position()</c>, refusing where there is no focus.</summary>
        public static readonly MethodInfo PositionOf = Method(nameof(PositionOfFocus));

        /// <summary>Reads <c>last()</c>, refusing where there is no focus.</summary>
        public static readonly MethodInfo SizeOf = Method(nameof(SizeOfFocus));

        /// <summary>
        /// The context position, or <c>XPDY0002</c> where there is no focus.
        /// </summary>
        /// <remarks>
        /// The focus is absent inside a stylesheet function, in a template called with no source document
        /// and in a target expression of <c>xsl:evaluate</c> given no context item, and <c>position()</c>
        /// there is the error XPath names rather than a zero nothing asked for. Read through a call in both
        /// backends so that the two cannot disagree about it.
        /// </remarks>
        /// <param name="context">The evaluation context.</param>
        public static int PositionOfFocus(ref Runtime.DynamicContext context)
        {
            return context.HasContextItem
                ? context.Position
                : throw XsltErrors.Error(
                    XsltErrorCode.XPDY0002,
                    "position() reads the focus, and there is none here — this expression was evaluated "
                    + "outside any focus.");
        }

        /// <summary>The context size, or <c>XPDY0002</c> where there is no focus.</summary>
        /// <param name="context">The evaluation context.</param>
        public static int SizeOfFocus(ref Runtime.DynamicContext context)
        {
            return context.HasContextItem
                ? context.Size
                : throw XsltErrors.Error(
                    XsltErrorCode.XPDY0002,
                    "last() reads the focus, and there is none here — this expression was evaluated outside "
                    + "any focus.");
        }

        /// <summary>Wraps a string as a value.</summary>
        public static readonly MethodInfo FromString = ValueMethod(nameof(XPathValue.FromString));

        /// <summary>Converts a value to a number.</summary>
        public static readonly MethodInfo ToNumberMethod = Method(nameof(ToNumber));

        /// <summary>Converts a value to a number under 1.0's rules, a sequence being its first item.</summary>
        public static readonly MethodInfo ToNumberFirstItemMethod = Method(nameof(ToNumberFirstItem));

        /// <summary>Converts a value to a boolean.</summary>
        public static readonly MethodInfo ToBooleanMethod = Method(nameof(ToBoolean));
    }
}
