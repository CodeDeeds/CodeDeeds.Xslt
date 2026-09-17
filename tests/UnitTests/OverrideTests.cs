namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what an <c>xsl:override</c> may replace and how: the signature it has to keep
    /// (<c>XTSE3070</c>), what <c>xsl:original</c> means for a function, a variable and an attribute set,
    /// an override reaching the used package's own references, and the rules around a package's entry
    /// points and its abstract components.
    /// </summary>
    [TestClass]
    public sealed class OverrideTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Serves packages and modules alike by name, knowing nothing of versions.</summary>
        private sealed class Library : IXsltResolver
        {
            private readonly Dictionary<string, string> m_texts = new(StringComparer.Ordinal);

            public Library Add(string name, string text)
            {
                m_texts[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string name, string? baseUri)
            {
                return m_texts.TryGetValue(name, out string? text)
                    ? new ResolvedResource(new StringReader(text), name)
                    : null;
            }
        }

        private static string Package(string name, string body, string attributes = "")
        {
            return $"<xsl:package name=\"{name}\" package-version=\"1.0\" version=\"3.0\" {attributes} "
                + $"xmlns:xsl=\"{Xsl}\" xmlns:p=\"urn:p\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" "
                + "exclude-result-prefixes=\"p xs\">" + body + "</xsl:package>";
        }

        private static string Using(string body, string overrides = "", string attributes = "")
        {
            return Package(
                "urn:main",
                "<xsl:use-package name=\"urn:lib\">" + overrides + "</xsl:use-package>" + body,
                attributes);
        }

        private static string Main(string body) =>
            "<xsl:template name=\"main\" visibility=\"public\">" + body + "</xsl:template>";

        private static Xslt Compile(string principal, Library library, string? initialTemplate = "main", string? initialMode = null)
        {
            return new Xslt(
                principal,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    InitialTemplate = initialTemplate,
                    InitialMode = initialMode,
                    PackageResolver = library,
                    StylesheetResolver = library,
                });
        }

        private static string Run(string principal, Library library) => Compile(principal, library).Transform();

        private static string Refuses(string principal, Library library, string? initialTemplate = "main")
        {
            return Assert.ThrowsExactly<XsltException>(() => Compile(principal, library, initialTemplate).Transform()).Code
                ?? string.Empty;
        }

        // ---- signatures --------------------------------------------------------------------------------

        private static readonly Library s_signatures = new Library().Add(
            "urn:lib",
            Package(
                "urn:lib",
                "<xsl:function name=\"p:f\" as=\"xs:string\" visibility=\"public\">"
                + "<xsl:param name=\"in\" as=\"xs:string\"/><xsl:param name=\"count\" as=\"xs:integer\"/>"
                + "<xsl:sequence select=\"string-join((1 to $count) ! $in)\"/></xsl:function>"
                + "<xsl:template name=\"t\" as=\"xs:string\" visibility=\"public\">"
                + "<xsl:context-item use=\"absent\"/>"
                + "<xsl:param name=\"in\" as=\"xs:string\"/><xsl:param name=\"extra\" as=\"xs:integer\" tunnel=\"yes\"/>"
                + "<xsl:sequence select=\"$in\"/></xsl:template>"));

        [TestMethod]
        public void AnOverrideKeepsTheSignatureOfWhatItOverrides()
        {
            static string Function(string parameters, string attributes = "") =>
                $"<xsl:override><xsl:function name=\"p:f\" as=\"xs:string\" visibility=\"public\" {attributes}>{parameters}"
                + "<xsl:sequence select=\"'*' || xsl:original($in, $count) || '*'\"/></xsl:function></xsl:override>";

            string same = "<xsl:param name=\"in\" as=\"xs:string\"/><xsl:param name=\"count\" as=\"xs:integer\"/>";

            // The same types, and the original reached as xsl:original.
            Assert.AreEqual("<out>*xxx*</out>", Run(Using(Main("<out><xsl:value-of select=\"p:f('x', 3)\"/></out>"), Function(same)), s_signatures));

            // A parameter of another type, a result of another type, and another say on new-each-time.
            Assert.AreEqual("XTSE3070", Refuses(Using(Main("<out/>"), Function("<xsl:param name=\"in\" as=\"xs:string\"/><xsl:param name=\"count\" as=\"xs:decimal\"/>")), s_signatures));
            Assert.AreEqual("XTSE3070", Refuses(Using(Main("<out/>"), Function(same).Replace("as=\"xs:string\" visibility", "as=\"xs:string?\" visibility")), s_signatures));
            Assert.AreEqual("XTSE3070", Refuses(Using(Main("<out/>"), Function(same, "new-each-time=\"no\"")), s_signatures));
        }

        [TestMethod]
        public void ATemplateOverrideKeepsItsParametersAndItsContextItem()
        {
            static string Template(string inside) =>
                "<xsl:override><xsl:template name=\"t\" as=\"xs:string\" visibility=\"public\">" + inside
                + "<xsl:sequence select=\"'*' || $in || '*'\"/></xsl:template></xsl:override>";

            string call = Main("<out><xsl:call-template name=\"t\"><xsl:with-param name=\"in\" select=\"'x'\"/></xsl:call-template></out>");
            string absent = "<xsl:context-item use=\"absent\"/>";
            string inString = "<xsl:param name=\"in\" as=\"xs:string\"/>";

            // The original's tunnel parameter need not be declared again; an added parameter may not be required.
            Assert.AreEqual("<out>*x*</out>", Run(Using(call, Template(absent + inString)), s_signatures));
            Assert.AreEqual("<out>*x*</out>", Run(Using(call, Template(absent + inString + "<xsl:param name=\"more\" select=\"1\"/>")), s_signatures));

            // Another type for a parameter, a tunnel parameter made an ordinary one, one made required, a
            // parameter dropped, and another say on the context item.
            Assert.AreEqual("XTSE3070", Refuses(Using(call, Template(absent + "<xsl:param name=\"in\" as=\"xs:string?\"/>")), s_signatures));
            Assert.AreEqual("XTSE3070", Refuses(Using(call, Template(absent + inString + "<xsl:param name=\"extra\" as=\"xs:integer\"/>")), s_signatures));
            Assert.AreEqual("XTSE3070", Refuses(Using(call, Template(absent + inString + "<xsl:param name=\"more\" required=\"yes\"/>")), s_signatures));
            Assert.AreEqual("XTSE3070", Refuses(Using(call, Template(absent)), s_signatures));
            Assert.AreEqual("XTSE3070", Refuses(Using(call, Template(inString)), s_signatures));
        }

        // ---- xsl:original ------------------------------------------------------------------------------

        [TestMethod]
        public void EachOverridingFunctionHasItsOwnOriginal()
        {
            // Two overrides of the same arity in one xsl:override: each one's xsl:original is the function
            // it replaced, not the last one declared — and a named reference to it or a partial
            // application of it means the same as a call.
            Library library = new Library().Add(
                "urn:lib",
                Package(
                    "urn:lib",
                    "<xsl:function name=\"p:f\" as=\"xs:string\" visibility=\"public\"><xsl:param name=\"in\" as=\"xs:string\"/>"
                    + "<xsl:sequence select=\"$in || $in\"/></xsl:function>"
                    + "<xsl:function name=\"p:g\" as=\"xs:string\" visibility=\"public\"><xsl:param name=\"in\" as=\"xs:string\"/>"
                    + "<xsl:sequence select=\"upper-case($in)\"/></xsl:function>"));

            string overrides =
                "<xsl:override>"
                + "<xsl:function name=\"p:f\" as=\"xs:string\" visibility=\"public\"><xsl:param name=\"in\" as=\"xs:string\"/>"
                + "<xsl:sequence select=\"'[' || xsl:original($in) || ']'\"/></xsl:function>"
                + "<xsl:function name=\"p:g\" as=\"xs:string\" visibility=\"public\"><xsl:param name=\"in\" as=\"xs:string\"/>"
                + "<xsl:variable name=\"named\" select=\"xsl:original#1\"/><xsl:variable name=\"partial\" select=\"xsl:original(?)\"/>"
                + "<xsl:sequence select=\"$named($in) || $partial($in)\"/></xsl:function>"
                + "</xsl:override>";

            Assert.AreEqual(
                "<out>[aa] AA</out>",
                Run(Using(Main("<out><xsl:value-of select=\"p:f('a'), p:g('a')\"/></out>"), overrides), library));
        }

        [TestMethod]
        public void AnOverridingVariableReadsItsOriginalAndIsReadByTheUsedPackage()
        {
            // $xsl:original in the override's own value is the variable overridden; and a match pattern the
            // library wrote against $v reads the override, since the override replaced the component for
            // everyone.
            Library library = new Library().Add(
                "urn:lib",
                Package(
                    "urn:lib",
                    "<xsl:mode/>"
                    + "<xsl:variable name=\"v\" as=\"xs:integer\" visibility=\"public\" select=\"1\"/>"
                    + "<xsl:template name=\"go\" visibility=\"public\"><xsl:param name=\"node\" as=\"node()\"/>"
                    + "<go><xsl:apply-templates select=\"$node\"/></go></xsl:template>"
                    + "<xsl:template match=\"*[$v = 1]\"><one/></xsl:template>"
                    + "<xsl:template match=\"*[$v != 1]\"><not-one v=\"{$v}\"/></xsl:template>"));

            string principal = Using(
                Main(
                    "<xsl:variable name=\"doc\"><a/></xsl:variable>"
                    + "<xsl:call-template name=\"go\"><xsl:with-param name=\"node\" select=\"$doc/a\"/></xsl:call-template>"),
                "<xsl:override><xsl:variable name=\"v\" as=\"xs:integer\" visibility=\"public\" select=\"$xsl:original + 12\"/></xsl:override>");

            Assert.AreEqual("<go><not-one v=\"13\"/></go>", Run(principal, library));

            // Another type is another signature.
            Assert.AreEqual(
                "XTSE3070",
                Refuses(
                    Using(Main("<out/>"), "<xsl:override><xsl:variable name=\"v\" as=\"xs:decimal\" visibility=\"public\" select=\"2\"/></xsl:override>"),
                    library));
        }

        [TestMethod]
        public void AnOverridingAttributeSetReplacesTheOriginalAndMayUseIt()
        {
            Library library = new Library().Add(
                "urn:lib",
                Package(
                    "urn:lib",
                    "<xsl:attribute-set name=\"pub\" visibility=\"public\" use-attribute-sets=\"priv\">"
                    + "<xsl:attribute name=\"pub1\" select=\"'pub1'\"/><xsl:attribute name=\"pub2\" select=\"'pub2'\"/></xsl:attribute-set>"
                    + "<xsl:attribute-set name=\"priv\"><xsl:attribute name=\"priv1\" select=\"'priv1'\"/></xsl:attribute-set>"
                    + "<xsl:function name=\"p:make\" as=\"element()\" visibility=\"public\"><lib xsl:use-attribute-sets=\"pub\"/></xsl:function>"));

            static string Override(string uses) =>
                $"<xsl:override><xsl:attribute-set name=\"pub\" visibility=\"public\" {uses}>"
                + "<xsl:attribute name=\"pub1\" select=\"'over1'\"/><xsl:attribute name=\"pub3\" select=\"'over3'\"/>"
                + "</xsl:attribute-set></xsl:override>";

            string body = Main(
                "<out><x xsl:use-attribute-sets=\"pub\"/><y xsl:use-attribute-sets=\"priv\"/><xsl:copy-of select=\"p:make()\"/></out>");

            // The override replaces the whole of the original, for the library's own element too; the
            // principal's private 'priv' is its own, not the library's.
            string principal = Using(
                body + "<xsl:attribute-set name=\"priv\"><xsl:attribute name=\"mine\" select=\"'mine'\"/></xsl:attribute-set>",
                Override(string.Empty));

            Assert.AreEqual(
                "<out><x pub1=\"over1\" pub3=\"over3\"/><y mine=\"mine\"/><lib pub1=\"over1\" pub3=\"over3\"/></out>",
                Run(principal, library));

            // With xsl:original, the original comes first and the override's attributes win over it — an
            // attribute written again moves to where it was written again.
            principal = Using(
                body + "<xsl:attribute-set name=\"priv\"><xsl:attribute name=\"mine\" select=\"'mine'\"/></xsl:attribute-set>",
                Override("use-attribute-sets=\"xsl:original\""));

            Assert.AreEqual(
                "<out><x priv1=\"priv1\" pub2=\"pub2\" pub1=\"over1\" pub3=\"over3\"/><y mine=\"mine\"/>"
                + "<lib priv1=\"priv1\" pub2=\"pub2\" pub1=\"over1\" pub3=\"over3\"/></out>",
                Run(principal, library));

            // Outside an override, xsl:original names nothing.
            Assert.AreEqual(
                "XTSE0710",
                Refuses(Using(Main("<out xsl:use-attribute-sets=\"xsl:original\"/>")), library));
        }

        [TestMethod]
        public void AccumulatorsOfOneNameInTwoPackagesAreTwoAccumulators()
        {
            Library library = new Library().Add(
                "urn:lib",
                Package(
                    "urn:lib",
                    "<xsl:accumulator name=\"ac\" initial-value=\"0\"><xsl:accumulator-rule match=\"*\" select=\"$value + 1\"/></xsl:accumulator>"
                    + "<xsl:template name=\"count\" visibility=\"public\"><lib><xsl:value-of select=\"accumulator-after('ac')\"/></lib></xsl:template>"));

            string principal = Using(
                "<xsl:accumulator name=\"ac\" initial-value=\"0\"><xsl:accumulator-rule match=\"*\" select=\"$value - 1\"/></xsl:accumulator>"
                + "<xsl:variable name=\"data\"><e><x/><g/></e></xsl:variable>"
                + Main(
                    "<xsl:for-each select=\"$data\"><out><mine><xsl:value-of select=\"accumulator-after('ac')\"/></mine>"
                    + "<xsl:call-template name=\"count\"/></out></xsl:for-each>"));

            Assert.AreEqual("<out><mine>-3</mine><lib>3</lib></out>", Run(principal, library));
        }

        // ---- where a package may be used from, and what a package may hold -------------------------------

        [TestMethod]
        public void APackageIsUsedFromThePrincipalModuleAndNotImported()
        {
            Library library = new Library()
                .Add("urn:lib", Package("urn:lib", "<xsl:template name=\"t\" visibility=\"public\"><t/></xsl:template>"))
                .Add("uses.xsl", $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\"><xsl:use-package name=\"urn:lib\"/></xsl:stylesheet>");

            // Included, the module is at the principal's level and may use a package; imported, it is not.
            Assert.AreEqual(
                "<t/>",
                Run(Package("urn:main", "<xsl:include href=\"uses.xsl\"/>" + Main("<xsl:call-template name=\"t\"/>")), library));
            Assert.AreEqual(
                "XTSE3008",
                Refuses(Package("urn:main", "<xsl:import href=\"uses.xsl\"/>" + Main("<xsl:call-template name=\"t\"/>")), library));

            // And a package is not a module: it cannot be imported at all.
            Assert.AreEqual("XTSE0165", Refuses(Package("urn:main", "<xsl:import href=\"urn:lib\"/>" + Main("<out/>")), library));

            // Text inside xsl:override is not a declaration.
            Assert.AreEqual(
                "XTSE0010",
                Refuses(Using(Main("<out/>"), "<xsl:override>Gotcha!</xsl:override>"), library));
        }

        [TestMethod]
        public void ATopLevelPackageHoldsNothingAbstract()
        {
            Library library = new Library().Add(
                "urn:lib",
                Package(
                    "urn:lib",
                    "<xsl:template name=\"t\" visibility=\"abstract\"/>"
                    + "<xsl:template name=\"other\" visibility=\"public\"><other/></xsl:template>"));

            // Accepted as abstract into the package meant to be run, or declared there: an error whether or
            // not anything reaches it. Overridden, it is supplied; hidden, it is not there.
            Assert.AreEqual(
                "XTSE3080",
                Refuses(Using(Main("<out/>"), "<xsl:accept component=\"template\" names=\"t\" visibility=\"abstract\"/>"), library));
            Assert.AreEqual(
                "XTSE3080",
                Refuses(Package("urn:main", "<xsl:template name=\"never\" visibility=\"abstract\"/>" + Main("<out/>")), library));
            Assert.AreEqual(
                "<t/>",
                Run(Using(Main("<xsl:call-template name=\"t\"/>"), "<xsl:override><xsl:template name=\"t\" visibility=\"public\"><t/></xsl:template></xsl:override>"), library));
            Assert.AreEqual(
                "<other/>",
                Run(Using(Main("<xsl:call-template name=\"other\"/>"), "<xsl:accept component=\"template\" names=\"t\" visibility=\"hidden\"/>"), library));
        }

        [TestMethod]
        public void ALibrarysGlobalsHaveNoContextItem()
        {
            Library library = new Library().Add(
                "urn:lib",
                Package("urn:lib", "<xsl:variable name=\"count\" visibility=\"public\" select=\"count(//*)\"/>"));

            string principal = Using("<xsl:mode/><xsl:template match=\"/\"><out><xsl:value-of select=\"$count\"/></out></xsl:template>");
            Xslt xslt = Compile(principal, library, initialTemplate: null);

            Assert.AreEqual("XPDY0002", Assert.ThrowsExactly<XsltException>(() => xslt.TransformXml("<a><b/></a>")).Code);
        }

        [TestMethod]
        public void ARuleInAUsedPackagesModeBelongsInAnOverride()
        {
            Library library = new Library().Add(
                "urn:lib",
                Package(
                    "urn:lib",
                    "<xsl:mode name=\"m\" visibility=\"public\"/>"
                    + "<xsl:template match=\"a\" mode=\"m\"><lib/></xsl:template>"
                    + "<xsl:template name=\"go\" visibility=\"public\"><xsl:param name=\"node\"/><xsl:apply-templates select=\"$node\" mode=\"m\"/></xsl:template>"));

            string call = Main(
                "<xsl:variable name=\"doc\"><a/></xsl:variable>"
                + "<out><xsl:call-template name=\"go\"><xsl:with-param name=\"node\" select=\"$doc/a\"/></xsl:call-template></out>");
            string rule = "<xsl:template match=\"a\" mode=\"m\"><mine/></xsl:template>";

            // Written outside xsl:override, the rule declares a mode of the same name here — two components
            // of one name in view. Inside, it is added to the library's mode.
            Assert.AreEqual("XTSE3050", Refuses(Using(call + rule, attributes: "declared-modes=\"no\""), library));
            Assert.AreEqual("<out><mine/></out>", Run(Using(call, "<xsl:override>" + rule + "</xsl:override>"), library));
        }

        [TestMethod]
        public void APackageStartsOnlyWhereItIsPublic()
        {
            Library library = new Library();

            // xsl:initial-template is an entry point only when the package makes it public; and a package
            // with nothing to start at cannot start.
            Assert.AreEqual(
                "XTDE0040",
                Refuses(Package("urn:main", "<xsl:template name=\"xsl:initial-template\"><out/></xsl:template>"), library, initialTemplate: null));
            Assert.AreEqual(
                "<out/>",
                Compile(Package("urn:main", "<xsl:template name=\"xsl:initial-template\" visibility=\"public\"><out/></xsl:template>"), library, initialTemplate: null).Transform());
            Assert.AreEqual("XTDE0040", Refuses(Package("urn:main", "<xsl:mode/>"), library, initialTemplate: null));

            // The default-mode a package names is a mode it has to declare.
            Assert.AreEqual(
                "XTSE3085",
                Refuses(Package("urn:main", "<xsl:mode/>", "default-mode=\"nowhere\""), library, initialTemplate: null));
        }

        [TestMethod]
        public void TheDefaultInitialModeIsThePackagesDefaultMode()
        {
            // #default asks for the package's default mode, which default-mode may name; #unnamed asks for
            // the unnamed mode whatever the package said.
            string package = Package(
                "urn:main",
                "<xsl:mode name=\"skip\" on-no-match=\"shallow-skip\" visibility=\"public\"/><xsl:mode on-no-match=\"text-only-copy\"/>",
                "default-mode=\"skip\"");

            Assert.AreEqual(string.Empty, Compile(package, new Library(), initialTemplate: null, initialMode: "#default").TransformXml("<a>text</a>"));
            Assert.AreEqual("text", Compile(package, new Library(), initialTemplate: null, initialMode: "#unnamed").TransformXml("<a>text</a>"));
        }

        [TestMethod]
        public void ACircularityAnOverrideCreatesIsTheDynamicError()
        {
            // The library has no cycle: a uses b, and b uses nothing. The override gives b a use of a,
            // and now a reaches itself — but neither package says so on its own, which is why XSLT 3.0
            // dropped the static code for this and left the general circularity. A cycle written inside
            // one package is still the static XTSE0720; see ModuleErrorCodeTests.
            Library library = new Library().Add(
                "urn:lib",
                Package(
                    "urn:lib",
                    "<xsl:attribute-set name=\"a\" visibility=\"public\" use-attribute-sets=\"b\">"
                    + "<xsl:attribute name=\"x\" select=\"'1'\"/></xsl:attribute-set>"
                    + "<xsl:attribute-set name=\"b\" visibility=\"public\">"
                    + "<xsl:attribute name=\"y\" select=\"'2'\"/></xsl:attribute-set>"));

            Assert.AreEqual(
                "XTDE0640",
                Refuses(
                    Using(
                        Main("<out xsl:use-attribute-sets=\"a\"/>"),
                        "<xsl:override>"
                        + "<xsl:attribute-set name=\"b\" visibility=\"public\" use-attribute-sets=\"a\">"
                        + "<xsl:attribute name=\"y\" select=\"'2o'\"/></xsl:attribute-set>"
                        + "</xsl:override>"),
                    library));
        }
        // ---- when two types written differently are one type -------------------------------------------

        /// <summary>A package declaring a union type of its own and a public variable of that type.</summary>
        private static string Union(string name, string type, string members, string body = "")
        {
            return $"<xsl:package name=\"{name}\" package-version=\"1.0\" version=\"3.0\" xmlns:xsl=\"{Xsl}\" "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + "<xsl:import-schema><xs:schema>"
                + $"<xs:simpleType name=\"{type}\"><xs:union memberTypes=\"{members}\"/></xs:simpleType>"
                + "</xs:schema></xsl:import-schema>"
                + body
                + "</xsl:package>";
        }

        /// <summary>Compiles a package that uses another, with the schemas each imports in scope.</summary>
        private static string RefusesTyped(string principal, Library library)
        {
            return Assert.ThrowsExactly<XsltException>(
                () => new Xslt(
                    principal,
                    new XsltOptions
                    {
                        Version = XsltVersion.V30,
                        OmitXmlDeclaration = true,
                        SchemaAware = true,
                        InitialTemplate = "main",
                        PackageResolver = library,
                        StylesheetResolver = library,
                    }).Transform()).Code ?? string.Empty;
        }

        private static string RunTyped(string principal, Library library)
        {
            return new Xslt(
                principal,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    SchemaAware = true,
                    InitialTemplate = "main",
                    PackageResolver = library,
                    StylesheetResolver = library,
                }).Transform();
        }

        [TestMethod]
        public void TwoUnionsOfTheSameMemberTypesAreTheSameType()
        {
            // §3.5.3.3: "Types S and T are considered identical for the purpose of these rules if and only if
            // subtype(S, T) and subtype(T, S) both hold", with a note drawing out what that means here: "two
            // plain union types are considered identical if they have the same set of member types, even if
            // the union types have different names or the ordering of the member types is different." An
            // override may declare its own union of the same members and still present the same interface.
            Library library = new Library().Add(
                "urn:lib",
                Union(
                    "urn:lib",
                    "u1",
                    "xs:date xs:time xs:dateTime",
                    "<xsl:variable name=\"v\" as=\"u1\" select=\"current-dateTime()\" visibility=\"public\"/>"));

            string overriding =
                "<xsl:use-package name=\"urn:lib\"><xsl:override>"
                + "<xsl:variable name=\"v\" as=\"u2\" select=\"current-date()\" visibility=\"public\"/>"
                + "</xsl:override></xsl:use-package>"
                + "<xsl:template name=\"main\" visibility=\"public\">"
                + "<out><xsl:value-of select=\"$v instance of xs:date\"/></out></xsl:template>";

            Assert.AreEqual(
                "<out>true</out>",
                RunTyped(Union("urn:main", "u2", "xs:time xs:dateTime xs:date", overriding), library));

            // A union of other members is another type, and the override no longer presents what the
            // original did.
            Assert.AreEqual(
                "XTSE3070",
                RefusesTyped(Union("urn:main", "u2", "xs:dateTime xs:date", overriding), library));
        }
    }
}
