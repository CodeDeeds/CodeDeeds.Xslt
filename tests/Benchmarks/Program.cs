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
///   dotnet run -c Release -- --filter *SchemaAware*
///   dotnet run -c Release -- --filter *TransformPhase* *RowCost*
///
/// Benchmarks included:
/// 1. XsltCompilationBenchmarks - Measures XSLT stylesheet compilation performance
/// 2. XmlTransformationBenchmarks - Measures XML to HTML transformation performance
/// 3. JsonTransformationBenchmarks - Measures JSON to HTML transformation performance
/// 4. SchemaAwareBenchmarks - Measures what validating an input, and what validating a result, cost
/// 5. TransformPhaseBenchmarks - Parsing and transforming apart, and the reader's share of the parse
/// 6. ScalingBenchmarks - Whether the cost of a product holds from 100 products to 10,000
/// 7. RowCostBenchmarks - What a template call, an element, a value-of and a format-number each cost
/// 8. BackendBenchmarks - Interpreted against compiled, by kind of expression and by version
/// 9. OutputTargetBenchmarks - Each output target, written to sinks that allocate nothing themselves
/// 10. IdentityTransformBenchmarks - Copying a document through, three ways, and on the framework
/// 11. FrameworkComparisonBenchmarks, FrameworkLoadBenchmarks - Beside XslCompiledTransform
/// 12. ColdStartBenchmarks - The first compile and the first transform in a fresh process
/// 13. JitSensitivityBenchmarks - The same transform with and without the runtime's dynamic PGO
/// 14. PatternPredicateBenchmarks - A predicate in a match pattern, as the siblings multiply
///
/// BenchmarkDotNet finds this project by name under the solution's folder, so it refuses to run while a
/// second copy of the repository sits inside it - a git worktree under .claude/worktrees, for one.
/// </summary>

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args.Length == 0 ? new[] { "--filter", "*" } : args);

/// <summary>The entry point's type, which the switcher finds the benchmarks beside.</summary>
public static partial class Program
{
}
