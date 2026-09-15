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

        private static string Run(string body, string version = "1.0")
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" xmlns:exsl=\"{Exsl}\" "
                + "exclude-result-prefixes=\"exsl\">"
                + body
                + "</xsl:stylesheet>";

            string Once(XsltBackend backend) => new Xslt(
                stylesheet,
                new XsltOptions
                {
                    Backend = backend,
                    Version = XsltVersion.Implemented,
                    OmitXmlDeclaration = true,
                })
                .TransformXml("<r><i>a</i><i>b</i></r>");

            string interpreted = Once(XsltBackend.Interpreted);
            Assert.AreEqual(
                interpreted, Once(XsltBackend.Compiled), "the compiled backend disagreed with the interpreter");

            return interpreted;
        }

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
    }
}
