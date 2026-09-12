using BenchmarkDotNet.Attributes;
using CodeDeeds.Xslt;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// Benchmarks for XSLT stylesheet compilation performance.
    /// Measures the cost of compiling different XSLT stylesheets.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, launchCount: 1)]
    public class XsltCompilationBenchmarks
    {
        private string m_xmlToHtmlStylesheet = null!;
        private string m_jsonToHtmlStylesheet = null!;
        private string m_docbookStylesheet = null!;

        // The DocBook stylesheet imports the xslTNG library from cdn.docbook.org, so compiling it needs a
        // resolver that reaches the network. One instance for every iteration, so they share a connection pool.
        private readonly UriResolver m_docbookResolver = new UriResolver();

        [GlobalSetup]
        public void Setup()
        {
            m_xmlToHtmlStylesheet = File.ReadAllText("Stylesheets/ProductsXmlToHtml.xslt");
            m_jsonToHtmlStylesheet = File.ReadAllText("Stylesheets/ProductsJsonToHtml.xslt");
            m_docbookStylesheet = File.ReadAllText("Stylesheets/DocBook.xslt");
        }

        [Benchmark(Description = "Compile XML to HTML stylesheet")]
        public Xslt CompileXmlToHtmlStylesheet()
        {
            return new Xslt(m_xmlToHtmlStylesheet);
        }

        [Benchmark(Description = "Compile JSON to HTML stylesheet")]
        public Xslt CompileJsonToHtmlStylesheet()
        {
            return new Xslt(m_jsonToHtmlStylesheet);
        }

        [Benchmark(Description = "Compile Docbook online stylesheet")]
        public Xslt CompileDocbookOnlineStylesheet()
        {
            XsltOptions options = new XsltOptions
            {
                StylesheetResolver = m_docbookResolver,
            };
            return new Xslt(m_docbookStylesheet, options);
        }

        [Benchmark(Description = "Compile XML to HTML stylesheet as IL code")]
        public Xslt CompileXmlToHtmlStylesheetIL()
        {
            XsltOptions options = new XsltOptions
            {
                Backend = XsltBackend.Compiled
            };
            return new Xslt(m_xmlToHtmlStylesheet, options);
        }

        [Benchmark(Description = "Compile JSON to HTML stylesheet as IL code")]
        public Xslt CompileJsonToHtmlStylesheetIL()
        {
            XsltOptions options = new XsltOptions
            {
                Backend = XsltBackend.Compiled
            };
            return new Xslt(m_jsonToHtmlStylesheet, options);
        }

        [Benchmark(Description = "Compile Docbook online stylesheet as IL code")]
        public Xslt CompileDocbookOnlineStylesheetIL()
        {
            XsltOptions options = new XsltOptions
            {
                Backend = XsltBackend.Compiled,
                StylesheetResolver = m_docbookResolver,
            };
            return new Xslt(m_docbookStylesheet, options);
        }
    }
}
