using System.Xml;
using System.Xml.XPath;
using BenchmarkDotNet.Attributes;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// Where the time of a transformation goes: reading the document, and transforming it once read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An end-to-end figure cannot say which half to work on. These take the thousand-product
    /// transformation apart: the parse on its own, the transformation of a tree that is already built,
    /// and the two together, which should come to about the sum.
    /// </para>
    /// <para>
    /// The bare reader is the floor under the parse. The engine reads XML through
    /// <see cref="XmlReader"/>, so whatever that loop costs is outside this library's reach, and what
    /// <see cref="XdmTreeBuilder"/> adds to it is the part that can be worked on. The framework's own
    /// <see cref="XPathDocument"/> is there for scale: it builds the tree the framework's XSLT reads.
    /// </para>
    /// <para>
    /// The job is the default one. The older classes ask for three warm-up iterations; a library of this
    /// size takes some seconds of running before the runtime has finished optimising it, and the default
    /// warms up until the measurements stop moving rather than for a count fixed in advance.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    public class TransformPhaseBenchmarks
    {
        private Xslt m_stylesheet = null!;
        private string m_xml = null!;
        private XdmTree m_tree = null!;

        [GlobalSetup]
        public void Setup()
        {
            m_stylesheet = new Xslt(BenchmarkData.ProductsStylesheet());
            m_xml = BenchmarkData.Products(1000);
            m_tree = XdmTreeBuilder.FromXmlText(m_xml);
        }

        [Benchmark(Description = "Parse floor: bare XmlReader, every value read")]
        public long ReadWithBareReader()
        {
            using XmlReader reader = XmlReader.Create(new StringReader(m_xml));
            long characters = 0;

            while (reader.Read())
            {
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.Whitespace)
                {
                    characters += reader.Value.Length;
                }
            }

            return characters;
        }

        [Benchmark(Description = "Parse 1000 products to an XdmTree")]
        public XdmTree ParseToTree()
        {
            return XdmTreeBuilder.FromXmlText(m_xml);
        }

        [Benchmark(Description = "Parse 1000 products to an XPathDocument (framework)")]
        public XPathDocument ParseToXPathDocument()
        {
            return new XPathDocument(new StringReader(m_xml));
        }

        [Benchmark(Description = "Transform a tree already parsed")]
        public string TransformParsedTree()
        {
            return m_stylesheet.Transform(m_tree);
        }

        [Benchmark(Description = "Parse and transform")]
        public string ParseAndTransform()
        {
            return m_stylesheet.TransformXml(m_xml);
        }
    }

    /// <summary>
    /// Whether the cost of a product stays the same as the products multiply.
    /// </summary>
    /// <remarks>
    /// The mean divided by <see cref="ProductCount"/> is the figure to read, and it should not grow.
    /// Anything that looks at every sibling, or every node, once per node shows up here as a cost per
    /// product that rises with the count — which a benchmark at one size cannot show at all. At ten
    /// thousand products every array of the tree is on the large object heap and the result is several
    /// megabytes, so a few per cent there is the collector and not the engine.
    /// </remarks>
    [MemoryDiagnoser]
    public class ScalingBenchmarks
    {
        private Xslt m_stylesheet = null!;
        private string m_xml = null!;

        /// <summary>How many products the document holds.</summary>
        [Params(100, 1000, 10000)]
        public int ProductCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            m_stylesheet = new Xslt(BenchmarkData.ProductsStylesheet());
            m_xml = BenchmarkData.Products(ProductCount);
        }

        [Benchmark(Description = "Parse and transform to a string")]
        public string ParseAndTransform()
        {
            return m_stylesheet.TransformXml(m_xml);
        }
    }
}
