namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what becomes of the items a sequence constructor produces: text in a tree, a string, or the
    /// sequence itself, each by its own rules.
    /// </summary>
    /// <remarks>
    /// XSLT 3.0 §5.7. The three destinations differ in what they do with adjacent items — a tree joins
    /// adjacent atomic values with spaces and merges adjacent text nodes, a string merges the text nodes and
    /// joins everything with a separator, and a sequence keeps every item as it was made — and most of what
    /// the suite's <c>misc/seqtor</c> set checks is the difference between the three.
    /// </remarks>
    [TestClass]
    public sealed class SequenceConstructorTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" exclude-result-prefixes=\"xs f\">";

        /// <summary>Runs a 3.0 stylesheet through both backends, which must agree.</summary>
        private static string Run(string body, string input = "<r/>")
        {
            string stylesheet = Head + body + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                Version = XsltVersion.V30,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        private static string Root(string content)
        {
            return "<xsl:template match=\"/\"><out>" + content + "</out></xsl:template>";
        }

        // ---- Into a tree -----------------------------------------------------------------------------------

        [TestMethod]
        public void AdjacentAtomicValuesBecomeOneTextNodeWithSpacesBetween()
        {
            // 1 and 2 are atomic values, so a space goes between them. The value-of makes a text node, which
            // breaks the run: no space before 3, and none between 3 and the atomic 4 after it.
            Assert.AreEqual(
                "<out>1 234</out>",
                Run(Root(
                    "<xsl:sequence select=\"1\"/><xsl:sequence select=\"2\"/>"
                    + "<xsl:value-of select=\"3\"/><xsl:sequence select=\"4\"/>")));

            // The same from one instruction, which is what the suite's seqtor tests mostly write.
            Assert.AreEqual("<out>1 2 3</out>", Run(Root("<xsl:sequence select=\"1 to 3\"/>")));
        }

        [TestMethod]
        public void TwoEmptyStringsInARowAreOneSpace()
        {
            // Which is the case that shows the rule is about the items and not about their text: nothing,
            // then nothing, with the space that goes between two atomic values.
            Assert.AreEqual(
                "<out> </out>",
                Run(Root("<xsl:sequence select=\"''\"/><xsl:sequence select=\"''\"/>")));
        }

        [TestMethod]
        public void AnEmptyTextIsNothingInATreeAndAZeroLengthTextNodeInASequence()
        {
            Assert.AreEqual("<out/>", Run(Root("<xsl:text/>")));

            // But it does end a run of atomic values: the space between two of them is for values with
            // nothing at all between, which the suite's on-empty-113a settles.
            Assert.AreEqual(
                "<out>12</out>",
                Run(Root("<xsl:sequence select=\"1\"/><xsl:text/><xsl:sequence select=\"2\"/>")));

            // But in a sequence it is an item, as the suite's seqtor-041 counts.
            Assert.AreEqual(
                "<out>3</out>",
                Run(
                    "<xsl:variable name=\"v\" as=\"text()*\">"
                    + "<xsl:text/><xsl:text>a</xsl:text><xsl:text/></xsl:variable>"
                    + Root("<xsl:value-of select=\"count($v)\"/>")));
        }

        // ---- Into a string ---------------------------------------------------------------------------------

        [TestMethod]
        public void SimpleContentJoinsItemsWithTheInstructionsSeparator()
        {
            const string Content =
                "<xsl:sequence select=\"1\"/><xsl:sequence select=\"2\"/><xsl:text>x</xsl:text>";

            // Three items either way. An attribute's content joins with nothing unless it says otherwise; a
            // comment's joins with a single space. The suite's seqtor-039d and seqtor-036a between them.
            Assert.AreEqual(
                "<out a=\"12x\"/>",
                Run(Root("<xsl:attribute name=\"a\">" + Content + "</xsl:attribute>")));
            Assert.AreEqual(
                "<out a=\"1-2-x\"/>",
                Run(Root("<xsl:attribute name=\"a\" separator=\"-\">" + Content + "</xsl:attribute>")));
            Assert.AreEqual(
                "<out><!--1 2 x--></out>",
                Run(Root("<xsl:comment>" + Content + "</xsl:comment>")));

            // A select is a sequence of items too, joined with a space by default.
            Assert.AreEqual(
                "<out a=\"1 2\"/>",
                Run(Root("<xsl:attribute name=\"a\" select=\"1, 2\"/>")));
        }

        [TestMethod]
        public void AnAttributeValueTemplateMergesAdjacentTextNodesBeforeJoining()
        {
            // The function returns three text nodes. Merged first, they are one item, [0]; joined as three
            // they would be [ 0 ]. The suite's avt-1205.
            Assert.AreEqual(
                "<out a=\"[0][1]\"/>",
                Run(
                    "<xsl:function name=\"f:t\" as=\"text()*\"><xsl:param name=\"p\"/>"
                    + "<xsl:text>[</xsl:text><xsl:value-of select=\"$p\"/><xsl:text>]</xsl:text>"
                    + "</xsl:function>"
                    + "<xsl:template match=\"/\"><out a=\"{f:t(0)}{f:t(1)}\"/></xsl:template>"));
        }

        // ---- The sequence itself ---------------------------------------------------------------------------

        [TestMethod]
        public void AnXslSequenceMayHaveContentInPlaceOfASelect()
        {
            Assert.AreEqual(
                "<out>2</out>",
                Run(
                    "<xsl:variable name=\"v\" as=\"xs:integer*\">"
                    + "<xsl:sequence><xsl:sequence select=\"1, 2\"/></xsl:sequence></xsl:variable>"
                    + Root("<xsl:value-of select=\"count($v)\"/>")));

            // An xsl:fallback is the one child a select may stand beside; anything else beside a select is
            // the instruction saying two things.
            Assert.AreEqual(
                "<out>1</out>",
                Run(Root("<xsl:sequence select=\"1\"><xsl:fallback/></xsl:sequence>")));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Root("<xsl:sequence select=\"1\"><xsl:text>x</xsl:text></xsl:sequence>")));

            Assert.AreEqual("XTSE3185", error.Code);
        }

        [TestMethod]
        public void AValueThatCannotBeCastToTheDeclaredTypeIsTheVariablesError()
        {
            // The cast of 'nope' to a date fails, and the specification names the failure after the variable
            // that declared the type rather than after the cast: XTTE0570, which the suite's sequence-0132
            // expects.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:variable name=\"d\" as=\"xs:date\"><xsl:value-of select=\"'nope'\"/></xsl:variable>"
                    + Root("<xsl:value-of select=\"$d\"/>")));

            Assert.AreEqual("XTTE0570", error.Code);
        }
    }
}
