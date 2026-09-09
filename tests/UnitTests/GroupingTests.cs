namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what <c>xsl:for-each-group</c> groups: any sequence of items, by keys compared as
    /// values rather than as text, with the group and its key reachable only where the specification
    /// puts them.
    /// </summary>
    [TestClass]
    public sealed class GroupingTests
    {
        private static string Sheet(string body, string version = "3.0")
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + "<xsl:template match=\"/\"><out>" + body + "</out></xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, string input = "<r/>", XsltVersion? version = null)
        {
            return new Xslt(stylesheet, new XsltOptions { Version = version ?? XsltVersion.V30, OmitXmlDeclaration = true })
                .TransformXml(input);
        }

        private static string Refuses(string stylesheet, XsltVersion? version = null)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(stylesheet, version: version)).Code ?? string.Empty;
        }

        [TestMethod]
        public void AtomicValuesAreGroupedLikeNodes()
        {
            // By key, adjacently with a composite key, and by a pattern on the value itself.
            Assert.AreEqual(
                "<out><g k=\"1\">1 3 5</g><g k=\"0\">2 4</g></out>",
                Run(Sheet(
                    "<xsl:for-each-group select=\"1 to 5\" group-by=\". mod 2\">"
                    + "<g k=\"{current-grouping-key()}\"><xsl:value-of select=\"current-group()\"/></g></xsl:for-each-group>")));
            Assert.AreEqual(
                "<out><g>1</g><g>2 3</g><g>4 5</g></out>",
                Run(Sheet(
                    "<xsl:for-each-group select=\"1 to 5\" group-adjacent=\". idiv 2, number('NaN')\" composite=\"yes\">"
                    + "<g><xsl:value-of select=\"current-group()\"/></g></xsl:for-each-group>")));
            Assert.AreEqual(
                "<out><g>a 1 2</g><g>b 3</g></out>",
                Run(Sheet(
                    "<xsl:for-each-group select=\"'a', 1, 2, 'b', 3\" group-starting-with=\".[. instance of xs:string]\">"
                    + "<g><xsl:value-of select=\"current-group()\"/></g></xsl:for-each-group>")));

            // Under XSLT 2.0 a pattern matched nodes only, and a population of anything else was an error.
            Assert.AreEqual(
                "XTTE1120",
                Refuses(
                    Sheet("<xsl:for-each-group select=\"1, 2\" group-starting-with=\"*\"><g/></xsl:for-each-group>", "2.0"),
                    XsltVersion.V20));
        }

        [TestMethod]
        public void KeysAreComparedAsValues()
        {
            // Two dateTimes naming one instant are one key; a date and the string that spells it are two
            // and no error; a QName is its namespace and local name whatever the prefix.
            Assert.AreEqual(
                "<out><g>2</g><g>1</g></out>",
                Run(Sheet(
                    "<xsl:for-each-group select=\"xs:dateTime('2001-04-04T13:00:00+02:00'), xs:dateTime('2001-04-04T11:00:00Z'), "
                    + "xs:dateTime('2001-04-04T11:00:00+02:00')\" group-by=\".\">"
                    + "<g><xsl:value-of select=\"count(current-group())\"/></g></xsl:for-each-group>")));
            Assert.AreEqual(
                "<out><g>1</g><g>1</g></out>",
                Run(Sheet(
                    "<xsl:for-each-group select=\"xs:date('2001-04-04'), '2001-04-04'\" group-by=\".\">"
                    + "<g><xsl:value-of select=\"count(current-group())\"/></g></xsl:for-each-group>")));
            Assert.AreEqual(
                "<out><g>a:x b:x</g><g>a:y</g></out>",
                Run(
                    Sheet(
                        "<xsl:for-each-group select=\"/r/*\" group-by=\"node-name(.)\">"
                        + "<g><xsl:value-of select=\"current-group()/name()\"/></g></xsl:for-each-group>"),
                    "<r xmlns:a=\"urn:one\" xmlns:b=\"urn:one\"><a:x/><a:y/><b:x/></r>"));

            // Numbers are compared as XPath compares them, promotion and all — which is not transitive,
            // so a float or a decimal is compared with each group's key rather than looked up.
            string keys = "xs:float('1.0'), xs:decimal('1.0000000000100000000001'), xs:double('1.00000000001')";

            Assert.AreEqual(
                "<out><g>2</g><g>1</g></out>",
                Run(Sheet(
                    $"<xsl:for-each-group select=\"{keys}\" group-by=\".\">"
                    + "<g><xsl:value-of select=\"count(current-group())\"/></g></xsl:for-each-group>")));
            Assert.AreEqual(
                "<out><g>3</g></out>",
                Run(Sheet(
                    "<xsl:for-each-group select=\"xs:decimal('1.0000000000100000000001'), xs:float('1.0'), xs:double('1.00000000001')\" group-by=\".\">"
                    + "<g><xsl:value-of select=\"count(current-group())\"/></g></xsl:for-each-group>")));
        }

        [TestMethod]
        public void AGroupSpansDocumentsInPopulationOrder()
        {
            // Nodes from a variable's tree and from the source, grouped together; the group is a node set
            // a path can be written on.
            Assert.AreEqual(
                "<out><g k=\"1\">a c</g><g k=\"2\">b</g></out>",
                Run(
                    Sheet(
                        "<xsl:variable name=\"more\"><e k=\"1\">c</e></xsl:variable>"
                        + "<xsl:for-each-group select=\"/r/e, $more/e\" group-by=\"@k\">"
                        + "<g k=\"{current-grouping-key()}\"><xsl:value-of select=\"current-group()\"/></g></xsl:for-each-group>"),
                    "<r><e k=\"1\">a</e><e k=\"2\">b</e></r>"));
        }

        [TestMethod]
        public void TheGroupIsAbsentWhereTheSpecificationSaysItIs()
        {
            // A group a pattern made has no key; the attribute value templates of xsl:sort see no group;
            // and a dynamic call on current-group() is inside none.
            Assert.AreEqual(
                "XTDE1071",
                Refuses(Sheet(
                    "<xsl:for-each-group select=\"1 to 3\" group-starting-with=\".[. = 1]\"><g k=\"{current-grouping-key()}\"/></xsl:for-each-group>")));
            Assert.AreEqual(
                "XTDE1061",
                Refuses(Sheet(
                    "<xsl:for-each-group select=\"1 to 4\" group-by=\". mod 2\">"
                    + "<xsl:sort select=\".\" order=\"{if (current-group()[1] = 1) then 'ascending' else 'descending'}\"/><g/></xsl:for-each-group>")));
            Assert.AreEqual(
                "XTDE1061",
                Refuses(Sheet(
                    "<xsl:variable name=\"f\" select=\"current-group#0\"/>"
                    + "<xsl:for-each-group select=\"1 to 4\" group-by=\". mod 2\"><g><xsl:value-of select=\"$f()\"/></g></xsl:for-each-group>")));
            Assert.AreEqual(
                "XTDE1071",
                Refuses(Sheet(
                    "<xsl:for-each-group select=\"1 to 4\" group-by=\". mod 2\">"
                    + "<g><xsl:value-of select=\"current-grouping-key#0()\"/></g></xsl:for-each-group>")));

            // Sorted groups still see their own group in the sort key itself.
            Assert.AreEqual(
                "<out><g>2 4 6</g><g>1 3</g><g>5</g></out>",
                Run(Sheet(
                    "<xsl:for-each-group select=\"1 to 6\" group-by=\"if (. = 5) then 3 else . mod 2\">"
                    + "<xsl:sort select=\"count(current-group())\" order=\"descending\"/>"
                    + "<g><xsl:value-of select=\"current-group()\"/></g></xsl:for-each-group>")));
        }
    }
}
