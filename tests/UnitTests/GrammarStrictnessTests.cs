namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for text the grammar does not admit, which a lenient parser reads as something else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A parser that accepts more than the grammar does is not more useful, it is less: every one of
    /// these reads as a perfectly sensible expression that means something other than what was written.
    /// <c>2 &lt; 3 &lt; 4</c> asks whether false is less than 4; <c>10div 3</c> looks like ten over three
    /// and could as easily be a name; <c>$2</c> is a variable nothing can ever have declared.
    /// </para>
    /// <para>
    /// Chaining is the one with a history. XPath 1.0 has a left-recursive EqualityExpr and does allow it;
    /// 2.0 gives ComparisonExpr room for a single operator and no more. So the rule follows the grammar
    /// in force rather than being applied everywhere.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class GrammarStrictnessTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private static string Writes(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        private static void Refuses(string code, string expression)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression));
            Assert.AreEqual(code, error.Code, $"'{expression}': {error.Message}");
        }

        // ---- A comparison takes one operator ---------------------------------------------------------------

        [TestMethod]
        public void AComparisonDoesNotChain()
        {
            Refuses("XPST0003", "2 &lt; 3 &lt; 4");
            Refuses("XPST0003", "true() = true() = true()");
            Refuses("XPST0003", "1 = 2 != 3");
            Refuses("XPST0003", "1 &lt;= 2 &lt; 3");

            // One of them is fine, and so is a chain made explicit with parentheses.
            Assert.AreEqual("true", Writes("2 &lt; 3"));
            Assert.AreEqual("true", Writes("(2 &lt; 3) = true()"));
        }

        // ---- A number and a name need separating -----------------------------------------------------------

        [TestMethod]
        public void ANumberDoesNotRunIntoAName()
        {
            // Without the rule there is no telling '10div' from a number called 10div.
            Refuses("XPST0003", "10div 3");
            Refuses("XPST0003", "10idiv 3");
            Refuses("XPST0003", "10mod 3");
            Refuses("XPST0003", "1to 3");

            // With the space it is the arithmetic it looks like, and an exponent is still a number.
            // Two integers divide to an xs:decimal, which keeps rather more of the answer than a
            // double would.
            Assert.AreEqual("3.3333333333333333333333333333", Writes("10 div 3"));
            Assert.AreEqual("3", Writes("10 idiv 3"));
            Assert.AreEqual("1", Writes("10 mod 3"));
            Assert.AreEqual("100000", Writes("1e5"));
        }

        // ---- A variable is named, not numbered -------------------------------------------------------------

        [TestMethod]
        public void AVariableNameStartsLikeAName()
        {
            // A digit cannot start an XML name, so $2 names nothing and never could: the failure is in the
            // grammar and not in the static context, which is why it is XPST0003 and not XPST0008.
            Refuses("XPST0003", "some $foo in (1, $2) return 1");
            Refuses("XPST0003", "$1");

            // And a name that starts like one is still read, digits and all.
            Assert.AreEqual("true", Writes("some $a2 in (1) satisfies $a2 = 1"));
        }
    }
}
