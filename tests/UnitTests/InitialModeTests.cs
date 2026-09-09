using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for which modes a transformation may start in, what an <c>xsl:mode</c> may say of itself, the
    /// initial match selection, and the current item where it is not a node.
    /// </summary>
    [TestClass]
    public sealed class InitialModeTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private static string Sheet(string declarations, string outermost = "xsl:stylesheet", string attributes = "")
        {
            return $"<{outermost} version=\"3.0\" xmlns:xsl=\"{Xsl}\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\" {attributes}>"
                + declarations + $"</{outermost}>";
        }

        private static string Run(string stylesheet, string? input = "<doc><a>1</a><b>2</b></doc>", string? mode = null, string? selection = null)
        {
            Xslt compiled = new Xslt(
                stylesheet,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    InitialMode = mode,
                    InitialMatchSelection = selection,
                });

            return input is null ? compiled.Transform() : compiled.TransformXml(input);
        }

        private static string Refuses(string stylesheet, string? input = "<doc><a>1</a><b>2</b></doc>", string? mode = null, string? selection = null)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(stylesheet, input, mode, selection)).Code ?? string.Empty;
        }

        [TestMethod]
        public void AModeDeclaresWhatItMay()
        {
            // The unnamed mode is private to its package, a mode is never abstract, and two declarations at
            // one precedence disagreeing about visibility are the conflict any other attribute is.
            Assert.AreEqual("XTSE0020", Refuses(Sheet("<xsl:mode visibility=\"public\"/>")));
            Assert.AreEqual("XTSE0020", Refuses(Sheet("<xsl:mode visibility=\"final\"/>")));
            Assert.AreEqual("XTSE0020", Refuses(Sheet("<xsl:mode name=\"m\" visibility=\"abstract\"/>")));
            Assert.AreEqual(
                "XTSE0545",
                Refuses(Sheet("<xsl:mode name=\"m\" visibility=\"final\"/><xsl:mode name=\"m\" visibility=\"private\"/>")));
            Assert.AreEqual("XTSE0020", Refuses(Sheet("<xsl:mode name=\"m\" typed=\"maybe\"/>")));
        }

        [TestMethod]
        public void OnlySomeModesAreAWayIn()
        {
            const string Rules = "<xsl:template match=\"/\" mode=\"m\"><in-m/></xsl:template><xsl:template match=\"/\" mode=\"#unnamed\"><unnamed/></xsl:template>";

            // A plain stylesheet: a mode its template rules name is eligible, and so is one it declares
            // without saying more — but not one it declares private, nor one only xsl:apply-templates names.
            Assert.AreEqual("<in-m/>", Run(Sheet(Rules), mode: "m"));
            Assert.AreEqual("<in-m/>", Run(Sheet("<xsl:mode name=\"m\"/>" + Rules), mode: "m"));
            Assert.AreEqual("XTDE0045", Refuses(Sheet("<xsl:mode name=\"m\" visibility=\"private\"/>" + Rules), mode: "m"));
            Assert.AreEqual(
                "XTDE0045",
                Refuses(Sheet("<xsl:template match=\"/\"><xsl:apply-templates mode=\"n\"/></xsl:template>"), mode: "n"));

            // A package declares its modes, the unnamed one included, and one declared without a
            // visibility is private: not a way in, unless it is the package's default mode. The unnamed
            // mode always is.
            const string Declared = "<xsl:mode/><xsl:mode name=\"m\"/>" + Rules;
            Assert.AreEqual("XTDE0045", Refuses(Sheet(Declared, "xsl:package"), mode: "m"));
            Assert.AreEqual("<in-m/>", Run(Sheet("<xsl:mode/><xsl:mode name=\"m\" visibility=\"public\"/>" + Rules, "xsl:package"), mode: "m"));
            Assert.AreEqual("<in-m/>", Run(Sheet(Declared, "xsl:package", "default-mode=\"m\""), mode: "m"));
            Assert.AreEqual("<unnamed/>", Run(Sheet(Declared, "xsl:package"), mode: "#unnamed"));

            // A package that declares no modes is back to the rule for a plain stylesheet.
            Assert.AreEqual("<in-m/>", Run(Sheet(Rules, "xsl:package", "declared-modes=\"no\""), mode: "m"));
        }

        [TestMethod]
        public void ATypedModeTakesNoUntypedNode()
        {
            // This engine validates nothing, so every element is untyped, and a mode declared typed cannot
            // process one. The document node is not an element, so the rule for it runs first.
            Assert.AreEqual(
                "XTTE3100",
                Refuses(
                    Sheet("<xsl:mode name=\"t\" typed=\"yes\" visibility=\"public\"/><xsl:template match=\"/\" mode=\"t\"><xsl:apply-templates mode=\"t\"/></xsl:template>"),
                    mode: "t"));
            Assert.AreEqual(
                "<ok/>",
                Run(Sheet("<xsl:mode name=\"t\" typed=\"no\" visibility=\"public\"/><xsl:template match=\"/\" mode=\"t\"><ok/></xsl:template>"), mode: "t"));
        }

        [TestMethod]
        public void DeepSkipStillEntersTheDocument()
        {
            // The built-in rule for a document node applies templates to its children whatever the mode's
            // on-no-match says (§6.7.1), so the document element is reached; deep-skip then skips the
            // elements nothing matches, with their subtrees.
            Assert.AreEqual(
                "<b>2</b>",
                Run(Sheet(
                    "<xsl:mode on-no-match=\"deep-skip\"/>"
                    + "<xsl:template match=\"doc\"><xsl:apply-templates/></xsl:template>"
                    + "<xsl:template match=\"b\"><b><xsl:value-of select=\".\"/></b></xsl:template>"),
                    input: "<doc><a><b>hidden</b></a><b>2</b></doc>"));
        }

        [TestMethod]
        public void AFunctionRunsInTheUnnamedMode()
        {
            // mode="#current" inside a function is the unnamed mode (erratum XT.E19): a function is not
            // called in any mode, whatever rule called it.
            Assert.AreEqual(
                "<out><in-m/><unnamed/></out>",
                Run(Sheet(
                    "<xsl:function name=\"f:go\" xmlns:f=\"urn:f\"><xsl:param name=\"n\"/><xsl:apply-templates select=\"$n\" mode=\"#current\"/></xsl:function>"
                    + "<xsl:template match=\"/\" mode=\"m\"><out><xsl:apply-templates select=\"doc/a\" mode=\"#current\"/><xsl:sequence select=\"f:go(doc/a)\" xmlns:f=\"urn:f\"/></out></xsl:template>"
                    + "<xsl:template match=\"a\" mode=\"m\"><in-m/></xsl:template><xsl:template match=\"a\"><unnamed/></xsl:template>"),
                    mode: "m"));
        }

        [TestMethod]
        public void AnInitialMatchSelectionIsWhereTemplatesAreFirstApplied()
        {
            const string Rules =
                "<xsl:template match=\"a | b\"><e n=\"{name()}\" p=\"{position()}\" of=\"{last()}\"/></xsl:template>"
                + "<xsl:template match=\".[. instance of xs:integer]\"><i v=\"{.}\" c=\"{current()}\"/></xsl:template>";

            // Nodes chosen inside the input, with their positions among the selection.
            Assert.AreEqual(
                "<e n=\"a\" p=\"1\" of=\"2\"/><e n=\"b\" p=\"2\" of=\"2\"/>",
                Run(Sheet(Rules), selection: "/doc/*"));

            // Atomic values, with no input at all — and current() is the value being processed.
            Assert.AreEqual("<i v=\"1\" c=\"1\"/><i v=\"2\" c=\"2\"/>", Run(Sheet(Rules), input: null, selection: "1 to 2"));

            // In the mode the caller names, and an expression that is not one is refused.
            Assert.AreEqual(
                "<m/>",
                Run(Sheet("<xsl:template match=\"a\" mode=\"m\"><m/></xsl:template>"), mode: "m", selection: "/doc/a"));
            Assert.AreEqual("XPST0003", Refuses(Sheet(Rules), selection: "1 +"));
        }

        [TestMethod]
        public void CurrentIsTheAtomicValueBeingWalked()
        {
            // xsl:for-each over atomic values, and a sort key over them: current() is the value, where before
            // it was an error for want of a node. An inner walk over nodes makes its node current again.
            Assert.AreEqual(
                "<out>3 1 2|1122</out>",
                Run(Sheet(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"sorted\" as=\"xs:integer*\"><xsl:for-each select=\"1 to 3\"><xsl:sort select=\"current() mod 3\"/><xsl:sequence select=\"current()\"/></xsl:for-each></xsl:variable>"
                    + "<xsl:value-of select=\"$sorted\"/>"
                    + "<xsl:text>|</xsl:text>"
                    + "<xsl:variable name=\"d\" select=\"/\"/>"
                    + "<xsl:for-each select=\"'a'\"><xsl:for-each select=\"$d/doc/*\"><xsl:value-of select=\"concat(current(), .)\"/></xsl:for-each></xsl:for-each>"
                    + "</out></xsl:template>"),
                    input: "<doc><a>1</a><b>2</b></doc>"));
        }
    }
}
