# CodeDeeds.Xslt

A lightweight **XSLT 3.0** and **XPath 3.1** processor for .NET, written in C# with no dependencies beyond the
base class library.

.NET's own `XslCompiledTransform` stops at XSLT 1.0. This library brings the rest of the language to .NET:
XSLT 3.0 (and 2.0 on request), XPath 3.1 with maps, arrays and higher-order functions, packages, accumulators,
`xsl:iterate`, `xsl:try`/`xsl:catch`, `xsl:merge`, `xsl:for-each-group`, JSON input and output, and the full
function library.

```
dotnet add package CodeDeeds.Xslt
```

Targets .NET 10. MIT licensed.

## Quick start

```csharp
using CodeDeeds.Xslt;

var xslt = new Xslt("""
    <xsl:stylesheet version="3.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
      <xsl:output method="text"/>
      <xsl:template match="/orders">
        <xsl:for-each-group select="order" group-by="@customer">
          <xsl:sort select="sum(current-group()/@total)" order="descending"/>
          <xsl:value-of select="current-grouping-key(), sum(current-group()/@total)" separator=": "/>
          <xsl:text>&#10;</xsl:text>
        </xsl:for-each-group>
      </xsl:template>
    </xsl:stylesheet>
    """);

string result = xslt.TransformXml("<orders><order customer='A' total='10'/><order customer='B' total='7'/></orders>");
```

Compile a stylesheet once and reuse the `Xslt` instance for any number of transformations. The transform
methods take XML as a string, `TextReader` or `Stream` and write to a string, `TextWriter` or `Stream`.

### Options

Everything is configured through `XsltOptions`:

```csharp
var xslt = new Xslt(stylesheet, new XsltOptions
{
    Backend = XsltBackend.Compiled,                    // compile to IL instead of interpreting
    Parameters = new Dictionary<string, object?> { ["limit"] = 10 },
    StylesheetResolver = new FileResolver(@"C:\stylesheets"),
});
```

| Want to | Use |
| --- | --- |
| Pass stylesheet parameters | `Parameters` |
| Start at a named template, mode or function | `InitialTemplate`, `InitialMode`, `InitialFunction` |
| Run faster on hot paths | `Backend = XsltBackend.Compiled` (same results as the interpreter) |
| Act as an XSLT 2.0 processor | `Version = XsltVersion.V20` |
| Read `xsl:include`, `document()`, `collection()` | `StylesheetResolver`, `DocumentResolver`, `CollectionResolver` (`FileResolver` for a directory, `UriResolver` for HTTP(S) too) |
| Capture `xsl:message` | `MessageWriter` |
| Write secondary results (`xsl:result-document`) | `ResultResolver` or `ResultStreamResolver` |
| Use XSD types | `SchemaAware`, `Schemas`, `InputValidation` |

### Other entry points

```csharp
xslt.TransformJson(json);                 // JSON input, seen by the stylesheet as the XML form of fn:json-to-xml (fn:map, fn:string, ...)
xslt.TransformXmlToTree(xml);             // result as an XdmTree, for chaining transformations
xslt.TransformXmlToSequence(xml);         // result as a sequence of typed items, not serialized text
Xslt.Embedded(tree, "id");                // a stylesheet embedded in another document
```

## Secure by default

A stylesheet cannot reach the file system, the network or the process environment unless you hand it a
resolver. `xsl:include`, `xsl:import`, `document()`, `doc()`, `unparsed-text()`, `collection()`, external
entities, `xsl:result-document` and `environment-variable()` are all errors until you configure them, so
running a stylesheet you did not write is safe in the way the defaults make easy.

## Conformance

Measured against the W3C test suites on 19 September 2026:

| Suite | Passed |
| --- | --- |
| XSLT 3.0 test suite, 3.0 processor | 8,061 of 8,071 (99.9%) |
| XSLT 3.0 test suite, 2.0 subset, 2.0 processor | 5,678 of 5,701 (99.6%) |
| QT3 (XPath), 3.1 | 18,268 of 18,285 (99.9%) |
| QT3 (XPath), 2.0 | 14,553 of 14,577 (99.8%) |

The processor claims **basic** XSLT conformance: not schema-aware by default and not streaming. The suites are
in [`tests/W3CConformanceTests`](https://github.com/CodeDeeds/CodeDeeds.Xslt/tree/main/tests/W3CConformanceTests).

### Major deviations from the W3C standard

Check these before adopting the library; each is a place where a stylesheet written for another processor
(Saxon, for instance) may behave differently or be refused.

- **No streaming.** `streamable="yes"` and `xsl:source-document` are accepted but run over a tree held in
  memory, so very large inputs are not processed in constant memory. `system-property('xsl:supports-streaming')`
  is `no`. The ~2,900 streaming tests are not run.
- **Schema awareness is optional and XSD 1.0 only.** Off by default; turn it on with
  `XsltOptions.SchemaAware`. Without it, `xsl:import-schema` is `XTSE1650`, `validation="strict"` and `type` are
  `XTSE1660`, and nothing carries a type annotation. With it, validation is done by .NET's XSD 1.0 implementation:
  there is no XSD 1.1, `xsi:schemaLocation` is not followed, and a scatter of validation edge cases remain
  (the schema-aware tests stand at about 99.3%).
- **No extension mechanism.** Only EXSLT's Common module (`exsl:node-set`, `exsl:object-type`, `exsl:document`)
  is provided; you cannot register your own extension functions or elements. An unknown function is
  `XPST0017`; an unknown extension instruction takes its `xsl:fallback` or is `XTDE1450`.
- **`fn:load-xquery-module` is not supported** (`XPST0017`), and **XInclude** is not applied.
- **`version="1.0"` is XSLT 2.0's backwards-compatible mode**, not a true 1.0 processor: XPath 2.0 grammar and
  functions with 1.0's conversions. Result trees follow XSLT 3.0's rules, so output can differ from
  `XslCompiledTransform` in namespace handling (the identity transform drops an `xmlns=""` that
  `XslCompiledTransform` keeps).
- **`xs:decimal` has about 28 significant digits** (it is `System.Decimal`). Dates are limited to years
  1 to 999,999,999 and durations to a 32-bit month count and `TimeSpan` seconds; beyond that is `FODT0001` /
  `FODT0002`.
- **Values are read by XSD 1.0 rules**: no year zero, no `+INF`, and no `xs:dateTimeStamp`.
- **Regular expressions** use the XPath dialect; lookaround, atomic groups, named groups and inline options are
  refused as `FORX0002`, as the specification requires, even though .NET would accept them.
- **Collations**: the Unicode codepoint collation, `html-ascii-case-insensitive` and the UCA collation are
  provided, but UCA parameters that .NET's `CompareInfo` cannot express (`alternate=shifted`, `backwards`,
  `caseFirst=upper`, `numeric`, `reorder`, …) are ignored, or `FOCH0002` under `fallback=no`. `xsl:sort`
  without `lang` compares by code point, not by the machine's culture.
- **Localized number words and date names** cover English, German, French, Spanish, Portuguese, Italian,
  Norwegian, Swedish and Danish. Other languages fall back to English, and non-Gregorian calendars to `AD`.
- **`xsl:output`**: `normalization-form="fully-normalized"` is refused; `undeclare-prefixes` has no effect
  (only XML 1.0 is written). `disable-output-escaping` is lost inside variables and temporary trees.
- **Packages**: re-exposing an *accepted* component through `xsl:expose` is not supported, and when two
  packages override the same component of a shared library, the library's own code keeps reading its own
  declaration.
- **DTDs**: only the internal subset is processed; external entities expand to nothing unless an
  `EntityResolver` is supplied.
- **`xsl:assert` is always checked** (the specification defaults it to off), and `xsl:mode`'s
  `warning-on-no-match` / `warning-on-multiple-match` default to *no*.
- **Recursion** is limited to 20,000 nested calls (deeper is an error, not a stack overflow); tail calls are
  loops and do not count. A range materialised in full is limited to 4,194,304 items (`XPDY0130`).

The complete, always up-to-date list, including smaller deviations and the reasoning for each, is in
[XsltCompatibility.md](https://github.com/CodeDeeds/CodeDeeds.Xslt/blob/main/source/CodeDeeds.Xslt/Documentation/XsltCompatibility.md),
with the history of the conformance work in
[ConformanceNotes.md](https://github.com/CodeDeeds/CodeDeeds.Xslt/blob/main/source/CodeDeeds.Xslt/Documentation/ConformanceNotes.md).

## Contributing

Issues and pull requests are welcome at <https://github.com/CodeDeeds/CodeDeeds.Xslt>. Most of the code was
written with the help of Claude Code.

## License

[MIT](https://github.com/CodeDeeds/CodeDeeds.Xslt/blob/main/LICENSE)
