namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for a range that is not built until something wants its items.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>1 to 10000000</c> is ten million items and two numbers, and which of those it costs depends
    /// entirely on what is asked of it. Counting it, or asking whether some number is among it, needs
    /// neither the items nor the hundred and sixty megabytes they would take; only something that walks
    /// them one at a time does.
    /// </para>
    /// <para>
    /// So a range is held as where it starts and how long it is, and the limit moved from having one to
    /// laying one out. What the cases here watch for is the two forms disagreeing: a range that counts
    /// differently from the items it would produce, an index that lands on the wrong integer, or a limit
    /// that fires where nothing was being built.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class LazyRangeTests
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

        // ---- A range still is what it was ------------------------------------------------------------------

        [TestMethod]
        public void ASmallRangeIsUnchanged()
        {
            Assert.AreEqual("1 2 3 4 5", Writes("string-join(1 to 5, ' ')"));
            Assert.AreEqual("5", Writes("count(1 to 5)"));
            Assert.AreEqual("3", Writes("(1 to 5)[3]"));
            Assert.AreEqual("15", Writes("sum(1 to 5)"));
            Assert.AreEqual("5 4 3 2 1", Writes("string-join(reverse(1 to 5), ' ')"));
            Assert.AreEqual("2 3", Writes("string-join(subsequence(1 to 5, 2, 2), ' ')"));

            // A descending range is empty rather than reversed, and a single one is one item.
            Assert.AreEqual("0", Writes("count(5 to 1)"));
            Assert.AreEqual("1", Writes("count(5 to 5)"));
            Assert.AreEqual("true", Writes("(5 to 5) instance of xs:integer"));
        }

        // ---- And answers about a large one without building it ---------------------------------------------

        [TestMethod]
        public void CountingALargeRangeDoesNotBuildIt()
        {
            // Ten million items. Laid out they would be a hundred and sixty megabytes; counted they are a
            // subtraction. This test taking milliseconds rather than seconds is the whole assertion.
            Assert.AreEqual("10000000", Writes("count(1 to 10000000)"));
            Assert.AreEqual("500000004", Writes("count(1000000000000000000000 to 1000000000000500000003)"));
        }

        [TestMethod]
        public void LookingForANumberInALargeRangeDoesNotBuildIt()
        {
            Assert.AreEqual("true", Writes("100000000002 = (100000000000 to 100001000003)"));
            Assert.AreEqual("false", Writes("99999999999 = (100000000000 to 100001000003)"));
            Assert.AreEqual("true", Writes("10000000 = (1 to 10000000)"));
            Assert.AreEqual("false", Writes("10000001 = (1 to 10000000)"));

            // The bounds may be wider than a long, which is where the count and the items part company:
            // there are ten million of these and none of them fits sixty-four bits.
            Assert.AreEqual(
                "true",
                Writes("1000000000000000020001 = (1000000000000000000000 to 1000000000000010000003)"));
        }

        [TestMethod]
        public void IndexingIntoALargeRangeLandsOnTheRightInteger()
        {
            Assert.AreEqual("1", Writes("(1 to 10000000)[1]"));
            Assert.AreEqual("5000000", Writes("(1 to 10000000)[5000000]"));
            Assert.AreEqual("10000000", Writes("(1 to 10000000)[10000000]"));

            Assert.AreEqual(
                "1000000000000000000042",
                Writes("(1000000000000000000000 to 1000000000000500000003)[43]"));
        }

        // ---- Where the items are genuinely wanted ----------------------------------------------------------

        [TestMethod]
        public void LayingOutMoreItemsThanTheEngineWillHoldIsRefused()
        {
            // The limit is on laying a range out, not on having one, and XPDY0130 is the code the
            // specification provides for a processor saying it will not do something on this scale.
            Refuses("XPDY0130", "string-join(1 to 10000000, ' ')");
            Refuses("XPDY0130", "count(for $i in 1 to 10000000 return $i)");

            // Just under the limit is laid out and just over is not, so the two forms agree about where
            // the line is.
            Assert.AreEqual("4194304", Writes("count(reverse(1 to 4194304))"));
            Refuses("XPDY0130", "count(reverse(1 to 4194305))");

            // A megabyte-long string is an ordinary enough thing to build, and is inside the limit.
            Assert.AreEqual(
                "1048576",
                Writes("string-length(string-join(for $i in 1 to 1048576 return 'x', ''))"));
        }

        [TestMethod]
        public void MoreItemsThanCanBeCountedIsRefusedWhereTheRangeIsMade()
        {
            // A sequence is indexed by an int here, so a range longer than an int counts has no position to
            // ask about past the first two billion. That one is refused on the way in rather than on the
            // way out, there being nothing it could answer.
            Refuses("XPDY0130", "count(1 to 3000000000)");
            Refuses("XPDY0130", "1 = (1 to 1000000000000000000000)");
        }
    }
}
