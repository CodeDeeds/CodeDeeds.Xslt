using System.Xml.Linq;

namespace CodeDeeds.Xslt.Conformance
{
    /// <summary>The XSLT 3.0 suite's catalog: the files its test-sets live in.</summary>
    /// <remarks>
    /// Flatter than the QT3 catalog beside it, because this suite keeps its environments in the test-sets
    /// rather than in the catalog. A test-set file and the directory it sits in are together self-contained,
    /// so what the catalog carries is only the list of them.
    /// </remarks>
    internal sealed class Xslt30Catalog
    {
        public static readonly XNamespace Ns = "http://www.w3.org/2012/10/xslt-test-catalog";

        private Xslt30Catalog(string root, List<string> testSetFiles)
        {
            Root = root;
            TestSetFiles = testSetFiles;
        }

        public string Root { get; }

        /// <summary>The test-set files, as paths relative to the suite root.</summary>
        public List<string> TestSetFiles { get; }

        public static Xslt30Catalog Load(string root)
        {
            XElement catalog = XDocument.Load(Path.Combine(root, "catalog.xml")).Root!;

            List<string> files = new();
            foreach (XElement testSet in catalog.Elements(Ns + "test-set"))
            {
                if ((string?)testSet.Attribute("file") is string file)
                {
                    files.Add(file);
                }
            }

            return new Xslt30Catalog(root, files);
        }
    }

    /// <summary>What a test needs around it: a source document, a stylesheet, and stylesheet parameters.</summary>
    /// <remarks>
    /// Much smaller than its QT3 counterpart, and for a structural reason. There, a document reachable only
    /// through <c>doc()</c> had to be wired into the static context by hand; here a transformation has a
    /// document resolver of its own, so a source that is not the context item needs no arranging — the
    /// stylesheet reaches it through <c>document()</c> exactly as it would outside the suite.
    /// </remarks>
    internal sealed class Xslt30Environment
    {
        public string? Name { get; private init; }

        /// <summary>The file supplying the context document, relative to the test-set's directory.</summary>
        public string? SourceFile { get; private init; }

        /// <summary>The context document written into the catalog rather than kept in a file.</summary>
        public string? SourceContent { get; private init; }

        /// <summary>
        /// Where an inline source document was written, which is its base URI.
        /// </summary>
        /// <remarks>
        /// A document written into the catalogue has no file of its own, and what it is relative to is the
        /// catalogue it stands in. Without it <c>base-uri()</c> of a node in such a document answers
        /// nothing, and a test that asks — <c>misc/backwards</c>'s 041 — is decided by the driver having
        /// forgotten where the document came from.
        /// </remarks>
        public string? SourceBaseUri { get; private init; }

        /// <summary>An expression selecting, within the source document, what templates are first applied to.</summary>
        public string? SourceSelect { get; private init; }

        /// <summary>
        /// Whether the catalog asks for a source to be validated strictly against the environment's
        /// schemas, which is what makes its nodes typed.
        /// </summary>
        public bool ValidatesSources { get; private init; }

        /// <summary>
        /// The documents the catalog marks <c>validation="strict"</c> that are not the context document,
        /// as the file names a <c>document()</c> call asks for them by.
        /// </summary>
        /// <remarks>
        /// Validation is declared per source, so an environment may validate one document a stylesheet
        /// reads and not another; the driver tells the engine which by marking what it resolves.
        /// </remarks>
        public HashSet<string> ValidatedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>A stylesheet the environment supplies, where the test case names none itself.</summary>
        public string? StylesheetFile { get; private init; }

        /// <summary>The stylesheet parameters the environment declares, as written.</summary>
        public List<XElement> Parameters { get; } = new();

        /// <summary>
        /// The library packages the environment supplies, keyed by the name an <c>xsl:use-package</c> asks
        /// for.
        /// </summary>
        /// <remarks>
        /// A package is found by its name rather than by a relative reference, so the catalog says which
        /// file holds which name and the driver hands the engine a resolver over exactly that map.
        /// </remarks>
        public List<(string Uri, string? Version, string File)> Packages { get; } = new();

        /// <summary>
        /// The collections the environment declares: each as the URI it is asked for by, or null for the
        /// default collection, and the files it holds, relative to the test-set's directory.
        /// </summary>
        /// <remarks>
        /// A catalog collection is a list of files under a name rather than a directory, and the name is
        /// relative to the test set: <c>collection-004.xml</c> is what a stylesheet beside it asks for as
        /// <c>collection('collection-004.xml')</c>. A file may carry a fragment identifier, which names an
        /// element in it rather than the whole; the engine follows one as <c>document()</c> does.
        /// </remarks>
        public List<(string? Uri, List<string> Files)> Collections { get; } = new();

        /// <summary>
        /// The schemas the environment declares, as files relative to the test-set's directory, with the
        /// role the catalog gives each: what the source references, what the stylesheet imports, or a
        /// secondary one the others include.
        /// </summary>
        public List<(string File, string? Role)> Schemas { get; } = new();

        /// <summary>Why this environment is beyond the driver, or null if it is usable.</summary>
        public string? Unsupported { get; private init; }

        public static Xslt30Environment Parse(XElement element)
        {
            XNamespace ns = element.Name.Namespace;

            // A collation an environment declares is one the engine provides or the driver does, through
            // SuiteCollations, which every transformation is given; nothing to arrange per environment. A
            // schema is recorded for the runner, which decides by whether the run is schema-aware.
            string? unsupported =
                element.Element(ns + "resource") is not null ? "environment declares a resource"
                : null;

            List<(string, string?)> schemas = new();

            foreach (XElement schema in element.Elements(ns + "schema"))
            {
                if ((string?)schema.Attribute("xsd-version") == "1.1")
                {
                    unsupported ??= "environment declares an XSD 1.1 schema";
                }

                if ((string?)schema.Attribute("file") is string file)
                {
                    // A few schemas use XSD 1.1 without the catalog saying so; an assertion is the tell,
                    // and a schema that will not load is a test that cannot be judged.
                    if (NeedsXsd11(element, file))
                    {
                        unsupported ??= "environment declares an XSD 1.1 schema";
                    }

                    schemas.Add((file, (string?)schema.Attribute("role")));
                }
            }

            List<(string?, List<string>)> collections = new();

            foreach (XElement collection in element.Elements(ns + "collection"))
            {
                if (collection.Element(ns + "query") is not null)
                {
                    // A collection defined by a query is an XQuery to run, which this driver has no way to.
                    unsupported ??= "environment declares a collection by a query";
                }

                string? uri = (string?)collection.Attribute("uri");
                List<string> files = new();

                foreach (XElement source in collection.Elements(ns + "source"))
                {
                    if ((string?)source.Attribute("file") is string file)
                    {
                        files.Add(file);
                    }
                }

                // An empty URI is the default collection, which is what a stylesheet asks for with no name.
                collections.Add((string.IsNullOrEmpty(uri) ? null : uri, files));
            }

            string? sourceFile = null;
            string? sourceContent = null;
            string? sourceSelect = null;
            bool validates = false;
            List<string> validatedFiles = new();

            foreach (XElement source in element.Elements(ns + "source"))
            {
                bool strict = (string?)source.Attribute("validation") == "strict";

                if ((string?)source.Attribute("role") != ".")
                {
                    // A source with no role is a document the stylesheet reaches through document(), which
                    // the driver's document resolver already serves out of the test-set's own directory.
                    // Whether it is validated is its own business, declared on the source itself.
                    if (strict && (string?)source.Attribute("file") is string validated)
                    {
                        validatedFiles.Add(validated);
                    }

                    continue;
                }

                // The context document's own validation, which is what the transformation is given.
                validates |= strict;

                if ((string?)source.Attribute("streaming") == "true")
                {
                    unsupported ??= "environment asks for a streamed source document";
                }

                sourceFile = (string?)source.Attribute("file");
                sourceContent = (string?)source.Element(ns + "content");
                sourceSelect = (string?)source.Attribute("select");
            }

            Xslt30Environment environment = new Xslt30Environment
            {
                Name = (string?)element.Attribute("name"),
                SourceFile = sourceFile,
                SourceContent = sourceContent,
                SourceBaseUri = sourceContent is null || element.BaseUri.Length == 0 ? null : element.BaseUri,
                SourceSelect = sourceSelect,
                ValidatesSources = validates,
                StylesheetFile = (string?)element.Element(ns + "stylesheet")?.Attribute("file"),
                Unsupported = unsupported,
            };

            environment.Parameters.AddRange(element.Elements(ns + "param"));
            environment.Collections.AddRange(collections);
            environment.Schemas.AddRange(schemas);

            foreach (string validated in validatedFiles)
            {
                environment.ValidatedFiles.Add(validated);
            }

            foreach (XElement package in element.Elements(ns + "package"))
            {
                if ((string?)package.Attribute("uri") is string uri
                    && (string?)package.Attribute("file") is string file)
                {
                    environment.Packages.Add((uri, (string?)package.Attribute("package-version"), file));
                }
            }

            return environment;
        }

        /// <summary>Whether a schema file uses XSD 1.1's assertions, which .NET's schema implementation has not.</summary>
        private static bool NeedsXsd11(XElement element, string file)
        {
            if (element.BaseUri.Length == 0 || !Uri.TryCreate(element.BaseUri, UriKind.Absolute, out Uri? catalog) || !catalog.IsFile)
            {
                return false;
            }

            string path = Path.Combine(Path.GetDirectoryName(catalog.LocalPath)!, file);

            try
            {
                return File.Exists(path) && File.ReadAllText(path).Contains(":assert", StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
