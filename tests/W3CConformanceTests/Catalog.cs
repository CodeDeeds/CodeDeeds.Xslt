using System.Xml.Linq;

namespace CodeDeeds.Xslt.Conformance
{
    /// <summary>The QT3 catalog: the environments tests run in, and the files the test-sets live in.</summary>
    internal sealed class Catalog
    {
        public static readonly XNamespace Ns = "http://www.w3.org/2010/09/qt-fots-catalog";

        private Catalog(string root, Dictionary<string, Environment> environments, List<string> testSetFiles)
        {
            Root = root;
            Environments = environments;
            TestSetFiles = testSetFiles;
        }

        public string Root { get; }

        public Dictionary<string, Environment> Environments { get; }

        public List<string> TestSetFiles { get; }

        public static Catalog Load(string root)
        {
            XDocument document = XDocument.Load(Path.Combine(root, "catalog.xml"));
            XElement catalog = document.Root!;

            Dictionary<string, Environment> environments = new(StringComparer.Ordinal);
            foreach (XElement element in catalog.Elements(Ns + "environment"))
            {
                Environment environment = Environment.Parse(element);
                if (environment.Name is not null)
                {
                    environments[environment.Name] = environment;
                }
            }

            List<string> files = new();
            foreach (XElement testSet in catalog.Elements(Ns + "test-set"))
            {
                string? file = (string?)testSet.Attribute("file");
                if (file is not null)
                {
                    files.Add(file);
                }
            }

            return new Catalog(root, environments, files);
        }
    }

    /// <summary>
    /// What a test needs around it: a context document, namespace bindings, and anything this driver cannot
    /// provide.
    /// </summary>
    internal sealed class Environment
    {
        public string? Name { get; private init; }

        /// <summary>The file supplying the context item, relative to the catalog root.</summary>
        public string? ContextFile { get; private init; }

        public List<(string Prefix, string Uri)> Namespaces { get; } = new();

        /// <summary>
        /// Documents the environment binds to variables, as (name without the <c>$</c>, file).
        /// </summary>
        /// <remarks>
        /// A source whose role is <c>$works</c> is that document bound to <c>$works</c>. Ignoring these left
        /// every test using one failing on an unbound variable, which says nothing about the engine.
        /// </remarks>
        public List<(string Name, string File)> Variables { get; } = new();

        /// <summary>
        /// The decimal formats the environment declares, as the <c>name</c> attribute wrote it against the
        /// element that carries it.
        /// </summary>
        /// <remarks>
        /// Kept as the written name and the element rather than as an expanded one, because the prefix is
        /// bound on the <c>decimal-format</c> element itself and only that element can resolve it.
        /// </remarks>
        public List<XElement> DecimalFormats { get; } = new();

        /// <summary>Why this environment is beyond the driver, or null if it is usable.</summary>
        public string? Unsupported { get; private init; }

        public static Environment Parse(XElement element)
        {
            string? unsupported = null;

            if (element.Element(Ns_(element, "schema")) is not null)
            {
                unsupported = "environment declares a schema";
            }
            else if (element.Element(Ns_(element, "collation")) is not null)
            {
                unsupported = "environment declares a collation";
            }
            else if (element.Element(Ns_(element, "param")) is not null)
            {
                unsupported = "environment declares external variables";
            }
            else if (element.Element(Ns_(element, "resource")) is not null)
            {
                unsupported = "environment declares a resource";
            }

            string? contextFile = null;
            List<(string, string)> variables = new();

            foreach (XElement source in element.Elements(Ns_(element, "source")))
            {
                string role = (string?)source.Attribute("role") ?? string.Empty;
                string? file = (string?)source.Attribute("file");

                if (role == ".")
                {
                    contextFile = file;
                }
                else if (role.StartsWith('$') && file is not null)
                {
                    variables.Add((role[1..], file));
                }
                else if (role.Length == 0)
                {
                    // A named document reachable only through doc(), which this driver does not wire up.
                    unsupported ??= "environment declares a document for doc()";
                }
            }

            Environment environment = new Environment
            {
                Name = (string?)element.Attribute("name"),
                ContextFile = contextFile,
                Unsupported = unsupported,
            };

            environment.Variables.AddRange(variables);

            foreach (XElement ns in element.Elements(Ns_(element, "namespace")))
            {
                environment.Namespaces.Add(
                    ((string?)ns.Attribute("prefix") ?? string.Empty, (string?)ns.Attribute("uri") ?? string.Empty));
            }

            environment.DecimalFormats.AddRange(element.Elements(Ns_(element, "decimal-format")));
            return environment;
        }

        private static XName Ns_(XElement context, string local) => context.Name.Namespace + local;
    }
}
