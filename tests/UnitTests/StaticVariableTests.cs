namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for static variables and parameters (XSLT 3.0 §9.7): what one is worth with nothing said,
    /// how two declarations of one name across imports settle, and what a static declaration may not carry.
    /// </summary>
    [TestClass]
    public sealed class StaticVariableTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        private sealed class Modules : IXsltResolver
        {
            private readonly Dictionary<string, string> m_texts = new(StringComparer.Ordinal);

            public Modules Add(string name, string text)
            {
                m_texts[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string name, string? baseUri)
            {
                return m_texts.TryGetValue(name, out string? text)
                    ? new ResolvedResource(new StringReader(text), name)
                    : null;
            }
        }

        private static string Sheet(string declarations, string body = "<out/>")
        {
            return $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" "
                + "exclude-result-prefixes=\"xs\">" + declarations
                + "<xsl:template name=\"xsl:initial-template\">" + body + "</xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, Modules? modules = null, Dictionary<string, object?>? parameters = null)
        {
            return new Xslt(
                stylesheet,
                new XsltOptions
                {
                    Version = XsltVersion.V30,
                    OmitXmlDeclaration = true,
                    StylesheetResolver = modules,
                    Parameters = parameters,
                }).Transform();
        }

        private static string Refuses(string stylesheet, Modules? modules = null, Dictionary<string, object?>? parameters = null)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(stylesheet, modules, parameters)).Code ?? string.Empty;
        }

        [TestMethod]
        public void AStaticDeclarationWithNothingSaidIsAZeroLengthString()
        {
            // No select, no content, no type: the zero-length string, as for any variable. With a type, the
            // empty sequence where the type takes it.
            Assert.AreEqual(
                "<out empty=\"false\" upper=\"\" equal=\"true\" typed=\"0\"/>",
                Run(Sheet(
                    "<xsl:param name=\"p\" static=\"yes\"/><xsl:variable name=\"v\" static=\"yes\" as=\"xs:string*\"/>",
                    "<out empty=\"{empty($p)}\" upper=\"{upper-case($p)}\" equal=\"{$p = ''}\" typed=\"{count($v)}\"/>")));

            // A parameter whose type does not take the empty sequence is mandatory, and one whose default
            // does not fit its type is mandatory too; a supplied value that does not convert is a type error.
            Assert.AreEqual("XTDE0700", Refuses(Sheet("<xsl:param name=\"p\" static=\"yes\" as=\"xs:integer\"/>")));
            Assert.AreEqual(
                "XTDE0050",
                Refuses(Sheet("<xsl:param name=\"p\" static=\"yes\" as=\"xs:integer\" select=\"xs:date('2014-03-03')\"/>")));
            Assert.AreEqual(
                "XTTE0590",
                Refuses(
                    Sheet("<xsl:param name=\"p\" static=\"yes\" as=\"xs:integer\"/>"),
                    parameters: new Dictionary<string, object?> { ["p"] = "eleven" }));
            Assert.AreEqual(
                "<out>11</out>",
                Run(
                    Sheet("<xsl:param name=\"p\" static=\"yes\" as=\"xs:integer\"/>", "<out>{$p}</out>").Replace(
                        "<xsl:template name=\"xsl:initial-template\">",
                        "<xsl:template name=\"xsl:initial-template\" expand-text=\"yes\">"),
                    parameters: new Dictionary<string, object?> { ["p"] = 11 }));
        }

        [TestMethod]
        public void AStaticDeclarationCarriesNoVisibilityAndIsNeverLocal()
        {
            Assert.AreEqual("XTSE0010", Refuses(Sheet("<xsl:param name=\"p\" static=\"yes\" required=\"yes\" select=\"1\"/>")));
            Assert.AreEqual("XTSE0090", Refuses(Sheet("<xsl:param name=\"p\" static=\"yes\" select=\"1\" visibility=\"private\"/>")));
            Assert.AreEqual("XTSE0090", Refuses(Sheet("<xsl:variable name=\"v\" static=\"yes\" select=\"1\" visibility=\"final\"/>")));
            Assert.AreEqual("XTSE0090", Refuses(Sheet(string.Empty, "<xsl:param name=\"p\" static=\"yes\" select=\"1\"/><out/>")));
        }

        [TestMethod]
        public void StaticsAreSettledInTreeOrderWithImportsInPlace()
        {
            Modules modules = new Modules()
                .Add("one.xsl", $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\"><xsl:variable name=\"p\" static=\"yes\" select=\"1\"/></xsl:stylesheet>")
                .Add("two.xsl", $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\"><xsl:variable name=\"p\" static=\"yes\" select=\"2\"/></xsl:stylesheet>");

            // A shadow attribute after an import reads the static the imported module declared; and a
            // non-static declaration of the name at a higher precedence is what a body's reference means.
            Assert.AreEqual(
                "<out v=\"1\" p=\"0\" q=\"11\"/>",
                Run(
                    Sheet(
                        "<xsl:import href=\"one.xsl\"/><xsl:variable name=\"v\" _select=\"{$p}\"/>"
                        + "<xsl:variable name=\"p\" select=\"0\"/><xsl:variable name=\"q\" static=\"yes\" select=\"$p + 10\"/>",
                        "<out v=\"{$v}\" p=\"{$p}\" q=\"{$q}\"/>"),
                    modules));

            // Declared first at the higher precedence, the principal's wins and the import's is shadowed;
            // declared after an import's, at the higher precedence, it has to agree — the same kind of
            // declaration and the same value — or it is XTSE3450.
            Assert.AreEqual(
                "<out v1=\"3\" v4=\"3\"/>",
                Run(
                    Sheet(
                        "<xsl:variable name=\"p\" static=\"yes\" select=\"3\"/><xsl:variable name=\"v1\" _select=\"{$p}\"/>"
                        + "<xsl:import href=\"one.xsl\"/><xsl:variable name=\"v4\" _select=\"{$p}\"/>",
                        "<out v1=\"{$v1}\" v4=\"{$v4}\"/>"),
                    modules));
            Assert.AreEqual(
                "<out v=\"1\"/>",
                Run(
                    Sheet(
                        "<xsl:import href=\"one.xsl\"/><xsl:variable name=\"p\" static=\"yes\" select=\"1\"/><xsl:variable name=\"v\" _select=\"{$p}\"/>",
                        "<out v=\"{$v}\"/>"),
                    modules));
            Assert.AreEqual(
                "XTSE3450",
                Refuses(Sheet("<xsl:import href=\"one.xsl\"/><xsl:variable name=\"p\" static=\"yes\" select=\"0\"/>"), modules));
            Assert.AreEqual(
                "XTSE3450",
                Refuses(Sheet("<xsl:import href=\"one.xsl\"/><xsl:param name=\"p\" static=\"yes\" select=\"1\"/>"), modules));
            Assert.AreEqual(
                "XTSE3450",
                Refuses(Sheet("<xsl:import href=\"one.xsl\"/><xsl:import href=\"two.xsl\"/>"), modules));
        }

        [TestMethod]
        public void AStaticValueMayHoldNodesOfATreeItBuilt()
        {
            Assert.AreEqual(
                "<out count=\"2\" names=\"key key\"/>",
                Run(Sheet(
                    "<xsl:variable name=\"keys\" static=\"yes\" select=\"json-to-xml('{&quot;a&quot;:1, &quot;b&quot;:2}')//@key\"/>",
                    "<out count=\"{count($keys)}\" names=\"{$keys/local-name()}\"/>")));
        }

        [TestMethod]
        public void AShadowAttributeMaySayThatADeclarationIsStatic()
        {
            // static is an ordinary no-namespace attribute of xsl:variable, so it takes a shadow attribute
            // like any other -- and what that computes decides whether a later static expression may read
            // the declaration at all. The version it asks about is the processor's, which is the whole
            // reason to ask: a stylesheet turns a 3.0 construct on where there is a 3.0 processor under it.
            const string question =
                "<xsl:variable name=\"N\" select=\"'x'\" _static=\"{if "
                + "(system-property('xsl:version') = '<VER>') then 'yes' else 'no'}\"/>";

            const string body =
                "<xsl:variable name=\"x\" select=\"3\"/><out><xsl:value-of _select=\"${$N}\"/></out>";

            // Answered 'yes': $N is static, worth 'x', and the shadow select computes to $x.
            Assert.AreEqual("<out>3</out>", Run(Sheet(question.Replace("<VER>", "3.0"), body)));

            // Answered 'no': an ordinary global variable, which no static expression may read.
            Assert.AreEqual("XPST0008", Refuses(Sheet(question.Replace("<VER>", "9.9"), body)));
        }
    }
}
