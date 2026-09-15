namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for arguments and operands being held to the types they are declared with.
    /// </summary>
    /// <remarks>
    /// A function that reads an argument for what it can get out of it, rather than checking what it was
    /// given, answers questions that were never validly asked: <c>in-scope-prefixes(/)</c> is handed a
    /// document node where an element is declared, and an empty sequence back reads as an element with
    /// nothing in scope. The same mistake in a different place makes
    /// <c>fn:unparsed-text-available(1)</c> answer false, where the argument is of the wrong type and
    /// nothing was ever looked for.
    /// </remarks>
    [TestClass]
    public sealed class ArgumentTypeTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private static string Writes(string expression, string input = "<r xmlns:p=\"urn:p\"><c/></r>")
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

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

        private static void Refuses(string code, string expression)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression));
            Assert.AreEqual(code, error.Code, $"'{expression}': {error.Message}");
        }

        // ---- An element argument is an element -------------------------------------------------------------

        [TestMethod]
        public void TheFunctionsDeclaringAnElementTakeOnlyOne()
        {
            Refuses("XPTY0004", "in-scope-prefixes(/)");
            Refuses("XPTY0004", "namespace-uri-for-prefix('p', /)");

            // An element answers as it always did, and carries the xml prefix whether anything declared
            // it or not.
            Assert.AreEqual("p xml", Writes("string-join(sort(in-scope-prefixes(/r)), ' ')"));
        }

        [TestMethod]
        public void TheXmlPrefixResolvesWithoutBeingDeclared()
        {
            // XML binds it by definition, so an element carries it whether the document mentions it or not.
            Assert.AreEqual(
                "http://www.w3.org/XML/1998/namespace",
                Writes("namespace-uri-from-QName(resolve-QName('xml:space', /r))"));

            Assert.AreEqual(
                "http://www.w3.org/XML/1998/namespace", Writes("namespace-uri-for-prefix('xml', /r)"));
        }

        // ---- A wrong type is not a false answer ------------------------------------------------------------

        [TestMethod]
        public void UnparsedTextAvailableRefusesAnArgumentOfTheWrongType()
        {
            // This function answers whether fn:unparsed-text() would succeed, and catches what that raises
            // to do it. An argument of the wrong type is a type error of this call, raised before either
            // function is entered, so answering false to it would be answering an invalid question.
            Refuses("XPTY0004", "unparsed-text-available(1)");
            Refuses("XPTY0004", "unparsed-text-available(current-date())");

            // A reference that simply is not there is still false, which is the whole point of the function.
            Assert.AreEqual("false", Writes("unparsed-text-available('no-such-file.txt')"));
        }

        // ---- An array is a value like any other ------------------------------------------------------------

        [TestMethod]
        public void AValueComparisonAtomizesAnArray()
        {
            // An array atomizes to its members, so [3] is the integer 3 on either side of the operator.
            Assert.AreEqual("true", Writes("[3] eq 3"));
            Assert.AreEqual("true", Writes("[3] le [3]"));
            Assert.AreEqual("true", Writes("3 eq [3]"));
            Assert.AreEqual("false", Writes("[3] eq [4]"));

            // One holding several is several values, which a value comparison has no room for.
            Refuses("XPTY0004", "[3, 4] eq 3");

            // And an empty one is the empty sequence, which makes the whole comparison empty.
            Assert.AreEqual("0", Writes("count([] eq 3)"));
        }

        // ---- min and max answer at the type the items promoted to ------------------------------------------

        [TestMethod]
        public void TheAnswerHasTheTypeTheItemsWereComparedAt()
        {
            // The items are brought to a common type before being compared, so the answer has that type
            // rather than the type of whichever item happened to win. Untyped text is an xs:double.
            Assert.AreEqual(
                "true",
                Writes("max((xs:float('NaN'), xs:untypedAtomic('3'), xs:float(2))) instance of xs:double"));

            Assert.AreEqual(
                "true",
                Writes("min((xs:float('NaN'), xs:untypedAtomic('3'), xs:float(2))) instance of xs:double"));

            // Which was already true of an answer that was not NaN, and is now true of one that is.
            Assert.AreEqual("true", Writes("min((1, xs:float(2))) instance of xs:float"));
            Assert.AreEqual("true", Writes("min((1, 2)) instance of xs:integer"));
        }
    }
}
