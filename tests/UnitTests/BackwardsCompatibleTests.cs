namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what backwards compatibility converts — a function's arguments by XPath 1.0's rules, and
    /// the operands of a general comparison where either of them is a number — and for the two things it
    /// is sometimes expected to restore and does not.
    /// </summary>
    /// <remarks>
    /// Backwards compatibility is XSLT 2.0's imitation of 1.0 within 2.0's data model rather than 1.0
    /// itself. Where 2.0 checks a type and refuses what does not reach it, 1.0 converts until it fits and
    /// cannot fail — a sequence becomes its first item, and a value becomes the string or the number the
    /// position asked for. What it does not do is put the data model back: the context item may still be
    /// an atomic value, and a sort key is still compared by what it is.
    /// </remarks>
    [TestClass]
    public sealed class BackwardsCompatibleTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private static string Writes(string expression, string version)
        {
            string stylesheet = $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\" separator=\",\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            string written = new Xslt(
                stylesheet,
                new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true })
                .TransformXml("<doc><a>one</a><a>two</a></doc>");

            return written == "<out/>" ? string.Empty : written["<out>".Length..^"</out>".Length];
        }

        [TestMethod]
        public void AnArgumentIsReducedToItsFirstItem()
        {
            // XPath 2.0 §3.1.5: where the expected type is one item or none and a sequence of more than one
            // is supplied, the value is replaced by its first item. So a call that is a type error under 2.0
            // answers under 1.0, which is what the compatibility is for.
            Assert.AreEqual("3", Writes("string-length(('abc','de'))", "1.0"));
            Assert.AreEqual("true", Writes("contains(doc/a, 'one')", "1.0"));
            Assert.AreEqual("one", Writes("string(doc/a)", "1.0"));

            // A position that takes a sequence takes the sequence: the rule is written for an expected type
            // of one item, and string-join joins what it is given.
            Assert.AreEqual("one-two", Writes("string-join(doc/a, '-')", "1.0"));

            // Under 2.0 the same calls are refused rather than answered.
            Assert.AreEqual("XPTY0004", Refuses("string-length(('abc','de'))"));
        }

        private static string Refuses(string expression)
        {
            return Assert.ThrowsExactly<XsltException>(() => Writes(expression, "2.0")).Code ?? string.Empty;
        }

        [TestMethod]
        public void AnArgumentIsConvertedToWhatThePositionAsksFor()
        {
            // fn:string() where a string was expected and fn:number() where a number was, neither of which
            // can fail. XPath 1.0 had no type error to raise here.
            Assert.AreEqual("2", Writes("string-length(12)", "1.0"));
            Assert.AreEqual("3", Writes("round('2.6')", "1.0"));
            Assert.AreEqual("true", Writes("starts-with(12.5, '12')", "1.0"));
        }

        [TestMethod]
        public void AComparisonWithANumberOnEitherSideComparesNumerically()
        {
            // And the conversion is to xs:double, whose lexical space has an exponent in it — so '5.00e0'
            // is the number five here, where XPath 1.0's own grammar for a number has no exponent and
            // number('5.00e0') is NaN.
            Assert.AreEqual("true", Writes("(1 to 5) = ('apple', 'banana', '5.00e0')", "1.0"));
            Assert.AreEqual("true", Writes("1 = '1.0e0'", "1.0"));
            Assert.AreEqual("false", Writes("1 = 'apple'", "1.0"));

            // Two strings still compare as text, there being no number on either side.
            Assert.AreEqual("false", Writes("'1' = '1.0e0'", "1.0"));

            // Under 2.0 an integer and a string are not comparable at all.
            Assert.AreEqual("XPTY0004", Refuses("1 = '1.0e0'"));
        }

        private static string Transforms(string body, string version)
        {
            string stylesheet = $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\">"
                + $"<xsl:template match=\"/\"><out>{body}</out></xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                Version = XsltVersion.V30,
                OmitXmlDeclaration = true,
            };

            const string Source = "<doc><a>10</a><a>9</a><a>100</a></doc>";
            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(Source);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(Source);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        [TestMethod]
        public void TheContextItemMayStillBeAnAtomicValue()
        {
            // XPath 1.0 defined '.' as self::node(), which held for as long as a context item had nothing
            // else it could be. A 1.0 stylesheet on a 2.0 processor can iterate a sequence of atomic values,
            // and then '.' is one of them: the mode settles how values are read and not what they are.
            Assert.AreEqual(
                "312",
                Transforms("<xsl:for-each select=\"(3,1,2)\"><xsl:value-of select=\".\"/></xsl:for-each>", "1.0"));

            // Including where it is compared, which is where the node-set reading of '.' used to be taken.
            Assert.AreEqual(
                "3|2|",
                Transforms(
                    "<xsl:for-each select=\"(3,1,2)\"><xsl:if test=\". &gt; 1\">"
                    + "<xsl:value-of select=\".\"/><xsl:text>|</xsl:text></xsl:if></xsl:for-each>",
                    "1.0"));

            // And a node still reads as a node: the comparison atomizes it to untyped text and converts.
            Assert.AreEqual(
                "10 100 ",
                Transforms(
                    "<xsl:for-each select=\"doc/a\"><xsl:if test=\". &gt; 9\">"
                    + "<xsl:value-of select=\".\"/><xsl:text> </xsl:text></xsl:if></xsl:for-each>",
                    "1.0"));
        }

        [TestMethod]
        public void CompatibilitySettlesTheSortKeyAndNotTheComparison()
        {
            // 1.0 had one value to a key, so everything after the first item is given up rather than being
            // XTTE1020. That is the whole of what the mode does to a sort.
            Assert.AreEqual(
                "5 4 3 2 1",
                Transforms(
                    "<xsl:perform-sort select=\"1 to 5\"><xsl:sort select=\"(-., 'banana')\"/></xsl:perform-sort>",
                    "1.0"));

            // data-type is absent there, and an absent data-type compares values by what they are whatever
            // the version says. A key that is a number sorts as a number.
            Assert.AreEqual(
                "1 2 3 4 5",
                Transforms(
                    "<xsl:perform-sort select=\"(5,3,1,4,2)\"><xsl:sort select=\".\"/></xsl:perform-sort>",
                    "1.0"));

            // Which is invisible to a stylesheet 1.0 could have written: it sorts on nodes, a node atomizes
            // to untyped text, and untyped text is collated. Ten before nine, as 1.0 had it.
            Assert.AreEqual(
                "10 100 9 ",
                Transforms(
                    "<xsl:for-each select=\"doc/a\"><xsl:sort select=\".\"/>"
                    + "<xsl:value-of select=\".\"/><xsl:text> </xsl:text></xsl:for-each>",
                    "1.0"));

            // And data-type says which it is either way.
            Assert.AreEqual(
                "9 10 100 ",
                Transforms(
                    "<xsl:for-each select=\"doc/a\"><xsl:sort select=\".\" data-type=\"number\"/>"
                    + "<xsl:value-of select=\".\"/><xsl:text> </xsl:text></xsl:for-each>",
                    "1.0"));
        }
    }
}
