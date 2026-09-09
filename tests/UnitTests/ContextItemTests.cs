namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:context-item</c>: what a template says it expects to be standing on, where the
    /// declaration may be written, and what it takes the whitespace around it to be.
    /// </summary>
    [TestClass]
    public sealed class ContextItemTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:my=\"urn:mine\""
            + " exclude-result-prefixes=\"xs my\">";

        private static string Run(string templates, string input = "<r/>")
        {
            return new Xslt(
                Head + templates + "</xsl:stylesheet>",
                new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                .TransformXml(input);
        }

        /// <summary>Runs a template that declares something about the item it is called on.</summary>
        private static string Declaring(string declaration, string body = "<xsl:sequence select=\"'ok'\"/>")
        {
            return Run(
                "<xsl:template match=\"/\"><out><xsl:call-template name=\"t\"/></out></xsl:template>"
                + "<xsl:template name=\"t\">" + declaration + body + "</xsl:template>");
        }

        private static string Refuses(string declaration)
        {
            return Assert.ThrowsExactly<XsltException>(() => Declaring(declaration)).Code ?? string.Empty;
        }

        [TestMethod]
        public void TheTypeOfAContextItemIsAnItemType()
        {
            // The context item is one item, so there is nothing for an occurrence indicator to say about
            // it: writing one is writing about a sequence that cannot be there.
            Assert.AreEqual("XTSE0020", Refuses("<xsl:context-item as=\"xs:integer?\"/>"));
            Assert.AreEqual("XTSE0020", Refuses("<xsl:context-item as=\"item()*\"/>"));
            Assert.AreEqual("XTSE0020", Refuses("<xsl:context-item as=\"empty-sequence()\"/>"));

            // A type nothing declares is the same complaint about the same attribute. This engine is not
            // schema-aware, so a named type is one that cannot exist.
            Assert.AreEqual("XTSE0020", Refuses("<xsl:context-item as=\"element(*, my:percentage)\"/>"));
            Assert.AreEqual("XTSE0020", Refuses("<xsl:context-item as=\"nonesuch\"/>"));

            // An item type is taken.
            Assert.AreEqual("<out>ok</out>", Declaring("<xsl:context-item as=\"document-node()\"/>"));
            Assert.AreEqual("<out>ok</out>", Declaring("<xsl:context-item as=\"node()\"/>"));
        }

        [TestMethod]
        public void ATemplateDeclaresItsContextItemOnceAndBeforeItsParameters()
        {
            // One declaration says one thing about the whole template, so two of them would be two answers
            // to one question.
            Assert.AreEqual(
                "XTSE0010",
                Refuses("<xsl:context-item use=\"absent\"/><xsl:context-item use=\"absent\"/>"));

            // And it comes before the parameters, which is the order the content model gives. Written after
            // one, it is a declaration read out of order rather than something a processor may put right on
            // the stylesheet's behalf.
            Assert.AreEqual(
                "XTSE0010",
                Refuses("<xsl:param name=\"p\" select=\"1\"/><xsl:context-item as=\"node()\"/>"));

            // Written in that order, both are taken.
            Assert.AreEqual(
                "<out>1</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:call-template name=\"t\"/></out></xsl:template>"
                    + "<xsl:template name=\"t\"><xsl:context-item as=\"node()\"/>"
                    + "<xsl:param name=\"p\" select=\"1\"/><xsl:sequence select=\"$p\"/></xsl:template>"));
        }

        [TestMethod]
        public void WhitespaceBeforeTheDeclarationIsNotContentOfTheTemplate()
        {
            // A template's sequence constructor begins after the declarations the template reads for
            // itself, so the layout standing between and before them is not part of it. It only survives to
            // be asked about at all under xml:space="preserve", which is where the difference shows: the
            // two spaces after the declaration are the whole of the template's output.
            Assert.AreEqual(
                "<out>  </out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:call-template name=\"ws\"/></out></xsl:template>"
                    + "<xsl:template name=\"ws\" xml:space=\"preserve\">  "
                    + "<xsl:context-item as=\"document-node()\"/>  </xsl:template>"));

            // The same of a parameter, which is the same rule reached by the other declaration.
            Assert.AreEqual(
                "<out> x</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:call-template name=\"ws\"/></out></xsl:template>"
                    + "<xsl:template name=\"ws\" xml:space=\"preserve\"> "
                    + "<xsl:param name=\"p\" select=\"1\"/> x</xsl:template>"));
        }
    }
}
