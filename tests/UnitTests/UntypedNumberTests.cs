using System.Xml;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that untyped text is read as <c>xs:double</c> writes a number in the two places that read it
    /// for themselves at every version: a sort key with <c>data-type="number"</c>, and the first argument
    /// of <c>format-number()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XSLT 2.0 §13.1.2 converts a <c>data-type="number"</c> key with <c>number()</c>, and the function
    /// conversion rules cast an untyped argument declared <c>xs:numeric?</c> to <c>xs:double</c>. Both
    /// read an exponent and a leading plus. Both places had read the text by XPath 1.0's grammar for a
    /// number, so <c>1e1</c> was NaN: sorted ahead of everything, and formatted as <c>NaN</c>, under
    /// 1.0, 2.0 and 3.0 alike. <c>XslCompiledTransform</c> reads both as ten, which makes it an oracle
    /// for the sort.
    /// </para>
    /// <para>
    /// Everything is asked of both backends, which must agree. See <c>ConformanceNotes.md</c>, "A number
    /// to sort by, and a number to format".
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class UntypedNumberTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Source =
            "<r><i k='100'/><i k='abc'/><i/><i k='1e1'/><i k='9'/><i k='+11'/><i k='xyz'/><i k='-5'/>"
            + "<i k=' 2.5E1 '/><a>1e1</a><plus>+10</plus><pad> 1e1 </pad><inf>INF</inf><word>ten</word>"
            + "<blank/><plain>10</plain></r>";

        private static string Stylesheet(string body, string version)
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + $"<xsl:template match=\"/r\"><out>{body}</out></xsl:template></xsl:stylesheet>";
        }

        /// <summary>Runs a template body on both backends, which must write the same thing.</summary>
        private static string Transforms(string body, string version)
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

        /// <summary>The code both backends refuse a template body with.</summary>
        private static string Refuses(string body, string version)
        {
            string stylesheet = Stylesheet(body, version);
            string? code = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                XsltOptions options = new XsltOptions
                {
                    Backend = backend,
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                };

                string refused = Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(stylesheet, options).TransformXml(Source)).Code ?? string.Empty;

                code ??= refused;
                Assert.AreEqual(code, refused, $"the backends refuse differently: {body}");
            }

            return code!;
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

        private static string Sorted(string order)
        {
            return $"<xsl:for-each select=\"i\"><xsl:sort select=\"@k\" data-type=\"number\" order=\"{order}\"/>"
                + "[<xsl:value-of select=\"@k\"/>]</xsl:for-each>";
        }

        private static readonly string[] s_versions = { "1.0", "2.0", "3.0" };

        [TestMethod]
        public void ANumericKeyIsSortedAsTheOracleSortsIt()
        {
            // An exponent, a leading plus and whitespace are all read, what is no number is NaN and sorts
            // first, and a key that is not there is number(()) and so NaN as well: it stands among the
            // others that are, in document order, and not ahead of them.
            const string Ascending = "[abc][][xyz][-5][9][1e1][+11][ 2.5E1 ][100]";
            const string Descending = "[100][ 2.5E1 ][+11][1e1][9][-5][abc][][xyz]";

            Assert.AreEqual(Ascending, TheOracleWrites(Sorted("ascending")), "the oracle itself");
            Assert.AreEqual(Descending, TheOracleWrites(Sorted("descending")), "the oracle itself");

            foreach (string version in s_versions)
            {
                Assert.AreEqual(Ascending, Transforms(Sorted("ascending"), version), version);
                Assert.AreEqual(Descending, Transforms(Sorted("descending"), version), version);
            }
        }

        [TestMethod]
        public void InfSortsAsTheInfinityItIsWhichIsTheDocumentedDifference()
        {
            // INF is a word to XslCompiledTransform, as it was to XPath 1.0, and sorts with the NaNs.
            // number() from 2.0 on reads it as infinity, backwards compatibility or no.
            const string Body =
                "<xsl:for-each select=\"inf | plain | word\"><xsl:sort select=\".\" data-type=\"number\"/>"
                + "[<xsl:value-of select=\".\"/>]</xsl:for-each>";

            Assert.AreEqual("[INF][ten][10]", TheOracleWrites(Body));

            foreach (string version in s_versions)
            {
                Assert.AreEqual("[ten][10][INF]", Transforms(Body, version), version);
            }
        }

        [TestMethod]
        public void ASequenceOfStringsIsSortedTheSameWay()
        {
            // number() of a string is the same cast, so the items of a sequence are keys like any other.
            foreach (string version in new[] { "2.0", "3.0" })
            {
                Assert.AreEqual(
                    "-INF 9 1e1 +11 100 INF",
                    Transforms(
                        "<xsl:perform-sort select=\"('100', 'INF', '1e1', '9', '-INF', '+11')\">"
                        + "<xsl:sort select=\".\" data-type=\"number\"/></xsl:perform-sort>",
                        version),
                    version);
            }
        }

        [TestMethod]
        public void ALesserKeyStillSettlesWhatTheNumericOneLeavesEqual()
        {
            // 1e1 and 10 are one number, so the second key orders them, 'plain' before 'a' descending.
            // While 1e1 was NaN it stood with 'ten' instead, and the second key ordered that pair.
            const string Body =
                "<xsl:for-each select=\"a | plain | word\">"
                + "<xsl:sort select=\".\" data-type=\"number\"/>"
                + "<xsl:sort select=\"name()\" order=\"descending\"/>"
                + "[<xsl:value-of select=\".\"/>]</xsl:for-each>";

            Assert.AreEqual("[ten][10][1e1]", TheOracleWrites(Body), "the oracle itself");

            foreach (string version in s_versions)
            {
                Assert.AreEqual("[ten][10][1e1]", Transforms(Body, version), version);
            }
        }

        [TestMethod]
        public void GroupsAreSortedByANumericKeyTheSameWay()
        {
            Assert.AreEqual(
                "[abc][xyz][-5][9][1e1][+11][ 2.5E1 ][100]",
                Transforms(
                    "<xsl:for-each-group select=\"i[@k]\" group-by=\"string(@k)\">"
                    + "<xsl:sort select=\"current-grouping-key()\" data-type=\"number\"/>"
                    + "[<xsl:value-of select=\"current-grouping-key()\"/>]"
                    + "</xsl:for-each-group>",
                    "3.0"));
        }

        [TestMethod]
        public void AMergeOrdersItsSourcesByANumericKeyTheSameWay()
        {
            // Both sources are in order as numbers and neither is as XPath 1.0's grammar reads them, where
            // 1e1 and +11 are NaN: the merge was refused as XTDE2220, a source out of order.
            Assert.AreEqual(
                "9 1e1 +11 100 2e2 ",
                Transforms(
                    "<xsl:merge>"
                    + "<xsl:merge-source name=\"one\" select=\"('9', '1e1', '100')\">"
                    + "<xsl:merge-key select=\".\" data-type=\"number\"/></xsl:merge-source>"
                    + "<xsl:merge-source name=\"two\" select=\"('+11', '2e2')\">"
                    + "<xsl:merge-key select=\".\" data-type=\"number\"/></xsl:merge-source>"
                    + "<xsl:merge-action><xsl:value-of select=\"current-merge-group()\"/>"
                    + "<xsl:text> </xsl:text></xsl:merge-action>"
                    + "</xsl:merge>",
                    "3.0"));
        }

        [TestMethod]
        public void FormatNumberReadsAnUntypedArgumentAsDoubleWritesOne()
        {
            foreach (string version in s_versions)
            {
                Assert.AreEqual("10.0", Transforms("<xsl:value-of select=\"format-number(a, '0.0')\"/>", version), version);
                Assert.AreEqual("10.0", Transforms("<xsl:value-of select=\"format-number(plus, '0.0')\"/>", version), version);
                Assert.AreEqual("10.0", Transforms("<xsl:value-of select=\"format-number(pad, '0.0')\"/>", version), version);
                Assert.AreEqual("10.0", Transforms("<xsl:value-of select=\"format-number(plain, '0.0')\"/>", version), version);
                Assert.AreEqual("Infinity", Transforms("<xsl:value-of select=\"format-number(inf, '0.0')\"/>", version), version);

                // Nothing at all is NaN at every version, the parameter being optional.
                Assert.AreEqual("NaN", Transforms("<xsl:value-of select=\"format-number(absent, '0.0')\"/>", version), version);
            }

            // The oracle agrees about all of it but INF.
            Assert.AreEqual(
                "10.0|10.0|10.0|NaN",
                TheOracleWrites(
                    "<xsl:value-of select=\"format-number(a, '0.0')\"/>|<xsl:value-of select=\"format-number(plus, '0.0')\"/>|"
                    + "<xsl:value-of select=\"format-number(pad, '0.0')\"/>|<xsl:value-of select=\"format-number(absent, '0.0')\"/>"));
        }

        [TestMethod]
        public void FormatNumberRefusesUntypedContentThatIsNoNumberAsEveryOtherFunctionDoes()
        {
            // From 2.0 an untyped argument is cast to xs:double, and a cast that fails is FORG0001. It is
            // what round() of the same node has always answered; format-number() answered 'NaN'.
            foreach (string version in new[] { "2.0", "3.0" })
            {
                Assert.AreEqual("FORG0001", Refuses("<xsl:value-of select=\"round(word)\"/>", version), version);
                Assert.AreEqual("FORG0001", Refuses("<xsl:value-of select=\"format-number(word, '0.0')\"/>", version), version);
                Assert.AreEqual("FORG0001", Refuses("<xsl:value-of select=\"format-number(blank, '0.0')\"/>", version), version);

                // number() is how a stylesheet says it wants NaN instead.
                Assert.AreEqual(
                    "NaN",
                    Transforms("<xsl:value-of select=\"format-number(number(word), '0.0')\"/>", version),
                    version);
            }

            // Backwards compatibility converts with number() for it, and cannot fail.
            Assert.AreEqual("NaN", Transforms("<xsl:value-of select=\"format-number(word, '0.0')\"/>", "1.0"));
            Assert.AreEqual("NaN", Transforms("<xsl:value-of select=\"format-number(blank, '0.0')\"/>", "1.0"));
        }

        [TestMethod]
        public void ATypedNumberIsFormattedFromItsOwnDigits()
        {
            // What was right already: a number is not read through a double on its way to the picture.
            Assert.AreEqual(
                "123456789012345678.5",
                Transforms(
                    "<xsl:value-of select=\"format-number(xs:decimal('123456789012345678.5'), '0.0')\"/>", "3.0"));
            Assert.AreEqual(
                "1,000",
                Transforms("<xsl:value-of select=\"format-number(1000, '#,##0')\"/>", "3.0"));
        }
    }
}
