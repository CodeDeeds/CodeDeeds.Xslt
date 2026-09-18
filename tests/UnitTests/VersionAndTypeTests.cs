using System.Runtime.CompilerServices;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the groundwork that lets one engine hold two languages: the version a stylesheet is written
    /// against, the type an atomic value carries, and the first rule that reads differently under each.
    /// </summary>
    /// <remarks>
    /// XPath 2.0 semantics are exercised here through <see cref="XPathStaticContext"/> rather than through a
    /// stylesheet, because the XSLT layer still reports itself as 1.0 and must go on doing so until enough of
    /// 2.0 exists to claim it. Compiling an expression against 2.0 rules is independent of that.
    /// </remarks>
    [TestClass]
    public sealed class VersionAndTypeTests
    {
        private static XPathValue Evaluate(string xml, string expression, XsltVersion version)
        {
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader(xml));
            XPathStaticContext staticContext = new XPathStaticContext { Version = version };

            Expr compiled = XPathParser.Parse(expression, staticContext);
            int[] map = staticContext.Names.BuildFingerprintMap(tree);

            DynamicContext context = new DynamicContext(tree, XdmTree.RootNode, map);
            return compiled.Evaluate(ref context);
        }

        private static bool Boolean(string xml, string expression, XsltVersion version)
        {
            return Evaluate(xml, expression, version).ToBoolean();
        }

        /// <summary>Evaluates with no context item at all, which is a thing XPath distinguishes.</summary>
        private static string? CodeWithoutAFocus(string expression)
        {
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader("<r/>"));
            XPathStaticContext staticContext = new XPathStaticContext { Version = XsltVersion.V20 };

            try
            {
                Expr compiled = XPathParser.Parse(expression, staticContext);
                int[] map = staticContext.Names.BuildFingerprintMap(tree);

                DynamicContext context = new DynamicContext(tree, DynamicContext.NotANode, map);
                compiled.Evaluate(ref context);
                return null;
            }
            catch (XsltException error)
            {
                return error.Code;
            }
        }

        // ---- The version model ---------------------------------------------------------------------------

        [TestMethod]
        [DataRow("1.0", 1.0)]
        [DataRow("2.0", 2.0)]
        [DataRow("3.0", 3.0)]
        [DataRow("1.1", 1.1)]
        [DataRow("2", 2.0)]
        public void AVersionAttributeParsesToItsNumber(string text, double expected)
        {
            Assert.AreEqual(expected, XsltVersion.Parse(text).Number, 0.0001);
        }

        [TestMethod]
        public void VersionsOrderAsNumbersRatherThanAsText()
        {
            // "10.0" must not sort before "2.0" the way the strings would.
            Assert.IsTrue(XsltVersion.Parse("2.0") > XsltVersion.V10);
            Assert.IsTrue(XsltVersion.Parse("10.0") > XsltVersion.Parse("2.0"));
            Assert.IsTrue(XsltVersion.Parse("1.1") > XsltVersion.V10);
            Assert.IsTrue(XsltVersion.Parse("1.1") < XsltVersion.V20);
        }

        [TestMethod]
        public void AnUnrecognisableVersionCountsAsALaterOne()
        {
            // Treating it as later puts the stylesheet into forwards-compatible processing, so it runs as far
            // as it can rather than being refused outright.
            Assert.IsTrue(XsltVersion.Parse("banana") > XsltVersion.Implemented);
            Assert.IsTrue(XsltVersion.Parse(string.Empty) > XsltVersion.Implemented);
        }

        [TestMethod]
        public void OnlyVersionsBeforeTwoAskForBackwardsCompatibleBehaviour()
        {
            Assert.IsTrue(XsltVersion.V10.IsBackwardsCompatible);
            Assert.IsTrue(XsltVersion.Parse("1.1").IsBackwardsCompatible);
            Assert.IsFalse(XsltVersion.V20.IsBackwardsCompatible);
            Assert.IsFalse(XsltVersion.V30.IsBackwardsCompatible);
        }

        [TestMethod]
        public void TheEngineReportsItselfAsThreePointZero()
        {
            // Claiming a version is a statement about the whole language, so this waited on the instruction
            // set and the XPath half both being there rather than on any one feature. What it changes for a
            // stylesheet: version="3.0" is now taken at its word instead of being read forwards-compatibly,
            // so a construct this engine does not implement is an error rather than a silent fallback. A
            // caller who names no version gets this one, and one who asks for 2.0 still gets a 2.0 processor.
            Assert.AreEqual(XsltVersion.V30, XsltVersion.Implemented);
            Assert.AreEqual(XsltVersion.Implemented, new XsltOptions().Version);
            Assert.AreEqual(XsltVersion.V20, new XsltOptions { Version = XsltVersion.V20 }.Version);
        }

        // ---- Atomic types --------------------------------------------------------------------------------

        [TestMethod]
        public void ValuesCarryTheirType()
        {
            Assert.AreEqual(XdmTypeCode.Double, XPathValue.FromNumber(1.5).TypeCode);
            Assert.AreEqual(XdmTypeCode.String, XPathValue.FromString("x").TypeCode);
            Assert.AreEqual(XdmTypeCode.Boolean, XPathValue.FromBoolean(true).TypeCode);
            Assert.AreEqual(XdmTypeCode.UntypedAtomic, XPathValue.FromUntypedAtomic("x").TypeCode);
        }

        [TestMethod]
        public void AnUntypedValueIsStillAStringToVersionOne()
        {
            // The type is extra information, not a change of kind: everything XPath 1.0 asks of the value
            // must answer exactly as before.
            XPathValue untyped = XPathValue.FromUntypedAtomic("42");

            Assert.AreEqual(XPathValueKind.String, untyped.Kind);
            Assert.AreEqual("42", untyped.ToStringValue());
            Assert.AreEqual(42.0, untyped.ToNumber());
            Assert.IsTrue(untyped.ToBoolean());
        }

        [TestMethod]
        public void CarryingATypeCostsNothing()
        {
            // The struct was already padded to hold a reference and a double, so the type byte fits in space
            // that was going to waste. If this ever grows, the hot predicate path pays for it.
            Assert.AreEqual(24, Unsafe.SizeOf<XPathValue>());
        }

        // ---- The first rule that differs between the versions --------------------------------------------

        [TestMethod]
        public void TwoStringsCompareAsNumbersInVersionOneAndAsStringsInVersionTwo()
        {
            // The headline incompatibility. '10' < '9' is false when both become numbers, true when they are
            // ordered as text.
            Assert.IsFalse(Boolean("<r/>", "'10' < '9'", XsltVersion.V10));
            Assert.IsTrue(Boolean("<r/>", "'10' < '9'", XsltVersion.V20));
        }

        [TestMethod]
        public void NonNumericStringsAreUnorderedInVersionOneAndOrderedInVersionTwo()
        {
            // In 1.0 both sides are NaN, and every comparison against NaN is false — including >=.
            Assert.IsFalse(Boolean("<r/>", "'apple' < 'banana'", XsltVersion.V10));
            Assert.IsFalse(Boolean("<r/>", "'apple' >= 'banana'", XsltVersion.V10));

            Assert.IsTrue(Boolean("<r/>", "'apple' < 'banana'", XsltVersion.V20));
            Assert.IsFalse(Boolean("<r/>", "'apple' >= 'banana'", XsltVersion.V20));
        }

        [TestMethod]
        [DataRow("'a' < 'b'", true)]
        [DataRow("'b' < 'a'", false)]
        [DataRow("'a' <= 'a'", true)]
        [DataRow("'b' > 'a'", true)]
        [DataRow("'a' >= 'b'", false)]
        public void EveryRelationalOperatorOrdersStringsInVersionTwo(string expression, bool expected)
        {
            Assert.AreEqual(expected, Boolean("<r/>", expression, XsltVersion.V20));
        }

        [TestMethod]
        public void ComparingANodeAgainstANumberIsUnchangedByTheVersion()
        {
            // A node's value is untyped, and an untyped operand becomes a number against a number in both
            // versions. This is what keeps the overwhelmingly common comparison meaning one thing.
            const string Xml = "<r><price>10</price></r>";

            foreach (XsltVersion version in new[] { XsltVersion.V10, XsltVersion.V20 })
            {
                Assert.IsTrue(Boolean(Xml, "/r/price > 9", version), $"{version}");
                Assert.IsFalse(Boolean(Xml, "/r/price < 9", version), $"{version}");
                Assert.IsTrue(Boolean(Xml, "/r/price >= 10", version), $"{version}");
            }
        }

        [TestMethod]
        public void EqualityIsUnchangedByTheVersion()
        {
            // Only the ordering operators were redefined; '=' compares strings as strings in both versions.
            foreach (XsltVersion version in new[] { XsltVersion.V10, XsltVersion.V20 })
            {
                Assert.IsTrue(Boolean("<r/>", "'10' = '10'", version), $"{version}");
                Assert.IsFalse(Boolean("<r/>", "'10' = '9'", version), $"{version}");
                Assert.IsTrue(Boolean("<r/>", "'abc' != 'abd'", version), $"{version}");
            }
        }

        // ---- The type and node-set operators -------------------------------------------------------------

        [TestMethod]
        [DataRow("1 instance of xs:integer", true)]
        [DataRow("1 instance of xs:string", false)]
        [DataRow("'a' instance of xs:string", true)]
        [DataRow("() instance of empty-sequence()", true)]
        [DataRow("1 instance of empty-sequence()", false)]
        [DataRow("(1, 2) instance of xs:integer*", true)]
        [DataRow("(1, 2) instance of xs:integer", false)]
        [DataRow("(1, 2) instance of xs:integer+", true)]
        [DataRow("() instance of xs:integer*", true)]
        [DataRow("() instance of xs:integer+", false)]
        [DataRow("1 instance of xs:integer?", true)]
        [DataRow("1 instance of item()", true)]
        [DataRow("(1, 'a') instance of item()*", true)]
        public void InstanceOfTestsTheTypeAndTheCount(string expression, bool expected)
        {
            Assert.AreEqual(expected, Boolean("<r/>", expression, XsltVersion.V20), expression);
        }

        [TestMethod]
        public void InstanceOfDistinguishesNodesFromAtomicValues()
        {
            const string Xml = "<r><i>1</i></r>";

            Assert.IsTrue(Boolean(Xml, "/r/i instance of node()", XsltVersion.V20));
            Assert.IsTrue(Boolean(Xml, "/r/i instance of element()", XsltVersion.V20));
            Assert.IsFalse(Boolean(Xml, "/r/i instance of attribute()", XsltVersion.V20));

            // A node holding "1" is not an xs:integer: only a schema-aware processor could say otherwise,
            // and this one does not validate.
            Assert.IsFalse(Boolean(Xml, "/r/i instance of xs:integer", XsltVersion.V20));
        }

        [TestMethod]
        [DataRow("'5' cast as xs:integer", "5")]
        [DataRow("5 cast as xs:string", "5")]
        [DataRow("'2020-01-01' cast as xs:date", "2020-01-01")]
        [DataRow("() cast as xs:integer?", "")]
        public void CastConvertsToTheTypeNamed(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        [DataRow("'5' castable as xs:integer", true)]
        [DataRow("'abc' castable as xs:integer", false)]
        [DataRow("'2020-01-01' castable as xs:date", true)]
        [DataRow("'2020-02-30' castable as xs:date", false)]
        [DataRow("() castable as xs:integer?", true)]
        [DataRow("() castable as xs:integer", false)]
        public void CastableAsksWhetherACastWouldWork(string expression, bool expected)
        {
            // The point of it: check before relying on the value, rather than catching the failure.
            Assert.AreEqual(expected, Boolean("<r/>", expression, XsltVersion.V20), expression);
        }

        [TestMethod]
        public void CastFailsWhereCastableWouldHaveSaidSo()
        {
            Assert.AreEqual("FORG0001", CodeOf("'abc' cast as xs:integer"));
        }

        [TestMethod]
        public void TreatAsPassesTheValueThroughOrFails()
        {
            Assert.AreEqual("5", Text("5 treat as xs:integer"));
            Assert.AreEqual("XPDY0050", CodeOf("'a' treat as xs:integer"));
        }

        [TestMethod]
        public void IntersectAndExceptCombineNodeSetsByIdentity()
        {
            const string Xml = "<r><a/><b/><c/></r>";

            Assert.AreEqual(1, CountIn(Xml, "(/r/a | /r/b) intersect (/r/b | /r/c)"));
            Assert.AreEqual(1, CountIn(Xml, "(/r/a | /r/b) except (/r/b | /r/c)"));
            Assert.AreEqual(0, CountIn(Xml, "/r/a intersect /r/b"));
            Assert.AreEqual(2, CountIn(Xml, "(/r/a | /r/b) except /r/c"));
        }

        [TestMethod]
        public void UnionHasAWordedSpelling()
        {
            const string Xml = "<r><a/><b/></r>";

            Assert.AreEqual(2, CountIn(Xml, "/r/a union /r/b"));
            Assert.AreEqual(CountIn(Xml, "/r/a | /r/b"), CountIn(Xml, "/r/a union /r/b"));
        }

        [TestMethod]
        public void NodeComparisonsAskAboutTheNodesThemselves()
        {
            const string Xml = "<r><a/><b/></r>";

            Assert.IsTrue(Boolean(Xml, "/r/a is /r/a", XsltVersion.V20));
            Assert.IsFalse(Boolean(Xml, "/r/a is /r/b", XsltVersion.V20));

            // Two nodes with identical content are still two nodes.
            Assert.IsFalse(Boolean("<r><i/><i/></r>", "/r/i[1] is /r/i[2]", XsltVersion.V20));
        }

        [TestMethod]
        public void DocumentOrderComparisonsOrderTwoNodes()
        {
            const string Xml = "<r><a/><b/></r>";

            Assert.IsTrue(Boolean(Xml, "/r/a << /r/b", XsltVersion.V20));
            Assert.IsFalse(Boolean(Xml, "/r/b << /r/a", XsltVersion.V20));
            Assert.IsTrue(Boolean(Xml, "/r/b >> /r/a", XsltVersion.V20));
        }

        [TestMethod]
        public void AnEmptyOperandMakesANodeComparisonEmpty()
        {
            Assert.AreEqual(0, Count("/r/nothing is /r/nothing"));
        }

        [TestMethod]
        public void TheseOperatorNamesAreStillNamesElsewhere()
        {
            // cast, castable, treat, instance, intersect, except, union and is may all name an element.
            const string Xml = "<r><cast/><treat/><instance/><intersect/><except/><union/><is/></r>";

            foreach (string name in new[] { "cast", "treat", "instance", "intersect", "except", "union", "is" })
            {
                Assert.AreEqual(1, CountIn(Xml, $"/r/{name}"), name);
            }
        }

        [TestMethod]
        public void TheseOperatorsDoNotExistInVersionOne()
        {
            foreach (string expression in new[]
            {
                "1 instance of xs:integer", "'5' cast as xs:integer", "'5' castable as xs:integer",
            })
            {
                Assert.ThrowsExactly<XsltException>(
                    () => Evaluate("<r/>", expression, XsltVersion.V10), expression);
            }
        }

        // ---- Dates, times and durations ------------------------------------------------------------------

        [TestMethod]
        [DataRow("xs:date('2020-02-29')", "2020-02-29")]
        [DataRow("xs:date('2020-01-01Z')", "2020-01-01Z")]
        [DataRow("xs:date('2020-01-01+01:00')", "2020-01-01+01:00")]
        [DataRow("xs:time('13:45:00')", "13:45:00")]
        [DataRow("xs:time('13:45:00.5')", "13:45:00.5")]
        [DataRow("xs:dateTime('2020-01-01T13:45:00')", "2020-01-01T13:45:00")]
        [DataRow("xs:dateTime('2020-01-01T13:45:00Z')", "2020-01-01T13:45:00Z")]
        public void DatesAndTimesRoundTripThroughTheirLexicalForm(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        [DataRow("xs:duration('P1Y2M')", "P1Y2M")]
        [DataRow("xs:duration('P1DT2H')", "P1DT2H")]
        [DataRow("xs:yearMonthDuration('P14M')", "P1Y2M")]
        [DataRow("xs:dayTimeDuration('PT90M')", "PT1H30M")]
        [DataRow("xs:dayTimeDuration('-PT1H')", "-PT1H")]
        [DataRow("xs:yearMonthDuration('P0M')", "P0M")]
        public void DurationsAreWrittenInCanonicalForm(string expression, string expected)
        {
            // The canonical form carries the value up into the largest units it fits, so P14M is P1Y2M.
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        [DataRow("xs:date('2020-02-30')")]
        [DataRow("xs:date('2019-02-29')")]
        [DataRow("xs:time('24:00:01')")]
        [DataRow("xs:time('12:60:00')")]
        [DataRow("xs:dateTime('2020-01-01')")]
        [DataRow("xs:duration('P')")]
        [DataRow("xs:duration('1Y')")]
        [DataRow("xs:yearMonthDuration('P1D')")]
        [DataRow("xs:dayTimeDuration('P1Y')")]
        public void AValueOutsideTheLexicalSpaceOfATemporalTypeIsRejected(string expression)
        {
            Assert.AreEqual("FORG0001", CodeOf(expression), expression);
        }

        [TestMethod]
        public void TheSameInstantWrittenTwoWaysIsOneValue()
        {
            // This is why dates cannot be compared as strings: these read quite differently and are equal.
            Assert.IsTrue(Boolean(
                "<r/>",
                "xs:dateTime('2020-01-01T12:00:00Z') eq xs:dateTime('2020-01-01T13:00:00+01:00')",
                XsltVersion.V20));
        }

        [TestMethod]
        public void DatesOrderByTheMomentTheyDenote()
        {
            Assert.IsTrue(Boolean("<r/>", "xs:date('2020-01-01') lt xs:date('2020-02-01')", XsltVersion.V20));
            Assert.IsTrue(Boolean("<r/>", "xs:time('09:00:00') lt xs:time('17:00:00')", XsltVersion.V20));
            Assert.IsFalse(Boolean("<r/>", "xs:date('2020-02-01') lt xs:date('2020-01-01')", XsltVersion.V20));
        }

        [TestMethod]
        public void AValueWithNoTimezoneIsNotTheSameAsOneInUtc()
        {
            // They compare equal, because the implicit timezone is UTC, but they are written differently and
            // are distinct values in the data model.
            Assert.AreEqual("2020-01-01T00:00:00", Text("xs:dateTime('2020-01-01T00:00:00')"));
            Assert.AreEqual("2020-01-01T00:00:00Z", Text("xs:dateTime('2020-01-01T00:00:00Z')"));
        }

        [TestMethod]
        [DataRow("year-from-date(xs:date('2020-03-15'))", "2020")]
        [DataRow("month-from-date(xs:date('2020-03-15'))", "3")]
        [DataRow("day-from-date(xs:date('2020-03-15'))", "15")]
        [DataRow("hours-from-time(xs:time('13:45:30'))", "13")]
        [DataRow("minutes-from-time(xs:time('13:45:30'))", "45")]
        [DataRow("seconds-from-time(xs:time('13:45:30.5'))", "30.5")]
        [DataRow("year-from-dateTime(xs:dateTime('2020-03-15T13:45:30'))", "2020")]
        [DataRow("hours-from-dateTime(xs:dateTime('2020-03-15T13:45:30'))", "13")]
        public void ComponentsCanBeReadOutOfADateOrTime(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        [DataRow("years-from-duration(xs:duration('P2Y6M'))", "2")]
        [DataRow("months-from-duration(xs:duration('P2Y6M'))", "6")]
        [DataRow("days-from-duration(xs:dayTimeDuration('P3DT4H'))", "3")]
        [DataRow("hours-from-duration(xs:dayTimeDuration('P3DT4H'))", "4")]
        [DataRow("minutes-from-duration(xs:dayTimeDuration('PT90M'))", "30")]
        public void ComponentsCanBeReadOutOfADuration(string expression, string expected)
        {
            // Each component is what remains after the larger ones, so PT90M has 30 minutes and 1 hour.
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        public void EveryComponentOfANegativeDurationIsNegative()
        {
            Assert.AreEqual("-2", Text("years-from-duration(xs:duration('-P2Y6M'))"));
            Assert.AreEqual("-6", Text("months-from-duration(xs:duration('-P2Y6M'))"));
        }

        [TestMethod]
        public void AComponentOfAnEmptySequenceIsAnEmptySequence()
        {
            Assert.AreEqual(0, Count("year-from-date(())"));
            Assert.AreEqual(0, Count("hours-from-duration(())"));
        }

        [TestMethod]
        public void TimezonesAreReadAsDayTimeDurations()
        {
            Assert.AreEqual("PT1H", Text("timezone-from-date(xs:date('2020-01-01+01:00'))"));
            Assert.AreEqual("PT0S", Text("timezone-from-date(xs:date('2020-01-01Z'))"));

            // A value with no timezone has none to report, rather than reporting zero.
            Assert.AreEqual(0, Count("timezone-from-date(xs:date('2020-01-01'))"));
        }

        [TestMethod]
        public void TheCurrentMomentIsAvailableInAllThreeShapes()
        {
            // The values change every run, so what is checked is that they are of the right type and shape.
            Assert.AreEqual(10, Text("current-date()").Length - 1);
            StringAssert.Contains(Text("current-dateTime()"), "T");
            StringAssert.Contains(Text("current-time()"), ":");
            Assert.AreEqual("PT0S", Text("implicit-timezone()"));
        }

        [TestMethod]
        public void ADateAndATimeCanBeCombined()
        {
            Assert.AreEqual(
                "2020-01-01T13:45:00",
                Text("dateTime(xs:date('2020-01-01'), xs:time('13:45:00'))"));
        }

        [TestMethod]
        public void CastingBetweenDurationTypesKeepsOnlyTheHalfTheTargetHas()
        {
            // The one place the specification asks for part of a value to be dropped: a month is not a fixed
            // number of days, so the two halves cannot be converted into one another.
            Assert.AreEqual("P1Y2M", Text("xs:yearMonthDuration(xs:duration('P1Y2M3DT4H'))"));
            Assert.AreEqual("P3DT4H", Text("xs:dayTimeDuration(xs:duration('P1Y2M3DT4H'))"));
        }

        [TestMethod]
        public void DurationsInDifferentHalvesCannotBeOrdered()
        {
            // P1M against P30D has no answer, because a month is between 28 and 31 days.
            Assert.AreEqual(
                "XPTY0004",
                CodeOf("xs:duration('P1M') lt xs:duration('P30D')"));
        }

        // ---- Error codes ---------------------------------------------------------------------------------

        private static string? CodeOf(string expression)
        {
            try
            {
                Evaluate("<r/>", expression, XsltVersion.V20);
                return null;
            }
            catch (XsltException error)
            {
                return error.Code;
            }
        }

        [TestMethod]
        [DataRow("xs:integer('abc')", "FORG0001")]
        [DataRow("xs:boolean('yes')", "FORG0001")]
        [DataRow("xs:decimal('1e3')", "FORG0001")]
        [DataRow("xs:byte('128')", "FORG0001")]
        [DataRow("xs:decimal(99e100)", "FOCA0001")]
        [DataRow("xs:decimal(xs:double('INF'))", "FOCA0002")]
        [DataRow("QName('', 'a:b')", "FOCA0002")]
        [DataRow("QName('http://e/', '1bad')", "FOCA0002")]
        [DataRow("(10)/child::*", "XPTY0019")]
        [DataRow("-'a string'", "XPTY0004")]
        [DataRow("1 idiv 0", "FOAR0001")]
        [DataRow("1 div 0", "FOAR0001")]
        [DataRow("zero-or-one((1, 2))", "FORG0003")]
        [DataRow("one-or-more(())", "FORG0004")]
        [DataRow("exactly-one((1, 2))", "FORG0005")]
        [DataRow("matches('a', 'a', 'z')", "FORX0001")]
        [DataRow("matches('a', '[')", "FORX0002")]
        [DataRow("(1, 2) eq 1", "XPTY0004")]
        [DataRow("no-such-function()", "XPST0017")]
        [DataRow("$undeclared", "XPST0008")]
        [DataRow("1 +", "XPST0003")]
        public void ErrorsCarryTheCodeTheSpecificationNames(string expression, string expected)
        {
            // The message is for whoever has to fix the expression; the code is for anything that has to
            // react to this error in particular, a conformance suite above all.
            Assert.AreEqual(expected, CodeOf(expression), expression);
        }

        [TestMethod]
        [DataRow("xs:anyType('a')", "XPST0051")]
        [DataRow("xs:integer('1', '2')", "XPST0017")]
        public void TypeAndNameErrorsCarryTheirCodes(string expression, string expected)
        {
            Assert.AreEqual(expected, CodeOf(expression), expression);
        }

        [TestMethod]
        public void TheContextItemCanBeAnAtomicValue()
        {
            // XPath 1.0's context item is always a node, so a predicate over atomic values had nothing for
            // '.' to mean. XPath 2.0 makes it an item, and this is the whole point of the change.
            Assert.AreEqual("3 4 5", Text("string-join(for $i in (1 to 5)[. ge 3] return string($i), ' ')"));
            Assert.AreEqual("2", Text("count((1, 2, 3)[. mod 2 = 1])"));
            Assert.AreEqual("b", Text("('a', 'b')[. = 'b']"));

            // A single atomic value is a sequence of one, so it filters like any other.
            Assert.AreEqual("0", Text("(0)[1]"));
            Assert.AreEqual("0", Text("count((0)[2])"));

            // And a range variable bound to one filters the same way: 2 and 4 survive, and nothing else.
            Assert.AreEqual("2", Text("count(for $x in 1 to 5 return $x[. mod 2 = 0])"));
            Assert.AreEqual("6", Text("sum(for $x in 1 to 5 return $x[. mod 2 = 0])"));
        }

        [TestMethod]
        public void AStepNeedsANodeToStartFrom()
        {
            // An atomic context item has no children, no parent and no siblings, and saying so is XPTY0020
            // rather than an empty answer.
            Assert.AreEqual("XPTY0020", CodeOf("(1)[foo]"));
            Assert.AreEqual("XPTY0020", CodeOf("(1)[..]"));

            // A function that reads the context node says the same, rather than answering about a node it
            // does not have.
            Assert.AreEqual("XPTY0004", CodeOf("1[lang('en')]"));
            Assert.AreEqual("XPTY0004", CodeOf("(1 to 10)[base-uri()]"));

            // Reading the context item where there is none is a different complaint, with its own code.
            Assert.AreEqual("XPDY0002", CodeWithoutAFocus("."));
            Assert.AreEqual("XPDY0002", CodeWithoutAFocus("foo"));
            Assert.AreEqual("XPDY0002", CodeWithoutAFocus("string()"));
            Assert.AreEqual("XPDY0002", CodeWithoutAFocus("local-name()"));

            // But an expression that never asks for it is unaffected.
            Assert.IsNull(CodeWithoutAFocus("1 + 1"));
            Assert.IsNull(CodeWithoutAFocus("count((1, 2)[. gt 1])"));
        }

        [TestMethod]
        public void AUriIsTextButNotAString()
        {
            // xs:anyURI holds text and every string operation works on it, so the promotion in the function
            // conversion rules has to be there or half the library would refuse one.
            Assert.AreEqual("3", Text("string-length(xs:anyURI('abc'))"));
            Assert.AreEqual("true", Text("starts-with(xs:anyURI('http://x/'), 'http')"));
            Assert.AreEqual("abc", Text("xs:string(xs:anyURI('abc'))"));

            // And it compares as text, which is what the operator mapping says of it.
            Assert.AreEqual("true", Text("xs:anyURI('a') = 'a'"));
            Assert.AreEqual("true", Text("xs:anyURI('a') lt 'b'"));

            // Where the type is the question it is a type of its own, and not a string.
            Assert.AreEqual("true", Text("xs:anyURI('a') instance of xs:anyURI"));
            Assert.AreEqual("false", Text("xs:anyURI('a') instance of xs:string"));
            Assert.AreEqual("false", Text("'a' instance of xs:anyURI"));
        }

        [TestMethod]
        public void AUriIsNotReadAsWhateverItsTextLooksLike()
        {
            // The casting table has no route from xs:anyURI to anything but text. Without that row, the text
            // of a URI would be read as a number or a date whenever one was asked for — which is a reading of
            // how the value was written rather than a conversion of what it is.
            Assert.AreEqual("XPTY0004", CodeOf("xs:anyURI('1') cast as xs:integer"));
            Assert.AreEqual("XPTY0004", CodeOf("xs:anyURI('2004-01-01') cast as xs:date"));
            Assert.AreEqual("XPTY0004", CodeOf("xs:anyURI('http://x/') cast as xs:QName"));

            // A string spelling the same text still casts, so a stylesheet that wants the reading asks for it.
            Assert.AreEqual("1", Text("xs:string(xs:anyURI('1')) cast as xs:integer"));
        }

        [TestMethod]
        public void CountingAnAtomicValueGivesOneRatherThanAnError()
        {
            // Every value is a sequence in XPath 2.0, and a single item is a sequence of one — so this is a
            // count of one, where XPath 1.0 would have insisted on a node-set and objected.
            Assert.AreEqual("1", Text("count('abc')"));
            Assert.AreEqual("1", Text("count(1)"));
            Assert.AreEqual("0", Text("count(())"));
        }

        [TestMethod]
        public void AStepMayBeAnExpressionAndNotOnlyAnAxis()
        {
            const string Xml = "<r><a><n>1</n></a><b><n>2</n></b></r>";

            // A parenthesized expression as a step, which is how one asks for two element names at once
            // without repeating the rest of the path.
            Assert.AreEqual(2, CountIn(Xml, "/r/(a|b)/n"));
            Assert.AreEqual("1 2", Evaluate(Xml, "string-join(/r/(a|b)/n, ' ')", XsltVersion.V20)
                .ToCanonicalString());

            // And a function call, which is the step that ends a path in a value rather than a node.
            Assert.AreEqual("1 2", Evaluate(Xml, "string-join(/r/(a|b)/n/string(), ' ')", XsltVersion.V20)
                .ToCanonicalString());

            // Nodes come back in document order without repeats, however the steps reached them.
            Assert.AreEqual(2, CountIn(Xml, "/r/(a|b|a)/n"));

            // A step still needs a node to start from.
            Assert.AreEqual("XPTY0019", CodeOf("(1, 2)/string()"));
        }

        [TestMethod]
        public void AQuoteIsWrittenTwiceToMeanItself()
        {
            // XPath 2.0's only escape, and without it a string holding both kinds of quote cannot be
            // written at all. XPath 1.0 has none, where the same text is two literals side by side.
            Assert.AreEqual("don't", Text("'don''t'"));
            Assert.AreEqual("say \"so\"", Text("\"say \"\"so\"\"\""));
            Assert.AreEqual("'", Text("''''"));
            Assert.AreEqual("\"", Text("\"\"\"\""));
            Assert.AreEqual(string.Empty, Text("''"));
            Assert.AreEqual("true", Text("'fo''o' eq \"fo'o\""));
        }

        [TestMethod]
        public void ASignBindsTighterThanTheTypeOperators()
        {
            // XPath 1.0 applies a sign to a union expression, so it sits outside everything; 2.0 puts
            // UnaryExpr between CastExpr and ValueExpr, so it sits inside 'cast as' and 'instance of'.
            // Read the 1.0 way, '-129 castable as xs:byte' asks for the negative of a boolean.
            Assert.AreEqual("false", Text("-129 castable as xs:byte"));
            Assert.AreEqual("true", Text("-127 castable as xs:byte"));
            Assert.AreEqual("true", Text("-1231.123e3 instance of xs:double"));
            Assert.AreEqual("true", Text("-3 instance of xs:integer"));

            // The signs stack, and the arithmetic around them is unchanged.
            Assert.AreEqual("1", Text("- -1"));
            Assert.AreEqual("-2", Text("-1 * 2"));
            Assert.AreEqual("3", Text("2 - -1"));
        }

        [TestMethod]
        public void ALeadingPlusIsAnOperatorAndNotDecoration()
        {
            // XPath 2.0 has a unary plus, which 1.0 does not. It gives the number back with its type intact.
            Assert.AreEqual("5", Text("+5"));
            Assert.AreEqual("true", Text("+xs:integer(5) instance of xs:integer"));
            Assert.AreEqual("NaN", Text("+0e0 div +0e0"));

            // And because it takes a number, it refuses what is not one — the same as a leading minus.
            Assert.AreEqual("XPTY0004", CodeOf("+'a string'"));
            Assert.AreEqual("XPTY0004", CodeOf("-'a string'"));
        }

        [TestMethod]
        public void NothingInIsNothingOut()
        {
            // A constructor is declared xs:anyAtomicType? -> T?, so an empty argument gives an empty result
            // rather than a value made out of the empty string.
            Assert.AreEqual("0", Text("count(xs:anyURI(()))"));
            Assert.AreEqual("0", Text("count(xs:integer(()))"));
            Assert.AreEqual("0", Text("count(xs:date(()))"));

            // Which is what carries through to a value comparison against a missing attribute: there is no
            // string there to be equal to anything, so there is no answer either.
            Assert.AreEqual("0", Text("count('abc' eq xs:string(/r/@id))"));
            Assert.AreEqual("0", Text("count(compare('b', ()))"));
        }

        [TestMethod]
        public void AKindTestSaysWhichKindOfNodeItWants()
        {
            const string Xml = "<r><a id='1'>x</a><b/><a/></r>";

            // Where '*' takes its meaning from the axis, a kind test names the kind itself.
            Assert.AreEqual(3, CountIn(Xml, "/r/element()"));
            Assert.AreEqual(2, CountIn(Xml, "/r/element(a)"));
            Assert.AreEqual(0, CountIn(Xml, "/r/element(nosuch)"));
            Assert.AreEqual(1, CountIn(Xml, "/r/a/attribute::attribute(id)"));
            Assert.AreEqual(1, CountIn(Xml, "/r/a/attribute::attribute()"));

            // So an attribute test on the child axis selects nothing, where '*' would select every element.
            Assert.AreEqual(0, CountIn(Xml, "/r/attribute()"));
            Assert.AreEqual(3, CountIn(Xml, "/r/*"));

            // And a document-node test asks about the element it holds.
            Assert.AreEqual(1, CountIn(Xml, "/self::document-node()"));
            Assert.AreEqual(1, CountIn(Xml, "/self::document-node(element(r))"));
            Assert.AreEqual(0, CountIn(Xml, "/self::document-node(element(a))"));
        }

        [TestMethod]
        public void AKindTestIsAlsoATypeToTestAgainst()
        {
            const string Xml = "<r><a/></r>";

            Assert.AreEqual("true", Evaluate(Xml, "/r instance of element(r)", XsltVersion.V20).ToCanonicalString());
            Assert.AreEqual("false", Evaluate(Xml, "/r instance of element(a)", XsltVersion.V20).ToCanonicalString());
            Assert.AreEqual("true", Evaluate(Xml, "/r instance of element()", XsltVersion.V20).ToCanonicalString());
            Assert.AreEqual("false", Evaluate(Xml, "/r instance of attribute()", XsltVersion.V20).ToCanonicalString());
            Assert.AreEqual("true", Evaluate(Xml, "/ instance of document-node()", XsltVersion.V20).ToCanonicalString());
        }

        [TestMethod]
        public void TheSchemaKindTestsParseAndThenSayThereIsNoSchema()
        {
            // They name a declaration, and this engine has no schema to hold one — XPST0008, which is what
            // an undeclared name gets, rather than a claim that the syntax is wrong.
            Assert.AreEqual("XPST0008", CodeOf("/r/schema-element(a)"));
            Assert.AreEqual("XPST0008", CodeOf("/r/attribute::schema-attribute(id)"));

            // A wildcard or a string is not a declaration name, and that *is* a syntax error.
            Assert.AreEqual("XPST0003", CodeOf("/r/schema-element(*)"));
            Assert.AreEqual("XPST0003", CodeOf("/r/schema-element('quoted')"));

            // document-node() takes an element test or nothing.
            Assert.AreEqual("XPST0003", CodeOf("/self::document-node(*)"));
            Assert.AreEqual("XPST0003", CodeOf("/self::document-node(name)"));
            Assert.AreEqual("XPST0003", CodeOf("/self::document-node(processing-instruction())"));

            // And a prefix inside one has to be bound, like any other.
            Assert.AreEqual("XPST0081", CodeOf("/r/element(nosuch:a)"));

            // A bare element() is an axis step, so with no focus it is the usual complaint.
            Assert.AreEqual("XPDY0002", CodeWithoutAFocus("element()"));
        }

        [TestMethod]
        public void RootIsTheTopOfWhicheverTreeTheNodeIsIn()
        {
            const string Xml = "<r><a><b/></a></r>";

            Assert.AreEqual(1, CountIn(Xml, "root(/r/a/b)"));
            Assert.AreEqual("true", Text("count(root(())) = 0"));

            // With no argument it is the context node's, so there has to be one.
            Assert.AreEqual("XPDY0002", CodeWithoutAFocus("root()"));
            Assert.AreEqual("XPTY0004", CodeOf("root(1)"));
        }

        [TestMethod]
        public void AskingForACollectionSaysWhichQuestionFailed()
        {
            // FODC0002 both ways round, because both are true here: there is no default collection to be
            // the answer, and a named one is not there either. Naming neither is what the old 'unknown
            // function' did, which sends the reader looking for a typo.
            Assert.AreEqual("FODC0002", CodeOf("collection()"));
            Assert.AreEqual("FODC0002", CodeOf("collection('nowhere')"));

            // No argument and an empty one ask the same question, both meaning the default collection, so
            // this is not a collection named by the empty URI -- which is what the message used to say.
            Assert.AreEqual("FODC0002", CodeOf("collection(())"));

            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => Evaluate("<r/>", "collection(())", XsltVersion.V20)).Message,
                "the default collection");
        }

        [TestMethod]
        public void SomeFunctionsGainedAnArgumentInTwoPointZero()
        {
            // sum() gained the value to return for an empty sequence, which is what lets a sum of durations
            // have a zero at all; lang() gained the node to ask about.
            Assert.AreEqual("P0M", Text("sum((), xs:yearMonthDuration('P0M'))"));
            Assert.AreEqual("0", Text("sum(())"));
            Assert.AreEqual(
                "true",
                Evaluate("<r xml:lang='en'/>", "lang('en', /r)", XsltVersion.V20).ToCanonicalString());

            // And string-join needs both of its own: there is no one-argument form to fall back on.
            Assert.AreEqual("XPST0017", CodeOf("string-join('a')"));
        }

        [TestMethod]
        public void AReservedNameNamesNoFunction()
        {
            // Each of these is a kind test or a keyword the grammar needs, so the call does not fail to
            // resolve — it does not parse. XPST0003, not XPST0017.
            Assert.AreEqual("XPST0003", CodeOf("node(1)"));
            Assert.AreEqual("XPST0003", CodeOf("if(1)"));
            Assert.AreEqual("XPST0003", CodeOf("item(1)"));
            Assert.AreEqual("XPST0003", CodeOf("empty-sequence(1)"));

            // A prefix lifts the reservation, the name then being an ordinary one.
            Assert.AreEqual("XPST0081", CodeOf("my:node(1)"));
        }

        [TestMethod]
        public void AnUnboundPrefixIsSaidBeforeTheFunctionIsLookedUp()
        {
            // Saying the function is unknown would send the reader looking for a function, where what is
            // missing is the binding.
            Assert.AreEqual("XPST0081", CodeOf("nosuch:whatever(1, 2, 3)"));
        }

        [TestMethod]
        public void AnAggregateAnswersAtTheWidestTypeItWasGiven()
        {
            // min((1, xs:float(2), xs:decimal(3))) is an xs:float, not the xs:integer that happened to be
            // smallest: the values are brought to a common type before the question is asked.
            Assert.AreEqual("true", Text("min((1, xs:float(2), xs:decimal(3))) instance of xs:float"));
            Assert.AreEqual("true", Text("min((5, 5.0e0)) instance of xs:double"));
            Assert.AreEqual("true", Text("max((1, 2)) instance of xs:integer"));

            // An integer already is a decimal, which is substitution rather than promotion, so nothing
            // converts and the value keeps the type it had.
            Assert.AreEqual("true", Text("min((1, xs:decimal(2))) instance of xs:integer"));
        }

        [TestMethod]
        public void IdivCountsAndSoAnswersWithAnInteger()
        {
            // The one operator whose result type does not follow from promoting its operands: it asks how
            // many times one number goes into another, and that is a count.
            Assert.AreEqual("true", Text("(xs:float(6) idiv xs:decimal(2)) instance of xs:integer"));
            Assert.AreEqual("true", Text("(xs:integer(6) idiv xs:float(2)) instance of xs:integer"));
            Assert.AreEqual("true", Text("(6.5e0 idiv 2) instance of xs:integer"));
            Assert.AreEqual("3", Text("6.5e0 idiv 2"));
        }

        [TestMethod]
        public void RoundingKeepsTheSignOfAZero()
        {
            // xs:double has two zeros and they write differently, so losing the sign here shows up in the
            // result. Rounding -0.2 gives negative zero, not zero.
            Assert.AreEqual("-0", Text("round(xs:double('-0.2'))"));
            Assert.AreEqual("-0", Text("round(xs:double('-0'))"));
            Assert.AreEqual("-0", Text("ceiling(xs:double('-0.5'))"));
            Assert.AreEqual("0", Text("round(xs:double('0.2'))"));
            Assert.AreEqual("-1", Text("round(xs:double('-0.6'))"));
        }

        [TestMethod]
        public void ConcatWritesANumberTheWayStringDoes()
        {
            // Declared xs:anyAtomicType rather than xs:string, so the conversion rules leave a number a
            // number and concat itself decides the lexical form.
            Assert.AreEqual("INF!", Text("concat(xs:double('INF'), '!')"));
            Assert.AreEqual("1.0E18!", Text("concat(xs:double('1e18'), '!')"));
        }

        [TestMethod]
        public void SumHasATypeAndNotEverythingHasASum()
        {
            // A sum takes the type promotion gives it, rather than coming back a double whatever went in.
            Assert.AreEqual("true", Text("sum((1, 2)) instance of xs:integer"));
            Assert.AreEqual("P2D", Text("sum((xs:dayTimeDuration('P1D'), xs:dayTimeDuration('P1D')))"));
            Assert.AreEqual("0", Text("sum(())"));

            // And what cannot be added is refused rather than read as a number: a string has no sum, and a
            // year-month duration and a day-time one have no common scale to be added on.
            Assert.AreEqual("FORG0006", CodeOf("sum('a string')"));
            Assert.AreEqual(
                "FORG0006",
                CodeOf("sum((xs:yearMonthDuration('P1Y'), xs:dayTimeDuration('P1D')))"));
        }

        [TestMethod]
        public void DividingByZeroSaysWhichKindOfNothingItReached()
        {
            // A zero divisor is the same error whatever type the operands are: doubles do not make it an
            // overflow. A duration divided by zero is different again — the answer would be a duration, and
            // there is no infinite one for it to be.
            Assert.AreEqual("FOAR0001", CodeOf("1 idiv 0"));
            Assert.AreEqual("FOAR0001", CodeOf("1 idiv 0e0"));
            Assert.AreEqual("FOAR0001", CodeOf("xs:float('1') idiv xs:float('0')"));
            Assert.AreEqual("FODT0002", CodeOf("xs:dayTimeDuration('P3D') div 0"));
            Assert.AreEqual("FODT0002", CodeOf("xs:yearMonthDuration('P1Y') div xs:double('-0')"));
            Assert.AreEqual("FOCA0005", CodeOf("xs:dayTimeDuration('P3D') div xs:double('NaN')"));
        }

        [TestMethod]
        public void AMomentOutOfRangeIsAnOverflowRatherThanABadValue()
        {
            // '2004-02-30' names no day and never will; '1000000000-01-01' names one perfectly well and
            // this engine holds the year in an int. Different codes, so a limit is not read as a typo.
            Assert.AreEqual("FORG0001", CodeOf("xs:date('2004-02-30')"));
            Assert.AreEqual("FORG0001", CodeOf("xs:date('02004-01-01')"));
            Assert.AreEqual("FODT0001", CodeOf("xs:date('1000000000-01-01')"));
            Assert.AreEqual("FODT0001", CodeOf("xs:date('-1000000000-01-01')"));

            // The rest of the form is still checked past the sign, so a bad month in a bad era is still a
            // bad month — and February keeps its length in a year too long to hold.
            Assert.AreEqual("FORG0001", CodeOf("xs:date('-2004-13-01')"));
            Assert.AreEqual("FORG0001", CodeOf("xs:date('25252734927766554-02-30')"));
            Assert.AreEqual("FODT0001", CodeOf("xs:date('25252734927766554-02-28')"));

            // And moving a date off the end of the range is the same complaint, rather than the bare
            // ArgumentOutOfRangeException the framework raises about an argument it would not take.
            Assert.AreEqual(
                "FODT0001", CodeOf("xs:date('999999999-12-31') + xs:yearMonthDuration('P1Y')"));
            Assert.AreEqual(
                "FODT0001", CodeOf("xs:date('-999999999-01-01') - xs:dayTimeDuration('P1D')"));
        }

        [TestMethod]
        public void TheTypesWithNoConstructorHaveNoFunctionOfTheirName()
        {
            // XPath 2.0 withholds a constructor from four types: the abstract xs:NOTATION, and the three that
            // name a place in the hierarchy rather than a set of values. There is no function to call, which
            // is a different complaint from a type this engine has not implemented.
            Assert.AreEqual("XPST0017", CodeOf("xs:NOTATION('a:b')"));
            Assert.AreEqual("XPST0017", CodeOf("xs:anyAtomicType('a')"));
            Assert.AreEqual("XPST0017", CodeOf("xs:anySimpleType('a')"));

            // xs:anyType is not among the four -- it has a constructor by the rule, and no values for one
            // to build, so the complaint is the other one: a type this engine cannot construct.
            Assert.AreEqual("XPST0051", CodeOf("xs:anyType('a')"));

            // And the three list types do have constructors, a cast to one being defined: the function is
            // that cast under its other spelling. XPath 3.0 added them, though, so below 3.0 there is no
            // function of the name at all — this had read them as available at every version, which is
            // what the suite's function-1902 is written to catch.
            Assert.AreEqual(
                "a b",
                Evaluate("<r/>", "string-join(xs:NMTOKENS('a b'), ' ')", XsltVersion.V30)
                    .ToCanonicalString());
            Assert.AreEqual("XPST0017", CodeOf("xs:NMTOKENS('a b')"));

            // xs:error and xs:numeric are 3.0's types outright, and the same follows.
            Assert.AreEqual("XPST0017", CodeOf("xs:numeric('1')"));
        }

        [TestMethod]
        public void AnUnimplementedTypeIsDistinguishableFromAWrongValue()
        {
            // XPST0051 says the type name is not defined here at all; FORG0001 says the value is wrong for a
            // type that is. Telling them apart is what lets a conformance run separate what this engine does
            // not do from what it does badly.
            Assert.AreEqual("XPST0051", CodeOf("xs:anyType('a')"));
            Assert.AreEqual("FORG0001", CodeOf("xs:integer('P1Y')"));
        }

        [TestMethod]
        public void NameFunctionsAcceptASequenceAsWellAsANodeSet()
        {
            // count() and the name functions predate sequences and demanded node-sets, so composing them with
            // anything returning a sequence failed.
            const string Xml = "<r><i>a</i><i>b</i></r>";

            Assert.AreEqual("2", Evaluate(Xml, "count(reverse(/r/i))", XsltVersion.V20).ToStringValue());
            Assert.AreEqual("3", Evaluate(Xml, "sum((1, 2))", XsltVersion.V20).ToStringValue());

            // local-name() is declared node()?, so what it takes from a sequence is one node — the sequence
            // being a sequence is what it could not do before, not the number of items in it.
            Assert.AreEqual(
                "i", Evaluate(Xml, "local-name(reverse(/r/i)[1])", XsltVersion.V20).ToStringValue());
        }

        [TestMethod]
        public void ANameFunctionStillTakesOnlyOneNode()
        {
            // The other half of the rule above. Two nodes is not a node, and 1.0's habit of quietly taking the
            // first is what XPath 2.0 declared the signatures to stop.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Evaluate("<r><i>a</i><i>b</i></r>", "local-name(/r/i)", XsltVersion.V20));

            Assert.AreEqual("XPTY0004", error.Code);
            Assert.AreEqual("i", Evaluate("<r><i>a</i></r>", "local-name(/r/i)", XsltVersion.V20).ToStringValue());
        }

        [TestMethod]
        public void ACodeSurvivesBeingGivenAPosition()
        {
            // The parser wraps an error to add the offset, which must not discard what kind of error it was.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Evaluate("<r/>", "no-such-function()", XsltVersion.V20));

            Assert.AreEqual("XPST0017", error.Code);
            StringAssert.Contains(error.Message, "offset");
        }

        [TestMethod]
        public void AnEngineErrorWithNoSpecifiedCodeCarriesNone()
        {
            // Not every error this engine reports is one a specification names, and inventing a code for
            // those would be worse than admitting there is none.
            XsltException error = new XsltException("something this engine objects to");

            Assert.IsNull(error.Code);
        }

        // ---- Bindings and conditionals -------------------------------------------------------------------

        [TestMethod]
        [DataRow("for $x in (1, 2, 3) return $x * 2", "2 4 6")]
        [DataRow("for $x in (1, 2) return ($x, $x)", "1 1 2 2")]
        [DataRow("for $x in () return $x", "")]
        [DataRow("for $x in 1 to 3 return $x", "1 2 3")]
        [DataRow("string-join(for $x in ('a', 'b') return upper-case($x), '')", "AB")]
        public void ForIteratesAndConcatenates(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        public void ForClausesNestRatherThanRunInParallel()
        {
            // for $x in A, $y in B is for $x in A return (for $y in B return ...), so this is the product.
            Assert.AreEqual("1 2 2 4", Text("for $x in (1, 2), $y in (1, 2) return $x * $y"));
            Assert.AreEqual(6, Count("for $x in (1, 2, 3), $y in ('a', 'b') return concat($x, $y)"));
        }

        [TestMethod]
        public void ALaterClauseSeesTheVariablesBoundBeforeIt()
        {
            Assert.AreEqual("1 1 2 1 2 3", Text("for $x in 1 to 3, $y in 1 to $x return $y"));
        }

        [TestMethod]
        public void NestedForsKeepTheirOwnVariables()
        {
            // The inner binding sits one slot deeper, and the outer variable must survive the inner loop.
            Assert.AreEqual("11 12 21 22", Text(
                "for $x in (1, 2) return string-join(for $y in (1, 2) return concat($x, $y), ' ')"));
        }

        [TestMethod]
        public void AnInnerBindingShadowsAnOuterOneOfTheSameName()
        {
            Assert.AreEqual("9 9", Text("for $x in (1, 2) return (for $x in (9) return $x)"));
        }

        [TestMethod]
        [DataRow("some $x in (1, 2, 3) satisfies $x > 2", true)]
        [DataRow("some $x in (1, 2, 3) satisfies $x > 5", false)]
        [DataRow("every $x in (1, 2, 3) satisfies $x > 0", true)]
        [DataRow("every $x in (1, 2, 3) satisfies $x > 1", false)]
        public void QuantifiersAnswerOverTheSequence(string expression, bool expected)
        {
            Assert.AreEqual(expected, Boolean("<r/>", expression, XsltVersion.V20));
        }

        [TestMethod]
        public void QuantifiersOverAnEmptySequenceFollowTheUsualConvention()
        {
            // Vacuously: nothing satisfies it, and everything does.
            Assert.IsFalse(Boolean("<r/>", "some $x in () satisfies true()", XsltVersion.V20));
            Assert.IsTrue(Boolean("<r/>", "every $x in () satisfies false()", XsltVersion.V20));
        }

        [TestMethod]
        [DataRow("if (true()) then 'a' else 'b'", "a")]
        [DataRow("if (false()) then 'a' else 'b'", "b")]
        [DataRow("if (1 > 2) then 'a' else 'b'", "b")]
        [DataRow("if (()) then 'a' else 'b'", "b")]
        [DataRow("if (true()) then () else 'b'", "")]
        public void ConditionalsChooseOneBranch(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        public void ConditionalsCombineWithBindings()
        {
            Assert.AreEqual("1 4 3", Text("for $x in (1, 2, 3) return if ($x = 2) then 4 else $x"));
        }

        [TestMethod]
        public void KeywordsAreStillNamesWhereAnExpressionCannotStart()
        {
            // Elements may be called for, if, some, every or return, and a path must still reach them.
            const string Xml = "<r><for>1</for><if>2</if><some>3</some><every>4</every><return>5</return></r>";

            Assert.AreEqual("1", Evaluate(Xml, "/r/for", XsltVersion.V20).ToStringValue());
            Assert.AreEqual("2", Evaluate(Xml, "/r/if", XsltVersion.V20).ToStringValue());
            Assert.AreEqual("3", Evaluate(Xml, "/r/some", XsltVersion.V20).ToStringValue());
            Assert.AreEqual("4", Evaluate(Xml, "/r/every", XsltVersion.V20).ToStringValue());
            Assert.AreEqual("5", Evaluate(Xml, "/r/return", XsltVersion.V20).ToStringValue());
        }

        [TestMethod]
        public void BindingsWorkOverDocumentNodes()
        {
            const string Xml = "<r><i n='1'>a</i><i n='2'>b</i></r>";

            Assert.AreEqual(
                "a b",
                Evaluate(Xml, "string-join(for $i in /r/i return string($i), ' ')", XsltVersion.V20)
                    .ToStringValue());

            Assert.IsTrue(Boolean(Xml, "some $i in /r/i satisfies $i = 'b'", XsltVersion.V20));
            Assert.IsFalse(Boolean(Xml, "every $i in /r/i satisfies $i = 'b'", XsltVersion.V20));
        }

        [TestMethod]
        public void TheseConstructsDoNotExistInVersionOne()
        {
            foreach (string expression in new[]
            {
                "for $x in (1) return $x",
                "some $x in (1) satisfies $x",
                "if (true()) then 1 else 2",
            })
            {
                Assert.ThrowsExactly<XsltException>(
                    () => Evaluate("<r/>", expression, XsltVersion.V10), expression);
            }
        }

        // ---- The XPath 2.0 function library --------------------------------------------------------------

        [TestMethod]
        [DataRow("empty(())", "true")]
        [DataRow("empty((1))", "false")]
        [DataRow("exists(())", "false")]
        [DataRow("exists((1, 2))", "true")]
        [DataRow("reverse((1, 2, 3))", "3 2 1")]
        [DataRow("distinct-values((1, 2, 1, 3, 2))", "1 2 3")]
        [DataRow("subsequence((1, 2, 3, 4), 2)", "2 3 4")]
        [DataRow("subsequence((1, 2, 3, 4), 2, 2)", "2 3")]
        [DataRow("insert-before((1, 4), 2, (2, 3))", "1 2 3 4")]
        [DataRow("remove((1, 2, 3), 2)", "1 3")]
        [DataRow("index-of((10, 20, 10), 10)", "1 3")]
        public void SequenceFunctionsWork(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        public void DistinctValuesTellsNumbersFromStrings()
        {
            // 1 and '1' read alike but are not the same value, so both survive.
            Assert.AreEqual(2, Count("distinct-values((1, '1'))"));
        }

        [TestMethod]
        [DataRow("zero-or-one(())")]
        [DataRow("zero-or-one((1))")]
        [DataRow("one-or-more((1, 2))")]
        [DataRow("exactly-one((1))")]
        public void CardinalityFunctionsAcceptWhatTheyPromise(string expression)
        {
            Text(expression);
        }

        [TestMethod]
        [DataRow("zero-or-one((1, 2))")]
        [DataRow("one-or-more(())")]
        [DataRow("exactly-one(())")]
        [DataRow("exactly-one((1, 2))")]
        public void CardinalityFunctionsRejectWhatTheyDoNot(string expression)
        {
            Assert.ThrowsExactly<XsltException>(() => Text(expression));
        }

        [TestMethod]
        [DataRow("deep-equal((1, 2), (1, 2))", true)]
        [DataRow("deep-equal((1, 2), (2, 1))", false)]
        [DataRow("deep-equal((1), (1, 2))", false)]
        [DataRow("deep-equal((), ())", true)]
        public void DeepEqualComparesSequencesInOrder(string expression, bool expected)
        {
            // Unlike '=', this asks whether the sequences are the same rather than whether they overlap.
            Assert.AreEqual(expected, Boolean("<r/>", expression, XsltVersion.V20));
        }

        [TestMethod]
        [DataRow("abs(-5)", "5")]
        [DataRow("abs(5)", "5")]
        [DataRow("abs(xs:decimal('-1.5'))", "1.5")]
        [DataRow("min((3, 1, 2))", "1")]
        [DataRow("max((3, 1, 2))", "3")]
        [DataRow("avg((1, 2, 3))", "2")]
        public void NumericFunctionsWork(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        public void AbsKeepsTheTypeItWasGiven()
        {
            // An integer stays an integer, so it has no decimal point.
            Assert.AreEqual("5", Text("abs(-5)"));
            Assert.AreEqual("1.5", Text("abs(xs:decimal('-1.5'))"));
        }

        [TestMethod]
        [DataRow("round-half-to-even(0.5)", "0")]
        [DataRow("round-half-to-even(1.5)", "2")]
        [DataRow("round-half-to-even(2.5)", "2")]
        [DataRow("round-half-to-even(3.5)", "4")]
        public void RoundHalfToEvenBreaksTiesTowardsEven(string expression, string expected)
        {
            // The point of the function: round() would give 1, 2, 3, 4 here.
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        [DataRow("upper-case('abC')", "ABC")]
        [DataRow("lower-case('AbC')", "abc")]
        [DataRow("ends-with('hello', 'llo')", "true")]
        [DataRow("ends-with('hello', 'he')", "false")]
        [DataRow("string-join(('a', 'b', 'c'), '-')", "a-b-c")]
        [DataRow("string-join((), '-')", "")]
        [DataRow("compare('a', 'b')", "-1")]
        [DataRow("compare('b', 'a')", "1")]
        [DataRow("compare('a', 'a')", "0")]
        [DataRow("codepoints-to-string((72, 105))", "Hi")]
        [DataRow("string-to-codepoints('Hi')", "72 105")]
        public void StringFunctionsWork(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        [DataRow("matches('abracadabra', 'bra')", "true")]
        [DataRow("matches('abracadabra', '^a.*a$')", "true")]
        [DataRow("matches('abracadabra', '^bra')", "false")]
        [DataRow("matches('ABC', 'abc', 'i')", "true")]
        [DataRow("replace('abracadabra', 'bra', '*')", "a*cada*")]
        // Each 'a' plus the character after it becomes that character; the trailing 'a' has nothing after it
        // and so is left alone, as are the two 'r's that no match covered.
        [DataRow("replace('abracadabra', 'a(.)', '$1')", "brcdbra")]
        [DataRow("tokenize('a,b,c', ',')", "a b c")]
        [DataRow("tokenize('a1b22c', '[0-9]+')", "a b c")]
        public void RegularExpressionFunctionsWork(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        public void AnInvalidRegularExpressionFlagIsReported()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Text("matches('a', 'a', 'z')"));

            StringAssert.Contains(error.Message, "z");
        }

        [TestMethod]
        public void TheseFunctionsDoNotExistInVersionOne()
        {
            // In a 1.0 stylesheet an unprefixed name that is not in the core library is a mistake, and saying
            // so is more useful than quietly providing a function the stylesheet was not written against.
            foreach (string expression in new[]
            {
                "matches('a', 'a')", "upper-case('a')", "distinct-values((1))", "abs(-1)",
            })
            {
                Assert.ThrowsExactly<XsltException>(
                    () => Evaluate("<r/>", expression, XsltVersion.V10), expression);
            }
        }

        [TestMethod]
        public void SequenceFunctionsReachNodesToo()
        {
            // A node-set contributes its nodes, so these work over document content and not only over
            // literal sequences.
            const string Xml = "<r><i>c</i><i>a</i><i>b</i></r>";

            Assert.AreEqual("c a b", Evaluate(Xml, "string-join(/r/i, ' ')", XsltVersion.V20).ToStringValue());
            Assert.AreEqual("b a c", Evaluate(Xml, "string-join(reverse(/r/i), ' ')", XsltVersion.V20).ToStringValue());
            Assert.AreEqual("3", Evaluate(Xml, "count(distinct-values(/r/i))", XsltVersion.V20).ToStringValue());
        }

        // ---- Arithmetic ----------------------------------------------------------------------------------

        [TestMethod]
        [DataRow("1 + 1", "2")]
        [DataRow("2 * 3", "6")]
        [DataRow("7 - 10", "-3")]
        [DataRow("7 idiv 2", "3")]
        [DataRow("-7 idiv 2", "-3")]
        [DataRow("7 mod 2", "1")]
        public void IntegerArithmeticStaysIntegral(string expression, string expected)
        {
            // The result has no decimal point, which is the visible half of the type being preserved.
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        public void DividingTwoIntegersGivesADecimal()
        {
            // The rule that catches people out: div is not truncating, idiv is.
            Assert.AreEqual("0.5", Text("1 div 2"));
            Assert.AreEqual("2.5", Text("5 div 2"));
            Assert.AreEqual("2", Text("5 idiv 2"));
        }

        [TestMethod]
        [DataRow("1 + 1.5", "2.5")]
        [DataRow("xs:decimal('1.1') + xs:decimal('2.2')", "3.3")]
        [DataRow("xs:decimal('0.1') * 3", "0.3")]
        public void DecimalArithmeticIsExact(string expression, string expected)
        {
            // 0.1 + 0.2 in binary floating point is famously not 0.3; in decimal it is.
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        public void ADoubleOperandMakesTheWholeResultADouble()
        {
            Assert.AreEqual("2.5", Text("xs:double('1') + xs:double('1.5')"));
            Assert.AreEqual("3", Text("xs:double('1') + 2"));
        }

        [TestMethod]
        public void IntegerArithmeticWidensRatherThanWrapping()
        {
            // xs:integer is unbounded, so arithmetic that leaves 64 bits behind carries on into a wider
            // integer. An unchecked multiply would wrap to a negative number, which is the one answer
            // the specification forbids, and refusing outright was the answer here until the wide form
            // existed to carry it.
            Assert.AreEqual("9223372036854775808", Text("xs:integer('9223372036854775807') + 1"));
            Assert.AreEqual("12000000000000000000", Text("xs:integer('4000000000000000000') * 3"));

            // And back down again where the answer fits, so that a value is stored the one way
            // whenever it can be.
            Assert.AreEqual(
                "9223372036854775807", Text("(xs:integer('9223372036854775807') + 1) - 1"));

            Assert.AreEqual("true", Text("(xs:integer('9223372036854775807') + 1) gt 0"));
        }

        [TestMethod]
        public void ArithmeticOnNodeContentIsUnchangedByTheVersion()
        {
            // A node's value is untyped and promotes to double, so document arithmetic reads the same in
            // both versions. This is what keeps existing stylesheets meaning what they meant.
            const string Xml = "<r><a>3</a><b>2</b></r>";

            foreach (XsltVersion version in new[] { XsltVersion.V10, XsltVersion.V20 })
            {
                Assert.AreEqual(
                    "1.5", Evaluate(Xml, "/r/a div /r/b", version).ToStringValue(), $"{version}");
            }
        }

        [TestMethod]
        public void VersionOneKeepsOneNumericType()
        {
            // Everything is a double there, so division never truncates and 1 + 1 has no integer-ness to
            // preserve.
            Assert.AreEqual("0.5", Evaluate("<r/>", "1 div 2", XsltVersion.V10).ToStringValue());
            Assert.AreEqual("2", Evaluate("<r/>", "1 + 1", XsltVersion.V10).ToStringValue());
        }

        [TestMethod]
        public void DivisionByZeroIsReportedForExactTypes()
        {
            // Integer and decimal division by zero is an error; double division by zero is infinity, which
            // is a value rather than a failure.
            Assert.ThrowsExactly<XsltException>(() => Text("1 idiv 0"));
            Assert.ThrowsExactly<XsltException>(() => Text("1 div 0"));
            Assert.AreEqual("INF", Text("xs:double('1') div xs:double('0')"));
        }

        // ---- Sequences -----------------------------------------------------------------------------------

        [TestMethod]
        [DataRow("(1, 2, 3)", "1 2 3")]
        [DataRow("(3, 1, 1)", "3 1 1")]
        [DataRow("('a', 'b')", "a b")]
        [DataRow("(1, (2, 3), 4)", "1 2 3 4")]
        [DataRow("()", "")]
        [DataRow("(42)", "42")]
        public void SequencesKeepWhatTheyWereGiven(string expression, string expected)
        {
            // A node-set would sort and de-duplicate this; a sequence must not.
            Assert.AreEqual(expected, Evaluate("<r/>", expression, XsltVersion.V20).ToStringValue());
        }

        [TestMethod]
        public void SequencesDoNotNest()
        {
            // (1, (2, 3)) is (1, 2, 3): the data model has no sequence of sequences.
            Assert.AreEqual(4, Count("(1, (2, (3, 4)))"));
            Assert.AreEqual(0, Count("((), ())"));
            Assert.AreEqual(2, Count("((), 1, (), 2)"));
        }

        [TestMethod]
        public void AnEmptySequenceIsFalseAndCountsAsNothing()
        {
            Assert.IsFalse(Boolean("<r/>", "()", XsltVersion.V20));
            Assert.AreEqual(0, Count("()"));
        }

        [TestMethod]
        [DataRow("1 to 5", 5)]
        [DataRow("1 to 1", 1)]
        [DataRow("5 to 1", 0)]
        [DataRow("(1 to 3, 7 to 8)", 5)]
        public void TheRangeOperatorCountsUpwardsOnly(string expression, int expected)
        {
            // A descending range is empty rather than reversed.
            Assert.AreEqual(expected, Count(expression));
        }

        [TestMethod]
        public void TheRangeOperatorProducesIntegers()
        {
            Assert.AreEqual("1 2 3", Evaluate("<r/>", "1 to 3", XsltVersion.V20).ToStringValue());
        }

        [TestMethod]
        public void ACommaIsStillAnArgumentSeparatorInsideACall()
        {
            // The comma is an operator at the top level and a separator in an argument list; parsing them at
            // the same level would turn every two-argument call into one sequence-valued argument.
            Assert.AreEqual("ab", Evaluate("<r/>", "concat('a', 'b')", XsltVersion.V20).ToStringValue());
            Assert.AreEqual("bc", Evaluate("<r/>", "substring('abc', 2, 2)", XsltVersion.V20).ToStringValue());
        }

        [TestMethod]
        public void SequenceSyntaxIsNotAvailableInVersionOne()
        {
            // '(1, 2)' is a syntax error in XPath 1.0, and '()' is too.
            Assert.ThrowsExactly<XsltException>(() => Evaluate("<r/>", "(1, 2)", XsltVersion.V10));
            Assert.ThrowsExactly<XsltException>(() => Evaluate("<r/>", "()", XsltVersion.V10));
        }

        // ---- Value comparisons ---------------------------------------------------------------------------

        [TestMethod]
        [DataRow("1 eq 1", true)]
        [DataRow("1 eq 2", false)]
        [DataRow("1 ne 2", true)]
        [DataRow("1 lt 2", true)]
        [DataRow("2 le 2", true)]
        [DataRow("3 gt 2", true)]
        [DataRow("2 ge 3", false)]
        [DataRow("'a' lt 'b'", true)]
        [DataRow("'b' lt 'a'", false)]
        public void ValueComparisonsCompareOneValueWithOne(string expression, bool expected)
        {
            Assert.AreEqual(expected, Boolean("<r/>", expression, XsltVersion.V20));
        }

        [TestMethod]
        public void AValueComparisonAgainstAnEmptySequenceIsEmpty()
        {
            // Not false: the empty sequence propagates, which is what separates 'eq' from '='.
            XPathValue result = Evaluate("<r/>", "() eq 1", XsltVersion.V20);

            Assert.AreEqual(XPathValueKind.Sequence, result.Kind);
            Assert.AreEqual(0, result.AsSequence().Count);
        }

        [TestMethod]
        public void AValueComparisonRejectsMoreThanOneItem()
        {
            // '=' would ask whether any item matches; 'eq' insists there is only one to ask about.
            Assert.ThrowsExactly<XsltException>(() => Boolean("<r/>", "(1, 2) eq 1", XsltVersion.V20));
        }

        [TestMethod]
        public void GeneralComparisonStillQuantifiesOverTheSequence()
        {
            Assert.IsTrue(Boolean("<r/>", "(0, 1) = 1", XsltVersion.V20));
            Assert.IsFalse(Boolean("<r/>", "(0, 2) = 1", XsltVersion.V20));
        }

        [TestMethod]
        public void OperatorNamesAreStillNamesWhereAnOperatorCannotAppear()
        {
            // An element may perfectly well be called "to" or "eq", so these words are only operators in
            // operator position. This is the rule that makes 'div' work in XPath 1.0 too.
            Assert.AreEqual(1, CountIn("<r><to>x</to><eq>y</eq></r>", "/r/to"));
            Assert.AreEqual(1, CountIn("<r><to>x</to><eq>y</eq></r>", "/r/eq"));
            Assert.AreEqual(2, CountIn("<r><to>x</to><eq>y</eq></r>", "/r/to | /r/eq"));
        }

        private static int Count(string expression)
        {
            XPathValue value = Evaluate("<r/>", expression, XsltVersion.V20);

            return value.Kind switch
            {
                XPathValueKind.Sequence => value.AsSequence().Count,
                XPathValueKind.NodeSet => value.AsNodeSet().Count,
                _ => 1,
            };
        }

        private static int CountIn(string xml, string expression)
        {
            return Evaluate(xml, expression, XsltVersion.V20).AsNodeSet().Count;
        }

        // ---- Type constructors ---------------------------------------------------------------------------

        /// <summary>
        /// The result as XPath 2.0 writes it. These tests are about 2.0, and the two languages write the
        /// same double differently.
        /// </summary>
        private static string Text(string expression)
        {
            return Evaluate("<r/>", expression, XsltVersion.V20).ToCanonicalString();
        }

        [TestMethod]
        [DataRow("xs:integer('5')", "5")]
        [DataRow("xs:integer(5.9)", "5")]
        [DataRow("xs:integer(-5.9)", "-5")]
        [DataRow("xs:double('1.5')", "1.5")]
        [DataRow("xs:double('INF')", "INF")]
        [DataRow("xs:decimal('1.50')", "1.5")]
        [DataRow("xs:string(42)", "42")]
        [DataRow("xs:boolean('true')", "true")]
        [DataRow("xs:boolean('0')", "false")]
        [DataRow("xs:int('-2147483648')", "-2147483648")]
        [DataRow("xs:unsignedByte('255')", "255")]
        public void ConstructorsCastToTheirType(string expression, string expected)
        {
            Assert.AreEqual(expected, Text(expression));
        }

        [TestMethod]
        public void AnIntegerKeepsEighteenDigitsOfPrecision()
        {
            // XPath 2.0 requires at least 18 digits, and a double carries 15 — so an integer that went
            // through a double would come back changed. This is the value the suite uses.
            Assert.AreEqual("999999999999999999", Text("xs:integer('999999999999999999')"));
            Assert.AreEqual("-999999999999999999", Text("xs:integer('-999999999999999999')"));
            Assert.AreEqual("830993497117024304", Text("xs:integer('830993497117024304')"));
        }

        [TestMethod]
        public void AnIntegerHasNoDecimalPointInItsLexicalForm()
        {
            Assert.AreEqual("5", Text("xs:integer(5)"));
            Assert.AreEqual("0", Text("xs:integer(0)"));
        }

        [TestMethod]
        [DataRow("xs:integer('1.5')")]
        [DataRow("xs:integer('abc')")]
        [DataRow("xs:boolean('yes')")]
        [DataRow("xs:decimal('1e3')")]
        [DataRow("xs:double('one')")]
        public void AValueOutsideTheLexicalSpaceIsRejected(string expression)
        {
            // The cast is not the same as XPath 1.0's number(), which answers NaN rather than objecting.
            Assert.ThrowsExactly<XsltException>(() => Text(expression));
        }

        [TestMethod]
        [DataRow("xs:byte('128')")]
        [DataRow("xs:unsignedByte('256')")]
        [DataRow("xs:int('2147483648')")]
        [DataRow("xs:positiveInteger('0')")]
        [DataRow("xs:negativeInteger('0')")]
        [DataRow("xs:nonNegativeInteger('-1')")]
        public void AValueOutsideTheTypesRangeIsRejected(string expression)
        {
            // The derived integer types share a representation and differ only in what they accept.
            Assert.ThrowsExactly<XsltException>(() => Text(expression));
        }

        [TestMethod]
        public void TheSchemaAndFunctionPrefixesNeedNoDeclaration()
        {
            // XPath 2.0 binds xs and fn for you.
            Assert.AreEqual("5", Text("xs:integer('5')"));
            Assert.AreEqual("3", Text("fn:string-length('abc')"));
            Assert.AreEqual("1", Text("fn:count(/r)"));
        }

        [TestMethod]
        public void APrefixedCoreFunctionIsTheSameCallAsAnUnprefixedOne()
        {
            Assert.AreEqual(Text("string-length('hello')"), Text("fn:string-length('hello')"));
            Assert.AreEqual(Text("normalize-space('  a  b ')"), Text("fn:normalize-space('  a  b ')"));
        }

        [TestMethod]
        public void ThePredeclaredPrefixesDoNotExistInVersionOne()
        {
            // They are an XPath 2.0 addition, so a 1.0 stylesheet writing xs: means an extension function.
            Assert.ThrowsExactly<XsltException>(
                () => Evaluate("<r/>", "xs:integer('5')", XsltVersion.V10));
        }

        [TestMethod]
        public void AnUnknownSchemaTypeIsReportedAsSuch()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Text("xs:anyType('a')"));
            StringAssert.Contains(error.Message, "anyType");
        }

        [TestMethod]
        public void NumbersStillCompareAsNumbersInVersionTwo()
        {
            Assert.IsTrue(Boolean("<r/>", "10 > 9", XsltVersion.V20));
            Assert.IsFalse(Boolean("<r/>", "10 < 9", XsltVersion.V20));
        }

        [TestMethod]
        public void AStringComparedToANumberIsRefusedInVersionTwo()
        {
            // Under 1.0 the number wins and this is arithmetic, so '10' > 9 is true. XPath 2.0 has a table of
            // which pairs of types a comparison is defined for, and a string against a number is not among
            // them — the conversion 1.0 performs is exactly what 2.0 declines to guess at.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Boolean("<r/>", "'10' > 9", XsltVersion.V20));

            Assert.AreEqual("XPTY0004", error.Code);
            Assert.IsTrue(Boolean("<r/>", "'10' > 9", XsltVersion.V10));
        }

        [TestMethod]
        public void AnUntypedValueStillFollowsTheOtherOperandInVersionTwo()
        {
            // The exception that keeps stylesheets working. Document content is untyped, and a general
            // comparison reads an untyped operand as whatever the other side is — so this is the number
            // comparison it looks like, and 2.0 changes nothing about it.
            Assert.IsTrue(Boolean("<r n='10'/>", "/r/@n > 9", XsltVersion.V20));
            Assert.IsFalse(Boolean("<r n='8'/>", "/r/@n > 9", XsltVersion.V20));
        }

        [TestMethod]
        public void IdAndIdrefAreCallableFromABareExpression()
        {
            // They are XPath's own functions and had been built by the stylesheet compiler alone, so an
            // expression evaluated outside a stylesheet could not call one at all: id((), ()) answered
            // XPST0017, "no such function", where the specification asks for a type error. Nothing about
            // them needs a stylesheet -- only a node to start from and the IDs of the document it is in.
            Assert.AreEqual(
                "b",
                Evaluate("<r><a xml:id='k'>b</a></r>", "id('k')", XsltVersion.V20).ToCanonicalString());

            // element-with-id is 3.0's spelling of the same lookup.
            Assert.AreEqual(
                "b",
                Evaluate("<r><a xml:id='k'>b</a></r>", "element-with-id('k')", XsltVersion.V30)
                    .ToCanonicalString());

            // The second argument names the document to search and is declared node(), which is one node: a
            // string is the wrong type, and so is the empty sequence, which had been read as a search of
            // nothing that finds nothing.
            Assert.AreEqual("XPTY0004", CodeOf("id('k', 'A')"));
            Assert.AreEqual("XPTY0004", CodeOf("id((), ())"));
            Assert.AreEqual("XPTY0004", CodeOf("idref((), ())"));

            // The one-argument form reads the context item, which has to be a node -- one code where it is
            // an atomic value, and another where there is no focus at all to read.
            Assert.AreEqual("XPTY0004", CodeOf("(1 to 5)[id('k')]"));
            Assert.AreEqual("XPTY0004", CodeOf("(1 to 5)[idref('k')]"));
            Assert.AreEqual("XPDY0002", CodeWithoutAFocus("idref('k', .)"));
        }

        [TestMethod]
        public void SubsequenceRoundsItsPositionsTheWayFnRoundDoes()
        {
            // The specification defines this function in the same words as fn:substring, so the two have to
            // round alike: halves towards positive infinity, not to even as Math.Round sends them. A length
            // of 2.5 takes three items exactly as it takes three characters.
            Assert.AreEqual("2 3 4", Text("subsequence((1, 2, 3, 4, 5), 2, 2.5)"));
            Assert.AreEqual("bcd", Text("substring('abcde', 2, 2.5)"));

            // And a starting position of 2.5 begins at the third, both ways round.
            Assert.AreEqual("3 4", Text("subsequence((1, 2, 3, 4), 2.5)"));
            Assert.AreEqual("cd", Text("substring('abcd', 2.5)"));
        }

        [TestMethod]
        public void ARangeBeyondWhatThisEngineWillBuildRefusesWithACodeOfItsOwn()
        {
            // A limit this engine sets rather than one the language does, which is what XPDY0130 is for: a
            // range is built here as an array of items, so three billion of them is this processor
            // declining rather than the expression being wrong. Saying so with the code is what lets a
            // stylesheet tell the two apart -- and what lets the suite accept either answer.
            Assert.AreEqual("XPDY0130", CodeOf("count(1 to 3000000000)"));

            // A range inside the limit is built as it always was.
            Assert.AreEqual("1000000", Text("count(1 to 1000000)"));
        }
    }
}
