namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for modes, and for what XSLT 2.0 added to them: several modes on one template, <c>#all</c>,
    /// and <c>#current</c>.
    /// </summary>
    [TestClass]
    public sealed class ModeTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        /// <summary>The map namespace, which the xsl:map tests below name.</summary>
        private const string MapNs = "xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\"";

        /// <summary>The array namespace, which the predicate-pattern tests below name.</summary>
        private const string ArrayNs = "xmlns:array=\"http://www.w3.org/2005/xpath-functions/array\"";

        /// <summary>The schema namespace, which the type tests below name.</summary>
        private const string XsNs = "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"";

        private static string Run(string body, string input, string version = "2.0")
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"{version}\" {Xsl} {MapNs} {ArrayNs} {XsNs}"
                + " exclude-result-prefixes=\"map array xs\">"
                + body
                + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,

                // The processor claims what the stylesheet says: a 2.0 stylesheet runs on a processor asked
                // to be 2.0, so that what 2.0 refuses stays refused, and a 3.0 one on the 3.0 processor the
                // engine is for a caller who names none.
                Version = version == "3.0" ? XsltVersion.V30 : XsltVersion.V20,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        private static string Root(string body) => $"<xsl:template match=\"/\"><out>{body}</out></xsl:template>";

        /// <summary>A template matching the root whose body is the whole result, not wrapped in an out.</summary>
        private static string Root2(string body) => $"<xsl:template match=\"/\">{body}</xsl:template>";

        [TestMethod]
        public void OneTemplateCanServeSeveralNamedModes()
        {
            Assert.AreEqual(
                "<out>[i][i]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\" mode=\"a\"/>"
                        + "<xsl:apply-templates select=\"/r/i\" mode=\"b\"/>")
                    + "<xsl:template match=\"i\" mode=\"a b\">[i]</xsl:template>",
                    "<r><i/></r>"));
        }

        [TestMethod]
        public void ANamedModeAndTheDefaultModeCanShareATemplate()
        {
            // #default is how a template names the mode that a template with no mode attribute belongs to.
            Assert.AreEqual(
                "<out>[i][i]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\"/>"
                        + "<xsl:apply-templates select=\"/r/i\" mode=\"a\"/>")
                    + "<xsl:template match=\"i\" mode=\"#default a\">[i]</xsl:template>",
                    "<r><i/></r>"));
        }

        [TestMethod]
        public void ATemplateInOneModeIsNotReachedFromAnother()
        {
            // The built-in rule handles the node in mode b, which copies its text.
            Assert.AreEqual(
                "<out>[i]text</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\" mode=\"a\"/>"
                        + "<xsl:apply-templates select=\"/r/i\" mode=\"b\"/>")
                    + "<xsl:template match=\"i\" mode=\"a\">[i]</xsl:template>",
                    "<r><i>text</i></r>"));
        }

        [TestMethod]
        public void EveryModeCanBeCoveredAtOnce()
        {
            Assert.AreEqual(
                "<out>[all][all][all]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\"/>"
                        + "<xsl:apply-templates select=\"/r/i\" mode=\"a\"/>"
                        + "<xsl:apply-templates select=\"/r/i\" mode=\"b\"/>")
                    + "<xsl:template match=\"i\" mode=\"#all\">[all]</xsl:template>",
                    "<r><i/></r>"));
        }

        [TestMethod]
        public void AModeNamedOnlyInATemplateBodyIsStillCoveredByEveryMode()
        {
            // The point of expanding #all after the whole stylesheet is compiled: mode 'late' comes into
            // existence in a template body read after the #all template was declared.
            Assert.AreEqual(
                "<out>[all]</out>",
                Run(
                    "<xsl:template match=\"i\" mode=\"#all\">[all]</xsl:template>"
                    + Root("<xsl:apply-templates select=\"/r/i\" mode=\"first\"/>")
                    + "<xsl:template match=\"i\" mode=\"first\">"
                    + "<xsl:apply-templates select=\".\" mode=\"late\"/></xsl:template>",
                    "<r><i/></r>"));
        }

        [TestMethod]
        public void AMoreSpecificTemplateStillWinsWithinAMode()
        {
            // #all does not outrank anything: it only puts the rule in every mode, where priority decides.
            Assert.AreEqual(
                "<out>[specific]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\" mode=\"a\"/>")
                    + "<xsl:template match=\"i\" mode=\"#all\">[all]</xsl:template>"
                    + "<xsl:template match=\"i[@x]\" mode=\"a\">[specific]</xsl:template>",
                    "<r><i x='1'/></r>"));
        }

        [TestMethod]
        public void CurrentModeCarriesOnDownTheTree()
        {
            // Without #current the inner apply-templates would drop into the default mode and find nothing.
            Assert.AreEqual(
                "<out>[a:i][a:j]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\" mode=\"a\"/>")
                    + "<xsl:template match=\"i\" mode=\"a\">[a:i]"
                    + "<xsl:apply-templates select=\"j\" mode=\"#current\"/></xsl:template>"
                    + "<xsl:template match=\"j\" mode=\"a\">[a:j]</xsl:template>",
                    "<r><i><j/></i></r>"));
        }

        [TestMethod]
        public void CurrentModeInsideAModelessTemplateIsTheDefaultMode()
        {
            Assert.AreEqual(
                "<out>[i][j]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\"/>")
                    + "<xsl:template match=\"i\">[i]"
                    + "<xsl:apply-templates select=\"j\" mode=\"#current\"/></xsl:template>"
                    + "<xsl:template match=\"j\">[j]</xsl:template>",
                    "<r><i><j/></i></r>"));
        }

        [TestMethod]
        public void CallTemplateLeavesTheModeAlone()
        {
            // A named template belongs to no mode. What was in force when it was called is still in force
            // inside it, which is what lets one helper be called from several modes and still behave.
            Assert.AreEqual(
                "<out>[helper][a:j]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\" mode=\"a\"/>")
                    + "<xsl:template match=\"i\" mode=\"a\"><xsl:call-template name=\"h\"/></xsl:template>"
                    + "<xsl:template name=\"h\">[helper]"
                    + "<xsl:apply-templates select=\"j\" mode=\"#current\"/></xsl:template>"
                    + "<xsl:template match=\"j\" mode=\"a\">[a:j]</xsl:template>",
                    "<r><i><j/></i></r>"));
        }

        [TestMethod]
        public void NextMatchStaysInTheModeItWasReachedIn()
        {
            Assert.AreEqual(
                "<out>[first][second]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/i\" mode=\"a\"/>")
                    + "<xsl:template match=\"i[@x]\" mode=\"a\">[first]<xsl:next-match/></xsl:template>"
                    + "<xsl:template match=\"i\" mode=\"a\">[second]</xsl:template>",
                    "<r><i x='1'/></r>"));
        }

        [TestMethod]
        public void NamingOtherModesAlongsideEveryModeIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root(string.Empty) + "<xsl:template match=\"i\" mode=\"#all a\">x</xsl:template>",
                    "<r/>"));

            StringAssert.Contains(error.Message, "already applies in every mode");
        }

        [TestMethod]
        public void AnEmptyModeAttributeIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root(string.Empty) + "<xsl:template match=\"i\" mode=\" \">x</xsl:template>",
                    "<r/>"));

            StringAssert.Contains(error.Message, "names no mode");
        }

        // ---- What a mode does with a node no rule matched --------------------------------------------------

        /// <summary>The document the six built-in rule sets are all exercised against.</summary>
        private const string Sample = "<r k=\"1\"><a>x</a><b>y</b></r>";

        [TestMethod]
        [DataRow("text-only-copy", "<out>xy</out>")]
        [DataRow("shallow-copy", "<out><r k=\"1\"><a>x</a><b>y</b></r></out>")]
        [DataRow("deep-copy", "<out><r k=\"1\"><a>x</a><b>y</b></r></out>")]
        [DataRow("shallow-skip", "<out/>")]
        [DataRow("deep-skip", "<out/>")]
        public void AModeSaysWhatBecomesOfANodeNoRuleMatched(string onNoMatch, string expected)
        {
            // Before 3.0 there was one built-in rule set and no way to ask for another. The five others are
            // here because that one answer is wrong for most of what stylesheets are written to do: changing
            // three elements of a document wants shallow-copy, and extracting three wants deep-skip.
            Assert.AreEqual(
                expected,
                Run(
                    $"<xsl:mode name=\"m\" on-no-match=\"{onNoMatch}\"/>"
                    + Root("<xsl:apply-templates select=\"r\" mode=\"m\"/>"),
                    Sample,
                    "3.0"));
        }

        [TestMethod]
        public void AModeThatCopiesShallowlyStillLetsARuleReplaceWhatItMatches()
        {
            // Which is the whole point of shallow-copy: the stylesheet writes the rules for what changes and
            // says nothing about the rest.
            Assert.AreEqual(
                "<out><r k=\"1\"><a>x</a><B/></r></out>",
                Run(
                    "<xsl:mode name=\"m\" on-no-match=\"shallow-copy\"/>"
                    + "<xsl:template match=\"b\" mode=\"m\"><B/></xsl:template>"
                    + Root("<xsl:apply-templates select=\"r\" mode=\"m\"/>"),
                    Sample,
                    "3.0"));
        }

        [TestMethod]
        public void AModeThatSkipsShallowlyStillReachesWhatARuleMatches()
        {
            // shallow-skip goes down without writing anything, so what comes out is exactly what the rules
            // wrote and nothing else — the extraction case.
            Assert.AreEqual(
                "<out><B/></out>",
                Run(
                    "<xsl:mode name=\"m\" on-no-match=\"shallow-skip\"/>"
                    + "<xsl:template match=\"b\" mode=\"m\"><B/></xsl:template>"
                    + Root("<xsl:apply-templates select=\"r\" mode=\"m\"/>"),
                    Sample,
                    "3.0"));
        }

        [TestMethod]
        public void ADeepCopyingModeMatchesNothingFurtherDown()
        {
            // The difference between deep-copy and shallow-copy in one test: deep-copy takes the subtree
            // whole, so a rule for something inside it never gets the chance to run.
            Assert.AreEqual(
                "<out><r k=\"1\"><a>x</a><b>y</b></r></out>",
                Run(
                    "<xsl:mode name=\"m\" on-no-match=\"deep-copy\"/>"
                    + "<xsl:template match=\"b\" mode=\"m\"><B/></xsl:template>"
                    + Root("<xsl:apply-templates select=\"r\" mode=\"m\"/>"),
                    Sample,
                    "3.0"));
        }

        [TestMethod]
        public void AModeMayRefuseToHaveAnythingUnmatched()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:mode name=\"m\" on-no-match=\"fail\"/>"
                    + Root("<xsl:apply-templates select=\"r\" mode=\"m\"/>"),
                    Sample,
                    "3.0"));

            Assert.AreEqual("XTDE0555", error.Code);
            StringAssert.Contains(error.Message, "'r'");
        }

        [TestMethod]
        public void AnUndeclaredModeBehavesAsEveryModeDidBeforeThirty()
        {
            // A mode exists as soon as a rule names it, so xsl:mode declares nothing into being. Saying
            // nothing about a mode has to keep meaning what it meant.
            Assert.AreEqual(
                "<out>xy</out>",
                Run(
                    Root("<xsl:apply-templates select=\"r\" mode=\"m\"/>")
                    + "<xsl:template match=\"zzz\" mode=\"m\"/>",
                    Sample,
                    "3.0"));
        }

        [TestMethod]
        public void TheUnnamedModeIsDeclaredByAnXslModeWithNoName()
        {
            Assert.AreEqual(
                "<out><r k=\"1\"><a>x</a><b>y</b></r></out>",
                Run(
                    "<xsl:mode on-no-match=\"shallow-copy\"/>"
                    + Root("<xsl:apply-templates select=\"r\"/>"),
                    Sample,
                    "3.0"));
        }

        [TestMethod]
        public void TwoDeclarationsOfOneModeAtOnePrecedenceAreRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:mode name=\"m\" on-no-match=\"deep-skip\"/>"
                    + "<xsl:mode name=\"m\" on-no-match=\"deep-copy\"/>"
                    + Root(string.Empty),
                    Sample,
                    "3.0"));

            Assert.AreEqual("XTSE0545", error.Code);
        }

        /// <summary>An accumulator counting elements, and a template reading it at the root.</summary>
        /// <param name="declared">What the <c>xsl:mode</c> for the unnamed mode says, or nothing.</param>
        private static string Counting(string declared)
        {
            return Run(
                declared
                + "<xsl:accumulator name=\"n\" initial-value=\"0\">"
                + "<xsl:accumulator-rule match=\"*\" select=\"$value + 1\"/></xsl:accumulator>"
                + Root("<xsl:value-of select=\"accumulator-after('n')\"/>"),
                "<r><a/><b/></r>",
                "3.0");
        }

        [TestMethod]
        public void AnAccumulatorAppliesOnlyWhereItWasAskedFor()
        {
            // Which accumulators apply to a document is fixed when the document is made available, and for
            // the source document that is the mode the transformation begins in. Asking for one that does
            // not apply is an error rather than the initial value: it was never run over this document.
            Assert.AreEqual("<out>3</out>", Counting("<xsl:mode use-accumulators=\"n\"/>"));
            Assert.AreEqual("<out>3</out>", Counting("<xsl:mode use-accumulators=\"#all\"/>"));

            // Saying nothing is none, whether by an xsl:mode with nothing to say or by no xsl:mode at all.
            // That is the whole point of the attribute: a streaming processor has to be told before it reads
            // the document what it will be asked about it.
            foreach (string declared in new[] { string.Empty, "<xsl:mode/>", "<xsl:mode use-accumulators=\"\"/>" })
            {
                Assert.AreEqual(
                    "XTDE3362",
                    Assert.ThrowsExactly<XsltException>(() => Counting(declared)).Code,
                    declared);
            }
        }

        [TestMethod]
        public void AStylesheetClaimingTwoPointZeroStillGetsTheProcessorsVocabulary()
        {
            // version="2.0" asks for backwards-compatible behaviour, not for a smaller language: a 3.0
            // processor reads the stylesheet as a 3.0 stylesheet with that behaviour on, so its
            // xsl:accumulator is an accumulator. The rule the attributes have followed for some time, and the
            // one the elements now follow too.
            string stylesheet =
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:mode use-accumulators=\"#all\"/>"
                + "<xsl:accumulator name=\"n\" initial-value=\"0\">"
                + "<xsl:accumulator-rule match=\"*\" select=\"$value + 1\"/></xsl:accumulator>"
                + "<xsl:template match=\"/\"><out><xsl:value-of select=\"accumulator-after('n')\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>3</out>",
                new Xslt(
                    stylesheet,
                    new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                    .TransformXml("<r><a/><b/></r>"));

            // And a processor claiming 2.0 refuses it, because it is not an element 2.0 has.
            Assert.AreEqual(
                "XTSE0010",
                Assert.ThrowsExactly<XsltException>(() => new Xslt(
                    stylesheet,
                    new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V20 })).Code);
        }

        [TestMethod]
        public void ADocumentHandedOverByTheCallerCarriesEveryAccumulator()
        {
            // Started at a named template, the source document is merely the global context item — nothing
            // in the stylesheet made it available, so nothing has narrowed what it carries. The same reading
            // doc() gets, and the reading the suite's own mode tests rely on.
            string stylesheet =
                $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + "<xsl:accumulator name=\"n\" initial-value=\"0\">"
                + "<xsl:accumulator-rule match=\"*\" select=\"$value + 1\"/></xsl:accumulator>"
                + "<xsl:template name=\"main\"><out><xsl:value-of select=\"accumulator-after('n')\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>3</out>",
                new Xslt(
                    stylesheet,
                    new XsltOptions
                    {
                        OmitXmlDeclaration = true,
                        Version = XsltVersion.V30,
                        InitialTemplate = "main",
                    }).TransformXml("<r><a/><b/></r>"));
        }

        [TestMethod]
        public void ACopyMadeWithItsAccumulatorsAnswersAsTheOriginalDid()
        {
            // The values are the original's, not values recomputed over the copy: the second group's items
            // are the third and fourth counted, and a copy that restarted at one would say so.
            const string Counted =
                "<xsl:accumulator name=\"n\" initial-value=\"0\">"
                + "<xsl:accumulator-rule match=\"i\" select=\"$value + 1\"/></xsl:accumulator>"
                + "<xsl:mode use-accumulators=\"n\"/>";

            const string Second =
                "<xsl:variable name=\"g\" as=\"element()\">"
                + "<xsl:copy-of select=\"/r/g[2]\" copy-accumulators=\"{0}\"/></xsl:variable>"
                + "<xsl:for-each select=\"$g/i\"><v><xsl:value-of select=\"accumulator-after('n')\"/></v>"
                + "</xsl:for-each>";

            string Copied(string flag) => Run(
                Counted + Root(string.Format(Second, flag)),
                "<r><g><i/><i/></g><g><i/><i/></g></r>",
                "3.0");

            Assert.AreEqual("<out><v>3</v><v>4</v></out>", Copied("yes"));

            // Without the attribute the copy is a fresh tree, and a tree the stylesheet builds for itself
            // carries every accumulator, computed over the copy from the beginning.
            Assert.AreEqual("<out><v>1</v><v>2</v></out>", Copied("no"));
        }

        [TestMethod]
        public void TheCopyOfAndSnapshotFunctionsCopyTheAccumulatorsWithTheNode()
        {
            // Neither function has a copy-accumulators switch: the copy always answers as the original did.
            // What the suite's accumulator-064 and -066 settle, and what makes a snapshot usable as the
            // context of a rule that counts.
            const string Counted =
                "<xsl:accumulator name=\"n\" initial-value=\"0\">"
                + "<xsl:accumulator-rule match=\"i\" select=\"$value + 1\"/></xsl:accumulator>"
                + "<xsl:mode use-accumulators=\"n\"/>";

            string Through(string function) => Run(
                Counted
                + Root("<xsl:for-each select=\"" + function + "(/r/i[2])\">"
                    + "<v><xsl:value-of select=\"accumulator-after('n')\"/></v></xsl:for-each>"),
                "<r><i/><i/></r>",
                "3.0");

            Assert.AreEqual("<out><v>2</v></out>", Through("copy-of"));
            Assert.AreEqual("<out><v>2</v></out>", Through("snapshot"));
        }

        [TestMethod]
        public void AComputedAccumulatorNameIsLookedUpWhenItIsKnown()
        {
            // A name written out is checked when the stylesheet is compiled. A computed one can only be
            // checked when it is computed, against the same declarations and with the prefixes in scope
            // where the call was written — or with none, written with its namespace in it.
            const string Counted =
                "<xsl:accumulator name=\"p:n\" initial-value=\"0\" xmlns:p=\"urn:p\">"
                + "<xsl:accumulator-rule match=\"i\" select=\"$value + 1\"/></xsl:accumulator>";

            string Through(string name) => Run(
                Applies
                + Counted
                + Root("<xsl:variable name=\"name\" select=\"" + name + "\"/>"
                    + "<xsl:value-of select=\"accumulator-after($name)\" xmlns:p=\"urn:p\"/>"),
                "<r><i/><i/></r>",
                "3.0");

            Assert.AreEqual("<out>2</out>", Through("'p:n'"));
            Assert.AreEqual("<out>2</out>", Through("'Q{urn:p}n'"));

            XsltException error = Assert.ThrowsExactly<XsltException>(() => Through("'p:nonesuch'"));
            Assert.AreEqual("XTDE3340", error.Code);
        }

        [TestMethod]
        public void AReferenceToAnAccumulatorFunctionReadsTheNodeItWasMadeOn()
        {
            // accumulator-after#1 made on r and called on each item still answers for r: the reference
            // carries the focus of the place it was written, which is how the suite's accumulator-062 binds
            // ../accumulator-before#1 to a parameter and reads the parent from inside the child.
            Assert.AreEqual(
                "<out><v>2</v><v>2</v></out>",
                Run(
                    Applies
                    + "<xsl:accumulator name=\"n\" initial-value=\"0\">"
                    + "<xsl:accumulator-rule match=\"i\" select=\"$value + 1\"/></xsl:accumulator>"
                    + Root("<xsl:variable name=\"f\" select=\"/r/accumulator-after#1\"/>"
                        + "<xsl:for-each select=\"/r/i\"><v><xsl:value-of select=\"$f('n')\"/></v>"
                        + "</xsl:for-each>"),
                    "<r><i/><i/></r>",
                    "3.0"));
        }

        [TestMethod]
        public void ACopyCannotCarryAnAccumulatorTheOriginalDidNotHave()
        {
            // What the suite's copy-3002 settles: with no xsl:mode the source document has no accumulators,
            // and a copy made with copy-accumulators="yes" carries exactly what the original had — nothing.
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(
                "<xsl:accumulator name=\"n\" initial-value=\"0\">"
                + "<xsl:accumulator-rule match=\"i\" select=\"$value + 1\"/></xsl:accumulator>"
                + Root("<xsl:variable name=\"g\" as=\"element()\">"
                    + "<xsl:copy-of select=\"/r/i\" copy-accumulators=\"yes\"/></xsl:variable>"
                    + "<xsl:value-of select=\"$g/accumulator-after('n')\"/>"),
                "<r><i/></r>",
                "3.0"));

            Assert.AreEqual("XTDE3362", error.Code);
        }

        [TestMethod]
        public void TwoModeDeclarationsAgreeAboutWhichAccumulatorsTheyNameRatherThanHowTheyWroteThem()
        {
            const string Accumulators =
                "<xsl:accumulator name=\"a\" initial-value=\"0\">"
                + "<xsl:accumulator-rule match=\"*\" select=\"$value + 1\"/></xsl:accumulator>"
                + "<xsl:accumulator name=\"b\" initial-value=\"0\">"
                + "<xsl:accumulator-rule match=\"*\" select=\"$value + 1\"/></xsl:accumulator>";

            // The same two accumulators, written in the other order. Not a disagreement: what the two
            // declarations have to agree about is which accumulators they name.
            Assert.AreEqual(
                "<out>3</out>",
                Run(
                    Accumulators
                    + "<xsl:mode use-accumulators=\"a b\"/>"
                    + "<xsl:mode use-accumulators=\"b a\"/>"
                    + Root("<xsl:value-of select=\"accumulator-after('a')\"/>"),
                    "<r><a/><b/></r>",
                    "3.0"));

            Assert.AreEqual(
                "XTSE0545",
                Assert.ThrowsExactly<XsltException>(() => Run(
                    Accumulators
                    + "<xsl:mode use-accumulators=\"a\"/>"
                    + "<xsl:mode use-accumulators=\"b\"/>"
                    + Root(string.Empty),
                    "<r><a/></r>",
                    "3.0")).Code);
        }

        [TestMethod]
        public void AnOnNoMatchOutsideTheSixIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:mode name=\"m\" on-no-match=\"deep-fry\"/>" + Root(string.Empty),
                    Sample,
                    "3.0"));

            Assert.AreEqual("XTSE0020", error.Code);
        }

        // ---- Text value templates --------------------------------------------------------------------------

        [TestMethod]
        public void BracesInTextAreExpressionsWhereExpandTextSaysSo()
        {
            Assert.AreEqual(
                "<out>2</out>",
                Run(
                    "<xsl:template match=\"/\" expand-text=\"yes\"><out>{1 + 1}</out></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void BracesInTextAreCharactersWhereNothingSaysOtherwise()
        {
            // The default, and the reason the attribute has to exist at all: text with a brace in it is far
            // more common than text meaning to compute something.
            Assert.AreEqual(
                "<out>{1 + 1}</out>",
                Run(Root("{1 + 1}"), "<r/>", "3.0"));
        }

        [TestMethod]
        public void ExpandTextIsInheritedAndCanBeTurnedBackOff()
        {
            // Inherited down the stylesheet tree the way version and xml:space are, so the nearest ancestor
            // that says anything is the one that decides.
            Assert.AreEqual(
                "<out><on>2</on><off>{1 + 1}</off></out>",
                Run(
                    "<xsl:template match=\"/\" expand-text=\"yes\"><out>"
                    + "<on>{1 + 1}</on><off xsl:expand-text=\"no\">{1 + 1}</off>"
                    + "</out></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void OnALiteralResultElementTheAttributeCarriesThePrefix()
        {
            // Unprefixed there it is an ordinary attribute of the element being produced, and is copied out
            // as one. The prefixed form is the only way to address the processor from a literal element.
            Assert.AreEqual(
                "<out expand-text=\"yes\">{1 + 1}</out>",
                Run(
                    "<xsl:template match=\"/\"><out expand-text=\"yes\">{1 + 1}</out></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void ADoubledBraceIsALiteralOne()
        {
            Assert.AreEqual(
                "<out>{1 + 1}</out>",
                Run(
                    "<xsl:template match=\"/\" expand-text=\"yes\"><out>{{1 + 1}}</out></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void AnExpandedSequenceIsJoinedWithSpaces()
        {
            Assert.AreEqual(
                "<out>1 2 3</out>",
                Run(
                    "<xsl:template match=\"/\" expand-text=\"yes\"><out>{(1, 2, 3)}</out></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void TextValueTemplatesAreRecognisedInsideXslText()
        {
            // xsl:text says how the text is treated, not whether it is text — so expand-text reaches into it
            // like anywhere else, and may be turned off on the element itself.
            Assert.AreEqual(
                "<out>4|{2+2}</out>",
                Run(
                    "<xsl:template match=\"/\" expand-text=\"yes\"><out>"
                    + "<xsl:text>{2+2}</xsl:text>|<xsl:text expand-text=\"no\">{2+2}</xsl:text>"
                    + "</out></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void ExpandTextReachesTheContentOfAnAttribute()
        {
            Assert.AreEqual(
                "<out a=\"1 2\"/>",
                Run(
                    "<xsl:template match=\"/\" expand-text=\"yes\"><out>"
                    + "<xsl:attribute name=\"a\">{(1, 2)}</xsl:attribute></out></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void AnExpandTextOutsideYesAndNoIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\" expand-text=\"maybe\"><out>{1}</out></xsl:template>",
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTSE0020", error.Code);
        }

        // ---- What a template says it is standing on ---------------------------------------------------------

        [TestMethod]
        public void ATemplateMayDeclareTheTypeOfWhatItIsStandingOn()
        {
            Assert.AreEqual(
                "<out>1 2 3</out>",
                Run(
                    "<xsl:template name=\"t\"><xsl:context-item as=\"xs:integer\"/>"
                    + "<xsl:sequence select=\".\"/></xsl:template>"
                    + Root("<xsl:for-each select=\"1 to 3\"><xsl:call-template name=\"t\"/></xsl:for-each>"),
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void AContextItemOfTheWrongTypeIsRefusedAtTheCall()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template name=\"t\"><xsl:context-item as=\"xs:integer\"/>"
                    + "<xsl:sequence select=\".\"/></xsl:template>"
                    + Root("<xsl:for-each select=\"'s'\"><xsl:call-template name=\"t\"/></xsl:for-each>"),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTTE0590", error.Code);
        }

        [TestMethod]
        public void ATemplateDeclaringNoContextItemCannotReadOne()
        {
            // use="absent" takes the focus away rather than checking for it: the promise is that the template
            // does not read its surroundings, so the caller having an item is no error — the body just
            // cannot see it, and reading it is XPDY0002 as anywhere else.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template name=\"t\"><xsl:context-item use=\"absent\"/>"
                    + "<xsl:value-of select=\"name(.)\"/></xsl:template>"
                    + Root("<xsl:call-template name=\"t\"/>"),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XPDY0002", error.Code);
        }

        [TestMethod]
        public void ATemplateDeclaringNoContextItemStillRuns()
        {
            Assert.AreEqual(
                "<out>ok</out>",
                Run(
                    "<xsl:template name=\"t\"><xsl:context-item use=\"absent\"/>"
                    + "<xsl:value-of select=\"'ok'\"/></xsl:template>"
                    + Root("<xsl:call-template name=\"t\"/>"),
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void AContextItemDeclaredAbsentAndTypedIsAContradiction()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template name=\"t\"><xsl:context-item use=\"absent\" as=\"xs:integer\"/>"
                    + "</xsl:template>" + Root(string.Empty),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTSE3088", error.Code);
        }

        [TestMethod]
        public void AUseOutsideTheThreeIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template name=\"t\"><xsl:context-item use=\"maybe\"/></xsl:template>"
                    + Root(string.Empty),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTSE0020", error.Code);
        }

        [TestMethod]
        public void AStylesheetMayDeclareTheDocumentItIsRunAgainst()
        {
            Assert.AreEqual(
                "<out>r</out>",
                Run(
                    "<xsl:global-context-item as=\"document-node()\"/>"
                    + Root("<xsl:value-of select=\"name(/*)\"/>"),
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void ASourceDocumentOfTheWrongShapeIsRefusedBeforeAnythingRuns()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:global-context-item as=\"document-node(element(other))\"/>"
                    + Root("<xsl:value-of select=\"name(/*)\"/>"),
                    "<r/>",
                    "3.0"));

            // The caller's mistake and reported to the caller: what was handed in does not match what the
            // stylesheet said it would be handed. XTTE3086 is the code for a stylesheet that needs a source
            // document and was started without one, which is a different complaint.
            Assert.AreEqual("XTTE0590", error.Code);
        }

        [TestMethod]
        public void OneGlobalContextItemDeclarationPerPackageAndNoMore()
        {
            // A module carrying two is refused whether or not they agree: one says what the transformation
            // is run against, and a second says it again or says something else (XSLT 3.0 §3.5.2).
            XsltException twice = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:global-context-item use=\"optional\"/>"
                    + "<xsl:global-context-item use=\"optional\"/>"
                    + Root(string.Empty),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTSE3087", twice.Code);

            // Saying the item is absent and declaring a type for it is a contradiction, and the two elements
            // that can say it have a code each.
            XsltException typed = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:global-context-item use=\"absent\" as=\"document-node()\"/>"
                    + Root(string.Empty),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTSE3089", typed.Code);
        }

        [TestMethod]
        public void GlobalsSeeNoContextItemWhereTheStylesheetSaidTheyWouldNot()
        {
            // A global is evaluated in a context of its own, and what xsl:global-context-item said has to
            // reach that one too: a stylesheet that declared it would be given no context item cannot then
            // name the source document from a global variable.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:global-context-item use=\"absent\"/>"
                    + "<xsl:variable name=\"g\" select=\"/r\"/>"
                    + Root("<xsl:value-of select=\"count($g)\"/>"),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XPDY0002", error.Code);
        }

        // ---- Markup around content that may not be there -----------------------------------------------------

        [TestMethod]
        public void WherePopulatedKeepsWhatHasSomethingInIt()
        {
            Assert.AreEqual(
                "<out><kept>x</kept></out>",
                Run(
                    Root("<xsl:where-populated><kept><xsl:value-of select=\"'x'\"/></kept>"
                        + "</xsl:where-populated>"),
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void AnElementWithOnlyAttributesIsNotPopulated()
        {
            // The case the instruction exists for: <div class="x"/> wrapped around nothing is exactly the
            // empty markup a stylesheet is trying not to write, and its attributes do not save it.
            Assert.AreEqual(
                "<out/>",
                Run(
                    Root("<xsl:where-populated><dropped a=\"1\"/></xsl:where-populated>"),
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void OnNonEmptyWritesWhereItStandsRatherThanAtTheEnd()
        {
            // A heading declared before the items it heads has to come out before them, so the conditional
            // pieces keep their places among the unconditional ones.
            Assert.AreEqual(
                "<out><list><h>Items</h><i/><i/></list></out>",
                Run(
                    Root("<list><xsl:on-non-empty><h>Items</h></xsl:on-non-empty>"
                        + "<xsl:for-each select=\"r/a\"><i/></xsl:for-each></list>"),
                    "<r><a/><a/></r>",
                    "3.0"));
        }

        [TestMethod]
        public void OnEmptyAnswersForTheWholeOfTheRest()
        {
            Assert.AreEqual(
                "<out><list><none/></list></out>",
                Run(
                    Root("<list><xsl:on-non-empty><h>Items</h></xsl:on-non-empty>"
                        + "<xsl:for-each select=\"r/zz\"><i/></xsl:for-each>"
                        + "<xsl:on-empty><none/></xsl:on-empty></list>"),
                    "<r><a/></r>",
                    "3.0"));
        }

        [TestMethod]
        public void OnEmptyHasToComeLast()
        {
            // Written anywhere else it would be asking about the part above it and answering for the whole,
            // and the specification refused to pick one of those two readings.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<list><xsl:on-empty><none/></xsl:on-empty><i/></list>"),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTSE0010", error.Code);
        }

        [TestMethod]
        public void EachConditionalPieceSeesTheVariablesInScopeWhereItStands()
        {
            // Which is why they are evaluated in the order they were written rather than deferred to after
            // the decision: $x is rebound between the two, and each has to read its own.
            Assert.AreEqual(
                "<out><a>21</a><b>23</b><in/></out>",
                Run(
                    Root("<xsl:variable name=\"x\" select=\"21\"/>"
                        + "<xsl:on-non-empty><a><xsl:value-of select=\"$x\"/></a></xsl:on-non-empty>"
                        + "<xsl:variable name=\"x\" select=\"23\"/>"
                        + "<xsl:on-non-empty><b><xsl:value-of select=\"$x\"/></b></xsl:on-non-empty>"
                        + "<in/>"),
                    "<r/>",
                    "3.0"));
        }

        // ---- Building a map from a sequence constructor -------------------------------------------------------

        [TestMethod]
        public void AnXslMapBuildsItsEntriesFromWhateverProducesThem()
        {
            // The whole reason the instruction exists beside the map { } expression: a map whose shape
            // depends on the document cannot be written as a literal.
            Assert.AreEqual(
                "<out size=\"5\" at3=\"30\"/>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"m\" as=\"item()\"><xsl:map>"
                    + "<xsl:for-each select=\"1 to 5\"><xsl:map-entry key=\".\" select=\". * 10\"/></xsl:for-each>"
                    + "</xsl:map></xsl:variable>"
                    + "<out size=\"{map:size($m)}\" at3=\"{$m(3)}\"/></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void AMapEntryMayTakeItsValueFromContent()
        {
            Assert.AreEqual(
                "<out><v/></out>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"m\" as=\"item()\"><xsl:map>"
                    + "<xsl:map-entry key=\"'a'\"><v/></xsl:map-entry></xsl:map></xsl:variable>"
                    + "<out><xsl:copy-of select=\"$m('a')\"/></out></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void TwoEntriesOfOneKeyAreRefusedRatherThanDecided()
        {
            // Nothing in the instruction says which was meant, and keeping one quietly would make the
            // answer depend on the order the content happened to run in.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"m\" as=\"item()\"><xsl:map>"
                    + "<xsl:map-entry key=\"'a'\" select=\"1\"/>"
                    + "<xsl:map-entry key=\"'a'\" select=\"2\"/></xsl:map></xsl:variable>"
                    + "<out/></xsl:template>",
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTDE3365", error.Code);
        }

        // ---- Walking several ordered sequences at once ---------------------------------------------------------

        /// <summary>Two overlapping sorted runs, which is the shape a merge is for.</summary>
        private const string Runs = "<r><odd><n>1</n><n>3</n><n>5</n></odd><even><n>2</n><n>3</n><n>4</n></even></r>";

        [TestMethod]
        public void AMergeVisitsEachDistinctKeyOnce()
        {
            // Key 3 is in both sources, so it is one group of two items rather than two groups.
            Assert.AreEqual(
                "<out><g k=\"1\" n=\"1\"/><g k=\"2\" n=\"1\"/><g k=\"3\" n=\"2\"/>"
                + "<g k=\"4\" n=\"1\"/><g k=\"5\" n=\"1\"/></out>",
                Run(
                    Root("<xsl:merge>"
                        + "<xsl:merge-source select=\"r/odd/n\">"
                        + "<xsl:merge-key select=\".\" data-type=\"number\"/></xsl:merge-source>"
                        + "<xsl:merge-source select=\"r/even/n\">"
                        + "<xsl:merge-key select=\".\" data-type=\"number\"/></xsl:merge-source>"
                        + "<xsl:merge-action>"
                        + "<g k=\"{current-merge-key()}\" n=\"{count(current-merge-group())}\"/>"
                        + "</xsl:merge-action></xsl:merge>"),
                    Runs,
                    "3.0"));
        }

        [TestMethod]
        public void AMergeGroupCanBeAskedWhichSourceAnItemCameFrom()
        {
            // Which is what naming a source is for: after the merge the items are interleaved, and the
            // action still has to be able to tell them apart.
            Assert.AreEqual(
                "<out><g k=\"3\" odd=\"1\" even=\"1\"/></out>",
                Run(
                    Root("<xsl:merge>"
                        + "<xsl:merge-source name=\"odd\" select=\"r/odd/n[. = 3]\">"
                        + "<xsl:merge-key select=\".\" data-type=\"number\"/></xsl:merge-source>"
                        + "<xsl:merge-source name=\"even\" select=\"r/even/n[. = 3]\">"
                        + "<xsl:merge-key select=\".\" data-type=\"number\"/></xsl:merge-source>"
                        + "<xsl:merge-action>"
                        + "<g k=\"{current-merge-key()}\" odd=\"{count(current-merge-group('odd'))}\""
                        + " even=\"{count(current-merge-group('even'))}\"/>"
                        + "</xsl:merge-action></xsl:merge>"),
                    Runs,
                    "3.0"));
        }

        [TestMethod]
        public void AMergeOrdersBySeveralKeysMostSignificantFirst()
        {
            // sort-before-merge, the input being deliberately out of order: without it a merge takes each
            // source as already sorted and refuses one that is not.
            Assert.AreEqual(
                "<out><g k=\"a1\"/><g k=\"a2\"/><g k=\"b1\"/></out>",
                Run(
                    Root("<xsl:merge>"
                        + "<xsl:merge-source select=\"r/i\" sort-before-merge=\"yes\">"
                        + "<xsl:merge-key select=\"@a\"/><xsl:merge-key select=\"@b\"/>"
                        + "</xsl:merge-source>"
                        + "<xsl:merge-action><g k=\"{current-merge-key()[1]}{current-merge-key()[2]}\"/>"
                        + "</xsl:merge-action></xsl:merge>"),
                    "<r><i a='b' b='1'/><i a='a' b='2'/><i a='a' b='1'/></r>",
                    "3.0"));
        }

        [TestMethod]
        public void AMergeWithNothingToMergeIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:merge><xsl:merge-action><g/></xsl:merge-action></xsl:merge>"), "<r/>", "3.0"));

            Assert.AreEqual("XTSE0010", error.Code);
        }

        // ---- The three that are shorter than what they replace -------------------------------------------------

        [TestMethod]
        public void AnAssertionThatHoldsProducesNothing()
        {
            Assert.AreEqual(
                "<out>ok</out>",
                Run(
                    "<xsl:param name=\"p\" select=\"2\"/>"
                    + Root("<xsl:assert test=\"$p eq 2\">never seen</xsl:assert>ok"),
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void AnAssertionThatFailsCarriesItsOwnMessage()
        {
            // Not xsl:message terminate="yes" written shorter: an assertion is a claim about what the
            // stylesheet expects, which is why it gets a code of its own.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:param name=\"p\" select=\"2\"/>"
                    + Root("<xsl:assert test=\"$p eq 3\">p was not 3</xsl:assert>"),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTMM9000", error.Code);
            StringAssert.Contains(error.Message, "p was not 3");
        }

        [TestMethod]
        public void ForkIsAPromiseAboutStreamingAndNothingElse()
        {
            // A streaming processor uses it to make several passes over a document it can read once. One
            // holding the whole document already has that freedom, so the branches simply run in order.
            Assert.AreEqual(
                "<out><one/><two/></out>",
                Run(Root("<xsl:fork><one/><two/></xsl:fork>"), "<r/>", "3.0"));
        }

        // ---- Packages ----------------------------------------------------------------------------------------

        /// <summary>Serves library packages by the name an <c>xsl:use-package</c> asks for.</summary>
        private sealed class PackageLibrary : IXsltResolver
        {
            private readonly Dictionary<string, string> m_packages = new(StringComparer.Ordinal);

            public PackageLibrary Add(string name, string source)
            {
                m_packages[name] = source;
                return this;
            }

            public ResolvedResource? Resolve(string name, string? baseUri)
            {
                return m_packages.TryGetValue(name, out string? text)
                    ? new ResolvedResource(new StringReader(text), name)
                    : null;
            }
        }

        /// <summary>Wraps declarations in an <c>xsl:package</c> of a given name.</summary>
        private static string Package(string name, string body)
        {
            return $"<xsl:package name=\"{name}\" package-version=\"1.0.0\" version=\"3.0\" "
                + "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:p=\"urn:p\" "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"p xs\">" + body + "</xsl:package>";
        }

        /// <summary>Runs a package from its named entry point.</summary>
        private static string RunPackage(string principal, PackageLibrary? library = null)
        {
            return new Xslt(
                principal,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    InitialTemplate = "main",
                    PackageResolver = library,
                }).Transform();
        }

        [TestMethod]
        public void APackageIsAModuleWhoseOutermostElementSaysSo()
        {
            Assert.AreEqual(
                "<ok/>",
                RunPackage(Package("urn:one", "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));
        }

        [TestMethod]
        public void APrivateTemplateIsNotAWayIntoAPackage()
        {
            // The caller is outside the package by definition, so what it may start at is what the package
            // said it offers. Inside a package the default is private, which is the whole point of one.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package("urn:one", "<xsl:template name=\"main\"><ok/></xsl:template>")));

            Assert.AreEqual("XTDE0040", error.Code);
        }

        [TestMethod]
        public void AnOrdinaryStylesheetHasNoBoundaryForVisibilityToBeAbout()
        {
            // Nothing outside to hide from, so a named template of a plain stylesheet stays an entry point.
            Assert.AreEqual(
                "<ok/>",
                new Xslt(
                    "<xsl:stylesheet version=\"3.0\" " + Xsl + ">"
                    + "<xsl:template name=\"main\"><ok/></xsl:template></xsl:stylesheet>",
                    new XsltOptions
                    {
                        Version = XsltVersion.V30,
                        OmitXmlDeclaration = true,
                        InitialTemplate = "main",
                    }).Transform());
        }

        [TestMethod]
        public void APublicComponentOfAUsedPackageIsReachable()
        {
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base", "<xsl:function name=\"p:f\" visibility=\"public\">"
                    + "<xsl:param name=\"n\"/><xsl:sequence select=\"concat('base-', $n)\"/></xsl:function>"));

            Assert.AreEqual(
                "<out>base-1</out>",
                RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"/>"
                        + "<xsl:template name=\"main\" visibility=\"public\">"
                        + "<out><xsl:value-of select=\"p:f(1)\"/></out></xsl:template>"),
                    library));
        }

        [TestMethod]
        public void AnOverrideReplacesAComponentAndCanStillReachIt()
        {
            // xsl:original is what makes an override a wrapper rather than a replacement, and it is resolved
            // against the template being compiled rather than by name — two overrides in one package must
            // not see each other's.
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base", "<xsl:template name=\"greet\" visibility=\"public\">"
                    + "<from-base/></xsl:template>"));

            Assert.AreEqual(
                "<out><wrapped><from-base/></wrapped></out>",
                RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"><xsl:override>"
                        + "<xsl:template name=\"greet\" visibility=\"public\"><wrapped>"
                        + "<xsl:call-template name=\"xsl:original\"/></wrapped></xsl:template>"
                        + "</xsl:override></xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\">"
                        + "<out><xsl:call-template name=\"greet\"/></out></xsl:template>"),
                    library));
        }

        [TestMethod]
        public void XslOriginalOutsideAnOverrideIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package("urn:one",
                    "<xsl:template name=\"main\" visibility=\"public\">"
                    + "<xsl:call-template name=\"xsl:original\"/></xsl:template>")));

            StringAssert.Contains(error.Message, "xsl:override");
        }

        [TestMethod]
        public void APackageNobodySuppliedIsReported()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(
                    Package("urn:main", "<xsl:use-package name=\"urn:missing\"/>"
                        + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"),
                    new PackageLibrary()));

            Assert.AreEqual("XTSE3000", error.Code);
        }

        [TestMethod]
        public void WithoutAPackageResolverAUsePackageIsRefused()
        {
            // The same posture as the other two resolvers: a stylesheet cannot reach what the caller has not
            // opted into.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package("urn:main", "<xsl:use-package name=\"urn:base\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));

            StringAssert.Contains(error.Message, "PackageResolver");
        }

        // ---- Shadow attributes -------------------------------------------------------------------------------

        [TestMethod]
        public void AShadowAttributeIsComputedWhileTheStylesheetIsRead()
        {
            // The ordinary attribute with an underscore in front. An AVT is evaluated when the instruction
            // runs; this one has to be settled before there is a transformation at all, which is what lets
            // it decide a template's name.
            Assert.AreEqual(
                "<out from=\"2.0.0\"/>",
                RunPackage(Package("urn:shadow",
                    "<xsl:param name=\"V\" static=\"yes\" select=\"'2.0.0'\"/>"
                    + "<xsl:param name=\"WHICH\" static=\"yes\" select=\"'alpha'\"/>"
                    + "<xsl:template _name=\"{$WHICH}\" visibility=\"public\">"
                    + "<out from=\"{$V}\"/></xsl:template>"
                    + "<xsl:template name=\"main\" visibility=\"public\">"
                    + "<xsl:call-template name=\"alpha\"/></xsl:template>")));
        }

        [TestMethod]
        public void AShadowAttributeIsAllowedExactlyWhereTheOrdinaryOneIs()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package("urn:shadow",
                    "<xsl:param name=\"V\" static=\"yes\" select=\"'x'\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\" _nonesuch=\"{$V}\"/>")));

            Assert.AreEqual("XTSE0090", error.Code);
        }

        // ---- A value that exists at every node ----------------------------------------------------------------

        /// <summary>Two items inside a group and one outside it, which is enough to show both phases.</summary>
        private const string Nested = "<r><g><item n=\"1\"/><item n=\"2\"/></g><item n=\"3\"/></r>";

        /// <summary>
        /// What lets the source document carry accumulators at all. A transformation that starts by applying
        /// templates makes its source document available through the mode it starts in, and a mode that says
        /// nothing about accumulators says none.
        /// </summary>
        private const string Applies = "<xsl:mode use-accumulators=\"#all\"/>";

        [TestMethod]
        public void AnAccumulatorCountsANodeAsThatNodeBegins()
        {
            // before and after are two different questions about one node, and neither is "everything
            // strictly before it": before is the value once the node's own start rule has fired, after is
            // the value once its descendants have been walked too. A leaf answers both alike, which is what
            // lets accumulator-before number the very figure it is called on.
            Assert.AreEqual(
                "<out><i n=\"1\" before=\"1\" after=\"1\"/><i n=\"2\" before=\"2\" after=\"2\"/>"
                + "<i n=\"3\" before=\"3\" after=\"3\"/></out>",
                Run(
                    Applies
                    + "<xsl:accumulator name=\"count\" as=\"xs:integer\" initial-value=\"0\">"
                    + "<xsl:accumulator-rule match=\"item\" select=\"$value + 1\"/></xsl:accumulator>"
                    + Root("<xsl:for-each select=\"//item\">"
                        + "<i n=\"{@n}\" before=\"{accumulator-before('count')}\" "
                        + "after=\"{accumulator-after('count')}\"/></xsl:for-each>"),
                    Nested,
                    "3.0"));
        }

        [TestMethod]
        public void AnEndPhaseRuleFiresWhenTheNodeCloses()
        {
            // Which is what makes a depth counter one accumulator rather than an axis: the start rule adds
            // on the way down and the end rule takes away on the way back up. An item under r and g is at
            // depth 3 counting itself, its own start rule being part of its pre-descent value.
            Assert.AreEqual(
                "<out><i n=\"1\" d=\"3\"/><i n=\"2\" d=\"3\"/><i n=\"3\" d=\"2\"/></out>",
                Run(
                    Applies
                    + "<xsl:accumulator name=\"depth\" as=\"xs:integer\" initial-value=\"0\">"
                    + "<xsl:accumulator-rule match=\"*\" select=\"$value + 1\"/>"
                    + "<xsl:accumulator-rule match=\"*\" phase=\"end\" select=\"$value - 1\"/>"
                    + "</xsl:accumulator>"
                    + Root("<xsl:for-each select=\"//item\">"
                        + "<i n=\"{@n}\" d=\"{accumulator-before('depth')}\"/></xsl:for-each>"),
                    Nested,
                    "3.0"));
        }

        [TestMethod]
        public void TheDocumentNodeSeesTheWholeDocument()
        {
            // accumulator-after at the root is the value once the walk has finished, which is what makes a
            // total a total.
            Assert.AreEqual(
                "<out>3</out>",
                Run(
                    Applies
                    + "<xsl:accumulator name=\"count\" as=\"xs:integer\" initial-value=\"0\">"
                    + "<xsl:accumulator-rule match=\"item\" select=\"$value + 1\"/></xsl:accumulator>"
                    + Root("<xsl:value-of select=\"accumulator-after('count')\"/>"),
                    Nested,
                    "3.0"));
        }

        [TestMethod]
        public void TheLastMatchingRuleOfAPhaseIsTheOneThatFires()
        {
            // Rules have no priority. When two of a phase match, the later one is taken to be the more
            // specific, which is the same tie-break templates use and here the only one.
            Assert.AreEqual(
                "<out>second</out>",
                Run(
                    Applies
                    + "<xsl:accumulator name=\"a\" initial-value=\"'none'\">"
                    + "<xsl:accumulator-rule match=\"item\" select=\"'first'\"/>"
                    + "<xsl:accumulator-rule match=\"item\" select=\"'second'\"/></xsl:accumulator>"
                    + Root("<xsl:value-of select=\"accumulator-after('a')\"/>"),
                    Nested,
                    "3.0"));
        }

        [TestMethod]
        public void AnAccumulatorNobodyDeclaredIsRefusedWhereItIsNamed()
        {
            // Resolved where the call is written rather than on whichever document first reaches it, which
            // is what makes a misspelt name a compile-time answer.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:value-of select=\"accumulator-before('nonesuch')\"/>"), Nested, "3.0"));

            Assert.AreEqual("XTDE3340", error.Code);
        }

        [TestMethod]
        public void TwoAccumulatorsOfOneNameAreRefused()
        {
            // Unlike a template there is no rule for choosing between them: the values would differ at every
            // node of every document.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:accumulator name=\"a\" initial-value=\"0\">"
                    + "<xsl:accumulator-rule match=\"item\" select=\"0\"/></xsl:accumulator>"
                    + "<xsl:accumulator name=\"a\" initial-value=\"1\">"
                    + "<xsl:accumulator-rule match=\"item\" select=\"1\"/></xsl:accumulator>" + Root(string.Empty),
                    Nested,
                    "3.0"));

            Assert.AreEqual("XTSE3350", error.Code);
        }

        [TestMethod]
        public void AnAccumulatorValueKeepsItsDeclaredType()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Applies
                    + "<xsl:accumulator name=\"a\" as=\"xs:integer\" initial-value=\"0\">"
                    + "<xsl:accumulator-rule match=\"item\" select=\"'text'\"/></xsl:accumulator>"
                    + Root("<xsl:value-of select=\"accumulator-after('a')\"/>"),
                    Nested,
                    "3.0"));

            // Not the XTTE0570 a variable would raise. 18.2.1 converts the value by the function
            // conversion rules and 18.2.4 gives the accumulator's delta the signature of a function
            // returning the declared type, so the code is the one a function call gives.
            Assert.AreEqual("XPTY0004", error.Code);
        }

        // ---- Copying a node away from where it came from ------------------------------------------------------

        /// <summary>Two elements with an attribute and a comment, so a copy has something to be faithful about.</summary>
        private const string Copied = "<r><c k=\"1\">one<!--note--></c><c k=\"2\">two</c></r>";

        [TestMethod]
        public void CopyOfKeepsTheContentAndChangesTheIdentity()
        {
            // The point of it is identity, not content: equal under deep-equal, never the same node under is.
            Assert.AreEqual(
                "<out equal=\"true\" same=\"false\"/>",
                Run(
                    Root2("<out equal=\"{deep-equal((//c)[1], copy-of((//c)[1]))}\" "
                        + "same=\"{(//c)[1] is copy-of((//c)[1])}\"/>"),
                    Copied,
                    "3.0"));
        }

        [TestMethod]
        public void ACopyHasNoProvenance()
        {
            // Which is what the function is for: a copy has no parent, no siblings and no document to be
            // found in, so nothing downstream can navigate out of it into the tree it came from.
            Assert.AreEqual(
                "<out>false</out>",
                Run(
                    Root("<xsl:value-of select=\"exists(copy-of((//c)[1])/parent::*)\"/>"),
                    Copied,
                    "3.0"));
        }

        [TestMethod]
        public void ACopyIsFaithfulToAttributesAndComments()
        {
            Assert.AreEqual(
                "<out><c k=\"1\">one<!--note--></c></out>",
                Run(Root("<xsl:copy-of select=\"copy-of((//c)[1])\"/>"), Copied, "3.0"));
        }

        [TestMethod]
        public void CopyOfMakesOneCopyPerItem()
        {
            // Two text nodes copied stay two: a copy is made per item, and merging them would give a
            // sequence one item shorter than the argument.
            Assert.AreEqual(
                "<out>2</out>",
                Run(Root("<xsl:value-of select=\"count(copy-of(//c/text()))\"/>"), Copied, "3.0"));
        }

        [TestMethod]
        public void AnAtomicValueHasNoIdentityToChange()
        {
            Assert.AreEqual(
                "<out>17</out>",
                Run(Root("<xsl:value-of select=\"copy-of(17)\"/>"), Copied, "3.0"));
        }

        [TestMethod]
        public void CopyOfWithNoArgumentCopiesTheContextItem()
        {
            Assert.AreEqual(
                "<out>true</out>",
                Run(Root("<xsl:value-of select=\"deep-equal(., copy-of())\"/>"), Copied, "3.0"));
        }

        [TestMethod]
        public void CopyOfWithNoContextItemIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\" xmlns:my=\"urn:my\"><out>"
                    + "<xsl:value-of select=\"my:f()\"/></out></xsl:template>"
                    + "<xsl:function name=\"my:f\" xmlns:my=\"urn:my\">"
                    + "<xsl:sequence select=\"copy-of()\"/></xsl:function>",
                    Copied,
                    "3.0"));

            Assert.AreEqual("XPDY0002", error.Code);
        }

        // ---- A rule that matches something which is not a node -------------------------------------------------

        /// <summary>Rules for three kinds of item and for an element, all in one mode.</summary>
        private const string ItemRules =
            "<xsl:template match=\".[. instance of xs:integer]\" mode=\"m\"><int v=\"{.}\"/></xsl:template>"
            + "<xsl:template match=\".[. instance of xs:string]\" mode=\"m\"><str v=\"{.}\"/></xsl:template>"
            + "<xsl:template match=\".[. instance of array(*)]\" mode=\"m\">"
            + "<arr n=\"{array:size(.)}\"/></xsl:template>"
            + "<xsl:template match=\"c\" mode=\"m\"><elem k=\"{@k}\"/></xsl:template>";

        [TestMethod]
        public void APredicatePatternMatchesAnItemRatherThanANode()
        {
            // Every other pattern names an axis and a node test, so it can only ever match a node. This one
            // asks about the item itself, which is what lets apply-templates be used over a sequence that
            // is not a tree.
            Assert.AreEqual(
                "<out><int v=\"1\"/><str v=\"two\"/><arr n=\"2\"/></out>",
                Run(
                    ItemRules + Root("<xsl:apply-templates select=\"(1, 'two', [3, 4])\" mode=\"m\"/>"),
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void NodesAndItemsCanBeMatchedInOneSequence()
        {
            // The two kinds of rule are not two dispatches: a node is an item, so an ordinary pattern and a
            // predicate pattern compete for the same sequence in the ordinary way.
            Assert.AreEqual(
                "<out><int v=\"1\"/><elem k=\"1\"/><elem k=\"2\"/></out>",
                Run(
                    ItemRules + Root("<xsl:apply-templates select=\"(1, //c)\" mode=\"m\"/>"),
                    "<r><c k=\"1\"/><c k=\"2\"/></r>",
                    "3.0"));
        }

        [TestMethod]
        public void AnItemNoRuleMatchesIsWrittenAsItself()
        {
            // The built-in rule for an atomic value, which is the same answer a text node gets and for the
            // same reason: there is nothing else to usefully do with a value it was handed.
            Assert.AreEqual(
                "<out>9x</out>",
                Run(Root("<xsl:apply-templates select=\"(9, 'x')\" mode=\"n\"/>"), "<r/>", "3.0"));
        }

        [TestMethod]
        public void APredicatePatternAlsoMatchesANode()
        {
            Assert.AreEqual(
                "<out><any/><any/></out>",
                Run(
                    "<xsl:template match=\".[self::c]\" mode=\"m\"><any/></xsl:template>"
                    + Root("<xsl:apply-templates select=\"//c\" mode=\"m\"/>"),
                    "<r><c/><c/></r>",
                    "3.0"));
        }

        [TestMethod]
        public void ADotWithNoPredicateMatchesEveryItem()
        {
            // The predicate list may be empty, and the grammar means it. The specification's own worked
            // implementation of fn:snapshot writes exactly this to copy anything at all, so a bare '.' is a
            // stylesheet saying "whatever this is" rather than a stylesheet saying nothing.
            Assert.AreEqual(
                "<out>[1][a][x]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"(1, 'a', /r/i)\" mode=\"m\"/>")
                    + "<xsl:template match=\".\" mode=\"m\">[<xsl:value-of select=\".\"/>]</xsl:template>",
                    "<r><i>x</i></r>",
                    "3.0"));
        }

        [TestMethod]
        public void ASequenceOfItemsIsStillRefusedBeforeThirty()
        {
            // apply-templates over an atomic value means nothing where no pattern could match one, so a 2.0
            // stylesheet gets the type error it always did.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:apply-templates select=\"(1, 2)\"/>"), "<r/>"));

            Assert.AreEqual("XTTE0520", error.Code);
        }

        // ---- Keeping just enough of where a node came from -----------------------------------------------------

        /// <summary>A node two levels deep with a sibling, so pruning has something to prune.</summary>
        private const string Nested2 = "<r a=\"top\"><g><c k=\"1\">one</c><c k=\"2\">two</c></g></r>";

        [TestMethod]
        public void ASnapshotKeepsItsAncestorsAndDropsTheirOtherChildren()
        {
            // The whole difference from copy-of. A snapshot of one row of a large table is that row and its
            // context, not the table: the ancestors come, with their attributes, and the siblings do not.
            Assert.AreEqual(
                "<out><r a=\"top\"><g><c k=\"1\">one</c></g></r></out>",
                Run(
                    Root("<xsl:sequence select=\"snapshot(//c[1])/ancestor::node()[last()]\"/>"),
                    Nested2,
                    "3.0"));
        }

        [TestMethod]
        public void ASnapshotIsStillNavigableUpwards()
        {
            Assert.AreEqual(
                "<out>g</out>",
                Run(Root("<xsl:value-of select=\"name(snapshot(//c[1])/..)\"/>"), Nested2, "3.0"));
        }

        [TestMethod]
        public void TheDocumentElementKeepsItsDocumentNode()
        {
            // The case that caught an off-by-one: with no element ancestors the copy is still one level
            // below the root, not the root itself.
            Assert.AreEqual(
                "<out>true|true</out>",
                Run(
                    Root("<xsl:value-of select=\"deep-equal(/*, snapshot(/*))\"/>|"
                        + "<xsl:value-of select=\"exists(snapshot(/*)/..)\"/>"),
                    Nested2,
                    "3.0"));
        }

        [TestMethod]
        public void ASnapshotOfTheDocumentNodeIsTheWholeDocument()
        {
            // The one case where the copy is the built root itself: copying a document node writes its
            // children into the root that is already there rather than opening anything of its own.
            Assert.AreEqual(
                "<out>true</out>",
                Run(Root("<xsl:value-of select=\"deep-equal(/, snapshot(/))\"/>"), Nested2, "3.0"));
        }

        [TestMethod]
        public void AnAttributeSnapshotHangsFromItsOwnElement()
        {
            Assert.AreEqual(
                "<out>1|c</out>",
                Run(
                    Root("<xsl:value-of select=\"snapshot(//c[1]/@k)\"/>|"
                        + "<xsl:value-of select=\"name(snapshot(//c[1]/@k)/..)\"/>"),
                    Nested2,
                    "3.0"));
        }

        [TestMethod]
        public void ASnapshotIsStillACopy()
        {
            Assert.AreEqual(
                "<out>false</out>",
                Run(Root("<xsl:value-of select=\"(//c)[1] is snapshot((//c)[1])\"/>"), Nested2, "3.0"));
        }

        [TestMethod]
        public void SnapshotPassesEverythingThatIsNotANodeThrough()
        {
            Assert.AreEqual(
                "<out>4</out>",
                Run(Root("<xsl:value-of select=\"count(snapshot((1, 'x', //c)))\"/>"), Nested2, "3.0"));
        }

        // ---- Carrying on down the list of rules that matched an item -------------------------------------

        [TestMethod]
        public void NextMatchWorksWhereTheContextItemIsNotANode()
        {
            // A predicate pattern matches an atomic value, so a rule that matched one can contain an
            // xsl:next-match. Sending that through the node-keyed search asked the tree for the kind of a
            // node that was not there, which crashed rather than answered.
            Assert.AreEqual(
                "<out><first><second>17</second></first></out>",
                Run(
                    Root("<xsl:apply-templates select=\"17\"/>")
                    + "<xsl:template match=\".[. instance of xs:integer]\" priority=\"2\""
                    + ">"
                    + "<first><xsl:next-match/></first></xsl:template>"
                    + "<xsl:template match=\".[. instance of xs:integer]\" priority=\"1\""
                    + ">"
                    + "<second><xsl:next-match/></second></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void APredicatePatternHasDefaultPriorityOne()
        {
            // Not the 0.5 a node test with a predicate gets. A predicate pattern says nothing about a name or
            // a kind and everything about the item, so it sits above the patterns that only narrowed a kind.
            // The middle rule here declares no priority and still comes between 1.0000001 and 0.999999.
            Assert.AreEqual(
                "<out><first><second><third>17</third></second></first></out>",
                Run(
                    Root("<xsl:apply-templates select=\"17\"/>")
                    + "<xsl:template match=\".[. instance of xs:integer]\" priority=\"0.999999\""
                    + ">"
                    + "<third><xsl:next-match/></third></xsl:template>"
                    + "<xsl:template match=\".[. instance of xs:integer]\" priority=\"1.0000001\""
                    + ">"
                    + "<first><xsl:next-match/></first></xsl:template>"
                    + "<xsl:template match=\".[. instance of xs:integer][. gt 0]\""
                    + ">"
                    + "<second><xsl:next-match/></second></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void ANumericPredicateInAPatternSelectsByPosition()
        {
            // The same rule an ordinary predicate follows. A pattern is asked about one item at a time, so
            // the position is 1 — and '.[2]' therefore matches nothing, where reading 2 as true would have
            // matched everything.
            Assert.AreEqual(
                "<out><one>17</one></out>",
                Run(
                    Root("<xsl:apply-templates select=\"17\"/>")
                    + "<xsl:template match=\".[. instance of xs:integer][2]\" priority=\"10\""
                    + ">"
                    + "<two><xsl:next-match/></two></xsl:template>"
                    + "<xsl:template match=\".[. instance of xs:integer][1]\" priority=\"5\""
                    + ">"
                    + "<one><xsl:next-match/></one></xsl:template>",
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void NextMatchWithNoContextItemIsRefused()
        {
            // The specification names the missing context item in the same error as the missing template
            // rule, and for the same reason: the instruction means "carry on down the list of rules that
            // matched this", and with nothing in focus there is no this.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Root("<xsl:apply-templates select=\"17\"/>")
                    + "<xsl:template match=\".[. instance of xs:integer]\""
                    + ">"
                    + "<xsl:call-template name=\"deeper\"/></xsl:template>"
                    + "<xsl:template name=\"deeper\">"
                    + "<xsl:context-item use=\"absent\"/><xsl:next-match/></xsl:template>",
                    "<r/>",
                    "3.0"));

            Assert.AreEqual("XTDE0560", error.Code);
            StringAssert.Contains(error.Message, "no context item");
        }

        // ---- A package that declares the modes it uses ---------------------------------------------------

        /// <summary>Wraps declarations in a package, with attributes of its own on the package element.</summary>
        private static string PackageWith(string attributes, string body)
        {
            return $"<xsl:package name=\"urn:one\" package-version=\"1.0.0\" version=\"3.0\" {attributes} "
                + "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">" + body + "</xsl:package>";
        }

        [TestMethod]
        [DataRow("#all")]
        [DataRow("#unnamed")]
        [DataRow("#default")]
        [DataRow(null)]
        public void AnOverridingRuleMustNameAModeThatCanBeOverridden(string? mode)
        {
            // A rule inside an override is redefining part of a mode, and a mode is a component only where it
            // has a name. '#all' names every mode and so no one component; the unnamed mode is never a
            // component; and '#default', or saying nothing, comes to the unnamed mode unless a default-mode
            // in scope says otherwise.
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base", "<xsl:mode name=\"m\" visibility=\"public\"/>"
                    + "<xsl:template match=\"a\" mode=\"m\"><base/></xsl:template>"));

            string written = mode is null ? string.Empty : $" mode=\"{mode}\"";

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(
                    PackageWith(
                        "declared-modes=\"no\"",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"><xsl:override>"
                        + $"<xsl:template match=\"a\"{written}><over/></xsl:template>"
                        + "</xsl:override></xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"),
                    library));

            Assert.AreEqual("XTSE3440", error.Code);
        }

        [TestMethod]
        public void ADefaultModeMakesTheSameRuleGood()
        {
            // The fourth clause is what makes this worth checking rather than obvious: written under a
            // default-mode that names a mode, the rule with no mode attribute is naming that one.
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base", "<xsl:mode name=\"m\" visibility=\"public\"/>"
                    + "<xsl:template match=\"a\" mode=\"m\"><base/></xsl:template>"));

            Assert.AreEqual(
                "<ok/>",
                RunPackage(
                    PackageWith(
                        "declared-modes=\"no\" default-mode=\"m\"",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"><xsl:override>"
                        + "<xsl:template match=\"a\"><over/></xsl:template>"
                        + "</xsl:override></xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"),
                    library));
        }

        [TestMethod]
        public void AModeUsedInAPackageMustBeDeclaredUnlessThePackageSaysOtherwise()
        {
            // The point of declaring them is that a misspelt mode name is otherwise a new mode nothing
            // reaches rather than a mistake, which is exactly the kind of error a package boundary exists
            // to catch.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(PackageWith(
                    string.Empty,
                    "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"
                    + "<xsl:template match=\"x\" mode=\"undeclared\"/>")));

            Assert.AreEqual("XTSE3085", error.Code);
            StringAssert.Contains(error.Message, "'undeclared'");
        }

        [TestMethod]
        public void DeclaringItLetsTheModeThrough()
        {
            Assert.AreEqual(
                "<ok/>",
                RunPackage(PackageWith(
                    string.Empty,
                    "<xsl:mode name=\"declared\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"
                    + "<xsl:template match=\"x\" mode=\"declared\"/>")));
        }

        [TestMethod]
        public void SoDoesSayingThePackageDoesNotDeclareThem()
        {
            // And the value is a boolean, so it takes whitespace around it like any attribute of its type.
            Assert.AreEqual(
                "<ok/>",
                RunPackage(PackageWith(
                    "declared-modes=' no '",
                    "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"
                    + "<xsl:template match=\"x\" mode=\"undeclared\"/>")));
        }

        [TestMethod]
        public void TheUnnamedModeCountsToo()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(PackageWith(
                    string.Empty,
                    "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"
                    + "<xsl:template match=\"x\"/>")));

            Assert.AreEqual("XTSE3085", error.Code);
        }

        [TestMethod]
        public void ANamedTemplateBelongsToNoModeAndSoNeedsNone()
        {
            // Which is what makes the check about 'match' rather than about xsl:template. A package whose
            // rules are all in named modes still has named templates, and those say nothing about modes.
            Assert.AreEqual(
                "<ok/>",
                RunPackage(PackageWith(
                    string.Empty,
                    "<xsl:mode name=\"m\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"
                    + "<xsl:template match=\"x\" mode=\"m\"/>")));
        }

        [TestMethod]
        public void AnUnnamedXslModeDeclaresTheUnnamedModeEvenUnderADefaultMode()
        {
            // xsl:mode with no name says which mode it declares in as many words. Letting a default-mode in
            // scope redirect it would mean a package that set one could not declare the unnamed mode at all,
            // which is what the rule for x below needs.
            Assert.AreEqual(
                "<ok/>",
                RunPackage(PackageWith(
                    "default-mode=\"a\"",
                    "<xsl:mode name=\"a\"/><xsl:mode/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"
                    + "<xsl:template match=\"x\" mode=\"#unnamed\"/>")));
        }

        // ---- xsl:expose and xsl:accept -------------------------------------------------------------------

        [TestMethod]
        public void AnExposeGivesAVisibilityToEveryComponentItNames()
        {
            // The point of the wildcard form: a package says once what it offers, rather than repeating a
            // visibility attribute on every declaration.
            Assert.AreEqual(
                "<ok/>",
                RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"template\" names=\"*\" visibility=\"public\"/>"
                    + "<xsl:template name=\"main\"><ok/></xsl:template>")));
        }

        [TestMethod]
        public void AnExposeNamingNoComponentIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"variable\" names=\"nowhere\" visibility=\"public\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));

            Assert.AreEqual("XTSE3020", error.Code);
        }

        [TestMethod]
        public void AFunctionIsNamedByItsArityAsWellAsItsName()
        {
            // Erratum E36. Two functions of one name and different arities are two components, so a name
            // without an arity does not identify one of them.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"function\" names=\"p:f\" visibility=\"public\"/>"
                    + "<xsl:function name=\"p:f\"><xsl:sequence select=\"1\"/></xsl:function>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));

            Assert.AreEqual("XTSE3020", error.Code);

            // The same name with the arity finds it.
            Assert.AreEqual(
                "<ok/>",
                RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"function\" names=\"p:f#0\" visibility=\"public\"/>"
                    + "<xsl:function name=\"p:f\"><xsl:sequence select=\"1\"/></xsl:function>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));
        }

        [TestMethod]
        public void OnlyAFunctionHasAnArityToBeNamedBy()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"template\" names=\"main#0\" visibility=\"public\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));

            Assert.AreEqual("XTSE3020", error.Code);
        }

        [TestMethod]
        public void AnExposeMayWithdrawWhatADeclarationOffersAndNotAddToIt()
        {
            // Naming a component outright and asking for more than its declaration gives is a
            // contradiction; asking for less is how a package keeps something in.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"variable\" names=\"v\" visibility=\"public\"/>"
                    + "<xsl:variable name=\"v\" select=\"1\" visibility=\"private\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));

            Assert.AreEqual("XTSE3010", error.Code);

            Assert.AreEqual(
                "<ok/>",
                RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"variable\" names=\"v\" visibility=\"private\"/>"
                    + "<xsl:variable name=\"v\" select=\"1\" visibility=\"public\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));
        }

        [TestMethod]
        public void AWildcardExposeYieldsToADeclarationThatSaidSomething()
        {
            // The ordinary way a library package is written: one line saying "everything is public", over a
            // template it deliberately keeps to itself. Reading that as a contradiction would refuse it.
            Assert.AreEqual(
                "<ok/>",
                RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"template\" names=\"*\" visibility=\"public\"/>"
                    + "<xsl:template name=\"hidden\" visibility=\"private\"><no/></xsl:template>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));
        }

        [TestMethod]
        public void AnExposeCannotMakeAComponentAbstract()
        {
            // Abstract says the declaration has no body for a using package to supply, which is a fact
            // about the declaration rather than about who may see it.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"template\" names=\"t\" visibility=\"abstract\"/>"
                    + "<xsl:template name=\"t\"><no/></xsl:template>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));

            Assert.AreEqual("XTSE3025", error.Code);
        }

        [TestMethod]
        public void EveryKindOfComponentAtOnceCanOnlyBeNamedByAWildcard()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"*\" names=\"main\" visibility=\"public\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));

            Assert.AreEqual("XTSE3022", error.Code);
        }

        [TestMethod]
        public void TheUnnamedModeIsNotAComponentAPackageCanOffer()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(Package(
                    "urn:one",
                    "<xsl:expose component=\"mode\" names=\"#unnamed\" visibility=\"public\"/>"
                    + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>")));

            Assert.AreEqual("XTSE0020", error.Code);
        }

        [TestMethod]
        public void AnExposeIsAboutItsOwnPackageAndNoOther()
        {
            // A used package saying everything of its own is public must not reach into the package that
            // used it and make a private template there an entry point.
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base", "<xsl:expose component=\"template\" names=\"*\" visibility=\"public\"/>"
                    + "<xsl:template name=\"greet\"><from-base/></xsl:template>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"/>"
                        + "<xsl:template name=\"main\"><ok/></xsl:template>"),
                    library));

            Assert.AreEqual("XTDE0040", error.Code);
        }

        [TestMethod]
        public void AnAcceptNamingAComponentTheUsedPackageHasNotGotIsRefused()
        {
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base", "<xsl:variable name=\"v\" select=\"1\" visibility=\"public\"/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\">"
                        + "<xsl:accept component=\"variable\" names=\"nowhere\" visibility=\"private\"/>"
                        + "</xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"),
                    library));

            Assert.AreEqual("XTSE3030", error.Code);
        }

        [TestMethod]
        public void AnAcceptCannotTakeMoreThanThePackageOffered()
        {
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base", "<xsl:variable name=\"v\" select=\"1\" visibility=\"final\"/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\">"
                        + "<xsl:accept component=\"variable\" names=\"v\" visibility=\"public\"/>"
                        + "</xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"),
                    library));

            Assert.AreEqual("XTSE3040", error.Code);

            // Wanting less of it than was offered is the ordinary case.
            Assert.AreEqual(
                "<ok/>",
                RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\">"
                        + "<xsl:accept component=\"variable\" names=\"v\" visibility=\"private\"/>"
                        + "</xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"),
                    library));
        }

        [TestMethod]
        public void AnAcceptSayingEveryKindAtOnceCanOnlyNameAWildcard()
        {
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base", "<xsl:variable name=\"v\" select=\"1\" visibility=\"public\"/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\">"
                        + "<xsl:accept component=\"*\" names=\"v\" visibility=\"private\"/>"
                        + "</xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"),
                    library));

            Assert.AreEqual("XTSE3032", error.Code);
        }

        [TestMethod]
        public void AnAbstractComponentNothingReadsIsNotAnError()
        {
            // The whole point of declaring one: a package names a component it does not define, and a using
            // package that never reaches it need not supply it. Forcing the globals up front made this fail
            // before anything ran.
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base",
                    "<xsl:variable name=\"v\" as=\"xs:integer\" visibility=\"abstract\"/>"
                    + "<xsl:variable name=\"seen\" as=\"xs:integer\" visibility=\"public\" select=\"$v\"/>"));

            Assert.AreEqual(
                "<ok/>",
                RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\">"
                        + "<xsl:accept component=\"variable\" names=\"v\" visibility=\"hidden\"/>"
                        + "</xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\"><ok/></xsl:template>"),
                    library));
        }

        [TestMethod]
        public void ReadingAnAbstractComponentNobodySuppliedIsReported()
        {
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base",
                    "<xsl:variable name=\"v\" as=\"xs:integer\" visibility=\"abstract\"/>"
                    + "<xsl:variable name=\"seen\" as=\"xs:integer\" visibility=\"public\" select=\"$v\"/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\">"
                        + "<xsl:accept component=\"variable\" names=\"v\" visibility=\"hidden\"/>"
                        + "</xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\">"
                        + "<out><xsl:value-of select=\"$seen\"/></out></xsl:template>"),
                    library));

            Assert.AreEqual("XTDE3052", error.Code);
        }

        [TestMethod]
        public void CallingAnAbstractTemplateOrFunctionIsReported()
        {
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base",
                    "<xsl:template name=\"t\" visibility=\"abstract\"/>"
                    + "<xsl:function name=\"p:f\" as=\"xs:integer\" visibility=\"abstract\"/>"));

            foreach (string body in new[]
            {
                "<xsl:call-template name=\"t\"/>",
                "<xsl:value-of select=\"p:f()\"/>",
            })
            {
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => RunPackage(
                        Package("urn:main",
                            "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"/>"
                            + "<xsl:template name=\"main\" visibility=\"public\"><out>"
                            + body + "</out></xsl:template>"),
                        library));

                Assert.AreEqual("XTDE3052", error.Code, body);
            }
        }

        [TestMethod]
        public void APrivateComponentOfAUsedPackageDoesNotExistFromOutsideIt()
        {
            // The whole of what visibility is for: a library's private template, function, variable and
            // attribute set are its own business, and a package using it that names one is naming
            // something that, from where it stands, is not there - in whichever code that kind of
            // reference has for an unknown name.
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base",
                    "<xsl:template name=\"t\"><t/></xsl:template>"
                    + "<xsl:function name=\"p:f\" as=\"xs:integer\"><xsl:sequence select=\"1\"/></xsl:function>"
                    + "<xsl:variable name=\"v\" select=\"1\"/>"
                    + "<xsl:attribute-set name=\"a\"><xsl:attribute name=\"A\" select=\"1\"/></xsl:attribute-set>"));

            foreach ((string body, string code) in new[]
            {
                ("<xsl:call-template name=\"t\"/>", "XTSE0650"),
                ("<xsl:value-of select=\"p:f()\"/>", "XPST0017"),
                ("<xsl:value-of select=\"$v\"/>", "XPST0008"),
                ("<x xsl:use-attribute-sets=\"a\"/>", "XTSE0710"),
            })
            {
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => RunPackage(
                        Package("urn:main",
                            "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"/>"
                            + "<xsl:template name=\"main\" visibility=\"public\"><out>"
                            + body + "</out></xsl:template>"),
                        library));

                Assert.AreEqual(code, error.Code, body);
            }

            // Offered, it is there - and taken as private by default, so a package using the one that took
            // it would not see it in turn.
            PackageLibrary offering = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base",
                    "<xsl:template name=\"t\" visibility=\"public\"><t/></xsl:template>"));

            Assert.AreEqual(
                "<out><t/></out>",
                RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"/>"
                        + "<xsl:template name=\"main\" visibility=\"public\"><out>"
                        + "<xsl:call-template name=\"t\"/></out></xsl:template>"),
                    offering));
        }

        [TestMethod]
        public void TwoComponentsOfOneNameInViewAreRefusedUnlessOneIsHiddenOrOverridden()
        {
            // XTSE3050: a reference to the name would have two things it could mean. An xsl:accept hiding
            // one settles it, and so does an xsl:override, which replaces the accepted one.
            PackageLibrary library = new PackageLibrary()
                .Add("urn:one", Package("urn:one", "<xsl:template name=\"t\" visibility=\"public\"><one/></xsl:template>"))
                .Add("urn:two", Package("urn:two", "<xsl:template name=\"t\" visibility=\"public\"><two/></xsl:template>"));

            const string Main =
                "<xsl:template name=\"main\" visibility=\"public\"><out><xsl:call-template name=\"t\"/></out>"
                + "</xsl:template>";

            Assert.AreEqual(
                "XTSE3050",
                Assert.ThrowsExactly<XsltException>(() => RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:one\" package-version=\"1.0.0\"/>"
                        + "<xsl:use-package name=\"urn:two\" package-version=\"1.0.0\"/>" + Main),
                    library)).Code);

            Assert.AreEqual(
                "<out><two/></out>",
                RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:one\" package-version=\"1.0.0\">"
                        + "<xsl:accept component=\"template\" names=\"t\" visibility=\"hidden\"/>"
                        + "</xsl:use-package>"
                        + "<xsl:use-package name=\"urn:two\" package-version=\"1.0.0\"/>" + Main),
                    library));

            // Declaring the name itself, outside an xsl:override, is the same two-things-it-could-mean.
            Assert.AreEqual(
                "XTSE3050",
                Assert.ThrowsExactly<XsltException>(() => RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:one\" package-version=\"1.0.0\"/>"
                        + "<xsl:template name=\"t\"><mine/></xsl:template>" + Main),
                    library)).Code);
        }

        [TestMethod]
        public void AnOverrideHasToReplaceSomethingThePackageOffersForOverriding()
        {
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base",
                    "<xsl:template name=\"kept\"><base/></xsl:template>"
                    + "<xsl:template name=\"fixed\" visibility=\"final\"><base/></xsl:template>"
                    + "<xsl:template name=\"open\" visibility=\"public\"><base/></xsl:template>"));

            string Overriding(string name) => Package("urn:main",
                "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"><xsl:override>"
                + $"<xsl:template name=\"{name}\"><over/></xsl:template></xsl:override></xsl:use-package>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out>"
                + $"<xsl:call-template name=\"{name}\"/></out></xsl:template>");

            // Nothing of that name to replace: XTSE3058. Something kept private, or final: XTSE3060.
            Assert.AreEqual(
                "XTSE3058",
                Assert.ThrowsExactly<XsltException>(() => RunPackage(Overriding("nonesuch"), library)).Code);

            Assert.AreEqual(
                "XTSE3060",
                Assert.ThrowsExactly<XsltException>(() => RunPackage(Overriding("kept"), library)).Code);

            Assert.AreEqual(
                "XTSE3060",
                Assert.ThrowsExactly<XsltException>(() => RunPackage(Overriding("fixed"), library)).Code);

            Assert.AreEqual("<out><over/></out>", RunPackage(Overriding("open"), library));
        }

        [TestMethod]
        public void AnAbstractComponentTakenAsAbstractIsAStaticErrorAndTakenAsHiddenIsNotThere()
        {
            // Three things a package can do with a library's abstract function it does not supply. Say
            // nothing, and the function is absent: there to be named, an error to reach. Say "abstract",
            // and a package meant to be run now holds an abstract component with a reference to it, which
            // is XTSE3080 before anything runs. Say "hidden", and from the using package it does not exist.
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base",
                    "<xsl:function name=\"p:f\" as=\"xs:integer\" visibility=\"abstract\"/>"
                    + "<xsl:template name=\"t\" visibility=\"public\"><xsl:value-of select=\"p:f()\"/>"
                    + "</xsl:template>"));

            string Using(string accept) => Package("urn:main",
                "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\">" + accept + "</xsl:use-package>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out><xsl:call-template name=\"t\"/>"
                + "</out></xsl:template>");

            Assert.AreEqual(
                "XTDE3052",
                Assert.ThrowsExactly<XsltException>(() => RunPackage(Using(string.Empty), library)).Code);

            Assert.AreEqual(
                "XTSE3080",
                Assert.ThrowsExactly<XsltException>(() => RunPackage(
                    Using("<xsl:accept component=\"function\" names=\"p:f#0\" visibility=\"abstract\"/>"),
                    library)).Code);

            Assert.AreEqual(
                "XTDE3052",
                Assert.ThrowsExactly<XsltException>(() => RunPackage(
                    Using("<xsl:accept component=\"function\" names=\"p:f#0\" visibility=\"hidden\"/>"),
                    library)).Code);
        }

        [TestMethod]
        public void UsingAnAbstractAttributeSetIsReportedAndSupplyingOneIsNot()
        {
            PackageLibrary library = new PackageLibrary().Add(
                "urn:base",
                Package("urn:base",
                    "<xsl:attribute-set name=\"a\" visibility=\"abstract\"/>"
                    + "<xsl:attribute-set name=\"a-proxy\" use-attribute-sets=\"a\" visibility=\"public\"/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"/>"
                        + "<xsl:template name=\"main\" visibility=\"public\">"
                        + "<out xsl:use-attribute-sets=\"a-proxy\"/></xsl:template>"),
                    library));

            Assert.AreEqual("XTDE3052", error.Code);

            // Supplied by an override, the set is defined and the abstract declaration contributes nothing.
            Assert.AreEqual(
                "<out A=\"1\"/>",
                RunPackage(
                    Package("urn:main",
                        "<xsl:use-package name=\"urn:base\" package-version=\"1.0.0\"><xsl:override>"
                        + "<xsl:attribute-set name=\"a\"><xsl:attribute name=\"A\" select=\"1\"/>"
                        + "</xsl:attribute-set></xsl:override></xsl:use-package>"
                        + "<xsl:template name=\"main\" visibility=\"public\">"
                        + "<out xsl:use-attribute-sets=\"a-proxy\"/></xsl:template>"),
                    library));
        }

        [TestMethod]
        public void OnEmptyAsksAboutAnySequenceConstructor()
        {
            // The body of an xsl:for-each, an xsl:try or an xsl:iterate is a sequence constructor as much as
            // an element's content is, so an xsl:on-empty in any of them answers for that body — here, per
            // item of the for-each, which is the suite's on-empty-010.
            Assert.AreEqual(
                "<out>apple apple</out>",
                Run(
                    Root("<xsl:for-each select=\"/r/i\"><xsl:copy-of select=\"banana\"/>"
                        + "<xsl:on-empty select=\"'apple'\"/></xsl:for-each>"),
                    "<r><i/><i/></r>",
                    "3.0"));

            Assert.AreEqual(
                "<out>none</out>",
                Run(
                    Root("<xsl:try><xsl:copy-of select=\"/r/banana\"/><xsl:on-empty select=\"'none'\"/>"
                        + "<xsl:catch/></xsl:try>"),
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void AnEmptyDocumentIsEmptyAndANamespaceIsNot()
        {
            // A document node stands for its children, so a parameter whose content copied a node that was
            // not there is nothing — the suite's on-empty-012. A namespace written beside the xsl:on-empty is
            // an item of the sequence, and a sequence with an item in it is not empty: on-empty-107.
            Assert.AreEqual(
                "<out>apple</out>",
                Run(
                    "<xsl:param name=\"banana\"><xsl:copy-of select=\"/banana\"/></xsl:param>"
                    + Root("<xsl:sequence select=\"$banana\"/><xsl:on-empty select=\"'apple'\"/>"),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual(
                "<out xmlns:t=\"urn:t\"/>",
                Run(
                    Root("<xsl:namespace name=\"t\">urn:t</xsl:namespace><xsl:on-empty select=\"42\"/>"),
                    "<r/>",
                    "3.0"));
        }

        [TestMethod]
        public void WhatMadeTheSequenceEmptyIsNotWrittenBesideTheReplacement()
        {
            // Two zero-length strings make the constructor empty; the xsl:on-empty replaces the whole of it,
            // and the strings are not written too. The two replacements are adjacent atomic values and get
            // the one space that says so — with the strings written beside them there would be three.
            Assert.AreEqual(
                "<out>| |</out>",
                Run(
                    Root("<xsl:for-each select=\"1 to 2\"><xsl:sequence select=\"''\"/>"
                        + "<xsl:value-of select=\"''\"/><xsl:on-empty select=\"'|'\"/></xsl:for-each>"),
                    "<r/>",
                    "3.0"));

            Assert.AreEqual(
                "<out>21 23</out>",
                Run(Root("<xsl:copy-of select=\"/comment()\"/><xsl:on-empty select=\"21, 23\"/>"), "<r/>", "3.0"));
        }
        // ---- A union in a match, which is several rules and not one ------------------------------------

        [TestMethod]
        public void AUnionInAMatchIsOneRulePerAlternative()
        {
            // Each alternative takes the default priority of its own shape, so element(x) outranks *:x and
            // an xsl:next-match goes from the one to the other — through two alternatives of one template
            // before it reaches the next template at all (XSLT 3.0 §6.4).
            Assert.AreEqual(
                "<out><foo/><foo/><bar/></out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/x\"/>")
                    + "<xsl:template match=\"*:x|element(x)\"><foo/><xsl:next-match/></xsl:template>"
                    + "<xsl:template match=\"node()\"><bar/></xsl:template>",
                    "<r><x/></r>"));

            // And the ordering is by priority and not by which alternative was written first: element(x)
            // scores 0 against node()'s -0.5, so the rule holding both is entered through element(x), and
            // the separate *:x rule at -0.25 is what comes next.
            Assert.AreEqual(
                "<out><foo/><bar/></out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/x\"/>")
                    + "<xsl:template match=\"node()|element(x)\"><foo/><xsl:next-match/></xsl:template>"
                    + "<xsl:template match=\"*:x\"><bar/></xsl:template>",
                    "<r><x/></r>"));
        }

        [TestMethod]
        public void AWrittenPriorityMakesTheAlternativesOneRuleAgain()
        {
            // The alternatives are separate rules so that each can take its own default priority. Where the
            // template writes a priority they all have it, there is nothing left to tell them apart, and
            // xsl:next-match passes over the whole of it rather than entering the same body twice.
            Assert.AreEqual(
                "<out><foo/><bar/></out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/x\"/>")
                    + "<xsl:template match=\"*:x|element(x)\" priority=\"1\">"
                    + "<foo/><xsl:next-match/></xsl:template>"
                    + "<xsl:template match=\"node()\"><bar/></xsl:template>",
                    "<r><x/></r>"));
        }

        [TestMethod]
        public void AKindTestScoresByHowMuchItSays()
        {
            // element(x) names one of the two things it could and scores as a name test does; element(x, T)
            // names both and scores above anything else a single step can be; element() names neither and is
            // a wildcard over its kind (XSLT 3.0 §6.5).
            Assert.AreEqual(
                "<out>[typed]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/x\"/>")
                    + "<xsl:template match=\"element(x, xs:untyped)\">[typed]</xsl:template>"
                    + "<xsl:template match=\"element(x)\">[named]</xsl:template>"
                    + "<xsl:template match=\"element()\">[any]</xsl:template>",
                    "<r><x/></r>"));

            Assert.AreEqual(
                "<out>[named]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/x\"/>")
                    + "<xsl:template match=\"element(x)\">[named]</xsl:template>"
                    + "<xsl:template match=\"element()\">[any]</xsl:template>",
                    "<r><x/></r>"));

            // A processing-instruction test naming its target says as much as a name test and scores as one.
            Assert.AreEqual(
                "<out>[named]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/processing-instruction()\"/>")
                    + "<xsl:template match=\"processing-instruction('p')\">[named]</xsl:template>"
                    + "<xsl:template match=\"processing-instruction()\">[any]</xsl:template>",
                    "<r><?p go?></r>"));
        }

        [TestMethod]
        public void AMatchPatternMayCallAFunctionDeclaredAfterIt()
        {
            // A stylesheet's declarations are not written in an order a reader may depend on, so a match
            // pattern is read once the whole of the stylesheet is in — as an xsl:key's own pattern already
            // was. Written the other way round the function was simply unknown.
            Assert.AreEqual(
                "<out>[wanted]</out>",
                Run(
                    Root("<xsl:apply-templates select=\"/r/*\"/>")
                    + "<xsl:template match=\"*[my:wanted(.)]\" xmlns:my=\"urn:my\">[wanted]</xsl:template>"
                    + "<xsl:template match=\"*\">[other]</xsl:template>"
                    + "<xsl:function name=\"my:wanted\" xmlns:my=\"urn:my\" as=\"xs:boolean\">"
                    + "<xsl:param name=\"e\" as=\"element()\"/>"
                    + "<xsl:sequence select=\"local-name($e) = 'x'\"/></xsl:function>",
                    "<r><x/></r>"));
        }
        [TestMethod]
        public void OnCompletionHasNoFocus()
        {
            // The iteration has finished, so there is no item it is standing on (§8.3). Letting the one the
            // loop was entered with show through would let an xsl:number in there number a node the
            // instruction is not about, and position() read a place in a sequence that is done with.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\"><out><xsl:iterate select=\"//a\">"
                    + "<xsl:on-completion><xsl:number/></xsl:on-completion>"
                    + "</xsl:iterate></out></xsl:template>",
                    "<r><a/><a/></r>",
                    "3.0"));

            Assert.AreEqual("XTTE0990", error.Code);

            // What it does have is the parameters, which is the whole point of the element.
            Assert.AreEqual(
                "<out>2</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:iterate select=\"//a\">"
                    + "<xsl:param name=\"seen\" select=\"0\"/>"
                    + "<xsl:on-completion><xsl:value-of select=\"$seen\"/></xsl:on-completion>"
                    + "<xsl:next-iteration><xsl:with-param name=\"seen\" select=\"$seen + 1\"/>"
                    + "</xsl:next-iteration></xsl:iterate></out></xsl:template>",
                    "<r><a/><a/></r>",
                    "3.0"));
        }
    }
}
