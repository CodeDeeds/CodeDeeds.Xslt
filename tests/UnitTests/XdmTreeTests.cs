using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the flat document model — preorder numbering, subtree ranges, the attribute id space,
    /// document order, string-values and namespace scoping.
    /// </summary>
    [TestClass]
    public sealed class XdmTreeTests
    {
        /// <summary>
        /// A document with no incidental whitespace, so that node ids can be asserted directly.
        /// Expected preorder numbering:
        /// 0 root, 1 &lt;root&gt;, 2 &lt;child&gt;, 3 "text", 4 &lt;child&gt;, 5 "more cdata", 6 comment, 7 PI.
        /// </summary>
        private const string CompactDocument =
            "<root xmlns:a=\"urn:a\" id=\"1\">" +
            "<child a:x=\"v\">text</child>" +
            "<child>more<![CDATA[ cdata]]></child>" +
            "<!--c-->" +
            "<?pi d?>" +
            "</root>";

        private static XdmTree Build(string xml)
        {
            return XdmTreeBuilder.FromXml(new StringReader(xml));
        }

        [TestMethod]
        public void PreorderNumberingFollowsDocumentOrder()
        {
            XdmTree tree = Build(CompactDocument);

            Assert.AreEqual(8, tree.NodeCount);
            Assert.AreEqual(NodeKind.Root, tree.KindOf(0));
            Assert.AreEqual(NodeKind.Element, tree.KindOf(1));
            Assert.AreEqual(NodeKind.Element, tree.KindOf(2));
            Assert.AreEqual(NodeKind.Text, tree.KindOf(3));
            Assert.AreEqual(NodeKind.Element, tree.KindOf(4));
            Assert.AreEqual(NodeKind.Text, tree.KindOf(5));
            Assert.AreEqual(NodeKind.Comment, tree.KindOf(6));
            Assert.AreEqual(NodeKind.ProcessingInstruction, tree.KindOf(7));
        }

        [TestMethod]
        public void SubtreeEndDelimitsExactlyTheDescendants()
        {
            XdmTree tree = Build(CompactDocument);

            // The whole document element spans everything after it.
            Assert.AreEqual(7, tree.SubtreeEndOf(1));

            // Each child spans only its own text node.
            Assert.AreEqual(3, tree.SubtreeEndOf(2));
            Assert.AreEqual(5, tree.SubtreeEndOf(4));

            // Leaves span only themselves, which is what makes the descendant sweep terminate immediately.
            Assert.AreEqual(3, tree.SubtreeEndOf(3));
            Assert.AreEqual(6, tree.SubtreeEndOf(6));
        }

        [TestMethod]
        public void SiblingAndParentLinksAreWired()
        {
            XdmTree tree = Build(CompactDocument);

            Assert.AreEqual(1, tree.FirstChildOf(0));
            Assert.AreEqual(2, tree.FirstChildOf(1));
            Assert.AreEqual(4, tree.NextSiblingOf(2));
            Assert.AreEqual(6, tree.NextSiblingOf(4));
            Assert.AreEqual(7, tree.NextSiblingOf(6));
            Assert.AreEqual(-1, tree.NextSiblingOf(7));

            Assert.AreEqual(-1, tree.ParentOf(0));
            Assert.AreEqual(0, tree.ParentOf(1));
            Assert.AreEqual(1, tree.ParentOf(2));
            Assert.AreEqual(2, tree.ParentOf(3));
        }

        [TestMethod]
        public void DepthCountsFromRootAtZero()
        {
            XdmTree tree = Build(CompactDocument);

            Assert.AreEqual(0, tree.DepthOf(0));
            Assert.AreEqual(1, tree.DepthOf(1));
            Assert.AreEqual(2, tree.DepthOf(2));
            Assert.AreEqual(3, tree.DepthOf(3));

            // An attribute sits one level below its owning element.
            Assert.AreEqual(2, tree.DepthOf(tree.AttributeAt(1, 0)));
        }

        [TestMethod]
        public void AttributesLiveInASeparateIdSpaceAndAreNotChildren()
        {
            XdmTree tree = Build(CompactDocument);

            Assert.AreEqual(1, tree.AttributeCountOf(1));
            Assert.AreEqual(1, tree.AttributeCountOf(2));
            Assert.AreEqual(0, tree.AttributeCountOf(4));

            int idAttribute = tree.AttributeAt(1, 0);
            Assert.IsTrue(XdmTree.IsAttribute(idAttribute));
            Assert.AreEqual(NodeKind.Attribute, tree.KindOf(idAttribute));
            Assert.AreEqual("1", tree.StringValueOf(idAttribute));

            // The owning element is the attribute's parent, but the attribute is never among its children.
            Assert.AreEqual(1, tree.ParentOf(idAttribute));
            Assert.AreEqual(2, tree.FirstChildOf(1));

            // Attribute ids are excluded from the preorder sweep, so a subtree scan never encounters them.
            Assert.IsFalse(XdmTree.IsAttribute(7));
        }

        [TestMethod]
        public void FindAttributeMatchesOnExpandedName()
        {
            XdmTree tree = Build(CompactDocument);
            NameTable names = tree.NameTable;

            int idFingerprint = names.GetFingerprint(string.Empty, "id");
            Assert.AreEqual(tree.AttributeAt(1, 0), tree.FindAttribute(1, idFingerprint));

            // The namespaced attribute must match on URI, not on prefix.
            int xFingerprint = names.GetFingerprint("urn:a", "x");
            int found = tree.FindAttribute(2, xFingerprint);
            Assert.AreEqual(tree.AttributeAt(2, 0), found);
            Assert.AreEqual("v", tree.StringValueOf(found));

            // A name that is in the table but not on this element.
            Assert.AreEqual(-1, tree.FindAttribute(4, idFingerprint));

            // A name that was never interned cannot match anything.
            Assert.AreEqual(-1, tree.FindAttribute(1, names.LookupFingerprint("urn:z", "nope")));
        }

        [TestMethod]
        public void DocumentOrderPlacesAttributesBetweenElementAndItsFirstChild()
        {
            XdmTree tree = Build(CompactDocument);

            long rootElement = tree.DocumentOrderKeyOf(1);
            long rootAttribute = tree.DocumentOrderKeyOf(tree.AttributeAt(1, 0));
            long firstChild = tree.DocumentOrderKeyOf(2);
            long childAttribute = tree.DocumentOrderKeyOf(tree.AttributeAt(2, 0));
            long childText = tree.DocumentOrderKeyOf(3);

            Assert.IsLessThan(rootAttribute, rootElement, "attribute must follow its element");
            Assert.IsLessThan(firstChild, rootAttribute, "attribute must precede the element's first child");
            Assert.IsLessThan(childAttribute, firstChild);
            Assert.IsLessThan(childText, childAttribute);
        }

        [TestMethod]
        public void MultipleAttributesKeepDocumentOrderAmongThemselves()
        {
            XdmTree tree = Build("<e a=\"1\" b=\"2\" c=\"3\"/>");

            long first = tree.DocumentOrderKeyOf(tree.AttributeAt(1, 0));
            long second = tree.DocumentOrderKeyOf(tree.AttributeAt(1, 1));
            long third = tree.DocumentOrderKeyOf(tree.AttributeAt(1, 2));

            Assert.IsLessThan(second, first);
            Assert.IsLessThan(third, second);
        }

        [TestMethod]
        public void StringValueConcatenatesDescendantText()
        {
            XdmTree tree = Build(CompactDocument);

            // Single text child: the fast path returns the string without building it.
            Assert.AreEqual("text", tree.StringValueOf(2));

            // Text and CDATA adjacent to one another form one text node per the data model.
            Assert.AreEqual("more cdata", tree.StringValueOf(4));
            Assert.AreEqual(NodeKind.Text, tree.KindOf(5));
            Assert.AreEqual(5, tree.SubtreeEndOf(4));

            // Comments and processing instructions contribute nothing to an ancestor's string-value.
            Assert.AreEqual("textmore cdata", tree.StringValueOf(1));
            Assert.AreEqual("textmore cdata", tree.StringValueOf(0));

            Assert.AreEqual("c", tree.StringValueOf(6));
            Assert.AreEqual("d", tree.StringValueOf(7));
        }

        [TestMethod]
        public void EmptyElementHasEmptyStringValue()
        {
            XdmTree tree = Build("<e/>");

            Assert.AreEqual(string.Empty, tree.StringValueOf(1));
            Assert.AreEqual(1, tree.SubtreeEndOf(1));
            Assert.AreEqual(-1, tree.FirstChildOf(1));
        }

        [TestMethod]
        public void NameTestsComparefingerprintsIgnoringPrefix()
        {
            XdmTree tree = Build("<a:e xmlns:a=\"urn:x\"><b:e xmlns:b=\"urn:x\"/></a:e>");
            NameTable names = tree.NameTable;

            // Different prefixes, same expanded name: the fingerprints must be equal...
            Assert.AreEqual(tree.FingerprintOf(1), tree.FingerprintOf(2));

            // ...while the name codes differ, so serialization can still reproduce each prefix.
            Assert.AreNotEqual(tree.NameCodeOf(1), tree.NameCodeOf(2));
            Assert.AreEqual("a", names.GetPrefix(tree.NameCodeOf(1)));
            Assert.AreEqual("b", names.GetPrefix(tree.NameCodeOf(2)));
            Assert.AreEqual("a:e", names.GetQualifiedName(tree.NameCodeOf(1)));
            Assert.AreEqual("urn:x", names.GetNamespaceUri(tree.FingerprintOf(1)));
            Assert.AreEqual("e", names.GetLocalName(tree.FingerprintOf(1)));
        }

        [TestMethod]
        public void PrefixesResolveThroughAncestors()
        {
            XdmTree tree = Build("<root xmlns:a=\"urn:a\"><mid xmlns:b=\"urn:b\"><leaf/></mid></root>");

            // Inherited from the document element.
            Assert.AreEqual("urn:a", tree.ResolvePrefix(3, "a"));

            // Inherited from an intermediate ancestor.
            Assert.AreEqual("urn:b", tree.ResolvePrefix(3, "b"));

            // Out of scope above the element that declared it.
            Assert.IsNull(tree.ResolvePrefix(1, "b"));

            // Never declared.
            Assert.IsNull(tree.ResolvePrefix(3, "zz"));

            // The xml prefix is always bound without declaration.
            Assert.AreEqual(XdmTree.XmlNamespaceUri, tree.ResolvePrefix(3, "xml"));
        }

        [TestMethod]
        public void DefaultNamespaceCanBeOverriddenAndUndeclared()
        {
            XdmTree tree = Build("<root xmlns=\"urn:d\"><mid xmlns=\"\"><leaf/></mid></root>");

            Assert.AreEqual("urn:d", tree.ResolvePrefix(1, string.Empty));

            // xmlns="" un-declares the default namespace rather than binding it.
            Assert.AreEqual(string.Empty, tree.ResolvePrefix(2, string.Empty));

            // The elements themselves must reflect the same scoping.
            NameTable names = tree.NameTable;
            Assert.AreEqual("urn:d", names.GetNamespaceUri(tree.FingerprintOf(1)));
            Assert.AreEqual(string.Empty, names.GetNamespaceUri(tree.FingerprintOf(3)));
        }

        [TestMethod]
        public void NamespaceDeclarationsAreRecordedButAreNotAttributes()
        {
            XdmTree tree = Build("<root xmlns:a=\"urn:a\" real=\"1\"/>");

            // xmlns:a must not have become an attribute node.
            Assert.AreEqual(1, tree.AttributeCountOf(1));
            Assert.AreEqual("real", tree.NameTable.GetLocalName(tree.FingerprintOf(tree.AttributeAt(1, 0))));

            (string Prefix, string Uri)[] declarations = tree.NamespaceDeclarationsOf(1).ToArray();
            Assert.HasCount(1, declarations);
            Assert.AreEqual("a", declarations[0].Prefix);
            Assert.AreEqual("urn:a", declarations[0].Uri);
        }

        [TestMethod]
        public void WhitespaceTextIsPreservedAndAdjacentTextIsMerged()
        {
            XdmTree tree = Build("<root>\n  <child/>\n</root>");

            Assert.AreEqual(NodeKind.Text, tree.KindOf(2));
            Assert.AreEqual("\n  ", tree.StringValueOf(2));
            Assert.AreEqual(NodeKind.Element, tree.KindOf(3));
            Assert.AreEqual(NodeKind.Text, tree.KindOf(4));
            Assert.AreEqual("\n", tree.StringValueOf(4));
        }

        [TestMethod]
        public void DescendantSweepFindsExactlyTheSubtree()
        {
            XdmTree tree = Build("<a><b><c/></b><d/></a>");

            // The point of preorder numbering: descendants are a contiguous integer range.
            List<int> descendantsOfB = new List<int>();
            for (int i = 2 + 1; i <= tree.SubtreeEndOf(2); i++)
            {
                descendantsOfB.Add(i);
            }

            CollectionAssert.AreEqual(new[] { 3 }, descendantsOfB);

            List<int> descendantsOfA = new List<int>();
            for (int i = 1 + 1; i <= tree.SubtreeEndOf(1); i++)
            {
                descendantsOfA.Add(i);
            }

            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, descendantsOfA);
        }

        [TestMethod]
        public void ExternalEntitiesAreNotFetchedWithoutAResolver()
        {
            // A transform library must not read what a document names outside itself unless the caller
            // said it may: with no resolver an external entity expands to nothing, and no file is read.
            // The declaration itself is read, so the document parses.
            string hostile =
                "<!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><r>&x;</r>";

            XdmTree tree = Build(hostile);
            Assert.AreEqual(string.Empty, tree.StringValueOf(1));
        }

        // ---- The builder's name cache ---------------------------------------------------------------------
        //
        // The cache in front of the name table recognises a repeated name by comparing string references, which
        // is sound only because a hit still has to agree on all three strings and a miss falls through to the
        // table. These tests attack the two ways that reasoning could be wrong: names that are equal without
        // being the same instance, and different names landing in the same slot.

        [TestMethod]
        public void NamesEqualButNotIdenticalStillIntern()
        {
            // A caller building a tree by hand — or from JSON — supplies strings assembled at run time rather
            // than the atomized ones a reader hands back. Two such names must still be one name.
            XdmTreeBuilder builder = new XdmTreeBuilder();
            builder.StartElement(string.Empty, string.Empty, "item");

            for (int i = 0; i < 3; i++)
            {
                // A fresh instance each time, equal to "item" but never the same object.
                builder.StartElement(string.Empty, string.Empty, new string("item".ToCharArray()));
                builder.EndElement();
            }

            builder.EndElement();
            XdmTree tree = builder.Finish();

            int expected = tree.FingerprintOf(1);
            for (int node = 2; node <= 4; node++)
            {
                Assert.AreEqual(expected, tree.FingerprintOf(node), $"node {node} interned as a different name");
            }
        }

        [TestMethod]
        public void ManyDistinctNamesSurviveCacheEviction()
        {
            // Far more names than the cache holds, interleaved so that slots are overwritten between uses of
            // the same name. Every element must still report the name it was given.
            const int Count = 500;

            XdmTreeBuilder builder = new XdmTreeBuilder();
            builder.StartElement(string.Empty, string.Empty, "root");

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < Count; i++)
                {
                    builder.StartElement(string.Empty, i % 2 == 0 ? "urn:even" : "urn:odd", $"name{i}");
                    builder.AddAttribute(string.Empty, string.Empty, $"attribute{i}", i.ToString());
                    builder.EndElement();
                }
            }

            builder.EndElement();
            XdmTree tree = builder.Finish();

            int node = 2;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < Count; i++, node++)
                {
                    int fingerprint = tree.FingerprintOf(node);

                    Assert.AreEqual($"name{i}", tree.NameTable.GetLocalName(fingerprint));
                    Assert.AreEqual(
                        i % 2 == 0 ? "urn:even" : "urn:odd", tree.NameTable.GetNamespaceUri(fingerprint));

                    int attribute = tree.AttributeAt(node, 0);
                    Assert.AreEqual(
                        $"attribute{i}", tree.NameTable.GetLocalName(tree.FingerprintOf(attribute)));
                }
            }

            // Both passes named the same things, so the table must not have grown a second set.
            Assert.AreEqual(2 * Count + 1, tree.NameTable.FingerprintCount);
        }

        [TestMethod]
        public void NamesDifferingOnlyInNamespaceAreNotConfused()
        {
            // The slot is chosen from the local name alone, so these two collide by construction. Only the
            // reference comparison on the namespace keeps them apart.
            XdmTreeBuilder builder = new XdmTreeBuilder();
            builder.StartElement(string.Empty, string.Empty, "root");

            for (int i = 0; i < 4; i++)
            {
                builder.StartElement(string.Empty, i % 2 == 0 ? "urn:a" : "urn:b", "same");
                builder.EndElement();
            }

            builder.EndElement();
            XdmTree tree = builder.Finish();

            Assert.AreEqual("urn:a", tree.NameTable.GetNamespaceUri(tree.FingerprintOf(2)));
            Assert.AreEqual("urn:b", tree.NameTable.GetNamespaceUri(tree.FingerprintOf(3)));
            Assert.AreEqual("urn:a", tree.NameTable.GetNamespaceUri(tree.FingerprintOf(4)));
            Assert.AreEqual("urn:b", tree.NameTable.GetNamespaceUri(tree.FingerprintOf(5)));
        }

        [TestMethod]
        public void PrefixesAreNotConfusedForTheSameExpandedName()
        {
            // Same expanded name, different prefixes: one fingerprint, two name codes. A cache hit has to
            // agree on the prefix too, or serialization would reproduce the wrong one.
            XdmTreeBuilder builder = new XdmTreeBuilder();
            builder.StartElement(string.Empty, string.Empty, "root");

            for (int i = 0; i < 4; i++)
            {
                builder.StartElement(i % 2 == 0 ? "p" : "q", "urn:x", "thing");
                builder.EndElement();
            }

            builder.EndElement();
            XdmTree tree = builder.Finish();

            Assert.AreEqual(tree.FingerprintOf(2), tree.FingerprintOf(3), "the expanded name is the same");

            Assert.AreEqual("p", tree.NameTable.GetPrefix(tree.NameCodeOf(2)));
            Assert.AreEqual("q", tree.NameTable.GetPrefix(tree.NameCodeOf(3)));
            Assert.AreEqual("p", tree.NameTable.GetPrefix(tree.NameCodeOf(4)));
            Assert.AreEqual("q", tree.NameTable.GetPrefix(tree.NameCodeOf(5)));
        }

        [TestMethod]
        public void WhitespaceOutsideTheDocumentElementIsNotANode()
        {
            // XML's grammar allows comments and processing instructions around the document element and no
            // character data at all — so the newline a text editor leaves at the end of a file is not part
            // of the document. Keeping it made a document node with a text child, which the built-in
            // template rule then copied: a newline in the result because the input file ended in one.
            XdmTree tree = Build("\n<doc>\n</doc>\n");

            int children = 0;
            for (int child = tree.FirstChildOf(XdmTree.RootNode); child >= 0; child = tree.NextSiblingOf(child))
            {
                children++;
                Assert.AreEqual(NodeKind.Element, tree.KindOf(child));
            }

            Assert.AreEqual(1, children);

            // Inside the element it is content like any other, and stays.
            Assert.AreEqual("\n", tree.StringValueOf(tree.FirstChildOf(XdmTree.RootNode)));
        }

        [TestMethod]
        public void CommentsAndInstructionsOutsideTheDocumentElementAreKept()
        {
            XdmTree tree = Build("<!--before--><?pi go?>\n<doc/>\n<!--after-->");

            List<NodeKind> kinds = new List<NodeKind>();
            for (int child = tree.FirstChildOf(XdmTree.RootNode); child >= 0; child = tree.NextSiblingOf(child))
            {
                kinds.Add(tree.KindOf(child));
            }

            CollectionAssert.AreEqual(
                new[]
                {
                    NodeKind.Comment, NodeKind.ProcessingInstruction, NodeKind.Element, NodeKind.Comment,
                },
                kinds);
        }
    }
}
