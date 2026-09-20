using System.Reflection;
using System.Text;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that a recursion may go as deep as the count of calls allows, whatever the stack it began on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The W3C suite's <c>call-template-1001</c> recurses five hundred deep, not in tail position, and
    /// failed once in ten full runs. A level cost between two and nearly four kilobytes of stack depending
    /// on what the just-in-time compiler had made of the methods in the chain by then, and the stack was
    /// the only limit there was: the count of calls stood at two thousand and was never reached.
    /// </para>
    /// <para>
    /// The depths here are ten times that and more, which no main thread's stack holds at any tier. They
    /// pass because a recursion that uses a stack up carries on upon another, and that is what these are
    /// tests of. A failure here is not a stack overflow — the guard that prevents one is unchanged — but
    /// an <see cref="XsltException"/> saying the recursion is too deep.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class DeepRecursionTests
    {
        private const int Deep = 5000;

        private const string Head =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
            + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" xmlns:e=\"urn:e\" "
            + "exclude-result-prefixes=\"xs f e\">";

        private static string Sheet(string body)
        {
            return Head + body + "</xsl:stylesheet>";
        }

        private static string Run(string stylesheet, string input = "<r/>")
        {
            string interpreted = Transform(stylesheet, input, XsltBackend.Interpreted);
            string compiled = Transform(stylesheet, input, XsltBackend.Compiled);

            Assert.AreEqual(interpreted, compiled, "The compiled backend disagreed with the interpreter.");

            return interpreted;
        }

        private static string Transform(string stylesheet, string input, XsltBackend backend)
        {
            return new Xslt(stylesheet, new XsltOptions { Backend = backend, OmitXmlDeclaration = true })
                .TransformXml(input);
        }

        /// <summary>Runs something on a thread with a stack of a given size, and returns what it returned.</summary>
        private static T OnAStackOf<T>(int kilobytes, Func<T> work)
        {
            T result = default!;
            Exception? failure = null;

            Thread thread = new Thread(
                () =>
                {
                    try
                    {
                        result = work();
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                },
                kilobytes * 1024);

            thread.Start();
            thread.Join();

            if (failure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }

            return result;
        }

        /// <summary>The shape of call-template-1001: the value is written after the call, so the call nests.</summary>
        private static string Countdown(int depth)
        {
            return Sheet(
                "<xsl:output method=\"text\"/>"
                + "<xsl:template match=\"/\"><xsl:call-template name=\"reverse\">"
                + $"<xsl:with-param name=\"index\" select=\"{depth}\"/></xsl:call-template></xsl:template>"
                + "<xsl:template name=\"reverse\"><xsl:param name=\"index\"/>"
                + "<xsl:if test=\"$index != 0\">"
                + "<xsl:call-template name=\"reverse\">"
                + "<xsl:with-param name=\"index\" select=\"$index - 1\"/></xsl:call-template>"
                + "<xsl:value-of select=\"concat($index, '|')\"/>"
                + "</xsl:if></xsl:template>");
        }

        private static string Counted(int depth)
        {
            StringBuilder expected = new StringBuilder();

            for (int i = 1; i <= depth; i++)
            {
                expected.Append(i).Append('|');
            }

            return expected.ToString();
        }

        [TestMethod]
        public void ATemplateMayCallItselfFiveThousandDeepWithTheCallNotLast()
        {
            Assert.AreEqual(Counted(Deep), Run(Countdown(Deep)));
        }

        [TestMethod]
        public void ATemplateMayCallItselfFiveThousandDeepWithTheCallLast()
        {
            // call-template-1002, the tail-recursive variant: the value is written before the call, which is
            // then the last thing the template does and is made in its place rather than beneath it.
            string stylesheet = Sheet(
                "<xsl:output method=\"text\"/>"
                + "<xsl:template match=\"/\"><xsl:call-template name=\"forward\">"
                + $"<xsl:with-param name=\"index\" select=\"{Deep}\"/></xsl:call-template></xsl:template>"
                + "<xsl:template name=\"forward\"><xsl:param name=\"index\"/>"
                + "<xsl:if test=\"$index != 0\">"
                + "<xsl:value-of select=\"concat($index, '|')\"/>"
                + "<xsl:call-template name=\"forward\">"
                + "<xsl:with-param name=\"index\" select=\"$index - 1\"/></xsl:call-template>"
                + "</xsl:if></xsl:template>");

            StringBuilder expected = new StringBuilder();

            for (int i = Deep; i >= 1; i--)
            {
                expected.Append(i).Append('|');
            }

            Assert.AreEqual(expected.ToString(), Run(stylesheet));
        }

        [TestMethod]
        public void AFunctionMayCallItselfFiveThousandDeep()
        {
            // Not in tail position: the sum is taken after the call returns.
            string stylesheet = Sheet(
                "<xsl:output method=\"text\"/>"
                + $"<xsl:template match=\"/\"><xsl:value-of select=\"f:sum({Deep})\"/></xsl:template>"
                + "<xsl:function name=\"f:sum\" as=\"xs:integer\"><xsl:param name=\"n\" as=\"xs:integer\"/>"
                + "<xsl:sequence select=\"if ($n = 0) then 0 else $n + f:sum($n - 1)\"/></xsl:function>");

            Assert.AreEqual((Deep * (Deep + 1L) / 2).ToString(), Run(stylesheet));
        }

        [TestMethod]
        public void AFunctionWhoseBodyIsASequenceConstructorMayCallItselfFiveThousandDeep()
        {
            // A body of more than one instruction is run as a sequence constructor rather than evaluated as
            // an expression, which is a different chain of calls between one level and the next.
            string stylesheet = Sheet(
                "<xsl:output method=\"text\"/>"
                + $"<xsl:template match=\"/\"><xsl:value-of select=\"f:sum({Deep})\"/></xsl:template>"
                + "<xsl:function name=\"f:sum\" as=\"xs:integer\"><xsl:param name=\"n\" as=\"xs:integer\"/>"
                + "<xsl:variable name=\"rest\" select=\"if ($n = 0) then 0 else f:sum($n - 1)\"/>"
                + "<xsl:sequence select=\"$n + $rest\"/></xsl:function>");

            Assert.AreEqual((Deep * (Deep + 1L) / 2).ToString(), Run(stylesheet));
        }

        [TestMethod]
        public void AFunctionMayCallItselfFiveThousandDeepWithTheCallLast()
        {
            string stylesheet = Sheet(
                "<xsl:output method=\"text\"/>"
                + $"<xsl:template match=\"/\"><xsl:value-of select=\"f:sum({Deep}, 0)\"/></xsl:template>"
                + "<xsl:function name=\"f:sum\" as=\"xs:integer\"><xsl:param name=\"n\" as=\"xs:integer\"/>"
                + "<xsl:param name=\"total\" as=\"xs:integer\"/>"
                + "<xsl:sequence select=\"if ($n = 0) then $total else f:sum($n - 1, $total + $n)\"/>"
                + "</xsl:function>");

            Assert.AreEqual((Deep * (Deep + 1L) / 2).ToString(), Run(stylesheet));
        }

        [TestMethod]
        public void AnInlineFunctionMayCallItselfFiveThousandDeep()
        {
            // A closure cannot name itself, so it is handed itself to call.
            string stylesheet = Sheet(
                "<xsl:output method=\"text\"/>"
                + "<xsl:template match=\"/\"><xsl:value-of select=\"let $sum := function($self, $n) "
                + "{ if ($n = 0) then 0 else $n + $self($self, $n - 1) } "
                + $"return $sum($sum, {Deep})\"/></xsl:template>");

            Assert.AreEqual((Deep * (Deep + 1L) / 2).ToString(), Run(stylesheet));
        }

        [TestMethod]
        public void TemplatesMayBeAppliedDownADocumentFiveThousandDeep()
        {
            // The element round the xsl:apply-templates is what keeps it from being the last thing the
            // template does, and makes the result as deep as the input.
            string stylesheet = Sheet(
                "<xsl:template match=\"a\"><b><xsl:apply-templates/></b></xsl:template>"
                + "<xsl:template match=\"text()\"><xsl:value-of select=\"count(ancestor::a)\"/></xsl:template>");

            StringBuilder input = new StringBuilder();
            StringBuilder expected = new StringBuilder();

            for (int i = 0; i < Deep; i++)
            {
                input.Append("<a>");
                expected.Append("<b>");
            }

            input.Append('x');
            expected.Append(Deep);

            for (int i = 0; i < Deep; i++)
            {
                input.Append("</a>");
                expected.Append("</b>");
            }

            Assert.AreEqual(expected.ToString(), Run(stylesheet, input.ToString()));
        }

        /// <summary>A document of one element inside another, so many deep, with a word of text at the bottom.</summary>
        private static string Nested(int depth, string open = "<a>", string close = "</a>")
        {
            StringBuilder built = new StringBuilder((open.Length + close.Length) * depth + 8);

            for (int i = 0; i < depth; i++)
            {
                built.Append(open);
            }

            built.Append("text");

            for (int i = 0; i < depth; i++)
            {
                built.Append(close);
            }

            return built.ToString();
        }

        [TestMethod]
        public void TheBuiltInRulesGoDownADocumentTwentyThousandDeep()
        {
            // No template is invoked on the way down, so nothing that guards a template invocation is
            // reached: the built-in rule for an element applies templates to its children, whose built-in
            // rule does the same. This ran the stack out for real at five thousand deep — not an error but
            // the end of the process, which is why it is tried here at a depth no thread's stack holds.
            string stylesheet = Sheet(
                "<xsl:output method=\"text\"/><xsl:template match=\"nothing\"/>");

            Assert.AreEqual("text", Run(stylesheet, Nested(20_000)));
        }

        [TestMethod]
        public void AModeThatCopiesWhatItHasNoRuleForGoesDownADocumentTwentyThousandDeep()
        {
            // The same, for the built-in rules 3.0 added, which ran out at two thousand: each level has an
            // element open in the output as well.
            string stylesheet = Sheet("<xsl:mode on-no-match=\"shallow-copy\"/>");
            string input = Nested(20_000);

            Assert.AreEqual(input, Run(stylesheet, input));
        }

        [TestMethod]
        public void TheStackTheTransformationBeganOnDoesNotDecideHowDeepItMayGo()
        {
            // A quarter of a megabyte holds a hundred levels or so. What it holds is not the point, and
            // that is the point: the same stylesheet gives the same answer whatever it was started on.
            string stylesheet = Countdown(Deep);

            string onASmallStack = OnAStackOf(256, () => Transform(stylesheet, "<r/>", XsltBackend.Interpreted));
            string onALargeStack = OnAStackOf(
                64 * 1024, () => Transform(stylesheet, "<r/>", XsltBackend.Interpreted));

            Assert.AreEqual(Counted(Deep), onASmallStack);
            Assert.AreEqual(Counted(Deep), onALargeStack);
        }

        [TestMethod]
        public void WhatADeepLevelSeesIsWhatAShallowOneWould()
        {
            // Everything a template can ask about where it is — the focus, the mode, the tunnel
            // parameters, the variables of the template that called it — asked five thousand levels down,
            // on a stack the transformation did not begin on.
            string stylesheet = Sheet(
                "<xsl:output method=\"text\"/>"
                + "<xsl:template match=\"/\"><xsl:apply-templates select=\"r/item\" mode=\"m\">"
                + "<xsl:with-param name=\"carried\" select=\"'tunnelled'\" tunnel=\"yes\"/>"
                + "</xsl:apply-templates></xsl:template>"
                + "<xsl:template match=\"item\" mode=\"m\">"
                + $"<xsl:call-template name=\"down\"><xsl:with-param name=\"n\" select=\"{Deep}\"/>"
                + "</xsl:call-template><xsl:text>;</xsl:text></xsl:template>"
                + "<xsl:template name=\"down\"><xsl:param name=\"n\"/>"
                + "<xsl:param name=\"carried\" tunnel=\"yes\"/>"
                + "<xsl:choose><xsl:when test=\"$n = 0\">"
                + "<xsl:value-of select=\"@id, position(), last(), $carried, name(current())\"/>"
                + "<xsl:apply-templates select=\"@id\" mode=\"#current\"/>"
                + "</xsl:when><xsl:otherwise>"
                + "<xsl:call-template name=\"down\"><xsl:with-param name=\"n\" select=\"$n - 1\"/>"
                + "</xsl:call-template><xsl:text>.</xsl:text>"
                + "</xsl:otherwise></xsl:choose></xsl:template>"
                + "<xsl:template match=\"@id\" mode=\"m\"> in-mode-m</xsl:template>"
                + "<xsl:template match=\"@id\"> in-no-mode</xsl:template>");

            string dots = new string('.', Deep);

            Assert.AreEqual(
                $"a 1 2 tunnelled item in-mode-m{dots};b 2 2 tunnelled item in-mode-m{dots};",
                Run(stylesheet, "<r><item id=\"a\"/><item id=\"b\"/></r>"));
        }

        [TestMethod]
        public void AnErrorRaisedDeepDownArrivesAsItself()
        {
            // The error is raised on another stack than the one that catches it, and has to arrive with its
            // code and its description, for an xsl:catch and for the caller alike.
            string recursion =
                "<xsl:template name=\"down\"><xsl:param name=\"n\"/>"
                + "<xsl:if test=\"$n = 0\">"
                + "<xsl:sequence select=\"error(QName('urn:e', 'e:bottom'), 'reached the bottom')\"/></xsl:if>"
                + "<xsl:call-template name=\"down\"><xsl:with-param name=\"n\" select=\"$n - 1\"/>"
                + "</xsl:call-template><xsl:text>.</xsl:text></xsl:template>";

            string Caught(int depth) => Sheet(
                "<xsl:output method=\"text\"/>"
                + "<xsl:template match=\"/\"><xsl:try>"
                + $"<xsl:call-template name=\"down\"><xsl:with-param name=\"n\" select=\"{depth}\"/>"
                + "</xsl:call-template>"
                + "<xsl:catch errors=\"e:bottom\"><xsl:value-of "
                + "xmlns:err=\"http://www.w3.org/2005/xqt-errors\" "
                + "select=\"local-name-from-QName($err:code), $err:description\"/></xsl:catch>"
                + "</xsl:try></xsl:template>"
                + recursion);

            // Caught, and reading exactly as the same error does raised three levels down, where no
            // second stack comes into it.
            string deep = Run(Caught(Deep));
            StringAssert.StartsWith(deep, "bottom ");
            StringAssert.Contains(deep, "reached the bottom");
            Assert.AreEqual(Run(Caught(3)), deep);

            string uncaught = Sheet(
                $"<xsl:template match=\"/\"><xsl:call-template name=\"down\">"
                + $"<xsl:with-param name=\"n\" select=\"{Deep}\"/></xsl:call-template></xsl:template>"
                + recursion);

            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(uncaught));
            Assert.AreEqual("bottom", error.Code);
            StringAssert.Contains(error.Message, "reached the bottom");
        }

        [TestMethod]
        public void ATemplateThatNeverStopsIsStoppedByTheCountOfCalls()
        {
            // The element keeps the call from being the last thing the template does; see
            // TransformTests.UnboundedRecursionIsReportedRatherThanCrashing.
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><xsl:call-template name=\"forever\"/></xsl:template>"
                + "<xsl:template name=\"forever\"><x><xsl:call-template name=\"forever\"/></x></xsl:template>");

            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(stylesheet));
            StringAssert.Contains(error.Message, "nested more than");
        }

        [TestMethod]
        public void AFunctionThatNeverStopsIsStoppedByTheCountOfCalls()
        {
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><xsl:value-of select=\"f:forever(1)\"/></xsl:template>"
                + "<xsl:function name=\"f:forever\"><xsl:param name=\"n\"/>"
                + "<xsl:sequence select=\"1 + f:forever($n + 1)\"/></xsl:function>");

            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(stylesheet));
            StringAssert.Contains(error.Message, "nested more than");
        }

        [TestMethod]
        public void AnInlineFunctionThatNeverStopsIsStoppedByTheCountOfCalls()
        {
            // Nothing used to count these, the stack running out being what stopped one. It no longer runs
            // out, so without the count this would not end.
            string stylesheet = Sheet(
                "<xsl:template match=\"/\"><xsl:value-of select=\"let $forever := function($self, $n) "
                + "{ 1 + $self($self, $n + 1) } return $forever($forever, 1)\"/></xsl:template>");

            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(stylesheet));
            StringAssert.Contains(error.Message, "nested more than");
        }

        [TestMethod]
        public void WhereTheCountStopsARecursionDoesNotDependOnTheStack()
        {
            // The limit is the count and nothing else, so a recursion a little short of it completes and
            // one a little past it does not, on a small stack exactly as on a large one. Before, what
            // stopped a recursion was the stack, some four hundred levels down, give or take a hundred
            // according to what the just-in-time compiler had been doing.
            foreach (int kilobytes in new[] { 256, 16 * 1024 })
            {
                string within = OnAStackOf(
                    kilobytes, () => Transform(Countdown(19_000), "<r/>", XsltBackend.Interpreted));

                Assert.IsTrue(within.EndsWith("|19000|", StringComparison.Ordinal));

                XsltException error = Assert.ThrowsExactly<XsltException>(() => OnAStackOf(
                    kilobytes, () => Transform(Countdown(21_000), "<r/>", XsltBackend.Interpreted)));

                StringAssert.Contains(error.Message, "nested more than");
            }
        }

        [TestMethod]
        public void AContextHeldForAnotherStackHoldsEveryField()
        {
            // A context is copied field by field when a recursion carries on upon another stack. A field
            // added to the context and not to the copy would be absent only down there, which no other
            // test would notice.
            static string[] Names(Type type) => type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Type held = typeof(DynamicContext).GetNestedType("Held", BindingFlags.NonPublic)
                ?? throw new AssertFailedException("DynamicContext.Held is gone.");

            CollectionAssert.AreEqual(Names(typeof(DynamicContext)), Names(held));
        }
    }
}
