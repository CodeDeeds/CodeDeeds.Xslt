namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the EXSLT Common module: <c>exsl:node-set()</c> and <c>exsl:object-type()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a W3C specification, and implemented all the same. XSLT 1.0 stylesheets that build something and
    /// then have to look inside it call <c>exsl:node-set()</c>, and they do not call it unguarded: they ask
    /// <c>function-available()</c> first and write another branch for the answer no. Those branches are the
    /// reason this is here — see <c>ATitlePageIsCountedTheWayDocBookCountsIt</c>, which is the shape the
    /// DocBook stylesheets use and which emits an element too many when the answer is no.
    /// </para>
    /// <para>
    /// XSLT 2.0 dropped the result tree fragment, so on this engine the function is very nearly the
    /// identity. What it has to get right is the rest: the answer to the question about it, and what it does
    /// with an argument that is not nodes.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class ExsltCommonTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";
        private const string Exsl = "http://exslt.org/common";

        /// <summary>Collects what a stylesheet writes with <c>exsl:document</c>.</summary>
        private sealed class Results : IXsltResultResolver
        {
            private readonly Dictionary<string, StringWriter> m_writers = new(StringComparer.Ordinal);

            public Dictionary<string, string> Written => m_writers.ToDictionary(
                entry => entry.Key, entry => entry.Value.ToString(), StringComparer.Ordinal);

            public TextWriter Resolve(string href, string? baseUri)
            {
                if (!m_writers.TryGetValue(href, out StringWriter? writer))
                {
                    m_writers[href] = writer = new StringWriter();
                }

                return writer;
            }
        }

        private static string Transform(
            string body,
            string version,
            bool extensionPrefix,
            bool resolver,
            out Dictionary<string, string> written)
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" xmlns:exsl=\"{Exsl}\" "
                + (extensionPrefix ? "extension-element-prefixes=\"exsl\" " : string.Empty)
                + "exclude-result-prefixes=\"exsl\">"
                + body
                + "</xsl:stylesheet>";

            string Once(XsltBackend backend, out Dictionary<string, string> documents)
            {
                Results results = new Results();

                string produced = new Xslt(
                    stylesheet,
                    new XsltOptions
                    {
                        Backend = backend,
                        Version = XsltVersion.Implemented,
                        OmitXmlDeclaration = true,
                        ResultResolver = resolver ? results : null,
                    })
                    .TransformXml("<r><i>a</i><i>b</i></r>");

                documents = results.Written;
                return produced;
            }

            string interpreted = Once(XsltBackend.Interpreted, out written);
            Assert.AreEqual(
                interpreted,
                Once(XsltBackend.Compiled, out Dictionary<string, string> compiled),
                "the compiled backend disagreed with the interpreter");

            CollectionAssert.AreEquivalent(
                written.Select(entry => $"{entry.Key}={entry.Value}").ToList(),
                compiled.Select(entry => $"{entry.Key}={entry.Value}").ToList(),
                "the two backends wrote different secondary documents");

            return interpreted;
        }

        private static string Run(string body, string version = "1.0") =>
            Transform(body, version, false, false, out _);

        /// <summary>Runs with the <c>exsl</c> prefix declared an extension one, which is what an element needs.</summary>
        private static string RunExtension(string body, out Dictionary<string, string> written) =>
            Transform(body, "1.0", true, true, out written);

        private static string RunExtension(string body) => RunExtension(body, out _);

        /// <summary>Wraps an expression in a template that writes it out.</summary>
        private static string Value(string expression, string version = "1.0")
        {
            return Run(
                "<xsl:template match=\"/\"><out><xsl:value-of select=\"" + expression
                + "\"/></out></xsl:template>",
                version);
        }

        [TestMethod]
        public void TheModuleSaysItIsThere()
        {
            Assert.AreEqual("<out>true</out>", Value("function-available('exsl:node-set')"));
            Assert.AreEqual("<out>true</out>", Value("function-available('exsl:object-type')"));

            // The arity is answered too, and one argument is all either takes.
            Assert.AreEqual("<out>true</out>", Value("function-available('exsl:node-set', 1)", "3.0"));
            Assert.AreEqual("<out>false</out>", Value("function-available('exsl:node-set', 2)", "3.0"));

            // Nothing else in that namespace, and nothing at all in a namespace this engine has never heard
            // of: the answer has to be no, or the guard means nothing.
            Assert.AreEqual("<out>false</out>", Value("function-available('exsl:tokenize')"));
            Assert.AreEqual("<out>false</out>", Value("function-available('exsl:document')"));
        }

        [TestMethod]
        public void WhatWasBuiltCanBeLookedInside()
        {
            // The whole point of the function in XSLT 1.0: $v is what a sequence constructor made, and it is
            // navigated. Here it was always navigable, so the answer is the same either way.
            Assert.AreEqual(
                "<out>2</out>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"v\"><a/><b/></xsl:variable>"
                    + "<out><xsl:value-of select=\"count(exsl:node-set($v)/*)\"/></out></xsl:template>"));

            // And the nodes are the same nodes, not copies of them.
            Assert.AreEqual("<out>2</out>", Value("count(exsl:node-set(/r/i))"));
            Assert.AreEqual("<out>a</out>", Value("exsl:node-set(/r/i[1])"));
        }

        [TestMethod]
        public void AnythingThatIsNotNodesBecomesItsText()
        {
            // EXSLT: a node-set holding one text node whose value is the string of the argument.
            Assert.AreEqual("<out>7</out>", Value("exsl:node-set(3 + 4)"));
            Assert.AreEqual("<out>1</out>", Value("count(exsl:node-set('x'))"));
            Assert.AreEqual("<out>true</out>", Value("exsl:node-set(true())"));

            // A text node, so it has no name and no children.
            Assert.AreEqual("<out>0</out>", Value("count(exsl:node-set('x')/*)"));
            Assert.AreEqual("<out/>", Value("name(exsl:node-set('x'))"));
        }

        [TestMethod]
        public void ObjectTypeNamesWhatItWasGiven()
        {
            Assert.AreEqual("<out>node-set</out>", Value("exsl:object-type(/r/i)"));
            Assert.AreEqual("<out>string</out>", Value("exsl:object-type('x')"));
            Assert.AreEqual("<out>number</out>", Value("exsl:object-type(1)"));
            Assert.AreEqual("<out>boolean</out>", Value("exsl:object-type(true())"));

            // What XSLT 1.0 would call a result tree fragment is an ordinary document node here, and that is
            // what it answers. A processor with no such type has no other truthful answer to give.
            Assert.AreEqual(
                "<out>node-set</out>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"v\"><a/></xsl:variable>"
                    + "<out><xsl:value-of select=\"exsl:object-type($v)\"/></out></xsl:template>"));
        }

        [TestMethod]
        public void ATitlePageIsCountedTheWayDocBookCountsIt()
        {
            // The shape the DocBook 1.79.1 title page templates use, cut down to its bones: build a part of
            // the page, count what is in it, and write the wrapper only if there is something to wrap. Told
            // the function is unavailable the stylesheet assumes a count of one and writes the wrapper
            // around nothing, which is where the suite's docbook-002 got nine fo:block elements too many.
            Assert.AreEqual(
                "<out/>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"verso\"/>"
                    + "<xsl:variable name=\"count\">"
                    + "<xsl:choose>"
                    + "<xsl:when test=\"function-available('exsl:node-set')\">"
                    + "<xsl:value-of select=\"count(exsl:node-set($verso)/*)\"/></xsl:when>"
                    + "<xsl:otherwise>1</xsl:otherwise>"
                    + "</xsl:choose></xsl:variable>"
                    + "<out><xsl:if test=\"normalize-space($verso) != '' or $count &gt; 0\">"
                    + "<block/></xsl:if></out></xsl:template>"));

            // And the same page with something on it still gets its wrapper.
            Assert.AreEqual(
                "<out><block><a/></block></out>",
                Run(
                    "<xsl:template match=\"/\">"
                    + "<xsl:variable name=\"verso\"><a/></xsl:variable>"
                    + "<xsl:variable name=\"count\">"
                    + "<xsl:choose>"
                    + "<xsl:when test=\"function-available('exsl:node-set')\">"
                    + "<xsl:value-of select=\"count(exsl:node-set($verso)/*)\"/></xsl:when>"
                    + "<xsl:otherwise>1</xsl:otherwise>"
                    + "</xsl:choose></xsl:variable>"
                    + "<out><xsl:if test=\"normalize-space($verso) != '' or $count &gt; 0\">"
                    + "<block><xsl:copy-of select=\"exsl:node-set($verso)\"/></block>"
                    + "</xsl:if></out></xsl:template>"));
        }

        [TestMethod]
        public void TheModuleIsThereAtEveryVersion()
        {
            // A 1.0 stylesheet is where the calls are written, and a 2.0 or 3.0 one importing it inherits
            // them. Nothing about either function depends on the version, so neither does the answer.
            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Assert.AreEqual("<out>7</out>", Value("exsl:node-set(3 + 4)", version), version);
                Assert.AreEqual("<out>string</out>", Value("exsl:object-type('x')", version), version);
            }
        }

        [TestMethod]
        public void AnExtensionNamespaceThisEngineHasNothingInStillFails()
        {
            // Unchanged by the module being there: one namespace is implemented and the rest are not, and an
            // unguarded call into one of those is the error it always was.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\" xmlns:my=\"urn:mine\">"
                    + "<out><xsl:value-of select=\"my:go(1)\"/></out></xsl:template>"));

            Assert.AreEqual("XTDE1425", error.Code);

            // And the wrong number of arguments to one that is implemented is refused where it is written,
            // there being no processor anywhere on which that call would work.
            XsltException arity = Assert.ThrowsExactly<XsltException>(
                () => Value("exsl:node-set(1, 2)"));

            Assert.AreEqual("XPST0017", arity.Code);
        }

        [TestMethod]
        public void TheDocumentElementSaysItIsThere()
        {
            Assert.AreEqual("<out>true</out>", Value("element-available('exsl:document')"));

            // The question is about an element of that name and not a function of it, and there is no
            // function: the two questions are asked with different words and get different answers.
            Assert.AreEqual("<out>false</out>", Value("function-available('exsl:document')"));

            // The two other elements a 1.0 stylesheet asks about when it wants to write a second document.
            // Neither is implemented, so a stylesheet that would rather have Saxon's or Xalan's is told no
            // and keeps looking. The namespace is declared on the template, so it lands on the literal
            // result element too, which is why the answer is looked for rather than matched.
            StringAssert.Contains(
                Run("<xsl:template match=\"/\" xmlns:saxon=\"http://icl.com/saxon\"><out>"
                    + "<xsl:value-of select=\"element-available('saxon:output')\"/></out></xsl:template>"),
                ">false<");

            StringAssert.Contains(
                Run("<xsl:template match=\"/\" xmlns:redirect=\"http://xml.apache.org/xalan/redirect\">"
                    + "<out><xsl:value-of select=\"element-available('redirect:write')\"/></out>"
                    + "</xsl:template>"),
                ">false<");
        }

        [TestMethod]
        public void ADocumentGoesWhereItsHrefSays()
        {
            Assert.AreEqual(
                "<out/>",
                RunExtension(
                    "<xsl:template match=\"/\"><out/>"
                    + "<exsl:document href=\"side.xml\"><side>a</side></exsl:document></xsl:template>",
                    out Dictionary<string, string> written));

            Assert.HasCount(1, written);
            StringAssert.Contains(written["side.xml"], "<side>a</side>");
        }

        [TestMethod]
        public void AnHrefMayBeComputed()
        {
            // Every one of EXSLT's attributes is an attribute value template, the href included, which is
            // how a chunking stylesheet names a file per section.
            RunExtension(
                "<xsl:template match=\"/\"><out/><xsl:for-each select=\"/r/i\">"
                + "<exsl:document href=\"part{position()}.xml\"><p><xsl:value-of select=\".\"/></p>"
                + "</exsl:document></xsl:for-each></xsl:template>",
                out Dictionary<string, string> written);

            Assert.HasCount(2, written);
            StringAssert.Contains(written["part1.xml"], "<p>a</p>");
            StringAssert.Contains(written["part2.xml"], "<p>b</p>");
        }

        [TestMethod]
        public void TheSerializationAttributesAreTheOnesEXSLTNames()
        {
            // method, and the text method writes what its text is and none of the markup.
            RunExtension(
                "<xsl:template match=\"/\"><out/>"
                + "<exsl:document href=\"plain.txt\" method=\"text\"><side>a</side></exsl:document>"
                + "</xsl:template>",
                out Dictionary<string, string> text);

            Assert.AreEqual("a", text["plain.txt"]);

            // version is EXSLT's spelling of what xsl:result-document came to call output-version, the
            // later element having had version taken by the stylesheet's own. Written here as xsl:output
            // writes it, which is where EXSLT took the name from.
            RunExtension(
                "<xsl:template match=\"/\"><out/>"
                + "<exsl:document href=\"decl.xml\" version=\"1.0\" omit-xml-declaration=\"no\"><side/>"
                + "</exsl:document></xsl:template>",
                out Dictionary<string, string> declared);

            StringAssert.StartsWith(declared["decl.xml"], "<?xml version=\"1.0\"");
        }

        [TestMethod]
        public void AnHrefIsRequiredAndOnlyEXSLTsAttributesAreTaken()
        {
            XsltException missing = Assert.ThrowsExactly<XsltException>(
                () => RunExtension(
                    "<xsl:template match=\"/\"><exsl:document><side/></exsl:document></xsl:template>"));

            Assert.AreEqual("XTSE0010", missing.Code);

            // Refused rather than ignored: a misspelt omit-xml-declaration quietly dropped writes a
            // declaration nobody asked for and says nothing about why.
            XsltException unknown = Assert.ThrowsExactly<XsltException>(
                () => RunExtension(
                    "<xsl:template match=\"/\">"
                    + "<exsl:document href=\"side.xml\" omit-xml-decl=\"yes\"><side/></exsl:document>"
                    + "</xsl:template>"));

            Assert.AreEqual("XTSE0090", unknown.Code);

            // An attribute in somebody else's namespace is somebody else's business. The DocBook chunker
            // writes saxon:character-representation on the Saxon branch of the same choose.
            RunExtension(
                "<xsl:template match=\"/\" xmlns:saxon=\"http://icl.com/saxon\"><out/>"
                + "<exsl:document href=\"side.xml\" saxon:character-representation=\"native\"><side/>"
                + "</exsl:document></xsl:template>",
                out Dictionary<string, string> written);

            // The namespace is in scope on the literal result element too, so it is declared on it.
            StringAssert.Contains(written["side.xml"], "<side");
        }

        [TestMethod]
        public void NothingIsWrittenWithoutAResolver()
        {
            // The same posture as xsl:result-document: the instruction is implemented, and where a
            // particular run may write is the caller's to say. That is why element-available() answers yes
            // whether a resolver was configured or not.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Transform(
                    "<xsl:template match=\"/\"><out/>"
                    + "<exsl:document href=\"side.xml\"><side/></exsl:document></xsl:template>",
                    "1.0",
                    true,
                    false,
                    out _));

            StringAssert.Contains(error.Message, "no result resolver was configured");
        }

        [TestMethod]
        public void WithoutTheExtensionPrefixItIsPartOfTheResult()
        {
            // Nothing about the element itself makes it an instruction: extension-element-prefixes is the
            // whole of what tells the two apart. Without it this is a literal result element and is copied
            // out, which is what keeps a stylesheet that never asked for any of this unchanged.
            string produced = Run(
                "<xsl:template match=\"/\"><out><exsl:document href=\"side.xml\"/></out></xsl:template>");

            StringAssert.Contains(produced, "<exsl:document");
            StringAssert.Contains(produced, "href=\"side.xml\"");
        }
    }
}
