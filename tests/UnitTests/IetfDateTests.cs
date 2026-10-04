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

        // ---- how the parts are put together ----------------------------------------------------------------

        [TestMethod]
        public void AFractionOfAnyLengthIsRead()
        {
            // The parts are written out as a lexical xs:dateTime in forty-eight characters of stack and
            // read back from there. Twenty-five of them are the date, the time and the offset, so a
            // fraction of twenty-two digits is the longest that fits and one of twenty-three is the
            // first that makes the buffer grow; what is read is the same either side of that.
            string twentyTwo = "1234567890123456789012";

            Assert.AreEqual("2014-08-20T19:36:01.1234567Z", Parsed($"Wed, 20 Aug 2014 19:36:01.{twentyTwo} GMT"));
            Assert.AreEqual("2014-08-20T19:36:01.1234567Z", Parsed($"Wed, 20 Aug 2014 19:36:01.{twentyTwo}3 GMT"));
            Assert.AreEqual("2014-08-20T19:36:01.1234567Z", Parsed($"Aug 20 14:36:01.{twentyTwo}{twentyTwo}{twentyTwo} -05:00 (EST) 2014"));
            Assert.AreEqual("1914-08-21T05:06:01.5Z", Parsed("Aug 20 19:36:01.5 -0930 14"));
        }

        private delegate bool Reader(string text, out XPath.XdmDateTime result);

        private static readonly Reader TryParse = typeof(Xslt).Assembly
            .GetType("CodeDeeds.Xslt.XPath.IetfDate")!
            .GetMethod("TryParse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .CreateDelegate<Reader>();

        /// <summary>Bytes allocated reading one date, the least of several thousand readings.</summary>
        private static long BytesToRead(string written)
        {
            for (int i = 0; i < 2000; i++)
            {
                Assert.IsTrue(TryParse(written, out _), written);
            }

            long best = long.MaxValue;

            for (int round = 0; round < 20; round++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();

                for (int i = 0; i < 100; i++)
                {
                    TryParse(written, out _);
                }

                best = Math.Min(best, (GC.GetAllocatedBytesForCurrentThread() - before) / 100);
            }

            return best;
        }

        [TestMethod]
        public void PuttingThePartsTogetherAllocatesNothing()
        {
            // What a reading allocates is the names it folds to lower case, and a name already in lower
            // case is one string of three letters: thirty-two bytes for the month here, there being no
            // day name and no timezone. Putting the parts together was a builder, its buffer, a string
            // for each of eight numbers and a string of the whole, 480 bytes on top of that.
            Assert.IsLessThanOrEqualTo(32, BytesToRead("20 aug 2014 19:36:01"));

            // In asctime order the month is read twice, once to find that it is not the name of a day.
            Assert.IsLessThanOrEqualTo(64, BytesToRead("aug 20 19:36 2014"));

            // A fraction is cut out of the text as a string of its own, and an offset is none.
            Assert.IsLessThanOrEqualTo(64, BytesToRead("20 aug 2014 19:36:01.25 -0500"));
        }

        // ---- the lexical form, read from characters that are not a string --------------------------------------

        [TestMethod]
        public void CharactersAreReadAsTheStringOfThemIs()
        {
            (string Text, XPath.XdmTypeCode Type)[] cases =
            {
                ("2014-08-20T19:36:01Z", XPath.XdmTypeCode.DateTime),
                ("2014-08-20T19:36:01.25-05:00", XPath.XdmTypeCode.DateTime),
                ("  2014-08-20T24:00:00  ", XPath.XdmTypeCode.DateTime),
                ("-0044-03-15T12:00:00", XPath.XdmTypeCode.DateTime),
                ("2014-08-20", XPath.XdmTypeCode.Date),
                ("2014-08-20+14:00", XPath.XdmTypeCode.Date),
                ("19:36:01", XPath.XdmTypeCode.Time),
                ("24:00:00Z", XPath.XdmTypeCode.Time),
                ("2014-02-29T00:00:00", XPath.XdmTypeCode.DateTime),
                ("2014-08-20", XPath.XdmTypeCode.DateTime),
                ("19:36:01", XPath.XdmTypeCode.Date),
                ("2014-08-20T19:36:01+15:00", XPath.XdmTypeCode.DateTime),
                ("", XPath.XdmTypeCode.DateTime),
                ("   ", XPath.XdmTypeCode.Date),
                ("999999999999-01-01T00:00:00", XPath.XdmTypeCode.DateTime),
            };

            foreach ((string text, XPath.XdmTypeCode type) in cases)
            {
                XPath.XdmDateTime.Reading ofString = XPath.XdmDateTime.Read(text, type, out XPath.XdmDateTime fromString);

                // The characters stand in the middle of a longer buffer, so that nothing can be read
                // from either side of them by mistake.
                char[] buffer = ("#" + text + "#").ToCharArray();
                XPath.XdmDateTime.Reading ofCharacters = XPath.XdmDateTime.Read(
                    buffer.AsSpan(1, text.Length), type, out XPath.XdmDateTime fromCharacters);

                Assert.AreEqual(ofString, ofCharacters, $"'{text}' as {type}");
                Assert.AreEqual(fromString.ToString(), fromCharacters.ToString(), $"'{text}' as {type}");
                Assert.AreEqual(fromString, fromCharacters, $"'{text}' as {type}");
            }

            Assert.AreEqual(
                XPath.XdmDateTime.Reading.NotLexical,
                XPath.XdmDateTime.Read(ReadOnlySpan<char>.Empty, XPath.XdmTypeCode.DateTime, out _));

            Assert.AreEqual(
                XPath.XdmDateTime.Reading.NotLexical,
                XPath.XdmDateTime.Read((string)null!, XPath.XdmTypeCode.DateTime, out _));
        }

        [TestMethod]
        public void TheExampleOnReadingFromCharactersRuns()
        {
            ReadOnlySpan<char> header = "Date: 2014-08-20T19:36:01Z";

            Assert.AreEqual(
                XPath.XdmDateTime.Reading.Value,
                XPath.XdmDateTime.Read(header[6..], XPath.XdmTypeCode.DateTime, out XPath.XdmDateTime sent));

            Assert.AreEqual(2014, sent.Year);
            Assert.AreEqual("2014-08-20T19:36:01Z", sent.ToString());
        }
    }
}
