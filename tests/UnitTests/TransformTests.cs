using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// End-to-end transformation tests.
    /// </summary>
    /// <remarks>
    /// Most cases are checked against <see cref="XslCompiledTransform"/> rather than against a hand-written
    /// expected string. The framework's own processor is a conformant XSLT 1.0 implementation that happens to
    /// be sitting in the box, which makes it a far better oracle early on than any expectation written by the
    /// same person who wrote the engine.
    /// </remarks>
    [TestClass]
    public sealed class TransformTests
    {
        private const string Header =
            "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">";

        private const string Footer = "</xsl:stylesheet>";

        private static string Sheet(string body, string? extra = null)
        {
            return string.Concat(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"",
                extra ?? string.Empty,
                ">",
                body,
                Footer);
        }

        /// <summary>
        /// Compiles a stylesheet for these tests, which compare result fragments.
        /// </summary>
        /// <remarks>
        /// The XML declaration is suppressed because the reference processor is configured the same way and a
        /// fragment cannot carry one anyway. The declaration's own behaviour is covered by
        /// <see cref="OutputAndWhitespaceTests"/>.
        /// </remarks>
        private static Xslt Compile(string stylesheet, XsltBackend backend)
        {
            return new Xslt(stylesheet, new XsltOptions { Backend = backend, OmitXmlDeclaration = true });
        }

        /// <summary>
        /// Runs a stylesheet with this engine, through both backends.
        /// </summary>
        /// <remarks>
        /// Every test in this class therefore checks the emitted IL as well as the interpreter, and the ones
        /// that go on to call <see cref="AssertMatchesReference"/> compare all three against
        /// <see cref="XslCompiledTransform"/>. The interpreter is the reference the emitted code must
        /// reproduce exactly, so any divergence is a code-generation bug and is caught here rather than in
        /// whichever test happened to exercise the affected node type.
        /// </remarks>
        private static string Run(string stylesheet, string input)
        {
            string interpreted = Compile(stylesheet, XsltBackend.Interpreted).TransformXml(input);
            string compiled = Compile(stylesheet, XsltBackend.Compiled).TransformXml(input);

            Assert.AreEqual(
                interpreted,
                compiled,
                "The compiled backend disagreed with the interpreter, which means a code-generation bug.");

            return interpreted;
        }

        /// <summary>Runs a stylesheet with the framework's processor, for comparison.</summary>
        private static string RunReference(string stylesheet, string input)
        {
            XslCompiledTransform transform = new XslCompiledTransform();
            using (XmlReader stylesheetReader = XmlReader.Create(new StringReader(stylesheet)))
            {
                transform.Load(stylesheetReader);
            }

            StringWriter output = new StringWriter();
            XmlWriterSettings settings = transform.OutputSettings!.Clone();
            settings.OmitXmlDeclaration = true;
            settings.ConformanceLevel = ConformanceLevel.Auto;

            using (XmlWriter writer = XmlWriter.Create(output, settings))
            using (XmlReader inputReader = XmlReader.Create(new StringReader(input)))
            {
                transform.Transform(inputReader, null, writer);
            }

            return output.ToString();
        }

        /// <summary>
        /// Re-parses a result fragment so that cosmetic differences — self-closing style, where a namespace
        /// declaration is placed, entity forms — do not count as mismatches.
        /// </summary>
        private static string Normalize(string fragment)
        {
            return XmlComparison.Normalize(fragment);
        }

        /// <summary>Asserts that this engine and the framework's processor agree.</summary>
        private static void AssertMatchesReference(string stylesheet, string input)
        {
            string actual = Run(stylesheet, input);
            string expected = RunReference(stylesheet, input);

            Assert.AreEqual(
                Normalize(expected),
                Normalize(actual),
                $"Output differed from XslCompiledTransform.\n  expected: {expected}\n  actual:   {actual}");
        }

        // ---- Basic shape --------------------------------------------------------------------------------

        [TestMethod]
        public void LiteralResultElementsAndValueOf()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out><xsl:value-of select=\"/r/a\"/></out></xsl:template>"),
                "<r><a>hello</a></r>");
        }

        [TestMethod]
        public void ForEachIteratesInDocumentOrder()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><list>"
                    + "<xsl:for-each select=\"/r/i\"><item><xsl:value-of select=\".\"/></item></xsl:for-each>"
                    + "</list></xsl:template>"),
                "<r><i>1</i><i>2</i><i>3</i></r>");
        }

        [TestMethod]
        public void PositionAndLastInsideForEach()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"/r/i\">"
                    + "<xsl:value-of select=\"position()\"/>/<xsl:value-of select=\"last()\"/>;"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                "<r><i>a</i><i>b</i><i>c</i></r>");
        }

        [TestMethod]
        public void AttributeValueTemplates()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\">"
                    + "<a href=\"{/r/@base}/page-{/r/@id}.html\" fixed=\"x\"><xsl:value-of select=\"/r\"/></a>"
                    + "</xsl:template>"),
                "<r base=\"http://example.com\" id=\"7\">text</r>");
        }

        [TestMethod]
        public void AttributeValueTemplatesOfEveryShape()
        {
            // One literal and one expression takes a different path from three parts or more, and which side
            // the literal falls on has to survive it.
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\">"
                    + "<a trailing=\"{/r/@id}.html\" leading=\"page-{/r/@id}\""
                    + " many=\"a-{/r/@id}-b-{/r/@base}-c\"/>"
                    + "</xsl:template>"),
                "<r base=\"http://example.com\" id=\"7\">text</r>");
        }

        [TestMethod]
        public void AnAttributeValueTemplateOfMorePartsThanAreNamedSeparately()
        {
            // Up to four parts are joined by name; beyond that they are collected first, which is the path
            // this takes. The parts are long enough that a wrong length would show.
            string padding = new string('x', 200);

            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><a v=\"{/r/@a}-{/r/@b}-{/r/@c}\"/></xsl:template>"),
                $"<r a=\"{padding}\" b=\"{padding}\" c=\"{padding}\"/>");
        }

        [TestMethod]
        public void EscapedBracesInAttributeValueTemplates()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><a style=\"{{margin:0}}\" v=\"{/r}\"/></xsl:template>"),
                "<r>value</r>");
        }

        [TestMethod]
        public void IfAndChoose()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:for-each select=\"/r/n\">"
                + "<xsl:if test=\". &gt; 1\"><big><xsl:value-of select=\".\"/></big></xsl:if>"
                + "<xsl:choose>"
                + "<xsl:when test=\". = 1\"><one/></xsl:when>"
                + "<xsl:when test=\". = 2\"><two/></xsl:when>"
                + "<xsl:otherwise><other/></xsl:otherwise>"
                + "</xsl:choose>"
                + "</xsl:for-each></out></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><n>1</n><n>2</n><n>3</n></r>");
        }

        // ---- Template dispatch --------------------------------------------------------------------------

        [TestMethod]
        public void ApplyTemplatesUsesBuiltInRulesToRecurse()
        {
            // No template matches <r>, so the built-in rule recurses into it and reaches <a>.
            AssertMatchesReference(
                Sheet("<xsl:template match=\"a\"><found><xsl:value-of select=\".\"/></found></xsl:template>"),
                "<r><a>x</a><a>y</a></r>");
        }

        [TestMethod]
        public void BuiltInRuleCopiesTextNodes()
        {
            AssertMatchesReference(Sheet("<xsl:template match=\"keep\"><k/></xsl:template>"),
                "<r>loose text<keep/>more text</r>");
        }

        [TestMethod]
        public void MoreSpecificPatternWinsOnPriority()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//i\"/></out></xsl:template>"
                + "<xsl:template match=\"i\"><generic/></xsl:template>"
                + "<xsl:template match=\"special/i\"><specific/></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><i/><special><i/></special></r>");
        }

        [TestMethod]
        public void ExplicitPriorityOverridesTheDefault()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//i\"/></out></xsl:template>"
                + "<xsl:template match=\"special/i\" priority=\"-1\"><specific/></xsl:template>"
                + "<xsl:template match=\"i\"><generic/></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><special><i/></special></r>");
        }

        [TestMethod]
        public void WildcardAndKindPatternsRankBelowNamedOnes()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/node()\"/></out></xsl:template>"
                + "<xsl:template match=\"*\"><star/></xsl:template>"
                + "<xsl:template match=\"named\"><named/></xsl:template>"
                + "<xsl:template match=\"text()\"><t/></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><named/><other/>text</r>");
        }

        [TestMethod]
        public void PatternsWithPredicatesMatchBottomUp()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//i\"/></out></xsl:template>"
                + "<xsl:template match=\"i[@k='y']\"><flagged/></xsl:template>"
                + "<xsl:template match=\"i\"><plain/></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><i/><i k=\"y\"/><i k=\"n\"/></r>");
        }

        [TestMethod]
        public void PositionalPredicateInAPatternCountsAmongSiblings()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//i\"/></out></xsl:template>"
                + "<xsl:template match=\"i[1]\"><first/></xsl:template>"
                + "<xsl:template match=\"i\"><rest/></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><i/><i/><g><i/></g></r>");
        }

        [TestMethod]
        public void DescendantPatternMatchesAtAnyDepth()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//i\"/></out></xsl:template>"
                + "<xsl:template match=\"top//i\"><under/></xsl:template>"
                + "<xsl:template match=\"i\"><free/></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><i/><top><mid><i/></mid></top></r>");
        }

        [TestMethod]
        public void AbsolutePatternAnchorsAtTheRoot()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//r\"/></out></xsl:template>"
                + "<xsl:template match=\"/r\"><document-element/></xsl:template>"
                + "<xsl:template match=\"r\"><nested/></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><inner><r/></inner></r>");
        }

        [TestMethod]
        public void UnionPatternsShareATemplate()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/*\"/></out></xsl:template>"
                + "<xsl:template match=\"a|b\"><hit><xsl:value-of select=\"name()\"/></hit></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><a/><b/><c/></r>");
        }

        [TestMethod]
        public void ModesKeepTemplateSetsApart()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<toc><xsl:apply-templates select=\"//h\" mode=\"toc\"/></toc>"
                + "<body><xsl:apply-templates select=\"//h\"/></body>"
                + "</out></xsl:template>"
                + "<xsl:template match=\"h\" mode=\"toc\"><entry><xsl:value-of select=\".\"/></entry></xsl:template>"
                + "<xsl:template match=\"h\"><heading><xsl:value-of select=\".\"/></heading></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><h>One</h><h>Two</h></r>");
        }

        // ---- Variables and parameters --------------------------------------------------------------------

        [TestMethod]
        public void LocalVariablesAndGlobalVariables()
        {
            string stylesheet = Sheet(
                "<xsl:variable name=\"suffix\" select=\"'!'\"/>"
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:variable name=\"greeting\" select=\"/r/a\"/>"
                + "<xsl:value-of select=\"concat($greeting, $suffix)\"/>"
                + "</out></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><a>hi</a></r>");
        }

        [TestMethod]
        public void GlobalVariablesMayReferToOneAnotherInAnyOrder()
        {
            // $first is declared before $second but depends on it, so evaluation cannot simply run in order.
            string stylesheet = Sheet(
                "<xsl:variable name=\"first\" select=\"concat($second, '-end')\"/>"
                + "<xsl:variable name=\"second\" select=\"'start'\"/>"
                + "<xsl:template match=\"/\"><out><xsl:value-of select=\"$first\"/></out></xsl:template>");

            AssertMatchesReference(stylesheet, "<r/>");
        }

        [TestMethod]
        public void CircularGlobalVariablesAreReported()
        {
            string stylesheet = Sheet(
                "<xsl:variable name=\"a\" select=\"$b\"/>"
                + "<xsl:variable name=\"b\" select=\"$a\"/>"
                + "<xsl:template match=\"/\"><out><xsl:value-of select=\"$a\"/></out></xsl:template>");

            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(stylesheet, "<r/>"));
            StringAssert.Contains(error.Message, "itself");
        }

        [TestMethod]
        public void CallTemplateWithParameters()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:call-template name=\"greet\"><xsl:with-param name=\"who\" select=\"/r/a\"/></xsl:call-template>"
                + "<xsl:call-template name=\"greet\"/>"
                + "</out></xsl:template>"
                + "<xsl:template name=\"greet\">"
                + "<xsl:param name=\"who\" select=\"'world'\"/>"
                + "<hello to=\"{$who}\"/>"
                + "</xsl:template>");

            AssertMatchesReference(stylesheet, "<r><a>you</a></r>");
        }

        [TestMethod]
        public void WithParamValuesBelongToTheInstructionAndNotToTheSelectedNodes()
        {
            // A parameter on xsl:apply-templates is evaluated once, where it is written, however many nodes
            // are selected — so here both name(.) and position() speak of the node the instruction is in, and
            // the two items receive the same values. Evaluating per selected node reads far more naturally
            // and is wrong; the reference processor settles it.
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\">"
                + "<xsl:with-param name=\"p\" select=\"name(.)\"/>"
                + "<xsl:with-param name=\"q\" select=\"position()\"/>"
                + "</xsl:apply-templates></out></xsl:template>"
                + "<xsl:template match=\"item\">"
                + "<xsl:param name=\"p\"/><xsl:param name=\"q\"/>"
                + "<got p=\"{$p}\" q=\"{$q}\"/></xsl:template>");

            AssertMatchesReference(stylesheet, "<root><item/><item/></root>");
        }

        [TestMethod]
        public void RecursiveTemplateTerminates()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:call-template name=\"countdown\"><xsl:with-param name=\"n\" select=\"5\"/></xsl:call-template>"
                + "</out></xsl:template>"
                + "<xsl:template name=\"countdown\">"
                + "<xsl:param name=\"n\"/>"
                + "<xsl:if test=\"$n &gt; 0\">"
                + "<n><xsl:value-of select=\"$n\"/></n>"
                + "<xsl:call-template name=\"countdown\">"
                + "<xsl:with-param name=\"n\" select=\"$n - 1\"/></xsl:call-template>"
                + "</xsl:if>"
                + "</xsl:template>");

            AssertMatchesReference(stylesheet, "<r/>");
        }

        [TestMethod]
        public void UnboundedRecursionIsReportedRatherThanCrashing()
        {
            // The call is wrapped in an element so that it is not the last thing the template does: a call
            // that is, is made in the template's own place and never deepens the stack, and a template that
            // does that without a terminating case is a loop rather than a recursion — as it would be under
            // any processor that runs the idiom the way it was meant to be run.
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><xsl:call-template name=\"forever\"/></xsl:template>"
                + "<xsl:template name=\"forever\"><x><xsl:call-template name=\"forever\"/></x></xsl:template>");

            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(stylesheet, "<r/>"));
            StringAssert.Contains(error.Message, "recursing");
        }

        [TestMethod]
        public void ACallThatIsTheLastThingATemplateDoesDoesNotGrowTheStack()
        {
            // The 1.0 idiom for a loop: a template that calls itself as the last thing it does, once per
            // item. Nested, each call costs a frame and the idiom fails a couple of thousand in; made in
            // the template's own place, it runs for as long as it likes. A hundred thousand is fifty times
            // the depth limit, so this passes only by not nesting.
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:call-template name=\"sum\"><xsl:with-param name=\"n\" select=\"100000\"/></xsl:call-template>"
                + "</out></xsl:template>"
                + "<xsl:template name=\"sum\">"
                + "<xsl:param name=\"n\"/><xsl:param name=\"total\" select=\"0\"/>"
                + "<xsl:choose>"
                + "<xsl:when test=\"$n = 0\"><xsl:value-of select=\"$total\"/></xsl:when>"
                + "<xsl:otherwise>"
                + "<xsl:call-template name=\"sum\">"
                + "<xsl:with-param name=\"n\" select=\"$n - 1\"/>"
                + "<xsl:with-param name=\"total\" select=\"$total + $n\"/>"
                + "</xsl:call-template>"
                + "</xsl:otherwise></xsl:choose>"
                + "</xsl:template>");

            // The total is a double, backwards-compatible arithmetic making one of every operand, and a
            // double that large is written with an exponent.
            Assert.AreEqual("<out>5.00005E9</out>", Run(stylesheet, "<r/>"));
        }

        [TestMethod]
        public void ACallMadeInATemplatesPlaceStillReturnsToTheCaller()
        {
            // What the caller of a template sees is unchanged by how the template's last call was made: the
            // called template's output comes first, and what the caller writes after comes after it.
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:call-template name=\"a\"/><after/></out></xsl:template>"
                + "<xsl:template name=\"a\">"
                + "<xsl:if test=\"true()\"><xsl:call-template name=\"b\"><xsl:with-param name=\"x\" select=\"'v'\"/></xsl:call-template></xsl:if>"
                + "</xsl:template>"
                + "<xsl:template name=\"b\"><xsl:param name=\"x\"/><b><xsl:value-of select=\"$x\"/></b></xsl:template>");

            AssertMatchesReference(stylesheet, "<r/>");
        }

        [TestMethod]
        public void ARunOfSignsIsReadAsItsParity()
        {
            // A run of signs nests without an expression between the levels, so the parser counts it
            // rather than recursing over it. Short runs are checked against the reference processor; the
            // long one would overflow either processor's stack if it were nested, parsing or evaluating.
            AssertMatchesReference(Sheet("<xsl:template match=\"/\"><out><xsl:value-of select=\"---1\"/>,<xsl:value-of select=\"- - - - 1\"/>,<xsl:value-of select=\"--'3'\"/></out></xsl:template>"), "<r/>");

            string signs = new string('-', 20001);
            Assert.AreEqual(
                "<out>-1,1</out>",
                Run(
                    Sheet($"<xsl:template match=\"/\"><out><xsl:value-of select=\"{signs}1\"/>,<xsl:value-of select=\"-{signs}1\"/></out></xsl:template>"),
                    "<r/>"));
        }

        [TestMethod]
        public void VariableContentBecomesAResultTreeFragment()
        {
            // A variable with content rather than a select captures markup, which copy-of must reproduce as
            // markup rather than as escaped text.
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:variable name=\"rows\">"
                + "<xsl:for-each select=\"/r/i\"><row><xsl:value-of select=\".\"/></row></xsl:for-each>"
                + "</xsl:variable>"
                + "<xsl:copy-of select=\"$rows\"/>"
                + "</out></xsl:template>");

            AssertMatchesReference(stylesheet, "<r><i>1</i><i>2</i></r>");
        }

        [TestMethod]
        public void StringValueOfAResultTreeFragmentIsItsText()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:variable name=\"v\"><a>x</a><b>y</b></xsl:variable>"
                + "<xsl:value-of select=\"$v\"/>"
                + "</out></xsl:template>");

            AssertMatchesReference(stylesheet, "<r/>");
        }

        // ---- Copying -------------------------------------------------------------------------------------

        [TestMethod]
        public void CopyOfReproducesSubtreesWithAttributes()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out><xsl:copy-of select=\"/r/keep\"/></out></xsl:template>"),
                "<r><keep a=\"1\"><child b=\"2\">text</child></keep><drop/></r>");
        }

        [TestMethod]
        public void CopyOfCarriesTheNamespacesInScopeThroughElementsThatDeclareNone()
        {
            // The in-scope namespaces are found by way of the ancestors that declare one, skipping those
            // that do not. The nearest declaration of a prefix wins over an outer one, an un-declaration of
            // the default namespace hides the outer one, and a declaration on a skipped-over ancestor still
            // counts.
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out><xsl:copy-of select=\"//x\"/></out></xsl:template>"),
                "<r xmlns=\"urn:d\" xmlns:p=\"urn:p1\"><m xmlns:s=\"urn:s\"><n><e xmlns:p=\"urn:p2\" xmlns=\"\">"
                + "<x/></e></n></m></r>");
        }

        [TestMethod]
        public void AbbreviatedDescendantStepsKeepTheirMeaningUnderEveryKindOfPredicate()
        {
            // '//b' is folded into one descendant step wherever that selects the same nodes, and left as
            // written where a predicate counts: //b[1] is the first b under each parent, not the first b in
            // the document, and so are [position() = 1], [last()] and [count(...)]. A boolean predicate,
            // and one that tests for a node, is the same test whichever way the b was reached.
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<a><xsl:value-of select=\"count(//b)\"/></a>"
                + "<b><xsl:value-of select=\"count(//b[1])\"/></b>"
                + "<c><xsl:value-of select=\"count(//b[position() = 1])\"/></c>"
                + "<d><xsl:value-of select=\"count(//b[last()])\"/></d>"
                + "<e><xsl:value-of select=\"count(//b[@k])\"/></e>"
                + "<f><xsl:value-of select=\"count(//b[. = 'x'])\"/></f>"
                + "<g><xsl:value-of select=\"count(//b[not(position() = 2)])\"/></g>"
                + "<h><xsl:value-of select=\"count(//b[2 > 1])\"/></h>"
                + "<i><xsl:for-each select=\"//b[@k]\"><xsl:value-of select=\"@k\"/></xsl:for-each></i>"
                + "<j><xsl:value-of select=\"count(/r//b)\"/>,<xsl:value-of select=\"count(//p//b)\"/></j>"
                + "</out></xsl:template>");

            AssertMatchesReference(
                stylesheet,
                "<r><p><b k=\"1\">x</b><b>y</b><q><b k=\"2\">x</b></q></p><p><b>x</b></p><b k=\"3\"/></r>");
        }

        [TestMethod]
        public void IdentityTransformCopiesEverything()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"@*|node()\">"
                + "<xsl:copy><xsl:apply-templates select=\"@*|node()\"/></xsl:copy>"
                + "</xsl:template>");

            AssertMatchesReference(stylesheet, "<r a=\"1\"><b>text</b><!--c--><d e=\"2\"/></r>");
        }

        [TestMethod]
        public void CopyIsShallow()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><xsl:for-each select=\"/r\"><xsl:copy/></xsl:for-each></xsl:template>"),
                "<r a=\"1\"><child/></r>");
        }

        // ---- Computed names and other instructions -------------------------------------------------------

        [TestMethod]
        public void ComputedElementsAndAttributes()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\">"
                    + "<xsl:element name=\"{/r/@tag}\">"
                    + "<xsl:attribute name=\"{/r/@att}\"><xsl:value-of select=\"/r\"/></xsl:attribute>"
                    + "</xsl:element>"
                    + "</xsl:template>"),
                "<r tag=\"widget\" att=\"size\">large</r>");
        }

        [TestMethod]
        public void CommentsAndProcessingInstructionsCanBeGenerated()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:comment>generated</xsl:comment>"
                    + "<xsl:processing-instruction name=\"target\">data</xsl:processing-instruction>"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void TextInstructionPreservesWhitespaceThatIsOtherwiseStripped()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>\n   "
                    + "<a/>\n   <xsl:text>  kept  </xsl:text>\n   <b/>\n"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void SortingByTextAndByNumber()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"/r/i\"><xsl:sort select=\".\" data-type=\"number\"/>"
                    + "<n><xsl:value-of select=\".\"/></n></xsl:for-each>"
                    + "</out></xsl:template>"),
                "<r><i>10</i><i>9</i><i>100</i></r>");
        }

        [TestMethod]
        public void SortingDescendingAndByMultipleKeys()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"/r/i\">"
                    + "<xsl:sort select=\"@g\"/>"
                    + "<xsl:sort select=\".\" data-type=\"number\" order=\"descending\"/>"
                    + "<n g=\"{@g}\"><xsl:value-of select=\".\"/></n></xsl:for-each>"
                    + "</out></xsl:template>"),
                "<r><i g=\"b\">1</i><i g=\"a\">2</i><i g=\"b\">3</i><i g=\"a\">1</i></r>");
        }

        [TestMethod]
        public void ApplyTemplatesCanSort()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:apply-templates select=\"/r/i\"><xsl:sort select=\".\"/></xsl:apply-templates>"
                    + "</out></xsl:template>"
                    + "<xsl:template match=\"i\"><n><xsl:value-of select=\".\"/></n></xsl:template>"),
                "<r><i>c</i><i>a</i><i>b</i></r>");
        }

        // ---- Namespaces ----------------------------------------------------------------------------------

        [TestMethod]
        public void NamespacedInputRequiresAPrefixInPatternsAndPaths()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//d:item\"/></out></xsl:template>"
                + "<xsl:template match=\"d:item\"><found><xsl:value-of select=\".\"/></found></xsl:template>",
                " xmlns:d=\"urn:demo\"");

            AssertMatchesReference(stylesheet, "<doc xmlns=\"urn:demo\"><item>one</item><item>two</item></doc>");
        }

        [TestMethod]
        public void NamespacedOutputDeclaresItsPrefixes()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><o:root xmlns:o=\"urn:out\">"
                + "<o:child o:a=\"1\"><xsl:value-of select=\"/r\"/></o:child>"
                + "</o:root></xsl:template>");

            AssertMatchesReference(stylesheet, "<r>text</r>");
        }

        [TestMethod]
        public void IdentityTransformPreservesNamespaces()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"@*|node()\">"
                + "<xsl:copy><xsl:apply-templates select=\"@*|node()\"/></xsl:copy>"
                + "</xsl:template>");

            AssertMatchesReference(
                stylesheet,
                "<doc xmlns=\"urn:a\" xmlns:b=\"urn:b\"><b:child b:att=\"1\">text</b:child></doc>");
        }

        [TestMethod]
        public void NamespaceSaturatedIso20022DocumentToHtml()
        {
            // The shape this engine is actually aimed at: an ISO 20022 message, where every element sits in a
            // default namespace and so every pattern and path has to go through a declared prefix.
            const string Camt =
                "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.054.001.02\">"
                + "<BkToCstmrDbtCdtNtfctn>"
                + "<Ntfctn>"
                + "<Id>NOTIF-001</Id>"
                + "<Ntry><Amt Ccy=\"NOK\">1250.00</Amt><CdtDbtInd>CRDT</CdtDbtInd>"
                + "<NtryDtls><TxDtls><RmtInf><Ustrd>Invoice 4711</Ustrd></RmtInf></TxDtls></NtryDtls></Ntry>"
                + "<Ntry><Amt Ccy=\"NOK\">99.50</Amt><CdtDbtInd>DBIT</CdtDbtInd>"
                + "<NtryDtls><TxDtls><RmtInf><Ustrd>Fee</Ustrd></RmtInf></TxDtls></NtryDtls></Ntry>"
                + "</Ntfctn>"
                + "</BkToCstmrDbtCdtNtfctn>"
                + "</Document>";

            string stylesheet = Sheet(
                "<xsl:template match=\"/\">"
                + "<div class=\"notification\">"
                + "<h1><xsl:value-of select=\"//c:Ntfctn/c:Id\"/></h1>"
                + "<table><xsl:apply-templates select=\"//c:Ntry\"/></table>"
                + "<p>Entries: <xsl:value-of select=\"count(//c:Ntry)\"/>, "
                + "credited <xsl:value-of select=\"sum(//c:Ntry[c:CdtDbtInd='CRDT']/c:Amt)\"/></p>"
                + "</div>"
                + "</xsl:template>"
                + "<xsl:template match=\"c:Ntry\">"
                + "<tr class=\"{translate(c:CdtDbtInd,'CRDTBI','crdtbi')}\">"
                + "<td><xsl:value-of select=\"c:NtryDtls/c:TxDtls/c:RmtInf/c:Ustrd\"/></td>"
                + "<td><xsl:value-of select=\"c:Amt\"/>&#160;<xsl:value-of select=\"c:Amt/@Ccy\"/></td>"
                + "</tr>"
                + "</xsl:template>",
                " xmlns:c=\"urn:iso:std:iso:20022:tech:xsd:camt.054.001.02\" exclude-result-prefixes=\"c\"");

            AssertMatchesReference(stylesheet, Camt);

            // And spot-check the actual content, not just that the two engines agree.
            string result = Run(stylesheet, Camt);
            StringAssert.Contains(result, "<h1>NOTIF-001</h1>");
            StringAssert.Contains(result, "Invoice 4711");
            StringAssert.Contains(result, "Entries: 2");
            StringAssert.Contains(result, "credited 1250");

            // exclude-result-prefixes must keep the CAMT namespace out of the HTML entirely.
            Assert.DoesNotContain("iso:20022", result, "the source namespace must not leak into the result");
        }

        // ---- Output methods ------------------------------------------------------------------------------

        [TestMethod]
        public void TextOutputMethodEmitsNoMarkup()
        {
            string stylesheet = Sheet(
                "<xsl:output method=\"text\"/>"
                + "<xsl:template match=\"/\"><ignored><xsl:value-of select=\"/r/a\"/></ignored></xsl:template>");

            Assert.AreEqual("hello", Run(stylesheet, "<r><a>hello</a></r>"));
            Assert.AreEqual(RunReference(stylesheet, "<r><a>hello</a></r>"), Run(stylesheet, "<r><a>hello</a></r>"));
        }

        [TestMethod]
        public void HtmlOutputMethodLeavesVoidElementsUnclosed()
        {
            string stylesheet = Sheet(
                "<xsl:output method=\"html\"/>"
                + "<xsl:template match=\"/\"><div><br/><p/><img src=\"{/r/@s}\"/></div></xsl:template>");

            string actual = Run(stylesheet, "<r s=\"a.png\"/>");

            StringAssert.Contains(actual, "<br>");
            Assert.IsFalse(actual.Contains("<br/>") || actual.Contains("<br/>"), "a void element must not self-close");
            StringAssert.Contains(actual, "<p></p>");
        }

        [TestMethod]
        public void EscapingIsAppliedToTextAndAttributes()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out v=\"{/r}\"><xsl:value-of select=\"/r\"/></out></xsl:template>"),
                "<r>a &lt; b &amp; c &gt; d \"quoted\"</r>");
        }

        [TestMethod]
        public void DisableOutputEscapingWritesMarkupVerbatim()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"/r\" disable-output-escaping=\"yes\"/>"
                + "</out></xsl:template>");

            Assert.AreEqual("<out><b>bold</b></out>", Run(stylesheet, "<r>&lt;b&gt;bold&lt;/b&gt;</r>"));
        }

        // ---- Diagnostics ---------------------------------------------------------------------------------

        [TestMethod]
        public void CompilationErrorsAreReportedFromTheConstructor()
        {
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(() => new Xslt("<not-a-stylesheet/>")).Message,
                "xsl:stylesheet");

            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(Sheet("<xsl:template match=\"/\"><xsl:value-of select=\"$missing\"/></xsl:template>")))
                    .Message,
                "missing");

            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(Sheet("<xsl:template match=\"/\"><xsl:call-template name=\"absent\"/></xsl:template>")))
                    .Message,
                "absent");
        }

        [TestMethod]
        public void ACompiledStylesheetCanBeReusedAcrossThreads()
        {
            Xslt stylesheet = Compile(
                Sheet("<xsl:template match=\"/\"><out><xsl:value-of select=\"/r/@n\"/></out></xsl:template>"), XsltBackend.Compiled);

            Parallel.For(0, 200, i =>
            {
                Assert.AreEqual($"<out>{i}</out>", stylesheet.TransformXml($"<r n=\"{i}\"/>"));
            });
        }

        [TestMethod]
        public void MessagesGoToTheMessageWriterNotTheResult()
        {
            StringWriter messages = new StringWriter();

            Xslt stylesheet = new Xslt(
                Sheet("<xsl:template match=\"/\"><out><xsl:message>noted</xsl:message></out></xsl:template>"),
                new XsltOptions { OmitXmlDeclaration = true, MessageWriter = messages });

            Assert.AreEqual("<out/>", stylesheet.TransformXml("<r/>"));
            StringAssert.Contains(messages.ToString(), "noted");
        }
        [TestMethod]
        public void AVariableGoesOutOfScopeWithTheConstructorItWasDeclaredIn()
        {
            // §9.7: a variable is in scope for the following siblings of its declaration and for their
            // descendants, and for nothing else. So one declared inside a literal result element shadows
            // nothing once that element closes, and the name goes back to meaning what it meant before —
            // here the template's own parameter. Each declaration inside still shadows the one before it,
            // which is what makes the second read the first.
            const string Body =
                "<xsl:template match=\"/\"><out><xsl:call-template name=\"t\"/></out></xsl:template>"
                + "<xsl:template name=\"t\">"
                + "<xsl:param name=\"y\" select=\"'a'\"/>"
                + "<p>"
                + "<xsl:variable name=\"y\" select=\"'b'\"/>"
                + "<i><xsl:value-of select=\"$y\"/></i>"
                + "<xsl:variable name=\"y\" select=\"concat($y,'c')\"/>"
                + "<i><xsl:value-of select=\"$y\"/></i>"
                + "</p>"
                + "<after><xsl:value-of select=\"$y\"/></after>"
                + "</xsl:template>";

            Assert.AreEqual(
                "<out><p><i>b</i><i>bc</i></p><after>a</after></out>",
                Run(Sheet(Body), "<r/>"));

            // XslCompiledTransform is not asked here: XSLT 1.0 refused two bindings of one name in a
            // scope outright, where 2.0 lets the second shadow the first, and the shadowing is what this
            // is about. What the two languages agree on is where the scope ends.
        }
    }
}
