namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>Tests for what <c>xsl:message</c> hands to whoever is reading messages.</summary>
    [TestClass]
    public sealed class MessagePresentationTests
    {
        private static IReadOnlyList<string> Messages(string body)
        {
            StringWriter written = new StringWriter();

            new Xslt(
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:template match=\"/\">" + body + "<out/></xsl:template></xsl:stylesheet>",
                new XsltOptions { OmitXmlDeclaration = true, MessageWriter = written })
                .TransformXml("<r><a>1</a><b>2</b></r>");

            return written.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        }

        [TestMethod]
        public void AMessageIsPresentedAsTheDocumentItIs()
        {
            // A message is a document node built from the instruction's content, and an xsl:message writing
            // an element means the element: reducing it to the text inside throws away what the stylesheet
            // put there to be read.
            Assert.AreEqual(
                "<note kind=\"warning\">check this</note>",
                Messages("<xsl:message><note kind=\"warning\">check this</note></xsl:message>")[0]);

            // Text is still text, and adjacent atomic values are still a space apart.
            Assert.AreEqual("plain words", Messages("<xsl:message>plain words</xsl:message>")[0]);
            Assert.AreEqual(
                "1 2",
                Messages("<xsl:message><xsl:sequence select=\"1, 2\"/></xsl:message>")[0]);

            // Nodes selected out of the source are copied into it, as the content of a variable would be.
            Assert.AreEqual(
                "<a>1</a><b>2</b>",
                Messages("<xsl:message><xsl:copy-of select=\"/r/*\"/></xsl:message>")[0]);
        }

        [TestMethod]
        public void EachMessageIsWrittenOnceAndInOrder()
        {
            IReadOnlyList<string> written = Messages(
                "<xsl:message>first</xsl:message><xsl:message><x/></xsl:message><xsl:message>third</xsl:message>");

            Assert.AreEqual(3, written.Count);
            Assert.AreEqual("first", written[0]);
            Assert.AreEqual("<x/>", written[1]);
            Assert.AreEqual("third", written[2]);
        }
    }
}
