namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for dates outside the years <see cref="System.DateTime"/> holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XML Schema puts no bound on the year. <c>-1999-05-31</c> is a date and so is <c>654321-01-01</c>,
    /// and <see cref="System.DateTime"/> reaches neither, beginning at the common era and ending at 9999.
    /// A value is held here as a <em>proxy</em> date whose year stands in for the real one, the real year
    /// being a whole number of 400-year cycles away; the proleptic Gregorian calendar repeats exactly over
    /// that span, so the proxy shares the value's month, day, time of day, day of week and day of year.
    /// </para>
    /// <para>
    /// What the cases below are watching for is the proxy showing through: a year written as its stand-in,
    /// two dates four hundred years apart taken for one another, a weekday that belongs to the proxy, or
    /// arithmetic that counts the years of one and the days of the other.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class DateRangeTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
            + " xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\""
            + " exclude-result-prefixes=\"xs map\"";

        private static string Writes(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        private static void Refuses(string code, string expression)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression));
            Assert.AreEqual(code, error.Code, $"'{expression}': {error.Message}");
        }

        // ---- Reading and writing ---------------------------------------------------------------------------

        [TestMethod]
        [DataRow("-1999-05-31")]
        [DataRow("-0001-01-01")]
        [DataRow("-0012-12-03")]
        [DataRow("0001-01-01")]
        [DataRow("9999-12-31")]
        [DataRow("10000-01-01")]
        [DataRow("654321-01-01")]
        [DataRow("999999999-12-31")]
        [DataRow("-999999999-01-01")]
        public void ADateOutsideTheCommonEraIsReadAndWrittenBack(string written)
        {
            // The canonical form of a date is the date, so anything that came in canonical goes back out
            // unchanged. A proxy year showing through would appear right here.
            Assert.AreEqual(written, Writes($"xs:date('{written}')"));
        }

        [TestMethod]
        [DataRow("-1999-05-31T13:20:00+14:00")]
        [DataRow("-0012-12-03T00:00:00-05:00")]
        [DataRow("654321-01-01T23:59:59.5Z")]
        public void ADateTimeOutsideTheCommonEraIsReadAndWrittenBack(string written)
        {
            Assert.AreEqual(written, Writes($"xs:dateTime('{written}')"));
        }

        [TestMethod]
        public void TheYearComesBackAsItWasWritten()
        {
            // Held on a continuous timeline, where 1 BCE is year zero, but answered in the spelling XML
            // Schema 1.0 uses, where it is -0001. The one place the two numberings must not be confused.
            Assert.AreEqual("-1999", Writes("year-from-dateTime(xs:dateTime('-1999-05-31T00:20:00-05:00'))"));
            Assert.AreEqual("-2", Writes("year-from-date(xs:date('-0002-06-01'))"));
            Assert.AreEqual("-1", Writes("year-from-date(xs:date('-0001-12-31'))"));
            Assert.AreEqual("1", Writes("year-from-date(xs:date('0001-01-01'))"));
            Assert.AreEqual("654321", Writes("year-from-date(xs:date('654321-01-01'))"));

            // And the other components are the proxy's own, which are the value's own.
            Assert.AreEqual("5", Writes("month-from-date(xs:date('-1999-05-31'))"));
            Assert.AreEqual("31", Writes("day-from-date(xs:date('-1999-05-31'))"));
        }

        [TestMethod]
        public void AYearTooBigToHoldIsStillAnOverflowAndNotATypo()
        {
            Refuses("FODT0001", "xs:date('1000000000-01-01')");
            Refuses("FODT0001", "xs:date('-1000000000-01-01')");

            // And the form is still checked first, so a bad day in an unholdable year is a bad day.
            Refuses("FORG0001", "xs:date('25252734927766554-02-30')");
            Refuses("FORG0001", "xs:date('-2004-13-01')");
        }

        // ---- The calendar ----------------------------------------------------------------------------------

        [TestMethod]
        public void ThereIsNoYearZeroAndTheDayBeforeYearOneSaysSo()
        {
            // XML Schema 1.0 counts 1 BCE as -0001 and has no year zero at all, so -0001-12-31 is the day
            // immediately before 0001-01-01. A representation that left a year's gap between them — or that
            // invented a year 0000 to fill it — would answer with something other than a single day.
            Assert.AreEqual("P1D", Writes("xs:date('0001-01-01') - xs:date('-0001-12-31')"));
            Refuses("FORG0001", "xs:date('0000-01-01')");
        }

        [TestMethod]
        public void ALeapYearBeforeTheCommonEraFollowsTheProlepticGregorianRule()
        {
            // 1 BCE is a leap year: written -0001, it is year zero on the continuous timeline, and zero is
            // divisible by 400. The written digits are not the ones the rule is applied to — 4 BCE is
            // written -0004 and is year -3 counted continuously, which is no leap year at all.
            Assert.AreEqual("-0001-02-29", Writes("xs:date('-0001-02-29')"));
            Refuses("FORG0001", "xs:date('-0004-02-29')");
            Assert.AreEqual("-0005-02-29", Writes("xs:date('-0005-02-29')"));

            // 400 years is the whole of the cycle, so the leap rule repeats across it exactly.
            Assert.AreEqual("true", Writes("xs:date('-0401-02-29') eq xs:date('-0401-02-29')"));
        }

        [TestMethod]
        public void TheDayOfTheWeekIsTheValueOwnAndNotTheProxyOne()
        {
            // The 400-year cycle is 146,097 days, which divides by seven, so two dates a whole number of
            // cycles apart fall on the same weekday. That is what lets a proxy stand in for a year at all,
            // and it is worth pinning: an off-by-one cycle would show up here and nowhere else.
            const string Picture = "'[FNn]'";

            Assert.AreEqual("Saturday", Writes($"format-date(xs:date('2000-01-01'), {Picture})"));
            Assert.AreEqual("Saturday", Writes($"format-date(xs:date('1600-01-01'), {Picture})"));
            Assert.AreEqual("Saturday", Writes($"format-date(xs:date('2400-01-01'), {Picture})"));
            Assert.AreEqual("Thursday", Writes($"format-date(xs:date('1970-01-01'), {Picture})"));

            // And the same holds going back past the common era, where the proxy is doing the work. A
            // whole cycle before 2000 is year -400 on the continuous timeline, which is spelled -0401.
            Assert.AreEqual(
                Writes($"format-date(xs:date('2000-01-01'), {Picture})"),
                Writes($"format-date(xs:date('-0401-01-01'), {Picture})"));
        }

        [TestMethod]
        public void TheGregorianPartsOfADateCarryItsYearToo()
        {
            // xs:gYear and xs:gYearMonth take the year off a date, and it is the written year they must take:
            // a proxy leaking here would answer with a year four centuries out and look entirely plausible.
            Assert.AreEqual("-1999", Writes("xs:date('-1999-05-31') cast as xs:gYear"));
            Assert.AreEqual("-1999-05", Writes("xs:date('-1999-05-31') cast as xs:gYearMonth"));
            Assert.AreEqual("654321-01", Writes("xs:date('654321-01-01') cast as xs:gYearMonth"));
        }

        [TestMethod]
        public void FormatDateWritesTheYearAndTheEraOfTheValue()
        {
            Assert.AreEqual("1999", Writes("format-date(xs:date('-1999-05-31'), '[Y]')"));
            Assert.AreEqual("654321", Writes("format-date(xs:date('654321-01-01'), '[Y]')"));
            Assert.AreEqual("54321", Writes("format-date(xs:date('654321-01-01'), '[Y#0,2-5]')"));
        }

        // ---- Arithmetic ------------------------------------------------------------------------------------

        [TestMethod]
        public void MovingBackPastTheCommonEraCrossesIntoTheYearBeforeOne()
        {
            // Every one of these is a W3C case, and each accepts the answer XML Schema 1.1 would give as
            // well. This engine reads by 1.0 rules, so it is the spelling with no year zero that comes out.
            Assert.AreEqual(
                "-0001-12-20Z",
                Writes("xs:date('0001-01-01Z') + xs:dayTimeDuration('-P11DT02H02M')"));

            Assert.AreEqual(
                "-0021-06-01Z",
                Writes("xs:date('0001-01-01Z') + xs:yearMonthDuration('-P20Y07M')"));

            Assert.AreEqual(
                "-0062-01-01Z",
                Writes("xs:date('1970-01-01Z') - xs:yearMonthDuration('P2030Y12M')"));
        }

        [TestMethod]
        public void MonthsAreAddedAsMonthsAcrossTheEraBoundary()
        {
            // A day past the end of the month it lands in comes back to that month's last day, and that
            // rule has to keep working where the year is a stand-in.
            Assert.AreEqual("-0001-02-29", Writes("xs:date('-0002-01-31') + xs:yearMonthDuration('P13M')"));
            Assert.AreEqual("0001-01-31", Writes("xs:date('-0001-01-31') + xs:yearMonthDuration('P1Y')"));
        }

        [TestMethod]
        public void SubtractingTwoMomentsCountsTheDaysBetweenThem()
        {
            Assert.AreEqual("P731D", Writes("xs:date('0001-01-01') - xs:date('-0002-01-01')"));
            Assert.AreEqual(
                "P365D", Writes("xs:date('654322-01-01') - xs:date('654321-01-01')"));

            // The far ends of the range subtract without overflowing, the days being counted apart from the
            // ticks within them.
            Assert.AreEqual(
                "true",
                Writes("(xs:date('999999999-12-31') - xs:date('-999999999-01-01')) gt xs:dayTimeDuration('P700000000000D')"));
        }

        [TestMethod]
        public void MovingOffTheEndOfTheRangeIsAnOverflow()
        {
            Refuses("FODT0001", "xs:date('999999999-12-31') + xs:yearMonthDuration('P1Y')");
            Refuses("FODT0001", "xs:date('-999999999-01-01') - xs:dayTimeDuration('P1D')");
        }

        // ---- Telling values apart --------------------------------------------------------------------------

        [TestMethod]
        public void TwoDatesAWholeCycleApartAreDifferentValues()
        {
            // They share a proxy, so anything keyed on the proxy alone would take them for one another:
            // comparison, a map key, a grouping key and distinct-values all have to see past it.
            Assert.AreEqual("false", Writes("xs:date('2000-01-01') eq xs:date('2400-01-01')"));
            Assert.AreEqual("true", Writes("xs:date('2000-01-01') lt xs:date('2400-01-01')"));
            Assert.AreEqual("true", Writes("xs:date('-0400-01-01') lt xs:date('0001-01-01')"));

            Assert.AreEqual(
                "2",
                Writes("count(distinct-values((xs:date('2000-01-01'), xs:date('2400-01-01'))))"));

            Assert.AreEqual(
                "2",
                Writes("map:size(map{xs:date('2000-01-01'): 1, xs:date('2400-01-01'): 2})"));

            Assert.AreEqual(
                "2",
                Writes("map:size(map{xs:date('-0400-01-01'): 1, xs:date('0001-01-01'): 2})"));
        }

        [TestMethod]
        public void ATimezoneIsStillReadPastTheEraBoundary()
        {
            // The sign of a negative year and the sign of an offset are the same character, and the offset
            // is taken off the end first so that the two are never confused.
            Assert.AreEqual("-0012-12-03-05:00", Writes("xs:date('-0012-12-03-05:00')"));
            Assert.AreEqual("-0012-12-03Z", Writes("adjust-date-to-timezone(xs:date('-0012-12-03'), xs:dayTimeDuration('PT0S'))"));
            Assert.AreEqual(
                "true",
                Writes("xs:dateTime('-1999-05-31T13:20:00+14:00') lt xs:dateTime('-1999-05-31T13:20:00Z')"));
        }
    }
}
