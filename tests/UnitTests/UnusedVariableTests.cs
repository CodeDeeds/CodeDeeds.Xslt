namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for a local variable nothing reads, which is not evaluated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The specification allows it in as many words: "if a variable is declared but never referenced, an
    /// implementation may choose whether or not to evaluate the variable declaration". What makes it worth
    /// doing rather than merely allowed is that the declaration may be the only thing standing between a
    /// stylesheet and a circularity it never actually has.
    /// </para>
    /// <para>
    /// Only where the value comes from a <c>select</c>. A sequence constructor may hold an
    /// <c>xsl:message</c>, which is an effect the stylesheet asked for and can see.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class UnusedVariableTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private static string Run(string body, out string messages)
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\" xmlns:f=\"urn:f\" "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"f xs\">"
                + body
                + "</xsl:stylesheet>";

            string Once(XsltBackend backend, out string written)
            {
                System.IO.StringWriter sink = new System.IO.StringWriter();

                string result = new Xslt(
                    stylesheet,
                    new XsltOptions
                    {
                        Backend = backend,
                        Version = XsltVersion.V30,
                        OmitXmlDeclaration = true,
                        MessageWriter = sink,
                    })
                    .TransformXml("<r/>");

                written = sink.ToString();
                return result;
            }

            string interpreted = Once(XsltBackend.Interpreted, out messages);
            string compiled = Once(XsltBackend.Compiled, out string compiledMessages);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            Assert.AreEqual(messages, compiledMessages, "the two backends wrote different messages");
            return interpreted;
        }

        private static string Run(string body) => Run(body, out _);

        [TestMethod]
        public void AVariableNothingReadsIsNotEvaluated()
        {
            // The value would be an error if it were worked out, and it is not.
            Assert.AreEqual(
                "<out>fine</out>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"bad\" select=\"1 idiv 0\"/><out>fine</out></xsl:template>"));

            // Read, and the error is the stylesheet's own.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"bad\" select=\"1 idiv 0\"/>"
                    + "<out><xsl:value-of select=\"$bad\"/></out></xsl:template>"));

            Assert.AreEqual("FOAR0001", error.Code);
        }

        [TestMethod]
        public void ACircularityNothingReachesIsNotACircularity()
        {
            // The suite's param-0301. The global is computed by the function, and the function declares a
            // variable naming the global — which would be a circularity if the variable were worked out.
            Assert.AreEqual(
                "<out>3</out>",
                Run(
                    "<xsl:variable name=\"x\" select=\"f:go(1)\"/>"
                    + "<xsl:function name=\"f:go\"><xsl:param name=\"a\"/>"
                    + "<xsl:variable name=\"b\" select=\"$x\"/>"
                    + "<xsl:sequence select=\"$a + 2\"/></xsl:function>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"$x\"/></out></xsl:template>"));

            // Read, and the circularity is real and reported.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:variable name=\"x\" select=\"f:go(1)\"/>"
                    + "<xsl:function name=\"f:go\"><xsl:param name=\"a\"/>"
                    + "<xsl:variable name=\"b\" select=\"$x\"/>"
                    + "<xsl:sequence select=\"$a + 2 + count($b)\"/></xsl:function>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"$x\"/></out></xsl:template>"));

            StringAssert.Contains(error.Message, "itself");
        }

        [TestMethod]
        public void AVariableWrittenAsContentIsEvaluatedEitherWay()
        {
            // A sequence constructor may write a message, and a message is something the stylesheet asked
            // for: it is not the variable's value, so nothing about the value being unread settles it.
            Assert.AreEqual(
                "<out/>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"v\"><xsl:message>seen</xsl:message>x</xsl:variable>"
                    + "<out/></xsl:template>",
                    out string messages));

            StringAssert.Contains(messages, "seen");
        }

        [TestMethod]
        public void ShadowingIsCountedAgainstTheDeclarationThatIsInScope()
        {
            // Two variables of one name, the inner one shadowing the outer. The reference reaches the
            // inner, so the outer is never read and never worked out — and the answer is the inner one's.
            Assert.AreEqual(
                "<out>inner</out>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"v\" select=\"1 idiv 0\"/>"
                    + "<out><xsl:for-each select=\"/r\">"
                    + "<xsl:variable name=\"v\" select=\"'inner'\"/>"
                    + "<xsl:value-of select=\"$v\"/></xsl:for-each></out></xsl:template>"));
        }
    }
}
