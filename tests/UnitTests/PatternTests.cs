namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the shapes of pattern XSLT 3.0 defines by their equivalent expression, and for the corners
    /// of the ordinary ones: what a positional predicate counts within, which nodes a step's axis can reach,
    /// what an error inside a pattern means.
    /// </summary>
    [TestClass]
    public sealed class PatternTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" exclude-result-prefixes=\"xs f\">";

        private static string Run(string body, string input = "<r/>", string version = "3.0")
        {
            XsltOptions options = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 };
            string head = Head.Replace("version=\"3.0\"", $"version=\"{version}\"");

            Xslt xslt = new Xslt(head + body + "</xsl:stylesheet>", options);
            return input.Length == 0 ? xslt.Transform() : xslt.TransformXml(input);
        }

        private static string Refuses(string body, string input = "<r/>", string version = "3.0")
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, input, version)).Code ?? string.Empty;
        }

        /// <summary>A stylesheet applying templates to the whole input, whose default rule copies nothing.</summary>
        private static string Applying(string rules)
        {
            return "<xsl:mode on-no-match=\"shallow-skip\"/><xsl:template match=\"/\"><out><xsl:apply-templates/></out></xsl:template>"
                + rules;
        }

        [TestMethod]
        public void ABareDotIsBelowEveryPatternThatNarrowsAnything()
        {
            // §6.5: a predicate pattern is 1 with predicates and -1 without, so '.' takes only what nothing
            // else does — a kind test being -0.5 and a name 0 — and '.[...]' outranks them all.
            Assert.AreEqual(
                "<out>att e text dot</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/@x, /r/e, /r/text(), 1\"/></out></xsl:template>"
                    + "<xsl:template match=\".\">dot</xsl:template>"
                    + "<xsl:template match=\"@*\">att </xsl:template>"
                    + "<xsl:template match=\"e\">e </xsl:template>"
                    + "<xsl:template match=\"text()\">text </xsl:template>",
                    "<r x=\"1\"><e/>t</r>"));
            Assert.AreEqual(
                "<out>pred</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/e\"/></out></xsl:template>"
                    + "<xsl:template match=\".[self::e]\">pred</xsl:template>"
                    + "<xsl:template match=\"e\">e</xsl:template>",
                    "<r><e/></r>"));
        }

        [TestMethod]
        public void APositionalPredicateCountsWithinWhatThePredicatesBeforeItLeft()
        {
            // The second foo among those with the attribute, not the second foo that happens to have it.
            Assert.AreEqual(
                "<out>c2</out>",
                Run(
                    Applying("<xsl:template match=\"foo[@a='c'][2]\"><xsl:value-of select=\"@id\"/></xsl:template>"),
                    "<r><foo a='c' id='c1'/><foo a='d' id='d1'/><foo a='c' id='c2'/><foo a='c' id='c3'/></r>"));

            // Two positional predicates in a row: the odd ones, and the fourth of those.
            Assert.AreEqual(
                "<out>g</out>",
                Run(
                    Applying("<xsl:template match=\"x[(position() mod 2) = 1][position() = 4]\"><xsl:value-of select=\"@id\"/></xsl:template>"),
                    "<r><x id='a'/><x id='b'/><x id='c'/><x id='d'/><x id='e'/><x id='f'/><x id='g'/><x id='h'/></r>"));

            // A sequence of one number is a number, and selects by position.
            Assert.AreEqual(
                "<out>a</out>",
                Run(
                    Applying("<xsl:template match=\"x[1 to 1]\"><xsl:value-of select=\"@id\"/></xsl:template>"),
                    "<r><x id='a'/><x id='b'/></r>"));
        }

        [TestMethod]
        public void AnErrorInsideAPatternMeansTheItemDoesNotMatch()
        {
            // Comparing an integer with a string is a type error, and here it is a non-match and nothing
            // more: the second rule is reached for the number and the first for the string.
            Assert.AreEqual(
                "<out>[xxxi]two</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"('xxxi', 2)\"/></out></xsl:template>"
                    + "<xsl:template match=\".[. = 'xxxi']\">[xxxi]</xsl:template>"
                    + "<xsl:template match=\".[number(.) eq 2]\">two</xsl:template>"));
        }

        [TestMethod]
        public void TheAxisWrittenDecidesWhatAStepCanReach()
        {
            const string Input = "<r a='1'><e/></r>";

            // A bare document-node() stands on the self axis; child::document-node() names an axis no
            // document node is on.
            Assert.AreEqual(
                "<out>doc</out>",
                Run("<xsl:template match=\"document-node()\"><out>doc</out></xsl:template>", Input));
            Assert.AreEqual(
                "<out>rule</out>",
                Run("<xsl:template match=\"child::document-node()\"><out>wrong</out></xsl:template>"
                    + "<xsl:template match=\"*\"><out>rule</out></xsl:template>", Input));

            // attribute() is on the attribute axis whether or not it says so, and a parentless attribute is
            // what attribute-or-top reaches; child::attribute() reaches no attribute at all.
            const string Copied =
                "<xsl:template match=\"/\"><xsl:variable name=\"att\" as=\"attribute()\"><xsl:copy-of select=\"r/attribute()\"/></xsl:variable>"
                + "<out><xsl:apply-templates select=\"$att\"/></out></xsl:template>";

            Assert.AreEqual(
                "<out><t>1</t></out>",
                Run(Copied + "<xsl:template match=\"attribute()\"><t><xsl:value-of select=\".\"/></t></xsl:template>", Input));
            Assert.AreEqual(
                "<out>1</out>",
                Run(Copied + "<xsl:template match=\"child::attribute()\"><t><xsl:value-of select=\".\"/></t></xsl:template>", Input));
        }

        [TestMethod]
        public void ARootedPatternNeedsADocumentNodeAboveIt()
        {
            // The element a variable's as declaration builds has no parent, so '//a' — which hangs from a
            // document node — does not match it; 'a' does, being child-or-top.
            const string Parentless =
                "<xsl:variable name=\"data\" as=\"element()\"><a/></xsl:variable>"
                + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"$data\"/></out></xsl:template>";

            Assert.AreEqual(
                "<out>top</out>",
                Run(Parentless + "<xsl:template match=\"//a\" priority=\"5\">rooted</xsl:template>"
                    + "<xsl:template match=\"a\">top</xsl:template>"));
            Assert.AreEqual(
                "<out>rooted</out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"a\"/></out></xsl:template>"
                    + "<xsl:template match=\"//a\">rooted</xsl:template>", "<a/>"));
        }

        [TestMethod]
        public void TheOuterParenthesesOfAPatternAreStripped()
        {
            // '(doc|cod)' is the rules 'doc' and 'cod', each with priority 0 — level with a plain 'doc', so
            // the later declaration wins, and the two are rivals where a mode says rivals fail.
            Assert.AreEqual(
                "<out>plain</out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates/></out></xsl:template>"
                    + "<xsl:template match=\"(doc|cod)\">bracketed</xsl:template>"
                    + "<xsl:template match=\"doc\">plain</xsl:template>", "<doc/>"));
            Assert.AreEqual(
                "XTDE0540",
                Refuses("<xsl:mode on-multiple-match=\"fail\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates/></out></xsl:template>"
                    + "<xsl:template match=\"doc\">plain</xsl:template>"
                    + "<xsl:template match=\"(doc|cod)\">bracketed</xsl:template>", "<doc/>"));
        }

        [TestMethod]
        public void ASelectionPatternIsEvaluatedFromTheCandidatesOwnRoot()
        {
            // root() of a parentless element is the element, so the pattern selects it.
            Assert.AreEqual(
                "<out>ok</out>",
                Run("<xsl:variable name=\"data\" as=\"element(A)\"><A><B/></A></xsl:variable>"
                    + "<xsl:template match=\"root()[self::A]\">ok</xsl:template>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"$data\"/></out></xsl:template>"));

            // A union on the right of a slash, an except between two axes, and a variable holding nodes and
            // other things besides: each is matched by what the expression selects.
            Assert.AreEqual(
                "<out><AB>23</AB><AB>25</AB></out>",
                Run(Applying("<xsl:template match=\"x/(a|b)\"><AB><xsl:value-of select=\".\"/></AB></xsl:template>"),
                    "<x><a>23</a><b>25</b></x>"));
            Assert.AreEqual(
                "<out><aa>23</aa><AA>25</AA></out>",
                Run("<xsl:variable name=\"nodes\" as=\"element()*\"><x><a>23</a><x><a>25</a></x></x></xsl:variable>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"$nodes\"/></out></xsl:template>"
                    + "<xsl:template match=\"descendant::a except child::a\" priority=\"20\"><AA><xsl:value-of select=\".\"/></AA></xsl:template>"
                    + "<xsl:template match=\"a\" priority=\"10\"><aa><xsl:value-of select=\".\"/></aa></xsl:template>"
                    + "<xsl:template match=\"x\"><xsl:apply-templates/></xsl:template>"));
            Assert.AreEqual(
                "<out>var</out>",
                Run("<xsl:variable name=\"e\" as=\"element()\"><e/></xsl:variable>"
                    + "<xsl:variable name=\"var\" select=\"($e, 1234)\"/>"
                    + "<xsl:template match=\"$var\" priority=\"100\">var</xsl:template>"
                    + "<xsl:template match=\"*\">element</xsl:template>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"$e\"/></out></xsl:template>"));
        }

        [TestMethod]
        public void ThePatternGrammarReadsWhatXPathReads()
        {
            // Q{uri}* is prefix:* with the namespace written out; a for expression in a predicate has a
            // wildcard after 'in' and not a multiplication.
            Assert.AreEqual(
                "<out>ns</out>",
                Run(Applying("<xsl:template match=\"Q{urn:f}*\">ns</xsl:template>"), "<r><f:x xmlns:f=\"urn:f\"/></r>"));
            Assert.AreEqual(
                "<out>e</out>",
                Run(Applying("<xsl:template match=\"e[for $t in * return $t/*]\">e</xsl:template>"
                        + "<xsl:template match=\"*\" priority=\"-1\"><xsl:apply-templates/></xsl:template>"),
                    "<r><e><g><h/></g></e></r>"));
        }

        [TestMethod]
        public void AnUnknownFunctionIsStaticFromXslt20()
        {
            const string Guarded =
                "<xsl:template match=\"/\"><out><xsl:if test=\"function-available('f:nonesuch')\">"
                + "<xsl:value-of select=\"f:nonesuch()\"/></xsl:if></out></xsl:template>";

            // Under 1.0 behaviour the guard means something and the call waits to be reached; from 2.0 the
            // call is refused when the stylesheet is read, use-when being the guard the specification gives.
            Assert.AreEqual("<out/>", Run(Guarded, "<r/>", "1.0"));
            Assert.AreEqual("XPST0017", Refuses(Guarded, "<r/>", "2.0"));
            Assert.AreEqual("XPST0017", Refuses(Guarded));
        }

        [TestMethod]
        public void AThreeProcessorAppliesTemplatesToItemsForATwoStylesheet()
        {
            // A version="2.0" stylesheet run by a 3.0 processor gets 3.0's rules: the vocabulary and the
            // behaviour alike, there being no 2.0 compatibility mode to fall back into.
            Assert.AreEqual(
                "<out>93</out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"93\"/></out></xsl:template>"
                    + "<xsl:template match=\".[. instance of xs:integer]\"><xsl:value-of select=\".\"/></xsl:template>",
                    "<r/>",
                    "2.0"));
        }

        [TestMethod]
        public void TheBuiltInRuleForAnItemFollowsTheMode()
        {
            const string Applying =
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"93\"/></out></xsl:template>";

            // Text-only-copy, the default, writes the value; a skip writes nothing; a copy keeps the item.
            Assert.AreEqual("<out>93</out>", Run(Applying));
            Assert.AreEqual("<out/>", Run("<xsl:mode on-no-match=\"deep-skip\"/>" + Applying));
            Assert.AreEqual("XTDE0555", Refuses("<xsl:mode on-no-match=\"fail\"/>" + Applying));
            Assert.AreEqual(
                "<out>93</out>",
                Run("<xsl:mode on-no-match=\"shallow-copy\"/><xsl:template match=\"/\"><out><xsl:variable name=\"v\" as=\"item()*\">"
                    + "<xsl:apply-templates select=\"93\"/></xsl:variable><xsl:value-of select=\"$v instance of xs:integer\"/></out></xsl:template>")
                    .Replace("true", "93"));
        }

        [TestMethod]
        public void IntersectAndExceptTakeSequencesOfNodes()
        {
            // Two typed variables filtered from one, which arrive as sequences rather than node-sets.
            Assert.AreEqual(
                "<out><n>21</n><n>42</n></out>",
                Run("<xsl:mode on-no-match=\"deep-skip\"/>"
                    + "<xsl:variable name=\"nodes\" as=\"element()*\"><xsl:for-each select=\"1 to 50\"><n><xsl:value-of select=\".\"/></n></xsl:for-each></xsl:variable>"
                    + "<xsl:variable name=\"threes\" select=\"$nodes[. mod 3 = 0]\"/><xsl:variable name=\"sevens\" select=\"$nodes[. mod 7 = 0]\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"$nodes\"/></out></xsl:template>"
                    + "<xsl:template match=\"$threes intersect $sevens\"><xsl:sequence select=\".\"/></xsl:template>"));
        }

    }
}
