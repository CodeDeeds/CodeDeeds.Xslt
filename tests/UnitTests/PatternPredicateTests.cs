namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that a predicate in a match pattern keeps what it kept whichever way it is asked: for a
    /// boolean outright where it can never be a number, and for its value where it might be one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pattern settles, when it is built, which of its predicates could answer with the number that
    /// selects by position. Those are evaluated and looked at. The rest — a path, a comparison,
    /// <c>and</c>, <c>not()</c> — are asked for their effective boolean value and never make the value it
    /// would have been taken from, so <c>product[@type]</c> builds no node-set for each product.
    /// </para>
    /// <para>
    /// Each case is asked on both backends, under each version named, and twice over: as
    /// <c>match="x[P]"</c> and as <c>select="//x[P]"</c>, which reaches the predicate by another road and
    /// counts positions within each parent as the pattern does. The two have to select the same nodes,
    /// and those have to be the ones written here.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class PatternPredicateTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Four x and a y under r, and two more x under a g of their own.</summary>
        private const string Input =
            "<r><x id='a' a='c' p='5'><n>1</n></x><x id='b' p='0'><n>2</n></x><y id='y'/>"
            + "<x id='c' a='c' p='7'><n>3</n><m/></x><x id='d' a='d'/>"
            + "<g><x id='e' a='c'><n>9</n></x><x id='f'/></g></r>";

        private static readonly string[] EveryVersion = { "1.0", "2.0", "3.0" };

        private static readonly string[] TwoAndThree = { "2.0", "3.0" };

        private static string Sheet(string version, string declarations, string templates)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" exclude-result-prefixes=\"xs f\">"
                + "<xsl:output method=\"text\"/>"
                + declarations
                + templates
                + "</xsl:stylesheet>";
        }

        /// <summary>A stylesheet applying templates to <paramref name="select"/>, one rule writing ids.</summary>
        private static string Matching(string version, string declarations, string select, string pattern)
        {
            return Sheet(
                version,
                declarations,
                $"<xsl:template match=\"/\"><xsl:apply-templates select=\"{select}\"/></xsl:template>"
                + $"<xsl:template match=\"{pattern}\" priority=\"2\"><xsl:value-of select=\"@id\"/><xsl:text> </xsl:text></xsl:template>"
                + "<xsl:template match=\"*\" priority=\"1\"/>");
        }

        private static string OnBoth(string stylesheet, XsltVersion processor, string what)
        {
            string? answer = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                XsltOptions options = new XsltOptions { Backend = backend, Version = processor };
                string result = new Xslt(stylesheet, options).TransformXml(Input);

                Assert.AreEqual(answer ?? result, result, $"{backend}: {what}");
                answer = result;
            }

            return answer ?? string.Empty;
        }

        /// <summary>
        /// Requires <c>match="x[…]"</c> to match the x whose ids are given, on both backends — and
        /// <c>select="//x[…]"</c> to select the same ones, unless the two are known to mean different
        /// things.
        /// </summary>
        /// <param name="expected">The ids, each followed by a space.</param>
        /// <param name="predicates">The predicates as written, brackets included.</param>
        /// <param name="version">The stylesheet's version.</param>
        /// <param name="declarations">Anything the predicates read.</param>
        /// <param name="alsoSelected">Whether the same predicates in a path are held to the same answer.</param>
        private static void Matches(
            string expected,
            string predicates,
            string version,
            string declarations = "",
            bool alsoSelected = true)
        {
            string what = $"x{predicates} at {version}";

            Assert.AreEqual(
                expected,
                OnBoth(Matching(version, declarations, "//x", "x" + predicates), XsltVersion.V30, what),
                "match: " + what);

            if (!alsoSelected)
            {
                return;
            }

            string selecting = Sheet(
                version,
                declarations,
                $"<xsl:template match=\"/\"><xsl:for-each select=\"//x{predicates}\">"
                + "<xsl:value-of select=\"@id\"/><xsl:text> </xsl:text></xsl:for-each></xsl:template>");

            Assert.AreEqual(expected, OnBoth(selecting, XsltVersion.V30, what), "select: " + what);
        }

        [TestMethod]
        public void ABarePathKeepsTheCandidateItFindsANodeFrom()
        {
            foreach (string version in EveryVersion)
            {
                Matches("a c d e ", "[@a]", version);
                Matches("a b c e ", "[n]", version);
                Matches("c ", "[m]", version);
                Matches(string.Empty, "[@none]", version);
                Matches(string.Empty, "[none/n]", version);
                Matches("a b c e ", "[n | m]", version);
                Matches("a c d e ", "[@a | none]", version);
                Matches("a b c d e f ", "[..]", version);
                Matches("a b c d ", "[../y]", version);
                Matches("a c e ", "[self::x[@a]/n]", version);
                Matches("a b c e ", "[n/text()]", version);
                Matches("a b c d e f ", "[/r]", version);
                Matches("b c ", "[n[. &gt; 1][../@p]]", version);
            }
        }

        [TestMethod]
        public void ABooleanPredicateKeepsTheCandidateItIsTrueOf()
        {
            foreach (string version in EveryVersion)
            {
                Matches("a c ", "[@a and @p &gt; 1]", version);
                Matches("b c ", "[n &gt; 1 and @p]", version);
                Matches("b f ", "[not(@a)]", version);
                Matches("a b c e ", "[@a = 'c' or n = 2]", version);
                Matches("d ", "[@a != 'c']", version);
                Matches("a ", "[contains(@id, 'a')]", version);
                Matches("a c d e ", "[boolean(@a)]", version);
                Matches("a b c d e f ", "[true()]", version);
                Matches(string.Empty, "[false()]", version);
                Matches("c ", "[(@a and m) or (not(@a) and @none)]", version);
            }

            foreach (string version in TwoAndThree)
            {
                Matches("c ", "[@p eq '7']", version);
                Matches("a b c d e f ", "[. instance of element(x)]", version);
                Matches("c e ", "[some $v in n satisfies $v &gt; 2]", version);
                Matches("a b d f ", "[every $v in n satisfies $v &lt; 3]", version);
                Matches("a b c e ", "[n[1] is n[last()]]", version);
            }
        }

        [TestMethod]
        public void ANumberStillSelectsByPosition()
        {
            const string Two = "<xsl:variable name=\"n\" select=\"2\"/>";
            const string AString = "<xsl:variable name=\"n\" select=\"'2'\"/>";
            const string Nodes = "<xsl:variable name=\"n\" select=\"/r/y\"/>";
            const string Nothing = "<xsl:variable name=\"n\" select=\"/r/none\"/>";

            foreach (string version in EveryVersion)
            {
                Matches("b f ", "[2]", version);
                Matches("d f ", "[last()]", version);
                Matches("c e ", "[last() - 1]", version);
                Matches("b ", "[count(n) + 1]", version);
                Matches("a c ", "[@p - 4]", version);

                // What a variable holds is not known until it is asked: a number is a position, and
                // anything else is whatever it is as a boolean.
                Matches("b f ", "[$n]", version, Two);
                Matches("a b c d e f ", "[$n]", version, AString);
                Matches("a b c d e f ", "[$n]", version, Nodes);
                Matches(string.Empty, "[$n]", version, Nothing);
            }

            foreach (string version in TwoAndThree)
            {
                // A sequence of one number is that number, and a path ending in a number answers with one.
                Matches("a e ", "[1 to 1]", version);
                Matches("a b c ", "[n/xs:integer(.)]", version);
                Matches("b f ", "[if (@a) then 0 else 2]", version);
            }
        }

        [TestMethod]
        public void ABooleanThatReadsThePositionIsAskedAtTheRightOne()
        {
            foreach (string version in EveryVersion)
            {
                Matches("b c d f ", "[position() &gt; 1]", version);
                Matches("d f ", "[position() = last()]", version);
                Matches("c d ", "[position() &gt; 1 and @a]", version);
                Matches("a c d e ", "[not(position() = 2)]", version);
                Matches("a b c e ", "[n = position() or position() = 1]", version);
            }
        }

        [TestMethod]
        public void SeveralPredicatesEachCountWithinWhatTheOnesBeforeLeft()
        {
            foreach (string version in EveryVersion)
            {
                Matches("c ", "[@a='c'][2]", version);
                Matches(string.Empty, "[2][@a]", version);
                Matches("b ", "[2][n]", version);
                Matches("c ", "[@a][n][2]", version);
                Matches("c ", "[position() &gt; 1][@a][1]", version);
                Matches("d e ", "[@a][last()]", version);
                Matches("a c e ", "[@a][n]", version);
                Matches("b c ", "[n][not(@a = 'd')][position() = 2 or m][@p]", version);
            }
        }

        [TestMethod]
        public void UnderTheOlderRulesAFilterAndTheCurrentItemAreStillLookedAt()
        {
            // Under version="1.0" a filter expression once promised a node-set, which is all XPath 1.0
            // let it be, and this processor lets a 1.0 stylesheet filter numbers: the filter's value here
            // is the number 2, which selects the second x. Taken at its word it was asked for a boolean
            // and kept every x, in a path and wherever a pattern counted again between two predicates. A
            // pattern never took the promise for its own test, and nothing makes it now, so the path is
            // held to the same answer.
            foreach (string version in EveryVersion)
            {
                Matches("b f ", "[(2, 5)[1]]", version);
                Matches("b f ", "[$n[1]]", version, "<xsl:variable name=\"n\" select=\"2\"/>");
                Matches("b f ", "[(2, 5)[1]][1]", version);
                Matches("c ", "[@a][(2, 5)[1]]", version);
                Matches("a b c e ", "[(n | m)[1]]", version);
            }

            foreach (string version in TwoAndThree)
            {
                Matches("a b c e ", "[(n, m)[1]]", version);
            }

            foreach (string version in EveryVersion)
            {
                // In a pattern current() is the node being matched, which a path has no word for.
                Matches("a c d e ", "[current()/@a]", version, alsoSelected: false);
                Matches("a b c d e f ", "[current()]", version, alsoSelected: false);
                Matches("a b c e ", "[n = current()/n]", version, alsoSelected: false);
            }
        }

        /// <summary>What matching a sequence of items against <c>.[…]</c> writes, on both backends.</summary>
        private static string Items(string select, string predicates, string version, string declarations = "")
        {
            // The rule for the document node is put above the one that takes every item, the document
            // node being an item.
            string stylesheet = Sheet(
                version,
                declarations,
                $"<xsl:template match=\"/\" priority=\"3\"><xsl:apply-templates select=\"{select}\"/></xsl:template>"
                + $"<xsl:template match=\".{predicates}\" priority=\"2\">[<xsl:value-of select=\"if (. instance of node()) then @id else .\"/>]</xsl:template>"
                + "<xsl:template match=\".\" priority=\"1\">-</xsl:template>");

            return OnBoth(stylesheet, XsltVersion.V30, $".{predicates} over {select} at {version}");
        }

        [TestMethod]
        public void AnItemPatternAsksItsPredicatesTheSameWay()
        {
            const string Two = "<xsl:variable name=\"n\" select=\"2\"/>";
            const string One = "<xsl:variable name=\"n\" select=\"1\"/>";

            foreach (string version in EveryVersion)
            {
                // An item is the first of the one there is, so a number other than 1 matches nothing,
                // where reading 2 as true would have matched everything.
                Assert.AreEqual("---", Items("(1, 2, 'x')", "[$n]", version, Two));
                Assert.AreEqual("[1][2][x]", Items("(1, 2, 'x')", "[$n]", version, One));
                Assert.AreEqual("---", Items("(1, 2, 'x')", "[2]", version));
                Assert.AreEqual("[1][2][x]", Items("(1, 2, 'x')", "[last()]", version));

                Assert.AreEqual("-[2][3]", Items("(1, 2, 3)", "[. &gt; 1]", version));
                Assert.AreEqual("-[2]-", Items("(1, 2, 3)", "[. &gt; 1 and . &lt; 3]", version));
                Assert.AreEqual("[1]--", Items("(1, 2, 3)", "[not(. &gt; 1)]", version));
                Assert.AreEqual("[a]-[c][d]", Items("/r/x", "[@a]", version));
                Assert.AreEqual("[a]--[d]", Items("/r/x", "[@a][not(m)]", version));
                Assert.AreEqual("-[b]--", Items("/r/x", "[self::x and n = 2]", version));

                // A step from a number is an error, and an error in a pattern is a non-match: the node
                // among them is still asked, and still answers.
                Assert.AreEqual("-[a]-", Items("(1, /r/x[1], 'x')", "[@a]", version));
                Assert.AreEqual("-[a]-", Items("(1, /r/x[1], 'x')", "[@a and n]", version));

                // The current item is looked at, a number among them being a position like any other.
                Assert.AreEqual("[1]--", Items("(1, 2, 0)", "[current()]", version));
            }
        }

        [TestMethod]
        public void APredicateWithNoBooleanValueMatchesNothingAndStopsNothing()
        {
            // Two atomic values say nothing about a condition, and neither does a date: FORG0006, which
            // in a select stops the transformation and in a pattern means the node does not match. That
            // has to be so whichever way the predicate is asked — on its own it is evaluated, and under
            // an operator it is asked for a boolean by the operator, itself asked for one by the pattern.
            string[] predicates =
            {
                "[(1, 2)]",
                "[xs:date('2020-01-01')]",
                "[@id and (1, 2)]",
                "[not((1, 2))]",
                "[(1, 2) or @id]",
                "[@id = 'a' or ('p', 'q')][true()]",
                "[@id][('p', 'q') and position() &gt; 0]",
            };

            string[] matched = { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "a ", string.Empty };

            foreach ((XsltVersion processor, string version) in new[]
                {
                    (XsltVersion.V20, "2.0"), (XsltVersion.V30, "2.0"), (XsltVersion.V30, "3.0"),
                })
            {
                for (int i = 0; i < predicates.Length; i++)
                {
                    string what = $"x{predicates[i]} at {version} on a {processor.Number} processor";

                    Assert.AreEqual(
                        matched[i],
                        OnBoth(Matching(version, string.Empty, "//x", "x" + predicates[i]), processor, what),
                        what);

                    string selecting = Sheet(
                        version,
                        string.Empty,
                        $"<xsl:template match=\"/\"><xsl:value-of select=\"count(//x{predicates[i]})\"/></xsl:template>");

                    foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                    {
                        XsltOptions options = new XsltOptions { Backend = backend, Version = processor };

                        XsltException refused = Assert.ThrowsExactly<XsltException>(
                            () => new Xslt(selecting, options).TransformXml(Input),
                            $"{backend} selected //{what}, which has no effective boolean value");

                        Assert.AreEqual("FORG0006", refused.Code, $"{backend}: {what}: {refused.Message}");
                    }
                }
            }

            // Where the first operand settles it the second is never asked, in a pattern as anywhere.
            foreach (string version in TwoAndThree)
            {
                Matches("b f ", "[@a or not(@a) or (1, 2)][not(@a and (1, 2))][not(@a)]", version, alsoSelected: false);
                Matches("a c d e ", "[@a or (1, 2)]", version, alsoSelected: false);
            }
        }

        [TestMethod]
        public void AnErrorThePatternMustReportIsReportedHoweverThePredicateIsAsked()
        {
            // A message that terminates is the stylesheet's own decision to stop, and is heard from inside
            // a pattern where other errors mean only that the node does not match. The call is an operand
            // in the first and the whole predicate in the second, which are asked differently.
            const string Stops =
                "<xsl:function name=\"f:stop\" as=\"xs:boolean\"><xsl:param name=\"n\"/>"
                + "<xsl:message terminate=\"yes\">stopped</xsl:message><xsl:sequence select=\"true()\"/></xsl:function>";

            foreach (string version in TwoAndThree)
            {
                foreach (string predicate in new[] { "[@id = 'c' and f:stop(.)]", "[f:stop(.)]" })
                {
                    foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                    {
                        XsltOptions options = new XsltOptions { Backend = backend, Version = XsltVersion.V30 };
                        string stylesheet = Matching(version, Stops, "//x", "x" + predicate);

                        XsltException stopped = Assert.ThrowsExactly<XsltException>(
                            () => new Xslt(stylesheet, options).TransformXml(Input),
                            $"{backend}: x{predicate} at {version}");

                        Assert.AreEqual("XTMM9000", stopped.Code, $"{backend}: x{predicate}: {stopped.Message}");
                    }
                }

                // And one that is never reached stops nothing.
                Matches(string.Empty, "[@none and f:stop(.)]", version, Stops);
            }
        }
    }
}
