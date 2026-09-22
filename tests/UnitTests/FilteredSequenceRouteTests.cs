namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that a filter expression is read as what it filtered, whichever way the expression holding it
    /// is reached, under backwards-compatible behaviour as much as without.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 1.0 could filter nothing but a node-set, and a filter expression once promised a node-set
    /// wherever the 1.0 behaviour was on — <see cref="FilterExpressionRouteTests"/> holds the promise
    /// withdrawn. What is held here is every road into a filter after that: what <c>count()</c>, a
    /// comparison, <c>xsl:value-of</c>, <c>format-number()</c> and <c>generate-id()</c> make of one, over
    /// a sequence and over nodes, over the item being walked, and as a predicate — where a filter that is
    /// a number is a position, one that is nodes keeps every candidate or none, and a step folded from
    /// <c>//x</c> counts that position among the candidate's siblings and not across the document.
    /// </para>
    /// <para>
    /// So each case is written as a value, in an <c>xsl:when</c> and in an <c>xsl:if</c>, run on both
    /// backends at every version, and required to give one answer or one error code.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class FilteredSequenceRouteTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Catalog = "<catalog>"
            + "<product id=\"a\"><price>10</price></product>"
            + "<product id=\"b\"><price>40</price></product>"
            + "<product><price>9</price></product>"
            + "<product><price>3</price></product>"
            + "</catalog>";

        private static readonly string[] s_versions = { "1.0", "2.0", "3.0" };

        /// <summary>Evaluated once, on the document element.</summary>
        private const string Once = "{0}";

        /// <summary>Two numbers being walked, with no context node while they are.</summary>
        private const string OverNumbers = "<xsl:for-each select=\"(3, 0)\">{0}</xsl:for-each>";

        /// <summary>Each product in turn.</summary>
        private const string OverProducts = "<xsl:for-each select=\"product\">{0}</xsl:for-each>";

        /// <summary>
        /// What the expressions read: the source's root, a number, the products, and no products at all.
        /// </summary>
        private const string Variables = "<xsl:variable name=\"root\" select=\"/\"/>"
            + "<xsl:variable name=\"n\" select=\"2\"/>"
            + "<xsl:variable name=\"products\" select=\"//product\"/>"
            + "<xsl:variable name=\"none\" select=\"//product[price = 0]\"/>";

        private static string Sheet(string version, string body)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:output method=\"text\"/>"
                + Variables
                + $"<xsl:template match=\"/*\">{body}</xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, XsltBackend backend)
        {
            XsltOptions options = new XsltOptions { Backend = backend, Version = XsltVersion.V30 };

            return new Xslt(stylesheet, options).TransformXml(Catalog);
        }

        /// <summary>
        /// Writes <paramref name="select"/> as a value and asks <paramref name="test"/> of an
        /// <c>xsl:when</c> and an <c>xsl:if</c>, on both backends, and returns the one answer all of them
        /// gave: once for each time <paramref name="within"/> comes round.
        /// </summary>
        private static string Answers(string select, string test, string version, string within)
        {
            string[] bodies =
            {
                $"<xsl:value-of select=\"{select}\"/><xsl:text>;</xsl:text>",
                $"<xsl:choose><xsl:when test=\"{test}\">true</xsl:when>"
                + "<xsl:otherwise>false</xsl:otherwise></xsl:choose><xsl:text>;</xsl:text>",
                $"<xsl:if test=\"{test}\">true</xsl:if><xsl:if test=\"not({test})\">false</xsl:if>"
                + "<xsl:text>;</xsl:text>",
            };

            string? answer = null;

            foreach (string body in bodies)
            {
                foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                {
                    string result = Run(Sheet(version, string.Format(within, body)), backend);

                    Assert.AreEqual(answer ?? result, result, $"{backend} at {version}, asked as {body}");
                    answer = result;
                }
            }

            return answer ?? string.Empty;
        }

        /// <summary>As <see cref="Answers"/>, for an expression that is a boolean as it stands.</summary>
        private static string Asked(string expression, string version, string within = Once)
        {
            return Answers(expression, expression, version, within);
        }

        /// <summary>
        /// What an expression writes, on both backends, which have to write the same — and, where it
        /// writes one thing every time round, what an <c>xsl:when</c> and an <c>xsl:if</c> make of the
        /// same expression, which has to be that thing too.
        /// </summary>
        /// <param name="oneItem">
        /// Whether the expression is one item at most, so that <c>string()</c> can be asked of it.
        /// </param>
        private static string Writes(string select, string version, string within = Once, bool oneItem = true)
        {
            string stylesheet = Sheet(
                version, string.Format(within, $"<xsl:value-of select=\"{select}\"/><xsl:text>;</xsl:text>"));

            string interpreted = Run(stylesheet, XsltBackend.Interpreted);

            Assert.AreEqual(interpreted, Run(stylesheet, XsltBackend.Compiled), $"{select} at {version}");

            // The answer is the same for every time round, so asking whether each is what was written is
            // a row of 'true's, by whichever way the test is reached.
            string[] each = interpreted.Split(';')[..^1];

            if (oneItem && each.Length > 0 && each[0].Length > 0 && Array.TrueForAll(each, one => one == each[0]))
            {
                string all = string.Concat(Enumerable.Repeat("true;", each.Length));

                Assert.AreEqual(all, Asked($"string({select}) = '{each[0]}'", version, within), $"{select} at {version}");
            }

            return interpreted;
        }

        /// <summary>
        /// The code an expression is refused with — as a value and as a test, on both backends, which have
        /// to refuse it alike.
        /// </summary>
        private static string Refused(string expression, string version, string within = Once)
        {
            string[] bodies =
            {
                $"<xsl:value-of select=\"{expression}\"/>",
                $"<xsl:if test=\"{expression}\">true</xsl:if>",
            };

            string? code = null;

            foreach (string body in bodies)
            {
                foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                {
                    XsltException refused = Assert.ThrowsExactly<XsltException>(
                        () => Run(Sheet(version, string.Format(within, body)), backend),
                        $"{backend} answered {body} at {version} where it should have refused it");

                    Assert.AreEqual(code ?? refused.Code, refused.Code, $"{backend}, {body}: {refused.Message}");
                    code = refused.Code;
                }
            }

            return code ?? string.Empty;
        }

        [TestMethod]
        public void AFilteredSequenceIsCounted()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("2;", Writes("count((3, 1, 2)[. &gt; 1])", version), version);
                Assert.AreEqual("true;", Asked("count((3, 1, 2)[. &gt; 1]) = 2", version), version);
                Assert.AreEqual("3;", Writes("count((1 to 5)[. &gt; 2])", version), version);
                Assert.AreEqual("2;", Writes("count(tokenize('a b c', ' ')[. != 'b'])", version), version);
                Assert.AreEqual("1;", Writes("count($n[1])", version), version);
                Assert.AreEqual("0;", Writes("count($n[. = 3])", version), version);
                Assert.AreEqual("0;", Writes("count((1 to 5)[. &gt; 9])", version), version);
            }
        }

        [TestMethod]
        public void AFilteredSequenceIsCompared()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("true;", Asked("(1 to 5)[. &gt; 4] = 5", version), version);
                Assert.AreEqual("true;", Asked("5 = (1 to 5)[. &gt; 4]", version), version);
                Assert.AreEqual("false;", Asked("(1 to 5)[. &gt; 4] != 5", version), version);
                Assert.AreEqual("true;", Asked("(1 to 5)[3] &lt; 4", version), version);
                Assert.AreEqual("false;", Asked("4 &lt; (1 to 5)[3]", version), version);
                Assert.AreEqual("true;", Asked("(1 to 5)[3] = (1 to 5)[. &gt; 2]", version), version);
                Assert.AreEqual("true;", Asked("(3, 1, 2)[2] = 1", version), version);
                Assert.AreEqual("true;", Asked("$n[1] = 2", version), version);
                Assert.AreEqual("true;", Asked("tokenize('a b c', ' ')[2] = 'b'", version), version);
                Assert.AreEqual("false;", Asked("(1 to 5)[. &gt; 9] = 5", version), version);
                Assert.AreEqual("true;", Asked("not((1 to 5)[. &gt; 4] = 4) and true()", version), version);
            }
        }

        [TestMethod]
        public void AFilteredSequenceIsComparedWithNodes()
        {
            // The other operand is a plain path in one document, which under 1.0 rules is the route that
            // reads its nodes from a list and the filter's as whatever it proves to be: there is a price
            // of 40, and none of 41.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("true;", Asked("(1 to 50)[. &gt; 39] = //price", version), version);
                Assert.AreEqual("true;", Asked("//price = (1 to 50)[. &gt; 39]", version), version);
                Assert.AreEqual("false;", Asked("(1 to 50)[. &gt; 40] = //price", version), version);
                Assert.AreEqual("true;", Asked("//price &lt; (1 to 5)[. = 4]", version), version);
                Assert.AreEqual("1;", Writes("count(//product[price = (1 to 50)[. &gt; 39]])", version), version);
                Assert.AreEqual("3;", Writes("count(//product[(1 to 9)[. = 9] &lt;= price])", version), version);
            }
        }

        [TestMethod]
        public void ASequenceIsComparedWithNodesItemByItem()
        {
            // No filter in it, and the same route: nodes read from a list, against a value that turns out
            // to be several. Read as the one string a sequence converts to, no price equalled
            // '40 41 42 ...' under 1.0 behaviour, where a comma sequence, taken another way, was right.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("true;", Asked("(40 to 50) = //price", version), version);
                Assert.AreEqual("true;", Asked("//price = (40 to 50)", version), version);
                Assert.AreEqual("true;", Asked("(41, 40) = //price", version), version);
                Assert.AreEqual("false;", Asked("(41 to 50) = //price", version), version);
                Assert.AreEqual("true;", Asked("//price != (40 to 50)", version), version);
                Assert.AreEqual("true;", Asked("//price &lt; (1 to 4)", version), version);
                Assert.AreEqual("false;", Asked("//price &lt; (1 to 3)", version), version);
                Assert.AreEqual("true;", Asked("(41 to 50) &gt; //price", version), version);
                Assert.AreEqual("1;", Writes("count(//product[price = (40 to 50)])", version), version);
                Assert.AreEqual("1;", Writes("count(//product[(40 to 50) = price])", version), version);
                Assert.AreEqual("1;", Writes("count(//product[price &gt; (10 to 50)])", version), version);
            }
        }

        [TestMethod]
        public void APredicateThatIsOneNumberInASequenceSelectsByPosition()
        {
            // A filter's predicates are applied where a step's are, which has to read one number that
            // arrives as a sequence of one for the position it is.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("b;", Writes("$products[subsequence((1, 2, 3), 2, 1)]/@id", version), version);
                Assert.AreEqual("b;", Writes("(//product)[subsequence((1, 2, 3), 2, 1)]/@id", version), version);
                Assert.AreEqual("b;", Writes("//product[subsequence((1, 2, 3), 2, 1)]/@id", version), version);
                Assert.AreEqual("a;", Writes("$products[1 to 1]/@id", version), version);
                Assert.AreEqual("b;", Writes("$products[reverse((2))]/@id", version), version);
                Assert.AreEqual("b;", Writes("$products[position() = 2][1]/@id", version), version);
                Assert.AreEqual("40;", Writes("$products[last() - 2]/price", version), version);
                Assert.AreEqual("3;", Writes("$products[position() &gt; 1][last()]/price", version), version);
            }
        }

        [TestMethod]
        public void AFilteredSequenceIsWrittenAndFormatted()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("3;", Writes("(1 to 5)[3]", version), version);
                Assert.AreEqual("2;", Writes("$n[1]", version), version);
                Assert.AreEqual("b;", Writes("tokenize('a b c', ' ')[2]", version), version);
                Assert.AreEqual(";", Writes("(1 to 5)[. &gt; 9]", version), version);
                Assert.AreEqual("3.0;", Writes("format-number((1 to 5)[3], '0.0')", version), version);
                Assert.AreEqual("2.0;", Writes("format-number($n[1], '0.0')", version), version);
                Assert.AreEqual("9;", Writes("sum((1 to 5)[. &gt; 3])", version), version);
                Assert.AreEqual("4;", Writes("(1 to 5)[3] + 1", version), version);
            }

            // More than one item is where the versions part: 1.0 writes the first and 2.0 all of them.
            Assert.AreEqual("3;", Writes("(3, 1, 2)[. &gt; 1]", "1.0", oneItem: false));
            Assert.AreEqual("3 2;", Writes("(3, 1, 2)[. &gt; 1]", "2.0", oneItem: false));
            Assert.AreEqual("3 2;", Writes("(3, 1, 2)[. &gt; 1]", "3.0", oneItem: false));
        }

        [TestMethod]
        public void AFilterThatIsANumberSelectsByPositionInAPredicate()
        {
            // A predicate that is a number keeps the node at that position, and a filter over numbers is
            // one: $n is 2 and keeps the second price, the third of one to five is 3 and keeps the third.
            // Read as a boolean, either kept all four, and the first was what got written.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("40;", Writes("//product[$n[1]]/price", version), version);
                Assert.AreEqual("40;", Writes("/catalog/product[$n[1]]/price", version), version);
                Assert.AreEqual("40;", Writes("($root//product)[$n[1]]/price", version), version);
                Assert.AreEqual("40;", Writes("$products[$n[1]]/price", version), version);
                Assert.AreEqual("9;", Writes("//product[(1 to 5)[3]]/price", version), version);
                Assert.AreEqual("40;", Writes("//product[(2)[1]]/price", version), version);
                Assert.AreEqual("1;", Writes("count(//product[(1 to 5)[3]])", version), version);
                Assert.AreEqual("0;", Writes("count(//product[(5 to 9)[1]])", version), version);
                Assert.AreEqual("true;", Asked("count(//product[$n[1]]) = 1", version), version);
                Assert.AreEqual(
                    "true;", Answers("//product[$n[1]]/@id = 'b'", "//product[$n[1]][@id = 'b']", version, Once), version);
            }
        }

        [TestMethod]
        public void AFoldedDescendantStepReadsANumberAsAPositionAmongSiblings()
        {
            // '//x[P]' is walked as one descendant step wherever P reads no position, and a P that turns
            // out to be a number is then held to what the unfolded step counts, the position among the
            // candidate's siblings: every price is the first price of its product, so //price[1] is all
            // four and //price[$n] none. A first predicate that is nodes keeps every product, and a
            // number after it counts among those.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("4;", Writes("count(//price[1])", version), version);
                Assert.AreEqual("0;", Writes("count(//price[$n])", version), version);
                Assert.AreEqual("1;", Writes("count(//product[1])", version), version);
                Assert.AreEqual("b;", Writes("//product[$n]/@id", version), version);
                Assert.AreEqual("b;", Writes("//product[$n][@id]/@id", version), version);
                Assert.AreEqual("0;", Writes("count(//product[$n][not(@id)])", version), version);
                Assert.AreEqual("b;", Writes("//product[number('2')]/@id", version), version);
                Assert.AreEqual("b;", Writes("//product[(2, 5)[1]]/@id", version), version);
                Assert.AreEqual("4;", Writes("count(//product[$products[2]])", version), version);
                Assert.AreEqual("b;", Writes("//product[$products[2]][2]/@id", version), version);
                Assert.AreEqual("b;", Writes("//product[position() = 2]/@id", version), version);
                Assert.AreEqual("1;", Writes("count(//product[last()])", version), version);
                Assert.AreEqual("9;", Writes("//product[$n[1] + 1]/price", version), version);
            }
        }

        [TestMethod]
        public void AFoldedDescendantStepCountsEachParentsChildrenApart()
        {
            // The position a number names is among the candidate's parent's matching children, and the
            // candidates of one parent are interleaved with those of another where the elements nest:
            // a's children stand between a and b. //x[2] is a2 and b, //x[1] is a and a1, //x[3] is c.
            const string Nested = "<r><x id=\"a\"><x id=\"a1\"/><x id=\"a2\"/></x><x id=\"b\"/><x id=\"c\"/></r>";

            foreach (string version in s_versions)
            {
                foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                {
                    string Ids(string select)
                    {
                        string stylesheet = $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\">"
                            + "<xsl:output method=\"text\"/>"
                            + "<xsl:variable name=\"n\" select=\"2\"/>"
                            + "<xsl:template match=\"/\">"
                            + $"<xsl:for-each select=\"{select}\"><xsl:value-of select=\"@id\"/><xsl:text> </xsl:text></xsl:for-each>"
                            + "</xsl:template></xsl:stylesheet>";

                        XsltOptions options = new XsltOptions { Backend = backend, Version = XsltVersion.V30 };

                        return new Xslt(stylesheet, options).TransformXml(Nested);
                    }

                    string where = $"{backend} at {version}";

                    Assert.AreEqual("a2 b ", Ids("//x[2]"), where);
                    Assert.AreEqual("a2 b ", Ids("//x[$n]"), where);
                    Assert.AreEqual("a2 b ", Ids("//x[$n[1]]"), where);
                    Assert.AreEqual("a2 b ", Ids("//x[number('2')]"), where);
                    Assert.AreEqual("a a1 ", Ids("//x[1]"), where);
                    Assert.AreEqual("c ", Ids("//x[3]"), where);
                    Assert.AreEqual("", Ids("//x[4]"), where);
                    Assert.AreEqual("a2 b ", Ids("//x[$n][@id]"), where);
                    Assert.AreEqual("a2 ", Ids("//x[$n][starts-with(@id, 'a')]"), where);
                    Assert.AreEqual("a a1 ", Ids("//x[$n - 1]"), where);
                    Assert.AreEqual("a a1 a2 b c ", Ids("//x[$n - 2 + 1 = 1]"), where);
                    Assert.AreEqual("a2 b ", Ids("descendant-or-self::node()/x[2]"), where);
                    Assert.AreEqual("a1 ", Ids("/descendant::x[2]"), where);
                }
            }
        }

        [TestMethod]
        public void AFilterThatIsNodesKeepsEveryCandidateOrNoneInAPredicate()
        {
            // Nodes are never the number that would make a predicate positional: a filter that finds
            // some keeps every candidate, and one that finds none keeps nothing.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("4;", Writes("count(//product[$products[price = 40]])", version), version);
                Assert.AreEqual("0;", Writes("count(//product[$products[price = 41]])", version), version);
                Assert.AreEqual("0;", Writes("count(//product[$none[1]])", version), version);
                Assert.AreEqual("4;", Writes("count(/catalog/product[$products[2]])", version), version);
                Assert.AreEqual("2;", Writes("count(//product[(@id)[1]])", version), version);
                Assert.AreEqual("2;", Writes("count($products[$products[price = 40]][@id])", version), version);
            }
        }

        [TestMethod]
        public void FilteredNodesAreStillCountedComparedAndWritten()
        {
            // What the promise was for, and what giving it up must not change.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("2;", Writes("count($products[price &gt; 9])", version), version);
                Assert.AreEqual("2;", Writes("count((//product)[price &gt; 9])", version), version);
                Assert.AreEqual("0;", Writes("count($none[price &gt; 9])", version), version);
                Assert.AreEqual("1;", Writes("count($products[price &gt; 9][2])", version), version);
                Assert.AreEqual("1;", Writes("count((//product)[last()])", version), version);
                Assert.AreEqual("true;", Asked("$products[price &gt; 9] = 40", version), version);
                Assert.AreEqual("true;", Asked("40 = $products[price &gt; 9]", version), version);
                Assert.AreEqual("false;", Asked("$products[price &gt; 9] = 9", version), version);
                Assert.AreEqual("true;", Asked("(//product)[price &gt; 9] = 40", version), version);
                Assert.AreEqual("true;", Asked("(//product)[price &gt; 9] = //price", version), version);
                Assert.AreEqual("false;", Asked("(//product)[price &gt; 40] = //price", version), version);
                Assert.AreEqual("true;", Asked("$products[2] = (//product)[2]", version), version);
                Assert.AreEqual("10;", Writes("$products[1]/price", version), version);
                Assert.AreEqual("40;", Writes("$products[2]", version), version);
                Assert.AreEqual("9;", Writes("(//product)[3]", version), version);
                Assert.AreEqual(";", Writes("$none[1]", version), version);
                Assert.AreEqual("40.0;", Writes("format-number($products[2], '0.0')", version), version);
                Assert.AreEqual("9.0;", Writes("format-number((//price)[3], '0.0')", version), version);
                Assert.AreEqual(
                    "true;", Asked("generate-id($products[2]) = generate-id(//product[2])", version), version);
                Assert.AreEqual(
                    "true;", Asked("generate-id((//product)[2]) = generate-id($products[2])", version), version);
                Assert.AreEqual("true;", Asked("generate-id($none[1]) = ''", version), version);
                Assert.AreEqual(
                    "1;", Writes("count(//product[generate-id(.) = generate-id($products[price = 9][1])])", version), version);
            }

            // Two nodes are numbers under 1.0 and strings from 2.0, and a node reached through a filter
            // is a node: 10 is not under 9 and '10' is under '9'.
            Assert.AreEqual("false;", Asked("(//price)[1] &lt; (//price)[3]", "1.0"));
            Assert.AreEqual("true;", Asked("(//price)[1] &lt; (//price)[3]", "2.0"));
            Assert.AreEqual("true;", Asked("(//price)[1] &lt; (//price)[3]", "3.0"));
            Assert.AreEqual("false;", Asked("$products[1]/price &lt; $products[price = 9]/price", "1.0"));
            Assert.AreEqual("true;", Asked("$products[1]/price &lt; $products[price = 9]/price", "3.0"));
        }

        [TestMethod]
        public void TheItemBeingWalkedIsFilteredAsTheItemItIs()
        {
            // The primary is the context item or the current one, which is in one document if it is a
            // node at all: the filter the promise sent down its own route, and refused where the item
            // was a number.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("1;0;", Writes("count(.[. &gt; 1])", version, OverNumbers), version);
                Assert.AreEqual("1;0;", Writes("count(current()[. &gt; 1])", version, OverNumbers), version);
                Assert.AreEqual("true;false;", Asked(".[. &gt; 1] = 3", version, OverNumbers), version);
                Assert.AreEqual("true;false;", Asked("3 = current()[. &gt; 1]", version, OverNumbers), version);
                Assert.AreEqual("3;;", Writes("current()[. &gt; 1]", version, OverNumbers), version);
                Assert.AreEqual("3.0;0.0;", Writes("format-number(.[1], '0.0')", version, OverNumbers), version);

                Assert.AreEqual("1;1;0;0;", Writes("count(.[price &gt; 9])", version, OverProducts), version);
                Assert.AreEqual("1;1;0;0;", Writes("count(current()[price &gt; 9])", version, OverProducts), version);
                Assert.AreEqual(
                    "false;true;false;false;", Asked(".[price &gt; 9] = 40", version, OverProducts), version);
                Assert.AreEqual(
                    "false;true;false;false;", Asked("40 = current()[price &gt; 9]", version, OverProducts), version);
                Assert.AreEqual(
                    "true;true;false;false;",
                    Asked("current()[price &gt; 9] = $root//product[@id]", version, OverProducts),
                    version);
                Assert.AreEqual("10;40;;;", Writes("current()[price &gt; 9]", version, OverProducts), version);
                Assert.AreEqual("10;40;;;", Writes(".[price &gt; 9]/price", version, OverProducts), version);
                Assert.AreEqual(
                    "true;true;true;true;",
                    Asked("generate-id(current()[1]) = generate-id(.)", version, OverProducts),
                    version);
            }
        }

        [TestMethod]
        public void AFilteredSequenceIsRefusedWhereANodeIsRequired()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("XPTY0004", Refused("generate-id((1 to 5)[3]) = ''", version), version);
                Assert.AreEqual("XPTY0004", Refused("generate-id($n[1]) = ''", version), version);
                Assert.AreEqual("XPTY0019", Refused("(1 to 5)[3]/price = 3", version), version);
                Assert.AreEqual("XPTY0004", Refused("count((1 to 5)[3] | //price) = 1", version), version);
            }
        }
    }
}
