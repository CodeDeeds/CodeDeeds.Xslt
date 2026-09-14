namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests that a date moved by a duration comes back a date, and a time a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The specification puts these operations the long way round on purpose. Adding a
    /// <c>xs:dayTimeDuration</c> to an <c>xs:date</c> is defined as taking the date as a dateTime at
    /// midnight, moving that, and casting the result back to <c>xs:date</c>; adjusting a date to a timezone
    /// is defined the same way. The cast at the end is not decoration — it is what drops the hours the
    /// duration carried, and without it the value keeps a time of day that its own type has no room for.
    /// </para>
    /// <para>
    /// Such a value is quietly wrong rather than visibly so: it prints as the right date, because a date
    /// prints no time, and then compares unequal to that same date. <c>xs:date("1999-08-12") +
    /// xs:dayTimeDuration("P23DT09H32M59S")</c> wrote itself as <c>1999-09-04</c> and was not
    /// <c>xs:date("1999-09-04")</c>. Every case here is drawn from the W3C QT3 suite.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class DateArithmeticTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

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

        // ---- A date keeps no time of day -------------------------------------------------------------------

        [TestMethod]
        public void ADateMovedByHoursIsStillADateAndHoldsNoHours()
        {
            // The hours move the date and are then gone. Comparing is what catches this: the written form
            // would have looked right either way, a date having nowhere to print a time.
            Assert.AreEqual(
                "true",
                Writes("xs:date('1999-08-12') + xs:dayTimeDuration('P23DT09H32M59S') eq xs:date('1999-09-04')"));

            Assert.AreEqual(
                "true",
                Writes("xs:dayTimeDuration('P23DT09H32M59S') + xs:date('1999-08-12') eq xs:date('1999-09-04')"));

            Assert.AreEqual(
                "true",
                Writes("xs:date('1999-08-12') - xs:dayTimeDuration('P23DT09H32M59S') eq xs:date('1999-07-19')"));

            // Hours short of a whole day still move the day where they cross midnight, and still leave none
            // behind: midnight less an hour is the day before, not that day at 23:00.
            Assert.AreEqual(
                "true",
                Writes("xs:date('1999-08-12') - xs:dayTimeDuration('PT1H') eq xs:date('1999-08-11')"));

            Assert.AreEqual("P1D", Writes("(xs:date('1999-08-12') + xs:dayTimeDuration('PT25H')) - xs:date('1999-08-12')"));
        }

        [TestMethod]
        public void ADateTimeKeepsBothHalves()
        {
            // The same arithmetic on the type that does have room for the hours keeps them, which is the
            // other half of the rule and the thing a blanket truncation would have broken.
            Assert.AreEqual(
                "1999-09-04T09:32:59",
                Writes("xs:dateTime('1999-08-12T00:00:00') + xs:dayTimeDuration('P23DT09H32M59S')"));
        }

        // ---- A time keeps no day ---------------------------------------------------------------------------

        [TestMethod]
        public void ATimeMovedPastMidnightWrapsAroundTheClock()
        {
            // A time has no day to roll into, so the days in the duration fall away and what is left is the
            // clock reading. Twenty-three days and some hours later is just the hours later.
            Assert.AreEqual(
                "true",
                Writes("xs:time('08:12:32') + xs:dayTimeDuration('P23DT09H32M59S') eq xs:time('17:45:31')"));

            Assert.AreEqual(
                "true",
                Writes("xs:dayTimeDuration('P23DT09H32M59S') + xs:time('08:12:32') eq xs:time('17:45:31')"));

            Assert.AreEqual(
                "true",
                Writes("xs:time('08:12:32') - xs:dayTimeDuration('P23DT09H32M59S') eq xs:time('22:39:33')"));

            // And a whole number of days leaves the clock where it was.
            Assert.AreEqual("08:12:32", Writes("xs:time('08:12:32') + xs:dayTimeDuration('P5D')"));
        }

        // ---- Adjusting to a timezone -----------------------------------------------------------------------

        [TestMethod]
        public void AdjustingADateToATimezoneLeavesItADate()
        {
            // Defined as taking the date as midnight, moving that to the timezone, and casting back — so the
            // day can change, and no time of day survives either way.
            Assert.AreEqual(
                "true",
                Writes("adjust-date-to-timezone(xs:date('2002-03-07-07:00'), xs:dayTimeDuration('-PT5H0M'))"
                    + " eq xs:date('2002-03-07-05:00')"));

            Assert.AreEqual(
                "true",
                Writes("adjust-date-to-timezone(xs:date('2002-03-07-07:00'), xs:dayTimeDuration('-PT10H'))"
                    + " eq xs:date('2002-03-06-10:00')"));

            Assert.AreEqual(
                "2002-03-06-10:00",
                Writes("adjust-date-to-timezone(xs:date('2002-03-07-07:00'), xs:dayTimeDuration('-PT10H'))"));
        }

        [TestMethod]
        public void AnAdjustedDateSubtractsByTheInstantItsTimezoneGivesIt()
        {
            // The place a stray time of day used to show. Moving 2002-03-07Z into +10:00 leaves the same
            // day, and the day now begins ten hours earlier, so the gap to a later date grows by ten hours.
            Assert.AreEqual(
                "-P1461DT10H",
                Writes("adjust-date-to-timezone(xs:date('2002-03-07Z'), xs:dayTimeDuration('PT10H'))"
                    + " - xs:date('2006-03-07Z')"));

            Assert.AreEqual(
                "P1095DT14H",
                Writes("adjust-date-to-timezone(xs:date('2004-03-07Z'), xs:dayTimeDuration('PT10H'))"
                    + " - xs:date('2001-03-07Z')"));
        }

        [TestMethod]
        public void AdjustingATimeToATimezoneLeavesItATime()
        {
            Assert.AreEqual(
                "true",
                Writes("adjust-time-to-timezone(xs:time('10:00:00-07:00'), xs:dayTimeDuration('-PT5H'))"
                    + " eq xs:time('12:00:00-05:00')"));

            Assert.AreEqual(
                "12:00:00-05:00",
                Writes("adjust-time-to-timezone(xs:time('10:00:00-07:00'), xs:dayTimeDuration('-PT5H'))"));
        }

        // ---- The same rule past the era boundary -----------------------------------------------------------

        [TestMethod]
        public void TheTruncationHoldsWhereTheYearIsAProxyToo()
        {
            Assert.AreEqual(
                "true",
                Writes("xs:date('0001-01-01Z') + xs:dayTimeDuration('-P11DT02H02M') eq xs:date('-0001-12-20Z')"));

            Assert.AreEqual(
                "-0001-12-20Z",
                Writes("xs:date('0001-01-01Z') + xs:dayTimeDuration('-P11DT02H02M')"));
        }
    }
}
