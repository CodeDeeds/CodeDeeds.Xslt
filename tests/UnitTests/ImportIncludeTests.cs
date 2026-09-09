namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:include</c>, <c>xsl:import</c>, and the resolver that decides what a stylesheet is
    /// allowed to reach.
    /// </summary>
    [TestClass]
    public sealed class ImportIncludeTests
    {
        /// <summary>Serves stylesheets from a dictionary, so most tests need no files at all.</summary>
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_modules = new(StringComparer.Ordinal);

            public int ResolveCount { get; private set; }

            public MapResolver Add(string name, string stylesheet)
            {
                m_modules[name] = stylesheet;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                ResolveCount++;
                return m_modules.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static string Sheet(string body)
        {
            return "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + body
                + "</xsl:stylesheet>";
        }

        private static string Run(string stylesheet, string input, IXsltResolver? resolver)
        {
            XsltOptions Options(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                StylesheetResolver = resolver,

                // Claimed for the static-variable tests below, which are 3.0's. It changes nothing for the
                // 1.0 stylesheets above: a claim only decides what a stylesheet naming a *later* version
                // gets, and none of them names one.
                Version = XsltVersion.V30,
            };

            string interpreted = new Xslt(stylesheet, Options(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, Options(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        // ---- The trust boundary --------------------------------------------------------------------------

        [TestMethod]
        public void WithoutAResolverAReferenceIsRefused()
        {
            // The default posture: a stylesheet cannot reach anything the caller has not opted into.
            foreach (string kind in new[] { "include", "import" })
            {
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(Sheet($"<xsl:{kind} href=\"other.xsl\"/>"
                        + "<xsl:template match=\"/\"><a/></xsl:template>")));

                StringAssert.Contains(error.Message, "resolver");
                StringAssert.Contains(error.Message, "other.xsl");
            }
        }

        [TestMethod]
        public void AReferenceTheResolverDeclinesIsReported()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(
                    Sheet("<xsl:include href=\"missing.xsl\"/><xsl:template match=\"/\"><a/></xsl:template>"),
                    new XsltOptions { StylesheetResolver = new MapResolver() }));

            StringAssert.Contains(error.Message, "missing.xsl");
        }

        [TestMethod]
        public void AReferenceCycleIsReportedRatherThanLoopingForever()
        {
            MapResolver resolver = new MapResolver()
                .Add("a.xsl", Sheet("<xsl:include href=\"b.xsl\"/>"))
                .Add("b.xsl", Sheet("<xsl:include href=\"a.xsl\"/>"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(
                    Sheet("<xsl:include href=\"a.xsl\"/><xsl:template match=\"/\"><o/></xsl:template>"),
                    new XsltOptions { StylesheetResolver = resolver }));

            StringAssert.Contains(error.Message, "cycle");
        }

        [TestMethod]
        public void FileResolverRefusesToLeaveItsRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "xslt-resolver-" + Guid.NewGuid().ToString("N"));
            string outside = Path.Combine(root, "..", "secret.xsl");

            Directory.CreateDirectory(Path.Combine(root, "sub"));
            try
            {
                File.WriteAllText(Path.GetFullPath(outside), Sheet(string.Empty));
                File.WriteAllText(Path.Combine(root, "sub", "ok.xsl"),
                    Sheet("<xsl:template match=\"/\"><from-sub/></xsl:template>"));

                FileResolver resolver = new FileResolver(root);

                // A path that climbs out of the root is refused, not silently clamped.
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(
                        Sheet("<xsl:import href=\"../secret.xsl\"/>"),
                        new XsltOptions { StylesheetResolver = resolver }));

                StringAssert.Contains(error.Message, "refused");

                // An absolute path elsewhere is refused for the same reason.
                StringAssert.Contains(
                    Assert.ThrowsExactly<XsltException>(
                        () => new Xslt(
                            Sheet($"<xsl:import href=\"{Path.GetFullPath(outside).Replace("\\", "/")}\"/>"),
                            new XsltOptions { StylesheetResolver = resolver })).Message,
                    "refused");

                // Something genuinely inside the root still works.
                Assert.AreEqual(
                    "<from-sub/>",
                    new Xslt(
                        Sheet("<xsl:include href=\"sub/ok.xsl\"/>"),
                        new XsltOptions { StylesheetResolver = resolver, OmitXmlDeclaration = true })
                        .TransformXml("<r/>"));
            }
            finally
            {
                File.Delete(Path.GetFullPath(outside));
                Directory.Delete(root, recursive: true);
            }
        }

        // ---- Include -------------------------------------------------------------------------------------

        [TestMethod]
        public void IncludeBringsInTemplatesAtTheSamePrecedence()
        {
            MapResolver resolver = new MapResolver()
                .Add("shared.xsl", Sheet("<xsl:template match=\"b\"><from-include/></xsl:template>"));

            Assert.AreEqual(
                "<out><from-main/><from-include/></out>",
                Run(Sheet("<xsl:include href=\"shared.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//a|//b\"/></out></xsl:template>"
                    + "<xsl:template match=\"a\"><from-main/></xsl:template>"),
                    "<r><a/><b/></r>",
                    resolver));
        }

        [TestMethod]
        public void IncludeIsTransitive()
        {
            MapResolver resolver = new MapResolver()
                .Add("one.xsl", Sheet("<xsl:include href=\"two.xsl\"/>"
                    + "<xsl:template match=\"a\"><one/></xsl:template>"))
                .Add("two.xsl", Sheet("<xsl:template match=\"b\"><two/></xsl:template>"));

            Assert.AreEqual(
                "<out><one/><two/></out>",
                Run(Sheet("<xsl:include href=\"one.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//a|//b\"/></out></xsl:template>"),
                    "<r><a/><b/></r>",
                    resolver));
        }

        [TestMethod]
        public void IncludeCarriesKeysGlobalsAndOutputSettings()
        {
            MapResolver resolver = new MapResolver()
                .Add("lib.xsl", Sheet("<xsl:output method=\"text\"/>"
                    + "<xsl:key name=\"byId\" match=\"item\" use=\"@id\"/>"
                    + "<xsl:variable name=\"prefix\" select=\"'#'\"/>"));

            Assert.AreEqual(
                "#B",
                Run(Sheet("<xsl:include href=\"lib.xsl\"/>"
                    + "<xsl:template match=\"/\">"
                    + "<xsl:value-of select=\"$prefix\"/><xsl:value-of select=\"key('byId','2')/@name\"/>"
                    + "</xsl:template>"),
                    "<r><item id=\"1\" name=\"A\"/><item id=\"2\" name=\"B\"/></r>",
                    resolver));
        }

        [TestMethod]
        public void AnIncludedModuleKeepsItsOwnNamespacePrefixes()
        {
            // A prefix means whatever it meant in the module the expression was written in, not in the one
            // that included it.
            MapResolver resolver = new MapResolver().Add(
                "ns.xsl",
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:p=\"urn:demo\" exclude-result-prefixes=\"p\">"
                + "<xsl:template match=\"p:item\"><found/></xsl:template>"
                + "</xsl:stylesheet>");

            // The including module binds the same prefix to something else entirely. If prefixes leaked
            // between modules, p:item would resolve to urn:unrelated and match nothing.
            Assert.AreEqual(
                "<out><found/></out>",
                Run("<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                    + "xmlns:p=\"urn:unrelated\" exclude-result-prefixes=\"p\">"
                    + "<xsl:include href=\"ns.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/*/*\"/></out></xsl:template>"
                    + "</xsl:stylesheet>",
                    "<r xmlns=\"urn:demo\"><item/></r>",
                    resolver));
        }

        // ---- Import --------------------------------------------------------------------------------------

        [TestMethod]
        public void ImportingStylesheetOverridesTheImportedOne()
        {
            MapResolver resolver = new MapResolver()
                .Add("base.xsl", Sheet("<xsl:template match=\"item\"><base/></xsl:template>"));

            Assert.AreEqual(
                "<out><override/></out>",
                Run(Sheet("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"
                    + "<xsl:template match=\"item\"><override/></xsl:template>"),
                    "<r><item/></r>",
                    resolver));
        }

        [TestMethod]
        public void ImportPrecedenceOutranksPriority()
        {
            // The imported pattern is far more specific, and still loses: precedence is decided first.
            MapResolver resolver = new MapResolver()
                .Add("base.xsl", Sheet("<xsl:template match=\"r/item[@k='y']\"><specific-imported/></xsl:template>"));

            Assert.AreEqual(
                "<out><generic-local/></out>",
                Run(Sheet("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"
                    + "<xsl:template match=\"item\"><generic-local/></xsl:template>"),
                    "<r><item k=\"y\"/></r>",
                    resolver));
        }

        [TestMethod]
        public void ImportedTemplatesStillApplyWhereNothingOverridesThem()
        {
            MapResolver resolver = new MapResolver()
                .Add("base.xsl", Sheet("<xsl:template match=\"a\"><base-a/></xsl:template>"
                    + "<xsl:template match=\"b\"><base-b/></xsl:template>"));

            Assert.AreEqual(
                "<out><mine/><base-b/></out>",
                Run(Sheet("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//a|//b\"/></out></xsl:template>"
                    + "<xsl:template match=\"a\"><mine/></xsl:template>"),
                    "<r><a/><b/></r>",
                    resolver));
        }

        [TestMethod]
        public void LaterImportsOutrankEarlierOnes()
        {
            MapResolver resolver = new MapResolver()
                .Add("first.xsl", Sheet("<xsl:template match=\"item\"><first/></xsl:template>"))
                .Add("second.xsl", Sheet("<xsl:template match=\"item\"><second/></xsl:template>"));

            Assert.AreEqual(
                "<out><second/></out>",
                Run(Sheet("<xsl:import href=\"first.xsl\"/><xsl:import href=\"second.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"),
                    "<r><item/></r>",
                    resolver));
        }

        [TestMethod]
        public void ANamedTemplateCanBeOverriddenAcrossAnImport()
        {
            MapResolver resolver = new MapResolver()
                .Add("base.xsl", Sheet("<xsl:template name=\"greet\"><base/></xsl:template>"));

            Assert.AreEqual(
                "<out><override/></out>",
                Run(Sheet("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:call-template name=\"greet\"/></out></xsl:template>"
                    + "<xsl:template name=\"greet\"><override/></xsl:template>"),
                    "<r/>",
                    resolver));
        }

        [TestMethod]
        public void TwoTemplatesWithTheSameNameInOneModuleAreStillAnError()
        {
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(() => new Xslt(Sheet(
                    "<xsl:template name=\"dup\"><a/></xsl:template>"
                    + "<xsl:template name=\"dup\"><b/></xsl:template>"))).Message,
                "dup");
        }

        [TestMethod]
        public void AGlobalVariableCanBeOverriddenAcrossAnImport()
        {
            MapResolver resolver = new MapResolver()
                .Add("base.xsl", Sheet("<xsl:variable name=\"label\" select=\"'imported'\"/>"));

            Assert.AreEqual(
                "<out>local</out>",
                Run(Sheet("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:variable name=\"label\" select=\"'local'\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"$label\"/></out></xsl:template>"),
                    "<r/>",
                    resolver));
        }

        [TestMethod]
        public void AModuleReferencedTwiceIsReadTwiceButDoesNotRecurse()
        {
            // A diamond is legal — it is only a cycle that must be refused.
            MapResolver resolver = new MapResolver()
                .Add("shared.xsl", Sheet("<xsl:template match=\"x\"><shared/></xsl:template>"))
                .Add("left.xsl", Sheet("<xsl:include href=\"shared.xsl\"/>"))
                .Add("right.xsl", Sheet("<xsl:import href=\"shared.xsl\"/>"));

            Assert.AreEqual(
                "<out><shared/></out>",
                Run(Sheet("<xsl:import href=\"right.xsl\"/><xsl:include href=\"left.xsl\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//x\"/></out></xsl:template>"),
                    "<r><x/></r>",
                    resolver));
        }

        // ---- What import precedence settles about a function ----------------------------------------------

        /// <summary>Compiles a stylesheet against a processor claiming 3.0, which is what these tests are about.</summary>
        private static Xslt Compile(string stylesheet)
        {
            return new Xslt(stylesheet, new XsltOptions { Version = XsltVersion.V30 });
        }

        /// <summary>A 3.0 stylesheet, which is what a static variable has to be written in.</summary>
        private static string Modern(string body)
        {
            return "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:f=\"urn:f\" exclude-result-prefixes=\"f\">" + body + "</xsl:stylesheet>";
        }

        [TestMethod]
        public void AnImportedFunctionMayBeRedefinedByTheModuleImportingIt()
        {
            // Same rule as a template, a variable and an attribute set: two of one name and arity are an
            // error only at the same import precedence, and an import is precisely a difference in it.
            MapResolver resolver = new MapResolver().Add(
                "base.xsl",
                Modern("<xsl:function name=\"f:g\"><xsl:param name=\"n\"/>"
                    + "<xsl:sequence select=\"'base'\"/></xsl:function>"));

            Assert.AreEqual(
                "<out>over</out>",
                Run(Modern("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:function name=\"f:g\"><xsl:param name=\"n\"/>"
                    + "<xsl:sequence select=\"'over'\"/></xsl:function>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"f:g(1)\"/></out></xsl:template>"),
                    "<r/>",
                    resolver));
        }

        [TestMethod]
        public void AFunctionRedefinedAtOneImportPrecedenceIsRefused()
        {
            // Nothing decides between them, which is the whole of why it is an error.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile(Modern(
                    "<xsl:function name=\"f:g\"><xsl:param name=\"n\"/>"
                    + "<xsl:sequence select=\"'a'\"/></xsl:function>"
                    + "<xsl:function name=\"f:g\"><xsl:param name=\"n\"/>"
                    + "<xsl:sequence select=\"'b'\"/></xsl:function>")));

            Assert.AreEqual("XTSE0770", error.Code);
        }

        [TestMethod]
        public void TwoFunctionsOfOneNameAndDifferentAritiesAreTwoFunctions()
        {
            Assert.AreEqual(
                "<out>1|2</out>",
                Run(Modern("<xsl:function name=\"f:g\"><xsl:param name=\"a\"/>"
                    + "<xsl:sequence select=\"'1'\"/></xsl:function>"
                    + "<xsl:function name=\"f:g\"><xsl:param name=\"a\"/><xsl:param name=\"b\"/>"
                    + "<xsl:sequence select=\"'2'\"/></xsl:function>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"f:g(1)\"/>|"
                    + "<xsl:value-of select=\"f:g(1, 2)\"/></out></xsl:template>"),
                    "<r/>",
                    null));
        }

        // ---- What a static variable settles, and when ------------------------------------------------------

        [TestMethod]
        public void AStaticVariableIsVisibleToAUseWhenBelowIt()
        {
            // The point of the whole feature: use-when is answered while the stylesheet is still being read,
            // so the only variable it can possibly see is one settled by then.
            Assert.AreEqual(
                "<out>kept</out>",
                Run(Modern("<xsl:variable name=\"RUN\" select=\"true()\" static=\"yes\"/>"
                    + "<xsl:template match=\"/\" use-when=\"$RUN\"><out>kept</out></xsl:template>"
                    + "<xsl:template match=\"/\" use-when=\"not($RUN)\"><out>dropped</out></xsl:template>"),
                    "<r/>",
                    null));
        }

        [TestMethod]
        public void WhatAUseWhenRemovesIsNeverCompiled()
        {
            // Which is what the attribute is for: the excluded branch may hold anything at all, including
            // instructions this engine has never heard of and expressions that would not parse.
            Assert.AreEqual(
                "<out>kept</out>",
                Run(Modern("<xsl:variable name=\"OLD\" select=\"false()\" static=\"yes\"/>"
                    + "<xsl:template match=\"/\" use-when=\"$OLD\">"
                    + "<xsl:nonesuch select=\"1 +\"/></xsl:template>"
                    + "<xsl:template match=\"/\" use-when=\"not($OLD)\"><out>kept</out></xsl:template>"),
                    "<r/>",
                    null));
        }

        [TestMethod]
        public void AStaticVariableIsAlsoAnOrdinaryVariable()
        {
            // It is not a use-when-only thing. What changes is when it is settled, not where it can be read.
            Assert.AreEqual(
                "<out>5</out>",
                Run(Modern("<xsl:variable name=\"N\" select=\"2 + 3\" static=\"yes\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"$N\"/></out></xsl:template>"),
                    "<r/>",
                    null));
        }

        [TestMethod]
        public void AStaticVariableMayNameTheStaticVariablesBeforeIt()
        {
            Assert.AreEqual(
                "<out>3</out>",
                Run(Modern("<xsl:variable name=\"A\" select=\"1\" static=\"yes\"/>"
                    + "<xsl:variable name=\"B\" select=\"$A + 2\" static=\"yes\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"$B\"/></out></xsl:template>"),
                    "<r/>",
                    null));
        }

        [TestMethod]
        public void AStaticVariableCannotNameOneDeclaredAfterIt()
        {
            // Reading order is the scoping rule, and there is no second pass in which a later declaration
            // could be waited for: the value is wanted before the next line of the stylesheet is read.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile(Modern(
                    "<xsl:variable name=\"A\" select=\"$B\" static=\"yes\"/>"
                    + "<xsl:variable name=\"B\" select=\"1\" static=\"yes\"/>")));

            Assert.AreEqual("XPST0008", error.Code);
        }

        [TestMethod]
        public void AStaticVariableCannotNameAnOrdinaryGlobal()
        {
            // An ordinary global is evaluated when the transformation runs, and a static variable has to be
            // worth something long before that — there is nothing yet for it to be evaluated against.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile(Modern(
                    "<xsl:variable name=\"O\" select=\"1\"/>"
                    + "<xsl:variable name=\"S\" select=\"$O\" static=\"yes\"/>")));

            Assert.AreEqual("XPST0008", error.Code);
        }

        [TestMethod]
        public void AStaticParameterIsSuppliedWhereTheStylesheetIsConstructed()
        {
            // Not where it is run. What a static parameter says decides which parts of the stylesheet exist,
            // and by the time a transformation starts there is nothing left to decide.
            Assert.AreEqual(
                "<out>b</out>",
                new Xslt(
                    Modern("<xsl:param name=\"WHICH\" select=\"'a'\" static=\"yes\"/>"
                        + "<xsl:template match=\"/\" use-when=\"$WHICH = 'a'\"><out>a</out></xsl:template>"
                        + "<xsl:template match=\"/\" use-when=\"$WHICH = 'b'\"><out>b</out></xsl:template>"),
                    new XsltOptions
                    {
                        OmitXmlDeclaration = true,
                        Version = XsltVersion.V30,
                        Parameters = new Dictionary<string, object?> { ["WHICH"] = "b" },
                    }).TransformXml("<r/>"));
        }

        [TestMethod]
        public void AStaticParameterFallsBackOnItsOwnDefault()
        {
            Assert.AreEqual(
                "<out>a</out>",
                Run(Modern("<xsl:param name=\"WHICH\" select=\"'a'\" static=\"yes\"/>"
                    + "<xsl:template match=\"/\" use-when=\"$WHICH = 'a'\"><out>a</out></xsl:template>"
                    + "<xsl:template match=\"/\" use-when=\"$WHICH = 'b'\"><out>b</out></xsl:template>"),
                    "<r/>",
                    null));
        }

        [TestMethod]
        public void ARequiredStaticParameterNobodySuppliedIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile(Modern(
                    "<xsl:param name=\"WHICH\" static=\"yes\" required=\"yes\"/>"
                    + "<xsl:template match=\"/\"><out/></xsl:template>")));

            Assert.AreEqual("XTDE0050", error.Code);
        }

        [TestMethod]
        public void AStaticVariableWithContentIsRefused()
        {
            // There is no result tree to build one from: the stylesheet around it has not been read yet.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile(Modern(
                    "<xsl:variable name=\"V\" static=\"yes\"><a/></xsl:variable>")));

            StringAssert.Contains(error.Message, "select");
        }

        [TestMethod]
        public void TwoStaticsOfOneNameAreRefusedLikeAnyOtherGlobal()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Compile(Modern(
                    "<xsl:variable name=\"V\" select=\"1\" static=\"yes\"/>"
                    + "<xsl:variable name=\"V\" select=\"2\" static=\"yes\"/>")));

            Assert.AreEqual("XTSE0630", error.Code);
        }

        [TestMethod]
        public void AStaticVariableHoldsASequenceAsReadilyAsAString()
        {
            Assert.AreEqual(
                "<out>3</out>",
                Run(Modern("<xsl:variable name=\"S\" select=\"(1, 2, 3)\" static=\"yes\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"count($S)\"/></out></xsl:template>"),
                    "<r/>",
                    null));
        }

        // ---- What import precedence does to modes and accumulators -------------------------------------

        private const string Xsl30 =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">";

        [TestMethod]
        public void TheDeclarationsOfOneModeMergeAttributeByAttribute()
        {
            // The import says what to do with an unmatched node and the importing module which accumulators
            // apply, and neither overrides the other: each attribute is settled on its own, by the highest
            // precedence that writes it. The suite's accumulator-023, where the text under r shows whether
            // the import's shallow-skip survived the importer's declaration.
            MapResolver resolver = new MapResolver().Add(
                "modes.xsl",
                Xsl30 + "<xsl:mode on-no-match=\"shallow-skip\"/></xsl:stylesheet>");

            Assert.AreEqual(
                "<out><v>1</v><v>2</v></out>",
                Run(
                    Xsl30
                    + "<xsl:import href=\"modes.xsl\"/>"
                    + "<xsl:mode use-accumulators=\"n\"/>"
                    + "<xsl:accumulator name=\"n\" initial-value=\"0\">"
                    + "<xsl:accumulator-rule match=\"i\" select=\"$value + 1\"/></xsl:accumulator>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates/></out></xsl:template>"
                    + "<xsl:template match=\"i\"><v><xsl:value-of select=\"accumulator-before('n')\"/></v>"
                    + "</xsl:template></xsl:stylesheet>",
                    "<r>text<i/><i/></r>",
                    resolver));
        }

        [TestMethod]
        public void TwoAccumulatorsOfOneNameAreRefusedOnlyWhereNothingAboveSettlesIt()
        {
            // A module declaring n twice is no clash so long as the module importing it declares n itself:
            // that is overriding both, which is what importing is for. Leave the declaration out and the two
            // are left standing at one precedence, which is XTSE3350. The suite's accumulator-027. (Two
            // imports would not do: each import is a precedence of its own, and the later one simply wins.)
            string Declaring(string step) =>
                "<xsl:accumulator name=\"n\" initial-value=\"0\">"
                + "<xsl:accumulator-rule match=\"i\" select=\"" + step + "\"/></xsl:accumulator>";

            MapResolver resolver = new MapResolver().Add(
                "twice.xsl",
                Xsl30 + Declaring("$value + 1") + Declaring("$value + 2") + "</xsl:stylesheet>");

            string Principal(string own) => Xsl30
                + "<xsl:import href=\"twice.xsl\"/>"
                + own
                + "<xsl:mode use-accumulators=\"n\"/>"
                + "<xsl:template match=\"/\"><out><xsl:value-of select=\"accumulator-after('n')\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>30</out>",
                Run(
                    Principal(Declaring("$value + 10")),
                    "<r><i/><i/><i/></r>",
                    resolver));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(Principal(string.Empty), "<r><i/></r>", resolver));

            Assert.AreEqual("XTSE3350", error.Code);
        }
    }
}
