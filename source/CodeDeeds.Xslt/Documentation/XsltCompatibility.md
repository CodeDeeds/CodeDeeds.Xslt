# XSLT compatibility

What this engine implements of XSLT 1.0, 2.0 and 3.0, and what it does not. Everything not listed here is
implemented. The reasoning behind each decision, and the history of the conformance work, is in
[ConformanceNotes.md](ConformanceNotes.md); where the two disagree, this page is the one kept current.

## What it is

| Area| Notes |
| --- | --- |
| XSLT | XSLT Version 3.0, claimed by default. `XsltOptions.Version = XsltVersion.V20` gives a 2.0 processor: `system-property('xsl:version')` answers `2.0`, the vocabulary is 2.0's, and a `version="3.0"` stylesheet is read forwards-compatibly. A `version="1.0"` stylesheet runs with 1.0's semantics on either. |
| XPath | XPath version 3.1, including maps, arrays, function items, higher-order functions and the JSON functions. |
| Conformance level | Basic: not schema-aware, not streaming. |
| Backends | Interpreted (default) and compiled to IL (`XsltOptions.Backend`), which produce the same results. |

Conformance as last measured, on 12 September 2026, against the W3C suites (see `tests/W3CConformanceTests`):

| Suite | Result |
| --- | --- |
| XSLT 3.0 test suite, 3.0 processor | 7,580 of 7,618, 99.5% |
| XSLT 3.0 test suite, 2.0 subset on a 2.0 processor | 5,329 of 5,364, 99.3% |
| QT3 (XPath), 3.1 | 98.9% of 17,606 |
| QT3 (XPath), 2.0 | 99.0% of 14,180 |

The 2,900 streaming tests and 672 schema-aware tests are skipped by design.

## Not implemented, and not planned

| Feature | What happens |
| --- | --- |
| Schema awareness | `xsl:import-schema` is `XTSE1650`; `validation="strict"` and a `type` attribute are `XTSE1660`; there are no schema-typed sequence types. A 2.0 processor accepts only `validation="strip"`; a 3.0 one accepts `strip`, `preserve` and `lax`, as the specification lets a basic processor do. Nothing carries a type annotation, so element and attribute content atomizes to `xs:untypedAtomic`. |
| Streaming | `streamable="yes"` and `xsl:source-document` are accepted and processed over a tree held in memory; accumulators work the same way. `system-property('xsl:supports-streaming')` is `no`. |
| Extension elements and functions | There is no way to register one. A call to an unknown function is `XPST0017` when the stylesheet is compiled; an extension instruction takes its `xsl:fallback` when reached, and is `XTDE1450` without one. `element-available()` and `function-available()` answer for them. |
| `fn:load-xquery-module` | `XPST0017`. It needs an XQuery processor. |
| XInclude | Not applied to any document read. |

## Limits

| Area | Limit |
| --- | --- |
| `xs:integer` | A 64-bit signed integer. A literal or a cast beyond it is `FOAR0002` or `FORG0001`. |
| `xs:decimal` | About 28 significant digits, being `System.Decimal`. |
| Dates and times | Years 1 to 9999 of the common era. A negative year, or a year of five digits, is `FODT0001`. |
| Recursion | A template or function call 2,000 levels deep, or one about to exhaust the stack, is refused as an error. A call in tail position is a loop and does not count. |
| Entity expansion | Capped at ten million characters. |
| Regular expressions | The XPath regular expression language, with `(?:…)` from 3.0. Lookaround, atomic groups, named groups, inline options and comments are refused as `FORX0002`, as the specification requires, even though .NET would read them. |

## Partly implemented

| Feature | Supported | Not supported |
| --- | --- | --- |
| Forwards-compatible processing | Instructions. A stylesheet claiming a version later than the processor may hold instructions the engine has never heard of; each falls back when reached, and is an error only if reached without an `xsl:fallback`. | Expression syntax from a later version. Every expression is parsed when the stylesheet is compiled, so one that is not XPath 3.1 is rejected even if never evaluated. |
| Packages | `xsl:package`, `xsl:use-package`, `xsl:override`, `xsl:accept`, `xsl:expose`, visibility, `xsl:original`, version ranges on `package-version`, and the override signature check (`XTSE3070`). | A package used by two others that each override it differently, and re-exposing through `xsl:expose` a component a package accepted rather than declared. |
| Entry points | A named initial template (`XsltOptions.InitialTemplate`), an initial mode (`InitialMode`), an initial match selection (`InitialMatchSelection`), with template and tunnel parameters. | Starting at a named function. |
| Number words (`xsl:number`, `format-integer`) | Cardinal and ordinal words in English, German, French, Spanish, Portuguese (Brazil, and Portugal as `pt-PT`), Italian, Norwegian (Bokmål, and Nynorsk as `nn`), Swedish and Danish, with the feminine of a Romance ordinal on request. Numbering by any Unicode decimal-digit family, Latin letters and Roman numerals. | Other languages fall back to English. Other numbering sequences, such as circled digits or Greek letters, fall back to decimal digits. |
| Date and time names (`format-date` and relatives) | Month, day, era and am/pm names in the same languages; the Gregorian calendar as `AD` or `ISO`. Conventional abbreviations for English only; other languages are cut to the width asked for. | Other languages get English with the `[Language: en]` prefix the specification prescribes; other calendars get `[Calendar: AD]`. |
| `xsl:output` | Every attribute of every method, `json` and `adaptive` included; `normalization-form` NFC, NFD, NFKC and NFKD. | `normalization-form="fully-normalized"` is refused. `undeclare-prefixes` has no effect, since only XML 1.0 is written. |
| DTD processing | The internal subset: entities expanded, default attributes supplied, `ID`/`IDREF` attributes typed, unparsed entities declared. | The external subset and external entities are fetched only through `XsltOptions.EntityResolver`; with none, an external entity expands to nothing. |
| `disable-output-escaping` | Everywhere a result is serialized directly. | Inside `xsl:try`, whose content is built as a tree first, so the flag is lost. |

## Deliberate differences

| Area | Behaviour |
| --- | --- |
| Nothing is reachable by default | `xsl:include` and `xsl:import` need `XsltOptions.StylesheetResolver`; `document()`, `doc()`, `unparsed-text()` and `json-doc()` need `DocumentResolver`; `collection()` and `uri-collection()` need `CollectionResolver`, which names what a collection holds, and `DocumentResolver`, which reads it; `xsl:use-package` needs `PackageResolver`; external entities need `EntityResolver`; `xsl:result-document` needs `ResultStreamResolver` or `ResultResolver`; `environment-variable()` needs `EnvironmentVariablesEnabled`. Without one, the reference is an error and a stylesheet cannot reach the file system, the network or the process's environment. `FileResolver` serves a directory; `UriResolver` serves HTTP and HTTPS as well as a directory. Both serve a directory as a collection, its files in name order, narrowed by `?select=*.xml;recurse=yes`. |
| Tunnel parameters and `xsl:function` | A function starts with an empty tunnel set, so templates it invokes do not see what its caller was tunnelling. `tunnel="yes"` on a function parameter is refused. The specification never settled this corner. |
| Character maps | A map's own `xsl:output-character` children override the maps it draws in with `use-character-maps`. The map also applies to text written with `disable-output-escaping` and inside CDATA sections. |
| Result tree fragments (1.0) | Represented as trees, so they can be navigated. This accepts more than XSLT 1.0 allows and rejects nothing it permits. |
| Error timing | Errors are reported at compile time wherever possible. What the fallback mechanism covers, and a call to an extension function, is left to run time, since a stylesheet may hold those legitimately and never reach them. |
| `element-available()` | Answers for the instructions on a 2.0 processor, and for every element the specification defines on a 3.0 one. `xsl:result-document` is reported available even when no result resolver is configured, and `function-available('id')` is `true`. |
| `xsl:sort` without `lang` | Text is compared by code point rather than by the machine's culture, so one stylesheet orders the same everywhere. `lang` or `collation` asks for a named collation and gets it, on `xsl:sort`, `xsl:merge-key`, `xsl:for-each-group` and `xsl:key` alike. |
| Document order across documents | Nodes of several documents are ordered by the sequence the documents were loaded in. Stable within a transformation, which is all the specification asks. |
| Binary ordering | `xs:hexBinary` and `xs:base64Binary` are ordered octet by octet at every version, which XPath 3.1 defines and 2.0 left undefined. |
| Output details | Indentation uses `\n` and indents uniformly; where exactly lines break is the processor's choice. `xsl:vendor` is `CodeDeeds` and `xsl:vendor-url` is empty. |

## Using it from .NET

| Setting | Notes |
| --- | --- |
| `XsltOptions.Parameters` | Values for the stylesheet's top-level `xsl:param` declarations, keyed by local name or `{uri}local`. A value may be a string, a boolean, an integer type, a `double`, `float` or `decimal`, an `XdmTree`, an `XPathValue`, or `null` for the empty sequence; anything else is refused. A date or duration is passed as its lexical form. Names the stylesheet does not declare are ignored. |
| `XsltOptions.OmitXmlDeclaration` | `null` follows the stylesheet, `true` suppresses the declaration, `false` forces it. `Xslt.With` gives a differently configured instance without compiling again. |
| `Xslt.OutputMethod`, `OutputEncoding`, `OutputMediaType` | What the stylesheet's `xsl:output` settled, for a caller setting a `Content-Type` header. |
| `XsltOptions.BaseUri`, `InputUri`, `BaseOutputUri` | Where the stylesheet, the input and the principal result are, which relative references resolve against. |
| `XsltOptions.DynamicEvaluation` | `xsl:evaluate` is on by default and can be switched off, which makes `element-available('xsl:evaluate')` false. |
| `XsltOptions.EnvironmentVariablesEnabled` | Off by default, so `environment-variable()` and `available-environment-variables()` answer nothing. On, they read the process's environment as it stood when the transformation first asked. |
| `XsltOptions.CollationResolver` | Collations of the caller's own, as `XsltCollation` subclasses under URIs of the caller's choosing, for `xsl:sort`, `xsl:for-each-group`, `xsl:key`, `default-collation` and every function that takes a collation. Provided without one: the Unicode codepoint collation, `html-ascii-case-insensitive`, and the UCA collation with its `lang`, `strength` and related parameters, but not `alternate=shifted`. Any other URI is `FOCH0002`. A caller's collation that makes no key cannot group, key or `distinct-values()`, and one that does not match substrings cannot `contains()`; both are `FOCH0004`. |
