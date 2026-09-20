using CodeDeeds.Xslt;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for XPath 1.0 parsing and evaluation: paths and axes, predicates and context position, the
    /// conversion rules, and the lexical disambiguation the grammar depends on.
    /// </summary>
    [TestClass]
    public sealed class XPathTests
    {
        private static XPathValue Evaluate(
            string xml,
            string expression,
            int contextNode = 0,
            Action<XPathStaticContext>? configure = null)
        {
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader(xml));
            XPathStaticContext staticContext = new XPathStaticContext();
            configure?.Invoke(staticContext);

            // Parsing populates the slot table, so the fingerprint map can only be built afterwards.
            Expr expression2 = XPathParser.Parse(expression, staticContext);
            int[] map = staticContext.Names.BuildFingerprintMap(tree);

            DynamicContext context = new DynamicContext(tree, contextNode, map);
            return expression2.Evaluate(ref context);
        }

        private static string[] LocalNames(string xml, string expression, int contextNode = 0)
        {
            NodeSet nodes = Evaluate(xml, expression, contextNode).AsNodeSet();
            string[] names = new string[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                names[i] = nodes.Tree.NameTable.GetLocalName(nodes.Tree.FingerprintOf(nodes[i]));
            }

            return names;
        }

        private static string[] StringValues(string xml, string expression, int contextNode = 0)
        {
            NodeSet nodes = Evaluate(xml, expression, contextNode).AsNodeSet();
            string[] values = new string[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                values[i] = nodes.Tree.StringValueOf(nodes[i]);
            }

            return values;
        }

        private static string Text(
            string xml,
            string expression,
            int contextNode = 0,
            Action<XPathStaticContext>? configure = null)
        {
            return Evaluate(xml, expression, contextNode, configure).ToStringValue();
        }

        private static double Number(
            string xml,
            string expression,
            int contextNode = 0,
            Action<XPathStaticContext>? configure = null)
        {
            return Evaluate(xml, expression, contextNode, configure).ToNumber();
        }

        private static bool Boolean(string xml, string expression, int contextNode = 0)
        {
            return Evaluate(xml, expression, contextNode).ToBoolean();
        }

        // ---- Location paths and axes ----------------------------------------------------------------

        [TestMethod]
        public void AbsoluteAndRelativePathsSelectChildren()
        {
            string xml = "<r><a>1</a><b>2</b><a>3</a></r>";

            CollectionAssert.AreEqual(new[] { "1", "3" }, StringValues(xml, "/r/a"));
            CollectionAssert.AreEqual(new[] { "2" }, StringValues(xml, "/r/b"));
            CollectionAssert.AreEqual(new[] { "1", "2", "3" }, StringValues(xml, "/r/*"));
        }

        [TestMethod]
        public void DoubleSlashSearchesDescendants()
        {
            string xml = "<r><x><y>deep</y></x><y>shallow</y></r>";

            CollectionAssert.AreEqual(new[] { "deep", "shallow" }, StringValues(xml, "//y"));
            CollectionAssert.AreEqual(new[] { "deep" }, StringValues(xml, "/r/x//y"));

            // '//' abbreviates descendant-or-self, so it can also match the node it starts from.
            CollectionAssert.AreEqual(new[] { "deep" }, StringValues(xml, "/r/x//y"));
        }

        [TestMethod]
        public void AttributesAreSelectedByTheAttributeAxis()
        {
            string xml = "<r><i id=\"1\" k=\"a\"/><i id=\"2\"/></r>";

            CollectionAssert.AreEqual(new[] { "1", "2" }, StringValues(xml, "/r/i/@id"));
            CollectionAssert.AreEqual(new[] { "1", "a", "2" }, StringValues(xml, "//@*"));
            Assert.AreEqual(3, Number(xml, "count(//@*)"));
        }

        [TestMethod]
        public void DotAndDoubleDotNavigateSelfAndParent()
        {
            string xml = "<r><a><b/></a></r>";

            // Context node 3 is <b>.
            CollectionAssert.AreEqual(new[] { "b" }, LocalNames(xml, ".", 3));
            CollectionAssert.AreEqual(new[] { "a" }, LocalNames(xml, "..", 3));
            CollectionAssert.AreEqual(new[] { "r" }, LocalNames(xml, "../..", 3));
        }

        [TestMethod]
        public void ForwardAxesAreSupported()
        {
            string xml = "<r><a/><b><c/></b><d/></r>";

            // Context node 3 is <b>.
            CollectionAssert.AreEqual(new[] { "c" }, LocalNames(xml, "child::*", 3));
            CollectionAssert.AreEqual(new[] { "c" }, LocalNames(xml, "descendant::*", 3));
            CollectionAssert.AreEqual(new[] { "b", "c" }, LocalNames(xml, "descendant-or-self::*", 3));
            CollectionAssert.AreEqual(new[] { "d" }, LocalNames(xml, "following-sibling::*", 3));
            CollectionAssert.AreEqual(new[] { "d" }, LocalNames(xml, "following::*", 3));
        }

        [TestMethod]
        public void ReverseAxesYieldNearestNodeFirst()
        {
            string xml = "<r><a/><b/><c><d/></c></r>";

            // Context node 4 is <c>; its preceding siblings are <b> then <a>, nearest first.
            CollectionAssert.AreEqual(new[] { "b" }, LocalNames(xml, "preceding-sibling::*[1]", 4));
            CollectionAssert.AreEqual(new[] { "a" }, LocalNames(xml, "preceding-sibling::*[2]", 4));

            // Context node 5 is <d>; ancestor::*[1] is <c>, not <r>.
            CollectionAssert.AreEqual(new[] { "c" }, LocalNames(xml, "ancestor::*[1]", 5));
            CollectionAssert.AreEqual(new[] { "r" }, LocalNames(xml, "ancestor::*[2]", 5));
            CollectionAssert.AreEqual(new[] { "d" }, LocalNames(xml, "ancestor-or-self::*[1]", 5));
        }

        [TestMethod]
        public void PrecedingAxisExcludesAncestors()
        {
            string xml = "<r><a><b/></a><c/></r>";

            // Context node 4 is <c>. <a> and <b> precede it; <r> is an ancestor and must be excluded.
            // The result of a path is a node-set, so it comes back in document order however the axis walked.
            CollectionAssert.AreEqual(new[] { "a", "b" }, LocalNames(xml, "preceding::*", 4));

            // The axis's reverse ordering shows up where it actually matters — counting inside a predicate,
            // where position 1 is the nearest preceding node rather than the first in the document.
            CollectionAssert.AreEqual(new[] { "b" }, LocalNames(xml, "preceding::*[1]", 4));
            CollectionAssert.AreEqual(new[] { "a" }, LocalNames(xml, "preceding::*[2]", 4));
        }

        [TestMethod]
        public void NodeTypeTestsSelectByKind()
        {
            string xml = "<r>text<!--c--><?pi d?><e/></r>";

            Assert.AreEqual(1, Number(xml, "count(/r/text())"));
            Assert.AreEqual("text", Text(xml, "/r/text()"));
            Assert.AreEqual("c", Text(xml, "/r/comment()"));
            Assert.AreEqual("d", Text(xml, "/r/processing-instruction()"));
            Assert.AreEqual("d", Text(xml, "/r/processing-instruction('pi')"));
            Assert.AreEqual(0, Number(xml, "count(/r/processing-instruction('other'))"));

            // node() matches every kind of child, but never attributes.
            Assert.AreEqual(4, Number(xml, "count(/r/node())"));
        }

        // ---- Predicates and context position ---------------------------------------------------------

        [TestMethod]
        public void NumericPredicatesSelectByPosition()
        {
            string xml = "<r><i>a</i><i>b</i><i>c</i></r>";

            Assert.AreEqual("a", Text(xml, "/r/i[1]"));
            Assert.AreEqual("b", Text(xml, "/r/i[2]"));
            Assert.AreEqual("c", Text(xml, "/r/i[last()]"));
            Assert.AreEqual("b", Text(xml, "/r/i[position()=2]"));
            CollectionAssert.AreEqual(new[] { "b", "c" }, StringValues(xml, "/r/i[position()>1]"));
        }

        [TestMethod]
        public void PredicatesApplyLeftToRight()
        {
            string xml = "<r><i k=\"y\">a</i><i>b</i><i k=\"y\">c</i></r>";

            // Filter by attribute first, then take the second survivor.
            Assert.AreEqual("c", Text(xml, "/r/i[@k='y'][2]"));

            // Reversing the predicates changes the meaning entirely: the second <i> has no @k.
            Assert.AreEqual(0, Number(xml, "count(/r/i[2][@k='y'])"));
        }

        [TestMethod]
        public void ContextSizeIsPerStepNotPerPath()
        {
            string xml = "<r><g><i>a</i><i>b</i></g><g><i>c</i></g></r>";

            // last() is evaluated within each <g> separately, so both first children are selected.
            CollectionAssert.AreEqual(new[] { "b", "c" }, StringValues(xml, "/r/g/i[last()]"));
        }

        // ---- Namespaces -------------------------------------------------------------------------------

        [TestMethod]
        public void PrefixedNameTestsMatchOnNamespaceUriNotPrefix()
        {
            string xml = "<r xmlns:a=\"urn:x\"><a:e>hit</a:e><e>miss</e></r>";

            // The expression's prefix need not match the document's prefix, only the URI.
            Assert.AreEqual("hit", Text(xml, "//p:e", configure: c => c.DeclarePrefix("p", "urn:x")));
            Assert.AreEqual("hit", Text(xml, "//p:*", configure: c => c.DeclarePrefix("p", "urn:x")));
        }

        [TestMethod]
        public void UnprefixedNamesAreInNoNamespaceEvenUnderADefaultNamespace()
        {
            // This is the rule that catches people out with ISO 20022 and other namespaced vocabularies:
            // a default xmlns in the document does not make unprefixed XPath names match it.
            string xml = "<r xmlns=\"urn:d\"><e>value</e></r>";

            Assert.AreEqual(0, Number(xml, "count(//e)"));
            Assert.AreEqual(1, Number(xml, "count(//p:e)", configure: c => c.DeclarePrefix("p", "urn:d")));
            Assert.AreEqual("value", Text(xml, "//p:e", configure: c => c.DeclarePrefix("p", "urn:d")));
        }

        [TestMethod]
        public void UnboundPrefixIsRejectedAtCompileTime()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Evaluate("<r/>", "//nope:e"));

            StringAssert.Contains(error.Message, "nope");
        }

        [TestMethod]
        public void NameTestForANameAbsentFromTheDocumentMatchesNothing()
        {
            // The name never gets interned in the tree's table, so its slot resolves to NoFingerprint.
            // That must not accidentally equal the NoFingerprint carried by text nodes and comments.
            string xml = "<r>text<!--c--></r>";

            Assert.AreEqual(0, Number(xml, "count(//missing)"));
            Assert.AreEqual(0, Number(xml, "count(//@missing)"));
        }

        // ---- Conversions and operators ----------------------------------------------------------------

        [TestMethod]
        public void ArithmeticFollowsIeeeSemantics()
        {
            string xml = "<r/>";

            Assert.AreEqual(5.0, Number(xml, "2 + 3"));
            Assert.AreEqual(-1.0, Number(xml, "2 - 3"));
            Assert.AreEqual(6.0, Number(xml, "2 * 3"));
            Assert.AreEqual(0.5, Number(xml, "1 div 2"));
            Assert.AreEqual(1.0, Number(xml, "7 mod 3"));
            Assert.AreEqual(-1.0, Number(xml, "-7 mod 3"), "mod takes the sign of the dividend");
            Assert.AreEqual(-3.0, Number(xml, "-3"));

            // Division by zero is an infinity, not an error.
            Assert.AreEqual(double.PositiveInfinity, Number(xml, "1 div 0"));
            Assert.AreEqual(double.NegativeInfinity, Number(xml, "-1 div 0"));
            Assert.IsTrue(double.IsNaN(Number(xml, "0 div 0")));
        }

        [TestMethod]
        public void ANumberIsWrittenTheWayThisProcessorWritesOne()
        {
            string xml = "<r/>";

            Assert.AreEqual("5", Text(xml, "string(5)"));
            Assert.AreEqual("0", Text(xml, "string(0)"));
            Assert.AreEqual("0.5", Text(xml, "string(0.5)"));
            Assert.AreEqual("-0.5", Text(xml, "string(-0.5)"));

            // Compiled against 1.0's rules and written the 2.0 way. Backwards compatibility restores the
            // rules of XPath 1.0 and not the text it produced: what such an expression gives is defined by
            // the 2.0 specifications rather than by reference to the 1.0 ones (XSLT 2.0 §3.8).
            Assert.AreEqual("INF", Text(xml, "string(1 div 0)"));
            Assert.AreEqual("-INF", Text(xml, "string(-1 div 0)"));
            Assert.AreEqual("NaN", Text(xml, "string(0 div 0)"));

            // An integer never grows a decimal point or an exponent, however large it is; a double outside
            // the window from 0.000001 to 1000000 is written with one.
            Assert.AreEqual("100000", Text(xml, "string(100000)"));
            Assert.AreEqual("1.0E-20", Text(xml, "string(1 div 100000000000000000000)"));
        }

        [TestMethod]
        public void UnderOnePointZeroRulesNegationGivesADouble()
        {
            string xml = "<r/>";

            // XPath 1.0 has one numeric type, and the compatibility rules keep that by making the operand
            // of an arithmetic operator an xs:double — the unary minus as much as the binary operators,
            // which already did it. It shows in the one value a double has and an integer has not, and
            // dividing by that value is what tells the two apart.
            Assert.AreEqual("-0", Text(xml, "string(-0)"));
            Assert.AreEqual("-INF", Text(xml, "string(1 div -0)"));

            // Under 2.0 the negation keeps the operand's type, so this is the integer zero, which has no
            // sign to keep and which nothing may be divided by.
            Assert.AreEqual("0", Text(xml, "string(-0)", configure: c => c.Version = XsltVersion.V20));

            Assert.AreEqual(
                "FOAR0001",
                Assert.ThrowsExactly<XsltException>(
                    () => Text(xml, "string(1 div -0)", configure: c => c.Version = XsltVersion.V20)).Code);
        }

        [TestMethod]
        public void AStylesheetWritesNumbersTheSameWayAsString()
        {
            // The rules have to reach the output, not only fn:string(): a stylesheet that writes a number
            // and an expression that asks for its string have to agree. Both give the 2.0 form, the
            // stylesheet's version="1.0" notwithstanding — backwards compatibility restores XPath 1.0's
            // rules and not its lexical output.
            const string Sheet =
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:template match=\"/\"><out>"
                + "<a><xsl:value-of select=\"1 div 0\"/></a>"
                + "<b><xsl:value-of select=\"-1 div 0\"/></b>"
                + "<c><xsl:value-of select=\"1 div 100000000000000000000\"/></c>"
                + "</out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out><a>INF</a><b>-INF</b><c>1.0E-20</c></out>",
                new Xslt(Sheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }

        [TestMethod]
        public void NumberConversionReadsTheLexicalSpaceOfDoubleAndNothingBeyondIt()
        {
            string xml = "<r/>";

            Assert.AreEqual(42.0, Number(xml, "number('42')"));
            Assert.AreEqual(-1.5, Number(xml, "number('  -1.5  ')"), "surrounding whitespace is allowed");
            Assert.AreEqual(0.5, Number(xml, "number('.5')"));

            // Exponent notation, a leading plus and INF were all NaN to XPath 1.0. These expressions are
            // compiled at 1.0, which here is XPath 2.0 with backwards compatibility on, and its number()
            // reads what xs:double writes (XPath 2.0 Appendix I.1).
            Assert.AreEqual(1000.0, Number(xml, "number('1e3')"));
            Assert.AreEqual(1.0, Number(xml, "number('+1')"));
            Assert.AreEqual(double.PositiveInfinity, Number(xml, "number('INF')"));
            Assert.AreEqual(double.NegativeInfinity, Number(xml, "number('-INF')"));

            // What xs:double does not write is still NaN, and never an error.
            Assert.IsTrue(double.IsNaN(Number(xml, "number('NaN')")));
            Assert.IsTrue(double.IsNaN(Number(xml, "number('Infinity')")));
            Assert.IsTrue(double.IsNaN(Number(xml, "number('+INF')")));
            Assert.IsTrue(double.IsNaN(Number(xml, "number('1e')")));
            Assert.IsTrue(double.IsNaN(Number(xml, "number('0x10')")));
            Assert.IsTrue(double.IsNaN(Number(xml, "number('')")));
        }

        [TestMethod]
        public void BooleanConversionOfANodeSetTestsEmptinessNotContent()
        {
            string xml = "<r><empty/><zero>0</zero><blank></blank></r>";

            // A node-set is true when it has members, whatever they contain.
            Assert.IsTrue(Boolean(xml, "boolean(/r/empty)"));
            Assert.IsTrue(Boolean(xml, "boolean(/r/zero)"));
            Assert.IsTrue(Boolean(xml, "boolean(/r/blank)"));
            Assert.IsFalse(Boolean(xml, "boolean(/r/absent)"));

            // A string or number is judged on its own value.
            Assert.IsFalse(Boolean(xml, "boolean('')"));
            Assert.IsFalse(Boolean(xml, "boolean(0)"));
            Assert.IsFalse(Boolean(xml, "boolean(0 div 0)"), "NaN is false");
            Assert.IsTrue(Boolean(xml, "boolean('0')"), "a non-empty string is true even if it reads as zero");
        }

        [TestMethod]
        public void NodeSetComparisonIsExistentialSoNotEqualIsNotTheNegationOfEqual()
        {
            string xml = "<r><a>1</a><a>2</a></r>";

            // Some node equals '1', and some node differs from '1'. Both are true at once.
            Assert.IsTrue(Boolean(xml, "/r/a = '1'"));
            Assert.IsTrue(Boolean(xml, "/r/a != '1'"));

            // Which makes != and not(=) genuinely different operators.
            Assert.IsFalse(Boolean(xml, "not(/r/a = '1')"));

            // An empty node-set satisfies neither comparison.
            Assert.IsFalse(Boolean(xml, "/r/missing = '1'"));
            Assert.IsFalse(Boolean(xml, "/r/missing != '1'"));
            Assert.IsTrue(Boolean(xml, "not(/r/missing = '1')"));
        }

        [TestMethod]
        public void ComparingTwoNodeSetsLooksForAnyMatchingPair()
        {
            string xml = "<r><l>a</l><l>b</l><m>b</m><m>c</m></r>";

            Assert.IsTrue(Boolean(xml, "/r/l = /r/m"), "'b' occurs on both sides");
            Assert.IsTrue(Boolean(xml, "/r/l != /r/m"));
            Assert.IsFalse(Boolean(xml, "/r/l = /r/absent"));
        }

        [TestMethod]
        public void RelationalOperatorsAlwaysCompareNumerically()
        {
            string xml = "<r><n>10</n><n>9</n></r>";

            // String operands are converted to numbers, so this is 10 > 9, not a lexicographic comparison.
            Assert.IsTrue(Boolean(xml, "'10' > '9'"));

            // Existential over the node-set: one <n> is greater than 9.
            Assert.IsTrue(Boolean(xml, "/r/n > 9"));
            Assert.IsFalse(Boolean(xml, "/r/n > 10"));

            // Operand order is preserved when the node-set is on the right.
            Assert.IsTrue(Boolean(xml, "9 < /r/n"));
            Assert.IsFalse(Boolean(xml, "10 < /r/n"));

            // Every comparison against NaN is false, including the ones that look like tautologies.
            Assert.IsFalse(Boolean(xml, "number('x') > 1"));
            Assert.IsFalse(Boolean(xml, "number('x') <= 1"));
        }

        [TestMethod]
        public void EqualityAgainstABooleanConvertsTheWholeNodeSet()
        {
            string xml = "<r><a/></r>";

            // Unlike the other operand types, this is not existential: the set becomes a single boolean.
            Assert.IsTrue(Boolean(xml, "/r/a = true()"));
            Assert.IsFalse(Boolean(xml, "/r/missing = true()"));
            Assert.IsTrue(Boolean(xml, "/r/missing = false()"));
        }

        [TestMethod]
        public void LogicalOperatorsShortCircuit()
        {
            string xml = "<r/>";

            Assert.IsTrue(Boolean(xml, "true() or false()"));
            Assert.IsFalse(Boolean(xml, "true() and false()"));

            // 'and' binds tighter than 'or'.
            Assert.IsTrue(Boolean(xml, "true() or true() and false()"));
        }

        // ---- Functions --------------------------------------------------------------------------------

        [TestMethod]
        public void StringFunctionsBehaveAsSpecified()
        {
            string xml = "<r/>";

            Assert.AreEqual("abcdef", Text(xml, "concat('ab','cd','ef')"));
            Assert.IsTrue(Boolean(xml, "starts-with('abc','ab')"));
            Assert.IsTrue(Boolean(xml, "contains('abc','b')"));
            Assert.AreEqual("a", Text(xml, "substring-before('a-b','-')"));
            Assert.AreEqual("b", Text(xml, "substring-after('a-b','-')"));
            Assert.AreEqual(string.Empty, Text(xml, "substring-before('ab','-')"));
            Assert.AreEqual(3.0, Number(xml, "string-length('abc')"));
            Assert.AreEqual("a b c", Text(xml, "normalize-space('  a   b \t c  ')"));
            Assert.AreEqual("ABC", Text(xml, "translate('abc','abc','ABC')"));
            Assert.AreEqual("ac", Text(xml, "translate('abc','b','')"), "characters beyond the replacement are removed");
        }

        [TestMethod]
        public void TheStringFunctionsCountCodePointsAndNotUtf16Units()
        {
            // U+1D156 is one character and two UTF-16 units, so .NET's Length is 8 where XPath's
            // string-length is 7. Every position these functions take is counted the same way, or
            // substring() could land between the halves of a pair and take a lone surrogate — which is no
            // character at all and cannot be written into a result.
            const string Note = "\U0001D156";
            string xml = "<r/>";

            Assert.AreEqual(7.0, Number(xml, $"string-length('abc{Note}def')"));
            Assert.AreEqual("def", Text(xml, $"substring('abc{Note}def', 5)"));
            Assert.AreEqual("\U0001D156def", Text(xml, $"substring('abc{Note}def', 4)"));
            Assert.AreEqual("\U0001D156", Text(xml, $"substring('abc{Note}def', 4, 1)"));

            // translate() maps character for character, so a pair is one entry on either side of it.
            Assert.AreEqual(
                "abc\U0001D156\U0001D156EF", Text(xml, $"translate('abc{Note}def', 'def', '{Note}EF')"));
            Assert.AreEqual("abcdef", Text(xml, $"translate('abc{Note}def', '{Note}', '')"));

            // And text with nothing above the basic plane is unchanged by all of it.
            Assert.AreEqual(6.0, Number(xml, "string-length('abcdef')"));
            Assert.AreEqual("cdef", Text(xml, "substring('abcdef', 3)"));
        }

        [TestMethod]
        public void StringFunctionsBuildResultsLongerThanTheStackBuffer()
        {
            string xml = "<r/>";

            // These build into a stack buffer sized for the common case and move to the heap beyond it, so
            // the lengths either side of that threshold are what needs checking.
            foreach (int length in new[] { 127, 128, 129, 4096 })
            {
                string word = new string('a', length);

                Assert.AreEqual(word, Text(xml, $"normalize-space('  {word}  ')"));
                Assert.AreEqual(new string('b', length), Text(xml, $"translate('{word}','a','b')"));
                Assert.AreEqual(word + word, Text(xml, $"concat('{word}','{word}')"));
            }

            // A long input whose result is short: the buffer is sized from the input, so nothing here has to
            // grow, and the result must still stop at the characters that were kept.
            string spaced = new string(' ', 500) + "a" + new string(' ', 500) + "b" + new string(' ', 500);
            Assert.AreEqual("a b", Text(xml, $"normalize-space('{spaced}')"));

            // Concat cannot size its buffer in advance, so this one really does grow, more than once.
            string part = new string('x', 100);
            Assert.AreEqual(
                new string('x', 500),
                Text(xml, $"concat('{part}','{part}','{part}','{part}','{part}')"));
        }

        [TestMethod]
        public void SubstringUsesOneBasedPositionsAndRoundsItsArguments()
        {
            string xml = "<r/>";

            Assert.AreEqual("234", Text(xml, "substring('12345',2,3)"));
            Assert.AreEqual("2345", Text(xml, "substring('12345',2)"));

            // Arguments are rounded, and the range is clamped to the string.
            Assert.AreEqual("234", Text(xml, "substring('12345',1.5,2.6)"));
            Assert.AreEqual("12", Text(xml, "substring('12345',0,3)"));
            Assert.AreEqual(string.Empty, Text(xml, "substring('12345',0 div 0,3)"), "NaN selects nothing");
            Assert.AreEqual("12345", Text(xml, "substring('12345',-42, 1 div 0)"));
            Assert.AreEqual(string.Empty, Text(xml, "substring('12345',1 div 0, 1 div 0)"));
        }

        [TestMethod]
        public void RoundingFollowsXPathNotBankersRounding()
        {
            string xml = "<r/>";

            // 2.5 is the case that separates the two rules: XPath rounds it up to 3, whereas the framework's
            // default round-half-to-even would give 2.
            Assert.AreEqual(3.0, Number(xml, "round(2.5)"), "halves round towards positive infinity");
            Assert.AreEqual(2.0, Math.Round(2.5), "which is not what Math.Round does");
            Assert.AreEqual(4.0, Number(xml, "round(3.5)"));
            Assert.AreEqual(-2.0, Number(xml, "round(-2.5)"), "-2.5 rounds towards positive infinity, to -2");
            Assert.AreEqual(2.0, Number(xml, "floor(2.7)"));
            Assert.AreEqual(3.0, Number(xml, "ceiling(2.1)"));
        }

        [TestMethod]
        public void NodeSetFunctionsUseTheFirstNodeInDocumentOrder()
        {
            string xml = "<r xmlns:a=\"urn:x\"><a:first/><second/></r>";

            Assert.AreEqual(2.0, Number(xml, "count(/r/*)"));
            Assert.AreEqual("first", Text(xml, "local-name(/r/*)"));
            Assert.AreEqual("a:first", Text(xml, "name(/r/*)"));
            Assert.AreEqual("urn:x", Text(xml, "namespace-uri(/r/*)"));
            Assert.AreEqual(string.Empty, Text(xml, "local-name(/r/missing)"));
        }

        [TestMethod]
        public void SumAddsTheNumericValueOfEveryNode()
        {
            string xml = "<r><n>1</n><n>2</n><n>3.5</n></r>";

            Assert.AreEqual(6.5, Number(xml, "sum(/r/n)"));
            Assert.AreEqual(0.0, Number(xml, "sum(/r/missing)"));
            Assert.IsTrue(double.IsNaN(Number("<r><n>x</n></r>", "sum(/r/n)")));
        }

        [TestMethod]
        public void UnknownFunctionIsRejectedAtCompileTime()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Evaluate("<r/>", "no-such-function()"));
            StringAssert.Contains(error.Message, "no-such-function");

            XsltException arity = Assert.ThrowsExactly<XsltException>(() => Evaluate("<r/>", "count()"));
            StringAssert.Contains(arity.Message, "count");
        }

        // ---- Unions, filters and variables --------------------------------------------------------------

        [TestMethod]
        public void UnionReturnsDocumentOrderWithoutDuplicates()
        {
            string xml = "<r><a>1</a><b>2</b><c>3</c></r>";

            CollectionAssert.AreEqual(new[] { "1", "2" }, StringValues(xml, "/r/b | /r/a"));

            // Overlapping operands must not produce the same node twice.
            CollectionAssert.AreEqual(new[] { "1" }, StringValues(xml, "/r/a | /r/a"));
            Assert.AreEqual(3.0, Number(xml, "count(/r/a | /r/b | /r/c)"));
        }

        [TestMethod]
        public void FilterExpressionsApplyPredicatesToAParenthesisedResult()
        {
            string xml = "<r><a>1</a><b>2</b></r>";

            Assert.AreEqual("1", Text(xml, "(/r/a | /r/b)[1]"));
            Assert.AreEqual("2", Text(xml, "(/r/a | /r/b)[last()]"));
        }

        [TestMethod]
        public void VariablesResolveThroughTheStaticContext()
        {
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader("<r><a>7</a></r>"));
            XPathStaticContext staticContext = new XPathStaticContext();
            staticContext.DeclareGlobalVariable("factor", 0);

            Expr expression = XPathParser.Parse("/r/a * $factor", staticContext);
            int[] map = staticContext.Names.BuildFingerprintMap(tree);

            DynamicContext context = new DynamicContext(tree, XdmTree.RootNode, map)
            {
                Globals = new[] { XPathValue.FromNumber(6) },
            };

            Assert.AreEqual(42.0, expression.Evaluate(ref context).ToNumber());
        }

        [TestMethod]
        public void UnknownVariableIsRejectedAtCompileTime()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Evaluate("<r/>", "$nope"));
            StringAssert.Contains(error.Message, "nope");
        }

        // ---- Lexical disambiguation ---------------------------------------------------------------------

        [TestMethod]
        public void StarIsMultiplicationOnlyWhenItFollowsAnOperand()
        {
            string xml = "<r><a>3</a></r>";

            // After a number or a node-set, '*' multiplies.
            Assert.AreEqual(6.0, Number(xml, "2 * 3"));
            Assert.AreEqual(9.0, Number(xml, "/r/a * 3"));

            // At the start of a step, or after '(' or '[', it is a wildcard name test.
            Assert.AreEqual(1.0, Number(xml, "count(/r/*)"));
            Assert.AreEqual(1.0, Number(xml, "count((/r/*))"));
            Assert.AreEqual(1.0, Number(xml, "count(/r/a[*|.])"));
        }

        [TestMethod]
        public void AWildcardMayStandForThePrefixInsteadOfTheName()
        {
            // '*:local' is the other half of XPath 2.0's wildcard pair, and the useful half: it reaches into
            // a document whose namespace the stylesheet would otherwise have to declare a prefix for. Unlike
            // a bare name, which is in no namespace, it matches whatever namespace the node is in.
            string xml = "<r xmlns:a='urn:a' xmlns:b='urn:b'><a:x/><b:x/><x/><a:y/></r>";

            Assert.AreEqual(3.0, Number(xml, "count(/r/*:x)"));
            Assert.AreEqual(1.0, Number(xml, "count(/r/*:y)"));
            Assert.AreEqual(0.0, Number(xml, "count(/r/*:z)"));
            Assert.AreEqual(1.0, Number(xml, "count(/r/x)"));

            // Still multiplication where an operand precedes it, a colon being unable to follow a number.
            Assert.AreEqual(6.0, Number(xml, "2 * 3"));
        }

        [TestMethod]
        public void OperatorNamesCanAlsoBeElementNames()
        {
            // 'div', 'and', 'or' and 'mod' are only operators when an operator could appear at that point.
            string xml = "<r><div>1</div><and>2</and><mod>3</mod><or>4</or></r>";

            Assert.AreEqual("1", Text(xml, "/r/div"));
            Assert.AreEqual("2", Text(xml, "/r/and"));
            Assert.AreEqual("3", Text(xml, "/r/mod"));
            Assert.AreEqual("4", Text(xml, "/r/or"));

            // And they still work as operators in the same expression.
            Assert.AreEqual(0.5, Number(xml, "/r/div div 2"));
            Assert.AreEqual(1.0, Number(xml, "/r/mod mod 2"));
            Assert.IsTrue(Boolean(xml, "/r/div and /r/or"));
        }

        [TestMethod]
        public void AxisNamesAreDistinguishedFromElementNames()
        {
            // 'child' is an axis before '::' and an element name otherwise.
            string xml = "<r><child>value</child></r>";

            Assert.AreEqual("value", Text(xml, "/r/child"));
            Assert.AreEqual("value", Text(xml, "/r/child::child"));
            Assert.AreEqual(1.0, Number(xml, "count(/r/child::*)"));
        }

        [TestMethod]
        public void MalformedExpressionsAreReportedWithAnOffset()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Evaluate("<r/>", "/r/["));
            StringAssert.Contains(error.Message, "offset");
        }
    }
}
