namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the instructions XSLT 2.0 adds.
    /// </summary>
    /// <remarks>
    /// These have no oracle. <c>XslCompiledTransform</c> implements XSLT 1.0 only, so it cannot say what any
    /// of this should produce — the expectations here come from the specification, and the QT3 suite covers
    /// the XPath half rather than these. That makes them worth reading carefully.
    /// </remarks>
    [TestClass]
    public sealed class Xslt2InstructionTests
    {
        private const string XslOnly = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private const string Xsl =
            XslOnly + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private static string Run(
            string body,
            string input,
            string version = "2.0",
            IXsltResolver? resolver = null,
            XsltVersion? implemented = null)
        {
            string stylesheet = $"<xsl:stylesheet version=\"{version}\" {Xsl}>{body}</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                StylesheetResolver = resolver,

                // A 2.0 processor unless a test asks for 3.0: this is the 2.0 instruction set, and what a 2.0
                // processor refuses is as much what these tests pin down as what it does. The engine itself
                // claims 3.0 for a caller who names none.
                Version = implemented ?? XsltVersion.V20,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        /// <summary>Wraps a body in a template matching the root.</summary>
        private static string Root(string body) => $"<xsl:template match=\"/\"><out>{body}</out></xsl:template>";

        // ---- xsl:for-each-group --------------------------------------------------------------------------

        [TestMethod]
        public void GroupByCollectsItemsSharingAKey()
        {
            // The whole point of the instruction: what XSLT 1.0 needed keys and generate-id() to express.
            Assert.AreEqual(
                "<out><g k=\"fruit\">apple pear</g><g k=\"veg\">leek</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/r/i\" group-by=\"@k\">"
                        + "<g k=\"{current-grouping-key()}\">"
                        + "<xsl:value-of select=\"current-group()\"/>"
                        + "</g></xsl:for-each-group>"),
                    "<r><i k='fruit'>apple</i><i k='veg'>leek</i><i k='fruit'>pear</i></r>"));
        }

        [TestMethod]
        public void GroupsKeepTheOrderTheirFirstItemAppearedIn()
        {
            Assert.AreEqual(
                "<out><g>b</g><g>a</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/r/i\" group-by=\".\">"
                        + "<g><xsl:value-of select=\"current-grouping-key()\"/></g>"
                        + "</xsl:for-each-group>"),
                    "<r><i>b</i><i>a</i><i>b</i></r>"));
        }

        [TestMethod]
        public void GroupAdjacentStartsAfreshWhenTheKeyChanges()
        {
            // The same value returning later is a new group, which is what separates this from group-by.
            Assert.AreEqual(
                "<out><g>a a</g><g>b</g><g>a</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/r/i\" group-adjacent=\".\">"
                        + "<g><xsl:value-of select=\"current-group()\"/></g>"
                        + "</xsl:for-each-group>"),
                    "<r><i>a</i><i>a</i><i>b</i><i>a</i></r>"));
        }

        [TestMethod]
        public void GroupStartingWithBeginsAGroupAtEachMatch()
        {
            Assert.AreEqual(
                "<out><g>h1 p1 p2</g><g>h2 p3</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/r/*\" group-starting-with=\"h\">"
                        + "<g><xsl:value-of select=\"current-group()\"/></g>"
                        + "</xsl:for-each-group>"),
                    "<r><h>h1</h><p>p1</p><p>p2</p><h>h2</h><p>p3</p></r>"));
        }

        [TestMethod]
        public void GroupEndingWithClosesAGroupAtEachMatch()
        {
            Assert.AreEqual(
                "<out><g>p1 end1</g><g>p2 end2</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/r/*\" group-ending-with=\"end\">"
                        + "<g><xsl:value-of select=\"current-group()\"/></g>"
                        + "</xsl:for-each-group>"),
                    "<r><p>p1</p><end>end1</end><p>p2</p><end>end2</end></r>"));
        }

        [TestMethod]
        public void ThePositionCountsGroupsRatherThanItems()
        {
            Assert.AreEqual(
                "<out><g n=\"1\">a</g><g n=\"2\">b</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/r/i\" group-by=\".\">"
                        + "<g n=\"{position()}\"><xsl:value-of select=\"current-grouping-key()\"/></g>"
                        + "</xsl:for-each-group>"),
                    "<r><i>a</i><i>b</i><i>a</i></r>"));
        }

        [TestMethod]
        public void GroupsCanBeSorted()
        {
            Assert.AreEqual(
                "<out><g>a</g><g>b</g><g>c</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/r/i\" group-by=\".\">"
                        + "<xsl:sort select=\"current-grouping-key()\"/>"
                        + "<g><xsl:value-of select=\"current-grouping-key()\"/></g>"
                        + "</xsl:for-each-group>"),
                    "<r><i>c</i><i>a</i><i>b</i></r>"));
        }

        [TestMethod]
        public void AnItemWhoseKeyIsASequenceJoinsEveryGroupItNames()
        {
            // One element under several headings at once, which is what makes grouping more than sorting.
            Assert.AreEqual(
                "<out><g k=\"x\">1</g><g k=\"y\">1</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/r/i\" group-by=\"tokenize(@k, ' ')\">"
                        + "<g k=\"{current-grouping-key()}\"><xsl:value-of select=\"current-group()\"/></g>"
                        + "</xsl:for-each-group>"),
                    "<r><i k='x y'>1</i></r>"));
        }

        [TestMethod]
        public void TheGroupingFunctionsOutsideAGroupingBodyDependOnTheProcessor()
        {
            // Outside any xsl:for-each-group there is no current group. XSLT 3.0 makes asking for one
            // XTDE1061 — the suite's error-1061a — and 2.0 answered with the empty sequence, which is
            // what the suite's for-each-group-081a and 081b assert of the two processors in turn.
            const string Asking = "<n><xsl:value-of select=\"count(current-group())\"/></n>";

            Assert.AreEqual("<out><n>0</n></out>", Run(Root(Asking), "<r/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root(Asking), "<r/>", "3.0", implemented: XsltVersion.V30));

            Assert.AreEqual("XTDE1061", error.Code);

            // And the key with it: a 3.0 processor refuses, a 2.0 one says nothing.
            const string Keyed = "<n><xsl:value-of select=\"count(current-grouping-key())\"/></n>";

            Assert.AreEqual("<out><n>0</n></out>", Run(Root(Keyed), "<r/>"));

            error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root(Keyed), "<r/>", "3.0", implemented: XsltVersion.V30));

            Assert.AreEqual("XTDE1071", error.Code);
        }

        [TestMethod]
        public void GroupingRejectsTwoWaysOfGroupingAtOnce()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:for-each-group select=\"/r/i\" group-by=\".\" group-adjacent=\".\">"
                        + "<g/></xsl:for-each-group>"),
                    "<r><i>a</i></r>"));

            StringAssert.Contains(error.Message, "group-adjacent");
        }

        // ---- xsl:function --------------------------------------------------------------------------------

        private const string Fn = "xmlns:f=\"urn:functions\"";

        private static string RunWithFunctions(string body, string input)
        {
            // The function namespace exists for the processor, not for the result, so it is excluded — which
            // is what a stylesheet declaring functions would do, and keeps these expectations about the
            // functions rather than about namespace copying.
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {XslOnly} {Fn} xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"f xs\">"
                + body + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        [TestMethod]
        public void AFunctionCanBeCalledFromAnExpression()
        {
            // What a named template cannot do: be used inside an expression.
            Assert.AreEqual(
                "<out>6</out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:double\"><xsl:param name=\"n\"/>"
                    + "<xsl:sequence select=\"$n * 2\"/></xsl:function>"
                    + Root("<xsl:value-of select=\"f:double(3)\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void AFunctionReturnsATypedValueRatherThanText()
        {
            // xsl:sequence gives back the value itself, so the result is still a number and still divides.
            Assert.AreEqual(
                "<out>2.5</out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:five\"><xsl:sequence select=\"5\"/></xsl:function>"
                    + Root("<xsl:value-of select=\"f:five() div 2\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void AFunctionCanBeUsedInAPredicate()
        {
            Assert.AreEqual(
                "<out>b</out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:long\"><xsl:param name=\"s\"/>"
                    + "<xsl:sequence select=\"string-length($s) &gt; 1\"/></xsl:function>"
                    + Root("<xsl:value-of select=\"/r/i[f:long(.)]\"/>"),
                    "<r><i>a</i><i>bb</i></r>").Replace("bb", "b"));
        }

        [TestMethod]
        public void AFunctionCanCallItself()
        {
            // Recursion is how anything iterative is expressed, so a function that cannot recurse is of
            // little use. The declaration happens before any body is compiled, which is what allows it.
            Assert.AreEqual(
                "<out>120</out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:fact\"><xsl:param name=\"n\"/>"
                    + "<xsl:sequence select=\"if ($n &lt;= 1) then 1 else $n * f:fact($n - 1)\"/>"
                    + "</xsl:function>"
                    + Root("<xsl:value-of select=\"f:fact(5)\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void FunctionsCanCallOneAnotherInEitherOrder()
        {
            Assert.AreEqual(
                "<out>9</out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:a\"><xsl:param name=\"n\"/>"
                    + "<xsl:sequence select=\"f:b($n) + 1\"/></xsl:function>"
                    + "<xsl:function name=\"f:b\"><xsl:param name=\"n\"/>"
                    + "<xsl:sequence select=\"$n * 2\"/></xsl:function>"
                    + Root("<xsl:value-of select=\"f:a(4)\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void NameAndArityTogetherIdentifyAFunction()
        {
            // Two functions of one name taking different numbers of arguments are two functions, as they are
            // in the core library where string-length() and string-length($s) both exist.
            Assert.AreEqual(
                "<out>10 7</out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:add\"><xsl:param name=\"a\"/>"
                    + "<xsl:sequence select=\"$a + 10\"/></xsl:function>"
                    + "<xsl:function name=\"f:add\"><xsl:param name=\"a\"/><xsl:param name=\"b\"/>"
                    + "<xsl:sequence select=\"$a + $b\"/></xsl:function>"
                    // xsl:text, because whitespace-only text in a stylesheet is stripped.
                    + Root("<xsl:value-of select=\"f:add(0)\"/><xsl:text> </xsl:text>"
                        + "<xsl:value-of select=\"f:add(3, 4)\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void AFunctionMayBuildContentInsteadOfReturningAValue()
        {
            Assert.AreEqual(
                "<out><wrapped>x</wrapped></out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:wrap\"><xsl:param name=\"s\"/>"
                    + "<wrapped><xsl:value-of select=\"$s\"/></wrapped></xsl:function>"
                    + Root("<xsl:copy-of select=\"f:wrap('x')\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void ArgumentsAreEvaluatedWhereTheyWereWritten()
        {
            // The context node inside the function is still the caller's, so an argument of "." means the
            // node the call was written against.
            Assert.AreEqual(
                "<out>a-b-</out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:mark\"><xsl:param name=\"s\"/>"
                    + "<xsl:sequence select=\"concat($s, '-')\"/></xsl:function>"
                    + Root("<xsl:for-each select=\"/r/i\"><xsl:value-of select=\"f:mark(.)\"/></xsl:for-each>"),
                    "<r><i>a</i><i>b</i></r>"));
        }

        [TestMethod]
        public void AFunctionNameMustBeInANamespace()
        {
            // Otherwise a stylesheet could shadow a core function, or be broken by one added later.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunWithFunctions(
                    "<xsl:function name=\"plain\"><xsl:sequence select=\"1\"/></xsl:function>"
                    + Root("x"),
                    "<r/>"));

            StringAssert.Contains(error.Message, "namespace");
        }

        [TestMethod]
        public void CallingWithTheWrongNumberOfArgumentsIsReported()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunWithFunctions(
                    "<xsl:function name=\"f:one\"><xsl:param name=\"a\"/>"
                    + "<xsl:sequence select=\"$a\"/></xsl:function>"
                    + Root("<xsl:value-of select=\"f:one(1, 2)\"/>"),
                    "<r/>"));

            StringAssert.Contains(error.Message, "argument");
        }

        [TestMethod]
        public void TwoFunctionsOfOneNameAndArityAreRejected()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunWithFunctions(
                    "<xsl:function name=\"f:x\"><xsl:sequence select=\"1\"/></xsl:function>"
                    + "<xsl:function name=\"f:x\"><xsl:sequence select=\"2\"/></xsl:function>"
                    + Root("x"),
                    "<r/>"));

            StringAssert.Contains(error.Message, "More than one");
        }


        /// <summary>A stylesheet whose templates walk a run of siblings one at a time.</summary>
        /// <param name="step">How the template reaches the next sibling.</param>
        private static string Walks(string step)
        {
            return "<xsl:variable name=\"in\"><doc><xsl:for-each select=\"1 to 20000\">"
                + "<e><xsl:value-of select=\".\"/></e></xsl:for-each></doc></xsl:variable>"
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:apply-templates select=\"$in/doc/e[1]\"/></out></xsl:template>"
                + "<xsl:template match=\"e\">"
                + "<xsl:if test=\"not(following-sibling::e)\"><xsl:value-of select=\".\"/></xsl:if>"
                + step
                + "</xsl:template>";
        }

        [TestMethod]
        public void ApplyingTemplatesAsTheLastThingATemplateDoesDoesNotGrowTheStack()
        {
            // The idiom for walking a long run of siblings: each template applies templates to the next
            // one and does nothing after it. Nested, that costs a frame per sibling and runs out a few
            // hundred in; made in the template's own place, the length of the run stops mattering.
            Assert.AreEqual(
                "<out>20000</out>",
                Run(Walks("<xsl:apply-templates select=\"following-sibling::e[1]\"/>"), "<r/>"));
        }

        [TestMethod]
        public void OnlyTheLastIterationOfAForEachIsInTailPosition()
        {
            // The same walk with the step wrapped in an xsl:for-each over one node, which is the shape
            // the suite's call-template-1003 takes. The body's last instruction is the template's last
            // act only on the last iteration, so the loop settles what every other iteration hands back.
            Assert.AreEqual(
                "<out>20000</out>",
                Run(
                    Walks(
                        "<xsl:for-each select=\"following-sibling::e[1]\">"
                        + "<xsl:apply-templates select=\".\"/></xsl:for-each>"),
                    "<r/>"));
        }

        [TestMethod]
        public void TheLastNodeOfASelectionKeepsItsPositionAndSize()
        {
            // A call handed back carries the focus it was made with, and the last node of a selection is
            // the one that is handed back: the third of three is still third of three.
            Assert.AreEqual(
                "<out>1/3 2/3 3/3 </out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/i\"/></out></xsl:template>"
                    + "<xsl:template match=\"i\">"
                    + "<xsl:value-of select=\"concat(position(), '/', last(), ' ')\"/></xsl:template>",
                    "<r><i/><i/><i/></r>"));
        }

        [TestMethod]
        public void ANamedCallInAForEachIsMadeInTheIterationThatMadeIt()
        {
            // Never handed on past the end of the loop. xsl:for-each suspends the current template rule
            // while its body runs, so a call made afterwards would see a rule the call site did not — and
            // there is one place to hand a call back to, so two iterations would lose the first.
            Assert.AreEqual(
                "<out>a1a2a3</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:for-each select=\"/r/i\">"
                    + "<xsl:call-template name=\"w\"/></xsl:for-each></out></xsl:template>"
                    + "<xsl:template name=\"w\">a<xsl:value-of select=\"position()\"/></xsl:template>",
                    "<r><i/><i/><i/></r>"));
        }

        [TestMethod]
        public void EndlessRecursionIsReportedRatherThanExhaustingTheStack()
        {
            // The call has something done to its value, so it is not the whole of the function's answer: a
            // call that is, is made in the function's own place and never deepens the stack, and a function
            // that does that without a terminating case is a loop rather than a recursion.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunWithFunctions(
                    "<xsl:function name=\"f:loop\"><xsl:sequence select=\"f:loop() + 1\"/></xsl:function>"
                    + Root("<xsl:value-of select=\"f:loop()\"/>"),
                    "<r/>"));

            // It matters that this is an exception and not a stack overflow: a stack overflow cannot be
            // caught and takes the process with it, so a stylesheet could bring down its host.
            StringAssert.Contains(error.Message, "nested");
        }

        [TestMethod]
        public void ACallThatIsTheWholeOfAFunctionsAnswerDoesNotGrowTheStack()
        {
            // Recursion as the last thing a function does is how a loop is written in a language without
            // one. Nested, each call costs a frame and it fails a couple of thousand in; made in the
            // function's own place, it runs for as long as it likes. A hundred thousand is fifty times the
            // depth limit, so this passes only by not nesting. The 3.0 form, through a let, is tested with
            // the other 3.0 attributes.
            Assert.AreEqual(
                "<out>5000050000</out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:sum\"><xsl:param name=\"n\"/><xsl:param name=\"total\"/>"
                    + "<xsl:sequence select=\"if ($n = 0) then $total else f:sum($n - 1, $total + $n)\"/>"
                    + "</xsl:function>"
                    + Root("<xsl:value-of select=\"f:sum(100000, 0)\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void ACallMadeInAFunctionsPlaceStillAnswersToItsDeclaredType()
        {
            // The first function promised a string, and the chain of calls made in its place ended in one
            // that returned an integer. The promise is the caller's to rely on, whichever function ended up
            // answering.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunWithFunctions(
                    "<xsl:function name=\"f:a\" as=\"xs:string\"><xsl:sequence select=\"f:b()\"/></xsl:function>"
                    + "<xsl:function name=\"f:b\"><xsl:sequence select=\"1\"/></xsl:function>"
                    + Root("<xsl:value-of select=\"f:a()\"/>"),
                    "<r/>"));

            Assert.AreEqual("XTTE0780", error.Code);
        }

        [TestMethod]
        public void ATunnelParameterReachesATemplateCalledInAnothersPlace()
        {
            // A call made in the calling template's place is still made from that template, so the tunnel
            // it extends is the calling template's.
            Assert.AreEqual(
                "<out>v</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:call-template name=\"a\">"
                    + "<xsl:with-param name=\"t\" select=\"'v'\" tunnel=\"yes\"/></xsl:call-template></out></xsl:template>"
                    + "<xsl:template name=\"a\"><xsl:call-template name=\"b\"/></xsl:template>"
                    + "<xsl:template name=\"b\"><xsl:param name=\"t\" tunnel=\"yes\"/><xsl:value-of select=\"$t\"/></xsl:template>",
                    "<r/>"));
        }

        // ---- xsl:result-document -------------------------------------------------------------------------

        /// <summary>Collects secondary results in memory, which is what a caller building a package wants.</summary>
        private sealed class CollectingResolver : IXsltResultResolver
        {
            public Dictionary<string, StringWriter> Documents { get; } = new(StringComparer.Ordinal);

            public TextWriter Resolve(string href, string? baseUri)
            {
                StringWriter writer = new StringWriter();
                Documents[href] = writer;
                return writer;
            }
        }

        private static (string Principal, CollectingResolver Results) RunWithResults(string body, string input)
        {
            CollectingResolver resolver = new CollectingResolver();

            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {Xsl}>{body}</xsl:stylesheet>";
            string principal = new Xslt(
                stylesheet,
                new XsltOptions { OmitXmlDeclaration = true, ResultResolver = resolver })
                .TransformXml(input);

            return (principal, resolver);
        }

        [TestMethod]
        public void ResultDocumentWritesElsewhere()
        {
            (string principal, CollectingResolver results) = RunWithResults(
                Root("<xsl:result-document href=\"side.xml\"><side>x</side></xsl:result-document>main"),
                "<r/>");

            Assert.AreEqual("<out>main</out>", principal);
            Assert.AreEqual("<side>x</side>", results.Documents["side.xml"].ToString());
        }

        [TestMethod]
        public void TheHrefIsAnAttributeValueTemplate()
        {
            (string _, CollectingResolver results) = RunWithResults(
                Root("<xsl:for-each select=\"/r/i\">"
                    + "<xsl:result-document href=\"{@n}.xml\"><p><xsl:value-of select=\".\"/></p>"
                    + "</xsl:result-document></xsl:for-each>"),
                "<r><i n='a'>one</i><i n='b'>two</i></r>");

            Assert.AreEqual("<p>one</p>", results.Documents["a.xml"].ToString());
            Assert.AreEqual("<p>two</p>", results.Documents["b.xml"].ToString());
        }

        [TestMethod]
        public void AResultDocumentMayBeSerializedDifferently()
        {
            // One transformation writing XML and HTML at once is the reason the settings are per document.
            (string _, CollectingResolver results) = RunWithResults(
                Root("<xsl:result-document href=\"p.html\" method=\"html\"><html><br/></html>"
                    + "</xsl:result-document>"),
                "<r/>");

            // The HTML method leaves a void element unclosed, where XML would have written "<br/>".
            StringAssert.Contains(results.Documents["p.html"].ToString(), "<br>");
        }

        [TestMethod]
        public void SettingsNotMentionedAreInheritedFromTheStylesheet()
        {
            (string _, CollectingResolver results) = RunWithResults(
                "<xsl:output omit-xml-declaration=\"yes\"/>"
                + Root("<xsl:result-document href=\"a.xml\"><a/></xsl:result-document>"),
                "<r/>");

            Assert.AreEqual("<a/>", results.Documents["a.xml"].ToString());
        }

        [TestMethod]
        public void WithoutAResolverAResultDocumentIsRefused()
        {
            // The one instruction that creates something outside the transformation, so it writes only where
            // the caller has said it may.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:result-document href=\"x.xml\"><a/></xsl:result-document>"),
                    "<r/>"));

            StringAssert.Contains(error.Message, "ResultResolver");
        }

        [TestMethod]
        public void WritingOneDocumentTwiceIsRefused()
        {
            // The second write would either destroy the first or interleave with it, and neither is what the
            // stylesheet meant.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunWithResults(
                    Root("<xsl:for-each select=\"/r/i\">"
                        + "<xsl:result-document href=\"same.xml\"><p/></xsl:result-document>"
                        + "</xsl:for-each>"),
                    "<r><i/><i/></r>"));

            StringAssert.Contains(error.Message, "more than once");
        }

        [TestMethod]
        public void TheBodyStillSeesTheContextItAppearedIn()
        {
            (string _, CollectingResolver results) = RunWithResults(
                Root("<xsl:for-each select=\"/r/i\">"
                    + "<xsl:result-document href=\"{position()}.xml\">"
                    + "<p n=\"{position()}\"><xsl:value-of select=\".\"/></p>"
                    + "</xsl:result-document></xsl:for-each>"),
                "<r><i>a</i><i>b</i></r>");

            Assert.AreEqual("<p n=\"2\">b</p>", results.Documents["2.xml"].ToString());
        }

        [TestMethod]
        public void AResultDocumentWithNoHrefIsThePrincipalResult()
        {
            // No href names the base output URI, which is where the transformation's own result goes. No
            // resolver is involved: nothing is being created outside the transformation.
            Assert.AreEqual(
                "<out>generated</out>",
                Run("<xsl:template match=\"/\"><xsl:result-document><out>generated</out>"
                    + "</xsl:result-document></xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void ThePrincipalResultIsStillSerializedAsThatInstructionAsked()
        {
            StringAssert.Contains(
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl}><xsl:template match=\"/\">"
                    + "<xsl:result-document method=\"html\"><html><br/></html></xsl:result-document>"
                    + "</xsl:template></xsl:stylesheet>").TransformXml("<r/>"),
                "<br>");
        }

        [TestMethod]
        [DataRow("<mine/><xsl:result-document><out/></xsl:result-document>")]
        [DataRow("<xsl:result-document><out/></xsl:result-document><mine/>")]
        public void TwoResultTreesMayNotShareTheBaseOutputUri(string body)
        {
            // Whichever order they come in: what is written outside an xsl:result-document goes to the same
            // URI as one that leaves out its href.
            Assert.AreEqual(
                "XTDE1490",
                Assert.ThrowsExactly<XsltException>(
                    () => Run($"<xsl:template match=\"/\">{body}</xsl:template>", "<r/>")).Code);
        }

        [TestMethod]
        public void AResultDocumentMayNameAnOutputDefinitionToSerializeBy()
        {
            (string _, CollectingResolver results) = RunWithResults(
                "<xsl:output method=\"xml\"/><xsl:output name=\"page\" method=\"html\"/>"
                + Root("<xsl:result-document href=\"p.html\" format=\"page\"><html><br/></html>"
                    + "</xsl:result-document>"),
                "<r/>");

            StringAssert.Contains(results.Documents["p.html"].ToString(), "<br>");
        }

        [TestMethod]
        public void ANamedOutputDefinitionDoesNotChangeTheUnnamedOne()
        {
            // The reason there are two: one transformation writing XML through the unnamed definition and
            // something else through a named one. A named method="text" reaching the unnamed settings would
            // have thrown the markup away.
            Assert.AreEqual(
                "<out><p/></out>",
                Run("<xsl:output method=\"xml\"/><xsl:output name=\"page\" method=\"text\"/>"
                    + Root("<p/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void AFormatNamingNoOutputDefinitionIsRefused()
        {
            Assert.AreEqual(
                "XTDE1460",
                Assert.ThrowsExactly<XsltException>(
                    () => RunWithResults(
                        Root("<xsl:result-document href=\"a.xml\" format=\"missing\"><a/>"
                            + "</xsl:result-document>"),
                        "<r/>")).Code);
        }

        [TestMethod]
        public void AnOutputMethodThatIsNotANameIsRefusedWhereItIsWritten()
        {
            // Checked even on a named definition nothing asks for: being unreachable does not make a
            // declaration well written.
            Assert.AreEqual(
                "XTSE1570",
                Assert.ThrowsExactly<XsltException>(
                    () => Run("<xsl:output name=\"a\" method=\"your::xml\"/>" + Root("x"), "<r/>")).Code);
        }

        [TestMethod]
        public void StandaloneMayBeOmittedRatherThanDenied()
        {
            // Three values rather than two: "omit" leaves the declaration without a standalone at all.
            string result = new Xslt(
                $"<xsl:stylesheet version=\"2.0\" {Xsl}><xsl:output standalone=\"omit\"/>"
                + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>").TransformXml("<r/>");

            StringAssert.StartsWith(result, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        }

        // ---- xsl:analyze-string --------------------------------------------------------------------------

        [TestMethod]
        public void AnalyzeStringHandlesTheTwoKindsOfPieceSeparately()
        {
            Assert.AreEqual(
                "<out>[12] and [34]</out>",
                Run(
                    Root("<xsl:analyze-string select=\"/r\" regex=\"[0-9]+\">"
                        + "<xsl:matching-substring>[<xsl:value-of select=\".\"/>]</xsl:matching-substring>"
                        + "<xsl:non-matching-substring><xsl:value-of select=\".\"/></xsl:non-matching-substring>"
                        + "</xsl:analyze-string>"),
                    "<r>12 and 34</r>"));
        }

        [TestMethod]
        public void CapturedGroupsAreEmptyInsideAFunctionAndAPattern()
        {
            // §17.2: the captured substrings are a dynamically scoped variable that a stylesheet function
            // and a pattern both see as empty, however they were reached. The function writes what
            // regex-group(1) says inside it; the pattern starts a group at every item whose attribute is
            // what regex-group(1) says, which is the empty string, so both items start one.
            Assert.AreEqual(
                "<out>[12:f=|p=2]</out>",
                RunWithFunctions(
                    "<xsl:function name=\"f:g\"><xsl:value-of select=\"regex-group(1)\"/></xsl:function>"
                    + Root("<xsl:variable name=\"d\" select=\"/\"/><xsl:analyze-string select=\"/r/s\" regex=\"([0-9]+)\">"
                        + "<xsl:matching-substring>[<xsl:value-of select=\".\"/>:f=<xsl:value-of select=\"f:g()\"/>"
                        + "|p=<xsl:for-each-group select=\"$d/r/i\" group-starting-with=\"i[@a = regex-group(1)]\">"
                        + "<xsl:value-of select=\"count(current-group())\"/></xsl:for-each-group>]"
                        + "</xsl:matching-substring></xsl:analyze-string>"),
                    "<r><s>12</s><i a=\"\"/><i a=\"12\"/></r>"));
        }

        [TestMethod]
        public void AnalyzeStringTakesOneString()
        {
            // The select is xs:string, read by the function conversion rules: a number is not a string and
            // three strings are not one. From 3.0 the type is xs:string?, so nothing is an empty input.
            const string Branches = "<xsl:matching-substring>m</xsl:matching-substring>"
                + "<xsl:non-matching-substring>n</xsl:non-matching-substring></xsl:analyze-string>";

            Assert.AreEqual("XPTY0004", Refuse(Root("<xsl:analyze-string select=\"22\" regex=\"2\">" + Branches)));
            Assert.AreEqual(
                "XPTY0004",
                Refuse(Root("<xsl:analyze-string select=\"('a', 'b')\" regex=\"a\">" + Branches)));
            Assert.AreEqual("XPTY0004", Refuse(Root("<xsl:analyze-string select=\"()\" regex=\"a\">" + Branches)));
            Assert.AreEqual(
                "<out/>",
                Run(
                    Root("<xsl:analyze-string select=\"()\" regex=\"a\">" + Branches),
                    "<r/>",
                    version: "3.0",
                    implemented: XsltVersion.V30));

            // A node is atomized and an untyped value is a string, and a 1.0 stylesheet converts as 1.0 did.
            Assert.AreEqual(
                "<out>nmn</out>",
                Run(Root("<xsl:analyze-string select=\"/r/@x\" regex=\"2\">" + Branches), "<r x=\"123\"/>"));
            Assert.AreEqual(
                "<out>nm</out>",
                Run(Root("<xsl:analyze-string select=\"12\" regex=\"2\">" + Branches), "<r/>", version: "1.0"));
        }

        [TestMethod]
        public void AZeroLengthMatchIsAdmittedFromThreePointZero()
        {
            // §17.1: a zero-length match is a matching substring of its own, and the character after it
            // begins the next non-matching one, so the walk moves on. XSLT 2.0 refused the regex (XTDE1150).
            const string Body = "<xsl:analyze-string select=\"'abc'\" regex=\"x*\">"
                + "<xsl:matching-substring>[<xsl:value-of select=\".\"/>]</xsl:matching-substring>"
                + "<xsl:non-matching-substring><xsl:value-of select=\".\"/></xsl:non-matching-substring>"
                + "</xsl:analyze-string>";

            Assert.AreEqual(
                "<out>[]a[]b[]c[]</out>",
                Run(Root(Body), "<r/>", version: "3.0", implemented: XsltVersion.V30));
            Assert.AreEqual("XTDE1150", Refuse(Root(Body)));
        }

        [TestMethod]
        public void TheBranchesOfAnalyzeStringComeInOrder()
        {
            // §17.1: the matching branch first, the non-matching one second, fallbacks after either, and
            // neither branch twice.
            const string Open = "<xsl:analyze-string select=\"'abc'\" regex=\"a\">";
            const string Matching = "<xsl:matching-substring>m</xsl:matching-substring>";
            const string NonMatching = "<xsl:non-matching-substring>n</xsl:non-matching-substring>";
            const string Close = "</xsl:analyze-string>";

            Assert.AreEqual("XTSE0010", Refuse(Root(Open + NonMatching + Matching + Close)));
            Assert.AreEqual("XTSE0010", Refuse(Root(Open + Matching + "<xsl:fallback/>" + NonMatching + Close)));
            Assert.AreEqual("XTSE0010", Refuse(Root(Open + Matching + Matching + Close)));
            Assert.AreEqual("XTSE1130", Refuse(Root(Open + "<xsl:fallback/>" + Close)));
            Assert.AreEqual(
                "<out>mn</out>",
                Run(Root(Open + Matching + NonMatching + "<xsl:fallback/>" + Close), "<r/>"));
        }

        [TestMethod]
        public void CapturedGroupsAreReadableWithRegexGroup()
        {
            Assert.AreEqual(
                "<out>day=01 month=02</out>",
                Run(
                    Root("<xsl:analyze-string select=\"/r\" regex=\"([0-9]+)/([0-9]+)\">"
                        + "<xsl:matching-substring>"
                        + "day=<xsl:value-of select=\"regex-group(1)\"/>"
                        + " month=<xsl:value-of select=\"regex-group(2)\"/>"
                        + "</xsl:matching-substring>"
                        + "</xsl:analyze-string>"),
                    "<r>01/02</r>"));
        }

        [TestMethod]
        public void AnAbsentBranchContributesNothing()
        {
            // Only the matching pieces are written, so this is a way of extracting rather than replacing.
            Assert.AreEqual(
                "<out>1234</out>",
                Run(
                    Root("<xsl:analyze-string select=\"/r\" regex=\"[0-9]+\">"
                        + "<xsl:matching-substring><xsl:value-of select=\".\"/></xsl:matching-substring>"
                        + "</xsl:analyze-string>"),
                    "<r>12 and 34</r>"));
        }

        [TestMethod]
        public void RegexFlagsAreHonoured()
        {
            Assert.AreEqual(
                "<out>[AB]</out>",
                Run(
                    Root("<xsl:analyze-string select=\"/r\" regex=\"ab\" flags=\"i\">"
                        + "<xsl:matching-substring>[<xsl:value-of select=\".\"/>]</xsl:matching-substring>"
                        + "</xsl:analyze-string>"),
                    "<r>AB</r>"));
        }

        // ---- xsl:sequence, xsl:perform-sort, xsl:next-match ----------------------------------------------

        [TestMethod]
        public void SequenceContributesAValue()
        {
            Assert.AreEqual("<out>1 2 3</out>", Run(Root("<xsl:sequence select=\"(1, 2, 3)\"/>"), "<r/>"));
        }

        [TestMethod]
        public void SequenceCopiesNodesRatherThanTheirText()
        {
            Assert.AreEqual(
                "<out><i>a</i><i>b</i></out>",
                Run(Root("<xsl:sequence select=\"/r/i\"/>"), "<r><i>a</i><i>b</i></r>"));
        }

        [TestMethod]
        public void PerformSortOrdersWithoutDoingAnythingElse()
        {
            Assert.AreEqual(
                "<out><i>a</i><i>b</i><i>c</i></out>",
                Run(
                    Root("<xsl:perform-sort select=\"/r/i\"><xsl:sort select=\".\"/></xsl:perform-sort>"),
                    "<r><i>c</i><i>a</i><i>b</i></r>"));
        }

        [TestMethod]
        public void NextMatchReachesTheTemplateBehindThisOne()
        {
            // Unlike apply-imports, which can only reach an imported module, this reaches the next template
            // down by priority in the same one.
            Assert.AreEqual(
                "<out>[specific][general]</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/i\"/></out></xsl:template>"
                    + "<xsl:template match=\"i[@x]\">[specific]<xsl:next-match/></xsl:template>"
                    + "<xsl:template match=\"i\">[general]</xsl:template>",
                    "<r><i x='1'/></r>"));
        }

        [TestMethod]
        public void NextMatchFallsBackToTheBuiltInRuleWhenNothingIsLeft()
        {
            Assert.AreEqual(
                "<out>[only]text</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/i\"/></out></xsl:template>"
                    + "<xsl:template match=\"i\">[only]<xsl:next-match/></xsl:template>",
                    "<r><i>text</i></r>"));
        }

        // ---- xsl:value-of under the two versions ---------------------------------------------------------

        [TestMethod]
        public void ValueOfWritesTheWholeSequenceInVersionTwo()
        {
            // The most visible incompatibility between the versions.
            Assert.AreEqual(
                "<out>a b c</out>",
                Run(Root("<xsl:value-of select=\"/r/i\"/>"), "<r><i>a</i><i>b</i><i>c</i></r>"));
        }

        [TestMethod]
        public void ValueOfWritesOnlyTheFirstNodeInVersionOne()
        {
            Assert.AreEqual(
                "<out>a</out>",
                Run(Root("<xsl:value-of select=\"/r/i\"/>"), "<r><i>a</i><i>b</i><i>c</i></r>", version: "1.0"));
        }

        [TestMethod]
        public void TheSeparatorCanBeChosen()
        {
            Assert.AreEqual(
                "<out>a, b, c</out>",
                Run(
                    Root("<xsl:value-of select=\"/r/i\" separator=\", \"/>"),
                    "<r><i>a</i><i>b</i><i>c</i></r>"));
        }

        [TestMethod]
        public void TheSeparatorMayBeEmpty()
        {
            Assert.AreEqual(
                "<out>abc</out>",
                Run(
                    Root("<xsl:value-of select=\"/r/i\" separator=\"\"/>"),
                    "<r><i>a</i><i>b</i><i>c</i></r>"));
        }

        // ---- Tunnel parameters ---------------------------------------------------------------------------

        /// <summary>The template that reads the value, written once and shared by most of these tests.</summary>
        private const string Reader =
            "<xsl:template match=\"b\"><xsl:param name=\"p\" select=\"'(none)'\" tunnel=\"yes\"/>"
            + "[<xsl:value-of select=\"$p\"/>]</xsl:template>";

        [TestMethod]
        public void ATunnelParameterReachesATemplateNothingInBetweenDeclared()
        {
            // The whole feature in one test: the template for 'a' has never heard of $p and passes it on
            // regardless. Without tunnelling, every template on the route would have to declare and forward it.
            Assert.AreEqual(
                "<out>[v]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/a\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"yes\"/></xsl:apply-templates>")
                    + "<xsl:template match=\"a\"><xsl:apply-templates select=\"b\"/></xsl:template>"
                    + Reader,
                    "<r><a><b/></a></r>"));
        }

        [TestMethod]
        public void AnOrdinaryParameterStopsAtTheTemplateItWasPassedTo()
        {
            // The same stylesheet without the tunnel attributes, which is what makes the test above mean
            // something: an ordinary parameter reaches 'a' and goes no further.
            Assert.AreEqual(
                "<out>[(none)]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/a\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\"/></xsl:apply-templates>")
                    + "<xsl:template match=\"a\"><xsl:apply-templates select=\"b\"/></xsl:template>"
                    + "<xsl:template match=\"b\"><xsl:param name=\"p\" select=\"'(none)'\"/>"
                    + "[<xsl:value-of select=\"$p\"/>]</xsl:template>",
                    "<r><a><b/></a></r>"));
        }

        [TestMethod]
        public void ATunnelledValueIsInvisibleToAnOrdinaryDeclaration()
        {
            // Passed as a tunnel parameter, read as an ordinary one: the declaration sees its default. The two
            // kinds are separate namespaces, so a stylesheet cannot pick a value up by accident.
            Assert.AreEqual(
                "<out>[(none)]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/b\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"yes\"/></xsl:apply-templates>")
                    + "<xsl:template match=\"b\"><xsl:param name=\"p\" select=\"'(none)'\"/>"
                    + "[<xsl:value-of select=\"$p\"/>]</xsl:template>",
                    "<r><b/></r>"));
        }

        [TestMethod]
        public void ATunnelDeclarationIgnoresAnOrdinaryValueOfTheSameName()
        {
            // And the other direction, with both supplied at once.
            Assert.AreEqual(
                "<out>[tunnelled]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/b\">"
                        + "<xsl:with-param name=\"p\" select=\"'direct'\"/>"
                        + "<xsl:with-param name=\"p\" select=\"'tunnelled'\" tunnel=\"yes\"/>"
                        + "</xsl:apply-templates>")
                    + Reader,
                    "<r><b/></r>"));
        }

        [TestMethod]
        public void ATunnelParameterCanBeOverriddenForOnePartOfTheTree()
        {
            // The override lasts as long as the invocation that made it: the second branch sees the outer
            // value again, not the one its sibling was given.
            Assert.AreEqual(
                "<out>[inner][outer]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/a\">"
                        + "<xsl:with-param name=\"p\" select=\"'outer'\" tunnel=\"yes\"/></xsl:apply-templates>")
                    + "<xsl:template match=\"a\">"
                    + "<xsl:apply-templates select=\"b[1]\">"
                    + "<xsl:with-param name=\"p\" select=\"'inner'\" tunnel=\"yes\"/></xsl:apply-templates>"
                    + "<xsl:apply-templates select=\"b[2]\"/>"
                    + "</xsl:template>"
                    + Reader,
                    "<r><a><b/><b/></a></r>"));
        }

        [TestMethod]
        public void ATunnelParameterPassesThroughTheBuiltInRules()
        {
            // Nothing matches 'a' at all, so the built-in rule handles it. The specification has the built-in
            // rules pass tunnel parameters on, which here means simply not disturbing them.
            Assert.AreEqual(
                "<out>[v]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/a\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"yes\"/></xsl:apply-templates>")
                    + Reader,
                    "<r><a><b/></a></r>"));
        }

        [TestMethod]
        public void ATunnelParameterFollowsCallTemplate()
        {
            Assert.AreEqual(
                "<out>[v]</out>",
                Run(
                    Root("<xsl:call-template name=\"outer\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"yes\"/></xsl:call-template>")
                    + "<xsl:template name=\"outer\"><xsl:call-template name=\"inner\"/></xsl:template>"
                    + "<xsl:template name=\"inner\"><xsl:param name=\"p\" tunnel=\"yes\"/>"
                    + "[<xsl:value-of select=\"$p\"/>]</xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void ATunnelParameterSurvivesNextMatch()
        {
            Assert.AreEqual(
                "<out>[first][v]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/b\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"yes\"/></xsl:apply-templates>")
                    + "<xsl:template match=\"b[@x]\">[first]<xsl:next-match/></xsl:template>"
                    + Reader,
                    "<r><b x='1'/></r>"));
        }

        [TestMethod]
        public void NextMatchCanReplaceATunnelParameter()
        {
            Assert.AreEqual(
                "<out>[first][replaced]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/b\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"yes\"/></xsl:apply-templates>")
                    + "<xsl:template match=\"b[@x]\">[first]<xsl:next-match>"
                    + "<xsl:with-param name=\"p\" select=\"'replaced'\" tunnel=\"yes\"/>"
                    + "</xsl:next-match></xsl:template>"
                    + Reader,
                    "<r><b x='1'/></r>"));
        }

        [TestMethod]
        public void ATunnelParameterSurvivesApplyImports()
        {
            // xsl:apply-imports also gained xsl:with-param in 2.0, so an override can both wrap the template it
            // inherited and hand it something.
            MapResolver modules = new MapResolver().Add(
                "base",
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>{Reader}</xsl:stylesheet>");

            Assert.AreEqual(
                "<out>[over][v]</out>",
                Run(
                    "<xsl:import href=\"base\"/>"
                    + Root("<xsl:apply-templates select=\"/r/b\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"yes\"/></xsl:apply-templates>")
                    + "<xsl:template match=\"b\">[over]<xsl:apply-imports/></xsl:template>",
                    "<r><b/></r>",
                    resolver: modules));
        }

        [TestMethod]
        public void ApplyImportsCanSupplyATunnelParameter()
        {
            MapResolver modules = new MapResolver().Add(
                "base",
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>{Reader}</xsl:stylesheet>");

            Assert.AreEqual(
                "<out>[over][given]</out>",
                Run(
                    "<xsl:import href=\"base\"/>"
                    + Root("<xsl:apply-templates select=\"/r/b\"/>")
                    + "<xsl:template match=\"b\">[over]<xsl:apply-imports>"
                    + "<xsl:with-param name=\"p\" select=\"'given'\" tunnel=\"yes\"/>"
                    + "</xsl:apply-imports></xsl:template>",
                    "<r><b/></r>",
                    resolver: modules));
        }

        [TestMethod]
        public void ATunnelParameterDoesNotCrossIntoAStylesheetFunction()
        {
            // Both routes to the same template, side by side. Through the function the tunnel set starts
            // empty, so the template reads its default; the direct route sees the tunnelled value. A
            // stylesheet function has to be a function of its arguments — that is what lets a call be hoisted
            // or skipped — and a tunnelled value would make two identical calls return different answers.
            // The specification never settled this corner; the choice is recorded in ConformanceNotes.md.
            Assert.AreEqual(
                "<out>[(none)][v]</out>",
                Run(
                    // xmlns:f sits on the xsl: elements that need it rather than on the template, so that the
                    // literal <out> does not inherit it and copy it to the result.
                    Root("<xsl:apply-templates select=\"/r/a\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"yes\"/>"
                        + "</xsl:apply-templates>")
                    + "<xsl:template match=\"a\">"
                    + "<xsl:value-of select=\"f:through(b)\" xmlns:f=\"urn:test\"/>"
                    + "<xsl:apply-templates select=\"b\"/>"
                    + "</xsl:template>"
                    + "<xsl:function name=\"f:through\" xmlns:f=\"urn:test\"><xsl:param name=\"n\"/>"
                    + "<xsl:apply-templates select=\"$n\"/></xsl:function>"
                    + Reader,
                    "<r><a><b/></a></r>"));
        }

        [TestMethod]
        public void AFunctionParameterCannotBeATunnelParameter()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:function name=\"f:x\" xmlns:f=\"urn:test\">"
                    + "<xsl:param name=\"n\" tunnel=\"yes\"/><xsl:sequence select=\"$n\"/></xsl:function>"
                    + Root(string.Empty),
                    "<r/>"));

            StringAssert.Contains(error.Message, "Only a template parameter");
        }

        [TestMethod]
        public void AStylesheetParameterCannotBeATunnelParameter()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run("<xsl:param name=\"p\" tunnel=\"yes\"/>" + Root(string.Empty), "<r/>"));

            StringAssert.Contains(error.Message, "Only a template parameter");
        }

        [TestMethod]
        public void AnUnrecognizedTunnelValueIsRefused()
        {
            // Rather than being read as "no", which would leave the stylesheet running and quietly wrong.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:apply-templates select=\"/r/b\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"maybe\"/></xsl:apply-templates>")
                    + Reader,
                    "<r><b/></r>"));

            Assert.AreEqual("XTSE0020", error.Code);
            StringAssert.Contains(error.Message, "may take: yes, no");
        }

        // ---- xsl:document --------------------------------------------------------------------------------

        [TestMethod]
        public void ADocumentNodeContributesItsChildrenToTheResult()
        {
            // A document node cannot be a child of an element, so writing one writes what it holds. In the
            // output the instruction is therefore invisible, which is the whole of its effect here.
            Assert.AreEqual(
                "<out><a>1</a><b>2</b></out>",
                Run(Root("<xsl:document><a>1</a><b>2</b></xsl:document>"), "<r/>"));
        }

        [TestMethod]
        public void AnEmptyDocumentContributesNothing()
        {
            Assert.AreEqual("<out/>", Run(Root("<xsl:document/>"), "<r/>"));
        }

        [TestMethod]
        public void ADocumentIsANavigableTreeWhenItIsAVariablesValue()
        {
            // What the instruction is for: a document node to run paths against, built where it is needed.
            Assert.AreEqual(
                "<out>2</out>",
                Run(
                    Root("<xsl:variable name=\"v\"><xsl:document><a>1</a><a>2</a></xsl:document></xsl:variable>"
                        + "<xsl:value-of select=\"count($v/a)\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void ADocumentStopsAnAttributeReachingTheEnclosingElement()
        {
            // Without the boundary the attribute would land on <out>. It belongs to the document node, which
            // cannot carry one, so it is refused instead.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:document><xsl:attribute name=\"a\">v</xsl:attribute></xsl:document>"),
                    "<r/>"));

            StringAssert.Contains(error.Message, "cannot be added here");
        }

        [TestMethod]
        public void ADocumentStopsANamespaceReachingTheEnclosingElement()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:document><xsl:namespace name=\"p\">urn:x</xsl:namespace></xsl:document>"),
                    "<r/>"));

            StringAssert.Contains(error.Message, "cannot be added here");
        }

        [TestMethod]
        public void ADocumentMayBeStrippedOrPreserved()
        {
            // Neither asks for anything this engine does not already do: there are no type annotations to
            // preserve, so both readings produce the same untyped tree. XSLT 2.0 refuses preserve from a
            // processor that cannot validate all the same, and 3.0 is where that line moved — see
            // WhichValidationIsRefusedFollowsTheProcessorsVersion.
            Assert.AreEqual(
                "<out><a/><b/></out>",
                Run(
                    Root("<xsl:document validation=\"strip\"><a/></xsl:document>"
                        + "<xsl:document validation=\"preserve\"><b/></xsl:document>"),
                    "<r/>",
                    implemented: XsltVersion.V30));

            Assert.AreEqual(
                "<out><a/></out>",
                Run(Root("<xsl:document validation=\"strip\"><a/></xsl:document>"), "<r/>"));
        }

        [TestMethod]
        [DataRow("strict")]
        [DataRow("lax")]
        public void ADocumentCannotAskForValidation(string validation)
        {
            // Ignoring the request would hand back an unvalidated result to a stylesheet that asked to be
            // told when its result does not fit the schema.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root($"<xsl:document validation=\"{validation}\"><a/></xsl:document>"), "<r/>"));

            StringAssert.Contains(error.Message, "schema-aware processor");
        }

        [TestMethod]
        public void ADocumentCannotNameAType()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:document type=\"xs:string\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                        + "<a/></xsl:document>"),
                    "<r/>"));

            StringAssert.Contains(error.Message, "schema-aware processor");
        }

        [TestMethod]
        public void AnUnrecognizedValidationValueIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:document validation=\"strick\"><a/></xsl:document>"), "<r/>"));

            // Caught by the schema check, which runs before the instruction is compiled and before the
            // required attributes — so a stylesheet asking for validation this engine cannot do hears that,
            // rather than hearing about a second thing wrong with the same element. A value outside the fixed
            // set is still XTSE0020 there, like every other one.
            Assert.AreEqual("XTSE0020", error.Code);
            StringAssert.Contains(error.Message, "not one of strict, lax, preserve or strip");
        }

        // ---- select in place of content --------------------------------------------------------------------

        [TestMethod]
        public void AnAttributeMayTakeItsValueFromSelect()
        {
            Assert.AreEqual(
                "<out v=\"a b\"/>",
                Run(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:attribute name=\"v\" select=\"/r/i\"/></out></xsl:template>",
                    "<r><i>a</i><i>b</i></r>"));
        }

        [TestMethod]
        public void AnAttributeSelectCanChooseItsSeparator()
        {
            Assert.AreEqual(
                "<out v=\"a,b\"/>",
                Run(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:attribute name=\"v\" select=\"/r/i\" separator=\",\"/></out></xsl:template>",
                    "<r><i>a</i><i>b</i></r>"));
        }

        [TestMethod]
        public void ACommentMayTakeItsTextFromSelect()
        {
            Assert.AreEqual(
                "<out><!--a b--></out>",
                Run(Root("<xsl:comment select=\"/r/i\"/>"), "<r><i>a</i><i>b</i></r>"));
        }

        [TestMethod]
        public void AProcessingInstructionMayTakeItsDataFromSelect()
        {
            Assert.AreEqual(
                "<out><?p a b?></out>",
                Run(Root("<xsl:processing-instruction name=\"p\" select=\"/r/i\"/>"), "<r><i>a</i><i>b</i></r>"));
        }

        [TestMethod]
        public void AnUntypedOperandIsReadAsAQNameWhereTheNamespacesSayOne()
        {
            // A general comparison reads an untyped operand as the other operand's type, and for a name
            // that means resolving whatever prefix it holds against the namespaces the comparison was
            // written among — so the same comparison answers differently in each branch of one xsl:choose.
            // XPath 2.0 allowed the cast only over a literal; 3.0 allows it over a computed string and over
            // an untyped value, which is what an attribute out of a document gives.
            Assert.AreEqual(
                "<out>Three</out>",
                Run(
                    "<xsl:param name=\"u\" select=\"xs:untypedAtomic('my:problem')\"/>"
                    + "<xsl:param name=\"q\" select=\"QName('urn:three', 'problem')\"/>"
                    + Root("<xsl:choose>"
                        + "<xsl:when test=\"$u = $q\" xmlns:my=\"urn:one\">One</xsl:when>"
                        + "<xsl:when test=\"$u = $q\" xmlns:my=\"urn:two\">Two</xsl:when>"
                        + "<xsl:when test=\"$u = $q\" xmlns:my=\"urn:three\">Three</xsl:when>"
                        + "<xsl:otherwise>Fail</xsl:otherwise></xsl:choose>"),
                    "<doc/>"));
        }

        [TestMethod]
        public void ACommentKeepsItsHyphensApart()
        {
            // XML has no escaping inside a comment, so two hyphens cannot stand together in one and a
            // comment cannot end with a hyphen. XSLT does not refuse the content for that: the processor
            // puts a space after the offending hyphen, which keeps what the stylesheet wrote legible and
            // the result well formed.
            Assert.AreEqual(
                "<out><!-- the double - - --><!-- the single - --></out>",
                Run(
                    Root("<xsl:comment> the double --</xsl:comment>"
                        + "<xsl:comment> the single -</xsl:comment>"),
                    "<r/>"));

            Assert.AreEqual(
                "<out><!--- -Valid comment- - --></out>",
                Run(Root("<xsl:comment select=\"'--Valid comment--'\"/>"), "<r/>"));

            // A hyphen that runs into neither another nor the end is left where it stands.
            Assert.AreEqual("<out><!--a-b--></out>", Run(Root("<xsl:comment>a-b</xsl:comment>"), "<r/>"));
        }

        [TestMethod]
        public void AProcessingInstructionKeepsItsQuestionMarkFromTheBracket()
        {
            // The same arrangement, and for the same reason: those two characters together would end the
            // instruction where it stands, and a processing instruction has no escaping either.
            Assert.AreEqual(
                "<out><?p a ? >?></out>",
                Run(Root("<xsl:processing-instruction name=\"p\" select=\"'a ?&gt;'\"/>"), "<r/>"));

            // Every one of them, however they run together.
            Assert.AreEqual(
                "<out><?p ----? >? >? >----? >?></out>",
                Run(
                    Root("<xsl:processing-instruction name=\"p\">----?&gt;?&gt;?&gt;----?&gt;"
                        + "</xsl:processing-instruction>"),
                    "<r/>"));

            // Data ending in a single question mark needs nothing: XML ends the instruction at the first
            // '?' followed by '>', and there is none.
            Assert.AreEqual(
                "<out><?p a??></out>",
                Run(Root("<xsl:processing-instruction name=\"p\">a?</xsl:processing-instruction>"), "<r/>"));
        }

        [TestMethod]
        [DataRow("<xsl:comment select=\"'a'\">b</xsl:comment>")]
        [DataRow("<xsl:processing-instruction name=\"p\" select=\"'a'\">b</xsl:processing-instruction>")]
        public void SelectAndContentTogetherAreRefused(string instruction)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root(instruction), "<r/>"));

            StringAssert.Contains(error.Message, "one or the other");
        }

        // ---- copy-namespaces -------------------------------------------------------------------------------

        [TestMethod]
        public void CopyOfBringsNamespacesWithItByDefault()
        {
            Assert.AreEqual(
                "<out><i xmlns:p=\"urn:x\"/></out>",
                Run(Root("<xsl:copy-of select=\"/r/i\"/>"), "<r xmlns:p='urn:x'><i/></r>"));
        }

        [TestMethod]
        public void CopyNamespacesNoLeavesBehindWhatTheCopyDoesNotUse()
        {
            // The declaration nothing in the copied subtree refers to is dropped; a prefix the copy does use
            // is still declared, because the writer supplies what a name needs as it goes.
            Assert.AreEqual(
                "<out><i/></out>",
                Run(
                    Root("<xsl:copy-of select=\"/r/i\" copy-namespaces=\"no\"/>"),
                    "<r xmlns:p='urn:x'><i/></r>"));
        }

        [TestMethod]
        public void CopyNamespacesNoStillDeclaresWhatTheNameNeeds()
        {
            Assert.AreEqual(
                "<out><p:i xmlns:p=\"urn:x\"/></out>",
                Run(
                    Root("<xsl:copy-of select=\"/r/p:i\" xmlns:p=\"urn:x\" copy-namespaces=\"no\"/>"),
                    "<r xmlns:p='urn:x' xmlns:q='urn:unused'><p:i/></r>"));
        }

        [TestMethod]
        public void CopyAlsoHonoursCopyNamespaces()
        {
            Assert.AreEqual(
                "<out><i/></out>",
                Run(
                    Root("<xsl:for-each select=\"/r/i\">"
                        + "<xsl:copy copy-namespaces=\"no\"/></xsl:for-each>"),
                    "<r xmlns:p='urn:x'><i/></r>"));
        }

        // ---- xsl:number additions --------------------------------------------------------------------------

        [TestMethod]
        public void NumberCanCountANodeOtherThanTheContextNode()
        {
            Assert.AreEqual(
                "<out>3</out>",
                Run(Root("<xsl:number select=\"/r/i[3]\"/>"), "<r><i/><i/><i/></r>"));
        }

        [TestMethod]
        [DataRow(1, "1st")]
        [DataRow(2, "2nd")]
        [DataRow(3, "3rd")]
        [DataRow(4, "4th")]
        [DataRow(11, "11th")]
        [DataRow(12, "12th")]
        [DataRow(13, "13th")]
        [DataRow(21, "21st")]
        [DataRow(112, "112th")]
        public void OrdinalNumbersAreWrittenInEnglish(int value, string expected)
        {
            Assert.AreEqual(
                $"<out>{expected}</out>",
                Run(Root($"<xsl:number value=\"{value}\" ordinal=\"yes\"/>"), "<r/>"));
        }

        // ---- Collations, keys, types and output --------------------------------------------------------------

        [TestMethod]
        [DataRow("<xsl:template match=\"/\"><out><xsl:for-each select=\"/r/i\">"
            + "<xsl:sort collation=\"urn:danish\"/></xsl:for-each></out></xsl:template>", "XTDE1035")]
        [DataRow("<xsl:key name=\"k\" match=\"i\" use=\".\" collation=\"urn:danish\"/>"
            + "<xsl:template match=\"/\"><out/></xsl:template>", "XTSE1210")]
        [DataRow("<xsl:template match=\"/\"><out>"
            + "<xsl:for-each-group select=\"/r/i\" group-by=\".\" collation=\"urn:danish\"/>"
            + "</out></xsl:template>", "XTDE1110")]
        public void ACollationThisEngineDoesNotHaveIsRefused(string body, string code)
        {
            // Silently comparing by code point would answer a question about Danish ordering with an answer
            // about Unicode, and nothing in the result would say so. One refusal, but XSLT gives it a code
            // apiece for the three places a collation may be written, so each hears its own.
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(body, "<r><i>a</i></r>"));

            Assert.AreEqual(code, error.Code);
        }

        [TestMethod]
        public void TheCodepointCollationIsAccepted()
        {
            Assert.AreEqual(
                "<out>ab</out>",
                Run(
                    Root("<xsl:for-each select=\"/r/i\">"
                        + "<xsl:sort collation=\"http://www.w3.org/2005/xpath-functions/collation/codepoint\"/>"
                        + "<xsl:value-of select=\".\"/></xsl:for-each>"),
                    "<r><i>b</i><i>a</i></r>"));
        }

        [TestMethod]
        public void KeyCanSearchAnotherDocument()
        {
            // What the third argument is for: under 1.0 a key could only ever look in the document the
            // instruction was reading, which made keys useless for a document loaded on the side.
            Assert.AreEqual(
                "<out>found</out>",
                Run(
                    "<xsl:key name=\"byId\" match=\"i\" use=\"@id\"/>"
                    + Root("<xsl:variable name=\"other\"><t><i id=\"7\">found</i></t></xsl:variable>"
                        + "<xsl:value-of select=\"key('byId', '7', $other)\"/>"),
                    "<r><i id=\"7\">wrong document</i></r>"));
        }

        [TestMethod]
        [DataRow("xs:date", "true")]
        [DataRow("xs:QName", "true")]
        [DataRow("xs:NMTOKENS", "false")]
        [DataRow("xs:nothing", "false")]
        public void TypeAvailableAnswersForTheTypesThisEngineHas(string name, string expected)
        {
            Assert.AreEqual(
                $"<out>{expected}</out>",
                Run(
                    // The prefix goes on the value-of, so the literal out element does not inherit it.
                    Root($"<xsl:value-of select=\"type-available('{name}')\""
                        + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void WhichTypesAreInScopeFollowsTheProcessorsVersion()
        {
            // XSLT 2.0 gives a processor without a schema the primitive types except xs:NOTATION, then
            // xs:integer, xs:anyType, xs:anySimpleType and the five XPath adds, and keeps the rest for a
            // schema-aware one. XSLT 3.0 dropped the distinction and gives every processor the whole of
            // XML Schema Part 2.
            foreach (string name in new[]
            {
                "xs:int", "xs:short", "xs:NCName", "xs:ID", "xs:NOTATION", "xs:ENTITIES", "xs:NMTOKENS",
            })
            {
                Assert.AreEqual("<out>false</out>", TypeAvailable(name, XsltVersion.V20), name);
                Assert.AreEqual("<out>true</out>", TypeAvailable(name, XsltVersion.V30), name);
            }

            // The shorter list, which both versions have.
            foreach (string name in new[]
            {
                "xs:string", "xs:integer", "xs:date", "xs:gYear", "xs:QName", "xs:untypedAtomic",
                "xs:anyAtomicType", "xs:dayTimeDuration", "xs:yearMonthDuration",
            })
            {
                Assert.AreEqual("<out>true</out>", TypeAvailable(name, XsltVersion.V20), name);
                Assert.AreEqual("<out>true</out>", TypeAvailable(name, XsltVersion.V30), name);
            }

            // The names that say where a type sits rather than which values it holds are in scope for both
            // and construct nothing for either, which is where this question and function-available's come
            // apart.
            foreach (string name in new[] { "xs:anyType", "xs:anySimpleType", "xs:untyped" })
            {
                Assert.AreEqual("<out>true</out>", TypeAvailable(name, XsltVersion.V20), name);
                Assert.AreEqual("<out>true</out>", TypeAvailable(name, XsltVersion.V30), name);
            }

            // A name nothing defines, and one XSD 1.1 added that this engine does not carry.
            Assert.AreEqual("<out>false</out>", TypeAvailable("xs:nothing", XsltVersion.V30));
            Assert.AreEqual("<out>false</out>", TypeAvailable("xs:dateTimeStamp", XsltVersion.V30));
        }

        /// <summary>Asks type-available() about one name, of a processor of a stated version.</summary>
        /// <param name="name">The type name to ask about.</param>
        /// <param name="processor">The version the engine says it implements.</param>
        private static string TypeAvailable(string name, XsltVersion processor)
        {
            return Run(
                // The prefix goes on the value-of, so the literal out element does not inherit it.
                Root($"<xsl:value-of select=\"type-available('{name}')\""
                    + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"/>"),
                "<r/>",
                implemented: processor);
        }

        [TestMethod]
        public void AByteOrderMarkCanBeginTheResult()
        {
            Assert.AreEqual(
                "﻿<out/>",
                Run("<xsl:output byte-order-mark=\"yes\"/>" + Root(string.Empty), "<r/>"));
        }

        [TestMethod]
        public void TheResultCanBeNormalized()
        {
            // e followed by a combining acute arrives as two characters and leaves as one.
            Assert.AreEqual(
                "<out>é</out>",
                Run(
                    "<xsl:output normalization-form=\"NFC\"/>" + Root("<xsl:text>e&#x301;</xsl:text>"),
                    "<r/>"));
        }

        [TestMethod]
        public void AnUnknownNormalizationFormIsRefusedByOutput()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run("<xsl:output normalization-form=\"fully-normalized\"/>" + Root(string.Empty), "<r/>"));

            StringAssert.Contains(error.Message, "NFC, NFD, NFKC, NFKD");
        }

        // ---- required parameters -------------------------------------------------------------------------

        [TestMethod]
        public void ARequiredParameterIsBoundFromTheCallSite()
        {
            // The template is declared after the call, which is the ordinary way round for a helper and the
            // reason the check cannot run while the call itself is compiled.
            Assert.AreEqual(
                "<out>[v]</out>",
                Run(
                    Root("<xsl:call-template name=\"t\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\"/></xsl:call-template>")
                    + "<xsl:template name=\"t\"><xsl:param name=\"p\" required=\"yes\"/>"
                    + "[<xsl:value-of select=\"$p\"/>]</xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void ACallThatOmitsARequiredParameterIsRefusedWhenTheStylesheetIsCompiled()
        {
            // A call by name says exactly which template it reaches, so this need not wait for the call to be
            // executed — and a stylesheet whose mistake is on a branch nothing takes today fails tomorrow.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:if test=\"false()\"><xsl:call-template name=\"t\"/></xsl:if>")
                    + "<xsl:template name=\"t\"><xsl:param name=\"p\" required=\"yes\"/>"
                    + "<xsl:value-of select=\"$p\"/></xsl:template>",
                    "<r/>"));

            StringAssert.Contains(error.Message, "does not supply its required parameter 'p'");
        }

        [TestMethod]
        public void ApplyTemplatesWithoutARequiredParameterFailsWhenItRuns()
        {
            // Nothing settles statically which template a pattern will match, so this one waits.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:apply-templates select=\"/r/i\"/>")
                    + "<xsl:template match=\"i\"><xsl:param name=\"p\" required=\"yes\"/>"
                    + "<xsl:value-of select=\"$p\"/></xsl:template>",
                    "<r><i/></r>"));

            StringAssert.Contains(error.Message, "No value was supplied for the required parameter 'p'");
        }

        [TestMethod]
        public void ApplyTemplatesSuppliesARequiredParameterLikeAnyOther()
        {
            Assert.AreEqual(
                "<out>[v]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\"/></xsl:apply-templates>")
                    + "<xsl:template match=\"i\"><xsl:param name=\"p\" required=\"yes\"/>"
                    + "[<xsl:value-of select=\"$p\"/>]</xsl:template>",
                    "<r><i/></r>"));
        }

        [TestMethod]
        public void ARequiredTunnelParameterIsSatisfiedByWhateverIsTunnelling()
        {
            Assert.AreEqual(
                "<out>[v]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/a\">"
                        + "<xsl:with-param name=\"p\" select=\"'v'\" tunnel=\"yes\"/></xsl:apply-templates>")
                    + "<xsl:template match=\"b\"><xsl:param name=\"p\" tunnel=\"yes\" required=\"yes\"/>"
                    + "[<xsl:value-of select=\"$p\"/>]</xsl:template>",
                    "<r><a><b/></a></r>"));
        }

        [TestMethod]
        public void ARequiredTunnelParameterThatNothingTunnelsFails()
        {
            // Not checked at the call site even for xsl:call-template: a tunnel parameter may be put in flight
            // by any invocation further out, so what arrives is not knowable from one call.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:call-template name=\"t\"/>")
                    + "<xsl:template name=\"t\"><xsl:param name=\"p\" tunnel=\"yes\" required=\"yes\"/>"
                    + "<xsl:value-of select=\"$p\"/></xsl:template>",
                    "<r/>"));

            StringAssert.Contains(error.Message, "required tunnel parameter 'p'");
        }

        [TestMethod]
        [DataRow("<xsl:param name=\"p\" required=\"yes\" select=\"'d'\"/>")]
        [DataRow("<xsl:param name=\"p\" required=\"yes\">d</xsl:param>")]
        public void ARequiredParameterCannotAlsoHaveADefault(string parameter)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root(string.Empty) + $"<xsl:template name=\"t\">{parameter}</xsl:template>",
                    "<r/>"));

            StringAssert.Contains(error.Message, "could never be used");
        }

        [TestMethod]
        public void AFunctionParameterIsRequiredAndMaySayNothingElse()
        {
            // Every parameter of a function is required already — a call supplies one argument for each, or
            // it calls a different function — so the only thing the attribute can say is so. XSLT 2.0 does
            // not admit it at all, and refuses it as an attribute the element has not got.
            XsltException refused = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:function name=\"f:x\" xmlns:f=\"urn:test\">"
                    + "<xsl:param name=\"n\" required=\"yes\"/><xsl:sequence select=\"$n\"/></xsl:function>"
                    + Root(string.Empty),
                    "<r/>"));

            Assert.AreEqual("XTSE0090", refused.Code);
        }

        [TestMethod]
        public void ARequiredStylesheetParameterCannotBeSatisfied()
        {
            // Honest about a gap rather than around it: there is no way for a caller to supply a stylesheet
            // parameter yet, so declaring one required means the transformation cannot run. The error is the
            // one the specification gives for a value that was not supplied, which is exactly the case.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run("<xsl:param name=\"p\" required=\"yes\"/>" + Root("x"), "<r/>"));

            StringAssert.Contains(error.Message, "required stylesheet parameter 'p'");
        }

        [TestMethod]
        public void RequiredNoLeavesTheDefaultInPlace()
        {
            Assert.AreEqual(
                "<out>[d]</out>",
                Run(
                    Root("<xsl:call-template name=\"t\"/>")
                    + "<xsl:template name=\"t\"><xsl:param name=\"p\" required=\"no\" select=\"'d'\"/>"
                    + "[<xsl:value-of select=\"$p\"/>]</xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void AnUnrecognizedRequiredValueIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root(string.Empty)
                    + "<xsl:template name=\"t\"><xsl:param name=\"p\" required=\"maybe\"/></xsl:template>",
                    "<r/>"));

            Assert.AreEqual("XTSE0020", error.Code);
            StringAssert.Contains(error.Message, "may take: yes, no");
        }

        // ---- The schema validation attributes --------------------------------------------------------------

        /// <summary>
        /// Every instruction that accepts <c>validation</c> refuses to be asked for what it cannot do.
        /// </summary>
        /// <remarks>
        /// The list is the specification's: <c>xsl:element</c>, <c>xsl:attribute</c>, <c>xsl:copy</c>,
        /// <c>xsl:copy-of</c>, <c>xsl:document</c>, <c>xsl:result-document</c> and a literal result element,
        /// which carries the attribute in the XSLT namespace instead.
        /// </remarks>
        [TestMethod]
        [DataRow("<xsl:element name=\"a\" validation=\"strict\"/>")]
        [DataRow("<xsl:attribute name=\"a\" validation=\"strict\"/>")]
        [DataRow("<xsl:copy validation=\"strict\"/>")]
        [DataRow("<xsl:copy-of select=\"/r\" validation=\"strict\"/>")]
        [DataRow("<xsl:document validation=\"strict\"><a/></xsl:document>")]
        [DataRow("<xsl:result-document href=\"x\" validation=\"strict\"><a/></xsl:result-document>")]
        [DataRow("<a xsl:validation=\"strict\"/>")]
        public void AskingForValidationIsRefusedWhereverItIsWritten(string instruction)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(Root(instruction), "<r/>"));

            StringAssert.Contains(error.Message, "schema-aware processor");
        }

        [TestMethod]
        [DataRow("<xsl:element name=\"a\" type=\"xs:string\"/>")]
        [DataRow("<xsl:attribute name=\"a\" type=\"xs:string\"/>")]
        [DataRow("<xsl:copy type=\"xs:string\"/>")]
        [DataRow("<xsl:copy-of select=\"/r\" type=\"xs:string\"/>")]
        [DataRow("<xsl:document type=\"xs:string\"><a/></xsl:document>")]
        [DataRow("<xsl:result-document href=\"x\" type=\"xs:string\"><a/></xsl:result-document>")]
        [DataRow("<a xsl:type=\"xs:string\"/>")]
        public void NamingATypeIsRefusedWhereverItIsWritten(string instruction)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><out>"
                    + instruction + "</out></xsl:template>",
                    "<r/>"));

            StringAssert.Contains(error.Message, "schema-aware processor");
        }

        [TestMethod]
        public void AnOrdinaryTypeAttributeOnALiteralElementIsPartOfTheResult()
        {
            // The distinction that makes the check above safe: a literal result element carries the request to
            // validate in the XSLT namespace, so an unprefixed type attribute is content and stays content.
            Assert.AreEqual(
                "<out><input type=\"text\"/></out>",
                Run(Root("<input type=\"text\"/>"), "<r/>"));
        }

        [TestMethod]
        [DataRow("<xsl:copy-of select=\"/r\" validation=\"strip\"/>")]
        [DataRow("<a xsl:validation=\"strip\"/>")]
        public void StrippingIsAcceptedWhicheverVersionIsRunning(string instruction)
        {
            // It asks for nothing beyond what an engine with no type annotations does anyway, and it is the
            // one value XSLT 2.0 lets a processor that cannot validate accept at all.
            Assert.AreNotEqual(string.Empty, Run(Root(instruction), "<r/>"));
        }

        [TestMethod]
        public void PreservingIsAcceptedFromThreePointZeroOnly()
        {
            // Preserving asks for nothing this engine cannot do either, there being no annotations to
            // preserve — but XSLT 2.0 lets a basic processor accept nothing but strip, and 3.0 is where the
            // line moved once that had been noticed.
            const string Instruction = "<xsl:copy-of select=\"/r\" validation=\"preserve\"/>";

            Assert.AreNotEqual(
                string.Empty, Run(Root(Instruction), "<r/>", implemented: XsltVersion.V30));

            Assert.AreEqual(
                "XTSE1660",
                Assert.ThrowsExactly<XsltException>(() => Run(Root(Instruction), "<r/>")).Code);
        }

        [TestMethod]
        public void AskingForValidationCarriesTheCodeTheSpecificationGivesIt()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:copy-of select=\"/r\" validation=\"strict\"/>"), "<r/>"));

            Assert.AreEqual("XTSE1660", error.Code);
        }

        [TestMethod]
        public void AnUnrecognizedValidationValueCarriesTheCodeForABadAttributeValue()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:copy-of select=\"/r\" validation=\"strick\"/>"), "<r/>"));

            Assert.AreEqual("XTSE0020", error.Code);
        }

        // ---- xsl:import-schema ---------------------------------------------------------------------------

        [TestMethod]
        public void ImportingASchemaIsRefusedRatherThanIgnored()
        {
            // The one declaration this engine must not shrug off. A stylesheet that imports a schema is
            // written against the types in it; running it with the import dropped would answer typed
            // questions with untyped values and never say so.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:import-schema namespace=\"urn:x\"/>" + Root("<a/>"),
                    "<r/>"));

            Assert.AreEqual("XTSE1650", error.Code);
            StringAssert.Contains(error.Message, "schema-aware processor");
        }

        [TestMethod]
        public void ImportingASchemaIsRefusedEvenWithAnInlineSchema()
        {
            // The schema may be written inline instead of referenced, which changes nothing about whether this
            // engine can act on it.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:import-schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                    + "<xs:schema targetNamespace=\"urn:x\"/></xsl:import-schema>" + Root("<a/>"),
                    "<r/>"));

            Assert.AreEqual("XTSE1650", error.Code);
        }

        [TestMethod]
        public void AnUnknownTopLevelDeclarationIsStillIgnored()
        {
            // The contrast that makes the refusal above a decision rather than a blanket rule. A later
            // version's declaration, met under forwards-compatible processing, costs the stylesheet nothing it
            // asked for: the templates still say what they said. Only a schema import changes what the rest of
            // the stylesheet means.
            Assert.AreEqual(
                "<out><a/></out>",
                Run(
                    "<xsl:accumulator name=\"n\" initial-value=\"0\"/>" + Root("<a/>"),
                    "<r/>",
                    version: "3.0"));
        }

        // ---- xsl:namespace -------------------------------------------------------------------------------

        [TestMethod]
        public void ANamespaceNodeIsAddedToTheElementBeingBuilt()
        {
            Assert.AreEqual(
                "<out xmlns:p=\"urn:x\"/>",
                Run(Root("<xsl:namespace name=\"p\">urn:x</xsl:namespace>"), "<r/>"));
        }

        [TestMethod]
        public void TheUriMayComeFromASelectExpression()
        {
            Assert.AreEqual(
                "<out xmlns:p=\"urn:from-input\"/>",
                Run(Root("<xsl:namespace name=\"p\" select=\"/r/@uri\"/>"), "<r uri='urn:from-input'/>"));
        }

        [TestMethod]
        public void ThePrefixMayBeAValueTemplate()
        {
            // What the instruction is for: neither half of the binding need be known until it runs.
            Assert.AreEqual(
                "<out xmlns:chosen=\"urn:x\"/>",
                Run(Root("<xsl:namespace name=\"{/r/@p}\">urn:x</xsl:namespace>"), "<r p='chosen'/>"));
        }

        [TestMethod]
        public void TheDefaultNamespaceCanBeBoundOnAnElementThatIsInOne()
        {
            Assert.AreEqual(
                "<out xmlns=\"urn:x\"/>",
                Run(
                    "<xsl:template match=\"/\"><out xmlns=\"urn:x\">"
                    + "<xsl:namespace name=\"\">urn:x</xsl:namespace></out></xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void RepeatingABindingTheElementAlreadyHasChangesNothing()
        {
            Assert.AreEqual(
                "<out xmlns:p=\"urn:x\"/>",
                Run(
                    "<xsl:template match=\"/\" xmlns:p=\"urn:x\"><out xmlns:p=\"urn:x\">"
                    + "<xsl:namespace name=\"p\">urn:x</xsl:namespace></out></xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void ANamespaceNodeSurvivesInAResultTreeFragment()
        {
            // The other output target: a namespace node created while a variable is being captured has to
            // reach the tree, or copying the variable out would lose it.
            Assert.AreEqual(
                "<out><held xmlns:p=\"urn:x\"/></out>",
                Run(
                    Root("<xsl:variable name=\"v\"><held>"
                        + "<xsl:namespace name=\"p\">urn:x</xsl:namespace></held></xsl:variable>"
                        + "<xsl:copy-of select=\"$v\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void ANamespaceCannotBeAddedOnceContentHasStarted()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("text<xsl:namespace name=\"p\">urn:x</xsl:namespace>"), "<r/>"));

            StringAssert.Contains(error.Message, "after the element's content has started");
        }

        [TestMethod]
        public void ANamespaceUriCannotBeEmpty()
        {
            // XML 1.0 has no way to undeclare a namespace, so there is nothing this could mean.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:namespace name=\"p\"/>"), "<r/>"));

            StringAssert.Contains(error.Message, "undeclare");
        }

        [TestMethod]
        public void APrefixThatIsNotANameIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:namespace name=\"not a name\">urn:x</xsl:namespace>"), "<r/>"));

            StringAssert.Contains(error.Message, "not a name");
        }

        [TestMethod]
        public void ThePrefixXmlnsIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:namespace name=\"xmlns\">urn:x</xsl:namespace>"), "<r/>"));

            StringAssert.Contains(error.Message, "'xmlns'");
        }

        [TestMethod]
        [DataRow("xml", "urn:x")]
        [DataRow("other", "http://www.w3.org/XML/1998/namespace")]
        public void TheXmlPrefixAndItsNamespaceCannotBeSeparated(string prefix, string uri)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root($"<xsl:namespace name=\"{prefix}\">{uri}</xsl:namespace>"), "<r/>"));

            StringAssert.Contains(error.Message, "bound to each other");
        }

        [TestMethod]
        public void OneElementCannotBindOnePrefixTwice()
        {
            // Serializing both would write the same attribute twice, which is not a document any parser reads.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:namespace name=\"p\">urn:a</xsl:namespace>"
                        + "<xsl:namespace name=\"p\">urn:b</xsl:namespace>"),
                    "<r/>"));

            StringAssert.Contains(error.Message, "cannot bind the prefix 'p' to both");
        }

        [TestMethod]
        public void AnElementInNoNamespaceCannotBeGivenADefaultNamespace()
        {
            // The declaration would apply to the element's own name, so <out> would read back as being in
            // urn:x — a different element from the one the stylesheet built.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:namespace name=\"\">urn:x</xsl:namespace>"), "<r/>"));

            StringAssert.Contains(error.Message, "change what its own name means");
        }

        [TestMethod]
        public void AnElementsOwnPrefixCannotBeReBound()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\" xmlns:p=\"urn:a\"><p:out>"
                    + "<xsl:namespace name=\"p\">urn:b</xsl:namespace></p:out></xsl:template>",
                    "<r/>"));

            StringAssert.Contains(error.Message, "change what its own name means");
        }

        [TestMethod]
        public void ANamespaceMayNotHaveBothASelectAndContent()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:namespace name=\"p\" select=\"'urn:a'\">urn:b</xsl:namespace>"),
                    "<r/>"));

            StringAssert.Contains(error.Message, "both a select attribute and content");
        }

        // ---- xsl:character-map ---------------------------------------------------------------------------

        /// <summary>A map substituting one easily-read character, used by most of these tests.</summary>
        private const string MapsX =
            "<xsl:output use-character-maps=\"m\"/>"
            + "<xsl:character-map name=\"m\">"
            + "<xsl:output-character character=\"x\" string=\"[X]\"/>"
            + "</xsl:character-map>";

        [TestMethod]
        public void AStylesheetIsReadInTheEncodingItDeclares()
        {
            // Handed over as bytes, a stylesheet is decoded as its declaration says: the two non-ASCII
            // characters of this ISO-8859-1 map are two entries, where reading the bytes as UTF-8 would have
            // made both U+FFFD and one entry, the last string winning for every character.
            string text = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>"
                + "<xsl:stylesheet version=\"2.0\" " + Xsl + ">"
                + "<xsl:output use-character-maps=\"m\"/><xsl:character-map name=\"m\">"
                + "<xsl:output-character character=\"\u00ab\" string=\"[&lt;]\"/>"
                + "<xsl:output-character character=\"\u00bb\" string=\"[&gt;]\"/>"
                + "</xsl:character-map>" + Root("<xsl:text>\u00ab=\u00bb</xsl:text>") + "</xsl:stylesheet>";

            Xslt sheet = new Xslt(
                new MemoryStream(System.Text.Encoding.Latin1.GetBytes(text)),
                new XsltOptions { OmitXmlDeclaration = true });

            Assert.AreEqual("<out>[<]=[>]</out>", sheet.TransformXml("<r/>"));
        }

        [TestMethod]
        public void AMapIsAppliedBeforeNormalization()
        {
            // Serialization §11: the map applies to the characters as they stand, and what it substitutes is
            // not normalized. Under NFD the literal ç decomposes, the c the map writes stays inside its
            // composed ç, and the c that decomposing leaves behind is not mapped in turn — in an attribute
            // value as in text.
            Assert.AreEqual(
                "<out a=\"ab#\u00e7#c\u0327de\">ab#\u00e7#c\u0327de</out>",
                Run(
                    "<xsl:output use-character-maps=\"m\" normalization-form=\"NFD\"/>"
                    + "<xsl:character-map name=\"m\"><xsl:output-character character=\"c\" string=\"#\u00e7#\"/></xsl:character-map>"
                    + "<xsl:template match=\"/\"><out a=\"abc\u00e7de\">abc\u00e7de</out></xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void AnEscapedUriAttributeIsNotMapped()
        {
            // Serialization §11: a URI attribute the HTML method escapes is left to the escaping, and is a
            // mapped attribute again once escape-uri-attributes says no.
            const string Body = "<xsl:template match=\"/\"><a href=\"x.html\">x</a></xsl:template>";
            const string Map = "<xsl:character-map name=\"m\"><xsl:output-character character=\"x\" string=\"[X]\"/></xsl:character-map>";

            Assert.AreEqual(
                "<a href=\"x.html\">[X]</a>",
                Run("<xsl:output method=\"html\" use-character-maps=\"m\"/>" + Map + Body, "<r/>"));
            Assert.AreEqual(
                "<a href=\"[X].html\">[X]</a>",
                Run("<xsl:output method=\"html\" use-character-maps=\"m\" escape-uri-attributes=\"no\"/>" + Map + Body, "<r/>"));
        }

        [TestMethod]
        public void AMappedCharacterIsSubstitutedInText()
        {
            Assert.AreEqual(
                "<out>a[X]b</out>",
                Run(MapsX + Root("<xsl:value-of select=\"/r\"/>"), "<r>axb</r>"));
        }

        [TestMethod]
        public void TheReplacementIsWrittenWithoutEscaping()
        {
            // The whole point of the feature, and what disable-output-escaping was reached for before it:
            // the replacement reaches the result as markup, not as an escaped rendering of markup.
            Assert.AreEqual(
                "<out>a<br/>b</out>",
                Run(
                    "<xsl:output use-character-maps=\"m\"/>"
                    + "<xsl:character-map name=\"m\">"
                    + "<xsl:output-character character=\"x\" string=\"&lt;br/&gt;\"/>"
                    + "</xsl:character-map>"
                    + Root("<xsl:value-of select=\"/r\"/>"),
                    "<r>axb</r>"));
        }

        [TestMethod]
        public void AMappedCharacterIsSubstitutedInAnAttributeValue()
        {
            Assert.AreEqual(
                "<out v=\"a[X]b\"/>",
                Run(MapsX + "<xsl:template match=\"/\"><out v=\"{/r}\"/></xsl:template>", "<r>axb</r>"));
        }

        [TestMethod]
        public void CharactersTheMapLeavesAloneAreStillEscaped()
        {
            Assert.AreEqual(
                "<out>&lt;[X]&amp;</out>",
                Run(MapsX + Root("<xsl:value-of select=\"/r\"/>"), "<r>&lt;x&amp;</r>"));
        }

        [TestMethod]
        public void ACharacterMayBeMappedToNothing()
        {
            Assert.AreEqual(
                "<out>ab</out>",
                Run(
                    "<xsl:output use-character-maps=\"m\"/>"
                    + "<xsl:character-map name=\"m\">"
                    + "<xsl:output-character character=\"x\" string=\"\"/>"
                    + "</xsl:character-map>"
                    + Root("<xsl:value-of select=\"/r\"/>"),
                    "<r>axb</r>"));
        }

        [TestMethod]
        public void ACharacterMapMayDrawInAnother()
        {
            Assert.AreEqual(
                "<out>[X][Y]</out>",
                Run(
                    "<xsl:output use-character-maps=\"outer\"/>"
                    + "<xsl:character-map name=\"outer\" use-character-maps=\"inner\">"
                    + "<xsl:output-character character=\"y\" string=\"[Y]\"/>"
                    + "</xsl:character-map>"
                    + "<xsl:character-map name=\"inner\">"
                    + "<xsl:output-character character=\"x\" string=\"[X]\"/>"
                    + "</xsl:character-map>"
                    + Root("<xsl:value-of select=\"/r\"/>"),
                    "<r>xy</r>"));
        }

        [TestMethod]
        public void AMapOverridesTheOneItDrawsIn()
        {
            // Where the same character is substituted twice the last wins, and a map's own declarations are
            // read after what it draws in. See the note in ConformanceNotes.md: the specification says the
            // last mapping wins but leaves the order of the two open.
            Assert.AreEqual(
                "<out>[own]</out>",
                Run(
                    "<xsl:output use-character-maps=\"outer\"/>"
                    + "<xsl:character-map name=\"outer\" use-character-maps=\"inner\">"
                    + "<xsl:output-character character=\"x\" string=\"[own]\"/>"
                    + "</xsl:character-map>"
                    + "<xsl:character-map name=\"inner\">"
                    + "<xsl:output-character character=\"x\" string=\"[drawn in]\"/>"
                    + "</xsl:character-map>"
                    + Root("<xsl:value-of select=\"/r\"/>"),
                    "<r>x</r>"));
        }

        [TestMethod]
        public void TheLastMapNamedOnOutputWins()
        {
            Assert.AreEqual(
                "<out>[second]</out>",
                Run(
                    "<xsl:output use-character-maps=\"first second\"/>"
                    + "<xsl:character-map name=\"first\">"
                    + "<xsl:output-character character=\"x\" string=\"[first]\"/>"
                    + "</xsl:character-map>"
                    + "<xsl:character-map name=\"second\">"
                    + "<xsl:output-character character=\"x\" string=\"[second]\"/>"
                    + "</xsl:character-map>"
                    + Root("<xsl:value-of select=\"/r\"/>"),
                    "<r>x</r>"));
        }

        [TestMethod]
        public void ACharacterMapDoesNotReachNamesCommentsOrProcessingInstructions()
        {
            // It substitutes characters in text and attribute values. Anywhere else it would produce a result
            // that no longer says what the stylesheet built.
            Assert.AreEqual(
                "<out><x/><!--x--><?x x?></out>",
                Run(
                    MapsX
                    + Root("<xsl:element name=\"x\"/><xsl:comment>x</xsl:comment>"
                        + "<xsl:processing-instruction name=\"x\">x</xsl:processing-instruction>"),
                    "<r/>"));
        }

        [TestMethod]
        public void ACharacterOutsideTheBasicPlaneCanBeMapped()
        {
            // Written as a surrogate pair in the stylesheet and in the text alike, so neither half may be
            // matched on its own.
            Assert.AreEqual(
                "<out>a[math A]b</out>",
                Run(
                    "<xsl:output use-character-maps=\"m\"/>"
                    + "<xsl:character-map name=\"m\">"
                    + "<xsl:output-character character=\"&#x1D400;\" string=\"[math A]\"/>"
                    + "</xsl:character-map>"
                    + Root("<xsl:value-of select=\"/r\"/>"),
                    "<r>a&#x1D400;b</r>"));
        }

        [TestMethod]
        public void ACharacterMapAppliesWhereOutputEscapingIsDisabled()
        {
            // The two mechanisms answer different questions — which characters to replace, and whether to
            // protect markup — so disabling escaping does not disable the map.
            Assert.AreEqual(
                "<out>a[X]<b/></out>",
                Run(
                    MapsX
                    + Root("<xsl:value-of select=\"/r\" disable-output-escaping=\"yes\"/>"),
                    "<r>ax&lt;b/&gt;</r>"));
        }

        [TestMethod]
        public void ACharacterMapDoesNotReachIntoACDataSection()
        {
            // The two want opposite things of the same text — one replaces characters on the way out, the
            // other says this text appears exactly as it stands — and the specification gives it to the
            // section.
            Assert.AreEqual(
                "<out><![CDATA[axb]]></out>",
                Run(
                    "<xsl:output use-character-maps=\"m\" cdata-section-elements=\"out\"/>"
                    + "<xsl:character-map name=\"m\">"
                    + "<xsl:output-character character=\"x\" string=\"[X]\"/>"
                    + "</xsl:character-map>"
                    + Root("<xsl:value-of select=\"/r\"/>"),
                    "<r>axb</r>"));
        }

        [TestMethod]
        public void ACharacterTheEncodingCannotHoldInterruptsACDataSection()
        {
            // A character reference is the one way to write such a character, and a CDATA section is the one
            // place a reference means nothing — so the section stops for it and starts again after.
            Assert.AreEqual(
                "<out><![CDATA[a]]>&#x2014;<![CDATA[b]]></out>",
                Run(
                    "<xsl:output encoding=\"ISO-8859-1\" cdata-section-elements=\"out\"/>"
                    + Root("<xsl:value-of select=\"/r\"/>"),
                    "<r>a—b</r>"));
        }

        [TestMethod]
        public void ACharacterMapAppliesToTheTextMethod()
        {
            Assert.AreEqual(
                "a[X]b",
                Run(
                    "<xsl:output method=\"text\" use-character-maps=\"m\"/>"
                    + "<xsl:character-map name=\"m\">"
                    + "<xsl:output-character character=\"x\" string=\"[X]\"/>"
                    + "</xsl:character-map>"
                    + "<xsl:template match=\"/\"><xsl:value-of select=\"/r\"/></xsl:template>",
                    "<r>axb</r>"));
        }

        [TestMethod]
        public void AResultDocumentMayNameItsOwnCharacterMaps()
        {
            (string principal, CollectingResolver results) = RunWithResults(
                "<xsl:output use-character-maps=\"m\"/>"
                + "<xsl:character-map name=\"m\">"
                + "<xsl:output-character character=\"x\" string=\"[principal]\"/>"
                + "</xsl:character-map>"
                + "<xsl:character-map name=\"side\">"
                + "<xsl:output-character character=\"x\" string=\"[side]\"/>"
                + "</xsl:character-map>"
                + Root("<xsl:result-document href=\"side.xml\" use-character-maps=\"side\">"
                    + "<side>x</side></xsl:result-document>x"),
                "<r/>");

            Assert.AreEqual("<out>[principal]</out>", principal);
            Assert.AreEqual("<side>[side]</side>", results.Documents["side.xml"].ToString());
        }

        [TestMethod]
        public void AResultDocumentInheritsTheCharacterMapItDoesNotOverride()
        {
            (string _, CollectingResolver results) = RunWithResults(
                "<xsl:output use-character-maps=\"m\"/>"
                + "<xsl:character-map name=\"m\">"
                + "<xsl:output-character character=\"x\" string=\"[X]\"/>"
                + "</xsl:character-map>"
                + Root("<xsl:result-document href=\"side.xml\"><side>x</side></xsl:result-document>"),
                "<r/>");

            Assert.AreEqual("<side>[X]</side>", results.Documents["side.xml"].ToString());
        }

        [TestMethod]
        public void AnImportedCharacterMapIsReplacedByTheImportingModule()
        {
            MapResolver modules = new MapResolver().Add(
                "base",
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:character-map name=\"m\">"
                + "<xsl:output-character character=\"x\" string=\"[imported]\"/>"
                + "</xsl:character-map></xsl:stylesheet>");

            Assert.AreEqual(
                "<out>[importing]</out>",
                Run(
                    "<xsl:import href=\"base\"/>"
                    + "<xsl:output use-character-maps=\"m\"/>"
                    + "<xsl:character-map name=\"m\">"
                    + "<xsl:output-character character=\"x\" string=\"[importing]\"/>"
                    + "</xsl:character-map>"
                    + Root("<xsl:value-of select=\"/r\"/>"),
                    "<r>x</r>",
                    resolver: modules));
        }

        [TestMethod]
        public void TwoCharacterMapsOfOneNameAtTheSamePrecedenceAreRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:character-map name=\"m\"/><xsl:character-map name=\"m\"/>" + Root(string.Empty),
                    "<r/>"));

            StringAssert.Contains(error.Message, "same import precedence");
        }

        [TestMethod]
        public void ACharacterMapThatUsesItselfIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:character-map name=\"a\" use-character-maps=\"b\"/>"
                    + "<xsl:character-map name=\"b\" use-character-maps=\"a\"/>"
                    + Root(string.Empty),
                    "<r/>"));

            StringAssert.Contains(error.Message, "uses itself");
        }

        [TestMethod]
        public void NamingACharacterMapThatDoesNotExistIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run("<xsl:output use-character-maps=\"missing\"/>" + Root(string.Empty), "<r/>"));

            StringAssert.Contains(error.Message, "No xsl:character-map is named 'missing'");
        }

        [TestMethod]
        public void ACharacterMapMayOnlyContainOutputCharacters()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:character-map name=\"m\"><xsl:text>no</xsl:text></xsl:character-map>"
                    + Root(string.Empty),
                    "<r/>"));

            // Caught by the general content-model check now, which gives it the code the specification does.
            Assert.AreEqual("XTSE0010", error.Code);
            StringAssert.Contains(error.Message, "holds only xsl:output-character");
        }

        [TestMethod]
        public void TheSubstitutedCharacterMustBeExactlyOne()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:character-map name=\"m\">"
                    + "<xsl:output-character character=\"ab\" string=\"x\"/></xsl:character-map>"
                    + Root(string.Empty),
                    "<r/>"));

            StringAssert.Contains(error.Message, "not a single character");
        }

        // ---- What the specification allows on an XSLT element ----------------------------------------------

        /// <remarks>
        /// None of this changes what a correct stylesheet does. What it changes is what an incorrect one
        /// hears: a misspelled attribute used to be read as absent and the stylesheet compiled around the
        /// hole, so <c>&lt;xsl:sort ordr="descending"/&gt;</c> sorted ascending and said nothing at all.
        /// </remarks>
        private static string Refuse(string body, string version = "2.0")
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(body, "<r/>", version));
            return error.Code ?? "(no code)";
        }

        [TestMethod]
        public void AnAttributeXsltDoesNotDefineForTheElementIsRefused()
        {
            Assert.AreEqual(
                "XTSE0090",
                Refuse(Root("<xsl:for-each select=\"1\"><xsl:sort ordr=\"descending\"/></xsl:for-each>")));
            Assert.AreEqual("XTSE0090", Refuse("<xsl:variable name=\"v\" department=\"x\"/>" + Root(string.Empty)));
            Assert.AreEqual("XTSE0090", Refuse(Root("<xsl:copy-of select=\".\" match=\"a\"/>")));
        }

        [TestMethod]
        public void AStandardAttributeTakesThePrefixOnlyOnALiteralResultElement()
        {
            // xsl:xpath-default-namespace on an XSLT element is an error rather than a long-winded way of
            // writing xpath-default-namespace.
            Assert.AreEqual(
                "XTSE0090",
                Refuse(Root("<xsl:value-of select=\"1\" xsl:xpath-default-namespace=\"urn:x\"/>")));

            // Unprefixed, the same attribute is one every XSLT element may carry.
            Assert.AreEqual("<out>1</out>", Run(Root("<xsl:value-of select=\"1\" xpath-default-namespace=\"urn:x\"/>"), "<r/>"));
        }

        [TestMethod]
        public void AnAttributeInSomeOtherNamespaceIsLeftAlone()
        {
            Assert.AreEqual(
                "<out xmlns:v=\"urn:vendor\">1</out>",
                Run("<xsl:template match=\"/\" xmlns:v=\"urn:vendor\" v:hint=\"fast\">"
                    + "<out><xsl:value-of select=\"1\"/></out></xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void AMissingRequiredAttributeIsRefused()
        {
            Assert.AreEqual("XTSE0010", Refuse(Root("<xsl:for-each><a/></xsl:for-each>")));
            Assert.AreEqual("XTSE0010", Refuse(Root("<xsl:if><a/></xsl:if>")));
            Assert.AreEqual("XTSE0010", Refuse("<xsl:key name=\"k\"/>" + Root(string.Empty)));
        }

        [TestMethod]
        public void AnElementXsltDoesNotDefineIsRefusedUnlessTheStylesheetIsNewer()
        {
            // xsl:iterate is an XSLT 3.0 element, and in a 2.0 stylesheet that means it is not an element at
            // all — whatever this engine has since implemented. A stylesheet saying version="2.0" gets the
            // language it asked for, which is what lets it rely on the fallback it wrote.
            Assert.AreEqual("XTSE0010", Refuse(Root("<xsl:iterate select=\"1\"/>")));

            // Under forwards-compatible processing an instruction this engine does not have is expected
            // rather than wrong: the stylesheet was written for a version beyond it, which is the whole
            // point of the mechanism.
            Assert.AreEqual(
                "<out>fell back</out>",
                Run(Root("<xsl:merge><xsl:fallback>fell back</xsl:fallback></xsl:merge>"),
                    "<r/>",
                    version: "3.0"));
        }

        [TestMethod]
        public void AnElementWrittenWhereItDoesNotBelongIsRefused()
        {
            // An instruction at the top level, and a declaration in a template.
            Assert.AreEqual("XTSE0010", Refuse("<xsl:apply-imports/>" + Root(string.Empty)));
            Assert.AreEqual("XTSE0010", Refuse(Root("<xsl:key name=\"k\" match=\"a\" use=\"@b\"/>")));

            // xsl:sort belongs to the instruction that sorts, not to a template body.
            Assert.AreEqual("XTSE0010", Refuse(Root("<xsl:sort select=\".\"/>")));
        }

        [TestMethod]
        public void ContentTheElementCannotHoldIsRefused()
        {
            Assert.AreEqual(
                "XTSE0010",
                Refuse(Root("<xsl:choose><xsl:when test=\"1\">a</xsl:when>stray text</xsl:choose>")));

            Assert.AreEqual(
                "XTSE0010",
                Refuse(Root("<xsl:call-template name=\"t\"><a/></xsl:call-template>")
                    + "<xsl:template name=\"t\"/>"));

            Assert.AreEqual("XTSE0010", Refuse(Root("<xsl:apply-imports><xsl:fallback/></xsl:apply-imports>")));
        }

        [TestMethod]
        public void ADeclarationThatMustComeFirstIsRefusedAfterContent()
        {
            // A parameter declared after the first instruction is a parameter that was never declared, and a
            // sort key after it is a sort that never happened.
            Assert.AreEqual(
                "XTSE0010",
                Refuse("<xsl:template match=\"/\"><out/><xsl:param name=\"p\"/></xsl:template>"));

            Assert.AreEqual(
                "XTSE0010",
                Refuse(Root("<xsl:for-each select=\"1\"><a/><xsl:sort select=\".\"/></xsl:for-each>")));
        }

        [TestMethod]
        public void AValueOutsideTheFixedSetIsRefused()
        {
            Assert.AreEqual("XTSE0020", Refuse(Root("<xsl:value-of select=\"1\" disable-output-escaping=\"YES\"/>")));
            Assert.AreEqual("XTSE0020", Refuse("<xsl:output indent=\"true\"/>" + Root(string.Empty)));
            Assert.AreEqual(
                "XTSE0020",
                Refuse(Root("<xsl:for-each select=\"1\"><xsl:sort select=\".\" stable=\"maybe\"/></xsl:for-each>")));
        }

        [TestMethod]
        public void WhitespaceAroundAYesOrNoIsNotPartOfTheValue()
        {
            // These attributes are token-typed, so " no " is no with whitespace around it rather than a
            // third value that is neither.
            Assert.AreEqual(
                "<out>&lt;</out>",
                Run(Root("<xsl:value-of select=\"'&lt;'\" disable-output-escaping=\" no \"/>"), "<r/>"));
        }

        [TestMethod]
        public void ANameThatIsNotANameIsRefused()
        {
            Assert.AreEqual("XTSE0020", Refuse("<xsl:variable name=\"x/y\"/>" + Root(string.Empty)));
            Assert.AreEqual("XTSE0020", Refuse("<xsl:attribute-set name=\"12foo\"/>" + Root(string.Empty)));

            // An attribute value template where a name was wanted: xsl:element computes its name and
            // xsl:decimal-format does not.
            Assert.AreEqual(
                "XTSE0020",
                Refuse("<xsl:decimal-format name=\"{concat('f','f')}\"/>" + Root(string.Empty)));
        }

        // ---- xsl:value-of with content ---------------------------------------------------------------------

        [TestMethod]
        public void ValueOfMayTakeItsContentInPlaceOfASelect()
        {
            Assert.AreEqual(
                "<out>abc</out>",
                Run(Root("<xsl:value-of>a<xsl:text>b</xsl:text>c</xsl:value-of>"), "<r/>"));
        }

        [TestMethod]
        public void ValueOfWithContentSeesASequenceRatherThanAFragment()
        {
            // The content is a sequence constructor, so three integers are three items to be joined — not
            // the string value of a tree they were built into.
            Assert.AreEqual(
                "<out>1-2-3</out>",
                Run(Root("<xsl:value-of separator=\"-\"><xsl:sequence select=\"1 to 3\"/></xsl:value-of>"), "<r/>"));
        }

        [TestMethod]
        public void TheDefaultSeparatorIsASpaceForASelectAndNothingForContent()
        {
            Assert.AreEqual("<out>1 2 3</out>", Run(Root("<xsl:value-of select=\"1 to 3\"/>"), "<r/>"));
            Assert.AreEqual(
                "<out>123</out>",
                Run(Root("<xsl:value-of><xsl:sequence select=\"1 to 3\"/></xsl:value-of>"), "<r/>"));
        }

        [TestMethod]
        public void ValueOfMustSayWhatToWriteOneWayOrTheOther()
        {
            XsltException both = Assert.ThrowsExactly<XsltException>(() =>
                Run(Root("<xsl:value-of select=\"1\">x</xsl:value-of>"), "<r/>"));

            Assert.AreEqual("XTSE0870", both.Code);

            XsltException neither = Assert.ThrowsExactly<XsltException>(() =>
                Run(Root("<xsl:value-of/>"), "<r/>"));

            Assert.AreEqual("XTSE0870", neither.Code);
        }

        [TestMethod]
        public void UnderXsltOneValueOfStillTakesItsContent()
        {
            // A version="1.0" asks for 1.0's behaviour and not for its content model: what may stand in for
            // the select is the processor's question. XSLT 1.0 gave xsl:value-of an empty content model and
            // 2.0 made it a sequence constructor, and this processor is at least 2.0 — so the content is
            // read and written, which is what the suite's version-021 asks for.
            Assert.AreEqual(
                "<out>a</out>",
                Run(Root("<xsl:value-of>a</xsl:value-of>"), "<r/>", version: "1.0"));
        }

        [TestMethod]
        public void SayingNeitherIsAnErrorUntilThreePointZero()
        {
            // 3.0 lets an xsl:value-of say nothing at all and write nothing, where 2.0 requires one of the
            // two. Which it is follows the processor rather than what the stylesheet says of itself: the
            // suite pairs its two tests over one version="2.0" stylesheet and wants an error from the one
            // processor and an empty result from the other.
            Assert.AreEqual(
                "<out/>",
                Run(Root("<xsl:value-of/>"), "<r/>", implemented: XsltVersion.V30));

            Assert.AreEqual(
                "XTSE0870",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Root("<xsl:value-of/>"), "<r/>")).Code);
        }

        // ---- Kind tests in a pattern -----------------------------------------------------------------------

        /// <remarks>
        /// A pattern's node test is the same production as a path's, and this engine had a second, shorter
        /// copy of the parser for it. Everything here is a test a path two lines away could already make.
        /// </remarks>
        [TestMethod]
        public void APatternMayUseTheKindTestsAPathCanUse()
        {
            Assert.AreEqual(
                "<out>[e:doc][e:a]1[e:b]2</out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//node()\"/></out></xsl:template>"
                    + "<xsl:template match=\"element()\">[e:<xsl:value-of select=\"name()\"/>]</xsl:template>",
                    "<doc><a>1</a><b>2</b></doc>"));
        }

        [TestMethod]
        public void ANamedKindTestInAPatternPicksOutThatNameOnly()
        {
            Assert.AreEqual(
                "<out>[a]2[a]122</out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//node()\"/></out></xsl:template>"
                    + "<xsl:template match=\"element(a)\">[a]</xsl:template>",
                    "<doc><a>1</a><b>2</b></doc>"));
        }

        [TestMethod]
        public void APatternMayMatchTheDocumentNodeAndStepDownFromIt()
        {
            Assert.AreEqual(
                "<out>[deep]</out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"doc/a\"/></out></xsl:template>"
                    + "<xsl:template match=\"document-node()/doc/element(a)\">[deep]</xsl:template>",
                    "<doc><a>1</a></doc>"));
        }

        [TestMethod]
        public void APatternMayNameALocalPartInAnyNamespace()
        {
            Assert.AreEqual(
                "<out>[any][any]</out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//*[local-name()='a']\"/></out></xsl:template>"
                    + "<xsl:template match=\"*:a\">[any]</xsl:template>",
                    "<doc xmlns:p=\"urn:p\"><a/><p:a/></doc>"));
        }

        [TestMethod]
        public void AProcessingInstructionTargetMayBeWrittenAsAName()
        {
            // processing-instruction(go) and processing-instruction('go') are the same test, and the name
            // form is the one anyone writes.
            Assert.AreEqual(
                "<out>[go]</out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"doc/processing-instruction()\"/></out></xsl:template>"
                    + "<xsl:template match=\"processing-instruction(go)\">[go]</xsl:template>",
                    "<doc><?go now?></doc>"));
        }

        // ---- Instructions over a sequence ------------------------------------------------------------------

        /// <remarks>
        /// The whole of XSLT 2.0's change to the value model, seen from the instructions: what a select
        /// expression yields is a sequence, and only some sequences are node-sets. Every one of these was
        /// refused with "a node-set was required" until the XSLT suite asked.
        /// </remarks>
        [TestMethod]
        public void ForEachIteratesASequenceOfNodesWrittenWithACommaAsItDoesAUnion()
        {
            Assert.AreEqual(
                Run(Root("<xsl:for-each select=\"/r/a | /r/b\"><i><xsl:value-of select=\".\"/></i></xsl:for-each>"),
                    "<r><a>1</a><b>2</b></r>"),
                Run(Root("<xsl:for-each select=\"(/r/a, /r/b)\"><i><xsl:value-of select=\".\"/></i></xsl:for-each>"),
                    "<r><a>1</a><b>2</b></r>"));
        }

        [TestMethod]
        public void ForEachIteratesARangeAndTheContextItemIsTheNumber()
        {
            Assert.AreEqual(
                "<out><i>1</i><i>2</i><i>3</i></out>",
                Run(Root("<xsl:for-each select=\"1 to 3\"><i><xsl:value-of select=\".\"/></i></xsl:for-each>"),
                    "<r/>"));
        }

        [TestMethod]
        public void ForEachOverASequenceKeepsItsOrderAndItsDuplicates()
        {
            // A node-set would have sorted these into document order and dropped the repeat; a sequence is
            // neither sorted nor deduplicated, and that is the difference the two models turn on.
            Assert.AreEqual(
                "<out><i>2</i><i>1</i><i>1</i></out>",
                Run(Root("<xsl:for-each select=\"(/r/b, /r/a, /r/a)\"><i><xsl:value-of select=\".\"/></i></xsl:for-each>"),
                    "<r><a>1</a><b>2</b></r>"));
        }

        [TestMethod]
        public void ForEachOverAMixtureOfNodesAndValuesPositionsOnEachInTurn()
        {
            Assert.AreEqual(
                "<out><i>1</i><i>x</i><i>7</i></out>",
                Run(Root("<xsl:for-each select=\"(/r/a, 'x', 7)\"><i><xsl:value-of select=\".\"/></i></xsl:for-each>"),
                    "<r><a>1</a></r>"));
        }

        [TestMethod]
        public void PositionAndLastCountTheSequenceRatherThanTheNodesInIt()
        {
            Assert.AreEqual(
                "<out><i>1/3</i><i>2/3</i><i>3/3</i></out>",
                Run(Root("<xsl:for-each select=\"('a', 'b', 'c')\">"
                        + "<i><xsl:value-of select=\"position()\"/>/<xsl:value-of select=\"last()\"/></i>"
                        + "</xsl:for-each>"),
                    "<r/>"));
        }

        [TestMethod]
        public void PerformSortOrdersASequenceItWasGivenRatherThanAUnion()
        {
            // Nodes rather than numbers, deliberately: a sorted sequence of atomic values written into an
            // element is the separate question of how adjacent atomic values are separated, which this
            // engine does not yet answer across instruction boundaries.
            Assert.AreEqual(
                "<out><a>1</a><b>2</b></out>",
                Run(Root("<xsl:perform-sort select=\"(/r/b, /r/a)\">"
                        + "<xsl:sort select=\".\"/></xsl:perform-sort>"),
                    "<r><a>1</a><b>2</b></r>"));
        }

        [TestMethod]
        public void ForEachSortsASequenceOfValuesBeforeIteratingIt()
        {
            Assert.AreEqual(
                "<out><i>a</i><i>b</i><i>c</i></out>",
                Run(Root("<xsl:for-each select=\"('c', 'a', 'b')\"><xsl:sort select=\".\"/>"
                        + "<i><xsl:value-of select=\".\"/></i></xsl:for-each>"),
                    "<r/>"));
        }

        [TestMethod]
        public void ApplyTemplatesTakesASequenceOfNodes()
        {
            Assert.AreEqual(
                "<out><i>1</i><i>2</i></out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"(/r/a, /r/b)\"/></out></xsl:template>"
                    + "<xsl:template match=\"a|b\"><i><xsl:value-of select=\".\"/></i></xsl:template>",
                    "<r><a>1</a><b>2</b></r>"));
        }

        [TestMethod]
        public void ApplyTemplatesStillRefusesASequenceHoldingSomethingThatIsNotANode()
        {
            // There is no template to match against the number 7, so this one is an error rather than a
            // widening — and the specification has a code for exactly it.
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"(/r/a, 7)\"/></out></xsl:template>",
                "<r><a>1</a></r>"));

            Assert.AreEqual("XTTE0520", error.Code);
        }

        [TestMethod]
        public void ForEachGroupGroupsAPopulationWrittenAsASequence()
        {
            Assert.AreEqual(
                "<out><g>1 3</g><g>2</g></out>",
                Run(Root("<xsl:for-each-group select=\"(/r/i, /r/j)\" group-by=\"@k\">"
                        + "<g><xsl:value-of select=\"current-group()\"/></g></xsl:for-each-group>"),
                    "<r><i k='a'>1</i><i k='b'>2</i><j k='a'>3</j></r>"));
        }

        [TestMethod]
        public void KeyLooksUpEveryValueOfASequence()
        {
            Assert.AreEqual(
                "<out>1 2</out>",
                Run("<xsl:key name=\"k\" match=\"i\" use=\"@k\"/>"
                    + Root("<xsl:value-of select=\"key('k', ('a', 'b'))\"/>"),
                    "<r><i k='a'>1</i><i k='b'>2</i><i k='c'>3</i></r>"));
        }

        [TestMethod]
        public void NumberTakesTheNodeToCountFromASequence()
        {
            Assert.AreEqual(
                "<out>2</out>",
                Run(Root("<xsl:number select=\"(/r/i[2])\" level=\"single\" count=\"i\"/>"),
                    "<r><i/><i/><i/></r>"));
        }

        // ---- What xsl:number's format really says -----------------------------------------------------------

        /// <remarks>
        /// The prefix and the suffix belong to the format, not to any number in it, so they are written even
        /// when the list is empty — which is what a node with no matching ancestors produces.
        /// </remarks>
        [TestMethod]
        [DataRow("A.", ".")]
        [DataRow("(1.)", "(.)")]
        [DataRow("[1]", "[]")]
        public void ANumberFormatKeepsItsPunctuationWithNoNumbersToPutInIt(string format, string expected)
        {
            Assert.AreEqual(
                $"<out>{expected}</out>",
                Run(Root($"<xsl:number level=\"multiple\" count=\"nothing\" format=\"{format}\"/>"), "<r/>"));
        }

        [TestMethod]
        public void AFormatOfPunctuationAloneBracketsTheNumber()
        {
            // The one token is both the first and the last, so it is both the prefix and the suffix.
            Assert.AreEqual("<out>*1*</out>", Run(Root("<xsl:number value=\"1\" format=\"*\"/>"), "<r/>"));
        }

        [TestMethod]
        [DataRow("5 to 8", "(1)", "(5.6.7.8)")]
        [DataRow("5 to 8", "1;1)", "5;6;7;8)")]
        [DataRow("(10, 20)", "1-1", "10-20")]
        public void NumberRendersAWholeSequenceOfValues(string value, string format, string expected)
        {
            Assert.AreEqual(
                $"<out>{expected}</out>",
                Run(Root($"<xsl:number value=\"{value}\" format=\"{format}\"/>"), "<r/>"));
        }

        [TestMethod]
        [DataRow("w", "twenty-one")]
        [DataRow("W", "TWENTY-ONE")]
        [DataRow("Ww", "Twenty-One")]
        public void ANumberMayBeWrittenInWords(string format, string expected)
        {
            // The sequences fn:format-integer already reads, rather than a second implementation of them.
            Assert.AreEqual(
                $"<out>{expected}</out>",
                Run(Root($"<xsl:number value=\"21\" format=\"{format}\"/>"), "<r/>"));
        }

        [TestMethod]
        public void AnOrdinalIsAskedOfTheSequenceRatherThanSuffixed()
        {
            Assert.AreEqual(
                "<out>third</out>",
                Run(Root("<xsl:number value=\"3\" format=\"w\" ordinal=\"yes\"/>"), "<r/>"));
        }

        [TestMethod]
        [DataRow("'fizz'")]
        [DataRow("-99.83")]
        public void AValueThatIsNotANonNegativeIntegerIsRefused(string value)
        {
            Assert.AreEqual(
                "XTDE0980",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Root($"<xsl:number value=\"{value}\"/>"), "<r/>")).Code);
        }

        [TestMethod]
        public void BackwardsCompatibleProcessingReportsAValueThatIsNotANumberAsNaN()
        {
            // XSLT 1.0 converts the value with number() and formats whatever comes back.
            Assert.AreEqual(
                "<out>NaN</out>",
                Run(Root("<xsl:number value=\"'fizz'\"/>"), "<r/>", version: "1.0"));
        }

        [TestMethod]
        public void NumberingTheContextItemNeedsItToBeANode()
        {
            Assert.AreEqual(
                "XTTE0990",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Root("<xsl:for-each select=\"1 to 3\"><xsl:number/></xsl:for-each>"), "<r/>")).Code);
        }

        [TestMethod]
        [DataRow("/r/nothing")]
        [DataRow("/r/i")]
        public void TheSelectOfNumberNamesExactlyOneNode(string select)
        {
            Assert.AreEqual(
                "XTTE1000",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Root($"<xsl:number select=\"{select}\"/>"), "<r><i/><i/></r>")).Code);
        }

        [TestMethod]
        public void AMisspeltLanguageIsRefusedWhereItIsWritten()
        {
            // The language itself does nothing here, this engine carrying only English. Being unsupported is
            // not the same as being misspelt, though, so the value is still checked.
            Assert.AreEqual(
                "XTSE0020",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Root("<xsl:number value=\"1\" lang=\"#####\"/>"), "<r/>")).Code);
        }

        [TestMethod]
        public void AMisspeltLanguageThatWasComputedIsRefusedWhereItIsUsed()
        {
            Assert.AreEqual(
                "XTDE0030",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(
                        "<xsl:param name=\"l\" select=\"'##'\"/>"
                        + Root("<xsl:number value=\"1\" lang=\"{$l}\"/>"),
                        "<r/>")).Code);
        }

        [TestMethod]
        public void FromNamesWhereNumberingRestartsRatherThanWhatToLeaveOut()
        {
            // The node matching from is the last one considered, and it is counted itself when it also
            // matches count — so numbering from="chapter" still contributes the chapter's own number.
            Assert.AreEqual(
                "<out>1.1</out>",
                Run("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//title\">"
                    + "<xsl:number level=\"multiple\" from=\"chapter\" count=\"chapter|section\" format=\"1.1\"/>"
                    + "</xsl:for-each></out></xsl:template>",
                    "<doc><chapter><section><title/></section></chapter></doc>"));
        }

        [TestMethod]
        public void CurrentInACountPatternIsTheNodeBeingTested()
        {
            // A pattern is asked about a node, and its own subject is the node it is asked about — which is
            // what makes count="*[name()=name(current())]/*" mean "an element whose parent shares its name".
            Assert.AreEqual(
                "<out>1|2|3|</out>",
                Run("<xsl:template match=\"/\"><out><xsl:for-each select=\"root/foo\">"
                    + "<xsl:number count=\"foo[@bar = current()/@bar]\"/><xsl:text>|</xsl:text>"
                    + "</xsl:for-each></out></xsl:template>",
                    "<root><foo bar=\"a\"/><foo bar=\"b\"/><foo bar=\"a\"/></root>"));
        }

        // ---- use-when ----------------------------------------------------------------------------------------

        /// <remarks>
        /// The expression is answered as the stylesheet is compiled, and a false answer removes the element
        /// and everything in it from the <em>stylesheet</em> — which is what lets one stylesheet be written
        /// for several processors, each reading only the part meant for it.
        /// </remarks>
        [TestMethod]
        public void UseWhenRemovesADeclarationFromTheStylesheet()
        {
            Assert.AreEqual(
                "<out>[plain]</out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"r\"/></out></xsl:template>"
                    + "<xsl:template match=\"r\" use-when=\"false()\">[removed]</xsl:template>"
                    + "<xsl:template match=\"r\">[plain]</xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void WhatUseWhenRemovesIsNeverCompiled()
        {
            // The point of the attribute: what is inside may be syntax from a later version, an extension
            // this processor has never heard of, or an outright mistake.
            Assert.AreEqual(
                "<out>ok</out>",
                Run(Root("ok")
                    + "<xsl:template match=\"nothing\" use-when=\"false()\">"
                    + "<xsl:value-of my=\"word\"/><xsl:not-an-instruction/></xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void ALiteralResultElementIsRemovedByTheXsltFormOfTheAttribute()
        {
            Assert.AreEqual(
                "<out><kept/></out>",
                Run(Root("<gone xsl:use-when=\"false()\"><inside/></gone><kept/>"), "<r/>"));
        }

        [TestMethod]
        public void NothingTheStylesheetDeclaresIsInScopeInAUseWhen()
        {
            // It decides whether a piece of the stylesheet exists, and is answered before the rest of the
            // stylesheet has been read — so a variable it named could itself be inside a piece another
            // use-when removed.
            Assert.AreEqual(
                "XPST0008",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(
                        "<xsl:variable name=\"v\" select=\"1\"/>"
                        + "<xsl:template match=\"/\" use-when=\"$v = 1\"><out/></xsl:template>",
                        "<r/>")).Code);
        }

        [TestMethod]
        public void AStylesheetFunctionCannotBeCalledFromAUseWhen()
        {
            Assert.AreEqual(
                "XPST0017",
                Assert.ThrowsExactly<XsltException>(
                    () => RunWithFunctions(
                        "<xsl:function name=\"f:yes\"><xsl:sequence select=\"true()\"/></xsl:function>"
                        + "<xsl:template match=\"/\" use-when=\"f:yes()\"><out/></xsl:template>",
                        "<r/>")).Code);
        }

        [TestMethod]
        public void OnTheStylesheetElementUseWhenRemovesTheContentRatherThanTheStylesheet()
        {
            // There would be no stylesheet left to have written the attribute, so what it removes is
            // everything inside — and a stylesheet with no rules falls through to the built-in ones.
            Assert.AreEqual(
                "text",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl} use-when=\"false()\">"
                    + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r>text</r>"));
        }

        [TestMethod]
        public void SystemPropertyReportsTheVersionAsAStringFromTwoPointZero()
        {
            // XSLT 1.0 made xsl:version a number and 2.0 makes every property a string, so a stylesheet
            // compares the answer the way its own version says to.
            Assert.AreEqual("<out>2.0</out>", Run(Root("<xsl:value-of select=\"system-property('xsl:version')\"/>"), "<r/>"));
        }

        [TestMethod]
        [DataRow("fn:doc")]
        [DataRow("doc")]
        [DataRow("fn:tokenize")]
        public void FunctionAvailableKnowsTheXPathTwoLibrary(string name)
        {
            // The core library lives in the function namespace, which is also what an unprefixed name in a
            // call means, so both spellings ask the same question. The prefix has to be declared all the
            // same: the name is resolved against the stylesheet's namespaces, not against XPath's own.
            Assert.AreEqual(
                "<out>true</out>",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {XslOnly} xmlns:fn=\"http://www.w3.org/2005/xpath-functions\""
                    + " exclude-result-prefixes=\"fn\">"
                    + Root($"<xsl:value-of select=\"function-available('{name}')\"/>")
                    + "</xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }

        // ---- xml:base ------------------------------------------------------------------------------------------

        /// <summary>Runs a stylesheet that has a base URI, and optionally an input that has one too.</summary>
        /// <param name="body">The stylesheet's declarations.</param>
        /// <param name="input">The source document.</param>
        /// <param name="baseUri">Where the stylesheet is.</param>
        /// <param name="attributes">Extra attributes for the outermost element.</param>
        /// <param name="inputUri">Where the input came from, which is a different question.</param>
        private static string RunWithBase(
            string body,
            string input,
            string baseUri,
            string attributes = "",
            string? inputUri = null)
        {
            return new Xslt(
                $"<xsl:stylesheet version=\"2.0\" {XslOnly} {attributes}>{body}</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    BaseUri = baseUri,
                    InputUri = inputUri,
                }).TransformXml(input);
        }

        /// <remarks>
        /// <c>xml:base</c> is how a document assembled from several places says where each part came from,
        /// so a relative reference inside a part resolves against where that part was rather than against
        /// the file it now lives in.
        /// </remarks>
        [TestMethod]
        public void BaseUriFollowsXmlBaseInTheSourceDocument()
        {
            Assert.AreEqual(
                "<out>http://a.example/deep/</out>",
                RunWithBase(
                    Root("<xsl:value-of select=\"base-uri(//i)\"/>"),
                    "<r xml:base=\"http://a.example/\"><s xml:base=\"deep/\"><i/></s></r>",
                    "file:///stylesheets/one.xsl"));
        }

        [TestMethod]
        public void TheDocumentUriIsWhereTheDocumentCameFromAndNothingElse()
        {
            // Different from base-uri(), which xml:base moves and this does not. And different from where
            // the stylesheet is, which this test used to be given and to assert - the two were one field
            // until a conformance run showed what that costs.
            Assert.AreEqual(
                "<out>file:///docs/one.xml</out>",
                RunWithBase(
                    Root("<xsl:value-of select=\"document-uri(/)\"/>"),
                    "<r xml:base=\"http://a.example/\"/>",
                    "file:///stylesheets/one.xsl",
                    inputUri: "file:///docs/one.xml"));
        }

        [TestMethod]
        public void AnInputHandedOverAsTextCameFromNowhere()
        {
            // Saying nothing is the honest answer, and it is what the caller gets by saying nothing. A base
            // URI still has to be something, so a relative reference falls back to the stylesheet's.
            Assert.AreEqual(
                "<out/>",
                RunWithBase(
                    Root("<xsl:value-of select=\"document-uri(/)\"/>"),
                    "<r/>",
                    "file:///stylesheets/one.xsl"));

            Assert.AreEqual(
                "<out>file:///stylesheets/one.xsl</out>",
                RunWithBase(
                    Root("<xsl:value-of select=\"base-uri(/)\"/>"),
                    "<r/>",
                    "file:///stylesheets/one.xsl"));
        }

        [TestMethod]
        public void XmlBaseOnTheStylesheetSettlesTheStaticBaseUri()
        {
            Assert.AreEqual(
                "<out>http://s.example/here/</out>",
                RunWithBase(
                    Root("<xsl:value-of select=\"static-base-uri()\"/>"),
                    "<r/>",
                    "file:///stylesheets/one.xsl",
                    "xml:base=\"http://s.example/here/\""));
        }

        [TestMethod]
        public void AOneArgumentResolveUriUsesTheStaticBase()
        {
            Assert.AreEqual(
                "<out>http://s.example/here/there</out>",
                RunWithBase(
                    Root("<xsl:value-of select=\"resolve-uri('there')\"/>"),
                    "<r/>",
                    "file:///stylesheets/one.xsl",
                    "xml:base=\"http://s.example/here/\""));
        }

        [TestMethod]
        public void ANodeTheStylesheetBuiltIsRelativeToTheStylesheet()
        {
            // A result tree came from nowhere, so it has no document URI; its nodes are still relative to
            // something, and that something is the stylesheet element that created them.
            Assert.AreEqual(
                "<out>http://s.example/main/</out>",
                RunWithBase(
                    "<xsl:template match=\"/\"><xsl:variable name=\"v\"><e xml:base=\"/main/\"/></xsl:variable>"
                    + "<out><xsl:value-of select=\"base-uri($v/e)\"/></out></xsl:template>",
                    "<r/>",
                    "file:///stylesheets/one.xsl",
                    "xml:base=\"http://s.example/\""));
        }

        [TestMethod]
        public void AParentlessNodeThatIsNotAnElementIsRelativeToNothing()
        {
            Assert.AreEqual(
                "<out/>",
                RunWithBase(
                    "<xsl:template match=\"/\"><xsl:variable name=\"a\" as=\"attribute()\">"
                    + "<xsl:attribute name=\"n\" select=\"1\"/></xsl:variable>"
                    + "<out><xsl:value-of select=\"base-uri($a)\"/></out></xsl:template>",
                    "<r/>",
                    "file:///stylesheets/one.xsl",
                    "xml:base=\"http://s.example/\""));
        }

        // ---- xpath-default-namespace ---------------------------------------------------------------------------

        /// <remarks>
        /// XPath puts an unprefixed name in no namespace whatever <c>xmlns</c> says, which is right and is
        /// also the thing everyone trips over. <c>xpath-default-namespace</c> is how a stylesheet asks for a
        /// default anyway, and without it a stylesheet reaching into a namespaced document has to declare a
        /// prefix for every path it writes.
        /// </remarks>
        [TestMethod]
        public void AnUnprefixedNameMayBeGivenADefaultNamespace()
        {
            Assert.AreEqual(
                "<out>one</out>",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl} xpath-default-namespace=\"urn:d\">"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"r/i\"/></out></xsl:template>"
                    + "</xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true })
                    .TransformXml("<r xmlns=\"urn:d\"><i>one</i></r>"));
        }

        [TestMethod]
        public void TheDefaultNamespaceReachesAPatternToo()
        {
            Assert.AreEqual(
                "<out>[i]</out>",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl} xpath-default-namespace=\"urn:d\">"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"r/i\"/></out></xsl:template>"
                    + "<xsl:template match=\"i\">[i]</xsl:template></xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true })
                    .TransformXml("<r xmlns=\"urn:d\"><i>one</i></r>"));
        }

        [TestMethod]
        public void AnAttributeNameHasNoDefaultNamespace()
        {
            // The attribute axis has no default namespace of its own, so @n still means the no-namespace
            // attribute — which is what an unprefixed attribute in the document is.
            Assert.AreEqual(
                "<out>yes</out>",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl} xpath-default-namespace=\"urn:d\">"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"r/@n\"/></out></xsl:template>"
                    + "</xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true })
                    .TransformXml("<r xmlns=\"urn:d\" n=\"yes\"/>"));
        }

        [TestMethod]
        public void ATypeNameTakesTheDefaultElementNamespace()
        {
            // A type name lives in the same default as an element name, so a stylesheet defaulting to the
            // schema namespace may write "string" for xs:string.
            Assert.AreEqual(
                "<out>true</out>",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl} "
                    + "xpath-default-namespace=\"http://www.w3.org/2001/XMLSchema\">"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"'abc' instance of string\"/></out></xsl:template></xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }

        [TestMethod]
        public void AnEmptyDefaultNamespacePutsNamesBackInNone()
        {
            // The nearest declaration wins, which is how a stylesheet turns the attribute off for a subtree.
            Assert.AreEqual(
                "<out>plain</out>",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl} xpath-default-namespace=\"urn:d\">"
                    + "<xsl:template match=\"/\" xpath-default-namespace=\"\"><out>"
                    + "<xsl:value-of select=\"r/i\"/></out></xsl:template></xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r><i>plain</i></r>"));
        }

        [TestMethod]
        public void WhitespaceIsNotContentWhereContentWouldBeAnError()
        {
            // A stylesheet strips whitespace-only text, so an instruction written across two lines has no
            // content — and must not be refused for having both a select and content it does not have.
            Assert.AreEqual(
                "<out><e n=\"1 2 3\"/></out>",
                Run(Root("<xsl:element name=\"e\"><xsl:attribute name=\"n\" select=\"1 to 3\"> </xsl:attribute>"
                    + "</xsl:element>"),
                    "<r/>"));
        }

        // ---- What a pattern's own axis leaves out ------------------------------------------------------------

        /// <remarks>
        /// A pattern's innermost step is reached down the child axis, which holds neither an attribute nor a
        /// document node. Getting this wrong let a catch-all rule take the document node away from the
        /// built-in one, so the stylesheet's outermost element was never written at all.
        /// </remarks>
        [TestMethod]
        public void NodeInAPatternMatchesNeitherTheDocumentNodeNorAnAttribute()
        {
            // The document node reaches the doc rule through the built-in one, so <out> is written at all;
            // the attribute reaches the built-in attribute rule, which writes its value. Neither is taken by
            // the catch-all, which would have answered [any-node] for both.
            Assert.AreEqual(
                "<out>[named]1</out>",
                Run("<xsl:template match=\"doc\"><out><xsl:apply-templates select=\"foo|foo/@a\"/></out></xsl:template>"
                    + "<xsl:template match=\"foo\">[named]</xsl:template>"
                    + "<xsl:template match=\"node()\">[any-node]</xsl:template>",
                    "<doc><foo a=\"1\"/></doc>"));
        }

        [TestMethod]
        public void DocumentNodeInAPatternMatchesTheDocumentNode()
        {
            // The one kind test that names the node nothing is a child of, and the specification gives it a
            // default priority of its own — so it has to stay reachable.
            Assert.AreEqual(
                "<t>found doc</t>",
                Run("<xsl:template match=\"document-node(element(doc))\"><t>found <xsl:value-of "
                    + "select=\"name(*)\"/></t></xsl:template>",
                    "<doc/>"));
        }

        [TestMethod]
        public void AStylesheetFunctionHasNoContextItemOfItsOwn()
        {
            // What a function sees comes through its parameters, so that the same arguments give the same
            // answer wherever it was called from.
            Assert.AreEqual(
                "XPDY0002",
                Assert.ThrowsExactly<XsltException>(
                    () => RunWithFunctions(
                        "<xsl:function name=\"f:here\"><xsl:value-of select=\"name(.)\"/></xsl:function>"
                        + Root("<xsl:value-of select=\"f:here()\"/>"),
                        "<r/>")).Code);
        }

        [TestMethod]
        public void CopyingTheContextItemNeedsThereToBeOne()
        {
            Assert.AreEqual(
                "XTTE0945",
                Assert.ThrowsExactly<XsltException>(
                    () => RunWithFunctions(
                        "<xsl:function name=\"f:here\"><xsl:copy/></xsl:function>"
                        + Root("<xsl:value-of select=\"f:here()\"/>"),
                        "<r/>")).Code);
        }

        // ---- A computed name, and where it is read -------------------------------------------------------

        /// <summary>Runs a stylesheet carrying extra namespace declarations, which computed names read.</summary>
        private static string RunWithPrefixes(string body, string input, string declarations)
        {
            return new Xslt(
                $"<xsl:stylesheet version=\"2.0\" {XslOnly} {declarations}>{body}</xsl:stylesheet>",
                new XsltOptions { OmitXmlDeclaration = true }).TransformXml(input);
        }

        /// <remarks>
        /// The prefix of a computed name stands for whatever the declarations where the instruction was
        /// written say it stands for — not for nothing, which is what dropping it amounted to.
        /// </remarks>
        [TestMethod]
        public void APrefixedElementNameIsResolvedWhereTheInstructionWasWritten()
        {
            Assert.AreEqual(
                "<out xmlns:p=\"urn:p\"><p:foo/></out>",
                RunWithPrefixes(
                    Root("<xsl:element name=\"p:foo\"/>"), "<r/>", "xmlns:p=\"urn:p\""));
        }

        [TestMethod]
        public void AnUnprefixedElementNameTakesTheDefaultNamespaceInScope()
        {
            // XML's own rule, and the half that separates xsl:element from xsl:attribute below.
            Assert.AreEqual(
                "<out><foo xmlns=\"urn:d\"/></out>",
                RunWithPrefixes(
                    "<xsl:template match=\"/\"><out><xsl:element name=\"foo\" xmlns=\"urn:d\"/></out>"
                    + "</xsl:template>",
                    "<r/>",
                    string.Empty));
        }

        [TestMethod]
        public void AnUnprefixedAttributeNameIsInNoNamespace()
        {
            // The default declaration reaches element names only, so there is nothing here to look up.
            Assert.AreEqual(
                "<out xmlns=\"urn:d\"><e a=\"1\"/></out>",
                RunWithPrefixes(
                    "<xsl:template match=\"/\"><out><e><xsl:attribute name=\"a\">1</xsl:attribute></e></out>"
                    + "</xsl:template>",
                    "<r/>",
                    "xmlns=\"urn:d\""));
        }

        [TestMethod]
        public void ANamespaceAttributeSettlesTheNamespaceAndLeavesThePrefixASuggestion()
        {
            Assert.AreEqual(
                "<out xmlns:p=\"urn:p\"><p:foo xmlns:p=\"urn:other\"/></out>",
                RunWithPrefixes(
                    Root("<xsl:element name=\"p:foo\" namespace=\"urn:other\"/>"),
                    "<r/>",
                    "xmlns:p=\"urn:p\""));
        }

        [TestMethod]
        public void AnEmptyNamespaceAttributePutsTheNameInNoNamespaceAndDropsThePrefix()
        {
            // No prefix can reach a name in no namespace, so the one written is discarded rather than kept.
            Assert.AreEqual(
                "<out xmlns:p=\"urn:p\"><foo/></out>",
                RunWithPrefixes(
                    Root("<xsl:element name=\"p:foo\" namespace=\"\"/>"), "<r/>", "xmlns:p=\"urn:p\""));
        }

        [TestMethod]
        [DataRow("<xsl:element name=\"a bad name\"/>", "XTDE0820")]
        [DataRow("<xsl:element name=\"nowhere:foo\"/>", "XTDE0830")]
        [DataRow("<xsl:element name=\"foo\" namespace=\"http://www.w3.org/2000/xmlns/\"/>", "XTDE0835")]
        [DataRow("<xsl:attribute name=\"a bad name\"/>", "XTDE0850")]
        [DataRow("<xsl:attribute name=\"xmlns\"/>", "XTDE0855")]
        [DataRow("<xsl:attribute name=\"nowhere:a\"/>", "XTDE0860")]
        [DataRow("<xsl:attribute name=\"a\" namespace=\"http://www.w3.org/2000/xmlns/\"/>", "XTDE0865")]
        [DataRow("<xsl:processing-instruction name=\"p:i\"/>", "XTDE0890")]
        [DataRow("<xsl:processing-instruction name=\"XmL\"/>", "XTDE0890")]
        [DataRow("<xsl:namespace name=\"p\">####</xsl:namespace>", "XTDE0905")]
        [DataRow("<xsl:namespace name=\"1p\">urn:x</xsl:namespace>", "XTDE0920")]
        [DataRow("<xsl:namespace name=\"xml\">urn:x</xsl:namespace>", "XTDE0925")]
        [DataRow("<xsl:namespace name=\"p\"></xsl:namespace>", "XTDE0930")]
        public void AComputedNameThatWillNotDoIsRefusedByItsOwnCode(string instruction, string code)
        {
            // One complaint per instruction, and the specification gives each of them a code: an unusable
            // name is not a generic failure but a failure of the instruction that computed it.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root(instruction), "<r/>"));

            Assert.AreEqual(code, error.Code);
        }

        [TestMethod]
        public void AGeneratedPrefixIsNumberedFromZeroAndSkipsOneAlreadyInScope()
        {
            // An attribute in a namespace cannot use a default declaration, so it needs a prefix invented —
            // and reusing ns0, which the result already binds, would change what ns0:keep means here.
            Assert.AreEqual(
                "<out xmlns:ns0=\"urn:taken\" ns0:keep=\"1\" xmlns:ns1=\"urn:other\" ns1:a=\"2\"/>",
                RunWithPrefixes(
                    "<xsl:template match=\"/\"><out ns0:keep=\"1\">"
                    + "<xsl:attribute name=\"a\" namespace=\"urn:other\">2</xsl:attribute></out></xsl:template>",
                    "<r/>",
                    "xmlns:ns0=\"urn:taken\""));
        }

        // ---- What backwards compatibility actually changes -------------------------------------------------

        /// <summary>Runs a stylesheet at a stated version, both backends agreeing.</summary>
        private static string AtVersion(string body, string input, string version)
        {
            return Run(body, input, version);
        }

        /// <remarks>
        /// A 1.0 stylesheet on a 2.0 processor is still reading XPath 2.0 expressions. What backwards
        /// compatibility changes is how the values are read, not what may be written — so the grammar and
        /// the function library are the 2.0 ones whatever the version attribute says.
        /// </remarks>
        [TestMethod]
        [DataRow("1 to 3", "1")]
        [DataRow("(), 'empty'", "empty")]
        [DataRow("('a', 'b')", "a")]
        [DataRow("string-length(string(current-date())) gt 0", "true")]
        public void AOnePointZeroStylesheetStillGetsTheTwoPointZeroGrammar(string select, string expected)
        {
            Assert.AreEqual(
                $"<out>{expected}</out>",
                AtVersion(Root($"<xsl:value-of select=\"{select}\"/>"), "<r/>", "1.0"));
        }

        /// <remarks>
        /// The first-item rule, which is most of what backwards compatibility comes to: XSLT 1.0 had nothing
        /// that could carry more than one thing, so where a 2.0 expression now gives a sequence, the first
        /// item is the answer and the rest are discarded.
        /// </remarks>
        [TestMethod]
        public void TheFirstItemIsTheAnswerWhereOnePointZeroExpectedOne()
        {
            Assert.AreEqual(
                "<out><a>1</a><b sel=\"1\"/><c>1</c><d>7</d><e>1,2,3</e></out>",
                AtVersion(
                    "<xsl:template match=\"/\"><out>"
                    + "<a><xsl:value-of select=\"1 to 5\"/></a>"
                    + "<b sel=\"{1 to 5}\"/>"
                    + "<c><xsl:number value=\"1 to 5\"/></c>"
                    + "<d><xsl:value-of select=\"1 + (6 to 10)\"/></d>"
                    + "<e><xsl:value-of select=\"(1 to 5) to (3, 4)\" separator=\",\"/></e>"
                    + "</out></xsl:template>",
                    "<r/>",
                    "1.0"));
        }

        [TestMethod]
        public void TheSameStylesheetAtTwoPointZeroKeepsTheWholeSequence()
        {
            // The control: every one of those is the whole sequence when nothing asks for 1.0's reading.
            Assert.AreEqual(
                "<out><a>1 2 3 4 5</a><b sel=\"1 2 3 4 5\"/></out>",
                AtVersion(
                    "<xsl:template match=\"/\"><out>"
                    + "<a><xsl:value-of select=\"1 to 5\"/></a>"
                    + "<b sel=\"{1 to 5}\"/>"
                    + "</out></xsl:template>",
                    "<r/>",
                    "2.0"));
        }

        [TestMethod]
        public void ASeparatorAsksForTheWholeSequenceWhateverTheVersion()
        {
            // A separator is a 2.0 attribute, and putting one between items a 1.0 reading would have thrown
            // away makes no sense — so writing one says which reading was meant.
            Assert.AreEqual(
                "<out>1,2,3,4,5</out>",
                AtVersion(
                    Root("<xsl:value-of select=\"1 to 5\" separator=\",\"/>"), "<r/>", "1.0"));
        }

        [TestMethod]
        public void ATooLargeIntegerLiteralIsADoubleUnderOnePointZero()
        {
            // XPath 1.0 had no integers to overflow, so the same digits meant a double there. Refusing one a
            // 1.0 stylesheet has always been allowed to write would be the compatibility mode failing. The
            // quotient is a double, and written as this processor writes one.
            Assert.AreEqual(
                "<out>1.0E-20</out>",
                AtVersion(
                    Root("<xsl:value-of select=\"1 div 100000000000000000000\"/>"), "<r/>", "1.0"));

            // Under 2.0 the same digits are an xs:integer, and one past 64 bits is simply a wider one:
            // an integer divided by an integer is an xs:decimal, which is where the answer lands.
            Assert.AreEqual(
                "<out>0.00000000000000000001</out>",
                AtVersion(
                    Root("<xsl:value-of select=\"1 div 100000000000000000000\"/>"), "<r/>", "2.0"));
        }

        [TestMethod]
        public void BackwardsCompatibilityDoesNotRestoreTheOnePointZeroSpellings()
        {
            // The one thing this mode is not for. It restores XPath 1.0's rules — one numeric type, the
            // first item where a sequence is given, a fallback for a function that is not there — and the
            // specification says outright that what a backwards-compatible expression produces is defined
            // by the 2.0 specifications and not by reference to the 1.0 ones. How a number is written is
            // not one of the rules, so it is written the way everything else here writes one.
            Assert.AreEqual(
                "<out>INF -INF NaN</out>",
                AtVersion(
                    Root(
                        "<xsl:value-of select=\"1 div 0\"/><xsl:text> </xsl:text>"
                        + "<xsl:value-of select=\"-1 div 0\"/><xsl:text> </xsl:text>"
                        + "<xsl:value-of select=\"0 div 0\"/>"),
                    "<r/>",
                    "1.0"));

            // A float and a double alike, and a negative zero that keeps its sign because the negation
            // made a double of the integer it was written as.
            Assert.AreEqual(
                "<out>INF -INF -0</out>",
                AtVersion(
                    Root(
                        "<xsl:value-of select=\"string(xs:float('INF'))\"/><xsl:text> </xsl:text>"
                        + "<xsl:value-of select=\"string(xs:double('-INF'))\"/><xsl:text> </xsl:text>"
                        + "<xsl:value-of select=\"string(xs:float(-0))\"/>"),
                    "<r/>",
                    "1.0"));

            // What the mode does restore is the value: 1 div -0 is a negative infinity because -0 is a
            // negative zero, which it would not be if the minus sign had left an integer behind.
            Assert.AreEqual(
                "<out>-INF</out>",
                AtVersion(Root("<xsl:value-of select=\"1 div -0\"/>"), "<r/>", "1.0"));
        }

        [TestMethod]
        public void AVersionJustBelowTwoIsStillBackwardsCompatible()
        {
            // The version is a decimal and is truncated to the hundredth, not rounded: rounding 1.999999999
            // to the nearest hundredth makes it exactly 2.0 and switches off the very thing it asked for.
            Assert.AreEqual(
                "<out>1</out>",
                AtVersion(Root("<xsl:value-of select=\"1 to 5\"/>"), "<r/>", "1.999999999"));
        }

        [TestMethod]
        public void ABuiltInRuleCarriesTheParametersItWasGiven()
        {
            // An element the stylesheet wrote no rule for is not a reason for a parameter to stop: what the
            // caller meant was for the templates it eventually reaches to have it.
            Assert.AreEqual(
                "<out><found p=\"3\"/></out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates>"
                    + "<xsl:with-param name=\"p\" select=\"3\"/></xsl:apply-templates></out></xsl:template>"
                    + "<xsl:template match=\"item\"><xsl:param name=\"p\"/><found p=\"{$p}\"/></xsl:template>",
                    "<doc><item/></doc>"));
        }

        // ---- What the specification will not let a stylesheet say ------------------------------------------

        /// <summary>Compiles a stylesheet body and returns the code it was refused with.</summary>
        private static string RefusedCode(string body, string attributes = "")
        {
            return Assert.ThrowsExactly<XsltException>(
                () => new Xslt($"<xsl:stylesheet version=\"2.0\" {XslOnly} {attributes}>{body}</xsl:stylesheet>"))
                .Code!;
        }

        [TestMethod]
        [DataRow("<xsl:template match=\"a\" mode=\"m!1\"/>", "XTSE0550")]
        [DataRow("<xsl:template match=\"a\" mode=\"m n m\"/>", "XTSE0550")]
        [DataRow("<xsl:template match=\"a\" mode=\"#all m\"/>", "XTSE0550")]
        [DataRow("<xsl:variable name=\"v\" select=\"1\"/><xsl:variable name=\"v\" select=\"2\"/>", "XTSE0630")]
        [DataRow("<xsl:decimal-format name=\"d\" percent=\"0\" per-mille=\"0\"/>", "XTSE1300")]
        [DataRow("<xsl:decimal-format name=\"d\" zero-digit=\"2\"/>", "XTSE1295")]
        [DataRow("<xsl:template match=\"/\"><out><xsl:number value=\"3\" count=\"a\"/></out></xsl:template>",
            "XTSE0975")]
        [DataRow("<xsl:template match=\"/\"><out><xsl:element name=\"e\" use-attribute-sets=\"z\"/></out>"
            + "</xsl:template>", "XTSE0710")]
        [DataRow("<xsl:template name=\"t\"><xsl:param name=\"p\"/></xsl:template>"
            + "<xsl:template match=\"/\"><xsl:call-template name=\"t\">"
            + "<xsl:with-param name=\"p\" select=\"1\"/><xsl:with-param name=\"p\" select=\"2\"/>"
            + "</xsl:call-template></xsl:template>", "XTSE0670")]
        [DataRow("<xsl:strip-space elements=\"*\">text</xsl:strip-space>", "XTSE0260")]
        [DataRow("<xsl:template match=\"/\"><out/></xsl:template>text", "XTSE0120")]
        public void AStylesheetSayingSomethingItMayNotIsRefusedByItsOwnCode(string body, string code)
        {
            // Every one of these used to be accepted and then quietly do nothing, or do one of the two
            // things it asked for. Each has a code of its own in the specification.
            Assert.AreEqual(code, RefusedCode(body));
        }

        [TestMethod]
        public void AnUnboundPrefixInExcludeResultPrefixesIsRefused()
        {
            // The commonest way to write this attribute wrongly is with commas, which the grammar does not
            // allow: "one, two" is two prefixes called "one," and "two", neither of them bound.
            Assert.AreEqual(
                "XTSE0808",
                RefusedCode(
                    "<xsl:template match=\"/\"><out/></xsl:template>",
                    "exclude-result-prefixes=\"nowhere\""));

            Assert.AreEqual(
                "XTSE0809",
                RefusedCode(
                    "<xsl:template match=\"/\"><out/></xsl:template>",
                    "exclude-result-prefixes=\"#default\""));
        }

        [TestMethod]
        public void ExcludeResultPrefixesTakesAllAsEveryPrefixInScope()
        {
            // The XSLT 2.0 shorthand, so a stylesheet need not list the namespaces it happens to have
            // declared. Without it the result would carry the two declarations the stylesheet used.
            Assert.AreEqual(
                "<out/>",
                new Xslt(
                    "<xsl:stylesheet version=\"2.0\" " + XslOnly + " xmlns:a=\"urn:a\" xmlns:b=\"urn:b\" "
                    + "exclude-result-prefixes=\"#all\">"
                    + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }

        [TestMethod]
        [DataRow("function-available($p)", "XTDE1400")]
        [DataRow("element-available($p)", "XTDE1440")]
        [DataRow("type-available($p)", "XTDE1428")]
        [DataRow("system-property($p)", "XTDE1390")]
        public void AskingWhetherSomethingExistsUnderANameThatCannotBeOneIsAnError(string call, string code)
        {
            // The answer for a name that could not exist is not "no": the question was malformed, and each
            // of these functions has a code of its own for it.
            Assert.AreEqual(
                code,
                Assert.ThrowsExactly<XsltException>(
                    () => Run(
                        "<xsl:param name=\"p\" select=\"'1234'\"/>" + Root($"<xsl:value-of select=\"{call}\"/>"),
                        "<r/>")).Code);
        }

        [TestMethod]
        public void ALiteralNameThatCannotBeOneStillCompiles()
        {
            // The guarded-call pattern writes a literal, and the specification makes this a dynamic error —
            // so a stylesheet whose bad name is on a branch nothing takes is entitled to compile.
            _ = new Xslt(
                "<xsl:stylesheet version=\"2.0\" " + Xsl + "><xsl:template match=\"/\"><out>"
                + "<xsl:if test=\"false()\"><xsl:value-of select=\"function-available('1234')\"/></xsl:if>"
                + "</out></xsl:template></xsl:stylesheet>");
        }

        // ---- The current template rule, and what continues it -----------------------------------------------

        /// <remarks>
        /// Both instructions mean "carry on down the list of rules that matched this node", so outside a rule
        /// there is nothing for them to continue. XSLT names that <c>XTDE0560</c>.
        /// </remarks>
        [TestMethod]
        [DataRow("<xsl:apply-imports/>")]
        [DataRow("<xsl:next-match/>")]
        public void ContinuingTheCurrentRuleWhereThereIsNoneIsRefused(string instruction)
        {
            Assert.AreEqual(
                "XTDE0560",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(
                        "<xsl:template match=\"/\"><out>"
                        + $"<xsl:for-each select=\"*\">{instruction}</xsl:for-each>"
                        + "</out></xsl:template>",
                        "<r><a/></r>")).Code);
        }

        [TestMethod]
        public void CallingATemplateByNameKeepsTheRuleThatCalledIt()
        {
            // xsl:call-template changes the current template rule not at all, so a named helper can still
            // say xsl:apply-imports and mean the rule that reached it.
            Assert.AreEqual(
                "<out>[named][imported]</out>",
                Run(
                    "<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"r/a\"/></out></xsl:template>"
                    + "<xsl:template match=\"a\">[named]<xsl:call-template name=\"h\"/></xsl:template>"
                    + "<xsl:template name=\"h\"><xsl:apply-imports/></xsl:template>",
                    "<r><a/></r>",
                    resolver: new MapResolver().Add(
                        "base.xsl",
                        "<xsl:stylesheet version=\"2.0\" " + Xsl + ">"
                        + "<xsl:template match=\"a\">[imported]</xsl:template></xsl:stylesheet>")));
        }

        [TestMethod]
        public void KeyNamesAreComparedWholeAndNotByTheirLocalPart()
        {
            // Two keys may share a local name in different namespaces, so a prefix bound to somewhere no key
            // is names no key at all.
            Assert.AreEqual(
                "XTDE1260",
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(
                        "<xsl:stylesheet version=\"2.0\" " + Xsl + " xmlns:my=\"urn:my\">"
                        + "<xsl:key name=\"k\" match=\"*\" use=\"@id\"/>"
                        + "<xsl:template match=\"/\"><xsl:value-of select=\"key('my:k', 'x')\"/>"
                        + "</xsl:template></xsl:stylesheet>")).Code);
        }

        [TestMethod]
        public void TwoOutputDeclarationsMayAgreeAndMayNotDisagree()
        {
            // Several declarations of one definition are ordinary — a module says what it needs and leaves
            // the rest alone. Two giving one attribute two values at one precedence are not: nothing decides
            // between them, and taking the later would make the result depend on the order they were written.
            _ = new Xslt(
                "<xsl:stylesheet version=\"2.0\" " + Xsl + "><xsl:output indent=\"no\"/>"
                + "<xsl:output method=\"xml\"/><xsl:output indent=\"no\"/>"
                + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>");

            Assert.AreEqual(
                "XTSE1560",
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(
                        "<xsl:stylesheet version=\"2.0\" " + Xsl + "><xsl:output indent=\"no\"/>"
                        + "<xsl:output indent=\"yes\"/>"
                        + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>")).Code);
        }

        [TestMethod]
        public void ABindingWithBothASelectAndContentIsRefused()
        {
            // Its value comes from one or the other, and one written with both says two things.
            Assert.AreEqual(
                "XTSE0620",
                Assert.ThrowsExactly<XsltException>(
                    () => Run(
                        Root("<xsl:variable name=\"v\" select=\"1\">text</xsl:variable>"), "<r/>")).Code);
        }


        // ---- One code per way of saying something that cannot be acted on ----------------------------------

        private const string Codepoint = "http://www.w3.org/2005/xpath-functions/collation/codepoint";

        [TestMethod]
        [DataRow("<xsl:template match=\"/\"><out><xsl:choose><xsl:otherwise/>"
            + "<xsl:when test=\"1\">a</xsl:when></xsl:choose></out></xsl:template>", "XTSE0010")]
        [DataRow("<xsl:template match=\"/\"><out><xsl:choose><xsl:when test=\"1\">a</xsl:when>"
            + "<xsl:otherwise/><xsl:otherwise/></xsl:choose></out></xsl:template>", "XTSE0010")]
        [DataRow("<xsl:template match=\"/\"><out><xsl:text>a<xsl:text>b</xsl:text></xsl:text></out>"
            + "</xsl:template>", "XTSE0010")]
        [DataRow("<xsl:template name=\"t\"/><xsl:template name=\"u\" priority=\"2\"/>", "XTSE0500")]
        [DataRow("<xsl:template name=\"t\"/><xsl:template name=\"u\" mode=\"m\"/>", "XTSE0500")]
        [DataRow("<xsl:template match=\"a\"><xsl:param name=\"p\" select=\"1\"/>"
            + "<xsl:param name=\"p\" select=\"2\"/></xsl:template>", "XTSE0580")]
        [DataRow("<xsl:function name=\"my:f\"><xsl:param name=\"x\" select=\"3\"/>"
            + "<xsl:sequence select=\"2\"/></xsl:function>", "XTSE0760")]
        [DataRow("<xsl:function name=\"my:f\"><xsl:param name=\"x\">3</xsl:param>"
            + "<xsl:sequence select=\"2\"/></xsl:function>", "XTSE0760")]
        [DataRow("<xsl:template match=\"/\"><out><xsl:for-each select=\"1 to 5\">"
            + "<xsl:sort select=\".\">twelve</xsl:sort></xsl:for-each></out></xsl:template>", "XTSE1015")]
        [DataRow("<xsl:template match=\"/\"><out><xsl:for-each select=\"1 to 5\">"
            + "<xsl:sort select=\".\"/><xsl:sort select=\".\" stable=\"no\"/></xsl:for-each></out>"
            + "</xsl:template>", "XTSE1017")]
        [DataRow("<xsl:template match=\"/\"><out><xsl:perform-sort select=\"1 to 5\">"
            + "<xsl:sort select=\".\"/><xsl:value-of select=\".\"/></xsl:perform-sort></out></xsl:template>",
            "XTSE1040")]
        [DataRow("<xsl:key name=\"k\" match=\"*\" use=\"17\">twelve</xsl:key>"
            + "<xsl:template match=\"/\"><out/></xsl:template>", "XTSE1205")]
        [DataRow("<xsl:key name=\"k\" match=\"*\"/><xsl:template match=\"/\"><out/></xsl:template>",
            "XTSE1205")]
        [DataRow("<xsl:template match=\"/\"><out>"
            + "<xsl:analyze-string select=\"&apos;a&apos;\" regex=\"[A-Z]+\"/></out></xsl:template>",
            "XTSE1130")]
        public void AnElementSayingTwoThingsWhereOnlyOneCanBeActedOnIsRefused(string body, string code)
        {
            // The specification states these one element at a time rather than folding them into the content
            // model, and gives each a code, because each is a different thing to have got wrong.
            Assert.AreEqual(code, RefusedCode(body, "xmlns:my=\"http://my.com/\""));
        }

        [TestMethod]
        [DataRow("<xsl:template name=\"xsl:main\"/>", "XTSE0080")]
        [DataRow("<xsl:function name=\"xs:f\"><xsl:sequence select=\"1\"/></xsl:function>", "XTSE0080")]
        [DataRow("<xsl:function name=\"map:f\"><xsl:sequence select=\"1\"/></xsl:function>", "XTSE0080")]
        [DataRow("<xsl:function name=\"array:f\"><xsl:sequence select=\"1\"/></xsl:function>", "XTSE0080")]
        [DataRow("<xsl:function name=\"math:f\"><xsl:sequence select=\"1\"/></xsl:function>", "XTSE0080")]
        [DataRow("<xsl:template match=\"/\"><out/></xsl:template><porridge/>", "XTSE0130")]
        [DataRow("<xsl:template match=\"/\"><out xsl:banana=\"3\"/></xsl:template>", "XTSE0805")]
        [DataRow("<xsl:namespace-alias stylesheet-prefix=\"my\" result-prefix=\"xs\"/>"
            + "<xsl:namespace-alias stylesheet-prefix=\"your\" result-prefix=\"xsl\"/>"
            + "<xsl:template match=\"/\"><out/></xsl:template>", "XTSE0810")]
        [DataRow("<xsl:template name=\"t\"/><xsl:template match=\"/\">"
            + "<xsl:call-template name=\"t\"><xsl:with-param name=\"p\" select=\"1\"/></xsl:call-template>"
            + "</xsl:template>", "XTSE0680")]
        [DataRow("<xsl:template match=\"*[empty(current-group())]\"><out/></xsl:template>", "XTSE1060")]
        [DataRow("<xsl:template match=\"*[empty(current-grouping-key())]\"><out/></xsl:template>",
            "XTSE1070")]
        [DataRow("<xsl:template match=\"/\"><out><xsl:for-each-group select=\"*\""
            + " group-starting-with=\"a\" collation=\"" + Codepoint + "\"/></out></xsl:template>", "XTSE1090")]
        [DataRow("<xsl:decimal-format name=\"d\" percent=\"%\"/>"
            + "<xsl:decimal-format name=\"d\" percent=\"*\"/>"
            + "<xsl:template match=\"/\"><out/></xsl:template>", "XTSE1290")]
        [DataRow("<xsl:decimal-format name=\"d\" decimal-separator=\"must-be-a-char\"/>"
            + "<xsl:template match=\"/\"><out/></xsl:template>", "XTSE0020")]
        [DataRow("<xsl:template match=\"/\"><out><xsl:apply-templates mode=\"{$x}\"/></out></xsl:template>",
            "XTSE0020")]
        public void AStylesheetNamingWhatItMayNotNameIsRefused(string body, string code)
        {
            // Names, namespaces and the declarations that give them meaning. Each of these used to compile
            // and then do something the stylesheet never asked for — an alias that went one of two ways, a
            // parameter nothing would ever read, a directive addressed to the processor in words it lacks.
            Assert.AreEqual(
                code,
                RefusedCode(
                    body,
                    "xmlns:my=\"http://my.com/\" xmlns:your=\"http://my.com/\" "
                    + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" "
                    + "xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\" "
                    + "xmlns:array=\"http://www.w3.org/2005/xpath-functions/array\" "
                    + "xmlns:math=\"http://www.w3.org/2005/xpath-functions/math\""));
        }

        [TestMethod]
        public void AVersionThatIsNotADecimalIsRefused()
        {
            // The version decides which language the stylesheet is read in, so a value that is not a version
            // cannot be shrugged off as naming a later one — that would turn every other check off.
            Assert.AreEqual(
                "XTSE0110",
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(
                        $"<xsl:stylesheet version=\"2.0e3\" {Xsl}>"
                        + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>")).Code);
        }

        [TestMethod]
        public void ADefaultCollationNamingNothingThisEngineHasIsRefused()
        {
            Assert.AreEqual(
                "XTSE0125",
                RefusedCode(
                    "<xsl:template match=\"/\"><out/></xsl:template>",
                    "default-collation=\"mailto:nobody@nowhere.example\""));
        }

        [TestMethod]
        public void ADefaultCollationIsAListAndOneRecognizedCandidateIsEnough()
        {
            // The value names what the stylesheet would like and what it will settle for, in order, so it is
            // an error only when none of them can be resolved.
            Assert.AreEqual(
                "<out/>",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl} "
                    + $"default-collation=\"mailto:nobody@nowhere.example {Codepoint}\">"
                    + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true })
                    .TransformXml("<r/>"));
        }

        [TestMethod]
        public void WhitespaceKeptByXmlSpaceIsContentAnEmptyElementMayNotHold()
        {
            // A stylesheet tree keeps all its whitespace, so what tells layout from content is whether the
            // stylesheet asked for it to be kept. The specification names this case outright.
            Assert.AreEqual(
                "XTSE0260",
                RefusedCode(
                    "<xsl:strip-space elements=\"*\" xml:space=\"preserve\">\n  </xsl:strip-space>"
                    + "<xsl:template match=\"/\"><out/></xsl:template>"));
        }

        [TestMethod]
        public void ADecimalFormatIsTheUnionOfEveryDeclarationOfIt()
        {
            // Two declarations of one format state different properties of it, and neither overrides the
            // other. Reading only the last would lose the grouping separator entirely.
            Assert.AreEqual(
                "<out>1.234,50</out>",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                    + "<xsl:decimal-format name=\"d\" decimal-separator=\",\" grouping-separator=\".\"/>"
                    + "<xsl:decimal-format name=\"d\" minus-sign=\"-\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"format-number(1234.5, &apos;#.##0,00&apos;, &apos;d&apos;)\"/>"
                    + "</out></xsl:template></xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true })
                    .TransformXml("<r/>"));
        }

        [TestMethod]
        public void ADecimalFormatConflictIsSettledByHigherImportPrecedence()
        {
            // Two declarations of one precedence disagreeing is an error only if nothing overrides them, so
            // it cannot be reported where it is read: the module that settles it may not have been reached.
            MapResolver modules = new MapResolver().Add(
                "conflicting",
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:decimal-format name=\"d\" decimal-separator=\".\"/>"
                + "<xsl:decimal-format name=\"d\" decimal-separator=\",\"/>"
                + "</xsl:stylesheet>");

            Assert.AreEqual(
                "<out>1:50</out>",
                Run(
                    "<xsl:import href=\"conflicting\"/>"
                    + "<xsl:decimal-format name=\"d\" decimal-separator=\":\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"format-number(1.5, &apos;0:00&apos;, &apos;d&apos;)\"/>"
                    + "</out></xsl:template>",
                    "<r/>",
                    resolver: modules));
        }

        [TestMethod]
        public void AnUnknownParameterOnACallIsAllowedUnderBackwardsCompatibility()
        {
            // XSLT 1.0 discarded it, and a 1.0 stylesheet running here is entitled to the 1.0 reading.
            Assert.AreEqual(
                "<out/>",
                Run(
                    "<xsl:template name=\"t\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:call-template name=\"t\">"
                    + "<xsl:with-param name=\"p\" select=\"1\"/></xsl:call-template></out></xsl:template>",
                    "<r/>",
                    version: "1.0"));
        }

        [TestMethod]
        public void TheNamesXsltDefinesInItsOwnNamespaceMayStillBeDeclared()
        {
            // The XSLT namespace is reserved so that a stylesheet cannot invent names in it, but XSLT puts a
            // few there for a stylesheet to declare. Refusing those would refuse the mechanism they exist for.
            Assert.AreEqual(
                "<out/>",
                new Xslt(
                    $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                    + "<xsl:template name=\"xsl:initial-template\"><out/></xsl:template>"
                    + "</xsl:stylesheet>",
                    new XsltOptions
                    {
                        OmitXmlDeclaration = true,
                        InitialTemplate = "{http://www.w3.org/1999/XSL/Transform}initial-template",
                    })
                    .Transform());
        }

        // ---- What a stylesheet function is standing in, and what it is not ---------------------------------

        /// <summary>Runs a stylesheet and returns the code it was refused with.</summary>
        private static string RaisedCode(string body, string input = "<r><a/></r>", string version = "2.0")
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, input, version)).Code!;
        }

        [TestMethod]
        [DataRow("<xsl:sequence select=\"count(//a)\"/>", "XPDY0002")]
        [DataRow("<xsl:sequence select=\"name(current())\"/>", "XTDE1360")]
        [DataRow("<xsl:sequence select=\"count(key('k', 'x'))\"/>", "XTDE1270")]
        public void AStylesheetFunctionHasNoNodeToStandOn(string body, string code)
        {
            // The context item, the current node and the tree a two-argument key() searches are all absent
            // inside one: what a function sees comes through its arguments, which is what lets two calls with
            // the same arguments be relied on to give the same answer.
            Assert.AreEqual(
                code,
                RaisedCode(
                    "<xsl:key name=\"k\" match=\"*\" use=\"'v'\"/>"
                    + "<xsl:template match=\"/\" xmlns:my=\"http://my.com/\"><out>"
                    + "<xsl:value-of select=\"my:f()\"/></out></xsl:template>"
                    + $"<xsl:function name=\"my:f\" xmlns:my=\"http://my.com/\">{body}</xsl:function>"));
        }

        [TestMethod]
        public void ARootStepNeedsANodeToTakeItFrom()
        {
            // An atomic context item is a different mistake from no context item at all, and the two codes
            // say which: '/' is an axis step, and a string is not something a step can be taken from.
            Assert.AreEqual(
                "XPTY0020",
                RaisedCode(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:analyze-string select=\"'abcd'\" regex=\"a\">"
                    + "<xsl:matching-substring><xsl:value-of select=\"count(//a)\"/></xsl:matching-substring>"
                    + "</xsl:analyze-string></out></xsl:template>"));
        }

        [TestMethod]
        public void AnAnalyzeStringBranchIsProcessingTheSubstringItself()
        {
            // The context item and the current item are both the piece of text, not a node standing in for
            // it. What that changes is what can be navigated from them, which is nothing.
            Assert.AreEqual(
                "<out>[b|b][#|#][a|a]</out>",
                Run(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:analyze-string select=\"'b#a'\" regex=\"#\">"
                    + "<xsl:matching-substring>[<xsl:value-of select=\".\"/>|"
                    + "<xsl:value-of select=\"current()\"/>]</xsl:matching-substring>"
                    + "<xsl:non-matching-substring>[<xsl:value-of select=\".\"/>|"
                    + "<xsl:value-of select=\"current()\"/>]</xsl:non-matching-substring>"
                    + "</xsl:analyze-string></out></xsl:template>",
                    "<r/>"));
        }

        [TestMethod]
        public void ADocumentNodeCarriesNeitherAttributeNorNamespace()
        {
            // xsl:copy of a document node runs its content straight into whatever is already open, a document
            // node contributing its children and nothing else. The one thing that separates the two readings
            // is this: without the check the attribute landed on the element outside.
            Assert.AreEqual(
                "XTDE0420",
                RaisedCode(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:copy><xsl:attribute name=\"x\">5</xsl:attribute></xsl:copy>"
                    + "</out></xsl:template>"));
        }

        [TestMethod]
        public void AResultDocumentCannotBeWrittenIntoAVariable()
        {
            // What is being built there is a temporary tree, not a document of the transformation's own, and
            // whether it is ever read is no business of the file system.
            Assert.AreEqual(
                "XTDE1480",
                RaisedCode(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"t\">"
                    + "<xsl:result-document href=\"temp.out\"><apple/></xsl:result-document>"
                    + "</xsl:variable><xsl:copy-of select=\"$t\"/></out></xsl:template>"));
        }

        [TestMethod]
        [DataRow("<xsl:for-each select=\"1 to 3\"><xsl:sort select=\"(., .)\"/></xsl:for-each>", "XTTE1020")]
        [DataRow("<xsl:for-each-group select=\"r/a\" group-adjacent=\"1, 2\">"
            + "<xsl:value-of select=\"current-group()\"/></xsl:for-each-group>", "XTTE1100")]
        [DataRow("<xsl:for-each-group select=\"r/a\" group-adjacent=\"b\">"
            + "<xsl:value-of select=\"current-group()\"/></xsl:for-each-group>", "XTTE1100")]
        public void AKeyThatDecidesAnOrderingGivesOneValuePerItem(string body, string code)
        {
            // One sort key is one value per item, and a group-adjacent is one value per item too: several
            // would give several orderings, and none decides nothing at all.
            Assert.AreEqual(code, RaisedCode(Root(body)));
        }

        [TestMethod]
        public void AComputedTerminateIsCheckedWhenItIsReached()
        {
            // terminate is an attribute value template, so what it says is not known until the message is
            // reached — which makes the fixed set of values it may take a dynamic error rather than a static
            // one. Reading anything else as "no" would carry on past a stylesheet asking to stop.
            Assert.AreEqual(
                "XTDE0030",
                RaisedCode(
                    "<xsl:param name=\"x\" select=\"'bananas'\"/>"
                    + Root("<xsl:message terminate=\"{$x}\"/>")));
        }

        [TestMethod]
        public void ADocumentReferenceWithAFragmentIsRefused()
        {
            // A bare name after the '#' names the element with that ID; any other form of fragment
            // identifier is refused, since returning the whole document would answer a narrower question
            // with a wider answer.
            Assert.AreEqual(
                "XTRE1160",
                RaisedCode(Root("<xsl:value-of select=\"document('other.xml#xpointer(id7)')\"/>")));
        }

        [TestMethod]
        public void ACircularGlobalIsNamedByItsCode()
        {
            Assert.AreEqual(
                "XTDE0640",
                RaisedCode(
                    "<xsl:variable name=\"p\" select=\"$q\"/><xsl:variable name=\"q\" select=\"$p\"/>"
                    + Root("<xsl:value-of select=\"$p\"/>")));
        }

        [TestMethod]
        public void ASystemPropertyThatIsNotANameIsRefusedWhenItIsReached()
        {
            // The answer for a name that could not exist is not "no such property" — the question was
            // malformed. It stays a dynamic error even for a literal, which is what lets a stylesheet probe
            // for a feature on a branch nothing takes.
            Assert.AreEqual("XTDE1390", RaisedCode(Root("<xsl:value-of select=\"system-property('c#')\"/>")));
        }

        [TestMethod]
        [DataRow("<xsl:param name=\"p\" as=\"xs:integer\" select=\"3.5\"/>", "XTTE0600")]
        [DataRow("<xsl:param name=\"p\" as=\"xs:integer\"/>", "XTDE0610")]
        public void AParameterDefaultAndAMissingOneAreDifferentComplaints(string parameter, string code)
        {
            // A written default that does not fit the declared type is a type error in the stylesheet. No
            // default at all, where the type does not admit the empty sequence, makes the parameter required
            // after all — so what is wrong is the call that left it out, and that depends on the caller.
            Assert.AreEqual(
                code,
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(
                        "<xsl:stylesheet version=\"2.0\" " + XslOnly
                        + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                        + "<xsl:template match=\"/\"><out><xsl:apply-templates/></out></xsl:template>"
                        + $"<xsl:template match=\"r\">{parameter}"
                        + "<xsl:value-of select=\"$p\"/></xsl:template></xsl:stylesheet>",
                        new XsltOptions { Version = XsltVersion.V20 })
                        .TransformXml("<r/>")).Code);
        }
        /// <summary>Serves imported modules from a dictionary, so these tests need no files.</summary>
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_modules = new(StringComparer.Ordinal);

            public MapResolver Add(string name, string stylesheet)
            {
                m_modules[name] = stylesheet;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_modules.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        // ---- Pattern axes ---------------------------------------------------------------------------------

        /// <summary>
        /// Applies templates to every element and reports which rule reached each one.
        /// </summary>
        /// <remarks>
        /// Against a processor that implements 3.0, because what a pattern may say is the vocabulary of the
        /// version the processor implements rather than the one the stylesheet claims — and the engine still
        /// says 2.0 by default.
        /// </remarks>
        /// <param name="rules">The template rules under test.</param>
        /// <param name="input">The source document.</param>
        private static string Matched(string rules, string input)
        {
            return Run(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//*\"/></out></xsl:template>"
                + "<xsl:template match=\"*\"/>"
                + rules,
                input,
                version: "3.0",
                implemented: XsltVersion.V30);
        }

        [TestMethod]
        public void ThePatternGrammarIsTheProcessorsVersion()
        {
            // A 2.0 processor reads the 2.0 pattern grammar however new the stylesheet says it is, so the
            // three axes 3.0 added are not there to be written. The suite is explicit about this: a handful
            // of tests marked XSLT20 exactly exist to check that they are refused.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"self::a\"><no/></xsl:template>",
                    "<a/>",
                    version: "3.0"));

            Assert.AreEqual("XTSE0340", error.Code);
        }

        [TestMethod]
        public void APatternMayNameTheSelfAxis()
        {
            // self:: does not move at all, so what is written to its left has to hold of the very same node.
            Assert.AreEqual(
                "<out>[baz][foo-with-att]</out>",
                Matched(
                    "<xsl:template match=\"self::baz\">[baz]</xsl:template>"
                    + "<xsl:template match=\"self::foo/self::*[@att]\">[foo-with-att]</xsl:template>",
                    "<doc><baz/><foo att=\"1\"/><foo/></doc>"));
        }

        [TestMethod]
        public void APatternMayNameTheDescendantAxis()
        {
            // a/descendant::b and a//b say the same thing, and the second foo is under the first rather than
            // under the doc — so a rule written either way has to reach it.
            Assert.AreEqual(
                "<out>[1][2]</out>",
                Matched(
                    "<xsl:template match=\"doc/descendant::foo\">[<xsl:value-of select=\"@n\"/>]</xsl:template>",
                    "<doc><foo n=\"1\"><foo n=\"2\"/></foo></doc>"));
        }

        [TestMethod]
        public void ADescendantStepCountsItsPredicateWithinTheAnchor()
        {
            // The point of asking the predicate once an anchor is chosen. chapter/descendant::deep[1] is the
            // first deep under the chapter, which is nested one level down and is nobody's first child.
            Assert.AreEqual(
                "<out>[first 1][second 2][last 3]</out>",
                Matched(
                    "<xsl:template match=\"chapter/descendant::deep[1]\">"
                    + "[first <xsl:value-of select=\"@n\"/>]</xsl:template>"
                    + "<xsl:template match=\"chapter/descendant::deep[2]\">"
                    + "[second <xsl:value-of select=\"@n\"/>]</xsl:template>"
                    + "<xsl:template match=\"chapter/descendant::deep[last()]\">"
                    + "[last <xsl:value-of select=\"@n\"/>]</xsl:template>",
                    "<doc><chapter><a><deep n=\"1\"/></a><deep n=\"2\"/><deep n=\"3\"/></chapter></doc>"));
        }

        [TestMethod]
        public void ASlashSlashStepStillCountsItsPredicateAmongSiblings()
        {
            // The distinction the descendant axis makes it easy to lose. 'chapter//footnote[1]' expands to
            // chapter/descendant-or-self::node()/child::footnote[1], so the predicate counts among the
            // footnote children of whatever the footnote hangs from — not among the chapter's descendants.
            Assert.AreEqual(
                "<out>[first a][other b][first c]</out>",
                Matched(
                    "<xsl:template match=\"chapter//footnote[1]\">"
                    + "[first <xsl:value-of select=\"@n\"/>]</xsl:template>"
                    + "<xsl:template match=\"chapter//footnote[position() != 1]\">"
                    + "[other <xsl:value-of select=\"@n\"/>]</xsl:template>",
                    "<doc><chapter><s><footnote n=\"a\"/><footnote n=\"b\"/></s>"
                    + "<t><footnote n=\"c\"/></t></chapter></doc>"));
        }

        [TestMethod]
        public void ASlashSlashBeforeSelfReachesTheNodeItself()
        {
            // '//' widens what the axis already reached rather than replacing it, so foo//self::qux asks for
            // a qux at or under a foo. The foo itself is not a qux, and the qux under it is.
            Assert.AreEqual(
                "<out>[qux]</out>",
                Matched(
                    "<xsl:template match=\"foo//self::qux\">[qux]</xsl:template>",
                    "<doc><qux/><foo><qux/></foo></doc>"));
        }

        [TestMethod]
        public void AnAxisAPatternCannotUseIsRefused()
        {
            // A pattern is matched upwards from the node, so an axis that reaches sideways or upwards has no
            // reading here at all.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Matched(
                    "<xsl:template match=\"ancestor::doc\">[no]</xsl:template>",
                    "<doc><a/></doc>"));

            Assert.AreEqual("XTSE0340", error.Code);
            StringAssert.Contains(error.Message, "ancestor");
        }

        // ---- xsl:key with a body --------------------------------------------------------------------------

        [TestMethod]
        public void AKeyMayTakeItsValueFromASequenceConstructor()
        {
            // Not sugar for the use attribute: a constructor can declare a variable and choose, which is how
            // a key indexes something an expression cannot reach in one go.
            Assert.AreEqual(
                "<out><p n=\"1\"/><p n=\"3\"/></out>",
                Run(
                    "<xsl:key name=\"k\" match=\"p\">"
                    + "<xsl:variable name=\"tag\" select=\"if (@n mod 2 = 1) then 'odd' else 'even'\"/>"
                    + "<xsl:value-of select=\"$tag\"/>"
                    + "</xsl:key>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:copy-of select=\"key('k', 'odd')\"/></out></xsl:template>",
                    "<doc><p n=\"1\"/><p n=\"2\"/><p n=\"3\"/></doc>",
                    version: "3.0",
                    implemented: XsltVersion.V30));
        }

        [TestMethod]
        public void AKeyFilesANodeUnderEveryValueItsBodyProduced()
        {
            // The body's result is a sequence, not a tree. Read as a tree it would be one string and the
            // node would be filed under 'ab' rather than under 'a' and under 'b'.
            Assert.AreEqual(
                "<out><p/></out>",
                Run(
                    "<xsl:key name=\"k\" match=\"p\">"
                    + "<xsl:sequence select=\"'a'\"/><xsl:sequence select=\"'b'\"/>"
                    + "</xsl:key>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:copy-of select=\"key('k', 'b')\"/></out></xsl:template>",
                    "<doc><p/></doc>",
                    version: "3.0",
                    implemented: XsltVersion.V30));
        }

        [TestMethod]
        public void AKeySayingItsValueTwiceOrNotAtAllIsRefused()
        {
            foreach (string declaration in new[]
            {
                "<xsl:key name=\"k\" match=\"p\" use=\"@n\"><xsl:sequence select=\"1\"/></xsl:key>",
                "<xsl:key name=\"k\" match=\"p\"/>",
            })
            {
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => Run(
                        declaration + "<xsl:template match=\"/\"><out/></xsl:template>",
                        "<doc><p n=\"1\"/></doc>",
                        version: "3.0",
                        implemented: XsltVersion.V30));

                Assert.AreEqual("XTSE1205", error.Code, declaration);
            }
        }

        [TestMethod]
        public void AKeyBodyIsTwoPointZeroVocabulary()
        {
            // XSLT 2.0 already lets a key say what it files a node under as a sequence constructor
            // (§16.3.1), and the values are what the constructor produces, typed: the integer 1 finds it.
            Assert.AreEqual(
                "<out>1</out>",
                Run(
                    "<xsl:key name=\"k\" match=\"p\"><xsl:sequence select=\"1\"/></xsl:key>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"count(key('k', 1))\"/></out></xsl:template>",
                    "<doc><p/></doc>"));

            // Both at once is the declaration saying two things.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:key name=\"k\" match=\"p\" use=\"1\"><xsl:sequence select=\"1\"/></xsl:key>"
                    + "<xsl:template match=\"/\"><out/></xsl:template>",
                    "<doc><p/></doc>"));

            Assert.AreEqual("XTSE1205", error.Code);
        }

        // ---- fn:path --------------------------------------------------------------------------------------

        /// <summary>The document the path tests describe nodes of.</summary>
        private const string PathInput =
            "<doc att=\"top\"><?mypi some data?><!--a comment-->text-in-doc<inner>more</inner></doc>";

        /// <summary>Evaluates one expression against that document and returns what it produced.</summary>
        /// <param name="expression">The expression, which is expected to be an fn:path call.</param>
        private static string PathOf(string expression)
        {
            return Run(
                $"<xsl:template match=\"/\"><xsl:value-of select=\"{expression}\"/></xsl:template>",
                PathInput,
                version: "3.0");
        }

        [TestMethod]
        public void PathNamesEveryKindOfNodeInADocument()
        {
            // Every name in the braced form and every step but an attribute's carrying a position: the
            // point is a path anyone can evaluate, not a readable one. No prefix has to be in scope where
            // the answer is used, and the path selects the one node rather than its like-named siblings.
            Assert.AreEqual("/", PathOf("path(/)"));
            Assert.AreEqual("/Q{}doc[1]", PathOf("path(/*)"));
            Assert.AreEqual("/Q{}doc[1]/Q{}inner[1]", PathOf("path(/*/inner)"));
            Assert.AreEqual("/Q{}doc[1]/comment()[1]", PathOf("path((//comment())[1])"));
            Assert.AreEqual(
                "/Q{}doc[1]/processing-instruction(mypi)[1]",
                PathOf("path((//processing-instruction())[1])"));
            Assert.AreEqual("/Q{}doc[1]/text()[1]", PathOf("path((//text())[1])"));
            Assert.AreEqual("/Q{}doc[1]/@att", PathOf("path((//@att)[1])"));
        }

        [TestMethod]
        public void PathCountsAmongTheSiblingsItsOwnStepWouldSelect()
        {
            // An element among its like-named siblings, because the step names it; a comment among all of
            // its kind, because the step cannot.
            Assert.AreEqual(
                "/Q{}doc[1]/Q{}b[2]",
                Run(
                    "<xsl:template match=\"/\"><xsl:value-of select=\"path(//b[2])\"/></xsl:template>",
                    "<doc><a/><b/><a/><b/></doc>",
                    version: "3.0"));

            Assert.AreEqual(
                "/Q{}doc[1]/comment()[2]",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:value-of select=\"path((//comment())[2])\"/></xsl:template>",
                    "<doc><!--one--><a/><!--two--></doc>",
                    version: "3.0"));
        }

        [TestMethod]
        public void PathNamesATreeThatHasNoDocumentNode()
        {
            // A sequence constructor with an as declaration builds a tree rooted at an element, and there is
            // no leading slash to write for one. The specification names it with a call to fn:root() instead.
            const string Root = "Q{http://www.w3.org/2005/xpath-functions}root()";

            Assert.AreEqual(
                Root + "|" + Root + "/Q{}inner[1]|" + Root + "/@att",
                Run(
                    "<xsl:variable name=\"orphan\" as=\"element()\"><xsl:copy-of select=\"/*\"/></xsl:variable>"
                    + "<xsl:template match=\"/\"><xsl:value-of separator=\"|\" select=\""
                    + "path($orphan), path($orphan/inner), path($orphan/@att)\"/></xsl:template>",
                    PathInput,
                    version: "3.0"));
        }

        [TestMethod]
        public void PathOfNothingIsNothing()
        {
            Assert.AreEqual(string.Empty, PathOf("path(/nowhere)"));
        }

        [TestMethod]
        public void TheXPathVersionIsWhicheverOfTheTwoSaysThree()
        {
            // Vocabulary belongs to the processor, so a 3.0 processor has fn:path however old the stylesheet
            // says it is. And a stylesheet saying 3.0 gets it too, which is this engine's own offer rather
            // than the specification's: it is what made XPath 3.0 usable while Implemented still said 2.0,
            // and what a caller who asks for 2.0 keeps.
            Assert.AreEqual(
                "/Q{}doc[1]",
                Run(
                    "<xsl:template match=\"/\"><xsl:value-of select=\"path(/*)\"/></xsl:template>",
                    "<doc/>",
                    version: "2.0",
                    implemented: XsltVersion.V30));

            // Neither says 3.0, so the name is one this stylesheet has not declared and cannot call.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\"><xsl:value-of select=\"path(/*)\"/></xsl:template>",
                    "<doc/>",
                    version: "2.0"));

            StringAssert.Contains(error.Message, "path()");
        }

        // ---- function-available with an arity -------------------------------------------------------------

        /// <summary>Evaluates a boolean expression and returns "true" or "false".</summary>
        /// <param name="expression">The expression to evaluate.</param>
        private static string Asked(string expression)
        {
            return Run(
                "<xsl:template match=\"/\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" "
                + "xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\" "
                + "xmlns:array=\"http://www.w3.org/2005/xpath-functions/array\" "
                + "xmlns:math=\"http://www.w3.org/2005/xpath-functions/math\">"
                + $"<xsl:value-of select=\"{expression}\"/></xsl:template>"
                + "<xsl:function name=\"p:f\" xmlns:p=\"urn:p\"><xsl:param name=\"i\"/><xsl:param name=\"j\"/>"
                + "<xsl:sequence select=\"$i + $j\"/></xsl:function>",
                "<doc/>",
                version: "3.0");
        }

        [TestMethod]
        public void AnArityAsksWhetherACallCanBeWrittenThatWay()
        {
            // The whole point of the second argument: the name alone says a function of that name exists,
            // and the arity says one can be called with that many arguments.
            Assert.AreEqual("true", Asked("function-available('true', 0)"));
            Assert.AreEqual("false", Asked("function-available('true', 1)"));
            Assert.AreEqual("true", Asked("function-available('translate', 3)"));
            Assert.AreEqual("false", Asked("function-available('translate', 2)"));

            // A variadic function takes as many as you like above its minimum.
            Assert.AreEqual("true", Asked("function-available('concat', 17)"));
            Assert.AreEqual("false", Asked("function-available('concat', 0)"));
        }

        [TestMethod]
        public void AStylesheetFunctionIsAvailableAtTheArityItWasDeclaredAt()
        {
            Assert.AreEqual("true", Asked("function-available('Q{urn:p}f')"));
            Assert.AreEqual("true", Asked("function-available('Q{urn:p}f', 2)"));
            Assert.AreEqual("false", Asked("function-available('Q{urn:p}f', 1)"));
            Assert.AreEqual("false", Asked("function-available('Q{urn:p}nowhere')"));
        }

        [TestMethod]
        public void AnUnprefixedNameMeansTheFunctionNamespaceAndBracesMeanWhatTheySay()
        {
            // A name written without a prefix in a function call means the default function namespace, so
            // 'abs' asks about fn:abs. Q{} says outright that the function is in no namespace, which is a
            // different question and a different answer: nothing here is.
            Assert.AreEqual("true", Asked("function-available('abs', 1)"));
            Assert.AreEqual(
                "true",
                Asked("function-available('Q{http://www.w3.org/2005/xpath-functions}abs', 1)"));
            Assert.AreEqual("false", Asked("function-available('Q{}abs', 1)"));
        }

        [TestMethod]
        public void ATypeIsAFunctionWhereItHasAConstructor()
        {
            // Calling a type's name with one argument constructs one, so a type is a function too — except
            // for the four that name a place in the hierarchy rather than a set of values, which is exactly
            // where function-available and type-available come apart.
            Assert.AreEqual("true", Asked("function-available('xs:integer', 1)"));
            Assert.AreEqual("false", Asked("function-available('xs:integer', 2)"));
            Assert.AreEqual("false", Asked("function-available('xs:anyAtomicType', 1)"));
            Assert.AreEqual("false", Asked("function-available('xs:anyAtomicType')"));
            Assert.AreEqual("true", Asked("type-available('xs:anyAtomicType')"));
        }

        [TestMethod]
        public void TheOtherLibrariesAnswerForTheirOwnNamespaces()
        {
            Assert.AreEqual("true", Asked("function-available('map:merge', 1)"));
            Assert.AreEqual("true", Asked("function-available('map:merge', 2)"));
            Assert.AreEqual("false", Asked("function-available('map:merge', 3)"));
            Assert.AreEqual("false", Asked("function-available('map:nowhere')"));
            Assert.AreEqual("true", Asked("function-available('array:head', 1)"));
            Assert.AreEqual("true", Asked("function-available('math:pow', 2)"));
            Assert.AreEqual("false", Asked("function-available('math:pow', 1)"));
            Assert.AreEqual("true", Asked("function-available('math:pi', 0)"));
        }

        [TestMethod]
        public void AnAvailabilityAnswerMatchesWhatACallWouldDo()
        {
            // The property that made this worth building from the libraries' own tables rather than from a
            // second list: what is reported available is what a call may be written at. fn:round is two
            // functions sharing a name, one argument in the 2.0 library and two in the 3.0 one, and both
            // arities have to come back true.
            Assert.AreEqual("true", Asked("function-available('round', 1)"));
            Assert.AreEqual("true", Asked("function-available('round', 2)"));
            Assert.AreEqual("false", Asked("function-available('round', 3)"));

            // And the reverse for fn:string-join, whose one-argument form 3.0 added.
            Assert.AreEqual("true", Asked("function-available('string-join', 1)"));
            Assert.AreEqual("true", Asked("function-available('string-join', 2)"));
        }

        [TestMethod]
        public void TooManyArgumentsToTheProbeItselfIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Asked("function-available('true', 0, 0)"));

            StringAssert.Contains(error.Message, "1 or 2 arguments");
        }

        /// <summary>Evaluates a boolean expression on a processor of a given version.</summary>
        /// <param name="expression">The expression to evaluate.</param>
        /// <param name="implemented">The version the processor was asked to be.</param>
        private static string AskedOn(string expression, XsltVersion implemented)
        {
            return Run(
                "<xsl:template match=\"/\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                + $"<xsl:value-of select=\"{expression}\"/></xsl:template>",
                "<doc/>",
                version: "3.0",
                implemented: implemented);
        }

        [TestMethod]
        public void EveryLibraryTheEngineBuildsFromIsOneTheProbeAsks()
        {
            // Two libraries this engine builds calls from were never being asked: the node-building
            // functions and the JSON ones. What is reported has to be what a call may be written at, or a
            // stylesheet guarding a call with function-available() takes a fallback it did not need.
            Assert.AreEqual("false", Asked("function-available('analyze-string', 1)"));
            Assert.AreEqual("true", Asked("function-available('analyze-string', 2)"));
            Assert.AreEqual("true", Asked("function-available('analyze-string', 3)"));
            Assert.AreEqual("false", Asked("function-available('analyze-string', 4)"));
            Assert.AreEqual("true", Asked("function-available('parse-xml', 1)"));
            Assert.AreEqual("true", Asked("function-available('parse-xml-fragment', 1)"));
            Assert.AreEqual("true", Asked("function-available('json-to-xml', 1)"));
            Assert.AreEqual("true", Asked("function-available('xml-to-json', 2)"));
            Assert.AreEqual("true", Asked("function-available('parse-json', 1)"));
            Assert.AreEqual("false", Asked("function-available('json-to-xml', 3)"));

            // fn:uri-collection is there to be called, and reads the collection resolver as fn:collection()
            // does; what either finds is CollectionTests' business.
            Assert.AreEqual("true", Asked("function-available('uri-collection', 0)"));
            Assert.AreEqual("true", Asked("function-available('uri-collection', 1)"));
            Assert.AreEqual("false", Asked("function-available('uri-collection', 2)"));
        }

        [TestMethod]
        public void TheThreeListTypesAreConstructorsAsWellAsCastTargets()
        {
            // A cast is what a constructor is, and the cast to a list type was already defined -- split the
            // text, cast each token -- so xs:NMTOKENS('a b c') is that cast under its other spelling. They
            // are held apart from the atomic types because nothing may be an *instance* of one, and that is
            // why they had been missing from both the answer and the call.
            Assert.AreEqual("true", Asked("function-available('xs:NMTOKENS', 1)"));
            Assert.AreEqual("true", Asked("function-available('xs:IDREFS', 1)"));
            Assert.AreEqual("true", Asked("function-available('xs:ENTITIES', 1)"));
            Assert.AreEqual("false", Asked("function-available('xs:NMTOKENS', 2)"));

            Assert.AreEqual("3", Asked("count(xs:NMTOKENS('a b c'))"));
            Assert.AreEqual("a b c", Asked("string-join(xs:IDREFS(' a  b c '), ' ')"));
        }

        [TestMethod]
        public void AnXsltFunctionAddedInThreePointZeroIsAvailableOnlyOnAThreePointZeroProcessor()
        {
            // These are XSLT's own functions, built by hand rather than from a library, and the build site
            // asks about the processor alone -- so the answer has to. A version="3.0" stylesheet on a 2.0
            // processor is processed forwards-compatibly, and fn:snapshot() there is not a call that can be
            // made, whatever the stylesheet says of itself.
            foreach (string name in new[]
            {
                "snapshot", "copy-of", "accumulator-before", "accumulator-after", "current-merge-group",
                "current-merge-key", "current-output-uri", "available-system-properties", "transform",
            })
            {
                string either = $"function-available('{name}', 0) or function-available('{name}', 1)";

                Assert.AreEqual("false", Asked(either), name);
                Assert.AreEqual("true", AskedOn(either, XsltVersion.V30), name);
            }
        }

        // ---- system-property ------------------------------------------------------------------------------

        /// <summary>Reads one system property.</summary>
        /// <param name="property">The property name, as the stylesheet would write it.</param>
        /// <param name="version">The version the stylesheet claims.</param>
        private static string PropertyOf(string property, string version = "3.0")
        {
            return Run(
                "<xsl:template match=\"/\">"
                + $"<xsl:value-of select=\"system-property('{property}')\"/></xsl:template>",
                "<doc/>",
                version: version);
        }

        [TestMethod]
        public void TheSupportsPropertiesSayWhatThisEngineHasNot()
        {
            // The point of the function: a stylesheet that asks can take another route, and one told 'yes'
            // and then failed has been misled. Each of these three is an omission written where it can be
            // read - no schema to validate against, no streaming, no namespace axis.
            Assert.AreEqual("no", PropertyOf("xsl:is-schema-aware"));
            Assert.AreEqual("no", PropertyOf("xsl:supports-streaming"));
            Assert.AreEqual("no", PropertyOf("xsl:supports-namespace-axis"));

            // And xsl:evaluate is there, unless the caller switched it off; see EvaluateTests for that.
            Assert.AreEqual("yes", PropertyOf("xsl:supports-dynamic-evaluation"));
            Assert.AreEqual("yes", PropertyOf("xsl:supports-serialization"));
            Assert.AreEqual("yes", PropertyOf("xsl:supports-backwards-compatibility"));
            Assert.AreEqual("yes", PropertyOf("xsl:supports-higher-order-functions"));
        }

        [TestMethod]
        public void TheVersionPropertiesAreDecimalsWrittenAsStrings()
        {
            Assert.AreEqual("3.1", PropertyOf("xsl:xpath-version"));
            Assert.AreEqual("1.0", PropertyOf("xsl:xsd-version"));
            Assert.AreEqual("CodeDeeds", PropertyOf("xsl:vendor"));
            Assert.AreEqual("CodeDeeds.Xslt", PropertyOf("xsl:product-name"));

            // Empty for a property no value has been settled on, which is the specified answer and better
            // than a fabricated one. The product version is the assembly's, in three parts, which is also a
            // package version for a stylesheet that sets one from the other.
            Assert.AreEqual("https://github.com/CodeDeeds/CodeDeeds.Xslt", PropertyOf("xsl:vendor-url"));
            StringAssert.Matches(
                PropertyOf("xsl:product-version"),
                new System.Text.RegularExpressions.Regex("^[0-9]+\\.[0-9]+\\.[0-9]+$"));
        }

        [TestMethod]
        public void AnUnknownPropertyIsAnEmptyStringAndNotAnError()
        {
            // What makes the function usable for probing at all: a stylesheet may ask about a property it
            // will not get.
            Assert.AreEqual(string.Empty, PropertyOf("xsl:nothing-like-this"));
        }

        [TestMethod]
        public void WhatSupportsHigherOrderFunctionsSaysIsWhatFunctionAvailableSays()
        {
            // The pair has to agree, and the suite checks exactly that. Reporting 'yes' obliges the library
            // to be there at the arities the specification gives it.
            Assert.AreEqual("yes", PropertyOf("xsl:supports-higher-order-functions"));

            foreach (string call in new[]
            {
                "function-available('function-name', 1)",
                "function-available('function-arity', 1)",
                "function-available('filter', 2)",
                "function-available('for-each', 2)",
                "function-available('fold-left', 3)",
                "function-available('fold-right', 3)",
                "function-available('for-each-pair', 3)",
                "function-available('sort', 3)",
                "function-available('apply', 2)",
            })
            {
                Assert.AreEqual("true", Asked(call), call);
            }
        }

        // ---- fn:function-lookup ---------------------------------------------------------------------------

        /// <summary>Evaluates an expression with a stylesheet function and the usual prefixes in scope.</summary>
        /// <param name="expression">The expression to evaluate.</param>
        private static string LookedUp(string expression)
        {
            return Run(
                "<xsl:template match=\"/\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" "
                + "xmlns:fn=\"http://www.w3.org/2005/xpath-functions\" xmlns:p=\"urn:p\">"
                + $"<xsl:value-of select=\"{expression}\"/></xsl:template>"
                + "<xsl:function name=\"p:add\" xmlns:p=\"urn:p\"><xsl:param name=\"i\"/><xsl:param name=\"j\"/>"
                + "<xsl:sequence select=\"$i + $j\"/></xsl:function>",
                "<doc/>",
                version: "3.0");
        }

        [TestMethod]
        public void ALookedUpLibraryFunctionCanBeCalled()
        {
            Assert.AreEqual("3", LookedUp("function-lookup(xs:QName('fn:count'), 1)((1, 2, 3))"));
            Assert.AreEqual("ac", LookedUp("function-lookup(xs:QName('fn:concat'), 2)('a', 'c')"));
            Assert.AreEqual("2", LookedUp("function-lookup(xs:QName('xs:integer'), 1)('2')"));
        }

        [TestMethod]
        public void ALookedUpStylesheetFunctionCanBeCalled()
        {
            Assert.AreEqual("7", LookedUp("function-lookup(xs:QName('p:add'), 2)(3, 4)"));
        }

        [TestMethod]
        public void ALookedUpXsltFunctionKeepsTheScopeOfTheLookup()
        {
            // The reason these are built where the lookup was written rather than where it runs: the name
            // handed to system-property carries a prefix, and only the lookup's own scope binds it.
            Assert.AreEqual(
                "CodeDeeds",
                LookedUp("function-lookup(xs:QName('fn:system-property'), 1)('xsl:vendor')"));
        }

        [TestMethod]
        public void ANameOrArityNothingHasIsTheEmptySequence()
        {
            // The specified answer for a name it cannot find - not an error, which is what makes the
            // function usable for probing at all.
            Assert.AreEqual(string.Empty, LookedUp("function-lookup(xs:QName('fn:nowhere'), 1)"));
            Assert.AreEqual(string.Empty, LookedUp("function-lookup(xs:QName('fn:count'), 7)"));
            Assert.AreEqual(string.Empty, LookedUp("function-lookup(xs:QName('p:add'), 1)"));

            // A type with no constructor is a name a library has and will not build, which comes back the
            // same way rather than as the error a written call would get.
            Assert.AreEqual(string.Empty, LookedUp("function-lookup(xs:QName('xs:anyAtomicType'), 1)"));
        }

        [TestMethod]
        public void FunctionLookupReportsItselfAvailable()
        {
            Assert.AreEqual("true", Asked("function-available('function-lookup', 2)"));
            Assert.AreEqual("false", Asked("function-available('function-lookup', 1)"));
        }

        // ---- document() ----------------------------------------------------------------------------------

        /// <summary>A resolver holding a few documents, each under a URI of its own.</summary>
        private sealed class Library : IXsltResolver
        {
            private readonly Dictionary<string, string> m_documents = new(StringComparer.Ordinal);

            /// <summary>Adds a document.</summary>
            /// <param name="uri">The URI it answers to.</param>
            /// <param name="content">Its content.</param>
            public Library Add(string uri, string content)
            {
                m_documents[uri] = content;
                return this;
            }

            /// <inheritdoc/>
            public ResolvedResource? Resolve(string name, string? baseUri)
            {
                string absolute = baseUri is null || !Uri.TryCreate(new Uri(baseUri), name, out Uri? made)
                    ? name
                    : made.AbsoluteUri;

                return m_documents.TryGetValue(absolute, out string? text)
                    ? new ResolvedResource(new StringReader(text), absolute)
                    : null;
            }
        }

        /// <summary>Runs a stylesheet against a library of documents.</summary>
        /// <param name="body">The stylesheet's declarations.</param>
        /// <param name="library">The documents it may reach.</param>
        /// <param name="input">The source document.</param>
        /// <param name="inputUri">Where the input came from.</param>
        private static string RunWithDocuments(
            string body,
            Library library,
            string input = "<r/>",
            string? inputUri = null)
        {
            return new Xslt(
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>{body}</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    DocumentResolver = library,
                    BaseUri = "file:///sheets/one.xsl",
                    InputUri = inputUri,
                }).TransformXml(input);
        }

        [TestMethod]
        public void DocumentTakesASequenceOfUrisAndNotOneStringMadeOfThem()
        {
            // Reading the argument as a single string turned two URIs into one with a space in it, which no
            // resolver was ever going to find.
            Library library = new Library()
                .Add("file:///sheets/a.xml", "<a>A</a>")
                .Add("file:///sheets/b.xml", "<b>B</b>");

            Assert.AreEqual(
                "<out>A B</out>",
                RunWithDocuments(
                    Root("<xsl:value-of select=\"document(('a.xml', 'b.xml'))/*\"/>"),
                    library));
        }

        [TestMethod]
        public void DocumentOfNothingIsAnEmptyNodeSet()
        {
            Assert.AreEqual(
                "<out>0</out>",
                RunWithDocuments(Root("<xsl:value-of select=\"count(document(()))\"/>"), new Library()));
        }

        [TestMethod]
        public void ARepeatedUriNamesOneDocument()
        {
            Library library = new Library().Add("file:///sheets/a.xml", "<a/>");

            Assert.AreEqual(
                "<out>1</out>",
                RunWithDocuments(
                    Root("<xsl:value-of select=\"count(document(('a.xml', 'a.xml')))\"/>"),
                    library));
        }

        [TestMethod]
        public void ANodeArgumentResolvesAgainstWhereThatNodeCameFrom()
        {
            // The rule that lets a catalogue be read: document(@href) over a list of relative references
            // means each one relative to the catalogue, which is rarely where the stylesheet is.
            Library library = new Library()
                .Add("file:///docs/inner.xml", "<inner>found</inner>");

            Assert.AreEqual(
                "<out>found</out>",
                RunWithDocuments(
                    Root("<xsl:value-of select=\"document(/r/@href)/*\"/>"),
                    library,
                    "<r href=\"inner.xml\"/>",
                    inputUri: "file:///docs/catalogue.xml"));
        }

        [TestMethod]
        public void ASecondArgumentSettlesTheBaseForEveryReference()
        {
            // And it wins over each item's own base, which is what makes it worth writing at all.
            Library library = new Library()
                .Add("file:///elsewhere/inner.xml", "<inner>elsewhere</inner>")
                .Add("file:///elsewhere/here.xml", "<here/>");

            Assert.AreEqual(
                "<out>elsewhere</out>",
                RunWithDocuments(
                    Root("<xsl:value-of select=\"document('inner.xml', document('/elsewhere/here.xml'))/*\"/>"),
                    library,
                    "<r/>",
                    inputUri: "file:///docs/catalogue.xml"));
        }

        [TestMethod]
        public void AStringArgumentStillResolvesAgainstTheStylesheet()
        {
            // A string arrived from wherever the expression got it and carries no origin of its own, so the
            // stylesheet is the only base there is for one.
            Library library = new Library().Add("file:///sheets/a.xml", "<a>sheet</a>");

            Assert.AreEqual(
                "<out>sheet</out>",
                RunWithDocuments(
                    Root("<xsl:value-of select=\"document('a.xml')/*\"/>"),
                    library,
                    "<r/>",
                    inputUri: "file:///docs/catalogue.xml"));
        }

        // ---- xsl:merge, refused --------------------------------------------------------------------------

        /// <summary>Wraps merge sources and an action in a stylesheet and runs it.</summary>
        /// <param name="merge">The <c>xsl:merge</c> content.</param>
        private static string Merged(string merge)
        {
            return Run(
                "<xsl:template match=\"/\"><out><xsl:merge>" + merge + "</xsl:merge></out></xsl:template>",
                "<doc><a n=\"1\"/><a n=\"2\"/></doc>",
                version: "3.0",
                implemented: XsltVersion.V30);
        }

        /// <summary>The code a merge is refused with.</summary>
        /// <param name="merge">The <c>xsl:merge</c> content.</param>
        private static string MergeRefusal(string merge)
        {
            return Assert.ThrowsExactly<XsltException>(() => Merged(merge)).Code ?? "(none)";
        }

        [TestMethod]
        public void AMergeSourceHasToSayWhereItsItemsComeFromAndWhatOrdersThem()
        {
            Assert.AreEqual(
                "XTSE0010",
                MergeRefusal(
                    "<xsl:merge-source><xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));

            Assert.AreEqual(
                "XTSE0010",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\"/>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));
        }

        [TestMethod]
        public void OneSourceCannotBeIteratedTwoWays()
        {
            Assert.AreEqual(
                "XTSE3195",
                MergeRefusal(
                    "<xsl:merge-source for-each-item=\"/doc\" for-each-source=\"'a.xml'\" select=\"a\">"
                    + "<xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));
        }

        [TestMethod]
        public void AMergeKeyComesFromTheAttributeOrTheContentAndNotBoth()
        {
            Assert.AreEqual(
                "XTSE3200",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\"><xsl:sequence select=\"@n\"/></xsl:merge-key>"
                    + "</xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));
        }

        [TestMethod]
        public void SortBeforeMergeIsAYesOrANo()
        {
            // Ignored by this engine, which sorts every source whatever it says - but an attribute nobody
            // reads is an attribute nobody checks, and 'maybe' is neither.
            Assert.AreEqual(
                "XTSE0020",
                MergeRefusal(
                    "<xsl:merge-source sort-before-merge=\"maybe\" select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));
        }

        [TestMethod]
        public void AMergeHasOneActionAndMayEndWithAFallback()
        {
            // The content model is (merge-source+, merge-action, fallback*). A second action is a second
            // answer to what is done with a group, and nothing decides between them.
            Assert.AreEqual(
                "XTSE0010",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\"><xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"
                    + "<xsl:merge-action><y/></xsl:merge-action>"));

            // An xsl:fallback is for a processor without xsl:merge, which this one is not: it is admitted
            // after the action, and ignored.
            Assert.AreEqual(
                "<out><x/><x/></out>",
                Merged(
                    "<xsl:merge-source select=\"/doc/a\"><xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"
                    + "<xsl:fallback>111</xsl:fallback><xsl:fallback>222</xsl:fallback>"));

            // After, not before: the content model is ordered, so a fallback ahead of the action, or a
            // source behind it, is out of place.
            Assert.AreEqual(
                "XTSE0010",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\"><xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:fallback>22</xsl:fallback>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));

            Assert.AreEqual(
                "XTSE0010",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\"><xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"
                    + "<xsl:merge-source select=\"/doc/a\"><xsl:merge-key select=\"@n\"/></xsl:merge-source>"));
        }

        [TestMethod]
        public void AMergeKeyIsNotASortAndHasNoStableAttribute()
        {
            // xsl:merge-key takes the attributes of xsl:sort but 'stable': a merge is not a sort, and there
            // is nothing for stability to be a property of. An attribute the element does not have is
            // XTSE0090, as everywhere.
            Assert.AreEqual(
                "XTSE0090",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\" stable=\"yes\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));
        }

        [TestMethod]
        public void TheMergeFunctionsCannotBeWrittenInAPattern()
        {
            // A pattern decides whether a node matches, and that is asked where no merge is under way.
            foreach ((string called, string code) in new[]
            {
                ("current-merge-group()", "XTSE3470"),
                ("current-merge-key()", "XTSE3500"),
            })
            {
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => Run(
                        $"<xsl:template match=\"a[{called}]\"><x/></xsl:template>",
                        "<doc><a/></doc>",
                        version: "3.0"));

                Assert.AreEqual(code, error.Code, called);
            }
        }

        [TestMethod]
        public void TheMergeFunctionsBelongToTheActionAndNotToWhatItCalls()
        {
            // Narrower than current-group(), which a called template does see. The action's own constructor
            // is the whole of the scope, so a template it calls is already outside.
            const string Sources =
                "<xsl:merge-source select=\"/doc/a\"><xsl:merge-key select=\"@n\"/></xsl:merge-source>";

            Assert.AreEqual(
                "XTDE3480",
                Assert.ThrowsExactly<XsltException>(() => Run(
                    "<xsl:template match=\"/\"><out><xsl:merge>" + Sources
                    + "<xsl:merge-action><xsl:call-template name=\"n\"/></xsl:merge-action>"
                    + "</xsl:merge></out></xsl:template>"
                    + "<xsl:template name=\"n\"><xsl:value-of select=\"current-merge-group()\"/></xsl:template>",
                    "<doc><a n=\"1\"/></doc>",
                    version: "3.0",
                    implemented: XsltVersion.V30)).Code);

            Assert.AreEqual(
                "XTDE3510",
                Assert.ThrowsExactly<XsltException>(() => Run(
                    "<xsl:template match=\"/\"><out><xsl:merge>" + Sources
                    + "<xsl:merge-action><xsl:call-template name=\"n\"/></xsl:merge-action>"
                    + "</xsl:merge></out></xsl:template>"
                    + "<xsl:template name=\"n\"><xsl:value-of select=\"current-merge-key()\"/></xsl:template>",
                    "<doc><a n=\"1\"/></doc>",
                    version: "3.0",
                    implemented: XsltVersion.V30)).Code);

            // And inside the action itself it answers, including within an xsl:for-each, which is still the
            // action's own constructor.
            Assert.AreEqual(
                "<out>1|1</out>",
                Merged(
                    Sources
                    + "<xsl:merge-action><xsl:value-of select=\"current-merge-key()\"/>|"
                    + "<xsl:for-each select=\"1\"><xsl:value-of select=\"current-merge-key()\"/>"
                    + "</xsl:for-each></xsl:merge-action>").Replace("2|2", string.Empty));
        }

        [TestMethod]
        public void AskingForASourceTheMergeHasNotGotIsAnError()
        {
            // Not an empty answer: a source that contributed nothing to this group answers with nothing, and
            // a source that does not exist is a different thing to say.
            Assert.AreEqual(
                "XTDE3490",
                MergeRefusal(
                    "<xsl:merge-source name=\"one\" select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action>"
                    + "<xsl:value-of select=\"current-merge-group('elsewhere')\"/></xsl:merge-action>"));

            Assert.AreEqual(
                "<out>12</out>",
                Merged(
                    "<xsl:merge-source name=\"one\" select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action>"
                    + "<xsl:value-of select=\"current-merge-group('one')/@n\"/></xsl:merge-action>"));
        }

        [TestMethod]
        public void TwoSourcesHaveToDescribeTheOrderingWithTheSameNumberOfKeys()
        {
            // Keys correspond by position, so a source with two of them and a source with one have nothing
            // to compare their second by.
            Assert.AreEqual(
                "XTSE2200",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\"><xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\"/><xsl:merge-key select=\".\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));
        }

        [TestMethod]
        public void TwoSourcesHaveToAgreeAboutHowTheirKeysOrder()
        {
            // Two orderings are not one: whichever way round the merge compared them, one of the sources
            // would be walked against an order it did not ask for.
            Assert.AreEqual(
                "XTDE2210",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\" order=\"ascending\"/></xsl:merge-source>"
                    + "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\" order=\"descending\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));

            // Written on one and left off the other counts as a disagreement too, even where the one left
            // off would have defaulted to the same thing. That is the 3.0 rule as written.
            Assert.AreEqual(
                "XTDE2210",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\" order=\"ascending\"/></xsl:merge-source>"
                    + "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));
        }

        [TestMethod]
        public void HowAMergeKeyOrdersIsReadWhenTheMergeRuns()
        {
            // The ordering attributes are attribute value templates, so two sources that agree are two that
            // agree about what the templates came out as, not about how they were written.
            Assert.AreEqual(
                "<out>12</out>",
                Merged(
                    "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\" order=\"{'ascend' || 'ing'}\"/></xsl:merge-source>"
                    + "<xsl:merge-source select=\"()\">"
                    + "<xsl:merge-key select=\"@n\" order=\"ascending\"/></xsl:merge-source>"
                    + "<xsl:merge-action><xsl:value-of select=\"current-merge-key()\"/>"
                    + "</xsl:merge-action>"));

            Assert.AreEqual(
                "XTDE2210",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\" order=\"{'desc' || 'ending'}\"/></xsl:merge-source>"
                    + "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\" order=\"ascending\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));
        }

        [TestMethod]
        public void ASourceIsTakenAsAlreadySortedAndRefusedWhenItIsNot()
        {
            // What separates a merge from concatenating and grouping: it reads each source once, in step
            // with the others, which it can only do if each one arrives in order.
            const string Backwards =
                "<xsl:merge-source select=\"(/doc/a[2], /doc/a[1])\"{0}>"
                + "<xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                + "<xsl:merge-action><xsl:value-of select=\"current-merge-key()\"/></xsl:merge-action>";

            Assert.AreEqual("XTDE2220", MergeRefusal(string.Format(Backwards, string.Empty)));

            // Unless the caller withdraws the promise, and then sorting is the instruction's own job.
            Assert.AreEqual(
                "<out>12</out>",
                Merged(string.Format(Backwards, " sort-before-merge=\"yes\"")));
        }

        [TestMethod]
        public void AKeyThatIsANumberIsOrderedAsOneWithoutBeingToldTo()
        {
            // Read as text, 1, 2, 10 is out of order and the merge would refuse its own input. data-type is
            // a 1.0 way of saying this; a value that knows it is a number needs no telling.
            Assert.AreEqual(
                "<out>1|2|10|</out>",
                Merged(
                    "<xsl:merge-source select=\"(1, 2, 10)\">"
                    + "<xsl:merge-key select=\".\"/></xsl:merge-source>"
                    + "<xsl:merge-action><xsl:value-of select=\"current-merge-key()\"/>|"
                    + "</xsl:merge-action>"));
        }

        [TestMethod]
        public void KeysOfTwoSourcesHaveToBeComparableWithOneAnother()
        {
            // Deciding which source to take from next means comparing a key of one against a key of the
            // other, and XPath has no comparison between a date and a number.
            Assert.AreEqual(
                "XTTE2230",
                MergeRefusal(
                    "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"number(@n)\"/></xsl:merge-source>"
                    + "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"current-date()\"/></xsl:merge-source>"
                    + "<xsl:merge-action><x/></xsl:merge-action>"));
        }

        [TestMethod]
        public void AMergeKeyIsEvaluatedWithASingletonFocus()
        {
            // Not the position in the sequence, which is what an xsl:sort key sees. A merge is meant to be
            // readable one item at a time, and an item arriving on its own does not know where in its source
            // it stands — so a key of position() is 1 for every item, and every item is in one group.
            Assert.AreEqual(
                "<out><g k=\"1\">12</g></out>",
                Merged(
                    "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"position()\"/></xsl:merge-source>"
                    + "<xsl:merge-action><g k=\"{current-merge-key()}\">"
                    + "<xsl:value-of select=\"current-merge-group()/@n\" separator=\"\"/></g>"
                    + "</xsl:merge-action>"));
        }

        [TestMethod]
        public void AMergeActionSeesTheGroupsFirstItemAtTheGroupsOwnPlace()
        {
            // position() counts groups rather than items, and the context item is what opened the group -
            // the same focus xsl:for-each-group gives its body. last() is the number of groups, which is why
            // every group is taken before any action runs.
            Assert.AreEqual(
                "<out><g p=\"1\" of=\"2\"><a n=\"1\"/></g><g p=\"2\" of=\"2\"><a n=\"2\"/></g></out>",
                Merged(
                    "<xsl:merge-source select=\"/doc/a\">"
                    + "<xsl:merge-key select=\"@n\"/></xsl:merge-source>"
                    + "<xsl:merge-action><g p=\"{position()}\" of=\"{last()}\">"
                    + "<xsl:copy-of select=\".\"/></g></xsl:merge-action>"));
        }

        [TestMethod]
        public void EachAnchorItemOfASourceIsAnInputSequenceOfItsOwn()
        {
            // for-each-item runs the select once per anchor item and each run is its own sequence, which is
            // the point of it: two sorted sections of one document are two sorted inputs, and their
            // concatenation is in no order at all.
            Assert.AreEqual(
                "<out>1|2|3|4|</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:merge>"
                    + "<xsl:merge-source for-each-item=\"/doc/part\" select=\"a\">"
                    + "<xsl:merge-key select=\"number(@n)\"/></xsl:merge-source>"
                    + "<xsl:merge-action><xsl:value-of select=\"current-merge-key()\"/>|"
                    + "</xsl:merge-action></xsl:merge></out></xsl:template>",
                    "<doc><part><a n='1'/><a n='3'/></part><part><a n='2'/><a n='4'/></part></doc>",
                    version: "3.0",
                    implemented: XsltVersion.V30));
        }

        [TestMethod]
        public void AStylesheetMaySayWhereToBeginWithNoSourceAndNoCaller()
        {
            // XSLT 3.0's default entry point, which is how a stylesheet that generates rather than
            // transforms is written: no arrangement with whoever runs it.
            Assert.AreEqual(
                "<ok/>",
                new Xslt(
                    $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                    + "<xsl:template name=\"xsl:initial-template\"><ok/></xsl:template>"
                    + "</xsl:stylesheet>",
                    new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 }).Transform());
        }

        [TestMethod]
        public void AnExpressionNestedTooDeeplyIsRefusedRatherThanFatal()
        {
            // Recursive descent costs a frame per precedence level per nesting, and a stack overflow is not
            // an error a caller can catch - it takes the process down. This one did, in a conformance run.
            string deep = new string('(', 5000) + "1" + new string(')', 5000);

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root($"<xsl:value-of select=\"{deep}\"/>"), "<doc/>", version: "3.0"));

            Assert.AreEqual("XPST0003", error.Code);
        }

        [TestMethod]
        public void CopyOfADeeplyNestedDocumentDoesNotGrowTheStack()
        {
            // The depth of a document is the one thing about its shape a stylesheet cannot bound, and a copy
            // that recursed once per level ran out of stack a few thousand in. Each level has a sibling
            // before the nesting continues, so the walk has to climb back out as well as go down. The
            // namespaces are copied, as they are by default: collecting an element's in-scope ones used to
            // walk every ancestor, which over this depth is the square of it and took half a minute.
            const int depth = 50000;
            string input = "<r xmlns:p=\"urn:p\">"
                + string.Concat(Enumerable.Repeat("<a><b/>", depth))
                + "x"
                + string.Concat(Enumerable.Repeat("</a>", depth))
                + "</r>";

            string result = Run(Root("<xsl:copy-of select=\"/\"/>"), input);

            Assert.AreEqual("<out>" + input.Replace("<b/>", "<b/>") + "</out>", result);
        }

        [TestMethod]
        public void ATypeNestedTooDeeplyIsRefusedRatherThanFatal()
        {
            // A type nests on its own, with no expression between the levels to pass the guard above, so it
            // has a guard of its own.
            string deep = string.Concat(Enumerable.Repeat("array(", 20000)) + "item()" + new string(')', 20000);

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root($"<xsl:value-of select=\"1 instance of {deep}\"/>"), "<doc/>", version: "3.0"));

            Assert.AreEqual("XPST0003", error.Code);
        }

        [TestMethod]
        public void ARunOfSignsIsReadAsItsInnermostSignAndTheParityOfTheRest()
        {
            // Signs nest without an expression between them, so they are counted rather than recursed over,
            // and the tree they make is two levels deep at most — which is what the evaluator needs too. The
            // innermost sign is the one kept, so that it is the one to refuse an operand that is not a number.
            Assert.AreEqual(
                "<out>-1,1,-1,1,3</out>",
                Run(
                    Root(
                        "<xsl:value-of select=\"---1\"/>,<xsl:value-of select=\"----1\"/>,"
                        + "<xsl:value-of select=\"- + - - 1\"/>,<xsl:value-of select=\"+ - - 1\"/>,"
                        + "<xsl:value-of select=\"-+-3\"/>"),
                    "<doc/>"));

            string signs = new string('-', 20001);
            Assert.AreEqual("<out>-1</out>", Run(Root($"<xsl:value-of select=\"{signs}1\"/>"), "<doc/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:value-of select=\"-+'a'\"/>"), "<doc/>"));
            StringAssert.Contains(error.Message, "'+'");
        }

        // ---- What a result document computes for itself -------------------------------------------------

        /// <summary>As <see cref="RunWithResults"/>, under a processor claiming 3.0 for a 3.0 stylesheet.</summary>
        private static (string Principal, CollectingResolver Results) RunWithResults30(string body, string input)
        {
            CollectingResolver resolver = new CollectingResolver();

            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>{body}</xsl:stylesheet>";
            string principal = new Xslt(
                stylesheet,
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    ResultResolver = resolver,
                    Version = XsltVersion.V30,
                })
                .TransformXml(input);

            return (principal, resolver);
        }

        [TestMethod]
        public void AResultDocumentsSerializationAttributesMayBeComputed()
        {
            // The method and the declaration are templates read when the instruction runs, so a document can
            // decide its own serialization from its data. A computed value the attribute may not take is the
            // serializer's SEPM0016, where a written one was refused when the stylesheet was read.
            (string _, CollectingResolver results) = RunWithResults30(
                Root("<xsl:result-document href=\"a.txt\" method=\"{/r/@m}\" omit-xml-declaration=\"{/r/@o}\">"
                    + "<p>x &amp; y</p></xsl:result-document>"),
                "<r m=\"text\" o=\"yes\"/>");

            Assert.AreEqual("x & y", results.Documents["a.txt"].ToString());

            XsltException error = Assert.ThrowsExactly<XsltException>(() => RunWithResults30(
                Root("<xsl:result-document href=\"b.xml\" indent=\"{/r/@i}\"><p/></xsl:result-document>"),
                "<r i=\"maybe\"/>"));

            Assert.AreEqual("SEPM0016", error.Code);
        }

        [TestMethod]
        public void AResultDocumentsFormatMayBeComputed()
        {
            // A computed format names an xsl:output when the instruction runs, and the attributes written on
            // the instruction still apply over what it settled; a name no xsl:output has is XTDE1460.
            (string _, CollectingResolver results) = RunWithResults30(
                "<xsl:output name=\"f1\" method=\"text\"/>"
                + "<xsl:output name=\"f2\" method=\"xml\" omit-xml-declaration=\"yes\"/>"
                + Root("<xsl:for-each select=\"/r/@f\"><xsl:result-document href=\"{.}.out\" format=\"{.}\">"
                    + "<p>a</p></xsl:result-document></xsl:for-each>"),
                "<r f=\"f2\"/>");

            Assert.AreEqual("<p>a</p>", results.Documents["f2.out"].ToString());

            XsltException error = Assert.ThrowsExactly<XsltException>(() => RunWithResults30(
                Root("<xsl:result-document href=\"c.out\" format=\"{/r/@f}\"><p/></xsl:result-document>"),
                "<r f=\"nonesuch\"/>"));

            Assert.AreEqual("XTDE1460", error.Code);
        }

        [TestMethod]
        public void ANamedItemSeparatorGoesBetweenEveryTopLevelItem()
        {
            // With a separator named, the result is written as the sequence it is: the separator between a
            // comment and a number as much as between two numbers. Without one, a tree's rule — a space
            // between adjacent atomic values and nothing beside a node. And a document with nothing in it is
            // still an XML document, declaration and all.
            (string _, CollectingResolver results) = RunWithResults30(
                Root(
                    "<xsl:result-document href=\"s.xml\" item-separator=\"~\">"
                    + "<xsl:comment>c</xsl:comment><xsl:sequence select=\"1 to 3\"/></xsl:result-document>"
                    + "<xsl:result-document href=\"t.xml\">"
                    + "<xsl:comment>c</xsl:comment><xsl:sequence select=\"1 to 3\"/></xsl:result-document>"
                    + "<xsl:result-document href=\"e.xml\" omit-xml-declaration=\"no\"/>"),
                "<r/>");

            Assert.AreEqual("<!--c-->~1~2~3", results.Documents["s.xml"].ToString());
            Assert.AreEqual("<!--c-->1 2 3", results.Documents["t.xml"].ToString());
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>", results.Documents["e.xml"].ToString());
        }

        [TestMethod]
        public void AMapCannotBeSerializedAsXml()
        {
            // The xml, html, xhtml and text methods have no way to write a map or a function item, and the
            // specification makes that a serialization error rather than text of some kind.
            XsltException error = Assert.ThrowsExactly<XsltException>(() => RunWithResults30(
                "<xsl:template match=\"/\"><xsl:sequence select=\"map { 'a': 1 }\"/></xsl:template>",
                "<r/>"));

            Assert.AreEqual("SENR0001", error.Code);
        }

        // ---- What an xsl:next-iteration supplies ------------------------------------------------------------

        /// <summary>Runs a 3.0 body through a 3.0 processor, which is what xsl:iterate needs.</summary>
        private static string RunsAtThreePointZero(string body)
        {
            return Run(body, "<r/>", "3.0", null, XsltVersion.V30);
        }

        [TestMethod]
        public void ANextIterationSuppliesEachParameterOnce()
        {
            // The second would silently win, and which of two values the iteration was meant to start again
            // with is not a processor's to decide. The same rule, and the same code, as on any other call.
            Assert.AreEqual(
                "XTSE0670",
                Assert.ThrowsExactly<XsltException>(() => RunsAtThreePointZero(Root(
                    "<xsl:iterate select=\"1 to 3\"><xsl:param name=\"n\" select=\"0\"/>"
                    + "<xsl:next-iteration><xsl:with-param name=\"n\" select=\"1\"/>"
                    + "<xsl:with-param name=\"n\" select=\"2\"/></xsl:next-iteration>"
                    + "</xsl:iterate>"))).Code);

            // One value each is what it takes.
            Assert.AreEqual(
                "<out>3</out>",
                RunsAtThreePointZero(Root(
                    "<xsl:iterate select=\"1 to 3\"><xsl:param name=\"n\" select=\"0\"/>"
                    + "<xsl:on-completion><xsl:value-of select=\"$n\"/></xsl:on-completion>"
                    + "<xsl:next-iteration><xsl:with-param name=\"n\" select=\"$n + 1\"/>"
                    + "</xsl:next-iteration></xsl:iterate>")));
        }

        [TestMethod]
        public void TheAsOfAWithParamConvertsWhatTheNextIterationSupplies()
        {
            // Both types apply where both are written: the as on the xsl:with-param converts the value the
            // call supplies, by the function conversion rules, and the as on the xsl:param says what the
            // parameter holds. So a parameter with no type of its own may still be handed an xs:double*.
            Assert.AreEqual(
                "<out>true</out>",
                RunsAtThreePointZero(Root(
                    "<xsl:iterate select=\"1 to 2\"><xsl:param name=\"total\" select=\"0\"/>"
                    + "<xsl:on-completion><xsl:value-of select=\"$total instance of xs:double*\"/>"
                    + "</xsl:on-completion>"
                    + "<xsl:next-iteration>"
                    + "<xsl:with-param name=\"total\" select=\"$total, .\" as=\"xs:double*\"/>"
                    + "</xsl:next-iteration></xsl:iterate>")));

            // Without one, the parameter's own type is what the value is held to, as it always was.
            Assert.AreEqual(
                "<out>true</out>",
                RunsAtThreePointZero(Root(
                    "<xsl:iterate select=\"1 to 2\">"
                    + "<xsl:param name=\"total\" as=\"xs:double*\" select=\"()\"/>"
                    + "<xsl:on-completion><xsl:value-of select=\"$total instance of xs:double*\"/>"
                    + "</xsl:on-completion>"
                    + "<xsl:next-iteration><xsl:with-param name=\"total\" select=\"$total, .\"/>"
                    + "</xsl:next-iteration></xsl:iterate>")));
        }
    }
}
