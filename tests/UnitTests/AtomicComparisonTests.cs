namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for a general comparison whose operands are atomic values or sequences of them — what it
    /// answers, which did not change, and what it allocates, which did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From XPath 2.0 a general comparison is existential over two sequences, and the engine took that at
    /// its word for every comparison: each operand laid out in a list, and the two lists walked through
    /// an interface. For <c>position() = 1</c> that was a list, its array and a boxed enumerator for each
    /// of two single integers, 416 bytes to compare two numbers, at every evaluation of every such
    /// comparison in a 2.0 or 3.0 stylesheet. Two atomic values are one pair and are compared as one now.
    /// </para>
    /// <para>
    /// The answers are asked of both backends, as a value and as a test, and every one of them is what the
    /// engine answered before. The order pairs are taken in is part of that, since it decides whether a
    /// pair that cannot be compared is reached before one that settles the answer. See
    /// <c>ConformanceNotes.md</c>, "What it costs to ask whether two numbers are equal".
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class AtomicComparisonTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Source = "<r><n>10</n><n>20</n><w>ten</w></r>";

        private static string Sheet(string version, string body)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                + "<xsl:output method=\"text\"/>"
                + "<xsl:variable name=\"five\" select=\"5\"/>"
                + "<xsl:variable name=\"nodes\" select=\"/r/n\"/>"
                + $"<xsl:template match=\"/r\">{body}</xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, XsltBackend backend)
        {
            return new Xslt(stylesheet, new XsltOptions { Backend = backend, Version = XsltVersion.V30 })
                .TransformXml(Source);
        }

        /// <summary>
        /// The one answer a comparison gives as a value and as a test, on both backends.
        /// </summary>
        private static string Answers(string asWritten, string version = "3.0")
        {
            string expression = asWritten.Replace("<", "&lt;", StringComparison.Ordinal);

            string stylesheet = Sheet(
                version,
                $"<xsl:value-of select=\"{expression}\"/>|<xsl:if test=\"{expression}\">true</xsl:if>");

            string interpreted = Run(stylesheet, XsltBackend.Interpreted);
            Assert.AreEqual(interpreted, Run(stylesheet, XsltBackend.Compiled), $"the backends disagree about {asWritten}");

            string[] parts = interpreted.Split('|');
            Assert.AreEqual(
                parts[0] == "true" ? "true" : string.Empty,
                parts[1],
                $"{asWritten} is one thing selected and another tested at {version}");

            return parts[0];
        }

        /// <summary>The code both backends refuse a comparison with.</summary>
        private static string Refuses(string asWritten, string version = "3.0")
        {
            string expression = asWritten.Replace("<", "&lt;", StringComparison.Ordinal);
            string stylesheet = Sheet(version, $"<xsl:value-of select=\"{expression}\"/>");
            string? code = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                string refused = Assert.ThrowsExactly<XsltException>(() => Run(stylesheet, backend)).Code ?? string.Empty;

                code ??= refused;
                Assert.AreEqual(code, refused, $"the backends refuse {asWritten} differently");
            }

            return code!;
        }

        [TestMethod]
        public void TwoAtomicValuesAreComparedAsThePairTheyAre()
        {
            foreach (string version in new[] { "2.0", "3.0" })
            {
                Assert.AreEqual("true", Answers("1 = 1", version));
                Assert.AreEqual("true", Answers("1 = 1.0", version));
                Assert.AreEqual("true", Answers("1 = 1e0", version));
                Assert.AreEqual("false", Answers("1 = 2", version));
                Assert.AreEqual("true", Answers("1 != 2", version));
                Assert.AreEqual("true", Answers("1 < 2", version));
                Assert.AreEqual("false", Answers("2 <= 1", version));
                Assert.AreEqual("true", Answers("'a' = 'a'", version));
                Assert.AreEqual("true", Answers("'a' < 'b'", version));
                Assert.AreEqual("true", Answers("true() = true()", version));
                Assert.AreEqual("true", Answers("false() < true()", version));
                Assert.AreEqual("true", Answers("$five = 5", version));
                Assert.AreEqual("true", Answers("$five * 2 > 9", version));
                Assert.AreEqual("true", Answers("xs:date('2020-01-01') < xs:date('2020-06-01')", version));
                Assert.AreEqual("false", Answers("number('x') = number('x')", version));

                // And refused where the two are not comparable, which one pair is as readily as many.
                Assert.AreEqual("XPTY0004", Refuses("1 = 'a'", version));
                Assert.AreEqual("XPTY0004", Refuses("'a' < 1", version));
                Assert.AreEqual("XPTY0004", Refuses("xs:date('2020-01-01') = 1", version));
            }
        }

        [TestMethod]
        public void PositionAndLastAreComparedAsBefore()
        {
            const string Body =
                "<xsl:for-each select=\"1 to 5\">"
                + "<xsl:if test=\"position() = 1\">first </xsl:if>"
                + "<xsl:if test=\"position() != last()\"><xsl:value-of select=\".\"/>,</xsl:if>"
                + "<xsl:if test=\"position() = last()\"><xsl:value-of select=\".\"/> last</xsl:if>"
                + "</xsl:for-each>|<xsl:value-of select=\"count(n[position() &lt; 2])\"/>";

            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                string stylesheet = Sheet(version, Body);
                string interpreted = Run(stylesheet, XsltBackend.Interpreted);

                Assert.AreEqual("first 1,2,3,4,5 last|1", interpreted, version);
                Assert.AreEqual(interpreted, Run(stylesheet, XsltBackend.Compiled), version);
            }
        }

        [TestMethod]
        public void ASequenceIsStillComparedItemByItem()
        {
            foreach (string version in new[] { "2.0", "3.0" })
            {
                Assert.AreEqual("true", Answers("(1, 2, 3) = (3, 4)", version));
                Assert.AreEqual("false", Answers("(1, 2) = (3, 4)", version));
                Assert.AreEqual("true", Answers("(1, 2) != (1, 2)", version));
                Assert.AreEqual("true", Answers("(1, 2, 3) > 2", version));
                Assert.AreEqual("true", Answers("2 < (1, 2, 3)", version));
                Assert.AreEqual("false", Answers("4 < (1, 2, 3)", version));
                Assert.AreEqual("true", Answers("3 = (1 to 5)", version));
                Assert.AreEqual("true", Answers("(1 to 5) = 3", version));
                Assert.AreEqual("false", Answers("(1 to 5) = 6", version));

                // Nothing on either side is no pair at all, and so false whichever way it is asked.
                Assert.AreEqual("false", Answers("() = 1", version));
                Assert.AreEqual("false", Answers("1 = ()", version));
                Assert.AreEqual("false", Answers("() != ()", version));

                // A range is asked about and not laid out, on whichever side it stands.
                Assert.AreEqual("true", Answers("5 = (1 to 100000000)", version));
                Assert.AreEqual("true", Answers("(1 to 100000000) = 5", version));
            }

            // XPath 3.1 has an array atomize to its members, however deep they lie.
            Assert.AreEqual("true", Answers("[2] = 2"));
            Assert.AreEqual("true", Answers("2 = [[1], [2]]"));
            Assert.AreEqual("false", Answers("[1, 3] = 2"));
        }

        [TestMethod]
        public void NodesOnOneSideOrBothAreStillAtomized()
        {
            foreach (string version in new[] { "2.0", "3.0" })
            {
                Assert.AreEqual("true", Answers("$nodes = 20", version));
                Assert.AreEqual("true", Answers("20 = $nodes", version));
                Assert.AreEqual("false", Answers("$nodes = 30", version));
                Assert.AreEqual("true", Answers("$nodes = (5, 10)", version));
                Assert.AreEqual("true", Answers("$nodes = ('20', 'x')", version));
                Assert.AreEqual("true", Answers("$nodes = $nodes", version));
                Assert.AreEqual("true", Answers("$nodes != $nodes", version));
                Assert.AreEqual("true", Answers("(n, w) = 'ten'", version));

                // Untyped text beside a number is cast to one, and the cast may fail.
                Assert.AreEqual("FORG0001", Refuses("(n, w) = 99", version));
            }
        }

        [TestMethod]
        public void PairsAreTakenLeftFirstAndTheAnswerStopsTheWalk()
        {
            foreach (string version in new[] { "2.0", "3.0" })
            {
                // The pair that settles it comes before the pair that cannot be compared, or after it.
                Assert.AreEqual("true", Answers("(1, 'a') = 1", version));
                Assert.AreEqual("XPTY0004", Refuses("('a', 1) = 1", version));
                Assert.AreEqual("true", Answers("1 = (1, 'a')", version));
                Assert.AreEqual("XPTY0004", Refuses("1 = ('a', 1)", version));

                // Each item on the left meets every item on the right before the next one does.
                Assert.AreEqual("true", Answers("(1, 'a') = (2, 1)", version));
                Assert.AreEqual("XPTY0004", Refuses("(1, 'a') = (2, 3)", version));
            }
        }

        [TestMethod]
        public void BackwardsCompatibilityComparesASequenceAsItDid()
        {
            Assert.AreEqual("true", Answers("(1, 2, 3) = 3", "1.0"));
            Assert.AreEqual("true", Answers("(1, 2, 3) != 3", "1.0"));
            Assert.AreEqual("true", Answers("(1, 2, 3) < 2", "1.0"));
            Assert.AreEqual("false", Answers("(1, 2, 3) > 3", "1.0"));
            Assert.AreEqual("true", Answers("(3, 4) >= (1 to 2)", "1.0"));
            Assert.AreEqual("true", Answers("3 <= (1 to 5)", "1.0"));
            Assert.AreEqual("false", Answers("() = 1", "1.0"));

            // Where 2.0 refuses the pair, 1.0 converts it: 'a' is NaN beside a number and equal to nothing.
            Assert.AreEqual("false", Answers("('a', 'b') = 1", "1.0"));
            Assert.AreEqual("true", Answers("('a', 1) = 1", "1.0"));
            Assert.AreEqual("true", Answers("('1', 'b') < 2", "1.0"));
        }

        /// <summary>
        /// Bytes allocated by one transformation of a stylesheet that evaluates a test once for each of a
        /// thousand items, once the transformation has been run enough to have settled.
        /// </summary>
        private static long BytesToEvaluateAThousandTimes(string test, string version, XsltBackend backend)
        {
            string stylesheet = Sheet(
                version,
                $"<xsl:for-each select=\"1 to 1000\"><xsl:if test=\"{test}\">x</xsl:if></xsl:for-each>");

            Xslt transform = new Xslt(stylesheet, new XsltOptions { Backend = backend, Version = XsltVersion.V30 });

            for (int i = 0; i < 20; i++)
            {
                transform.TransformXml(Source);
            }

            long best = long.MaxValue;

            for (int round = 0; round < 5; round++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                transform.TransformXml(Source);
                best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            return best;
        }

        [TestMethod]
        public void ComparingTwoAtomicValuesAllocatesNothing()
        {
            // Measured against the same loop with a test that compares nothing and writes nothing, so that
            // what the loop and the source cost is taken out; what is left over is the 'x' a true test
            // writes, two bytes of the result at most. It was 416 bytes for each of these from 2.0 on,
            // and 24 for the ordering under 1.0, whose closure was made on every call.
            string[] tests = { "position() = 1", "position() != last()", "position() &lt; 10", "$five = 5", ". mod 2 = 0" };

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                foreach (string version in new[] { "1.0", "2.0", "3.0" })
                {
                    long floor = BytesToEvaluateAThousandTimes("false()", version, backend);

                    foreach (string test in tests)
                    {
                        long each = (BytesToEvaluateAThousandTimes(test, version, backend) - floor) / 1000;

                        Assert.IsLessThan(
                            8,
                            each,
                            $"'{test}' allocates {each} bytes an evaluation at {version} on the {backend} backend");
                    }
                }
            }
        }
    }
}
