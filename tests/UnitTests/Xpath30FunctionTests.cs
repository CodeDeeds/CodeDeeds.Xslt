using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the XPath 3.0 core-library additions and for the <c>math:</c> namespace.
    /// </summary>
    /// <remarks>
    /// Small functions, most of them a line each, and the reason they are worth having together is that they
    /// are the ones a 2.0 stylesheet had to write out by hand: <c>head()</c> for <c>[1]</c>,
    /// <c>tail()</c> for <c>position() gt 1</c>, <c>contains-token()</c> for the <c>concat(' ', …, ' ')</c>
    /// trick that everyone gets subtly wrong.
    /// </remarks>
    [TestClass]
    public sealed class Xpath30FunctionTests
    {
        private const string XslOnly = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private const string Xsl =
            XslOnly + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        /// <summary>The three namespaces 3.0 adds, which a stylesheet using them has to declare.</summary>
        private const string Libraries =
            "xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\""
            + " xmlns:array=\"http://www.w3.org/2005/xpath-functions/array\""
            + " xmlns:math=\"http://www.w3.org/2005/xpath-functions/math\"";

        /// <summary>Serves text from a dictionary, so the functions that read one have something to read.</summary>
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_resources = new(StringComparer.Ordinal);

            public MapResolver Add(string name, string content)
            {
                m_resources[name] = content;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_resources.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static string Writes(string expression, string input = "<r/>", IXsltResolver? documents = null)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {XslOnly} xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" {Libraries}"
                + " exclude-result-prefixes=\"xs map array math\">"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\" separator=\",\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                DocumentResolver = documents,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        /// <summary>
        /// Evaluates through a 2.0 stylesheet on a processor asked to be 2.0, where none of this exists. A 3.0
        /// processor, which this engine is unless told otherwise, gives a 2.0 stylesheet the 3.0 library.
        /// </summary>
        private static string RefusedByTwoPointZero(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V20 })
                    .TransformXml("<r/>"));

            return error.Code ?? string.Empty;
        }

        [TestMethod]
        public void TheseAreNotAvailableInATwoPointZeroStylesheet()
        {
            // A 2.0 stylesheet calling head() means an extension function of its own, and should be told the
            // name is unknown rather than quietly given this one.
            Assert.AreEqual("XPST0017", RefusedByTwoPointZero("head((1, 2))"));
            Assert.AreEqual("XPST0017", RefusedByTwoPointZero("contains-token('a b', 'a')"));

            // The math namespace is not bound there either, so the prefix is what is wrong first.
            Assert.AreEqual("XPST0081", RefusedByTwoPointZero("math:pi()"));
        }

        [TestMethod]
        public void ThePrefixesOfTheThreePointZeroLibrariesHaveToBeDeclared()
        {
            // XSLT declares no prefix for a stylesheet: the statically known namespaces are the ones in
            // scope where the expression is written and nothing besides. So the three namespaces 3.0 adds
            // are reached through declarations of their own, and a stylesheet that forgets one is told the
            // prefix is unbound rather than quietly given a library it may not have meant.
            Assert.AreEqual("3", Writes("map:size(map { 'a': 1, 'b': 2, 'c': 3 })"));
            Assert.AreEqual("2", Writes("array:size([1, 2])"));
            Assert.AreEqual("4", Writes("math:sqrt(16)"));

            Assert.AreEqual("XPST0081", RefusedByTwoPointZero("math:pi()"));
        }

        [TestMethod]
        public void AnExpressionMayCarryComments()
        {
            // XPath 2.0's only comment, and it goes wherever whitespace goes.
            Assert.AreEqual("3", Writes("1 (: one :) + (: plus :) 2"));
            Assert.AreEqual("3", Writes("(: leading :) 1 + 2 (: trailing :)"));
            Assert.AreEqual("2", Writes("count(: how many :)((1, 2))"));

            // It nests, which is what lets a stretch of expression that already had a comment in it be
            // commented out whole.
            Assert.AreEqual("3", Writes("1 + (: outer (: inner :) still outer :) 2"));

            // And a comment inside a string literal is text, not a comment.
            Assert.AreEqual("(: not a comment :)", Writes("'(: not a comment :)'"));

            Assert.AreEqual("XPST0003", Assert.ThrowsExactly<XsltException>(
                () => Writes("1 + (: never closed 2")).Code);
        }

        [TestMethod]
        public void LetNamesAValueAndEvaluatesItOnce()
        {
            Assert.AreEqual("3", Writes("let $x := 1 + 2 return $x"));
            Assert.AreEqual("6", Writes("let $a := 1, $b := $a + 1, $c := $b + 1 return $a + $b + $c"));

            // The opposite of 'for', which is what makes it worth having: 'for' runs its body once per item,
            // 'let' runs it once with the whole sequence bound.
            Assert.AreEqual("1,2,3", Writes("let $x := (1, 2, 3) return $x"));
            Assert.AreEqual("3", Writes("let $x := (1, 2, 3) return count($x)"));
            Assert.AreEqual("1,1,1", Writes("for $x in (1, 2, 3) return count($x)"));

            // A binding cannot see itself: the outer $x is what the inner one is bound from.
            Assert.AreEqual("2", Writes("let $x := 1 return let $x := $x + 1 return $x"));

            // And it nests inside the other bindings rather than beside them.
            Assert.AreEqual("2,4,6", Writes("for $n in 1 to 3 return let $d := $n * 2 return $d"));
        }

        [TestMethod]
        public void LetIsNotAvailableBelowThreePointZero()
        {
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("let $x := 1 return $x"));

            // 'let' is only a keyword before a variable, so an element may still be called it.
            Assert.AreEqual("v", Writes("/r/let", "<r><let>v</let></r>"));
        }

        [TestMethod]
        public void TheSimpleMapOperatorAppliesItsRightToEachItemOnTheLeft()
        {
            Assert.AreEqual("1,4,9", Writes("(1 to 3) ! (. * .)"));
            Assert.AreEqual("A,B", Writes("('a', 'b') ! upper-case(.)"));

            // Almost '/', and the differences are the point: a path insists on nodes, sorts into document
            // order and removes duplicates, and this does none of those.
            Assert.AreEqual("1,1,1", Writes("(1, 1, 1) ! ."));
            Assert.AreEqual("1,1,2", Writes("(1, 1, 2) ! ."));

            // A path cannot end in something that is not a node; this can.
            Assert.AreEqual("1,1", Writes("/r/a ! string-length(.)", "<r><a>x</a><a>y</a></r>"));

            // position() counts along the items on the left.
            Assert.AreEqual("1,2,3", Writes("('a', 'b', 'c') ! position()"));

            // It binds tighter than everything but a path, so this is (a ! b) + 1 and not a ! (b + 1).
            Assert.AreEqual("3", Writes("(1) ! 2 + 1"));
            Assert.AreEqual(string.Empty, Writes("() ! 1"));
        }

        [TestMethod]
        public void TheSimpleMapOperatorIsNotAvailableBelowThreePointZero()
        {
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("(1, 2) ! ."));
        }

        [TestMethod]
        public void TheConcatOperatorJoinsStrings()
        {
            Assert.AreEqual("abc", Writes("'ab' || 'c'"));
            Assert.AreEqual("abcd", Writes("'a' || 'b' || 'c' || 'd'"));

            // fn:concat written as an operator, so everything it does follows: values are atomized, and an
            // empty sequence is the empty string rather than nothing.
            Assert.AreEqual("12", Writes("1 || 2"));
            Assert.AreEqual("a", Writes("() || 'a'"));
            Assert.AreEqual("true", Writes("(() || ()) instance of xs:string"));
        }

        [TestMethod]
        public void TheConcatOperatorBindsTighterThanComparisonAndLooserThanArithmetic()
        {
            // Both of these change meaning if it sits anywhere else in the precedence chain: the first
            // would be '(12 || 34) - 50' and the second '("1234" eq 12) || 34'.
            Assert.AreEqual("12-16", Writes("12 || 34 - 50"));
            Assert.AreEqual("true", Writes("'1234' eq 12 || 34"));

            // A union is still a union, and two of them in a row was never valid, which is what lets the
            // scanner read '||' as one token wherever it appears.
            Assert.AreEqual("2", Writes("count(/r/a | /r/b)", "<r><a/><b/></r>"));
        }

        [TestMethod]
        public void TheConcatOperatorIsNotAvailableBelowThreePointZero()
        {
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("'a' || 'b'"));
        }

        [TestMethod]
        public void HeadAndTailTakeASequenceApart()
        {
            Assert.AreEqual("1", Writes("head(1 to 5)"));
            Assert.AreEqual("2,3,4,5", Writes("tail(1 to 5)"));
            Assert.AreEqual(string.Empty, Writes("head(())"));
            Assert.AreEqual(string.Empty, Writes("tail(())"));
            Assert.AreEqual(string.Empty, Writes("tail((1))"));

            // A node comes back as a node, not as its text, which is what lets head() start a path.
            Assert.AreEqual("a", Writes("name(head(/r/*))", "<r><a/><b/></r>"));
        }

        [TestMethod]
        public void ContainsTokenSearchesAWhitespaceSeparatedList()
        {
            Assert.AreEqual("true", Writes("contains-token('a b c', 'b')"));
            Assert.AreEqual("false", Writes("contains-token('a b c', 'd')"));

            // The trap it exists to close: a substring is not a token.
            Assert.AreEqual("false", Writes("contains-token('alpha beta', 'alph')"));

            // Any whitespace separates, and the token searched for is trimmed first.
            Assert.AreEqual("true", Writes("contains-token('a&#9;b&#10;c', 'c')"));
            Assert.AreEqual("true", Writes("contains-token('a b', '  b  ')"));

            // An empty token is in nothing, so contains-token(@class, ' ') is false rather than always true.
            Assert.AreEqual("false", Writes("contains-token('a b', ' ')"));
            Assert.AreEqual("false", Writes("contains-token((), 'a')"));

            // The input is a sequence, so a token found in any of the strings counts.
            Assert.AreEqual("true", Writes("contains-token(('a b', 'c d'), 'd')"));
        }

        [TestMethod]
        public void HasChildrenAsksWhetherThereIsAnythingBelow()
        {
            Assert.AreEqual("true", Writes("has-children(/r)", "<r><a/></r>"));
            Assert.AreEqual("false", Writes("has-children(/r/a)", "<r><a/></r>"));

            // Text counts as a child, an element having children rather than element children.
            Assert.AreEqual("true", Writes("has-children(/r/a)", "<r><a>text</a></r>"));
            Assert.AreEqual("false", Writes("has-children(())"));
        }

        private const string Nested = "<r><s><s><t/></s></s><s/></r>";

        [TestMethod]
        public void OutermostAndInnermostPickOneEndOfTheNesting()
        {
            // What both are for: a path has selected a subtree and its parts, and only one level is wanted.
            Assert.AreEqual("2", Writes("count(outermost(//s))", Nested));
            Assert.AreEqual("2", Writes("count(innermost(//s))", Nested));

            // The outer two are the direct children of r; the inner two are the deepest s and the empty one.
            Assert.AreEqual("2", Writes("count(outermost(//s)[parent::r])", Nested));
            Assert.AreEqual("1", Writes("count(innermost(//s)[t])", Nested));

            // Answered in document order however they were given, and with no repeats.
            Assert.AreEqual("a,b", Writes("outermost((/r/b, /r/a, /r/a))/name()", "<r><a/><b/></r>"));
        }

        [TestMethod]
        public void RoundTakesAPrecision()
        {
            Assert.AreEqual("2.5", Writes("round(2.4999, 2)"));
            Assert.AreEqual("35600", Writes("round(35612.25, -2)"));
            Assert.AreEqual("1.13", Writes("round(1.125, 2)"));

            // Halves go towards positive infinity, as they do without a precision — which is the rule
            // round-half-to-even() exists to escape.
            Assert.AreEqual("2.5", Writes("round(2.45, 1)"));
            Assert.AreEqual("-2.4", Writes("round(-2.45, 1)"));

            // An integer rounded to a whole number of digits is itself; to fewer, it moves.
            Assert.AreEqual("17", Writes("round(17, 2)"));
            Assert.AreEqual("20", Writes("round(17, -1)"));

            Assert.AreEqual(string.Empty, Writes("round((), 2)"));
            Assert.AreEqual("2", Writes("round(2.4999)"));
        }

        [TestMethod]
        public void StringJoinTakesOneArgumentInThreePointZero()
        {
            Assert.AreEqual("abc", Writes("string-join(('a', 'b', 'c'))"));
            Assert.AreEqual("a-b-c", Writes("string-join(('a', 'b', 'c'), '-')"));
            Assert.AreEqual(string.Empty, Writes("string-join(())"));
        }

        [TestMethod]
        public void UnparsedTextLinesSplitsOnAnyLineEnding()
        {
            MapResolver documents = new MapResolver()
                .Add("unix.txt", "a\nb\nc\n")
                .Add("windows.txt", "a\r\nb\r\nc")
                .Add("nothing.txt", string.Empty);

            // The trailing newline of a file is a terminator and not a separator, so three lines are three
            // strings whether or not the file ends in one.
            Assert.AreEqual("a,b,c", Writes("unparsed-text-lines('unix.txt')", documents: documents));
            Assert.AreEqual("a,b,c", Writes("unparsed-text-lines('windows.txt')", documents: documents));
            Assert.AreEqual(string.Empty, Writes("unparsed-text-lines('nothing.txt')", documents: documents));
            Assert.AreEqual("3", Writes("count(unparsed-text-lines('unix.txt'))", documents: documents));
        }

        [TestMethod]
        public void EnvironmentVariablesAreNotVisible()
        {
            // The specification lets a processor decide whether they are, and this one says no — the same
            // posture as the opt-in resolvers: nothing outside the transformation is reached unless the
            // caller opened the way.
            Assert.AreEqual(string.Empty, Writes("environment-variable('PATH')"));
            Assert.AreEqual("0", Writes("count(available-environment-variables())"));
        }

        /// <summary>
        /// Evaluates without a stylesheet around it, which is the only way to reach the core-library
        /// <c>fn:format-number()</c>: inside one, the stylesheet answers the call itself so that
        /// <c>xsl:decimal-format</c> can be honoured.
        /// </summary>
        private static string BareXPath(string expression)
        {
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader("<r/>"));
            XPathStaticContext staticContext = new XPathStaticContext { Version = XsltVersion.V30 };

            Expr compiled = XPathParser.Parse(expression, staticContext);
            int[] map = staticContext.Names.BuildFingerprintMap(tree);

            DynamicContext context = new DynamicContext(tree, XdmTree.RootNode, map);
            return compiled.Evaluate(ref context).ToStringValue();
        }

        [TestMethod]
        public void FormatNumberIsInTheCoreLibraryFromThreePointZero()
        {
            // XPath 3.0 moved it out of XSLT. A stylesheet still answers the call itself, because only the
            // stylesheet knows what xsl:decimal-format declared; this is what an expression outside one gets.
            Assert.AreEqual("1,234.57", BareXPath("format-number(1234.567, '#,##0.00')"));
            Assert.AreEqual("-42", BareXPath("format-number(-42, '0')"));
            Assert.AreEqual("48.57%", BareXPath("format-number(0.4857, '###.##%')"));
            Assert.AreEqual("NaN", BareXPath("format-number(xs:double('NaN'), '000')"));

            // The default symbols are the ones the specification names, there being nothing to declare
            // others with.
            Assert.AreEqual("0.5", BareXPath("format-number(0.5, '0.0')"));
        }

        [TestMethod]
        public void FormatNumberOutsideAStylesheetHasNoNamedFormats()
        {
            // Naming nothing is the only third argument that names something available: the empty sequence
            // and the empty string both mean the default format.
            Assert.AreEqual("48.57%", BareXPath("format-number(0.4857, '###.##%', ())"));
            Assert.AreEqual("48.57%", BareXPath("format-number(0.4857, '###.##%', '')"));

            // Anything else is checked where it is evaluated, because a computed name is allowed and
            // nothing says a computed one has to be wrong before it is worked out.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => BareXPath("format-number(1, '0', 'swiss')"));

            Assert.AreEqual("FODF1280", error.Code);
        }

        [TestMethod]
        public void AStylesheetStillAnswersFormatNumberItself()
        {
            // The host gets first refusal, so xsl:decimal-format keeps working and the core-library version
            // is what an expression reaches only when there is no stylesheet to ask.
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + "<xsl:decimal-format decimal-separator=\",\" grouping-separator=\".\"/>"
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"format-number(1234.5, '#.##0,00')\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>1.234,50</out>",
                new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }

        /// <summary>Copies what an expression selects into the result, so a built tree can be seen.</summary>
        private static string Copies(string expression, string input = "<r/>")
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><xsl:copy-of select=\"{expression}\"/>"
                + "</xsl:template></xsl:stylesheet>";

            return new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml(input);
        }

        private const string Fn = " xmlns=\"http://www.w3.org/2005/xpath-functions\"";

        [TestMethod]
        public void ParseXmlMakesNodesOutOfText()
        {
            Assert.AreEqual("<a><b/></a>", Copies("parse-xml('&lt;a&gt;&lt;b/&gt;&lt;/a&gt;')"));
            Assert.AreEqual("b", Writes("name(parse-xml('&lt;a&gt;&lt;b/&gt;&lt;/a&gt;')/a/b)"));
            Assert.AreEqual("text", Writes("parse-xml('&lt;a&gt;text&lt;/a&gt;')"));

            Assert.AreEqual(string.Empty, Writes("parse-xml(())"));
            Assert.AreEqual("FODC0006", Assert.ThrowsExactly<XsltException>(
                () => Writes("parse-xml('&lt;a&gt;')")).Code);
        }

        [TestMethod]
        public void ParseXmlFragmentTakesWhatIsNotAWholeDocument()
        {
            // A fragment is what an external parsed entity may be: several top-level elements, or text with
            // no element at all. Neither is a document, and parse-xml() refuses both.
            Assert.AreEqual("<a/><b/>", Copies("parse-xml-fragment('&lt;a/&gt;&lt;b/&gt;')"));
            Assert.AreEqual("just text", Copies("parse-xml-fragment('just text')"));
            Assert.AreEqual("2", Writes("count(parse-xml-fragment('&lt;a/&gt;&lt;b/&gt;')/*)"));

            Assert.AreEqual("FODC0006", Assert.ThrowsExactly<XsltException>(
                () => Writes("parse-xml('&lt;a/&gt;&lt;b/&gt;')")).Code);
        }

        [TestMethod]
        public void AnalyzeStringSplitsTextIntoWhatMatchedAndWhatDidNot()
        {
            Assert.AreEqual(
                $"<analyze-string-result{Fn}><non-match>a</non-match><match>1</match>"
                + "<non-match>b</non-match><match>2</match></analyze-string-result>",
                Copies("analyze-string('a1b2', '[0-9]')"));

            // Nothing matching is one non-match, and nothing at all is an empty result element.
            Assert.AreEqual(
                $"<analyze-string-result{Fn}><non-match>abc</non-match></analyze-string-result>",
                Copies("analyze-string('abc', '[0-9]')"));

            Assert.AreEqual(
                $"<analyze-string-result{Fn}/>",
                Copies("analyze-string('', '[0-9]')"));

            Assert.AreEqual(
                "2", Writes("count(analyze-string('a1b2', '[0-9]')/*[local-name() = 'match'])"));
        }

        [TestMethod]
        public void AnalyzeStringMarksTheCapturingGroups()
        {
            Assert.AreEqual(
                $"<analyze-string-result{Fn}><match><group nr=\"1\">a</group>"
                + "<group nr=\"2\">1</group></match></analyze-string-result>",
                Copies("analyze-string('a1', '([a-z])([0-9])')"));

            // Nested in the pattern, nested in the result — which has to be rebuilt from where each group
            // landed, a match reporting nothing about which group is inside which.
            Assert.AreEqual(
                $"<analyze-string-result{Fn}><match><group nr=\"1\">ab<group nr=\"2\">c</group></group>"
                + "</match></analyze-string-result>",
                Copies("analyze-string('abc', '(ab(c))')"));

            // A group that did not participate contributes nothing, which is what separates (a)|(b) matching
            // 'a' from it matching an empty 'b' as well.
            Assert.AreEqual(
                $"<analyze-string-result{Fn}><match><group nr=\"1\">a</group></match></analyze-string-result>",
                Copies("analyze-string('a', '([a-z])|([0-9])')"));

            // A pattern that occupies no width would never move past the first position.
            Assert.AreEqual("FORX0003", Assert.ThrowsExactly<XsltException>(
                () => Writes("analyze-string('abc', 'x*')")).Code);
        }

        [TestMethod]
        public void TheMathFunctionsAreTheOnesTheNamespaceNames()
        {
            Assert.AreEqual("3.141592653589793", Writes("math:pi()"));
            Assert.AreEqual("1", Writes("math:exp(0)"));
            Assert.AreEqual("100", Writes("math:exp10(2)"));
            Assert.AreEqual("0", Writes("math:log(1)"));
            Assert.AreEqual("3", Writes("math:log10(1000)"));
            Assert.AreEqual("1.4142135623730951", Writes("math:sqrt(2)"));
            Assert.AreEqual("1024", Writes("math:pow(2, 10)"));
            Assert.AreEqual("0.25", Writes("math:pow(2, -2)"));
            Assert.AreEqual("0", Writes("math:sin(0)"));
            Assert.AreEqual("1", Writes("math:cos(0)"));
            Assert.AreEqual("0", Writes("math:tan(0)"));
            Assert.AreEqual("0", Writes("math:asin(0)"));
            Assert.AreEqual("0", Writes("math:atan(0)"));
            Assert.AreEqual("1.5707963267948966", Writes("math:acos(0)"));

            // atan2 takes y before x, which is the order the mathematics is written in and the opposite of
            // what the name suggests to anyone reading it as "the angle of x and y".
            Assert.AreEqual("1.5707963267948966", Writes("math:atan2(1, 0)"));
            Assert.AreEqual("0", Writes("math:atan2(0, 1)"));
        }

        [TestMethod]
        public void TheMathFunctionsAnswerNothingForNothing()
        {
            // Declared xs:double? -> xs:double?, so an absent value stays absent rather than becoming NaN.
            Assert.AreEqual(string.Empty, Writes("math:sqrt(())"));
            Assert.AreEqual(string.Empty, Writes("math:exp(())"));
            Assert.AreEqual(string.Empty, Writes("math:pow((), 2)"));

            // And the edges answer what IEEE says rather than raising.
            Assert.AreEqual("NaN", Writes("math:sqrt(-1)"));
            Assert.AreEqual("-INF", Writes("math:log(0)"));
            Assert.AreEqual("INF", Writes("math:exp(1e9)"));
            Assert.AreEqual("1", Writes("math:pow(2, 0)"));

            Assert.AreEqual("XPST0017", Assert.ThrowsExactly<XsltException>(
                () => Writes("math:nonesuch(1)")).Code);
        }

        // ---- what XPath 3.1 widened ------------------------------------------------------------------------

        [TestMethod]
        public void AnArrayAtomizesToItsMembers()
        {
            // XPath 3.0 refused this and 3.1 allows it, so the count changes: an array contributes as many
            // values as it holds, and a nested one as many as its own members atomize to.
            Assert.AreEqual("6", Writes("sum([1, 2, 3])"));
            Assert.AreEqual("10", Writes("sum([[1, 2], [3, 4]])"));
            Assert.AreEqual("5", Writes("max([3, 4, 5])"));
            Assert.AreEqual("3", Writes("min([3, 4, 5])"));
            Assert.AreEqual("3,4", Writes("index-of([1, [5, 6], [6, 7]], 6)"));

            // A map has no typed value even so: an array is a list of things and a map is not.
            Assert.AreEqual("FOTY0013", Assert.ThrowsExactly<XsltException>(
                () => Writes("data(map { 'a': 1 })")).Code);
        }

        [TestMethod]
        public void TheUnaryLookupAsksTheContextItem()
        {
            // Which is what makes it useful in a predicate: '?1' asks each array in the sequence in turn.
            Assert.AreEqual("c,d", Writes("(['a', 'b'], ['c', 'd'])[?1 eq 'c']?*"));
            Assert.AreEqual("1,2", Writes("(map{'a':1}, map{'a':2})[?a] ! ?a"));
            Assert.AreEqual("a,b,c", Writes("['a', 'b', 'c'] ! ?*"));

            // A key written as a decimal names nothing: the grammar asks for digits there.
            Assert.AreEqual("XPST0003", Assert.ThrowsExactly<XsltException>(
                () => Writes("['a'][?1.0 = 'a']")).Code);

            // '?()' looks up nothing, which is an empty result rather than a malformed lookup.
            Assert.AreEqual("0", Writes("count(['a', 'b'] ! ?())"));
        }

        [TestMethod]
        public void ANameMayCarryItsNamespaceInsteadOfAPrefix()
        {
            // 'Q{uri}local' reaches a namespace nothing declared a prefix for, and goes wherever a name
            // goes: a step, an attribute, a variable, a function.
            Assert.AreEqual("4", Writes("Q{http://www.w3.org/2005/xpath-functions/math}sqrt(16)"));
            Assert.AreEqual("2", Writes("Q{http://www.w3.org/2005/xpath-functions}count((1, 2))"));
            Assert.AreEqual("v", Writes("/Q{urn:x}r/Q{urn:x}a", "<r xmlns='urn:x'><a>v</a></r>"));
            Assert.AreEqual("1", Writes("/r/@Q{urn:x}k", "<r xmlns:p='urn:x' p:k='1'/>"));
            Assert.AreEqual("2,3", Writes("for $Q{urn:x}n in (1, 2) return $Q{urn:x}n + 1"));

            // The URI is whitespace-collapsed, being an xs:anyURI: trimmed at the ends, and each run inside
            // reduced to one space.
            Assert.AreEqual("2,4", Writes("for $Q{ urn:a b }n in (1, 2) return $Q{urn:a   b}n * 2"));

            // Nothing else about it is read, so a per-cent escape is a character the URI contains.
            Assert.AreEqual("25", Writes("let $Q{a%20b}x := 12, $Q{a b}x := 13 return $Q{a%20b}x + $Q{ a b }x"));
        }

        [TestMethod]
        public void ABracedNameIsNotWrittenEverywhereANameIs()
        {
            // A lookup key is an NCName, a name with no namespace at all — so 'Q{}x' is not one, however
            // empty its namespace is.
            Assert.AreEqual("XPST0003", Assert.ThrowsExactly<XsltException>(
                () => Writes("map{'a':1} ? Q{}a")).Code);

            // And a URI holds no brace of its own.
            Assert.AreEqual("XPST0003", Assert.ThrowsExactly<XsltException>(
                () => Writes("Q{{urn:x}name()")).Code);
        }

        [TestMethod]
        public void AnArrayTypeMaySayWhatItHolds()
        {
            // Each member is matched as a whole sequence, so a member of two strings is an array(xs:string*)
            // and not an array(xs:string).
            Assert.AreEqual("true", Writes("['A', 'B'] instance of array(xs:string)"));
            Assert.AreEqual("true", Writes("[('A', 'B')] instance of array(xs:string*)"));
            Assert.AreEqual("false", Writes("[('A', 'B'), 'C'] instance of array(xs:string)"));
            Assert.AreEqual("false", Writes("[(), 'A'] instance of array(xs:string)"));
            Assert.AreEqual("true", Writes("[['A'], ['B']] instance of array(array(xs:string))"));
            Assert.AreEqual("true", Writes("['A', 'B'] instance of array(*)"));

        }

        [TestMethod]
        public void AMapTypeAsksOfEveryEntry()
        {
            // An empty map satisfies every map type, having no entry to fail one.
            Assert.AreEqual("true", Writes("map{} instance of map(xs:integer, xs:string)"));
            Assert.AreEqual("true", Writes("map{1:'London'} instance of map(xs:integer, xs:string)"));
            Assert.AreEqual(
                "false", Writes("map{1:'London', 'London':1} instance of map(xs:integer, xs:string)"));

            // The key is matched as an item and the value as a whole sequence, so the occurrence indicator
            // on the value half decides whether an absent value and a pair of them are admitted.
            Assert.AreEqual("true", Writes("map{'a':()} instance of map(xs:string, xs:integer?)"));
            Assert.AreEqual("false", Writes("map{'a':()} instance of map(xs:string, xs:integer+)"));
            Assert.AreEqual("true", Writes("map{'a':(1, 2)} instance of map(xs:string, xs:integer+)"));
            Assert.AreEqual("true", Writes("map{'a':()} instance of map(xs:string, empty-sequence())"));

            // An integer is an xs:decimal by derivation, which the key half honours as any other test does.
            Assert.AreEqual("true", Writes("map{1:'a'} instance of map(xs:decimal, xs:string)"));

            // A key is one item, so its type takes no occurrence indicator; and the type needs both halves.
            Assert.AreEqual("XPST0003", Assert.ThrowsExactly<XsltException>(
                () => Writes("map{} instance of map(xs:string+, xs:integer)")).Code);
            Assert.AreEqual("XPST0003", Assert.ThrowsExactly<XsltException>(
                () => Writes("map{} instance of map(xs:string)")).Code);

            // An unprefixed name is not in the schema namespace, so it names no type this engine knows.
            Assert.AreEqual("XPST0051", Assert.ThrowsExactly<XsltException>(
                () => Writes("map{} instance of map(integer, string)")).Code);
        }

        [TestMethod]
        public void GenerateIdIsInTheFunctionLibraryFromThirty()
        {
            // The identifier has to be an XML name, so it cannot start with a digit.
            Assert.AreEqual("true", Writes("matches(generate-id(/*), '^[A-Za-z][A-Za-z0-9]*$')", Two));

            // Two references to one node agree, two nodes do not, and the empty sequence has no identity.
            Assert.AreEqual("true", Writes("generate-id(/*) eq generate-id(/*)", Two));
            Assert.AreEqual("false", Writes("generate-id(/*) eq generate-id((//b)[1])", Two));
            Assert.AreEqual("", Writes("generate-id(())"));

            // An attribute is a node like any other, addressed in its own id space.
            Assert.AreEqual("false", Writes("generate-id((//@*)[1]) eq generate-id(/*)", Two));

            // 3.0 declares the argument node()?, so two nodes are refused rather than the first taken.
            Assert.AreEqual("XPTY0004", Assert.ThrowsExactly<XsltException>(
                () => Writes("generate-id(//*)", Two)).Code);
            Assert.AreEqual("XPTY0004", Assert.ThrowsExactly<XsltException>(
                () => Writes("generate-id(1)")).Code);
        }

        [TestMethod]
        public void NothingIsAnErrorBecauseNothingIsAnXsError()
        {
            // The type has an empty value space, so no value is an instance of it and none casts to it.
            Assert.AreEqual("false", Writes("1 instance of xs:error"));
            Assert.AreEqual("false", Writes("'' instance of xs:error"));
            Assert.AreEqual("false", Writes("1 castable as xs:error"));

            // The empty sequence is not a value, so a type admitting it is satisfied by it.
            Assert.AreEqual("true", Writes("() instance of xs:error?"));
            Assert.AreEqual("true", Writes("() instance of xs:error*"));
            Assert.AreEqual("true", Writes("() castable as xs:error?"));
            Assert.AreEqual("", Writes("() cast as xs:error?"));
            Assert.AreEqual("", Writes("() treat as xs:error?"));

            // Casting to it is FORG0001 and not XPTY0004: the complaint is that the value falls outside the
            // type, which is what that code says, rather than that the two types have no route between them.
            Assert.AreEqual("FORG0001", Assert.ThrowsExactly<XsltException>(
                () => Writes("1 cast as xs:error")).Code);
            Assert.AreEqual("FORG0001", Assert.ThrowsExactly<XsltException>(
                () => Writes("xs:error(1)")).Code);

            // A treat-as that fails is a dynamic error rather than a false answer, this being an assertion.
            Assert.AreEqual("XPDY0050", Assert.ThrowsExactly<XsltException>(
                () => Writes("1 treat as xs:error")).Code);
        }

        [TestMethod]
        public void ValuesThatCannotBeComparedAreDistinctRatherThanAnError()
        {
            // Both functions look for equal values, and ask 'eq' to decide. Where eq is not defined between
            // two types the answer is that they differ, not that the question was wrong: a date is not a
            // string, so a date is not equal to one.
            Assert.AreEqual("false", Writes("deep-equal(xs:date('1993-03-31'), '1993-03-31')"));
            Assert.AreEqual("false", Writes("deep-equal((true(), 2, 3), (1, 2, 3))"));
            Assert.AreEqual("", Writes("index-of(('a', 1, 'c'), xs:date('1993-03-31'))"));

            // eq and not the general comparison, so an untyped value is read as a string rather than as
            // whatever it is held up against — however much 'P1Y' looks like a duration.
            Assert.AreEqual(
                "false",
                Writes("deep-equal(xs:untypedAtomic('P1Y'), xs:yearMonthDuration('P12M'))"));

            // And where eq is defined, it is eq that decides: a decimal and a float promote to a float, so
            // these agree where comparing both as doubles would have them differ.
            Assert.AreEqual("true", Writes("deep-equal(xs:decimal(1.01), xs:float(1.01))"));
        }

        [TestMethod]
        public void OnlyDeepEqualHasNaNAgreeWithNaN()
        {
            // The one place the two part company. deep-equal asks whether two sequences are the same, and a
            // sequence holding NaN is the same as itself...
            Assert.AreEqual("true", Writes("deep-equal(xs:double('NaN'), xs:double('NaN'))"));
            Assert.AreEqual("true", Writes("deep-equal(xs:float('NaN'), xs:double('NaN'))"));

            // ...where index-of asks eq outright, which no NaN satisfies — not even the one it was given.
            Assert.AreEqual("", Writes("index-of(xs:double('NaN'), xs:double('NaN'))"));
            Assert.AreEqual("", Writes("index-of(xs:float('NaN'), xs:float('NaN'))"));
        }

        /// <summary>A document with two elements and an attribute, for asking about node identity.</summary>
        private const string Two = "<r x='1'><b/></r>";

        [TestMethod]
        public void EveryAtomicTypeIsDerivedFromAnyAtomicType()
        {
            Assert.AreEqual("true", Writes("false() instance of xs:anyAtomicType"));
            Assert.AreEqual("true", Writes("(1, 2, 'a string') instance of xs:anyAtomicType*"));
            Assert.AreEqual("true", Writes("xs:date('2026-08-23') instance of xs:anyAtomicType"));

            // A node is an item and is not an atomic value, and neither is a function.
            Assert.AreEqual("false", Writes("/r instance of xs:anyAtomicType"));
            Assert.AreEqual("false", Writes("map{} instance of xs:anyAtomicType"));
        }

        [TestMethod]
        public void StringJoinTakesAnythingAtomicAndTokenizeTakesOneArgument()
        {
            // 3.1 widened string-join's first parameter from xs:string* to xs:anyAtomicType*, and gave
            // tokenize a one-argument form that splits on whitespace.
            Assert.AreEqual("123456789", Writes("string-join(1 to 9, '')"));
            Assert.AreEqual("1, 2, 3", Writes("string-join(1 to 3, ', ')"));

            Assert.AreEqual("red|green|blue", Writes("string-join(tokenize(' red  green blue '), '|')"));
            Assert.AreEqual("0", Writes("count(tokenize('   '))"));
        }

        [TestMethod]
        public void TheDefaultLanguageIsTheOneTheFormattersWouldHaveUsed()
        {
            // The specification leaves the default language to the implementation, and this one settles it
            // at English. The point of the function is that a caller can pass on what would have been used
            // anyway, so asking for it by name has to answer what leaving it out answers.
            Assert.AreEqual("en", Writes("default-language()"));
            Assert.AreEqual(
                "true", Writes("format-integer(17, 'Ww') eq format-integer(17, 'Ww', default-language())"));
            Assert.AreEqual(
                "true",
                Writes(
                    "format-date(xs:date('2026-08-23'), '[MNn]')"
                    + " eq format-date(xs:date('2026-08-23'), '[MNn]', default-language(), (), ())"));

            // It is an xs:language rather than a bare string, which is the type the signature declares.
            Assert.AreEqual("true", Writes("default-language() instance of xs:language"));
        }
    }
}
