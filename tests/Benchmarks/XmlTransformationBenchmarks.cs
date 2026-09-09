using BenchmarkDotNet.Attributes;
using CodeDeeds.Xslt;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// Benchmarks for XML transformation performance.
    /// Measures the performance of transforming XML documents of various sizes.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, launchCount: 1)]
    public class XmlTransformationBenchmarks
    {
        private Xslt m_stylesheet = null!;
        private string m_xmlInput = null!;
        private string m_largeXmlInput = null!;

        [GlobalSetup]
        public void Setup()
        {
            var stylesheetContent = File.ReadAllText("Stylesheets/ProductsXmlToHtml.xslt");
            XsltOptions options = new XsltOptions
            {
                Backend = XsltBackend.Compiled
            };
            m_stylesheet = new Xslt(stylesheetContent, options);

            m_xmlInput = File.ReadAllText("Data/products.xml");
            m_largeXmlInput = GenerateLargeXmlDocument(10); // 100 x 10 = 1000 products
        }

        [Benchmark(Description = "Transform small XML (100 products) to string")]
        public string TransformXmlToString()
        {
            return m_stylesheet.TransformXml(m_xmlInput);
        }

        [Benchmark(Description = "Transform small XML (100 products) to TextWriter")]
        public string TransformXmlToTextWriter()
        {
            var writer = new StringWriter();
            m_stylesheet.TransformXml(m_xmlInput, writer);
            return writer.ToString();
        }

        [Benchmark(Description = "Transform small XML (100 products) to Stream")]
        public string TransformXmlToStream()
        {
            var stream = new MemoryStream();
            m_stylesheet.TransformXml(new StringReader(m_xmlInput), stream);
            stream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        [Benchmark(Description = "Transform large XML (1000 products) to string")]
        public string TransformLargeXmlToString()
        {
            return m_stylesheet.TransformXml(m_largeXmlInput);
        }

        [Benchmark(Description = "Transform large XML (1000 products) to TextWriter")]
        public string TransformLargeXmlToTextWriter()
        {
            var writer = new StringWriter();
            m_stylesheet.TransformXml(m_largeXmlInput, writer);
            return writer.ToString();
        }

        [Benchmark(Description = "Transform large XML (1000 products) to Stream")]
        public string TransformLargeXmlToStream()
        {
            var stream = new MemoryStream();
            m_stylesheet.TransformXml(new StringReader(m_largeXmlInput), stream);
            stream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string GenerateLargeXmlDocument(int multiplier)
        {
            var baseXml = File.ReadAllText("Data/products.xml");

            // Extract products from base XML
            var startIndex = baseXml.IndexOf("<product>", StringComparison.Ordinal);
            var endIndex = baseXml.LastIndexOf("</product>", StringComparison.Ordinal) + "</product>".Length;
            var productsSection = baseXml.Substring(startIndex, endIndex - startIndex);

            // Parse the XML and duplicate products
            var productsList = new List<string>();
            var lines = productsSection.Split(Environment.NewLine);

            for (int i = 0; i < multiplier; i++)
            {
                foreach (var line in lines)
                {
                    if (line.Contains("<id>"))
                    {
                        // Generate unique IDs
                        productsList.Add($"    <id>{i * 10 + int.Parse(line.Split('>')[1].Split('<')[0])}</id>");
                    }
                    else if (!string.IsNullOrWhiteSpace(line) && !line.Contains("<id>"))
                    {
                        productsList.Add(line);
                    }
                }
            }

            var header = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + "<products>";
            var footer = "</products>";
            var productsContent = string.Join(Environment.NewLine, productsList);

            return header + Environment.NewLine + productsContent + Environment.NewLine + footer;
        }
    }
}
