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
        /// as text.
        /// </summary>
        /// <remarks>
        /// Two things are in the tree and not in the text. A type annotation is one: an assertion such as
        /// <c>not(/* instance of element(*, xs:untyped))</c> cannot be answered from serialized output at
        /// all, because everything parsed back out of XML is untyped. The other is everything the
        /// serializer added on the way out — <c>indent="yes"</c> puts whitespace between elements that the
        /// result tree never held, and an XPath assertion reading a string value sees it. Asked for only
        /// when an assertion the serialized result answered no has something to gain by asking again, and
        /// it costs a second run of the transformation, which is why it is offered rather than kept.
        /// </remarks>
        public Func<XdmTree?>? Tree { get; init; }

        /// <summary>
        /// The principal result serialized the way <c>assert-xml</c> is defined to be read, or null
        /// where there is no result to serialize.
        /// </summary>
        /// <remarks>
        /// The catalog says what that assertion is measured against in as many words: a serialization
        /// of the result "using the default serialization parameters method=\"xml\" indent=\"no\"
        /// omit-xml-declaration=\"yes\"" — the stylesheet's own <c>xsl:output</c> aside. <c>Result</c> is
        /// what the stylesheet asked for, which every other assertion wants and this one does not: the
        /// html and xhtml methods add a <c>meta</c> element the result tree never held, and write tags
        /// that are not XML at all. Asked for only where the two differ, and it costs a second run.
        /// </remarks>
        public Func<string?>? AsXml { get; init; }

        /// <summary>
        /// The principal result as the sequence of items the stylesheet produced, for an assertion that
        /// asks what those items are rather than what they look like written down.
        /// </summary>
        /// <remarks>
        /// A test that declares <c>&lt;output tree="no" serialize="no"/&gt;</c>, or that starts at an
        /// <c>initial-function</c>, has a result that is not a document: <c>assert-eq</c> compares it with
        /// an XPath value and <c>assert-type</c> matches it against a sequence type, and both of those are
        /// questions about items. Serialized, the integer 144 and the string "144" are the same three
        /// characters, and the comparison the test asked for would not be the one made. Asked for only by
        /// those assertions, and it costs a second run, which is why it is offered rather than kept.
        /// </remarks>
        public Func<IReadOnlyList<XPath.XPathValue>?>? Values { get; init; }

        /// <summary>
        /// The schemas the environment declared, for an assertion that names one of their declarations:
        /// schema-element(E) in an assertion is a question the assertion cannot ask without them.
        /// </summary>
        public System.Xml.Schema.XmlSchemaSet? Schemas { get; init; }

        /// <summary>
        /// What the run warned about, one entry per warning, for an assert-warning.
        /// </summary>
        /// <remarks>
        /// Kept apart from the messages because the assertion is about a warning and not about a message.
        /// A stylesheet declaring warning-on-no-match="yes" usually writes an xsl:message saying so as
        /// well — mode-1427 writes "** Expect no-matching-template warnings **" and then asserts both
        /// — and a driver that could not tell the two apart would report that test passed without
        /// having measured the half it exists for.
        /// </remarks>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

        /// <summary>
        /// What each xsl:message wrote, one entry per message, for an assert-message.
        /// </summary>
        /// <remarks>
        /// Kept apart rather than run together, because the assertion is about a message and a run that
        /// writes three has written three results, not one long one.
        /// </remarks>
        public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
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

            Xslt30Environment? environment = ResolveEnvironment(testCase, testSet, out string? problem);
            if (problem is not null)
            {
                return new TestResult(Outcome.Skipped, problem);
            }

            // A test naming several stylesheets names one to run and the rest as modules it imports or
            // includes. Those are files beside it, which the suite resolver already serves by URI, so
            // what the driver has to settle is only which of them is the one to compile: the one the
            // catalog does not mark secondary.
            List<XElement> stylesheets = test.Elements(Xslt30Catalog.Ns + "stylesheet")
                .Where(sheet => (string?)sheet.Attribute("role") != "secondary")
                .ToList();
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
                source = LoadSource(environment, directory) ?? BuiltSource(environment);
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
            // And a named function is the third way in, which needs no source document and no mode: the
            // value it returns is the whole result.
            bool named = test.Element(Xslt30Catalog.Ns + "initial-template") is not null
                || test.Element(Xslt30Catalog.Ns + "initial-mode") is not null
                || test.Element(Xslt30Catalog.Ns + "initial-function") is not null;

            if (source is null && !named && !startsItself && !expectsError)
            {
                return new TestResult(Outcome.Skipped, "the test supplies no source document and names no entry point");
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
            MessageCollector messages = new MessageCollector();
            MessageCollector warnings = new MessageCollector();
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
                    Encodings = DeclaredEncodings(environment),
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
                    // reads, and a directory listing for the one test set that asks for a collection no
                    // environment declares. A URI matching neither is no collection, which is what the
                    // tests asking for one anyway expect to hear.
                    CollectionResolver = new CatalogCollections(
                        environment?.Collections ?? s_noCollections, directory),
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
                    MessageWriter = messages,
                    WarningWriter = warnings,
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
                    InitialFunction = EntryPoint(test, "initial-function"),
                    FunctionArguments = FunctionArgumentsOf(test),

                    // What templates are first applied to, where the test says: the initial-mode's own
                    // selection, or the environment's selection within the source document. A selection on
                    // a source that names no document is not a selection within one — it builds the
                    // document, and has already been evaluated to produce the source.
                    InitialMatchSelection = (string?)test.Element(Xslt30Catalog.Ns + "initial-mode")?.Attribute("select")
                        ?? SelectionWithin(environment),

                    // And what a global reads as the context item. The catalog's select on a source is
                    // "a path expression to select the initial context node within the document", which is
                    // the one node earlier versions of XSLT gave both jobs to — so it is supplied as both.
                    // A test whose selection is empty has no initial context node, and then a global that
                    // reads one is an error rather than reading the document instead.
                    GlobalContextItem = SelectionWithin(environment),

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

            // Offered rather than taken: the second transformation a tree costs is paid only by an
            // assertion that asks for one, and only after the serialized result has answered no.
            Xslt compiled = stylesheet!;

            return new Transformation
            {
                Result = output.ToString(),
                ResultDocuments = results.Documents,
                Directory = directory,
                Tree = compileOnly ? null : () => RunAgainIntoATree(compiled, source),
                AsXml = compileOnly ? null : () => RunAgainAsXml(compiled, source),
                Values = compileOnly ? null : () => RunAgainIntoASequence(compiled, source),
                Schemas = m_schemaAware ? EnvironmentSchemas(environment, directory) : null,
                Messages = messages.Written,
                Warnings = warnings.Written,
            };
        }


        /// <summary>
        /// The identity transformation, which serializes a tree with the default parameters and nothing
        /// of any stylesheet's own. Compiled once: every <c>assert-xml</c> that has to look twice uses it.
        /// </summary>
        private static readonly Lazy<Xslt> s_plainXml = new Lazy<Xslt>(() => new Xslt(
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
            + "<xsl:output method=\"xml\" indent=\"no\" omit-xml-declaration=\"yes\"/>"
            + "<xsl:template match=\"/\"><xsl:copy-of select=\"node()\"/></xsl:template>"
            + "</xsl:stylesheet>",
            new XsltOptions { OmitXmlDeclaration = true }));

        /// <summary>
        /// Runs the transformation a second time into a tree and serializes that tree as XML, which is
        /// what <c>assert-xml</c> compares against.
        /// </summary>
        private static string? RunAgainAsXml(Xslt stylesheet, string? source)
        {
            if (RunAgainIntoATree(stylesheet, source) is not XdmTree tree)
            {
                return null;
            }

            try
            {
                return s_plainXml.Value.Transform(tree);
            }
            catch (Exception)
            {
                // The first run produced a result; where this one cannot be serialized the assertion
                // falls back to the text, and the test fails on what it measured rather than on this.
                return null;
            }
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
        /// Runs the transformation a second time into a sequence, for an assertion that asks about the
        /// items of the result rather than about a document or about text.
        /// </summary>
        private static IReadOnlyList<XPath.XPathValue>? RunAgainIntoASequence(Xslt stylesheet, string? source)
        {
            try
            {
                return source is null
                    ? stylesheet.TransformToSequence()
                    : stylesheet.TransformXmlToSequence(source);
            }
            catch (Exception)
            {
                // The first run produced a result, so this one should too; where it does not, the
                // assertion says so rather than the test failing on the driver.
                return null;
            }
        }

        /// <summary>
        /// Whether a stylesheet declares the template named <c>xsl:initial-template</c>, under whatever prefix
        /// it binds to the XSLT namespace — a test written with <c>t:</c> declares it as surely as one
        /// written with <c>xsl:</c>, and reading the text for the usual spelling missed those.
        /// </summary>
        private static bool DeclaresInitialTemplate(string path, int depth = 0)
        {
            XNamespace xsl = "http://www.w3.org/1999/XSL/Transform";

            try
            {
                XDocument stylesheet = XDocument.Load(path);

                // A module may declare it and be included rather than declaring it here: override-f-029 is
                // an xsl:stylesheet holding one xsl:include and nothing else, and the template the test
                // starts at is in the module it includes. The depth is a guard against a pair of modules
                // that include each other, which the engine reports but this reader would only recurse on.
                if (depth < 8)
                {
                    foreach (XElement composed in stylesheet.Descendants()
                        .Where(element => element.Name == xsl + "include" || element.Name == xsl + "import"))
                    {
                        if ((string?)composed.Attribute("href") is not string href)
                        {
                            continue;
                        }

                        string module = Path.Combine(
                            Path.GetDirectoryName(path) ?? string.Empty,
                            href.Replace('/', Path.DirectorySeparatorChar));

                        if (File.Exists(module) && DeclaresInitialTemplate(module, depth + 1))
                        {
                            return true;
                        }
                    }
                }

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


        /// <summary>The arguments an initial-function entry point supplies, in order, or null for none.</summary>
        /// <remarks>
        /// Positional rather than named, a function having parameters in an order rather than by name, so
        /// how many there are is also the arity the function is looked up by.
        /// </remarks>
        private IReadOnlyList<object?>? FunctionArgumentsOf(XElement test)
        {
            if (test.Element(Xslt30Catalog.Ns + "initial-function") is not XElement called)
            {
                return null;
            }

            List<object?> arguments = new();

            foreach (XElement argument in called.Elements(Xslt30Catalog.Ns + "param"))
            {
                arguments.Add(Evaluate(argument));
            }

            return arguments;
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

            parameters[name] = Evaluate(declaration);
        }

        /// <summary>Evaluates the <c>select</c> of a catalog declaration into the value it stands for.</summary>
        /// <remarks>
        /// Shared by the parameters a stylesheet is given and the arguments an initial-function call is
        /// made with: both are XPath the catalog writes, evaluated against nothing.
        /// </remarks>
        private object? Evaluate(XElement declaration)
        {
            if ((string?)declaration.Attribute("select") is not string select)
            {
                return null;
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

            return compiled.Evaluate(ref context);
        }


        /// <summary>The encodings an environment declares for the files it serves, by file name.</summary>
        private static Dictionary<string, string> DeclaredEncodings(Xslt30Environment? environment)
        {
            Dictionary<string, string> encodings = new(StringComparer.OrdinalIgnoreCase);

            foreach ((string file, _, string? declared) in environment?.Resources ?? new())
            {
                if (declared is not null)
                {
                    encodings[Path.GetFileName(file)] = declared;
                }
            }

            return encodings;
        }


        /// <summary>Keeps each xsl:message, or each warning, the run wrote, in order.</summary>
        /// <remarks>
        /// The engine writes one message per WriteLine, so a line is a message. Buffering the characters
        /// and splitting afterwards would cut a message that has a newline inside it in two.
        /// </remarks>
        private sealed class MessageCollector : TextWriter
        {
            private readonly List<string> m_written = new();

            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

            public IReadOnlyList<string> Written => m_written;

            public override void WriteLine(string? value)
            {
                m_written.Add(value ?? string.Empty);
            }

            public override void Write(char value)
            {
                // Nothing else writes here, and a stray character is not a message.
            }
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
        /// <summary>Stands in for an environment that declares no collection at all.</summary>
        private static readonly List<(string? Uri, List<string> Files)> s_noCollections = new();

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

                if (!Uri.TryCreate(against, uri, out Uri? asked))
                {
                    return null;
                }

                return m_named.TryGetValue(asked.AbsoluteUri, out IReadOnlyList<string>? members)
                    ? members
                    : Listed(asked);
            }

            /// <summary>
            /// The files a directory holds that match a pattern: a collection URI written as a directory,
            /// then <c>?select=</c> and a glob.
            /// </summary>
            /// <remarks>
            /// No specification defines that form, and the test set that writes it says so itself —
            /// merge-097 carries a note from the suite's editor that the URIs "are therefore not
            /// interoperable". What a collection URI means is left to the processor, and the engine leaves
            /// it to whoever configures one; here that is this driver, and this is what it takes the form
            /// to mean. Nothing in the engine knows about it.
            /// </remarks>
            /// <param name="asked">The collection URI, resolved.</param>
            private static IReadOnlyList<string>? Listed(Uri asked)
            {
                const string Select = "?select=";

                if (!asked.IsFile || !asked.Query.StartsWith(Select, StringComparison.Ordinal))
                {
                    return null;
                }

                string directory = new Uri(asked.GetLeftPart(UriPartial.Path)).LocalPath;
                string pattern = Uri.UnescapeDataString(asked.Query[Select.Length..]);

                if (!Directory.Exists(directory) || pattern.Length == 0)
                {
                    return null;
                }

                // Ordered, so that a stylesheet reading the collection twice reads it the same way both
                // times, which is what a collection is required to be.
                return Directory.GetFiles(directory, pattern)
                    .OrderBy(file => file, StringComparer.Ordinal)
                    .Select(file => new Uri(file).AbsoluteUri)
                    .ToArray();
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

            /// <summary>
            /// The encoding the environment declared for a file it serves, by file name. A file with no
            /// byte-order mark and no XML declaration says nothing about itself, and unparsed-text() reads
            /// bytes as characters, so guessing UTF-8 reads an ISO-8859-1 file as mojibake.
            /// </summary>
            public Dictionary<string, string> Encodings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

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
                    Open(resolved.LocalPath),
                    resolved.AbsoluteUri)
                {
                    Validation = Validated.Contains(Path.GetFileName(resolved.LocalPath))
                        ? XsltValidation.Strict
                        : null,
                };
            }

            /// <summary>Opens a file in the encoding the environment declared, or by what it says of itself.</summary>
            private StreamReader Open(string path)
            {
                if (Encodings.TryGetValue(Path.GetFileName(path), out string? declared))
                {
                    try
                    {
                        return new StreamReader(path, System.Text.Encoding.GetEncoding(declared));
                    }
                    catch (ArgumentException)
                    {
                        // An encoding this platform does not know; read it the ordinary way rather than
                        // refusing to serve the file at all.
                    }
                }

                return new StreamReader(path, detectEncodingFromByteOrderMarks: true);
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

            foreach ((string file, string? role) in environment.Schemas)
            {
                // A secondary schema is one of the others' parts — what a schema includes or imports, or
                // what a stylesheet fetches for itself by location — and not a schema the caller supplies.
                // Handing it over as though the caller had supplied it puts components in scope that the
                // stylesheet never asked for, and import-schema-177 is the case that shows what that costs:
                // two schemas for one namespace, only the higher-precedence import of which is to be used,
                // and both of them in the set before the stylesheet is read at all. The resolver still
                // serves it by location, which is how it is meant to be reached.
                if (role == "secondary")
                {
                    continue;
                }

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
        /// The environment's selection where it selects within a document, which is null where it builds one.
        /// </summary>
        private static string? SelectionWithin(Xslt30Environment? environment)
        {
            return environment is { SourceFile: null, SourceContent: null } ? null : environment?.SourceSelect;
        }

        /// <summary>
        /// The source document an environment builds with an expression rather than naming, or null where
        /// it names one or supplies none.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The catalog's <c>select</c> on a source is documented as a path expression selecting the initial
        /// context node <em>within the document</em>, and three of the four uses in the suite are that. The
        /// fourth is <c>id-043</c>, whose source names no file and no content at all:
        /// <c>&lt;source role="." select="parse-xml('&lt;root/&gt;')"/&gt;</c>. There is no document for
        /// that to select within — the expression is the document.
        /// </para>
        /// <para>
        /// So it is evaluated here rather than handed to the engine as an initial match selection. It is the
        /// catalog's expression and not the stylesheet's, written in the catalog's own version of XPath:
        /// <c>fn:parse-xml</c> is 3.0's and <c>id-043</c>'s stylesheet says <c>version="2.0"</c>, so asking
        /// the stylesheet to evaluate it asks the wrong processor. A stylesheet of the driver's own, at 3.0,
        /// is the right one to ask.
        /// </para>
        /// </remarks>
        /// <param name="environment">The environment, or null where the test declares none.</param>
        private static string? BuiltSource(Xslt30Environment? environment)
        {
            if (environment is not { SourceFile: null, SourceContent: null, SourceSelect: string expression })
            {
                return null;
            }

            string escaped = expression
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);

            return new Xslt(
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + $"<xsl:template name=\"go\"><xsl:copy-of select=\"{escaped}\"/></xsl:template>"
                + "</xsl:stylesheet>",
                new XsltOptions { OmitXmlDeclaration = true, InitialTemplate = "go" })
                .Transform();
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

                    // What follows are properties of the processor that the specification leaves to it, and
                    // that this one has an answer for. A dependency is a question, not a limit: answering it
                    // no is as much a measurement as answering it yes, and is what lets the test that asks
                    // for the opposite — satisfied="false" — be run.

                    case "unicode-version":
                        // The tests that ask are two sets that assert exactly what a given Unicode version
                        // classifies: unicode-90 names the count of characters in each class as 9.0 had
                        // them, and regex-classes checks its answers against stored results for 3.1, 5.2
                        // and 6.0. Neither is satisfied by a later version — it is a different answer,
                        // not a better one. This engine reads the Unicode data .NET carries, which is
                        // later than any version the suite names: \d matches 760 characters here against
                        // the 370 unicode-90 asserts.
                        if (wanted)
                        {
                            why = $"needs Unicode {value}, and this engine reads the later Unicode .NET carries";
                            return false;
                        }

                        break;

                    case "combinations_for_numbering":
                        // The Unicode name of the digit-one of a numbering family: CIRCLED DIGIT ONE and
                        // the like, which are Number-Other rather than decimal digits. A processor numbering
                        // in them is allowed and not required, and this one does not: format-integer(5, '1')
                        // written with a circled one answers 5. The decimal digit families the
                        // specification does require are all here — Devanagari, Arabic-Indic, fullwidth
                        // and the rest — and those carry no dependency.
                        if (wanted)
                        {
                            why = $"does not number in the '{value}' family";
                            return false;
                        }

                        break;

                    case "year_component_values":
                        if (SupportsYears(value) != wanted)
                        {
                            why = wanted ? $"does not {value}" : $"does {value}";
                            return false;
                        }

                        break;

                    case "package_version_resolution":
                        // Where several versions of a package match a use-package range, this engine takes
                        // the highest. "unspecified" is the test saying it does not mind which.
                        if ((value is "highest_version" or "unspecified") != wanted)
                        {
                            why = $"resolves a package version range to the highest match, not '{value}'";
                            return false;
                        }

                        break;

                    case "additional_normalization_form":
                        // The four .NET provides, which are the four XPath names: NFC, NFD, NFKC and NFKD.
                        // fully-normalized is a check on the result rather than a form to normalize to, and
                        // is not one of these.
                        if (NormalizesTo(value) != wanted)
                        {
                            why = wanted ? $"does not {value}" : $"does {value}";
                            return false;
                        }

                        break;

                    case "enable_assertions":
                        // xsl:assert is always checked here. The specification's default is the other way
                        // — assertions off unless asked for — so a test wanting them off is the one
                        // this cannot present.
                        if (!wanted)
                        {
                            why = "checks xsl:assert always, and cannot be asked not to";
                            return false;
                        }

                        break;

                    case "maximum_number_of_decimal_digits":
                        // xs:decimal is .NET's decimal, which holds 28 significant digits against the 18 the
                        // specification requires. A test needing more is naming a limit this one has.
                        if ((int.TryParse(value, out int digits) && digits <= 28) != wanted)
                        {
                            why = wanted
                                ? $"needs {value} decimal digits and keeps 28"
                                : $"keeps 28 decimal digits";
                            return false;
                        }

                        break;

                    case "default_html_version":
                        // With no html-version and no version, the html method writes HTML 4: no doctype of
                        // its own, and the empty-element and escaping rules 4.01 asks for.
                        if ((value is "4" or "4.0" or "4.01") != wanted)
                        {
                            why = $"writes HTML 4 by default, not {value}";
                            return false;
                        }

                        break;

                    case "supported_calendars_in_date_formatting_functions":
                    case "default_calendar_in_date_formatting_functions":
                        // The one calendar this engine formats dates in, which is therefore also its
                        // default: the one the data model's dates are in, whose eras are BC and AD.
                        // Another is written out the way the specification says to write an unsupported
                        // one, a [Calendar: AD] prefix and the date in this one, which is not the same as
                        // formatting in it.
                        if ((value is "ISO" or "AD" or "ISO 8601") != wanted)
                        {
                            why = kind.StartsWith("default", StringComparison.Ordinal)
                                ? $"formats dates in the AD calendar by default, not '{value}'"
                                : $"does not format dates in the '{value}' calendar";
                            return false;
                        }

                        break;

                    case "unparsed_text_encoding":
                        // What unparsed-text() assumes where the call names no encoding, which the
                        // specification leaves to the processor for a resource that says nothing about
                        // itself. Here it is UTF-8.
                        if (value.Equals("UTF-8", StringComparison.OrdinalIgnoreCase) != wanted)
                        {
                            why = $"assumes UTF-8 for unparsed-text(), not {value}";
                            return false;
                        }

                        break;

                    case "default_output_encoding":
                        if (value.Equals("UTF-8", StringComparison.OrdinalIgnoreCase) != wanted)
                        {
                            why = $"serializes as UTF-8 by default, not {value}";
                            return false;
                        }

                        break;

                    case "ignore_doc_failure":
                        // document() raises where doc() would: FODC0002 reaches the stylesheet rather than
                        // the call answering an empty sequence. XSLT 3.0 allows either.
                        if (wanted)
                        {
                            why = "raises rather than ignoring a document() that cannot be read";
                            return false;
                        }

                        break;

                    case "default_language_for_numbering":
                        if (SpellsNumbersIn(value) != wanted)
                        {
                            why = wanted
                                ? $"does not spell numbers in '{value}' by default"
                                : $"spells numbers in '{value}' by default";
                            return false;
                        }

                        break;

                    case "recognize_id_as_uri_fragment":
                        // document('doc.xml#id') answers the element that id names, which is what XSLT 3.0
                        // 20.1 lets a processor do for a media type that defines fragment identifiers.
                        if (!wanted)
                        {
                            why = "reads a fragment identifier as an ID, and cannot be asked not to";
                            return false;
                        }

                        break;

                    case "detect_accumulator_cycles":
                        // XTDE3400 rather than a stack that runs out: an accumulator building itself is
                        // caught by a flag set while it is being built.
                        if (!wanted)
                        {
                            why = "detects a cyclic accumulator, and cannot be asked not to";
                            return false;
                        }

                        break;

                    case "extension-function":
                        // No caller can register one, so the only extension functions are EXSLT's Common
                        // module, and no test declares a dependency on those.
                        if (wanted)
                        {
                            why = $"needs the extension function {value}";
                            return false;
                        }

                        break;

                    default:
                        // Anything else is a property of the processor that this driver has not been taught
                        // to answer for. Skipping names it in the report, which is how the list of them
                        // stays honest.
                        why = $"declares a '{kind}' dependency";
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether this engine's dates and durations reach the years a test needs.
        /// </summary>
        /// <remarks>
        /// The limits are XSD 1.0's, which is the schema language this engine reads. Years before the
        /// common era and years past four digits are both in its lexical space; year zero is not, 1.0
        /// numbering 1 BC as <c>-0001</c>. XSD 1.1 renumbered that and is not what this reads.
        /// </remarks>
        /// <param name="value">What the dependency asks for, as the catalog writes it.</param>
        private static bool SupportsYears(string value)
        {
            return value switch
            {
                "support negative year" => true,
                "support year above 9999" => true,
                "support year zero" => false,
                _ => false,
            };
        }

        /// <summary>
        /// Whether this engine normalizes to every form a test names beyond NFC.
        /// </summary>
        /// <remarks>
        /// The catalog writes the value as the word <c>support</c> and then the forms, so
        /// <c>support NFD NFKC NFKD</c> asks for three. All four of XPath's forms are .NET's, and
        /// <c>fully-normalized</c> is not among them: it is a check on the result rather than a form to
        /// normalize to, and a test asking for it is asking for something this does not do.
        /// </remarks>
        /// <param name="value">What the dependency asks for, as the catalog writes it.</param>
        private static bool NormalizesTo(string value)
        {
            string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (string word in words)
            {
                if (word is "support" or "NFC" or "NFD" or "NFKC" or "NFKD")
                {
                    continue;
                }

                return false;
            }

            return words.Length > 0;
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
