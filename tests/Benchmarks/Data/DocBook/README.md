# DocBook documents for `DocBookTransformationBenchmarks`

Two documents from the tests of the [DocBook xslTNG](https://xsltng.docbook.org/) stylesheets, version
2.8.5, by Norman Walsh, fetched on 5 October 2026 from `src/test/resources/xml` of
<https://codeberg.org/DocBook/xslTNG>. They are under the MIT licence the project is under, which is
in `Stylesheets/DocBook/LICENSE`, and are not part of the CodeDeeds.Xslt package.

- `book.001.xml` is as it was published, byte for byte: a book in two parts, with its authors, revision
  history and legal notice, prefaces, a dedication, two chapters, two appendixes and a colophon. Seven
  sections and 77 paragraphs of filler, so what it exercises is the structure of a book and its title
  pages more than what goes inside a paragraph.
- `table-cals.049-1000-rows.xml` is the first thousand rows of `table-cals.049.xml`, which has 4,545
  and is 1.2 MB: one CALS table of twelve columns, some of its cells spanning rows and columns.
  Nothing else in it is changed. The benchmark cuts it again, to a hundred rows, when it sets up.

Some larger and more varied documents were left where they were, for their licences. `ptoc.001.xml` is
text copied from Wikipedia, which is under a licence of its own. The stylesheets' reference guide is
under the GNU Free Documentation License, and so is `chapter.003.xml`, which is a chapter of it.
