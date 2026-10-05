# DocBook xslTNG 2.8.5, as the benchmarks compile it

The XSLT modules of the [DocBook xslTNG](https://xsltng.docbook.org/) stylesheets, version 2.8.5
(`xslt/VERSION.xsl` says which), by Norman Walsh. They are here so that `XsltCompilationBenchmarks` has a
large stylesheet somebody else wrote to compile: fifty modules and about 800,000 characters, where the
two product stylesheets beside this folder are one module and a few thousand.

They were fetched on 5 October 2026 from `https://cdn.docbook.org/release/xsltng/current/xslt/` and are
unmodified, byte for byte: `.gitattributes` keeps their line endings as they were published. The project
itself is at <https://codeberg.org/DocBook/xslTNG>.

These are the modules that compiling `xslt/docbook.xsl` reads and nothing else of the distribution. The
locale files, the CSS and the JavaScript that a transformation with these stylesheets goes on to read are
not here, so the copy is for compiling and is not a working DocBook installation.

They are under the MIT licence, which is in `LICENSE` beside this file, and are not part of the
CodeDeeds.Xslt package.
