using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the XML declaration going first whatever comes before the document element: a comment
    /// or processing instruction written while the output method is still undecided waits for it.
    /// </summary>
    [TestClass]
    public sealed class SerializationOrderTests
    {
        private static string Run(string body, string? output = null)
        {
            string stylesheet =
                "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + (output ?? string.Empty)
                + "<xsl:template match=\"/\">" + body + "</xsl:template></xsl:stylesheet>";

            return new Xslt(stylesheet, new XsltOptions { Version = XsltVersion.V20 }).TransformXml("<doc/>");
        }

        [TestMethod]
        public void TheDeclarationPrecedesATopLevelComment()
        {
            // No xsl:output: the first element decides the method, and the comment before it has to wait
            // so that the declaration can go ahead of it.
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!-- first --><?pi data?><root/>",
                Run("<xsl:comment> first </xsl:comment><xsl:processing-instruction name=\"pi\">data</xsl:processing-instruction><root/>"));

            // An html document element still makes the method html, comment and all, and then there is no
            // declaration to write.
            Assert.AreEqual(
                "<!-- first --><html></html>",
                Run("<xsl:comment> first </xsl:comment><html/>"));

            // A document that is a comment and nothing else is an XML document.
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!-- alone -->",
                Run("<xsl:comment> alone </xsl:comment>"));

            // Text after the comment settles the method as xml, and keeps its place behind the comment.
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!-- c -->text<root/>",
                Run("<xsl:comment> c </xsl:comment><xsl:text>text</xsl:text><root/>"));
        }
    }
}
