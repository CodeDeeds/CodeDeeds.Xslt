namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what a static expression may see and where one is looked for: the narrower function
    /// library a <c>use-when</c> or a shadow attribute is answered against, the documents it may not read
    /// under XSLT 2.0, and the rule that every one of them is answered while the stylesheet is being
    /// prepared rather than where the compiler happens to walk.
    /// </summary>
    [TestClass]
    public sealed class UseWhenTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private static string Sheet(string body, XsltVersion version)
        {
            string number = version.CompareTo(XsltVersion.V30) >= 0 ? "3.0" : "2.0";

            return $"<xsl:stylesheet version=\"{number}\" xmlns:xsl=\"{Xsl}\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + "<xsl:template match=\"/\"><out>" + body + "</out></xsl:template></xsl:stylesheet>";
        }

        private static string Run(string body, XsltVersion version)
        {
            string written = new Xslt(
                Sheet(body, version),
                new XsltOptions { Version = version, OmitXmlDeclaration = true }).TransformXml("<r/>");

            return written == "<out/>" ? string.Empty : written["<out>".Length..^"</out>".Length];
        }

        private static string Refuses(string body, XsltVersion version)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, version)).Code ?? string.Empty;
        }

        [TestMethod]
        public void AUseWhenIsAnsweredWhereverItIsWritten()
        {
            // XSLT 3.0 §3.11 answers every use-when while the stylesheet is being prepared, so an element
            // inside an xsl:fallback that nothing will ever fall back to still carries one. The compiler
            // never walks in there — xsl:value-of is an instruction it knows — and the mistake is a mistake
            // all the same.
            const string inFallback =
                "<xsl:value-of><xsl:text>a</xsl:text><xsl:fallback>"
                + "<row xsl:use-when=\"{0}\">unreachable</row>"
                + "</xsl:fallback></xsl:value-of>";

            foreach (XsltVersion version in new[] { XsltVersion.V20, XsltVersion.V30 })
            {
                Assert.AreEqual(
                    "XPST0003",
                    Refuses(string.Format(inFallback, "invalid///xpath"), version),
                    "a use-when that is not an expression");

                // A path expression needs a context item, and a static expression has none.
                Assert.AreEqual(
                    "XPDY0002",
                    Refuses(string.Format(inFallback, "error"), version),
                    "a use-when that reads the context item");

                // One that answers is answered, and nothing about the transformation changes.
                Assert.AreEqual("a", Run(string.Format(inFallback, "true()"), version));
            }
        }

        [TestMethod]
        public void WhatAStaticExpressionMayNotCall()
        {
            // Each of these reads the source document or the state of a running transformation, and a
            // use-when is answered before there is either.
            foreach (string call in new[]
            {
                "current()", "key('k', 'v')", "unparsed-entity-uri('e')", "unparsed-entity-public-id('e')",
                "regex-group(1)", "current-output-uri()", "current-group()", "current-grouping-key()",
            })
            {
                Assert.AreEqual(
                    "XPST0017",
                    Refuses($"<in xsl:use-when=\"exists({call})\"/>", XsltVersion.V30),
                    call);
            }

            // generate-id() is the one the version decides: XSLT's own function about the document being
            // transformed until 3.0 made it fn:generate-id, a function of the node it is given.
            Assert.AreEqual("XPST0017", Refuses("<in xsl:use-when=\"generate-id(()) = ''\"/>", XsltVersion.V20));
            Assert.AreEqual("<in/>", Run("<in xsl:use-when=\"generate-id(()) = ''\"/>", XsltVersion.V30));
        }

        [TestMethod]
        public void FunctionAvailableAnswersForTheExpressionItIsWrittenIn()
        {
            // The two static contexts are different, and this is the one place where the answer depends on
            // where the question was written: inside a use-when it asks what that expression may call.
            Assert.AreEqual(
                "true,true",
                Run(
                    "<xsl:value-of separator=\",\" select=\""
                    + "function-available('current'), function-available('key')\"/>",
                    XsltVersion.V30));

            Assert.AreEqual(
                string.Empty,
                Run("<in xsl:use-when=\"function-available('current')\"/>", XsltVersion.V30));

            Assert.AreEqual(
                string.Empty,
                Run("<in xsl:use-when=\"function-available('key', 2)\"/>", XsltVersion.V30));

            // And what it may call it still says yes to.
            Assert.AreEqual(
                "<in/>",
                Run("<in xsl:use-when=\"function-available('system-property')\"/>", XsltVersion.V30));
        }

        [TestMethod]
        public void NoDocumentIsAvailableToAStaticExpressionUnderTwoPointZero()
        {
            // XSLT 2.0 §3.13.2 answers a static expression with the set of available documents empty, so
            // doc-available(), which asks by trying, answers false however readable the document is. 3.0
            // lifted that, and lifting it is what lets a static expression read doc('') — the module it
            // stands in.
            Assert.AreEqual(string.Empty, Run("<in xsl:use-when=\"doc-available('')\"/>", XsltVersion.V20));
            Assert.AreEqual("<in/>", Run("<in xsl:use-when=\"doc-available('')\"/>", XsltVersion.V30));
        }

        [TestMethod]
        public void ADeclarationFromALaterVersionIsIgnoredBeforeItsUseWhenIsRead()
        {
            // XSLT 3.0 3.11: an element in the XSLT namespace standing among the declarations, which this
            // version does not allow to stand there, is ignored together with its content. Nothing in it is
            // read at all -- and its use-when is the one part of it that could still refuse the stylesheet,
            // being answered before the element's name would otherwise be looked at. A stylesheet written
            // for XSLT 4.0 may well have written that condition in XPath 4.0.
            const string later =
                "<xsl:stylesheet version=\"4.0\" xmlns:xsl=\"" + Xsl + "\">"
                + "<xsl:template match=\"/\"><out>ok</out></xsl:template>"
                + "<xsl:new-declaration use-when=\"fn:new-function()\">"
                + "<xsl:new-child use-when=\"nonsense///\"/>"
                + "</xsl:new-declaration>"
                + "</xsl:stylesheet>";

            Assert.AreEqual(
                "<out>ok</out>",
                new Xslt(later, new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true })
                    .TransformXml("<r/>"));

            // An element XSLT does know, but does not allow among the declarations, goes the same way: a
            // later version might allow it there, and this one is in no position to say it may not.
            const string misplaced =
                "<xsl:stylesheet version=\"4.0\" xmlns:xsl=\"" + Xsl + "\">"
                + "<xsl:template match=\"/\"><out>ok</out></xsl:template>"
                + "<xsl:when test=\"nonsense///\"/>"
                + "</xsl:stylesheet>";

            Assert.AreEqual(
                "<out>ok</out>",
                new Xslt(misplaced, new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true })
                    .TransformXml("<r/>"));

            // Under a version this engine implements there is no later version to defer to, and the
            // declaration it has never heard of refuses the stylesheet.
            const string here =
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"" + Xsl + "\">"
                + "<xsl:template match=\"/\"><out>ok</out></xsl:template>"
                + "<xsl:new-declaration/>"
                + "</xsl:stylesheet>";

            Assert.AreEqual(
                "XTSE0010",
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(here, new XsltOptions { Version = XsltVersion.V30 }).TransformXml("<r/>"))
                    .Code);

            // And a use-when on a declaration this version does allow is answered as it always was, however
            // late the version the stylesheet claims.
            const string known =
                "<xsl:stylesheet version=\"4.0\" xmlns:xsl=\"" + Xsl + "\">"
                + "<xsl:template match=\"/\"><out>ok</out></xsl:template>"
                + "<xsl:template match=\"r\" use-when=\"nonsense///\"/>"
                + "</xsl:stylesheet>";

            Assert.AreEqual(
                "XPST0003",
                Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(known, new XsltOptions { Version = XsltVersion.V30 }).TransformXml("<r/>"))
                    .Code);
        }
    }
}
