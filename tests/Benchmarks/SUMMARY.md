# Benchmark Project Summary

## ✅ Project Successfully Created

A comprehensive performance benchmarking suite has been created for the CodeDeeds.Xslt library.

## 📁 Project Structure

```
source/CodeDeeds.Xslt.Benchmarks/
├── CodeDeeds.Xslt.Benchmarks.csproj      # Project file with BenchmarkDotNet dependency
├── Program.cs                             # BenchmarkDotNet runner
├── README.md                              # Comprehensive documentation
│
├── XsltCompilationBenchmarks.cs          # XSLT compilation performance tests
├── XmlTransformationBenchmarks.cs        # XML to HTML transformation tests
├── JsonTransformationBenchmarks.cs       # JSON to HTML transformation tests
│
├── Data/
│   ├── products.xml                      # Sample XML data (100 products)
│   └── products.json                     # Sample JSON data (100 products)
│
└── Stylesheets/
	├── ProductsXmlToHtml.xslt            # XML to HTML transformation stylesheet
	└── ProductsJsonToHtml.xslt           # JSON to HTML transformation stylesheet
```

## 🎯 Benchmark Coverage

### 1. XSLT Compilation (XsltCompilationBenchmarks)
- **Compile XML to HTML stylesheet**: ~269 μs per compilation (interpreted backend), ~378 μs with IL emission
- **Compile JSON to HTML stylesheet**: ~116 μs per compilation (interpreted backend), ~124 μs with IL emission
- Memory diagnostics showing allocation patterns

### 2. XML Transformation (XmlTransformationBenchmarks)
- Small dataset (100 products) transformations
  - To string (StringWriter)
  - To TextWriter
  - To Stream
- Large dataset (1000 products) transformations
  - To string
  - To TextWriter
  - To Stream

### 3. JSON Transformation (JsonTransformationBenchmarks)
- Small dataset (100 products) transformations
  - To string
  - To TextWriter
  - To Stream
- Large dataset (1000 products) transformations
  - To string
  - To TextWriter
  - To Stream

## 📊 Sample Performance Results

From the full run of 8 September 2026 (i7-6700K, .NET 10.0.11); the complete tables are in README.md:
```
XsltCompilationBenchmarks
| Method                                        | Mean     | Allocated |
|---                                            |---       |---        |
| Compile XML to HTML stylesheet (interpreted)  | 268.8 us | 117.7 KB  |
| Compile XML to HTML stylesheet (compiled)     | 377.6 us | 155.1 KB  |
| Compile JSON to HTML stylesheet (interpreted) | 116.2 us | 69.9 KB   |
| Compile JSON to HTML stylesheet (compiled)    | 123.7 us | 71.8 KB   |

XmlTransformationBenchmarks
| Method                                        | Mean       | Allocated  |
|---                                            |---         |---         |
| Transform small XML (100 products) to string  | 552.5 us   | 174.6 KB   |
| Transform large XML (1000 products) to string | 5,599.7 us | 1,470.4 KB |

JsonTransformationBenchmarks
| Method                                         | Mean       | Allocated  |
|---                                             |---         |---         |
| Transform small JSON (100 products) to string  | 161.1 us   | 67.3 KB    |
| Transform large JSON (1000 products) to string | 1,441.8 us | 517.9 KB   |
```

## 🚀 Quick Start

```bash
# Navigate to benchmarks project
cd source/CodeDeeds.Xslt.Benchmarks

# Run every benchmark class (default)
dotnet run -c Release

# Results include:
# - Execution time (Mean, Error, StdDev)
# - Memory allocation (Gen0 collections, bytes allocated)
# - Statistical outliers removal
# - BenchmarkDotNet artifacts
```

## 📝 Sample Data

Both XML and JSON datasets contain 100 realistic e-commerce products with:
- Product IDs and names
- Categories (Electronics, Storage, Lighting, Accessories)
- Pricing in USD
- Availability status
- Customer ratings (0-5 stars)
- Detailed descriptions

Large datasets are generated dynamically by multiplying base data 10x (1000 products).

## 🎓 Benchmark Methodology

- **3 warmup iterations** - Stabilize JIT and CPU state
- **1 launch** - Single process to minimize interference
- **Memory diagnostics** - Track Gen0 collections and allocations
- **Statistical analysis** - Mean, standard deviation, outlier detection

## 🔧 Technologies

- **.NET 10** - Target framework
- **BenchmarkDotNet 0.15.8** - Performance measurement framework
- **XSLT 3.0** - Stylesheet version
- **C# 14** - Language version

## 📚 Documentation

Comprehensive README.md included with:
- Project overview and structure
- Running instructions for different scenarios
- Benchmark methodology and considerations
- Performance analysis guidance
- Extension points for adding new benchmarks

## ✨ Features

✅ Production-ready benchmark project
✅ Realistic sample XSLT stylesheets
✅ Representative e-commerce data
✅ Multiple input sizes (small and large)
✅ Multiple output methods (String, TextWriter, Stream)
✅ Memory diagnostics enabled
✅ Complete documentation
✅ Ready for performance regression testing
✅ Easy to extend with new benchmarks

## 🔄 Integration with Solution

The benchmarks project has been added to the solution file:
- `source/CodeDeeds.Xslt.Benchmarks/CodeDeeds.Xslt.Benchmarks.csproj`
- Added to `source/CodeDeeds.Xslt/CodeDeeds.Xslt.slnx`

## 🎯 Next Steps (Optional)

1. **Narrow a run**: every class runs by default; pass `-- --filter *XmlTransformation*` to run one
2. **Compare performance**: Generate baseline and track improvements over time
3. **Add more scenarios**: Create additional XSLT stylesheets for different use cases
4. **CI/CD integration**: Integrate benchmarks into your build pipeline
5. **Performance thresholds**: Set limits for acceptable performance regressions

---

**Project Status**: ✅ Complete and tested
**Build Status**: ✅ Successful
**Sample Run**: ✅ Benchmarks execute successfully
