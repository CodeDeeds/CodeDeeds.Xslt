namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the attributes XSLT 3.0 added to elements XSLT 1.0 and 2.0 already had.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate class because these share a hazard the instructions do not. A new element is refused
    /// outright by a processor that does not have it, so a stylesheet finds out; a new attribute on an old
    /// element is silently ignored unless something checks, and then the instruction does something other
    /// than what was written. Each of these therefore has a test that the attribute is <em>refused</em> below
    /// 3.0 as well as one that it works at 3.0.
    /// </para>
    /// <para>
    /// There is no oracle for any of it. <c>XslCompiledTransform</c> is 1.0, and the expectations here are
    /// read from the specification.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class Xslt30AttributeTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private const string Xs = "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"";

        private static string Run(
            string body,
            string input,
            string version = "3.0",
            string stylesheetAttributes = "")
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"{version}\" {Xsl} {Xs} xmlns:x=\"urn:x\""
                + " exclude-result-prefixes=\"xs x\""
                + stylesheetAttributes
                + ">"
                + body
                + "</xsl:stylesheet>";

            // The processor claims what the stylesheet says, so that a 2.0 stylesheet is refused what a 2.0
            // processor refuses; the engine itself claims 3.0 for a caller who names none.
            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                Version = version == "3.0" ? XsltVersion.V30 : XsltVersion.V20,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        private static string Root(string body) => $"<xsl:template match=\"/\"><out>{body}</out></xsl:template>";

        /// <summary>Two countries, two populations, so a pair identifies a group and neither half does.</summary>
        private const string Cities =
            "<doc><c country='NO' pop='700'>Oslo</c><c country='NO' pop='280'>Bergen</c>"
            + "<c country='SE' pop='280'>Malmo</c><c country='NO' pop='700'>Trondheim</c></doc>";

        // ---- composite, on xsl:for-each-group ------------------------------------------------------------

        [TestMethod]
        public void CompositeReadsTheValuesOfAGroupingKeyAsOneKey()
        {
            Assert.AreEqual(
                "<out><g k=\"NO 700\">Oslo Trondheim</g><g k=\"NO 280\">Bergen</g>"
                + "<g k=\"SE 280\">Malmo</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/doc/c\" group-by=\"@country, @pop\" composite=\"yes\">"
                        + "<g k=\"{current-grouping-key()}\">"
                        + "<xsl:value-of select=\"current-group()\"/>"
                        + "</g></xsl:for-each-group>"),
                    Cities));
        }

        [TestMethod]
        public void WithoutCompositeEachValueOfTheKeyNamesItsOwnGroup()
        {
            // The same stylesheet without the attribute, which is what makes the one above worth having:
            // four groups rather than three, and every city in two of them.
            Assert.AreEqual(
                "<out><g k=\"NO\">Oslo Bergen Trondheim</g><g k=\"700\">Oslo Trondheim</g>"
                + "<g k=\"280\">Bergen Malmo</g><g k=\"SE\">Malmo</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/doc/c\" group-by=\"@country, @pop\">"
                        + "<g k=\"{current-grouping-key()}\">"
                        + "<xsl:value-of select=\"current-group()\"/>"
                        + "</g></xsl:for-each-group>"),
                    Cities));
        }

        [TestMethod]
        public void ACompositeGroupingKeyIsTheWholeSequence()
        {
            // current-grouping-key() gives back all of it, so the parts can be told apart afterwards.
            Assert.AreEqual(
                "<out>2|NO|700</out>",
                Run(
                    Root("<xsl:for-each-group select=\"/doc/c[1]\" group-by=\"@country, @pop\" composite=\"yes\">"
                        + "<xsl:value-of select=\"count(current-grouping-key())\"/>|"
                        + "<xsl:value-of select=\"current-grouping-key()[1]\"/>|"
                        + "<xsl:value-of select=\"current-grouping-key()[2]\"/>"
                        + "</xsl:for-each-group>"),
                    Cities));
        }

        [TestMethod]
        public void CompositeKeysOfDifferentLengthsDoNotRunTogether()
        {
            // The hazard a join on a separator has: ("a", "bc") and ("ab", "c") spell the same thing once
            // the separator is chosen badly, and any separator can appear in a value.
            Assert.AreEqual(
                "<out><g k=\"a|bc\">2</g><g k=\"ab|c\">1</g><g k=\"abc\">1</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/doc/w\" group-by=\"tokenize(@v, ',')\" composite=\"yes\">"
                        + "<g k=\"{string-join(current-grouping-key(), '|')}\">"
                        + "<xsl:value-of select=\"count(current-group())\"/>"
                        + "</g></xsl:for-each-group>"),
                    "<doc><w v='a,bc'/><w v='ab,c'/><w v='a,bc'/><w v='abc'/></doc>"));
        }

        [TestMethod]
        public void GroupAdjacentTakesACompositeKeyOfAnyLength()
        {
            // Without composite this is XTTE1100: two values decide nothing about whether one item continues
            // the run before it. With it there is one answer however many values went into it — and a run
            // still ends when the pair changes, so NO 700 starts afresh at the end.
            Assert.AreEqual(
                "<out><g>NO/700</g><g>NO/280</g><g>SE/280</g><g>NO/700</g></out>",
                Run(
                    Root("<xsl:for-each-group select=\"/doc/c\" group-adjacent=\"@country, @pop\""
                        + " composite=\"yes\">"
                        + "<g><xsl:value-of select=\"string-join(current-grouping-key(), '/')\"/></g>"
                        + "</xsl:for-each-group>"),
                    Cities));
        }

        [TestMethod]
        public void CompositeIsRefusedOnAGroupingThatHasNoKey()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:for-each-group select=\"/doc/c\" group-starting-with=\"c[@country='SE']\""
                        + " composite=\"yes\"><g/></xsl:for-each-group>"),
                    Cities));

            Assert.AreEqual("XTSE1090", error.Code);
            StringAssert.Contains(error.Message, "a pattern gives no values");
        }

        [TestMethod]
        public void CompositeIsRefusedInATwoPointZeroStylesheet()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:for-each-group select=\"/doc/c\" group-by=\"@country\" composite=\"yes\">"
                        + "<g/></xsl:for-each-group>"),
                    Cities,
                    "2.0"));

            Assert.AreEqual("XTSE0090", error.Code);
            StringAssert.Contains(error.Message, "'composite' attribute");
        }

        // ---- composite, on xsl:key -----------------------------------------------------------------------

        [TestMethod]
        public void ACompositeKeyIsLookedUpByTheWholeSequence()
        {
            Assert.AreEqual(
                "<out>Oslo Trondheim</out>",
                Run(
                    "<xsl:key name=\"pair\" match=\"c\" use=\"@country, @pop\" composite=\"yes\"/>"
                    + Root("<xsl:value-of select=\"key('pair', ('NO', '700'))\"/>"),
                    Cities));
        }

        [TestMethod]
        public void HalfOfACompositeKeyFindsNothing()
        {
            // Which is the point of it: the node was filed once, under the pair, so a lookup of one value is
            // a lookup of something that was never filed.
            Assert.AreEqual(
                "<out>0</out>",
                Run(
                    "<xsl:key name=\"pair\" match=\"c\" use=\"@country, @pop\" composite=\"yes\"/>"
                    + Root("<xsl:value-of select=\"count(key('pair', 'NO'))\"/>"),
                    Cities));
        }

        [TestMethod]
        public void AKeyWithSeveralValuesAndNoCompositeFilesTheNodeUnderEachOfThem()
        {
            // The same declaration without the attribute. Each city is found by its country and by its
            // population, which is what a key on a sequence has always meant.
            Assert.AreEqual(
                "<out>3|2</out>",
                Run(
                    "<xsl:key name=\"loose\" match=\"c\" use=\"@country, @pop\"/>"
                    + Root("<xsl:value-of select=\"count(key('loose', 'NO'))\"/>|"
                        + "<xsl:value-of select=\"count(key('loose', '700'))\"/>"),
                    Cities));
        }

        [TestMethod]
        public void CompositeIsRefusedOnAKeyInATwoPointZeroStylesheet()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:key name=\"pair\" match=\"c\" use=\"@country\" composite=\"yes\"/>"
                    + Root(string.Empty),
                    Cities,
                    "2.0"));

            Assert.AreEqual("XTSE0090", error.Code);
            StringAssert.Contains(error.Message, "'composite' attribute");
        }

        // ---- select, on xsl:copy -------------------------------------------------------------------------

        /// <summary>An element with an attribute and a child, so a shallow copy is visibly shallow.</summary>
        private const string Nested = "<doc><a k='1'><deep/></a><b>B</b></doc>";

        [TestMethod]
        public void CopySelectNamesWhatIsCopiedInsteadOfTheContextItem()
        {
            // Shallow still: the child does not come, only the element and what the body puts in it.
            Assert.AreEqual(
                "<out><a>filled</a></out>",
                Run(Root("<xsl:copy select=\"/doc/a\">filled</xsl:copy>"), Nested));
        }

        [TestMethod]
        public void CopySelectGivesTheBodyASingletonFocus()
        {
            // Not just the context item: position() and last() are 1 as well, so the body reads the same
            // whether the selected node was one of many or the only one.
            Assert.AreEqual(
                "<out><b>b/1/1</b></out>",
                Run(
                    Root("<xsl:copy select=\"/doc/b\">"
                        + "<xsl:value-of select=\"concat(name(), '/', position(), '/', last())\"/>"
                        + "</xsl:copy>"),
                    Nested));
        }

        [TestMethod]
        public void CopySelectWorksWhereThereIsNoFocusAtAll()
        {
            // Which is the reason the attribute exists. A stylesheet function has no context item, so
            // xsl:copy could not be written in one before 3.0.
            Assert.AreEqual(
                "<out xmlns:f=\"urn:f\"><b p=\"1\"/></out>",
                Run(
                    "<xsl:function name=\"f:shallow\" xmlns:f=\"urn:f\">"
                    + "<xsl:param name=\"n\" as=\"node()\"/>"
                    + "<xsl:copy select=\"$n\"><xsl:attribute name=\"p\" select=\"position()\"/></xsl:copy>"
                    + "</xsl:function>"
                    + "<xsl:template match=\"/\" xmlns:f=\"urn:f\">"
                    + "<out><xsl:sequence select=\"f:shallow(/doc/b)\"/></out></xsl:template>",
                    Nested));
        }

        [TestMethod]
        public void CopySelectLeavesTheSurroundingFocusAlone()
        {
            // The focus it sets belongs to the body. An instruction after it reads what it read before.
            Assert.AreEqual(
                "<out><a/>doc</out>",
                Run(
                    Root("<xsl:copy select=\"/doc/a\"/><xsl:value-of select=\"name(/*)\"/>"),
                    Nested));
        }

        [TestMethod]
        public void CopyingAnItemThatIsNotANodeGivesTheItemBack()
        {
            // XSLT 2.0 calls this XTTE0945. From 3.0 there is nothing to shallow-copy about an atomic value,
            // so the copy is the value, and the body is not run — there is no element for it to fill.
            Assert.AreEqual(
                "<out>17</out>",
                Run(Root("<xsl:copy select=\"17\">ignored</xsl:copy>"), Nested));
        }

        [TestMethod]
        public void CopySelectLeavesNoCurrentTemplateRule()
        {
            // Same rule xsl:for-each follows: the focus is one the rules did not choose, so the rule that was
            // running is not the current one inside. Getting this wrong is not a wrong answer but a hang —
            // an xsl:next-match under select=".." matches the parent, and a rule matching the parent too
            // never stops.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:apply-templates select=\"/doc/a/deep\"/>")
                    + "<xsl:template match=\"deep\"><xsl:copy select=\"..\">"
                    + "<xsl:next-match/></xsl:copy></xsl:template>",
                    Nested));

            Assert.AreEqual("XTDE0560", error.Code);
        }

        [TestMethod]
        public void CopySelectWantsAtMostOneItem()
        {
            // More than one item is the instruction's own type error, XTTE3180; none copies nothing.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:copy select=\"/doc/*\"/>"), Nested));

            Assert.AreEqual("XTTE3180", error.Code);
            Assert.AreEqual("<out/>", Run(Root("<xsl:copy select=\"/doc/nonesuch\"/>"), Nested));
        }

        [TestMethod]
        public void CopySelectIsRefusedInATwoPointZeroStylesheet()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:copy select=\"/doc/a\"/>"), Nested, "2.0"));

            Assert.AreEqual("XTSE0090", error.Code);
            StringAssert.Contains(error.Message, "'select' attribute");
        }

        // ---- default-mode --------------------------------------------------------------------------------

        /// <summary>One rule per mode, so which mode was chosen is legible from one character of output.</summary>
        private const string Rules =
            "<xsl:template match=\"x\" default-mode=\"a\">A</xsl:template>"
            + "<xsl:template match=\"x\" default-mode=\"b\">B</xsl:template>"
            + "<xsl:template match=\"x\" mode=\"#unnamed\">U</xsl:template>"
            + "<xsl:template match=\"x\" mode=\"t:c\" xmlns:t=\"urn:t\">C</xsl:template>";

        /// <summary>
        /// The rule the transformation starts at, which declares no mode and so belongs to whichever mode is
        /// the default where it is written — the same one the run begins in.
        /// </summary>
        private static string Start(string body)
        {
            return $"<xsl:template match=\"/\"><out>{body}</out></xsl:template>";
        }

        [TestMethod]
        public void DefaultModeSuppliesTheModeOfATemplateAndOfAnApplyTemplates()
        {
            // Both at once, and that is the point: a stylesheet writing a group of rules in a named mode says
            // so once, instead of on every rule and every call. The two rules below differ only in their
            // default-mode, so they are rules in different modes.
            Assert.AreEqual(
                "<out>A</out>",
                Run(
                    "<xsl:template match=\"/\" mode=\"#unnamed\" default-mode=\"a\">"
                    + "<out><xsl:apply-templates select=\"doc/x\"/></out></xsl:template>"
                    + Rules,
                    "<doc><x/></doc>"));
        }

        [TestMethod]
        public void ADefaultModeOnTheStylesheetElementReachesEverything()
        {
            Assert.AreEqual(
                "<out>A</out>",
                Run(
                    Start("<xsl:apply-templates select=\"doc/x\"/>") + Rules,
                    "<doc><x/></doc>",
                    stylesheetAttributes: " default-mode=\"a\""));
        }

        [TestMethod]
        public void TheNearestDefaultModeInScopeWins()
        {
            // On an xsl:if here, which is a scope like any other — the attribute is a standard one in 3.0, so
            // it is allowed on every XSLT element rather than on a chosen few.
            Assert.AreEqual(
                "<out>A<i>B</i></out>",
                Run(
                    Start("<xsl:apply-templates select=\"doc/x\"/>"
                        + "<xsl:if test=\"true()\" default-mode=\"b\">"
                        + "<i><xsl:apply-templates select=\"doc/x\"/></i></xsl:if>")
                    + Rules,
                    "<doc><x/></doc>",
                    stylesheetAttributes: " default-mode=\"a\""));
        }

        [TestMethod]
        public void ALiteralResultElementCarriesItWithThePrefix()
        {
            // Written without the prefix it would be an ordinary attribute of the result, so the prefixed
            // form is the only way to address the processor from a literal element.
            Assert.AreEqual(
                "<out><l xmlns:t=\"urn:t\">C</l></out>",
                Run(
                    Start("<l xsl:default-mode=\"t:c\" xmlns:t=\"urn:t\">"
                        + "<xsl:apply-templates select=\"doc/x\"/></l>")
                    + Rules,
                    "<doc><x/></doc>",
                    stylesheetAttributes: " default-mode=\"a\""));
        }

        [TestMethod]
        public void AnExplicitModeStillWins()
        {
            Assert.AreEqual(
                "<out>B</out>",
                Run(
                    Start("<xsl:apply-templates select=\"doc/x\" mode=\"b\"/>") + Rules,
                    "<doc><x/></doc>",
                    stylesheetAttributes: " default-mode=\"a\""));
        }

        [TestMethod]
        public void UnnamedNamesTheModeWithNoName()
        {
            // Which 3.0 has to have: once #default can be a named mode, nothing else says "the one with no
            // name at all".
            Assert.AreEqual(
                "<out>U</out>",
                Run(
                    Start("<xsl:apply-templates select=\"doc/x\" mode=\"#unnamed\"/>") + Rules,
                    "<doc><x/></doc>",
                    stylesheetAttributes: " default-mode=\"a\""));
        }

        [TestMethod]
        public void ADefaultModeOnTheOutermostElementDecidesWhereTheRunBegins()
        {
            // Not only which mode a rule belongs to but which mode the transformation starts in, where the
            // caller names none. It has to be both: the rule for the root is in the default mode like every
            // other rule, so starting in the unnamed mode would reach nothing and let the built-in rules run
            // the whole document out as text.
            Assert.AreEqual(
                "<out/>",
                Run(
                    "<xsl:template match=\"/\"><out/></xsl:template>",
                    "<doc><x/></doc>",
                    stylesheetAttributes: " default-mode=\"a\""));
        }

        [TestMethod]
        public void AnInitialModeFromTheCallerStillWins()
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"3.0\" {Xsl} default-mode=\"a\">"
                + "<xsl:template match=\"/\"><a/></xsl:template>"
                + "<xsl:template match=\"/\" mode=\"b\"><b/></xsl:template>"
                + "</xsl:stylesheet>";

            Assert.AreEqual(
                "<b/>",
                new Xslt(
                    stylesheet,
                    new XsltOptions
                    {
                        OmitXmlDeclaration = true,
                        Version = XsltVersion.V30,
                        InitialMode = "b",
                    }).TransformXml("<doc/>"));
        }

        [TestMethod]
        public void DefaultModeIsRefusedInATwoPointZeroStylesheet()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\" default-mode=\"a\"><out/></xsl:template>",
                    "<doc/>",
                    "2.0"));

            Assert.AreEqual("XTSE0090", error.Code);
            StringAssert.Contains(error.Message, "'default-mode' attribute");
        }

        [TestMethod]
        public void TheLiteralElementFormIsRefusedInATwoPointZeroStylesheetToo()
        {
            // The hole this closes: a literal element's directives were checked against one list for every
            // version, so xsl:default-mode there would have been accepted and then quietly ignored.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\"><out xsl:default-mode=\"a\"/></xsl:template>",
                    "<doc/>",
                    "2.0"));

            Assert.AreEqual("XTSE0805", error.Code);
            StringAssert.Contains(error.Message, "'xsl:default-mode' is not a directive");
        }

        [TestMethod]
        public void ADefaultModeThatNamesNothingIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\" default-mode=\"#every\"><out/></xsl:template>",
                    "<doc/>"));

            Assert.AreEqual("XTSE0550", error.Code);
            StringAssert.Contains(error.Message, "neither a mode name nor '#unnamed'");
        }

        // ---- start-at, on xsl:number ---------------------------------------------------------------------

        [TestMethod]
        public void StartAtShiftsTheWholeNumbering()
        {
            // A shift, not a starting value: the numbers still count up by one from wherever they are told to
            // begin, so start-at="0" on a list of five gives 0 to 4.
            Assert.AreEqual(
                "<out>0.1.2.3.4</out>",
                Run(
                    Root("<xsl:for-each select=\"/doc/x\">"
                        + "<xsl:if test=\"position() gt 1\">.</xsl:if>"
                        + "<xsl:number value=\"position()\" start-at=\"0\"/>"
                        + "</xsl:for-each>"),
                    "<doc><x/><x/><x/><x/><x/></doc>"));
        }

        [TestMethod]
        public void StartAtTakesOneIntegerPerLevelAndRepeatsTheLast()
        {
            // Five values, three integers: the third serves for the fourth and fifth as well, which is what
            // makes start-at="0" mean "from zero at every level" rather than "at the first level only".
            Assert.AreEqual(
                "<out>10.21.32.33.34</out>",
                Run(
                    Root("<xsl:number value=\"1 to 5\" format=\"1.1.1.1.1\" start-at=\"10 20 30\"/>"),
                    "<doc/>"));
        }

        [TestMethod]
        public void StartAtMayBeNegativeAndIsAnAttributeValueTemplate()
        {
            Assert.AreEqual(
                "<out>-5</out>",
                Run(
                    "<xsl:param name=\"s\" select=\"-5\"/>"
                    + Root("<xsl:number value=\"1\" start-at=\"{$s}\"/>"),
                    "<doc/>"));
        }

        [TestMethod]
        public void StartAtIsRefusedInATwoPointZeroStylesheet()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:number value=\"1\" start-at=\"0\"/>"), "<doc/>", "2.0"));

            Assert.AreEqual("XTSE0090", error.Code);
            StringAssert.Contains(error.Message, "'start-at' attribute");
        }

        // ---- new-each-time and cache, on xsl:function -----------------------------------------------------

        /// <summary>A function building an element per call, so node identity is visible in a count.</summary>
        private static string Building(string attributes)
        {
            return $"<xsl:function name=\"x:f\" as=\"element()\"{attributes}>"
                + "<xsl:param name=\"n\" as=\"xs:integer\"/>"
                + "<e><xsl:value-of select=\"$n\"/></e>"
                + "</xsl:function>";
        }

        /// <summary>Ten calls with seven distinct arguments, counting the distinct nodes that come back.</summary>
        private const string TenCalls =
            "<xsl:template match=\"/\"><out>"
            + "<xsl:variable name=\"nodes\" as=\"element()*\">"
            + "<xsl:for-each select=\"(1,4,6,8,3,5,6,2,1,3)\">"
            + "<xsl:sequence select=\"x:f(.)\"/></xsl:for-each></xsl:variable>"
            + "<xsl:value-of select=\"count($nodes | ())\"/></out></xsl:template>";

        [TestMethod]
        public void NewEachTimeNoMakesTheSameArgumentsGiveTheSameNodes()
        {
            // The promise is about node identity, not about speed. Seven distinct arguments, so seven
            // distinct nodes however many times the function was called.
            Assert.AreEqual(
                "<out>7</out>",
                Run(Building(" new-each-time=\"no\"") + TenCalls, "<doc/>"));
        }

        [TestMethod]
        public void WithoutThePromiseEveryCallBuildsAfresh()
        {
            // The same function with nothing said, which defaults to "maybe" — read here as no promise, so
            // ten calls give ten nodes.
            Assert.AreEqual(
                "<out>10</out>",
                Run(Building(string.Empty) + TenCalls, "<doc/>"));
        }

        [TestMethod]
        public void MaybeIsNoPromiseEither()
        {
            Assert.AreEqual(
                "<out>10</out>",
                Run(Building(" new-each-time=\"maybe\"") + TenCalls, "<doc/>"));
        }

        [TestMethod]
        public void CacheMakesANaiveRecursionFinish()
        {
            // Written this way the call tree is exponential: fib(90) is not a slow answer without the cache,
            // it is no answer at all. Memoized it is immediate, and this test would hang if it were not.
            Assert.AreEqual(
                "<out>2880067194370816120</out>",
                Run(
                    "<xsl:function name=\"x:fib\" as=\"xs:integer\" cache=\"yes\">"
                    + "<xsl:param name=\"n\" as=\"xs:integer\"/>"
                    + "<xsl:sequence select=\"if ($n lt 2) then $n else x:fib($n - 1) + x:fib($n - 2)\"/>"
                    + "</xsl:function>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"x:fib(90)\"/></out></xsl:template>",
                    "<doc/>"));
        }

        [TestMethod]
        public void ACallInTheReturnOfALetIsMadeInTheFunctionsPlace()
        {
            // A let hands its return clause's value on as it is, so a call there is the whole of the
            // function's answer and is made once the body has returned rather than beneath it. A hundred
            // thousand iterations is fifty times the depth limit, so this passes only by not nesting; a
            // cached function does the same, the cache being asked before each call is made.
            Assert.AreEqual(
                "<out>5000050000,5000050000</out>",
                Run(
                    "<xsl:function name=\"x:sum\" as=\"xs:integer\">"
                    + "<xsl:param name=\"n\" as=\"xs:integer\"/><xsl:param name=\"total\" as=\"xs:integer\"/>"
                    + "<xsl:sequence select=\"if ($n eq 0) then $total else (let $m := $n - 1 return x:sum($m, $total + $n))\"/>"
                    + "</xsl:function>"
                    + "<xsl:function name=\"x:cached\" as=\"xs:integer\" cache=\"yes\">"
                    + "<xsl:param name=\"n\" as=\"xs:integer\"/><xsl:param name=\"total\" as=\"xs:integer\"/>"
                    + "<xsl:sequence select=\"if ($n eq 0) then $total else x:cached($n - 1, $total + $n)\"/>"
                    + "</xsl:function>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"x:sum(100000, 0)\"/>,<xsl:value-of select=\"x:cached(100000, 0)\"/>"
                    + "</out></xsl:template>",
                    "<doc/>"));
        }

        [TestMethod]
        public void ANodeArgumentIsRememberedByIdentityRatherThanByValue()
        {
            // Two elements that read alike are two different calls, because a function that navigates its
            // argument can tell them apart.
            Assert.AreEqual(
                "<out>a1,a2</out>",
                Run(
                    "<xsl:function name=\"x:at\" as=\"xs:string\" new-each-time=\"no\">"
                    + "<xsl:param name=\"e\" as=\"element()\"/>"
                    + "<xsl:sequence select=\"concat(name($e), count($e/preceding-sibling::*) + 1)\"/>"
                    + "</xsl:function>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"string-join(/doc/a!x:at(.), ',')\"/></out></xsl:template>",
                    "<doc><a/><a/></doc>"));
        }

        [TestMethod]
        public void OverrideAndItsThirtyPointZeroSpellingMaySayTheSameThingAndNotTwoThings()
        {
            // The two are one attribute under two names, 3.0 having renamed it. Writing both is how one
            // stylesheet serves a 2.0 processor and a 3.0 one, so it is allowed where they agree.
            Assert.AreEqual(
                "<out>1</out>",
                Run(
                    "<xsl:function name=\"x:f\" override=\"yes\" override-extension-function=\"yes\""
                    + "><xsl:sequence select=\"1\"/></xsl:function>"
                    + Root("<xsl:value-of select=\"x:f()\"/>"),
                    "<doc/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:function name=\"x:f\" override=\"yes\" override-extension-function=\"no\""
                    + "><xsl:sequence select=\"1\"/></xsl:function>"
                    + Root(string.Empty),
                    "<doc/>"));

            Assert.AreEqual("XTSE0020", error.Code);
            StringAssert.Contains(error.Message, "opposite things");
        }

        [TestMethod]
        public void AFunctionParameterMaySayItIsRequiredFromThreePointZero()
        {
            // Allowed as a restatement of what is true of every one of them, and only as that: required="no"
            // says something a function's parameter cannot be.
            Assert.AreEqual(
                "<out>2</out>",
                Run(
                    "<xsl:function name=\"x:f\"><xsl:param name=\"n\" required=\"yes\"/>"
                    + "<xsl:sequence select=\"$n\"/></xsl:function>"
                    + Root("<xsl:value-of select=\"x:f(2)\"/>"),
                    "<doc/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:function name=\"x:f\"><xsl:param name=\"n\" required=\"no\"/>"
                    + "<xsl:sequence select=\"$n\"/></xsl:function>"
                    + Root(string.Empty),
                    "<doc/>"));

            Assert.AreEqual("XTSE0020", error.Code);
        }

        [TestMethod]
        public void NewEachTimeWantsOneOfItsThreeValues()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Building(" new-each-time=\"sometimes\"") + Root(string.Empty),
                    "<doc/>"));

            // The element table catches it first and says it better; the reader's own check behind it is
            // what still answers under forwards-compatible processing, where the table is not consulted.
            StringAssert.Contains(error.Message, "'new-each-time' may take");
            StringAssert.Contains(error.Message, "maybe");
        }

        // ---- A processor's vocabulary is its own version's ------------------------------------------------

        [TestMethod]
        public void AThirtyPointZeroAttributeIsReadInATwoPointZeroStylesheetByAThirtyPointZeroProcessor()
        {
            // The distinction the whole cluster turns on. version="2.0" asks for backwards-compatible
            // behaviour, not for a smaller set of attributes — so a 3.0 processor reads start-at here, and
            // only a processor that does not claim 3.0 refuses it.
            string stylesheet =
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:template match=\"/\"><out><xsl:number value=\"1\" start-at=\"7\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            Assert.AreEqual(
                "<out>7</out>",
                new Xslt(
                    stylesheet,
                    new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                    .TransformXml("<doc/>"));
        }

        // ---- What the union takes ------------------------------------------------------------------------

        [TestMethod]
        public void AUnionTakesASequenceOfNodesAndNotOnlyANodeSet()
        {
            // A variable holding elements is a sequence, and '$nodes | ()' is the ordinary way of asking how
            // many distinct nodes are in one. Refusing it for the difference between the two shapes refused
            // the question.
            Assert.AreEqual(
                "<out>2</out>",
                Run(
                    Root("<xsl:variable name=\"n\" as=\"element()*\" select=\"/doc/a\"/>"
                        + "<xsl:value-of select=\"count($n | $n)\"/>"),
                    "<doc><a/><a/></doc>"));
        }

        // ---- html-version, on xsl:output -----------------------------------------------------------------

        /// <summary>Serializes with the given <c>xsl:output</c>, the XML declaration left on.</summary>
        private static string Serialize(string output, string body, string version = "3.0")
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"{version}\" {Xsl}>"
                + output
                + $"<xsl:template match=\"/\">{body}</xsl:template>"
                + "</xsl:stylesheet>";

            return new Xslt(
                stylesheet,
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Version = version == "3.0" ? XsltVersion.V30 : XsltVersion.V20,
                }).TransformXml("<doc/>");
        }

        /// <summary>A page whose root is <c>html</c>, in the namespace the XHTML method wants.</summary>
        private const string Page =
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title/></head>"
            + "<body><br/><embed/><p/></body></html>";

        [TestMethod]
        public void HtmlVersionFiveWritesTheBareDoctype()
        {
            // HTML 5 has no DTD to name, so the declaration names none.
            StringAssert.StartsWith(
                Serialize("<xsl:output method=\"xhtml\" html-version=\"5.0\" include-content-type=\"no\"/>", Page),
                "<!DOCTYPE html>");
        }

        [TestMethod]
        public void FiveAndFivePointZeroAndFivePointZeroZeroAreTheSameVersion()
        {
            foreach (string written in new[] { "5", "5.0", "5.00" })
            {
                StringAssert.StartsWith(
                    Serialize(
                        $"<xsl:output method=\"xhtml\" html-version=\"{written}\" include-content-type=\"no\"/>",
                        Page),
                    "<!DOCTYPE html>",
                    $"html-version=\"{written}\"");
            }
        }

        [TestMethod]
        public void TheDoctypeNamesTheDocumentElementAsItWasWritten()
        {
            // <HTML> is still an HTML 5 document, and the declaration is about that element, so it takes the
            // spelling the element actually has.
            StringAssert.StartsWith(
                Serialize(
                    "<xsl:output method=\"xhtml\" html-version=\"5\" include-content-type=\"no\"/>",
                    "<HTML xmlns=\"http://www.w3.org/1999/xhtml\"><body/></HTML>"),
                "<!DOCTYPE HTML>");
        }

        [TestMethod]
        public void ADocumentRootedAtSomethingElseGetsNoDoctype()
        {
            // Declaring it HTML 5 would misdescribe what follows.
            StringAssert.StartsWith(
                Serialize(
                    "<xsl:output method=\"xhtml\" html-version=\"5\" include-content-type=\"no\"/>",
                    "<thing xmlns=\"http://www.w3.org/1999/xhtml\"/>"),
                "<thing");
        }

        [TestMethod]
        public void AnHtmlInSomeOtherNamespaceGetsNoDoctypeEither()
        {
            StringAssert.StartsWith(
                Serialize(
                    "<xsl:output method=\"xhtml\" html-version=\"5\" include-content-type=\"no\"/>",
                    "<html xmlns=\"http://example.com/not-xhtml\"/>"),
                "<html");
        }

        [TestMethod]
        public void AnExplicitDoctypeSystemStillWins()
        {
            // A stylesheet naming a DTD means that DTD.
            StringAssert.StartsWith(
                Serialize(
                    "<xsl:output method=\"xhtml\" html-version=\"5\" include-content-type=\"no\""
                    + " doctype-system=\"about:legacy-compat\"/>",
                    Page),
                "<!DOCTYPE html SYSTEM \"about:legacy-compat\">");
        }

        [TestMethod]
        public void ADoctypePublicOnItsOwnDoesNotStopTheBareForm()
        {
            // There is nothing to point at without a system identifier, so the bare declaration is still the
            // honest answer.
            StringAssert.StartsWith(
                Serialize(
                    "<xsl:output method=\"xhtml\" html-version=\"5\" include-content-type=\"no\""
                    + " doctype-public=\"-//W3C//DTD XHTML 1.0 Strict//EN\"/>",
                    Page),
                "<!DOCTYPE html>");
        }

        [TestMethod]
        public void HtmlFiveUsesItsOwnListOfEmptyElements()
        {
            // embed joined the void elements in HTML 5 and is not in HTML 4's list, so this is the difference
            // the version makes. title and p are not void, so they get both tags rather than collapsing.
            string page = Serialize(
                "<xsl:output method=\"xhtml\" html-version=\"5\" include-content-type=\"no\"/>", Page);

            StringAssert.Contains(page, "<br />");
            StringAssert.Contains(page, "<embed />");
            StringAssert.Contains(page, "<title></title>");
            StringAssert.Contains(page, "<p></p>");
        }

        [TestMethod]
        public void WithoutHtmlVersionTheHtmlFourRulesStillApply()
        {
            string page = Serialize("<xsl:output method=\"xhtml\" include-content-type=\"no\"/>", Page);

            StringAssert.Contains(page, "<br />");
            StringAssert.Contains(page, "<embed></embed>");
            Assert.DoesNotContain("<!DOCTYPE", page, "no doctype without one being asked for");
        }

        [TestMethod]
        public void TheNameAloneDecidesUnderHtmlFive()
        {
            // The suite asks the same questions of the XHTML namespace and of no namespace and wants the same
            // answers, where plain XML would collapse the lot.
            string page = Serialize(
                "<xsl:output method=\"xhtml\" html-version=\"5\" include-content-type=\"no\"/>",
                "<html><head><title/></head><body><br/></body></html>");

            StringAssert.Contains(page, "<title></title>");
            StringAssert.Contains(page, "<br />");
        }

        [TestMethod]
        public void HtmlVersionIsRefusedInATwoPointZeroStylesheet()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Serialize("<xsl:output method=\"xhtml\" html-version=\"5\"/>", Page, "2.0"));

            Assert.AreEqual("XTSE0090", error.Code);
            StringAssert.Contains(error.Message, "'html-version' attribute");
        }

        [TestMethod]
        public void AnHtmlVersionThatIsNotANumberIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Serialize("<xsl:output method=\"xhtml\" html-version=\"five\"/>", Page));

            Assert.AreEqual("XTSE0020", error.Code);
            StringAssert.Contains(error.Message, "which is not a number");
        }

        // ---- suppress-indentation and item-separator ------------------------------------------------------

        [TestMethod]
        public void SuppressIndentationLeavesAnElementsContentAlone()
        {
            // Indenting p would put whitespace between the text and the emphasis, which a reader sees.
            Assert.AreEqual(
                "<out>\n  <p><b>bold</b><i>italic</i></p>\n</out>",
                Serialize(
                    "<xsl:output method=\"xml\" indent=\"yes\" suppress-indentation=\"p\"/>",
                    "<out><p><b>bold</b><i>italic</i></p></out>"));
        }

        [TestMethod]
        public void WithoutSuppressionTheSameContentIsIndented()
        {
            Assert.AreEqual(
                "<out>\n  <p>\n    <b>bold</b>\n    <i>italic</i>\n  </p>\n</out>",
                Serialize(
                    "<xsl:output method=\"xml\" indent=\"yes\"/>",
                    "<out><p><b>bold</b><i>italic</i></p></out>"));
        }

        [TestMethod]
        public void SuppressionReachesEverythingBelowTheNamedElement()
        {
            Assert.AreEqual(
                "<out>\n  <p><span><b>x</b></span></p>\n</out>",
                Serialize(
                    "<xsl:output method=\"xml\" indent=\"yes\" suppress-indentation=\"p\"/>",
                    "<out><p><span><b>x</b></span></p></out>"));
        }

        [TestMethod]
        public void ItemSeparatorSaysWhatGoesBetweenAdjacentValues()
        {
            Assert.AreEqual(
                "<out>1|2|3</out>",
                Serialize(
                    "<xsl:output item-separator=\"|\"/>",
                    "<out><xsl:sequence select=\"1 to 3\"/></out>"));
        }

        [TestMethod]
        public void WithoutItemSeparatorItIsASpace()
        {
            Assert.AreEqual(
                "<out>1 2 3</out>",
                Serialize(string.Empty, "<out><xsl:sequence select=\"1 to 3\"/></out>"));
        }

        // ---- Codes that a later specification renamed -----------------------------------------------------

        /// <summary>Runs a stylesheet against a named processor version, returning the error code raised.</summary>
        private static string CodeFrom(string body, XsltVersion processor, string version = "2.0")
        {
            string stylesheet = $"<xsl:stylesheet version=\"{version}\" {Xsl} {Xs}>{body}</xsl:stylesheet>";

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true, Version = processor })
                    .TransformXml("<doc/>"));

            return error.Code ?? string.Empty;
        }

        [TestMethod]
        public void AnUnreadablePictureIsNamedByTheLibraryThatDefinesFormatNumber()
        {
            // XSLT 2.0 has a format-number of its own; XPath 3.0 moved the function into the core library,
            // where the same failure has an F&O code. Which one a caller hears is a question about which
            // specification defines the function, so it follows the processor — the suite pairs these tests
            // over one version="2.0" stylesheet and wants a code apiece.
            const string Body =
                "<xsl:template match=\"/\"><xsl:value-of select=\"format-number(1, '#,')\"/></xsl:template>";

            Assert.AreEqual("XTDE1310", CodeFrom(Body, XsltVersion.V20));
            Assert.AreEqual("FODF1310", CodeFrom(Body, XsltVersion.V30));
        }

        [TestMethod]
        public void AnUndeclaredDecimalFormatIsNamedTheSameWay()
        {
            const string Body =
                "<xsl:template match=\"/\"><xsl:value-of select=\"format-number(1, '#', 'nope')\"/>"
                + "</xsl:template>";

            Assert.AreEqual("XTDE1280", CodeFrom(Body, XsltVersion.V20));
            Assert.AreEqual("FODF1280", CodeFrom(Body, XsltVersion.V30));
        }

        [TestMethod]
        public void AParameterWhoseTypeExcludesTheEmptySequenceIsNamedTheSameWayToo()
        {
            // XSLT 3.0 dropped XTDE0610 in favour of XTDE0700, the two being one complaint reached two ways:
            // a parameter with no default whose declared type does not admit the empty sequence is required
            // after all, so a caller that left it out is what is wrong.
            const string Body =
                "<xsl:template match=\"/\"><xsl:apply-templates select=\"doc\"/></xsl:template>"
                + "<xsl:template match=\"doc\"><xsl:param name=\"p\" as=\"xs:integer\"/>"
                + "<xsl:value-of select=\"$p\"/></xsl:template>";

            Assert.AreEqual("XTDE0610", CodeFrom(Body, XsltVersion.V20));
            Assert.AreEqual("XTDE0700", CodeFrom(Body, XsltVersion.V30));
        }

        // ---- What a processor that cannot validate says ---------------------------------------------------

        [TestMethod]
        public void AskingForValidationThisEngineCannotDoIsItsOwnError()
        {
            // XTSE1660 rather than a complaint about the value: the spelling is fine and the processor
            // simply cannot do what was asked, which is a different thing to be told.
            Assert.AreEqual(
                "XTSE1660",
                CodeFrom("<xsl:template match=\"/\"><xsl:element name=\"a\" validation=\"strict\"/>"
                    + "</xsl:template>", XsltVersion.V30, "3.0"));

            Assert.AreEqual(
                "XTSE1660",
                CodeFrom("<xsl:template match=\"/\"><xsl:element name=\"a\" type=\"xs:untyped\"/>"
                    + "</xsl:template>", XsltVersion.V30, "3.0"));
        }

        [TestMethod]
        public void ItIsReportedBeforeAnythingElseWrongWithTheSameElement()
        {
            // xsl:element without a name is also a static error, and the specification says a non-schema-aware
            // processor must signal this one — so the stylesheet hears what it cannot have rather than being
            // sent to fix the other thing first.
            Assert.AreEqual(
                "XTSE1660",
                CodeFrom("<xsl:template match=\"/\"><xsl:element type=\"xs:untyped\"/></xsl:template>",
                    XsltVersion.V30, "3.0"));
        }

        [TestMethod]
        public void ADefaultValidationAskingForSchemaAwarenessSaysTheSame()
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"3.0\" {Xsl} default-validation=\"strict\">"
                + "<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>";

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(
                    stylesheet,
                    new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                    .TransformXml("<doc/>"));

            Assert.AreEqual("XTSE1660", error.Code);
        }

        [TestMethod]
        public void WhichValidationIsRefusedFollowsTheProcessorsVersion()
        {
            // XTSE1660 names the set, and the two languages name different sets. A 2.0 basic processor
            // refuses every value but strip; a 3.0 non-schema-aware processor refuses only strict, preserve
            // and lax having been let through once it was clear that they ask for nothing a processor
            // without a schema cannot give — with no type annotations anywhere, preserving them and
            // stripping them come to the same thing, and validating laxly against no declaration at all
            // validates nothing.
            foreach (string mode in new[] { "strict", "lax", "preserve" })
            {
                Assert.AreEqual("XTSE1660", Validating(mode, XsltVersion.V20), mode);
            }

            Assert.IsNull(Validating("strip", XsltVersion.V20));

            Assert.AreEqual("XTSE1660", Validating("strict", XsltVersion.V30));
            Assert.IsNull(Validating("lax", XsltVersion.V30));
            Assert.IsNull(Validating("preserve", XsltVersion.V30));
            Assert.IsNull(Validating("strip", XsltVersion.V30));

            // A value outside the grammar is a different complaint — the spelling is wrong rather than the
            // processor unable — and that is so whichever version is running.
            Assert.AreEqual("XTSE0020", Validating("maybe", XsltVersion.V20));
            Assert.AreEqual("XTSE0020", Validating("maybe", XsltVersion.V30));
        }

        [TestMethod]
        public void ADefaultValidationOfPreserveIsATwoPointZeroErrorOnly()
        {
            // The same line, drawn on the attribute that says what everything else defaults to. Its own
            // grammar is narrower — preserve or strip — so strict is both outside it and a request for
            // schema awareness, and the specification names the second of those.
            Assert.AreEqual("XTSE1660", DefaultValidating("preserve", XsltVersion.V20));
            Assert.IsNull(DefaultValidating("preserve", XsltVersion.V30));

            Assert.IsNull(DefaultValidating("strip", XsltVersion.V20));
            Assert.IsNull(DefaultValidating("strip", XsltVersion.V30));

            Assert.AreEqual("XTSE1660", DefaultValidating("strict", XsltVersion.V20));
            Assert.AreEqual("XTSE1660", DefaultValidating("strict", XsltVersion.V30));
        }

        /// <summary>
        /// Runs a stylesheet whose xsl:element asks for a validation mode, and reports the code it was
        /// refused with or null where it ran.
        /// </summary>
        /// <param name="mode">The value of the validation attribute.</param>
        /// <param name="processor">The version this engine says it implements.</param>
        private static string? Validating(string mode, XsltVersion processor)
        {
            return Attempted(
                $"<xsl:stylesheet version=\"2.0\" {Xsl} {Xs}>"
                + $"<xsl:template match=\"/\"><out><xsl:element name=\"e\" validation=\"{mode}\">"
                + "x</xsl:element></out></xsl:template></xsl:stylesheet>",
                processor);
        }

        /// <summary>The same for a default-validation on the outermost element.</summary>
        /// <param name="mode">The value of the default-validation attribute.</param>
        /// <param name="processor">The version this engine says it implements.</param>
        private static string? DefaultValidating(string mode, XsltVersion processor)
        {
            return Attempted(
                $"<xsl:stylesheet version=\"2.0\" {Xsl} default-validation=\"{mode}\">"
                + $"<xsl:template match=\"/\"><out/></xsl:template></xsl:stylesheet>",
                processor);
        }

        /// <summary>The code a stylesheet was refused with, or null where it ran.</summary>
        /// <param name="stylesheet">The stylesheet text.</param>
        /// <param name="processor">The version this engine says it implements.</param>
        private static string? Attempted(string stylesheet, XsltVersion processor)
        {
            try
            {
                new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true, Version = processor })
                    .TransformXml("<doc/>");
                return null;
            }
            catch (XsltException failed)
            {
                return failed.Code;
            }
        }

        // ---- Prefix normalization ------------------------------------------------------------------------

        private const string Html5Output =
            "<xsl:output method=\"xhtml\" html-version=\"5\" include-content-type=\"no\"/>";

        /// <summary>The three namespaces, each bound to a prefix, so normalization has something to strip.</summary>
        private const string Prefixed =
            "<h:html xmlns:h=\"http://www.w3.org/1999/xhtml\">"
            + "<h:body>"
            + "<s:svg xmlns:s=\"http://www.w3.org/2000/svg\"><s:circle/></s:svg>"
            + "<m:math xmlns:m=\"http://www.w3.org/1998/Math/MathML\"><m:mi>a</m:mi></m:math>"
            + "</h:body></h:html>";

        [TestMethod]
        public void HtmlFiveWritesItsThreeNamespacesWithoutAPrefix()
        {
            // An HTML 5 parser recognises svg and math by name and has no general namespace mechanism, so
            // <s:svg> is not SVG to it but an unknown element called "s:svg".
            string page = Serialize(Html5Output, Prefixed);

            StringAssert.Contains(page, "<html xmlns=\"http://www.w3.org/1999/xhtml\">");
            StringAssert.Contains(page, "<svg xmlns=\"http://www.w3.org/2000/svg\">");
            StringAssert.Contains(page, "<math xmlns=\"http://www.w3.org/1998/Math/MathML\">");
        }

        [TestMethod]
        public void ThePrefixDeclarationsThemselvesAreDroppedNotJustUnused()
        {
            // The specification says to remove the namespace nodes, not merely to stop using them. A
            // declaration nothing refers to would still be a prefix in the output the suite asserts against.
            string page = Serialize(Html5Output, Prefixed);

            Assert.DoesNotContain("xmlns:h=", page);
            Assert.DoesNotContain("xmlns:s=", page);
            Assert.DoesNotContain("xmlns:m=", page);
            Assert.DoesNotContain("h:", page);
        }

        [TestMethod]
        public void TheDoctypeNamesTheElementAfterItsPrefixIsTaken()
        {
            // Written before normalization it would say h:html, naming something the document does not
            // contain.
            StringAssert.StartsWith(Serialize(Html5Output, Prefixed), "<!DOCTYPE html>");
        }

        [TestMethod]
        public void AnInheritedDefaultNamespaceDoesNotRebindWhatWasJustNormalized()
        {
            // The svg element takes the default prefix for itself while an xmlns binding XHTML is in scope
            // around it. Copying that binding on would write xmlns twice on one element and make the second
            // one decide what svg means.
            string page = Serialize(
                Html5Output,
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body>"
                + "<s:svg xmlns:s=\"http://www.w3.org/2000/svg\"><s:circle/></s:svg>"
                + "</body></html>");

            StringAssert.Contains(page, "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle></circle></svg>");
        }

        [TestMethod]
        public void AnAttributeInOneOfThoseNamespacesKeepsItsPrefix()
        {
            // An attribute has no default namespace to be in — an unprefixed attribute is in no namespace at
            // all — so the prefix stays, and the declaration is written on the element carrying it rather
            // than inherited from an ancestor this rule stripped it from.
            string page = Serialize(
                Html5Output,
                "<h:html xmlns:h=\"http://www.w3.org/1999/xhtml\" xmlns:svg=\"http://www.w3.org/2000/svg\">"
                + "<h:body><h:p svg:att=\"1\"/></h:body></h:html>");

            StringAssert.Contains(page, "xmlns:svg=\"http://www.w3.org/2000/svg\"");
            StringAssert.Contains(page, "svg:att=\"1\"");
        }

        [TestMethod]
        public void AForeignNamespaceIsLeftAlone()
        {
            // Only the three HTML 5 knows about are normalized. Anything else keeps its prefix, there being
            // no unprefixed spelling of it that would mean the same thing.
            string page = Serialize(
                Html5Output,
                "<h:html xmlns:h=\"http://www.w3.org/1999/xhtml\" xmlns:n=\"urn:n\">"
                + "<h:body><n:thing/></h:body></h:html>");

            StringAssert.Contains(page, "xmlns:n=\"urn:n\"");
            StringAssert.Contains(page, "<n:thing");
        }

        [TestMethod]
        public void WithoutHtmlVersionThePrefixesStay()
        {
            // The rule belongs to HTML 5, so the XHTML method without it serializes as XML does.
            string page = Serialize(
                "<xsl:output method=\"xhtml\" include-content-type=\"no\"/>", Prefixed);

            StringAssert.Contains(page, "<h:html");
            StringAssert.Contains(page, "<s:svg");
        }
    }
}
