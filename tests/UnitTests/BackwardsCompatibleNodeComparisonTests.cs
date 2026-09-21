namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that a backwards-compatible comparison reads a node as the same number it reads the node's
    /// text as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 2.0 §3.5.2 atomizes both operands of a general comparison before it converts either, and under
    /// backwards compatibility converts with <c>fn:number</c>, which from 2.0 on reads the lexical space of
    /// <c>xs:double</c>: an exponent, a leading plus, <c>INF</c>. So <c>a = 10</c> and <c>string(a) = 10</c>
    /// are one question. They had two answers over <c>&lt;a&gt;1e1&lt;/a&gt;</c>, because the paths that
    /// compare nodes without materialising them read the text by XPath 1.0's grammar, which has no exponent.
    /// </para>
    /// <para>
    /// Every comparison is asked four ways — in a <c>select</c> and in an <c>xsl:if</c>, which evaluate a
    /// comparison by different routes, and on both backends — and every node form is asked beside the string
    /// form it must agree with. XPath 1.0 as specified answers NaN for <c>1e1</c> either way round; this
    /// is XSLT 2.0's backwards compatibility, which is not that — and neither is
    /// <c>XslCompiledTransform</c>, which reads the exponent, as <c>BackwardsCompatibleNumberTests</c>
    /// shows. See <c>ConformanceNotes.md</c>, "What backwards compatibility is, and what it is not".
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class BackwardsCompatibleNodeComparisonTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Source =
            "<r><a>1e1</a><b>2.5E1</b><plus>+10</plus><inf>INF</inf><neg>-INF</neg>"
            + "<word>ten</word><plain>10</plain><pad> 1e1 </pad><empty/></r>";

        /// <summary>
        /// Answers a comparison under <c>version="1.0"</c>, having asked it in a <c>select</c> and in an
        /// <c>xsl:if</c> on both backends and had one answer from all four.
        /// </summary>
        private static string Answers(string asWritten)
        {
            // An expression is written here as it reads, and a '<' in an attribute is written '&lt;'.
            string expression = asWritten.Replace("<", "&lt;", StringComparison.Ordinal);

            string stylesheet = $"<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template match=\"/r\">"
                + "<xsl:variable name=\"v\" select=\"a\"/>"
                + "<xsl:variable name=\"w\" select=\"b\"/>"
                + "<xsl:variable name=\"all\" select=\"*\"/>"
                + $"<out><s><xsl:value-of select=\"{expression}\"/></s>"
                + $"<i><xsl:if test=\"{expression}\">true</xsl:if></i></out>"
                + "</xsl:template></xsl:stylesheet>";

            string? answer = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                string written = new Xslt(
                    stylesheet,
                    new XsltOptions { Backend = backend, Version = XsltVersion.V30, OmitXmlDeclaration = true })
                    .TransformXml(Source);

                string selected = written.Contains("<s>true</s>", StringComparison.Ordinal) ? "true" : "false";
                string tested = written.Contains("<i>true</i>", StringComparison.Ordinal) ? "true" : "false";

                Assert.IsTrue(
                    written.Contains($"<s>{selected}</s>", StringComparison.Ordinal),
                    $"'{asWritten}' selected neither true nor false on the {backend} backend: {written}");
                Assert.AreEqual(
                    selected, tested, $"'{asWritten}' is one thing selected and another tested, {backend} backend");

                answer ??= selected;
                Assert.AreEqual(answer, selected, $"the backends disagree about '{asWritten}'");
            }

            return answer!;
        }

        /// <summary>Asserts that a comparison of a node and the same comparison of its text are both this.</summary>
        private static void Agree(string expected, string ofTheNode, string ofItsText)
        {
            Assert.AreEqual(expected, Answers(ofItsText), ofItsText);
            Assert.AreEqual(expected, Answers(ofTheNode), $"{ofTheNode}, which {ofItsText} answers {expected} for");
        }

        [TestMethod]
        public void ANodeEqualToANumberIsReadAsItsTextIs()
        {
            Agree("true", "a = 10", "string(a) = 10");
            Agree("true", "10 = a", "10 = string(a)");
            Agree("false", "a != 10", "string(a) != 10");
            Agree("true", "b = 25", "string(b) = 25");

            // A leading plus and the two infinities are in the lexical space as well, and whitespace
            // around any of it is not part of the value.
            Agree("true", "plus = 10", "string(plus) = 10");
            Agree("true", "pad = 10", "string(pad) = 10");
            Agree("true", "inf = 1 div 0", "string(inf) = 1 div 0");
            Agree("true", "neg = -1 div 0", "string(neg) = -1 div 0");
        }

        [TestMethod]
        public void ANodeOrderedAgainstANumberIsReadAsItsTextIs()
        {
            Agree("true", "a > 9", "string(a) > 9");
            Agree("true", "a >= 10", "string(a) >= 10");
            Agree("true", "a < 11", "string(a) < 11");
            Agree("true", "9 < a", "9 < string(a)");
            Agree("false", "a > 10", "string(a) > 10");
            Agree("true", "inf > 1000000", "string(inf) > 1000000");
            Agree("true", "neg < 0", "string(neg) < 0");
        }

        [TestMethod]
        public void TheValueANodeIsOrderedAgainstIsReadThatWayToo()
        {
            // An ordering converts both sides with fn:number whatever they are, so the exponent may be on
            // the side that is not a node — or on both.
            Agree("true", "plain < '1e2'", "string(plain) < '1e2'");
            Agree("true", "'1e2' > a", "'1e2' > string(a)");
            Agree("true", "b > a", "string(b) > string(a)");
            Agree("false", "a >= b", "string(a) >= string(b)");
        }

        [TestMethod]
        public void ANodeSetInAVariableIsReadAsItsTextIs()
        {
            // A variable is not statically a node-set, so these go the general way and reach the node-set
            // branches there rather than the ones that take the nodes from a path.
            Agree("true", "$v = 10", "string($v) = 10");
            Agree("false", "$v != 10", "string($v) != 10");
            Agree("true", "$v > 9", "string($v) > 9");
            Agree("true", "9 < $v", "9 < string($v)");
            Agree("true", "$w > $v", "string($w) > string($v)");
            Agree("true", "$w > a", "string($w) > string(a)");
            Agree("true", "b > $v", "string(b) > string($v)");
        }

        [TestMethod]
        public void AComparisonOverSeveralNodesFindsTheOneWrittenWithAnExponent()
        {
            Assert.AreEqual("true", Answers("* = 25"));
            Assert.AreEqual("true", Answers("$all = 25"));
            Assert.AreEqual("true", Answers("count(*[. > 9]) = count(*[string(.) > 9])"));
            Assert.AreEqual("true", Answers("count(*[. > 9]) = 6"));
            Assert.AreEqual("true", Answers("count(*[. = 10]) = 4"));
        }

        [TestMethod]
        public void ANodeComparedWithASequenceIsReadAsItsTextIs()
        {
            // A sequence is something a 1.0 stylesheet on this processor may write, and each of its items
            // is an operand in its turn.
            Agree("true", "plain = (3, 10)", "string(plain) = (3, 10)");
            Agree("true", "a = (3, 10)", "string(a) = (3, 10)");
            Agree("true", "a > (30, 9)", "string(a) > (30, 9)");
        }

        [TestMethod]
        public void ANodeComparedWithARangeIsComparedWithEachIntegerInIt()
        {
            // A range is a sequence that does not say so where the comparison is built, so it reached
            // the route that reads the nodes from a list, which read it as one string: its integers
            // joined by spaces, equal to no price and no number at all. An empty one was the empty
            // string, and equal to every empty element.
            Agree("true", "plain = (1 to 10)", "string(plain) = (1 to 10)");
            Agree("true", "(1 to 10) = a", "(1 to 10) = string(a)");
            Agree("false", "plain = (1 to 9)", "string(plain) = (1 to 9)");
            Agree("true", "plain != (1 to 10)", "string(plain) != (1 to 10)");
            Agree("true", "plain > (1 to 3)", "string(plain) > (1 to 3)");
            Agree("true", "(11 to 12) > a", "(11 to 12) > string(a)");
            Agree("false", "plain > (10 to 12)", "string(plain) > (10 to 12)");
            Agree("false", "empty = (5 to 1)", "string(empty) = (5 to 1)");
            Agree("false", "empty != (5 to 1)", "string(empty) != (5 to 1)");

            // Inside a predicate, where the emitted form walks the step itself and compares through a
            // helper of its own.
            Assert.AreEqual("true", Answers("count(/r[plain = (1 to 10)]) = 1"));
            Assert.AreEqual("true", Answers("count(/r[(1 to 10) = a]) = 1"));
            Assert.AreEqual("true", Answers("count(/r[plain > (1 to 3)]) = 1"));
            Assert.AreEqual("true", Answers("count(/r[empty = (5 to 1)]) = 0"));
            Assert.AreEqual("true", Answers("count(/r/*[. = (1 to 10)]) = 4"));
        }

        [TestMethod]
        public void TextThatIsNotANumberIsStillNotOne()
        {
            // No FORG0001 under backwards compatibility: what cannot be read is NaN, and NaN is unequal
            // to everything and ordered against nothing.
            Agree("false", "word = 10", "string(word) = 10");
            Agree("true", "word != 10", "string(word) != 10");
            Agree("false", "word > 0", "string(word) > 0");
            Agree("false", "word <= 0", "string(word) <= 0");
            Agree("false", "empty = 0", "string(empty) = 0");
            Agree("false", "word > a", "string(word) > string(a)");
        }

        [TestMethod]
        public void TwoNodesAreStillEqualAsTextAndNumberReadsThemAsTheComparisonDoes()
        {
            // Neither operand being a number, an equality is between two untyped values and compares
            // them as strings: '1e1' and '10' are different text, as nodes and as strings alike.
            Agree("false", "a = plain", "string(a) = string(plain)");
            Agree("true", "a != plain", "string(a) != string(plain)");

            // And number() is the conversion the comparison makes, called by name, so it makes two nodes
            // that are different text the same number. It read XPath 1.0's grammar here once, and
            // 'number(a) = 10' was false beside an 'a = 10' that was true; BackwardsCompatibleNumberTests
            // has the rest of that.
            Agree("true", "number(a) = 10", "number(string(a)) = 10");
            Agree("true", "number(a) = number(plain)", "number(string(a)) = number(string(plain))");
        }
    }
}
