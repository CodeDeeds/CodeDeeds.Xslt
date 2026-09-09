using BenchmarkDotNet.Running;

/// <summary>
/// BenchmarkDotNet runner for CodeDeeds.Xslt performance testing.
///
/// Run with: dotnet run -c Release
///
/// With no arguments every benchmark class runs. To choose, pass a BenchmarkDotNet filter after "--":
///   dotnet run -c Release -- --filter *Compilation*
///   dotnet run -c Release -- --filter *XmlTransformation*
///   dotnet run -c Release -- --filter *JsonTransformation*
///
/// Benchmarks included:
/// 1. XsltCompilationBenchmarks - Measures XSLT stylesheet compilation performance
/// 2. XmlTransformationBenchmarks - Measures XML to HTML transformation performance
/// 3. JsonTransformationBenchmarks - Measures JSON to HTML transformation performance
/// </summary>

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args.Length == 0 ? new[] { "--filter", "*" } : args);

/// <summary>The entry point's type, which the switcher finds the benchmarks beside.</summary>
public static partial class Program
{
}
