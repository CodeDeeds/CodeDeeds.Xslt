using System.Globalization;
using System.Text;
using System.Xml.Linq;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.Conformance
{
    /// <summary>What running one test case produced, for the assertions to be checked against.</summary>
    internal sealed class Transformation
    {
        /// <summary>The principal result, serialized as the stylesheet asked for it.</summary>
        public string Result { get; init; } = string.Empty;

        /// <summary>What was raised instead of a result, or null if the transformation completed.</summary>
        public string? Error { get; init; }

        /// <summary>The code the engine gave that error, where it has been given one.</summary>
        public string? ErrorCode { get; init; }

        /// <summary>Secondary results, keyed by the <c>href</c> that asked for them.</summary>
        public Dictionary<string, string> ResultDocuments { get; init; } = new(StringComparer.Ordinal);

        /// <summary>The directory the test-set lives in, which its expected-result files are relative to.</summary>
        public string Directory { get; init; } = string.Empty;

        /// <summary>The URI a result document was written to, for what base-uri() says of it.</summary>
        public string? ResultUri { get; init; }

        /// <summary>Why the test could not be presented, where something the driver cannot offer got in the way.</summary>
        public string? Unsupported { get; init; }

        /// <summary>
        /// The principal result as a tree, for an assertion that asks about it as a document rather than
        /// as text; null where the run has no schemas in scope and the question cannot arise.
        /// </summary>
        /// <remarks>
        /// A type annotation is in the tree and not in the text, so an assertion such as
        /// <c>not(/* instance of element(*, xs:untyped))</c> cannot be answered from serialized output at
        /// all: everything parsed back out of XML is untyped. Asked for only when an assertion wants it,
        /// and it costs a second run of the transformation, which is why it is offered rather than kept.
        /// </remarks>
        public Func<XdmTree?>? Tree { get; init; }

        /// <summary>
        /// The schemas the environment declared, for an assertion that names one of their declarations:
        /// schema-element(E) in an assertion is a question the assertion cannot ask without them.
        /// </summary>
        public System.Xml.Schema.XmlSchemaSet? Schemas { get; init; }
    }

    /// <summary>
    /// Runs one test case of the W3C XSLT 3.0 suite against this engine and decides whether it passed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The QT3 driver beside this one measures expressions; this one measures the language around them —
    /// template dispatch, modes, grouping, sorting, secondary results, serialization. That half has had no
    /// conformance coverage at all, because the differential tests against
    /// <c>System.Xml.Xsl.XslCompiledTransform</c> can only speak for the XSLT 1.0 subset.
    /// </para>
    /// <para>
    /// Same posture as the QT3 driver: a test this driver cannot present fairly is skipped with a reason
    /// rather than counted either way, so the pass rate is over tests genuinely attempted. The two figures
    /// are not comparable with each other and are not meant to be.
    /// </para>
    /// </remarks>
    internal sealed class Xslt30Runner
    {
        private readonly Xslt30Catalog m_catalog;
        private readonly XsltVersion m_version;
        private readonly XsltBackend m_backend;
        private readonly bool m_schemaAware;

        public Xslt30Runner(Xslt30Catalog catalog, XsltVersion version, XsltBackend backend, bool schemaAware = false)
        {
            m_catalog = catalog;
            m_version = version;
            m_backend = backend;
            m_schemaAware = schemaAware;
        }

        /// <summary>Features a test may declare that this engine does not have.</summary>
        /// <remarks>
        /// <para>
        /// Read in both directions: a test declaring a feature this engine lacks is skipped, and so is a test
        /// declaring <c>satisfied="false"</c> on one it has. A feature absent from this set is one the engine
        /// claims, so a test asking for it is judged rather than excused.
        /// </para>
        /// <para>
        /// Every entry is therefore an assertion about the engine that nothing checks, and it is worth
        /// re-reading whenever a feature lands. <c>namespace_axis</c> stayed here long after the axis was
        /// built, and taking it out brought 71 tests in: 59 passed at once and 12 were real defects the suite
        /// had been ready to report all along.
        /// </para>
        /// </remarks>
        private static readonly HashSet<string> s_absentFeatures = new(StringComparer.Ordinal)
        {
            "schema_aware",
            "XSD_1.1",
            "streaming",
            "streaming-fallback",
            "XML_1.1",
            "xsl-stylesheet-processing-instruction",
        };

        public TestResult Run(XElement testCase, XElement testSet, string directory)
        {
            if (!IsApplicable(testCase, testSet, out string? why))
            {
                return new TestResult(Outcome.Skipped, why!);
            }

            XElement? result = testCase.Element(Xslt30Catalog.Ns + "result");
            XElement? test = testCase.Element(Xslt30Catalog.Ns + "test");

            if (result is null || test is null)
            {
                return new TestResult(Outcome.Skipped, "the test case has no test or no result");
            }

            // A package test names its principal module as <package role="principal"> rather than as
            // <stylesheet>. The two are the same thing to the engine — a package is a module whose outermost
            // element happens to be xsl:package — so the driver reads it the same way and lets the library
            // packages the environment declares be found by name.
            List<XElement> principals = test.Elements(Xslt30Catalog.Ns + "package")
                .Where(package => (string?)package.Attribute("role") != "secondary")
                .ToList();

            if (principals.Count > 1)
            {
                return new TestResult(Outcome.Skipped, "the test names several principal packages");
            }

            // XSLT 3.0 has three ways in — a source document, a named template, a named function. The first
            // two are entry points this engine offers; a test starting at a named function is skipped rather
            // than approximated, which would answer a different question.
            if (test.Element(Xslt30Catalog.Ns + "initial-function") is not null)
            {
                return new TestResult(Outcome.Skipped, "the test starts at a named function");
            }

            Xslt30Environment? environment = ResolveEnvironment(testCase, testSet, out string? problem);
            if (problem is not null)
            {
                return new TestResult(Outcome.Skipped, problem);
            }

            List<XElement> stylesheets = test.Elements(Xslt30Catalog.Ns + "stylesheet").ToList();
            string? stylesheetFile = principals.Count == 1
                ? (string?)principals[0].Attribute("file")
                : stylesheets.Count switch
                {
                    1 => (string?)stylesheets[0].Attribute("file"),
                    0 => environment?.StylesheetFile,
                    _ => null,
                };

            if (stylesheetFile is null)
            {
                return new TestResult(
                    Outcome.Skipped,
                    stylesheets.Count > 1 ? "the test names several stylesheets" : "the test names no stylesheet");
            }

            string stylesheetPath = Path.Combine(directory, stylesheetFile.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(stylesheetPath))
            {
                return new TestResult(Outcome.Skipped, "the stylesheet named is not in the suite");
            }

            string? source;

            try
            {
                source = LoadSource(environment, directory);
            }
            catch (Exception exception)
            {
                return new TestResult(Outcome.Skipped, $"source document would not load: {Short(exception)}");
            }

            // A test with neither a source document nor an entry point still has something to say when what
            // it expects is an error: a static error is raised by compiling the stylesheet, and compiling it
            // is exactly what this driver can do without either. Thirteen of the xsl:expose tests are that
            // shape — they name the error and leave it there, because nothing needs to run for it to be
            // raised. Presenting them as compile-only cannot manufacture a pass: compiling either raises the
            // code the test names or it does not.
            bool expectsError = result.Descendants(Xslt30Catalog.Ns + "error").Any();

            // Unless the stylesheet says where to begin itself. XSLT 3.0 lets one declare a template called
            // xsl:initial-template and be run with nothing else supplied, and a test written that way has a
            // whole transformation to offer rather than only a compilation — which is what the merge tests
            // for current-merge-group() are, and they were being answered by not running them.
            bool startsItself = DeclaresInitialTemplate(stylesheetPath);

            // Or a package it uses does: a library may declare xsl:initial-template and have it accepted
            // into the principal package, which is how the accept tests reach an absent component.
            foreach (XElement package in test.Elements(Xslt30Catalog.Ns + "package"))
            {
                if (!startsItself
                    && (string?)package.Attribute("role") == "secondary"
                    && (string?)package.Attribute("file") is string library)
                {
                    string path = Path.Combine(directory, library.Replace('/', Path.DirectorySeparatorChar));

                    startsItself = File.Exists(path) && DeclaresInitialTemplate(path);
                }
            }

            // A named mode is an entry point too — one that asks for templates to be applied, which with no
            // source document is exactly the error some tests are written to reach. One that brings its own
            // selection to apply them to is an initial match selection, which the engine takes as an
            // expression.
            bool named = test.Element(Xslt30Catalog.Ns + "initial-template") is not null
                || test.Element(Xslt30Catalog.Ns + "initial-mode") is not null;

            if (source is null && !named && !startsItself && !expectsError)
            {
                return new TestResult(Outcome.Skipped, "the test supplies no source document and names no entry point");
            }

            // A test whose assertion is written against the result as a value — an output element naming a
            // result-var — asks for what this driver cannot present, for the reason every assertion about a
            // typed sequence is skipped: a transformation here writes a document, and the items are text by
            // the time anything can look at them. A bare tree="no" says only that the result is not a
            // document node, which is presentable exactly as it stands.
            if (test.Element(Xslt30Catalog.Ns + "output")?.Attribute("result-var") is not null)
            {
                return new TestResult(
                    Outcome.Skipped, "the test asks for the result as a value rather than as a document");
            }

            // A test expecting an error is run rather than only compiled: with nothing to start at, the error
            // it expects may be the engine's refusal to start, which compiling alone never reaches.
            bool compileOnly = source is null && !named && !startsItself && !expectsError;

            if (TransformWithin(stylesheetPath, source, test, environment, directory, compileOnly)
                is not Transformation outcome)
            {
                return new TestResult(
                    Outcome.Skipped,
                    $"the transformation had not finished after {s_limit.TotalSeconds:F0} seconds");
            }

            return outcome.Unsupported is string unsupported
                ? new TestResult(Outcome.Skipped, unsupported)
                : Xslt30Assertions.Check(result.Elements().First(), outcome);
        }

        // ---- Running -------------------------------------------------------------------------------------

        /// <summary>How long one transformation is given before the run goes on without it.</summary>
        private static readonly TimeSpan s_limit = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Runs a transformation, abandoning it if it takes longer than the driver is willing to wait.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The specification sets no time bound, so a test that hits this is skipped with the wait named
        /// rather than counted as wrong: what ran out was the driver's patience, not the engine's
        /// correctness. It exists because otherwise one test takes the whole suite hostage.
        /// <c>function-1031</c> is that test — <c>x:fib(92)</c> declared <c>cache="yes"</c>, a request an
        /// implementation is free to ignore, and ignoring it makes the call some ten quintillion
        /// invocations. The 3.0 run did not terminate at all before this.
        /// </para>
        /// <para>
        /// The thread is abandoned rather than stopped, there being no safe way to stop one from outside, so
        /// it runs until the process exits — which is what makes it a background thread. That costs a core
        /// for the rest of the run, and the rest of the run is eleven thousand other tests.
        /// </para>
        /// </remarks>
        /// <returns>What the transformation produced, or null if it had not finished in time.</returns>
        private Transformation? TransformWithin(
            string stylesheetPath,
            string? source,
            XElement test,
            Xslt30Environment? environment,
            string directory,
            bool compileOnly = false)
        {
            Transformation? outcome = null;

            Thread worker = new Thread(
                () => outcome = Transform(stylesheetPath, source, test, environment, directory, compileOnly))
            {
                IsBackground = true,
            };

            worker.Start();

            return worker.Join(s_limit) ? outcome : null;
        }

        private Transformation Transform(
            string stylesheetPath,
            string? source,
            XElement test,
            Xslt30Environment? environment,
            string directory,
            bool compileOnly = false)
        {
            ResultCollector results = new ResultCollector();
            StringWriter output = new StringWriter();
            Xslt? stylesheet = null;

            try
            {
                Dictionary<string, object?> parameters = new(StringComparer.Ordinal);

                foreach (XElement declaration in (environment?.Parameters ?? Enumerable.Empty<XElement>())
                    .Concat(test.Elements(Xslt30Catalog.Ns + "param")))
                {
                    Declare(parameters, declaration);
                }

                // The param children of an entry point are the arguments of the call the transformation
                // starts with rather than stylesheet parameters, and a tunnel one among them is meant for
                // everything below it as well. Both ways in carry them: XSLT 3.0 gives the caller the same
                // two sets of parameters whether the transformation begins at a named template or by
                // applying templates in a mode, and reading them only from the initial-template left the
                // mode's declared and then never supplied.
                Dictionary<string, object?> template = new(StringComparer.Ordinal);
                Dictionary<string, object?> tunnel = new(StringComparer.Ordinal);

                foreach (string way in new[] { "initial-template", "initial-mode" })
                {
                    foreach (XElement declaration in
                        test.Element(Xslt30Catalog.Ns + way)?.Elements(Xslt30Catalog.Ns + "param")
                            ?? Enumerable.Empty<XElement>())
                    {
                        Declare(
                            (string?)declaration.Attribute("tunnel") == "yes" ? tunnel : template,
                            declaration);
                    }
                }

                // Schema-aware runs tell the resolver which documents the environment validates, so that a
                // stylesheet reading two of them gets each as the catalog declares it.
                SuiteResolver resolver = new SuiteResolver(m_catalog.Root)
                {
                    Validated = m_schemaAware && environment is not null
                        ? new HashSet<string>(environment.ValidatedFiles, StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                };

                // A library package may be declared on the environment or beside the principal one in the
                // test itself, and most tests use the second. Reading only the environment left every one of
                // those saying no package resolver was configured, which reads as a driver that cannot do
                // packages rather than as a driver that did not look in both places.
                List<(string Uri, string? Version, string File)> libraries = new();

                if (environment is not null)
                {
                    libraries.AddRange(environment.Packages);
                }

                foreach (XElement package in test.Elements(Xslt30Catalog.Ns + "package"))
                {
                    if ((string?)package.Attribute("file") is not string file)
                    {
                        continue;
                    }

                    // A secondary package is usually declared with the name it answers to, and sometimes
                    // with only its file — the name being written inside it, on the xsl:package element,
                    // where the engine will read it anyway. Reading it here too is what lets that package be
                    // found; thirteen tests had been failing as "no package resolver was configured".
                    string? uri = (string?)package.Attribute("uri") ?? PackageNameIn(directory, file);

                    if (uri is not null)
                    {
                        libraries.Add((uri, (string?)package.Attribute("package-version"), file));
                    }
                }

                XsltOptions options = new XsltOptions
                {
                    // Rooted at the suite rather than at the test-set's own directory: a stylesheet imports
                    // from a shared directory often enough, and the containment check is the point of the
                    // root rather than the exact depth of it.
                    StylesheetResolver = resolver,
                    DocumentResolver = resolver,
                    // The collections the environment declares, as lists of files the suite resolver then
                    // reads; with none declared there is no collection, which is what the tests asking
                    // for one anyway expect to hear.
                    CollectionResolver = environment is { Collections.Count: > 0 }
                        ? new CatalogCollections(environment.Collections, directory)
                        : null,
                    // The suite's own case-blind collation, which a test names by URI and expects the
                    // driver to supply; everything else an environment declares the engine provides.
                    CollationResolver = SuiteCollations.Instance,
                    // Schema-aware where the run asks: the schemas the environment declares are in scope,
                    // and anything a stylesheet imports by location is read from the suite.
                    SchemaAware = m_schemaAware,
                    SchemaResolver = m_schemaAware ? resolver : null,
                    Schemas = m_schemaAware ? EnvironmentSchemas(environment, directory) : null,
                    // The catalog says which sources are validated, and a validated source is a typed one.
                    InputValidation = m_schemaAware && environment is { ValidatesSources: true }
                        ? XsltValidation.Strict
                        : XsltValidation.Strip,
                    // A source given inline has no URI of its own, so what its declaration names resolves
                    // against the test set's directory, as the catalog means it to.
                    EntityResolver = new EntityResolverWithin(resolver, directory),
                    BaseUri = new Uri(stylesheetPath).AbsoluteUri,
                    ResultResolver = results,
                    MessageWriter = TextWriter.Null,
                    Parameters = parameters,
                    TemplateParameters = template,
                    TunnelParameters = tunnel,
                    PackageResolver = libraries.Count > 0 ? new PackageLibrary(libraries, directory) : null,

                    // The base output URI is what the test's output element names, relative to the test's
                    // directory; absent, the directory itself; "#absent", none at all.
                    BaseOutputUri = BaseOutputUriOf(test, directory),

                    // The version the run is measuring, so that a 3.0 stylesheet is read as 3.0 here and
                    // forwards-compatibly in the 2.0 run. That difference is what the two runs exist to
                    // measure, and it was previously not reaching the engine at all.
                    Version = m_version,

                    // Which backend evaluates the expressions. Both must reach the same verdict on every
                    // test: what the emitted one cannot express it hands back to the interpreter, so the
                    // suite is checking the part that was emitted and nothing else.
                    Backend = m_backend,

                    InitialTemplate = EntryPoint(test, "initial-template"),
                    InitialMode = EntryPoint(test, "initial-mode"),

                    // What templates are first applied to, where the test says: the initial-mode's own
                    // selection, or the environment's selection within the source document.
                    InitialMatchSelection = (string?)test.Element(Xslt30Catalog.Ns + "initial-mode")?.Attribute("select")
                        ?? environment?.SourceSelect,

                    // Where the input came from, which is not where the stylesheet is.
                    InputUri = SourcePath(environment, directory) is string path && File.Exists(path)
                        ? new Uri(path).AbsoluteUri
                        : environment?.SourceBaseUri,
                };

                // The bytes, so that a stylesheet declaring itself ISO-8859-1 is read as one.
                stylesheet = new Xslt(File.OpenRead(stylesheetPath), options);

                if (compileOnly)
                {
                    // Compiling is the whole of the question. Running would need a source document or an
                    // entry point, and the test supplied neither.
                }
                else if (source is null)
                {
                    stylesheet.Transform(output);
                }
                else
                {
                    stylesheet.TransformXml(source, output);
                }
            }
            catch (XsltException exception)
            {
                return new Transformation
                {
                    Error = exception.Message,
                    ErrorCode = exception.Code,
                    Directory = directory,
                };
            }
            catch (System.Xml.XmlException exception)
            {
                // The source document would not parse, which is a property of the test rather than of the
                // engine.
                return new Transformation
                {
                    Unsupported = $"the source document would not parse: {Short(exception)}",
                    Directory = directory,
                };
            }
            catch (Exception exception)
            {
                // Not a refusal but a defect, so it is reported with its type visible rather than folded in
                // with the errors the specification asks for.
                return new Transformation
                {
                    Error = $"unexpected {exception.GetType().Name}: {Short(exception)}",
                    Directory = directory,
                };
            }

            // Offered rather than taken: a schema-aware run is the only one where the result can carry
            // annotations, and the second transformation it costs is paid only by an assertion that asks.
            Xslt compiled = stylesheet!;

            return new Transformation
            {
                Result = output.ToString(),
                ResultDocuments = results.Documents,
                Directory = directory,
                Tree = m_schemaAware && !compileOnly ? () => RunAgainIntoATree(compiled, source) : null,
                Schemas = m_schemaAware ? EnvironmentSchemas(environment, directory) : null,
            };
        }


        /// <summary>
        /// Runs the transformation a second time into a tree, for an assertion that asks about the result
        /// as a document rather than as text.
        /// </summary>
        private static XdmTree? RunAgainIntoATree(Xslt stylesheet, string? source)
        {
            try
            {
                return source is null ? stylesheet.TransformToTree() : stylesheet.TransformXmlToTree(source);
            }
            catch (Exception)
            {
                // The first run produced a result, so this one should too; where it does not, the
                // assertion falls back to the serialized text rather than the test failing on the driver.
                return null;
            }
        }

        /// <summary>
        /// Whether a stylesheet declares the template named <c>xsl:initial-template</c>, under whatever prefix
        /// it binds to the XSLT namespace — a test written with <c>t:</c> declares it as surely as one
        /// written with <c>xsl:</c>, and reading the text for the usual spelling missed those.
        /// </summary>
        private static bool DeclaresInitialTemplate(string path)
        {
            XNamespace xsl = "http://www.w3.org/1999/XSL/Transform";

            try
            {
                XDocument stylesheet = XDocument.Load(path);

                foreach (XElement template in stylesheet.Descendants(xsl + "template"))
                {
                    if ((string?)template.Attribute("name") is not string name)
                    {
                        continue;
                    }

                    int colon = name.IndexOf(':');
                    string local = colon < 0 ? name : name[(colon + 1)..];

                    // An unprefixed name is in no namespace, whatever the default namespace says: a template
                    // called initial-template is not the entry point, only xsl:initial-template is.
                    XNamespace? bound = colon < 0 ? XNamespace.None : template.GetNamespaceOfPrefix(name[..colon]);

                    if (local == "initial-template"
                        && (name.StartsWith("Q{http://www.w3.org/1999/XSL/Transform}", StringComparison.Ordinal)
                            || bound == xsl))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (System.Xml.XmlException)
            {
                return File.ReadAllText(path).Contains("name=\"xsl:initial-template\"", StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Reads the name of an entry point the test names, in the notation the engine takes.
        /// </summary>
        /// <remarks>
        /// The catalog writes a QName, so a prefix on it is resolved against the catalog's own namespaces —
        /// which is where <c>xsl:initial-template</c> gets its meaning. The engine takes a name without a
        /// prefix or an expanded one, for the reason it does everywhere: the prefixes in scope belong to the
        /// stylesheet and a caller has no way to know them.
        /// </remarks>
        private static string? EntryPoint(XElement test, string element)
        {
            if (test.Element(Xslt30Catalog.Ns + element) is not XElement named
                || (string?)named.Attribute("name") is not string name)
            {
                return null;
            }

            int colon = name.IndexOf(':');
            if (colon < 0)
            {
                return name;
            }

            XNamespace? uri = named.GetNamespaceOfPrefix(name[..colon]);

            return uri is null ? name : $"{{{uri.NamespaceName}}}{name[(colon + 1)..]}";
        }

        /// <summary>Reads one <c>param</c> declaration into the value the transformation binds it to.</summary>
        /// <remarks>
        /// The <c>select</c> is an XPath expression rather than a literal, so it is evaluated — by the same
        /// engine, which is not circular here: the parameter is an input to the test and not the thing the
        /// test is about.
        /// </remarks>
        private void Declare(Dictionary<string, object?> parameters, XElement declaration)
        {
            string? name = (string?)declaration.Attribute("name");
            string? select = (string?)declaration.Attribute("select");

            if (name is null || select is null)
            {
                return;
            }

            // A prefixed name is resolved against the element that wrote it — the suite declares the prefix
            // on the param itself — and handed over expanded, which is the only form the engine takes: the
            // caller's prefixes are not the stylesheet's, and there would be nothing to resolve them against.
            if (name.IndexOf(':', StringComparison.Ordinal) is int colon && colon > 0)
            {
                if (declaration.GetNamespaceOfPrefix(name[..colon]) is not XNamespace bound)
                {
                    return;
                }

                name = "{" + bound.NamespaceName + "}" + name[(colon + 1)..];
            }

            // A declared type is applied by constructing the value in it, which is what the engine would do
            // with a parameter it read itself: a test supplying 111 as an xs:string means the string.
            if ((string?)declaration.Attribute("as") is string declared
                && System.Text.RegularExpressions.Regex.IsMatch(declared.Trim(), "^xs:[A-Za-z]+$"))
            {
                select = declared.Trim() + "(" + select + ")";
            }

            XPath.XPathStaticContext staticContext = new XPath.XPathStaticContext { Version = m_version };
            XPath.Expr compiled = XPath.XPathParser.Parse(select, staticContext);
            Model.XdmTree tree = Model.XdmTreeBuilder.FromXml("<empty/>", false);

            Runtime.DynamicContext context = new Runtime.DynamicContext(
                tree,
                Runtime.DynamicContext.NotANode,
                staticContext.Names.BuildFingerprintMap(tree),
                staticContext.Names);

            parameters[name] = compiled.Evaluate(ref context);
        }

        /// <summary>Serves the suite's stylesheets and documents, addressed as URIs.</summary>
        /// <remarks>
        /// <see cref="FileResolver"/> would do, and does everywhere else, but it addresses by path. This
        /// suite asks what <c>base-uri()</c> and <c>static-base-uri()</c> answer, and resolves
        /// <c>document('')</c> against the stylesheet that wrote it — neither of which a path can answer, one
        /// because it is not a URI and the other because an empty reference relative to a directory is that
        /// directory. So this resolves URIs and hands back the absolute one as the identity, which is what
        /// the specification means by the base URI of a stylesheet module.
        /// </remarks>
        /// <summary>
        /// Finds a library package by the name an <c>xsl:use-package</c> asked for.
        /// </summary>
        /// <remarks>
        /// The catalog says which file holds which package name, so this is a lookup in that map rather than
        /// a path resolution — which is exactly the distinction that makes the engine's package resolver a
        /// separate thing from its stylesheet resolver.
        /// </remarks>
        /// <summary>The name a package file declares for itself, or null where it cannot be read.</summary>
        /// <param name="directory">The test set's directory.</param>
        /// <param name="file">The package file, relative to it.</param>
        /// <summary>The base output URI a test asks for through its output element, if any.</summary>
        private static string? BaseOutputUriOf(XElement test, string directory)
        {
            string? file = (string?)test.Element(Xslt30Catalog.Ns + "output")?.Attribute("file");

            if (file == "#absent")
            {
                return null;
            }

            string root = directory.EndsWith(Path.DirectorySeparatorChar) ? directory : directory + Path.DirectorySeparatorChar;

            return string.IsNullOrEmpty(file)
                ? new Uri(root).AbsoluteUri
                : new Uri(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))).AbsoluteUri;
        }

        private static string? PackageVersionIn(string directory, string file)
        {
            string path = Path.Combine(directory, file.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                return File.Exists(path) ? (string?)XDocument.Load(path).Root?.Attribute("package-version") : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? PackageNameIn(string directory, string file)
        {
            string path = Path.Combine(directory, file.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                return File.Exists(path) ? (string?)XDocument.Load(path).Root?.Attribute("name") : null;
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }

        private sealed class PackageLibrary : IXsltPackageResolver
        {
            private readonly List<(string Uri, string? Version, string File)> m_packages = new();
            private readonly string m_directory;

            public PackageLibrary(IEnumerable<(string Uri, string? Version, string File)> packages, string directory)
            {
                m_directory = directory;

                // The version is read off the package itself, which declares it on its xsl:package element
                // and is the authority; the catalog's attribute stands in where the package says nothing,
                // and one test's catalog disagrees with its package.
                foreach ((string uri, string? version, string file) in packages)
                {
                    string? declared = PackageVersionIn(directory, file) ?? version;
                    m_packages.Add((uri, declared, file));

                    // And under the name the package gives itself, where the catalog called it something
                    // else: one environment lists a package under a name no xsl:use-package asks for.
                    if (PackageNameIn(directory, file) is string own && own != uri)
                    {
                        m_packages.Add((own, declared, file));
                    }
                }
            }

            public IReadOnlyList<string> VersionsOf(string name)
            {
                List<string> versions = new List<string>();

                foreach ((string uri, string? version, _) in m_packages)
                {
                    if (uri == name && version is not null)
                    {
                        versions.Add(version);
                    }
                }

                return versions;
            }

            public ResolvedResource? Resolve(string name, string? version)
            {
                string? file = null;

                foreach ((string uri, string? offered, string candidate) in m_packages)
                {
                    if (uri == name && (version is null || offered == version))
                    {
                        file = candidate;
                        break;
                    }
                }

                if (file is null)
                {
                    return null;
                }

                string path = Path.Combine(m_directory, file.Replace('/', Path.DirectorySeparatorChar));

                return File.Exists(path)
                    ? new ResolvedResource(new StreamReader(path), new Uri(path).AbsoluteUri)
                    : null;
            }
        }

        /// <summary>
        /// Resolves what a document type declaration names against a directory where the document itself
        /// has no base — a source the catalog gives inline.
        /// </summary>
        private sealed class EntityResolverWithin : IXsltResolver
        {
            private readonly IXsltResolver m_inner;
            private readonly string m_directory;

            public EntityResolverWithin(IXsltResolver inner, string directory)
            {
                m_inner = inner;
                m_directory = new Uri(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar).AbsoluteUri;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_inner.Resolve(href, baseUri ?? m_directory);
            }
        }

        /// <summary>
        /// Serves the collections a catalog environment declares: each a list of files under a name, which a
        /// stylesheet asks for relative to the test set's directory.
        /// </summary>
        private sealed class CatalogCollections : IXsltCollectionResolver
        {
            private readonly Uri m_directory;
            private readonly Dictionary<string, IReadOnlyList<string>> m_named = new(StringComparer.Ordinal);
            private IReadOnlyList<string>? m_default;

            public CatalogCollections(List<(string? Uri, List<string> Files)> collections, string directory)
            {
                m_directory = new Uri(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar);

                foreach ((string? uri, List<string> files) in collections)
                {
                    // Identified as the suite resolver identifies a file, so that what is handed back is
                    // what it then reads.
                    string[] members = files.Select(FileUri).ToArray();

                    if (uri is null)
                    {
                        m_default = members;
                    }
                    else
                    {
                        m_named[new Uri(m_directory, uri).AbsoluteUri] = members;
                    }
                }
            }

            /// <summary>
            /// A file's URI under the test set's directory, with a fragment identifier kept as one.
            /// </summary>
            /// <remarks>
            /// <see cref="Uri"/> takes a <c>#</c> in a file reference as part of the file's name and escapes
            /// it, since a file may be called that; the catalog means the element the fragment names, as
            /// <c>collection-004</c>'s <c>doc15.xml#frag2</c> does, so the fragment is set aside and put back.
            /// </remarks>
            private string FileUri(string file)
            {
                int hash = file.IndexOf('#');
                string uri = new Uri(m_directory, hash < 0 ? file : file[..hash]).AbsoluteUri;

                return hash < 0 ? uri : uri + file[hash..];
            }

            public IReadOnlyList<string>? ResolveCollection(string? uri, string? baseUri)
            {
                if (uri is null)
                {
                    return m_default;
                }

                Uri against = baseUri is null ? m_directory : new Uri(baseUri);

                return Uri.TryCreate(against, uri, out Uri? asked)
                    && m_named.TryGetValue(asked.AbsoluteUri, out IReadOnlyList<string>? members)
                    ? members
                    : null;
            }
        }

        private sealed class SuiteResolver : IXsltResolver
        {
            private readonly Uri m_root;

            public SuiteResolver(string rootDirectory)
            {
                m_root = new Uri(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory))
                    + Path.DirectorySeparatorChar);
            }

            /// <summary>
            /// The documents the environment declares <c>validation="strict"</c>, by file name. Validation
            /// is declared per source, so an environment may validate one document a stylesheet reads and
            /// not another, and this is how the engine is told which.
            /// </summary>
            public HashSet<string> Validated { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                Uri baseline = baseUri is null ? m_root : new Uri(baseUri);

                if (!Uri.TryCreate(baseline, href, out Uri? resolved)
                    || !resolved.IsFile
                    || !m_root.IsBaseOf(resolved)
                    || !File.Exists(resolved.LocalPath))
                {
                    return null;
                }

                return new ResolvedResource(
                    new StreamReader(resolved.LocalPath, detectEncodingFromByteOrderMarks: true),
                    resolved.AbsoluteUri)
                {
                    Validation = Validated.Contains(Path.GetFileName(resolved.LocalPath))
                        ? XsltValidation.Strict
                        : null,
                };
            }
        }

        /// <summary>Collects what <c>xsl:result-document</c> writes, keyed by the href that asked for it.</summary>
        private sealed class ResultCollector : IXsltResultResolver
        {
            private readonly Dictionary<string, StringWriter> m_writers = new(StringComparer.Ordinal);

            public Dictionary<string, string> Documents =>
                m_writers.ToDictionary(entry => entry.Key, entry => entry.Value.ToString(), StringComparer.Ordinal);

            public TextWriter Resolve(string href, string? baseUri)
            {
                if (!m_writers.TryGetValue(href, out StringWriter? writer))
                {
                    writer = new StringWriter();
                    m_writers[href] = writer;
                }

                return writer;
            }
        }

        // ---- The environment -----------------------------------------------------------------------------

        /// <summary>
        /// The schemas an environment declares, loaded into a set of their own, or null where it declares
        /// none. What <c>xsl:import-schema namespace="..."</c> with no location finds.
        /// </summary>
        private static System.Xml.Schema.XmlSchemaSet? EnvironmentSchemas(Xslt30Environment? environment, string directory)
        {
            if (environment is null || environment.Schemas.Count == 0)
            {
                return null;
            }

            System.Xml.Schema.XmlSchemaSet set = new System.Xml.Schema.XmlSchemaSet
            {
                XmlResolver = new System.Xml.XmlUrlResolver(),
            };

            foreach ((string file, string? _) in environment.Schemas)
            {
                set.Add(null, Path.Combine(directory, file));
            }

            set.Compile();
            return set;
        }

        private Xslt30Environment? ResolveEnvironment(
            XElement testCase, XElement testSet, out string? problem)
        {
            problem = null;

            XElement? reference = testCase.Element(Xslt30Catalog.Ns + "environment");
            if (reference is null)
            {
                return null;
            }

            Xslt30Environment environment;

            if ((string?)reference.Attribute("ref") is string name)
            {
                XElement? declared = testSet.Elements(Xslt30Catalog.Ns + "environment")
                    .FirstOrDefault(element => (string?)element.Attribute("name") == name);

                if (declared is null)
                {
                    problem = "the environment the test names is not in the test-set";
                    return null;
                }

                environment = Xslt30Environment.Parse(declared);
            }
            else
            {
                environment = Xslt30Environment.Parse(reference);
            }

            // A schema is something to declare only to a schema-aware run; to the other it is a reason to
            // stand aside, since the test is asking typed questions.
            problem = environment.Unsupported
                ?? (environment.Schemas.Count > 0 && !m_schemaAware ? "environment declares a schema" : null);

            return environment;
        }

        /// <summary>
        /// Reads a source document, in the encoding the document says it is in.
        /// </summary>
        /// <remarks>
        /// The bytes are what the file holds, and an XML document is self-describing about what encoding
        /// they are in. Read as UTF-8, a Latin-1 byte above 0x7F becomes a replacement character and the
        /// test is decided by the driver's mistake rather than by the stylesheet: <c>insn/attribute</c>'s
        /// 0301 writes <c>père</c> through the HTML output method and asks for it back percent-escaped,
        /// which says nothing at all unless the character survived being read. The engine's string entry
        /// point takes text that has already been decoded, so the decoding belongs here.
        /// </remarks>
        /// <param name="environment">The environment naming the source, or holding it inline.</param>
        /// <param name="directory">The test set's directory, which a relative name is under.</param>
        private static string? LoadSource(Xslt30Environment? environment, string directory)
        {
            if (environment?.SourceContent is string inline)
            {
                return inline;
            }

            if (SourcePath(environment, directory) is not string path)
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);

            if (DeclaredEncodingOf(bytes) is not System.Text.Encoding declared)
            {
                return File.ReadAllText(path);
            }

            return declared.GetString(bytes).TrimStart('﻿');
        }

        /// <summary>
        /// The encoding a document's XML declaration names, or null where it names none.
        /// </summary>
        /// <remarks>
        /// Only the declaration is read, and a declaration is ASCII by definition — so decoding the first
        /// few bytes as Latin-1 to find it cannot go wrong whatever the rest of the document is in.
        /// </remarks>
        /// <param name="bytes">The document's bytes.</param>
        private static System.Text.Encoding? DeclaredEncodingOf(byte[] bytes)
        {
            string head = System.Text.Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 200));
            int close = head.IndexOf("?>", StringComparison.Ordinal);
            int at = head.IndexOf("encoding=", StringComparison.Ordinal);

            if (at < 0 || close < 0 || at > close)
            {
                return null;
            }

            int open = at + "encoding=".Length;

            if (open >= head.Length || head[open] is not ('"' or '\''))
            {
                return null;
            }

            int end = head.IndexOf(head[open], open + 1);

            if (end < 0)
            {
                return null;
            }

            try
            {
                return System.Text.Encoding.GetEncoding(head[(open + 1)..end]);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Where the source document is, or null where the environment wrote its content inline.
        /// </summary>
        /// <remarks>
        /// Told to the engine as well as read from, because a reference written in the source resolves
        /// against the source and not against the stylesheet. The two are usually different directories:
        /// <c>misc/catalog</c> runs a stylesheet over the suite's own catalogue, whose entries are relative
        /// to the suite root, and reading them from beside the stylesheet finds nothing at all.
        /// </remarks>
        /// <param name="environment">The environment naming the source.</param>
        /// <param name="directory">The test set's directory, which a relative name is under.</param>
        private static string? SourcePath(Xslt30Environment? environment, string directory)
        {
            return environment?.SourceFile is string file
                ? Path.Combine(directory, file.Replace('/', Path.DirectorySeparatorChar))
                : null;
        }

        // ---- Applicability -------------------------------------------------------------------------------

        private bool IsApplicable(XElement testCase, XElement testSet, out string? why)
        {
            why = null;

            IEnumerable<XElement> dependencies = testSet.Elements(Xslt30Catalog.Ns + "dependencies")
                .Concat(testCase.Elements(Xslt30Catalog.Ns + "dependencies"))
                .SelectMany(element => element.Elements());

            foreach (XElement dependency in dependencies)
            {
                string kind = dependency.Name.LocalName;
                string value = (string?)dependency.Attribute("value") ?? string.Empty;
                bool wanted = (string?)dependency.Attribute("satisfied") != "false";

                switch (kind)
                {
                    case "spec":
                        if (wanted != SpecIncludesVersion(value))
                        {
                            why = $"not an XSLT {m_version} test";
                            return false;
                        }

                        break;

                    case "feature":
                        // schema_aware is absent unless the run was asked to be schema-aware.
                        if ((s_absentFeatures.Contains(value) && !(m_schemaAware && value == "schema_aware")) == wanted)
                        {
                            why = wanted ? $"needs feature '{value}'" : $"needs feature '{value}' to be absent";
                            return false;
                        }

                        break;

                    case "languages_for_numbering":
                        if (SpellsNumbersIn(value) != wanted)
                        {
                            why = wanted
                                ? $"does not spell numbers in '{value}'"
                                : $"spells numbers in '{value}'";
                            return false;
                        }

                        break;

                    case "ordinal_scheme_name":
                        // Two ways of naming an ordinal form, and this engine reads both: a variation
                        // beginning with a hyphen is the ending itself, which is the scheme called
                        // inflection, and one beginning with a per cent sign names a CLDR rule set.
                        if ((value is "CLDR" or "inflection") != wanted)
                        {
                            why = $"declares the '{value}' ordinal scheme";
                            return false;
                        }

                        break;

                    default:
                        // Anything else is a property of the processor that this driver has not been taught
                        // to answer for — a calendar, a numbering combination, a collation URI. Skipping
                        // names it in the report, which is how the list of them stays honest.
                        why = $"declares a '{kind}' dependency";
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether this engine spells numbers in a language, which is what <c>lang</c> asks of it.
        /// </summary>
        /// <remarks>
        /// The primary subtag decides: a test asking for <c>de-AT</c> is asking for German. Every other
        /// language falls back to English, which the specification allows a processor to do — but a test
        /// declaring the language wants the language, so it is skipped rather than answered in the wrong one.
        /// </remarks>
        /// <param name="language">The language code the test declares.</param>
        private static bool SpellsNumbersIn(string language)
        {
            string primary = language.Split('-')[0];

            return Array.Exists(
                s_spoken, code => code.Equals(primary, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The primary subtags of the languages the engine spells numbers in.</summary>
        private static readonly string[] s_spoken = { "en", "de", "fr", "es", "pt", "it", "nb", "no", "nn", "sv", "da" };

        /// <summary>
        /// Decides whether a <c>spec</c> dependency takes in the version being run.
        /// </summary>
        /// <remarks>
        /// The value is a space-separated list of versions, each optionally suffixed <c>+</c> for that
        /// version and later. <c>XSLT20</c> alone means a test only an XSLT 2.0 processor should pass, which
        /// is not the same claim as <c>XSLT20+</c>.
        /// </remarks>
        private bool SpecIncludesVersion(string value)
        {
            foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                bool orLater = token.EndsWith('+');
                string bare = orLater ? token[..^1] : token;

                if (!bare.StartsWith("XSLT", StringComparison.Ordinal)
                    || !int.TryParse(bare[4..], NumberStyles.None, CultureInfo.InvariantCulture, out int hundredths))
                {
                    continue;
                }

                double named = hundredths / 10.0;

                if (orLater ? m_version.Number >= named : m_version.Number == named)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Short(Exception exception)
        {
            string message = exception.Message.Replace('\n', ' ').Replace('\r', ' ');
            return message.Length > 160 ? message[..160] : message;
        }
    }
}
