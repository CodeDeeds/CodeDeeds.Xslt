namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the type errors XPath 2.0 requires: expressions a 1.0 processor answers and a 2.0 processor
    /// refuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the least visible part of the language. Nothing here produces a wrong answer that a reader
    /// would notice — <c>"1" = 1</c> comes out true, <c>1 + "2"</c> comes out 3, and a stylesheet built on
    /// either keeps working until the day the data changes shape. XPath 2.0's position is that a comparison
    /// between a string and a number was a mistake at the moment it was written, and that saying so is worth
    /// more than the answer.
    /// </para>
    /// <para>
    /// Every case here is drawn from the W3C QT3 suite, which is the authority for what a conformant
    /// processor does; the expectations are not this engine's own reading of the specification. Each test
    /// pairs the refusal with the expression that <em>is</em> accepted, so what the rule costs a stylesheet
    /// stays visible next to what it catches.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class TypeErrorTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        /// <summary>
        /// Writes an expression through a 2.0 stylesheet on a processor asked to be 2.0, on both backends.
        /// These are 2.0's rules, and a 3.0 processor relaxes some of them for a 2.0 stylesheet.
        /// </summary>
        private static string Writes(string expression, string input = "<r/>")
        {
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                Version = XsltVersion.V20,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        /// <summary>Asserts that an expression is refused, and returns the code it was refused with.</summary>
        private static string Refuses(string expression, string input = "<r/>")
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression, input));
            Assert.IsNotNull(error.Code, $"'{expression}' was refused without a code: {error.Message}");
            return error.Code!;
        }

        /// <summary>Writes an expression through a 1.0 stylesheet, where the old rules still hold.</summary>
        private static string WritesUnderOnePointZero(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"1.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            return new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>")
                is var result && result == "<out/>" ? string.Empty : result["<out>".Length..^"</out>".Length];
        }

        // ---- Comparison: the operator mapping --------------------------------------------------------------

        [TestMethod]
        [DataRow("'1' = 1")]
        [DataRow("5 = false()")]
        [DataRow("'a' &lt; 1")]
        [DataRow("xs:date('2026-08-24') = 1")]
        [DataRow("xs:date('2026-08-24') = xs:dateTime('2026-08-24T00:00:00')")]
        public void AComparisonBetweenIncompatibleTypesIsRefused(string expression)
        {
            Assert.AreEqual("XPTY0004", Refuses(expression));
        }

        [TestMethod]
        public void TheSameComparisonIsStillAnsweredUnderVersionOne()
        {
            // What the rule costs: 1.0 converts until the operands match, and every one of these was a usable
            // answer. A stylesheet declaring version="1.0" keeps them.
            Assert.AreEqual("true", WritesUnderOnePointZero("'1' = 1"));
            Assert.AreEqual("true", WritesUnderOnePointZero("5 = true()"));
            Assert.AreEqual("false", WritesUnderOnePointZero("'a' &lt; 1"));
        }

        [TestMethod]
        [DataRow("1 = 1.0")]
        [DataRow("1 = xs:double('1')")]
        [DataRow("'a' &lt; 'b'")]
        [DataRow("false() = false()")]
        [DataRow("xs:date('2026-08-24') &lt; xs:date('2026-08-25')")]
        [DataRow("xs:dayTimeDuration('PT1H') &lt; xs:dayTimeDuration('PT2H')")]
        public void AComparisonWithinOneFamilyIsStillMade(string expression)
        {
            // The four numeric types are one family, so mixing them is not mixing types.
            Assert.AreEqual("true", Writes(expression));
        }

        [TestMethod]
        public void AnUntypedOperandFollowsTheOtherSide()
        {
            // The exception that keeps stylesheets working: everything drawn from a document is untyped, and a
            // general comparison reads it as whatever it is being compared against.
            Assert.AreEqual("true", Writes("/r/@n > 9", "<r n='10'/>"));
            Assert.AreEqual("true", Writes("/r/@n = 10", "<r n='10'/>"));
            Assert.AreEqual("true", Writes("/r/@s = 'yes'", "<r s='yes'/>"));
        }

        [TestMethod]
        public void AValueComparisonReadsAnUntypedOperandAsAStringInstead()
        {
            // The asymmetry that gives 'eq' its point. '=' guesses from the other side; 'eq' does not, so a
            // stylesheet that meant a number has to say so.
            Assert.AreEqual("XPTY0004", Refuses("/r/@n eq 10", "<r n='10'/>"));
            Assert.AreEqual("true", Writes("xs:integer(/r/@n) eq 10", "<r n='10'/>"));
            Assert.AreEqual("true", Writes("/r/@n eq '10'", "<r n='10'/>"));
        }

        [TestMethod]
        [DataRow("xs:QName('a') &lt; xs:QName('b')")]
        [DataRow("xs:gYear('1999') &lt; xs:gYear('2000')")]
        [DataRow("xs:yearMonthDuration('P1Y') &lt; xs:dayTimeDuration('P1D')")]
        public void ValuesWithNoOrderCanStillOnlyBeComparedForEquality(string expression)
        {
            Assert.AreEqual("XPTY0004", Refuses(expression));
        }

        [TestMethod]
        public void BinaryValuesOrderByTheirOctets()
        {
            // XPath 3.1 gives the binary types an order that 2.0 left undefined, and this engine applies it
            // at every version rather than only at 3.0 — recorded as a deliberate difference, since a 2.0
            // expression comparing two of them gets an answer where the specification says a type error.
            Assert.AreEqual("false", Writes("xs:hexBinary('FF') &lt; xs:hexBinary('EF')"));
            Assert.AreEqual("true", Writes("xs:hexBinary('EF') &lt; xs:hexBinary('FF')"));

            // Octet by octet, and a value that is a prefix of another comes first.
            Assert.AreEqual("true", Writes("xs:hexBinary('AB') lt xs:hexBinary('ABCD')"));
            Assert.AreEqual("true", Writes("xs:base64Binary('AAA=') lt xs:base64Binary('AQA=')"));

            // Unsigned, so the top bit does not make a byte negative.
            Assert.AreEqual("true", Writes("xs:hexBinary('7F') lt xs:hexBinary('80')"));
            Assert.AreEqual("false", Writes("xs:hexBinary('FF') gt xs:hexBinary('FF')"));
        }

        [TestMethod]
        public void EqualityHoldsWhereOrderingDoesNot()
        {
            Assert.AreEqual("true", Writes("xs:QName('a') eq xs:QName('a')"));
            Assert.AreEqual("false", Writes("xs:gYear('1999') eq xs:gYear('2000')"));
            Assert.AreEqual("true", Writes("xs:yearMonthDuration('P1Y') ne xs:dayTimeDuration('P1D')"));
        }

        [TestMethod]
        public void NaNStandsInNoRelationToAnything()
        {
            // Including to itself, and including under the operators that a sign comparison would otherwise
            // make true by accident.
            Assert.AreEqual("false", Writes("xs:double('NaN') eq xs:double('NaN')"));
            Assert.AreEqual("true", Writes("xs:double('NaN') ne xs:double('NaN')"));
            Assert.AreEqual("false", Writes("xs:double('NaN') ge 1"));
            Assert.AreEqual("false", Writes("xs:double('NaN') gt 1"));
            Assert.AreEqual("false", Writes("xs:double('NaN') le 1"));
        }

        [TestMethod]
        public void TwoLargeIntegersCompareExactly()
        {
            // Eighteen digits is more than a double carries, so comparing through one would call these equal.
            Assert.AreEqual("false", Writes("999999999999999999 eq 999999999999999998"));
            Assert.AreEqual("true", Writes("999999999999999999 gt 999999999999999998"));
        }

        // ---- Casting: the table ----------------------------------------------------------------------------

        [TestMethod]
        [DataRow("xs:gYear('1999') cast as xs:float")]
        [DataRow("xs:gYear('1999') cast as xs:boolean")]
        [DataRow("xs:gYear('1999') cast as xs:gMonth")]
        [DataRow("xs:QName('n') cast as xs:float")]
        [DataRow("xs:date('2026-08-24') cast as xs:integer")]
        [DataRow("xs:date('2026-08-24') cast as xs:time")]
        [DataRow("xs:time('12:00:00') cast as xs:date")]
        [DataRow("1 cast as xs:date")]
        [DataRow("true() cast as xs:hexBinary")]
        [DataRow("xs:dayTimeDuration('PT1H') cast as xs:integer")]
        public void ACastWithNoRouteBetweenTheTypesIsRefused(string expression)
        {
            // Almost everything has a lexical form, so without the table these would all go by way of the
            // text and come back with an answer — a re-reading of how the value was written rather than a
            // conversion of what it is.
            Assert.AreEqual("XPTY0004", Refuses(expression));
        }

        [TestMethod]
        [DataRow("xs:gYear('1999') cast as xs:string", "1999")]
        [DataRow("xs:date('2026-08-24') cast as xs:dateTime", "2026-08-24T00:00:00")]
        [DataRow("xs:dateTime('2026-08-24T12:30:00') cast as xs:date", "2026-08-24")]
        [DataRow("xs:dateTime('2026-08-24T12:30:00') cast as xs:gYear", "2026")]
        [DataRow("'1999' cast as xs:gYear", "1999")]
        [DataRow("1 cast as xs:boolean", "true")]
        [DataRow("xs:hexBinary('FF') cast as xs:base64Binary", "/w==")]
        public void ACastTheTableAllowsIsStillMade(string expression, string expected)
        {
            Assert.AreEqual(expected, Writes(expression));
        }

        [TestMethod]
        public void EverythingStillCastsToAndFromAString()
        {
            // The row and column that make the rest of the table workable: a stylesheet that wants the
            // re-reading can ask for it in as many words.
            Assert.AreEqual("1999", Writes("string(xs:gYear('1999') cast as xs:string)"));
            Assert.AreEqual("1999", Writes("(xs:gYear('1999') cast as xs:string) cast as xs:float"));
        }

        [TestMethod]
        [DataRow("1 castable as xs:date", "false")]
        [DataRow("xs:gYear('1999') castable as xs:float", "false")]
        [DataRow("'1999' castable as xs:gYear", "true")]
        public void CastableAnswersWhereCastRefuses(string expression, string expected)
        {
            // castable as is the question, not the attempt: a cast the table does not have is one that would
            // not work, which is what it was asked.
            Assert.AreEqual(expected, Writes(expression));
        }

        [TestMethod]
        [DataRow("'x' cast as xs:anyAtomicType")]
        [DataRow("'x' cast as xs:anySimpleType")]
        [DataRow("'x' cast as xs:NOTATION")]
        public void ATypeThatNamesAFamilyCannotBeCastTo(string expression)
        {
            Assert.AreEqual("XPST0080", Refuses(expression));
        }

        [TestMethod]
        public void AnUntypedValueIsNotReadAsAName()
        {
            // A prefix means whatever it meant where it was written, and a value drawn from a document does
            // not carry the bindings that would settle it.
            Assert.AreEqual("XPTY0004", Refuses("/r/@q cast as xs:QName", "<r q='n'/>"));
        }

        // ---- Arithmetic ------------------------------------------------------------------------------------

        [TestMethod]
        [DataRow("1 + '2'")]
        [DataRow("1 + 'x'")]
        [DataRow("1 + true()")]
        [DataRow("xs:date('2026-08-24') * 2")]
        [DataRow("1 + xs:QName('n')")]
        public void ArithmeticOnSomethingThatIsNotANumberIsRefused(string expression)
        {
            // 1 + '2' was 3 and 1 + 'x' was NaN. Neither was a number the stylesheet asked for.
            Assert.AreEqual("XPTY0004", Refuses(expression));
        }

        [TestMethod]
        [DataRow("xs:dayTimeDuration('P3D') - xs:yearMonthDuration('P3Y3M')")]
        [DataRow("xs:yearMonthDuration('P3Y3M') + xs:dayTimeDuration('P3D')")]
        [DataRow("xs:duration('P3D') + xs:yearMonthDuration('P3Y3M')")]
        [DataRow("xs:duration('P1Y3M') * 3")]
        [DataRow("xs:duration('P1Y3M') div 3")]
        [DataRow("xs:dayTimeDuration('P3D') div xs:yearMonthDuration('P3Y3M')")]
        [DataRow("xs:time('08:01:23') - xs:yearMonthDuration('P1Y')")]
        [DataRow("xs:date('2026-08-24') - xs:time('08:01:23')")]
        public void DurationArithmeticNeedsTwoOfOneHalf(string expression)
        {
            // A year-month duration and a day-time one have no common scale — how long a month is depends on
            // which month — so there is no sum of the two and no ratio between them. A plain xs:duration may
            // carry both halves, which puts it out of the arithmetic altogether: only its two halves are
            // operands, and that is what they are for.
            Assert.AreEqual("XPTY0004", Refuses(expression));
        }

        [TestMethod]
        public void DurationArithmeticWithinOneHalfStillWorks()
        {
            Assert.AreEqual("P4Y", Writes(
                "xs:yearMonthDuration('P3Y') + xs:yearMonthDuration('P1Y')"));
            Assert.AreEqual("P6D", Writes("xs:dayTimeDuration('P3D') * 2"));
            Assert.AreEqual("3", Writes("xs:dayTimeDuration('P3D') div xs:dayTimeDuration('P1D')"));

            // A date moves by either half, since it has both a year and a day to move within; a time has no
            // date, so only the day-time half means anything to it.
            Assert.AreEqual("2027-08-24", Writes(
                "xs:date('2026-08-24') + xs:yearMonthDuration('P1Y')"));
            Assert.AreEqual("09:01:23", Writes(
                "xs:time('08:01:23') + xs:dayTimeDuration('PT1H')"));
        }

        // ---- The range operator ----------------------------------------------------------------------------

        [TestMethod]
        [DataRow("1.1 to 3")]
        [DataRow("3 to 1.1")]
        [DataRow("'1' to 3")]
        public void ARangeRunsBetweenIntegers(string expression)
        {
            // A range counts, so its ends are counting numbers. Truncating 1.1 to 1 would answer a question
            // that was not asked.
            Assert.AreEqual("XPTY0004", Refuses(expression));
        }

        [TestMethod]
        public void ANegativeBoundIsStillAnInteger()
        {
            // Negation keeps the operand's type, so the minus sign does not turn 3 into a double on the way
            // past. It is the kind of thing that only shows up somewhere an integer is insisted on.
            Assert.AreEqual("-3 -2 -1 0 1", Writes("-3 to 1"));
            Assert.AreEqual("-3", Writes("string(-3 cast as xs:integer)"));
        }

        [TestMethod]
        public void ArithmeticOnDocumentContentIsUnaffected()
        {
            // Every node atomizes to an untyped value, and untyped values become doubles — which is the whole
            // reason the rule above can be as strict as it is.
            Assert.AreEqual("11", Writes("/r/@n + 1", "<r n='10'/>"));
            Assert.AreEqual("20", Writes("/r/@n * 2", "<r n='10'/>"));
        }

        [TestMethod]
        public void ArithmeticOnAnEmptyOperandIsEmptyRatherThanNaN()
        {
            // Nothing to add, so no number to give back. Under 1.0 this was NaN, which is a number and reads
            // as one all the way to the output.
            Assert.AreEqual(string.Empty, Writes("/r/@missing + 1", "<r/>"));
            Assert.AreEqual("NaN", WritesUnderOnePointZero("/r/@missing + 1"));
        }

        [TestMethod]
        public void ArithmeticOnMoreThanOneItemIsRefused()
        {
            Assert.AreEqual("XPTY0004", Refuses("(1, 2) + 1"));
        }

        // ---- Effective boolean value -----------------------------------------------------------------------

        [TestMethod]
        [DataRow("boolean(xs:date('2026-08-24'))")]
        [DataRow("boolean(xs:dayTimeDuration('PT1H'))")]
        [DataRow("boolean(xs:QName('n'))")]
        [DataRow("boolean(('a', 'b'))")]
        [DataRow("if (('a', 'b')) then 1 else 2")]
        public void AValueWithNoEffectiveBooleanValueIsRefused(string expression)
        {
            // All of these came back true, because a date is held as something non-empty and a sequence was
            // read by its length. Neither is an answer to the question a condition asks.
            Assert.AreEqual("FORG0006", Refuses(expression));
        }

        [TestMethod]
        public void TheValuesThatDoHaveOneStillAnswer()
        {
            Assert.AreEqual("false", Writes("boolean(())"));
            Assert.AreEqual("false", Writes("boolean('')"));
            Assert.AreEqual("true", Writes("boolean('x')"));
            Assert.AreEqual("false", Writes("boolean(0)"));
            Assert.AreEqual("false", Writes("boolean(xs:double('NaN'))"));
            Assert.AreEqual("true", Writes("boolean(/r)", "<r/>"));

            // A sequence beginning with a node is true however long it is, which is the node-set rule.
            Assert.AreEqual("true", Writes("boolean(/r/a)", "<r><a/><a/></r>"));
        }

        // ---- Aggregates ------------------------------------------------------------------------------------

        [TestMethod]
        [DataRow("max(('a string', 1))")]
        [DataRow("min((1, 'a string'))")]
        [DataRow("avg((xs:yearMonthDuration('P20Y'), 3))")]
        [DataRow("avg(('a', 'b'))")]
        [DataRow("avg((true(), false()))")]
        [DataRow("max((xs:QName('a'), xs:QName('b')))")]
        public void AnAggregateOverAMixtureIsRefused(string expression)
        {
            // There is no comparison between a string and a number to be the larger under, and no average of
            // a duration and a number.
            Assert.AreEqual("FORG0006", Refuses(expression));
        }

        [TestMethod]
        public void AnAggregateOverOneFamilyStillAnswers()
        {
            Assert.AreEqual("3", Writes("max((1, 3, 2))"));
            Assert.AreEqual("c", Writes("max(('a', 'c', 'b'))"));
            Assert.AreEqual("2", Writes("avg((1, 2, 3))"));
            Assert.AreEqual("P1Y6M", Writes(
                "avg((xs:yearMonthDuration('P1Y'), xs:yearMonthDuration('P2Y')))"));

            // Untyped values become doubles, so an aggregate over document content works unchanged.
            Assert.AreEqual("3", Writes("max(/r/a)", "<r><a>1</a><a>3</a><a>2</a></r>"));
            Assert.AreEqual("2", Writes("avg(/r/a)", "<r><a>1</a><a>3</a><a>2</a></r>"));
        }

        [TestMethod]
        public void NaNIsTheExtremeOfAnySequenceHoldingIt()
        {
            // It orders with nothing, so no other member can be shown to be the largest.
            Assert.AreEqual("NaN", Writes("max((1, xs:double('NaN'), 3))"));
            Assert.AreEqual("NaN", Writes("min((1, xs:double('NaN'), 3))"));
        }

        [TestMethod]
        public void AnAggregateOverNothingIsEmpty()
        {
            Assert.AreEqual(string.Empty, Writes("max(())"));
            Assert.AreEqual(string.Empty, Writes("avg(())"));
        }

        // ---- Function arguments ----------------------------------------------------------------------------

        [TestMethod]
        [DataRow("abs(xs:string('1'))")]
        [DataRow("abs(true())")]
        [DataRow("translate(1, '-', 'x')")]
        [DataRow("translate('abc', 1, 'x')")]
        [DataRow("upper-case(1)")]
        [DataRow("substring('abc', xs:date('2026-08-24'))")]
        [DataRow("remove((1, 2), 1.5)")]
        [DataRow("codepoints-to-string('65')")]
        [DataRow("year-from-date(xs:dateTime('2026-08-24T00:00:00'))")]
        public void AnArgumentOfTheWrongTypeIsRefused(string expression)
        {
            // XPath 2.0 declares a type for every parameter in the core library, and only three conversions
            // reach one: atomize, read an untyped value as the declared type, promote a number to a wider one.
            // A string is not among the things read as a number, so abs('1') has gone wrong somewhere earlier.
            Assert.AreEqual("XPTY0004", Refuses(expression));
        }

        [TestMethod]
        public void TheSameCallsAreStillAnsweredUnderVersionOne()
        {
            // 1.0 declares no types at all: it converts whatever it is given, which is why these were answers.
            Assert.AreEqual("1", WritesUnderOnePointZero("translate(1, '-', 'x')"));
            Assert.AreEqual("bc", WritesUnderOnePointZero("substring('abc', '2')"));
        }

        [TestMethod]
        public void AnUntypedArgumentIsReadAsTheDeclaredType()
        {
            // The conversion that keeps document content usable without a cast at every reference.
            Assert.AreEqual("bc", Writes("substring('abc', /r/@n)", "<r n='2'/>"));
            Assert.AreEqual("2", Writes("abs(/r/@n)", "<r n='-2'/>"));
            Assert.AreEqual("A", Writes("codepoints-to-string(/r/@n)", "<r n='65'/>"));
        }

        [TestMethod]
        public void AnUntypedArgumentThatWillNotConvertSaysSo()
        {
            // The cast is a real cast, and fails as any cast does — with the code for a value outside the
            // type's lexical space, not the one for a value of the wrong type.
            Assert.AreEqual("FORG0001", Refuses("codepoints-to-string(/r/@n)", "<r n='six'/>"));
        }

        [TestMethod]
        public void ANumberIsPromotedToAWiderOne()
        {
            // An integer where a double is declared is converted; that is promotion, and it only ever widens.
            Assert.AreEqual("bc", Writes("substring('abc', 2)"));
            Assert.AreEqual("2", Writes("abs(-2)"));
            Assert.AreEqual("2.5", Writes("abs(-2.5)"));
        }

        [TestMethod]
        public void AnArgumentIsCheckedForHowManyItemsItHolds()
        {
            // fn:string takes item()?, so two nodes is not one item — 1.0's habit of quietly taking the first
            // is what the declarations were written to stop.
            Assert.AreEqual("XPTY0004", Refuses("string(/r/i)", "<r><i>a</i><i>b</i></r>"));
            Assert.AreEqual("a", Writes("string(/r/i)", "<r><i>a</i></r>"));

            // And a parameter that is not optional needs a value.
            Assert.AreEqual("XPTY0004", Refuses("translate('abc', /r/@missing, 'x')", "<r/>"));
        }

        [TestMethod]
        public void AnOptionalArgumentStillTakesTheEmptySequence()
        {
            Assert.AreEqual(string.Empty, Writes("abs(())"));
            Assert.AreEqual(string.Empty, Writes("upper-case(())"));
        }

        [TestMethod]
        public void AStringFunctionTakesACollationInVersionTwo()
        {
            // Four of them gained a third argument in 2.0. A 1.0 stylesheet passing one is still making a
            // mistake, and the collation is checked against the one collation this engine has.
            const string Codepoint = "http://www.w3.org/2005/xpath-functions/collation/codepoint";

            Assert.AreEqual("true", Writes($"contains('foo', 'o', '{Codepoint}')"));
            Assert.AreEqual("FOCH0002", Refuses("contains('foo', 'o', 'urn:danish')"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => WritesUnderOnePointZero($"contains('foo', 'o', '{Codepoint}')"));

            Assert.AreEqual("XPST0017", error.Code);
        }
    }
}
