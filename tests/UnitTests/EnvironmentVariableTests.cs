namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:environment-variable()</c> and <c>fn:available-environment-variables()</c>: invisible
    /// unless the caller says otherwise, and the same environment for a whole transformation once they are.
    /// </summary>
    /// <remarks>
    /// Tests run in parallel and the environment is the process's, so each test sets a variable of its own
    /// name and asks about that one: another test's variable may be there or not, and nothing here minds.
    /// </remarks>
    [TestClass]
    public sealed class EnvironmentVariableTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>A variable of this test's own, unset when the test is done with it.</summary>
        private sealed class Variable : IDisposable
        {
            public Variable(string? value)
            {
                Name = "CODEDEEDS_XSLT_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                Environment.SetEnvironmentVariable(Name, value);
            }

            public string Name { get; }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable(Name, null);
            }
        }

        private static string Sheet(string body, string version = "3.0", string declarations = "")
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + declarations
                + $"<xsl:template match=\"/\"><out>{body}</out></xsl:template></xsl:stylesheet>";
        }

        private static XsltOptions Options(
            XsltBackend backend,
            bool enabled,
            XsltVersion? version = null,
            IXsltResolver? documents = null)
        {
            return new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                EnvironmentVariablesEnabled = enabled,
                Version = version ?? XsltVersion.Implemented,
                DocumentResolver = documents,
            };
        }

        /// <summary>Runs on both backends, which must agree, and returns what the out element holds.</summary>
        private static string Both(string stylesheet, Func<XsltBackend, XsltOptions> options)
        {
            string interpreted = new Xslt(stylesheet, options(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, options(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        private static string Fails(string stylesheet, Func<XsltBackend, XsltOptions> options)
        {
            string? interpreted = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, options(XsltBackend.Interpreted)).TransformXml("<r/>")).Code;
            string? compiled = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, options(XsltBackend.Compiled)).TransformXml("<r/>")).Code;

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted ?? string.Empty;
        }

        private static string Value(string expression)
        {
            return $"<xsl:value-of select=\"{expression}\" separator=\",\"/>";
        }

        [TestMethod]
        public void TheEnvironmentIsInvisibleUnlessTheCallerSaysOtherwise()
        {
            using Variable variable = new Variable("secret");
            XsltOptions Off(XsltBackend backend) => Options(backend, enabled: false);

            // Nothing, which is what the specification has a processor that provides no access answer,
            // and the functions are there all the same.
            Assert.AreEqual(
                "0,0,true,true",
                Both(
                    Sheet(Value(
                        $"count(environment-variable('{variable.Name}')), count(available-environment-variables()), "
                        + "function-available('environment-variable', 1), "
                        + "function-available('available-environment-variables', 0)")),
                    Off));
        }

        [TestMethod]
        public void EnabledAStylesheetReadsTheEnvironment()
        {
            using Variable variable = new Variable("forty two");
            using Variable empty = new Variable(string.Empty);
            XsltOptions On(XsltBackend backend) => Options(backend, enabled: true);

            // The value; an empty value, which is a value; a name nobody set, which is none; and the names,
            // among which this test's own are found.
            Assert.AreEqual(
                "forty two,true,,0,true,true",
                Both(
                    Sheet(Value(
                        $"environment-variable('{variable.Name}'), "
                        + $"exists(environment-variable('{empty.Name}')), environment-variable('{empty.Name}'), "
                        + $"count(environment-variable('{variable.Name}_NOT_SET')), "
                        + $"available-environment-variables() = '{variable.Name}', "
                        + $"available-environment-variables() = '{empty.Name}'")),
                    On));

            // The names come in one order, whatever order the process keeps them in.
            Assert.AreEqual(
                "true",
                Both(
                    Sheet(Value(
                        "let $names := available-environment-variables() return "
                        + "every $i in 1 to count($names) - 1 satisfies "
                        + "compare($names[$i], $names[$i + 1], 'http://www.w3.org/2005/xpath-functions/collation/codepoint') lt 0")),
                    On));
        }

        [TestMethod]
        public void TheArgumentIsCheckedWhetherOrNotTheEnvironmentIsVisible()
        {
            foreach (bool enabled in new[] { false, true })
            {
                XsltOptions With(XsltBackend backend) => Options(backend, enabled);

                // A name is one string: not none, and not a number.
                Assert.AreEqual("XPTY0004", Fails(Sheet(Value("environment-variable(())")), With), $"enabled={enabled}");
                Assert.AreEqual("XPTY0004", Fails(Sheet(Value("environment-variable(1)")), With), $"enabled={enabled}");
                Assert.AreEqual("XPST0017", Fails(Sheet(Value("environment-variable()")), With), $"enabled={enabled}");
                Assert.AreEqual("XPST0017", Fails(Sheet(Value("available-environment-variables(1)")), With), $"enabled={enabled}");
            }
        }

        [TestMethod]
        public void ATwoPointZeroProcessorDoesNotHaveTheFunctions()
        {
            XsltOptions Old(XsltBackend backend) => Options(backend, enabled: true, version: XsltVersion.V20);

            Assert.AreEqual("XPST0017", Fails(Sheet(Value("environment-variable('PATH')"), "2.0"), Old));
            Assert.AreEqual("XPST0017", Fails(Sheet(Value("available-environment-variables()"), "2.0"), Old));
        }

        [TestMethod]
        public void AUseWhenReadsTheEnvironmentWhenTheStylesheetIsCompiled()
        {
            using Variable variable = new Variable("on");

            // A static expression runs before any transformation does, so the decision reaches it from the
            // options the compiler was given.
            string stylesheet =
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                + $"<xsl:template match=\"/\" use-when=\"environment-variable('{variable.Name}') = 'on'\"><out>kept</out></xsl:template>"
                + $"<xsl:template match=\"/\" use-when=\"not(environment-variable('{variable.Name}') = 'on')\"><out>dropped</out></xsl:template>"
                + "</xsl:stylesheet>";

            Assert.AreEqual("kept", Both(stylesheet, backend => Options(backend, enabled: true)));
            Assert.AreEqual("dropped", Both(stylesheet, backend => Options(backend, enabled: false)));
        }

        [TestMethod]
        public void ATransformationStartedByTheStylesheetInheritsTheSetting()
        {
            using Variable variable = new Variable("inherited");

            // fn:transform() runs on the caller's behalf and may reach no further than the caller could,
            // which is also no less far.
            string stylesheet = Sheet(
                Value("transform(map{'stylesheet-text': $sheet, 'initial-template': xs:QName('xsl:initial-template')})?output/v/string()"),
                declarations:
                    "<xsl:variable name=\"sheet\" as=\"xs:string\"><![CDATA["
                    + $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                    + "<xsl:template name=\"xsl:initial-template\">"
                    + $"<v><xsl:value-of select=\"environment-variable('{variable.Name}')\"/></v>"
                    + "</xsl:template></xsl:stylesheet>"
                    + "]]></xsl:variable>");

            Assert.AreEqual("inherited", Both(stylesheet, backend => Options(backend, enabled: true)));
            Assert.AreEqual(string.Empty, Both(stylesheet, backend => Options(backend, enabled: false)));
        }

        [TestMethod]
        public void NamesAreComparedAsThePlatformComparesThem()
        {
            using Variable variable = new Variable("here");

            string found = Both(
                Sheet(Value($"count(environment-variable(lower-case('{variable.Name}')))")),
                backend => Options(backend, enabled: true));

            // Windows has one variable under any spelling of its name; everywhere else the spelling is the
            // name.
            Assert.AreEqual(OperatingSystem.IsWindows() ? "1" : "0", found);
        }

        /// <summary>Changes a variable when a document is asked for, which is mid-transformation.</summary>
        private sealed class Meddler : IXsltResolver
        {
            private readonly string m_name;

            public Meddler(string name)
            {
                m_name = name;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                Environment.SetEnvironmentVariable(m_name, "after");
                return new ResolvedResource(new StringReader("<doc/>"), href);
            }
        }

        [TestMethod]
        public void TheEnvironmentIsTheSameForTheWholeTransformation()
        {
            using Variable variable = new Variable("before");

            // Read before and after a document load that changes the variable underneath the
            // transformation: the specification asks that the answer not change within one, and it does
            // not. Each run starts from 'before', since the previous one left it at 'after'.
            XsltOptions With(XsltBackend backend)
            {
                Environment.SetEnvironmentVariable(variable.Name, "before");
                return Options(backend, enabled: true, documents: new Meddler(variable.Name));
            }

            Assert.AreEqual(
                "before,doc,before",
                Both(
                    Sheet(Value(
                        $"environment-variable('{variable.Name}'), name(document('trigger.xml')/*), "
                        + $"environment-variable('{variable.Name}')")),
                    With));
        }
    }
}
