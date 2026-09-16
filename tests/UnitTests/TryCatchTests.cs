namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:try</c> and <c>xsl:catch</c>: what an error is caught by, what a clause is told about
    /// it, and what the try does with the output its body had written.
    /// </summary>
    [TestClass]
    public sealed class TryCatchTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:err=\"http://www.w3.org/2005/xqt-errors\""
            + " exclude-result-prefixes=\"xs err\">";

        private static string Run(string body, string input = "<r/>", IXsltResolver? documents = null)
        {
            XsltOptions options = new XsltOptions
            {
                OmitXmlDeclaration = true,
                Version = XsltVersion.V30,
                DocumentResolver = documents,
                BaseUri = "file:///sheets/main.xsl",
            };

            return new Xslt(Head + body + "</xsl:stylesheet>", options).TransformXml(input);
        }

        private static string Root(string content)
        {
            return "<xsl:template match=\"/\"><out>" + content + "</out></xsl:template>";
        }

        private static string Refuses(string body)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body)).Code ?? string.Empty;
        }

        [TestMethod]
        public void OutputEscapingStaysDisabledThroughATry()
        {
            // An xsl:try holds its body back so that an error can take it away again, and that buffer
            // stands in for the final output: nothing else is between the instruction and the serializer,
            // so a text node written with the escaping off is still written with it off. Inside an element
            // as well as at the top, which is where the suite's doe-0191 puts it.
            Assert.AreEqual(
                "<out><z>&lt;</z>10</out>",
                Run(Root(
                    "<xsl:try><z><xsl:value-of select=\"'&amp;lt;'\" disable-output-escaping=\"yes\"/></z>"
                    + "<xsl:value-of select=\"10 idiv 1\"/>"
                    + "<xsl:catch errors=\"*\"><error/></xsl:catch></xsl:try>")));

            // And where the try does catch, nothing of the body survives to be escaped either way.
            Assert.AreEqual(
                "<out><error/></out>",
                Run(Root(
                    "<xsl:try><z><xsl:value-of select=\"'&amp;lt;'\" disable-output-escaping=\"yes\"/></z>"
                    + "<xsl:value-of select=\"10 idiv 0\"/>"
                    + "<xsl:catch errors=\"*\"><error/></xsl:catch></xsl:try>")));

            // A variable is not that buffer: the specification lets a text node lose the flag on its way
            // into one, and this engine does — the suite's doe-0186 turns on it.
            Assert.AreEqual(
                "<out><z>&amp;lt;</z></out>",
                Run(Root(
                    "<xsl:variable name=\"v\"><z>"
                    + "<xsl:value-of select=\"'&amp;lt;'\" disable-output-escaping=\"yes\"/></z></xsl:variable>"
                    + "<xsl:copy-of select=\"$v\"/>")));
        }

        [TestMethod]
        public void AnErrorCodeKeepsItsNamespace()
        {
            // fn:error() may raise a code in any namespace or in none, and a clause matches the whole name:
            // an unprefixed name in 'errors' is in no namespace, whatever the default namespace says
            // (§8.3), and $err:code compares equal to the QName that was raised.
            Assert.AreEqual(
                "<out>Caught</out>",
                Run(Root(
                    "<xsl:try select=\"error(QName('', 'too-late'))\" xpath-default-namespace=\"urn:x\">"
                    + "<xsl:catch errors=\"too-late\" select=\"'Caught'\"/>"
                    + "<xsl:catch errors=\"*\" select=\"'Fail'\"/></xsl:try>")));

            Assert.AreEqual(
                "<out>Bang!</out>",
                Run(Root(
                    "<xsl:variable name=\"mine\" as=\"xs:QName\" select=\"xs:QName('mine')\"/>"
                    + "<xsl:try select=\"error($mine)\">"
                    + "<xsl:catch><xsl:value-of select=\"'Bang!'[$err:code eq $mine]\"/></xsl:catch>"
                    + "</xsl:try>")));

            Assert.AreEqual(
                "<out>err:FOER0000</out>",
                Run(Root(
                    "<xsl:try select=\"error()\"><xsl:catch select=\"string($err:code)\"/></xsl:try>")));
        }

        [TestMethod]
        public void AClauseIsToldWhereTheErrorStands()
        {
            // The module and line of the instruction that was running (§8.3): the stylesheet's base URI
            // for the principal module, and the line the xsl:sequence stands on — the third line of the
            // text, the head and the template being the first two.
            string result = Run(
                "\n<xsl:template match=\"/\"><out>\n"
                + "<xsl:try><xsl:sequence select=\"1 div 0\"/>"
                + "<xsl:catch select=\"$err:code, $err:module, $err:line-number, exists($err:column-number)\"/>"
                + "</xsl:try></out></xsl:template>");

            Assert.AreEqual("<out>err:FOAR0001 file:///sheets/main.xsl 3 true</out>", result);
        }

        [TestMethod]
        public void AMissingDocumentIsFODC0002()
        {
            Assert.AreEqual(
                "<out>err:FODC0002</out>",
                Run(
                    Root("<xsl:try><xsl:sequence select=\"doc('missing.xml')\"/>"
                        + "<xsl:catch errors=\"*\" select=\"string($err:code)\"/></xsl:try>"),
                    documents: new NothingResolver()));
        }

        [TestMethod]
        public void ATryDoesNotMakeItsBodyTemporaryOutput()
        {
            // A result document may be written from inside a try (§25): the buffer kept for the rollback
            // stands for the output the try is writing to. A variable inside the try is still temporary
            // output state, and so is a try inside a variable.
            ResultCollector results = new ResultCollector();
            XsltOptions options = new XsltOptions
            {
                OmitXmlDeclaration = true,
                Version = XsltVersion.V30,
                ResultResolver = results,
                BaseOutputUri = "file:///out/",
            };

            string result = new Xslt(
                Head + Root("<xsl:try><xsl:result-document href=\"a.xml\"><a/></xsl:result-document>"
                    + "<xsl:catch select=\"string($err:code)\"/></xsl:try>"
                    + "<xsl:try><xsl:variable name=\"v\"><xsl:result-document href=\"b.xml\"><b/></xsl:result-document></xsl:variable>"
                    + "<xsl:catch select=\"string($err:code)\"/></xsl:try>"
                    + "<xsl:variable name=\"w\"><xsl:try><xsl:result-document href=\"c.xml\"><c/></xsl:result-document>"
                    + "<xsl:catch select=\"string($err:code)\"/></xsl:try></xsl:variable>"
                    + "<xsl:value-of select=\"$w\"/>")
                + "</xsl:stylesheet>",
                options).TransformXml("<r/>");

            Assert.AreEqual("<out>err:XTDE1480err:XTDE1480</out>", result);
            Assert.AreEqual(1, results.Written.Count);
            StringAssert.EndsWith(results.Written["file:///out/a.xml"].ToString(), "<a/>");
        }

        [TestMethod]
        public void WithoutRollbackAnErrorAfterOutputIsXTDE3530()
        {
            // rollback-output="no" keeps no buffer, so an error before anything was written is caught as
            // ever, and one after is XTDE3530 (§8.3.1), the state of the output being what it is.
            Assert.AreEqual(
                "<out>err:FOAR0001</out>",
                Run(Root("<xsl:try rollback-output=\"no\"><xsl:sequence select=\"1 div 0\"/>"
                    + "<xsl:catch select=\"string($err:code)\"/></xsl:try>")));

            Assert.AreEqual(
                "XTDE3530",
                Refuses(Root("<xsl:try rollback-output=\"no\"><a/><xsl:sequence select=\"1 div 0\"/>"
                    + "<xsl:catch select=\"string($err:code)\"/></xsl:try>")));

            // With the default the output is taken back, however much of it there was.
            Assert.AreEqual(
                "<out>err:FOAR0001</out>",
                Run(Root("<xsl:try><a/><b/><xsl:sequence select=\"1 div 0\"/>"
                    + "<xsl:catch select=\"string($err:code)\"/></xsl:try>")));
        }

        [TestMethod]
        public void EveryDefinedElementIsAvailableAtThreePointZero()
        {
            // §24.2.2: the question is no longer whether the name is an instruction but whether the element
            // is defined and implemented, so xsl:catch is available where xsl:try is.
            Assert.AreEqual(
                "<out>true true false</out>",
                Run(Root("<xsl:value-of select=\"element-available('xsl:try'), element-available('xsl:catch'), "
                    + "element-available('xsl:nonsense')\"/>")));
        }

        private sealed class NothingResolver : IXsltResolver
        {
            public ResolvedResource? Resolve(string href, string? baseUri) => null;
        }

        private sealed class ResultCollector : IXsltResultResolver
        {
            public Dictionary<string, StringWriter> Written { get; } = new(StringComparer.Ordinal);

            public TextWriter Resolve(string href, string? baseUri)
            {
                StringWriter writer = new StringWriter();
                Written[href] = writer;
                return writer;
            }
        }
    }
}
