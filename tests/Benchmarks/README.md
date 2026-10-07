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
11. **DocBook** - Documents written for the DocBook xslTNG stylesheets, transformed with them

Items 5 to 10 came out of the performance review of 19 September 2026
(`source/CodeDeeds.Xslt/Documentation/PerformanceReview-2026-09-19.md`), which says what each was built to find out.

## Projects & Files

### Benchmark Classes

- **XsltCompilationBenchmarks.cs** - Tests stylesheet compilation performance
  - `CompileXmlToHtmlStylesheet()` - Compiles XML to HTML stylesheet
  - `CompileJsonToHtmlStylesheet()` - Compiles JSON to HTML stylesheet
  - `CompileDocbookStylesheet()` - Compiles the DocBook xslTNG stylesheets, fifty modules read from `Stylesheets/DocBook`
  - Each of the three again with `Backend = Compiled`

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

- **DocBookTransformationBenchmarks.cs** - DocBook documents through the DocBook xslTNG stylesheets, fifty modules somebody else wrote
  - A book of 77 paragraphs, and one table at a hundred rows and at a thousand
  - The stylesheet is compiled in the setup; what is measured is the parse and everything the stylesheets do, the four stylesheets they run the document through with `fn:transform()` first included

- **BenchmarkData.cs** - The documents and stylesheets the classes above share, and the counting sinks

### Sample Data & Stylesheets

#### Data Files
- **Data/products.xml** - Sample e-commerce product catalog in XML format (100 products)
- **Data/products.json** - Sample e-commerce product catalog in JSON format (100 products)
- **Data/DocBook/** - Two documents from the DocBook xslTNG tests, under its MIT licence: a book, and a CALS table of a thousand rows; see the README in the folder

#### Stylesheets
- **Stylesheets/ProductsXmlToHtml.xslt** - Transforms XML product data to HTML
  - Features: Product table, summary statistics, inline CSS styling
  - Uses: XPath functions, conditional logic, formatting

- **Stylesheets/ProductsJsonToHtml.xslt** - Transforms JSON product data to HTML
  - Features: Product table, multiple stat cards, enhanced styling
  - Uses: XPath 3.0 JSON functions, array operations, distinct-values()

- **Stylesheets/DocBook.xslt** - One `xsl:import` of `DocBook/xslt/docbook.xsl`
- **Stylesheets/DocBook/** - The XSLT modules of DocBook xslTNG 2.8.5, unmodified, with their MIT licence
  - A large stylesheet somebody else wrote: fifty modules and about 800,000 characters
  - Only what compiling reads; see the README in the folder for where they came from and what is left out

## Running the Benchmarks

### Prerequisites
- .NET 10 SDK or later
- Release build recommended for accurate results

### Quick Start

```bash
cd tests/Benchmarks
dotnet run -c Release
```

With no arguments, every benchmark class runs in turn. The first four classes take about five minutes between
them, and the rest about twenty-four more: they use BenchmarkDotNet's default job, which warms up until the
measurements settle. Nothing needs a network connection.

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

Measured 6 October 2026 on an Intel Core i7-6700K (4 cores, 4.00 GHz), Windows 10, .NET 10.0.12,
BenchmarkDotNet 0.15.8, at commit `0193bf8`: every class in one run, 105 cases in 30 minutes. Times are
means; allocation is per operation. Run from a copy of the working tree, because a git worktree inside the
repository stops BenchmarkDotNet finding the project.

### Compilation

| Benchmark | Backend | Mean | Allocated |
|---|---|---:|---:|
| Compile XML to HTML stylesheet | Interpreted | 170.4 μs | 122.4 KB |
| Compile XML to HTML stylesheet | Compiled | 297.2 μs | 169.6 KB |
| Compile JSON to HTML stylesheet | Interpreted | 102.3 μs | 72.8 KB |
| Compile JSON to HTML stylesheet | Compiled | 111.8 μs | 74.8 KB |
| Compile DocBook stylesheet | Interpreted | 77.2 ms | 27.1 MB |
| Compile DocBook stylesheet | Compiled | 139.9 ms | 37.7 MB |

The fifty modules of xslTNG 2.8.5 are read from `Stylesheets/DocBook`. Until commit `62e6730` the stylesheet imported
them from `cdn.docbook.org` and every iteration fetched all fifty: the rows read 1,805 and 2,025 ms, of which the
compiler's share was some 80 and 144 and the rest was fifty requests, and what was compiled changed whenever
DocBook published a release. Read from memory instead of the folder the compile is within a twentieth of this.

### XML transformation (compiled backend)

| Benchmark | Mean | Gen0 / Gen1 / Gen2 | Allocated |
|---|---:|---:|---:|
| Small XML (100 products) to string | 528.8 μs | 35 / 0 / 0 | 157.5 KB |
| Small XML (100 products) to TextWriter | 552.1 μs | 59 / 14 / 0 | 245.8 KB |
| Small XML (100 products) to Stream | 618.0 μs | 98 / 4 / 0 | 407.6 KB |
| Large XML (1000 products) to string | 5,464.6 μs | 273 / 211 / 156 | 1,296.9 KB |
| Large XML (1000 products) to TextWriter | 5,708.6 μs | 398 / 320 / 156 | 2,013.0 KB |
| Large XML (1000 products) to Stream | 6,182.9 μs | 695 / 617 / 430 | 3,087.9 KB |

### JSON transformation (compiled backend)

| Benchmark | Mean | Gen0 / Gen1 / Gen2 | Allocated |
|---|---:|---:|---:|
| Small JSON (100 products) to string | 159.7 μs | 16 / 0 / 0 | 66.2 KB |
| Small JSON (100 products) to TextWriter | 158.6 μs | 18 / 0 / 0 | 72.7 KB |
| Small JSON (100 products) to Stream | 164.8 μs | 30 / 0 / 0 | 123.1 KB |
| Large JSON (1000 products) to string | 1,464.1 μs | 121 / 6 / 0 | 516.8 KB |
| Large JSON (1000 products) to TextWriter | 1,418.9 μs | 105 / 53 / 0 | 523.3 KB |
| Large JSON (1000 products) to Stream | 1,410.9 μs | 119 / 55 / 0 | 573.7 KB |

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
| Read 100 products, untyped | 248.8 μs | 1.00 | 76.9 KB | 1.00 |
| Read 100 products, input validated and typed | 497.9 μs | 2.00 | 190.6 KB | 2.48 |
| Build 100 products, nothing validated | 340.0 μs | 1.37 | 122.9 KB | 1.60 |
| Build 100 products, result validated strictly | 803.7 μs | 3.23 | 605.9 KB | 7.88 |
| Compile a stylesheet that imports the schema | 39.5 μs | 0.16 | 55.2 KB | 0.72 |

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
| Parse floor: bare `XmlReader`, every value read | 1.060 ms | 650.1 KB |
| Parse to an `XdmTree` | 2.060 ms | 1,669.6 KB |
| Parse to an `XPathDocument` (framework) | 2.147 ms | 923.9 KB |
| Transform a tree already parsed | 3.677 ms | 871.7 KB |
| Parse and transform | 5.598 ms | 1,296.9 KB |

Half of the parse is the framework's reader. The parse on its own allocates more than the parse inside a
transformation, which takes the tree's arrays from a pool and gives them back; 698 KB of each transformation's
allocation is the result string.

| Products | Mean | Per product | Allocated |
|---:|---:|---:|---:|
| 100 | 539.3 μs | 5.39 μs | 157.5 KB |
| 1,000 | 5,561.0 μs | 5.56 μs | 1,296.9 KB |
| 10,000 | 61,726.7 μs | 6.17 μs | 12,698.8 KB |

#### What a row costs

One template applied to each of 1,000 products in a tree already parsed.

| Template body | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Nothing | 91.4 μs | 1.00 | 5.0 KB |
| One empty element | 176.8 μs | 1.93 | 40.8 KB |
| 8 elements of static text | 1,165.6 μs | 12.76 | 523.5 KB |
| The same, `indent="no"` | 1,033.0 μs | 11.30 | 338.1 KB |
| 8 elements, 7 `xsl:value-of` | 1,828.6 μs | 20.01 | 517.1 KB |
| 8 elements, 5 `xsl:value-of`, 2 `format-number` | 2,143.9 μs | 23.46 | 582.5 KB |
| 2 elements and an `xsl:choose` on `inStock='true'` | 482.8 μs | 5.28 | 103.2 KB |

By subtraction, and no steadier than the rows it is taken from, of which the `xsl:value-of` row has read
1.75 to 2.04 ms in three runs: about 91 ns for a template call, 134 ns for an element with its text and indentation, 95 ns
for an `xsl:value-of` of a child, and 158 ns and 33 bytes for a `format-number()`.

#### The two backends

| Benchmark | Version | Interpreted | Compiled |
|---|---|---:|---:|
| The whole products stylesheet | 1.0 | 3,543.3 μs | 3,361.8 μs |
| The whole products stylesheet | 3.0 | 3,846.9 μs | 3,892.4 μs |
| `count(//product)` | 1.0 | 16.19 μs | 16.22 μs |
| `count(//product)` | 3.0 | 16.00 μs | 15.90 μs |
| `count(//product[inStock='true'])` | 1.0 | 118.23 μs | 74.24 μs |
| `count(//product[inStock='true'])` | 3.0 | 114.48 μs | 85.24 μs |
| `count(//product[price > 100 and rating > 4])` | 1.0 | 189.62 μs | 115.79 μs |
| `count(//product[price > 100 and rating > 4])` | 3.0 | 175.21 μs | 115.61 μs |

Allocation is the same for both backends in every row. The compiled backend gains nothing on the whole
stylesheet or on `count(//product)` at either version, and 1.3 to 1.6 times on the two predicates at both.
When this table was first made the two versions were far apart on the compiled side, and that was a fault
and not a gain: the emitted code read a comparison under `and` or `or` by 1.0's rules whatever the version.
The route a comparison takes is settled once now, where the expression is built, and both backends take it.
Making a stylesheet ready costs 173.9 μs interpreted and 294.9 μs compiled (122.4 and 169.6 KB), against
432.6 μs and 268.3 KB for `XslCompiledTransform.Load`.

#### Output targets

| Target | Mean | Ratio | Gen0 / Gen1 / Gen2 | Allocated |
|---|---:|---:|---:|---:|
| A `TextWriter` that keeps nothing | 5.006 ms | 1.00 | 117 / 55 / 0 | 599.4 KB |
| A caller's `StreamWriter` over a stream that keeps nothing | 5.561 ms | 1.11 | 109 / 70 / 0 | 604.7 KB |
| A `Stream` that keeps nothing | 5.571 ms | 1.11 | 125 / 55 / 0 | 637.9 KB |
| A string | 5.582 ms | 1.12 | 258 / 203 / 148 | 1,296.9 KB |

These are the engine's own figures for the three targets: set them beside 1,297, 2,013 and 3,088 KB in the XML
transformation table above, where the difference is the benchmark's. The three real targets cost the same time;
the tenth they cost over the first row is what about 100,000 `Write` calls cost a writer that does any work.
Only the string collects in generation 2, its result being on the large object heap.

#### Identity transforms

| Stylesheet | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| `xsl:copy-of select="."` | 2.734 ms | 1.00 | 429.6 KB |
| `xsl:mode on-no-match="shallow-copy"` | 3.267 ms | 1.20 | 429.1 KB |
| The identity template, `match="@*\|node()"` | 6.269 ms | 2.29 | 429.5 KB |
| The identity template on `XslCompiledTransform` (framework) | 6.330 ms | 2.32 | 2,995.2 KB |

The identity template allocated 2,250 KB when the review measured it, a new list of nodes for every
`@*|node()` it applied templates to. The union is gathered into a list it is lent now, and the template
allocates what the other two do. It is still 2.29 times `xsl:copy-of`, and 1% slower than the same template
on the framework, where it was 24%.

#### Predicates in match patterns

One template applied to each item of a flat list in a tree already parsed. The first row is the same
transformation with the test moved out of the pattern, and the setup checks that the first four stylesheets
give identical results. Each step is four times the items, so a linear cost goes up four times a step and
one that enumerates the siblings per candidate sixteen.

| Pattern | 1,000 items | 4,000 items | 16,000 items | Allocated at 16,000 |
|---|---:|---:|---:|---:|
| `match="item"`, the test in an `xsl:choose` | 248.1 μs | 956.2 μs | 4,205.0 μs | 129.5 KB |
| `match="item[@type='a']"` | 248.2 μs | 994.8 μs | 4,384.7 μs | 129.5 KB |
| `match="*[@type='a']"` | 240.4 μs | 993.5 μs | 4,344.7 μs | 129.2 KB |
| `match="item[position() mod 2 = 1]"` | 350.6 μs | 1,439.3 μs | 5,809.0 μs | 258.1 KB |
| `match="item[1]"` | 234.2 μs | 959.1 μs | 3,930.3 μs | 258.1 KB |
| `match="item[@type]"`, which every item matches | 196.2 μs | 792.3 μs | 3,408.1 μs | 129.5 KB |
| `match="item[@type='a']"`, the items ten to a parent | 254.4 μs | 1,024.4 μs | 4,571.2 μs | 129.5 KB |

Every row goes up 3.9 to 4.5 times for four times the items, so each is linear in the list, and at 16,000
items the rows are 0.8 to 1.4 times the test written in the template.

It was not so when this class was first run, on 19 September 2026. A pattern with a predicate enumerated the
candidate's siblings on every test, whether or not the predicate could read a position, and its cost was the
square of the sibling count: `match="item[@type='a']"` took 664 ms at 16,000 items where it takes 4.4 ms now,
146 times the first row, and allocated two gigabytes on the way. A step whose predicates cannot read a
position enumerates nothing now, and one whose predicates can keeps what it selected from the last parent,
so that the siblings are selected once per parent. `position() mod 2 = 1` made some 430 bytes each time it
was evaluated, 6,948 KB at 16,000 items, and is 258 KB now.

Two shapes this class does not measure were still the square at commit `688e32b`, and have been made linear
since: a step where a later predicate counts among what an earlier one left, `item[@type='a'][2]`, which took
158 ms over 2,000 items and 2.6 s over 8,000 and takes 0.9 ms and 3.6 ms; and a step on the `descendant::`
axis whose predicate reads a position, `list/descendant::item[2]`, which took 22 ms and 338 ms and takes
0.9 ms and 3.6 ms. What is left of the square is the two at once, `list/descendant::item[@type='a'][2]`, and
a step whose earlier predicate reads `current()` or a variable, which has to be counted for each candidate.

#### Beside the framework's processor

| Products | Target | CodeDeeds.Xslt | `XslCompiledTransform` | Allocated |
|---:|---|---:|---:|---|
| 100 | A string | 542.6 μs | 555.1 μs | 157.5 against 421.4 KB |
| 100 | A writer that keeps nothing | 506.8 μs | 544.9 μs | 85.2 against 280.6 KB |
| 1,000 | A string | 5,596.8 μs | 6,533.1 μs | 1,297.0 against 3,555.0 KB |
| 1,000 | A writer that keeps nothing | 5,088.0 μs | 6,132.5 μs | 599.4 against 2,203.7 KB |

At 100 products the two are within 7% of each other. At 1,000 this engine is 14% faster to a string and 17%
faster to a writer, and at either size it allocates 27 to 37% of what the framework does.

#### Cold start and the JIT

One measurement from each of ten fresh processes, with no warm-up.

| First call in a fresh process | Mean | StdDev | Allocated |
|---|---:|---:|---:|
| First compile and first transform, 100 products | 189.22 ms | 1.14 ms | 396.2 KB |
| First compile and first transform, 1,000 products | 226.98 ms | 1.84 ms | 2,547.8 KB |
| First compile and first transform, 100 products, IL backend | 201.31 ms | 2.56 ms | 448.7 KB |
| First transform of a stylesheet already compiled, 100 products | 54.49 ms | 0.47 ms | 272.1 KB |
| First transform of a stylesheet already compiled, 1,000 products | 91.49 ms | 0.98 ms | 2,423.7 KB |

Those include the runtime loading `System.Xml` and compiling every method on the way for the first time, and
nothing here separates that from the engine's own share.

| JIT configuration | Mean | Ratio | Allocated |
|---|---:|---:|---:|
| Tiered with dynamic PGO (the default) | 5.070 ms | 1.00 | 599.4 KB |
| Tiered, `DOTNET_TieredPGO=0` | 6.958 ms | 1.37 | 599.4 KB |
| `DOTNET_TieredCompilation=0` | 9.587 ms | 1.89 | 599.4 KB |

#### DocBook

The stylesheets under `Stylesheets/DocBook`, compiled once, over the documents under `Data/DocBook`, written to a
writer that keeps nothing.

| Document | Mean | Allocated |
|---|---:|---:|
| A book of 77 paragraphs, 44 KB | 31.8 ms | 27.0 MB |
| The book, the documents read kept parsed | 28.2 ms | 24.5 MB |
| A table of 100 rows, 26 KB | 80.3 ms | 62.4 MB |
| A table of 1,000 rows, 257 KB | 769.6 ms | 581.1 MB |

The class had been run four times when the rest were measured with it, and the first three found something
that was not the documents'. The class run alone the same day gave 28.9, 75.6 and 719.5 ms with the
same allocation, which is what the notes quote; the times move a tenth with what else the machine is doing,
the allocation not at all.

The stylesheets put every document through four stylesheets of their own with `fn:transform()` before
formatting it. When the class was first run each of the four was read and compiled again at every
transformation, thirty-one modules in all, and the three rows were 114.3 ms and 61.6 MB, 228.8 ms and
121.4 MB, and 1,720.3 ms and 1,110.1 MB. A stylesheet `fn:transform()` has compiled is kept now by the
call that named it, which made them 60.2 ms and 44.8 MB, 165.7 ms and 104.8 MB, and 1,627.8 ms and
1,094.6 MB.

The stylesheets also declare 224 parameters and hand them on in one `xsl:map`, which the stylesheets they
run build again, and `xsl:map` made a new map for every entry, each a copy of the last: twelve megabytes of every
transformation and a quarter of what a short document took. It builds a map in one pass now, and two
searches of a list that were made for every parameter are lookups, which made the rows 53.4 ms and
32.6 MB, 161.5 ms and 92.5 MB, and 1,645.0 ms and 1,082.4 MB.

The third run found four things the engine did for every temporary tree, whitespace node and sequence,
which the DocBook stylesheets have more of than most: a tree built by a variable had a name table of its
own, so a path into it and a template applied to it cost a mapping of the stylesheet's names and an index
of its templates, 130 and 8 of them for a document of three hundred characters; the strip-space
declarations, two hundred of them, were walked for every whitespace-only text node of every document
parsed; every sequence a function read or an `as` checked was laid out again in a list of its own; and
the evaluation context carried seven fields that never change. The trees of a transformation share one
table now, the decisions are kept by name, a sequence reads as itself, and the context is 96 bytes where
it was 144, which is the table above.

What is left that is not the document's is what the stylesheets do for any document at all. One of
three hundred characters takes 11 ms and 10 MB: the title-page templates, a document the stylesheets
build from thirty-nine templates by a recursive copy, 4.7 ms of it; the localization file, 84 KB parsed
once a transformation, 1.3; the four maps of 224 parameters, 1.5; and the formatting. And a row of the
table costs 0.7 ms and 0.6 MB, which is the stylesheets' way with a CALS table, a tree a cell.

The second row of the table is the first with the two documents the stylesheets read, the localization
file and the title-page templates, parsed once by the resolver with `Xslt.ParseDocument` and handed back
as trees through `ResolvedResource(XdmTree, string)` to every transformation after, which a caller whose
documents do not change can do; measured on 7 October with the class alone, when the first row read 32.1 ms and 27.0 MB. The templates document is still built from the
tree every time.

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
