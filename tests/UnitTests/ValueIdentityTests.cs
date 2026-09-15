namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for when two values written differently are one value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Several of these types are written in more ways than they are valued in. A timezone moves what a
    /// gregorian value or a moment denotes without changing its digits; a duration of no length has two
    /// spellings; a number keeps its type as well as its magnitude. Anything asking whether two of them
    /// are the same value has to compare what <c>eq</c> compares and not what the text looks like.
    /// </para>
    /// <para>
    /// The place that is easiest to get wrong is a <em>key</em> — <c>fn:distinct-values</c> and a map
    /// both reach for one — because a key that is merely the text agrees with <c>eq</c> on almost every
    /// value and then quietly disagrees on the ones these cases are made of.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class ValueIdentityTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
            + " xmlns:array=\"http://www.w3.org/2005/xpath-functions/array\""
            + " exclude-result-prefixes=\"xs array\"";

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

        // ---- A timezone moves what the digits denote -------------------------------------------------------

        [TestMethod]
        public void TwoGregorianValuesAreComparedByTheMomentTheyDenote()
        {
            // Day 30 twelve hours west and day 31 twelve hours east are the same midnight seen from two
            // sides of the world. XML Schema orders these types by filling the fields they do not carry
            // from a fixed reference date, so the day numbers alone say nothing.
            Assert.AreEqual("true", Writes("xs:gDay('---30-12:00') eq xs:gDay('---31+12:00')"));
            Assert.AreEqual("false", Writes("xs:gDay('---30-12:00') ne xs:gDay('---31+12:00')"));
            Assert.AreEqual(
                "true", Writes("xs:gMonthDay('--01-30-12:00') eq xs:gMonthDay('--01-31+12:00')"));

            // And two that genuinely differ still do.
            Assert.AreEqual("false", Writes("xs:gDay('---30') eq xs:gDay('---31')"));
            Assert.AreEqual("true", Writes("xs:gYear('2020Z') eq xs:gYear('2020Z')"));
        }

        [TestMethod]
        public void DistinctValuesCountsByWhatEqualityCompares()
        {
            // A moment with no timezone is compared in the implicit one, so these two are one value even
            // though one is written with a Z and the other is not.
            Assert.AreEqual(
                "1",
                Writes("count(distinct-values((xs:dateTime('2008-01-01T13:00:00'),"
                    + " adjust-dateTime-to-timezone(xs:dateTime('2008-01-01T13:00:00')))))"));

            // A year-month duration of no months and a day-time one of no seconds are both the zero
            // duration, and 'eq' says so.
            Assert.AreEqual(
                "1",
                Writes("count(distinct-values((xs:yearMonthDuration('P0Y'), xs:dayTimeDuration('P0D'))))"));

            // What must not happen is the other way round: a value and the text of it are two values.
            Assert.AreEqual("2", Writes("count(distinct-values((xs:date('2020-01-01'), '2020-01-01')))"));
            Assert.AreEqual("2", Writes("count(distinct-values((true(), 'true')))"));
            Assert.AreEqual("2", Writes("count(distinct-values((xs:hexBinary('AB'), 'AB')))"));
        }

        // ---- The binary types order among themselves -------------------------------------------------------

        [TestMethod]
        public void MinAndMaxTakeTheBinaryTypes()
        {
            // XPath 3.1 gives each of them an lt and a gt, so there is an answer to ask for. The canonical
            // form of an xs:hexBinary is upper case, which is what comes back.
            Assert.AreEqual(
                "88", Writes("min((xs:hexBinary('aa'), xs:hexBinary('bb'), xs:hexBinary('88')))"));

            Assert.AreEqual(
                "BB", Writes("max((xs:hexBinary('aa'), xs:hexBinary('bb'), xs:hexBinary('88')))"));

            Assert.AreEqual(
                "iA==", Writes("min((xs:base64Binary('qg=='), xs:base64Binary('uw=='), xs:base64Binary('iA==')))"));

            // Not over a mixture of the two, which is the FORG0006 a mixture of families always was.
            Refuses("FORG0006", "min((xs:hexBinary('aa'), xs:base64Binary('qg==')))");
        }

        // ---- A position is an integer ----------------------------------------------------------------------

        [TestMethod]
        public void AnArrayPositionIsAnIntegerAndNothingElse()
        {
            // The function conversion rules promote an integer outwards to a decimal and a double and
            // never inwards, so a decimal position is a type error rather than a truncation.
            Refuses("XPTY0004", "array:get([1,2,3], 1.2)");
            Refuses("XPTY0004", "array:get([1,2,3], 1 to 2)");
            Refuses("XPTY0004", "array:get([1,2,3], ())");
            Refuses("XPTY0004", "[1,2,3](1.1)");

            Assert.AreEqual("2", Writes("array:get([1,2,3], 2)"));
            Assert.AreEqual("2", Writes("[1,2,3](2)"));
        }

        // ---- A context item has a type too -----------------------------------------------------------------

        [TestMethod]
        public void TheContextFormsStillTakeOnlyANode()
        {
            // fn:nilled and fn:node-name are declared to take node()?, and the form with no argument is
            // the same function with the same declared type: an integer in the context is a type error,
            // not a node that happens to be neither nilled nor named.
            Refuses("XPTY0004", "23[nilled()]");
            Refuses("XPTY0004", "79[node-name()]");

            // With a node in the context they answer as they always did.
            Assert.AreEqual("r", Writes("/r/node-name()"));
        }

        // ---- A collation is named where the expression is written ------------------------------------------

        [TestMethod]
        public void ARelativeCollationUriIsResolvedAgainstTheBaseUri()
        {
            const string Where = "http://www.w3.org/2005/xpath-functions/";

            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"substring-after('banana', 'a', 'collation/codepoint')\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>nana</out>",
                new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true, BaseUri = Where })
                    .TransformXml("<r/>"));
        }
    }
}
