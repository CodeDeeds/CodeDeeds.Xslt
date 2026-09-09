using BenchmarkDotNet.Attributes;
using CodeDeeds.Xslt;
using System.Text.Json;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// Benchmarks for JSON transformation performance.
    /// Measures the performance of transforming JSON documents of various sizes.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, launchCount: 1)]
    public class JsonTransformationBenchmarks
    {
        private Xslt m_stylesheet = null!;
        private string m_jsonInput = null!;
        private string m_largeJsonInput = null!;

        [GlobalSetup]
        public void Setup()
        {
            var stylesheetContent = File.ReadAllText("Stylesheets/ProductsJsonToHtml.xslt");
            XsltOptions options = new XsltOptions
            {
                Backend = XsltBackend.Compiled
            };
            m_stylesheet = new Xslt(stylesheetContent, options);

            m_jsonInput = File.ReadAllText("Data/products.json");
            m_largeJsonInput = GenerateLargeJsonDocument(10); // 10 * 100 = 1000 products
        }

        [Benchmark(Description = "Transform small JSON (100 products) to string")]
        public string TransformJsonToString()
        {
            return m_stylesheet.TransformJson(m_jsonInput);
        }

        [Benchmark(Description = "Transform small JSON (100 products) to TextWriter")]
        public string TransformJsonToTextWriter()
        {
            var writer = new StringWriter();
            m_stylesheet.TransformJson(m_jsonInput, writer);
            return writer.ToString();
        }

        [Benchmark(Description = "Transform small JSON (100 products) to Stream")]
        public string TransformJsonToStream()
        {
            var stream = new MemoryStream();
            m_stylesheet.TransformJson(new StringReader(m_jsonInput), stream);
            stream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        [Benchmark(Description = "Transform large JSON (1000 products) to string")]
        public string TransformLargeJsonToString()
        {
            return m_stylesheet.TransformJson(m_largeJsonInput);
        }

        [Benchmark(Description = "Transform large JSON (1000 products) to TextWriter")]
        public string TransformLargeJsonToTextWriter()
        {
            var writer = new StringWriter();
            m_stylesheet.TransformJson(m_largeJsonInput, writer);
            return writer.ToString();
        }

        [Benchmark(Description = "Transform large JSON (1000 products) to Stream")]
        public string TransformLargeJsonToStream()
        {
            var stream = new MemoryStream();
            m_stylesheet.TransformJson(new StringReader(m_largeJsonInput), stream);
            stream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string GenerateLargeJsonDocument(int multiplier)
        {
            var baseJson = File.ReadAllText("Data/products.json");
            using var doc = JsonDocument.Parse(baseJson);
            var productsArray = doc.RootElement.GetProperty("products");

            var products = new List<JsonDocument>();
            var largeProducts = new System.Text.Json.Nodes.JsonArray();

            foreach (var product in productsArray.EnumerateArray())
            {
                for (int i = 0; i < multiplier; i++)
                {
                    var productObj = new System.Text.Json.Nodes.JsonObject
                    {
                        ["id"] = (product.GetProperty("id").GetInt32() + i * 1000),
                        ["name"] = product.GetProperty("name").GetString(),
                        ["category"] = product.GetProperty("category").GetString(),
                        ["price"] = new System.Text.Json.Nodes.JsonObject
                        {
                            ["amount"] = product.GetProperty("price").GetProperty("amount").GetDecimal(),
                            ["currency"] = product.GetProperty("price").GetProperty("currency").GetString()
                        },
                        ["description"] = product.GetProperty("description").GetString(),
                        ["inStock"] = product.GetProperty("inStock").GetBoolean(),
                        ["rating"] = product.GetProperty("rating").GetDouble()
                    };
                    largeProducts.Add(productObj);
                }
            }

            var result = new System.Text.Json.Nodes.JsonObject
            {
                ["products"] = largeProducts
            };

            return result.ToJsonString();
        }
    }
}
