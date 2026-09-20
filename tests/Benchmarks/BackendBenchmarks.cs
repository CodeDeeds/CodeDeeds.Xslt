using BenchmarkDotNet.Attributes;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// What <see cref="XsltBackend.Compiled"/> gains over <see cref="XsltBackend.Interpreted"/>, by the
    /// kind of expression and by the version the stylesheet declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gain is not one number. What the compiled backend emits depends on the expression, and for a
    /// comparison it depends on the version as well: under 2.0 and later the emitted code calls back into
    /// the interpreter, because which comparison a pair of operands calls for is decided by their types.
    /// So every measurement here is taken four ways, and the pairs to read are the two backends at one
    /// version.
    /// </para>
    /// <para>
    /// Everything runs against a tree that is already parsed. Parsing is a third of an end-to-end
    /// transformation and no backend touches it, so leaving it in would only blur the comparison.
    /// </para>
    /// <para>
    /// The whole stylesheet is the portable form of the products stylesheet at both versions, so that the
    /// version is the only thing that differs between them; see
    /// <see cref="BenchmarkData.PortableProductsStylesheet"/>.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    public class BackendBenchmarks
    {
        private XdmTree m_tree = null!;
        private Xslt m_whole = null!;
        private Xslt m_count = null!;
        private Xslt m_stringPredicate = null!;
        private Xslt m_numericPredicates = null!;

        /// <summary>The backend the stylesheets are compiled for.</summary>
        [ParamsAllValues]
        public XsltBackend Backend { get; set; }

        /// <summary>The version the stylesheets declare, which settles how a comparison is read.</summary>
        [Params("1.0", "3.0")]
        public string Version { get; set; } = "3.0";

        [GlobalSetup]
        public void Setup()
        {
            m_tree = XdmTreeBuilder.FromXmlText(BenchmarkData.Products(1000));

            XsltOptions options = new XsltOptions { Backend = Backend };

            m_whole = new Xslt(BenchmarkData.PortableProductsStylesheet(Version), options);
            m_count = new Xslt(BenchmarkData.SingleExpression("count(//product)", Version), options);
            m_stringPredicate = new Xslt(
                BenchmarkData.SingleExpression("count(//product[inStock='true'])", Version), options);
            m_numericPredicates = new Xslt(
                BenchmarkData.SingleExpression("count(//product[price &gt; 100 and rating &gt; 4])", Version), options);
        }

        [Benchmark(Description = "The whole products stylesheet")]
        public string WholeStylesheet() => m_whole.Transform(m_tree);

        [Benchmark(Description = "count(//product)")]
        public string CountDescendants() => m_count.Transform(m_tree);

        [Benchmark(Description = "count(//product[inStock='true'])")]
        public string StringEqualityPredicate() => m_stringPredicate.Transform(m_tree);

        [Benchmark(Description = "count(//product[price > 100 and rating > 4])")]
        public string NumericPredicatesUnderAnd() => m_numericPredicates.Transform(m_tree);
    }
}
