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
    /// That includes compiling the four stylesheets <c>fn:transform()</c> is handed, each time, for as
    /// long as they are not kept from one call to the next, and reading the localization and the title
    /// page templates the stylesheets ask for.
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
        }

        /// <summary>The table with only its first rows, everything else as it was.</summary>
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
