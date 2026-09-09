namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the errors a stylesheet is refused with, by the code the specification gives each: the
    /// static ones when the stylesheet is read, the dynamic ones when the instruction runs.
    /// </summary>
    /// <remarks>
    /// A code is part of the contract: a caller catching <c>XTDE0540</c> is catching one thing, and a test
    /// suite asking for it is asking whether this engine draws the same line. Most of these are one-line
    /// checks whose whole content is where the line falls, which is why each test says what it refuses and
    /// little else.
    /// </remarks>
    [TestClass]
    public sealed class ErrorCodeTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" exclude-result-prefixes=\"xs f\">";

        private static string Run(string body, string input = "<r><a/><b/></r>", string? mode = null)
        {
            XsltOptions options = new XsltOptions
            {
                OmitXmlDeclaration = true,
                Version = XsltVersion.V30,
                InitialMode = mode,
            };

            Xslt xslt = new Xslt(Head + body + "</xsl:stylesheet>", options);
            return input.Length == 0 ? xslt.Transform() : xslt.TransformXml(input);
        }

        private static string Root(string content)
        {
            return "<xsl:template match=\"/\"><out>" + content + "</out></xsl:template>";
        }

        private static string Refuses(string body, string input = "<r><a/><b/></r>", string? mode = null)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, input, mode)).Code ?? string.Empty;
        }

        // ---- What is refused when the stylesheet is read ---------------------------------------------------

        [TestMethod]
        public void ANameIsMadeOfXmlNameCharacters()
        {
            // The micro sign is a letter to .NET and not a name character to XML, so an expression made of
            // it is not a name and not an expression (XPST0003).
            Assert.AreEqual("XPST0003", Refuses(Root("<xsl:value-of select=\"\u00b5\"/>")));
            Assert.AreEqual("<out>1</out>", Run(Root("<xsl:value-of select=\"count(/r/\u00e9)\"/>"), "<r><\u00e9/></r>"));
        }

        [TestMethod]
        public void AParentlessTextNodeHasNoBaseUriToResolveAgainst()
        {
            // XTDE1162: document() over a text node nothing constructed in a document has nothing to
            // resolve a relative reference against, and says so rather than asking the resolver.
            Assert.AreEqual(
                "XTDE1162",
                Refuses(
                    "<xsl:variable name=\"t\" as=\"text()\"><xsl:value-of select=\"'a.xml'\"/></xsl:variable>"
                    + Root("<xsl:copy-of select=\"document($t)\"/>")));
        }

        [TestMethod]
        public void AMergeSourceNamesItsDocumentsWithStrings()
        {
            Assert.AreEqual(
                "XPTY0004",
                Refuses(Root("<xsl:merge><xsl:merge-source for-each-source=\"1 to 2\" select=\".\">"
                    + "<xsl:merge-key select=\".\"/></xsl:merge-source><xsl:merge-action/></xsl:merge>")));
        }

        [TestMethod]
        public void ATerminatingMessageRaisesTheCodeItNames()
        {
            // error-code is the code a caller sees, in any spelling a name has; without one it is XTMM9000.
            Assert.AreEqual(
                "XTDE1420",
                Refuses(Root("<xsl:message terminate=\"yes\" error-code=\"Q{{http://www.w3.org/2005/xqt-errors}}XTDE1420\">x</xsl:message>")));
            Assert.AreEqual(
                "XTMM9000", Refuses(Root("<xsl:message terminate=\"yes\">x</xsl:message>")));
        }

        [TestMethod]
        public void WhitespaceDeclarationsAtOnePrecedenceMayNotDisagree()
        {
            Assert.AreEqual(
                "XTSE0270",
                Refuses("<xsl:strip-space elements=\"a\"/><xsl:preserve-space elements=\"a\"/>" + Root("")));
        }

        [TestMethod]
        public void KeysOfOneNameAgreeAboutComposite()
        {
            Assert.AreEqual(
                "XTSE1222",
                Refuses(
                    "<xsl:key name=\"k\" match=\"a\" use=\"@x\" composite=\"yes\"/>"
                    + "<xsl:key name=\"k\" match=\"b\" use=\"@x\"/>" + Root("")));
        }

        [TestMethod]
        public void AnIterateIsHeldToItsShape()
        {
            // A break has to be in a tail position, reached through nothing but xsl:if, xsl:choose and
            // xsl:try; one with no enclosing xsl:iterate at all is content the element may not hold. Neither
            // xsl:break nor xsl:on-completion takes both a select and content, and a parameter with no value
            // and a type that admits no empty sequence could never begin.
            Assert.AreEqual(
                "XTSE3120",
                Refuses(Root("<xsl:iterate select=\"1 to 3\"><xsl:if test=\". = 2\"><xsl:break/></xsl:if><x/></xsl:iterate>")));
            Assert.AreEqual(
                "XTSE0010",
                Refuses(Root("<xsl:iterate select=\"1 to 3\"><xsl:call-template name=\"t\"/></xsl:iterate>")
                    + "<xsl:template name=\"t\"><xsl:break/></xsl:template>"));
            Assert.AreEqual(
                "<out>x</out>",
                Run(Root("<xsl:iterate select=\"1 to 3\"><xsl:choose><xsl:when test=\". = 2\"><xsl:break/></xsl:when>"
                    + "<xsl:otherwise>x</xsl:otherwise></xsl:choose></xsl:iterate>")));
            Assert.AreEqual(
                "XTSE3125",
                Refuses(Root("<xsl:iterate select=\"1 to 3\"><xsl:break select=\"1\">x</xsl:break></xsl:iterate>")));
            Assert.AreEqual(
                "XTSE3125",
                Refuses(Root("<xsl:iterate select=\"1 to 3\"><xsl:on-completion select=\"1\">x</xsl:on-completion></xsl:iterate>")));
            Assert.AreEqual(
                "XTSE3520",
                Refuses(Root("<xsl:iterate select=\"1 to 3\"><xsl:param name=\"p\" as=\"xs:integer\"/></xsl:iterate>")));
        }

        [TestMethod]
        public void ATryAndACatchTakeOneSourceOfContentEach()
        {
            Assert.AreEqual(
                "XTSE3140",
                Refuses(Root("<xsl:try select=\"1\"><x/><xsl:catch/></xsl:try>")));
            Assert.AreEqual(
                "XTSE3150",
                Refuses(Root("<xsl:try select=\"1\"><xsl:catch select=\"2\"><x/></xsl:catch></xsl:try>")));
        }

        [TestMethod]
        public void MergeSourcesAndMapEntriesAreHeldToTheirShape()
        {
            Assert.AreEqual(
                "XTSE3190",
                Refuses(Root("<xsl:merge><xsl:merge-source name=\"s\" select=\"1\"><xsl:merge-key select=\".\"/></xsl:merge-source>"
                    + "<xsl:merge-source name=\"s\" select=\"2\"><xsl:merge-key select=\".\"/></xsl:merge-source>"
                    + "<xsl:merge-action/></xsl:merge>")));
            Assert.AreEqual(
                "XTSE3280",
                Refuses(Root("<xsl:map><xsl:map-entry key=\"1\" select=\"2\"><xsl:sequence select=\"3\"/></xsl:map-entry></xsl:map>")));
        }

        [TestMethod]
        public void AModeMayRefuseTwoRulesOfOneRank()
        {
            // on-multiple-match="fail" makes a tie XTDE0540 where the later rule would otherwise win; two
            // declarations of the mode disagreeing about it are XTSE0545, like any other attribute.
            Assert.AreEqual(
                "XTDE0540",
                Refuses(
                    "<xsl:mode on-multiple-match=\"fail\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/a\"/></out></xsl:template>"
                    + "<xsl:template match=\"a[not(*)]\"><x/></xsl:template>"
                    + "<xsl:template match=\"a[parent::r]\"><y/></xsl:template>"));
            Assert.AreEqual(
                "XTSE0545",
                Refuses("<xsl:mode on-multiple-match=\"fail\"/><xsl:mode on-multiple-match=\"use-last\"/>" + Root("")));
        }

        // ---- What is refused when the transformation runs --------------------------------------------------

        [TestMethod]
        public void AFunctionItemHasNoPlaceInContent()
        {
            Assert.AreEqual("XTDE0450", Refuses(Root("<x><xsl:sequence select=\"true#0\"/></x>")));
        }

        [TestMethod]
        public void ATemplateIsHeldToTheTypeItDeclares()
        {
            // The whole of what it produced, a call its body ends in included.
            Assert.AreEqual(
                "XTTE0505",
                Refuses(Root("<xsl:call-template name=\"t\"/>")
                    + "<xsl:template name=\"t\" as=\"xs:integer\"><xsl:sequence select=\"current-date()\"/></xsl:template>"));
            Assert.AreEqual(
                "<out>ab</out>",
                Run(Root("<xsl:call-template name=\"t\"/>")
                    + "<xsl:template name=\"t\" as=\"xs:string\"><xsl:call-template name=\"u\"/></xsl:template>"
                    + "<xsl:template name=\"u\" as=\"xs:string\"><xsl:sequence select=\"'ab'\"/></xsl:template>"));
        }

        [TestMethod]
        public void TheGroupingFunctionsNeedAGroup()
        {
            Assert.AreEqual("XTDE1061", Refuses(Root("<xsl:value-of select=\"current-group()\"/>")));
            Assert.AreEqual("XTDE1071", Refuses(Root("<xsl:value-of select=\"current-grouping-key()\"/>")));
        }

        [TestMethod]
        public void AnInitialModeNeedsSomethingToApplyTemplatesTo()
        {
            // With no source document there is nothing to apply templates to in the mode, XTDE0044 — unless
            // the mode is one nobody declares, which is refused first as XTDE0045.
            Assert.AreEqual("XTDE0044", Refuses(Root("") + "<xsl:template match=\"/\" mode=\"m\"/>", string.Empty, "m"));
            Assert.AreEqual("XTDE0045", Refuses(Root(""), string.Empty, "nonesuch"));
        }

        [TestMethod]
        public void AnAccumulatorFunctionWantsANodeThatIsNotAnAttribute()
        {
            const string Counting =
                "<xsl:mode use-accumulators=\"#all\"/>"
                + "<xsl:accumulator name=\"n\" initial-value=\"0\"><xsl:accumulator-rule match=\"*\" select=\"$value + 1\"/></xsl:accumulator>";

            Assert.AreEqual(
                "XTTE3360",
                Refuses(Counting + Root("<xsl:for-each select=\"1 to 2\"><xsl:value-of select=\"accumulator-after('n')\"/></xsl:for-each>")));
            Assert.AreEqual(
                "XTTE3360",
                Refuses(Counting + Root("<xsl:for-each select=\"/r/a/@x\"><xsl:value-of select=\"accumulator-after('n')\"/></xsl:for-each>"),
                    "<r><a x=\"1\"/></r>"));
        }
        [TestMethod]
        public void APrefixTheStylesheetNeverDeclaredIsUnbound()
        {
            // XPath declares xs, fn and a few more for you; XSLT does not. A stylesheet's statically known
            // namespaces are the ones in scope where the expression is written and nothing besides
            // (XSLT 3.0 §5.4.1), so a stylesheet writing fn:string() having declared no fn is told the
            // prefix is unbound rather than quietly given the library it happened to mean.
            Assert.AreEqual("XPST0081", Refuses(Root("<xsl:value-of select=\"fn:string(1)\"/>")));
            Assert.AreEqual("XPST0081", Refuses(Root("<xsl:value-of select=\"math:pi()\"/>")));

            // The unprefixed form is the one that needs nothing declared, the standard function namespace
            // being the default for a function name.
            Assert.AreEqual("<out>1</out>", Run(Root("<xsl:value-of select=\"string(1)\"/>")));
        }
        [TestMethod]
        public void ACastToANameResolvesItsPrefixWhereTheCastIsWritten()
        {
            // XPath 2.0 would cast only a literal to xs:QName, a prefix meaning what it meant where it was
            // written and nothing else being there to read it; 3.0 kept the static context as the answer and
            // dropped the restriction, so the bindings in scope travel with the cast.
            Assert.AreEqual(
                "<out>urn:f</out>",
                Run(Root(
                    "<xsl:value-of select=\"namespace-uri-from-QName("
                    + "string('f:thing') cast as xs:QName)\"/>")));

            // A prefix that nothing binds where the cast stands has nowhere to come from.
            Assert.AreEqual(
                "FONS0004",
                Refuses(Root("<xsl:value-of select=\"string('nope:thing') cast as xs:QName\"/>")));
        }
    }
}
