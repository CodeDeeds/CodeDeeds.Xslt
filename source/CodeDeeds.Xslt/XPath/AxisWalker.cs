using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Enumerates the nodes on an axis, in the order that axis defines.
    /// </summary>
    /// <remarks>
    /// Results are appended in <em>axis order</em>, not document order: reverse axes yield their nearest node
    /// first, because <c>position()</c> inside a predicate counts along the axis. Callers that need a node-set
    /// must order the result afterwards.
    /// <para>
    /// Preorder numbering makes several axes far cheaper than a naive tree walk would suggest.
    /// <see cref="Axis.Descendant"/> is a contiguous range sweep, <see cref="Axis.Following"/> is everything
    /// past the subtree's end, and <see cref="Axis.Preceding"/> is a downward sweep skipping the ancestors —
    /// which are recognised by the fact that an ancestor's subtree contains the context node.
    /// </para>
    /// </remarks>
    public static class AxisWalker
    {
        /// <summary>
        /// Appends every node on an axis that satisfies a node test.
        /// </summary>
        /// <param name="tree">The tree to walk.</param>
        /// <param name="origin">The context node the axis starts from.</param>
        /// <param name="axis">The axis to walk.</param>
        /// <param name="test">The test each candidate must satisfy.</param>
        /// <param name="fingerprintMap">Slot-to-fingerprint mapping for <paramref name="tree"/>.</param>
        /// <param name="output">The list results are appended to, in axis order.</param>
        public static void Collect(
            XdmTree tree,
            int origin,
            Axis axis,
            NodeTest test,
            int[] fingerprintMap,
            List<int> output)
        {
            NodeKind principal = axis.PrincipalNodeKind();

            // A name test on an element axis is the step nearly every path is made of, and the three axes
            // that walk a run of nodes — children, descendants — are answered by the tree itself, straight
            // off its arrays, with the name's fingerprint looked up once rather than once per node.
            if (test is NameNodeTest byName && principal == NodeKind.Element
                && axis is Axis.Child or Axis.Descendant or Axis.DescendantOrSelf)
            {
                // A name absent from the tree, or a tree whose names were never mapped, matches nothing —
                // the same two refusals the test itself makes, made once here.
                if (byName.Slot >= fingerprintMap.Length)
                {
                    return;
                }

                int wanted = fingerprintMap[byName.Slot];
                if (wanted == NameTable.NoFingerprint)
                {
                    return;
                }

                switch (axis)
                {
                    case Axis.Child:
                        tree.CollectChildElementsByFingerprint(origin, wanted, output);
                        return;

                    case Axis.Descendant:
                        tree.CollectElementsByFingerprint(origin + 1, tree.SubtreeEndOf(origin), wanted, output);
                        return;

                    default:
                        tree.CollectElementsByFingerprint(origin, tree.SubtreeEndOf(origin), wanted, output);
                        return;
                }
            }

            switch (axis)
            {
                case Axis.Self:
                    TestAndAdd(tree, origin, principal, test, fingerprintMap, output);
                    break;

                case Axis.Child:
                    for (int child = tree.FirstChildOf(origin); child >= 0; child = tree.NextSiblingOf(child))
                    {
                        TestAndAdd(tree, child, principal, test, fingerprintMap, output);
                    }

                    break;

                case Axis.Parent:
                {
                    int parent = tree.ParentOf(origin);
                    if (parent >= 0)
                    {
                        TestAndAdd(tree, parent, principal, test, fingerprintMap, output);
                    }

                    break;
                }

                case Axis.Attribute:
                {
                    int count = tree.AttributeCountOf(origin);
                    for (int i = 0; i < count; i++)
                    {
                        TestAndAdd(tree, tree.AttributeAt(origin, i), principal, test, fingerprintMap, output);
                    }

                    break;
                }

                case Axis.Descendant:
                {
                    int end = tree.SubtreeEndOf(origin);
                    for (int node = origin + 1; node <= end; node++)
                    {
                        TestAndAdd(tree, node, principal, test, fingerprintMap, output);
                    }

                    break;
                }

                case Axis.DescendantOrSelf:
                {
                    int end = tree.SubtreeEndOf(origin);
                    for (int node = origin; node <= end; node++)
                    {
                        TestAndAdd(tree, node, principal, test, fingerprintMap, output);
                    }

                    break;
                }

                case Axis.Ancestor:
                    for (int node = tree.ParentOf(origin); node >= 0; node = tree.ParentOf(node))
                    {
                        TestAndAdd(tree, node, principal, test, fingerprintMap, output);
                    }

                    break;

                case Axis.AncestorOrSelf:
                    for (int node = origin; node >= 0; node = tree.ParentOf(node))
                    {
                        TestAndAdd(tree, node, principal, test, fingerprintMap, output);
                    }

                    break;

                case Axis.FollowingSibling:
                    for (int node = tree.NextSiblingOf(origin); node >= 0; node = tree.NextSiblingOf(node))
                    {
                        TestAndAdd(tree, node, principal, test, fingerprintMap, output);
                    }

                    break;

                case Axis.PrecedingSibling:
                    CollectPrecedingSiblings(tree, origin, principal, test, fingerprintMap, output);
                    break;

                case Axis.Following:
                {
                    // Everything after this node's subtree. For an attribute, its owner's children count as
                    // following, so the sweep starts just after the owner itself.
                    int start = XdmTree.IsAttribute(origin)
                        ? tree.ParentOf(origin) + 1
                        : tree.SubtreeEndOf(origin) + 1;

                    for (int node = start; node < tree.NodeCount; node++)
                    {
                        TestAndAdd(tree, node, principal, test, fingerprintMap, output);
                    }

                    break;
                }

                case Axis.Preceding:
                {
                    int reference = XdmTree.IsAttribute(origin) ? tree.ParentOf(origin) : origin;
                    for (int node = reference - 1; node >= 0; node--)
                    {
                        // An ancestor is precisely a node whose subtree still contains the reference node.
                        if (tree.SubtreeEndOf(node) >= reference)
                        {
                            continue;
                        }

                        TestAndAdd(tree, node, principal, test, fingerprintMap, output);
                    }

                    break;
                }

                case Axis.Namespace:
                {
                    // An element's namespace nodes are made on first use, the whole set at once, and only an
                    // element has any: the axis from anything else is empty, as the specification has it.
                    if (tree.TryGetNamespaceNodes(origin, out int first, out int count))
                    {
                        for (int i = 0; i < count; i++)
                        {
                            TestAndAdd(tree, first + i, principal, test, fingerprintMap, output);
                        }
                    }

                    break;
                }
            }
        }

        private static void CollectPrecedingSiblings(
            XdmTree tree,
            int origin,
            NodeKind principal,
            NodeTest test,
            int[] fingerprintMap,
            List<int> output)
        {
            int parent = tree.ParentOf(origin);
            if (parent < 0 || XdmTree.IsAttribute(origin))
            {
                return;
            }

            // Siblings are only reachable forwards, so gather them and reverse: this axis counts from the
            // nearest sibling outwards.
            int firstResult = output.Count;
            for (int node = tree.FirstChildOf(parent); node >= 0 && node != origin; node = tree.NextSiblingOf(node))
            {
                TestAndAdd(tree, node, principal, test, fingerprintMap, output);
            }

            output.Reverse(firstResult, output.Count - firstResult);
        }

        private static void TestAndAdd(
            XdmTree tree,
            int node,
            NodeKind principal,
            NodeTest test,
            int[] fingerprintMap,
            List<int> output)
        {
            if (test.Matches(tree, node, principal, fingerprintMap))
            {
                output.Add(node);
            }
        }
    }
}
