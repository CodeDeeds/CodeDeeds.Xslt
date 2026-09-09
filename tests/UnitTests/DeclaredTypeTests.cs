namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>as</c> declarations on variables, parameters and functions.
    /// </summary>
    /// <remarks>
    /// The declaration is not only a check. It decides what a sequence constructor's content amounts to:
    /// without one everything the constructor produces is built into a single document node, so three
    /// integers arrive as the text <c>1 2 3</c>; with one they arrive as three integers.
    /// </remarks>
    [TestClass]
    public sealed class DeclaredTypeTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        /// <summary>
        /// Runs a 2.0 stylesheet. The <c>xs</c> prefix is not declared anywhere: XPath 2.0 binds it without
        /// being asked, and declaring it here would only put it on the result.
        /// </summary>
        private static string Run(string body, string input = "<r/>")
        {
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {Xsl}>{body}</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        /// <summary>Wraps a body in a template writing an expression, with the xs prefix declared.</summary>
        private static string Writes(string declarations, string expression)
        {
            return Run(
                "<xsl:template match=\"/\"><out>"
                + declarations
                + $"<xsl:value-of select=\"{expression}\"/>"
                + "</out></xsl:template>");
        }

        // ---- What a declared type makes of content ---------------------------------------------------------

        [TestMethod]
        public void WithoutADeclarationContentIsOneDocumentNode()
        {
            // Three integers become the text of three integers, which is one node and counts as one.
            Assert.AreEqual(
                "<out>1</out>",
                Writes(
                    "<xsl:variable name=\"v\">"
                    + "<xsl:sequence select=\"1\"/><xsl:sequence select=\"2\"/><xsl:sequence select=\"3\"/>"
                    + "</xsl:variable>",
                    "count($v)"));
        }

        [TestMethod]
        public void WithADeclarationContentIsTheSequenceItProduced()
        {
            Assert.AreEqual(
                "<out>3</out>",
                Writes(
                    "<xsl:variable name=\"v\" as=\"xs:integer*\">"
                    + "<xsl:sequence select=\"1\"/><xsl:sequence select=\"2\"/><xsl:sequence select=\"3\"/>"
                    + "</xsl:variable>",
                    "count($v)"));
        }

        [TestMethod]
        public void ItemsInADeclaredSequenceKeepTheirTypes()
        {
            Assert.AreEqual(
                "<out>6</out>",
                Writes(
                    "<xsl:variable name=\"v\" as=\"xs:integer*\">"
                    + "<xsl:sequence select=\"1\"/><xsl:sequence select=\"2\"/><xsl:sequence select=\"3\"/>"
                    + "</xsl:variable>",
                    "sum($v)"));
        }

        [TestMethod]
        public void ADeclaredConstructorStillBuildsNodes()
        {
            // A literal result element inside such a constructor is an element node, not text, and takes its
            // place in the sequence alongside the atomic items.
            Assert.AreEqual(
                "<out>2|a|1</out>",
                Writes(
                    "<xsl:variable name=\"v\" as=\"item()*\">"
                    + "<a/><xsl:sequence select=\"1\"/>"
                    + "</xsl:variable>",
                    "concat(count($v), '|', local-name($v[1]), '|', $v[2])"));
        }

        [TestMethod]
        public void AdjacentTextInADeclaredConstructorIsTwoNodes()
        {
            // Merging adjacent text nodes is what building a tree does. A declared constructor is a sequence,
            // and two xsl:text in it are two text nodes — which the suite's seqtor-041 counts, zero-length
            // ones included.
            Assert.AreEqual(
                "<out>2|one</out>",
                Writes(
                    "<xsl:variable name=\"v\" as=\"item()*\">"
                    + "<xsl:text>one</xsl:text><xsl:text>two</xsl:text>"
                    + "</xsl:variable>",
                    "concat(count($v), '|', $v[1])"));
        }

        [TestMethod]
        public void AnEmptyDeclaredConstructorIsTheEmptySequence()
        {
            Assert.AreEqual(
                "<out>0</out>",
                Writes("<xsl:variable name=\"v\" as=\"item()*\"/>", "count($v)"));
        }

        // ---- What a declared type checks -------------------------------------------------------------------

        [TestMethod]
        public void ASelectIsCheckedAgainstTheDeclaredType()
        {
            Assert.AreEqual(
                "<out>7</out>",
                Writes("<xsl:variable name=\"v\" as=\"xs:integer\" select=\"7\"/>", "$v"));
        }

        [TestMethod]
        public void AnUntypedValueIsReadAsTheTypeDeclared()
        {
            // Document content is untyped, so this is what saves a stylesheet from casting every reference by
            // hand: the declaration says what the text means.
            Assert.AreEqual(
                "<out>8</out>",
                Run(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"n\" as=\"xs:integer\" select=\"/r/@n\"/>"
                    + "<xsl:value-of select=\"$n + 1\"/></out></xsl:template>",
                    "<r n='7'/>"));
        }

        [TestMethod]
        public void ANumberIsPromotedButNotDemoted()
        {
            Assert.AreEqual(
                "<out>1</out>",
                Writes("<xsl:variable name=\"v\" as=\"xs:double\" select=\"1\"/>", "$v"));
        }

        [TestMethod]
        public void AValueThatCannotFitTheTypeIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes(
                    "<xsl:variable name=\"v\" as=\"xs:integer\" select=\"xs:date('2026-08-23')\"/>",
                    "$v"));

            Assert.AreEqual("XTTE0570", error.Code);
        }

        [TestMethod]
        public void TooManyItemsForTheDeclaredOccurrenceIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("<xsl:variable name=\"v\" as=\"xs:integer\" select=\"(1, 2)\"/>", "$v"));

            Assert.AreEqual("XTTE0570", error.Code);
        }

        [TestMethod]
        public void AnEmptySequenceIsRefusedWhereOneItemIsDeclared()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("<xsl:variable name=\"v\" as=\"xs:integer\" select=\"()\"/>", "$v"));

            Assert.AreEqual("XTTE0570", error.Code);
        }

        // ---- Parameters and functions ----------------------------------------------------------------------

        [TestMethod]
        public void ATemplateParameterIsCheckedAgainstItsType()
        {
            // The value comes from a node, so it is untyped and the declaration says what it means.
            Assert.AreEqual(
                "<out>8</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:call-template name=\"t\">"
                    + "<xsl:with-param name=\"n\" select=\"/r/@n\"/></xsl:call-template></out></xsl:template>"
                    + "<xsl:template name=\"t\"><xsl:param name=\"n\" as=\"xs:integer\"/>"
                    + "<xsl:value-of select=\"$n + 1\"/></xsl:template>",
                    "<r n='7'/>"));
        }

        [TestMethod]
        public void AStringIsNotSilentlyReadAsANumber()
        {
            // Only an untyped value takes the declared type. A string is a string, and a stylesheet that
            // passes one where a number was declared has made a mistake worth hearing about.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\"><out><xsl:call-template name=\"t\">"
                    + "<xsl:with-param name=\"n\" select=\"'7'\"/></xsl:call-template></out></xsl:template>"
                    + "<xsl:template name=\"t\"><xsl:param name=\"n\" as=\"xs:integer\"/>"
                    + "<xsl:value-of select=\"$n\"/></xsl:template>"));

            Assert.AreEqual("XTTE0590", error.Code);
        }

        [TestMethod]
        public void ATemplateParameterOfTheWrongTypeIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:template match=\"/\"><out><xsl:call-template name=\"t\">"
                    + "<xsl:with-param name=\"n\" select=\"/r/@n\"/></xsl:call-template></out></xsl:template>"
                    + "<xsl:template name=\"t\"><xsl:param name=\"n\" as=\"xs:integer\"/>"
                    + "<xsl:value-of select=\"$n\"/></xsl:template>",
                    "<r n='seven'/>"));

            // The cast of 'seven' to an integer fails, and XSLT names the failure after the parameter that
            // asked for it rather than after the cast: XTTE0590, where a variable would be XTTE0570.
            Assert.AreEqual("XTTE0590", error.Code);
        }

        [TestMethod]
        public void AFunctionChecksItsArgumentsAndItsResult()
        {
            Assert.AreEqual(
                "<out>14</out>",
                Run(
                    "<xsl:function name=\"f:twice\" as=\"xs:integer\" xmlns:f=\"urn:test\">"
                    + "<xsl:param name=\"n\" as=\"xs:integer\"/>"
                    + "<xsl:sequence select=\"$n * 2\"/></xsl:function>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"f:twice(/r/@n)\" xmlns:f=\"urn:test\"/></out></xsl:template>",
                    "<r n='7'/>"));
        }

        [TestMethod]
        public void AFunctionResultOfTheWrongTypeIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    "<xsl:function name=\"f:x\" as=\"xs:integer\" xmlns:f=\"urn:test\">"
                    + "<xsl:sequence select=\"'not a number'\"/></xsl:function>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"f:x()\" xmlns:f=\"urn:test\"/></out></xsl:template>"));

            Assert.AreEqual("XTTE0780", error.Code);
        }

        [TestMethod]
        public void AFunctionCanReturnASequence()
        {
            Assert.AreEqual(
                "<out>3</out>",
                Run(
                    "<xsl:function name=\"f:three\" as=\"xs:integer*\" xmlns:f=\"urn:test\">"
                    + "<xsl:sequence select=\"1\"/><xsl:sequence select=\"2\"/><xsl:sequence select=\"3\"/>"
                    + "</xsl:function>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"count(f:three())\" xmlns:f=\"urn:test\"/></out></xsl:template>"));
        }

        [TestMethod]
        public void ADeclaredSequenceCanBeCopiedIntoTheResult()
        {
            // The nodes in it are real nodes, so copying works as it would from any other tree.
            Assert.AreEqual(
                "<out><a/><b/></out>",
                Run(
                    "<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"v\" as=\"item()*\"><a/><b/></xsl:variable>"
                    + "<xsl:copy-of select=\"$v\"/></out></xsl:template>"));
        }

        // ---- Which type a value was made under -------------------------------------------------------------

        [TestMethod]
        [DataRow("xs:int(5) instance of xs:int")]
        [DataRow("xs:int(5) instance of xs:long")]
        [DataRow("xs:int(5) instance of xs:integer")]
        [DataRow("xs:int(5) instance of xs:decimal")]
        [DataRow("xs:unsignedByte('1') instance of xs:unsignedLong")]
        [DataRow("xs:positiveInteger('1') instance of xs:nonNegativeInteger")]
        [DataRow("xs:ID('n') instance of xs:NCName")]
        [DataRow("xs:ID('n') instance of xs:string")]
        [DataRow("xs:NCName('n') instance of xs:token")]
        public void ADerivedTypeIsAnInstanceOfWhatItRestricts(string expression)
        {
            Assert.AreEqual("<out>true</out>", Writes(string.Empty, expression));
        }

        [TestMethod]
        [DataRow("12678967543233 instance of xs:int")]
        [DataRow("5 instance of xs:long")]
        [DataRow("'plain' instance of xs:NCName")]
        [DataRow("xs:long('1') instance of xs:nonNegativeInteger")]
        [DataRow("xs:nonNegativeInteger('1') instance of xs:nonPositiveInteger")]
        [DataRow("xs:ID('n') instance of xs:IDREF")]
        [DataRow("xs:token('ncname') instance of xs:NCName")]
        [DataRow("xs:normalizedString('n') instance of xs:token")]
        public void AndOfNothingElse(string expression)
        {
            // 'instance of' asks which type a value was *made under*, not which types could have held it. So
            // xs:long(1) is not an xs:nonNegativeInteger although 1 is one, and a plain integer is not an
            // xs:int however small it is — the two are siblings under xs:integer, not one above the other.
            Assert.AreEqual("<out>false</out>", Writes(string.Empty, expression));
        }

        [TestMethod]
        public void ADerivedTypeIsCarriedForTypeTestsAndNothingElse()
        {
            // Arithmetic gives back the type it works in, so a sum of an xs:int and an integer is an
            // xs:integer — which is what XML Schema says, and what keeps this out of everything but the
            // type tests.
            Assert.AreEqual("<out>false</out>", Writes(string.Empty, "(xs:int(5) + 1) instance of xs:int"));
            Assert.AreEqual("<out>true</out>", Writes(string.Empty, "(xs:int(5) + 1) instance of xs:integer"));

            // And casting to a name that is not a derived one clears what the value arrived carrying.
            Assert.AreEqual(
                "<out>false</out>",
                Writes(string.Empty, "(xs:int(5) cast as xs:integer) instance of xs:int"));
        }

        [TestMethod]
        [DataRow("3.14e0 instance of xs:numeric", "true")]
        [DataRow("3.14 instance of xs:numeric", "true")]
        [DataRow("3 instance of xs:numeric", "true")]
        [DataRow("xs:float('93.7') instance of xs:numeric", "true")]
        [DataRow("xs:nonNegativeInteger('93') instance of xs:numeric", "true")]
        [DataRow("'93' instance of xs:numeric", "false")]
        [DataRow("true() instance of xs:numeric", "false")]
        public void XsNumericNamesTheThreeNumericTypesAtOnce(string expression, string expected)
        {
            // A string that reads as a number is not one until something casts it.
            Assert.AreEqual($"<out>{expected}</out>", Writes(string.Empty, expression));
        }

        [TestMethod]
        public void CastingToXsNumericKeepsANumberAsItWas()
        {
            // A cast to a union keeps a value already of one of its members, so 17 stays an xs:integer
            // rather than becoming 17e0: there is nothing for the cast to do.
            Assert.AreEqual(
                "<out>true</out>", Writes(string.Empty, "(17 cast as xs:numeric) instance of xs:integer"));
            Assert.AreEqual(
                "<out>true</out>", Writes(string.Empty, "(17.2 cast as xs:numeric) instance of xs:decimal"));
            Assert.AreEqual(
                "<out>true</out>", Writes(string.Empty, "(1e3 cast as xs:numeric) instance of xs:double"));
            Assert.AreEqual(
                "<out>true</out>",
                Writes(string.Empty, "(xs:float(1e3) cast as xs:numeric) instance of xs:float"));

            // Anything that is no number at all becomes an xs:double, the first member that admits it.
            Assert.AreEqual(
                "<out>true</out>", Writes(string.Empty, "(true() cast as xs:numeric) instance of xs:double"));
            Assert.AreEqual(
                "<out>true</out>", Writes(string.Empty, "xs:numeric('12.5') instance of xs:double"));
            Assert.AreEqual("<out>true</out>", Writes(string.Empty, "'12.5' castable as xs:numeric"));
            Assert.AreEqual("<out>false</out>", Writes(string.Empty, "'12.5.7' castable as xs:numeric"));
        }

        [TestMethod]
        public void CountingFunctionsGiveAnIntegerFromTwoPointZero()
        {
            // count, last, position and string-length all return a whole number, and 2.0 declares them
            // xs:integer where 1.0 said only "number" and meant a double.
            Assert.AreEqual("<out>true</out>", Writes(string.Empty, "count(/r) instance of xs:integer"));
            Assert.AreEqual("<out>true</out>", Writes(string.Empty, "string-length('ab') instance of xs:integer"));
            Assert.AreEqual("<out>true</out>", Writes(string.Empty, "position() instance of xs:integer"));
            Assert.AreEqual("<out>true</out>", Writes(string.Empty, "last() instance of xs:integer"));

            // Which is visible to arithmetic: integer div integer is an xs:decimal, and it writes as one.
            Assert.AreEqual("<out>0.5</out>", Writes(string.Empty, "count(/r) div 2"));
        }

        [TestMethod]
        public void AnIntegerLiteralPastTheRangeHeldIsAnOverflow()
        {
            // This engine's xs:integer is a 64-bit one, which the specification permits so long as going
            // past the limit is reported. Reading the literal as a double instead would answer with a
            // different number than was written, and silently.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes(string.Empty, "18446744073709551616"));

            Assert.AreEqual("FOAR0002", error.Code);

            // Overflow is a dynamic error, so a branch that never runs never raises it and the stylesheet
            // holding it still compiles.
            Assert.AreEqual(
                "<out>ok</out>",
                Writes(string.Empty, "if (false()) then 18446744073709551616 else 'ok'"));

            // The largest a long holds is still a literal like any other.
            Assert.AreEqual(
                "<out>9223372036854775807</out>", Writes(string.Empty, "9223372036854775807"));
        }

        // ---- A global declares a type as much as a local does -----------------------------------------------

        [TestMethod]
        public void AGlobalVariableHonoursItsDeclaredType()
        {
            // The declaration was read and then dropped on the way to the compiled stylesheet, so a global
            // was whatever its content happened to make — three integers arriving as the text of three.
            Assert.AreEqual(
                "<out>3</out>",
                Run("<xsl:variable name=\"v\" as=\"xs:integer*\">"
                    + "<xsl:sequence select=\"1\"/><xsl:sequence select=\"2\"/><xsl:sequence select=\"3\"/>"
                    + "</xsl:variable>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"count($v)\"/></out></xsl:template>"));
        }

        [TestMethod]
        public void AGlobalOfTheWrongTypeIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run("<xsl:variable name=\"v\" as=\"xs:integer\" select=\"'not a number'\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"$v\"/></out></xsl:template>"));

            Assert.AreEqual("XTTE0570", error.Code);
        }

        [TestMethod]
        public void AGregorianTypeTakesUntypedContent()
        {
            // Five Gregorian types share one representation, so working back from the representation to a
            // type to convert towards lands on none of them and the content converted to nothing at all.
            Assert.AreEqual(
                "<out>2004</out>",
                Run("<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"v\" as=\"xs:gYear\">2004</xsl:variable>"
                    + "<xsl:value-of select=\"$v\"/></out></xsl:template>"));
        }

        // ---- Nodes a sequence may hold that a tree cannot ---------------------------------------------------

        [TestMethod]
        public void ASequenceMayHoldAnAttributeWithNoElementToBelongTo()
        {
            // Which is the whole reason for declaring as="attribute()": the attribute is the value rather
            // than something to attach. It used to be dropped, leaving an empty sequence where the
            // stylesheet had plainly produced something.
            Assert.AreEqual(
                "<out>my_att=v</out>",
                Run("<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"a\" as=\"attribute()\">"
                    + "<xsl:attribute name=\"my_att\">v</xsl:attribute></xsl:variable>"
                    + "<xsl:value-of select=\"concat(name($a), '=', $a)\"/></out></xsl:template>"));
        }

        [TestMethod]
        public void AParentlessAttributeHasNoParent()
        {
            Assert.AreEqual(
                "<out>0</out>",
                Run("<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"a\" as=\"attribute()\">"
                    + "<xsl:attribute name=\"m\">v</xsl:attribute></xsl:variable>"
                    + "<xsl:value-of select=\"count($a/..)\"/></out></xsl:template>"));
        }

        [TestMethod]
        public void AGlobalMayHoldAParentlessAttributeToo()
        {
            Assert.AreEqual(
                "<out>m</out>",
                Run("<xsl:variable name=\"a\" as=\"attribute()\">"
                    + "<xsl:attribute name=\"m\">v</xsl:attribute></xsl:variable>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"name($a)\"/></out></xsl:template>"));
        }

        [TestMethod]
        public void AnAttributeStillCannotBeAddedToADocumentNode()
        {
            // Without a declared type the content is built into a document node, which carries no
            // attributes — so this one is an error rather than a value, and stays one.
            Assert.ThrowsExactly<XsltException>(
                () => Run("<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"a\"><xsl:attribute name=\"m\">v</xsl:attribute></xsl:variable>"
                    + "<xsl:value-of select=\"$a\"/></out></xsl:template>"));
        }

        [TestMethod]
        public void XslDocumentContributesTheDocumentNodeItMakes()
        {
            // Copying its content out instead would contribute the children and lose the one node the
            // instruction exists to make.
            Assert.AreEqual(
                "<out>1</out>",
                Run("<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"d\" as=\"document-node()\">"
                    + "<xsl:document><a/></xsl:document></xsl:variable>"
                    + "<xsl:value-of select=\"count($d)\"/></out></xsl:template>"));
        }

        // ---- What the declaration does to a node's provenance ---------------------------------------------

        [TestMethod]
        public void ANodeBuiltUnderADeclarationHasNoParent()
        {
            // The other half of what an as declaration decides. Without one the constructor builds a document
            // and everything is inside it; with one it produces a sequence, and the nodes of a sequence are
            // parentless. So the element is its own root and has nothing above it.
            Assert.AreEqual(
                "<out>false|false</out>",
                Run("<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"e\" as=\"element()\"><e/></xsl:variable>"
                    + "<xsl:value-of select=\"exists($e/..)\"/>|"
                    + "<xsl:value-of select=\"$e/root() instance of document-node()\"/>"
                    + "</out></xsl:template>"));
        }

        [TestMethod]
        public void WithoutADeclarationItIsStillADocument()
        {
            // The comparison that makes the one above mean something: the same content with no as attribute
            // is a result tree fragment, which is a document node with the element inside it.
            Assert.AreEqual(
                "<out>true|true</out>",
                Run("<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"d\"><e/></xsl:variable>"
                    + "<xsl:value-of select=\"exists($d/e/..)\"/>|"
                    + "<xsl:value-of select=\"$d/root() instance of document-node()\"/>"
                    + "</out></xsl:template>"));
        }

        [TestMethod]
        public void AParentlessNodeStillMatchesAPattern()
        {
            // A pattern names what a node is, not what it hangs from, so a rule for 'e' applies to one that
            // hangs from nothing. The half-wildcard is here because that form used to be treated as matching
            // any kind of node, which made it look like a test that needs a parent.
            Assert.AreEqual(
                "<out><plain/><half/></out>",
                Run("<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"e\" as=\"element()\"><e/></xsl:variable>"
                    + "<xsl:variable name=\"f\" as=\"element()\"><f/></xsl:variable>"
                    + "<xsl:apply-templates select=\"$e\"/><xsl:apply-templates select=\"$f\"/>"
                    + "</out></xsl:template>"
                    + "<xsl:template match=\"e\"><plain/></xsl:template>"
                    + "<xsl:template match=\"*:f\"><half/></xsl:template>"));
        }

        [TestMethod]
        public void AHalfWildcardOutranksAWholeOne()
        {
            // '*:f' fixes one half of the name and '*' fixes neither, so the first is the more specific and
            // the specification gives it the higher default priority. Scoring both alike let the rule written
            // later win, which is a silent way to pick the wrong template.
            Assert.AreEqual(
                "<out><half/></out>",
                Run("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"/r/f\"/></out>"
                    + "</xsl:template>"
                    + "<xsl:template match=\"*:f\"><half/></xsl:template>"
                    + "<xsl:template match=\"*\"><whole/></xsl:template>",
                    "<r><f/></r>"));
        }

        [TestMethod]
        public void AKeyCannotBeLookedUpInATreeThatIsNotADocument()
        {
            // A key indexes a document, so a tree rooted at a parentless element has no index to consult.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run("<xsl:key name=\"k\" match=\"*\" use=\"'v'\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:variable name=\"e\" as=\"element()\"><e/></xsl:variable>"
                    + "<xsl:for-each select=\"$e\">"
                    + "<xsl:sequence select=\"key('k', 'v')\"/></xsl:for-each>"
                    + "</out></xsl:template>"));

            Assert.AreEqual("XTDE1270", error.Code);
            StringAssert.Contains(error.Message, "root is not a document node");
        }
        [TestMethod]
        public void AnElementTestMayNameTheTypeNothingHereWasValidatedAgainst()
        {
            // Nothing is validated, so every element is annotated xs:untyped and every attribute
            // xs:untypedAtomic (XDM §5.2). A test naming one of those, or a type it derives from, matches;
            // naming anything else matches nothing at all, which is the honest answer with no schema to hand.
            Assert.AreEqual(
                "<out>true true false false</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:value-of select=\""
                    + "/r/e instance of element(*, xs:untyped),"
                    + " /r/e instance of element(*, xs:anyType),"
                    + " /r/e instance of element(*, xs:untypedAtomic),"
                    + " /r/e instance of element(*, xs:string)\"/></out></xsl:template>",
                    "<r><e a=\"1\"/></r>"));

            Assert.AreEqual(
                "<out>true true true false</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:value-of select=\""
                    + "/r/e/@a instance of attribute(*, xs:untypedAtomic),"
                    + " /r/e/@a instance of attribute(*, xs:anyAtomicType),"
                    + " /r/e/@a instance of attribute(*, xs:anyType),"
                    + " /r/e/@a instance of attribute(*, xs:untyped)\"/></out></xsl:template>",
                    "<r><e a=\"1\"/></r>"));
        }
        // ---- Subtype substitution, promotion, and what a declaration converts -------------------------------

        [TestMethod]
        public void ADeclaredTypeIsSatisfiedByATypeDerivedFromIt()
        {
            // xs:dayTimeDuration and xs:yearMonthDuration are derived from xs:duration, so a value of
            // either satisfies a declaration of xs:duration and keeps the type it had. Subtype
            // substitution converts nothing: the value goes through as it stood.
            Assert.AreEqual(
                "<out>true true</out>",
                Writes(
                    "<xsl:variable name=\"v\" select=\"xs:dayTimeDuration('PT99.999S')\" as=\"xs:duration\"/>",
                    "$v instance of xs:dayTimeDuration, $v instance of xs:duration"));

            Assert.AreEqual(
                "<out>true true</out>",
                Writes(
                    "<xsl:variable name=\"v\" select=\"xs:yearMonthDuration('-P21M')\" as=\"xs:duration\"/>",
                    "$v instance of xs:yearMonthDuration, $v instance of xs:duration"));

            // Derivation goes the one way: a duration that is neither of them is an instance of neither.
            Assert.AreEqual(
                "<out>false false</out>",
                Writes(
                    string.Empty,
                    "xs:duration('P1Y1D') instance of xs:dayTimeDuration,"
                    + " xs:duration('P1Y1D') instance of xs:yearMonthDuration"));

            // The one relation of this shape that was already answered, which the durations now stand
            // beside.
            Assert.AreEqual(
                "<out>true false</out>",
                Writes(string.Empty, "7 instance of xs:decimal, xs:decimal(7) instance of xs:integer"));
        }

        [TestMethod]
        public void AUriDeclaredAsAStringIsPromotedAndStopsBeingAUri()
        {
            // Promotion is not substitution. xs:anyURI is not derived from xs:string — the two meet only
            // in the promotion the conversion rules allow — so the value is converted rather than
            // admitted, and what comes out is a string that no longer answers to being a URI.
            Assert.AreEqual(
                "<out>false true urn:x</out>",
                Writes(
                    "<xsl:variable name=\"v\" select=\"xs:anyURI('urn:x')\" as=\"xs:string\"/>",
                    "$v instance of xs:anyURI, $v instance of xs:string, $v"));

            // Undeclared it stays what it was: nothing promotes a value that was not asked to be a string.
            Assert.AreEqual(
                "<out>true false</out>",
                Writes(
                    "<xsl:variable name=\"v\" select=\"xs:anyURI('urn:x')\"/>",
                    "$v instance of xs:anyURI, $v instance of xs:string"));
        }

        [TestMethod]
        public void AnyAtomicTypeAtomizesTheNodeItIsGiven()
        {
            // xs:anyAtomicType names no one type to be converted towards, but it does ask for an atomic
            // value — so the first of the conversion rules still applies and the node contributes its
            // typed value, which with nothing validated is untyped.
            Assert.AreEqual(
                "<out>true true 5 9</out>",
                Run(
                    "<xsl:variable name=\"e\" select=\"/r/e\" as=\"xs:anyAtomicType\"/>"
                    + "<xsl:variable name=\"a\" select=\"/r/e/@a\" as=\"xs:anyAtomicType\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\""
                    + "$e instance of xs:untypedAtomic, $a instance of xs:anyAtomicType, $e, $a\"/>"
                    + "</out></xsl:template>",
                    "<r><e a=\"9\">5</e></r>"));
        }

        // ---- What position() and last() are, under a backend that emits --------------------------------------

        [TestMethod]
        public void PositionIsAnIntegerADeclarationAccepts()
        {
            // The emitted backend wrapped position() as a double at every version, which a variable
            // declaring xs:integer refused. Run() runs both backends and requires them to agree, so this
            // fails on the compiled one alone if the two ever part company again.
            Assert.AreEqual(
                "<out>1 2 3</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:for-each select=\"r/e\">"
                    + "<xsl:variable name=\"p\" select=\"position()\" as=\"xs:integer\"/>"
                    + "<xsl:if test=\"position() gt 1\"><xsl:text> </xsl:text></xsl:if>"
                    + "<xsl:value-of select=\"$p\"/>"
                    + "</xsl:for-each></out></xsl:template>",
                    "<r><e/><e/><e/></r>"));
        }

        [TestMethod]
        public void LastIsAnIntegerADeclarationAccepts()
        {
            Assert.AreEqual(
                "<out>3</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:for-each select=\"r/e\">"
                    + "<xsl:variable name=\"n\" select=\"last()\" as=\"xs:integer\"/>"
                    + "<xsl:if test=\"position() eq 1\"><xsl:value-of select=\"$n\"/></xsl:if>"
                    + "</xsl:for-each></out></xsl:template>",
                    "<r><e/><e/><e/></r>"));
        }

        [TestMethod]
        public void ARangeMayEndAtAPositionHeldInAVariable()
        {
            // 'to' wants an integer on each side, so a double was refused outright rather than converted.
            // The variable is the point: written as "1 to position()" the range is one expression, and a
            // range is not emitted, so the whole of it falls back and the interpreter answers. It is only
            // when position() is a compiled expression in its own right — an xsl:variable's select — that
            // what the emitted code produced is what the range receives. This is the shape the suite's
            // result-document-1502 failed on.
            Assert.AreEqual(
                "<out>1 1 2 1 2 3</out>",
                Run(
                    "<xsl:template match=\"/\"><out><xsl:for-each select=\"r/e\">"
                    + "<xsl:variable name=\"p\" select=\"position()\"/>"
                    + "<xsl:if test=\"$p gt 1\"><xsl:text> </xsl:text></xsl:if>"
                    + "<xsl:value-of select=\"1 to $p\"/>"
                    + "</xsl:for-each></out></xsl:template>",
                    "<r><e/><e/><e/></r>"));
        }

        [TestMethod]
        public void UnderOnePointZeroPositionStaysADouble()
        {
            // The other half of the rule, and why the fix is not simply "emit an integer": 1.0 has one
            // numeric type, so position() is a double there and the emitted form must stay one.
            string stylesheet = "<xsl:stylesheet version=\"1.0\" " + Xsl + ">"
                + "<xsl:template match=\"/\"><out><xsl:value-of select=\"position() * 1.5\"/></out>"
                + "</xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            Assert.AreEqual(
                "<out>1.5</out>",
                new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>"));

            Assert.AreEqual(
                new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>"),
                new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>"),
                "the compiled backend disagreed with the interpreter");
        }
    }
}
