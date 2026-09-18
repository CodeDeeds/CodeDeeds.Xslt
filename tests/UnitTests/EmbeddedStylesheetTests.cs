using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for a stylesheet module that is not a document of its own but an element inside one
    /// (XSLT 3.0 §3.12), and for the <c>xml-stylesheet</c> instruction a document names one with.
    /// </summary>
    [TestClass]
    public sealed class EmbeddedStylesheetTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        /// <summary>A host document holding a module, and the module's result of transforming it.</summary>
        private static string Run(string host, string? id = null)
        {
            XdmTree document = XdmTreeBuilder.FromXml(host, fragment: false);

            Xslt sheet = Xslt.Embedded(document, id, new XsltOptions { OmitXmlDeclaration = true });
            string interpreted = sheet.Transform(document);

            string compiled = Xslt
                .Embedded(document, id, new XsltOptions { OmitXmlDeclaration = true, Backend = XsltBackend.Compiled })
                .Transform(document);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        private static string Refused(string host, string? id = null)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(host, id)).Message;
        }

        /// <summary>The usual shape: a document carrying the stylesheet that transforms it.</summary>
        private static string Host(string module, string instruction = "type=\"text/xsl\" href=\"#s\"")
        {
            return $"<?xml-stylesheet {instruction}?>"
                + "<doc><head>" + module + "</head><body>seen</body></doc>";
        }

        private const string Simple =
            "<xsl:stylesheet xml:id=\"s\" version=\"3.0\" " + Xsl + ">"
            + "<xsl:template match=\"/\"><out><xsl:value-of select=\"doc/body\"/></out></xsl:template>"
            + "</xsl:stylesheet>";

        [TestMethod]
        public void ADocumentMayCarryTheStylesheetThatTransformsIt()
        {
            // Both ways of saying which element the module is: the document's own xml-stylesheet
            // instruction, and the identifier named outright.
            Assert.AreEqual("<out>seen</out>", Run(Host(Simple)));
            Assert.AreEqual("<out>seen</out>", Run(Host(Simple), "s"));
        }

        [TestMethod]
        public void StandardAttributesOnAncestorsHaveNoEffect()
        {
            // §3.4: "In an embedded stylesheet module, standard attributes appearing on ancestors of the
            // outermost element of the stylesheet module have no effect." Each of these would change what
            // the module does if it reached in: 1.0 would take the first item of a sequence rather than all
            // of it, a default namespace would make doc/body select nothing, and expand-text would read the
            // braces as an expression.
            const string Host =
                "<?xml-stylesheet type=\"text/xsl\" href=\"#s\"?>"
                + "<doc " + Xsl + " xsl:version=\"1.0\" xsl:xpath-default-namespace=\"urn:elsewhere\""
                + " xsl:expand-text=\"yes\" xsl:default-collation=\"urn:no-such-collation\">"
                + "<head><xsl:stylesheet xml:id=\"s\" version=\"3.0\" " + Xsl + ">"
                + "<xsl:template match=\"/\"><out><xsl:value-of select=\"1 to 3\"/>"
                + "<xsl:text>,</xsl:text><xsl:value-of select=\"doc/body\"/>"
                + "<xsl:text>,{1+1}</xsl:text></out></xsl:template>"
                + "</xsl:stylesheet></head><body>seen</body></doc>";

            Assert.AreEqual("<out>1 2 3,seen,{1+1}</out>", Run(Host));
        }

        [TestMethod]
        public void NamespaceDeclarationsOnAncestorsStillApply()
        {
            // They are not standard attributes. A namespace is in scope for an element because XML says it
            // is, and the module is inside the document that declared it.
            const string Host =
                "<?xml-stylesheet type=\"text/xsl\" href=\"#s\"?>"
                + "<doc xmlns:t=\"urn:t\">"
                + "<head><xsl:stylesheet xml:id=\"s\" version=\"3.0\" " + Xsl + ">"
                + "<xsl:template match=\"/\"><t:out><xsl:value-of select=\"doc/body\"/></t:out></xsl:template>"
                + "</xsl:stylesheet></head><body>seen</body></doc>";

            Assert.AreEqual("<t:out xmlns:t=\"urn:t\">seen</t:out>", Run(Host));
        }

        [TestMethod]
        public void ASimplifiedModuleMayBeEmbeddedToo()
        {
            // §3.12 says both kinds may be: the outermost element of a simplified module is a literal
            // result element carrying xsl:version, and one of those may be a child of a host element as
            // readily as an xsl:stylesheet may.
            const string Host =
                "<?xml-stylesheet type=\"text/xsl\" href=\"#s\"?>"
                + "<doc><head><out xml:id=\"s\" " + Xsl + " xsl:version=\"3.0\">"
                + "<xsl:value-of select=\"doc/body\"/></out></head><body>seen</body></doc>";

            // The identifier comes out in the result, which is right rather than awkward: the outermost
            // element of a simplified module is the template body, and every attribute of it that is not
            // in the XSLT namespace is an attribute of the element the body writes. There is nowhere else
            // to put an identifier that names the module.
            Assert.AreEqual("<out xml:id=\"s\">seen</out>", Run(Host));
        }

        [TestMethod]
        public void OnlyAnInstructionNamingAnXsltStylesheetIsRead()
        {
            // A document may name a stylesheet for each of several media, and mark all but one alternate.
            // The one taken is the first that is XSLT and is not an alternative to something else.
            Assert.AreEqual(
                "<out>seen</out>",
                Run("<?xml-stylesheet type=\"text/css\" href=\"#c\"?>"
                    + "<?xml-stylesheet type=\"application/xslt+xml\" href=\"#s\" alternate=\"yes\"?>"
                    + "<?xml-stylesheet type=\"application/xslt+xml\" href=\"#s\"?>"
                    + "<doc><head>" + Simple + "</head><body>seen</body></doc>"));
        }

        [TestMethod]
        public void ADocumentThatNamesNoEmbeddedStylesheetSaysSo()
        {
            // Four ways of not naming one, and each is told apart from the others: no instruction at all,
            // one naming a stylesheet that is somewhere else, one naming an identifier nothing carries, and
            // one naming an element that is not a module.
            StringAssert.Contains(
                Refused("<doc><head>" + Simple + "</head><body>seen</body></doc>"),
                "carries no xml-stylesheet instruction");

            StringAssert.Contains(
                Refused(Host(Simple, "type=\"text/xsl\" href=\"other.xsl\"")),
                "not a stylesheet embedded in this document");

            StringAssert.Contains(Refused(Host(Simple), "elsewhere"), "nothing in the document carries");

            StringAssert.Contains(
                Refused("<?xml-stylesheet type=\"text/xsl\" href=\"#s\"?>"
                    + "<doc><head xml:id=\"s\">" + Simple.Replace("xml:id=\"s\" ", string.Empty)
                    + "</head><body>seen</body></doc>"),
                "must start with xsl:stylesheet");
        }

        [TestMethod]
        public void ANullDocumentIsRefused()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => Xslt.Embedded(null!));
        }
    }
}
