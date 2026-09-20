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

With no arguments, every benchmark class runs in turn. The first four classes take about ten minutes between
them, and the rest about twenty-three more: they use BenchmarkDotNet's default job, which warms up until the
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

### The review's benchmarks

Measured 19 September 2026 on the same machine, at commit `5ac67f5`, .NET 10.0.12, BenchmarkDotNet 0.15.8,
default job with memory diagnostics; 53 cases in 16 minutes, and the cold-start class in 37 seconds. Run from a
copy of the working tree, because a git worktree inside the repository stops BenchmarkDotNet finding the
project. Every transformation here is of the 1,000-product document unless it says otherwise, and uses the
default `Interpreted` backend unless it names one. What each table was built to find out is in
`source/CodeDeeds.Xslt/Documentation/PerformanceReview-2026-09-19.md`.

#### Where the time goes

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Parse floor: bare `XmlReader`, every value read | 1.078 ms | 650.1 KB |
| Parse to an `XdmTree` | 2.066 ms | 1,669.9 KB |
| Parse to an `XPathDocument` (framework) | 2.171 ms | 923.9 KB |
| Transform a tree already parsed | 4.095 ms | 1,045.5 KB |
| Parse and transform | 6.147 ms | 1,471.0 KB |

Half of the parse is the framework's reader. The parse on its own allocates more than the parse inside a
transformation, which takes the tree's arrays from a pool and gives them back; 697 KB of each transformation's
allocation is the result string.

| Products | Mean | Per product | Allocated |
|---:|---:|---:|---:|
| 100 | 586.3 μs | 5.86 μs | 175.4 KB |
| 1,000 | 6,002.4 μs | 6.00 μs | 1,471.1 KB |
| 10,000 | 68,340.0 μs | 6.83 μs | 15,539.3 KB |

#### What a row costs

One template applied to each of 1,000 products in a tree already parsed.

| Template body | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Nothing | 115.6 μs | 1.00 | 8.7 KB |
| One empty element | 206.7 μs | 1.79 | 44.4 KB |
| 8 elements of static text | 1,211.7 μs | 10.48 | 527.3 KB |
| The same, `indent="no"` | 1,060.7 μs | 9.17 | 341.8 KB |
| 8 elements, 7 `xsl:value-of` | 1,979.5 μs | 17.12 | 520.9 KB |
| 8 elements, 5 `xsl:value-of`, 2 `format-number` | 2,353.7 μs | 20.36 | 755.9 KB |
| 2 elements and an `xsl:choose` on `inStock='true'` | 531.2 μs | 4.59 | 106.9 KB |

By subtraction: about 115 ns for a template call, 140 ns for an element with its text and indentation, 110 ns
for an `xsl:value-of` of a child, and 190 ns and 118 bytes for a `format-number()`.

#### The two backends

| Benchmark | Version | Interpreted | Compiled |
|---|---|---:|---:|
| The whole products stylesheet | 1.0 | 3,862.7 μs | 3,811.0 μs |
| The whole products stylesheet | 3.0 | 4,234.3 μs | 4,347.6 μs |
| `count(//product)` | 1.0 | 16.38 μs | 16.29 μs |
| `count(//product)` | 3.0 | 16.34 μs | 16.16 μs |
| `count(//product[inStock='true'])` | 1.0 | 148.25 μs | 93.00 μs |
| `count(//product[inStock='true'])` | 3.0 | 180.67 μs | 193.98 μs |
| `count(//product[price > 100 and rating > 4])` | 1.0 | 210.21 μs | 124.05 μs |
| `count(//product[price > 100 and rating > 4])` | 3.0 | 313.40 μs | 120.61 μs |

Allocation is the same for both backends in every row. The compiled backend gains nothing on the whole
stylesheet at either version, 1.6 to 1.7 times on predicates at 1.0, and at 3.0 only on the comparisons under
`and` — where the 2.6 times is a bug and not a gain. At this commit the emitted code reads a comparison under
`and` or `or` by 1.0's rules whatever the version, so it runs the 1.0 code at the 1.0 speed and gives a
different answer from the interpreter where the rules differ: `count(//p[price < '5' and true()])` over a
`price` of 10 is 1 interpreted and 0 compiled. A fix to that will move the 3.0 rows of this table. Making a stylesheet ready costs 299.3 μs interpreted and
398.9 μs compiled (119.1 and 156.6 KB), against 435.5 μs and 268.4 KB for `XslCompiledTransform.Load`.

#### Output targets

| Target | Mean | Ratio | Gen0 / Gen1 / Gen2 | Allocated |
|---|---:|---:|---:|---:|
| A `TextWriter` that keeps nothing | 5.471 ms | 1.00 | 148 / 78 / 0 | 773.6 KB |
| A caller's `StreamWriter` over a stream that keeps nothing | 6.000 ms | 1.10 | 141 / 86 / 0 | 778.8 KB |
| A `Stream` that keeps nothing | 6.053 ms | 1.11 | 188 / 55 / 0 | 812.1 KB |
| A string | 6.079 ms | 1.11 | 297 / 227 / 148 | 1,471.1 KB |

These are the engine's own figures for the three targets: set them beside 1,470, 2,187 and 3,262 KB in the XML
transformation table above, where the difference is the benchmark's. The three real targets cost the same time;
the tenth they cost over the first row is what about 100,000 `Write` calls cost a writer that does any work.
Only the string collects in generation 2, its result being on the large object heap.

#### Identity transforms

| Stylesheet | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| `xsl:copy-of select="."` | 2.701 ms | 1.00 | 429.6 KB |
| `xsl:mode on-no-match="shallow-copy"` | 3.478 ms | 1.29 | 429.1 KB |
| The identity template, `match="@*\|node()"` | 7.972 ms | 2.95 | 2,250.0 KB |
| The identity template on `XslCompiledTransform` (framework) | 6.416 ms | 2.38 | 2,995.2 KB |

#### Predicates in match patterns

One template applied to each item of a flat list in a tree already parsed; 18 cases in six and a half
minutes. The first row is the same transformation with the test moved out of the pattern, and the setup checks
that the first four stylesheets give identical results. Each step is four times the items, so a linear cost
goes up four times a step and one that enumerates the siblings per candidate sixteen.

| Pattern | 1,000 items | 4,000 items | 16,000 items |
|---|---:|---:|---:|
| `match="item"`, the test in an `xsl:choose` | 290.0 μs | 1,094.1 μs | 4,548.4 μs |
| `match="item[@type='a']"` | 2,399.6 μs | 33,220.4 μs | 663,929.5 μs |
| `match="*[@type='a']"` | 2,154.1 μs | 29,716.0 μs | 1,366,778.2 μs |
| `match="item[position() mod 2 = 1]"` | 2,569.3 μs | 33,456.3 μs | 649,532.9 μs |
| `match="item[1]"` | 2,314.4 μs | 33,517.2 μs | 645,445.2 μs |
| `match="item[@type='a']"`, the items ten to a parent | 381.1 μs | 1,501.8 μs | 6,386.8 μs |

| Allocated | 1,000 items | 4,000 items | 16,000 items |
|---|---:|---:|---:|
| `match="item"`, the test in an `xsl:choose` | 15.9 KB | 51.0 KB | 319.9 KB |
| `match="item[@type='a']"`, `*[@type='a']` and `item[1]` | 15.6 to 15.9 KB | 50.7 to 51.0 KB | 2,051,945 KB |
| `match="item[position() mod 2 = 1]"` | 422.1 KB | 1,676.0 KB | 2,058,445 KB |
| `match="item[@type='a']"`, the items ten to a parent | 15.9 KB | 51.1 KB | 319.9 KB |

A pattern with a predicate enumerates the candidate's siblings on every test, whether or not the predicate
can read a position, so its cost is the square of the sibling count: 146 times the baseline at 16,000, and 300
for the wildcard. The last row, where no item has more than nine siblings, stays linear. Past 4,096 siblings
`NodeListPool` stops keeping the list the siblings are collected into, and every test grows a new one: two
gigabytes and 499 generation-0 collections per transformation.

Those are the figures that found the problem. The same class after `PatternStep.CountsPosition` was added,
measured the same day against the same commit with that one change, so that a step whose predicates cannot
read a position enumerates nothing:

| Pattern | 1,000 items | 4,000 items | 16,000 items | Allocated at 16,000 |
|---|---:|---:|---:|---:|
| `match="item"`, the test in an `xsl:choose` | 276.4 μs | 1,119.7 μs | 4,702.1 μs | 319.9 KB |
| `match="item[@type='a']"` | 334.2 μs | 1,381.8 μs | 6,042.8 μs | 319.9 KB |
| `match="*[@type='a']"` | 329.1 μs | 1,346.9 μs | 5,948.3 μs | 327.2 KB |
| `match="item[position() mod 2 = 1]"` | 2,623.0 μs | 33,969.9 μs | 691,980.0 μs | 2,058,445 KB |
| `match="item[1]"` | 2,298.7 μs | 32,952.2 μs | 686,717.3 μs | 2,052,193 KB |
| `match="item[@type='a']"`, the items ten to a parent | 350.6 μs | 1,337.4 μs | 6,169.7 μs | 328.5 KB |

The second and third rows are linear now, within 30% of the test written in the template. The fourth and
fifth read a position, so their siblings are still counted and they are still the square. Their 6% over the
earlier table is between processes and not between builds: run turn about three times each, `item[1]` at
16,000 was 660, 691 and 700 ms before the change and 673, 667 and 671 ms after.

And after the second change, which keeps what a step selected from the last parent so that the next
candidate is looked up in it and the siblings are selected once per parent. Measured the same day against
commit `a59a0ed` with both changes; the commits between are about comparisons and touch nothing here:

| Pattern | 1,000 items | 4,000 items | 16,000 items | Allocated at 16,000 |
|---|---:|---:|---:|---:|
| `match="item"`, the test in an `xsl:choose` | 282.9 μs | 1,104.7 μs | 4,511.9 μs | 319.9 KB |
| `match="item[@type='a']"` | 302.9 μs | 1,198.0 μs | 4,928.4 μs | 319.9 KB |
| `match="*[@type='a']"` | 294.2 μs | 1,217.7 μs | 4,836.9 μs | 319.6 KB |
| `match="item[position() mod 2 = 1]"` | 494.1 μs | 1,960.6 μs | 8,107.6 μs | 6,948.5 KB |
| `match="item[1]"` | 267.9 μs | 1,058.1 μs | 4,244.4 μs | 448.5 KB |
| `match="item[@type='a']"`, the items ten to a parent | 308.2 μs | 1,201.6 μs | 5,091.4 μs | 319.9 KB |

Every row goes up four times for four times the items. What the fourth still allocates is its predicate,
`position() mod 2 = 1` making some 430 bytes each time it is evaluated. Two shapes this class does not
measure are still the square: a step where a later predicate counts among what an earlier one left, such
as `item[@type='a'][2]`, and a step on `descendant::`.

#### Beside the framework's processor

| Products | Target | CodeDeeds.Xslt | `XslCompiledTransform` | Allocated |
|---:|---|---:|---:|---|
| 100 | A string | 583.9 μs | 566.0 μs | 175.4 against 421.5 KB |
| 100 | A writer that keeps nothing | 551.6 μs | 539.8 μs | 103.0 against 280.6 KB |
| 1,000 | A string | 5,917.9 μs | 6,801.6 μs | 1,471.1 against 3,555.0 KB |
| 1,000 | A writer that keeps nothing | 5,454.7 μs | 6,125.9 μs | 773.6 against 2,203.6 KB |

#### Cold start and the JIT

One measurement from each of ten fresh processes, with no warm-up.

| First call in a fresh process | Mean | StdDev | Allocated |
|---|---:|---:|---:|
| First compile and first transform, 100 products | 194.69 ms | 1.73 ms | 410.8 KB |
| First compile and first transform, 1,000 products | 231.35 ms | 6.02 ms | 2,718.7 KB |
| First compile and first transform, 100 products, IL backend | 201.68 ms | 1.51 ms | 453.3 KB |
| First transform of a stylesheet already compiled, 100 products | 58.33 ms | 1.08 ms | 289.9 KB |
| First transform of a stylesheet already compiled, 1,000 products | 97.48 ms | 2.41 ms | 2,597.9 KB |

Those include the runtime loading `System.Xml` and compiling every method on the way for the first time, and
nothing here separates that from the engine's own share.

| JIT configuration | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Tiered with dynamic PGO (the default) | 5.499 ms | 1.00 | 773.6 KB |
| Tiered, `DOTNET_TieredPGO=0` | 7.195 ms | 1.31 | 773.7 KB |
| `DOTNET_TieredCompilation=0` | 9.660 ms | 1.76 | 773.6 KB |

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
