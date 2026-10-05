# DocBook xslTNG 2.8.5, as the benchmarks use it

Part of the [DocBook xslTNG](https://xsltng.docbook.org/) stylesheets, version 2.8.5
(`xslt/VERSION.xsl` says which), by Norman Walsh. They are here so that the benchmarks have a large
stylesheet somebody else wrote: `XsltCompilationBenchmarks` compiles it, and
`DocBookTransformationBenchmarks` transforms the documents under `Data/DocBook` with it.

They were fetched on 5 October 2026 from `https://cdn.docbook.org/release/xsltng/current/xslt/` and are
unmodified, byte for byte: `.gitattributes` keeps their line endings as they were published. The project
itself is at <https://codeberg.org/DocBook/xslTNG>.

What is here is what those two benchmarks read and nothing else of the distribution:

- the fifty modules that compiling `xslt/docbook.xsl` reads, about 800,000 characters;
- the four stylesheets of `xslt/transforms` that a document with nothing unusual in it is put through
  before it is formatted, and `xslt/environment.xsl`, which they include;
- `xslt/locale/en.xml`, the English localization, and `xslt/modules/templates.xml`, the title pages.

The other transforms, the other localizations, the CSS and the JavaScript are not here, so
the copy is not a working DocBook installation: a document in another language, one written in DocBook
4, or one that uses XInclude asks for something it does not have.

They are under the MIT licence, which is in `LICENSE` beside this file, and are not part of the
CodeDeeds.Xslt package.
