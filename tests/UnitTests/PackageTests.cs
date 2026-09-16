namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what a package is to another: which version of it an <c>xsl:use-package</c> is given,
    /// and which of its declarations stay its own — decimal formats, keys, namespace aliases, character
    /// maps and output definitions, all of which XSLT 3.0 §3.5.3 makes local to the package that wrote them.
    /// </summary>
    [TestClass]
    public sealed class PackageTests
    {
        /// <summary>Serves library packages by name, and knows the versions it holds of each.</summary>
        private sealed class VersionedLibrary : IXsltPackageResolver
        {
            private readonly List<(string Name, string? Version, string Source)> m_packages = new();

            public VersionedLibrary Add(string name, string? version, string source)
            {
                m_packages.Add((name, version, source));
                return this;
            }

            public IReadOnlyList<string> VersionsOf(string name)
            {
                return m_packages.Where(p => p.Name == name && p.Version is not null).Select(p => p.Version!).ToList();
            }

            public ResolvedResource? Resolve(string name, string? version)
            {
                foreach ((string candidate, string? offered, string source) in m_packages)
                {
                    if (candidate == name && (version is null || offered == version))
                    {
                        return new ResolvedResource(new StringReader(source), name);
                    }
                }

                return null;
            }
        }

        /// <summary>Serves one package per name and knows nothing of versions.</summary>
        private sealed class PlainLibrary : IXsltResolver
        {
            private readonly Dictionary<string, string> m_packages = new(StringComparer.Ordinal);

            public PlainLibrary Add(string name, string source)
            {
                m_packages[name] = source;
                return this;
            }

            public ResolvedResource? Resolve(string name, string? baseUri)
            {
                return m_packages.TryGetValue(name, out string? text)
                    ? new ResolvedResource(new StringReader(text), name)
                    : null;
            }
        }

        private static string Package(string name, string body, string? version = "1.0.0")
        {
            string versioned = version is null ? string.Empty : $" package-version=\"{version}\"";

            return $"<xsl:package name=\"{name}\"{versioned} version=\"3.0\" "
                + "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:p=\"urn:p\" xmlns:q=\"urn:q\" "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"p q xs\">" + body + "</xsl:package>";
        }

        private static string Run(string principal, IXsltResolver library)
        {
            return new Xslt(
                principal,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    InitialTemplate = "main",
                    PackageResolver = library,
                }).Transform();
        }

        private static string Refuses(string principal, IXsltResolver library)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(principal, library)).Code ?? string.Empty;
        }

        /// <summary>A library declaring a public variable naming its own version.</summary>
        private static string Versioned(string version)
        {
            return Package("urn:lib", $"<xsl:variable name=\"v\" select=\"'{version}'\" visibility=\"public\"/>", version);
        }

        private static string Using(string range)
        {
            string versioned = range.Length == 0 ? string.Empty : $" package-version=\"{range}\"";

            return Package(
                "urn:main",
                $"<xsl:use-package name=\"urn:lib\"{versioned}/>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out><xsl:value-of select=\"$v\"/></out></xsl:template>");
        }

        [TestMethod]
        public void AUsePackageIsGivenTheHighestVersionItsRangeTakes()
        {
            VersionedLibrary library = new VersionedLibrary()
                .Add("urn:lib", "1.0.0", Versioned("1.0.0"))
                .Add("urn:lib", "2.0.0", Versioned("2.0.0"))
                .Add("urn:lib", "2.0.0-alpha", Versioned("2.0.0-alpha"))
                .Add("urn:lib", "3.5.4", Versioned("3.5.4"));

            Assert.AreEqual("<out>1.0.0</out>", Run(Using("1.0.0"), library));
            Assert.AreEqual("<out>2.0.0</out>", Run(Using("2.0"), library));
            Assert.AreEqual("<out>2.0.0-alpha</out>", Run(Using("2.0.0-alpha"), library));
            Assert.AreEqual("<out>1.0.0</out>", Run(Using("to 1.5"), library));
            Assert.AreEqual("<out>3.5.4</out>", Run(Using("3.5.*"), library));
            Assert.AreEqual("<out>3.5.4</out>", Run(Using("2.7.0-a to 5.0.0-gamma"), library));
            Assert.AreEqual("<out>3.5.4</out>", Run(Using("1+"), library));
            Assert.AreEqual("<out>3.5.4</out>", Run(Using("*"), library));
            Assert.AreEqual("<out>3.5.4</out>", Run(Using(string.Empty), library));
            Assert.AreEqual("<out>2.0.0</out>", Run(Using("1.0.0, 2.0.0"), library));

            // A range nothing on offer falls in, and one that is not a range at all.
            Assert.AreEqual("XTSE3000", Refuses(Using("4.*"), library));
            Assert.AreEqual("XTSE0020", Refuses(Using("2.0.0-alpha:beta"), library));
            Assert.AreEqual("XTSE0020", Refuses(Using("-3.6"), library));
        }

        [TestMethod]
        public void APackagesOwnVersionHasToBeOne()
        {
            // §3.5.1: integers separated by dots, then a name after a hyphen if wanted — an NCName by XML's
            // own name characters, in their fifth edition, so CJK punctuation is in and a private-use plane
            // is out. The principal package's version is read as a used one's is, and a shadow attribute
            // computing it stands in for the plain one, whatever that evaluation comes to.
            PlainLibrary library = new PlainLibrary();
            const string Body = "<xsl:template name=\"main\" visibility=\"public\"><out>ok</out></xsl:template>";

            static string Raw(string attributes) =>
                "<xsl:package name=\"urn:main\" " + attributes
                + " xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">" + Body + "</xsl:package>";

            Assert.AreEqual("<out>ok</out>", Run(Package("urn:main", Body, "3.10-alpha"), library));
            Assert.AreEqual("<out>ok</out>", Run(Package("urn:main", Body, "1-alpha-2"), library));
            Assert.AreEqual("XTSE0020", Refuses(Package("urn:main", Body, "1."), library));
            Assert.AreEqual("XTSE0020", Refuses(Package("urn:main", Body, "34..99"), library));
            Assert.AreEqual("XTSE0020", Refuses(Package("urn:main", Body, "-5"), library));
            Assert.AreEqual("XTSE0020", Refuses(Package("urn:main", Body, "34E9"), library));
            Assert.AreEqual("XTSE0020", Refuses(Package("urn:main", Body, ""), library));
            Assert.AreEqual("<out>ok</out>", Run(Package("urn:main", Body, "1.0-『』〇々"), library));
            Assert.AreEqual("XTSE0020", Refuses(Package("urn:main", Body, "1.0-a\U000F00DCb"), library));
            Assert.AreEqual("XTSE0020", Refuses(Package("urn:main", Body, "1.0-a b"), library));

            // The shadow attribute stands in for the plain one: an error in it is the error, an empty
            // result is no version, and a version computed from the processor's own is a version.
            Assert.AreEqual(
                "FOAR0001",
                Refuses(Raw("package-version=\"1.0-valid\" _package-version=\"{1 div 0}\" version=\"3.0\""), library));
            Assert.AreEqual(
                "XTSE0020",
                Refuses(Raw("package-version=\"1.0-valid\" _package-version=\"{''}\" version=\"3.0\""), library));
            Assert.AreEqual(
                "<out>ok</out>",
                Run(Raw("_package-version=\"{'3.0'}\" _version=\"{'3.0'}\""), library));
        }

        [TestMethod]
        public void APackageDeclaringNoVersionIsVersionOne()
        {
            // A resolver that knows no versions supplies what it has, and what the package declares is
            // checked against the range: nothing declared is version 1, the specification says.
            PlainLibrary library = new PlainLibrary().Add(
                "urn:lib",
                Package("urn:lib", "<xsl:variable name=\"v\" select=\"'none'\" visibility=\"public\"/>", null));

            Assert.AreEqual("<out>none</out>", Run(Using("1"), library));
            Assert.AreEqual("<out>none</out>", Run(Using("1.*"), library));
            Assert.AreEqual("<out>none</out>", Run(Using("*"), library));
            Assert.AreEqual("XTSE3000", Refuses(Using("2"), library));
        }

        [TestMethod]
        public void DecimalFormatsAndKeysAreLocalToTheirPackage()
        {
            // Each package's format-number reads its own decimal formats, and each package's key() its own
            // keys, however alike the names: the library groups with '~', the principal with '^'.
            string library = Package(
                "urn:lib",
                "<xsl:decimal-format grouping-separator=\"~\"/>"
                + "<xsl:function name=\"p:format\" as=\"xs:string\" visibility=\"public\"><xsl:param name=\"in\" as=\"xs:decimal\"/>"
                + "<xsl:value-of select=\"format-number($in, '0~000.0')\"/></xsl:function>"
                + "<xsl:key name=\"k\" match=\"row\" use=\"column[1]\"/>"
                + "<xsl:function name=\"p:find\" as=\"element(row)?\" visibility=\"public\"><xsl:param name=\"data\" as=\"document-node()\"/>"
                + "<xsl:param name=\"search\" as=\"xs:string\"/><xsl:sequence select=\"key('k', $search, $data)\"/></xsl:function>");
            string principal = Package(
                "urn:main",
                "<xsl:use-package name=\"urn:lib\"/>"
                + "<xsl:decimal-format grouping-separator=\"^\"/>"
                + "<xsl:key name=\"k\" match=\"row\" use=\"column[2]\"/>"
                + "<xsl:function name=\"q:find\" as=\"element(row)?\"><xsl:param name=\"data\" as=\"document-node()\"/>"
                + "<xsl:param name=\"search\" as=\"xs:string\"/><xsl:sequence select=\"key('k', $search, $data)\"/></xsl:function>"
                + "<xsl:variable name=\"data\"><row><column>aaa</column><column>bbb</column><column>one</column></row>"
                + "<row><column>bbb</column><column>aaa</column><column>two</column></row></xsl:variable>"
                + "<xsl:template name=\"main\" visibility=\"public\">"
                + "<out p=\"{p:format(1234.5)}\" q=\"{format-number(1234.5, '0^000.0')}\" pk=\"{p:find($data, 'aaa')/column[3]}\" qk=\"{q:find($data, 'aaa')/column[3]}\"/>"
                + "</xsl:template>");

            Assert.AreEqual(
                "<out p=\"1~234.5\" q=\"1^234.5\" pk=\"one\" qk=\"two\"/>",
                Run(principal, new PlainLibrary().Add("urn:lib", library)));

            // A format the library declared is not one the principal can name.
            string asking = Package(
                "urn:main",
                "<xsl:use-package name=\"urn:lib\"/>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out p=\"{format-number(1, '0.0', 'a')}\"/></xsl:template>");
            string declaring = Package("urn:lib", "<xsl:decimal-format name=\"a\" grouping-separator=\"!\"/>");

            Assert.AreEqual("FODF1280", Refuses(asking, new PlainLibrary().Add("urn:lib", declaring)));
        }

        [TestMethod]
        public void AliasesCharacterMapsAndOutputDefinitionsAreLocalToTheirPackage()
        {
            // The library aliases xs to p; the principal aliases xs to q; each element built comes out in
            // the namespace its own package's alias names.
            string library = Package(
                "urn:lib",
                "<xsl:namespace-alias stylesheet-prefix=\"xs\" result-prefix=\"p\"/>"
                + "<xsl:function name=\"p:alias\" as=\"element()\" visibility=\"public\"><xs:test/></xsl:function>"
                + "<xsl:character-map name=\"cm\"><xsl:output-character character=\"z\" string=\"ZZ\"/></xsl:character-map>"
                + "<xsl:output indent=\"yes\"/>");
            string principal = Package(
                "urn:main",
                "<xsl:use-package name=\"urn:lib\"/>"
                + "<xsl:namespace-alias stylesheet-prefix=\"xs\" result-prefix=\"q\"/>"
                + "<xsl:function name=\"q:alias\" as=\"element()\"><xs:test/></xsl:function>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out>"
                + "<a><xsl:value-of select=\"namespace-uri(q:alias())\"/></a><b><xsl:value-of select=\"namespace-uri(p:alias())\"/></b>"
                + "</out></xsl:template>");

            // And the library's indent="yes" does not reach the principal result, which is the top-level
            // package's to serialize. The principal's alias target, q, is declared on its literal result
            // element though excluded, as §11.1.4 has it; the library's p is nothing to the principal.
            Assert.AreEqual(
                "<out xmlns:q=\"urn:q\"><a>urn:q</a><b>urn:p</b></out>",
                Run(principal, new PlainLibrary().Add("urn:lib", library)));

            // A character map the library declared is not one the principal's xsl:output can name.
            string asking = Package(
                "urn:main",
                "<xsl:use-package name=\"urn:lib\"/><xsl:output use-character-maps=\"cm\"/>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out>z</out></xsl:template>");

            Assert.AreEqual("XTSE1590", Refuses(asking, new PlainLibrary().Add("urn:lib", library)));
        }
        [TestMethod]
        public void FunctionLookupSeesWhatThePackageItIsWrittenInSees()
        {
            // A package sees its own functions whatever visibility they carry, and of another package's
            // only what that package offers it. So a library's private function does not exist from
            // outside the library, and the using package's functions do not exist from inside it — the
            // call asks about the static context where it stands, and not about everything the
            // compilation happened to declare.
            string library = Package(
                "urn:lib",
                "<xsl:template name=\"ask\" visibility=\"public\"><asked"
                + " own=\"{exists(function-lookup(QName('urn:p','own'), 0))}\""
                + " theirs=\"{exists(function-lookup(QName('urn:p','theirs'), 0))}\""
                + " shared=\"{function-lookup(QName('urn:p','shared'), 0)()}\"/></xsl:template>"
                + "<xsl:function name=\"p:own\" visibility=\"private\">"
                + "<xsl:sequence select=\"1\"/></xsl:function>"
                + "<xsl:function name=\"p:shared\" visibility=\"public\">"
                + "<xsl:sequence select=\"'library'\"/></xsl:function>");

            string principal = Package(
                "urn:main",
                "<xsl:use-package name=\"urn:lib\"><xsl:override>"
                + "<xsl:function name=\"p:shared\" visibility=\"public\">"
                + "<xsl:sequence select=\"'override'\"/></xsl:function>"
                + "</xsl:override></xsl:use-package>"
                + "<xsl:function name=\"p:theirs\" visibility=\"public\">"
                + "<xsl:sequence select=\"2\"/></xsl:function>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out>"
                + "<xsl:call-template name=\"ask\"/>"
                + "<here own=\"{exists(function-lookup(QName('urn:p','own'), 0))}\""
                + " theirs=\"{exists(function-lookup(QName('urn:p','theirs'), 0))}\"/>"
                + "</out></xsl:template>");

            // Inside the library: its own private function is there, the using package's is not, and the
            // one the using package overrode is still the library's own — an override replaces the
            // component for whoever uses the package, not for the package itself.
            // Outside it: the private one is gone and the principal's own is there.
            Assert.AreEqual(
                "<out><asked own=\"true\" theirs=\"false\" shared=\"library\"/>"
                + "<here own=\"false\" theirs=\"true\"/></out>",
                Run(principal, new PlainLibrary().Add("urn:lib", library)));
        }
        [TestMethod]
        public void WhitespaceStrippingIsLocalToThePackageThatReads()
        {
            // XSLT 3.0 §3.6.5: an xsl:strip-space or xsl:preserve-space in a library package affects only
            // the doc() and document() calls written in that package. So one file read from two packages
            // that strip differently is two documents, and each sees its own.
            string library = Package(
                "urn:lib",
                "<xsl:strip-space elements=\"\"/>"
                + "<xsl:template name=\"ask\" visibility=\"public\"><kept>"
                + "<xsl:value-of select=\"count(document('urn:data')/*/text())\"/>"
                + "</kept></xsl:template>");

            string principal = Package(
                "urn:main",
                "<xsl:strip-space elements=\"*\"/>"
                + "<xsl:use-package name=\"urn:lib\"/>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out>"
                + "<xsl:call-template name=\"ask\"/>"
                + "<stripped><xsl:value-of select=\"count(document('urn:data')/*/text())\"/></stripped>"
                + "</out></xsl:template>");

            PlainLibrary files = new PlainLibrary()
                .Add("urn:lib", library)
                .Add("urn:data", "<r> <a/> </r>");

            Assert.AreEqual(
                "<out><kept>2</kept><stripped>0</stripped></out>",
                new Xslt(
                    principal,
                    new XsltOptions
                    {
                        Version = XsltVersion.V30,
                        OmitXmlDeclaration = true,
                        InitialTemplate = "main",
                        PackageResolver = files,
                        DocumentResolver = files,
                    }).Transform());
        }

        [TestMethod]
        public void TwoPackagesThatStripNothingReadOneDocument()
        {
            // The other half of it: a URI names one document, and two packages with nothing to say about
            // whitespace have nothing to disagree about — so they see the same nodes, and 'is' says so.
            string library = Package(
                "urn:lib",
                "<xsl:template name=\"ask\" visibility=\"public\"><xsl:param name=\"theirs\"/>"
                + "<same><xsl:value-of select=\"document('urn:data') is $theirs\"/></same>"
                + "</xsl:template>");

            string principal = Package(
                "urn:main",
                "<xsl:use-package name=\"urn:lib\"/>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out>"
                + "<xsl:call-template name=\"ask\">"
                + "<xsl:with-param name=\"theirs\" select=\"document('urn:data')\"/>"
                + "</xsl:call-template></out></xsl:template>");

            PlainLibrary files = new PlainLibrary()
                .Add("urn:lib", library)
                .Add("urn:data", "<r> <a/> </r>");

            Assert.AreEqual(
                "<out><same>true</same></out>",
                new Xslt(
                    principal,
                    new XsltOptions
                    {
                        Version = XsltVersion.V30,
                        OmitXmlDeclaration = true,
                        InitialTemplate = "main",
                        PackageResolver = files,
                        DocumentResolver = files,
                    }).Transform());
        }

        [TestMethod]
        public void OneComponentTakenThroughTwoNamingsIsInViewTwice()
        {
            // A package is loaded once however often it is named, but an xsl:use-package is a relationship
            // rather than a reference: each has its own xsl:accept children, and what the using package
            // ends up holding is what each relationship brought in. Two of them leaving one component in
            // view leave a reference to its name with two things it could mean, which is XTSE3050.
            PlainLibrary files = new PlainLibrary().Add(
                "urn:lib",
                Package(
                    "urn:lib",
                    "<xsl:variable name=\"v\" select=\"'one'\" visibility=\"public\"/>"
                    + "<xsl:variable name=\"w\" select=\"'two'\" visibility=\"public\"/>"));

            // The template writes a constant: what is being asked here is whether the package compiles at
            // all, and a reference to the component would bring in the separate question of which naming
            // a reference resolves through.
            string Twice(string first, string second) => Package(
                "urn:main",
                $"<xsl:use-package name=\"urn:lib\">{first}</xsl:use-package>"
                + $"<xsl:use-package name=\"urn:lib\">{second}</xsl:use-package>"
                + "<xsl:template name=\"main\" visibility=\"public\"><out/></xsl:template>");

            const string TakeV = "<xsl:accept component=\"variable\" names=\"v\" visibility=\"private\"/>";
            const string HideAll = "<xsl:accept component=\"variable\" names=\"*\" visibility=\"hidden\"/>";

            Assert.AreEqual("XTSE3050", Refuses(Twice(TakeV, TakeV), files));

            // Hidden in one of them and the name means one thing again. That is the shape the suite's
            // package-021 and package-022 are built on, and they pass.
            Assert.AreEqual("<out/>", Run(Twice(TakeV, HideAll), files));
            Assert.AreEqual("<out/>", Run(Twice(HideAll, TakeV), files));

            // Saying nothing is not the same as hiding: a public component is taken as private, which is
            // still a component the using package holds. So a naming with no xsl:accept at all beside one
            // that takes the same component is the conflict too.
            Assert.AreEqual("XTSE3050", Refuses(Twice(string.Empty, TakeV), files));

            // And the count is per name. Taking v through one naming and w through the other is two
            // components with two names, which is no conflict at all.
            Assert.AreEqual(
                "<out/>",
                Run(
                    Twice(
                        TakeV + "<xsl:accept component=\"variable\" names=\"w\" visibility=\"hidden\"/>",
                        "<xsl:accept component=\"variable\" names=\"w\" visibility=\"private\"/>"
                        + "<xsl:accept component=\"variable\" names=\"v\" visibility=\"hidden\"/>"),
                    files));
        }

        [TestMethod]
        public void AReferenceResolvesThroughTheNamingThatKeptTheComponent()
        {
            // The other half of the same rule. Which naming a reference means is settled per naming too:
            // one that hides the component says nothing about the one that took it, whichever was written
            // first. Read as one set of acceptances per used package, the later claim won and a reference
            // to a component the first naming had taken was out of scope.
            PlainLibrary files = new PlainLibrary().Add(
                "urn:lib",
                Package(
                    "urn:lib",
                    "<xsl:variable name=\"v\" select=\"'one'\" visibility=\"public\"/>"
                    + "<xsl:variable name=\"w\" select=\"'two'\" visibility=\"public\"/>"));

            string Twice(string first, string second) => Package(
                "urn:main",
                $"<xsl:use-package name=\"urn:lib\">{first}</xsl:use-package>"
                + $"<xsl:use-package name=\"urn:lib\">{second}</xsl:use-package>"
                + "<xsl:template name=\"main\" visibility=\"public\">"
                + "<out><xsl:value-of select=\"$v\"/></out></xsl:template>");

            const string TakeV = "<xsl:accept component=\"variable\" names=\"v\" visibility=\"private\"/>";
            const string HideAll = "<xsl:accept component=\"variable\" names=\"*\" visibility=\"hidden\"/>";

            Assert.AreEqual("<out>one</out>", Run(Twice(TakeV, HideAll), files));
            Assert.AreEqual("<out>one</out>", Run(Twice(HideAll, TakeV), files));

            // Hidden by every naming and it is out of scope, which is the answer the merged reading gave
            // for both orders above and is right only here.
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(() => Run(Twice(HideAll, HideAll), files)).Message,
                "$v");
        }
    }
}
