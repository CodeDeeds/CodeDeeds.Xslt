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

        [GlobalSetup]
        public void Setup()
        {
            m_xmlToHtmlStylesheet = File.ReadAllText("Stylesheets/ProductsXmlToHtml.xslt");
            m_jsonToHtmlStylesheet = File.ReadAllText("Stylesheets/ProductsJsonToHtml.xslt");
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

        [Benchmark(Description = "Compile XML to HTML stylesheet")]
        public Xslt CompileXmlToHtmlStylesheetIL()
        {
            XsltOptions options = new XsltOptions
            {
                Backend = XsltBackend.Compiled
            };
            return new Xslt(m_xmlToHtmlStylesheet, options);
        }

        [Benchmark(Description = "Compile JSON to HTML stylesheet")]
        public Xslt CompileJsonToHtmlStylesheetIL()
        {
            XsltOptions options = new XsltOptions
            {
                Backend = XsltBackend.Compiled
            };
            return new Xslt(m_jsonToHtmlStylesheet, options);
        }
    }
}
