namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the warnings a mode can ask for: no template rule matching a node, and two rules of one
    /// precedence and priority matching it.
    /// </summary>
    /// <remarks>
    /// XSLT 3.0 6.6.1 leaves the form of a warning, where it goes, and the default value of both
    /// attributes to the processor. Here the defaults are no: a stylesheet that has not asked is not told,
    /// which is also what keeps the second search of the rules that the multiple-match question costs off
    /// every transformation that would throw the answer away.
    /// </remarks>
    [TestClass]
    public sealed class WarningTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        /// <summary>What a run wrote to each of the two sinks, and what it produced.</summary>
        private sealed record Run(string Result, string Warned, string Messaged);

        private static Run Transform(string body, string input = "<r><a>one</a></r>", bool separate = true)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>{body}</xsl:stylesheet>";

            Run For(XsltBackend backend)
            {
                StringWriter warnings = new StringWriter();
                StringWriter messages = new StringWriter();

                string result = new Xslt(
                    stylesheet,
                    new XsltOptions
                    {
                        Backend = backend,
                        OmitXmlDeclaration = true,
                        MessageWriter = messages,
                        WarningWriter = separate ? warnings : null,
                    })
                    .TransformXml(input);

                return new Run(result, warnings.ToString(), messages.ToString());
            }

            Run interpreted = For(XsltBackend.Interpreted);
            Run compiled = For(XsltBackend.Compiled);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        /// <summary>How many warnings were written, a line being a warning.</summary>
        private static int Count(string written)
        {
            return written.Length == 0
                ? 0
                : written.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        [TestMethod]
        public void AModeIsSilentAboutANodeNothingMatchedUnlessItAsks()
        {
            // The default this processor chooses, and the reason the suite's mode-1441 and mode-1443 can
            // arrange for the condition and assert nothing about it.
            Assert.AreEqual(
                0,
                Count(Transform(
                    "<xsl:mode name=\"m\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/a\" mode=\"m\"/></out>"
                    + "</xsl:template>").Warned));

            foreach (string no in new[] { "no", "false", "0" })
            {
                Assert.AreEqual(
                    0,
                    Count(Transform(
                        $"<xsl:mode name=\"m\" warning-on-no-match=\"{no}\"/>"
                        + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/a\" mode=\"m\"/></out>"
                        + "</xsl:template>").Warned),
                    $"warning-on-no-match=\"{no}\" asked to be told");
            }
        }

        [TestMethod]
        public void AModeThatAsksIsToldWhatNothingMatched()
        {
            foreach (string yes in new[] { "yes", "true", "1" })
            {
                Run run = Transform(
                    $"<xsl:mode name=\"m\" warning-on-no-match=\"{yes}\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/a\" mode=\"m\"/></out>"
                    + "</xsl:template>");

                // The built-in rule ran, so the transformation produced what it always would; the warning
                // is beside the result and not instead of it.
                Assert.AreEqual("<out>one</out>", run.Result);
                Assert.Contains("No template rule matched", run.Warned);
            }
        }

        [TestMethod]
        public void AModeIsSilentAboutTwoRulesMatchingUnlessItAsks()
        {
            const string Rival =
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/a\" mode=\"m\"/></out></xsl:template>"
                + "<xsl:template match=\"a\" mode=\"m\">first</xsl:template>"
                + "<xsl:template match=\"a\" mode=\"m\">second</xsl:template>";

            Run quiet = Transform("<xsl:mode name=\"m\"/>" + Rival);

            // The later rule wins, which is what 3.0 says happens where the mode has not asked for
            // anything else, and nothing is said about it.
            Assert.AreEqual("<out>second</out>", quiet.Result);
            Assert.AreEqual(0, Count(quiet.Warned));

            foreach (string no in new[] { "no", "false", "0" })
            {
                Assert.AreEqual(
                    0,
                    Count(Transform($"<xsl:mode name=\"m\" warning-on-multiple-match=\"{no}\"/>" + Rival).Warned),
                    $"warning-on-multiple-match=\"{no}\" asked to be told");
            }

            foreach (string yes in new[] { "yes", "true", "1" })
            {
                Run told = Transform($"<xsl:mode name=\"m\" warning-on-multiple-match=\"{yes}\"/>" + Rival);

                Assert.AreEqual("<out>second</out>", told.Result);
                Assert.AreEqual(1, Count(told.Warned));
                Assert.Contains("More than one template rule", told.Warned);
            }
        }

        [TestMethod]
        public void AskingToBeWarnedIsNotAskingToFail()
        {
            // The two attributes are separate questions: warning-on-multiple-match says tell me, and
            // on-multiple-match="fail" says stop. A mode may ask either, or both.
            const string Rival =
                "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/a\" mode=\"m\"/></out></xsl:template>"
                + "<xsl:template match=\"a\" mode=\"m\">first</xsl:template>"
                + "<xsl:template match=\"a\" mode=\"m\">second</xsl:template>";

            Assert.AreEqual(
                "<out>second</out>",
                Transform("<xsl:mode name=\"m\" warning-on-multiple-match=\"yes\"/>" + Rival).Result);

            Assert.AreEqual(
                "XTDE0540",
                Assert.ThrowsExactly<XsltException>(() => Transform(
                    "<xsl:mode name=\"m\" on-multiple-match=\"fail\" warning-on-multiple-match=\"yes\"/>"
                    + Rival)).Code);
        }

        [TestMethod]
        public void AWarningGoesToTheMessageWriterWhereTheCallerNamedNoOther()
        {
            // So that a caller who set one sink and not the other sees warnings rather than losing them,
            // which is what every caller written before there were two sinks did.
            Run run = Transform(
                "<xsl:mode name=\"m\" warning-on-no-match=\"yes\"/>"
                + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/a\" mode=\"m\"/></out>"
                + "</xsl:template>",
                separate: false);

            Assert.AreEqual(0, Count(run.Warned));
            Assert.Contains("No template rule matched", run.Messaged);
        }

        [TestMethod]
        public void WarningsAndMessagesAreSilencedApart()
        {
            // TextWriter.Null is a sink that was named, so the fallback does not apply and the warnings go
            // nowhere while the messages still arrive.
            StringWriter messages = new StringWriter();

            string result = new Xslt(
                $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + "<xsl:mode name=\"m\" warning-on-no-match=\"yes\"/>"
                + "<xsl:template match=\"/\"><out><xsl:message>said</xsl:message>"
                + "<xsl:apply-templates select=\"/r/a\" mode=\"m\"/></out></xsl:template>"
                + "</xsl:stylesheet>",
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    MessageWriter = messages,
                    WarningWriter = TextWriter.Null,
                })
                .TransformXml("<r><a>one</a></r>");

            Assert.AreEqual("<out>one</out>", result);
            Assert.AreEqual("said", messages.ToString().Trim());
        }

        [TestMethod]
        public void TwoDeclarationsOfOneModeHaveToAgreeAboutTheWarning()
        {
            // Settled like every other attribute of xsl:mode: the highest precedence that writes it
            // decides, and two at that precedence disagreeing is XTSE0545 rather than two modes.
            Assert.AreEqual(
                "XTSE0545",
                Assert.ThrowsExactly<XsltException>(() => Transform(
                    "<xsl:mode name=\"m\" warning-on-multiple-match=\"yes\"/>"
                    + "<xsl:mode name=\"m\" warning-on-multiple-match=\"no\"/>"
                    + "<xsl:template match=\"/\"><out/></xsl:template>")).Code);
        }
    }
}
