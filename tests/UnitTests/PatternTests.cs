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

        [TestMethod]
        public void WhatAnEarlierPredicateLeftIsCountedWithinEachParent()
        {
            // x[@k][2] is the second of the x's that have a k. Which of a parent's children have one is
            // worked out once for the parent and kept, so it has to be let go of with the parent, and
            // has to be what it was when it was worked out for every candidate.
            const string Input =
                "<r><g><x id='a' k='1'/><x id='b'/><x id='c' k='2'/><x id='d' k='1'/></g>"
                + "<g><x id='e' k='2'/><x id='f' k='1'/></g><g><x id='h'/></g></r>";

            Assert.AreEqual("<out>a e </out>", Matched("x[@k][1]", Input));
            Assert.AreEqual("<out>c f </out>", Matched("x[@k][2]", Input));
            Assert.AreEqual("<out>d f </out>", Matched("x[@k][last()]", Input));
            Assert.AreEqual("<out>c d f </out>", Matched("x[@k][position() > 1]", Input));
            Assert.AreEqual("<out>b h </out>", Matched("x[not(@k)][1]", Input));
            Assert.AreEqual("<out>d </out>", Matched("x[@k = '1'][2]", Input));
            Assert.AreEqual("<out>a d f </out>", Matched("x[@k][. = ''][@k = '1']", Input));
            Assert.AreEqual("<out>d f </out>", Matched("x[@k][last()][@k = '1']", Input));

            // Three deep: those with a k, the odd ones of those, and the second of what is left.
            Assert.AreEqual("<out>d </out>", Matched("x[@k][position() mod 2 = 1][2]", Input));
            Assert.AreEqual("<out>a d e </out>", Matched("x[@k][position() mod 2 = 1][position() >= 1]", Input));

            // A predicate that reads the position the step gave it, before one that counts again.
            Assert.AreEqual("<out>c f </out>", Matched("x[position() > 1][@k][1]", Input));
            Assert.AreEqual("<out>c e </out>", Matched("x[position() != last()][@k][last()]", Input));
            Assert.AreEqual("<out>c </out>", Matched("x[position() > 1][2]", Input));

            // Steps either side, and a step that climbs.
            Assert.AreEqual("<out>c f </out>", Matched("g/x[@k][2]", Input));
            Assert.AreEqual("<out>c f </out>", Matched("r//x[@k][2]", Input));
            Assert.AreEqual("<out>e f </out>", Matched("g[x[@k][2]][2]/x", Input));
            Assert.AreEqual("<out>c </out>", Matched("g[1]/x[@k][2]", Input));
        }

        [TestMethod]
        public void WhatAnEarlierPredicateLeftIsRightWhicheverOrderTheCandidatesComeIn()
        {
            const string Rules =
                "<xsl:template match=\"x[@k][last()]\"><xsl:value-of select=\"@id\"/>! </xsl:template>"
                + "<xsl:template match=\"x\"><xsl:value-of select=\"@id\"/><xsl:text> </xsl:text></xsl:template>";

            // Turn about between two parents, each candidate finding the other parent's survivors kept.
            Assert.AreEqual(
                "<out>a d b! e! c </out>",
                OnBoth(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"for $i in 1 to 3 return (/r/g[1]/x[$i], /r/g[2]/x[$i])\"/></out></xsl:template>"
                    + Rules,
                    "<r><g><x id='a' k='1'/><x id='b' k='1'/><x id='c'/></g><g><x id='d' k='1'/><x id='e' k='1'/></g></r>"));

            // Turn about between two trees whose nodes have the same numbers: b is the last with a k in
            // the one and q in the other, and each would be answered from the other's list were the tree
            // not part of what is kept.
            Assert.AreEqual(
                "<out>a p b! q c r! </out>",
                OnBoth(
                    "<xsl:variable name=\"t\"><g><x id='p' k='1'/><x id='q' k='1'/><x id='r' k='1'/></g></xsl:variable>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"for $i in 1 to 3 return (/g/x[$i], $t/g/x[$i])\"/></out></xsl:template>"
                    + Rules,
                    "<g><x id='a' k='1'/><x id='b' k='1'/><x id='c'/></g>"));

            // The same stylesheet over a second document, the first one's parent having the number the
            // second one's has.
            Xslt twice = new Xslt(
                Head + Applying("<xsl:template match=\"x[@k][2]\"><xsl:value-of select=\"@id\"/></xsl:template>") + "</xsl:stylesheet>",
                new XsltOptions { OmitXmlDeclaration = true });

            Assert.AreEqual("<out>c</out>", twice.TransformXml("<g><x id='a' k='1'/><x id='b'/><x id='c' k='1'/></g>"));
            Assert.AreEqual("<out>b</out>", twice.TransformXml("<g><x id='a' k='1'/><x id='b' k='1'/><x id='c'/></g>"));
        }

        [TestMethod]
        public void WhatAnEarlierPredicateLeftIsNotKeptWhereItCouldDiffer()
        {
            const string Input = "<r><x id='a' k='1'/><x id='b' k='2'/><x id='c' k='1'/><x id='d' k='2'/><x id='e' k='1'/></r>";

            // current() in a pattern is the candidate, so the first predicate keeps a different set for
            // every candidate: c is the second of those with its k, and d the second of those with its.
            // Kept from one candidate for the next, d would be looked for among the ones and not found.
            Assert.AreEqual("<out>c d </out>", Matched("x[@k = current()/@k][2]", Input));
            Assert.AreEqual("<out>e </out>", Matched("x[@k = current()/@k][3]", Input));

            // A variable is not read as part of the tree either, though a global one would not differ.
            Assert.AreEqual("<out>d </out>", Matched("x[@k = $want][2]", Input, "<xsl:variable name=\"want\" select=\"'2'\"/>"));

            // xsl:number's count may read a local variable that differs from one call to the next: each
            // x is numbered where it is the first of those with its own k, which a and b are.
            Assert.AreEqual(
                "<out>a:1 b:1 c: d: e: </out>",
                OnBoth(
                    "<xsl:template match=\"/\"><out><xsl:for-each select=\"/r/x\">"
                    + "<xsl:variable name=\"mine\" select=\"string(@k)\"/>"
                    + "<xsl:value-of select=\"@id\"/>:<xsl:number count=\"x[@k = $mine][1]\"/><xsl:text> </xsl:text>"
                    + "</xsl:for-each></out></xsl:template>",
                    Input));

            // A predicate after the counting one may match this same pattern under another parent while
            // it is asked, through a function that applies templates. What the outer candidate was
            // counted among has to be its own parent's still when the predicates after that are asked.
            const string Probe =
                "<xsl:function name=\"f:probe\" as=\"xs:string\"><xsl:param name=\"n\" as=\"element()\"/>"
                + "<xsl:value-of><xsl:if test=\"$n/../@id = 'g1'\"><xsl:apply-templates select=\"$n/../../g[@id = 'g2']/x\"/></xsl:if></xsl:value-of>"
                + "</xsl:function>";

            Assert.AreEqual(
                "<out>c e </out>",
                Matched(
                    "x[@k][f:probe(.) = f:probe(.) and position() = last()][@id]",
                    "<r><g id='g1'><x id='a' k='1'/><x id='b'/><x id='c' k='1'/></g><g id='g2'><x id='d' k='1'/><x id='e' k='1'/></g></r>",
                    Probe));
        }

        [TestMethod]
        public void APositionOnADescendantStepIsCountedUnderEachAnchorTheSearchClimbsTo()
        {
            // A step on descendant:: counts among everything its anchor selects, and a node matches where
            // some ancestor is an anchor it holds under. What is kept is what the highest anchor asked
            // selected, and each anchor beneath it reads its own run out of that.
            const string Input =
                "<r id='r'><g id='g1'><x id='a'/><h id='h1'><x id='b'/><x id='c'/></h></g>"
                + "<g id='g2'><x id='d'/><h id='h2'><x id='e'/></h></g><x id='f'/></r>";

            Assert.AreEqual("<out>b e </out>", Matched("g/descendant::x[2]", Input));
            Assert.AreEqual("<out>b e </out>", Matched("h/descendant::x[1]", Input));
            Assert.AreEqual("<out>c e </out>", Matched("h/descendant::x[last()]", Input));
            Assert.AreEqual("<out>c e </out>", Matched("g/descendant::x[last()]", Input));
            Assert.AreEqual("<out>f </out>", Matched("r/descendant::x[last()]", Input));
            Assert.AreEqual("<out>d </out>", Matched("r/descendant::x[4]", Input));
            Assert.AreEqual("<out>e </out>", Matched("r/descendant::x[last() - 1]", Input));
            Assert.AreEqual("<out>a b d e </out>", Matched("g/descendant::x[position() &lt; 3]", Input));

            // With nothing to its left the anchor may be any ancestor at all: c is the third x under g1,
            // and nothing else is the third under anything.
            Assert.AreEqual("<out>c </out>", Matched("descendant::x[3]", Input));
            Assert.AreEqual("<out>a b c d e f </out>", Matched("descendant-or-self::x[1]", Input));

            // descendant-or-self:: counts the anchor in: the second element at or under a g is its first x.
            Assert.AreEqual("<out>a d </out>", Matched("g/descendant-or-self::*[2]", Input));
            Assert.AreEqual("<out>h1 h2 </out>", Matched("g/descendant-or-self::*[3]", Input));

            // More predicates after the one that counts, all at the one position.
            Assert.AreEqual("<out>b </out>", Matched("g/descendant::x[2][@id = 'b']", Input));
            Assert.AreEqual("<out/>", Matched("g/descendant::x[2][@id = 'c']", Input));

            // And one that counts among what an earlier one left, which is counted afresh as it was.
            Assert.AreEqual("<out>c e </out>", Matched("g/descendant::x[@id != 'a'][2]", Input));

            const string Rules =
                "<xsl:template match=\"g/descendant::x[last()]\"><xsl:value-of select=\"@id\"/>! </xsl:template>"
                + "<xsl:template match=\"x\"><xsl:value-of select=\"@id\"/><xsl:text> </xsl:text></xsl:template>";

            // Candidates out of document order, and from a second tree whose nodes have the same numbers.
            Assert.AreEqual(
                "<out>f e! d c! b a </out>",
                OnBoth(
                    "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"reverse(//x)\"/></out></xsl:template>" + Rules,
                    Input));

            Assert.AreEqual(
                "<out>a p b q! c! </out>",
                OnBoth(
                    "<xsl:variable name=\"t\"><g><x id='p'/><x id='q'/></g></xsl:variable>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/g/x[1], $t/g/x[1], /g/x[2], $t/g/x[2], /g/x[3]\"/></out></xsl:template>"
                    + Rules,
                    "<g><x id='a'/><x id='b'/><x id='c'/></g>"));
        }

        [TestMethod]
        public void AStepOnADescendantAxisHangsFromANodeAndNotFromNothing()
        {
            // A pattern matches a node where root(.)//(the pattern) selects it, so a first step on
            // descendant:: selects what is beneath some node of the tree. Once the search for that node
            // had climbed past the document it went on to an anchor of nothing, which is there for a
            // parentless node on the child axis and counts the node as the one node there is: every x
            // was the first x and the last, under nothing.
            const string Input =
                "<r id='r'><g id='g1'><x id='a'/><h id='h1'><x id='b'/><x id='c'/></h></g>"
                + "<g id='g2'><x id='d'/><h id='h2'><x id='e'/></h></g><x id='f'/></r>";

            // The first x under something: a under g1, r and the document, b under h1, d under g2, e under h2.
            Assert.AreEqual("<out>a b d e </out>", Matched("descendant::x[1]", Input));
            Assert.AreEqual("<out>a b d e </out>", Matched("descendant::x[position() = 1]", Input));

            // The last under something: c under h1 and g1, e under h2 and g2, f under r and the document.
            Assert.AreEqual("<out>c e f </out>", Matched("descendant::x[last()]", Input));
            Assert.AreEqual("<out>c e f </out>", Matched("descendant::x[position() = last()]", Input));

            // The only one under something, which e is under h2 and nothing else is under anything; and
            // the last of what the first predicate left, which is the first again.
            Assert.AreEqual("<out>e </out>", Matched("descendant::x[last() = 1]", Input));
            Assert.AreEqual("<out>a b d e </out>", Matched("descendant::x[1][last()]", Input));

            // What was right already stays so: no predicate, a position nothing has under nothing, a
            // step to the left, and descendant-or-self::, where the node itself is an anchor and every
            // x is the first and the last of what it selects from itself.
            Assert.AreEqual("<out>a b c d e f </out>", Matched("descendant::x", Input));
            Assert.AreEqual("<out>c </out>", Matched("descendant::x[3]", Input));
            Assert.AreEqual("<out>a d </out>", Matched("g/descendant::x[1]", Input));
            Assert.AreEqual("<out>a b c d e f </out>", Matched("descendant-or-self::x[1]", Input));
            Assert.AreEqual("<out>a b c d e f </out>", Matched("descendant-or-self::x[last()]", Input));

            // An element with no parent is beneath nothing, so descendant:: does not reach it, where the
            // child axis is adjusted to (child-or-top, XSLT 3.0 section 5.5.3) and self and
            // descendant-or-self begin at it. p is parentless and q is its child.
            static string Parentless(string pattern) => OnBoth(
                "<xsl:mode on-no-match=\"deep-skip\"/>"
                + "<xsl:variable name=\"v\" as=\"element()*\"><x id='p'><x id='q'/></x><y id='s'/></xsl:variable>"
                + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"$v, $v/x\"/></out></xsl:template>"
                + $"<xsl:template match=\"{pattern}\"><xsl:value-of select=\"@id\"/></xsl:template>",
                "<r/>");

            Assert.AreEqual("<out>q</out>", Parentless("descendant::x"));
            Assert.AreEqual("<out>q</out>", Parentless("descendant::x[1]"));
            Assert.AreEqual("<out>q</out>", Parentless("descendant::x[last()]"));
            Assert.AreEqual("<out>pq</out>", Parentless("descendant-or-self::x"));
            Assert.AreEqual("<out>pq</out>", Parentless("descendant-or-self::x[1]"));
            Assert.AreEqual("<out>pq</out>", Parentless("self::x"));
            Assert.AreEqual("<out>pq</out>", Parentless("x"));
            Assert.AreEqual("<out>pq</out>", Parentless("x[1]"));
            Assert.AreEqual("<out>pq</out>", Parentless("x[last()]"));
            Assert.AreEqual("<out>s</out>", Parentless("y[1]"));
        }

        /// <summary>The least time of several a stylesheet takes over a tree, in milliseconds, once warm.</summary>
        private static double LeastTime(Xslt sheet, CodeDeeds.Xslt.Model.XdmTree tree)
        {
            System.Diagnostics.Stopwatch warming = System.Diagnostics.Stopwatch.StartNew();

            for (int runs = 0; runs < 2 || (warming.ElapsedMilliseconds < 300 && runs < 200); runs++)
            {
                sheet.Transform(tree, System.IO.TextWriter.Null);
            }

            double least = double.MaxValue;

            for (int i = 0; i < 9; i++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                sheet.Transform(tree, System.IO.TextWriter.Null);
                least = Math.Min(least, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            return least;
        }

        [TestMethod]
        [DataRow("item[@type='a'][2]", "item[2]", 600)]
        [DataRow("item[@type='a'][last()]", "item[last()]", 600)]
        [DataRow("list/descendant::item[2]", "list//item[2]", 170)]
        [DataRow("list/descendant::item[last()]", "list//item[last()]", 170)]
        public void APositionalStepCostsWhatItsListDoesAndNotTheSquare(string pattern, string beside, int timesAsItWas)
        {
            // Four thousand items under one parent, three elements down. Each pattern is set beside one
            // that does the same work and was never the square of the list: asked of every item, the
            // first two evaluated their first predicate against every sibling for every candidate, and
            // the second two selected every descendant of every ancestor for every candidate, which made
            // them the given number of times the pattern beside them, and more as the list grew. They
            // are within a few times of it now, and the test asks for ten, so that neither a busy
            // machine nor a slow one decides it.
            System.Text.StringBuilder items = new System.Text.StringBuilder("<w><w><list>");

            for (int i = 0; i < 4000; i++)
            {
                items.Append(i % 2 == 0 ? "<item type='a'/>" : "<item type='b'/>");
            }

            CodeDeeds.Xslt.Model.XdmTree tree =
                CodeDeeds.Xslt.Model.XdmTreeBuilder.FromXmlText(items.Append("</list></w></w>").ToString());

            static Xslt Sheet(string match) => new Xslt(
                Head + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"
                + $"<xsl:template match=\"{match}\"><a/></xsl:template>"
                + "<xsl:template match=\"item\" priority=\"-1\"><b/></xsl:template></xsl:stylesheet>");

            Xslt asked = Sheet(pattern);
            Xslt control = Sheet(beside);

            // Both find one item of the four thousand, or the test is of two different things.
            Assert.AreEqual(1, asked.Transform(tree).Split("<a/>").Length - 1, pattern);
            Assert.AreEqual(1, control.Transform(tree).Split("<a/>").Length - 1, beside);

            double ratio = LeastTime(asked, tree) / LeastTime(control, tree);

            if (ratio >= 10)
            {
                // Once more before saying so: one measurement can be anything on a machine doing other work.
                ratio = Math.Min(ratio, LeastTime(asked, tree) / LeastTime(control, tree));
            }

            Assert.IsTrue(
                ratio < 10,
                $"match=\"{pattern}\" took {ratio:F1} times what match=\"{beside}\" took over 4,000 items; it was about {timesAsItWas} when it was the square.");
        }
    }
}
