namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what a function item's type is: the subtype judgement that <c>instance of function(…)</c>
    /// asks, the coercion of an item to the type it was handed to, and the two questions a function item
    /// answers about itself — its name and its arity.
    /// </summary>
    /// <remarks>
    /// The direction of the judgement is the part worth holding down. A function type is contravariant in
    /// its arguments and covariant in its result: one function stands in for another where it accepts
    /// everything that one accepts and returns no more than that one promises. Written out, that means a
    /// <em>narrower</em> required argument type answers true and a wider one answers false, which reads
    /// backwards until you ask what would happen if it were the other way round.
    /// </remarks>
    [TestClass]
    public sealed class FunctionTypeTests
    {
        private const string Xsl =
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\""
            + " exclude-result-prefixes=\"xs f\"";

        /// <summary>A stylesheet declaring a function of known types, whose body writes an expression.</summary>
        private static string Sheet(string declarations, string expression)
        {
            return $"<xsl:stylesheet version=\"3.0\" {Xsl}>" + declarations
                + "<xsl:template name=\"xsl:initial-template\"><out>"
                + $"<xsl:value-of select=\"{expression}\" separator=\",\"/>"
                + "</out></xsl:template></xsl:stylesheet>";
        }

        private const string TwoArguments =
            "<xsl:function name=\"f:f\" as=\"element(e)\">"
            + "<xsl:param name=\"x\" as=\"xs:long\"/><xsl:param name=\"y\" as=\"xs:NCName\"/>"
            + "<e/></xsl:function>";

        private static string Run(string declarations, string expression)
        {
            string written = new Xslt(
                Sheet(declarations, expression),
                new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 }).Transform();

            return written == "<out/>" ? string.Empty : written["<out>".Length..^"</out>".Length];
        }

        private static string Refuses(string declarations, string expression)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(declarations, expression)).Code
                ?? string.Empty;
        }

        [TestMethod]
        public void OneFunctionTypeIsWithinAnotherByWhatItTakesAndGivesBack()
        {
            // The item's own type is function(xs:long, xs:NCName) as element(e), and every answer below
            // follows from comparing that against what was written.
            Assert.AreEqual(
                "true,true",
                Run(
                    TwoArguments,
                    "f:f#2 instance of function(*),"
                    + " f:f#2 instance of function(xs:long, xs:NCName) as element(e)"));

            // Contravariant in the arguments: a function taking xs:long stands in wherever one taking
            // xs:int is asked for, since every xs:int is an xs:long — and not the other way about, which is
            // why the wider item()* answers false.
            Assert.AreEqual(
                "true,false,false",
                Run(
                    TwoArguments,
                    "f:f#2 instance of function(xs:int, xs:NCName) as element(e),"
                    + " f:f#2 instance of function(item()*, item()*) as element(e),"
                    + " f:f#2 instance of function(xs:long, xs:anyAtomicType?) as element(e)"));

            // Covariant in the result, and the occurrence counts as much as the item type: an element(e) is
            // an element(), and one of them is one or more of them, but it is not zero or more of them
            // returned as exactly one.
            Assert.AreEqual(
                "true,true,false",
                Run(
                    TwoArguments,
                    "f:f#2 instance of function(xs:long, xs:NCName) as element(),"
                    + " f:f#2 instance of function(xs:long, xs:NCName) as element(e)+,"
                    + " f:f#2 instance of function(xs:long, xs:NCName) as xs:string"));

            // An arity that does not match is not a function type this item is within, whatever the types.
            Assert.AreEqual(
                "false",
                Run(TwoArguments, "f:f#2 instance of function(xs:long) as element(e)"));
        }

        [TestMethod]
        public void AnElementTestNamingATypeIsNarrowerThanOneNamingNone()
        {
            // element(e) is element(e, xs:anyType?), and the '?' there is what makes it nillable — so the
            // test that writes the type out without one asks for something this item does not promise,
            // however alike the two look.
            Assert.AreEqual(
                "false,false",
                Run(
                    TwoArguments,
                    "f:f#2 instance of function(xs:long, xs:NCName) as element(e, xs:anyType),"
                    + " f:f#2 instance of function(xs:long, xs:NCName) as element(*, xs:untyped)"));
        }

        [TestMethod]
        public void ATypeLeftUndeclaredIsAnythingAtAll()
        {
            // Which is what an xsl:function without 'as' and a parameter without one mean, so such a
            // function is not an instance of a signature that names anything narrower than item()*.
            const string untyped =
                "<xsl:function name=\"f:g\"><xsl:param name=\"x\"/><xsl:sequence select=\"$x\"/></xsl:function>";

            Assert.AreEqual(
                "true,false,true",
                Run(
                    untyped,
                    "f:g#1 instance of function(*),"
                    + " f:g#1 instance of function(xs:integer) as xs:integer,"
                    + " f:g#1 instance of function(xs:integer) as item()*"));
        }

        [TestMethod]
        public void AFunctionItemIsCoercedToTheTypeItWasHandedTo()
        {
            // XPath 3.1 §3.4.2. The item is not refused where it is passed: it is wrapped in a function of
            // the required type, which converts the argument on the way in and the result on the way out.
            const string apply =
                "<xsl:function name=\"f:apply\" as=\"xs:string\">"
                + "<xsl:param name=\"g\" as=\"function(xs:string) as xs:string\"/>"
                + "<xsl:sequence select=\"$g('abc')\"/></xsl:function>";

            Assert.AreEqual("ABC", Run(apply, "f:apply(upper-case#1)"));

            // fn:string-length returns an xs:integer, which the function conversion rules will not read as
            // an xs:string. So the failure belongs to the call and not to the passing, and a function that
            // is never called never fails at all.
            Assert.AreEqual("XPTY0004", Refuses(apply, "f:apply(string-length#1)"));

            // The argument is converted to the required type before the function inside ever sees it, which
            // is what makes a function declared to take an xs:float refuse the xs:double it is handed.
            const string number =
                "<xsl:function name=\"f:number\" as=\"xs:double\">"
                + "<xsl:param name=\"g\" as=\"function(xs:double) as xs:double\"/>"
                + "<xsl:sequence select=\"$g(xs:decimal('1.5'))\"/></xsl:function>";

            Assert.AreEqual("2.5", Run(number, "f:number(function($x as xs:double){$x + 1})"));
            Assert.AreEqual("XPTY0004", Refuses(number, "f:number(function($x as xs:float){$x + 1})"));
        }

        [TestMethod]
        public void AFunctionMadeFromAnotherIsNobodysNamedFunction()
        {
            // Applying some of a function's arguments makes a new function, and XPath 3.1 gives the name
            // only to the one that was written. So fn:function-name() of it is the empty sequence, where
            // this engine had been passing the underlying function's name through.
            Assert.AreEqual(
                "true,1,contains",
                Run(
                    string.Empty,
                    "empty(function-name(contains(?, 'e'))),"
                    + " function-arity(contains(?, 'e')),"
                    + " function-name(contains#2)"));
        }

        [TestMethod]
        public void AGlobalVariableIsNotInScopeWithinItself()
        {
            // XSLT 3.0 §9.7. The reference is to a variable that has not been declared where it stands, so
            // an inline function bound to a global cannot recurse by naming the global it is being bound
            // to — however much sense that would make of a definition like this one.
            Assert.AreEqual(
                "XPST0008",
                Refuses(
                    "<xsl:variable name=\"gcd\" as=\"function(*)\" select=\"function($x as xs:integer,"
                    + " $y as xs:integer) { if ($y eq 0) then abs($x) else $gcd($y, $x mod $y) }\"/>",
                    "$gcd(6, 4)"));
        }

        [TestMethod]
        public void AFunctionItemHasNoTypedValueToCompare()
        {
            // A value comparison atomizes each operand, and atomizing a function item is FOTY0013 — not
            // the XPTY0004 of a pair that names no comparison, since what is wrong is the operand rather
            // than the pairing.
            Assert.AreEqual("FOTY0013", Refuses(TwoArguments, "f:f#2 eq 3"));
            Assert.AreEqual("FOTY0013", Refuses(string.Empty, "map { 1: 2 } eq 3"));
        }

        [TestMethod]
        public void ANamedFunctionReferenceMayAskForAsManyArgumentsAsTheFunctionTakes()
        {
            // fn:concat takes any number of them, so concat#123456 names a function that exists and the
            // item is built with one argument position per unit of arity. Past what this engine will build
            // one for, the reference names nothing.
            Assert.AreEqual("2000", Run(string.Empty, "function-arity(concat#2000)"));
            Assert.AreEqual("XPST0017", Refuses(string.Empty, "function-arity(concat#99999999)"));
        }
    }
}
