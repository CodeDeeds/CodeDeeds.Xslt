namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the ways into a transformation other than matching a source document.
    /// </summary>
    /// <remarks>
    /// XSLT 3.0 §2.3 gives three: apply templates to a document, call a named template, call a named
    /// function. The second is what a stylesheet that generates rather than transforms needs — there is no
    /// input to match against, so there is nothing for a pattern to be about.
    /// </remarks>
    [TestClass]
    public sealed class EntryPointTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private static Xslt Compile(string body, string? template = null, string? mode = null)
        {
            return new Xslt(
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>{body}</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    InitialTemplate = template,
                    InitialMode = mode,
                });
        }

        [TestMethod]
        public void AStylesheetMayBeRunWithNothingToTransform()
        {
            Assert.AreEqual(
                "<out>generated</out>",
                Compile("<xsl:template name=\"go\"><out>generated</out></xsl:template>", "go").Transform());
        }

        [TestMethod]
        public void WithNoSourceDocumentThereIsNoContextItemAtAll()
        {
            // Absent, which is not the same as empty: reading it is an error, and a good many stylesheets
            // are written to rely on that.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile("<xsl:template name=\"go\"><out><xsl:value-of select=\".\"/></out></xsl:template>", "go")
                    .Transform());

            Assert.AreEqual("XPDY0002", error.Code);
        }

        [TestMethod]
        public void ASourceDocumentSuppliedAlongsideIsTheContextItem()
        {
            Assert.AreEqual(
                "<out>doc</out>",
                Compile(
                    "<xsl:template name=\"go\"><out><xsl:value-of select=\"name(*)\"/></out></xsl:template>",
                    "go")
                    .TransformXml("<doc/>"));
        }

        [TestMethod]
        public void GlobalParametersReachATransformationStartedAtATemplate()
        {
            Xslt stylesheet = new Xslt(
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:param name=\"who\"/>"
                + "<xsl:template name=\"go\"><out><xsl:value-of select=\"$who\"/></out></xsl:template>"
                + "</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    InitialTemplate = "go",
                    Parameters = new Dictionary<string, object?> { ["who"] = "world" },
                });

            Assert.AreEqual("<out>world</out>", stylesheet.Transform());
        }

        [TestMethod]
        public void ATemplateThatIsNotThereIsNamedInTheError()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile("<xsl:template name=\"go\"><out/></xsl:template>", "nope").Transform());

            Assert.AreEqual("XTDE0040", error.Code);
            StringAssert.Contains(error.Message, "nope");
        }

        [TestMethod]
        public void WithNeitherASourceNorATemplateThereIsNothingToDo()
        {
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => Compile("<xsl:template match=\"/\"><out/></xsl:template>").Transform()).Message,
                "InitialTemplate");
        }

        [TestMethod]
        public void ANameInANamespaceIsWrittenAsTheCallerCanKnowIt()
        {
            // XSLT 3.0's own entry point is xsl:initial-template. A caller has no way to know the
            // stylesheet's prefixes, so it names the namespace outright.
            Assert.AreEqual(
                "<out>3.0</out>",
                Compile(
                    "<xsl:template name=\"xsl:initial-template\"><out>3.0</out></xsl:template>",
                    "{http://www.w3.org/1999/XSL/Transform}initial-template")
                    .Transform());
        }

        [TestMethod]
        public void ATransformationMayStartInANamedMode()
        {
            // A stylesheet whose rules are all in a mode has no unnamed rules to start from, and without
            // this the transformation falls through to the built-in rules and produces the document's text.
            Assert.AreEqual(
                "<in-m/>",
                Compile(
                    "<xsl:template match=\"doc\" mode=\"m\"><in-m/></xsl:template>"
                    + "<xsl:template match=\"doc\"><plain/></xsl:template>",
                    mode: "m")
                    .TransformXml("<doc/>"));
        }

        [TestMethod]
        public void AModeThatIsNotThereIsNamedInTheError()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile("<xsl:template match=\"doc\"><plain/></xsl:template>", mode: "zzz")
                    .TransformXml("<doc/>"));

            Assert.AreEqual("XTDE0045", error.Code);
        }

        [TestMethod]
        public void TheTwoEntryPointsCombine()
        {
            // XSLT 2.0 took one way in or the other and refused the pair outright (XTDE0047, which
            // ErrorConditionTests holds). 3.0 dropped the error, so this asks for 3.0: a mode is worth
            // naming alongside a template for what the template's own xsl:apply-templates does with it.
            Assert.AreEqual(
                "<in-m/>",
                new Xslt(
                    "<xsl:stylesheet version=\"3.0\" " + Xsl + ">"
                    + "<xsl:template name=\"go\"><xsl:apply-templates select=\"doc\" mode=\"#current\"/></xsl:template>"
                    + "<xsl:template match=\"doc\" mode=\"m\"><in-m/></xsl:template>"
                    + "<xsl:template match=\"doc\"><plain/></xsl:template>"
                    + "</xsl:stylesheet>",
                    new XsltOptions
                    {
                        Version = XsltVersion.V30,
                        OmitXmlDeclaration = true,
                        InitialTemplate = "go",
                        InitialMode = "m",
                    })
                    .TransformXml("<doc/>"));
        }

        [TestMethod]
        public void BothBackendsAgreeAboutTheEntryPoint()
        {
            const string Body =
                "<xsl:template name=\"go\"><out><xsl:value-of select=\"1 + 1\"/></out></xsl:template>";

            string Run(XsltBackend backend) => new Xslt(
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>{Body}</xsl:stylesheet>",
                new XsltOptions
                {
                    Backend = backend,
                    OmitXmlDeclaration = true,
                    InitialTemplate = "go",
                }).Transform();

            Assert.AreEqual(Run(XsltBackend.Interpreted), Run(XsltBackend.Compiled));
        }

        // ---- What the caller may supply to the way in ------------------------------------------------------

        /// <summary>Starts a transformation at a named template with parameters supplied to it.</summary>
        private static string Starts(
            string body,
            IReadOnlyDictionary<string, object?>? template = null,
            IReadOnlyDictionary<string, object?>? tunnel = null,
            XsltVersion? implemented = null,
            string declared = "3.0")
        {
            return new Xslt(
                "<xsl:stylesheet version=\"" + declared + "\" " + Xsl
                    + " xmlns:my=\"urn:mine\" exclude-result-prefixes=\"my\">" + body + "</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Version = implemented ?? XsltVersion.V30,
                    InitialTemplate = "go",
                    TemplateParameters = template,
                    TunnelParameters = tunnel,
                }).Transform();
        }

        [TestMethod]
        public void TheCallerMaySupplyTheParametersOfTheTemplateItStartsAt()
        {
            // A transformation started at a named template is a call, and XSLT 3.0 lets the caller supply
            // its arguments. The names are the caller's own form — {uri}local — because the caller's
            // prefixes are not the stylesheet's and there would be nothing to resolve them against.
            Assert.AreEqual(
                "<out>1234 999</out>",
                Starts(
                    "<xsl:template name=\"go\">"
                    + "<xsl:param name=\"a\"/><xsl:param name=\"my:b\" tunnel=\"yes\"/>"
                    + "<out><xsl:value-of select=\"$a, $my:b\"/></out></xsl:template>",
                    new Dictionary<string, object?> { ["a"] = 1234 },
                    new Dictionary<string, object?> { ["{urn:mine}b"] = 999 }));

            // A tunnel parameter passes through every template that does not declare it, so it reaches as
            // far down as the transformation goes rather than stopping where it went in.
            Assert.AreEqual(
                "<out>999</out>",
                Starts(
                    "<xsl:template name=\"go\"><out><xsl:call-template name=\"deeper\"/></out></xsl:template>"
                    + "<xsl:template name=\"deeper\"><xsl:call-template name=\"deepest\"/></xsl:template>"
                    + "<xsl:template name=\"deepest\"><xsl:param name=\"my:b\" tunnel=\"yes\"/>"
                    + "<xsl:value-of select=\"$my:b\"/></xsl:template>",
                    null,
                    new Dictionary<string, object?> { ["{urn:mine}b"] = 999 }));

            // A name the template does not declare is ignored, as a stylesheet parameter is: one set of
            // values can then serve several entry points.
            Assert.AreEqual(
                "<out>1234</out>",
                Starts(
                    "<xsl:template name=\"go\"><xsl:param name=\"a\"/>"
                    + "<out><xsl:value-of select=\"$a\"/></out></xsl:template>",
                    new Dictionary<string, object?> { ["a"] = 1234, ["nobody-declared-this"] = 7 }));
        }

        [TestMethod]
        public void ARequiredParameterOfTheEntryPointHasToBeSupplied()
        {
            const string Body =
                "<xsl:template name=\"go\"><xsl:param name=\"a\" required=\"yes\"/>"
                + "<out><xsl:value-of select=\"$a\"/></out></xsl:template>";

            // The caller's mistake rather than the stylesheet's: a template reached from inside the
            // stylesheet is checked where the call is written, and one the transformation starts at has no
            // call to check. XSLT 3.0 renamed the code XSLT 2.0 gave this, and the processor's version
            // decides which is reported.
            Assert.AreEqual(
                "XTDE0700",
                Assert.ThrowsExactly<XsltException>(() => Starts(Body)).Code);

            Assert.AreEqual(
                "XTDE0060",
                Assert.ThrowsExactly<XsltException>(
                    () => Starts(Body, implemented: XsltVersion.V20, declared: "2.0")).Code);

            // Supplied, it runs — and XSLT 2.0 had no way for a caller to supply one at all, so a 2.0
            // processor has nowhere to put what was offered and the parameter is unsupplied still.
            Assert.AreEqual(
                "<out>12</out>",
                Starts(Body, new Dictionary<string, object?> { ["a"] = 12 }));

            Assert.AreEqual(
                "XTDE0060",
                Assert.ThrowsExactly<XsltException>(
                    () => Starts(
                        Body,
                        new Dictionary<string, object?> { ["a"] = 12 },
                        implemented: XsltVersion.V20,
                        declared: "2.0")).Code);
        }
    }
}
