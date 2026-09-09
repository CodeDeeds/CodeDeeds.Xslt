namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:parse-ietf-date</c>, which reads the date formats email and HTTP headers are
    /// written in.
    /// </summary>
    /// <remarks>
    /// The specification gives a grammar rather than a list of formats, and it is wider than RFC 5322's, so
    /// these are grouped by which part of the grammar each is about rather than by the header each came from.
    /// </remarks>
    [TestClass]
    public sealed class IetfDateTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private static string Writes(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) =>
                new XsltOptions { Backend = backend, OmitXmlDeclaration = true };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        /// <summary>Reads a date and writes the moment it names, moved to UTC so one form is compared.</summary>
        private static string Parsed(string written)
        {
            return Writes(
                $"adjust-dateTime-to-timezone(parse-ietf-date('{written}'), xs:dayTimeDuration('PT0S'))");
        }

        private static void Refuses(string written, string why)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Parsed(written), why);
            Assert.AreEqual("FORG0010", error.Code, why);
        }

        [TestMethod]
        [DataRow("Wed, 20 Aug 2014 19:36:01 GMT")]
        [DataRow("  Wed, 20 Aug 2014 19:36:01 GMT   ")]
        [DataRow("Wed 20 Aug 2014 19:36:01 GMT")]
        [DataRow("Wednesday, 20 Aug 2014 19:36:01 GMT")]
        [DataRow("20 Aug 2014 19:36:01 GMT")]
        [DataRow("wed, 20 aug 2014 19:36:01 gmt")]
        [DataRow("Wed, 20 Aug 2014 19:36:01GMT")]
        [DataRow("Wed, 20 Aug 2014 19:36:01")]
        [DataRow("Wed, 20 - Aug - 2014 19:36:01")]
        [DataRow("Wed, 20- Aug- 2014 19:36:01")]
        [DataRow("Aug 20 19:36:01 2014")]
        [DataRow("Aug-20 19:36:01 2014")]
        public void OneMomentWrittenEveryWayTheGrammarAllows(string written)
        {
            // The day name is optional, may be spelled out, and its comma is optional; the separators may be
            // hyphens; the timezone may be absent, and where it is absent the moment is UTC; and the whole
            // may be written in asctime order with the year last.
            Assert.AreEqual("2014-08-20T19:36:01Z", Parsed(written));
        }

        [TestMethod]
        public void TheDayNameIsReadButNotChecked()
        {
            // The twentieth of August 2014 was a Wednesday. The specification asks for every one of these to
            // be accepted anyway, so the name is read to get past it and then dropped.
            foreach (string day in new[] { "Sun", "Mon", "Tue", "Thu", "Fri", "Sat", "Tuesday" })
            {
                Assert.AreEqual("2014-08-20T19:36:01Z", Parsed($"{day}, 20 Aug 2014 19:36:01 GMT"), day);
            }
        }

        [TestMethod]
        public void SecondsAndTheirFractionAreOptional()
        {
            Assert.AreEqual("2014-08-20T19:36:00Z", Parsed("Wed, 20 Aug 2014 19:36 GMT"));
            Assert.AreEqual("2014-08-20T19:36:01.25Z", Parsed("Wed, 20 Aug 2014 19:36:01.25 GMT"));

            // The hour may be written with one digit, and midnight may be written as the hour before it.
            Assert.AreEqual("2014-08-20T09:36:01Z", Parsed("Wed, 20 Aug 2014 9:36:01 GMT"));
            Assert.AreEqual("2014-08-21T00:00:00Z", Parsed("Aug 20 24:00:00 2014"));
        }

        [TestMethod]
        [DataRow("Aug 20 14:36:01 -05:00 2014")]
        [DataRow("Aug 20 14:36:01 -5:00 2014")]
        [DataRow("Aug 20 14:36:01 -500 2014")]
        [DataRow("Aug 20 14:36:01 -0500 2014")]
        [DataRow("Aug 20 14:36:01 EST 2014")]
        [DataRow("Aug 20 14:36:01-05:00(GMT) 2014")]
        [DataRow("Aug 20 14:36:01 -05:00  (  EST  ) 2014")]
        public void AnOffsetMayBeWrittenSeveralWays(string written)
        {
            // Counting the digits before reading them is what gets '-500' right: read greedily, two digits
            // of hours would leave one of minutes where the grammar wants two.
            Assert.AreEqual("2014-08-20T19:36:01Z", Parsed(written));
        }

        [TestMethod]
        public void AnOffsetMayLeaveItsMinutesOut()
        {
            Assert.AreEqual("2014-08-20T19:36:01Z", Parsed("Aug 20 14:36:01 -5 2014"));

            // Including after a colon, which may stand with nothing behind it.
            Assert.AreEqual("1902-02-02T04:02:00Z", Parsed("Feb-02 02:02-02: 02"));
        }

        [TestMethod]
        public void AYearOfTwoDigitsIsInTheTwentiethCentury()
        {
            Assert.AreEqual("1914-08-20T19:36:01Z", Parsed("Aug-20 14:36:01-05(EST) 14"));
            Assert.AreEqual("1999-08-20T19:36:01Z", Parsed("Aug 20 19:36:01 GMT 99"));
        }

        [TestMethod]
        [DataRow("2014-08-20T19:36:01Z", "a lexical xs:dateTime is not one of these forms")]
        [DataRow("", "nothing at all")]
        [DataRow("Wed, 020 Aug 2014 19:36:01 GMT", "three digits of day")]
        [DataRow("Wed, 20 August 2014 19:36:01 GMT", "a month spelled out")]
        [DataRow("Wed, 20 Aug 114 19:36:01 GMT", "three digits of year")]
        [DataRow("Wed, 20 Aug 2014 19:36:01 CET", "a timezone name outside the list")]
        [DataRow("Mon, 32 Aug 2014 19:36:01 GMT", "a day past the end of the month")]
        [DataRow("Wed, 00 Aug 2014 19:36:01 GMT", "a day before the start of it")]
        [DataRow("Sat, 29 Feb 2014 19:36:01 GMT", "a leap day in a common year")]
        [DataRow("Boy 20 Aug 2014 19:36:01 GMT", "no month of that name")]
        [DataRow("Wed,20 Aug 2014 19:36:01", "no space after the comma")]
        [DataRow("20Aug 2014 19:36:01", "no separator after the day")]
        [DataRow("Aug,20 19:36:01 2014", "a comma after the month")]
        [DataRow("Aug 20 19:3:01GMT 2014", "one digit of minutes")]
        [DataRow("Aug 20 19:36:0.1GMT 2014", "one digit of seconds")]
        [DataRow("Aug 20 19:36:01.GMT 2014", "a point with no fraction after it")]
        [DataRow("Aug 20 19.36.01GMT 2014", "points where the colons go")]
        [DataRow("Aug 20 29:36:01GMT 2014", "an hour past the end of the day")]
        [DataRow("Aug 20 19:66:01GMT 2014", "minutes past the end of the hour")]
        [DataRow("Aug 20 19:36:01 -15:00 2014", "an offset past the end of the world")]
        [DataRow("Aug 20 19:36:01 -05:60 2014", "an offset with too many minutes")]
        [DataRow("Aug 20 19:36:01 -05:0 2014", "an offset with one digit of minutes")]
        [DataRow("Aug 20 19:36:01 -05:00 EST 2014", "a timezone name after an offset, unbracketed")]
        [DataRow("Aug 20 19:36:01 -05:00 (CET) 2014", "a bracketed name outside the list")]
        [DataRow("Aug 20 19:36(EST) 2014", "a bracketed name with no offset in front of it")]
        [DataRow("Wed, 20 Aug 2014 19:36:01 GMT Manchester", "words after the date")]
        [DataRow("Aug 20 19:36:01GMT2014", "no space before the year")]
        public void TextThatNamesNoDateIsRefused(string written, string why)
        {
            Refuses(written, why);
        }

        [TestMethod]
        public void NothingInIsNothingOut()
        {
            // Declared xs:string? -> xs:dateTime?, so an absent argument is not a date that failed to parse.
            Assert.AreEqual(string.Empty, Writes("parse-ietf-date(())"));
        }
    }
}
