using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:deep-equal</c> over nodes: kind, name, attributes and children, with comments and
    /// processing instructions left out of the comparison.
    /// </summary>
    [TestClass]
    public sealed class DeepEqualTests
    {
        private static string Run(string declarations, string body)
        {
            string stylesheet =
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + declarations + "<xsl:template match=\"/\"><out>" + body + "</out></xsl:template></xsl:stylesheet>";

            return new Xslt(stylesheet, new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true })
                .TransformXml("<doc/>");
        }

        [TestMethod]
        public void NodesAreComparedAsNodes()
        {
            // Two elements with one string value are not equal for it: the names, the attributes and the
            // children all count, and a node against a value never agrees.
            Assert.AreEqual(
                "<out>false|true|false|false|true|false</out>",
                Run(
                    "<xsl:variable name=\"a\"><rtf>abc</rtf></xsl:variable>"
                    + "<xsl:variable name=\"b\" as=\"element()\"><doc><rtf>abc</rtf></doc></xsl:variable>"
                    + "<xsl:variable name=\"c\" as=\"element()\"><rtf x=\"1\" y=\"2\">abc</rtf></xsl:variable>"
                    + "<xsl:variable name=\"d\" as=\"element()\"><rtf y=\"2\" x=\"1\">abc</rtf></xsl:variable>"
                    + "<xsl:variable name=\"e\" as=\"element()\"><rtf x=\"1\" y=\"3\">abc</rtf></xsl:variable>",
                    "<xsl:value-of select=\"deep-equal($b, $a/*), deep-equal($b/rtf, $a/*), deep-equal($a/*, 'abc'), "
                    + "deep-equal($c, $a/*), deep-equal($c, $d), deep-equal($c, $e)\" separator=\"|\"/>"));
        }

        [TestMethod]
        public void CommentsAndProcessingInstructionsAreLeftOut()
        {
            // Children are compared once comments and processing instructions are dropped; a document node
            // compares its children; text is compared as text, and a comment against a comment by value.
            Assert.AreEqual(
                "<out>true|true|false|true|false</out>",
                Run(
                    "<xsl:variable name=\"a\"><e><xsl:comment>one</xsl:comment><f/>t<xsl:processing-instruction name=\"pi\">x</xsl:processing-instruction></e></xsl:variable>"
                    + "<xsl:variable name=\"b\"><e><f/><xsl:comment>two</xsl:comment>t</e></xsl:variable>"
                    + "<xsl:variable name=\"c\"><e><f/>T</e></xsl:variable>",
                    "<xsl:value-of select=\"deep-equal($a/e, $b/e), deep-equal($a, $b), deep-equal($a, $c), "
                    + "deep-equal($a/e/comment(), $b/e/comment()) = false(), deep-equal($a/e/f, $a/e/text())\" separator=\"|\"/>"));
        }
    }
}
