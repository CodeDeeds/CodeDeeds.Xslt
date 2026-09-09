using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for transforming JSON, which is mapped to the node shape XSLT 3.0 defines for
    /// <c>json-to-xml()</c> and then run through the same engine as XML.
    /// </summary>
    [TestClass]
    public sealed class JsonTransformTests
    {
        private const string Json = "http://www.w3.org/2005/xpath-functions";

        private static string Sheet(string body)
        {
            return "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + $"xmlns:j=\"{Json}\" exclude-result-prefixes=\"j\">"
                + body
                + "</xsl:stylesheet>";
        }

        /// <summary>Compiles a stylesheet with the XML declaration suppressed, since these tests compare fragments.</summary>
        private static Xslt Compile(string stylesheet)
        {
            return new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true });
        }

        [TestMethod]
        public void ObjectsBecomeMapsWithKeyedChildren()
        {
            XdmTree tree = JsonTreeBuilder.FromJson("{\"name\":\"Ada\",\"age\":36}");

            Assert.AreEqual(NodeKind.Element, tree.KindOf(1));
            Assert.AreEqual("map", tree.NameTable.GetLocalName(tree.FingerprintOf(1)));
            Assert.AreEqual(Json, tree.NameTable.GetNamespaceUri(tree.FingerprintOf(1)));

            Assert.AreEqual("string", tree.NameTable.GetLocalName(tree.FingerprintOf(2)));
            Assert.AreEqual("Ada", tree.StringValueOf(2));
            Assert.AreEqual("name", tree.StringValueOf(tree.AttributeAt(2, 0)));
        }

        [TestMethod]
        public void EveryJsonTypeMapsToItsOwnElement()
        {
            string result = Compile(Sheet(
                "<xsl:template match=\"/\"><types>"
                + "<xsl:for-each select=\"j:map/*\">"
                + "<t k=\"{@key}\" kind=\"{local-name()}\"><xsl:value-of select=\".\"/></t>"
                + "</xsl:for-each>"
                + "</types></xsl:template>"))
                .TransformJson("{\"s\":\"text\",\"n\":1.5,\"b\":true,\"z\":null}");

            Assert.AreEqual(
                "<types>"
                + "<t k=\"s\" kind=\"string\">text</t>"
                + "<t k=\"n\" kind=\"number\">1.5</t>"
                + "<t k=\"b\" kind=\"boolean\">true</t>"
                + "<t k=\"z\" kind=\"null\"/>"
                + "</types>",
                result);
        }

        [TestMethod]
        public void ArraysIterateInOrderAndCarryNoKeys()
        {
            string result = Compile(Sheet(
                "<xsl:template match=\"/\"><list>"
                + "<xsl:for-each select=\"j:map/j:array[@key='items']/*\">"
                + "<item n=\"{position()}\"><xsl:value-of select=\".\"/></item>"
                + "</xsl:for-each>"
                + "</list></xsl:template>"))
                .TransformJson("{\"items\":[\"a\",\"b\",\"c\"]}");

            Assert.AreEqual(
                "<list><item n=\"1\">a</item><item n=\"2\">b</item><item n=\"3\">c</item></list>",
                result);
        }

        [TestMethod]
        public void NestedStructuresAreNavigableWithOrdinaryXPath()
        {
            string result = Compile(Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"j:map/j:map[@key='user']/j:string[@key='city']\"/>"
                + "</out></xsl:template>"))
                .TransformJson("{\"user\":{\"name\":\"Ada\",\"city\":\"London\"}}");

            Assert.AreEqual("<out>London</out>", result);
        }

        [TestMethod]
        public void TemplatesAndPatternsWorkAgainstJsonJustAsForXml()
        {
            string result = Compile(Sheet(
                "<xsl:template match=\"/\"><ul><xsl:apply-templates select=\"//j:map[@key]\"/></ul></xsl:template>"
                + "<xsl:template match=\"j:map[@key]\">"
                + "<li><xsl:value-of select=\"@key\"/>=<xsl:value-of select=\"j:number[@key='qty']\"/></li>"
                + "</xsl:template>"))
                .TransformJson("{\"apples\":{\"qty\":3},\"pears\":{\"qty\":5}}");

            Assert.AreEqual("<ul><li>apples=3</li><li>pears=5</li></ul>", result);
        }

        [TestMethod]
        public void NumbersKeepTheirLexicalForm()
        {
            // Round-tripping through a double would turn 1.10 into 1.1 and lose the distinction.
            string result = Compile(Sheet(
                "<xsl:template match=\"/\"><n><xsl:value-of select=\"j:map/j:number\"/></n></xsl:template>"))
                .TransformJson("{\"v\":1.10}");

            Assert.AreEqual("<n>1.10</n>", result);
        }

        [TestMethod]
        public void SortingAndArithmeticApplyToJsonNumbers()
        {
            string result = Compile(Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<total><xsl:value-of select=\"sum(j:map/j:array/j:map/j:number[@key='n'])\"/></total>"
                + "<xsl:for-each select=\"j:map/j:array/j:map\">"
                + "<xsl:sort select=\"j:number[@key='n']\" data-type=\"number\" order=\"descending\"/>"
                + "<v><xsl:value-of select=\"j:number[@key='n']\"/></v>"
                + "</xsl:for-each>"
                + "</out></xsl:template>"))
                .TransformJson("{\"rows\":[{\"n\":3},{\"n\":10},{\"n\":7}]}");

            Assert.AreEqual("<out><total>20</total><v>10</v><v>7</v><v>3</v></out>", result);
        }

        [TestMethod]
        public void JsonAndXmlProduceTheSameResultFromEquivalentInput()
        {
            // The same compiled stylesheet drives both inputs; only the tree builder differs.
            Xslt stylesheet = Compile(Sheet(
                "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"//*[@key='city'] | //city\"/>"
                + "</out></xsl:template>"));

            Assert.AreEqual("<out>London</out>", stylesheet.TransformJson("{\"city\":\"London\"}"));
            Assert.AreEqual("<out>London</out>", stylesheet.TransformXml("<r><city>London</city></r>"));
        }

        [TestMethod]
        public void MalformedJsonIsReportedAsAStylesheetError()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile(Sheet("<xsl:template match=\"/\"><o/></xsl:template>"))
                    .TransformJson("{\"unterminated\": "));

            StringAssert.Contains(error.Message, "JSON");
        }

        [TestMethod]
        public void JsonCanBeTransformedToHtml()
        {
            string result = new Xslt(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + $"xmlns:j=\"{Json}\" exclude-result-prefixes=\"j\">"
                + "<xsl:output method=\"html\"/>"
                + "<xsl:template match=\"/\">"
                + "<table><xsl:for-each select=\"j:array/j:map\">"
                + "<tr><td><xsl:value-of select=\"j:string[@key='name']\"/></td>"
                + "<td><xsl:value-of select=\"j:number[@key='age']\"/></td></tr>"
                + "</xsl:for-each></table>"
                + "</xsl:template>"
                + "</xsl:stylesheet>")
                .TransformJson("[{\"name\":\"Ada\",\"age\":36},{\"name\":\"Alan\",\"age\":41}]");

            // The HTML method indents by default, as XSLT specifies.
            Assert.AreEqual(
                "<table>\n  <tr>\n    <td>Ada</td>\n    <td>36</td>\n  </tr>\n"
                + "  <tr>\n    <td>Alan</td>\n    <td>41</td>\n  </tr>\n</table>",
                result);
        }
    }
}
