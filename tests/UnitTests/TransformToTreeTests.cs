using System.Xml.Schema;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for handing a transformation's result back as a tree or as the items it is made of, and for
    /// taking a tree as input: the two ends of a chain of transformations that never passes through text.
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
            Assert.ThrowsExactly<ArgumentNullException>(() => sheet.TransformXmlToSequence(null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => sheet.TransformToSequence((XdmTree)null!));
        }

        [TestMethod]
        public void AResultComesBackAsTheItemsItIsMadeOf()
        {
            IReadOnlyList<XPath.XPathValue> items = Sheet(
                "<xsl:template match=\"/\"><xsl:sequence select=\"1 to 3\"/></xsl:template>")
                .TransformXmlToSequence("<r/>");

            Assert.HasCount(3, items);
            CollectionAssert.AreEqual(
                new[] { 1L, 2L, 3L },
                items.Select(item => item.ToInteger()).ToArray());
        }

        [TestMethod]
        public void TheItemsKeepTheTypesSerializingWouldLose()
        {
            // The whole reason for asking: an xs:integer and the string of it are the same characters
            // written down and are not the same value, and a caller wanting the value rather than the text
            // has nowhere else to get it.
            Xslt sheet = Sheet(
                "<xsl:template match=\"/\"><xsl:sequence select=\"xs:integer(42), '42'\"/></xsl:template>");

            Assert.AreEqual("42 42", sheet.TransformXml("<r/>"));

            IReadOnlyList<XPath.XPathValue> items = sheet.TransformXmlToSequence("<r/>");

            Assert.HasCount(2, items);
            Assert.AreEqual(XPath.XdmTypeCode.Integer, items[0].TypeCode);
            Assert.AreEqual(XPath.XdmTypeCode.String, items[1].TypeCode);
        }

        [TestMethod]
        public void AResultThatIsADocumentIsOneItem()
        {
            // A stylesheet that builds a document has built one item, and what comes back is that node
            // rather than the two elements under it.
            IReadOnlyList<XPath.XPathValue> items = Sheet(
                "<xsl:template match=\"/\"><out><a/><b/></out></xsl:template>")
                .TransformXmlToSequence("<r/>");

            Assert.HasCount(1, items);
            Assert.AreEqual(XPath.XPathValueKind.Node, items[0].Kind);
            Assert.AreEqual(NodeKind.Element, items[0].NodeTree.KindOf(items[0].NodeId));
        }

        [TestMethod]
        public void AFunctionsReturnValueComesBackAsItself()
        {
            // The entry point 3.0 added, and the one the pair exists for: what an initial function returns
            // is the result, and it is a value rather than a document.
            IReadOnlyList<XPath.XPathValue> items = Sheet(
                "<xsl:function name=\"t:square\" visibility=\"public\" as=\"xs:integer\">"
                + "<xsl:param name=\"n\" as=\"xs:integer\"/><xsl:sequence select=\"$n * $n\"/></xsl:function>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    InitialFunction = "{urn:t}square",
                    FunctionArguments = new object?[] { 12 },
                })
                .TransformToSequence();

            Assert.HasCount(1, items);
            Assert.AreEqual(XPath.XdmTypeCode.Integer, items[0].TypeCode);
            Assert.AreEqual(144L, items[0].ToInteger());
        }

        [TestMethod]
        public void ATreeHandedOverIsNotSpentByAskingForItems()
        {
            XdmTree input = XdmTreeBuilder.FromXml("<r><a>1</a><a>2</a></r>", false);

            Xslt sheet = Sheet("<xsl:template match=\"/\"><xsl:sequence select=\"sum(/r/a)\"/></xsl:template>");

            Assert.AreEqual(3L, sheet.TransformToSequence(input)[0].ToInteger());
            Assert.AreEqual(3L, sheet.TransformToSequence(input)[0].ToInteger());
        }
    }
}
