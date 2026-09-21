namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that <c>current()</c> is read as the item being processed whatever that item is and whichever
    /// way the expression holding it is reached, under backwards-compatible behaviour as much as without.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XSLT 1.0 had no instruction that made the current item anything but a node, and <c>current()</c>
    /// once promised a node-set wherever the 1.0 behaviour was on. Everything that reads a node list
    /// directly took it at its word: a comparison, a predicate, <c>count()</c>, <c>xsl:value-of</c>,
    /// <c>format-number()</c>, <c>generate-id()</c>. But a <c>version="1.0"</c> stylesheet on this
    /// processor can write <c>xsl:for-each select="(3, 0)"</c> and stand on a number with the mode still
    /// on, and each of those then refused the call as having no current item at all.
    /// </para>
    /// <para>
    /// So each case is written as a value, in an <c>xsl:when</c> and in an <c>xsl:if</c>, run on both
    /// backends at every version, and required to give one answer or one error code.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class CurrentItemRouteTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Catalog = "<catalog>"
            + "<product id=\"a\"><price>10</price></product>"
            + "<product id=\"b\"><price>40</price></product>"
            + "<product><price>9</price></product>"
            + "<product><price>3</price></product>"
            + "</catalog>";

        private static readonly string[] s_versions = { "1.0", "2.0", "3.0" };

        /// <summary>Two numbers being walked, with no context node while they are.</summary>
        private const string OverNumbers = "<xsl:for-each select=\"(3, 0)\">{0}</xsl:for-each>";

        /// <summary>Each product in turn.</summary>
        private const string OverProducts = "<xsl:for-each select=\"product\">{0}</xsl:for-each>";

        /// <summary>The source's root, which a path has to start from where the context item is a number.</summary>
        private const string Root = "<xsl:variable name=\"root\" select=\"/\"/>";

        private static string Sheet(string version, string body)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:output method=\"text\"/>"
                + Root
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
        /// gave: <c>true</c> or <c>false</c> once for each time <paramref name="within"/> comes round.
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
        private static string Asked(string expression, string version, string within)
        {
            return Answers(expression, expression, version, within);
        }

        /// <summary>What an expression writes, on both backends, which have to write the same.</summary>
        private static string Writes(string select, string version, string within)
        {
            string stylesheet = Sheet(
                version, string.Format(within, $"<xsl:value-of select=\"{select}\"/><xsl:text>;</xsl:text>"));

            string interpreted = Run(stylesheet, XsltBackend.Interpreted);

            Assert.AreEqual(interpreted, Run(stylesheet, XsltBackend.Compiled), $"{select} at {version}");
            return interpreted;
        }

        /// <summary>
        /// The code an expression is refused with — as a value and as a test, on both backends, which have
        /// to refuse it alike.
        /// </summary>
        private static string Refused(string expression, string version, string within)
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
        public void ANumberBeingWalkedIsComparedAsTheNumberItIs()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("true;false;", Asked("current() = 3", version, OverNumbers), version);
                Assert.AreEqual("true;false;", Asked("3 = current()", version, OverNumbers), version);
                Assert.AreEqual("false;true;", Asked("current() != 3", version, OverNumbers), version);
                Assert.AreEqual("false;true;", Asked("current() &lt; 2", version, OverNumbers), version);
                Assert.AreEqual("false;true;", Asked("2 &gt; current()", version, OverNumbers), version);
                Assert.AreEqual("true;true;", Asked("current() = current()", version, OverNumbers), version);
                Assert.AreEqual("true;true;", Asked("current() = .", version, OverNumbers), version);
                Assert.AreEqual("true;false;", Asked("current() = 3 and true()", version, OverNumbers), version);
                Assert.AreEqual("false;true;", Asked("not(current() = 3)", version, OverNumbers), version);
            }
        }

        [TestMethod]
        public void ANumberBeingWalkedIsComparedWithNodes()
        {
            // The other operand is a node-set, which under 1.0 rules is the route that reads both sides'
            // nodes from a list: a price of 3 is there to be found, and no price is 0.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("true;false;", Asked("$root//price = current()", version, OverNumbers), version);
                Assert.AreEqual("true;false;", Asked("current() = $root//price", version, OverNumbers), version);
                Assert.AreEqual("false;true;", Asked("current() &lt; $root//price[. &lt; 5]", version, OverNumbers), version);
            }
        }

        [TestMethod]
        public void ANumberBeingWalkedIsComparedWithAStepInsideAPredicate()
        {
            // Inside the predicate there is a context node again, so the step beside current() is a plain
            // one in one document, which is the comparison that reads its nodes from a list. The current
            // item is still the number: one price is 3 and none is 0, three are over 3 and all four over 0.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("1;0;", Writes("count($root//product[price = current()])", version, OverNumbers), version);
                Assert.AreEqual("1;0;", Writes("count($root//product[current() = price])", version, OverNumbers), version);
                Assert.AreEqual("3;4;", Writes("count($root//product[price &gt; current()])", version, OverNumbers), version);
                Assert.AreEqual("3;4;", Writes("count($root//product[current() &lt; price])", version, OverNumbers), version);
                Assert.AreEqual("3;4;", Writes("count($root//product[price != current()])", version, OverNumbers), version);
                Assert.AreEqual("1;0;", Writes("count($root//price[. = current()])", version, OverNumbers), version);
                Assert.AreEqual(
                    "true;false;",
                    Asked("count($root//product[price = current() and not(@id)]) = 1", version, OverNumbers),
                    version);
            }
        }

        [TestMethod]
        public void ANodeBeingProcessedIsIdentifiedAsTheNodeItIs()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual(
                    "true;true;true;true;",
                    Asked("count(//product[generate-id(.) = generate-id(current())]) = 1", version, OverProducts),
                    version);
                Assert.AreEqual(
                    "true;true;true;true;",
                    Asked("generate-id(current()) = generate-id(.)", version, OverProducts),
                    version);
                Assert.AreEqual("10;40;9;3;", Writes("current()", version, OverProducts), version);
            }
        }

        [TestMethod]
        public void ANodeBeingProcessedIsStillComparedAsANode()
        {
            // What the promise was for, and what giving it up must not change: the usual join.
            foreach (string version in s_versions)
            {
                Assert.AreEqual(
                    "true;false;false;false;", Asked("current()/price = 10", version, OverProducts), version);
                Assert.AreEqual(
                    "true;true;true;true;",
                    Asked("count(//product[price = current()/price]) = 1", version, OverProducts),
                    version);
                Assert.AreEqual(
                    "true;true;true;true;",
                    Asked("count(//product[. = current()]) = 1", version, OverProducts),
                    version);
                Assert.AreEqual(
                    "true;true;false;false;",
                    Asked("//product[@id = 'a' or @id = 'b'] = current()", version, OverProducts),
                    version);
                Assert.AreEqual(
                    "false;true;false;false;", Asked("current() &gt; 30", version, OverProducts), version);
            }

            // Two nodes are numbers under 1.0 and strings from 2.0, and a node reached through current()
            // is a node: 10 is not under 9 and '10' is under '9'.
            const string OverTheFirst = "<xsl:for-each select=\"product[1]/price\">{0}</xsl:for-each>";

            Assert.AreEqual("false;", Asked("current() &lt; $root//product[3]/price", "1.0", OverTheFirst));
            Assert.AreEqual("true;", Asked("current() &lt; $root//product[3]/price", "2.0", OverTheFirst));
            Assert.AreEqual("true;", Asked("current() &lt; $root//product[3]/price", "3.0", OverTheFirst));
        }

        [TestMethod]
        public void ANodeBeingProcessedIsComparedWithEachItemOfASequence()
        {
            // The route that reads current() from a list took whatever was beside it for one value,
            // and a range for the string its integers make joined by spaces: a price of 10 was not
            // among 1 to 10 under 1.0, where it was from 2.0 on.
            const string OverTheFirst = "<xsl:for-each select=\"product[1]/price\">{0}</xsl:for-each>";

            foreach (string version in s_versions)
            {
                Assert.AreEqual("true;", Asked("current() = (1 to 10)", version, OverTheFirst), version);
                Assert.AreEqual("true;", Asked("(1 to 10) = current()", version, OverTheFirst), version);
                Assert.AreEqual("false;", Asked("current() = (1 to 9)", version, OverTheFirst), version);
                Assert.AreEqual("true;", Asked("current() &gt; (1 to 3)", version, OverTheFirst), version);
                Assert.AreEqual("false;", Asked("current() = (5 to 1)", version, OverTheFirst), version);
                Assert.AreEqual(
                    "true;false;",
                    Asked("count($root//product[price = (current() to current())]) = 1", version, OverNumbers),
                    version);
            }
        }

        [TestMethod]
        public void ANumberBeingWalkedSelectsByPositionInAPredicate()
        {
            // A predicate that is a number keeps the node at that position, and current() is a number
            // here: 3 keeps the third price and 0 keeps none. Read as a boolean, 3 kept all four.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("9;;", Writes("$root//product[current()]/price", version, OverNumbers), version);
                Assert.AreEqual("9;;", Writes("($root//product)[current()]/price", version, OverNumbers), version);
                Assert.AreEqual(
                    "true;false;", Asked("count($root//product[current()]) = 1", version, OverNumbers), version);
                Assert.AreEqual(
                    "true;false;",
                    Answers("$root//product[current()] and true()", "$root//product[current()]", version, OverNumbers),
                    version);
            }
        }

        [TestMethod]
        public void ANodeBeingProcessedKeepsEveryCandidateInAPredicate()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual(
                    "true;true;true;true;",
                    Asked("count(//product[current()]) = 4", version, OverProducts),
                    version);
            }
        }

        [TestMethod]
        public void ANumberBeingWalkedIsWrittenCountedAndFormatted()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("3;0;", Writes("current()", version, OverNumbers), version);
                Assert.AreEqual("1;1;", Writes("count(current())", version, OverNumbers), version);
                Assert.AreEqual("3.0;0.0;", Writes("format-number(current(), '0.0')", version, OverNumbers), version);
                Assert.AreEqual("4;1;", Writes("current() + 1", version, OverNumbers), version);
                Assert.AreEqual("3;0;", Writes("string(current())", version, OverNumbers), version);
            }
        }

        [TestMethod]
        public void ANumberBeingWalkedIsRefusedWhereANodeIsRequired()
        {
            // There is a current item, so it is not XTDE1360 that refuses it: it is the wrong type of
            // item for what was asked of it.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("XPTY0019", Refused("current()/price = 3", version, OverNumbers), version);
                Assert.AreEqual("XPTY0004", Refused("generate-id(current()) = ''", version, OverNumbers), version);

                // An atomic value has no identity whichever way it arrives. At 1.0 and 2.0, where the
                // argument is not held to its declared type first, these were a null reference and an
                // invalid cast; 3.0 refused them already.
                Assert.AreEqual("XPTY0004", Refused("generate-id(.) = ''", version, OverNumbers), version);
                Assert.AreEqual("XPTY0004", Refused("generate-id(3) = ''", version, OverNumbers), version);
                Assert.AreEqual("XPTY0004", Refused("generate-id('x') = ''", version, OverNumbers), version);
            }
        }
    }
}
