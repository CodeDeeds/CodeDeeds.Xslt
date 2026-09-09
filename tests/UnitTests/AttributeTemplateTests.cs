namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for attribute value templates, and for the text value templates that are the same thing
    /// written as content: where the braces end, what may stand between them, and what a run of text is.
    /// </summary>
    [TestClass]
    public sealed class AttributeTemplateTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private static string Run(string body, XsltVersion? implemented = null, string version = "3.0")
        {
            return new Xslt(
                "<xsl:stylesheet version=\"" + version + "\" " + Xsl + ">" + body + "</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Version = implemented ?? XsltVersion.V30,
                }).TransformXml("<doc><b id=\"1\"/><b id=\"3\"/></doc>");
        }

        private static string Root(string body)
        {
            return "<xsl:template match=\"/\">" + body + "</xsl:template>";
        }

        private static string Refuses(string body, XsltVersion? implemented = null, string version = "3.0")
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, implemented, version)).Code
                ?? string.Empty;
        }

        [TestMethod]
        public void ACommentInsideTheBracesHidesWhatItHolds()
        {
            // A comment is skipped whole, so a brace or a quotation mark inside one says nothing about
            // where the expression ends — and an apostrophe in it is not the start of a string.
            Assert.AreEqual(
                "<out test=\"someexp 23stuff\"/>",
                Run(Root("<out test=\"some{('exp', (: comments isn't }fun{ :) 23)}stuff\"/>")));

            // Comments nest, which is the whole reason for counting them rather than looking for the end.
            Assert.AreEqual(
                "<out test=\"ab\"/>",
                Run(Root("<out test=\"{'a'}{(: outer (: inner :) still outer :)}{'b'}\"/>")));

            // And the braces of a map still count: the closing brace of the expression is the last of them.
            Assert.AreEqual("<out test=\"1\"/>", Run(Root("<out test=\"{map { 'a': 1 }?a}\"/>")));
        }

        [TestMethod]
        public void AnEmbeddedExpressionMayBeAbsent()
        {
            // XSLT 3.0 lets the braces hold nothing and gives that the empty sequence, so the literal text
            // around them simply runs on.
            Assert.AreEqual(
                "<out a=\"xyz\" b=\"\" c=\"xyz\"/>",
                Run(Root("<out a=\"x{}y{}z\" b=\"{ }\" c=\"x{(:comment:)}y{  (:comment:) }z\"/>")));

            // The same of a text value template, where an empty one leaves an element with no content.
            Assert.AreEqual(
                "<out><a/><b>xyz</b></out>",
                Run(Root("<out xsl:expand-text=\"yes\"><a>{}</a><b>x{ (: a (:nested:) comment :) }y{}z</b></out>")));

            // XPath has no empty expression, and XSLT 2.0 did not make one: a 2.0 processor reads it as the
            // syntax error it is there. The grammar follows the processor rather than the stylesheet's claim.
            Assert.AreEqual(
                "XPST0003",
                Refuses(Root("<out a=\"x{}y\"/>"), XsltVersion.V20, "2.0"));
        }

        [TestMethod]
        public void ATextValueTemplateIsFoundInTheWholeRunOfText()
        {
            // Comments and processing instructions are removed before the stylesheet is read, so a run of
            // text with one in the middle is a single text node by then — and the brace that opens an
            // expression may stand in a different piece of the run from the one that closes it.
            Assert.AreEqual(
                "<out>The Lord of the Invisible Rings</out>",
                Run("<xsl:param name=\"p\" select=\"'Invisible'\"/>"
                    + Root("<out xsl:expand-text=\"yes\">The Lord of the "
                        + "{str<!--not in real life-->ing($p)} Rings</out>")));

            Assert.AreEqual(
                "<out>((5))</out>",
                Run(Root("<out xsl:expand-text=\"yes\">(({co<?pi data?>unt(1 to 5)}))</out>")));

            // An element divides one run from the next, being a node the preparation leaves standing, so
            // the braces on either side of one are two templates and not one.
            Assert.AreEqual(
                "<out>1<i/>3</out>",
                Run(Root("<out xsl:expand-text=\"yes\">{1}<i/>{3}</out>")));
        }

        [TestMethod]
        public void AComputedCollationIsReadWhereItIsEvaluated()
        {
            // The collation of an xsl:for-each-group is an attribute value template, so what it names is
            // not known until the instruction runs. A written one is refused when the stylesheet is read
            // and a computed one when it is evaluated; neither is quietly given code point ordering.
            const string Codepoint = "http://www.w3.org/2005/xpath-functions/collation/codepoint";

            Assert.AreEqual(
                "<out>13</out>",
                Run(Root("<out><xsl:for-each-group select=\"doc/b\" group-by=\"@id\" "
                    + "collation=\"{'" + Codepoint + "'}\">"
                    + "<xsl:value-of select=\"current-grouping-key()\"/></xsl:for-each-group></out>")));

            Assert.AreEqual(
                "XTDE1110",
                Refuses(Root("<out><xsl:for-each-group select=\"doc/b\" group-by=\"@id\" "
                    + "collation=\"{'urn:danish'}\">"
                    + "<xsl:value-of select=\"current-grouping-key()\"/></xsl:for-each-group></out>")));

            // And one written outright is still refused where the stylesheet is read.
            Assert.AreEqual(
                "XTDE1110",
                Refuses(Root("<out><xsl:for-each-group select=\"doc/b\" group-by=\"@id\" "
                    + "collation=\"urn:danish\">"
                    + "<xsl:value-of select=\"current-grouping-key()\"/></xsl:for-each-group></out>")));
        }
    }
}
