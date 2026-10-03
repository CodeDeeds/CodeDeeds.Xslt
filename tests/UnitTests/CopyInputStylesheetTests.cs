using CodeDeeds.Xslt.Model;
using System.Xml.Linq;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that the classic identity transform gives back the document it was given.
    /// </summary>
    [TestClass]
    public sealed class CopyInputStylesheetTests
    {
        private const string CopyInput =
            "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
            + "<xsl:template match=\"@*|node()\"><xsl:copy><xsl:apply-templates select=\"@*|node()\"/></xsl:copy></xsl:template>"
            + "</xsl:stylesheet>";

        private const string Input =
            "<?xml-stylesheet href=\"a.css\"?><!--before-->"
            + "<catalog xmlns=\"urn:catalog\" xmlns:p=\"urn:price\" version=\"2\">\n"
            + "  <!-- the first book -->\n"
            + "  <book id=\"b1\" p:currency=\"NOK\"><title>Tom &amp; Jerry &lt;3</title><p:price>129.50</p:price></book>\n"
            + "  <book id=\"b2\"><title>Mixed <em>content</em> and <?pi data?> more</title><empty/></book>\n"
            + "  <note xmlns=\"\">No namespace here</note>\n"
            + "</catalog><!--after-->";

        private static Xslt Load()
        {
            return new Xslt(CopyInput, new XsltOptions { OmitXmlDeclaration = true });
        }

        [TestMethod]
        public void TheOutputIsTheInputAsWritten()
        {
            Assert.AreEqual(Input, Load().TransformXml(Input));
        }

        [TestMethod]
        public void TheOutputIsTheSameTreeAsTheInput()
        {
            // Compared as trees too, so the test says whether a failure above is in the copy or only in how it is written.
            XDocument expected = XDocument.Parse(Input, LoadOptions.PreserveWhitespace);
            XDocument actual = XDocument.Parse(Load().TransformXml(Input), LoadOptions.PreserveWhitespace);

            Assert.IsTrue(XNode.DeepEquals(expected, actual), actual.ToString(SaveOptions.DisableFormatting));
        }

        [TestMethod]
        public void APrefixedElementThatUndeclaredTheDefaultTakesItFromItsRebuiltParent()
        {
            // The one thing this copy does not give back. xsl:copy builds a new parent, whose namespaces its
            // children inherit (XSLT 3.0 §11.9.2), and p:note's name does not need the undeclaration to keep it.
            // XslCompiledTransform, an XSLT 1.0 processor, keeps the xmlns="" here.
            Assert.AreEqual(
                "<r xmlns=\"urn:r\" xmlns:p=\"urn:p\"><p:note>x</p:note></r>",
                Load().TransformXml("<r xmlns=\"urn:r\" xmlns:p=\"urn:p\"><p:note xmlns=\"\">x</p:note></r>"));
        }
    }
}
