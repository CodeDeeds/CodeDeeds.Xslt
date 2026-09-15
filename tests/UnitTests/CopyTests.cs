namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what a copy carries with it — namespaces, the document node, nothing at all — and for the
    /// instructions around one: <c>xsl:where-populated</c>, a leading comment, <c>serialize()</c>.
    /// </summary>
    [TestClass]
    public sealed class CopyTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" exclude-result-prefixes=\"xs f\">";

        private static string Run(string body, string input = "<r/>", string? initialTemplate = null)
        {
            XsltOptions options = new XsltOptions
            {
                OmitXmlDeclaration = true,
                Version = XsltVersion.V30,
                InitialTemplate = initialTemplate,
            };

            Xslt xslt = new Xslt(Head + body + "</xsl:stylesheet>", options);
            return input.Length == 0 ? xslt.Transform() : xslt.TransformXml(input);
        }

        private static string Refuses(string body, string input = "<r/>", string? initialTemplate = null)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, input, initialTemplate)).Code ?? string.Empty;
        }

        private static string Root(string content)
        {
            return "<xsl:template match=\"/\"><out>" + content + "</out></xsl:template>";
        }

        /// <summary>Writes each element's in-scope prefixes and namespaces, sorted, as one attribute.</summary>
        private const string InScope =
            "<xsl:function name=\"f:ns\" as=\"xs:string\"><xsl:param name=\"e\" as=\"element()\"/>"
            + "<xsl:value-of><xsl:for-each select=\"in-scope-prefixes($e)[. != 'xml']\"><xsl:sort select=\".\"/>"
            + "<xsl:value-of select=\"concat('|', ., '=', namespace-uri-for-prefix(., $e))\"/></xsl:for-each></xsl:value-of></xsl:function>";

        [TestMethod]
        public void ALaterAttributeOfOneNameReplacesTheEarlierInABuiltTree()
        {
            // §5.7.1: the element has one attribute of the name, the value written last, and it stands
            // last — the order the serializer already kept for the same case.
            Assert.AreEqual(
                "<out>2 3 b,a</out>",
                Run(Root(
                    "<xsl:variable name=\"v\" as=\"element()\"><e a=\"1\" b=\"2\"><xsl:attribute name=\"a\" select=\"'3'\"/></e></xsl:variable>"
                    + "<xsl:value-of select=\"count($v/@*), $v/@a, string-join($v/@*/name(), ',')\"/>")));
        }

        [TestMethod]
        public void ACopiedDocumentNodeIsOneItemOfASequence()
        {
            // Three xsl:copy over a document node, into a variable wanting document nodes: three of them.
            Assert.AreEqual(
                "<out count=\"3\"><a>24</a><a>25</a><a>26</a></out>",
                Run("<xsl:template match=\"/\"><xsl:variable name=\"doc\">23</xsl:variable>"
                    + "<xsl:variable name=\"docs\" as=\"document-node()*\"><xsl:for-each select=\"$doc\">"
                    + "<xsl:copy><a>24</a></xsl:copy><xsl:copy><a>25</a></xsl:copy><xsl:copy><a>26</a></xsl:copy>"
                    + "</xsl:for-each></xsl:variable>"
                    + "<out count=\"{count($docs)}\"><xsl:sequence select=\"$docs\"/></out></xsl:template>"));

            // And a copy-of the same, in a function returning a sequence: a document node, distinct from the
            // original, whose children are new nodes too.
            Assert.AreEqual(
                "<out>9</out>",
                Run(Root("<xsl:variable name=\"in\"><a/><b/><c/></xsl:variable>"
                    + "<xsl:value-of select=\"count($in/* | f:copy($in/*) | f:copy($in)/*)\"/>")
                    + "<xsl:function name=\"f:copy\"><xsl:param name=\"node\" as=\"node()*\"/><xsl:copy-of select=\"$node\"/></xsl:function>"));
        }

        [TestMethod]
        public void ANamespaceNodeInsideACopiedDocumentGoesOnItsElement()
        {
            // The floor below which no attribute or namespace may go belongs to the output the copy was
            // measured on; a body captured to check what it produced starts at a floor of its own.
            Assert.AreEqual(
                "<out><a xmlns:n=\"urn:n\"/></out>",
                Run(Root("<xsl:copy><a><xsl:namespace name=\"n\">urn:n</xsl:namespace></a><xsl:on-empty select=\"()\"/></xsl:copy>")));
        }

        [TestMethod]
        public void WherePopulatedLeavesOutEachItemDeemedEmpty()
        {
            // An empty comment and an empty processing instruction are deemed empty, as is an element with
            // no children; the rest stays, item by item.
            Assert.AreEqual(
                "<out><b>x</b>t</out>",
                Run(Root("<xsl:where-populated><xsl:comment/><xsl:processing-instruction name=\"go\"/><a/><b>x</b>t</xsl:where-populated>")));
            Assert.AreEqual(
                "<out/>",
                Run(Root("<xsl:variable name=\"v\" as=\"node()\"><xsl:comment/></xsl:variable>"
                    + "<xsl:where-populated><xsl:copy select=\"$v\"/></xsl:where-populated>")));
        }

        [TestMethod]
        public void ACopyWithNoContextItemIsRefused()
        {
            // No source document: the global context item is absent, so a global variable reaching xsl:copy
            // has nothing to copy — the error the specification names rather than a copy of nothing.
            Assert.AreEqual(
                "XTTE0945",
                Refuses("<xsl:template name=\"main\"><out><xsl:copy-of select=\"$var\"/></out></xsl:template>"
                    + "<xsl:variable name=\"var\"><xsl:copy><in/></xsl:copy></xsl:variable>", string.Empty, "main"));
        }

        [TestMethod]
        public void TheDeclarationComesBeforeALeadingComment()
        {
            XsltOptions options = new XsltOptions { Version = XsltVersion.V30 };
            Xslt xslt = new Xslt(
                Head + "<xsl:output method=\"xml\" encoding=\"UTF-8\"/>"
                    + "<xsl:template match=\"/\"><xsl:copy-of select=\"doc/comment()\"/><out/></xsl:template></xsl:stylesheet>",
                options);

            Assert.AreEqual(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!-- XYZ --><out/>",
                xslt.TransformXml("<doc><!-- XYZ --></doc>"));
        }

        [TestMethod]
        public void ACopiedElementDeclaresItsOwnNamespaceAndInheritsAsTold()
        {
            const string Trees =
                "<xsl:variable name=\"outer\"><a xmlns=\"uri:a\"><b:b xmlns:b=\"uri:b\"><c xmlns=\"uri:c\" xmlns:d=\"uri:d\"><graft xmlns=\"\"/></c></b:b></a></xsl:variable>"
                + "<xsl:variable name=\"inner\"><p xmlns=\"uri:p\"><q xmlns=\"uri:q\"><r xmlns=\"uri:r\" xmlns:s=\"uri:s\"/></q></p></xsl:variable>"
                + "<xsl:mode on-no-match=\"shallow-copy\"/>"
                + "<xsl:template name=\"main\"><xsl:variable name=\"capture\"><xsl:apply-templates select=\"$outer\"/></xsl:variable>"
                + "<out><xsl:for-each select=\"$capture//*\"><e n=\"{local-name()}\" ns=\"{f:ns(.)}\"/></xsl:for-each></out></xsl:template>";

            // copy-namespaces="no": p arrives with no namespace nodes and gets one for its own name, the
            // rest coming down from the parent it was grafted under.
            Assert.AreEqual(
                "<out><e n=\"a\" ns=\"|=uri:a\"/><e n=\"b\" ns=\"|=uri:a|b=uri:b\"/><e n=\"c\" ns=\"|=uri:c|b=uri:b|d=uri:d\"/>"
                + "<e n=\"p\" ns=\"|=uri:p|b=uri:b|d=uri:d\"/><e n=\"q\" ns=\"|=uri:q|b=uri:b|d=uri:d\"/><e n=\"r\" ns=\"|=uri:r|b=uri:b|d=uri:d\"/></out>",
                Run(InScope + Trees
                    + "<xsl:template match=\"*\"><xsl:copy><xsl:apply-templates/></xsl:copy></xsl:template>"
                    + "<xsl:template match=\"graft\"><xsl:copy-of select=\"$inner\" copy-namespaces=\"no\"/></xsl:template>",
                    string.Empty,
                    "main"));

            // inherit-namespaces="no" on the copies: what a child did not declare for itself, it does not have.
            Assert.AreEqual(
                "<out><e n=\"a\" ns=\"|=uri:a\"/><e n=\"b\" ns=\"|=uri:a|b=uri:b\"/><e n=\"c\" ns=\"|=uri:c|b=uri:b|d=uri:d\"/>"
                + "<e n=\"p\" ns=\"|=uri:p\"/><e n=\"q\" ns=\"|=uri:q\"/><e n=\"r\" ns=\"|=uri:r|s=uri:s\"/></out>",
                Run(InScope + Trees
                    + "<xsl:template match=\"*\"><xsl:copy inherit-namespaces=\"no\"><xsl:apply-templates/></xsl:copy></xsl:template>"
                    + "<xsl:template match=\"graft\"><xsl:copy-of select=\"$inner\"/></xsl:template>",
                    string.Empty,
                    "main"));
        }


        [TestMethod]
        public void WhatACopiedParentTookAtTheCopyReachesNothingBeneathIt()
        {
            // copy-namespaces="no" gives a copied element the declarations its own name and attribute
            // names need and no others, and "its own" is said of every element of the copy: the a element
            // takes a prefix for its attribute, and the aa element inside it does not acquire that prefix
            // merely because its parent has it (§11.7.2). What the copy takes from where it was attached
            // is the other half of the same rule and reaches all of it, parent and child alike.
            const string Fragment =
                "<xsl:variable name=\"fragment\"><w xmlns:w=\"uri:w\">"
                + "<a xmlns:p=\"uri:p\" p:att=\"A\"><aa xmlns=\"uri:aa\"/></a></w></xsl:variable>";

            Assert.AreEqual(
                "<out><e n=\"doc\" ns=\"|q=uri:q\"/><e n=\"a\" ns=\"|p=uri:p|q=uri:q\"/>"
                + "<e n=\"aa\" ns=\"|=uri:aa|q=uri:q\"/></out>",
                Run(InScope + Fragment
                    + "<xsl:template name=\"main\"><xsl:variable name=\"result\">"
                    + "<doc xmlns:q=\"uri:q\"><xsl:copy-of select=\"$fragment/w/*\" copy-namespaces=\"no\"/></doc>"
                    + "</xsl:variable>"
                    + "<out><xsl:for-each select=\"$result//*\"><e n=\"{local-name()}\" ns=\"{f:ns(.)}\"/>"
                    + "</xsl:for-each></out></xsl:template>",
                    string.Empty,
                    "main"));
        }

        [TestMethod]
        public void SerializeWritesNoDeclarationUnlessAsked()
        {
            Assert.AreEqual(
                "<out>&lt;bar xmlns=\"urn:b\"&gt;test&lt;/bar&gt;</out>",
                Run(Root("<xsl:variable name=\"bar\" as=\"element()\"><xsl:copy-of select=\"/r/*\" copy-namespaces=\"no\"/></xsl:variable>"
                    + "<xsl:value-of select=\"serialize($bar)\"/>"),
                    "<r xmlns:x=\"urn:x\"><bar xmlns=\"urn:b\">test</bar></r>"));
            Assert.AreEqual(
                "<out>&lt;?xml version=\"1.0\" encoding=\"UTF-8\"?&gt;&lt;bar/&gt;</out>",
                Run(Root("<xsl:value-of select=\"serialize(/r/bar, map{'omit-xml-declaration': false()})\"/>"), "<r><bar/></r>"));
        }
    }
}
