namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that a general comparison with a node-set operand gives one answer however it is reached —
    /// as a value in a <c>select</c>, as a boolean in a <c>test</c>, under <c>and</c> and <c>or</c>, inside
    /// <c>not()</c> and <c>boolean()</c>, inside a predicate — and that the answer is the one the version
    /// in force gives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison that tells the versions apart is a relational one between a node and a string.
    /// XPath 1.0 converts both sides to numbers, so <c>price &lt; '5'</c> over a price of 10 is false. From
    /// 2.0 on the node atomizes to <c>xs:untypedAtomic</c>, which takes the type of the string it is
    /// compared with, and <c>"10" &lt; "5"</c> by codepoint is true. Two nodes part ways likewise: numbers
    /// under 1.0, strings from 2.0.
    /// </para>
    /// <para>
    /// A comparison has more than one way in, because the hot ones avoid building a value: an expression
    /// asked for as a boolean takes a route of its own, and so does one the emitted backend writes inline.
    /// Each route has to ask which version it is answering for, and these are the tests that one of them
    /// forgetting to would fail.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class ComparisonRouteTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Price = "<r><price>10</price><limit>9</limit></r>";

        private const string Catalog = "<catalog>"
            + "<product id=\"a\"><price>10</price></product>"
            + "<product id=\"b\"><price>40</price></product>"
            + "<product><price>9</price></product>"
            + "<product><price>3</price></product>"
            + "</catalog>";

        /// <summary>
        /// Writes an expression as a value and asks it as a test, on both backends, and returns the one
        /// answer all four gave.
        /// </summary>
        private static string Answers(string expression, string version, string source = Price)
        {
            return Answers(expression, expression, version, source);
        }

        /// <summary>
        /// As <see cref="Answers(string, string, string)"/>, for a value that is not a boolean: the value
        /// is written by <paramref name="select"/> and <paramref name="test"/> says whether it is the one
        /// returned.
        /// </summary>
        private static string Answers(
            string select,
            string test,
            string version,
            string source,
            string attributes = "",
            IXsltResolver? documents = null,
            string schema = "")
        {
            string stylesheet = Sheet(
                version,
                attributes,
                schema,
                $"<xsl:value-of select=\"{select}\"/>"
                + "<xsl:text>|</xsl:text>"
                + $"<xsl:choose><xsl:when test=\"{test}\">true</xsl:when>"
                + "<xsl:otherwise>false</xsl:otherwise></xsl:choose>"
                + "<xsl:text>|</xsl:text>"
                + $"<xsl:if test=\"{test}\">true</xsl:if>");

            string interpreted = Run(stylesheet, source, XsltBackend.Interpreted, documents, typed: schema.Length != 0);
            string compiled = Run(stylesheet, source, XsltBackend.Compiled, documents, typed: schema.Length != 0);

            Assert.AreEqual(
                interpreted,
                compiled,
                $"the compiled backend disagreed with the interpreter over {select} at {version}");

            string[] parts = interpreted.Split('|');
            Assert.HasCount(3, parts, interpreted);

            if (select == test)
            {
                Assert.AreEqual(parts[0], parts[1], $"xsl:when disagreed with select over {select} at {version}");
            }

            Assert.AreEqual(
                parts[1] == "true" ? "true" : string.Empty,
                parts[2],
                $"xsl:if disagreed with xsl:when over {test} at {version}");

            return select == test ? parts[0] : parts[0] + "," + parts[1];
        }

        private static string Sheet(string version, string attributes, string schema, string body)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" {attributes}>"
                + (schema.Length == 0 ? string.Empty : $"<xsl:import-schema namespace=\"urn:t\">{schema}</xsl:import-schema>")
                + "<xsl:output method=\"text\"/>"
                + $"<xsl:template match=\"/*\">{body}</xsl:template></xsl:stylesheet>";
        }

        private static string Run(
            string stylesheet,
            string source,
            XsltBackend backend,
            IXsltResolver? documents = null,
            bool typed = false)
        {
            XsltOptions options = new XsltOptions
            {
                Backend = backend,
                Version = XsltVersion.V30,
                DocumentResolver = documents,
                SchemaAware = typed,
                InputValidation = typed ? XsltValidation.Strict : XsltValidation.Strip,
            };

            return new Xslt(stylesheet, options).TransformXml(source);
        }

        /// <summary>
        /// The code an expression is refused with — as a value and as a test, on both backends, which
        /// have to refuse it alike. <paramref name="within"/> is what the instruction asking stands
        /// inside, with <c>{0}</c> for its place.
        /// </summary>
        private static string Refuses(string expression, string version, string source, string within = "{0}")
        {
            string[] bodies =
            {
                string.Format(within, $"<xsl:value-of select=\"{expression}\"/>"),
                string.Format(within, $"<xsl:if test=\"{expression}\">true</xsl:if>"),
            };

            string? code = null;

            foreach (string body in bodies)
            {
                foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
                {
                    XsltException refused = Assert.ThrowsExactly<XsltException>(
                        () => Run(Sheet(version, string.Empty, string.Empty, body), source, backend),
                        $"{backend} answered {expression} at {version} where it should have refused it");

                    Assert.AreEqual(code ?? refused.Code, refused.Code, $"{backend}, {body}");
                    code = refused.Code;
                }
            }

            return code ?? string.Empty;
        }

        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_resources = new(StringComparer.Ordinal);

            public MapResolver Add(string name, string text)
            {
                m_resources[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_resources.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        [TestMethod]
        public void ANodeAgainstAStringIsComparedByTheVersionInForce()
        {
            Assert.AreEqual("false", Answers("price &lt; '5'", "1.0"));
            Assert.AreEqual("true", Answers("price &lt; '5'", "2.0"));
            Assert.AreEqual("true", Answers("price &lt; '5'", "3.0"));

            // And the other way about, which no version answers differently from the first.
            Assert.AreEqual("true", Answers("price &gt;= '5'", "1.0"));
            Assert.AreEqual("false", Answers("price &gt;= '5'", "2.0"));
            Assert.AreEqual("false", Answers("price &gt;= '5'", "3.0"));
        }

        [TestMethod]
        public void TheNodeMayBeOnTheRight()
        {
            Assert.AreEqual("false", Answers("'5' &gt; price", "1.0"));
            Assert.AreEqual("true", Answers("'5' &gt; price", "2.0"));
            Assert.AreEqual("true", Answers("'5' &gt; price", "3.0"));
        }

        [TestMethod]
        public void TwoNodesAreNumbersUnderOneAndStringsFromTwo()
        {
            // Neither side is anything but a node, so there is no literal to say what was meant: 10 is
            // not less than 9, and "10" is less than "9".
            Assert.AreEqual("false", Answers("price &lt; limit", "1.0"));
            Assert.AreEqual("true", Answers("price &lt; limit", "2.0"));
            Assert.AreEqual("true", Answers("price &lt; limit", "3.0"));
        }

        [TestMethod]
        public void AComparisonUnderAndAnswersAsItDoesAlone()
        {
            Assert.AreEqual("false", Answers("price &lt; '5' and true()", "1.0"));
            Assert.AreEqual("true", Answers("price &lt; '5' and true()", "2.0"));
            Assert.AreEqual("true", Answers("price &lt; '5' and true()", "3.0"));

            Assert.AreEqual("false", Answers("true() and '5' &gt; price", "1.0"));
            Assert.AreEqual("true", Answers("true() and '5' &gt; price", "2.0"));
            Assert.AreEqual("true", Answers("true() and '5' &gt; price", "3.0"));

            Assert.AreEqual("false", Answers("price &lt; limit and price &lt; '5'", "1.0"));
            Assert.AreEqual("true", Answers("price &lt; limit and price &lt; '5'", "3.0"));
        }

        [TestMethod]
        public void AComparisonUnderOrAnswersAsItDoesAlone()
        {
            Assert.AreEqual("false", Answers("price &lt; '5' or false()", "1.0"));
            Assert.AreEqual("true", Answers("price &lt; '5' or false()", "2.0"));
            Assert.AreEqual("true", Answers("price &lt; '5' or false()", "3.0"));

            Assert.AreEqual("false", Answers("false() or '5' &gt; price", "1.0"));
            Assert.AreEqual("true", Answers("false() or '5' &gt; price", "2.0"));
            Assert.AreEqual("true", Answers("false() or '5' &gt; price", "3.0"));
        }

        [TestMethod]
        public void AComparisonReadAsABooleanByAFunctionAnswersAsItDoesAlone()
        {
            // not() and boolean() ask their argument for a boolean rather than for a value, and so does
            // the condition of an 'if', which only 2.0 and later can write.
            Assert.AreEqual("true", Answers("not(price &lt; '5')", "1.0"));
            Assert.AreEqual("false", Answers("not(price &lt; '5')", "2.0"));
            Assert.AreEqual("false", Answers("not(price &lt; '5')", "3.0"));

            Assert.AreEqual("false", Answers("boolean(price &lt; '5')", "1.0"));
            Assert.AreEqual("true", Answers("boolean(price &lt; '5')", "2.0"));
            Assert.AreEqual("true", Answers("boolean(price &lt; '5')", "3.0"));

            Assert.AreEqual("true", Answers("if (price &lt; '5') then true() else false()", "2.0"));
            Assert.AreEqual("true", Answers("if (price &lt; '5') then true() else false()", "3.0"));
            Assert.AreEqual("true", Answers("some $p in price satisfies $p/../price &lt; '5'", "3.0"));
        }

        [TestMethod]
        public void AComparisonInAPredicateAnswersAsItDoesAlone()
        {
            // As numbers only 3 is under 5; as strings "10", "40" and "3" are and "9" is not.
            Assert.AreEqual("1,true", Count("price &lt; '5'", 1, "1.0"));
            Assert.AreEqual("3,true", Count("price &lt; '5'", 3, "2.0"));
            Assert.AreEqual("3,true", Count("price &lt; '5'", 3, "3.0"));

            Assert.AreEqual("1,true", Count("'5' &gt; price", 1, "1.0"));
            Assert.AreEqual("3,true", Count("'5' &gt; price", 3, "3.0"));
        }

        [TestMethod]
        public void AComparisonUnderAnOperatorInAPredicateAnswersAsItDoesAlone()
        {
            // Only the first two products carry an id, so 'and @id' takes nothing from the string
            // reading and everything from the numeric one.
            Assert.AreEqual("0,true", Count("price &lt; '5' and @id", 0, "1.0"));
            Assert.AreEqual("2,true", Count("price &lt; '5' and @id", 2, "2.0"));
            Assert.AreEqual("2,true", Count("price &lt; '5' and @id", 2, "3.0"));

            Assert.AreEqual("1,true", Count("price &lt; '5' or @none", 1, "1.0"));
            Assert.AreEqual("3,true", Count("price &lt; '5' or @none", 3, "3.0"));

            Assert.AreEqual("3,true", Count("not(price &lt; '5')", 3, "1.0"));
            Assert.AreEqual("1,true", Count("not(price &lt; '5')", 1, "2.0"));
            Assert.AreEqual("1,true", Count("not(price &lt; '5')", 1, "3.0"));
        }

        [TestMethod]
        public void AComparisonWithANumberIsNumericAtEveryVersion()
        {
            // The case the fast routes were written for, which no version answers differently: an
            // untyped node against a number is compared as a double.
            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Assert.AreEqual("false", Answers("price &lt; 5", version));
                Assert.AreEqual("true", Answers("price &gt; 5 and price &lt; 20", version));
                Assert.AreEqual("true", Answers("5 &gt; price or 20 &gt; price", version));
                Assert.AreEqual("2,true", Count("price &gt; 8 and @id", 2, version));
                Assert.AreEqual("1,true", Count("not(price &gt; 8)", 1, version));
                Assert.AreEqual("3,true", Count("price &gt; 8", 3, version));
            }
        }

        [TestMethod]
        public void OneNodeAgainstSeveralIsComparedWithEachOfThem()
        {
            // Two node-sets under 1.0 rules are compared without a set of strings where either side is
            // one node, which is how a join reads: the quantification is over the one node's pairs, and
            // '!=' holds where any of the others differs from it. Prices 10, 40, 9 and 3.
            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Assert.AreEqual("1,true", Count("price = ../product[1]/price", 1, version));
                Assert.AreEqual("1,true", Count("../product[1]/price = price", 1, version));
                Assert.AreEqual("3,true", Count("price != ../product[1]/price", 3, version));
                Assert.AreEqual("3,true", Count("../product[1]/price != price", 3, version));
                Assert.AreEqual("4,true", Count("price != ../product/price", 4, version));
                Assert.AreEqual("4,true", Count("../product/price != price", 4, version));
                Assert.AreEqual("4,true", Count("price = ../product/price", 4, version));
            }

            // Ordered as numbers under 1.0 and as strings from 2.0: 10 and 40 are over 9, and neither
            // '10' nor '40' is over '9'.
            Assert.AreEqual("2,true", Count("price &gt; ../product[3]/price", 2, "1.0"));
            Assert.AreEqual("2,true", Count("../product[3]/price &lt; price", 2, "1.0"));
            Assert.AreEqual("3,true", Count("price &lt; ../product[2]/price", 3, "1.0"));
            Assert.AreEqual("0,true", Count("price &gt; ../product[3]/price", 0, "3.0"));
            Assert.AreEqual("0,true", Count("../product[3]/price &lt; price", 0, "3.0"));
        }

        [TestMethod]
        public void AnUntypedNodeAgainstANumberIsCastToADouble()
        {
            // From 2.0 the node is cast to xs:double, whose lexical space is wider than the grammar of an
            // XPath 1.0 number: an exponent, a leading plus, INF and NaN. The comparison reads a plain
            // number by a shorter way than the cast, and has to answer as the cast does for the rest.
            const string Source = "<r><a>1e1</a><b>+5</b><c> 10 </c><d>INF</d><e>NaN</e><f>-0</f><g>.5</g></r>";

            foreach (string version in new[] { "2.0", "3.0" })
            {
                Assert.AreEqual("true", Answers("a = 10", version, Source));
                Assert.AreEqual("true", Answers("b = 5", version, Source));
                Assert.AreEqual("true", Answers("c = 10", version, Source));
                Assert.AreEqual("true", Answers("c = 10.0 and c &lt; 10.5 and 10.5 &gt; c", version, Source));
                Assert.AreEqual("true", Answers("d &gt; 1000000 and -1000000 &lt; d", version, Source));
                Assert.AreEqual("true", Answers("f = 0 and g = 0.5 and g &lt; 1e0", version, Source));

                // NaN stands in no relation to anything, so '!=' is the only operator that holds of it.
                Assert.AreEqual("true", Answers("e != 1", version, Source));
                Assert.AreEqual("false", Answers("e &lt; 1 or e = 1 or e &gt; 1 or 1 &gt;= e", version, Source));
            }
        }

        [TestMethod]
        public void ANodeThatIsNotANumberIsAnErrorFromTwoAndFalseUnderOne()
        {
            const string Source = "<r><price>abc</price><p>7</p><p>abc</p></r>";

            Assert.AreEqual("false", Answers("price &gt; 5", "1.0", Source));
            Assert.AreEqual("false", Answers("price &gt; 5 and true()", "1.0", Source));

            // A cast that cannot be made is an error and not an answer, in a test as much as in a select:
            // read under 1.0 rules, the test answered false where the select raised this.
            Assert.AreEqual("FORG0001", Refuses("price &gt; 5", "2.0", Source));
            Assert.AreEqual("FORG0001", Refuses("price &gt; 5", "3.0", Source));
            Assert.AreEqual("FORG0001", Refuses("5 &lt; price", "3.0", Source));
            Assert.AreEqual("FORG0001", Refuses("price &gt; 5 and true()", "3.0", Source));
            Assert.AreEqual("FORG0001", Refuses("not(price &gt; 5)", "3.0", Source));

            // The comparison is existential and stops at the first node that satisfies it, so the text
            // after that one is never read as a number at all.
            Assert.AreEqual("true", Answers("p &gt; 5", "3.0", Source));
        }

        [TestMethod]
        public void ANodeAgainstAStringIsComparedByTheCollationInScope()
        {
            const string CaseBlind =
                "default-collation=\"http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive\"";
            const string Source = "<r><name>Widget</name><name>gadget</name></r>";

            Assert.AreEqual("false", Answers("name = 'WIDGET'", "name = 'WIDGET'", "3.0", Source));
            Assert.AreEqual("true", Answers("name = 'WIDGET'", "name = 'WIDGET'", "3.0", Source, CaseBlind));
            Assert.AreEqual("true", Answers("'GADGET' = name", "'GADGET' = name", "3.0", Source, CaseBlind));
            Assert.AreEqual(
                "true", Answers("name = 'WIDGET' and true()", "name = 'WIDGET' and true()", "3.0", Source, CaseBlind));

            // By code point every capital comes before every small letter, so neither name is before 'H'
            // and 'Widget' is before 'a'; with case set aside 'gadget' is before 'H' and nothing before 'a'.
            Assert.AreEqual("false", Answers("name &lt; 'H'", "name &lt; 'H'", "3.0", Source));
            Assert.AreEqual("true", Answers("name &lt; 'H'", "name &lt; 'H'", "3.0", Source, CaseBlind));
            Assert.AreEqual("false", Answers("name &lt; 'a'", "name &lt; 'a'", "3.0", Source, CaseBlind));
            Assert.AreEqual("true", Answers("name &lt; 'a'", "name &lt; 'a'", "3.0", Source));
            Assert.AreEqual("true", Answers("name != 'widget'", "name != 'widget'", "3.0", Source, CaseBlind));
            Assert.AreEqual(
                "false",
                Answers("name[1] != 'widget'", "name[1] != 'widget'", "3.0", Source, CaseBlind));

            // The context node inside a predicate, which is asked for its node and compared as a step's
            // nodes are, by the collation in scope and not by code point.
            Assert.AreEqual("0,true", Answers("count(name[. = 'WIDGET'])", "count(name[. = 'WIDGET']) = 0", "3.0", Source));
            Assert.AreEqual(
                "1,true", Answers("count(name[. = 'WIDGET'])", "count(name[. = 'WIDGET']) = 1", "3.0", Source, CaseBlind));
            Assert.AreEqual(
                "1,true", Answers("count(name['GADGET' = .])", "count(name['GADGET' = .]) = 1", "3.0", Source, CaseBlind));
            Assert.AreEqual(
                "1,true", Answers("count(name[. &lt; 'H'])", "count(name[. &lt; 'H']) = 1", "3.0", Source, CaseBlind));
            Assert.AreEqual("1,true", Answers("count(name[. &lt; 'a'])", "count(name[. &lt; 'a']) = 1", "3.0", Source));
        }

        [TestMethod]
        public void ANodeASchemaTypedIsComparedByItsTypedValue()
        {
            // A validated node is what its type says and not its text: a list is several values, any one
            // of which may be the one that matches, and a date is a date. Read under 1.0 rules in a test,
            // '3 5' was no number at all and neither was '1990-01-02'.
            const string Schema =
                "<xs:schema targetNamespace=\"urn:t\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " elementFormDefault=\"qualified\">"
                + "<xs:element name=\"order\"><xs:complexType><xs:sequence>"
                + "<xs:element name=\"due\" type=\"xs:date\"/>"
                + "<xs:element name=\"sizes\"><xs:simpleType><xs:list itemType=\"xs:int\"/></xs:simpleType></xs:element>"
                + "</xs:sequence></xs:complexType></xs:element></xs:schema>";

            const string Names = "xmlns:t=\"urn:t\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"";
            const string Source = "<order xmlns=\"urn:t\"><due>1990-01-02</due><sizes>3 5</sizes></order>";

            string Typed(string expression) =>
                Answers(expression, expression, "3.0", Source, Names, schema: Schema);

            Assert.AreEqual("true", Typed("t:sizes = 5"));
            Assert.AreEqual("false", Typed("t:sizes = 4"));
            Assert.AreEqual("true", Typed("5 = t:sizes and true()"));
            Assert.AreEqual("false", Typed("not(t:sizes &gt; 4)"));
            Assert.AreEqual("true", Typed("t:due &lt; xs:date('2000-01-01')"));
            Assert.AreEqual("true", Typed("t:due &lt; xs:date('2000-01-01') or false()"));
            Assert.AreEqual("false", Typed("xs:date('2000-01-01') &lt; t:due"));

            // The same nodes as the context item of a predicate, asked for and read by their type.
            Assert.AreEqual("true", Typed("count(t:sizes[. = 5]) = 1"));
            Assert.AreEqual("true", Typed("count(t:sizes[4 = .]) = 0"));
            Assert.AreEqual("true", Typed("count(t:sizes[. &gt; 4]) = 1"));
            Assert.AreEqual("true", Typed("count(t:due[. &lt; xs:date('2000-01-01')]) = 1"));
            Assert.AreEqual("true", Typed("count(t:due[xs:date('2000-01-01') &lt; .]) = 0"));
            Assert.AreEqual("true", Typed("count(t:sizes[. = xs:untypedAtomic('5')]) = 1"));

            // A date beside a string is a pair no comparison is defined for, by the general way and by
            // the route that asks '.' for its node alike.
            Assert.AreEqual(
                "XPTY0004",
                Assert.ThrowsExactly<XsltException>(() => Typed("count(t:due[. = '1990-01-02']) = 0")).Code);
        }

        [TestMethod]
        public void AStepComparedFromAnAtomicContextItemIsRefusedOnBothBackends()
        {
            // The emitted form of a comparison walks the context node's children inline, and inside a
            // for-each over atomic values there is no context node to walk from. It ran off the end of the
            // tree's arrays where the interpreter raised the type error.
            const string OverNumbers = "<xsl:for-each select=\"(1, 2)\">{0}</xsl:for-each>";

            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Assert.AreEqual("XPTY0020", Refuses("price &gt; 5", version, Price, OverNumbers));
                Assert.AreEqual("XPTY0020", Refuses("price &gt; 5 and true()", version, Price, OverNumbers));
                Assert.AreEqual("XPTY0020", Refuses("not(5 &lt; price)", version, Price, OverNumbers));
            }
        }

        [TestMethod]
        public void AnOperandSpanningDocumentsIsReadFromEachOfThem()
        {
            // Two documents of one shape, so a node of the second has the identifier of a node of the
            // first, and reading it from the wrong tree finds a value rather than a fault.
            MapResolver documents = new MapResolver()
                .Add("a.xml", "<d><v>1</v></d>")
                .Add("b.xml", "<d><v>2</v></d>");

            const string Both = "(document('a.xml') | document('b.xml'))//v";
            const string Source = "<r><v>2</v></r>";

            foreach (string version in new[] { "1.0", "2.0", "3.0" })
            {
                Assert.AreEqual("true", Answers($"v = {Both}", $"v = {Both}", version, Source, documents: documents));
                Assert.AreEqual(
                    "false",
                    Answers($"not(v = {Both})", $"not(v = {Both})", version, Source, documents: documents));
                Assert.AreEqual(
                    "true",
                    Answers($"v = {Both} and true()", $"v = {Both} and true()", version, Source, documents: documents));
            }
        }

        /// <summary>
        /// Counts the products a predicate keeps, as a value, and asks in a test whether the count is
        /// the one expected — the predicate being evaluated afresh by each.
        /// </summary>
        private static string Count(string predicate, int expected, string version)
        {
            return Answers(
                $"count(//product[{predicate}])",
                $"count(//product[{predicate}]) = {expected} and "
                + (expected == 0 ? $"not(//product[{predicate}])" : $"//product[{predicate}]"),
                version,
                Catalog);
        }
    }
}
