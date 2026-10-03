# Schema awareness: the plan to finish

Written 3 October 2026 after reviewing [SchemaAwarenessPlan.md](SchemaAwarenessPlan.md) against the code, the
notes and a fresh `--schema` run, and carried out the same day. This page keeps the plan as it was made and
says, under each phase, what came of it. The reasoning behind each result is in
[ConformanceNotes.md](ConformanceNotes.md), and what the engine does is in
[XsltCompatibility.md](XsltCompatibility.md).

## Where it stood

The schema-aware XSLT 3.0 run (`--xslt --30 --schema`) was **8,668 of 8,727, 99.3%**, with 59 failures:

| Count | Cause |
| --- | --- |
| 46 | One thing. `System.Xml.Schema` holds dates in `System.DateTime`, so a source document with `-0012-12-03-05:00` is "not valid". |
| 4 | Small validation rules: `validation-0006`, `validation-1702`, `import-schema-137`, `validation-0201`. |
| 9 | Nothing to do with schemas. They fail in the headline run too. |

What the suite did not measure was most of "full support": the QT3 driver skipped every schema environment
(140 tests, and 485 more depending on `schemaImport` or `schemaValidation`), there was no public way to
validate a document or read a type annotation, and `xsi:schemaLocation` was never followed.

## Decisions

- **Dates.** Accept .NET's `DateTime` range, report it properly, document the difference.
- **XSD 1.1.** Dropped for now. XSLT 3.0 does not ask a schema-aware processor for it. Doing it would mean our
  own validator in place of `XmlSchemaValidator`, and would need a plan and a measured spike of its own.

## Phases, and what came of them

**S0. Housekeeping. Done.** The original plan's status, risks and next steps were brought current;
`XsltCompatibility.md` has one row for schema awareness in *Partly implemented*, a row for XSD 1.1 where
"not planned" belongs, and says conformance is basic by default and schema-aware on request.

**S1. Dates. Done, as decided.** A validation error whose value is a negative year or one past 9999 now says
the validator is .NET's and holds years 1 to 9999. The code is unchanged. The conformance driver reads that
sentence out of the error and skips the test with the reason named, 44 tests, rather than carrying a list of
names. Two tests that want a typed date out of such a document still show as failures.

**S2. The four small rules. Done, with no engine change.** Each was already argued in the notes as a mistake or a
contradiction in the suite: `validation-0006` asks for the code for a document node's ID constraints,
`validation-1702` for the strict code under lax, `validation-0201` compares serialized whitespace, and
`import-schema-136` and `-137` are one stylesheet with two different answers.

**S3. The XPath side. Done, and it found real faults.** The QT3 driver reads its schema environments under
`--schema`: 196 tests came into the 3.1 run and 83 into the 2.0 run, and the runs stand at 18,464 of 18,481
and 14,636 of 14,660. Seventeen failed at first, all faults and none about schemas as such: `cast as` and
`castable as` did not atomize an array; an element of a list type, or a union's ID member, was never an ID;
`element(*, xs:numeric)` matched nothing; `fn:analyze-string()` was untyped; `fn:json-to-xml(validate)` had no
schemas outside a stylesheet; `serialize()` mishandled `standalone` and a two-character character-map key.
All fixed, with unit tests. The plain runs are unchanged, test for test, on both backends.

**S4. Public API. Done.** `XdmSchemas` (`Parse`, `Validate`), `XdmTree.TypeNameOf`, `IsNilled` and
`HasTypeAnnotations`, and `XPathStaticContext.TypedSchemas`. It is also what let the QT3 driver, which has no
access to the engine's internals, do S3.

**S5. Schema reachability. Done.** `XsltOptions.FollowSchemaLocation`, off by default, fetching through
`SchemaResolver` onto a per-read copy of the schema set. A relative `xs:include` in an inline schema was found
to work already through the resolver, and is now tested. A document element undeclared in a namespace the
schemas cover is `XTTE1512` under strict and untyped under lax, where it had been `XTTE1510` and `XTTE1515` respectively.

**S6. XSD 1.1. Dropped.**

## Result

- Schema-aware XSLT run: **8,668 of 8,683, 99.8%**, 44 skipped for the date range, 15 failures: nine unrelated to
  schemas, four suite mistakes, and the two typed-date tests. Both backends agree test for test.
- QT3 schema runs: 18,464 of 18,481 (3.1) and 14,636 of 14,660 (2.0), the failures the plain runs' own.
- Headline runs unchanged: XSLT 3.0 8,061 of 8,071, 2.0 5,678 of 5,701, QT3 18,268 of 18,285 and
  14,553 of 14,577, failure sets identical on both backends.
- 3,084 unit tests pass, 20 of them new, in `SchemaCompletionTests`.

## Not done, and why

- **XSD 1.1**, by decision.
- **Streaming**, as before.
- **A type only a schema hint supplies cannot be named by a stylesheet**, since the stylesheet is compiled before
  any document is read. A hint types the nodes and is matched by name.
- **Benchmarks of the new paths.** `SchemaAwareBenchmarks` was not run again; following hints compiles a schema set
  per validated read, which the option's documentation says, and was not measured.
