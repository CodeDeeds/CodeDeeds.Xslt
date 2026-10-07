using BenchmarkDotNet.Attributes;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// Transforming DocBook documents with the DocBook xslTNG stylesheets: a large stylesheet somebody else
    /// wrote, over documents written for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The product stylesheets the other classes use are one module and a few templates, and say little
    /// about what a stylesheet of fifty modules does: functions that call functions, maps, tunnel
    /// parameters, modes by the dozen, and a document put through four stylesheets of its own by
    /// <c>fn:transform()</c> before the one that writes HTML is applied to it. This class is that, over
    /// two documents from the stylesheets' own tests: a short book, which is title pages, parts,
    /// chapters and appendixes around plain paragraphs, and one table at a hundred rows and at a
    /// thousand, where the stylesheets do most per element and the cost of a row can be read off the
    /// difference.
    /// </para>
    /// <para>
    /// The stylesheet is compiled once, in the setup, so what is measured is the transformation: the
    /// parse of the document, which is small beside the rest, and everything the stylesheets do with it.
    /// That includes running the four stylesheets <c>fn:transform()</c> is handed, which are compiled
    /// the first time and kept, and reading the localization and the title page templates the
    /// stylesheets ask for, which are read every time.
    /// </para>
    /// <para>
    /// Two things the stylesheets need of a caller. They resolve what they read against their own
    /// location, so the modules have to be identified by URI, which <see cref="UriResolver"/> does and
    /// <see cref="FileResolver"/>, naming them by path, does not. And they ask the document for its base
    /// URI, so the transformation is given an <see cref="XsltOptions.InputUri"/>; without one the first
    /// variable they declare is an empty sequence where a string was declared.
    /// </para>
    /// <para>
    /// Everything is written to a writer that keeps nothing, and nothing is fetched over the network: the
    /// stylesheets are under <c>Stylesheets/DocBook</c> and the documents under <c>Data/DocBook</c>.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    public class DocBookTransformationBenchmarks
    {
        private readonly CountingWriter m_writer = new CountingWriter();

        private Xslt m_stylesheet = null!;
        private Xslt m_keeping = null!;
        private string m_book = null!;
        private string m_table100 = null!;
        private string m_table1000 = null!;

        [GlobalSetup]
        public void Setup()
        {
            UriResolver resolver = new UriResolver(Path.GetFullPath("Stylesheets"));

            m_stylesheet = new Xslt(
                File.ReadAllText("Stylesheets/DocBook.xslt"),
                new XsltOptions
                {
                    StylesheetResolver = resolver,
                    DocumentResolver = resolver,
                    InputUri = "https://example.org/docbook/input.xml",
                    BaseOutputUri = "https://example.org/docbook/output.html",

                    // What the stylesheets say as they go is not part of the result, and is not wanted on
                    // the console once for every iteration.
                    MessageWriter = TextWriter.Null,
                    WarningWriter = TextWriter.Null,
                });

            // The same stylesheet with a resolver that parses each document the stylesheets read once,
            // the way the stylesheet reads it, and hands the tree back to every transformation after.
            KeepingResolver keeping = new KeepingResolver(resolver);
            m_keeping = new Xslt(
                File.ReadAllText("Stylesheets/DocBook.xslt"),
                new XsltOptions
                {
                    StylesheetResolver = resolver,
                    DocumentResolver = keeping,
                    InputUri = "https://example.org/docbook/input.xml",
                    BaseOutputUri = "https://example.org/docbook/output.html",
                    MessageWriter = TextWriter.Null,
                    WarningWriter = TextWriter.Null,
                });
            keeping.Parser = m_keeping;

            m_book = File.ReadAllText("Data/DocBook/book.001.xml");
            m_table1000 = File.ReadAllText("Data/DocBook/table-cals.049-1000-rows.xml");
            m_table100 = FirstRows(m_table1000, 100);

            // A document that did not transform would be measured as the time it takes to fail.
            foreach (string document in new[] { m_book, m_table100, m_table1000 })
            {
                string result = m_stylesheet.TransformXml(document);

                if (!result.Contains("</html>", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A DocBook document did not transform to a page of HTML.");
                }
            }

            // The two differ in the dc.modified stamp of the moment each ran and in nothing else.
            if (Unstamped(m_keeping.TransformXml(m_book)) != Unstamped(m_stylesheet.TransformXml(m_book)))
            {
                throw new InvalidOperationException("The book transformed differently with the documents kept parsed.");
            }
        }

        /// <summary>
        /// A resolver that parses each document once, as the stylesheet would read it, and gives the tree
        /// back for every transformation after: what a caller does whose documents do not change.
        /// </summary>
        private sealed class KeepingResolver : IXsltResolver
        {
            private readonly IXsltResolver m_inner;
            private readonly Dictionary<string, Model.XdmTree> m_kept = new(StringComparer.Ordinal);

            public KeepingResolver(IXsltResolver inner) => m_inner = inner;

            public Xslt? Parser { get; set; }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                ResolvedResource? resolved = m_inner.Resolve(href, baseUri);

                if (resolved is null)
                {
                    return null;
                }

                lock (m_kept)
                {
                    if (!m_kept.TryGetValue(resolved.Uri, out Model.XdmTree? tree))
                    {
                        using (resolved.Reader)
                        {
                            tree = Parser!.ParseDocument(resolved.Reader, resolved.Uri);
                        }

                        m_kept[resolved.Uri] = tree;
                    }
                    else
                    {
                        resolved.Reader.Dispose();
                    }

                    return new ResolvedResource(tree, resolved.Uri);
                }
            }
        }

        /// <summary>The table with only its first rows, everything else as it was.</summary>
        private static string Unstamped(string page)
        {
            return System.Text.RegularExpressions.Regex.Replace(page, "name=\"dc.modified\" content=\"[^\"]*\"", "name=\"dc.modified\"");
        }

        private static string FirstRows(string table, int count)
        {
            int at = 0;

            for (int i = 0; i < count; i++)
            {
                at = table.IndexOf("</row>", at, StringComparison.Ordinal) + "</row>".Length;
            }

            return string.Concat(table.AsSpan(0, at), table.AsSpan(table.IndexOf("</tbody>", at, StringComparison.Ordinal)));
        }

        [Benchmark(Description = "A book of 77 paragraphs, 44 KB")]
        public long Book()
        {
            m_stylesheet.TransformXml(m_book, m_writer);
            return m_writer.Characters;
        }

        [Benchmark(Description = "The book, the documents read kept parsed")]
        public long BookWithDocumentsKept()
        {
            m_keeping.TransformXml(m_book, m_writer);
            return m_writer.Characters;
        }

        [Benchmark(Description = "A table of 100 rows, 26 KB")]
        public long Table100()
        {
            m_stylesheet.TransformXml(m_table100, m_writer);
            return m_writer.Characters;
        }

        [Benchmark(Description = "A table of 1,000 rows, 257 KB")]
        public long Table1000()
        {
            m_stylesheet.TransformXml(m_table1000, m_writer);
            return m_writer.Characters;
        }
    }
}
