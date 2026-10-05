namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what a stylesheet of several hundred parameters costs to set going, apart from anything
    /// it does: a large <c>xsl:map</c> built in one pass, supplied parameters bound by name, and a global
    /// found by its slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found in the DocBook xslTNG stylesheets, which declare 224 parameters, hand them all on in one
    /// <c>xsl:map</c>, and run a document through four stylesheets of their own before formatting it, each
    /// of which is supplied the 224 and builds the map again. A document of three hundred characters took
    /// 28 milliseconds and 24 megabytes, and a quarter of the one and half of the other was that map: a
    /// map is not changed once made, and <c>xsl:map</c> made a new one for every entry, each a copy of the
    /// last. See <c>ConformanceNotes.md</c>, "What every DocBook document paid".
    /// </para>
    /// <para>
    /// Most of these hold answers, which were right before: the entries of a map and their order, which
    /// key is named when one is written twice, which parameter a supplied name reaches. One measures, and
    /// does not pass against the engine as it was.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class ManyParametersTests
    {
        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\""
            + " xmlns:p=\"urn:p\" xmlns:q=\"urn:q\" exclude-result-prefixes=\"#all\">";

        private static Xslt Compiled(string body, XsltBackend backend = XsltBackend.Interpreted, XsltOptions? options = null)
        {
            return new Xslt(
                Head + body + "</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Backend = backend,
                    Parameters = options?.Parameters,
                });
        }

        /// <summary>What a stylesheet writes, on both backends, which have to agree.</summary>
        private static string Run(string body, XsltOptions? options = null)
        {
            string interpreted = Compiled(body, XsltBackend.Interpreted, options).Transform();
            string compiled = Compiled(body, XsltBackend.Compiled, options).Transform();

            Assert.AreEqual(interpreted, compiled, "The backends disagree.");
            return interpreted;
        }

        private static string Refuses(string body, XsltOptions? options = null)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, options)).Code ?? string.Empty;
        }

        // ---- xsl:map ---------------------------------------------------------------------------------------

        [TestMethod]
        public void AMapHoldsItsEntriesInTheOrderTheyWereWritten()
        {
            // Entries from xsl:map-entry and from maps that are already maps, turn about.
            Assert.AreEqual(
                "<out>5 | c a b d e | 3 1 2 4 5</out>",
                Run(
                    "<xsl:template name=\"xsl:initial-template\"><xsl:variable name=\"m\" as=\"map(*)\"><xsl:map>"
                    + "<xsl:map-entry key=\"'c'\" select=\"3\"/>"
                    + "<xsl:sequence select=\"map { 'a': 1 }\"/>"
                    + "<xsl:map-entry key=\"'b'\"><xsl:sequence select=\"2\"/></xsl:map-entry>"
                    + "<xsl:sequence select=\"map:entry('d', 4), map { 'e': 5 }\"/>"
                    + "</xsl:map></xsl:variable>"
                    + "<out><xsl:value-of select=\"map:size($m), '|', map:keys($m), '|', map:keys($m) ! $m(.)\"/></out>"
                    + "</xsl:template>"));

            // No entries at all is the empty map.
            Assert.AreEqual(
                "<out>0 true</out>",
                Run(
                    "<xsl:template name=\"xsl:initial-template\"><xsl:variable name=\"m\" as=\"map(*)\"><xsl:map/></xsl:variable>"
                    + "<out><xsl:value-of select=\"map:size($m), empty(map:keys($m))\"/></out></xsl:template>"));

            // A thousand of them, by a loop, each where it should be.
            Assert.AreEqual(
                "<out>1000 2 1000 2000 1 1000</out>",
                Run(
                    "<xsl:template name=\"xsl:initial-template\"><xsl:variable name=\"m\" as=\"map(xs:integer, xs:integer)\"><xsl:map>"
                    + "<xsl:for-each select=\"1 to 1000\"><xsl:map-entry key=\".\" select=\". * 2\"/></xsl:for-each>"
                    + "</xsl:map></xsl:variable>"
                    + "<out><xsl:value-of select=\"map:size($m), $m(1), $m(500), $m(1000), map:keys($m)[1], map:keys($m)[last()]\"/></out>"
                    + "</xsl:template>"));
        }

        [TestMethod]
        public void AKeyWrittenTwiceInAMapIsNamed()
        {
            static string Mapping(string entries) =>
                "<xsl:template name=\"xsl:initial-template\"><xsl:variable name=\"m\" as=\"map(*)\"><xsl:map>"
                + entries + "</xsl:map></xsl:variable><out><xsl:value-of select=\"map:size($m)\"/></out></xsl:template>";

            // Twice by xsl:map-entry, once each way of making an entry, and far apart among many.
            foreach (string entries in new[]
            {
                "<xsl:map-entry key=\"'a'\" select=\"1\"/><xsl:map-entry key=\"'b'\" select=\"2\"/><xsl:map-entry key=\"'a'\" select=\"3\"/>",
                "<xsl:map-entry key=\"'a'\" select=\"1\"/><xsl:sequence select=\"map { 'b': 2, 'a': 3 }\"/>",
                "<xsl:sequence select=\"map { 'a': 1 }\"/><xsl:sequence select=\"map { 'a': 1 }\"/>",
                "<xsl:for-each select=\"1 to 300, 150\"><xsl:map-entry key=\"concat('k', .)\" select=\".\"/></xsl:for-each>",
            })
            {
                Assert.AreEqual("XTDE3365", Refuses(Mapping(entries)), entries);
            }

            // 1 and 1.0 are one key, an integer and a decimal being compared as numbers; '1' is another.
            Assert.AreEqual(
                "XTDE3365",
                Refuses(Mapping("<xsl:map-entry key=\"1\" select=\"'x'\"/><xsl:map-entry key=\"1.0\" select=\"'y'\"/>")));
            Assert.AreEqual(
                "<out>2</out>",
                Run(Mapping("<xsl:map-entry key=\"1\" select=\"'x'\"/><xsl:map-entry key=\"'1'\" select=\"'y'\"/>")));

            // The message says which key, the first one met a second time.
            XsltException twice = Assert.ThrowsExactly<XsltException>(() => Compiled(Mapping(
                "<xsl:map-entry key=\"'one'\" select=\"1\"/><xsl:map-entry key=\"'two'\" select=\"2\"/>"
                + "<xsl:map-entry key=\"'two'\" select=\"3\"/><xsl:map-entry key=\"'one'\" select=\"4\"/>")).Transform());

            StringAssert.Contains(twice.Message, "'two'");
        }

        [TestMethod]
        public void ALargeMapIsBuiltWithoutAMapForEveryEntry()
        {
            // A thousand entries. Built by adding each to the map so far, a map being something that is
            // not changed once made, that was a thousand maps, each a copy of the last: some fifty
            // megabytes, 51.7 measured. Built in one pass it is the entries and the map, 1.2. The bound is
            // four, so that code not yet compiled its best, which allocates more, does not decide it; and
            // the least of several is taken for the same reason.
            Xslt building = Compiled(
                "<xsl:template name=\"xsl:initial-template\"><xsl:variable name=\"m\" as=\"map(*)\"><xsl:map>"
                + "<xsl:for-each select=\"1 to 1000\"><xsl:map-entry key=\".\" select=\". * 2\"/></xsl:for-each>"
                + "</xsl:map></xsl:variable><out><xsl:value-of select=\"map:size($m)\"/></out></xsl:template>");

            Assert.AreEqual("<out>1000</out>", building.Transform());

            long least = long.MaxValue;

            for (int i = 0; i < 12; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                building.Transform(System.IO.TextWriter.Null);
                least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            Assert.IsTrue(
                least < 4 * 1024 * 1024,
                $"An xsl:map of a thousand entries allocated {least / 1024:N0} KB; it was about fifty megabytes when every entry made a new map.");
        }

        // ---- Parameters supplied by name -------------------------------------------------------------------

        [TestMethod]
        public void EverySuppliedParameterReachesTheParameterItNames()
        {
            // Three hundred parameters, all of them supplied, and summed. Supplied in the opposite order
            // to the one they are declared in, a few of them by their expanded names, with some that no
            // parameter is named by among them.
            System.Text.StringBuilder declared = new System.Text.StringBuilder();
            Dictionary<string, object?> supplied = new Dictionary<string, object?>();

            for (int i = 300; i >= 1; i--)
            {
                supplied[i % 7 == 0 ? "{}n" + i : "n" + i] = i;
                supplied["absent" + i] = "nothing reads this";
            }

            for (int i = 1; i <= 300; i++)
            {
                declared.Append("<xsl:param name=\"n").Append(i).Append("\" as=\"xs:integer\" select=\"-1000000\"/>");
            }

            string sum = string.Join(" + ", Enumerable.Range(1, 300).Select(i => "$n" + i));

            Assert.AreEqual(
                "<out>45150</out>",
                Run(
                    declared + "<xsl:template name=\"xsl:initial-template\"><out><xsl:value-of select=\"" + sum + "\"/></out></xsl:template>",
                    new XsltOptions { Parameters = supplied }));

            // Not supplied, a parameter keeps its default, whatever else was.
            supplied.Remove("n150");

            Assert.AreEqual(
                "<out>-955000</out>",
                Run(
                    declared + "<xsl:template name=\"xsl:initial-template\"><out><xsl:value-of select=\"" + sum + "\"/></out></xsl:template>",
                    new XsltOptions { Parameters = supplied }));
        }

        [TestMethod]
        public void ASuppliedParameterIsHeldToTheTypeOfTheOneItNames()
        {
            // Two parameters of one local name in two namespaces are two parameters, and what is supplied
            // for one is nothing to the other.
            const string Body =
                "<xsl:param name=\"p:size\" as=\"xs:integer\" select=\"1\"/>"
                + "<xsl:param name=\"q:size\" as=\"xs:string\" select=\"'unset'\"/>"
                + "<xsl:param name=\"size\" select=\"'plain'\"/>"
                + "<xsl:template name=\"xsl:initial-template\"><out><xsl:value-of select=\"$p:size + 1, $q:size, $size\"/></out></xsl:template>";

            Assert.AreEqual("<out>2 unset plain</out>", Run(Body));

            Assert.AreEqual(
                "<out>42 large given</out>",
                Run(Body, new XsltOptions
                {
                    Parameters = new Dictionary<string, object?>
                    {
                        ["{urn:p}size"] = 41, ["{urn:q}size"] = "large", ["size"] = "given",
                    },
                }));

            Assert.AreEqual(
                "<out>8 unset plain</out>",
                Run(Body, new XsltOptions { Parameters = new Dictionary<string, object?> { ["{urn:p}size"] = 7 } }));

            // What does not fit the type of the parameter it names is refused, as it was.
            Assert.AreEqual(
                "XTTE0590",
                Refuses(Body, new XsltOptions { Parameters = new Dictionary<string, object?> { ["{urn:p}size"] = "wide" } }));
        }

        // ---- Globals found by their slot -------------------------------------------------------------------

        [TestMethod]
        public void EachOfManyGlobalsIsTheOneItsNameSays()
        {
            // Four hundred global variables, each defined by the one before, read from the far end so
            // that every one of them is first asked for from inside the evaluation of another.
            System.Text.StringBuilder declared = new System.Text.StringBuilder("<xsl:variable name=\"v1\" select=\"1\"/>");

            for (int i = 2; i <= 400; i++)
            {
                declared.Append("<xsl:variable name=\"v").Append(i).Append("\" select=\"$v").Append(i - 1).Append(" + ").Append(i).Append("\"/>");
            }

            Assert.AreEqual(
                "<out>80200 3 20100</out>",
                Run(declared + "<xsl:template name=\"xsl:initial-template\"><out><xsl:value-of select=\"$v400, $v2, $v200\"/></out></xsl:template>"));

            // And one stylesheet run by two transformations at once reads the same globals in both.
            Xslt shared = Compiled(
                declared + "<xsl:template name=\"xsl:initial-template\"><out><xsl:value-of select=\"$v400\"/></out></xsl:template>");
            string[] results = new string[16];

            Parallel.For(0, results.Length, i => results[i] = shared.Transform());

            foreach (string result in results)
            {
                Assert.AreEqual("<out>80200</out>", result);
            }
        }
    }
}
