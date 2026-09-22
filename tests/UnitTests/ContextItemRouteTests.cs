namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that <c>.</c> is read as the context item whatever that item is and whichever way the
    /// expression holding it is reached: a node, a number, a string, or nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>.</c> promises nothing about being a node, and says only that it usually is one. A comparison
    /// under 1.0 rules, <c>xsl:value-of</c> and <c>generate-id()</c> act on that by asking it for its node
    /// each time and taking its value where it has none, and the emitted form of a comparison beside a
    /// child step does the same through a helper of its own. None of that may show: what is answered is
    /// what evaluating <c>.</c> as a value answers, errors included.
    /// </para>
    /// <para>
    /// So each case is written as a value, in an <c>xsl:when</c> and in an <c>xsl:if</c>, run on both
    /// backends at every version, and required to give one answer or one error code. The atomic cases
    /// stand beside the node cases they must not be confused with.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class ContextItemRouteTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Catalog = "<catalog>"
            + "<product id=\"a\"><price>10</price><category>x</category></product>"
            + "<product id=\"b\"><price>40</price><category>y</category></product>"
            + "<product><price>9</price><category>x</category></product>"
            + "<product><price>3</price><category/></product>"
            + "</catalog>";

        private static readonly string[] s_versions = { "1.0", "2.0", "3.0" };

        /// <summary>Two numbers being walked, with no context node while they are.</summary>
        private const string OverNumbers = "<xsl:for-each select=\"(3, 0)\">{0}</xsl:for-each>";

        /// <summary>Two strings being walked, the first of them empty.</summary>
        private const string OverStrings = "<xsl:for-each select=\"('', 'x')\">{0}</xsl:for-each>";

        /// <summary>Each price in turn: 10, 40, 9 and 3.</summary>
        private const string OverPrices = "<xsl:for-each select=\"product/price\">{0}</xsl:for-each>";

        /// <summary>Each category in turn: x, y, x and an empty one.</summary>
        private const string OverCategories = "<xsl:for-each select=\"product/category\">{0}</xsl:for-each>";

        /// <summary>Inside a stylesheet function, where there is no focus at all.</summary>
        private const string InAFunction = "<xsl:value-of select=\"f:ask()\"/>";

        private static string Sheet(string version, string body, string declarations = "")
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" xmlns:f=\"urn:f\">"
                + "<xsl:output method=\"text\"/>"
                + "<xsl:variable name=\"root\" select=\"/\"/>"
                + declarations
                + $"<xsl:template match=\"/*\">{body}</xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, XsltBackend backend)
        {
            XsltOptions options = new XsltOptions { Backend = backend, Version = XsltVersion.V30 };

            return new Xslt(stylesheet, options).TransformXml(Catalog);
        }

        /// <summary>The three ways an expression is asked: as a value, in a when, and in an if.</summary>
        private static string[] Bodies(string expression)
        {
            return new[]
            {
                $"<xsl:value-of select=\"{expression}\"/><xsl:text>;</xsl:text>",
                $"<xsl:choose><xsl:when test=\"{expression}\">true</xsl:when>"
                + "<xsl:otherwise>false</xsl:otherwise></xsl:choose><xsl:text>;</xsl:text>",
                $"<xsl:if test=\"{expression}\">true</xsl:if><xsl:if test=\"not({expression})\">false</xsl:if>"
                + "<xsl:text>;</xsl:text>",
            };
        }

        /// <summary>
        /// Asks a boolean expression as a value, in an <c>xsl:when</c> and in an <c>xsl:if</c>, on both
        /// backends, and returns the one answer all of them gave.
        /// </summary>
        private static string Asked(string expression, string version, string within)
        {
            string? answer = null;

            foreach (string body in Bodies(expression))
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
            string? code = null;

            foreach (string body in Bodies(expression))
            {
                foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                {
                    string stylesheet = within == InAFunction
                        ? Sheet(version, within, $"<xsl:function name=\"f:ask\">{body}</xsl:function>")
                        : Sheet(version, string.Format(within, body));

                    XsltException refused = Assert.ThrowsExactly<XsltException>(
                        () => Run(stylesheet, backend),
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
                Assert.AreEqual("true;false;", Asked(". = 3", version, OverNumbers), version);
                Assert.AreEqual("true;false;", Asked("3 = .", version, OverNumbers), version);
                Assert.AreEqual("false;true;", Asked(". != 3", version, OverNumbers), version);
                Assert.AreEqual("false;true;", Asked(". &lt; 2", version, OverNumbers), version);
                Assert.AreEqual("false;true;", Asked("2 &gt; .", version, OverNumbers), version);
                Assert.AreEqual("true;true;", Asked(". = .", version, OverNumbers), version);
                Assert.AreEqual("true;true;", Asked(". = current()", version, OverNumbers), version);
                Assert.AreEqual("true;false;", Asked(". = 3 and true()", version, OverNumbers), version);
                Assert.AreEqual("false;true;", Asked("not(. = 3)", version, OverNumbers), version);

                // Against a sequence each item is an operand, a range as much as one written out.
                Assert.AreEqual("true;false;", Asked(". = (3, 10)", version, OverNumbers), version);
                Assert.AreEqual("true;false;", Asked(". = (1 to 3)", version, OverNumbers), version);

                // And against nodes, a price of 3 being there to be found and none of 0.
                Assert.AreEqual("true;false;", Asked(". = $root//price", version, OverNumbers), version);
                Assert.AreEqual("true;false;", Asked("$root//price = .", version, OverNumbers), version);
            }
        }

        [TestMethod]
        public void AStringBeingWalkedIsComparedAsTheStringItIs()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("false;true;", Asked(". = 'x'", version, OverStrings), version);
                Assert.AreEqual("false;true;", Asked("'x' = .", version, OverStrings), version);
                Assert.AreEqual("true;false;", Asked(". != 'x'", version, OverStrings), version);
                Assert.AreEqual("true;false;", Asked(". = ''", version, OverStrings), version);

                // Against nodes: one category is empty, as the first string is, and two are 'x'.
                Assert.AreEqual("true;true;", Asked(". = $root//category", version, OverStrings), version);
                Assert.AreEqual("false;true;", Asked(". = $root//category[. != '']", version, OverStrings), version);
            }

            // A string beside a number is NaN under 1.0, equal to nothing and ordered against nothing,
            // and a type error from 2.0. Beside a boolean it is its own effective boolean value under
            // 1.0, and a type error again after.
            Assert.AreEqual("false;false;", Asked(". = 3", "1.0", OverStrings));
            Assert.AreEqual("false;false;", Asked(". &lt; 2", "1.0", OverStrings));
            Assert.AreEqual("false;true;", Asked(". = true()", "1.0", OverStrings));

            foreach (string version in new[] { "2.0", "3.0" })
            {
                Assert.AreEqual("XPTY0004", Refused(". = 3", version, OverStrings), version);
                Assert.AreEqual("XPTY0004", Refused(". &lt; 2", version, OverStrings), version);
                Assert.AreEqual("XPTY0004", Refused(". = true()", version, OverStrings), version);
            }
        }

        [TestMethod]
        public void ANodeIsComparedAsTheNodeItIs()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("true;false;false;false;", Asked(". = 10", version, OverPrices), version);
                Assert.AreEqual("true;false;false;false;", Asked("10 = .", version, OverPrices), version);
                Assert.AreEqual("false;true;true;true;", Asked(". != 10", version, OverPrices), version);
                Assert.AreEqual("true;true;false;false;", Asked(". &gt; 9", version, OverPrices), version);
                Assert.AreEqual("true;true;false;false;", Asked("9 &lt; .", version, OverPrices), version);
                Assert.AreEqual("true;false;false;false;", Asked(". = '10'", version, OverPrices), version);
                Assert.AreEqual("true;false;true;false;", Asked(". = 'x'", version, OverCategories), version);
                Assert.AreEqual("false;true;false;true;", Asked(". != 'x'", version, OverCategories), version);
                Assert.AreEqual("false;false;false;true;", Asked(". = ''", version, OverCategories), version);
                Assert.AreEqual("true;true;true;true;", Asked(". = .", version, OverPrices), version);
                Assert.AreEqual("true;true;true;true;", Asked(". = current()", version, OverPrices), version);
                Assert.AreEqual("true;false;true;true;", Asked(". = (1 to 10)", version, OverPrices), version);
                Assert.AreEqual("true;false;false;true;", Asked(". = (3, 10)", version, OverPrices), version);
                Assert.AreEqual("true;false;false;false;", Asked(". = 10 and true()", version, OverPrices), version);

                // Beside a path in the same document, which is two lists under 1.0 rules.
                Assert.AreEqual("true;true;true;true;", Asked(". = ../price", version, OverPrices), version);
                Assert.AreEqual("false;false;false;false;", Asked(". = ../category", version, OverPrices), version);
            }

            // Two nodes are numbers under 1.0 and strings from 2.0: 10 and 40 are not under 9, and '10'
            // and '40' are under '9'.
            Assert.AreEqual("false;false;false;true;", Asked(". &lt; ../../product[3]/price", "1.0", OverPrices));
            Assert.AreEqual("true;true;false;true;", Asked(". &lt; ../../product[3]/price", "2.0", OverPrices));
            Assert.AreEqual("true;true;false;true;", Asked(". &lt; ../../product[3]/price", "3.0", OverPrices));

            // A node-set beside a boolean is whether it has a node, under 1.0, and a node always has.
            Assert.AreEqual("true;true;true;true;", Asked(". = true()", "1.0", OverPrices));
            Assert.AreEqual("false;false;false;false;", Asked("false() = .", "1.0", OverPrices));
        }

        [TestMethod]
        public void ACandidateIsComparedInsideAPredicate()
        {
            // Once for each candidate, which is where the comparison is worth having cheap.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("1;1;", Writes("count($root//price[. = 10])", version, OverNumbers), version);
                Assert.AreEqual("1;1;", Writes("count($root//price[10 = .])", version, OverNumbers), version);
                Assert.AreEqual("2;2;", Writes("count($root//price[. &gt; 9])", version, OverNumbers), version);
                Assert.AreEqual("2;2;", Writes("count($root//category[. = 'x'])", version, OverNumbers), version);
                Assert.AreEqual("2;2;", Writes("count($root//category[. != 'x'])", version, OverNumbers), version);
                Assert.AreEqual("1;1;", Writes("count($root//category[. = ''])", version, OverNumbers), version);
                Assert.AreEqual("1;0;", Writes("count($root//price[. = current()])", version, OverNumbers), version);
                Assert.AreEqual(
                    "2;1;2;1;", Writes("count($root//category[. = current()])", version, OverCategories), version);
                Assert.AreEqual("3;3;", Writes("count($root//price[. = (1 to 10)])", version, OverNumbers), version);

                // And atomic candidates, which the same predicate has to read as what they are. Summed
                // and joined, not counted: count() of a filtered sequence is refused under 1.0 for a
                // reason of its own, which ConformanceNotes records.
                Assert.AreEqual("5;5;", Writes("sum((3, 1, 2)[. &gt; 1])", version, OverNumbers), version);
                Assert.AreEqual("x;x;", Writes("string-join(('', 'x', 'y')[. = 'x'], ',')", version, OverNumbers), version);
                Assert.AreEqual("3;0;", Writes("sum((3, 1, 2)[. = current()])", version, OverNumbers), version);
            }
        }

        [TestMethod]
        public void AStepBesideTheContextItemIsComparedOnBothBackends()
        {
            // The emitted form walks a plain child step itself and hands the other operand to a helper,
            // which asks '.' and current() for a node before it asks them for a value. The last product
            // has an empty category, so its text is its price's and nothing more.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("1;1;", Writes("count($root//product[price = .])", version, OverNumbers), version);
                Assert.AreEqual("1;1;", Writes("count($root//product[. = price])", version, OverNumbers), version);
                Assert.AreEqual("3;3;", Writes("count($root//product[price != .])", version, OverNumbers), version);
                Assert.AreEqual("1;1;", Writes("count($root//product[price &gt;= .])", version, OverNumbers), version);
                Assert.AreEqual("1;1;1;1;", Writes("count($root//product[price = current()])", version, OverPrices), version);
                Assert.AreEqual("1;0;", Writes("count($root//product[price = current()])", version, OverNumbers), version);
                Assert.AreEqual("1;0;", Writes("count($root//product[current() = price])", version, OverNumbers), version);
                Assert.AreEqual("2;1;2;1;", Writes("count($root//product[category = current()])", version, OverCategories), version);
                Assert.AreEqual("1;2;", Writes("count($root//product[category = current()])", version, OverStrings), version);
                Assert.AreEqual("3;4;", Writes("count($root//product[price &gt; current()])", version, OverNumbers), version);
            }
        }

        [TestMethod]
        public void TheContextItemIsWrittenAndIdentifiedAsWhatItIs()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("3;0;", Writes(".", version, OverNumbers), version);
                Assert.AreEqual(";x;", Writes(".", version, OverStrings), version);
                Assert.AreEqual("10;40;9;3;", Writes(".", version, OverPrices), version);
                Assert.AreEqual("x;y;x;;", Writes(".", version, OverCategories), version);
                Assert.AreEqual("4;1;", Writes(". + 1", version, OverNumbers), version);
                Assert.AreEqual("1;1;", Writes("count(.)", version, OverNumbers), version);

                Assert.AreEqual(
                    "true;true;true;true;", Asked("generate-id(.) = generate-id(current())", version, OverPrices), version);
                Assert.AreEqual(
                    "true;true;true;true;", Asked("string-length(generate-id(.)) &gt; 0", version, OverPrices), version);
                Assert.AreEqual(
                    "true;true;true;true;",
                    Asked("count($root//price[generate-id(.) = generate-id(current())]) = 1", version, OverPrices),
                    version);

                // An atomic value has no identity to name.
                Assert.AreEqual("XPTY0004", Refused("generate-id(.) = ''", version, OverNumbers), version);
                Assert.AreEqual("XPTY0004", Refused("generate-id(.) = ''", version, OverStrings), version);
            }
        }

        [TestMethod]
        public void WithNoContextItemItIsRefusedHoweverItIsAsked()
        {
            // Asking '.' for its node first must not answer for it where there is nothing to ask about.
            foreach (string version in s_versions)
            {
                Assert.AreEqual("XPDY0002", Refused(". = 3", version, InAFunction), version);
                Assert.AreEqual("XPDY0002", Refused("'x' = .", version, InAFunction), version);
                Assert.AreEqual("XPDY0002", Refused(". &gt; 3", version, InAFunction), version);
                Assert.AreEqual("XPDY0002", Refused(". = (1 to 3)", version, InAFunction), version);
                Assert.AreEqual("XPDY0002", Refused("string(.) = ''", version, InAFunction), version);
                Assert.AreEqual("XPDY0002", Refused("generate-id(.) = ''", version, InAFunction), version);
                Assert.AreEqual("XPDY0002", Refused("count(.) = 1", version, InAFunction), version);
            }
        }
    }
}
