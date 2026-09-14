using System.Xml.Schema;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for handing a transformation's result back as a tree, and for taking one as input: the two
    /// ends of a chain of transformations that never passes through text.
    /// </summary>
    [TestClass]
    public sealed class TransformToTreeTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private static Xslt Sheet(string body, XsltOptions? options = null)
        {
            return new Xslt(
                $"<xsl:stylesheet version=\"3.0\" {Xsl} xmlns:t=\"urn:t\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"t xs\">"
                + body
                + "</xsl:stylesheet>",
                options ?? new XsltOptions { OmitXmlDeclaration = true });
        }

        [TestMethod]
        public void AResultComesBackAsADocumentNode()
        {
            XdmTree result = Sheet("<xsl:template match=\"/\"><out><xsl:value-of select=\"/r/@n + 1\"/></out></xsl:template>")
                .TransformXmlToTree("<r n=\"7\"/>");

            Assert.AreEqual(NodeKind.Root, result.KindOf(XdmTree.RootNode));
            Assert.AreEqual("8", result.StringValueOf(XdmTree.RootNode));

            int element = result.FirstChildOf(XdmTree.RootNode);
            Assert.AreEqual("out", result.NameTable.GetLocalName(result.FingerprintOf(element)));
        }

        [TestMethod]
        public void AStylesheetWithNoSourceGivesATreeToo()
        {
            XdmTree result = Sheet(
                "<xsl:template name=\"go\"><made>yes</made></xsl:template>",
                new XsltOptions { OmitXmlDeclaration = true, InitialTemplate = "go" })
                .TransformToTree();

            Assert.AreEqual("yes", result.StringValueOf(XdmTree.RootNode));
        }

        [TestMethod]
        public void OneResultIsTheNextTransformationsInput()
        {
            // The point of the pair: a chain of stylesheets with no serializing between the steps, and the
            // tree handed on is left as it was, so it may be transformed more than once.
            XdmTree first = Sheet("<xsl:template match=\"/\"><mid><xsl:copy-of select=\"/r/*\"/></mid></xsl:template>")
                .TransformXmlToTree("<r><a>1</a><b>2</b></r>");

            Xslt second = Sheet("<xsl:template match=\"/\"><end><xsl:value-of select=\"sum(/mid/*)\"/></end></xsl:template>");

            Assert.AreEqual("<end>3</end>", second.Transform(first));
            Assert.AreEqual("<end>3</end>", second.Transform(first), "the input tree is not spent by a run");

            XdmTree chained = second.TransformToTree(first);
            Assert.AreEqual("3", chained.StringValueOf(XdmTree.RootNode));

            using StringWriter writer = new StringWriter();
            second.Transform(first, writer);
            Assert.AreEqual("<end>3</end>", writer.ToString());
        }

        [TestMethod]
        public void TheTypesAValidatedResultCarriesSurviveIntoTheTree()
        {
            // The reason the pair exists rather than the caller serializing and parsing back: a type
            // annotation is in the tree and not in the text, so a round trip through XML loses it.
            const string Schema =
                "<xs:schema targetNamespace=\"urn:t\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:t=\"urn:t\">"
                + "<xs:element name=\"n\" type=\"xs:int\"/></xs:schema>";

            XmlSchemaSet schemas = new XmlSchemaSet();
            schemas.Add(XmlSchema.Read(new StringReader(Schema), null)!);
            schemas.Compile();

            XdmTree result = Sheet(
                "<xsl:template match=\"/\"><xsl:copy-of select=\"/t:n\" validation=\"strict\"/></xsl:template>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    SchemaAware = true,
                    Schemas = schemas,
                })
                .TransformXmlToTree("<n xmlns=\"urn:t\">42</n>");

            // Read back by a second transformation, since the annotation is the tree's and not the
            // text's: serializing this result and parsing it again would answer false and 43 would be a
            // string concatenation rather than a sum.
            Xslt reader = Sheet(
                "<xsl:template match=\"/\"><xsl:value-of select=\"/t:n instance of element(t:n, xs:int)\"/>"
                + "<xsl:text>,</xsl:text><xsl:value-of select=\"data(/t:n) + 1\"/></xsl:template>",
                new XsltOptions { OmitXmlDeclaration = true, SchemaAware = true, Schemas = schemas });

            Assert.AreEqual("true,43", reader.Transform(result));
        }

        [TestMethod]
        public void ANullArgumentIsRefused()
        {
            Xslt sheet = Sheet("<xsl:template match=\"/\"><out/></xsl:template>");

            Assert.ThrowsExactly<ArgumentNullException>(() => sheet.TransformXmlToTree((string)null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => sheet.TransformToTree((XdmTree)null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => sheet.Transform((XdmTree)null!));
        }
    }
}
