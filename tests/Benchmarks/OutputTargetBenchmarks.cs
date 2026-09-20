using System.Text;
using BenchmarkDotNet.Attributes;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// What each kind of output costs the engine, with nothing of the benchmark's own in the figure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="XmlTransformationBenchmarks"/> collects its results in a <see cref="StringWriter"/> and a
    /// <see cref="MemoryStream"/> and reads them back, and its README says that the extra allocation in
    /// those rows is the benchmark's. These write to sinks that keep nothing, so the rows can be compared:
    /// the same document, read the same way through <c>TransformXml(string, ...)</c>, to each target.
    /// </para>
    /// <para>
    /// A sink that does nothing per call flatters the engine in one respect, which is why a real
    /// <see cref="StreamWriter"/> is here beside it. A thousand-product transformation makes about a
    /// hundred thousand calls on its writer, a few characters at a time, and what each call costs is the
    /// writer's: nothing to <see cref="CountingWriter"/>, something to every writer that does any work.
    /// The difference between the first two rows is that cost.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    public class OutputTargetBenchmarks
    {
        private readonly CountingWriter m_writer = new CountingWriter();
        private readonly CountingStream m_stream = new CountingStream();
        private readonly UTF8Encoding m_utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private Xslt m_stylesheet = null!;
        private string m_xml = null!;

        [GlobalSetup]
        public void Setup()
        {
            m_stylesheet = new Xslt(BenchmarkData.ProductsStylesheet());
            m_xml = BenchmarkData.Products(1000);
        }

        [Benchmark(Baseline = true, Description = "To a TextWriter that keeps nothing")]
        public long ToCountingWriter()
        {
            m_stylesheet.TransformXml(m_xml, m_writer);
            return m_writer.Characters;
        }

        [Benchmark(Description = "To a caller's StreamWriter over a stream that keeps nothing")]
        public long ToCallersStreamWriter()
        {
            using (StreamWriter writer = new StreamWriter(m_stream, m_utf8, bufferSize: 1024, leaveOpen: true))
            {
                m_stylesheet.TransformXml(m_xml, writer);
            }

            return m_stream.Length;
        }

        [Benchmark(Description = "To a Stream that keeps nothing")]
        public long ToCountingStream()
        {
            m_stylesheet.TransformXml(m_xml, m_stream);
            return m_stream.Length;
        }

        [Benchmark(Description = "To a string")]
        public string ToText()
        {
            return m_stylesheet.TransformXml(m_xml);
        }
    }
}
