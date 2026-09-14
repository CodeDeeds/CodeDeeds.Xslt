namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for starting a transformation at a named function: XSLT 3.0's third way in, beside a source
    /// document and a named template.
    /// </summary>
    [TestClass]
    public sealed class InitialFunctionTests
    {
        private const string Sheet =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" exclude-result-prefixes=\"xs f\">"
            + "<xsl:function name=\"f:add\" visibility=\"public\" as=\"xs:integer\">"
            + "<xsl:param name=\"a\" as=\"xs:integer\"/><xsl:param name=\"b\" as=\"xs:integer\"/>"
            + "<xsl:sequence select=\"$a + $b\"/></xsl:function>"
            + "<xsl:function name=\"f:add\" visibility=\"public\" as=\"xs:integer\">"
            + "<xsl:param name=\"a\" as=\"xs:integer\"/>"
            + "<xsl:sequence select=\"$a + 100\"/></xsl:function>"
            + "<xsl:function name=\"f:wrap\" visibility=\"public\">"
            + "<xsl:param name=\"t\"/><made><xsl:value-of select=\"$t\"/></made></xsl:function>"
            + "<xsl:function name=\"f:hidden\" as=\"xs:integer\">"
            + "<xsl:sequence select=\"1\"/></xsl:function>"
            + "</xsl:stylesheet>";

        private static string Run(string name, params object?[] arguments)
        {
            return new Xslt(
                Sheet,
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    InitialFunction = name,
                    FunctionArguments = arguments,
                })
                .Transform();
        }

        private static string Fails(string name, params object?[] arguments)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(name, arguments)).Code ?? "(no code)";
        }

        [TestMethod]
        public void TheFunctionsReturnValueIsTheResult()
        {
            // What the function returns is the whole of the transformation: an atomic value is written as
            // its string, an element as itself.
            Assert.AreEqual("12", Run("{urn:f}add", 5, 7));
            Assert.AreEqual("<made>hello</made>", Run("{urn:f}wrap", "hello"));
        }

        [TestMethod]
        public void HowManyArgumentsThereAreIsTheArity()
        {
            // Two declarations of one name are two functions, and the call picks the one it fits rather
            // than mis-calling the other.
            Assert.AreEqual("12", Run("{urn:f}add", 5, 7));
            Assert.AreEqual("105", Run("{urn:f}add", 5));
            Assert.AreEqual("XTDE0041", Fails("{urn:f}add", 1, 2, 3));
        }

        [TestMethod]
        public void AFunctionThatIsNotThereOrNotPublicIsNoWayIn()
        {
            Assert.AreEqual("XTDE0041", Fails("{urn:f}missing"));
            Assert.AreEqual("XTDE0041", Fails("{urn:f}hidden"));
        }

        [TestMethod]
        public void AnArgumentIsHeldToTheDeclarationAsAnyCallsIs()
        {
            // The conversion is a call's own, not this entry point's: the function conversion rules
            // promote a number and cast an untyped value, and leave a string where an integer was asked
            // for as the type error it is.
            Assert.AreEqual("12", Run("{urn:f}add", 5L, 7));
            Assert.AreEqual("<made>5</made>", Run("{urn:f}wrap", 5));

            XsltException refused = Assert.ThrowsExactly<XsltException>(() => Run("{urn:f}add", "5", "7"));
            StringAssert.Contains(refused.Message, "xs:integer");
        }

        [TestMethod]
        public void TheResultCanBeHadAsATreeToo()
        {
            Model.XdmTree result = new Xslt(
                Sheet,
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    InitialFunction = "{urn:f}wrap",
                    FunctionArguments = new object?[] { "hello" },
                })
                .TransformToTree();

            Assert.AreEqual("hello", result.StringValueOf(Model.XdmTree.RootNode));
        }
    }
}
