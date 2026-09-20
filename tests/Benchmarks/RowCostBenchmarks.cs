using BenchmarkDotNet.Attributes;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// What each thing a template does costs, found by adding them one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every benchmark applies one template to each of a thousand products in a tree that is already
    /// parsed, and each template does a little more than the one before. The difference between two
    /// neighbours, over a thousand, is what the thing added costs per use: a template call, an element
    /// written, an <c>xsl:value-of</c>, a <c>format-number()</c>, an <c>xsl:choose</c>.
    /// </para>
    /// <para>
    /// The results are of different lengths, and a result is a string, so part of each allocation figure
    /// is the result itself at two bytes a character. The static text happens to come to about the length
    /// of the values it stands in for — the two results are within two per cent of each other — so the
    /// literal and <c>xsl:value-of</c> rows can be compared without correcting for that.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    public class RowCostBenchmarks
    {
        private static readonly string[] s_fields = { "id", "name", "category", "price", "description", "inStock", "rating" };

        private XdmTree m_tree = null!;
        private Xslt m_empty = null!;
        private Xslt m_oneElement = null!;
        private Xslt m_literal = null!;
        private Xslt m_literalUnindented = null!;
        private Xslt m_valueOf = null!;
        private Xslt m_formatNumber = null!;
        private Xslt m_choose = null!;

        [GlobalSetup]
        public void Setup()
        {
            m_tree = XdmTreeBuilder.FromXmlText(BenchmarkData.Products(1000));

            string literalCells = "<tr>" + string.Concat(s_fields.Select(_ => "<td>some text here</td>")) + "</tr>";
            string valueCells = "<tr>" + string.Concat(s_fields.Select(f => $"<td><xsl:value-of select=\"{f}\"/></td>")) + "</tr>";

            string formatted = BenchmarkData.Replace(valueCells, "select=\"price\"", "select=\"format-number(price, '0.00')\"");
            formatted = BenchmarkData.Replace(formatted, "select=\"rating\"", "select=\"format-number(rating, '0.0')\"");

            m_empty = new Xslt(Rows(string.Empty));
            m_oneElement = new Xslt(Rows("<tr/>"));
            m_literal = new Xslt(Rows(literalCells));
            m_literalUnindented = new Xslt(Rows(literalCells, indent: "no"));
            m_valueOf = new Xslt(Rows(valueCells));
            m_formatNumber = new Xslt(Rows(formatted));
            m_choose = new Xslt(Rows(
                "<tr><td><xsl:choose><xsl:when test=\"inStock='true'\">yes</xsl:when>"
                + "<xsl:otherwise>no</xsl:otherwise></xsl:choose></td></tr>"));
        }

        [Benchmark(Baseline = true, Description = "1000 template calls that write nothing")]
        public string EmptyTemplate() => m_empty.Transform(m_tree);

        [Benchmark(Description = "... each writing one empty element")]
        public string OneElement() => m_oneElement.Transform(m_tree);

        [Benchmark(Description = "... each writing 8 elements of static text")]
        public string LiteralRow() => m_literal.Transform(m_tree);

        [Benchmark(Description = "... the same, indent=no")]
        public string LiteralRowUnindented() => m_literalUnindented.Transform(m_tree);

        [Benchmark(Description = "... 8 elements, 7 xsl:value-of")]
        public string ValueOfRow() => m_valueOf.Transform(m_tree);

        [Benchmark(Description = "... 8 elements, 5 xsl:value-of, 2 format-number")]
        public string FormatNumberRow() => m_formatNumber.Transform(m_tree);

        [Benchmark(Description = "... 2 elements and an xsl:choose on inStock='true'")]
        public string ChooseRow() => m_choose.Transform(m_tree);

        private static string Rows(string body, string indent = "yes")
        {
            return "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + $"<xsl:output method=\"html\" indent=\"{indent}\" encoding=\"UTF-8\"/>"
                + "<xsl:template match=\"/\"><html><body><table><tbody>"
                + "<xsl:apply-templates select=\"//product\" mode=\"row\"/>"
                + "</tbody></table></body></html></xsl:template>"
                + $"<xsl:template match=\"product\" mode=\"row\">{body}</xsl:template>"
                + "</xsl:stylesheet>";
        }
    }
}
