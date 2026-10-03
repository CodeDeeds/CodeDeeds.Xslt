using System.Xml;
using System.Xml.Xsl;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for four everyday things that allocated far more than what they produced — a temporary tree,
    /// the string value of an element with several text nodes, a union, and <c>format-number()</c> — for
    /// what they answer, which did not change, and what they allocate, which did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tree built without an estimate began with sixty-four entries in each of some twenty arrays and a
    /// name table of its own the same size: ten kilobytes for the value of a variable holding one
    /// element. An element's string value was gathered in a builder two and a half times the size of the
    /// string. A union made a set for either operand and a third for the result, which the identity
    /// template paid once for every element it copied, and <c>xsl:apply-templates</c> made a set of
    /// whatever it selected. <c>format-number()</c> made four strings of every number.
    /// </para>
    /// <para>
    /// The answers are asked of both backends, and of <c>XslCompiledTransform</c> where the stylesheet
    /// is one it can run. See <c>ConformanceNotes.md</c>, "Four things that cost more than they made".
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class EverydayAllocationTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Source =
            "<r xmlns:p=\"urn:p\" z=\"26\" a=\"1\">"
            + "<e id=\"1\" k=\"x\">one<b>two</b>three<!--c--><?pi d?>four</e>"
            + "<e id=\"2\"><deep><deeper><deepest>only</deepest></deeper></deep></e>"
            + "<e id=\"3\"/>"
            + "<e id=\"4\" p:q=\"pq\">  <s/>tail</e>"
            + "<n>29.99</n><n>9.995</n><n>0.5</n><n>1234567.891</n><n>-42.5</n><n>0</n><n>99.5</n><n>0.004</n>"
            + "<n>0.0000125</n><n>999.9996</n></r>";

        private static string Sheet(string version, string body, string declarations = "")
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:p=\"urn:p\">"
                + $"<xsl:output method=\"text\"/>{declarations}"
                + $"<xsl:template match=\"/r\">{body}</xsl:template></xsl:stylesheet>";
        }

        /// <summary>The one answer both backends write for a template body.</summary>
        private static string Writes(string version, string body, string declarations = "")
        {
            string stylesheet = Sheet(version, body, declarations);
            string? answer = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                string written = new Xslt(stylesheet, new XsltOptions { Backend = backend }).TransformXml(Source);

                answer ??= written;
                Assert.AreEqual(answer, written, $"the backends disagree about: {body}");
            }

            return answer!;
        }

        /// <summary>What <c>XslCompiledTransform</c> writes for the same body as a 1.0 stylesheet.</summary>
        private static string TheOracleWrites(string body, string declarations = "")
        {
            XslCompiledTransform reference = new XslCompiledTransform();
            using (XmlReader reader = XmlReader.Create(new StringReader(Sheet("1.0", body, declarations))))
            {
                reference.Load(reader);
            }

            StringWriter output = new StringWriter();
            using (XmlReader reader = XmlReader.Create(new StringReader(Source)))
            {
                reference.Transform(reader, null, output);
            }

            return output.ToString();
        }

        /// <summary>
        /// Asserts what a 1.0 body writes: the framework's processor first, so that the expectation is
        /// its and not this engine's, and then both backends at 1.0 and at 3.0.
        /// </summary>
        private static void AssertAtEveryVersion(string expected, string body, string declarations = "")
        {
            Assert.AreEqual(expected, TheOracleWrites(body, declarations), $"the oracle itself, about: {body}");
            Assert.AreEqual(expected, Writes("1.0", body, declarations), $"1.0: {body}");
            Assert.AreEqual(expected, Writes("3.0", body, declarations), $"3.0: {body}");
        }

        // ---- a union --------------------------------------------------------------------------------------

        [TestMethod]
        public void AUnionIsItsNodesInDocumentOrderWhicheverWayItIsWritten()
        {
            AssertAtEveryVersion("16", "<xsl:value-of select=\"count(@*|node())\"/>");

            // Attributes come after their element and before its children, in the order they were written.
            AssertAtEveryVersion(
                "id:1/8 k:2/8 :3/8 b:4/8 :5/8 :6/8 pi:7/8 :8/8 ",
                "<xsl:for-each select=\"e[1]/@*|e[1]/node()\">"
                + "<xsl:value-of select=\"concat(name(), ':', position(), '/', last(), ' ')\"/></xsl:for-each>");

            // Operands out of order are put in order, and a node selected twice is there once.
            AssertAtEveryVersion("123", "<xsl:for-each select=\"e[3]|e[1]|e[2]\"><xsl:value-of select=\"@id\"/></xsl:for-each>");
            AssertAtEveryVersion("4", "<xsl:value-of select=\"count(e|e[2]|e)\"/>");

            AssertAtEveryVersion(
                "za,za",
                "<xsl:for-each select=\"@z|@a\"><xsl:value-of select=\"name()\"/></xsl:for-each>,"
                + "<xsl:for-each select=\"@a|@z\"><xsl:value-of select=\"name()\"/></xsl:for-each>");

            // Attributes, elements and text of four elements, each operand in order and the three
            // interleaved: this is the case that needs the ordering key and not the ids.
            AssertAtEveryVersion(
                "id.k..b...id.deep.id.id.p:q..s..",
                "<xsl:for-each select=\"e/@*|e/*|e/text()\"><xsl:value-of select=\"concat(name(), '.')\"/></xsl:for-each>");

            AssertAtEveryVersion(
                "e.id.k.b.e.id.deep.e.id.e.id.p:q.s.",
                "<xsl:for-each select=\"e/*|e/@*|e\"><xsl:value-of select=\"concat(name(), '.')\"/></xsl:for-each>");
        }

        [TestMethod]
        public void AUnionWithAnOperandThatSelectsNothingIsTheOther()
        {
            AssertAtEveryVersion(
                "4,4,0",
                "<xsl:value-of select=\"count(nothing|e)\"/>,<xsl:value-of select=\"count(e|nothing)\"/>,"
                + "<xsl:value-of select=\"count(nothing|nada)\"/>");
        }

        [TestMethod]
        public void AUnionOfUnionsAndOfReverseAxesIsStillInDocumentOrder()
        {
            AssertAtEveryVersion(
                "4,b",
                "<xsl:value-of select=\"count(e/b|e/deep/deeper|//deepest|/r)\"/>,"
                + "<xsl:value-of select=\"name((//deepest|e/b|/r)[2])\"/>");

            AssertAtEveryVersion(
                "4,1234",
                "<xsl:value-of select=\"count(e[1]/following-sibling::e|e[3]/preceding-sibling::e)\"/>,"
                + "<xsl:for-each select=\"e[3]/preceding-sibling::e|e[2]/following-sibling::e\">"
                + "<xsl:value-of select=\"@id\"/></xsl:for-each>");

            AssertAtEveryVersion(
                "4,3,47",
                "<xsl:value-of select=\"count(e[@id|b])\"/>,<xsl:value-of select=\"count(e[b|s|deep])\"/>,"
                + "<xsl:value-of select=\"count(//node()|//@*)\"/>");
        }

        [TestMethod]
        public void NamespaceNodesComeBeforeAttributesInAUnionAndBothBeforeChildren()
        {
            AssertAtEveryVersion(
                "4,p:q,s",
                "<xsl:value-of select=\"count(e[4]/namespace::*|e[4]/@*)\"/>,"
                + "<xsl:value-of select=\"name((e[4]/namespace::*|e[4]/@*)[last()])\"/>,"
                + "<xsl:value-of select=\"name((e[4]/@*|e[4]/namespace::*|e[4]/s)[last()])\"/>");
        }

        [TestMethod]
        public void AUnionWithAVariableOrAnotherTreeGoesTheGeneralWayAndAnswersTheSame()
        {
            // A variable may hold nodes of another document, so a union with one is not gathered in place.
            AssertAtEveryVersion(
                "2,12",
                "<xsl:variable name=\"v\" select=\"e[2]\"/><xsl:value-of select=\"count($v|e[1]|e[2])\"/>,"
                + "<xsl:for-each select=\"$v|e[1]\"><xsl:value-of select=\"@id\"/></xsl:for-each>");

            Assert.AreEqual(
                "6,2,4",
                Writes(
                    "3.0",
                    "<xsl:variable name=\"t\"><x/><y/></xsl:variable><xsl:value-of select=\"count($t/*|e)\"/>,"
                    + "<xsl:value-of select=\"count($t/*|$t/x)\"/>,<xsl:value-of select=\"count((e, e[1]) | ())\"/>"));

            Assert.AreEqual(
                "onetwothreefouronly3,b s",
                Writes(
                    "3.0",
                    "<xsl:value-of select=\"(e[2], e[1]) | e[3] ! @id\" separator=\"\"/>,"
                    + "<xsl:value-of select=\"(e/b union e/s union e/b)/name()\"/>"));
        }

        // ---- xsl:apply-templates over a selection ------------------------------------------------------------

        private const string Positions =
            "<xsl:template match=\"@*|*\" mode=\"p\">"
            + "<xsl:value-of select=\"concat(name(), @id, ':', position(), '/', last(), ' ')\"/></xsl:template>";

        [TestMethod]
        public void TemplatesAppliedToASelectionSeeTheirPlaceInIt()
        {
            AssertAtEveryVersion("e1:1/4 e2:2/4 e3:3/4 e4:4/4 ", "<xsl:apply-templates select=\"e\" mode=\"p\"/>", Positions);

            // A reverse axis selects in document order all the same.
            AssertAtEveryVersion(
                "e1:1/3 e2:2/3 e3:3/3 ", "<xsl:apply-templates select=\"e[4]/preceding-sibling::e\" mode=\"p\"/>", Positions);

            AssertAtEveryVersion(
                "id:1/7 k:2/7 b:3/7 id:4/7 id:5/7 id:6/7 p:q:7/7 ",
                "<xsl:apply-templates select=\"e/@*|e/b|nothing\" mode=\"p\"/>",
                Positions);

            // Sorted, which is not the route a plain selection takes and has to go on working beside it.
            AssertAtEveryVersion(
                "e4:1/4 e3:2/4 e2:3/4 e1:4/4 ",
                "<xsl:apply-templates select=\"e\" mode=\"p\"><xsl:sort select=\"@id\" order=\"descending\"/></xsl:apply-templates>",
                Positions);
        }

        [TestMethod]
        public void TemplatesAppliedToNothingDoNothingAndParametersArriveWithTheRest()
        {
            AssertAtEveryVersion(
                "[bW1]",
                "<xsl:apply-templates select=\"nothing\" mode=\"q\"/>[<xsl:apply-templates select=\"e[1]/b\" mode=\"q\">"
                + "<xsl:with-param name=\"w\" select=\"'W'\"/></xsl:apply-templates>]",
                "<xsl:template match=\"*\" mode=\"q\"><xsl:param name=\"w\" select=\"'-'\"/>"
                + "<xsl:value-of select=\"concat(name(), $w, count(../*))\"/></xsl:template>");
        }

        [TestMethod]
        public void TemplatesAppliedToNodesOfAnotherTreeRunInThatTree()
        {
            Assert.AreEqual(
                "x:1/2 y:2/2 ",
                Writes(
                    "3.0",
                    "<xsl:variable name=\"t\"><x/><y/></xsl:variable><xsl:apply-templates select=\"$t/*\" mode=\"o\"/>",
                    "<xsl:template match=\"*\" mode=\"o\">"
                    + "<xsl:value-of select=\"concat(name(), ':', position(), '/', last(), ' ')\"/>"
                    + "</xsl:template>"));
        }

        [TestMethod]
        public void TheIdentityTemplateCopiesWhatCopyOfCopies()
        {
            const string Identity =
                "<xsl:template match=\"@*|node()\"><xsl:copy><xsl:apply-templates select=\"@*|node()\"/></xsl:copy></xsl:template>";

            string copied = new Xslt(
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\"><xsl:template match=\"/\"><xsl:copy-of select=\".\"/>"
                + "</xsl:template></xsl:stylesheet>").TransformXml(Source);

            StringAssert.Contains(copied, "<e id=\"4\" p:q=\"pq\">  <s/>tail</e>");

            foreach (string version in new[] { "1.0", "3.0" })
            {
                foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                {
                    string written = new Xslt(
                        $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\">{Identity}</xsl:stylesheet>",
                        new XsltOptions { Backend = backend }).TransformXml(Source);

                    Assert.AreEqual(copied, written, $"{version} on the {backend} backend");
                }
            }
        }

        [TestMethod]
        public void ALongRunOfSiblingsIsWalkedOneTemplateAtATimeEitherSideOfTheCall()
        {
            // Each template applies templates to the sibling after it, so the selection is one node and
            // the walk is as deep as the run is long: with the call last it is made in the template's own
            // place, and with something after it every level is still waiting, each holding the list its
            // selection was gathered in, long after the pool has no more lists to lend.
            const int Count = 5000;
            string source = "<r>" + string.Concat(Enumerable.Repeat("<e/>", Count)) + "</r>";

            foreach (string template in new[]
            {
                "<xsl:text>.</xsl:text><xsl:apply-templates select=\"following-sibling::e[1]\"/>",
                "<xsl:apply-templates select=\"following-sibling::e[1]\"/><xsl:text>.</xsl:text>",
            })
            {
                foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                {
                    string written = new Xslt(
                        $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\"><xsl:output method=\"text\"/>"
                        + "<xsl:template match=\"/\"><xsl:apply-templates select=\"r/e[1]\"/></xsl:template>"
                        + $"<xsl:template match=\"e\">{template}</xsl:template></xsl:stylesheet>",
                        new XsltOptions { Backend = backend }).TransformXml(source);

                    Assert.AreEqual(new string('.', Count), written, $"{template} on the {backend} backend");
                }
            }
        }

        // ---- the string value of an element ------------------------------------------------------------------

        [TestMethod]
        public void TheStringValueOfAnElementIsEveryTextNodeBeneathItInOrder()
        {
            AssertAtEveryVersion(
                "[onetwothreefour][only][][  tail][81][two]",
                "[<xsl:value-of select=\"e[1]\"/>][<xsl:value-of select=\"e[2]\"/>][<xsl:value-of select=\"e[3]\"/>]"
                + "[<xsl:value-of select=\"e[4]\"/>][<xsl:value-of select=\"string-length(.)\"/>]"
                + "[<xsl:value-of select=\"string(e[1]/b)\"/>]");

            AssertAtEveryVersion(
                "[onetwothreefouronly  tail29.999.9950.51234567.891-42.5099.50.0040.0000125999.9996]",
                "[<xsl:value-of select=\".\"/>]");

            AssertAtEveryVersion(
                "[onetwothreefour][only][][  tail][2][1]",
                "<xsl:for-each select=\"e\"><xsl:value-of select=\"concat('[', ., ']')\"/></xsl:for-each>"
                + "[<xsl:value-of select=\"e[. = 'only']/@id\"/>][<xsl:value-of select=\"count(e[contains(., 'two')])\"/>]");
        }

        [TestMethod]
        public void TheStringValueOfAnElementWithOneTextNodeBeneathItIsThatNodesOwnString()
        {
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader(Source));

            int root = tree.FirstChildOf(XdmTree.RootNode);
            int first = tree.FirstChildOf(root);
            int second = tree.NextSiblingOf(first);
            int third = tree.NextSiblingOf(second);

            Assert.AreEqual("onetwothreefour", tree.StringValueOf(first));
            Assert.AreEqual("only", tree.StringValueOf(second));
            Assert.AreEqual(string.Empty, tree.StringValueOf(third));

            // <e><deep><deeper><deepest>only: the text is three elements down, and no copy is made of it
            // at any of them.
            int deepest = tree.FirstChildOf(tree.FirstChildOf(tree.FirstChildOf(second)));
            int text = tree.FirstChildOf(deepest);

            Assert.AreEqual(NodeKind.Text, tree.KindOf(text));
            Assert.AreSame(tree.StringValueOf(text), tree.StringValueOf(second));
            Assert.AreSame(tree.StringValueOf(text), tree.StringValueOf(deepest));
        }

        // ---- format-number ---------------------------------------------------------------------------------

        private static string Formatted(string picture, string value = ".")
        {
            return $"<xsl:for-each select=\"n\"><xsl:value-of select=\"format-number({value}, '{picture}')\"/>|</xsl:for-each>";
        }

        [TestMethod]
        public void NumbersAreFormattedAsTheFrameworkFormatsThem()
        {
            // The values are 29.99, 9.995, 0.5, 1234567.891, -42.5, 0, 99.5, 0.004, 0.0000125 and 999.9996:
            // one that needs no rounding, ones that round up through a run of nines, ones that round away
            // to nothing, and one below every place a picture here keeps.
            AssertAtEveryVersion("29.99|10.00|0.50|1234567.89|-42.50|0.00|99.50|0.00|0.00|1000.00|", Formatted("0.00"));
            AssertAtEveryVersion("29.99|10.00|0.50|1,234,567.89|-42.50|0.00|99.50|0.00|0.00|1,000.00|", Formatted("#,##0.00"));
            AssertAtEveryVersion("30|10|1|1234568|-43|0|100|0|0|1000|", Formatted("0"));
            AssertAtEveryVersion(
                "0029.990|0009.995|0000.500|1234567.891|-0042.500|0000.000|0099.500|0000.004|0000.000|1000.000|",
                Formatted("0000.000"));
            AssertAtEveryVersion("30.0|10.0|0.5|1234567.9|(42.5)|0.0|99.5|0.0|0.0|1000.0|", Formatted("0.0;(0.0)"));
        }

        [TestMethod]
        public void WhatAPictureLeavesOptionalIsWrittenAsEachVersionSays()
        {
            // 1.0 writes nothing at all for a zero no digit of the picture insists on; from 2.0 the first
            // fraction place is made to appear.
            Assert.AreEqual("30|10|.5|1234567.9|-42.5||99.5|||1000|", TheOracleWrites(Formatted("#.#")));
            Assert.AreEqual("30|10|.5|1234567.9|-42.5||99.5|||1000|", Writes("1.0", Formatted("#.#")));
            Assert.AreEqual("30.0|10.0|.5|1234567.9|-42.5|.0|99.5|.0|.0|1000.0|", Writes("3.0", Formatted("#.#")));

            Assert.AreEqual("30|10|1|1,234,568|-43||100|||1,000|", TheOracleWrites(Formatted("#,###")));
            Assert.AreEqual("30|10|1|1,234,568|-43||100|||1,000|", Writes("1.0", Formatted("#,###")));

            // A percentage of 9.995 is 999.5 as a decimal and a little under it as a double.
            Assert.AreEqual("2999%|1000%|50%|123456789%|-4250%|0%|9950%|0%|0%|100000%|", TheOracleWrites(Formatted("0%")));
            Assert.AreEqual("2999%|1000%|50%|123456789%|-4250%|0%|9950%|0%|0%|100000%|", Writes("1.0", Formatted("0%")));
            Assert.AreEqual("2999%|999%|50%|123456789%|-4250%|0%|9950%|0%|0%|100000%|", Writes("3.0", Formatted("0%")));
        }

        [TestMethod]
        public void ANumberBelowEveryPlaceThePictureKeepsIsZero()
        {
            // Two places or more under the last one kept, a number has no digit left and no carry to
            // send up. That value was a struct with no string in it, and reading its fraction threw a
            // NullReferenceException: format-number(0.004, '0') did not answer at all.
            foreach (string version in new[] { "1.0", "3.0" })
            {
                Assert.AreEqual(
                    "0|0.00|0.0|-0|000.0|",
                    Writes(
                        version,
                        "<xsl:value-of select=\"format-number(0.004, '0')\"/>|"
                        + "<xsl:value-of select=\"format-number(0.0009, '0.00')\"/>|"
                        + "<xsl:value-of select=\"format-number(0.000001, '0.0')\"/>|"
                        + "<xsl:value-of select=\"format-number(-0.004, '0')\"/>|"
                        + "<xsl:value-of select=\"format-number(0.0125, '000.0')\"/>|"),
                    version);
            }

            Assert.AreEqual(
                "0|0.00|0.0|-0|000.0|",
                TheOracleWrites(
                    "<xsl:value-of select=\"format-number(0.004, '0')\"/>|"
                    + "<xsl:value-of select=\"format-number(0.0009, '0.00')\"/>|"
                    + "<xsl:value-of select=\"format-number(0.000001, '0.0')\"/>|"
                    + "<xsl:value-of select=\"format-number(-0.004, '0')\"/>|"
                    + "<xsl:value-of select=\"format-number(0.0125, '000.0')\"/>|"));
        }

        [TestMethod]
        public void AnExponentIsWrittenWithTheDigitsItsPictureAsksFor()
        {
            Assert.AreEqual(
                "3.00e1|10.00e0|5.00e-1|1.23e6|-4.25e1|0.00e0|9.95e1|4.00e-3|1.25e-5|10.00e2|",
                Writes("3.0", Formatted("0.00e0")));

            Assert.AreEqual(
                "30.0e000|100.0e-001|50.0e-002|12.3e005|-42.5e000|00.0e000|99.5e000|40.0e-004|12.5e-006|100.0e001|",
                Writes("3.0", Formatted("00.0e000")));

            Assert.AreEqual(
                ".30e2|1.00e1|.50e0|.12e7|-.43e2|.00e0|1.00e2|.40e-2|.13e-4|1.00e3|",
                Writes("3.0", Formatted(".00e0")));
        }

        [TestMethod]
        public void ATypedNumberIsFormattedFromItsOwnDigitsHoweverManyThereAre()
        {
            Assert.AreEqual(
                "29.99|9.995|0.5|1,234,567.891|-42.5|0.0|99.5|0.004|0.0|999.9996|",
                Writes("3.0", Formatted("#,##0.0###", "xs:decimal(.)")));

            // Eighteen digits, which a double cannot hold; sixty, which stand in an array of their own
            // because no stack space set aside for a number is that long; and a decimal's twenty-nine.
            Assert.AreEqual(
                "123,456,789,012,345,678"
                + "|123,456,789,012,345,678,901,234,567,890,123,456,789,012,345,678,901,234,567,890"
                + "|-123456789012345678901234567890123456789012345678901234567890.0"
                + "|1234567890123456789012345678.90"
                + "|0.1234567890123456789012345678",
                Writes(
                    "3.0",
                    "<xsl:value-of select=\"format-number(123456789012345678, '#,##0')\"/>|"
                    + "<xsl:value-of select=\"format-number(123456789012345678901234567890123456789012345678901234567890, '#,##0')\"/>|"
                    + "<xsl:value-of select=\"format-number(-123456789012345678901234567890123456789012345678901234567890, '0.0')\"/>|"
                    + "<xsl:value-of select=\"format-number(1234567890123456789012345678.9, '0.00')\"/>|"
                    + "<xsl:value-of select=\"format-number(0.1234567890123456789012345678, '0.0###########################')\"/>"));

            Assert.AreEqual(
                "1" + new string('0', 308)
                + "|1,000,000,000,000,000,000,000,000,000,000|0.0000001|0.1000000015"
                + "|99999999999999999999|9223372036854775807|-9223372036854775808",
                Writes(
                    "3.0",
                    "<xsl:value-of select=\"format-number(1e308, '0')\"/>|"
                    + "<xsl:value-of select=\"format-number(1e30, '#,##0')\"/>|"
                    + "<xsl:value-of select=\"format-number(1e-7, '0.0#########')\"/>|"
                    + "<xsl:value-of select=\"format-number(xs:float(0.1), '0.0#########')\"/>|"
                    + "<xsl:value-of select=\"format-number(99999999999999999999, '0')\"/>|"
                    + "<xsl:value-of select=\"format-number(9223372036854775807, '0')\"/>|"
                    + "<xsl:value-of select=\"format-number(-9223372036854775808, '0')\"/>"));
        }

        [TestMethod]
        public void RoundingCarriesThroughEveryNineAndOffTheLeftEnd()
        {
            Assert.AreEqual(
                "1.00|1.00|10.00|100.0|0.1|0.0|1|2|05|Infinity|123,450.00%",
                Writes(
                    "3.0",
                    "<xsl:value-of select=\"format-number(0.995, '0.00')\"/>|"
                    + "<xsl:value-of select=\"format-number(xs:decimal('0.995'), '0.00')\"/>|"
                    + "<xsl:value-of select=\"format-number(xs:decimal('9.995'), '0.00')\"/>|"
                    + "<xsl:value-of select=\"format-number(xs:decimal('99.999'), '0.0')\"/>|"
                    + "<xsl:value-of select=\"format-number(xs:decimal('0.05'), '0.0')\"/>|"
                    + "<xsl:value-of select=\"format-number(xs:decimal('0.04'), '0.0')\"/>|"
                    + "<xsl:value-of select=\"format-number(xs:decimal('0.5'), '0')\"/>|"
                    + "<xsl:value-of select=\"format-number(xs:decimal('1.5'), '0')\"/>|"
                    + "<xsl:value-of select=\"format-number(5, '00')\"/>|"
                    + "<xsl:value-of select=\"format-number(1 div 0e0, '0')\"/>|"
                    + "<xsl:value-of select=\"format-number(1234.5, '#,##0.00%')\"/>"));

            // What 1.0 has one numeric type for: a division by zero is an infinity there and an error
            // from 2.0, and eighteen digits are a double's fifteen.
            const string Legacy =
                "<xsl:value-of select=\"format-number(1 div 0, '0')\"/>|<xsl:value-of select=\"format-number(0 div 0, '0')\"/>|"
                + "<xsl:value-of select=\"format-number(-1 div 0, '0.0')\"/>|"
                + "<xsl:value-of select=\"format-number(1 div 3, '0.00000000000000000000')\"/>|"
                + "<xsl:value-of select=\"format-number(123456789012345678, '#,##0')\"/>";

            const string Expected = "Infinity|NaN|-Infinity|0.33333333333333300000|123,456,789,012,346,000";

            Assert.AreEqual(Expected, TheOracleWrites(Legacy), "the oracle itself");
            Assert.AreEqual(Expected, Writes("1.0", Legacy));
        }

        // ---- a temporary tree ------------------------------------------------------------------------------

        [TestMethod]
        public void ATemporaryTreeThatOutgrowsTheRoomItBeganWithIsBuiltWhole()
        {
            // A hundred elements with three attributes and a namespace each, and one branch ten deep:
            // past the eight nodes, the no attributes, the no namespaces and the four open elements a
            // tree built without an estimate begins with, and past the thirty-two nodes after which
            // names go through the cache.
            Assert.AreEqual(
                "100 300 10100 101 q:o deep 311 50 urn:q",
                Writes(
                    "3.0",
                    "<xsl:variable name=\"t\"><xsl:for-each select=\"1 to 100\">"
                    + "<x n=\"{.}\" m=\"{. * 2}\" xmlns:q=\"urn:q\" q:o=\"{. + 1}\"><y><xsl:value-of select=\".\"/></y></x>"
                    + "</xsl:for-each><d1><d2><d3><d4><d5><d6><d7><d8><d9><d10>deep</d10></d9></d8></d7></d6></d5></d4></d3></d2></d1>"
                    + "</xsl:variable>"
                    + "<xsl:value-of select=\"count($t/x), count($t//@*), sum($t/x/@m), $t/x[100]/@*[last()], "
                    + "name($t/x[77]/@*[3]), string($t/d1), count($t//node()), $t/x[50]/y, namespace-uri($t/x[9]/@*[3])\"/>"));
        }

        [TestMethod]
        public void EveryKindOfNodeMayBeAnItemWithATreeOfItsOwn()
        {
            Assert.AreEqual(
                "8:=one;a=;at=v;=c;pi=d;pp=urn:pp;atom=3;b=text;",
                Writes(
                    "3.0",
                    "<xsl:variable name=\"t\" as=\"item()*\"><xsl:text>one</xsl:text><a/>"
                    + "<xsl:attribute name=\"at\">v</xsl:attribute><xsl:comment>c</xsl:comment>"
                    + "<xsl:processing-instruction name=\"pi\">d</xsl:processing-instruction>"
                    + "<xsl:namespace name=\"pp\">urn:pp</xsl:namespace><xsl:sequence select=\"3\"/><b><c/>text</b>"
                    + "</xsl:variable><xsl:value-of select=\"count($t)\"/>:<xsl:for-each select=\"$t\">"
                    + "<xsl:value-of select=\"if (. instance of node()) then concat(name(), '=', ., ';') "
                    + "else concat('atom=', ., ';')\"/></xsl:for-each>"));
        }

        [TestMethod]
        public void AVariableMadeOnceForEveryNodeHoldsThatNodesOwnTree()
        {
            Assert.AreEqual(
                "1=onetwothreefour;2=only;3=;4=  tail;",
                Writes(
                    "3.0",
                    "<xsl:for-each select=\"e\"><xsl:variable name=\"v\"><w id=\"{@id}\"><xsl:value-of select=\".\"/></w>"
                    + "</xsl:variable><xsl:value-of select=\"concat($v/w/@id, '=', $v/w, ';')\"/></xsl:for-each>"));

            AssertAtEveryVersion(
                "t1;t2;t3;t4;",
                "<xsl:for-each select=\"e\"><xsl:variable name=\"v\">t<xsl:value-of select=\"@id\"/></xsl:variable>"
                + "<xsl:value-of select=\"concat($v, ';')\"/></xsl:for-each>");

            Assert.AreEqual(
                "1;2;3;4;",
                Writes(
                    "3.0",
                    "<xsl:for-each select=\"e\"><xsl:variable name=\"v\" as=\"xs:string\"><xsl:value-of select=\"@id\"/>"
                    + "</xsl:variable><xsl:try><xsl:value-of select=\"concat($v, ';')\"/><xsl:catch>!</xsl:catch></xsl:try>"
                    + "</xsl:for-each>"));
        }

        [TestMethod]
        public void ABuilderGivenNoEstimateGrowsToWhateverItIsGiven()
        {
            // Built by hand, past every first size: forty elements each with two attributes and a
            // namespace, names repeated from the same strings so that the cache is used once the tree
            // is large enough to have one, and a branch twelve deep.
            XdmTreeBuilder builder = new XdmTreeBuilder();
            builder.StartElement(string.Empty, string.Empty, "root");

            for (int i = 0; i < 40; i++)
            {
                builder.StartElement("q", "urn:q", "item");
                builder.AddNamespaceDeclaration("q", "urn:q");
                builder.AddAttribute(string.Empty, string.Empty, "n", i.ToString());
                builder.AddAttribute("q", "urn:q", "m", (i * 2).ToString());
                builder.AddText("t" + i);
                builder.EndElement();
            }

            for (int depth = 0; depth < 12; depth++)
            {
                builder.StartElement(string.Empty, string.Empty, "d" + depth);
            }

            builder.AddText("deep");

            for (int depth = 0; depth < 12; depth++)
            {
                builder.EndElement();
            }

            builder.EndElement();
            XdmTree tree = builder.Finish();

            // The document, its element, forty items each with a text node, twelve nested and their text.
            Assert.AreEqual(1 + 1 + 80 + 12 + 1, tree.NodeCount);

            int root = tree.FirstChildOf(XdmTree.RootNode);
            int item = tree.FirstChildOf(root);

            for (int i = 0; i < 40; i++)
            {
                Assert.AreEqual("item", tree.NameTable.GetLocalName(tree.FingerprintOf(item)), $"item {i}");
                Assert.AreEqual("q:item", tree.NameTable.GetQualifiedName(tree.NameCodeOf(item)), $"item {i}");
                Assert.AreEqual(2, tree.AttributeCountOf(item), $"item {i}");
                Assert.AreEqual(i.ToString(), tree.StringValueOf(tree.AttributeAt(item, 0)), $"item {i}");
                Assert.AreEqual((i * 2).ToString(), tree.StringValueOf(tree.AttributeAt(item, 1)), $"item {i}");
                Assert.AreEqual("q:m", tree.NameTable.GetQualifiedName(tree.NameCodeOf(tree.AttributeAt(item, 1))), $"item {i}");
                Assert.AreEqual("urn:q", tree.ResolvePrefix(item, "q"), $"item {i}");
                Assert.AreEqual("t" + i, tree.StringValueOf(item), $"item {i}");
                item = tree.NextSiblingOf(item);
            }

            // What follows the last item is the branch, twelve deep with the text at the bottom.
            Assert.AreEqual("d0", tree.NameTable.GetLocalName(tree.FingerprintOf(item)));
            Assert.AreEqual("deep", tree.StringValueOf(item));
            Assert.AreEqual(14, tree.DepthOf(tree.SubtreeEndOf(item)));
        }

        // ---- what they allocate ----------------------------------------------------------------------------

        /// <summary>A thousand items, each with an attribute to format and more than one text node.</summary>
        private static readonly XdmTree Thousand = XdmTreeBuilder.FromXmlText(
            "<r>" + string.Concat(Enumerable.Range(1, 1000).Select(i => $"<i n=\"{i}.125\">one<b>two</b>three</i>")) + "</r>");

        /// <summary>
        /// Bytes allocated by one transformation of the thousand items, once it has been run enough to
        /// have settled, the result going to a writer that keeps none of it.
        /// </summary>
        private static long BytesToTransform(string stylesheet, XsltBackend backend)
        {
            Xslt transform = new Xslt(stylesheet, new XsltOptions { Backend = backend });

            for (int i = 0; i < 20; i++)
            {
                transform.Transform(Thousand, TextWriter.Null);
            }

            long best = long.MaxValue;

            for (int round = 0; round < 5; round++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                transform.Transform(Thousand, TextWriter.Null);
                best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            return best;
        }

        /// <summary>Bytes for a body run once for each of the thousand items.</summary>
        private static long BytesForEachItem(string body, string version, XsltBackend backend, string declarations = "")
        {
            return BytesToTransform(
                $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                + $"<xsl:output method=\"text\"/>{declarations}"
                + $"<xsl:template match=\"/r\"><xsl:for-each select=\"i\">{body}</xsl:for-each></xsl:template></xsl:stylesheet>",
                backend);
        }

        /// <summary>
        /// Asserts how many bytes a body allocates each time it runs, over what another body does.
        /// </summary>
        private static void AssertBytesEach(int atMost, string body, string floor, string declarations = "")
        {
            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                foreach (string version in new[] { "1.0", "3.0" })
                {
                    long each = (BytesForEachItem(body, version, backend, declarations)
                        - BytesForEachItem(floor, version, backend, declarations)) / 1000;

                    Assert.IsLessThanOrEqualTo(
                        atMost,
                        each,
                        $"'{body}' allocates {each} bytes each time at {version} on the {backend} backend");
                }
            }
        }

        [TestMethod]
        public void ATemporaryTreeOfAFewNodesCostsAFractionOfWhatItDid()
        {
            // Ten thousand three hundred bytes for each of these before: the builder's arrays at
            // sixty-four entries, its name cache, and a name table of the same size made for the tree.
            AssertBytesEach(
                TemporaryTreeBytes,
                "<xsl:variable name=\"v\"><w><xsl:value-of select=\"@n\"/></w></xsl:variable><xsl:value-of select=\"$v/w\"/>",
                "<xsl:value-of select=\"@n\"/>");
        }

        [TestMethod]
        public void TheStringValueOfAnElementCostsItsStringAndNoMore()
        {
            // 'onetwothree' is eleven characters, which is a string of forty-eight bytes. It was a
            // builder, the builder's first chunk and the string: about a hundred and fifty more.
            AssertBytesEach(56, "<xsl:value-of select=\".\"/>", "<xsl:value-of select=\"@n\"/>");
        }

        [TestMethod]
        public void FormattingANumberCostsItsResultAndNoMore()
        {
            // The result is a string of up to eight characters, forty bytes; the digits kept, the two
            // halves either side of the point and the padding were four strings more. The second picture
            // rounds, which had an array and two strings of its own.
            AssertBytesEach(48, "<xsl:value-of select=\"format-number(@n, '0.000')\"/>", "<xsl:value-of select=\"@n\"/>");
            AssertBytesEach(48, "<xsl:value-of select=\"format-number(@n, '#,##0.0')\"/>", "<xsl:value-of select=\"@n\"/>");
        }

        [TestMethod]
        public void ApplyingTemplatesToASelectionAllocatesNothingForTheSelection()
        {
            const string Rule = "<xsl:template match=\"node()|@*\" mode=\"m\"/>";

            // Eighty bytes for a path and two hundred and forty for a union, each time, before.
            AssertBytesEach(0, "<xsl:apply-templates select=\"b\" mode=\"m\"/>", string.Empty, Rule);
            AssertBytesEach(0, "<xsl:apply-templates select=\"@*|node()\" mode=\"m\"/>", string.Empty, Rule);
            AssertBytesEach(0, "<xsl:apply-templates select=\"text()|b|@n\" mode=\"m\"/>", string.Empty, Rule);
        }

        [TestMethod]
        public void TheIdentityTemplateAllocatesNoMoreThanCopyingTheDocumentDoes()
        {
            const string Identity =
                "<xsl:template match=\"@*|node()\"><xsl:copy><xsl:apply-templates select=\"@*|node()\"/></xsl:copy></xsl:template>";

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                XsltOptions options = new XsltOptions { Backend = backend };

                Xslt copies = new Xslt(
                    $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\"><xsl:template match=\"/\"><xsl:copy-of select=\".\"/>"
                    + "</xsl:template></xsl:stylesheet>",
                    options);

                Xslt applies = new Xslt($"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">{Identity}</xsl:stylesheet>", options);

                // Turn about, and the least each ever allocated: writing a copied element allocates in
                // code the runtime has not yet optimised and nothing once it has, a few hundred bytes
                // an element that both of these pay or neither does. Measured one after the other, the
                // first could be read before that had settled and the second after.
                long copyOf = long.MaxValue;
                long identity = long.MaxValue;

                for (int round = 0; round < 60; round++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    copies.Transform(Thousand, TextWriter.Null);
                    long between = GC.GetAllocatedBytesForCurrentThread();
                    applies.Transform(Thousand, TextWriter.Null);
                    long after = GC.GetAllocatedBytesForCurrentThread();

                    if (round >= 20)
                    {
                        copyOf = Math.Min(copyOf, between - before);
                        identity = Math.Min(identity, after - between);
                    }
                }

                // Two thousand elements, each of which applied templates to a union: 233 bytes apiece,
                // which was most of half a megabyte. What is allowed here is a few bytes an element.
                Assert.IsLessThan(
                    copyOf + 16_000,
                    identity,
                    $"the identity template allocates {identity} bytes and xsl:copy-of {copyOf} on the {backend} backend");
            }
        }

        /// <summary>
        /// What a temporary tree of an element and a text node may cost: the tree, its builder, a name
        /// table and the arrays of all three at their first sizes.
        /// </summary>
        private const int TemporaryTreeBytes = 3500;
    }
}
