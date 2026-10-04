using System.Linq.Expressions;
using System.Reflection;
using System.Xml;
using System.Xml.Xsl;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the strings that are put together on the stack rather than in a builder made for each,
    /// and for the capture of an instruction's content as a string: for what they answer, which did not
    /// change, and what they allocate, which did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>format-date()</c> and its two relatives, <c>xsl:number</c>, <c>format-integer()</c> with a
    /// picture of digits, the three functions that escape a URI, and the identity a value is grouped,
    /// keyed and told apart by were each written into a <c>StringBuilder</c> made for the call, or joined
    /// from a string for every part. They are written into stack space now. The content of an
    /// <c>xsl:attribute</c>, an <c>xsl:comment</c> or an <c>xsl:processing-instruction</c> was captured in
    /// a target made for the instruction, with a list and a builder of its own; the transformation lends
    /// one and takes it back, and content that is one string is that string.
    /// </para>
    /// <para>
    /// A stack buffer has a size and what is written may be longer, so each of these is asked for an
    /// answer longer than its buffer as well as for ordinary ones. See <c>ConformanceNotes.md</c>,
    /// "Strings put together on the stack".
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class StackBuiltStringTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private const string Source =
            "<r><i n=\"1\" d=\"2024-03-05\" g=\"x\">alpha</i><i n=\"2\" d=\"2024-03-05\" g=\"y\">beta</i>"
            + "<i n=\"3\" d=\"2023-12-31\" g=\"x\">gamma</i><i n=\"2.0\" d=\"2024-03-06\" g=\"z\">delta</i></r>";

        private static string Sheet(string version, string body, string declarations = "", string method = "text")
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\" "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:p=\"urn:p\" exclude-result-prefixes=\"xs p\">"
                + $"<xsl:output method=\"{method}\" omit-xml-declaration=\"yes\"/>{declarations}"
                + $"<xsl:template match=\"/r\">{body}</xsl:template></xsl:stylesheet>";
        }

        /// <summary>The one answer both backends write for a template body.</summary>
        private static string Writes(string body, string declarations = "", string version = "3.0", string method = "text")
        {
            string stylesheet = Sheet(version, body, declarations, method);
            string? answer = null;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                string written = new Xslt(stylesheet, new XsltOptions { Backend = backend }).TransformXml(Source);

                answer ??= written;
                Assert.AreEqual(answer, written, $"the backends disagree about: {body}");
            }

            return answer!;
        }

        /// <summary>What <c>XslCompiledTransform</c> writes for the same body as a 1.0 stylesheet.</summary>
        private static string TheOracleWrites(string body, string declarations = "", string method = "text")
        {
            XslCompiledTransform reference = new XslCompiledTransform();
            using (XmlReader reader = XmlReader.Create(new StringReader(Sheet("1.0", body, declarations, method))))
            {
                reference.Load(reader);
            }

            StringWriter output = new StringWriter();
            using (XmlReader reader = XmlReader.Create(new StringReader(Source)))
            {
                reference.Transform(reader, null, output);
            }

            return output.ToString();
        }

        /// <summary>Asserts what a 1.0 body writes: the framework's processor first, then both versions here.</summary>
        private static void AssertAtEveryVersion(string expected, string body, string declarations = "", string method = "text")
        {
            // The framework writes an empty element with a space before its slash, and nothing else here
            // is written differently.
            Assert.AreEqual(
                expected,
                TheOracleWrites(body, declarations, method).Replace(" />", "/>", StringComparison.Ordinal),
                $"the oracle itself, about: {body}");
            Assert.AreEqual(expected, Writes(body, declarations, "1.0", method), $"1.0: {body}");
            Assert.AreEqual(expected, Writes(body, declarations, "3.0", method), $"3.0: {body}");
        }

        private static string Value(string expression)
        {
            return Writes($"<xsl:value-of select=\"{expression}\"/>");
        }

        // ---- format-date, format-time and format-dateTime ---------------------------------------------------

        [TestMethod]
        public void ADateIsWrittenAsItsPictureSays()
        {
            Assert.AreEqual("2024-03-05", Value("format-date(xs:date('2024-03-05'), '[Y0001]-[M01]-[D01]')"));
            Assert.AreEqual("5 March 2024", Value("format-date(xs:date('2024-03-05'), '[D] [MNn] [Y]')"));
            Assert.AreEqual("Tuesday, 5th Mar 2024", Value("format-date(xs:date('2024-03-05'), '[FNn], [D1o] [MNn,3-3] [Y]')"));
            Assert.AreEqual("[x] 2024 ]", Value("format-date(xs:date('2024-03-05'), '[[x]] [Y] ]]')"));
            Assert.AreEqual("March     |24|005", Value("format-date(xs:date('2024-03-05'), '[MNn,10-10]|[Y,2-2]|[D001]')"));
            Assert.AreEqual("14:07:09", Value("format-time(xs:time('14:07:09'), '[H01]:[m01]:[s01]')"));

            Assert.AreEqual(
                "14:07:09.500 +02:00 GMT+02:00",
                Value("format-dateTime(xs:dateTime('2024-03-05T14:07:09.5+02:00'), '[H01]:[m01]:[s01].[f001] [Z] [z]')"));

            // A language this engine has no names in is answered in English and says so in front.
            Assert.AreEqual("[Language: en]5 March", Value("format-date(xs:date('2024-03-05'), '[D] [MNn]', 'xx', (), ())"));
        }

        [TestMethod]
        public void ADateInDigitsOutsideTheBasicPlaneIsWrittenInPairs()
        {
            // Osmanya digits, U+104A0 to U+104A9, each of which is two chars: the day and the padding of
            // the year are written a pair at a time.
            string five = char.ConvertFromUtf32(0x104A5);
            string zero = char.ConvertFromUtf32(0x104A0);
            string two = char.ConvertFromUtf32(0x104A2);
            string four = char.ConvertFromUtf32(0x104A4);

            Assert.AreEqual(five, Value("format-date(xs:date('2024-03-05'), '[D&#x104A1;]')"));

            Assert.AreEqual(
                zero + zero + two + zero + two + four,
                Value("format-date(xs:date('2024-03-05'), '[Y&#x104A0;&#x104A0;&#x104A0;&#x104A0;&#x104A0;&#x104A1;]')"));
        }

        [TestMethod]
        public void ADateLongerThanTheStackSpaceIsWrittenWhole()
        {
            // A hundred and twenty-eight characters are set aside, and this picture writes two hundred
            // and twenty: ten dates and twenty words of literal text between them.
            string picture = string.Concat(Enumerable.Repeat("[Y0001]-[M01]-[D01] and then ", 10));
            string expected = string.Concat(Enumerable.Repeat("2024-03-05 and then ", 10));

            Assert.AreEqual(expected, Value($"format-date(xs:date('2024-03-05'), '{picture}')"));
        }

        // ---- xsl:number ------------------------------------------------------------------------------------

        [TestMethod]
        public void NumbersAreWrittenAsTheirFormatSays()
        {
            AssertAtEveryVersion(
                "1.|2.|3.|4.|",
                "<xsl:for-each select=\"i\"><xsl:number value=\"position()\" format=\"1.\"/>|</xsl:for-each>");

            AssertAtEveryVersion(
                "(a)(b)(c)(d)",
                "<xsl:for-each select=\"i\"><xsl:number value=\"position()\" format=\"(a)\"/></xsl:for-each>");

            AssertAtEveryVersion(
                "I-001;II-002;III-003;IV-004;",
                "<xsl:for-each select=\"i\"><xsl:number value=\"position()\" format=\"I-\"/>"
                + "<xsl:number value=\"position()\" format=\"001;\"/></xsl:for-each>");

            AssertAtEveryVersion(
                "1.1 1.2 1.3 1.4 ",
                "<xsl:for-each select=\"i\"><xsl:number level=\"multiple\" count=\"r|i\" format=\"1.1 \"/></xsl:for-each>");

            AssertAtEveryVersion(
                "1,000|2,000|3,000|4,000|",
                "<xsl:for-each select=\"i\"><xsl:number value=\"position() * 1000\" grouping-separator=\",\" grouping-size=\"3\"/>|</xsl:for-each>");

            AssertAtEveryVersion("1234", "<xsl:for-each select=\"i\"><xsl:number/></xsl:for-each>");
        }

        [TestMethod]
        public void ANumberLongerThanTheStackSpaceIsWrittenWhole()
        {
            // Sixty-four characters are set aside. The prefix alone is eighty, and thirty levels of
            // numbering with a separator between each are another fifty-nine.
            string prefix = new string('<', 80);
            string levels = string.Join(", ", Enumerable.Range(1, 30));
            string format = prefix.Replace("<", "&lt;", StringComparison.Ordinal) + string.Join(".", Enumerable.Repeat("1", 30)) + "!";

            Assert.AreEqual(
                prefix + string.Join(".", Enumerable.Range(1, 30)) + "!",
                Writes($"<xsl:number value=\"{levels}\" format=\"{format}\"/>"));
        }

        // ---- format-integer --------------------------------------------------------------------------------

        [TestMethod]
        public void AnIntegerIsWrittenInTheDigitsOfItsPicture()
        {
            Assert.AreEqual("005", Value("format-integer(5, '000')"));
            Assert.AreEqual("-005", Value("format-integer(-5, '000')"));
            Assert.AreEqual("1,234,567", Value("format-integer(1234567, '#,##0')"));
            Assert.AreEqual("12.34.56", Value("format-integer(123456, '#.00.00')"));
            Assert.AreEqual("3rd", Value("format-integer(3, '1;o')"));
            Assert.AreEqual("0", Value("format-integer(0, '1')"));
            Assert.AreEqual("18446744073709551615", Value("format-integer(18446744073709551615, '1')"));

            Assert.AreEqual(
                "123,456,789,012,345,678,901,234,567,890",
                Value("format-integer(123456789012345678901234567890, '#,##0')"));

            // Osmanya digits again: a number written two chars to the digit.
            Assert.AreEqual(
                char.ConvertFromUtf32(0x104A0) + char.ConvertFromUtf32(0x104A4) + char.ConvertFromUtf32(0x104A2),
                Value("format-integer(42, '&#x104A0;&#x104A0;&#x104A0;')"));
        }

        [TestMethod]
        public void AnIntegerWiderThanTheStackSpaceIsWrittenWhole()
        {
            // Sixty-four characters are set aside, and this picture insists on ninety digits.
            Assert.AreEqual(new string('0', 88) + "42", Value($"format-integer(42, '{new string('0', 90)}')"));
        }

        // ---- encode-for-uri, iri-to-uri and escape-html-uri ------------------------------------------------

        [TestMethod]
        public void AUriIsEscapedOctetByOctet()
        {
            Assert.AreEqual("a%20b%2Fc%3Fd%3D%C3%A9%26e", Value("encode-for-uri('a b/c?d=&#233;&amp;e')"));
            Assert.AreEqual("http://x/a%20b?%C3%A9#f", Value("iri-to-uri('http://x/a b?&#233;#f')"));
            Assert.AreEqual("http://x/a b?%C3%A9#f", Value("escape-html-uri('http://x/a b?&#233;#f')"));

            // Four octets for a character outside the basic plane, and three for one high inside it.
            Assert.AreEqual("%F0%9F%98%80%E2%82%AC", Value("encode-for-uri('&#x1F600;&#x20AC;')"));

            Assert.AreEqual("%7F~-_.", Value("encode-for-uri('&#x7F;~-_.')"));
            Assert.AreEqual(string.Empty, Value("encode-for-uri('')"));
            Assert.AreEqual("0", Value("string-length(iri-to-uri(()))"));
        }

        [TestMethod]
        public void AUriWithNothingToEscapeIsItself()
        {
            Assert.AreEqual("abc-XYZ_0.9~", Value("encode-for-uri('abc-XYZ_0.9~')"));
            Assert.AreEqual("http://example.com/a/b?c=d&e#f", Value("iri-to-uri('http://example.com/a/b?c=d&amp;e#f')"));
            Assert.AreEqual("http://example.com/a b", Value("escape-html-uri('http://example.com/a b')"));
        }

        [TestMethod]
        public void AUriLongerThanTheStackSpaceIsEscapedWhole()
        {
            // Five hundred and twelve octets and two hundred and fifty-six characters are set aside. Three
            // hundred of a character that is two octets and six characters escaped is past both.
            string text = new string('é', 300);
            string stylesheet = Sheet("3.0", "<xsl:value-of select=\"encode-for-uri($t)\"/>|<xsl:value-of select=\"escape-html-uri(concat('a b', $t))\"/>",
                "<xsl:param name=\"t\"/>");

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                string written = new Xslt(
                    stylesheet,
                    new XsltOptions { Backend = backend, Parameters = new Dictionary<string, object?> { ["t"] = text } })
                    .TransformXml(Source);

                string escaped = string.Concat(Enumerable.Repeat("%C3%A9", 300));
                Assert.AreEqual(escaped + "|a b" + escaped, written, backend.ToString());
            }
        }

        // ---- the identity a value is grouped, keyed and told apart by ------------------------------------------

        [TestMethod]
        public void ValuesAreGroupedByWhatTheyAreAndNotByHowTheyAreSpelled()
        {
            const string Groups = "<xsl:value-of select=\"count(current-group())\"/>";

            // 2 and 2.0 are one number, and so one group.
            Assert.AreEqual("1,2,1,", Writes($"<xsl:for-each-group select=\"i\" group-by=\"number(@n)\">{Groups},</xsl:for-each-group>"));
            Assert.AreEqual("1,2,1,", Writes($"<xsl:for-each-group select=\"i\" group-by=\"xs:decimal(@n)\">{Groups},</xsl:for-each-group>"));
            Assert.AreEqual("1,1,1,1,", Writes($"<xsl:for-each-group select=\"i\" group-by=\"string(@n)\">{Groups},</xsl:for-each-group>"));
            Assert.AreEqual("2,1,1,", Writes($"<xsl:for-each-group select=\"i\" group-by=\"xs:date(@d)\">{Groups},</xsl:for-each-group>"));
            Assert.AreEqual("2,2,", Writes($"<xsl:for-each-group select=\"i\" group-by=\"@g = 'x'\">{Groups},</xsl:for-each-group>"));

            // Two dateTimes naming one instant in two timezones are one key, and one with no timezone
            // is another.
            Assert.AreEqual(
                "2,1,1,",
                Writes(
                    "<xsl:for-each-group select=\"(xs:dateTime('2024-03-05T12:00:00Z'), xs:dateTime('2024-03-05T14:00:00+02:00'), "
                    + $"xs:dateTime('2024-03-05T12:00:00'), xs:dateTime('2024-03-05T12:00:01Z'))\" group-by=\".\">{Groups},</xsl:for-each-group>"));

            // A name is its namespace and local part, whatever the prefix.
            Assert.AreEqual(
                "2,1,",
                Writes(
                    "<xsl:for-each-group select=\"(QName('urn:p', 'p:a'), QName('urn:p', 'q:a'), QName('urn:q', 'p:a'))\" "
                    + $"group-by=\".\">{Groups},</xsl:for-each-group>"));
        }

        [TestMethod]
        public void ACompositeKeyIsItsPartsAndNotTheirSpellingRunTogether()
        {
            const string Groups = "<xsl:value-of select=\"count(current-group())\"/>";

            Assert.AreEqual(
                "1,1,1,1,",
                Writes($"<xsl:for-each-group select=\"i\" group-by=\"@g, number(@n)\" composite=\"yes\">{Groups},</xsl:for-each-group>"));

            Assert.AreEqual(
                "2,1,1,",
                Writes($"<xsl:for-each-group select=\"i\" group-by=\"xs:date(@d), 1\" composite=\"yes\">{Groups},</xsl:for-each-group>"));

            // ("a", "bc") and ("ab", "c") spell the same run of letters and are two keys.
            Assert.AreEqual(
                "1,1,2,",
                Writes(
                    "<xsl:for-each-group select=\"1 to 4\" composite=\"yes\" "
                    + "group-by=\"if (. = 1) then ('a', 'bc') else if (. = 2) then ('ab', 'c') else ('abc', 2, true())\">"
                    + $"{Groups},</xsl:for-each-group>"));

            // Parts longer than the stack space set aside for one, and for the whole.
            string wide = new string('w', 200);

            Assert.AreEqual(
                "2,2,",
                Writes(
                    "<xsl:for-each-group select=\"1 to 4\" composite=\"yes\" "
                    + $"group-by=\"concat('{wide}', . mod 2), '{wide}', xs:date('2024-03-05')\">{Groups},</xsl:for-each-group>"));

            Assert.AreEqual(
                "alpha|gamma",
                Writes(
                    "<xsl:value-of select=\"key('k', ('x', 1))\"/>|<xsl:value-of select=\"key('k', ('x', 3))\"/>",
                    "<xsl:key name=\"k\" match=\"i\" use=\"@g, number(@n)\" composite=\"yes\"/>"));
        }

        [TestMethod]
        public void DistinctValuesAreToldApartByWhatTheyAre()
        {
            Assert.AreEqual("4", Value("count(distinct-values((1, 1.0, 1e0, 2, xs:float(2), '1', 0.5)))"));
            Assert.AreEqual("3", Value("count(distinct-values(//@n ! number()))"));
            Assert.AreEqual("4", Value("count(distinct-values(//@n))"));
            Assert.AreEqual("3", Value("count(distinct-values(//@d ! xs:date(.)))"));

            Assert.AreEqual(
                "2",
                Value("count(distinct-values((xs:dateTime('2024-03-05T12:00:00Z'), xs:dateTime('2024-03-05T14:00:00+02:00'), xs:dateTime('2024-03-05T12:00:01Z'))))"));

            Assert.AreEqual(
                "2",
                Value("count(distinct-values((xs:dayTimeDuration('PT60M'), xs:dayTimeDuration('PT1H'), xs:yearMonthDuration('P1Y'))))"));

            Assert.AreEqual("2", Value("count(distinct-values((12345678901234567890123, 12345678901234567890123, 1e23)))"));
            Assert.AreEqual("3", Value("count(distinct-values((true(), 'true', false(), true())))"));
            Assert.AreEqual("2", Value("count(distinct-values((xs:double('NaN'), xs:double('NaN'), xs:double('INF'))))"));
        }

        // ---- the content of an instruction, captured as a string ----------------------------------------------

        [TestMethod]
        public void AnAttributeIsTheStringOfItsContent()
        {
            AssertAtEveryVersion(
                "<o><a href=\"1\"/><a href=\"2\"/><a href=\"3\"/><a href=\"2.0\"/></o>",
                "<o><xsl:for-each select=\"i\"><a><xsl:attribute name=\"href\"><xsl:value-of select=\"@n\"/></xsl:attribute></a></xsl:for-each></o>",
                method: "xml");

            AssertAtEveryVersion(
                "<o><a href=\"p/1.html\" class=\"row\" none=\"\"/></o>",
                "<o><xsl:for-each select=\"i[1]\"><a><xsl:attribute name=\"href\">p/<xsl:value-of select=\"@n\"/>.html</xsl:attribute>"
                + "<xsl:attribute name=\"class\">row</xsl:attribute><xsl:attribute name=\"none\"/></a></xsl:for-each></o>",
                method: "xml");

            // Instructions in the content, and an element in it, whose text is all that is kept. XSLT
            // 1.0 had that an error, and the framework's processor still does.
            Assert.AreEqual(
                "<o><a v=\"alpha-first[1]\"/><a v=\"beta[2]\"/><a v=\"gamma[3]\"/><a v=\"delta[2.0]\"/></o>",
                Writes(
                    "<o><xsl:for-each select=\"i\"><a><xsl:attribute name=\"v\"><xsl:value-of select=\".\"/>"
                    + "<xsl:if test=\"position() = 1\">-first</xsl:if><b>[<xsl:value-of select=\"@n\"/>]</b></xsl:attribute></a></xsl:for-each></o>",
                    method: "xml"));
        }

        [TestMethod]
        public void ACommentAndAProcessingInstructionAreTheStringOfTheirContent()
        {
            AssertAtEveryVersion(
                "<o><!--c1--><?p d2?><!----><!--alpha and beta--></o>",
                "<o><xsl:comment>c<xsl:value-of select=\"i[1]/@n\"/></xsl:comment>"
                + "<xsl:processing-instruction name=\"p\">d<xsl:value-of select=\"i[2]/@n\"/></xsl:processing-instruction>"
                + "<xsl:comment/><xsl:comment><xsl:value-of select=\"i[1]\"/> and <xsl:value-of select=\"i[2]\"/></xsl:comment></o>",
                method: "xml");
        }

        [TestMethod]
        public void TheItemsOfCapturedContentAreJoinedByTheSeparator()
        {
            // Nothing goes between the items of content unless a separator says so, where a space goes
            // between the items of a select.
            Assert.AreEqual(
                "<o a=\"1,2,3\" b=\"123\" c=\"x12y\" d=\"alphabeta\" e=\"1 2 3\"/>",
                Writes(
                    "<o><xsl:attribute name=\"a\" separator=\",\"><xsl:sequence select=\"1 to 3\"/></xsl:attribute>"
                    + "<xsl:attribute name=\"b\"><xsl:sequence select=\"1 to 3\"/></xsl:attribute>"
                    + "<xsl:attribute name=\"c\">x<xsl:sequence select=\"1, 2\"/>y</xsl:attribute>"
                    + "<xsl:attribute name=\"d\"><xsl:value-of select=\"i[1]\"/><xsl:value-of select=\"i[2]\"/></xsl:attribute>"
                    + "<xsl:attribute name=\"e\" select=\"1 to 3\"/></o>",
                    method: "xml"));
        }

        [TestMethod]
        public void ContentCapturedInsideContentBeingCapturedIsKeptApart()
        {
            // The variable's tree has an attribute with content of its own, built while the outer
            // attribute's content is still being gathered: two captures at once, and the inner one must
            // neither see nor leave anything in the outer.
            Assert.AreEqual(
                "<o a=\"out[in1]after\"/>",
                Writes(
                    "<o><xsl:attribute name=\"a\">out<xsl:variable name=\"v\"><e><xsl:attribute name=\"b\">in<xsl:value-of select=\"i[1]/@n\"/>"
                    + "</xsl:attribute></e></xsl:variable>[<xsl:value-of select=\"$v/e/@b\"/>]after</xsl:attribute></o>",
                    method: "xml"));

            // Through a function called from the content, three deep.
            Assert.AreEqual(
                "<o a=\"1:(2:(3:(end)))\"/>",
                Writes(
                    "<o><xsl:attribute name=\"a\"><xsl:value-of select=\"p:nest(1)\"/></xsl:attribute></o>",
                    "<xsl:function name=\"p:nest\"><xsl:param name=\"n\"/><xsl:variable name=\"v\"><e>"
                    + "<xsl:attribute name=\"b\"><xsl:value-of select=\"$n\"/>:(<xsl:value-of select=\"if ($n &lt; 3) then p:nest($n + 1) else 'end'\"/>)"
                    + "</xsl:attribute></e></xsl:variable><xsl:sequence select=\"string($v/e/@b)\"/></xsl:function>",
                    method: "xml"));
        }

        [TestMethod]
        public void ContentThatFailsLeavesNothingBehindForTheNextCapture()
        {
            // The first attribute's content raises an error after writing 'lost', which the xsl:try
            // catches. The target that held 'lost' is lent again for the second, which must begin empty,
            // and again for every one after it.
            Assert.AreEqual(
                "<o><caught/><a v=\"kept\" w=\"1\"/><a v=\"kept\" w=\"2\"/></o>",
                Writes(
                    "<o><xsl:try><a><xsl:attribute name=\"v\">lost<b>also lost<xsl:value-of select=\"error()\"/></b></xsl:attribute></a>"
                    + "<xsl:catch><caught/></xsl:catch></xsl:try>"
                    + "<xsl:for-each select=\"i[position() &lt; 3]\"><a><xsl:attribute name=\"v\">kept</xsl:attribute>"
                    + "<xsl:attribute name=\"w\"><xsl:value-of select=\"@n\"/></xsl:attribute></a></xsl:for-each></o>",
                    method: "xml"));
        }

        // ---- what they allocate ----------------------------------------------------------------------------

        /// <summary>A thousand items, each with a number, a date and a text that needs escaping in a URI.</summary>
        private static readonly XdmTree Thousand = XdmTreeBuilder.FromXmlText(
            "<r>" + string.Concat(Enumerable.Range(1, 1000).Select(i => $"<i n=\"{i}\" q=\"a b/{i}\" g=\"plain{i}\">t</i>")) + "</r>");

        private static Xslt Each(string body, XsltBackend backend)
        {
            return new Xslt(
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                + "<xsl:output method=\"xml\"/>"
                + "<xsl:variable name=\"d\" select=\"xs:date('2024-03-05')\"/>"
                + "<xsl:variable name=\"dt\" select=\"xs:dateTime('2024-03-05T14:07:09')\"/>"
                + $"<xsl:template match=\"/r\"><o><xsl:for-each select=\"i\">{body}</xsl:for-each></o></xsl:template></xsl:stylesheet>",
                new XsltOptions { Backend = backend });
        }

        /// <summary>
        /// Bytes allocated by one transformation of the thousand items by each of several stylesheets,
        /// once all of them have settled, the results going to a writer that keeps none of them.
        /// </summary>
        /// <remarks>
        /// Writing an attribute and formatting a date both allocate ninety-six bytes a time in code the
        /// runtime has not yet optimised and none once it has, and it optimises in the background, some
        /// while after the code has been run often enough: twenty runs and the best of five read these
        /// a hundred bytes high, or did not, by what had run before them. So every stylesheet of a test
        /// is run, the runtime is given a moment, and that is repeated until what is asked of them
        /// holds or ten seconds have gone by. What is kept is the least each ever allocated, which only
        /// falls: waiting longer cannot make a stylesheet that allocates too much look as if it did not.
        /// </remarks>
        /// <param name="transforms">The stylesheets.</param>
        /// <param name="settled">Whether the figures so far are the ones hoped for.</param>
        private static long[] SettledBytes(IReadOnlyList<Xslt> transforms, Func<long[], bool> settled)
        {
            long[] best = new long[transforms.Count];
            Array.Fill(best, long.MaxValue);
            System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                for (int i = 0; i < transforms.Count; i++)
                {
                    for (int run = 0; run < 40; run++)
                    {
                        long before = GC.GetAllocatedBytesForCurrentThread();
                        transforms[i].Transform(Thousand, TextWriter.Null);
                        best[i] = Math.Min(best[i], GC.GetAllocatedBytesForCurrentThread() - before);
                    }
                }

                if (settled(best) || waited.Elapsed.TotalSeconds > 10)
                {
                    return best;
                }

                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// Asserts how many bytes each of several bodies allocates each time it runs, over what another
        /// body does, on both backends.
        /// </summary>
        private static void AssertBytesEach(params (int AtMost, string Body, string Floor)[] cases)
        {
            List<Xslt> transforms = new List<Xslt>();

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                foreach ((int _, string body, string floor) in cases)
                {
                    transforms.Add(Each(body, backend));
                    transforms.Add(Each(floor, backend));
                }
            }

            long[] bytes = SettledBytes(
                transforms,
                figures =>
                {
                    for (int i = 0; i < figures.Length; i += 2)
                    {
                        if ((figures[i] - figures[i + 1]) / 1000 > cases[(i / 2) % cases.Length].AtMost)
                        {
                            return false;
                        }
                    }

                    return true;
                });

            int at = 0;

            foreach (XsltBackend backend in new[] { XsltBackend.Interpreted, XsltBackend.Compiled })
            {
                foreach ((int atMost, string body, string _) in cases)
                {
                    long each = (bytes[at] - bytes[at + 1]) / 1000;
                    at += 2;

                    Assert.IsLessThanOrEqualTo(
                        atMost, each, $"'{body}' allocates {each} bytes each time on the {backend} backend");
                }
            }
        }

        [TestMethod]
        public void FormattingADateCostsItsResultAndTheNamesItReads()
        {
            // Ten characters are a string of forty-eight bytes and eight of forty. A builder and its
            // buffer were a hundred and twelve on top of either.
            AssertBytesEach(
                (88, "<xsl:value-of select=\"format-date($d, '[Y0001]-[M01]-[D01]')\"/>", string.Empty),
                (48, "<xsl:value-of select=\"format-dateTime($dt, '[H01]:[m01]:[s01]')\"/>", string.Empty));
        }

        [TestMethod]
        public void NumberingCostsNoBuilder()
        {
            // The list of numbers and its array are what is left, and the string written. It was a
            // hundred and four bytes more.
            AssertBytesEach(
                (160, "<xsl:number/>", string.Empty),
                (40, "<xsl:value-of select=\"format-integer(position(), '000')\"/>", string.Empty),
                (48, "<xsl:value-of select=\"format-integer(position() * 1000, '#,##0')\"/>", string.Empty));
        }

        [TestMethod]
        public void EscapingAUriCostsItsResultAndNothingWhereThereIsNothingToEscape()
        {
            // Over the same attribute read by a function that allocates nothing of its own. The result
            // of escaping 'a b/1000' is a string of fourteen characters; it was that, a builder, its
            // buffer, an array of octets and a string for each of the two octets escaped.
            AssertBytesEach(
                (56, "<xsl:value-of select=\"encode-for-uri(@q)\"/>", "<xsl:value-of select=\"string-length(@q)\"/>"),
                (8, "<xsl:value-of select=\"encode-for-uri(@g)\"/>", "<xsl:value-of select=\"string-length(@g)\"/>"),
                (8, "<xsl:value-of select=\"escape-html-uri(@q)\"/>", "<xsl:value-of select=\"string-length(@q)\"/>"));
        }

        [TestMethod]
        public void CapturingContentThatIsOneStringAllocatesNothing()
        {
            // Two hundred and eighty bytes for each of these: the target, its list, the list's array,
            // its builder, the builder's buffer and a copy of a string that was already there.
            // The last has two pieces, which are joined, and the join is the one string made:
            // 'p/1000.html' is eleven characters.
            AssertBytesEach(
                (8, "<a><xsl:attribute name=\"href\"><xsl:value-of select=\"@n\"/></xsl:attribute></a>", "<a/>"),
                (8, "<a><xsl:attribute name=\"class\">row</xsl:attribute></a>", "<a/>"),
                (8, "<xsl:comment>c</xsl:comment>", string.Empty),
                (8, "<xsl:processing-instruction name=\"p\">d</xsl:processing-instruction>", string.Empty),
                (56, "<a><xsl:attribute name=\"href\">p/<xsl:value-of select=\"@n\"/>.html</xsl:attribute></a>", "<a/>"));
        }

        private static readonly Func<XPathValue, string> KeyIdentity = BindKeyIdentity();

        private static Func<XPathValue, string> BindKeyIdentity()
        {
            Assembly library = typeof(Xslt).Assembly;

            MethodInfo method = library.GetType("CodeDeeds.Xslt.Compiler.ForEachGroupInstruction")!
                .GetMethod("KeyIdentity", BindingFlags.NonPublic | BindingFlags.Static)!;

            ParameterExpression key = Expression.Parameter(typeof(XPathValue));

            return Expression.Lambda<Func<XPathValue, string>>(
                Expression.Call(method, key, Expression.Constant(null, method.GetParameters()[1].ParameterType)), key).Compile();
        }

        /// <summary>Bytes allocated making the identity of one key, the least of many makings.</summary>
        private static long BytesToIdentify(XPathValue key)
        {
            for (int i = 0; i < 2000; i++)
            {
                KeyIdentity(key);
            }

            long best = long.MaxValue;

            for (int round = 0; round < 20; round++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();

                for (int i = 0; i < 100; i++)
                {
                    KeyIdentity(key);
                }

                best = Math.Min(best, (GC.GetAllocatedBytesForCurrentThread() - before) / 100);
            }

            return best;
        }

        [TestMethod]
        public void TheIdentityOfAKeyIsOneString()
        {
            Assert.AreEqual("n:4.5", KeyIdentity(XPathValue.FromNumber(4.5)));
            Assert.AreEqual("b:1", KeyIdentity(XPathValue.FromBoolean(true)));
            Assert.AreEqual("s:text", KeyIdentity(XPathValue.FromString("text")));

            // A number was its digits as a string and then the identity; a date, a string for the type,
            // one for each part of the moment, the moment and then the identity.
            Assert.IsLessThanOrEqualTo(32, BytesToIdentify(XPathValue.FromNumber(4.5)));
            Assert.AreEqual(0, BytesToIdentify(XPathValue.FromBoolean(true)));
            Assert.IsLessThanOrEqualTo(40, BytesToIdentify(XPathValue.FromString("text")));

            Assert.AreEqual(
                XdmDateTime.Reading.Value,
                XdmDateTime.Read("2024-03-05T12:00:00Z", XdmTypeCode.DateTime, out XdmDateTime moment));

            XPathValue date = XPathValue.FromDateTime(moment);
            string identity = KeyIdentity(date);

            Assert.IsTrue(identity.StartsWith("d:", StringComparison.Ordinal), identity);
            Assert.IsLessThanOrEqualTo(24 + (2 * identity.Length) + 8, BytesToIdentify(date));
        }
    }
}
