namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that an expression asked for as a boolean answers what its value would have, errors included —
    /// which is what lets <c>and</c> and <c>or</c> ask their operands that way wherever they are written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An expression has two ways in. <c>Evaluate</c> gives its value, and <c>EvaluateAsBoolean</c> its
    /// effective boolean value without building the value first: a path says whether it found a node and
    /// never makes the node-set. Several expressions answer the second for themselves, and each of those is
    /// a second statement of what the expression means that nothing but a test holds to the first.
    /// </para>
    /// <para>
    /// The second was once reached only from a <c>test</c>, from <c>not()</c> and <c>boolean()</c>, and from
    /// the condition of an <c>if</c>. The operands of <c>and</c> and <c>or</c> take it now, in a
    /// <c>select</c> and in a predicate as much as in a test. So each case here is written as a value and as
    /// a test, run on both backends, and required to give one answer or one error code.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class BooleanRouteTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Catalog = "<catalog>"
            + "<product id=\"a\"><price>10</price></product>"
            + "<product id=\"b\"><price>40</price></product>"
            + "<product><price>9</price></product>"
            + "<product><price>3</price></product>"
            + "</catalog>";

        /// <summary>What the instruction asking stands inside: nothing, the template's own focus.</summary>
        private const string Here = "{0}";

        /// <summary>Two atomic context items, the first false as a boolean and the second true.</summary>
        private const string OverNumbers = "<xsl:for-each select=\"(0, 1)\">{0}</xsl:for-each>";

        /// <summary>Each product in turn.</summary>
        private const string OverProducts = "<xsl:for-each select=\"product\">{0}</xsl:for-each>";

        private static string Sheet(string version, string declarations, string body)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\""
                + " xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\" exclude-result-prefixes=\"xs f map\">"
                + "<xsl:output method=\"text\"/>"
                + declarations
                + $"<xsl:template match=\"/*\">{body}</xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, XsltBackend backend, string? baseOutputUri)
        {
            XsltOptions options = new XsltOptions
            {
                Backend = backend,
                Version = XsltVersion.V30,
                BaseOutputUri = baseOutputUri,
            };

            return new Xslt(stylesheet, options).TransformXml(Catalog);
        }

        /// <summary>
        /// Writes <paramref name="select"/> as a value and asks <paramref name="test"/> of an
        /// <c>xsl:when</c> and an <c>xsl:if</c>, on both backends, and returns the one answer all of them
        /// gave: <c>true</c> or <c>false</c> once for each time <paramref name="within"/> comes round.
        /// </summary>
        /// <remarks>
        /// The two are one expression where the value is a boolean already. Where it is not — a bare path,
        /// a call — the value is written as <c>E and true()</c>, whose operand is then asked as the test
        /// asks the whole of it, and the test as <c>E</c>.
        /// </remarks>
        private static string Answers(
            string select,
            string test,
            string version,
            string within = Here,
            string declarations = "",
            string? baseOutputUri = null)
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
                    string result = Run(
                        Sheet(version, declarations, string.Format(within, body)), backend, baseOutputUri);

                    Assert.AreEqual(answer ?? result, result, $"{backend} at {version}, asked as {body}");
                    answer = result;
                }
            }

            return answer ?? string.Empty;
        }

        /// <summary>As <see cref="Answers"/>, for an expression that is a boolean as it stands.</summary>
        private static string Asked(string expression, string version, string within = Here)
        {
            return Answers(expression, expression, version, within);
        }

        /// <summary>
        /// The code an expression is refused with — as a value and as a test, on both backends, which have
        /// to refuse it alike.
        /// </summary>
        private static string Refuses(
            string select,
            string test,
            string version,
            string within = Here,
            string declarations = "")
        {
            string[] bodies =
            {
                $"<xsl:value-of select=\"{select}\"/>",
                $"<xsl:if test=\"{test}\">true</xsl:if>",
            };

            string? code = null;

            foreach (string body in bodies)
            {
                foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                {
                    XsltException refused = Assert.ThrowsExactly<XsltException>(
                        () => Run(Sheet(version, declarations, string.Format(within, body)), backend, null),
                        $"{backend} answered {body} at {version} where it should have refused it");

                    Assert.AreEqual(code ?? refused.Code, refused.Code, $"{backend}, {body}: {refused.Message}");
                    code = refused.Code;
                }
            }

            return code ?? string.Empty;
        }

        private static string Refused(string expression, string version, string within = Here)
        {
            return Refuses(expression, expression, version, within);
        }

        /// <summary>
        /// Requires a predicate to keep so many of the four products, along a step and over a sequence —
        /// the two places a predicate is asked — and as a value and as a test in each.
        /// </summary>
        private static void Keeps(int expected, string predicate, string version)
        {
            Assert.AreEqual(
                "true;",
                Asked($"count(//product[{predicate}]) = {expected}", version),
                $"//product[{predicate}] at {version}");

            Assert.AreEqual(
                "true;",
                Asked($"count((//product)[{predicate}]) = {expected}", version),
                $"(//product)[{predicate}] at {version}");

            Assert.AreEqual(
                expected == 0 ? "false;" : "true;",
                Answers($"//product[{predicate}] and true()", $"//product[{predicate}]", version),
                $"whether //product[{predicate}] finds anything at {version}");
        }

        [TestMethod]
        public void APathUnderAndOrOrAnswersWhetherItFoundANode()
        {
            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Assert.AreEqual("true;", Asked("product/@id and product/price", version));
                Assert.AreEqual("false;", Asked("product/@none and product/price", version));
                Assert.AreEqual("false;", Asked("product/price and product/@none", version));
                Assert.AreEqual("true;", Asked("product/@none or product/price", version));
                Assert.AreEqual("false;", Asked("product/@none or none", version));
                Assert.AreEqual("true;", Asked("(/) and . and .. and /catalog", version));
                Assert.AreEqual("true;false;false;false;", Asked("@id = 'a' or price/@none", version, OverProducts));
                Assert.AreEqual("true;true;false;false;", Asked("@id and price &gt; 8", version, OverProducts));
                Assert.AreEqual("false;false;false;true;", Asked("not(@id) and not(price &gt; 8)", version, OverProducts));
            }
        }

        [TestMethod]
        public void AnOperatorInAPredicateKeepsWhatItsOperandsSay()
        {
            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Keeps(2, "@id and price", version);
                Keeps(2, "@id and price &gt; 8", version);
                Keeps(1, "@id and price &gt; 30", version);
                Keeps(3, "@none or price &gt; 8", version);
                Keeps(3, "@id or price &lt; 5", version);
                Keeps(1, "not(@id) and price &lt; 5", version);
                Keeps(2, "(@id or price &lt; 5) and price &lt; 20", version);
                Keeps(0, "@id and @none", version);
                Keeps(4, "true() or @none", version);
            }
        }

        [TestMethod]
        public void ABooleanPredicateStillCountsWhereItStands()
        {
            // A predicate known to be a boolean is asked for one and never for a value, and it may still
            // read the position it is asked at; a number still selects by position.
            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Keeps(1, "position() &gt; 1 and @id", version);
                Keeps(2, "position() = last() or @id = 'a'", version);
                Keeps(3, "not(position() = 2)", version);
                Keeps(1, "2", version);
                Keeps(1, "last() - 1", version);
                Keeps(1, "position() = 2", version);

                Assert.AreEqual("true;", Asked("//product[@id and price][2]/@id = 'b'", version));
                Assert.AreEqual("true;", Asked("//product[not(@id)][price &lt; 5 or @id][1]/price = 3", version));
                Assert.AreEqual("true;", Asked("count(product[not(@id) and position() = 3]) = 1", version));
            }

            Assert.AreEqual("true;", Asked("string-join(((1 to 6)[. &gt; 2 and . &lt; 5]) ! string(), ',') = '3,4'", "3.0"));
            Assert.AreEqual("true;", Asked("count((1 to 6)[. mod 2 = 0 or position() = 1]) = 4", "3.0"));
        }

        [TestMethod]
        public void TheSecondOperandIsNotAskedOnceTheFirstHasSettledIt()
        {
            foreach (string version in new[] { "2.0", "3.0" })
            {
                Assert.AreEqual("false;", Asked("product/@none and (1, 2)", version));
                Assert.AreEqual("true;", Asked("product/@id or (1, 2)", version));
                Assert.AreEqual("false;", Asked("false() and xs:date('2020-01-01')", version));
            }
        }

        [TestMethod]
        public void AnOperandWithNoEffectiveBooleanValueIsRefusedHoweverItIsAsked()
        {
            foreach (string version in new[] { "2.0", "3.0" })
            {
                // Two atomic values say nothing about a condition, and neither does a date.
                Assert.AreEqual("FORG0006", Refused("(1, 2) and true()", version));
                Assert.AreEqual("FORG0006", Refused("false() or (1, 2)", version));
                Assert.AreEqual("FORG0006", Refused("product/@id and ('a', 'b')", version));
                Assert.AreEqual("FORG0006", Refused("xs:date('2020-01-01') and true()", version));
                Assert.AreEqual("FORG0006", Refused("not((1, 2)) or true()", version));

                // A path whose last step is a call gives strings, not nodes: four of them here, where
                // asking only whether the path found anything would have answered true.
                Assert.AreEqual("FORG0006", Refused("product/price/string() and true()", version));
                Assert.AreEqual("FORG0006", Refused("true() and product/string(@id)", version));
                Assert.AreEqual("FORG0006", Refused("count(//product[price/string() and (1, 2)]) = 0", version));

                // One string is a string, and none is nothing.
                Assert.AreEqual("true;", Asked("product[2]/price/string() and true()", version));
                Assert.AreEqual("false;", Asked("product/none/string() or false()", version));
                Assert.AreEqual("false;", Asked("product[3]/string(@id) and true()", version));

                // A sequence that begins with a node is true however long it is.
                Assert.AreEqual("true;", Asked("(product, 1, 2) and true()", version));
            }

            Assert.AreEqual("FORG0006", Refused("map{} and true()", "3.0"));
            Assert.AreEqual("FORG0006", Refused("true() and [1]", "3.0"));
            Assert.AreEqual("FORG0006", Refused("false() or string-length#1", "3.0"));
        }

        [TestMethod]
        public void TheContextItemUnderAnOperatorIsReadAsWhatItIs()
        {
            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                // A number is its own effective boolean value, and a node is true for being one.
                Assert.AreEqual("false;true;", Answers(". and true()", ".", version, OverNumbers));
                Assert.AreEqual("false;true;", Answers("false() or .", ".", version, OverNumbers));
                Assert.AreEqual("true;true;true;true;", Answers(". and true()", ".", version, OverProducts));

                // A step from a number has no node to start from.
                Assert.AreEqual("XPTY0020", Refuses("price and true()", "price", version, OverNumbers));
                Assert.AreEqual("XPTY0020", Refuses("false() or @id", "@id", version, OverNumbers));
                Assert.AreEqual("XPTY0020", Refuses("(/) and true()", "/", version, OverNumbers));
            }
        }

        [TestMethod]
        public void AnOperandWithNoContextItemIsRefusedHoweverItIsAsked()
        {
            // Inside a stylesheet function there is no focus at all.
            static string Function(string body) =>
                $"<xsl:function name=\"f:ask\" as=\"xs:boolean\"><xsl:param name=\"n\"/>{body}</xsl:function>";

            foreach (string expression in new[] { "price", ".", "/", "//product" })
            {
                foreach (string version in new[] { "2.0", "3.0" })
                {
                    string asValue = Function($"<xsl:sequence select=\"({expression}) and true()\"/>");
                    string asTest = Function(
                        $"<xsl:choose><xsl:when test=\"{expression}\"><xsl:sequence select=\"true()\"/></xsl:when>"
                        + "<xsl:otherwise><xsl:sequence select=\"false()\"/></xsl:otherwise></xsl:choose>");

                    string byValue = Refuses("f:ask(1)", "f:ask(1)", version, Here, asValue);
                    string byTest = Refuses("f:ask(1)", "f:ask(1)", version, Here, asTest);

                    Assert.AreEqual("XPDY0002", byValue, expression);
                    Assert.AreEqual(byValue, byTest, expression);
                }
            }
        }

        [TestMethod]
        public void TheCurrentItemUnderAnOperatorIsReadAsWhatItIs()
        {
            // Under 1.0 behaviour as much as without: a 1.0 stylesheet on this processor can walk
            // numbers, and current() is then a number with the mode still on.
            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                // An atomic value being walked is the current item as much as a node is. Asked for as a
                // boolean it was refused as though nothing were being processed at all.
                Assert.AreEqual("false;true;", Answers("current() and true()", "current()", version, OverNumbers));
                Assert.AreEqual("false;true;", Answers("false() or current()", "current()", version, OverNumbers));
                Assert.AreEqual("true;false;", Asked("not(current())", version, OverNumbers));
                Assert.AreEqual("true;true;true;true;", Answers("current() and true()", "current()", version, OverProducts));

                Assert.AreEqual(
                    "true;",
                    Answers(
                        "current() and true()",
                        "current()",
                        version,
                        "<xsl:analyze-string select=\"'abc'\" regex=\"b\"><xsl:matching-substring>{0}"
                        + "</xsl:matching-substring></xsl:analyze-string>"));

                // Two strings being walked are each a string, and the empty one is false.
                Assert.AreEqual(
                    "false;true;",
                    Answers(
                        "current() and true()",
                        "current()",
                        version,
                        "<xsl:for-each select=\"('', 'x')\">{0}</xsl:for-each>"));
            }

            const string InAFunction =
                "<xsl:function name=\"f:ask\" as=\"xs:boolean\"><xsl:param name=\"n\"/>"
                + "<xsl:sequence select=\"current() and true()\"/></xsl:function>";

            Assert.AreEqual("XTDE1360", Refuses("f:ask(1)", "f:ask(1)", "3.0", Here, InAFunction));
        }

        [TestMethod]
        public void AFunctionCalledForABooleanIsStillInTemporaryOutputState()
        {
            // A function is evaluated in temporary output state wherever it is called from, so
            // current-output-uri() answers nothing inside one. Called for a boolean the state was not
            // entered, and the function answered with the URI in a test and with nothing in a select.
            const string Declarations =
                "<xsl:function name=\"f:uri\"><xsl:sequence select=\"current-output-uri()\"/></xsl:function>"
                + "<xsl:function name=\"f:node\"><xsl:param name=\"n\"/>"
                + "<xsl:sequence select=\"$n[current-output-uri()]\"/></xsl:function>";

            const string Uri = "file:///out/result.xml";

            Assert.AreEqual("false;", Answers("f:uri() and true()", "f:uri()", "3.0", Here, Declarations, Uri));
            Assert.AreEqual("false;", Answers("false() or f:uri()", "f:uri()", "3.0", Here, Declarations, Uri));
            Assert.AreEqual("true;", Answers("not(f:uri())", "not(f:uri())", "3.0", Here, Declarations, Uri));

            // Asked for as nodes, which is the third way in: a comparison under 1.0 rules reads them so.
            Assert.AreEqual(
                "false;", Answers("f:node(.) = . and true()", "f:node(.) = .", "3.0", Here, Declarations, Uri));

            // Where it is written in the template itself the output is the final one, and it answers.
            Assert.AreEqual(
                "true;",
                Answers("current-output-uri() and true()", "current-output-uri()", "3.0", Here, string.Empty, Uri));
        }

        [TestMethod]
        public void ACallUnderAnOperatorIsMadeThereAndNotHandedBack()
        {
            // The last call of a function body may be handed back for the caller to make in the body's
            // place, and the value returned then is a placeholder. An operand is read where it stands, so
            // a call that is one has to be made there, whichever way the operand is asked.
            const string Declarations =
                "<xsl:function name=\"f:all\" as=\"xs:boolean\"><xsl:param name=\"s\"/>"
                + "<xsl:sequence select=\"if (empty($s)) then true() else ($s[1] &gt; 0 and f:all($s[position() &gt; 1]))\"/>"
                + "</xsl:function>"
                + "<xsl:function name=\"f:any\" as=\"xs:boolean\"><xsl:param name=\"s\"/>"
                + "<xsl:sequence select=\"if (empty($s)) then false() else (f:any($s[position() &gt; 1]) or $s[1] &gt; 0)\"/>"
                + "</xsl:function>"
                + "<xsl:function name=\"f:priced\"><xsl:param name=\"p\"/>"
                + "<xsl:sequence select=\"$p/price[. &gt; 8]\"/></xsl:function>";

            foreach (string version in new[] { "2.0", "3.0" })
            {
                Assert.AreEqual("true;", Answers("f:all((1, 2, 3))", "f:all((1, 2, 3))", version, Here, Declarations));
                Assert.AreEqual("false;", Answers("f:all((1, -2, 3))", "f:all((1, -2, 3))", version, Here, Declarations));
                Assert.AreEqual("true;", Answers("f:any((-1, -2, 3))", "f:any((-1, -2, 3))", version, Here, Declarations));
                Assert.AreEqual("false;", Answers("f:any((-1, -2))", "f:any((-1, -2))", version, Here, Declarations));

                Assert.AreEqual(
                    "true;true;true;false;",
                    Answers("f:priced(.) and true()", "f:priced(.)", version, OverProducts, Declarations));
                Assert.AreEqual(
                    "true;",
                    Answers("count(//product[f:priced(.) and @id]) = 2", "count(//product[f:priced(.) and @id]) = 2",
                        version, Here, Declarations));
            }
        }

        [TestMethod]
        public void AnExpressionThatAnswersForItselfAnswersAsItsValueWould()
        {
            // Each of these says for itself what it is as a boolean rather than leaving it to its value:
            // a conditional, a let, a quantifier, an instance-of, the empty sequence.
            string[] operands =
            {
                "(if (@id) then price[. &gt; 30] else price[. &lt; 5])",
                "(if (price &gt; 9) then @id else ())",
                "(let $p := price return $p[. &gt; 8])",
                "(let $p := price return if ($p &gt; 9) then @id else $p &lt; 5)",
                "(some $p in price satisfies $p &gt; 9)",
                "(every $p in (price, @id) satisfies string-length($p) = 1)",
                "(. instance of element(product))",
                "(@id instance of attribute())",
                "()",
                "(for $p in price return $p[. &gt; 9])",
                "(price ! (. &gt; 9))",
            };

            string[] expected =
            {
                "false;true;false;true;",
                "true;true;false;false;",
                "true;true;true;false;",
                "true;true;false;true;",
                "true;true;false;false;",
                "false;false;true;true;",
                "true;true;true;true;",
                "true;true;false;false;",
                "false;false;false;false;",
                "true;true;false;false;",
                "true;true;false;false;",
            };

            for (int i = 0; i < operands.Length; i++)
            {
                Assert.AreEqual(
                    expected[i],
                    Answers($"{operands[i]} and true()", operands[i], "3.0", OverProducts),
                    operands[i]);

                Assert.AreEqual(
                    expected[i],
                    Answers($"false() or {operands[i]}", operands[i], "3.0", OverProducts),
                    operands[i]);
            }

            // And where a branch has no effective boolean value, taking it is refused either way.
            Assert.AreEqual(
                "FORG0006",
                Refuses("(if (@id) then (1, 2) else 1) and true()", "if (@id) then (1, 2) else 1", "3.0", OverProducts));
            Assert.AreEqual(
                "FORG0006",
                Refuses("(let $p := (price, 1) return reverse($p)) and true()", "let $p := (price, 1) return reverse($p)",
                    "3.0", OverProducts));
        }
    }
}
