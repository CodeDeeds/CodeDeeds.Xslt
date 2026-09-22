using System.Runtime.ExceptionServices;
using System.Text;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that the shape of a stylesheet, as distinct from its input, cannot end the process: a run of
    /// thousands of one operator, a stylesheet nested thousands of elements deep, a pattern of thousands of
    /// steps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stack overflow cannot be caught and takes the test host with it, so every case here was run in a
    /// process of its own first, on the stacks it is run on here. Where a case is meant to be refused, it
    /// is compiled on a thread with a large stack and run on one with a small stack, or the other way
    /// about, so that which guard answers does not depend on what the host gives a test.
    /// </para>
    /// <para>
    /// A run of one operator is built flat by the parser once it is longer than a few dozen operands, and
    /// <c>or</c>, <c>and</c> and <c>|</c> as a balanced tree, so those have no depth to refuse and the
    /// tests hold them to the answer. Nesting that means something — element inside element, a pattern
    /// step that climbs — cannot be laid flat, and is compiled, run and matched on a new stack where the
    /// one in use runs short, as a deep template recursion is; the tests hold those to the answer on a
    /// small stack. The clauses of one <c>for</c> are the one nesting that is neither, and is refused
    /// past a count.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class StylesheetShapeTests
    {
        private const int Long = 20000;

        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\""
            + " xmlns:f=\"urn:f\" xmlns:err=\"http://www.w3.org/2005/xqt-errors\" exclude-result-prefixes=\"#all\">";

        private static string Sheet(string body, string version = "3.0")
        {
            return Head.Replace("version=\"3.0\"", "version=\"" + version + "\"") + body + "</xsl:stylesheet>";
        }

        private static string Selecting(string expression, string version = "3.0")
        {
            return Sheet(
                "<xsl:template match=\"/\"><out><xsl:value-of select=\"" + expression + "\"/></out></xsl:template>",
                version);
        }

        private static string Repeat(string unit, string separator, int count)
        {
            return string.Join(separator, Enumerable.Repeat(unit, count));
        }

        private static string Nested(string open, string close, string innermost, int depth)
        {
            return string.Concat(Enumerable.Repeat(open, depth)) + innermost + string.Concat(Enumerable.Repeat(close, depth));
        }

        /// <summary>Runs a stylesheet on both backends over <c>&lt;r&gt;5&lt;/r&gt;</c>, and requires one answer of them.</summary>
        private static string Run(string stylesheet, string input = "<r>5</r>")
        {
            string interpreted = Transform(stylesheet, input, XsltBackend.Interpreted);
            string compiled = Transform(stylesheet, input, XsltBackend.Compiled);

            Assert.AreEqual(interpreted, compiled, "The compiled backend disagreed with the interpreter.");
            return interpreted;
        }

        private static string Transform(string stylesheet, string input, XsltBackend backend)
        {
            return new Xslt(stylesheet, new XsltOptions { Backend = backend, OmitXmlDeclaration = true })
                .TransformXml(input);
        }

        /// <summary>Runs something on a thread with a stack of a given size, and returns what it returned.</summary>
        private static T OnAStackOf<T>(int kilobytes, Func<T> work)
        {
            T result = default!;
            Exception? failure = null;

            Thread thread = new Thread(
                () =>
                {
                    try
                    {
                        result = work();
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                },
                kilobytes * 1024);

            thread.Start();
            thread.Join();

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return result;
        }

        // ---- Runs of one operator: built flat, answered at any length ----------------------------------

        [TestMethod]
        public void ArithmeticChainsOfAnyLengthAreEvaluated()
        {
            Assert.AreEqual("<out>" + Long + "</out>", Run(Selecting(Repeat("1", "+", Long))));
            Assert.AreEqual("<out>" + (2 - Long) + "</out>", Run(Selecting(Repeat("1", "-", Long))));
            Assert.AreEqual("<out>1</out>", Run(Selecting(Repeat("1", "*", Long))));
            Assert.AreEqual("<out>" + Long + "</out>", Run(Selecting(Repeat("1", "+", Long), "1.0")));
            Assert.AreEqual("<out>" + (5 * Long) + "</out>", Run(Selecting(Repeat("r", "+", Long))));

            // Precedence is still the grammar's: each level of it is a run of its own, and a run of
            // multiplications is an operand of the run of additions.
            Assert.AreEqual("<out>" + (7 * 100) + "</out>", Run(Selecting(Repeat("1+2*3", "+", 100))));

            // Subtraction is not associative, which is what a run laid flat has to keep: 1-1-1 is -1.
            Assert.AreEqual("<out>-98</out>", Run(Selecting(Repeat("1", "-", 100))));
            Assert.AreEqual("<out>-38000</out>", Run(Selecting(Repeat("1000", " - ", 40))));
        }

        [TestMethod]
        public void LogicalChainsOfAnyLengthAreEvaluated()
        {
            Assert.AreEqual("<out>false</out>", Run(Selecting(Repeat("false()", " or ", Long))));
            Assert.AreEqual("<out>true</out>", Run(Selecting(Repeat("true()", " and ", Long))));
            Assert.AreEqual("<out>true</out>", Run(Selecting(Repeat("false()", " or ", Long) + " or true()")));
            Assert.AreEqual(
                "<out>yes</out>",
                Run(Sheet("<xsl:template match=\"/\"><out><xsl:if test=\"" + Repeat("false()", " or ", Long)
                    + " or r = 5\">yes</xsl:if></out></xsl:template>")));

            // In a predicate, on both backends: the compiled one emits a balanced tree of the same
            // operators, and leaves an expression too large for one method to the interpreter.
            Assert.AreEqual(
                "<out>1</out>",
                Run(Selecting("count(r[" + Repeat(". = 'zz'", " or ", Long) + " or . = 5])")));
        }

        [TestMethod]
        public void ALongLogicalChainStillStopsWhereItAlwaysStopped()
        {
            // Regrouped as a balanced tree, the operands are still reached left to right and the chain
            // still stops at the first operand that settles it: the error further along is never raised.
            Assert.AreEqual(
                "<out>true</out>",
                Run(Selecting(Repeat("false()", " or ", 40) + " or true() or " + Repeat("error()", " or ", 40))));

            Assert.AreEqual(
                "<out>false</out>",
                Run(Selecting(Repeat("true()", " and ", 40) + " and false() and " + Repeat("error()", " and ", 40))));

            // And where nothing settles it, the first error is the one raised.
            Assert.AreEqual(
                "<out>first</out>",
                Run(Sheet("<xsl:template match=\"/\"><out><xsl:try select=\""
                    + Repeat("false()", " or ", 40) + " or error(xs:QName('f:first')) or error(xs:QName('f:second'))\">"
                    + "<xsl:catch select=\"local-name-from-QName($err:code)\"/></xsl:try></out></xsl:template>")));
        }

        [TestMethod]
        public void NodeChainsOfAnyLengthAreEvaluated()
        {
            Assert.AreEqual("<out>1</out>", Run(Selecting("count(" + Repeat("r", "|", Long) + ")")));
            Assert.AreEqual("<out>1</out>", Run(Selecting("count(" + Repeat("r", " intersect ", Long) + ")")));
            Assert.AreEqual("<out>0</out>", Run(Selecting("count(" + Repeat("r", " except ", Long) + ")")));
            Assert.AreEqual("<out>1</out>", Run(Selecting("count(" + Repeat(".", "/", Long) + ")")));
            Assert.AreEqual("<out>5</out>", Run(Selecting(Repeat(".", "!", Long) + "! 5")));

            // Steps that are expressions, between steps that are axes: what stands between two of them
            // is one path, and the run is of the expressions.
            Assert.AreEqual("<out>5</out>", Run(Selecting("r/" + Repeat("(.)/self::r", "/", Long))));
        }

        [TestMethod]
        public void PostfixChainsOfAnyLengthAreEvaluated()
        {
            Assert.AreEqual("<out>A</out>", Run(Selecting("'a'" + string.Concat(Enumerable.Repeat(" => upper-case()", Long)))));

            // A map in a map, built by a fold with no nesting in the stylesheet, and read by a run of
            // lookups as long as it is deep.
            Assert.AreEqual(
                "<out>1</out>",
                Run(Selecting("fold-left(1 to " + Long + ", map{'k':1}, function($a, $i) { map{'k':$a} })"
                    + string.Concat(Enumerable.Repeat("?k", Long + 1)))));

            // A dynamic call on what the last call gave, thousands of times over: each call answers a
            // function holding the count so far, and the last is asked for it.
            Assert.AreEqual(
                "<out>" + Long + "</out>",
                Run(Selecting("let $mk := function($mk, $n) { function($m) { if ($m = -1) then $n else $mk($mk, $n + $m) } } "
                    + "return $mk($mk, 0)" + string.Concat(Enumerable.Repeat("(1)", Long)) + "(-1)")));
        }

        [TestMethod]
        public void ALongLetIsOneExpression()
        {
            string bindings = string.Join(", ", Enumerable.Range(1, Long).Select(i => "$v" + i + " := " + i));
            Assert.AreEqual("<out>" + Long + "</out>", Run(Selecting("let " + bindings + " return $v" + Long)));

            // Each binding sees the ones before it, and the body sees them all.
            string running = string.Join(", ", Enumerable.Range(1, 100).Select(i => i == 1 ? "$v1 := 1" : "$v" + i + " := $v" + (i - 1) + " + 1"));
            Assert.AreEqual("<out>100 5050</out>", Run(Selecting("let " + running + " return ($v100, "
                + string.Join("+", Enumerable.Range(1, 100).Select(i => "$v" + i)) + ")")));

            // A let inside a function called recursively, so that the slots are re-entered while in use.
            Assert.AreEqual(
                "<out>10</out>",
                Run(Selecting("let $f := function($f, $n) { if ($n = 0) then 0 else let "
                    + string.Join(", ", Enumerable.Range(1, 40).Select(i => "$a" + i + " := $n")) + " return $f($f, $n - 1) + $a40 - $a1 + 1 } "
                    + "return $f($f, 10)")));
        }

        [TestMethod]
        public void ARunInsideARunTakesTheSlotAfterIt()
        {
            // A run keeps the value so far in a range-variable slot of its own. An operand that is itself
            // a long run, or that binds a variable, takes a slot after it and not the same one.
            Assert.AreEqual(
                "<out>" + (40 * 40) + "</out>",
                Run(Selecting(Repeat("(" + Repeat("1", "+", 40) + ")", "+", 40))));

            Assert.AreEqual(
                "<out>" + (40 * 41) + "</out>",
                Run(Selecting(Repeat("(let $x := 41 return $x)", "+", 40))));

            Assert.AreEqual(
                "<out>" + (40 * 3) + "</out>",
                Run(Selecting(Repeat("sum(for $i in 1 to 2 return $i)", "+", 40))));
        }


        // ---- Nesting that means something: carried on upon a new stack, or refused past a count -------

        [TestMethod]
        public void TooManyClausesOfOneForAreRefused()
        {
            string clauses(int count) => string.Join(", ", Enumerable.Range(1, count).Select(i => "$v" + i + " in 1"));

            Assert.AreEqual("<out>1</out>", Run(Selecting("for " + clauses(256) + " return $v1")));
            Assert.AreEqual("<out>true</out>", Run(Selecting("some " + clauses(256) + " satisfies $v256 = 1")));

            foreach (string keyword in new[] { "for", "some", "every" })
            {
                string body = keyword == "for" ? " return $v1" : " satisfies $v1 = 1";
                XsltException refused = Assert.ThrowsExactly<XsltException>(
                    () => Run(Selecting(keyword + " " + clauses(257) + body)));

                Assert.AreEqual("XPST0003", refused.Code);
                StringAssert.Contains(refused.Message, "More than 256 variables");
            }
        }

        [TestMethod]
        public void ADeeplyNestedStylesheetIsCompiledAndRunOnAnyStack()
        {
            // Compiled on a small stack and run on a small stack: what a stylesheet is compiled on and what
            // it is run on may be any thread's, and neither is a limit on its nesting. Each of these
            // overflowed a megabyte of stack a few thousand levels in, on either side.
            foreach ((string open, string close, string expected) in new[]
            {
                ("<a>", "</a>", "<out>" + Nested("<a>", "</a>", "x", 3000) + "</out>"),
                ("<xsl:if test=\"true()\">", "</xsl:if>", "<out>x</out>"),
                ("<xsl:for-each select=\".\">", "</xsl:for-each>", "<out>x</out>"),
                ("<xsl:choose><xsl:when test=\"false()\"/><xsl:otherwise>", "</xsl:otherwise></xsl:choose>", "<out>x</out>"),
                ("<xsl:element name=\"a\">", "</xsl:element>", "<out>" + Nested("<a>", "</a>", "x", 3000) + "</out>"),
                ("<xsl:copy>", "</xsl:copy>", "<out>x</out>"),
                ("<xsl:variable name=\"v\">", "</xsl:variable><xsl:copy-of select=\"$v\"/>", "<out>x</out>"),
                ("<xsl:try>", "<xsl:catch/></xsl:try>", "<out>x</out>"),
            })
            {
                string stylesheet = Sheet("<xsl:template match=\"/\"><out>" + Nested(open, close, "x", 3000) + "</out></xsl:template>");

                foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                {
                    Xslt compiled = OnAStackOf(256, () => new Xslt(stylesheet, new XsltOptions { Backend = backend, OmitXmlDeclaration = true }));
                    Assert.AreEqual(expected, OnAStackOf(256, () => compiled.TransformXml("<r/>")), open);
                }
            }
        }

        [TestMethod]
        public void AnErrorRaisedOnANewStackIsRaisedAsItself()
        {
            // What goes wrong three thousand levels down, on whatever stack that is by then, reaches the
            // xsl:try at the top as the error it was, code and all, and a stack that is not the caller's
            // makes no difference to what is written after.
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:try>"
                + Nested("<xsl:if test=\"true()\">", "</xsl:if>", "<xsl:sequence select=\"error(xs:QName('f:deep'), 'from the bottom')\"/>", 3000)
                + "<xsl:catch errors=\"f:deep\" select=\"local-name-from-QName($err:code), contains($err:description, 'from the bottom')\"/></xsl:try>|after</out></xsl:template>");

            Xslt compiled = OnAStackOf(256, () => new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }));
            Assert.AreEqual("<out>deep true|after</out>", OnAStackOf(256, () => compiled.TransformXml("<r/>")));

            // And one nothing catches is the transformation's error, with its code.
            string uncaught = Sheet(
                "<xsl:template match=\"/\"><out>"
                + Nested("<xsl:if test=\"true()\">", "</xsl:if>", "<xsl:sequence select=\"error(xs:QName('f:deep'))\"/>", 3000)
                + "</out></xsl:template>");

            Xslt failing = OnAStackOf(256, () => new Xslt(uncaught, new XsltOptions { OmitXmlDeclaration = true }));
            XsltException raised = Assert.ThrowsExactly<XsltException>(() => OnAStackOf(256, () => failing.TransformXml("<r/>")));
            Assert.AreEqual("deep", raised.Code);
        }

        [TestMethod]
        public void ModerateNestingRunsAsItDid()
        {
            // Deeper than the interval at which a sequence constructor asks about the stack, on both
            // backends, and with the things that read a constructor's shape inside: on-empty at the
            // bottom, a tail call at the bottom, a variable's scope closing on the way out.
            Assert.AreEqual(
                "<out>x</out>",
                Run(Sheet("<xsl:template match=\"/\"><out>" + Nested("<xsl:if test=\"true()\">", "</xsl:if>", "x", 200) + "</out></xsl:template>")));

            Assert.AreEqual(
                "<out>none</out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + Nested("<xsl:if test=\"true()\">", "</xsl:if>", "<xsl:on-empty>none</xsl:on-empty>", 100)
                    + "</out></xsl:template>")));

            // A call at the bottom of forty nested xsl:ifs is still in tail position, so a thousand of
            // them in a row unwind between one and the next: were they not, the guard forty levels in
            // would refuse the thousandth, which is what makes this safe to ask on a large stack.
            string recursive = Sheet(
                "<xsl:template match=\"/\"><out><xsl:call-template name=\"f:go\"><xsl:with-param name=\"n\" select=\"1000\"/></xsl:call-template></out></xsl:template>"
                + "<xsl:template name=\"f:go\"><xsl:param name=\"n\"/><xsl:if test=\"$n > 0\"><a/>"
                + Nested("<xsl:if test=\"true()\">", "</xsl:if>", "<xsl:call-template name=\"f:go\"><xsl:with-param name=\"n\" select=\"$n - 1\"/></xsl:call-template>", 40)
                + "</xsl:if></xsl:template>");

            Assert.AreEqual(
                "<out>" + string.Concat(Enumerable.Repeat("<a/>", 1000)) + "</out>",
                OnAStackOf(16 * 1024, () => Run(recursive)));

            // A variable declared at the bottom is out of scope once the nesting closes.
            Assert.AreEqual(
                "<out>inner outer</out>",
                Run(Sheet("<xsl:template match=\"/\"><xsl:variable name=\"v\" select=\"'outer'\"/><out>"
                    + Nested("<xsl:if test=\"true()\">", "</xsl:if>", "<xsl:variable name=\"w\" select=\"'inner'\"/><xsl:value-of select=\"$w\"/>", 100)
                    + "<xsl:value-of select=\"concat(' ', $v)\"/></out></xsl:template>")));

            XsltException outOfScope = Assert.ThrowsExactly<XsltException>(() => Run(Sheet(
                "<xsl:template match=\"/\"><out>"
                + Nested("<xsl:if test=\"true()\">", "</xsl:if>", "<xsl:variable name=\"w\" select=\"'inner'\"/>", 100)
                + "<xsl:value-of select=\"$w\"/></out></xsl:template>")));

            Assert.AreEqual("XPST0008", outOfScope.Code);
        }

        // ---- Patterns --------------------------------------------------------------------------------------

        [TestMethod]
        public void APatternOfAnyNumberOfOrdinaryStepsMatches()
        {
            // Every step has one anchor, so the pattern is walked in a loop over the ancestors, and the
            // stylesheet's a/a/a/... of twenty thousand steps matches the bottom of a document that deep.
            string input = Nested("<a>", "</a>", "x", Long);
            string stylesheet = Sheet(
                "<xsl:template match=\"" + Repeat("a", "/", Long) + "\">hit</xsl:template>"
                + "<xsl:template match=\"/\"><xsl:apply-templates select=\"(//a)[last()]\"/></xsl:template>");

            Assert.AreEqual("hit", OnAStackOf(256, () => Run(stylesheet, input)));
        }

        [TestMethod]
        public void APatternOfAnyNumberOfClimbingStepsMatches()
        {
            // A step that climbs is a search, and the search is a recursion, which goes on upon a new
            // stack where the one in use runs short: five thousand of them over a document that deep
            // match on a quarter of a megabyte.
            string input = Nested("<a>", "</a>", "x", 5000);
            string stylesheet = Sheet(
                "<xsl:template match=\"" + Repeat("a", "//", 5000) + "\">hit</xsl:template>"
                + "<xsl:template match=\"/\"><xsl:apply-templates select=\"(//a)[last()]\"/></xsl:template>");

            Assert.AreEqual("hit", OnAStackOf(256, () => Run(stylesheet, input)));

            // A climbing pattern short enough is matched as it was, by the search.
            string shallow = Sheet(
                "<xsl:template match=\"" + Repeat("a", "//", 50) + "\">hit</xsl:template>"
                + "<xsl:template match=\"/\"><xsl:apply-templates select=\"(//a)[last()]\"/></xsl:template>");

            Assert.AreEqual("hit", OnAStackOf(256, () => Run(shallow, Nested("<a>", "</a>", "x", 60))));

            // Not matched where the innermost step does not: a climbing pattern that fails only at the
            // top of a deep chain is a search of every way up it, which is the search it always was and
            // not something to ask of a test.
            string other = Sheet(
                "<xsl:template match=\"" + string.Concat(Enumerable.Repeat("a//", 49)) + "b\">hit</xsl:template>"
                + "<xsl:template match=\"/\"><xsl:apply-templates select=\"(//a)[last()]\"/></xsl:template>");

            Assert.AreEqual("x", OnAStackOf(256, () => Run(other, Nested("<a>", "</a>", "x", 60))));
        }
    }
}
