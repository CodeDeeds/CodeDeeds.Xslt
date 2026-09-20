using System.Text;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that what is walked may be nested deeper than any stack: a document twenty thousand elements
    /// deep, and a map or an array nested further than that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stack overflow cannot be caught and takes the process with it, test host included, so a failure
    /// here does not read as a failed test: the run ends. Every case was run by hand in a process of its
    /// own before it was put here, and a case added later should be too.
    /// </para>
    /// <para>
    /// Two promises are tested, and they differ. A walk over a <em>node tree</em> has no limit, because a
    /// deep document is ordinary input: those cases give the right answer at any depth. A walk over nested
    /// <em>maps and arrays</em> is a recursion that asks how much stack is left, so those cases end in the
    /// right answer or in an <see cref="XsltException"/> saying the value is nested too deeply, according
    /// to the stack the test host happens to give, and either is a pass. What they may not do is end the
    /// process.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class DeepNestingTests
    {
        /// <summary>How deep the documents are: more levels than a megabyte of stack has room for.</summary>
        private const int Deep = 20000;

        /// <summary>
        /// How deep the maps and arrays are. Ten times the documents, so that no stack a host is likely
        /// to give holds the walk and the refusal is what is tested wherever these run.
        /// </summary>
        private const int DeepValue = 200000;

        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\""
            + " xmlns:array=\"http://www.w3.org/2005/xpath-functions/array\" exclude-result-prefixes=\"#all\">";

        /// <summary>An array in an array, as deep as asked, made by a fold: no recursion builds it.</summary>
        private static readonly string NestedArrays =
            Variable("deep", "[1]", "[$a]") + Variable("again", "[1]", "[$a]") + Variable("other", "[2]", "[$a]");

        /// <summary>A map in a map, made the same way.</summary>
        private static readonly string NestedMaps =
            Variable("deep", "map{'k':1}", "map{'k':$a}") + Variable("again", "map{'k':1}", "map{'k':$a}");

        private static string Variable(string name, string innermost, string wrap)
        {
            return "<xsl:variable name=\"" + name + "\" select=\"fold-left(1 to " + DeepValue + ", " + innermost
                + ", function($a, $i) { " + wrap + " })\"/>";
        }

        /// <summary>One element inside another, <paramref name="depth"/> deep, round some text.</summary>
        private static string Nested(int depth, string name = "a", string innermost = "x")
        {
            StringBuilder xml = new StringBuilder(depth * (2 * name.Length + 5));

            for (int i = 0; i < depth; i++)
            {
                xml.Append('<').Append(name).Append('>');
            }

            xml.Append(innermost);

            for (int i = 0; i < depth; i++)
            {
                xml.Append("</").Append(name).Append('>');
            }

            return xml.ToString();
        }

        /// <summary>A variable holding the text of a document like <see cref="Nested"/>'s, for <c>parse-xml</c>.</summary>
        private static string NestedText(string variable, string innermost)
        {
            return "<xsl:variable name=\"" + variable + "\" select=\"string-join((for $i in 1 to " + Deep
                + " return '&lt;a>'), '') || '" + innermost + "' || string-join((for $i in 1 to " + Deep
                + " return '&lt;/a>'), '')\"/>";
        }

        private static string Run(string declarations, string body, string? input = null, XsltOptions? options = null)
        {
            string stylesheet = Head + declarations
                + "<xsl:template match=\"/\" name=\"xsl:initial-template\"><out>" + body + "</out></xsl:template>"
                + "</xsl:stylesheet>";

            Xslt xslt = new Xslt(
                stylesheet, options ?? new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true });

            return input is null ? xslt.Transform() : xslt.TransformXml(input);
        }

        /// <summary>
        /// Runs something over a value nested too deep for the stack, which has to end in its answer or
        /// in the refusal, and says which.
        /// </summary>
        private static void AnswersOrRefuses(string expected, string declarations, string body)
        {
            try
            {
                Assert.AreEqual("<out>" + expected + "</out>", Run(declarations, body));
            }
            catch (XsltException refusal)
            {
                StringAssert.Contains(refusal.Message, "nested too deeply");
            }
        }

        // ---- Node trees: no limit ------------------------------------------------------------------------

        [TestMethod]
        public void DeepEqualComparesTwoDeepDocuments()
        {
            // Two trees and not one twice: the document that was read, and one parsed from the same text.
            // The third has other text at the bottom, which only a comparison that gets there can tell.
            Assert.AreEqual(
                "<out>true|true|false|false</out>",
                Run(
                    NestedText("same", "x") + NestedText("differs", "y"),
                    "<xsl:value-of select=\"deep-equal(., .), deep-equal(., parse-xml($same)), "
                    + "deep-equal(., parse-xml($differs)), deep-equal(*, parse-xml($same)/*/*)\" separator=\"|\"/>",
                    Nested(Deep)));
        }

        [TestMethod]
        public void DeepEqualTellsShapesApartByDepth()
        {
            // The comparison reads two subtrees side by side in document order, so what tells two shapes
            // apart is the depth each node is at: the same names in the same order are not the same tree.
            Assert.AreEqual(
                "<out>false|false|false|true|true|false|true</out>",
                Run(
                    "<xsl:variable name=\"siblings\"><a><b/><c/></a></xsl:variable>"
                    + "<xsl:variable name=\"nested\"><a><b><c/></b></a></xsl:variable>"
                    + "<xsl:variable name=\"longer\"><a><b/><c/><d/></a></xsl:variable>"
                    + "<xsl:variable name=\"commented\"><a><xsl:comment>one</xsl:comment><b><xsl:comment>two</xsl:comment></b>"
                    + "<xsl:processing-instruction name=\"p\">three</xsl:processing-instruction><c/><xsl:comment>four</xsl:comment></a></xsl:variable>"
                    + "<xsl:variable name=\"lower\"><w><w><a><b/><c/></a></w></w></xsl:variable>"
                    + "<xsl:variable name=\"split\"><a>x<xsl:comment>between</xsl:comment>y</a></xsl:variable>"
                    + "<xsl:variable name=\"joined\"><a>xy</a></xsl:variable>",
                    "<xsl:value-of select=\"deep-equal($siblings, $nested), deep-equal($siblings, $longer), "
                    + "deep-equal($longer, $siblings), deep-equal($siblings, $commented), "

                    // The same subtree at another depth of another tree: depths count from the two nodes asked about.
                    + "deep-equal($siblings/a, $lower/w/w/a), "

                    // A comment is left out, and the text either side of it stays two text nodes.
                    + "deep-equal($split, $joined), deep-equal($split/a/comment(), $split/a/comment())\" separator=\"|\"/>"));
        }

        [TestMethod]
        public void AParentlessNodeIsAtTheTopOfItsOwnDepths()
        {
            // What deep-equal reads a shape from. A node an 'as' declaration makes parentless is a root, and
            // what is under it counts its depth from there: it used to keep the depth it had inside the
            // document it was built in, one too many, and a copy then compared unlike what it was a copy of.
            IReadOnlyList<XPath.XPathValue> built = new Xslt(
                Head + "<xsl:template name=\"xsl:initial-template\" as=\"element()\"><p><q><r/>text</q></p></xsl:template></xsl:stylesheet>",
                new XsltOptions { Version = XsltVersion.V30 }).TransformToSequence();

            Model.XdmTree tree = built[0].NodeTree;
            int p = built[0].NodeId;
            int q = tree.FirstChildOf(p);
            int r = tree.FirstChildOf(q);

            Assert.AreEqual(-1, tree.ParentOf(p));
            Assert.AreEqual("0 1 2 2", string.Join(
                " ", tree.DepthOf(p), tree.DepthOf(q), tree.DepthOf(r), tree.DepthOf(tree.NextSiblingOf(r))));

            Assert.AreEqual(
                "<out>true|true|false</out>",
                Run(
                    "<xsl:variable name=\"made\" as=\"element()\"><p k=\"1\"><q><r/>text</q></p></xsl:variable>"
                    + "<xsl:variable name=\"inside\"><w><p k=\"1\"><q><r/>text</q></p></w></xsl:variable>",
                    "<xsl:value-of select=\"deep-equal($made, $inside/w/p), deep-equal($made, copy-of($made)), "
                    + "deep-equal($made/q, $inside/w/p)\" separator=\"|\"/>"));
        }

        [TestMethod]
        public void InnermostAndOutermostReadADeepChain()
        {
            // Every element of the chain is inside the one before it, so one is innermost and one outermost.
            // Asking each about each by walking up from it was the cube of the depth, and did not come back.
            Assert.AreEqual(
                "<out>1|1|0|" + (Deep - 1) + "</out>",
                Run(
                    string.Empty,
                    "<xsl:value-of select=\"count(innermost(//a)), count(outermost(//a)), "
                    + "count(innermost(//a)/a), count(outermost(//a)//a)\" separator=\"|\"/>",
                    Nested(Deep)));
        }

        [TestMethod]
        public void InnermostAndOutermostCountAttributesAsBeingUnderTheirElement()
        {
            const string Input = "<r><b x=\"1\"><c/></b><b><d/></b><e/></r>";

            Assert.AreEqual(
                "<out>r|b b|c b|x b|x c d|x c|b b e</out>",
                Run(
                    string.Empty,
                    "<xsl:value-of select=\"string-join(outermost(//*)/name(), ' '), "
                    + "string-join(outermost((//c, //@x, //b))/name(), ' '), "
                    + "string-join(innermost((//b, //c))/name(), ' '), "
                    + "string-join(innermost((//b[1], //@x, //b[2]))/name(), ' '), "

                    // An attribute is nothing's ancestor, and is under what its element is under. Given out
                    // of order and twice over, and answered in document order once.
                    + "string-join(outermost((//d, //c, //@x, //c))/name(), ' '), "
                    + "string-join(innermost((//c, //@x, //b[1], /r))/name(), ' '), "
                    + "string-join(outermost((//b, //e, //c, //d))/name(), ' ')\" separator=\"|\"/>",
                    Input));
        }

        [TestMethod]
        public void ValidationWalksADeepTree()
        {
            const string Schema =
                "<xsl:import-schema><xs:schema elementFormDefault=\"qualified\">"
                + "<xs:element name=\"a\" type=\"aType\"/>"
                + "<xs:complexType name=\"aType\" mixed=\"true\"><xs:sequence>"
                + "<xs:element ref=\"a\" minOccurs=\"0\"/></xs:sequence></xs:complexType>"
                + "</xs:schema></xsl:import-schema>";

            XsltOptions options = new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true, SchemaAware = true };

            // The element copied and validated, and the document built and validated: both walk the tree
            // that was built, and every element of it comes back with the type the schema gives it.
            Assert.AreEqual(
                "<out>" + Deep + "|" + Deep + "</out>",
                Run(
                    Schema,
                    "<xsl:variable name=\"copied\"><xsl:copy-of select=\"*\" validation=\"strict\"/></xsl:variable>"
                    + "<xsl:variable name=\"built\"><xsl:document validation=\"strict\"><xsl:copy-of select=\"*\"/></xsl:document></xsl:variable>"
                    + "<xsl:value-of select=\"count($copied//element(*, aType)), count($built//element(*, aType))\" separator=\"|\"/>",
                    Nested(Deep),
                    options));
        }

        [TestMethod]
        public void ValidationStillFeedsWhitespaceAndTextInOrder()
        {
            // The walk is a loop now, so what it has to get right is the order: whitespace between the
            // children of an element-only type is formatting, text in the middle of one is an error, and
            // an element is ended after its last child and not before.
            const string Schema =
                "<xsl:import-schema><xs:schema elementFormDefault=\"qualified\">"
                + "<xs:element name=\"list\"><xs:complexType><xs:sequence>"
                + "<xs:element name=\"item\" maxOccurs=\"unbounded\"><xs:complexType><xs:sequence>"
                + "<xs:element name=\"n\" type=\"xs:integer\"/></xs:sequence></xs:complexType></xs:element>"
                + "</xs:sequence></xs:complexType></xs:element>"
                + "</xs:schema></xsl:import-schema>";

            XsltOptions options = new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true, SchemaAware = true };

            Assert.AreEqual(
                "<out>2 3</out>",
                Run(
                    Schema,
                    "<xsl:variable name=\"v\"><xsl:copy-of select=\"*\" validation=\"strict\"/></xsl:variable>"
                    + "<xsl:value-of select=\"count($v//n[. instance of element(*, xs:integer)]), sum($v//n)\"/>",
                    "<list>\n  <item>\n    <n>1</n>\n  </item>\n  <!-- between -->\n  <item><n>2</n></item>\n</list>",
                    options));

            XsltException invalid = Assert.ThrowsExactly<XsltException>(() => Run(
                Schema,
                "<xsl:copy-of select=\"*\" validation=\"strict\"/>",
                "<list><item><n>1</n></item>stray<item><n>2</n></item></list>",
                options));

            Assert.AreEqual("XTTE1510", invalid.Code);
        }

        // ---- Maps and arrays: the answer, or a refusal ---------------------------------------------------

        [TestMethod]
        public void BuildingADeepValueCostsNoStack()
        {
            // Nothing here goes inside, so nothing here is refused: the value is made, held and measured.
            Assert.AreEqual(
                "<out>1|1|true</out>",
                Run(
                    NestedArrays + NestedMaps.Replace("\"deep\"", "\"deepMap\"").Replace("\"again\"", "\"againMap\""),
                    "<xsl:value-of select=\"array:size($deep), map:size($deepMap), $deep instance of array(*)\" separator=\"|\"/>"));
        }

        [TestMethod]
        public void DeepEqualOverDeepArraysAndMaps()
        {
            AnswersOrRefuses("true false", NestedArrays, "<xsl:value-of select=\"deep-equal($deep, $again), deep-equal($deep, $other)\"/>");
            AnswersOrRefuses("true", NestedMaps, "<xsl:value-of select=\"deep-equal($deep, $again)\"/>");
        }

        [TestMethod]
        public void AtomizingADeepArray()
        {
            AnswersOrRefuses("1", NestedArrays, "<xsl:value-of select=\"data($deep)\"/>");
            AnswersOrRefuses("1", NestedArrays, "<xsl:value-of select=\"$deep\"/>");
            AnswersOrRefuses("1", NestedArrays, "<xsl:value-of select=\"sum($deep)\"/>");
            AnswersOrRefuses("true", NestedArrays, "<xsl:value-of select=\"$deep = 1\"/>");
            AnswersOrRefuses("1", NestedArrays, "<xsl:value-of select=\"count(sort(($deep, $again))[1])\"/>");
        }

        [TestMethod]
        public void FlatteningADeepArray()
        {
            AnswersOrRefuses("1", NestedArrays, "<xsl:value-of select=\"array:flatten($deep)\"/>");
            AnswersOrRefuses("1", NestedArrays, "<xsl:copy-of select=\"$deep\"/>");
            AnswersOrRefuses("1", NestedArrays, "<xsl:where-populated><xsl:sequence select=\"$deep\"/></xsl:where-populated>");
        }

        [TestMethod]
        public void SearchingADeepMap()
        {
            AnswersOrRefuses((DeepValue + 1).ToString(), NestedMaps, "<xsl:value-of select=\"array:size(map:find($deep, 'k'))\"/>");
        }

        [TestMethod]
        public void ApplyingTemplatesToADeepArray()
        {
            // The built-in rule for an array applies templates to its members, which here is an array again.
            AnswersOrRefuses("1", NestedArrays, "<xsl:apply-templates select=\"$deep\"/>");
        }

        [TestMethod]
        public void SerializingADeepValue()
        {
            string length = (2 * (DeepValue + 1) + 1).ToString();

            AnswersOrRefuses(length, NestedArrays, "<xsl:value-of select=\"string-length(serialize($deep, map{'method':'json'}))\"/>");
            AnswersOrRefuses(length, NestedArrays, "<xsl:value-of select=\"string-length(serialize($deep, map{'method':'adaptive'}))\"/>");

            AnswersOrRefuses(
                (5 * (DeepValue + 1) + 1 + (DeepValue + 1)).ToString(),
                NestedMaps,
                "<xsl:value-of select=\"string-length(serialize($deep, map{'method':'json'}))\"/>");
        }

        [TestMethod]
        public void WritingDeepElementsAsJson()
        {
            // The elements that stand for JSON are the one node tree read by recursion, a map or an array
            // at a time, and so the one that is refused rather than read at any depth.
            string declarations =
                "<xsl:variable name=\"text\" select=\"string-join((for $i in 1 to " + Deep
                + " return '&lt;array xmlns=&quot;http://www.w3.org/2005/xpath-functions&quot;>'), '') || "
                + "'&lt;number xmlns=&quot;http://www.w3.org/2005/xpath-functions&quot;>1&lt;/number>' || "
                + "string-join((for $i in 1 to " + Deep + " return '&lt;/array>'), '')\"/>";

            try
            {
                Assert.AreEqual(
                    "<out>" + (2 * Deep + 1) + "</out>",
                    Run(declarations, "<xsl:value-of select=\"string-length(xml-to-json(parse-xml($text)))\"/>"));
            }
            catch (XsltException refusal)
            {
                StringAssert.Contains(refusal.Message, "nested too deeply");
            }
        }

        [TestMethod]
        public void ARefusalCanBeCaught()
        {
            // An error like any other: xsl:try sees it, and the transformation goes on.
            string result = Run(
                NestedArrays,
                "<xsl:try><xsl:value-of select=\"data($deep)\"/><xsl:catch>caught</xsl:catch></xsl:try>");

            Assert.IsTrue(result is "<out>1</out>" or "<out>caught</out>", result);
        }
    }
}
