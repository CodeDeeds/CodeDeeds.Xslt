# CodeDeeds.Xslt.Benchmarks

Performance benchmarking suite for the CodeDeeds.Xslt library using BenchmarkDotNet.

## Overview

This project provides comprehensive performance benchmarks for the XSLT transformation engine. It tests the performance of:

1. **XSLT Compilation** - Measures the cost of compiling different XSLT stylesheets
2. **XML Transformation** - Benchmarks XML to HTML transformations with various input sizes
3. **JSON Transformation** - Benchmarks JSON to HTML transformations with various input sizes
4. **Schema awareness** - What validating an input, and validating what a stylesheet builds, cost over doing neither
5. **Where the time goes** - Parsing and transforming apart, what each thing a template does costs, and whether the cost of a product holds as the document grows
6. **The two backends** - Interpreted against compiled, by kind of expression and by the version the stylesheet declares
7. **Output targets and identity transforms** - Each target written to a sink that allocates nothing of its own, and the three ways of copying a document through
8. **The framework's processor** - The same work on `System.Xml.Xsl.XslCompiledTransform`, for scale
9. **Cold start and the JIT** - The first call in a fresh process, and how much of the settled speed is the runtime's dynamic PGO
10. **Predicates in match patterns** - What `match="item[@type='a']"` costs as the siblings multiply

Items 5 to 10 came out of the performance review of 19 September 2026
(`source/CodeDeeds.Xslt/Documentation/PerformanceReview-2026-09-19.md`), which says what each was built to find out.

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

- **TransformPhaseBenchmarks.cs** - The thousand-product transformation taken apart
  - A bare `XmlReader` loop, which is the floor under the parse and outside this library's reach
  - The parse to an `XdmTree`, and to the framework's `XPathDocument` for scale
  - The transformation of a tree already parsed, and the two together
  - `ScalingBenchmarks` in the same file: end to end at 100, 1,000 and 10,000 products; the mean over the count should not grow

- **RowCostBenchmarks.cs** - One template applied to a thousand products, each benchmark's template doing a little more than the last
  - The difference between neighbours, over a thousand, is the cost of a template call, an element, an `xsl:value-of`, a `format-number()` or an `xsl:choose`

- **BackendBenchmarks.cs** - `Interpreted` against `Compiled`, four ways: both backends at versions 1.0 and 3.0
  - The whole products stylesheet, `count(//product)`, a string-equality predicate, and two numeric predicates under `and`
  - Read the two backends at one version as a pair; what the compiled backend emits for a comparison depends on the version

- **OutputTargetBenchmarks.cs** - The same transformation to a string, a `TextWriter` and a `Stream`
  - Written to sinks that keep nothing, so that no `StringWriter` or `MemoryStream` of the benchmark's own is in the figure
  - A caller's `StreamWriter` beside them, because a sink that does nothing per call hides what each of about 100,000 `Write` calls costs a real writer

- **IdentityTransformBenchmarks.cs** - `xsl:copy-of`, `xsl:mode on-no-match="shallow-copy"` and the identity template, and the identity template on the framework

- **FrameworkComparisonBenchmarks.cs** - This engine beside `XslCompiledTransform` at 100 and 1,000 products, to a string and to a sink
  - This engine runs the products stylesheet as written; the framework runs the same stylesheet with `avg()` spelt as a sum over a count, at version 1.0
  - `FrameworkLoadBenchmarks` in the same file: making a stylesheet ready, here on both backends and in the framework

- **ColdStartBenchmarks.cs** - The first compile and the first transform, one measurement from each of ten fresh processes
  - `JitSensitivityBenchmarks` in the same file: one transformation under the default JIT, with dynamic PGO off, and with tiered compilation off

- **PatternPredicateBenchmarks.cs** - A predicate in a match pattern, over flat lists of 1,000, 4,000 and 16,000 items
  - Five ways of writing one transformation, the first with the test in the template instead of the pattern; the setup refuses to run if they disagree
  - The same pattern over the same items ten to a parent, which shows whether the cost follows the siblings or the document

- **BenchmarkData.cs** - The documents and stylesheets the classes above share, and the counting sinks

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
cd tests/Benchmarks
dotnet run -c Release
```

With no arguments, every benchmark class runs in turn. The first four classes take about nine minutes between
them, and the rest about twenty-two more: they use BenchmarkDotNet's default job, which warms up until the
measurements settle. `CompileDocbookOnlineStylesheet` fetches its stylesheet over the network on every iteration and needs a connection.

BenchmarkDotNet finds this project by its name under the solution's folder and refuses to run if it finds
two. A git worktree inside the repository — one under `.claude/worktrees`, for instance — is a second copy:
remove the worktree, or run from a copy of the working tree outside the repository.

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

# Several classes at once
dotnet run -c Release -- --filter *TransformPhase* *RowCost* *OutputTarget*

# The cold-start measurements, which take about half a minute
dotnet run -c Release -- --filter *ColdStart*

# One benchmark, by its method name
dotnet run -c Release -- --filter *TransformLargeXmlToString*
```

Every other BenchmarkDotNet switch is available the same way, such as `--job short` for a quick check or
`--exporters json` for a machine-readable report.

## Results

Measured 5 October 2026 on an Intel Core i7-6700K (4 cores, 4.00 GHz), Windows 10, .NET 10.0.12,
BenchmarkDotNet 0.15.8, at commit `688e32b`: every class in one run, 102 cases in 31 minutes. Times are
means; allocation is per operation. Run from a copy of the working tree, because a git worktree inside the
repository stops BenchmarkDotNet finding the project.

### Compilation

| Benchmark | Backend | Mean | Allocated |
|---|---|---:|---:|
| Compile XML to HTML stylesheet | Interpreted | 165.7 μs | 121.7 KB |
| Compile XML to HTML stylesheet | Compiled | 295.1 μs | 168.9 KB |
| Compile JSON to HTML stylesheet | Interpreted | 103.5 μs | 72.2 KB |
| Compile JSON to HTML stylesheet | Compiled | 112.8 μs | 74.2 KB |
| Compile Docbook online stylesheet | Interpreted | 1,805 ms | 27.0 MB |
| Compile Docbook online stylesheet | Compiled | 2,025 ms | 37.5 MB |

The DocBook stylesheet imports the xslTNG library and fetches it over the network on every iteration, so its
time is the connection's as well as the compiler's.

### XML transformation (compiled backend)

| Benchmark | Mean | Gen0 / Gen1 / Gen2 | Allocated |
|---|---:|---:|---:|
| Small XML (100 products) to string | 538.9 μs | 35 / 0 / 0 | 157.2 KB |
| Small XML (100 products) to TextWriter | 544.0 μs | 59 / 12 / 0 | 245.5 KB |
| Small XML (100 products) to Stream | 615.9 μs | 98 / 23 / 0 | 407.3 KB |
| Large XML (1000 products) to string | 5,522.8 μs | 273 / 211 / 156 | 1,296.7 KB |
| Large XML (1000 products) to TextWriter | 5,598.3 μs | 398 / 328 / 164 | 2,012.9 KB |
| Large XML (1000 products) to Stream | 6,223.9 μs | 695 / 617 / 430 | 3,087.7 KB |

### JSON transformation (compiled backend)

| Benchmark | Mean | Gen0 / Gen1 / Gen2 | Allocated |
|---|---:|---:|---:|
| Small JSON (100 products) to string | 157.2 μs | 16 / 2 / 0 | 65.8 KB |
| Small JSON (100 products) to TextWriter | 156.3 μs | 18 / 0 / 0 | 72.4 KB |
| Small JSON (100 products) to Stream | 162.3 μs | 29 / 4 / 0 | 122.8 KB |
| Large JSON (1000 products) to string | 1,397.8 μs | 119 / 12 / 0 | 516.4 KB |
| Large JSON (1000 products) to TextWriter | 1,393.5 μs | 104 / 66 / 0 | 523.0 KB |
| Large JSON (1000 products) to Stream | 1,399.1 μs | 117 / 55 / 0 | 573.4 KB |

Collection counts are per thousand operations. In the TextWriter and Stream rows the extra allocation over
the string row belongs to the benchmark itself, which builds a `StringWriter` or a `MemoryStream` and reads
the result back out; the engine's own work is the same in all three.

For scale: before the September 2026 performance work, the large XML transformation measured 12.9 ms and
12.0 MB per operation on the same machine, with a gen2 collection on every run, and the large JSON
transformation 3.0 ms and 5.6 MB with a gen2 collection every second run.

### Schema awareness

Every pair transforms the same 100-product document with the same stylesheet, so what separates the two
members of a pair is the schema awareness and nothing else.

| Benchmark | Mean | Ratio | Allocated | Alloc ratio |
|---|---:|---:|---:|---:|
| Read 100 products, untyped | 248.9 μs | 1.00 | 76.5 KB | 1.00 |
| Read 100 products, input validated and typed | 495.3 μs | 1.99 | 190.3 KB | 2.49 |
| Build 100 products, nothing validated | 343.4 μs | 1.38 | 122.5 KB | 1.60 |
| Build 100 products, result validated strictly | 818.3 μs | 3.29 | 605.8 KB | 7.92 |
| Compile a stylesheet that imports the schema | 38.5 μs | 0.15 | 54.6 KB | 0.71 |

**Validating the input roughly doubles the read.** The document is parsed through a validating reader
rather than a plain one, and the elements and attributes it settles types for carry them, which is the
extra allocation. What the stylesheet then does with the typed values costs nothing extra: a sum over
typed prices is a sum over numbers rather than over strings that have to be cast.

**Validating what the stylesheet builds costs more: 2.4 times the build, and 4.9 times its memory.** The
result is built, walked into a validator, and written out carrying what validation settled, so the nodes
exist twice over for as long as that takes. It is asked for per instruction, so a stylesheet validating one
element of a large result pays for that element.

**Nothing is paid for not asking.** The untyped rows are the engine as it stands with no schema in sight,
and they are what they were before schema awareness was built: the option costs a field and a branch that
is never taken.

### The review's benchmarks

The classes from here on came out of the performance review of 19 September 2026 and use BenchmarkDotNet's
default job with memory diagnostics. Every transformation here is of the 1,000-product document unless it
says otherwise, and uses the default `Interpreted` backend unless it names one. What each table was built to
find out is in `source/CodeDeeds.Xslt/Documentation/PerformanceReview-2026-09-19.md`, whose own figures are
the ones measured then; where a table here has moved a long way since, the text under it says from what.

#### Where the time goes

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Parse floor: bare `XmlReader`, every value read | 1.017 ms | 650.1 KB |
| Parse to an `XdmTree` | 1.983 ms | 1,669.6 KB |
| Parse to an `XPathDocument` (framework) | 2.113 ms | 923.9 KB |
| Transform a tree already parsed | 3.784 ms | 871.5 KB |
| Parse and transform | 5.558 ms | 1,296.5 KB |

Half of the parse is the framework's reader. The parse on its own allocates more than the parse inside a
transformation, which takes the tree's arrays from a pool and gives them back; 697 KB of each transformation's
allocation is the result string.

| Products | Mean | Per product | Allocated |
|---:|---:|---:|---:|
| 100 | 540.0 μs | 5.40 μs | 157.2 KB |
| 1,000 | 5,489.9 μs | 5.49 μs | 1,296.5 KB |
| 10,000 | 63,617.1 μs | 6.36 μs | 12,697.4 KB |

#### What a row costs

One template applied to each of 1,000 products in a tree already parsed.

| Template body | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Nothing | 102.9 μs | 1.00 | 4.7 KB |
| One empty element | 195.4 μs | 1.90 | 40.5 KB |
| 8 elements of static text | 1,164.4 μs | 11.32 | 523.1 KB |
| The same, `indent="no"` | 1,087.4 μs | 10.57 | 337.7 KB |
| 8 elements, 7 `xsl:value-of` | 1,751.6 μs | 17.03 | 516.8 KB |
| 8 elements, 5 `xsl:value-of`, 2 `format-number` | 2,191.6 μs | 21.30 | 582.2 KB |
| 2 elements and an `xsl:choose` on `inStock='true'` | 494.2 μs | 4.80 | 102.9 KB |

By subtraction, and no steadier than the rows it is taken from, of which the `xsl:value-of` row has read
1.75 to 2.04 ms in three runs: about 103 ns for a template call, 133 ns for an element with its text and indentation, 84 ns
for an `xsl:value-of` of a child, and 220 ns and 33 bytes for a `format-number()`.

#### The two backends

| Benchmark | Version | Interpreted | Compiled |
|---|---|---:|---:|
| The whole products stylesheet | 1.0 | 3,546.0 μs | 3,379.1 μs |
| The whole products stylesheet | 3.0 | 3,832.3 μs | 3,821.0 μs |
| `count(//product)` | 1.0 | 15.88 μs | 15.90 μs |
| `count(//product)` | 3.0 | 15.71 μs | 15.61 μs |
| `count(//product[inStock='true'])` | 1.0 | 111.54 μs | 93.72 μs |
| `count(//product[inStock='true'])` | 3.0 | 127.94 μs | 89.03 μs |
| `count(//product[price > 100 and rating > 4])` | 1.0 | 177.55 μs | 128.51 μs |
| `count(//product[price > 100 and rating > 4])` | 3.0 | 179.75 μs | 124.05 μs |

Allocation is the same for both backends in every row. The compiled backend gains nothing on the whole
stylesheet or on `count(//product)` at either version, and 1.2 to 1.4 times on the two predicates at both.
When this table was first made the two versions were far apart on the compiled side, and that was a fault
and not a gain: the emitted code read a comparison under `and` or `or` by 1.0's rules whatever the version.
The route a comparison takes is settled once now, where the expression is built, and both backends take it.
Making a stylesheet ready costs 165.2 μs interpreted and 280.2 μs compiled (121.7 and 168.9 KB), against
424.8 μs and 268.4 KB for `XslCompiledTransform.Load`.

#### Output targets

| Target | Mean | Ratio | Gen0 / Gen1 / Gen2 | Allocated |
|---|---:|---:|---:|---:|
| A `TextWriter` that keeps nothing | 5.079 ms | 1.00 | 117 / 47 / 0 | 599.1 KB |
| A caller's `StreamWriter` over a stream that keeps nothing | 5.572 ms | 1.10 | 109 / 70 / 0 | 604.4 KB |
| A `Stream` that keeps nothing | 5.518 ms | 1.09 | 125 / 62 / 0 | 637.6 KB |
| A string | 5.611 ms | 1.10 | 273 / 211 / 156 | 1,296.5 KB |

These are the engine's own figures for the three targets: set them beside 1,297, 2,013 and 3,088 KB in the XML
transformation table above, where the difference is the benchmark's. The three real targets cost the same time;
the tenth they cost over the first row is what about 100,000 `Write` calls cost a writer that does any work.
Only the string collects in generation 2, its result being on the large object heap.

#### Identity transforms

| Stylesheet | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| `xsl:copy-of select="."` | 2.651 ms | 1.00 | 429.3 KB |
| `xsl:mode on-no-match="shallow-copy"` | 3.337 ms | 1.26 | 428.8 KB |
| The identity template, `match="@*\|node()"` | 6.984 ms | 2.63 | 429.2 KB |
| The identity template on `XslCompiledTransform` (framework) | 6.081 ms | 2.29 | 2,995.2 KB |

The identity template allocated 2,250 KB when the review measured it, a new list of nodes for every
`@*|node()` it applied templates to. The union is gathered into a list it is lent now, and the template
allocates what the other two do. It is still 2.63 times `xsl:copy-of`, and 15% slower than the same template
on the framework, where it was 24%.

#### Predicates in match patterns

One template applied to each item of a flat list in a tree already parsed. The first row is the same
transformation with the test moved out of the pattern, and the setup checks that the first four stylesheets
give identical results. Each step is four times the items, so a linear cost goes up four times a step and
one that enumerates the siblings per candidate sixteen.

| Pattern | 1,000 items | 4,000 items | 16,000 items | Allocated at 16,000 |
|---|---:|---:|---:|---:|
| `match="item"`, the test in an `xsl:choose` | 287.6 μs | 1,042.1 μs | 4,435.3 μs | 129.1 KB |
| `match="item[@type='a']"` | 281.3 μs | 1,104.5 μs | 4,652.4 μs | 129.1 KB |
| `match="*[@type='a']"` | 269.7 μs | 1,079.0 μs | 5,116.6 μs | 128.8 KB |
| `match="item[position() mod 2 = 1]"` | 379.1 μs | 1,499.2 μs | 6,120.9 μs | 257.7 KB |
| `match="item[1]"` | 257.5 μs | 1,037.0 μs | 4,242.0 μs | 257.7 KB |
| `match="item[@type]"`, which every item matches | 223.3 μs | 876.0 μs | 3,813.4 μs | 129.1 KB |
| `match="item[@type='a']"`, the items ten to a parent | 278.4 μs | 1,121.9 μs | 4,748.2 μs | 129.2 KB |

Every row goes up 3.6 to 4.7 times for four times the items, so each is linear in the list, and at 16,000
items the rows are 0.9 to 1.4 times the test written in the template.

It was not so when this class was first run, on 19 September 2026. A pattern with a predicate enumerated the
candidate's siblings on every test, whether or not the predicate could read a position, and its cost was the
square of the sibling count: `match="item[@type='a']"` took 664 ms at 16,000 items where it takes 4.7 ms now,
146 times the first row, and allocated two gigabytes on the way. A step whose predicates cannot read a
position enumerates nothing now, and one whose predicates can keeps what it selected from the last parent,
so that the siblings are selected once per parent. `position() mod 2 = 1` made some 430 bytes each time it
was evaluated, 6,948 KB at 16,000 items, and is 258 KB now.

Two shapes this class does not measure were still the square at this commit, and have been made linear
since: a step where a later predicate counts among what an earlier one left, `item[@type='a'][2]`, which took
158 ms over 2,000 items and 2.6 s over 8,000 and takes 0.9 ms and 3.6 ms; and a step on the `descendant::`
axis whose predicate reads a position, `list/descendant::item[2]`, which took 22 ms and 338 ms and takes
0.9 ms and 3.6 ms. What is left of the square is the two at once, `list/descendant::item[@type='a'][2]`, and
a step whose earlier predicate reads `current()` or a variable, which has to be counted for each candidate.

#### Beside the framework's processor

| Products | Target | CodeDeeds.Xslt | `XslCompiledTransform` | Allocated |
|---:|---|---:|---:|---|
| 100 | A string | 548.2 μs | 545.2 μs | 157.2 against 421.4 KB |
| 100 | A writer that keeps nothing | 509.8 μs | 534.2 μs | 84.8 against 280.6 KB |
| 1,000 | A string | 5,546.6 μs | 6,366.5 μs | 1,296.7 against 3,555.0 KB |
| 1,000 | A writer that keeps nothing | 5,109.2 μs | 5,919.8 μs | 599.1 against 2,203.7 KB |

At 100 products the two are within 5% of each other. At 1,000 this engine is 13% faster to a string and 14%
faster to a writer, and at either size it allocates 27 to 37% of what the framework does.

#### Cold start and the JIT

One measurement from each of ten fresh processes, with no warm-up.

| First call in a fresh process | Mean | StdDev | Allocated |
|---|---:|---:|---:|
| First compile and first transform, 100 products | 183.43 ms | 0.95 ms | 395.2 KB |
| First compile and first transform, 1,000 products | 219.93 ms | 0.84 ms | 2,546.8 KB |
| First compile and first transform, 100 products, IL backend | 192.07 ms | 3.05 ms | 448.0 KB |
| First transform of a stylesheet already compiled, 100 products | 51.53 ms | 0.58 ms | 271.8 KB |
| First transform of a stylesheet already compiled, 1,000 products | 88.48 ms | 0.74 ms | 2,423.4 KB |

Those include the runtime loading `System.Xml` and compiling every method on the way for the first time, and
nothing here separates that from the engine's own share.

| JIT configuration | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Tiered with dynamic PGO (the default) | 4.990 ms | 1.00 | 599.1 KB |
| Tiered, `DOTNET_TieredPGO=0` | 6.824 ms | 1.37 | 599.2 KB |
| `DOTNET_TieredCompilation=0` | 9.415 ms | 1.89 | 599.1 KB |

## Benchmark Methodology

The first four classes (compilation, XML and JSON transformation, schema awareness) use:
- **3 warmup iterations** - To stabilize JIT compilation and CPU state
- **1 launch** - Single process launch
- **Memory diagnostics** - Tracks memory allocations during transformations
- **Reasonable test count** - 5 target operations for statistical significance

The classes added after the September 2026 review use BenchmarkDotNet's default job instead, with memory
diagnostics. This library is large enough that the runtime takes some seconds of running to finish optimising
it — about five, measured on the thousand-product transformation — and the default job warms up until the
measurements stop moving rather than for a count fixed in advance. `ColdStartBenchmarks` is the exception by
design: no warm-up at all, and one measurement from each of ten fresh processes.

Several of the newer classes write to `CountingWriter` and `CountingStream`, which keep nothing, rather than
to a `StringWriter` or a `MemoryStream`. What they report is then the engine's allocation and not the
benchmark's. The cost they hide is the per-call cost of a real writer, which `OutputTargetBenchmarks`
measures separately.

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
- A string, a `TextWriter` and a `Stream` cost the same time to within 2%; see Output targets above
- What differs is memory: the string overloads allocate the result, two bytes a character, and a large one lands on the large object heap, so the `TextWriter` and `Stream` overloads are the ones for large results
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
