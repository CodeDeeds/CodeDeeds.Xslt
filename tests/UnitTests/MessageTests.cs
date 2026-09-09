namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:message</c>: what it reports, what it does when it cannot, and what a terminating
    /// one hands to whoever catches it.
    /// </summary>
    [TestClass]
    public sealed class MessageTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private static (string Result, string Reported) Run(
            string body,
            XsltVersion? version = null,
            string declared = "3.0")
        {
            StringWriter messages = new StringWriter();

            string result = new Xslt(
                "<xsl:stylesheet version=\"" + declared + "\" " + Xsl
                + " xmlns:err=\"http://www.w3.org/2005/xqt-errors\" exclude-result-prefixes=\"err\">"
                + body + "</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Version = version ?? XsltVersion.V30,
                    MessageWriter = messages,
                }).TransformXml("<r/>");

            return (result, messages.ToString());
        }

        private static string Root(string content)
        {
            return "<xsl:template match=\"/\"><out>" + content + "</out></xsl:template>";
        }

        private static string CodeFrom(string body, XsltVersion? version = null, string declared = "3.0")
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, version, declared)).Code
                ?? string.Empty;
        }

        [TestMethod]
        public void TerminateTakesEverySpellingOfATruthTheProcessorReads()
        {
            // The attribute is a value template, so what it says is settled while the transformation runs —
            // where the element table, which is what holds a 2.0 stylesheet to the two spellings 2.0 had,
            // cannot see it. Surrounding whitespace is not part of the value: the attribute is token-typed.
            foreach (string no in new[] { "no", "false", "0", " 0 " })
            {
                Assert.AreEqual(
                    "<out>on</out>",
                    Run(Root("<xsl:message terminate=\"{'" + no + "'}\">going</xsl:message>on")).Result);
            }

            foreach (string yes in new[] { "yes", "true", "1" })
            {
                Assert.AreEqual(
                    "XTMM9000",
                    CodeFrom(Root("<xsl:message terminate=\"{'" + yes + "'}\">stop</xsl:message>")));
            }

            // A 2.0 processor reads the four spellings 3.0 added as a mistake rather than as a truth, since
            // reading one of them as "no" would let a stylesheet ask to stop and be quietly carried on.
            Assert.AreEqual(
                "XTDE0030",
                CodeFrom(
                    Root("<xsl:message terminate=\"{'false'}\">going</xsl:message>"),
                    XsltVersion.V20,
                    "2.0"));
        }

        [TestMethod]
        public void AMessageMayBeGivenAsAnExpression()
        {
            Assert.AreEqual("twenty-one", Run(Root("<xsl:message select=\"'twenty-one'\"/>")).Reported.Trim());

            // Several items are a list rather than a run-on string, which is what the separator is for.
            Assert.AreEqual("1 2 3", Run(Root("<xsl:message select=\"1 to 3\"/>")).Reported.Trim());
        }

        [TestMethod]
        public void AMessageThatCannotBeBuiltDoesNotStopTheTransformation()
        {
            // A diagnostic that fails is still only a diagnostic: XSLT 3.0 says in as many words that the
            // transformation must not fail because the content of an xsl:message could not be evaluated.
            (string result, string reported) =
                Run(Root("<xsl:message><xsl:value-of select=\"100 idiv 0\"/></xsl:message>kept"));

            Assert.AreEqual("<out>kept</out>", result);
            StringAssert.Contains(reported, "could not be reported");

            // And one that cannot be rendered rather than not evaluated, which is the same thing from the
            // instruction's side: a map is a value it may be given and no value a message can be made of.
            Assert.AreEqual("<out>kept</out>", Run(Root("<xsl:message select=\"map{1:2}\"/>kept")).Result);
        }

        [TestMethod]
        public void AnErrorCodeThatIsNoNameLeavesTheMessageItsOwn()
        {
            // The attribute is computed, so it may come out as something no more a QName than a number is.
            // The specification gives that no code, the instruction already having one.
            Assert.AreEqual(
                "XTMM9000",
                CodeFrom(Root("<xsl:message error-code=\"{23}CODE\" terminate=\"yes\">stop</xsl:message>")));

            // A name is taken as one, and an unprefixed code is in the namespace the specifications' own
            // errors live in, which is where a caller reads it by its local part alone.
            Assert.AreEqual(
                "mine",
                CodeFrom(Root("<xsl:message error-code=\"mine\" terminate=\"yes\">stop</xsl:message>")));
        }

        [TestMethod]
        public void AnErrorCarriesWhatTheStylesheetAttachedToIt()
        {
            // A terminating message hands its content to whoever catches it, and hands it over as the
            // sequence it built rather than as text: a catch may go into what the message said.
            Assert.AreEqual(
                "<out><test>Take me<!--REALLY--></test></out>",
                Run(Root(
                    "<xsl:try><xsl:message terminate=\"yes\">"
                    + "<test>Take me<xsl:comment>REALLY</xsl:comment></test></xsl:message>"
                    + "<xsl:catch><xsl:sequence select=\"$err:value\"/></xsl:catch></xsl:try>")).Result);

            // And so does fn:error(), whose third argument is the error object for the same reason.
            Assert.AreEqual(
                "<out>42</out>",
                Run(Root(
                    "<xsl:try select=\"error(QName('', 'mine'), 'no good', 42)\">"
                    + "<xsl:catch select=\"$err:value\"/></xsl:try>")).Result);

            // An error this engine raised on its own account carries a message and nothing else, so there
            // is nothing for the value to be.
            Assert.AreEqual(
                "<out>0</out>",
                Run(Root(
                    "<xsl:try select=\"100 idiv 0\">"
                    + "<xsl:catch select=\"count($err:value)\"/></xsl:try>")).Result);
        }
    }
}
