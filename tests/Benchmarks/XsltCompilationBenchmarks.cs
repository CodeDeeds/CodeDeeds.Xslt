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

        // The DocBook stylesheet imports the xslTNG library, fifty modules of it, from the copy under
        // Stylesheets/DocBook. They were fetched from cdn.docbook.org at every iteration once, and what
        // was measured then was fifty requests: 2,000 milliseconds, of which the compiler's were 80.
        private FileResolver m_docbookResolver = null!;

        [GlobalSetup]
        public void Setup()
        {
            m_xmlToHtmlStylesheet = File.ReadAllText("Stylesheets/ProductsXmlToHtml.xslt");
            m_jsonToHtmlStylesheet = File.ReadAllText("Stylesheets/ProductsJsonToHtml.xslt");
            m_docbookStylesheet = File.ReadAllText("Stylesheets/DocBook.xslt");
            m_docbookResolver = new FileResolver(Path.GetFullPath("Stylesheets"));
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

        [Benchmark(Description = "Compile DocBook stylesheet")]
        public Xslt CompileDocbookStylesheet()
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

        [Benchmark(Description = "Compile DocBook stylesheet as IL code")]
        public Xslt CompileDocbookStylesheetIL()
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
