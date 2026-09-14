using System.Xml.Schema;
using BenchmarkDotNet.Attributes;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// What schema awareness costs, measured against the same work done without it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three questions, and the comparisons are what make them answerable. What does validating the input
    /// cost over parsing it? What do the typed values cost to read once it is validated? And what does
    /// validating what the stylesheet builds cost over building it?
    /// </para>
    /// <para>
    /// Every pair transforms the same document with the same stylesheet, so the difference between the two
    /// members of a pair is the schema awareness and nothing else. The unvalidated members double as the
    /// check that the option, left off, costs nothing: a caller who never asks for a schema should be
    /// running the engine that existed before there were any.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, launchCount: 1)]
    public class SchemaAwareBenchmarks
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        /// <summary>Reads every product, summing and comparing values the schema gives types to.</summary>
        private const string Reading =
            "<xsl:template match=\"/\"><report>"
            + "<total><xsl:value-of select=\"sum(//price)\"/></total>"
            + "<stocked><xsl:value-of select=\"count(//product[inStock = true()])\"/></stocked>"
            + "<best><xsl:value-of select=\"max(//rating)\"/></best>"
            + "<dear><xsl:value-of select=\"count(//product[price > 100])\"/></dear>"
            + "</report></xsl:template>";

        /// <summary>Builds a document of the same shape as the input, which validation can then check.</summary>
        private const string Building =
            "<xsl:template match=\"/\"><products><xsl:for-each select=\"//product\">"
            + "<product><id><xsl:value-of select=\"id\"/></id><name><xsl:value-of select=\"name\"/></name>"
            + "<category><xsl:value-of select=\"category\"/></category>"
            + "<price currency=\"{price/@currency}\"><xsl:value-of select=\"price\"/></price>"
            + "<description><xsl:value-of select=\"description\"/></description>"
            + "<inStock><xsl:value-of select=\"inStock\"/></inStock>"
            + "<rating><xsl:value-of select=\"rating\"/></rating></product>"
            + "</xsl:for-each></products></xsl:template>";

        private XmlSchemaSet m_schemas = null!;
        private string m_input = null!;

        private Xslt m_readingPlain = null!;
        private Xslt m_readingTyped = null!;
        private Xslt m_buildingPlain = null!;
        private Xslt m_buildingValidated = null!;

        [GlobalSetup]
        public void Setup()
        {
            m_input = File.ReadAllText("Data/products.xml");

            m_schemas = new XmlSchemaSet();
            using (StreamReader schema = new StreamReader("Schemas/Products.xsd"))
            {
                m_schemas.Add(XmlSchema.Read(schema, null)!);
            }

            m_schemas.Compile();

            m_readingPlain = new Xslt(Sheet(Reading), Plain());
            m_readingTyped = new Xslt(Sheet(Reading), Typed());

            m_buildingPlain = new Xslt(Sheet(Building), Plain());

            // The stylesheet validates what it builds, and nothing else: the input is read as it always was,
            // so the pair measures the checking of the result rather than of both ends.
            m_buildingValidated = new Xslt(
                Sheet(Building).Replace("<products>", "<products xsl:validation=\"strict\">", StringComparison.Ordinal),
                new XsltOptions { OmitXmlDeclaration = true, SchemaAware = true, Schemas = m_schemas });
        }

        [Benchmark(Baseline = true, Description = "Read 100 products, untyped")]
        public string ReadUntyped()
        {
            return m_readingPlain.TransformXml(m_input);
        }

        [Benchmark(Description = "Read 100 products, input validated and typed")]
        public string ReadTyped()
        {
            return m_readingTyped.TransformXml(m_input);
        }

        [Benchmark(Description = "Build 100 products, nothing validated")]
        public string BuildPlain()
        {
            return m_buildingPlain.TransformXml(m_input);
        }

        [Benchmark(Description = "Build 100 products, result validated strictly")]
        public string BuildValidated()
        {
            return m_buildingValidated.TransformXml(m_input);
        }

        [Benchmark(Description = "Compile a stylesheet that imports the schema")]
        public Xslt CompileSchemaAware()
        {
            return new Xslt(Sheet(Reading), Typed());
        }

        private static string Sheet(string body)
        {
            return $"<xsl:stylesheet version=\"3.0\" {Xsl}>{body}</xsl:stylesheet>";
        }

        private static XsltOptions Plain()
        {
            return new XsltOptions { OmitXmlDeclaration = true };
        }

        private XsltOptions Typed()
        {
            return new XsltOptions
            {
                OmitXmlDeclaration = true,
                SchemaAware = true,
                Schemas = m_schemas,
                InputValidation = XsltValidation.Strict,
            };
        }
    }
}
