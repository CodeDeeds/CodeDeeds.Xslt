using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Implements the XPath 1.0 comparison operators.
    /// </summary>
    /// <remarks>
    /// Comparison involving a node-set is <em>existential</em>: the result is true when <em>some</em> node in
    /// the set satisfies the comparison. A direct consequence is that <c>!=</c> is not the negation of
    /// <c>=</c> — for a node-set with two differently valued nodes, <c>@x = 'a'</c> and <c>@x != 'a'</c> can
    /// both be true. Relational operators always compare numerically, whatever the operand types.
    /// </remarks>
    internal static class XPathComparison
    {
        /// <summary>
        /// Compares a list of nodes against a non-node-set value, without materialising a
        /// <see cref="NodeSet"/>.
        /// </summary>
        /// <param name="tree">The tree the nodes belong to.</param>
        /// <param name="nodes">The nodes forming one operand.</param>
        /// <param name="other">
        /// The other operand, which is one boolean, number or string — see <see cref="IsOneAtomicValue"/>.
        /// </param>
        /// <param name="nodesOnLeft">Whether the nodes were written on the left of the operator.</param>
        /// <param name="op">The operator to apply.</param>
        public static bool NodesVersusValue(
            XdmTree tree,
            List<int> nodes,
            XPathValue other,
            bool nodesOnLeft,
            BinaryOperator op)
        {
            // Read as a span, the list being a pooled one nothing else touches while it is compared. A
            // List's enumerator checks at each step that the list has not changed, and this method is
            // large enough that the JIT leaves MoveNext as a call rather than inlining it — two calls,
            // each with its check, to reach the one node a predicate such as price > 100 usually has.
            return NodesVersusValue(tree, CollectionsMarshal.AsSpan(nodes), other, nodesOnLeft, op);
        }

        /// <summary>
        /// <see cref="NodesVersusValue(XdmTree, List{int}, XPathValue, bool, BinaryOperator)"/> over a span,
        /// which is how one node is compared with nothing rented to hold it: <c>[. = 'x']</c> asks its
        /// context node, once for every candidate, and the node is a span of one on the stack.
        /// </summary>
        /// <param name="tree">The tree the nodes belong to.</param>
        /// <param name="list">The nodes forming one operand.</param>
        /// <param name="other">The other operand, which is one boolean, number or string.</param>
        /// <param name="nodesOnLeft">Whether the nodes were written on the left of the operator.</param>
        /// <param name="op">The operator to apply.</param>
        public static bool NodesVersusValue(
            XdmTree tree,
            ReadOnlySpan<int> list,
            XPathValue other,
            bool nodesOnLeft,
            BinaryOperator op)
        {
            if (op is BinaryOperator.Equal or BinaryOperator.NotEqual)
            {
                bool equal = op == BinaryOperator.Equal;

                // Comparison against a boolean converts the whole set rather than testing each node.
                if (other.Kind == XPathValueKind.Boolean)
                {
                    bool asBoolean = list.Length != 0;
                    return equal ? asBoolean == other.ToBoolean() : asBoolean != other.ToBoolean();
                }

                if (other.Kind == XPathValueKind.Number)
                {
                    double target = other.ToNumber();
                    foreach (int node in list)
                    {
                        double value = NodeAsDouble(tree, node);
                        if (equal ? value == target : value != target)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                string text = other.ToStringValue();
                foreach (int node in list)
                {
                    bool same = string.Equals(tree.StringValueOf(node), text, StringComparison.Ordinal);
                    if (equal ? same : !same)
                    {
                        return true;
                    }
                }

                return false;
            }

            double scalar = AsDouble(other);
            foreach (int node in list)
            {
                double value = NodeAsDouble(tree, node);
                if (nodesOnLeft ? Compare(value, scalar, op) : Compare(scalar, value, op))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Compares a list of nodes against an operand whose value proved to be something other than the
        /// one boolean, number or string <see cref="NodesVersusValue"/> reads.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A node-set a variable turned out to hold is laid out in a list and compared as two lists are.
        /// </para>
        /// <para>
        /// Anything else is a sequence, which a 1.0 stylesheet on this processor may write, and each of
        /// its items is an operand in its turn: the general comparison knows how, so the nodes are given
        /// to it as the node-set they would have been. <see cref="NodesVersusValue"/> was handed these
        /// once, and read a sequence as its items joined by spaces — <c>price = (1 to 10)</c> compared
        /// each price with the text <c>1 2 3 …</c> and found none, where <c>string(price) = (1 to 10)</c>
        /// beside it found four, and <c>category = (5 to 1)</c> found every empty category equal to an
        /// empty sequence. Which expressions yield a sequence is not something an expression says, so it
        /// is asked of the value, by whoever calls: one boolean, number or string goes there and the rest
        /// come here, which is one test of the kind where there was one before.
        /// </para>
        /// </remarks>
        /// <param name="tree">The tree the nodes belong to.</param>
        /// <param name="nodes">The nodes forming one operand, in document order.</param>
        /// <param name="other">The other operand, which is not one boolean, number or string.</param>
        /// <param name="nodesOnLeft">Whether the nodes were written on the left of the operator.</param>
        /// <param name="op">The operator to apply.</param>
        public static bool NodesVersusOther(
            XdmTree tree,
            List<int> nodes,
            XPathValue other,
            bool nodesOnLeft,
            BinaryOperator op)
        {
            if (other.Kind == XPathValueKind.NodeSet)
            {
                NodeSet set = other.AsNodeSet();
                List<int> otherNodes = NodeListPool.Rent();

                try
                {
                    for (int i = 0; i < set.Count; i++)
                    {
                        otherNodes.Add(set[i]);
                    }

                    return nodesOnLeft
                        ? NodesVersusNodes(tree, nodes, set.Tree, otherNodes, op)
                        : NodesVersusNodes(set.Tree, otherNodes, tree, nodes, op);
                }
                finally
                {
                    NodeListPool.Return(otherNodes);
                }
            }

            XPathValue mine = XPathValue.FromNodeSet(NodeSet.FromOrderedNodes(tree, nodes));
            XPathValue left = nodesOnLeft ? mine : other;
            XPathValue right = nodesOnLeft ? other : mine;

            return op switch
            {
                BinaryOperator.Equal => AreEqual(left, right),
                BinaryOperator.NotEqual => NotEquals(left, right),
                _ => Relational(left, right, op),
            };
        }

        /// <summary>
        /// Compares one node against an operand whose value proved to be a node-set or a sequence.
        /// </summary>
        /// <remarks>
        /// <see cref="NodesVersusOther"/> with the node laid out in a rented list, that being what the
        /// comparisons it goes on to take. Seldom asked: it is <c>. = (3, 10)</c> with the context item a
        /// node, and a variable beside <c>.</c> goes the general way from the start.
        /// </remarks>
        /// <param name="tree">The tree the node belongs to.</param>
        /// <param name="node">The one node forming one operand.</param>
        /// <param name="other">The other operand, which is not one boolean, number or string.</param>
        /// <param name="nodeOnLeft">Whether the node was written on the left of the operator.</param>
        /// <param name="op">The operator to apply.</param>
        public static bool NodeVersusOther(XdmTree tree, int node, XPathValue other, bool nodeOnLeft, BinaryOperator op)
        {
            List<int> one = NodeListPool.Rent();

            try
            {
                one.Add(node);
                return NodesVersusOther(tree, one, other, nodeOnLeft, op);
            }
            finally
            {
                NodeListPool.Return(one);
            }
        }

        /// <summary>
        /// Whether a value is one boolean, number or string, which is what <see cref="NodesVersusValue"/>
        /// compares nodes against; anything else is <see cref="NodesVersusOther"/>'s.
        /// </summary>
        /// <param name="value">The operand beside the nodes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOneAtomicValue(XPathValue value)
        {
            // Three kinds in a row, so one comparison.
            return value.Kind is XPathValueKind.Boolean or XPathValueKind.Number or XPathValueKind.String;
        }

        /// <summary>
        /// Compares two lists of nodes, without materialising either as a <see cref="NodeSet"/>.
        /// </summary>
        /// <param name="leftTree">The tree the left nodes belong to.</param>
        /// <param name="left">The left operand's nodes.</param>
        /// <param name="rightTree">The tree the right nodes belong to.</param>
        /// <param name="right">The right operand's nodes.</param>
        /// <param name="op">The operator to apply.</param>
        public static bool NodesVersusNodes(
            XdmTree leftTree,
            List<int> left,
            XdmTree rightTree,
            List<int> right,
            BinaryOperator op)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return false;
            }

            // One node on either side is one string against each of the others, and wants no set: a
            // join in a 1.0 stylesheet is @ref = current()/@id, one node each side, once for every
            // candidate, and the set built to hold the one string cost more than the comparison.
            if (right.Count == 1)
            {
                return NodesVersusNode(leftTree, left, rightTree, right[0], nodesOnLeft: true, op);
            }

            if (left.Count == 1)
            {
                return NodesVersusNode(rightTree, right, leftTree, left[0], nodesOnLeft: false, op);
            }

            if (op is BinaryOperator.Equal or BinaryOperator.NotEqual)
            {
                HashSet<string> leftValues = new HashSet<string>(StringComparer.Ordinal);
                foreach (int node in left)
                {
                    leftValues.Add(leftTree.StringValueOf(node));
                }

                if (op == BinaryOperator.Equal)
                {
                    foreach (int node in right)
                    {
                        if (leftValues.Contains(rightTree.StringValueOf(node)))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                HashSet<string> rightValues = new HashSet<string>(StringComparer.Ordinal);
                foreach (int node in right)
                {
                    rightValues.Add(rightTree.StringValueOf(node));
                }

                // Two or more distinct values on either side guarantee some differing pair.
                return leftValues.Count > 1
                    || rightValues.Count > 1
                    || !string.Equals(leftValues.First(), rightValues.First(), StringComparison.Ordinal);
            }

            foreach (int leftNode in left)
            {
                double a = NodeAsDouble(leftTree, leftNode);
                foreach (int rightNode in right)
                {
                    if (Compare(a, NodeAsDouble(rightTree, rightNode), op))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Compares a list of nodes against one node, without materialising either side or building a set.
        /// </summary>
        /// <remarks>
        /// What <see cref="NodesVersusNodes"/> answers where one side is a single node, which it takes this
        /// way: the quantification over pairs is over the one node's pairs, and a set of one string is the
        /// string. It is also how an operand that is usually one node — <c>current()</c>, or <c>.</c> —
        /// is compared once it has been asked for that node, with nothing rented to hold it.
        /// </remarks>
        /// <param name="tree">The tree the nodes belong to.</param>
        /// <param name="nodes">The nodes forming one operand.</param>
        /// <param name="otherTree">The tree the one node belongs to.</param>
        /// <param name="otherNode">The one node forming the other operand.</param>
        /// <param name="nodesOnLeft">Whether the list was written on the left of the operator.</param>
        /// <param name="op">The operator to apply.</param>
        public static bool NodesVersusNode(
            XdmTree tree,
            List<int> nodes,
            XdmTree otherTree,
            int otherNode,
            bool nodesOnLeft,
            BinaryOperator op)
        {
            return NodesVersusNode(tree, CollectionsMarshal.AsSpan(nodes), otherTree, otherNode, nodesOnLeft, op);
        }

        /// <summary>
        /// <see cref="NodesVersusNode(XdmTree, List{int}, XdmTree, int, bool, BinaryOperator)"/> over a
        /// span, so that two single nodes — <c>. = current()</c> — are compared with nothing rented.
        /// </summary>
        /// <param name="tree">The tree the nodes belong to.</param>
        /// <param name="list">The nodes forming one operand.</param>
        /// <param name="otherTree">The tree the one node belongs to.</param>
        /// <param name="otherNode">The one node forming the other operand.</param>
        /// <param name="nodesOnLeft">Whether the list was written on the left of the operator.</param>
        /// <param name="op">The operator to apply.</param>
        public static bool NodesVersusNode(
            XdmTree tree,
            ReadOnlySpan<int> list,
            XdmTree otherTree,
            int otherNode,
            bool nodesOnLeft,
            BinaryOperator op)
        {
            if (op is BinaryOperator.Equal or BinaryOperator.NotEqual)
            {
                bool equal = op == BinaryOperator.Equal;
                string text = otherTree.StringValueOf(otherNode);

                foreach (int node in list)
                {
                    bool same = string.Equals(tree.StringValueOf(node), text, StringComparison.Ordinal);
                    if (equal ? same : !same)
                    {
                        return true;
                    }
                }

                return false;
            }

            double scalar = NodeAsDouble(otherTree, otherNode);

            foreach (int node in list)
            {
                double value = NodeAsDouble(tree, node);
                if (nodesOnLeft ? Compare(value, scalar, op) : Compare(scalar, value, op))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Evaluates the <c>=</c> operator.</summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <summary>
        /// Applies a general comparison across sequences, which is existential: true when <em>some</em> item
        /// on the left stands in the given relation to <em>some</em> item on the right.
        /// </summary>
        /// <remarks>
        /// The same rule XPath 1.0 already applies to node-sets, extended to the sequences XPath 2.0 adds. It
        /// is why <c>$a = 1</c> and <c>$a != 1</c> can both be true, and why <c>eq</c> exists.
        /// </remarks>
        private static bool QuantifyOverSequences(
            XPathValue left,
            XPathValue right,
            BinaryOperator op,
            Func<XPathValue, XPathValue, BinaryOperator, bool> compare)
        {
            Atomized lefts = new Atomized(left);
            Atomized rights = new Atomized(right);

            for (int i = 0; i < lefts.Count; i++)
            {
                XPathValue a = lefts[i];

                for (int j = 0; j < rights.Count; j++)
                {
                    if (compare(a, rights[j], op))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a value is one item that atomization leaves as it is, so that there is nothing to lay
        /// out before it is compared.
        /// </summary>
        /// <remarks>
        /// A number, a string, a boolean — and a map or a function, which have no typed value and are
        /// handed on as they are for the comparison to refuse.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool AtomizesToItself(XPathValue value)
        {
            return value.Kind is not (XPathValueKind.Sequence
                or XPathValueKind.NodeSet
                or XPathValueKind.Node
                or XPathValueKind.Array);
        }

        /// <summary>
        /// The atomic items of one operand, read by position.
        /// </summary>
        /// <remarks>
        /// One atomic value is by far the commonest operand there is — <c>position() = 1</c>,
        /// <c>$n = 5</c> — and is held as itself: a list built to hold it, the array inside the list and
        /// an enumerator boxed to walk it through an interface came to 208 bytes an operand, 416 for each
        /// such comparison a 2.0 stylesheet made. Read by position rather than enumerated for the same
        /// reason, the list being known only by its interface.
        /// </remarks>
        private readonly struct Atomized
        {
            private readonly XPathValue m_one;
            private readonly IReadOnlyList<XPathValue>? m_many;

            public Atomized(XPathValue value)
            {
                if (AtomizesToItself(value))
                {
                    m_one = value;
                    m_many = null;
                }
                else
                {
                    m_one = default;
                    m_many = Atomize(value);
                }
            }

            public int Count => m_many is null ? 1 : m_many.Count;

            public XPathValue this[int index] => m_many is null ? m_one : m_many[index];
        }

        /// <summary>Reduces a value to the atomic items a comparison sees.</summary>
        /// <remarks>
        /// A list rather than a stream, because the right-hand side is walked once for every item on
        /// the left and re-atomizing nodes that many times would cost more than holding them. A range
        /// is the exception and is handed back as it stands: its items are integers already, it needs
        /// no atomizing, and it answers by index without ever being laid out — which is what lets
        /// <c>5 = (1 to 10000000)</c> be a question about two numbers.
        /// </remarks>
        private static IReadOnlyList<XPathValue> Atomize(XPathValue value)
        {
            if (value.Kind == XPathValueKind.Sequence && value.AsSequence().IsRange)
            {
                return value.AsSequence();
            }

            // Sized to what is known of it: a list left to size itself makes room for four at its first
            // item, which is three too many for the one node a comparison usually has on a side. Only
            // up to a point, a count being a guess at what atomizing will give and not a promise.
            List<XPathValue> items = new List<XPathValue>(Math.Min(1024, value.Kind switch
            {
                XPathValueKind.NodeSet => value.AsNodeSet().Count,
                XPathValueKind.Sequence => value.AsSequence().Count,
                _ => 1,
            }));

            AtomizeInto(value, items);
            return items;
        }

        /// <summary>Adds the atomic items of a value to a list, into the one list however deep they lie.</summary>
        /// <remarks>
        /// Recursing through <see cref="Atomize"/> gave every item of a sequence a list of its own to be
        /// copied out of — three lists and three arrays to lay out <c>(1, 5, 9)</c>, at every evaluation.
        /// </remarks>
        private static void AtomizeInto(XPathValue value, List<XPathValue> items)
        {
            switch (value.Kind)
            {
                case XPathValueKind.Sequence:
                {
                    XdmSequence sequence = value.AsSequence();

                    // A range answers by index and is walked here where it has to be laid out beside
                    // other items; on its own it is handed back whole, and never reaches this.
                    for (int i = 0; i < sequence.Count; i++)
                    {
                        AtomizeInto(sequence[i], items);
                    }

                    break;
                }

                case XPathValueKind.NodeSet:
                {
                    NodeSet nodes = value.AsNodeSet();
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        XdmSequence.AtomizeNodeInto(nodes.TreeAt(i), nodes[i], items);
                    }

                    break;
                }

                case XPathValueKind.Node:
                    XdmSequence.AtomizeNodeInto(value.NodeTree, value.NodeId, items);
                    break;

                case XPathValueKind.Array:
                    // XPath 3.1 makes an array atomize to its members, so '[2] = 2' is true and
                    // '[[1], [2]] = 2' is too. A map still has no typed value, and says so where it is asked.
                    NestingGuard.Descend("atomize");

                    foreach (XPathValue member in value.AsArray().Members)
                    {
                        AtomizeInto(member, items);
                    }

                    break;

                default:
                    items.Add(value);
                    break;
            }
        }

        /// <summary>
        /// Whether some item on the left stands in the relation to some item on the right, by XPath 2.0's
        /// rules for a pair.
        /// </summary>
        /// <remarks>
        /// The right operand is atomized once and its items read for every item on the left, which is
        /// what <see cref="Atomize"/> gives a list for; it had been atomized again for each of them. And
        /// it is atomized only where the left has an item to compare it with, as it always was, so that
        /// an operand that cannot be atomized is refused in the same cases as before and in no new one.
        /// </remarks>
        private static bool AnyPair(XPathValue left, XPathValue right, BinaryOperator op, ComparisonContext? where)
        {
            Atomized lefts = new Atomized(left);

            if (lefts.Count == 0)
            {
                return false;
            }

            Atomized rights = new Atomized(right);

            for (int i = 0; i < lefts.Count; i++)
            {
                XPathValue a = lefts[i];

                for (int j = 0; j < rights.Count; j++)
                {
                    if (XdmComparison.Pair(a, rights[j], op, where))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Returns whether either operand is a sequence, which the node-set paths cannot describe.</summary>
        private static bool EitherIsSequence(XPathValue left, XPathValue right)
        {
            return left.Kind is XPathValueKind.Sequence or XPathValueKind.Node
                || right.Kind is XPathValueKind.Sequence or XPathValueKind.Node;
        }

        /// <summary>
        /// Applies a general comparison under the rules of a given version.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The quantification is the same in both: true when <em>some</em> item on the left stands in the
        /// given relation to <em>some</em> item on the right. What changes is what happens to a pair whose
        /// types do not go together. XPath 1.0 converts until they do, so <c>"a" = 1</c> is false; XPath 2.0
        /// consults a table of which pairs a comparison is defined for and refuses the rest, so the same
        /// expression is a type error. See <see cref="XdmComparison"/>.
        /// </para>
        /// <para>
        /// The 1.0 route keeps the specialised paths that compare a node list without materialising anything,
        /// because that is where a stylesheet spends its time. The 2.0 route atomizes and compares pair by
        /// pair, since the conversions it has to make are per pair and depend on both sides.
        /// </para>
        /// </remarks>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="op">The operator.</param>
        /// <param name="version">The XSLT version whose rules apply.</param>
        public static bool General(
            XPathValue left,
            XPathValue right,
            BinaryOperator op,
            XsltVersion version,
            ComparisonContext? where = null)
        {
            if (!version.IsBackwardsCompatible)
            {
                // Two atomic values are one pair, and the whole of what 'position() = 1' asks.
                return AtomizesToItself(left) && AtomizesToItself(right)
                    ? XdmComparison.Pair(left, right, op, where)
                    : AnyPair(left, right, op, where);
            }

            return op switch
            {
                BinaryOperator.Equal => AreEqual(left, right),
                BinaryOperator.NotEqual => NotEquals(left, right),
                _ => Relational(left, right, op),
            };
        }

        public static bool AreEqual(XPathValue left, XPathValue right)
        {
            if (EitherIsSequence(left, right))
            {
                return QuantifyOverSequences(left, right, BinaryOperator.Equal, static (a, b, _) => ScalarEqual(a, b));
            }

            bool leftIsNodeSet = left.Kind == XPathValueKind.NodeSet;
            bool rightIsNodeSet = right.Kind == XPathValueKind.NodeSet;

            if (leftIsNodeSet && rightIsNodeSet)
            {
                return NodeSetsIntersect(left.AsNodeSet(), right.AsNodeSet());
            }

            if (leftIsNodeSet || rightIsNodeSet)
            {
                NodeSet nodes = leftIsNodeSet ? left.AsNodeSet() : right.AsNodeSet();
                XPathValue other = leftIsNodeSet ? right : left;
                return NodeSetMatchesScalar(nodes, other, equal: true);
            }

            if (left.Kind == XPathValueKind.Boolean || right.Kind == XPathValueKind.Boolean)
            {
                return left.ToBoolean() == right.ToBoolean();
            }

            if (left.Kind == XPathValueKind.Number || right.Kind == XPathValueKind.Number)
            {
                return AsDouble(left) == AsDouble(right);
            }

            return string.Equals(left.ToStringValue(), right.ToStringValue(), StringComparison.Ordinal);
        }

        /// <summary>Evaluates the <c>!=</c> operator.</summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        public static bool NotEquals(XPathValue left, XPathValue right)
        {
            if (EitherIsSequence(left, right))
            {
                return QuantifyOverSequences(left, right, BinaryOperator.NotEqual, static (a, b, _) => !ScalarEqual(a, b));
            }

            bool leftIsNodeSet = left.Kind == XPathValueKind.NodeSet;
            bool rightIsNodeSet = right.Kind == XPathValueKind.NodeSet;

            if (leftIsNodeSet && rightIsNodeSet)
            {
                return NodeSetsDiffer(left.AsNodeSet(), right.AsNodeSet());
            }

            if (leftIsNodeSet || rightIsNodeSet)
            {
                NodeSet nodes = leftIsNodeSet ? left.AsNodeSet() : right.AsNodeSet();
                XPathValue other = leftIsNodeSet ? right : left;
                return NodeSetMatchesScalar(nodes, other, equal: false);
            }

            if (left.Kind == XPathValueKind.Boolean || right.Kind == XPathValueKind.Boolean)
            {
                return left.ToBoolean() != right.ToBoolean();
            }

            if (left.Kind == XPathValueKind.Number || right.Kind == XPathValueKind.Number)
            {
                return AsDouble(left) != AsDouble(right);
            }

            return !string.Equals(left.ToStringValue(), right.ToStringValue(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Compares two atomic items for equality, following the XPath 1.0 rule that a boolean operand makes
        /// the comparison a boolean one and a numeric operand makes it numeric.
        /// </summary>
        /// <summary>
        /// An operand as the number a backwards-compatible comparison reads it as.
        /// </summary>
        /// <remarks>
        /// XSLT 2.0 §3.9 converts each operand of a general comparison to <c>xs:double</c> where either side
        /// is numeric, and the lexical space of <c>xs:double</c> has an exponent in it — so
        /// <c>1 = '1.0e0'</c> is true here, where XPath 1.0's own grammar for a number has no exponent at
        /// all and would read that string as NaN. Backwards compatibility is XSLT 2.0's imitation of 1.0
        /// within 2.0's data model rather than 1.0 itself, and the conversion it names is 2.0's
        /// <c>fn:number</c>. <c>number()</c> called in the same stylesheet is that function and answers
        /// the same, as do arithmetic, <c>sum()</c> and an argument converted to a number — see
        /// <see cref="XdmType.FirstItemAsDoubleOrNaN"/>. A node is read the same way — see
        /// <see cref="NodeAsDouble"/>.
        /// </remarks>
        /// <param name="value">The operand.</param>
        private static double AsDouble(XPathValue value)
        {
            return value.TypeCode is XdmTypeCode.String or XdmTypeCode.UntypedAtomic
                ? XdmType.AsDoubleOrNaN(value)
                : value.ToNumber();
        }

        /// <summary>
        /// A node as the number a backwards-compatible comparison reads it as.
        /// </summary>
        /// <remarks>
        /// The same number <see cref="AsDouble"/> reads from the node's text, and it has to be: XPath 2.0
        /// §3.5.2 atomizes a node operand before it converts anything, so by the time there is a number to
        /// read there is no node left to read it differently from. <c>price = 10</c> and
        /// <c>string(price) = 10</c> are one question, and over <c>&lt;price&gt;1e1&lt;/price&gt;</c> both
        /// are true. The paths that take nodes directly had been reading them by XPath 1.0's grammar, which
        /// has no exponent, so the answer depended on which of the two ways the text arrived.
        /// </remarks>
        /// <param name="tree">The tree the node belongs to.</param>
        /// <param name="node">The node.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double NodeAsDouble(XdmTree tree, int node)
        {
            return XdmType.TextAsDoubleOrNaN(tree.StringValueOf(node));
        }

        private static bool ScalarEqual(XPathValue left, XPathValue right)
        {
            if (left.Kind == XPathValueKind.Boolean || right.Kind == XPathValueKind.Boolean)
            {
                return left.ToBoolean() == right.ToBoolean();
            }

            // Two moments are equal when they are the same instant, however differently they are written.
            if (ValueComparisonExpr.TryCompareTemporal(left, right, out int temporal))
            {
                return temporal == 0;
            }

            // Two untyped, string or URI operands compare as text; anything numeric on either side is numeric.
            bool asText = IsText(left.TypeCode) && IsText(right.TypeCode);

            return asText
                ? string.Equals(left.ToStringValue(), right.ToStringValue(), StringComparison.Ordinal)
                : AsDouble(left) == AsDouble(right);
        }

        private static bool IsText(XdmTypeCode type)
        {
            return type is XdmTypeCode.String or XdmTypeCode.UntypedAtomic or XdmTypeCode.AnyUri;
        }

        /// <summary>Compares two numbers, giving a sign that no relational operator accepts for NaN.</summary>
        private static int NumericSign(double left, double right)
        {
            if (double.IsNaN(left) || double.IsNaN(right))
            {
                return 2;
            }

            return left < right ? -1 : left > right ? 1 : 0;
        }

        /// <summary>Applies a relational operator to the sign of a comparison.</summary>
        private static bool CompareOrdering(int comparison, BinaryOperator op)
        {
            return op switch
            {
                BinaryOperator.LessThan => comparison < 0,
                BinaryOperator.LessThanOrEqual => comparison <= 0,
                BinaryOperator.GreaterThan => comparison > 0,
                _ => comparison >= 0,
            };
        }

        /// <summary>Evaluates <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> or <c>&gt;=</c> under XPath 1.0 rules.</summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="op">The relational operator to apply.</param>
        public static bool Relational(XPathValue left, XPathValue right, BinaryOperator op)
        {
            if (EitherIsSequence(left, right))
            {
                // The operator goes in beside the operands rather than being captured, a lambda that
                // captures being an object and a delegate made anew for every comparison.
                return QuantifyOverSequences(
                    left,
                    right,
                    op,
                    static (a, b, by) => CompareOrdering(NumericSign(AsDouble(a), AsDouble(b)), by));
            }

            bool leftIsNodeSet = left.Kind == XPathValueKind.NodeSet;
            bool rightIsNodeSet = right.Kind == XPathValueKind.NodeSet;

            if (leftIsNodeSet && rightIsNodeSet)
            {
                NodeSet leftNodes = left.AsNodeSet();
                NodeSet rightNodes = right.AsNodeSet();
                for (int i = 0; i < leftNodes.Count; i++)
                {
                    double a = NodeAsDouble(leftNodes.TreeAt(i), leftNodes[i]);
                    for (int j = 0; j < rightNodes.Count; j++)
                    {
                        double b = NodeAsDouble(rightNodes.TreeAt(j), rightNodes[j]);
                        if (Compare(a, b, op))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            if (leftIsNodeSet || rightIsNodeSet)
            {
                NodeSet nodes = leftIsNodeSet ? left.AsNodeSet() : right.AsNodeSet();
                double scalar = AsDouble(leftIsNodeSet ? right : left);

                for (int i = 0; i < nodes.Count; i++)
                {
                    double value = NodeAsDouble(nodes.TreeAt(i), nodes[i]);

                    // The node-set supplies whichever side it originally occupied.
                    bool matched = leftIsNodeSet
                        ? Compare(value, scalar, op)
                        : Compare(scalar, value, op);

                    if (matched)
                    {
                        return true;
                    }
                }

                return false;
            }

            return Compare(AsDouble(left), AsDouble(right), op);
        }

        private static bool Compare(double left, double right, BinaryOperator op)
        {
            // Every comparison against NaN is false, which the IEEE semantics of these operators already give.
            return op switch
            {
                BinaryOperator.LessThan => left < right,
                BinaryOperator.LessThanOrEqual => left <= right,
                BinaryOperator.GreaterThan => left > right,
                _ => left >= right,
            };
        }

        private static bool NodeSetsIntersect(NodeSet left, NodeSet right)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return false;
            }

            HashSet<string> leftValues = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < left.Count; i++)
            {
                leftValues.Add(left.TreeAt(i).StringValueOf(left[i]));
            }

            for (int i = 0; i < right.Count; i++)
            {
                if (leftValues.Contains(right.TreeAt(i).StringValueOf(right[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NodeSetsDiffer(NodeSet left, NodeSet right)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return false;
            }

            HashSet<string> leftValues = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < left.Count; i++)
            {
                leftValues.Add(left.TreeAt(i).StringValueOf(left[i]));
            }

            HashSet<string> rightValues = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < right.Count; i++)
            {
                rightValues.Add(right.TreeAt(i).StringValueOf(right[i]));
            }

            // With two or more distinct values on either side, some pair is guaranteed to differ.
            if (leftValues.Count > 1 || rightValues.Count > 1)
            {
                return true;
            }

            return !string.Equals(leftValues.First(), rightValues.First(), StringComparison.Ordinal);
        }

        private static bool NodeSetMatchesScalar(NodeSet nodes, XPathValue other, bool equal)
        {
            // Comparison against a boolean is the one case that is not existential: the node-set is converted
            // to a boolean as a whole, so an empty set participates meaningfully.
            if (other.Kind == XPathValueKind.Boolean)
            {
                bool nodeSetAsBoolean = nodes.Count != 0;
                return equal
                    ? nodeSetAsBoolean == other.ToBoolean()
                    : nodeSetAsBoolean != other.ToBoolean();
            }

            if (other.Kind == XPathValueKind.Number)
            {
                double target = other.ToNumber();
                for (int i = 0; i < nodes.Count; i++)
                {
                    double value = NodeAsDouble(nodes.TreeAt(i), nodes[i]);
                    if (equal ? value == target : value != target)
                    {
                        return true;
                    }
                }

                return false;
            }

            string text = other.ToStringValue();
            for (int i = 0; i < nodes.Count; i++)
            {
                bool same = string.Equals(nodes.TreeAt(i).StringValueOf(nodes[i]), text, StringComparison.Ordinal);
                if (equal ? same : !same)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
