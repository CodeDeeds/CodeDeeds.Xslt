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

        [TestMethod]
        public void TheDeclarationPrecedesTextWrittenAtTheTop()
        {
            // Text with nothing before it had been going out on its own, with the declaration written
            // afterwards when the first element arrived. A declaration is the first thing in a document or
            // it is not a declaration at all.
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>text<root/>",
                Run("<xsl:text>text</xsl:text><root/>"));

            // Whitespace decides nothing: an html document element may still follow it, so it waits with
            // whatever else is waiting and comes out after the declaration, or after no declaration.
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>  <root/>",
                Run("<xsl:text>  </xsl:text><root/>"));

            Assert.AreEqual("  <html></html>", Run("<xsl:text>  </xsl:text><html/>"));
        }

        [TestMethod]
        public void TheDeclarationPrecedesADoctypeAStylesheetWritesItself()
        {
            // The case this was found in. A stylesheet that wants the HTML 5 document type declaration on
            // an XML result writes it as text with the escaping off, there being no xsl:output attribute
            // that produces a doctype naming no DTD. That is what the DocBook XHTML5 stylesheets do, and
            // the declaration was coming out behind it: the suite's docbook-001 produced
            // "<!DOCTYPE html><?xml ...?><html ...>", which no parser will read.
            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE html><r/>",
                Run(
                    "<xsl:text disable-output-escaping=\"yes\">&lt;!DOCTYPE html&gt;</xsl:text><r/>",
                    "<xsl:output method=\"xml\"/>"));

            // And with the declaration omitted, the doctype is still the first thing out.
            Assert.AreEqual(
                "<!DOCTYPE html><r/>",
                Run(
                    "<xsl:text disable-output-escaping=\"yes\">&lt;!DOCTYPE html&gt;</xsl:text><r/>",
                    "<xsl:output method=\"xml\" omit-xml-declaration=\"yes\"/>"));
        }
    }
}
