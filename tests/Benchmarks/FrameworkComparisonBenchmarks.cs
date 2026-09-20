using System.Xml;
using System.Xml.Xsl;
using BenchmarkDotNet.Attributes;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// This engine beside <see cref="XslCompiledTransform"/>, the processor the framework ships, on the
    /// products stylesheet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A figure on its own says whether the engine got faster, and not whether it is fast. The
    /// framework's processor compiles XSLT 1.0 to IL and is what a .NET application reaches for first, so
    /// it is the yardstick a user will hold this one against.
    /// </para>
    /// <para>
    /// Each runs the stylesheet it would be given. This engine runs the products stylesheet as it is
    /// written, in XSLT 3.0; the framework cannot, so it runs the portable form at 1.0, where
    /// <c>avg()</c> is a sum over a count. The two results have the same thousand and one rows and differ
    /// by about four per cent in length, in indentation and in how the HTML <c>meta</c> element is
    /// handled.
    /// </para>
    /// <para>
    /// Both pairs are needed. To a string is what most callers ask for, and each processor gathers the
    /// string its own way. To a writer that keeps nothing takes the gathering out, and leaves what each
    /// does to produce the result.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    public class FrameworkComparisonBenchmarks
    {
        private readonly CountingWriter m_writer = new CountingWriter();

        private Xslt m_stylesheet = null!;
        private XslCompiledTransform m_framework = null!;
        private string m_xml = null!;

        /// <summary>How many products the document holds.</summary>
        [Params(100, 1000)]
        public int ProductCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            m_xml = BenchmarkData.Products(ProductCount);
            m_stylesheet = new Xslt(BenchmarkData.ProductsStylesheet());
            m_framework = FrameworkLoadBenchmarks.Load(BenchmarkData.PortableProductsStylesheet("1.0"));
        }

        [Benchmark(Baseline = true, Description = "CodeDeeds.Xslt, to a string")]
        public string ToText()
        {
            return m_stylesheet.TransformXml(m_xml);
        }

        [Benchmark(Description = "XslCompiledTransform, to a string")]
        public string FrameworkToText()
        {
            using XmlReader reader = XmlReader.Create(new StringReader(m_xml));
            StringWriter writer = new StringWriter();
            m_framework.Transform(reader, null, writer);
            return writer.ToString();
        }

        [Benchmark(Description = "CodeDeeds.Xslt, to a writer that keeps nothing")]
        public long ToCountingWriter()
        {
            m_stylesheet.TransformXml(m_xml, m_writer);
            return m_writer.Characters;
        }

        [Benchmark(Description = "XslCompiledTransform, to a writer that keeps nothing")]
        public long FrameworkToCountingWriter()
        {
            using XmlReader reader = XmlReader.Create(new StringReader(m_xml));
            m_framework.Transform(reader, null, m_writer);
            return m_writer.Characters;
        }
    }

    /// <summary>
    /// What it costs to make a stylesheet ready, here and in the framework.
    /// </summary>
    /// <remarks>
    /// Apart from <see cref="FrameworkComparisonBenchmarks"/> because it does not depend on the size of
    /// any document, and would otherwise be measured once per size to the same answer.
    /// </remarks>
    [MemoryDiagnoser]
    public class FrameworkLoadBenchmarks
    {
        private string m_stylesheet = null!;
        private string m_portable = null!;

        [GlobalSetup]
        public void Setup()
        {
            m_stylesheet = BenchmarkData.ProductsStylesheet();
            m_portable = BenchmarkData.PortableProductsStylesheet("1.0");
        }

        [Benchmark(Baseline = true, Description = "new Xslt(...), interpreted")]
        public Xslt CompileInterpreted()
        {
            return new Xslt(m_stylesheet);
        }

        [Benchmark(Description = "new Xslt(...), compiled to IL")]
        public Xslt CompileToIL()
        {
            return new Xslt(m_stylesheet, new XsltOptions { Backend = XsltBackend.Compiled });
        }

        [Benchmark(Description = "XslCompiledTransform.Load (framework)")]
        public XslCompiledTransform LoadOnFramework()
        {
            return Load(m_portable);
        }

        /// <summary>Loads a stylesheet into the framework's processor.</summary>
        internal static XslCompiledTransform Load(string stylesheet)
        {
            XslCompiledTransform transform = new XslCompiledTransform();
            using XmlReader reader = XmlReader.Create(new StringReader(stylesheet));
            transform.Load(reader);
            return transform;
        }
    }
}
