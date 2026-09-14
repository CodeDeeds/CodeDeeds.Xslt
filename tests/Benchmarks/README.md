# CodeDeeds.Xslt.Benchmarks

Performance benchmarking suite for the CodeDeeds.Xslt library using BenchmarkDotNet.

## Overview

This project provides comprehensive performance benchmarks for the XSLT transformation engine. It tests the performance of:

1. **XSLT Compilation** - Measures the cost of compiling different XSLT stylesheets
2. **XML Transformation** - Benchmarks XML to HTML transformations with various input sizes
3. **JSON Transformation** - Benchmarks JSON to HTML transformations with various input sizes
4. **Schema awareness** - What validating an input, and validating what a stylesheet builds, cost over doing neither

## Projects & Files

### Benchmark Classes

- **XsltCompilationBenchmarks.cs** - Tests stylesheet compilation performance
  - `CompileXmlToHtmlStylesheet()` - Compiles XML to HTML stylesheet
  - `CompileJsonToHtmlStylesheet()` - Compiles JSON to HTML stylesheet

- **XmlTransformationBenchmarks.cs** - Tests XML transformation performance
  - Small dataset (100 products, 30 KB) transformations to string, TextWriter, and Stream
  - Large dataset (1000 products, 300 KB) transformations to string, TextWriter, and Stream

- **JsonTransformationBenchmarks.cs** - Tests JSON transformation performance
  - Small dataset (100 products, 23 KB) transformations to string, TextWriter, and Stream
  - Large dataset (1000 products, 230 KB) transformations to string, TextWriter, and Stream

- **SchemaAwareBenchmarks.cs** - What schema awareness costs, each measurement paired with the same work done without it
  - Reading 100 products untyped and with the input validated strictly
  - Building a 100-product document, validated strictly and not at all
  - Compiling a stylesheet that imports a schema

### Sample Data & Stylesheets

#### Data Files
- **Data/products.xml** - Sample e-commerce product catalog in XML format (100 products)
- **Data/products.json** - Sample e-commerce product catalog in JSON format (100 products)

#### Stylesheets
- **Stylesheets/ProductsXmlToHtml.xslt** - Transforms XML product data to HTML
  - Features: Product table, summary statistics, inline CSS styling
  - Uses: XPath functions, conditional logic, formatting

- **Stylesheets/ProductsJsonToHtml.xslt** - Transforms JSON product data to HTML
  - Features: Product table, multiple stat cards, enhanced styling
  - Uses: XPath 3.0 JSON functions, array operations, distinct-values()

## Running the Benchmarks

### Prerequisites
- .NET 10 SDK or later
- Release build recommended for accurate results

### Quick Start

```bash
cd source/CodeDeeds.Xslt.Benchmarks
dotnet run -c Release
```

With no arguments, every benchmark class runs in turn. A full run takes about ten minutes.

### Running Specific Benchmarks

Pass a BenchmarkDotNet filter after `--` to choose a class, or a single benchmark within one:

```bash
# Only the compilation benchmarks
dotnet run -c Release -- --filter *Compilation*

# Only the XML transformation benchmarks
dotnet run -c Release -- --filter *XmlTransformation*

# Only the JSON transformation benchmarks
dotnet run -c Release -- --filter *JsonTransformation*

# Only the schema-awareness benchmarks
dotnet run -c Release -- --filter *SchemaAware*

# One benchmark, by its method name
dotnet run -c Release -- --filter *TransformLargeXmlToString*
```

Every other BenchmarkDotNet switch is available the same way, such as `--job short` for a quick check or
`--exporters json` for a machine-readable report.

## Results

Measured 8 September 2026 on an Intel Core i7-6700K (4 cores, 4.00 GHz), Windows 10, .NET 10.0.11,
BenchmarkDotNet 0.15.8, one launch with three warmup iterations. Times are means; allocation is per operation.

### Compilation

| Benchmark | Backend | Mean | Allocated |
|---|---|---:|---:|
| Compile XML to HTML stylesheet | Interpreted | 268.8 μs | 117.7 KB |
| Compile XML to HTML stylesheet | Compiled | 377.6 μs | 155.1 KB |
| Compile JSON to HTML stylesheet | Interpreted | 116.2 μs | 69.9 KB |
| Compile JSON to HTML stylesheet | Compiled | 123.7 μs | 71.8 KB |

### XML transformation (compiled backend)

| Benchmark | Mean | Gen0 / Gen1 / Gen2 | Allocated |
|---|---:|---:|---:|
| Small XML (100 products) to string | 552.5 μs | 39 / 0 / 0 | 174.6 KB |
| Small XML (100 products) to TextWriter | 561.0 μs | 63 / 16 / 0 | 262.9 KB |
| Small XML (100 products) to Stream | 646.0 μs | 102 / 23 / 0 | 424.7 KB |
| Large XML (1000 products) to string | 5,599.7 μs | 281 / 242 / 148 | 1,470.4 KB |
| Large XML (1000 products) to TextWriter | 5,768.4 μs | 438 / 344 / 164 | 2,186.7 KB |
| Large XML (1000 products) to Stream | 6,419.8 μs | 734 / 633 / 430 | 3,261.5 KB |

### JSON transformation (compiled backend)

| Benchmark | Mean | Gen0 / Gen1 / Gen2 | Allocated |
|---|---:|---:|---:|
| Small JSON (100 products) to string | 161.1 μs | 16 / 0 / 0 | 67.3 KB |
| Small JSON (100 products) to TextWriter | 162.1 μs | 18 / 0 / 0 | 73.9 KB |
| Small JSON (100 products) to Stream | 169.3 μs | 30 / 7 / 0 | 124.3 KB |
| Large JSON (1000 products) to string | 1,441.8 μs | 123 / 43 / 0 | 517.9 KB |
| Large JSON (1000 products) to TextWriter | 1,439.6 μs | 109 / 43 / 0 | 524.5 KB |
| Large JSON (1000 products) to Stream | 1,429.3 μs | 121 / 72 / 0 | 574.9 KB |

Collection counts are per thousand operations. In the TextWriter and Stream rows the extra allocation over
the string row belongs to the benchmark itself, which builds a `StringWriter` or a `MemoryStream` and reads
the result back out; the engine's own work is the same in all three.

For scale: before the September 2026 performance work, the large XML transformation measured 12.9 ms and
12.0 MB per operation on the same machine, with a gen2 collection on every run, and the large JSON
transformation 3.0 ms and 5.6 MB with a gen2 collection every second run.

### Schema awareness

Measured 14 September 2026 on the same machine. Every pair transforms the same 100-product document with
the same stylesheet, so what separates the two members of a pair is the schema awareness and nothing else.

| Benchmark | Mean | Ratio | Allocated | Alloc ratio |
|---|---:|---:|---:|---:|
| Read 100 products, untyped | 258.4 μs | 1.00 | 76.2 KB | 1.00 |
| Read 100 products, input validated and typed | 488.7 μs | 1.89 | 189.6 KB | 2.49 |
| Build 100 products, nothing validated | 333.4 μs | 1.29 | 122.3 KB | 1.60 |
| Build 100 products, result validated strictly | 693.4 μs | 2.68 | 539.0 KB | 7.07 |
| Compile a stylesheet that imports the schema | 65.7 μs | 0.25 | 55.5 KB | 0.73 |

**Validating the input roughly doubles the read.** The document is parsed through a validating reader
rather than a plain one, and the elements and attributes it settles types for carry them, which is the
extra allocation. What the stylesheet then does with the typed values costs nothing extra: a sum over
typed prices is a sum over numbers rather than over strings that have to be cast.

**Validating what the stylesheet builds costs about the same again, and rather more memory.** The result
is built, walked into a validator, and written out carrying what validation settled, so the nodes exist
twice over for as long as that takes. It is asked for per instruction, so a stylesheet validating one
element of a large result pays for that element.

**Nothing is paid for not asking.** The untyped rows are the engine as it stands with no schema in sight,
and they are what they were before schema awareness was built: the option costs a field and a branch that
is never taken.

## Benchmark Methodology

Each benchmark class uses:
- **3 warmup iterations** - To stabilize JIT compilation and CPU state
- **1 launch** - Single process launch
- **Memory diagnostics** - Tracks memory allocations during transformations
- **Reasonable test count** - 5 target operations for statistical significance

## Sample Data

### Products Dataset
Both XML and JSON datasets contain 100 sample products with:
- Product ID, name, and category
- Price (USD) and availability status
- Product description (50+ characters)
- Customer rating (0-5 stars)

The benchmarks generate larger datasets (1000 products) dynamically by multiplying the base data.

## Output

BenchmarkDotNet generates detailed reports including:
- Mean execution time (ns, μs, ms)
- Minimum and maximum times
- Standard deviation
- Memory allocations per operation
- Allocated objects count

Results are displayed in the console and can be exported for comparison with other runs.

## Performance Considerations

### Compilation
- First-time compilation includes parsing, analysis, and code generation
- Subsequent uses can reuse the compiled stylesheet via the `With()` method
- Large or complex stylesheets take longer to compile

### Transformation
- String overloads require additional encoding/decoding overhead
- Stream-based transformations are more efficient for large datasets
- JSON parsing adds overhead compared to pre-parsed XML

### Memory
- Memory benchmarks show allocations per transformation
- Repeated transformations with the same stylesheet instance are most efficient
- Large output documents will show proportionally more allocations

## Extending the Benchmarks

To add new benchmarks:

1. Create new XSLT stylesheet in `Stylesheets/` folder
2. Add corresponding sample data in `Data/` folder
3. Create new benchmark class inheriting from appropriate base
4. Add `[Benchmark]` attributed methods
5. Update `Program.cs` to include new benchmark class

## License

Part of the CodeDeeds.Xslt project. See main repository for license information.
