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

        /// <summary>Why this environment is beyond the driver, or null if it is usable.</summary>
        public string? Unsupported { get; private init; }

        public static Xslt30Environment Parse(XElement element)
        {
            XNamespace ns = element.Name.Namespace;

            string? unsupported =
                element.Element(ns + "schema") is not null ? "environment declares a schema"
                : element.Element(ns + "collation") is not null ? "environment declares a collation"
                : element.Element(ns + "collection") is not null ? "environment declares a collection"
                : element.Element(ns + "resource") is not null ? "environment declares a resource"
                : null;

            string? sourceFile = null;
            string? sourceContent = null;
            string? sourceSelect = null;

            foreach (XElement source in element.Elements(ns + "source"))
            {
                if ((string?)source.Attribute("role") != ".")
                {
                    // A source with no role is a document the stylesheet reaches through document(), which
                    // the driver's document resolver already serves out of the test-set's own directory.
                    continue;
                }

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
                StylesheetFile = (string?)element.Element(ns + "stylesheet")?.Attribute("file"),
                Unsupported = unsupported,
            };

            environment.Parameters.AddRange(element.Elements(ns + "param"));

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
    }
}
