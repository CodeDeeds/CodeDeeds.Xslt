using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace CodeDeeds.Xslt.Benchmarks
{
    /// <summary>
    /// What the first transformation in a process costs, which is all a process that makes only one
    /// ever sees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other class here measures the engine once the runtime has finished optimising it, which
    /// takes some seconds and a few hundred transformations. A command-line tool, a build step or a
    /// function started for one request never gets there: what it pays is the first call, with every
    /// method it reaches compiled for the first time on the way. That figure is several times the settled
    /// one, and no amount of work on the settled one is sure to move it.
    /// </para>
    /// <para>
    /// The strategy is <see cref="RunStrategy.ColdStart"/> with one iteration to a launch: no warm-up,
    /// and one measurement from each of ten fresh processes, so that every figure is a first call.
    /// BenchmarkDotNet warns that an iteration this short should be given more operations; here that
    /// would be measuring the second call, so the warning is to be expected and left alone.
    /// </para>
    /// <para>
    /// The second pair separates the two halves. With the stylesheet compiled during setup, what is
    /// measured is the first transformation alone — though not an entirely cold one, since compiling a
    /// stylesheet has already run the XML reader and the tree builder.
    /// </para>
    /// </remarks>
    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.ColdStart, launchCount: 10, warmupCount: 0, iterationCount: 1)]
    public class ColdStartBenchmarks
    {
        private string m_stylesheetText = null!;
        private string m_small = null!;
        private string m_large = null!;
        private Xslt m_compiledDuringSetup = null!;

        [GlobalSetup]
        public void Setup()
        {
            m_stylesheetText = BenchmarkData.ProductsStylesheet();
            m_small = BenchmarkData.Products(100);
            m_large = BenchmarkData.Products(1000);
        }

        [GlobalSetup(Targets = new[] { nameof(FirstTransformSmall), nameof(FirstTransformLarge) })]
        public void SetupWithStylesheet()
        {
            Setup();
            m_compiledDuringSetup = new Xslt(m_stylesheetText);
        }

        [Benchmark(Description = "First compile and first transform, 100 products")]
        public string CompileAndTransformSmall()
        {
            return new Xslt(m_stylesheetText).TransformXml(m_small);
        }

        [Benchmark(Description = "First compile and first transform, 1000 products")]
        public string CompileAndTransformLarge()
        {
            return new Xslt(m_stylesheetText).TransformXml(m_large);
        }

        [Benchmark(Description = "First compile and first transform, 100 products, IL backend")]
        public string CompileToILAndTransformSmall()
        {
            return new Xslt(m_stylesheetText, new XsltOptions { Backend = XsltBackend.Compiled }).TransformXml(m_small);
        }

        [Benchmark(Description = "First transform of a stylesheet already compiled, 100 products")]
        public string FirstTransformSmall()
        {
            return m_compiledDuringSetup.TransformXml(m_small);
        }

        [Benchmark(Description = "First transform of a stylesheet already compiled, 1000 products")]
        public string FirstTransformLarge()
        {
            return m_compiledDuringSetup.TransformXml(m_large);
        }
    }

    /// <summary>
    /// How much of the engine's settled speed the runtime's profile-guided optimisation is supplying.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An interpreter is virtual calls from end to end — an expression evaluating its operands, an
    /// instruction executing its children — and those are what dynamic PGO is best at: it sees which
    /// implementation a call site reaches and compiles a guarded direct call, which can then be inlined.
    /// So the default figure owes a good deal to the runtime, and these jobs say how much by taking it
    /// away.
    /// </para>
    /// <para>
    /// It matters wherever the runtime cannot do it. Native AOT has no dynamic PGO at all, and a host
    /// may turn tiered compilation off; the third job is about what either would see. The gap between
    /// the first job and the third is also the measure of what making the hottest call sites direct by
    /// construction could recover for everybody.
    /// </para>
    /// </remarks>
    [Config(typeof(JitConfigurations))]
    public class JitSensitivityBenchmarks
    {
        private readonly CountingWriter m_writer = new CountingWriter();

        private Xslt m_stylesheet = null!;
        private string m_xml = null!;

        [GlobalSetup]
        public void Setup()
        {
            m_stylesheet = new Xslt(BenchmarkData.ProductsStylesheet());
            m_xml = BenchmarkData.Products(1000);
        }

        [Benchmark(Description = "Parse and transform 1000 products")]
        public long ParseAndTransform()
        {
            m_stylesheet.TransformXml(m_xml, m_writer);
            return m_writer.Characters;
        }

        private sealed class JitConfigurations : ManualConfig
        {
            public JitConfigurations()
            {
                AddDiagnoser(MemoryDiagnoser.Default);

                AddJob(Job.Default.WithId("Tiered with PGO (default)").AsBaseline());
                AddJob(Job.Default.WithEnvironmentVariable("DOTNET_TieredPGO", "0").WithId("Tiered, no PGO"));
                AddJob(Job.Default.WithEnvironmentVariable("DOTNET_TieredCompilation", "0").WithId("No tiering, no PGO"));
            }
        }
    }
}
