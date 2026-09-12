namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for maps and arrays, which XPath 3.1 adds to the data model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of both is that they are <em>items</em>. A sequence does not nest, so before them there was
    /// no way to hold a list of lists or a value under a key; an array is one item however many members it
    /// has, and a map is one item however many entries.
    /// </para>
    /// <para>
    /// Neither has a string-value, so most of what is asserted here is read back through <c>?</c>, through
    /// the size functions, or by flattening — which is the shape of using them for real.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class MapAndArrayTests
    {
        private const string XslOnly = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private const string Xsl =
            XslOnly + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private const string Namespaces =
            "xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\" "
            + "xmlns:array=\"http://www.w3.org/2005/xpath-functions/array\"";

        /// <summary>Evaluates an expression, joining a sequence with commas so its shape shows.</summary>
        private static string Writes(string expression, string input = "<r/>")
        {
            // The prefixes are excluded, or the literal out element inherits them and carries the two
            // declarations into every result.
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {XslOnly} {Namespaces} xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " exclude-result-prefixes=\"map array xs\">"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\" separator=\",\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        private static string Refuses(string expression)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression));
            return error.Code ?? string.Empty;
        }

        /// <summary>
        /// Evaluates through a 2.0 stylesheet on a processor asked to be 2.0, where none of this syntax exists.
        /// </summary>
        private static string RefusedByTwoPointZero(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {XslOnly} {Namespaces} xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " exclude-result-prefixes=\"map array xs\">"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V20 })
                    .TransformXml("<r/>"));

            return error.Code ?? string.Empty;
        }

        [TestMethod]
        public void NoneOfThisSyntaxExistsInATwoPointZeroStylesheet()
        {
            // Reading '[1]' as an array in a 2.0 expression would take an error away from a stylesheet that
            // had written a predicate with nothing in front of it, which is what that used to mean and still
            // does. The same goes for '?', which 2.0 uses only as an occurrence indicator.
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("[1, 2]"));
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("(1, 2)?1"));
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("1 instance of map(*)"));
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("map { 'a': 1 }"));
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("array { 1 }"));

            // The occurrence indicator still reads as one, which is the thing the gate has to leave alone.
            Assert.AreEqual("2.0", TwoPointZeroWrites("string(1) instance of xs:string?"));
        }

        /// <summary>Evaluates through a 2.0 stylesheet that is expected to succeed.</summary>
        private static string TwoPointZeroWrites(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            string result = new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true })
                .TransformXml("<r/>");

            return result == "<out>true</out>" ? "2.0" : result;
        }

        // ---- Arrays ----------------------------------------------------------------------------------------

        [TestMethod]
        public void AnArrayHoldsItsMembersWithoutFlatteningThem()
        {
            // The whole reason arrays exist. A sequence of a sequence is one sequence — (1, (2, 3)) is three
            // items — so nothing before this could hold a list of lists.
            Assert.AreEqual("3", Writes("array:size([1, 2, 3])"));
            Assert.AreEqual("2", Writes("array:size([(1, 2), 3])"));
            Assert.AreEqual("0", Writes("array:size([])"));
            Assert.AreEqual("1", Writes("array:size([()])"), "a member may be the empty sequence");
        }

        [TestMethod]
        public void AnArrayIsReadByPosition()
        {
            Assert.AreEqual("2", Writes("[1, 2, 3]?2"));
            Assert.AreEqual("2", Writes("array:get([1, 2, 3], 2)"));
            Assert.AreEqual("1,2,3", Writes("[1, 2, 3]?*"));
            Assert.AreEqual("1", Writes("array:head([1, 2, 3])"));
            Assert.AreEqual("2,3", Writes("array:tail([1, 2, 3])?*"));
            Assert.AreEqual("0", Writes("array:size(array:tail([1]))"), "the tail of one member is empty");

            // A member that is a sequence comes back whole.
            Assert.AreEqual("1,2", Writes("[(1, 2), 3]?1"));
        }

        [TestMethod]
        public void AnArrayIsChangedByBuildingAnother()
        {
            Assert.AreEqual("1,2,9", Writes("array:append([1, 2], 9)?*"));
            Assert.AreEqual("1,9,3", Writes("array:put([1, 2, 3], 2, 9)?*"));
            Assert.AreEqual("3,2,1", Writes("array:reverse([1, 2, 3])?*"));
            Assert.AreEqual("2,3", Writes("array:subarray([1, 2, 3, 4], 2, 2)?*"));
            Assert.AreEqual("2,3,4", Writes("array:subarray([1, 2, 3, 4], 2)?*"));
            Assert.AreEqual("1,3", Writes("array:remove([1, 2, 3], 2)?*"));
            Assert.AreEqual("1,9,2", Writes("array:insert-before([1, 2], 2, 9)?*"));
            Assert.AreEqual("1,2,3", Writes("array:join(([1, 2], [3]))?*"));
        }

        [TestMethod]
        public void FlatteningAnArrayGoesAllTheWayDown()
        {
            Assert.AreEqual("1,2,3", Writes("array:flatten([[1, 2], [3]])"));
            Assert.AreEqual("1,2,3,4", Writes("array:flatten([1, [2, [3, [4]]]])"));
            Assert.AreEqual("1,2,3", Writes("array:flatten((1, [2, [3]]))"), "it takes any sequence");
        }

        [TestMethod]
        public void APositionOutsideAnArrayIsAnError()
        {
            Assert.AreEqual("FOAY0001", Refuses("[1, 2]?3"));
            Assert.AreEqual("FOAY0001", Refuses("array:get([1, 2], 0)"));
            Assert.AreEqual("FOAY0001", Refuses("array:head([])"));
            Assert.AreEqual("FOAY0002", Refuses("array:subarray([1, 2], 1, -1)"));
        }

        // ---- Maps ------------------------------------------------------------------------------------------

        [TestMethod]
        public void AMapIsReadByKey()
        {
            Assert.AreEqual("1", Writes("map:get(map:entry('a', 1), 'a')"));
            Assert.AreEqual("1", Writes("map:entry('a', 1)?a"));
            Assert.AreEqual("1", Writes("map:entry('a', 1)?('a')"));
            Assert.AreEqual("1", Writes("map:size(map:entry('a', 1))"));
            Assert.AreEqual("true", Writes("map:contains(map:entry('a', 1), 'a')"));
            Assert.AreEqual("false", Writes("map:contains(map:entry('a', 1), 'b')"));

            // An absent key is the empty sequence, not an error — which is why contains() is worth having.
            Assert.AreEqual(string.Empty, Writes("map:get(map:entry('a', 1), 'b')"));
        }

        [TestMethod]
        public void AMapIsChangedByBuildingAnother()
        {
            Assert.AreEqual("2", Writes("map:size(map:put(map:entry('a', 1), 'b', 2))"));
            Assert.AreEqual("9", Writes("map:put(map:entry('a', 1), 'a', 9)?a"), "an existing key is replaced");
            Assert.AreEqual("0", Writes("map:size(map:remove(map:entry('a', 1), 'a'))"));
            Assert.AreEqual("1", Writes("map:size(map:remove(map:entry('a', 1), 'b'))"), "an absent key is ignored");
        }

        [TestMethod]
        public void MergingDecidesWhatARepeatedKeyMeans()
        {
            const string Two = "(map:entry('a', 1), map:entry('a', 2))";

            Assert.AreEqual("1", Writes($"map:merge({Two})?a"), "the first wins by default");
            Assert.AreEqual(
                "2", Writes($"map:merge({Two}, map:entry('duplicates', 'use-last'))?a"));
            Assert.AreEqual(
                "1,2", Writes($"map:merge({Two}, map:entry('duplicates', 'combine'))?a"));
            Assert.AreEqual(
                "FOJS0003", Refuses($"map:merge({Two}, map:entry('duplicates', 'reject'))"));
            Assert.AreEqual(
                "FOJS0005", Refuses($"map:merge({Two}, map:entry('duplicates', 'invented'))"));

            Assert.AreEqual("2", Writes("map:size(map:merge((map:entry('a', 1), map:entry('b', 2))))"));
        }

        [TestMethod]
        public void KeysAreComparedMoreCoarselyThanValues()
        {
            // op:same-key, not eq. The numeric types are one family, so how the number was written does not
            // decide whether it finds the entry.
            Assert.AreEqual("x", Writes("map:entry(1, 'x')?(1.0)"));
            Assert.AreEqual("x", Writes("map:entry(1, 'x')?(1e0)"));
            Assert.AreEqual("1", Writes("map:size(map:merge((map:entry(1, 'x'), map:entry(1.0, 'y'))))"));

            // NaN is the same key as NaN, where eq says it equals nothing at all — otherwise a map could hold
            // an entry nobody could read back.
            Assert.AreEqual("true", Writes("map:contains(map:entry(xs:double('NaN'), 1), xs:double('NaN'))"));

            // A string and a number are never the same key, however they are written.
            Assert.AreEqual("false", Writes("map:contains(map:entry(1, 'x'), '1')"));
        }

        [TestMethod]
        public void AKeyHasToBeOneAtomicValue()
        {
            Assert.AreEqual("XPTY0004", Refuses("map:entry((1, 2), 'x')"));
            Assert.AreEqual("XPTY0004", Refuses("map:entry([1], 'x')"));
        }

        // ---- Written with braces ---------------------------------------------------------------------------

        [TestMethod]
        public void AMapCanBeWrittenOut()
        {
            Assert.AreEqual("1", Writes("map { 'a': 1, 'b': 2 }?a"));
            Assert.AreEqual("2", Writes("map:size(map { 'a': 1, 'b': 2 })"));
            Assert.AreEqual("0", Writes("map:size(map { })"));
            Assert.AreEqual("two", Writes("map { 1 + 1: 'two' }?(2)"), "a key is any expression");
            Assert.AreEqual("1,2", Writes("map { 'a': (1, 2) }?a"), "and so is a value");
            Assert.AreEqual("2", Writes("map { 'a': [1, 2] }?a?2"), "which may be an array");

            // A qualified name still scans as one, so the colon inside it is not the entry's colon.
            Assert.AreEqual("1", Writes("map { xs:string('a'): 1 }?a"));
        }

        [TestMethod]
        public void AKeyWrittenTwiceIsRefused()
        {
            // Written out, a repeated key is a mistake rather than a choice — and 1 and 1.0 are one key, so
            // it is a mistake that would be easy to miss.
            //
            // XQDY0137 and not the FOJS0003 map:merge raises: three ways of arriving at the same shape and a
            // code apiece, so a stylesheet reacting to one of them knows which it was. The third is
            // xsl:map's own XTDE3365.
            Assert.AreEqual("XQDY0137", Refuses("map { 'a': 1, 'a': 2 }"));
            Assert.AreEqual("XQDY0137", Refuses("map { 1: 'x', 1.0: 'y' }"));

            Assert.AreEqual(
                "FOJS0003",
                Refuses(
                    "map:merge((map:entry('a', 1), map:entry('a', 2)), map { 'duplicates': 'reject' })"));
        }

        [TestMethod]
        public void ADurationIsOneKeyHoweverItWasWritten()
        {
            // A key is tagged by what it measures rather than by how it was written or which of the three
            // duration types wrote it: xs:duration('P1Y') and xs:yearMonthDuration('P12M') are twelve months
            // and no seconds either way, so they are one key.
            Assert.AreEqual(
                "true", Writes("map:contains(map { xs:duration('P1Y'): 'W' }, xs:yearMonthDuration('P12M'))"));
            Assert.AreEqual(
                "W", Writes("map:get(map { xs:duration('P1Y'): 'W' }, xs:yearMonthDuration('P12M'))"));

            // Two that measure different things stay two keys, months and seconds being separate.
            Assert.AreEqual(
                "2",
                Writes("map:size(map { xs:yearMonthDuration('P1Y'): 1, xs:dayTimeDuration('P1D'): 2 })"));
        }

        [TestMethod]
        public void AnArrayCanBeWrittenWithBracesInstead()
        {
            Assert.AreEqual("3", Writes("array:size(array { 1, 2, 3 })"));
            Assert.AreEqual("0", Writes("array:size(array { })"));
            Assert.AreEqual("2", Writes("array { 1, 2, 3 }?2"));

            // The whole difference between the two forms: braces take one sequence and give a member per
            // item, brackets take one expression per member.
            Assert.AreEqual("3", Writes("array:size(array { (1, 2), 3 })"));
            Assert.AreEqual("2", Writes("array:size([(1, 2), 3])"));
            Assert.AreEqual("0", Writes("array:size(array { () })"), "an empty sequence is no members at all");
            Assert.AreEqual("1", Writes("array:size([()])"), "where an empty member is still a member");
        }

        [TestMethod]
        public void AKeyMayComeOutOfTheInput()
        {
            // The case that needs the scanner to leave the colon alone: '@id:' is a name, then a colon, and
            // not a name with the prefix 'id'.
            Assert.AreEqual(
                "found",
                Writes("map { /r/@id: 'found' }?k", "<r id=\"k\"/>"));

            // A node is atomized to be a key, and untypedAtomic shares a family with string, so the text is
            // what decides.
            Assert.AreEqual(
                "1",
                Writes("map:get(map { 'k': 1 }, /r/@id)", "<r id=\"k\"/>"));
        }

        [TestMethod]
        public void MapAndArrayAreStillOrdinaryNamesWithoutABrace()
        {
            // An element may be called either, and a path still reaches it. Only a following brace makes a
            // constructor, which is the rule that already lets an element be called 'if'.
            Assert.AreEqual("inside", Writes("/r/map", "<r><map>inside</map></r>"));
            Assert.AreEqual("here", Writes("/r/array", "<r><array>here</array></r>"));
            Assert.AreEqual("1", Writes("count(/r/map)", "<r><map>inside</map></r>"));
        }

        [TestMethod]
        public void AConstructorCanBeWrittenInsideAnAttributeValueTemplate()
        {
            // The braces of the constructor are inside the braces of the template, so the template has to
            // count them rather than end at the first one it meets.
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {XslOnly} {Namespaces} xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " exclude-result-prefixes=\"map array xs\">"
                + "<xsl:template match=\"/\"><a v=\"{map { 'k': 'value' }?k}\"/></xsl:template>"
                + "</xsl:stylesheet>";

            Assert.AreEqual(
                "<a v=\"value\"/>",
                new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }

        // ---- Where they sit in the data model ---------------------------------------------------------------

        [TestMethod]
        public void AMapAndAnArrayAreItemsAndNothingElse()
        {
            Assert.AreEqual("true", Writes("[1, 2] instance of array(*)"));
            Assert.AreEqual("true", Writes("map:entry('a', 1) instance of map(*)"));
            Assert.AreEqual("true", Writes("[1, 2] instance of item()"));
            Assert.AreEqual("1", Writes("count([1, 2, 3])"), "one item, however many members");

            // Each admits only its own, and neither is a node or an atomic value.
            Assert.AreEqual("false", Writes("[1, 2] instance of map(*)"));
            Assert.AreEqual("false", Writes("map:entry('a', 1) instance of array(*)"));
            Assert.AreEqual("false", Writes("[1, 2] instance of xs:string"));
            Assert.AreEqual("false", Writes("[1, 2] instance of node()"));
        }

        [TestMethod]
        public void NeitherHasAStringValue()
        {
            // There is text that would render either, but none that means the same, so asking is an error
            // rather than an answer a stylesheet might go on to compare.
            Assert.AreEqual("FOTY0014", Refuses("string([1, 2])"));
            Assert.AreEqual("FOTY0014", Refuses("string(map:entry('a', 1))"));

            // Writing one into a result is a different question and gets a different code. xsl:value-of
            // atomizes what it is given rather than asking it for a string-value, and a map has no typed
            // value to atomize at all — which is FOTY0013, not FOTY0014.
            Assert.AreEqual("FOTY0013", Refuses("map:entry('a', 1)"));
        }

        [TestMethod]
        public void AnArrayWrittenIntoAResultContributesItsMembers()
        {
            // Where a map has no typed value, an array does: XPath 3.1 made arrays atomizable, the same
            // change that makes sum([1, 2, 3]) six. So an array in a value-of is its members flattened, not
            // an error — and nested arrays flatten all the way down.
            Assert.AreEqual("1,2", Writes("[1, 2]"));
            Assert.AreEqual("1,2,3,4", Writes("[[1, 2], [3, 4]]"));
        }

        [TestMethod]
        public void LookingIntoSomethingThatIsNeitherIsAnError()
        {
            Assert.AreEqual("XPTY0004", Refuses("'text'?a"));
            Assert.AreEqual("XPTY0004", Refuses("1?1"));
        }

        [TestMethod]
        public void ALookupAppliesToEveryItemItIsGiven()
        {
            // Which is what makes it worth writing: one lookup reads the same entry out of every map.
            Assert.AreEqual(
                "1,2",
                Writes("(map:entry('a', 1), map:entry('a', 2))?a"));

            Assert.AreEqual("1,2,3,4", Writes("([1, 2], [3, 4])?*"));
        }

        [TestMethod]
        public void ASquareBracketStillBeginsAPredicateAfterAnExpression()
        {
            // The one place the new syntax could have taken something away: '[' starts an array only where an
            // expression may start, and a predicate needs something in front of it.
            Assert.AreEqual("2", Writes("(1, 2, 3)[2]"));
            Assert.AreEqual("1,3", Writes("(1, 2, 3)[. != 2]"));
            Assert.AreEqual("2", Writes("[1, 2, 3]?*[2]"), "and both can appear together");
        }

        /// <summary>Runs a whole stylesheet body, for what a single value-of cannot show.</summary>
        private static string Applies(string body)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {XslOnly} {Namespaces} "
                + "xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"map array xs\">"
                + body + "</xsl:stylesheet>";

            XsltOptions options = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 };

            return new Xslt(stylesheet, options).TransformXml("<r/>");
        }

        private static string RefusesBody(string body)
        {
            return Assert.ThrowsExactly<XsltException>(() => Applies(body)).Code ?? string.Empty;
        }

        [TestMethod]
        public void TemplatesAppliedToAnArrayReachItsMembers()
        {
            // The built-in rule for an array is to go into it: templates are applied to the members, which
            // is what ?* gives. An array is the one item that is a container, and asking one for the string
            // value the built-in rule for an atomic value writes is asking for what it has not got.
            const string Body =
                "<xsl:variable name=\"data\" as=\"element()*\"><a/><b/><c/></xsl:variable>"
                + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"array{$data}\"/></out>"
                + "</xsl:template>"
                + "<xsl:template match=\"*\"><e n=\"{name()}\"/></xsl:template>";

            Assert.AreEqual("<out><e n=\"a\"/><e n=\"b\"/><e n=\"c\"/></out>", Applies(Body));

            // And it is the same rule whatever the mode says about no match, where shallow-skip would
            // otherwise skip the array and everything in it.
            Assert.AreEqual(
                "<out><e n=\"a\"/><e n=\"b\"/><e n=\"c\"/></out>",
                Applies("<xsl:mode on-no-match=\"shallow-skip\"/>" + Body));

            // A member holding several items contributes each of them, the members being processed as the
            // one sequence they make rather than one item apiece.
            Assert.AreEqual(
                "<out>123</out>",
                Applies("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"[(1, 2), 3]\"/>"
                    + "</out></xsl:template>"));
        }

        [TestMethod]
        public void TheKeyOfAMapEntryIsOneAtomicValue()
        {
            // The key attribute has a required type of xs:anyAtomicType and is converted to it by the
            // ordinary rules, so an empty sequence there is that conversion failing.
            Assert.AreEqual(
                "XPTY0004",
                RefusesBody("<xsl:template match=\"/\"><xsl:variable name=\"m\" as=\"map(*)\">"
                    + "<xsl:map><xsl:map-entry key=\"()\" select=\"1\"/></xsl:map></xsl:variable>"
                    + "<out size=\"{map:size($m)}\"/></xsl:template>"));

            // XTTE3375 is the other thing: an xsl:map whose content is not maps.
            Assert.AreEqual(
                "XTTE3375",
                RefusesBody("<xsl:template match=\"/\"><xsl:variable name=\"m\" as=\"map(*)\">"
                    + "<xsl:map><xsl:sequence select=\"1\"/></xsl:map></xsl:variable>"
                    + "<out size=\"{map:size($m)}\"/></xsl:template>"));
        }

        [TestMethod]
        public void ADateOrTimeKeysByTheMomentItNames()
        {
            // Two values are the same key when eq says so, and eq compares these as moments: one time
            // written in two timezones is one key.
            Assert.AreEqual(
                "1",
                Writes("map:size(map:merge((map{ xs:time('18:15:00-05:00') : 1 }, "
                    + "map{ xs:time('23:15:00Z') : 2 })))"));

            Assert.AreEqual(
                "1",
                Writes("map:get(map{ xs:time('18:15:00-05:00') : 1 }, xs:time('23:15:00Z'))"));

            // A value with no timezone is never the same key as one that has a timezone, however the
            // implicit timezone would settle it: a map's keys must not collide or not by a setting outside
            // the map.
            Assert.AreEqual(
                "2",
                Writes("map:size(map:merge((map{ xs:dateTime('2020-01-01T00:00:00') : 1 }, "
                    + "map{ xs:dateTime('2020-01-01T00:00:00Z') : 2 })))"));

            // And a date is still never the same key as a string of one.
            Assert.AreEqual(
                "2",
                Writes("map:size(map:merge((map{ xs:date('2020-01-01') : 1 }, map{ '2020-01-01' : 2 })))"));
        }
    }
}
