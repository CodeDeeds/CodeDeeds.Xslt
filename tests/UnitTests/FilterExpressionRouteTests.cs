namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that a filter expression — <c>$n[1]</c>, <c>(2, 5)[1]</c> — is read as whatever it turns out
    /// to be, under backwards-compatible behaviour as much as without.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 1.0 let nothing but a node-set be filtered, and a filter expression once promised a node-set
    /// wherever the 1.0 behaviour was on. Whatever reads a node list directly took it at its word. But a
    /// <c>version="1.0"</c> stylesheet on this processor can filter a sequence of numbers with the mode
    /// still on, and then a predicate whose value was the number 2 was asked whether it had found a node,
    /// said yes, and kept every candidate where it names the second; and <c>count()</c> refused the
    /// sequence outright.
    /// </para>
    /// <para>
    /// The mode changes none of this, so every case is asked at 1.0, 2.0 and 3.0 on both backends and
    /// held to one answer, apart from the one the mode does change: a function given several nodes
    /// where it wants a string.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class FilterExpressionRouteTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Four x under r, numbered by their text, and two more under a g of their own.</summary>
        private const string Input =
            "<r><x id='a' k='1'>1</x><x id='b'>2</x><x id='c' k='1'>3</x><x id='d'>4</x>"
            + "<g><x id='e' k='1'>5</x><x id='f'>6</x></g></r>";

        /// <summary>A number, a sequence of numbers, some nodes and no nodes.</summary>
        private const string Variables =
            "<xsl:variable name=\"n\" select=\"2\"/><xsl:variable name=\"s\" select=\"(3, 1, 2)\"/>"
            + "<xsl:variable name=\"nodes\" select=\"/r/x\"/><xsl:variable name=\"none\" select=\"/r/none\"/>";

        private static readonly string[] s_versions = { "1.0", "2.0", "3.0" };

        private static string Sheet(string version, string templates)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:output method=\"text\"/>"
                + Variables
                + templates
                + "</xsl:stylesheet>";
        }

        private static string OnBoth(string stylesheet, string what)
        {
            string? answer = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                XsltOptions options = new XsltOptions { Backend = backend, Version = XsltVersion.V30 };
                string result = new Xslt(stylesheet, options).TransformXml(Input);

                Assert.AreEqual(answer ?? result, result, $"{backend}: {what}");
                answer = result;
            }

            return answer ?? string.Empty;
        }

        /// <summary>The ids of the x an expression selects, on both backends.</summary>
        private static string Selected(string expression, string version)
        {
            return OnBoth(
                Sheet(
                    version,
                    $"<xsl:template match=\"/\"><xsl:for-each select=\"{expression}\">"
                    + "<xsl:value-of select=\"@id\"/></xsl:for-each></xsl:template>"),
                $"{expression} at {version}");
        }

        /// <summary>What an expression writes, on both backends.</summary>
        private static string Written(string expression, string version)
        {
            return OnBoth(
                Sheet(version, $"<xsl:template match=\"/\"><xsl:value-of select=\"{expression}\"/></xsl:template>"),
                $"{expression} at {version}");
        }

        /// <summary>The ids of the x a pattern matches, and a dash for each it does not, on both backends.</summary>
        private static string Matched(string pattern, string version)
        {
            return OnBoth(
                Sheet(
                    version,
                    "<xsl:template match=\"/\"><xsl:apply-templates select=\"//x\"/></xsl:template>"
                    + $"<xsl:template match=\"{pattern}\" priority=\"2\"><xsl:value-of select=\"@id\"/></xsl:template>"
                    + "<xsl:template match=\"x\" priority=\"1\">-</xsl:template>"),
                $"match=\"{pattern}\" at {version}");
        }

        /// <summary>The code an expression is refused with, on both backends, which have to refuse it alike.</summary>
        private static string Refused(string expression, string version)
        {
            string stylesheet = Sheet(
                version, $"<xsl:template match=\"/\"><xsl:value-of select=\"{expression}\"/></xsl:template>");

            string? code = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                XsltOptions options = new XsltOptions { Backend = backend, Version = XsltVersion.V30 };

                XsltException refused = Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(stylesheet, options).TransformXml(Input),
                    $"{backend} answered {expression} at {version} where it should have refused it");

                Assert.AreEqual(code ?? refused.Code, refused.Code, $"{backend}: {expression}: {refused.Message}");
                code = refused.Code;
            }

            return code ?? string.Empty;
        }

        [TestMethod]
        public void AFilterThatIsANumberSelectsByPositionWhereverItIsAPredicate()
        {
            foreach (string version in s_versions)
            {
                // Along a step, where the candidates of each parent are filtered where they stand.
                Assert.AreEqual("b", Selected("r/x[(2, 5)[1]]", version));
                Assert.AreEqual("b", Selected("r/x[$n[1]]", version));
                Assert.AreEqual("b", Selected("r/x[$s[3]]", version));
                Assert.AreEqual("a", Selected("r/x[$s[2]]", version));

                // Under '//', which may be read as one descendant step only where the predicate could
                // not be a position: the second of each parent, not the second x of the document.
                Assert.AreEqual("bf", Selected("//x[(2, 5)[1]]", version));
                Assert.AreEqual("bf", Selected("//x[$n[1]]", version));

                // Over a sequence, in a filter expression of its own.
                Assert.AreEqual("b", Selected("(r/x)[(2, 5)[1]]", version));
                Assert.AreEqual("b", Selected("$nodes[$n[1]]", version));
                Assert.AreEqual("e", Selected("(//x)[$s[1] + $n[1]]", version));

                // And before or after another predicate, each counting within what the last one left.
                Assert.AreEqual("b", Selected("r/x[(2, 5)[1]][1]", version));
                Assert.AreEqual("c", Selected("r/x[@k][(2, 5)[1]]", version));
                Assert.AreEqual(string.Empty, Selected("r/x[(2, 5)[1]][@k]", version));

                // A literal filtered is the literal, which spans no documents: the one filter a 1.0
                // comparison read as nodes outright, and refused.
                Assert.AreEqual("c", Selected("r/x[3[1]]", version));
                Assert.AreEqual("true", Written("3[1] = 3", version));
                Assert.AreEqual("1", Written("count(3[1])", version));

                // Two numbers are still no boolean, and no position either.
                Assert.AreEqual("FORG0006", Refused("count(r/x[$s[. &gt; 1]])", version));
            }
        }

        [TestMethod]
        public void AFilterThatIsNodesKeepsACandidateByFindingOne()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("abcd", Selected("r/x[$nodes[@k]]", version));
                Assert.AreEqual(string.Empty, Selected("r/x[$none[1]]", version));
                Assert.AreEqual("abcdef", Selected("//x[$nodes[@k]]", version));
                Assert.AreEqual(string.Empty, Selected("//x[$none[1]]", version));
                Assert.AreEqual("ac", Selected("r/x[$nodes[@k][@id = current()/r/x[@k]/@id]][@k]", version));

                // A node is a node whatever its text says: the second x reads 2 and is no position.
                Assert.AreEqual("abcd", Selected("r/x[$nodes[2]]", version));
                Assert.AreEqual("ace", Selected("(r/x | r/g/x)[@k]", version));
                Assert.AreEqual("c", Selected("(r/x | r/g/x)[@k][2]", version));
                Assert.AreEqual("c", Selected("$nodes[@k][last()]", version));
            }
        }

        [TestMethod]
        public void AFilteredSequenceIsCountedAndSummedAsWhatItIs()
        {
            foreach (string version in s_versions)
            {
                // The two that were refused at 1.0: nodes were required, and these are numbers.
                Assert.AreEqual("2", Written("count((3, 1, 2)[. &gt; 1])", version));
                Assert.AreEqual("2", Written("count($s[. &gt; 1])", version));

                Assert.AreEqual("2", Written("count($nodes[@k])", version));
                Assert.AreEqual("0", Written("count($none[1])", version));
                Assert.AreEqual("1", Written("count($nodes[$n[1]])", version));
                Assert.AreEqual("5", Written("sum((3, 1, 2)[. &gt; 1])", version));
                Assert.AreEqual("4", Written("sum($nodes[@k])", version));
            }
        }

        [TestMethod]
        public void AFilteredNodeSetIsReadAsItWas()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("2", Written("$nodes[2]", version));
                Assert.AreEqual("1", Written("(3, 1, 2)[2]", version));
                Assert.AreEqual("true", Written("$nodes[2] = 2", version));
                Assert.AreEqual("true", Written("$nodes[@k] = 3", version));
                Assert.AreEqual("false", Written("$nodes[@k] = 2", version));
                Assert.AreEqual("true", Written("r/x = $s[1]", version));
                Assert.AreEqual("true", Written("(3, 1, 2)[. &gt; 1] = 2", version));
                Assert.AreEqual("true", Written("generate-id($nodes[2]) = generate-id(r/x[2])", version));
                Assert.AreEqual("2.0", Written("format-number($nodes[2], '0.0')", version));
                Assert.AreEqual("3.0", Written("format-number($s[1], '0.0')", version));
                Assert.AreEqual("x", Written("name($nodes[3])", version));
                Assert.AreEqual("true", Written("boolean($s[1])", version));
                Assert.AreEqual("true", Written("not($none[1])", version));
                Assert.AreEqual("4", Written("$s[1] + 1", version));

                // A number has no identifier to generate, filtered or not.
                Assert.AreEqual("XPTY0004", Refused("generate-id((3, 1, 2)[1])", version));
            }

            // The one thing here the mode does change: several nodes where a string is wanted are the
            // first of them under 1.0, and a type error after it.
            Assert.AreEqual("1", Written("string-length($nodes[@k])", "1.0"));
            Assert.AreEqual("XPTY0004", Refused("string-length($nodes[@k])", "2.0"));
            Assert.AreEqual("XPTY0004", Refused("string-length($nodes[@k])", "3.0"));
        }

        [TestMethod]
        public void APatternCountsAgainAmongWhatANumericFilterLeft()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("-b---f", Matched("x[(2, 5)[1]]", version));

                // The second x is the only one the first predicate leaves, so it is the first of those.
                // Counting again asked the filter for a boolean and kept every x, the second among them.
                Assert.AreEqual("-b---f", Matched("x[(2, 5)[1]][1]", version));
                Assert.AreEqual("------", Matched("x[(2, 5)[1]][2]", version));
                Assert.AreEqual("-b---f", Matched("x[$n[1]][last()]", version));
                Assert.AreEqual("--c---", Matched("x[@k][(2, 5)[1]]", version));
                Assert.AreEqual("-b---f", Matched("x[$nodes[@k]][2]", version));
            }
        }
    }
}
