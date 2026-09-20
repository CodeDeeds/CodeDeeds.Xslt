using System.Text;
using BenchmarkDotNet.Attributes;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// What a predicate in a match pattern costs as the siblings multiply.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pattern's predicate is evaluated with a context position and size, which are the candidate's place
    /// among the nodes its step selects — its siblings, on the child axis. Establishing them means
    /// enumerating those siblings, once for every candidate the pattern is tried against. Where the
    /// predicate reads a position that is the price of the answer. Where it cannot — <c>[@type='a']</c>
    /// reads none — nothing needs counting, and a pattern that counts anyway does work that grows with
    /// the square of the list.
    /// </para>
    /// <para>
    /// <see cref="ItemCount"/> steps by four. Read each row across its three sizes: a cost that is linear
    /// in the list goes up four times a step, and one that enumerates the siblings per candidate sixteen
    /// times. <see cref="ChooseInTemplate"/> is the same stylesheet with the test moved out of the pattern
    /// and into the template, which enumerates nothing, and is what a linear row looks like.
    /// </para>
    /// <para>
    /// The first four stylesheets give the same result, the items being typed <c>a</c> and <c>b</c> by
    /// turns so that the odd positions are the <c>a</c>s, and the setup refuses to go on if they do not:
    /// they are four ways of writing one transformation. <see cref="AttributePredicateInGroups"/> runs the
    /// second of them over the same number of items ten to a parent, where no candidate has more than nine
    /// siblings. If the cost follows the siblings and not the size of the document, that row stays linear
    /// whatever the others do.
    /// </para>
    /// <para>
    /// <see cref="BarePathPredicate"/> is a different transformation, every item having a type, and is
    /// there for what it allocates rather than for how it grows: <c>[@type]</c> is a predicate whose value
    /// is a node-set, which a pattern has no use for beyond whether it is empty. Asked for that alone it
    /// should allocate what <see cref="AttributePredicate"/> does, a comparison never having made one.
    /// </para>
    /// <para>
    /// Everything runs against a tree already parsed, and writes one small element per item, so that what
    /// grows is the matching and little else.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    public class PatternPredicateBenchmarks
    {
        private const int GroupSize = 10;

        private XdmTree m_flat = null!;
        private XdmTree m_grouped = null!;

        private Xslt m_choose = null!;
        private Xslt m_attributePredicate = null!;
        private Xslt m_wildcardPredicate = null!;
        private Xslt m_positionalPredicate = null!;
        private Xslt m_firstOnly = null!;
        private Xslt m_attributePredicateInGroups = null!;
        private Xslt m_barePathPredicate = null!;

        /// <summary>How many items the list holds.</summary>
        [Params(1000, 4000, 16000)]
        public int ItemCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            m_flat = XdmTreeBuilder.FromXmlText(Items(ItemCount, grouped: false));
            m_grouped = XdmTreeBuilder.FromXmlText(Items(ItemCount, grouped: true));

            const string Rest = "<xsl:template match=\"item\"><b/></xsl:template>";

            m_choose = new Xslt(Sheet(
                "list/item",
                "<xsl:template match=\"item\"><xsl:choose><xsl:when test=\"@type='a'\"><a/></xsl:when>"
                + "<xsl:otherwise><b/></xsl:otherwise></xsl:choose></xsl:template>"));

            m_attributePredicate = new Xslt(Sheet(
                "list/item", "<xsl:template match=\"item[@type='a']\"><a/></xsl:template>" + Rest));

            m_wildcardPredicate = new Xslt(Sheet(
                "list/item",
                "<xsl:template match=\"*[@type='a']\"><a/></xsl:template><xsl:template match=\"*\"><b/></xsl:template>"));

            m_positionalPredicate = new Xslt(Sheet(
                "list/item", "<xsl:template match=\"item[position() mod 2 = 1]\"><a/></xsl:template>" + Rest));

            m_firstOnly = new Xslt(Sheet(
                "list/item", "<xsl:template match=\"item[1]\"><a/></xsl:template>" + Rest));

            m_attributePredicateInGroups = new Xslt(Sheet(
                "list/group/item", "<xsl:template match=\"item[@type='a']\"><a/></xsl:template>" + Rest));

            m_barePathPredicate = new Xslt(Sheet(
                "list/item", "<xsl:template match=\"item[@type]\"><a/></xsl:template>" + Rest));

            // Four spellings of one transformation, and a fifth over the grouped list: if they disagree,
            // one of them is not measuring what its name says.
            string expected = m_choose.Transform(m_flat);

            Require(m_attributePredicate.Transform(m_flat), expected, nameof(AttributePredicate));
            Require(m_wildcardPredicate.Transform(m_flat), expected, nameof(WildcardAttributePredicate));
            Require(m_positionalPredicate.Transform(m_flat), expected, nameof(PositionalPredicate));
            Require(m_attributePredicateInGroups.Transform(m_grouped), expected, nameof(AttributePredicateInGroups));

            // Every item has a type, so the bare path matches them all.
            Require(
                m_barePathPredicate.Transform(m_flat),
                expected.Replace("<b/>", "<a/>", StringComparison.Ordinal),
                nameof(BarePathPredicate));
        }

        [Benchmark(Baseline = true, Description = "match=\"item\", the test in an xsl:choose")]
        public string ChooseInTemplate() => m_choose.Transform(m_flat);

        [Benchmark(Description = "match=\"item[@type='a']\"")]
        public string AttributePredicate() => m_attributePredicate.Transform(m_flat);

        [Benchmark(Description = "match=\"*[@type='a']\"")]
        public string WildcardAttributePredicate() => m_wildcardPredicate.Transform(m_flat);

        [Benchmark(Description = "match=\"item[position() mod 2 = 1]\"")]
        public string PositionalPredicate() => m_positionalPredicate.Transform(m_flat);

        [Benchmark(Description = "match=\"item[1]\"")]
        public string FirstOnly() => m_firstOnly.Transform(m_flat);

        [Benchmark(Description = "match=\"item[@type]\", which every item matches")]
        public string BarePathPredicate() => m_barePathPredicate.Transform(m_flat);

        [Benchmark(Description = "match=\"item[@type='a']\", the items ten to a parent")]
        public string AttributePredicateInGroups() => m_attributePredicateInGroups.Transform(m_grouped);

        private static string Sheet(string select, string templates)
        {
            return "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:output method=\"xml\" indent=\"no\" omit-xml-declaration=\"yes\"/>"
                + $"<xsl:template match=\"/\"><out><xsl:apply-templates select=\"{select}\"/></out></xsl:template>"
                + templates
                + "</xsl:stylesheet>";
        }

        /// <summary>
        /// A list of items typed <c>a</c> and <c>b</c> by turns, with no whitespace between them, either
        /// all under one parent or <see cref="GroupSize"/> to a group.
        /// </summary>
        private static string Items(int count, bool grouped)
        {
            StringBuilder xml = new StringBuilder("<list>");

            for (int i = 0; i < count; i++)
            {
                if (grouped && i % GroupSize == 0)
                {
                    xml.Append(i == 0 ? "<group>" : "</group><group>");
                }

                xml.Append(i % 2 == 0 ? "<item type=\"a\"/>" : "<item type=\"b\"/>");
            }

            return xml.Append(grouped ? "</group></list>" : "</list>").ToString();
        }

        private static void Require(string actual, string expected, string benchmark)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{benchmark} does not give the result the baseline gives, so the two are not the same transformation.");
            }
        }
    }
}
