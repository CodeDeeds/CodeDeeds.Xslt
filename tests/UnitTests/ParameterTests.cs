using System.Xml;
using System.Xml.Xsl;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for values a caller supplies through <see cref="XsltOptions.Parameters"/>.
    /// </summary>
    /// <remarks>
    /// Stylesheet parameters are an XSLT 1.0 feature, so <c>XslCompiledTransform</c> can settle how one
    /// behaves once it arrives — it takes them through <see cref="XsltArgumentList"/>. What it cannot settle
    /// is the part that is this engine's own: which .NET types are accepted, and what each becomes in the
    /// XPath data model. Those tests carry their own expectations.
    /// </remarks>
    [TestClass]
    public sealed class ParameterTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private static string Sheet(string body, string version = "1.0")
        {
            return $"<xsl:stylesheet version=\"{version}\" {Xsl}>{body}</xsl:stylesheet>";
        }

        /// <summary>Wraps a body in a template that writes an expression's value.</summary>
        private static string Writes(string declarations, string expression)
        {
            return Sheet(
                declarations
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>");
        }

        private static string Run(string stylesheet, string input, Dictionary<string, object?> parameters)
        {
            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                Parameters = parameters,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        /// <summary>Runs the same stylesheet and parameters through the framework's processor.</summary>
        private static string RunReference(
            string stylesheet,
            string input,
            Dictionary<string, object?> parameters)
        {
            XslCompiledTransform transform = new XslCompiledTransform();
            using (XmlReader reader = XmlReader.Create(new StringReader(stylesheet)))
            {
                transform.Load(reader);
            }

            XsltArgumentList arguments = new XsltArgumentList();
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                arguments.AddParam(parameter.Key, string.Empty, parameter.Value!);
            }

            StringWriter output = new StringWriter();
            XmlWriterSettings settings = transform.OutputSettings!.Clone();
            settings.OmitXmlDeclaration = true;
            settings.ConformanceLevel = ConformanceLevel.Auto;

            using (XmlWriter writer = XmlWriter.Create(output, settings))
            using (XmlReader reader = XmlReader.Create(new StringReader(input)))
            {
                transform.Transform(reader, arguments, writer);
            }

            return output.ToString();
        }

        private static void AssertMatchesReference(
            string stylesheet,
            string input,
            Dictionary<string, object?> parameters)
        {
            Assert.AreEqual(
                XmlComparison.Normalize(RunReference(stylesheet, input, parameters)),
                XmlComparison.Normalize(Run(stylesheet, input, parameters)),
                "output differed from XslCompiledTransform");
        }

        // ---- Against the reference ------------------------------------------------------------------------

        [TestMethod]
        public void ASuppliedValueReplacesTheDefault()
        {
            AssertMatchesReference(
                Writes("<xsl:param name=\"who\" select=\"'world'\"/>", "concat('hello ', $who)"),
                "<r/>",
                new Dictionary<string, object?> { ["who"] = "you" });
        }

        [TestMethod]
        public void AParameterNobodySuppliesKeepsItsDefault()
        {
            AssertMatchesReference(
                Writes("<xsl:param name=\"who\" select=\"'world'\"/>", "concat('hello ', $who)"),
                "<r/>",
                new Dictionary<string, object?>());
        }

        [TestMethod]
        public void ANumberArrivesAsANumber()
        {
            // Not as the string of one: adding to it has to mean addition.
            AssertMatchesReference(
                Writes("<xsl:param name=\"n\"/>", "$n + 1"),
                "<r/>",
                new Dictionary<string, object?> { ["n"] = 41.0 });
        }

        [TestMethod]
        public void ABooleanArrivesAsABoolean()
        {
            AssertMatchesReference(
                Writes("<xsl:param name=\"flag\"/>", "$flag and true()"),
                "<r/>",
                new Dictionary<string, object?> { ["flag"] = false });
        }

        [TestMethod]
        public void AParameterCanBeUsedInAPredicate()
        {
            AssertMatchesReference(
                Writes("<xsl:param name=\"want\"/>", "count(/r/i[@k = $want])"),
                "<r><i k='a'/><i k='b'/><i k='a'/></r>",
                new Dictionary<string, object?> { ["want"] = "a" });
        }

        [TestMethod]
        public void ANameTheStylesheetDoesNotDeclareIsIgnored()
        {
            // The specification says so, and it is what lets one set of values serve several stylesheets.
            AssertMatchesReference(
                Writes("<xsl:param name=\"used\" select=\"'d'\"/>", "$used"),
                "<r/>",
                new Dictionary<string, object?> { ["used"] = "v", ["unused"] = "x" });
        }

        // ---- This engine's own ground ---------------------------------------------------------------------

        [TestMethod]
        public void AnIntegerKeepsMorePrecisionThanADoubleWould()
        {
            // Eighteen digits, which is what xs:integer promises and a double cannot hold.
            Assert.AreEqual(
                "<out>999999999999999999</out>",
                Run(
                    Writes("<xsl:param name=\"n\"/>", "$n"),
                    "<r/>",
                    new Dictionary<string, object?> { ["n"] = 999999999999999999L }));
        }

        [TestMethod]
        public void ADecimalArrivesAsADecimal()
        {
            Assert.AreEqual(
                "<out>3.5</out>",
                Run(
                    Writes("<xsl:param name=\"n\"/>", "$n + 1"),
                    "<r/>",
                    new Dictionary<string, object?> { ["n"] = 2.5m }));
        }

        [TestMethod]
        public void NullIsTheEmptySequence()
        {
            Assert.AreEqual(
                "<out>0|</out>",
                Run(
                    Writes("<xsl:param name=\"p\"/>", "concat(count($p), '|', string($p))"),
                    "<r/>",
                    new Dictionary<string, object?> { ["p"] = null }));
        }

        [TestMethod]
        public void AWholeDocumentCanBeSuppliedAsAParameter()
        {
            // A second document without a resolver: the caller has already read it, so nothing needs to be
            // fetched and nothing about the stylesheet's reach changes.
            XdmTree lookup = XdmTreeBuilder.FromXml(new StringReader("<t><i k='a'>1</i><i k='b'>2</i></t>"));

            Assert.AreEqual(
                "<out>2</out>",
                Run(
                    Writes("<xsl:param name=\"doc\"/>", "$doc/t/i[@k = 'b']"),
                    "<r/>",
                    new Dictionary<string, object?> { ["doc"] = lookup }));
        }

        [TestMethod]
        public void AParameterInANamespaceIsNamedByItsExpandedName()
        {
            // The prefix declared on the template reaches the literal result element, as it always does.
            Assert.AreEqual(
                "<out xmlns:x=\"urn:x\">v</out>",
                Run(
                    Sheet(
                        "<xsl:param name=\"x:p\" xmlns:x=\"urn:x\"/>"
                        + "<xsl:template match=\"/\" xmlns:x=\"urn:x\"><out>"
                        + "<xsl:value-of select=\"$x:p\"/></out></xsl:template>"),
                    "<r/>",
                    new Dictionary<string, object?> { ["{urn:x}p"] = "v" }));
        }

        [TestMethod]
        public void AGlobalVariableIsNotAParameterAndKeepsItsValue()
        {
            // Only xsl:param declares something the caller may set. A name that happens to match a variable
            // is a name the stylesheet does not declare as a parameter, and is ignored like any other.
            Assert.AreEqual(
                "<out>mine</out>",
                Run(
                    Writes("<xsl:variable name=\"v\" select=\"'mine'\"/>", "$v"),
                    "<r/>",
                    new Dictionary<string, object?> { ["v"] = "yours" }));
        }

        [TestMethod]
        public void AGlobalDeclaredBeforeAParameterStillSeesTheSuppliedValue()
        {
            // Declaration order decides nothing here. Evaluating globals in order would reach the parameter
            // through the variable above it and take its default before the supplied value was installed.
            Assert.AreEqual(
                "<out>[v]</out>",
                Run(
                    Writes(
                        "<xsl:variable name=\"wrapped\" select=\"concat('[', $p, ']')\"/>"
                        + "<xsl:param name=\"p\" select=\"'default'\"/>",
                        "$wrapped"),
                    "<r/>",
                    new Dictionary<string, object?> { ["p"] = "v" }));
        }

        [TestMethod]
        public void ARequiredParameterIsSatisfiedByASuppliedValue()
        {
            Assert.AreEqual(
                "<out>v</out>",
                Run(
                    Writes("<xsl:param name=\"p\" required=\"yes\"/>", "$p"),
                    "<r/>",
                    new Dictionary<string, object?> { ["p"] = "v" }));
        }

        [TestMethod]
        public void ChangingTheDictionaryAfterwardsChangesNothing()
        {
            // The options object copies what it is given, because "safe to share between threads" must not
            // depend on the caller never touching the dictionary again.
            Dictionary<string, object?> parameters = new Dictionary<string, object?> { ["p"] = "first" };
            Xslt stylesheet = new Xslt(
                Writes("<xsl:param name=\"p\"/>", "$p"),
                new XsltOptions { OmitXmlDeclaration = true, Parameters = parameters });

            parameters["p"] = "second";

            Assert.AreEqual("<out>first</out>", stylesheet.TransformXml("<r/>"));
        }

        // ---- What is refused ------------------------------------------------------------------------------

        [TestMethod]
        public void APrefixedParameterNameIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Writes("<xsl:param name=\"p\"/>", "$p"),
                    "<r/>",
                    new Dictionary<string, object?> { ["x:p"] = "v" }));

            StringAssert.Contains(error.Message, "carries a prefix");
        }

        [TestMethod]
        public void AnUnclosedNamespaceBraceIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Writes("<xsl:param name=\"p\"/>", "$p"),
                    "<r/>",
                    new Dictionary<string, object?> { ["{urn:x p"] = "v" }));

            StringAssert.Contains(error.Message, "never closes it");
        }

        [TestMethod]
        public void AValueWithNoXPathCounterpartIsRefused()
        {
            // Rather than stringified, which would turn a caller's mistake into a stylesheet that runs and
            // produces something nobody asked for.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Run(
                    Writes("<xsl:param name=\"p\"/>", "$p"),
                    "<r/>",
                    new Dictionary<string, object?> { ["p"] = new Uri("urn:x") }));

            StringAssert.Contains(error.Message, "has no XPath counterpart");
        }
    }
}
