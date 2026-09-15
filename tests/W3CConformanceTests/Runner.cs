using System.Globalization;
using System.Xml.Linq;
using CodeDeeds.Xslt;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Conformance
{
    internal enum Outcome
    {
        Passed,
        Failed,
        Skipped,
    }

    internal readonly record struct TestResult(Outcome Outcome, string Detail);

    /// <summary>
    /// Runs one QT3 test case against this engine and decides whether it passed.
    /// </summary>
    /// <remarks>
    /// The suite is written for XQuery and XPath together, and most of it is XQuery. What is applicable here
    /// is decided by each test's <c>spec</c> dependency; everything else is skipped with a reason rather than
    /// counted as a failure, so the pass rate means what it says.
    /// </remarks>
    internal sealed class Runner
    {
        private readonly Catalog m_catalog;
        private readonly Dictionary<string, XdmTree> m_documents = new(StringComparer.Ordinal);
        private readonly XsltVersion m_version;

        /// <summary>
        /// One name table for every document this driver loads.
        /// </summary>
        /// <remarks>
        /// A test comparing two documents — <c>$works/… is $staff/…</c> — walks from one tree into another,
        /// and a compiled name test holds a slot resolved against a particular table. Sharing the table
        /// means one mapping serves every tree. A transformation solves this through the runtime, which the
        /// driver has none of, evaluating expressions directly.
        /// </remarks>
        private readonly NameTable m_names = new();

        /// <summary>
        /// Where the test set being run lives, relative to the catalog, for the references inside it.
        /// </summary>
        /// <remarks>
        /// A test writes unparsed-text('parse-json/data001.json'), which is relative to the file the
        /// test is written in and not to the catalog: the same name means a different file in two
        /// test sets, so the directory has to come along with the case.
        /// </remarks>
        private string? m_testSetDirectory;

        public Runner(Catalog catalog, XsltVersion version)
        {
            m_catalog = catalog;
            m_version = version;
        }

        /// <summary>Features a test may declare that this run cannot offer.</summary>
        /// <remarks>
        /// <para>
        /// Every entry is an assertion about this engine or this driver that nothing checks, so it is worth
        /// re-reading whenever a feature lands: an entry that outlives the omission it describes goes on
        /// skipping tests, and the summary reports them as a feature not built rather than as a measurement
        /// nobody took. <c>namespace-axis</c> sat here long after the axis did, hiding nine tests.
        /// </para>
        /// <para>
        /// The first group below is the driver's limit rather than the engine's, which is why taking one out
        /// measures nothing. <c>schemaImport</c> and <c>schemaValidation</c> need an environment's schemas
        /// loaded, which this driver has no way to do. <c>higherOrderFunctions</c>, <c>fn-transform-XSLT</c>
        /// and <c>fn-transform-XSLT30</c> name <c>function-lookup()</c> and <c>transform()</c>, which the
        /// engine has inside a stylesheet: here an expression is evaluated on its own, with no
        /// <c>XsltOptions</c> and no stylesheet to carry a scope, so neither is in the static context this
        /// builds. Removing the three ran 1,676 more tests and failed 807 of them.
        /// </para>
        /// </remarks>
        private static readonly HashSet<string> s_unsupportedFeatures = new(StringComparer.Ordinal)
        {
            "schemaValidation",
            "schemaImport",
            "higherOrderFunctions",
            "fn-transform-XSLT",
            "fn-transform-XSLT30",

            "staticTyping",
            "moduleImport",
            "collection-stability",
            "directory-as-collation-uri",
            "fn-format-integer-CLDR",

            // This driver reads the suite's own files and nothing over the network.
            "remote_http",
            "non_empty_sequence_collection",
            "typedData",

            // The simple fallback is what this engine has: it reads the UCA collation URI and honours the
            // parameters CompareInfo can express, refusing the rest where fallback=no forbids them. The
            // advanced one asks for parameters ICU is not exposed for here.
            "advanced-uca-fallback",

            // An expression is compiled against a stated version here and this driver states 2.0 or 3.0, so
            // there is no mode in which format-number('foo', '#') is NaN rather than a type error. A test
            // asking for that mode is asking about a language this run is not running.
            "xpath-1.0-compatibility",

            // The place argument of format-dateTime and its two siblings, which names a civil timezone by
            // its Olson identifier: the value is shown at that place's offset, and [ZN] writes the name it
            // goes under there. The offset .NET could answer from TimeZoneInfo; the abbreviation — EST, CET
            // — it has no API for, and half of this is worse than none of it.
            "olson-timezone",
        };

        public TestResult Run(XElement testCase, XElement testSet, string? testSetDirectory = null)
        {
            m_testSetDirectory = testSetDirectory;

            if (!IsApplicable(testCase, testSet, out string? why))
            {
                return new TestResult(Outcome.Skipped, why!);
            }

            string expression = (string?)testCase.Element(Catalog.Ns + "test") ?? string.Empty;

            if (LooksLikeXQuery(expression))
            {
                return new TestResult(Outcome.Skipped, "test body is XQuery, not an XPath expression");
            }

            XElement? result = testCase.Element(Catalog.Ns + "result");
            if (result is null)
            {
                return new TestResult(Outcome.Skipped, "no result assertion");
            }

            Environment? environment = ResolveEnvironment(testCase, testSet, out string? environmentProblem);
            if (environmentProblem is not null)
            {
                return new TestResult(Outcome.Skipped, environmentProblem);
            }

            // The suite's own collations are always on offer, since a test may name one without its
            // environment declaring it; a declaration marked default puts that collation in force.
            XPathStaticContext staticContext = new XPathStaticContext
            {
                Version = m_version,
                CollationResolver = SuiteCollations.Instance,
                StaticBaseUri = StaticBaseUriFor(environment),
            };

            if (environment is not null)
            {
                foreach ((string prefix, string uri) in environment.Namespaces)
                {
                    staticContext.DeclarePrefix(prefix, uri);

                    // The empty prefix in an environment is the static context's default element/type
                    // namespace, which is what an unprefixed element name and a cast to xs:QName go to.
                    if (prefix.Length == 0)
                    {
                        staticContext.DefaultElementNamespace = uri;
                    }
                }

                foreach (XElement declaration in environment.DecimalFormats)
                {
                    DeclareDecimalFormat(staticContext, declaration);
                }

                foreach ((string uri, bool isDefault) in environment.Collations)
                {
                    if (isDefault)
                    {
                        staticContext.DefaultCollation = uri;
                    }
                }
            }

            XdmTree tree;
            XPathValue[] globals;

            try
            {
                tree = LoadContext(environment);
                globals = BindVariables(environment, staticContext);
            }
            catch (Exception exception)
            {
                return new TestResult(Outcome.Skipped, $"context document would not load: {Short(exception)}");
            }

            XPathValue value;
            string? error = null;
            string? errorCode = null;

            try
            {
                Expr compiled = XPathParser.Parse(expression, staticContext);
                int[] map = staticContext.Names.BuildFingerprintMap(tree);

                // A test whose environment names no context document has no context item, which is not the
                // same as having an empty one: XPath makes reading an absent context item an error, and a
                // good many tests are there to check that it is raised. Handing over the root of a stand-in
                // document would answer those with an empty node-set instead.
                DynamicContext context = new DynamicContext(
                    tree,
                    environment?.ContextFile is null ? DynamicContext.NotANode : XdmTree.RootNode,
                    map,
                    staticContext.Names)
                {
                    Globals = globals,
                    Collations = SuiteCollations.Instance,
                    TextLoader = environment?.BaseUriIsUndefined == true ? WithoutABase : ReadSuiteText,
                };

                value = compiled.Evaluate(ref context);
            }
            catch (XsltException exception)
            {
                value = default;
                error = exception.Message;
                errorCode = exception.Code;
            }
            catch (Exception exception)
            {
                // An exception that is not an XsltException is a defect in the engine rather than a refusal,
                // so it is reported as a failure with its type visible.
                return new TestResult(Outcome.Failed, $"unexpected {exception.GetType().Name}: {Short(exception)}");
            }

            return Assertions.Check(result, value, error, errorCode, staticContext, tree);
        }

        // ---- Applicability -------------------------------------------------------------------------------

        private bool IsApplicable(XElement testCase, XElement testSet, out string? why)
        {
            why = null;

            foreach (XElement dependency in testSet.Elements(Catalog.Ns + "dependency")
                .Concat(testCase.Elements(Catalog.Ns + "dependency")))
            {
                string type = (string?)dependency.Attribute("type") ?? string.Empty;
                string value = (string?)dependency.Attribute("value") ?? string.Empty;
                bool satisfied = (string?)dependency.Attribute("satisfied") != "false";
                bool onCase = dependency.Parent == testCase;

                switch (type)
                {
                    case "spec":
                        if (satisfied && !SpecIncludesVersion(value))
                        {
                            why = $"not an XPath {m_version} test";
                            return false;
                        }

                        break;

                    case "feature":
                        if (satisfied && s_unsupportedFeatures.Contains(value))
                        {
                            why = $"needs feature '{value}'";
                            return false;
                        }

                        break;

                    case "xml-version":
                        if (satisfied && value.Contains("1.1", StringComparison.Ordinal))
                        {
                            why = "needs XML 1.1";
                            return false;
                        }

                        break;

                    case "xsd-version":
                        // XSD 1.1 widens value spaces this engine reads by XSD 1.0 rules: the year zero,
                        // a leading plus on INF. A case that declares the dependency is asking for the
                        // 1.1 answer and would be told the 1.0 one.
                        //
                        // Only where the case declares it. A whole set carrying the dependency is saying
                        // what its subject came in with, not what each case expects: every case in
                        // xs/error.xml passes here, because xs:error is XPath 3.0's as much as XSD 1.1's,
                        // and skipping the set would hide the answers rather than the version.
                        if (onCase && satisfied && value.Trim().Contains("1.1", StringComparison.Ordinal))
                        {
                            why = "needs XSD 1.1";
                            return false;
                        }

                        break;

                    case "language":
                    case "default-language":
                        // A test wanting a language this engine writes numbers and dates in is one it should
                        // be judged on rather than excused from.
                        if (IsSpoken(value) != satisfied)
                        {
                            why = $"needs {type} '{value}'";
                            return false;
                        }

                        break;

                    case "format-integer-sequence":
                        // A numbering sequence is named by a character from it. The decimal families are all
                        // supported, because one digit pattern serves every one of them; the sequences that
                        // are a list of symbols — circled digits, Greek letters, Kanji — are not.
                        if (IsDecimalDigit(value) != satisfied)
                        {
                            why = $"needs the numbering sequence '{value}'";
                            return false;
                        }

                        break;

                    case "unicode-version":
                    case "unicode-normalization-form":
                    case "calendar":
                        why = $"needs {type} '{value}'";
                        return false;
                }
            }

            return true;
        }

        /// <summary>The primary subtags of the languages the engine spells numbers and writes dates in.</summary>
        private static readonly string[] s_spoken = { "en", "de", "fr", "es", "pt", "it", "nb", "no", "nn", "sv", "da" };

        /// <summary>Whether a language tag names one of the languages the engine has, by its primary subtag.</summary>
        private static bool IsSpoken(string language)
        {
            string primary = language.Split('-')[0];
            return Array.Exists(s_spoken, code => code.Equals(primary, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Whether a numbering sequence named by one of its characters is a family of digits.</summary>
        private static bool IsDecimalDigit(string sequence)
        {
            return sequence.Length > 0 && CharUnicodeInfo.GetDecimalDigitValue(sequence, 0) >= 0;
        }

        /// <summary>
        /// Reads a spec dependency, which lists the specifications a test applies to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A token such as <c>XP20+</c> means XPath 2.0 and later; <c>XP30+</c> excludes 2.0; <c>XQ10+</c> is
        /// XQuery only. A test with no spec dependency applies everywhere.
        /// </para>
        /// <para>
        /// Which tokens count depends on the version being run. Reading the 3.0 and 3.1 tests only when 3.1
        /// is what is being asked for is what keeps the 2.0 figure a 2.0 figure: those tests are about
        /// syntax a 2.0 expression is meant to refuse, so counting them there would measure the wrong thing
        /// twice over.
        /// </para>
        /// </remarks>
        private bool SpecIncludesVersion(string value)
        {
            // The '+' is not decoration. 'XP20' is a test about XPath 2.0 and no later version, which is
            // where the suite puts the things a later version changed its mind about: tokenize with one
            // argument is an arity error there and a function here, and string-join of integers is a type
            // error there and a string here. Reading those in the 3.1 run would measure the wrong language.
            bool thirty = m_version.CompareTo(XsltVersion.V30) >= 0;

            foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                bool applies = token switch
                {
                    "XP10+" or "XP20+" => true,
                    "XP20" => !thirty,
                    "XP30+" or "XP31+" or "XP31" => thirty,

                    // Exactly 3.0, which this engine is not: what it implements from that family is 3.1.
                    "XP30" => false,
                    _ => false,
                };

                if (applies)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Recognises an XQuery body that no XPath parser could accept, for tests that declare no spec.
        /// </summary>
        private static bool LooksLikeXQuery(string expression)
        {
            ReadOnlySpan<char> text = expression.AsSpan().TrimStart();

            return text.StartsWith("declare ") || text.StartsWith("import ") || text.StartsWith("module ")
                || text.StartsWith("xquery ") || text.StartsWith("<");
        }

        // ---- Environment ---------------------------------------------------------------------------------

        private Environment? ResolveEnvironment(XElement testCase, XElement testSet, out string? problem)
        {
            problem = null;

            XElement? declared = testCase.Element(Catalog.Ns + "environment");
            if (declared is null)
            {
                return null;
            }

            string? reference = (string?)declared.Attribute("ref");

            if (reference is not null)
            {
                if (reference == "empty")
                {
                    return null;
                }

                if (testSet.Elements(Catalog.Ns + "environment")
                        .FirstOrDefault(e => (string?)e.Attribute("name") == reference) is XElement local)
                {
                    Environment parsed = Environment.Parse(local);
                    problem = parsed.Unsupported;
                    return parsed;
                }

                if (m_catalog.Environments.TryGetValue(reference, out Environment? shared))
                {
                    problem = shared.Unsupported;
                    return shared;
                }

                problem = $"unknown environment '{reference}'";
                return null;
            }

            Environment inline = Environment.Parse(declared);
            problem = inline.Unsupported;
            return inline;
        }

        /// <summary>
        /// Declares and loads the documents an environment binds to variables.
        /// </summary>
        /// <remarks>
        /// A source with <c>role="$works"</c> is that document under that name, and a good many tests do
        /// nothing but compare two of them — <c>$works/works[1]/employee[1] is $staff/…</c>. Left unbound,
        /// they failed on the variable and said nothing about the engine.
        /// </remarks>
        /// <summary>
        /// Declares one of the environment's <c>decimal-format</c> elements against the static context.
        /// </summary>
        /// <remarks>
        /// XPath 3.0 made the decimal formats part of the static context, so these are declared the way an
        /// XSLT stylesheet declares its own and the engine reads both through one path. The prefix in a
        /// <c>name</c> is bound on the element that carries it rather than by the environment's
        /// <c>namespace</c> declarations, which is why the name is resolved against the element.
        /// </remarks>
        private static void DeclareDecimalFormat(XPathStaticContext staticContext, XElement declaration)
        {
            DecimalFormat format = new DecimalFormat();

            void Symbol(string attribute, Action<int> set)
            {
                if ((string?)declaration.Attribute(attribute) is { Length: > 0 } value)
                {
                    set(char.ConvertToUtf32(value, 0));
                }
            }

            Symbol("decimal-separator", c => format.DecimalSeparator = c);
            Symbol("grouping-separator", c => format.GroupingSeparator = c);
            Symbol("exponent-separator", c => format.ExponentSeparator = c);
            Symbol("minus-sign", c => format.MinusSign = c);
            Symbol("percent", c => format.Percent = c);
            Symbol("per-mille", c => format.PerMille = c);
            Symbol("zero-digit", c => format.ZeroDigit = c);
            Symbol("digit", c => format.Digit = c);
            Symbol("pattern-separator", c => format.PatternSeparator = c);

            if ((string?)declaration.Attribute("infinity") is string infinity)
            {
                format.Infinity = infinity;
            }

            if ((string?)declaration.Attribute("NaN") is string notANumber)
            {
                format.NaN = notANumber;
            }

            string written = (string?)declaration.Attribute("name") ?? string.Empty;

            if (DecimalFormat.TryReadName(
                written,
                prefix => declaration.GetNamespaceOfPrefix(prefix)?.NamespaceName,
                out ExpandedName name))
            {
                staticContext.DeclareDecimalFormat(name, format);
            }
        }

        private XPathValue[] BindVariables(Environment? environment, XPathStaticContext staticContext)
        {
            if (environment is null || environment.Variables.Count == 0)
            {
                return Array.Empty<XPathValue>();
            }

            XPathValue[] globals = new XPathValue[environment.Variables.Count];

            for (int slot = 0; slot < globals.Length; slot++)
            {
                (string name, string file) = environment.Variables[slot];

                staticContext.DeclareGlobalVariable(name, slot);
                globals[slot] = XPathValue.FromNodeSet(
                    NodeSet.Singleton(LoadDocument(file), XdmTree.RootNode));
            }

            return globals;
        }

        /// <summary>
        /// Refuses a relative reference where the environment declared the static base URI undefined.
        /// </summary>
        /// <remarks>
        /// There is nothing to resolve against, so there is no file to look for; the engine turns
        /// whatever a loader raises into FOUT1170, which is what the suite asks for here.
        /// </remarks>
        /// <param name="href">The reference as the test wrote it.</param>
        /// <param name="encoding">The encoding the call named, which does not come into it.</param>
        private string WithoutABase(string href, string? encoding)
        {
            return Uri.TryCreate(href, UriKind.Absolute, out _)
                ? ReadSuiteText(href, encoding)
                : throw new InvalidOperationException(
                    $"'{href}' is relative and this environment declares no static base URI.");
        }

        /// <summary>
        /// Answers <c>unparsed-text()</c> from the suite's own files, there being no transformation
        /// running to bring a resolver of its own.
        /// </summary>
        /// <param name="href">The reference as the test wrote it, relative to its test set.</param>
        /// <param name="encoding">The encoding the call named, or null for none.</param>
        private string ReadSuiteText(string href, string? encoding)
        {
            // Nothing outside the suite directory is read, and nothing over the network: a test that
            // wants either declares a dependency the driver skips on.
            if (href.Contains("://", StringComparison.Ordinal))
            {
                throw new IOException($"This driver reads the suite's own files, and not '{href}'.");
            }

            string path = Path.GetFullPath(
                Path.Combine(m_catalog.Root, m_testSetDirectory ?? string.Empty, href));

            using StreamReader reader = encoding is null
                ? new StreamReader(path)
                : new StreamReader(path, System.Text.Encoding.GetEncoding(encoding));

            return reader.ReadToEnd();
        }

        /// <summary>
        /// The base URI an expression in this test is written at.
        /// </summary>
        /// <remarks>
        /// The environment's own where it declares one, nothing where it declares the URI undefined,
        /// and otherwise the test set's own file — which is where the expression is written, and is
        /// what the specification makes the default.
        /// </remarks>
        /// <param name="environment">The environment the test runs in, if it has one.</param>
        private string? StaticBaseUriFor(Environment? environment)
        {
            if (environment?.BaseUriIsUndefined == true)
            {
                return null;
            }

            if (environment?.StaticBaseUri is string declared)
            {
                return declared;
            }

            if (m_testSetDirectory is null)
            {
                return null;
            }

            return new Uri(Path.GetFullPath(
                Path.Combine(m_catalog.Root, m_testSetDirectory) + Path.DirectorySeparatorChar))
                .AbsoluteUri;
        }

        private XdmTree LoadContext(Environment? environment)
        {
            if (environment?.ContextFile is null)
            {
                // No context document: an empty one, so that a path expression simply selects nothing.
                return XdmTreeBuilder.FromXml(new StringReader("<empty/>"), m_names);
            }

            return LoadDocument(environment.ContextFile);
        }

        /// <summary>Loads a document named by the catalog, once per run.</summary>
        private XdmTree LoadDocument(string file)
        {
            if (m_documents.TryGetValue(file, out XdmTree? cached))
            {
                return cached;
            }

            using FileStream stream = File.OpenRead(Path.Combine(m_catalog.Root, file));
            XdmTree tree = XdmTreeBuilder.FromXml(stream, m_names);
            m_documents.Add(file, tree);
            return tree;
        }

        private static string Short(Exception exception)
        {
            string message = exception.Message.Replace('\n', ' ').Replace('\r', ' ');
            return message.Length > 120 ? message[..120] : message;
        }
    }
}
