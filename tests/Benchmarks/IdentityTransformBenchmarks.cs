using System.Xml;
using System.Xml.Xsl;
using BenchmarkDotNet.Attributes;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// Copying a document through, the three ways a stylesheet can say it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The identity template with a few overrides is the commonest shape a stylesheet takes, and most of
    /// them were written for 1.0, where the template is the only way to say it. XSLT 3.0 can say the same
    /// with <c>xsl:mode on-no-match="shallow-copy"</c>, and the two should cost about the same: they do
    /// the same work. Where they do not, the difference is what the template's
    /// <c>select="@*|node()"</c> and its <c>match="@*|node()"</c> cost over the built-in rule, once per
    /// node. <c>xsl:copy-of</c> is the floor: the copy with no template applied to anything.
    /// </para>
    /// <para>
    /// The framework's processor runs the same identity template, for scale. Everything is measured end
    /// to end from a string to a writer that keeps nothing, so the parse is in every figure and is about
    /// the same in all of them.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    public class IdentityTransformBenchmarks
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private const string IdentityTemplate =
            "<xsl:template match=\"@*|node()\"><xsl:copy><xsl:apply-templates select=\"@*|node()\"/></xsl:copy></xsl:template>";

        private readonly CountingWriter m_writer = new CountingWriter();

        private string m_xml = null!;
        private Xslt m_shallowCopyMode = null!;
        private Xslt m_template = null!;
        private Xslt m_copyOf = null!;
        private XslCompiledTransform m_framework = null!;

        [GlobalSetup]
        public void Setup()
        {
            m_xml = BenchmarkData.Products(1000);

            m_shallowCopyMode = new Xslt(
                $"<xsl:stylesheet version=\"3.0\" {Xsl}><xsl:mode on-no-match=\"shallow-copy\"/></xsl:stylesheet>");

            m_template = new Xslt($"<xsl:stylesheet version=\"1.0\" {Xsl}>{IdentityTemplate}</xsl:stylesheet>");

            m_copyOf = new Xslt(
                $"<xsl:stylesheet version=\"3.0\" {Xsl}><xsl:template match=\"/\"><xsl:copy-of select=\".\"/></xsl:template></xsl:stylesheet>");

            m_framework = new XslCompiledTransform();
            using XmlReader stylesheet = XmlReader.Create(
                new StringReader($"<xsl:stylesheet version=\"1.0\" {Xsl}>{IdentityTemplate}</xsl:stylesheet>"));
            m_framework.Load(stylesheet);
        }

        [Benchmark(Baseline = true, Description = "xsl:copy-of select=\".\"")]
        public long CopyOf()
        {
            m_copyOf.TransformXml(m_xml, m_writer);
            return m_writer.Characters;
        }

        [Benchmark(Description = "xsl:mode on-no-match=\"shallow-copy\"")]
        public long ShallowCopyMode()
        {
            m_shallowCopyMode.TransformXml(m_xml, m_writer);
            return m_writer.Characters;
        }

        [Benchmark(Description = "The identity template, match=\"@*|node()\"")]
        public long IdentityTemplateRule()
        {
            m_template.TransformXml(m_xml, m_writer);
            return m_writer.Characters;
        }

        [Benchmark(Description = "The identity template on XslCompiledTransform (framework)")]
        public long IdentityTemplateOnFramework()
        {
            using XmlReader reader = XmlReader.Create(new StringReader(m_xml));
            m_framework.Transform(reader, null, m_writer);
            return m_writer.Characters;
        }
    }
}
