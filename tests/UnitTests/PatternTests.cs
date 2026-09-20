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

        /// <summary>
        /// The ids of the nodes one pattern matches, on both backends, which have to agree.
        /// </summary>
        private static string Matched(string pattern, string input, string declarations = "")
        {
            string body = declarations + Applying(
                $"<xsl:template match=\"{pattern}\"><xsl:value-of select=\"@id\"/><xsl:text> </xsl:text></xsl:template>");

            string sheet = Head + body + "</xsl:stylesheet>";
            string interpreted = new Xslt(sheet, Options(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(sheet, Options(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, $"The backends disagree on match=\"{pattern}\".");
            return interpreted;

            static XsltOptions Options(XsltBackend backend) =>
                new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30, Backend = backend };
        }

        [TestMethod]
        public void APredicateThatReadsNoPositionIsAnsweredWithoutOne()
        {
            // None of these can select by position, so none has its siblings counted. What each matches
            // must be what it matched when they were.
            const string Input =
                "<r><foo id='1' a='c'><k/></foo><foo id='2' a='d'/><foo id='3' a='c' b='x'/><bar id='4' a='c'/>"
                + "<g><foo id='5' a='c'><bar id='6'/></foo><foo id='7'><k/><bar id='8' a='d'/></foo></g></r>";

            Assert.AreEqual("<out>1 3 5 </out>", Matched("foo[@a='c']", Input));
            Assert.AreEqual("<out>1 2 3 5 </out>", Matched("foo[@a]", Input));
            Assert.AreEqual("<out>1 7 </out>", Matched("foo[k]", Input));
            Assert.AreEqual("<out>7 </out>", Matched("foo[not(@a)]", Input));
            Assert.AreEqual("<out>3 </out>", Matched("foo[@a='c' and @b]", Input));
            Assert.AreEqual("<out>1 5 7 </out>", Matched("foo[k | bar]", Input));
            Assert.AreEqual("<out>1 3 4 5 </out>", Matched("*[@a='c']", Input));

            // Several of them on one step, and steps either side of the one that carries them.
            Assert.AreEqual("<out>3 </out>", Matched("foo[@a][@b]", Input));
            Assert.AreEqual("<out>1 2 3 </out>", Matched("r/foo[@a]", Input));
            Assert.AreEqual("<out>6 </out>", Matched("foo[@a]/bar", Input));
            Assert.AreEqual("<out>5 </out>", Matched("g//foo[@a]", Input));
            Assert.AreEqual("<out>5 </out>", Matched("g/descendant::foo[@a='c']", Input));
            Assert.AreEqual("<out>1 5 </out>", Matched("foo[@a='c'][*]", Input));
        }

        [TestMethod]
        public void APredicateThatCouldReadAPositionStillHasItCounted()
        {
            const string Input = "<r><x id='a' n='2'/><x id='b' n='2'/><y id='c'/><x id='d' n='3'/><x id='e' n='9'/></r>";

            Assert.AreEqual("<out>b </out>", Matched("x[2]", Input));
            Assert.AreEqual("<out>e </out>", Matched("x[last()]", Input));
            Assert.AreEqual("<out>a d </out>", Matched("x[position() mod 2 = 1]", Input));
            Assert.AreEqual("<out>d </out>", Matched("x[not(position() = (1, 2, 4))]", Input));

            // A number from somewhere the pattern cannot see into is still a number.
            Assert.AreEqual("<out>d </out>", Matched("x[$n]", Input, "<xsl:variable name=\"n\" select=\"3\"/>"));
            Assert.AreEqual("<out>b </out>", Matched("x[count(../y) + 1]", Input));

            // A path that ends in something other than a node answers with numbers, one here, and a
            // number selects by position: the x whose n is where it stands.
            Assert.AreEqual("<out>b d </out>", Matched("x[@n/xs:integer(.)]", Input));

            // Whichever comes first, the positional one counts within what it should.
            Assert.AreEqual("<out>b </out>", Matched("x[2][@n='2']", Input));
            Assert.AreEqual("<out/>", Matched("x[3][@n='2']", Input));
            Assert.AreEqual("<out>d </out>", Matched("x[@n != '2'][1]", Input));
        }

        /// <summary>What a stylesheet writes, on both backends, which have to agree.</summary>
        private static string OnBoth(string body, string input)
        {
            string sheet = Head + body + "</xsl:stylesheet>";

            string interpreted = new Xslt(sheet, new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                .TransformXml(input);
            string compiled = new Xslt(
                sheet,
                new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30, Backend = XsltBackend.Compiled })
                .TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "The backends disagree.");
            return interpreted;
        }

        [TestMethod]
        public void APositionIsCountedWithinEachParentAndNotCarriedToTheNext()
        {
            // The siblings a position is counted among are remembered from one candidate to the next, and
            // have to be let go of when the next candidate has a different parent.
            const string Input =
                "<r><g><x id='a'/><x id='b'/><x id='c'/></g><g><x id='d'/><x id='e'/></g><g><x id='f'/></g></r>";

            Assert.AreEqual("<out>a d f </out>", Matched("x[1]", Input));
            Assert.AreEqual("<out>b e </out>", Matched("x[2]", Input));
            Assert.AreEqual("<out>c e f </out>", Matched("x[last()]", Input));
            Assert.AreEqual("<out>b d </out>", Matched("x[last() - 1]", Input));
            Assert.AreEqual("<out>b c e </out>", Matched("x[position() > 1]", Input));
            Assert.AreEqual("<out>f </out>", Matched("x[last() = 1]", Input));
            Assert.AreEqual("<out>a c d f </out>", Matched("x[position() = (1, 3)]", Input));

            // A step further out, and the descendant axis, where what is counted among is whatever the
            // anchor selects and the anchor climbs.
            Assert.AreEqual("<out>d e </out>", Matched("g[2]/x", Input));
            Assert.AreEqual("<out>d </out>", Matched("r/descendant::x[4]", Input));
            Assert.AreEqual("<out>b e </out>", Matched("g/descendant::x[2]", Input));
        }

        [TestMethod]
        public void APositionIsRightWhicheverOrderTheCandidatesComeIn()
        {
            const string Rules =
                "<xsl:template match=\"x[last()]\"><xsl:value-of select=\"@id\"/>! </xsl:template>"
                + "<xsl:template match=\"x\"><xsl:value-of select=\"@id\"/><xsl:text> </xsl:text></xsl:template>";

            // Turn about between two parents, so that each candidate finds the other parent's children
            // remembered and must not be counted among them.
            Assert.AreEqual(
                "<out>a d b e! c! </out>",
                OnBoth(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"for $i in 1 to 3 return (/r/g[1]/x[$i], /r/g[2]/x[$i])\"/></out></xsl:template>"
                    + Rules,
                    "<r><g><x id='a'/><x id='b'/><x id='c'/></g><g><x id='d'/><x id='e'/></g></r>"));

            // Turn about between two trees whose parents have the same node number, and whose children
            // do as well: q is the second of two, and would be the second of three if the tree were not
            // part of what is remembered.
            Assert.AreEqual(
                "<out>a p b q! c! </out>",
                OnBoth(
                    "<xsl:variable name=\"t\"><g><x id='p'/><x id='q'/></g></xsl:variable>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/g/x[1], $t/g/x[1], /g/x[2], $t/g/x[2], /g/x[3]\"/></out></xsl:template>"
                    + Rules,
                    "<g><x id='a'/><x id='b'/><x id='c'/></g>"));
        }

        [TestMethod]
        public void APredicateThatMatchesPatternsOfItsOwnDoesNotDisturbThePositionItWasGiven()
        {
            // The function applies templates to the other parent's children, which asks this same pattern
            // about them while the outer candidate's predicate is still being evaluated. What the outer
            // one then reads as position() and last() has to be its own.
            const string Probe =
                "<xsl:function name=\"f:probe\" as=\"xs:string\"><xsl:param name=\"n\" as=\"element()\"/>"
                + "<xsl:value-of><xsl:if test=\"$n/../@id = 'g1'\"><xsl:apply-templates select=\"$n/../../g[@id = 'g2']/x\"/></xsl:if></xsl:value-of>"
                + "</xsl:function>";

            Assert.AreEqual(
                "<out>c e </out>",
                Matched(
                    "x[f:probe(.) = f:probe(.) and position() = last()]",
                    "<r><g id='g1'><x id='a'/><x id='b'/><x id='c'/></g><g id='g2'><x id='d'/><x id='e'/></g></r>",
                    Probe));
        }

        [TestMethod]
        public void APositionalPatternStillReadsTheVariablesOfTheCallItIsAskedIn()
        {
            // xsl:number's count may read a local variable, and it differs from call to call here: each
            // x asks for the x's past the one before it, which is always itself alone.
            Assert.AreEqual(
                "<out>1 1 1 </out>",
                OnBoth(
                    "<xsl:template match=\"/\"><out><xsl:for-each select=\"/g/x\">"
                    + "<xsl:variable name=\"skip\" select=\"position() - 1\"/>"
                    + "<xsl:number count=\"x[position() > $skip]\"/><xsl:text> </xsl:text>"
                    + "</xsl:for-each></out></xsl:template>",
                    "<g><x id='a'/><x id='b'/><x id='c'/></g>"));
        }

        [TestMethod]
        public void APositionSpeltAsAFunctionReferenceIsStillAPosition()
        {
            const string Input = "<r><g><x id='a' k='1'/><x id='b' k='1'/><x id='c'/></g><g><x id='d' k='1'/></g></r>";

            // position#0 and last#0 read the focus they are written in, as the calls do.
            Assert.AreEqual("<out>b </out>", Matched("x[position#0() = 2]", Input));
            Assert.AreEqual("<out>c d </out>", Matched("x[position#0() = last#0()]", Input));

            // After a predicate that drops the first x, the second of what is left is the third of them
            // all: counted among the survivors, which needs the reference seen for the position it is.
            Assert.AreEqual(
                "<out>c </out>",
                Matched("x[@k][position#0() = 2]", "<r><x id='a'/><x id='b' k='1'/><x id='c' k='1'/></r>"));

            // And so may whatever function-lookup hands back, which is not known until it is asked.
            Assert.AreEqual(
                "<out>b </out>",
                Matched("x[function-lookup(QName('http://www.w3.org/2005/xpath-functions', 'position'), 0)() = 2]", Input));
            Assert.AreEqual(
                "<out>c d </out>",
                Matched("x[@id][function-lookup(QName('http://www.w3.org/2005/xpath-functions', 'position'), 0)() = function-lookup(QName('http://www.w3.org/2005/xpath-functions', 'last'), 0)()]", Input));

            // The same question decides whether //x[...] may be read as descendant::x[...], where the
            // position would be counted across the whole document instead of within each parent.
            Assert.AreEqual(
                "<out>a d</out>",
                Run("<xsl:template match=\"/\"><out><xsl:value-of select=\"//x[position#0() = 1]/@id\"/></out></xsl:template>", Input));
            Assert.AreEqual(
                "<out>a d</out>",
                Run("<xsl:template match=\"/\"><out><xsl:value-of select=\"//x[function-lookup(QName('http://www.w3.org/2005/xpath-functions', 'position'), 0)() = 1]/@id\"/></out></xsl:template>", Input));
        }
    }
}
