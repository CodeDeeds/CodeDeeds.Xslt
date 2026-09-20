using System.Text;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// The documents and stylesheets the benchmarks share, so that two classes measuring the same thing
    /// are measuring it on the same input.
    /// </summary>
    internal static class BenchmarkData
    {
        /// <summary>
        /// A product catalogue of the size asked for, in the form
        /// <see cref="XmlTransformationBenchmarks"/> has always generated it.
        /// </summary>
        /// <remarks>
        /// A hundred products is the file as it stands. Anything larger repeats the file's products with
        /// their ids renumbered, by the same steps the original benchmark takes, so that a figure measured
        /// here can be set beside one measured there.
        /// </remarks>
        /// <param name="count">How many products, in hundreds: 100, 1000, 10000.</param>
        public static string Products(int count)
        {
            if (count < 100 || count % 100 != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "The catalogue comes in hundreds.");
            }

            string baseXml = File.ReadAllText("Data/products.xml");

            if (count == 100)
            {
                return baseXml;
            }

            int multiplier = count / 100;
            int startIndex = baseXml.IndexOf("<product>", StringComparison.Ordinal);
            int endIndex = baseXml.LastIndexOf("</product>", StringComparison.Ordinal) + "</product>".Length;
            string[] lines = baseXml.Substring(startIndex, endIndex - startIndex).Split(Environment.NewLine);
            List<string> products = new List<string>();

            for (int i = 0; i < multiplier; i++)
            {
                foreach (string line in lines)
                {
                    if (line.Contains("<id>"))
                    {
                        products.Add($"    <id>{i * 10 + int.Parse(line.Split('>')[1].Split('<')[0])}</id>");
                    }
                    else if (!string.IsNullOrWhiteSpace(line))
                    {
                        products.Add(line);
                    }
                }
            }

            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + "<products>" + Environment.NewLine
                + string.Join(Environment.NewLine, products) + Environment.NewLine + "</products>";
        }

        /// <summary>The products stylesheet as it is written: XSLT 3.0, using <c>avg()</c>.</summary>
        public static string ProductsStylesheet()
        {
            return File.ReadAllText("Stylesheets/ProductsXmlToHtml.xslt");
        }

        /// <summary>
        /// The products stylesheet with nothing in it that XSLT 1.0 lacks, at the version asked for.
        /// </summary>
        /// <remarks>
        /// <c>avg()</c> becomes a sum over a count, which is all that stands between the stylesheet and a
        /// 1.0 processor. The same text is then given either version, so that where two measurements differ
        /// only in the version they ran at, the version is what they measure. It is also the one form the
        /// framework's <see cref="System.Xml.Xsl.XslCompiledTransform"/> can run.
        /// </remarks>
        /// <param name="version">The <c>version</c> attribute to give it: <c>1.0</c> or <c>3.0</c>.</param>
        public static string PortableProductsStylesheet(string version)
        {
            string original = ProductsStylesheet();

            string portable = Replace(original, "avg(//product/price)", "sum(//product/price) div count(//product/price)");
            portable = Replace(portable, "avg(//product/rating)", "sum(//product/rating) div count(//product/rating)");

            return version == "3.0" ? portable : Replace(portable, "version=\"3.0\"", $"version=\"{version}\"");
        }

        /// <summary>A stylesheet that writes the value of one expression and nothing else.</summary>
        /// <param name="select">The expression, as it would be written in an attribute.</param>
        /// <param name="version">The <c>version</c> attribute to give it.</param>
        public static string SingleExpression(string select, string version = "3.0")
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:output method=\"text\"/>"
                + $"<xsl:template match=\"/\"><xsl:value-of select=\"{select}\"/></xsl:template>"
                + "</xsl:stylesheet>";
        }

        /// <summary>
        /// Replaces text that has to be there. A stylesheet edited so that the text is gone would otherwise
        /// leave the benchmark measuring the wrong thing without a word.
        /// </summary>
        public static string Replace(string text, string find, string with)
        {
            if (!text.Contains(find, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The stylesheet no longer contains '{find}', which this benchmark rewrites.");
            }

            return text.Replace(find, with, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A writer that counts what it is given and keeps none of it.
    /// </summary>
    /// <remarks>
    /// A benchmark that collects its result in a <see cref="StringWriter"/> measures the
    /// <see cref="StringWriter"/> as well: on a large result that is more allocation than the
    /// transformation makes. Writing here instead leaves the engine's own work as the whole of the figure.
    /// What it does not show is what each call costs a real writer, which is why the output benchmarks
    /// set it beside one; see <see cref="Calls"/>.
    /// </remarks>
    internal sealed class CountingWriter : TextWriter
    {
        /// <summary>How many characters have been written.</summary>
        public long Characters { get; private set; }

        /// <summary>How many calls wrote them, which is what a real writer pays for.</summary>
        public long Calls { get; private set; }

        /// <inheritdoc/>
        public override Encoding Encoding => Encoding.UTF8;

        /// <inheritdoc/>
        public override void Write(char value)
        {
            Characters++;
            Calls++;
        }

        /// <inheritdoc/>
        public override void Write(char[] buffer, int index, int count)
        {
            Characters += count;
            Calls++;
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<char> buffer)
        {
            Characters += buffer.Length;
            Calls++;
        }

        /// <inheritdoc/>
        public override void Write(string? value)
        {
            Characters += value?.Length ?? 0;
            Calls++;
        }
    }

    /// <summary>A stream that counts the bytes it is given and keeps none of them.</summary>
    internal sealed class CountingStream : Stream
    {
        private long m_bytes;

        /// <inheritdoc/>
        public override bool CanRead => false;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override long Length => m_bytes;

        /// <inheritdoc/>
        public override long Position
        {
            get => m_bytes;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => m_bytes += count;

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer) => m_bytes += buffer.Length;
    }
}
