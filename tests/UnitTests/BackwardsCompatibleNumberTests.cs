using System.Xml;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that everything a backwards-compatible expression converts to a number is converted by the one
    /// <c>fn:number</c>, which is XPath 2.0's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 2.0 has compatibility mode convert with <c>fn:number</c> in four places: the operands of a
    /// general comparison (§3.5.2), the operands of arithmetic (§3.4), an argument where a number is
    /// expected (§3.1.5), and wherever a stylesheet calls <c>number()</c> or <c>sum()</c> itself. From 2.0
    /// on that function reads the lexical space of <c>xs:double</c> — an exponent, a leading plus,
    /// <c>INF</c> — and Appendix I.1 lists exactly that among the incompatibilities the mode does not
    /// remove, "either explicitly when using the number function, or implicitly". The comparison read it so
    /// and the other three read XPath 1.0's grammar, so over <c>&lt;a&gt;1e1&lt;/a&gt;</c> the test
    /// <c>a = 10</c> was true beside <c>number(a) = 10</c>, <c>a + 0 = 10</c> and <c>sum(a) = 10</c> that
    /// were all false.
    /// </para>
    /// <para>
    /// Every expression is asked of both backends, which must agree. What the mode keeps from 1.0 is
    /// tested beside what it does not: the first item of a sequence is the operand, and what cannot be read
    /// is NaN and never <c>FORG0001</c>. See <c>ConformanceNotes.md</c>, "One number() for everything the
    /// mode converts".
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class BackwardsCompatibleNumberTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Source =
            "<r><a>1e1</a><b>2.5E1</b><plus>+10</plus><inf>INF</inf><neg>-INF</neg><nan>NaN</nan>"
            + "<word>ten</word><plain>10</plain><pad> 1e1 </pad><empty/>"
            + "<n>1</n><n>1e1</n><n>+100</n></r>";

        private static string Stylesheet(string body, string version)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\">"
                + $"<xsl:template match=\"/r\"><out>{body}</out></xsl:template></xsl:stylesheet>";
        }

        /// <summary>Runs a template body on both backends, which must write the same thing.</summary>
        private static string Transforms(string body, string version = "1.0")
        {
            string stylesheet = Stylesheet(body, version);
            string? answer = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                string written = new Xslt(
                    stylesheet,
                    new XsltOptions { Backend = backend, Version = XsltVersion.V30, OmitXmlDeclaration = true })
                    .TransformXml(Source);

                answer ??= written;
                Assert.AreEqual(answer, written, $"the backends disagree about: {body}");
            }

            return answer == "<out/>" ? string.Empty : answer!["<out>".Length..^"</out>".Length];
        }

        /// <summary>Writes the value of an expression, a '&lt;' in it being written as it reads.</summary>
        private static string Writes(string expression, string version = "1.0")
        {
            string escaped = expression.Replace("<", "&lt;", StringComparison.Ordinal);

            return Transforms($"<xsl:value-of select=\"{escaped}\"/>", version);
        }

        [TestMethod]
        public void TheFourWaysToANumberGiveOneAnswer()
        {
            // The comparison, the function, arithmetic and the sum, over one node written with an exponent.
            // The first was true and the other three false.
            Assert.AreEqual("true", Writes("a = 10"));
            Assert.AreEqual("true", Writes("number(a) = 10"));
            Assert.AreEqual("true", Writes("a + 0 = 10"));
            Assert.AreEqual("true", Writes("sum(a) = 10"));

            // And over one that is no number, where none of them is an error and all of them are NaN.
            Assert.AreEqual("false", Writes("word = 10"));
            Assert.AreEqual("NaN", Writes("number(word)"));
            Assert.AreEqual("NaN", Writes("word + 0"));
            Assert.AreEqual("NaN", Writes("sum(word)"));
        }

        [TestMethod]
        public void NumberReadsWhatDoubleWrites()
        {
            Assert.AreEqual("10", Writes("number(a)"));
            Assert.AreEqual("25", Writes("number(b)"));
            Assert.AreEqual("10", Writes("number(plus)"));
            Assert.AreEqual("10", Writes("number(pad)"));
            Assert.AreEqual("INF", Writes("number(inf)"));
            Assert.AreEqual("-INF", Writes("number(neg)"));
            Assert.AreEqual("10", Writes("number(plain)"));

            // The same text as a string, which is what a node atomizes to before it is converted.
            Assert.AreEqual("10", Writes("number('1e1')"));
            Assert.AreEqual("0.15", Writes("number('1.5E-1')"));
            Assert.AreEqual("1", Writes("number('+1')"));
            Assert.AreEqual("INF", Writes("number('INF')"));

            // With no argument it is the context item that is read.
            Assert.AreEqual(
                "10",
                Transforms("<xsl:for-each select=\"a\"><xsl:value-of select=\"number()\"/></xsl:for-each>"));
        }

        [TestMethod]
        public void ArithmeticReadsAnOperandAsNumberDoes()
        {
            Assert.AreEqual("10", Writes("a + 0"));
            Assert.AreEqual("9", Writes("a - 1"));
            Assert.AreEqual("250", Writes("a * b"));
            Assert.AreEqual("2.5", Writes("b div a"));
            Assert.AreEqual("5", Writes("b mod a"));
            Assert.AreEqual("2", Writes("b idiv a"));
            Assert.AreEqual("20", Writes("plus + pad"));
            Assert.AreEqual("INF", Writes("inf + 1"));
            Assert.AreEqual("NaN", Writes("inf + neg"));

            // A string operand is converted by the same function.
            Assert.AreEqual("11", Writes("'1e1' + 1"));
            Assert.AreEqual("3", Writes("'+1' + '2e0'"));

            // And so is the operand of a unary minus, which is arithmetic like the rest.
            Assert.AreEqual("-10", Writes("-a"));
            Assert.AreEqual("INF", Writes("-neg"));
            Assert.AreEqual("-9", Writes("-a + 1"));
            Assert.AreEqual("10", Writes("- - a"));
            Assert.AreEqual("-250", Writes("-(a * b)"));
        }

        [TestMethod]
        public void SumReadsEveryItemAsNumberDoes()
        {
            Assert.AreEqual("111", Writes("sum(n)"));
            Assert.AreEqual("35", Writes("sum(a | b)"));
            Assert.AreEqual("INF", Writes("sum(n | inf)"));

            // A sequence is something a 1.0 stylesheet may write on this processor, and it is added up
            // the same way whatever it holds.
            Assert.AreEqual("15", Writes("sum((a, '2e0', 3))"));

            // One item that is not a number makes the total NaN rather than an error, and none is zero.
            Assert.AreEqual("NaN", Writes("sum(n | word)"));
            Assert.AreEqual("0", Writes("sum(absent)"));
        }

        [TestMethod]
        public void AnArgumentIsConvertedAsNumberDoes()
        {
            // XPath 2.0 §3.1.5: where a number is expected, the first item is converted by fn:number.
            Assert.AreEqual("10", Writes("round(a)"));
            Assert.AreEqual("26", Writes("ceiling('2.51e1')"));
            Assert.AreEqual("2", Writes("floor(b div a)"));
            Assert.AreEqual("bcd", Writes("substring('abcdef', '2e0', '+3')"));
            Assert.AreEqual("10.0", Writes("format-number(a, '0.0')"));
            Assert.AreEqual("NaN", Writes("round(word)"));
        }

        [TestMethod]
        public void TheValueOfAnXslNumberIsConvertedAsNumberDoes()
        {
            Assert.AreEqual("10", Transforms("<xsl:number value=\"a\"/>"));
            Assert.AreEqual("x", Transforms("<xsl:number value=\"'2.4e1'\" format=\"a\"/>"));
            Assert.AreEqual("NaN", Transforms("<xsl:number value=\"word\"/>"));
        }

        [TestMethod]
        public void WhatDoubleDoesNotWriteIsStillNaNAndNeverAnError()
        {
            // The half of XPath 1.0 the mode keeps. Infinity, +INF and nan are none of them in the lexical
            // space of xs:double, however readily .NET would read them.
            foreach (string text in new[] { "", " ", "ten", "Infinity", "+INF", "nan", "1e", "e1", "1e1.5", "0x10", "1 2", "1,5", "--1" })
            {
                Assert.AreEqual("NaN", Writes($"number('{text}')"), $"number('{text}')");
                Assert.AreEqual("NaN", Writes($"'{text}' + 0"), $"'{text}' + 0");
                Assert.AreEqual("NaN", Writes($"- '{text}'"), $"- '{text}'");
            }

            Assert.AreEqual("NaN", Writes("number(empty)"));
            Assert.AreEqual("NaN", Writes("number(nan)"));
            Assert.AreEqual("NaN", Writes("number(absent)"));
            Assert.AreEqual("NaN", Writes("absent + 1"));
            Assert.AreEqual("NaN", Writes("() * 2"));
        }

        [TestMethod]
        public void TheFirstItemIsStillTheOperand()
        {
            // The other half: a sequence where one item was expected is its first item, the rest being
            // discarded, where 2.0 refuses it.
            Assert.AreEqual("11", Writes("(a, b) + 1"));
            Assert.AreEqual("7", Writes("1 + (6 to 10)"));
            Assert.AreEqual("2", Writes("n + n"));
            Assert.AreEqual("10", Writes("number((a, b))"));
            Assert.AreEqual("10", Writes("round(('1e1', 'x'))"));

            // Including under a minus sign, where the compiled backend once read the whole sequence and
            // answered NaN.
            Assert.AreEqual("-3", Writes("-(3, 4)"));
            Assert.AreEqual("-1", Writes("-n"));
        }

        [TestMethod]
        public void WithoutTheModeTheRulesAreWhatTheyWere()
        {
            // Under 2.0 an untyped operand is cast to xs:double, which always read the exponent; what the
            // cast cannot read is an error there, and a sequence is not an operand at all.
            Assert.AreEqual("10", Writes("number(a)", "2.0"));
            Assert.AreEqual("10", Writes("a + 0", "2.0"));
            Assert.AreEqual("111", Writes("sum(n)", "2.0"));
            Assert.AreEqual("NaN", Writes("number(word)", "2.0"));

            Assert.AreEqual(
                "FORG0001",
                Assert.ThrowsExactly<XsltException>(() => Writes("word + 0", "2.0")).Code);
            Assert.AreEqual(
                "FORG0001",
                Assert.ThrowsExactly<XsltException>(() => Writes("sum(word)", "2.0")).Code);
            Assert.AreEqual(
                "XPTY0004",
                Assert.ThrowsExactly<XsltException>(() => Writes("(a, b) + 1", "2.0")).Code);
        }

        /// <summary>What <c>XslCompiledTransform</c> writes for a template body over the same source.</summary>
        private static string TheOracleWrites(string body)
        {
            XslCompiledTransform reference = new XslCompiledTransform();
            using (XmlReader reader = XmlReader.Create(new StringReader(Stylesheet(body, "1.0"))))
            {
                reference.Load(reader);
            }

            StringWriter output = new StringWriter();
            XmlWriterSettings settings = reference.OutputSettings!.Clone();
            settings.OmitXmlDeclaration = true;

            using (XmlWriter writer = XmlWriter.Create(output, settings))
            using (XmlReader reader = XmlReader.Create(new StringReader(Source)))
            {
                reference.Transform(reader, null, writer);
            }

            string written = output.ToString();
            return written["<out>".Length..^"</out>".Length];
        }

        private static string Selecting(params string[] expressions)
        {
            return string.Join(
                "|",
                expressions.Select(expression => $"<xsl:value-of select=\"{expression}\"/>"));
        }

        /// <summary>
        /// Tests that an exponent and a leading plus are read as <c>XslCompiledTransform</c> reads them.
        /// </summary>
        /// <remarks>
        /// XPath 1.0's grammar for a number has neither, and the oracle reads both all the same — in
        /// <c>number()</c>, in arithmetic, in <c>sum()</c>, in a comparison and in an argument. So for a
        /// stylesheet moved here from that processor the narrow reading was not faithfulness to 1.0 but a
        /// change of answer, from ten to NaN, and the only one of the two readings that agrees with both
        /// the specification and the processor most 1.0 stylesheets on .NET were written against is the
        /// wide one. Every expression here but the two comparisons differed before.
        /// </remarks>
        [TestMethod]
        public void TheOracleReadsAnExponentAndALeadingPlusTheSameWay()
        {
            string body = Selecting(
                "number(a)", "number(b)", "number(plus)", "number(pad)", "number('1.5E-1')",
                "a + 0", "plus + 0", "b div a", "b mod a", "a * b", "-a", "'1e1' + '+1'",
                "sum(n)", "sum(a | b)",
                "round('2.6e0')", "floor(b div a)", "substring('abcdef', '2e0', '+3')",
                "format-number(a, '0.0')",
                "a = 10", "plus = 10", "number(a) = 10", "sum(a) = 10")
                + "|<xsl:number value=\"a\"/>";

            string expected = TheOracleWrites(body);

            Assert.AreEqual(
                "10|25|10|10|0.15|10|10|2.5|5|250|-10|11|111|35|3|2|bcd|10.0|true|true|true|true|10",
                expected,
                "the oracle itself");
            Assert.AreEqual(expected, Transforms(body));
        }

        /// <summary>
        /// Records the one place left where this engine running a 1.0 stylesheet and the oracle running it
        /// differ over a number, so that the difference is on the record as deliberate.
        /// </summary>
        /// <remarks>
        /// <c>INF</c> and <c>-INF</c> are words to <c>XslCompiledTransform</c>, as they were to XPath 1.0.
        /// XPath 2.0 Appendix I.1 names them, with the exponent and the plus sign, among the strings that
        /// convert to numbers now with compatibility mode on, and XSLT 2.0's backwards compatibility is
        /// that mode and not XSLT 1.0. A differential test over them would be asserting a processor this
        /// engine is not.
        /// </remarks>
        [TestMethod]
        public void TheOracleDoesNotReadInfAndThatIsTheDocumentedDifference()
        {
            string body = Selecting("number(inf)", "number('-INF')", "inf = 1 div 0", "number(word)");

            Assert.AreEqual("NaN|NaN|false|NaN", TheOracleWrites(body));
            Assert.AreEqual("INF|-INF|true|NaN", Transforms(body));
        }
    }
}
