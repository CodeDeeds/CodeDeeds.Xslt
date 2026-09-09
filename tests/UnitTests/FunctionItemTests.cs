namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for function items, which XPath 3.0 adds, and for the library that only exists because of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The feature is one sentence — a function can be a value — and everything here follows from it: a
    /// function can be written where it is used, named without being called, held in a variable, and handed
    /// to <c>filter</c> or <c>sort</c> or a fold.
    /// </para>
    /// <para>
    /// What is easiest to get wrong is the <em>closure</em>: an inline function has to remember the variables
    /// in scope where it was written, not where it is called, and both stores it reads from are reused by the
    /// expressions that own them. Several tests below exist only to hold that down.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class FunctionItemTests
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

        private static string Refuses(string expression, string input = "<r/>")
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression, input));
            return error.Code ?? string.Empty;
        }

        /// <summary>Evaluates through a 2.0 stylesheet, where none of this syntax exists.</summary>
        private static string RefusedByTwoPointZero(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));

            return error.Code ?? string.Empty;
        }

        [TestMethod]
        public void NoneOfThisSyntaxExistsInATwoPointZeroStylesheet()
        {
            // Reading '$f(1)' as a call would take an error away from a 2.0 stylesheet that had written a
            // variable and a parenthesis by accident, and there is nothing else it could have meant.
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("concat#2"));
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("'a' =&gt; upper-case()"));
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("1 instance of function(*)"));

            // 'function' is not a name XPath 2.0 reserves, so the inline form is refused there as a call to a
            // function nobody declared rather than as syntax that does not exist. Either way it is refused,
            // and pointing at the name is the more useful of the two complaints.
            Assert.AreEqual("XPST0017", RefusedByTwoPointZero("function() { 1 }"));
        }

        [TestMethod]
        public void AnInlineFunctionIsCalledWhereItIsWritten()
        {
            Assert.AreEqual("42", Writes("(function($x) { $x * 2 })(21)"));
            Assert.AreEqual("ab", Writes("(function($a, $b) { concat($a, $b) })('a', 'b')"));
            Assert.AreEqual("7", Writes("(function() { 7 })()"));
        }

        [TestMethod]
        public void AnInlineFunctionDeclaresTypesAndTheyAreChecked()
        {
            Assert.AreEqual("2", Writes("(function($x as xs:integer) as xs:integer { $x + 1 })(1)"));

            // The declared type converts an untyped value and refuses one that cannot be read as it, which is
            // the same rule an xsl:function parameter follows.
            Assert.AreEqual("XPTY0004", Refuses("(function($x as xs:integer) { $x })('nonsense')"));
            Assert.AreEqual("XPTY0004", Refuses("(function() as xs:integer { 'text' })()"));
        }

        [TestMethod]
        public void AnInlineFunctionRemembersWhereItWasWrittenAndNotWhereItIsCalled()
        {
            // The three closures are made inside the loop and called after it has finished. Holding the
            // range-variable array rather than a copy of it would answer 3,3,3 — the loop puts back what was
            // in the slot when it ends, so the last value seen would be what all three read.
            Assert.AreEqual(
                "1,2,3",
                Writes("for-each(for $x in 1 to 3 return function() { $x }, function($f) { $f() })"));

            // Nested closures: the inner one reaches past its own parameter to the outer function's.
            Assert.AreEqual("15", Writes("(function($a) { (function($b) { $a + $b })(5) })(10)"));
        }

        [TestMethod]
        public void AnInlineFunctionBodyHasNoContextItem()
        {
            // The specification makes the focus absent inside an inline function, so '.' there is an error
            // rather than quietly meaning whatever the caller happened to be positioned on. A function that
            // wants a node is given one.
            Assert.AreEqual("XPDY0002", Refuses("(function() { . })()"));
            Assert.AreEqual("r", Writes("(function($n) { name($n) })(/r)", "<r/>"));
        }

        [TestMethod]
        public void AFunctionIsNamedWithoutBeingCalled()
        {
            Assert.AreEqual("abc", Writes("concat#3('a', 'b', 'c')"));
            Assert.AreEqual("ABC", Writes("upper-case#1('abc')"));
            Assert.AreEqual("3", Writes("count#1((1, 2, 3))"));

            // A type constructor is a function of the same kind, and a reference to one works the same way.
            Assert.AreEqual("2026-08-25", Writes("xs:date#1('2026-08-25')"));
        }

        [TestMethod]
        public void AFunctionItemReportsItsNameAndArity()
        {
            Assert.AreEqual("3", Writes("function-arity(concat#3)"));
            Assert.AreEqual("2", Writes("function-arity(function($a, $b) { 1 })"));
            Assert.AreEqual("1", Writes("function-arity(map { 'a': 1 })"));

            Assert.AreEqual("upper-case", Writes("local-name-from-QName(function-name(upper-case#1))"));
            Assert.AreEqual(
                "http://www.w3.org/2005/xpath-functions",
                Writes("namespace-uri-from-QName(function-name(upper-case#1))"));

            // An inline function has no name, which is the empty sequence rather than an empty string.
            Assert.AreEqual("0", Writes("count(function-name(function($x) { $x }))"));
        }

        [TestMethod]
        public void AMapAndAnArrayAreFunctionsToo()
        {
            // XPath 3.1 does not merely allow this, it defines a map as the function from key to value.
            Assert.AreEqual("1", Writes("map { 'a': 1 }('a')"));
            Assert.AreEqual("20", Writes("[10, 20, 30](2)"));

            // Which is what lets one be handed to a higher-order function with nothing written around it.
            Assert.AreEqual("1,2", Writes("for-each(('a', 'b'), map { 'a': 1, 'b': 2 })"));

            Assert.AreEqual("XPTY0004", Refuses("map { 'a': 1 }('a', 'b')"));
        }

        [TestMethod]
        public void TheArrowPassesWhatIsOnItsLeftAsTheFirstArgument()
        {
            Assert.AreEqual("ABC", Writes("'abc' => upper-case()"));
            Assert.AreEqual("A B", Writes("'  a   b  ' => normalize-space() => upper-case()"));
            Assert.AreEqual("a-b", Writes("'a' => concat('-', 'b')"));

            // The right-hand side may be worked out at run time as well as named.
            Assert.AreEqual("42", Writes("21 => (function($x) { $x * 2 })()"));

            // It binds tighter than 'cast as' and looser than a leading sign, which is where the grammar puts
            // it: this is abs(-3) and not -abs(3).
            Assert.AreEqual("3", Writes("-3 => abs()"));
        }

        [TestMethod]
        public void FunctionsAreSelectedByTypeAsAnyItemIs()
        {
            Assert.AreEqual("true", Writes("function($x) { $x } instance of function(*)"));
            Assert.AreEqual("true", Writes("map { 'a': 1 } instance of function(*)"));
            Assert.AreEqual("true", Writes("[1] instance of function(*)"));
            Assert.AreEqual("false", Writes("1 instance of function(*)"));
            Assert.AreEqual("false", Writes("/r instance of function(*)"));

            // A written-out signature is read for its arity, which is as much of it as anything here records.
            Assert.AreEqual("true", Writes("upper-case#1 instance of function(xs:string) as xs:string"));
            Assert.AreEqual("false", Writes("concat#3 instance of function(item()) as item()"));

            // And a function is an item but not an atomic value, so a type asking for one refuses it.
            Assert.AreEqual("true", Writes("function($x) { $x } instance of item()"));
            Assert.AreEqual("false", Writes("function($x) { $x } instance of xs:anyAtomicType"));
            Assert.AreEqual("false", Writes("function($x) { $x } instance of node()"));
        }

        [TestMethod]
        public void AFunctionItemHasNoStringValue()
        {
            // The same rule as a map, and for the same reason: there is text that would render one, but none
            // that means the same thing. The two codes divide by the question asked rather than by what was
            // asked about — fn:data of a function is FOTY0013 and fn:string of the same function is
            // FOTY0014 — and writing one into a result asks the first of them, xsl:value-of atomizing what
            // it is given rather than asking it for a string-value.
            Assert.AreEqual("FOTY0014", Refuses("string(function($x) { $x })"));
            Assert.AreEqual("FOTY0013", Refuses("data(function($x) { $x })"));
            Assert.AreEqual("FOTY0013", Refuses("function($x) { $x }"));
        }

        [TestMethod]
        public void NoFunctionItemIsEitherTrueOrFalse()
        {
            // An empty map is no more false than a full one: what a condition would be asking is not a
            // question any of the three answers, so asking is an error rather than a convention.
            Assert.AreEqual("FORG0006", Refuses("boolean(map { })"));
            Assert.AreEqual("FORG0006", Refuses("boolean([1, 2])"));
            Assert.AreEqual("FORG0006", Refuses("boolean(function($x) { $x })"));
            Assert.AreEqual("FORG0006", Refuses("if ([1]) then 'y' else 'n'"));

            // And there is no number to read either, which is a different complaint: NaN would say "read as
            // a number and was not one", and this is something that cannot be read at all.
            Assert.AreEqual("FOTY0013", Refuses("number(map { 'a': 1 })"));
        }

        [TestMethod]
        public void DeepEqualGoesInsideArraysAndMapsAndRefusesFunctions()
        {
            Assert.AreEqual("true", Writes("deep-equal([1, 2], [1, 2])"));
            Assert.AreEqual("false", Writes("deep-equal([1, 2], [1, 3])"));
            Assert.AreEqual("false", Writes("deep-equal([1, 2], [1])"));
            Assert.AreEqual("true", Writes("deep-equal([[1], [2]], [[1], [2]])"));

            // An array is not a sequence holding the same values, and that is the point of it being an item.
            Assert.AreEqual("false", Writes("deep-equal([1, 2], (1, 2))"));
            Assert.AreEqual("false", Writes("deep-equal([1], map { 1: 1 })"));

            Assert.AreEqual("true", Writes("deep-equal(map { 'a': 1 }, map { 'a': 1 })"));
            Assert.AreEqual("true", Writes("deep-equal(map { 'a': 1, 'b': 2 }, map { 'b': 2, 'a': 1 })"));
            Assert.AreEqual("false", Writes("deep-equal(map { 'a': 1 }, map { 'a': 2 })"));
            Assert.AreEqual("false", Writes("deep-equal(map { 'a': 1 }, map { 'z': 1 })"));

            // Two functions agreeing on every input is not a question that can be asked of the values.
            Assert.AreEqual("FOTY0015", Refuses("deep-equal(upper-case#1, upper-case#1)"));
        }

        [TestMethod]
        public void ForEachAndFilterApplyAFunctionToASequence()
        {
            Assert.AreEqual("2,4,6", Writes("for-each(1 to 3, function($x) { $x * 2 })"));
            Assert.AreEqual("2,4", Writes("filter(1 to 5, function($x) { $x mod 2 = 0 })"));

            // A function may answer with any number of items, which is what separates for-each from a map
            // over items: the results are concatenated, not paired up.
            Assert.AreEqual("1,1,2,2", Writes("for-each(1 to 2, function($x) { ($x, $x) })"));
            Assert.AreEqual(string.Empty, Writes("for-each((), function($x) { $x })"));
        }

        [TestMethod]
        public void TheFoldsReduceASequenceToOneValue()
        {
            Assert.AreEqual("15", Writes("fold-left(1 to 5, 0, function($total, $x) { $total + $x })"));

            // The two differ in the order they build the answer, which shows on an operation that is not
            // associative: leftwards is ((((0-1)-2)-3)) and rightwards is (1-(2-(3-0))).
            Assert.AreEqual("-6", Writes("fold-left(1 to 3, 0, function($t, $x) { $t - $x })"));
            Assert.AreEqual("2", Writes("fold-right(1 to 3, 0, function($x, $t) { $x - $t })"));

            Assert.AreEqual("abc", Writes("fold-left(('a','b','c'), '', function($t, $x) { concat($t, $x) })"));
            Assert.AreEqual("0", Writes("fold-left((), 0, function($t, $x) { $t + 1 })"));
        }

        [TestMethod]
        public void ForEachPairStopsAtTheShorterSequence()
        {
            Assert.AreEqual("11,22,33", Writes("for-each-pair(1 to 3, (10, 20, 30), function($a, $b) { $a + $b })"));
            Assert.AreEqual("11,22", Writes("for-each-pair(1 to 5, (10, 20), function($a, $b) { $a + $b })"));

            // Zipping a sequence with its own tail is how neighbours are compared, which is the reason the
            // shorter one wins rather than it being an error.
            Assert.AreEqual(
                "1,1,1",
                Writes("for-each-pair((1,2,3,4), (2,3,4), function($a, $b) { $b - $a })"));
        }

        [TestMethod]
        public void SortOrdersByAKeyAndKeepsEqualItemsWhereTheyWere()
        {
            Assert.AreEqual("1,2,3", Writes("sort((3, 1, 2))"));
            Assert.AreEqual("a,b,c", Writes("sort(('c', 'a', 'b'))"));
            Assert.AreEqual("3,2,1", Writes("sort((1, 2, 3), (), function($x) { -$x })"));

            // Stability is what makes sorting twice a way to sort by two things. Every key here is 1, so the
            // answer is the input unchanged.
            Assert.AreEqual("c,a,b", Writes("sort(('c', 'a', 'b'), (), function($x) { 1 })"));

            // The default key atomizes, so nodes sort by their text.
            Assert.AreEqual("a,b,c", Writes("sort(/r/n)", "<r><n>c</n><n>a</n><n>b</n></r>"));
            Assert.AreEqual("FOCH0002", Refuses("sort((1, 2), 'http://example.com/collation')"));
        }

        [TestMethod]
        public void SortTakesAnArrayApartToKeyIt()
        {
            // The default key is fn:data, and 3.1 has that flatten an array — so an array's key is a whole
            // sequence, compared item by item with the shorter of two coming first where they agree so far.
            // The key of [1, 2] is (1, 2) and the key of 1 is (1), so the 1 sorts first.
            Assert.AreEqual("1,A", Writes(Marked("sort((1, [1, 2]))")));
            Assert.AreEqual("1,A", Writes(Marked("sort(([1, 2], 1))")));

            // An empty key is a prefix of every key, so it comes before all of them.
            Assert.AreEqual("A,1", Writes(Marked("sort(([()], 1))")));
            Assert.AreEqual("A,1", Writes(Marked("sort((1, [()]))")));

            // Two values that cannot be compared are a type error, and that error is what the caller hears
            // rather than a report that a sort failed.
            Assert.AreEqual("XPTY0004", Refuses("sort((1, 'a'))"));
        }

        /// <summary>
        /// Wraps a sequence so that an array in it reads as <c>A</c>, an array having no string-value.
        /// </summary>
        /// <param name="expression">The expression whose result is to be written.</param>
        private static string Marked(string expression) =>
            $"for $x in {expression} return (if ($x instance of array(*)) then 'A' else $x)";

        [TestMethod]
        public void TraceLeavesTheLabelOffWhenThereIsNone()
        {
            // 3.1 made the label optional. The value passes through either way, which is the whole point of
            // the function: it can be dropped into an expression without changing what the expression means.
            Assert.AreEqual("3", Writes("trace(1 + 2)"));
            Assert.AreEqual("3", Writes("trace(1 + 2, 'sum')"));
        }

        [TestMethod]
        public void ApplyCallsAFunctionWithArgumentsHeldInAnArray()
        {
            Assert.AreEqual("abc", Writes("apply(concat#3, ['a', 'b', 'c'])"));
            Assert.AreEqual("42", Writes("apply(function($x) { $x * 2 }, [21])"));

            // The array has to fit the function, and a mismatch is its own error rather than a general one.
            Assert.AreEqual("FOAP0001", Refuses("apply(concat#3, ['a'])"));
        }

        [TestMethod]
        public void MapForEachAndFindSearchAMap()
        {
            // The entries come back in no particular order, a map being unordered, so the test sorts them.
            Assert.AreEqual(
                "a=1,b=2",
                Writes("sort(map:for-each(map { 'a': 1, 'b': 2 }, function($k, $v) { concat($k, '=', $v) }))"));

            Assert.AreEqual("2", Writes("array:size(map:find(map { 'x': 1, 'y': map { 'x': 2 } }, 'x'))"));
            Assert.AreEqual("1", Writes("map:find(map { 'x': 1 }, 'x')?1"));
            Assert.AreEqual("0", Writes("array:size(map:find(map { 'x': 1 }, 'z'))"));
        }

        private const string Nested = "map { 'x': 1, 'y': map { 'x': 2 }, 'z': [ map { 'x': 3 } ] }";

        [TestMethod]
        public void MapFindLooksInsideArraysAsWellAsMaps()
        {
            // The recursion is the point of it: on parsed JSON the map holding what you want is several
            // levels down, and writing the path to it is what map:find() saves.
            Assert.AreEqual("1,2,3", Writes($"sort(map:find({Nested}, 'x')?*)"));
        }

        [TestMethod]
        public void TheArrayLibraryTakesFunctionsToo()
        {
            Assert.AreEqual("2,4,6", Writes("array:for-each([1, 2, 3], function($x) { $x * 2 })?*"));
            Assert.AreEqual("2,4", Writes("array:filter([1, 2, 3, 4], function($x) { $x mod 2 = 0 })?*"));
            Assert.AreEqual("6", Writes("array:fold-left([1, 2, 3], 0, function($t, $x) { $t + $x })"));
            Assert.AreEqual("2", Writes("array:fold-right([1, 2, 3], 0, function($x, $t) { $x - $t })"));
            Assert.AreEqual("1,2,3", Writes("array:sort([3, 1, 2])?*"));
            Assert.AreEqual("3,2,1", Writes("array:sort([1, 2, 3], (), function($x) { -$x })?*"));
            Assert.AreEqual(
                "11,22",
                Writes("array:for-each-pair([1, 2, 3], [10, 20], function($a, $b) { $a + $b })?*"));
        }

        [TestMethod]
        public void AFunctionCanBeHeldInAVariableAndPassedOn()
        {
            string stylesheet =
                $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + "<xsl:template match=\"/\">"
                + "<xsl:variable name=\"double\" select=\"function($x) { $x * 2 }\"/>"
                + "<out><xsl:value-of select=\"for-each(1 to 3, $double)\" separator=\",\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>2,4,6</out>",
                new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }

        [TestMethod]
        public void AStylesheetFunctionCanBeNamedAsAValue()
        {
            // A reference to an xsl:function is built the same way as one to a built-in — the call the name
            // stands for, compiled once with a variable in each argument position.
            string stylesheet =
                $"<xsl:stylesheet version=\"3.0\" {XslOnly} xmlns:my=\"urn:my\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"my xs\">"
                + "<xsl:function name=\"my:double\" as=\"xs:integer\">"
                + "<xsl:param name=\"x\" as=\"xs:integer\"/>"
                + "<xsl:sequence select=\"$x * 2\"/>"
                + "</xsl:function>"
                + "<xsl:template match=\"/\">"
                + "<out><xsl:value-of select=\"for-each(1 to 3, my:double#1)\" separator=\",\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>2,4,6</out>",
                new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true }).TransformXml("<r/>"));
        }

        [TestMethod]
        public void ACallChecksHowManyArgumentsTheFunctionTakes()
        {
            Assert.AreEqual("XPTY0004", Refuses("(function($x) { $x })(1, 2)"));
            Assert.AreEqual("XPTY0004", Refuses("(function($x) { $x })()"));

            // Something that is not a function at all cannot be called.
            Assert.AreEqual("XPTY0004", Refuses("(1)(2)"));
        }

        [TestMethod]
        public void RecursionThroughAFoldTerminates()
        {
            // A closure recurses by being handed to something that calls it, since it cannot name itself.
            Assert.AreEqual(
                "120",
                Writes("fold-left(1 to 5, 1, function($total, $x) { $total * $x })"));
        }

        // ---- Leaving an argument open ---------------------------------------------------------------------

        [TestMethod]
        public void APlaceholderTurnsACallIntoAFunction()
        {
            // XPath 3.1's partial application. A '?' says which argument is not being supplied, and the
            // result is a function waiting for it.
            Assert.AreEqual("ab", Writes("substring-before(?, '-')('ab-cd')"));
            Assert.AreEqual("1", Writes("function-arity(substring-before(?, '-'))"));

            // Every place open is the function itself, and none is an ordinary call.
            Assert.AreEqual("ab", Writes("substring-before(?, ?)('ab-cd', '-')"));
            Assert.AreEqual("ab", Writes("substring-before('ab-cd', '-')"));
        }

        [TestMethod]
        public void TheSuppliedArgumentsAreReadWhereTheQuestionMarkIs()
        {
            // Which is the point of it rather than a detail: the fixed arguments are values, evaluated once
            // at the partial application, so the function carries them rather than re-reading them. Here the
            // separator is taken while $sep is 'X', and stays 'X' for the call made afterwards.
            Assert.AreEqual(
                "aXb",
                Writes("let $sep := 'X', $f := string-join(?, $sep) return $f(('a', 'b'))"));
        }

        [TestMethod]
        public void APlaceholderWorksOnAFunctionItemToo()
        {
            // Named call and dynamic call reduce to the same thing — something that yields a function item,
            // and a list of argument slots some of which are open.
            Assert.AreEqual("ef", Writes("substring-before#2(?, '-')('ef-gh')"));
            Assert.AreEqual("L*R", Writes("concat#3(?, '*', ?)('L', 'R')"));
            Assert.AreEqual("YZ", Writes("function($a, $b) { $a || $b }(?, 'Z')('Y')"));

            // And on one held in a variable.
            Assert.AreEqual("ij", Writes("let $f := substring-before#2 return $f(?, '-')('ij-kl')"));
        }

        [TestMethod]
        public void APlaceholderIsNotAnExpressionInATwoPointZeroStylesheet()
        {
            // '?' is only an occurrence indicator there, so reading it as a placeholder would accept a
            // stylesheet a conformant 2.0 processor refuses.
            Assert.AreEqual("XPST0003", RefusedByTwoPointZero("substring-before(?, '-')"));
        }

        [TestMethod]
        public void AReferenceToAFunctionThatReadsTheFocusTakesTheFocusWithIt()
        {
            // name#0 made on a and called on b names a: the reference carries the focus of the place it was
            // written, as the specification says, rather than reading whatever is in focus when it is
            // called. Which is what lets a function that reads the context item be handed to another.
            Assert.AreEqual("a", Writes("let $f := (/r/a)/name#0 return (/r/b)/$f()", "<r><a/><b/></r>"));
            Assert.AreEqual(
                "x",
                Writes("let $f := (/r/a)/string#0 return (/r/b)/$f()", "<r><a>x</a><b>y</b></r>"));
        }

        [TestMethod]
        public void AParenthesizedItemTypeTakesAnOccurrenceOfItsOwn()
        {
            // Which is what lets a function type take one: without the parentheses the '?' after
            // 'function(*) as xs:integer' would belong to the result type.
            Assert.AreEqual(
                "true",
                Writes("(function() as xs:integer { 1 }) instance of (function() as xs:integer)?"));

            // A type the function did not declare is item()*, which is not within xs:integer — so the same
            // function written without its result type is not an instance of a signature that names one.
            Assert.AreEqual(
                "false", Writes("(function() { 1 }) instance of (function() as xs:integer)?"));
            Assert.AreEqual("true", Writes("() instance of (function(*))?"));
            Assert.AreEqual("false", Writes("(1, 2) instance of (xs:integer)"));
            Assert.AreEqual("true", Writes("(1, 2) instance of (xs:integer)+"));
        }

        [TestMethod]
        public void AFunctionMayBeCalledByANameThatCarriesItsNamespace()
        {
            // Q{urn:f}twice(2) reaches the same xsl:function as f:twice(2), with no prefix bound anywhere
            // near the call — the suite's xml-to-json-A2-019 calls its stylesheet implementation that way.
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl} xmlns:f=\"urn:f\">"
                + "<xsl:function name=\"f:twice\" as=\"xs:integer\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                + "<xsl:param name=\"n\"/><xsl:sequence select=\"$n * 2\"/></xsl:function>"
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"Q{urn:f}twice(2)\"/></out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out xmlns:f=\"urn:f\">4</out>",
                new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 }).TransformXml("<r/>"));
        }

        [TestMethod]
        public void AvailableSystemPropertiesNamesEveryPropertyTheProcessorAnswers()
        {
            // Each is a QName in the XSLT namespace, and system-property() answers for every one of them —
            // the two it answers with an empty string included, which is the specified answer for a property
            // it does not provide a value for. The function is reached by its namespace as well as bare.
            const string Every =
                "every $p in available-system-properties() satisfies ("
                + "namespace-uri-from-QName($p) = 'http://www.w3.org/1999/XSL/Transform' and "
                + "(system-property('xsl:' || local-name-from-QName($p)) != '' "
                + "or local-name-from-QName($p) = ('product-version', 'vendor-url')))";

            // A 3.0 function, so the processor has to claim 3.0 for it to exist.
            string Writes30(string expression) => new Xslt(
                $"<xsl:stylesheet version=\"3.0\" {Xsl}><xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\"/></out></xsl:template></xsl:stylesheet>",
                new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                .TransformXml("<r/>")["<out>".Length..^"</out>".Length];

            Assert.AreEqual("true", Writes30(Every));
            Assert.AreEqual(
                "1",
                Writes30("count(Q{http://www.w3.org/2005/xpath-functions}available-system-properties()"
                    + "[. = QName('http://www.w3.org/1999/XSL/Transform', 'version')])"));
            Assert.AreEqual(
                "true", Writes30("Q{http://www.w3.org/2005/xpath-functions}current#0 instance of function(*)"));
        }
    }
}
