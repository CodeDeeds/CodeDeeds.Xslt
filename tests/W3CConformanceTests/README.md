# Conformance

Runs the two W3C suites this engine is answerable to and reports where it stands.

| suite | what it measures | how to run it |
|---|---|---|
| [qt3tests](https://github.com/w3c/qt3tests) | XPath — expressions, the function library, the type system | `-- <path>` |
| [xslt30-test](https://github.com/w3c/xslt30-test) | XSLT — the language around the expressions | `-- --xslt <path>` |

**The two figures are not comparable and are not meant to be.** They measure different halves of the language
against differently written suites, and one is much further along than the other. Quote which one you ran.

## Running them

Neither suite is part of this repository. Clone them anywhere:

```
git clone --depth 1 https://github.com/w3c/qt3tests.git
```

```
git -c core.longpaths=true clone --depth 1 https://github.com/w3c/xslt30-test.git
```

The `core.longpaths` is not optional on Windows: the XSLT suite carries a copy of the DocBook stylesheets,
whose deepest paths exceed `MAX_PATH` and fail the checkout without it.

Then point the driver at one, by argument or through the `QT3TESTS` / `XSLT30TESTS` environment variable:

```
dotnet run --project CodeDeeds.Xslt.Conformance -- ../../qt3tests
```

```
dotnet run --project CodeDeeds.Xslt.Conformance -- --xslt ../../xslt30-test
```

`XSLT30SETS=fn/base-uri,attr/mode` runs only the test sets whose names contain one of the comma-separated
parts, for iterating on one cluster without waiting for the whole run.

A second argument filters which failures are listed in full, which is how to look into one category at a time
rather than reading past the other ten thousand:

```
dotnet run --project CodeDeeds.Xslt.Conformance -- ../../qt3tests "Expected"
```

## Which version each runs

The version the driver asks the engine to be, which is 2.0 unless opted in below. It is asked for by name
because the engine claims 3.0 for a caller who names none, and the 2.0 half of a run is only meaningful
against a 2.0 processor. `--31` opts the QT3 run into the
tests marked `XP30+` and `XP31+`, and `--30` opts the XSLT run into those marked `XSLT30+` — and into a
processor that claims 3.0, `system-property('xsl:version')` included; that is how the work towards 3.0
and 3.1 — maps, arrays, function items — is measured.

Opting in reads thousands more tests, many of them about syntax a 2.0 processor is required to *refuse*, so
mixing the two would measure the wrong thing in both directions.

## Which backend each runs

`--compiled` runs the XSLT suite with `XsltBackend.Compiled`, which emits IL for expressions instead of
walking the expression tree. The backend it selects is named in the headline, because two runs of this suite
otherwise differ by something the numbers do not say.

**The two backends must reach the same verdict on every test.** An expression the emitted backend cannot
express calls back into the interpreted node it was compiled from, so the suite is checking the part that was
emitted and nothing else, and any test that passes one way and fails the other is a fault in that part. That
is the whole point of the flag: 12,880 tests across the two runs, against 28 of 68 unit-test files that
assert the two agree.

It found three failures on its first run, and they were one bug: `position()` and `last()` were emitted as
`xs:double` at every version, where from 2.0 on they are an `xs:integer`. **Both runs now report the same
figures under either backend**, and a diff of the two failure name-sets is empty at both versions — which is
the state the flag exists to keep, not a fact about it worth recording once.

The flag is for the XSLT suite only. The QT3 runner parses and evaluates an expression directly rather than
through a stylesheet, so there is no `XsltOptions` in its path to carry a backend, and the driver says so
rather than running the interpreter and reporting the number as though it had measured the other one.

## Schema awareness, while it is being built

`--schema` runs the XSLT suite with `XsltOptions.SchemaAware`: the schemas an environment declares are
loaded into `XsltOptions.Schemas`, a `schema-location` is read from the suite, and the tests marked
`schema_aware` are judged rather than skipped. It is opt-in so that the headline figure does not move while
the phases of `Documentation/SchemaAwarenessPlan.md` land one at a time: a test that needs typed input or
validation of constructed nodes fails under the flag until the phase that provides it, and the count of
those failures is the measure of what is left. Without the flag a run is what it was.

On landing phase 1 (13 September 2026) the schema-aware 3.0 run stood at **7,673 of 8,208, 93.5%**: 590
tests came in that the plain run skips, 93 of them pass on the type system alone, and the 497 that fail
are where the plan says the next two phases are — 179 in `decl/import-schema` and 43 in `attr/validation`
wanting `type` and `validation` on constructed nodes, and 81 in `attr/match`, 50 in `attr/as` and 34 in
`expr/nodetest` wanting annotated input.

Phase 2, typed input, took it to **7,822 of 8,208, 95.3%** on the same day, on both backends. A source
the catalog marks `validation="strict"` is now read under `XsltOptions.InputValidation`, and the tests
that ask about annotated input followed: `expr/nodetest` fell from 34 failures to none,
`attr/strip-type-annotations` from 4 to 1, `attr/xpath-default-namespace` from 4 to none, `attr/match`
from 81 to 27 and `attr/as` from 50 to 33. Of the 386 failures left, 293 were one thing said many ways:
`validation` or `type` on an instruction that constructs nodes.

Phase 3, validation of constructed nodes, took it to **8,101 of 8,207, 98.7%** (14 September 2026), the
same on both backends, with the headline runs unchanged. `validation` and `type` on `xsl:element`,
`xsl:attribute`, `xsl:copy`, `xsl:copy-of`, `xsl:document`, `xsl:result-document` and literal result
elements build their result, validate it against the schemas through .NET's push validator, and carry
what validation settled onto the output; the `XTTE15xx` codes are chosen by what was being validated.
All 293 refusals cleared: `insn/copy` fell from 17 failures to 2, `attr/validation` from 31 to 17,
`attr/as` from 33 to a handful, `misc/error` from 26 to 6.

Phase 4, the tail, took it to **8,110 of 8,207, 98.8%**, both backends. `fn:json-to-xml` with
`validate:=true` validates its result against the schema for the XPath functions namespace, which is
built in, and `xsl:evaluate schema-aware="no"` — the default — raises `XTDE3160` where the target names
an imported type.

`xs:NOTATION` followed, taking it to **8,115 of 8,207, 98.9%**. A notation and a plain name are two
primitive types held alike here, so a notation carries a mark beside the name it is held as and the
matching paths refuse to call one the other; a name written with no prefix in validated content takes
the default namespace in scope; and a key or a group now files a node under its typed value rather than
its text, so two names spelled with different prefixes are one key.

A last round on the remaining validation issues reached **8,125 of 8,207, 99.0%**, both backends.
Validation is declared per source, so the driver now tells the engine which documents an environment
validates and the engine honours it per document; a rule in a `typed="strict"` mode matching an
undeclared element is refused when the stylesheet is compiled; and several error codes were put right —
a schema that is reached and is not a schema is `XTSE0220` rather than `XTSE0165`, an `xs:unique`
failure is ordinary invalidity rather than the ID constraint's `XTTE1555`, and a document node validated
against a named type is still held to its own shape.

Of the 82 left, six asserted that the result *document* carries type annotations, which the driver could
not answer: it handed the engine's serialized output to a fresh parse, and serialized XML carries no
annotations, so `not(/* instance of element(*, xs:untyped))` was answered no however the transformation
had gone. The engine now hands a result back as a tree — `TransformXmlToTree` and its siblings — and the
driver puts an assertion the serialized text answered no to that tree as well. The two are one result in
two renderings, and one the engine satisfies in either it satisfies. The assertion's own static context
also gets the environment's schemas, since `schema-element(E)` in an assertion is a question that cannot
be read without them. That took the run to **8,204 of 8,280, 99.1%**, both backends, and the named-function entry point below to **8,223 of 8,299**, and the driver work after it to **8,345 of 8,430**, and the module-boundary fixes below to **8,352 of 8,430**, and the error codes after them to **8,449 of 8,527**.

Of the 76 left, 15 fail in the headline run too and have nothing to do with schema awareness. Four name
schema files the suite does not contain. The rest are small individual rules.

## When the driver is behind the engine

Each driver keeps a list of the suite's feature names it does not claim, and every test declaring one is
skipped. The list is written by hand, so it goes stale the moment a feature is finished and nobody thinks to
take it out again. That is a silent failure: the tests keep being skipped, the engine keeps being unmeasured,
and nothing in the output says so.

Both lists had drifted. `namespace_axis` was still in the XSLT driver's and `namespace-axis` in the XPath
driver's, long after the axis was built. Taking the two entries out brought **71 XSLT tests and 9 XPath tests**
into the runs. Of the 71, **59 passed at once and 12 failed** — twelve real gaps that had been hidden behind a
stale line in a list rather than behind anything about the engine. All twelve are fixed, and five causes
account for them:

| what was wrong | tests |
|---|---|
| The serializer wrote `xmlns:xml`, which is implicit in every XML document and is never output | 4 |
| `base-uri()` of a namespace node answered the element's, where the data model gives it nothing | 4 |
| `xsl:key` did not index namespace nodes, so a `namespace-node()` pattern filed nothing | 2 |
| `current()` inside a key's `use` was not the node being indexed | 1 |
| A copied namespace node taking the element's own prefix was dropped, where namespace fixup renames the element | 1 |

Three entries in the XPath driver's list looked equally stale and were not. `higherOrderFunctions`,
`fn-transform-XSLT` and `fn-transform-XSLT30` name functions this engine has — `function-lookup()` and
`transform()` — but has only inside a stylesheet. The XPath driver evaluates an expression directly, with no
`XsltOptions` and no stylesheet around it, so neither function is in the context it builds. Removing the three
entries ran 1,676 more tests and failed 807 of them. They went back in: that is the driver's limit and not the
engine's, and it sits beside `schemaImport` and `schemaValidation`, which are in the list for the same reason —
the driver has no way to load an environment's schemas.

**The list is an assertion about the engine that nothing checks.** It is worth re-reading whenever a feature
lands, and the cost of not doing so is measured above: twelve defects that the suite had been ready to report
for as long as the entry was wrong.

## The third way in

XSLT 3.0 defines three entry points and this engine had two: a source document to apply templates to, and
a named template to call. The third calls a named stylesheet function with arguments the caller supplies
and makes what it returns the whole result. Thirty-eight tests declared it and the driver skipped every
one, which is not the same as their failing but is not a measurement either.

It is `XsltOptions.InitialFunction` with `FunctionArguments` now. How many arguments there are is the
arity the function is looked up by, so a stylesheet declaring one name at two arities is asked for the one
the call fits; the function must be public, a package's private function being no way in; and a name with
no public function of that arity is `XTDE0041`. The arguments are converted by the call itself, exactly as
a call written in a stylesheet would be, so a value that will not convert raises the type error conversion
raises rather than one of the entry point's own. Nineteen of the thirty-eight now run, and all nineteen
pass; the rest are skipped for a second reason, most of them asserting the result as a typed sequence.

## What the driver could not present

A skip is not a failure, but it is not a measurement either, and three of the driver's own limits were
skipping 132 tests between them. Each turned out to be smaller than it looked.

**A test naming several stylesheets** was 85 of them. It names one to run and the rest as modules it
imports or includes, and those are files beside it that the suite resolver already serves by URI. All the
driver had to settle was which to compile: the one the catalog does not mark `role="secondary"`. Of the 85,
80 pass at once, and the five that did not are five real gaps, taken below. Three more were found and
fixed on the way there: an
`xsl:import` inside an external entity resolved against the module rather than against the entity it was
read from, `doctype-system=""` wrote a document type declaration where erratum E31 says it takes one back,
and `xsl:strip-space` and `xsl:preserve-space` were ranked by specificity without ranking by import
precedence first, so an imported `preserve` of a name beat an importing `strip` of a wildcard.

**An environment declaring a resource** was 27. A resource is a file served at a URI with a stated media
type and encoding, which the resolver already serves — all that was missing was honouring the declared
encoding, since `unparsed-text()` reads bytes as characters and a file with no byte-order mark says nothing
about itself. Still skipped: a resource naming an `http:` URI, which would make the run depend on the
network, and one holding an XQuery module, which is code for a processor this engine has not got.

**`assert-message`** was 20. It asks about what `xsl:message` wrote rather than about the result, with the
same assertions the catalog uses for the result, so the driver keeps each message and stands it in the
result's place. That found the messages themselves being flattened: a message is a document node built from
the instruction's content, and an `xsl:message` writing an element means the element. They are serialized
now, as every other processor presents them.

## Where one module ends and another begins

Running the tests that name several stylesheets left five failures, and they had one thing in common: each
was about what a module may see of another module. None of them can arise in a stylesheet of one file,
which is why they had gone unnoticed, and each was its own rule.

**`xsl:apply-imports` reached too far.** The rules it considers are the ones imported into the module
containing the rule that wrote it, and that is not the same as every rule of lower precedence. A module
importing two others gives both a lower precedence than its own, and neither of them imported the other,
so a rule in one must not override a rule in the other. Precedences are handed out depth-first, so what a
module imported is the contiguous range from its first import to its own precedence; a rule now carries
the bottom of that range as well as its own precedence, and the search is bounded at both ends.

**`xsl:namespace-alias` moved attributes it should have left.** Aliasing the default namespace moves a
literal result element written with no prefix, the default namespace being one an element can be in. An
attribute written with no prefix is in no namespace at all, so there is nothing there to move: a
stylesheet writing out a stylesheet means `version="1.0"` and not `xsl:version="1.0"`.

**One key declared by a 1.0 module and a 2.0 module could only answer one of them.** The 1.0 declaration
files a value as the string it spells and the 2.0 declaration files it as what it is, and a lookup asks
the key one question. Which declaration answers depended on how it asked, and both ways of asking are
entitled to an answer over the whole key. A key any 1.0 module declared is now filed both ways; the second
filing costs nothing wherever the two spellings agree, which is every string value and every value of a
key no 1.0 module declared.

**A module could not be found inside a document that is not one.** An `xsl:import` or `xsl:include` may
name an element by a fragment identifier rather than naming the document, which is how a stylesheet is
carried in something else. The identifier is an attribute the document type declared as an ID, so the
document says which attribute that is, and the module is compiled from that element rather than from the
document element.

Seven tests in all, the five and two that came with them: an `xsl:import` written after the templates it
imports over, and a second `namespace-alias` case. The suites reach **7,792 of 7,828** at 3.0 on both
backends, **5,500 of 5,533** at 2.0, and **8,352 of 8,430** schema-aware.

## Saying which rule was broken

A test that expects an error and gets one with no code is **skipped**, not passed: the right thing
happened and whether it happened for the right reason is unknown. Ninety-seven tests stood there, and they
were the largest single block of skips the driver had left that was about the engine rather than about the
driver. The engine refused every one of those stylesheets. It simply did not say which rule they broke.

Naming the codes turned 86 of those skips into passes. Most were a line each: a reference to a module that
is not there is `XTSE0165`, a prefix nothing binds is `XTSE0280`, an attribute value template with an
unclosed brace is `XTSE0350`, an attribute set that uses itself is `XTSE0720`. Four needed a distinction
the engine was not drawing at all:

| what had to be told apart | codes |
|---|---|
| A module that reaches itself, by `xsl:include` or by `xsl:import` | `XTSE0180`, `XTSE0210` |
| A module that is not a stylesheet: the one handed in, or one a reference reached | `XTSE0150`, `XTSE0165` |
| Two components of one name: two declarations, or a declaration beside an override of it | `XTSE0660`, `XTSE3055` |
| An `xsl:import` after another top-level element, which 2.0 forbids and 3.0 allows | `XTSE0200`, none |

The last of those is a rule this engine had never applied, and the tests for it name modules that are not
in the suite: a stylesheet that puts its imports in the wrong place is wrong whether or not the modules it
names are there to be read, so the placement is now checked before the reference is followed.

Two more came out of reading what the engine said while it said it. `Q{uri}local` is a name with its
namespace in braces and no prefix at all, and the check for a function name in no namespace was reading it
as one — so `<xsl:function name="Q{f}n"/>` was refused for a fault it did not have. And a template that a
package *uses* is not an entry point of the package using it unless that package accepted it as one, which
is a different question from what the library declared.

**The driver now prints what the engine said** beside a code it did not expect, and beside a refusal that
carried no code. A code that is wrong is a code the engine chose for a reason, and the reason is what says
which of the two is mistaken. Every fix above was found by reading that line.

**None of the ninety-seven are left**, in any of the three runs. The last nine came to four more sites and
one defect. The defect: an `xsl:apply-templates` with no `select` reaches for the children of the context
item, and where that item was an atomic value it walked off the end of the node arrays with an
`IndexOutOfRangeException` rather than raising `XTTE0510`. It reached them by two routes, sorted and
unsorted, and the guard is asked once before either. The four sites were a missing required attribute,
which is `XTSE0010` wherever it is missing from; `tunnel="yes"` on a function parameter, which a function
has no use for; and an instruction from a later version reached with no `xsl:fallback` to stand in for it.

One test is worse off for it. `sequence-0132` wants `XTTE0570` for an `xsl:sequence` with content in both
languages, and `sequence-0137` wants `XTSE0010` for the same thing in 2.0, where the `select` really was
required. Three tests take the second reading and one takes the first, so the second is what the engine
does and the first is recorded here.

## Reading the result

A test the driver cannot present fairly is **skipped with a reason** rather than counted either way, so the
pass rate is over tests that were genuinely attempted. Skips are reported by reason; most are simply tests for
a version or a feature this engine does not claim.

**Error codes are checked where the engine assigns one.** `XsltException.Code` carries the code the
specification names, and a test expecting a particular error passes only when that code matches. Where the
engine raises the error but has not been given a code for it, the test is **skipped** rather than passed: the
right thing happened and whether it happened for the right reason is unknown, so counting it either way would
be a guess. Those skips are the work queue for finishing the codes off.

The driver reports **where** the failures are as well as what they are. The two are different questions and
the first is the one that decides what to do next: one absent instruction shows up as a dozen
unrelated-looking reasons, while a reason shared across twenty files is nobody's next task.

### What an XPath assertion is put to

The result tree, where the serialized result answers no. An assertion such as `/out = 'true'` is about
the result as a data model instance, and the text is that result after a serializer has been over it:
`indent="yes"` puts whitespace between elements that the tree never held, and a string value read back
out of the text carries it. The driver already ran the transformation a second time into a tree for a
schema-aware run, where a type annotation cannot survive the round trip either; it does so on every run
now, and only for an assertion that has something to gain by asking again.

A fragment first, and a document only if that fails. Most results are a fragment — a bare string, two
elements side by side, a comment on its own — and what a fragment may not carry is a document type
declaration, so a result that has one could not be read at all and the test was set aside as *the result is
not well-formed XML*. A stylesheet writes its own doctype where `xsl:output` has no attribute for the one it
wants, which is how the DocBook XHTML5 stylesheets write the HTML 5 form, and `docbook-001` counts the
elements underneath it. Four tests are still skipped for that reason and are right to be: their stylesheets
produce HTML, where `<meta>` and `<br>` are written with no closing tag, and no reading of that is XML.

### What `assert-xml` is measured against

Not the result as the stylesheet asked for it. The catalog says so in as many words: the assertion supplies
"a serialization of the expression result using the default serialization parameters `method="xml"`
`indent="no"` `omit-xml-declaration="yes"`". Everything else the driver asks — an XPath assertion, a string
value, a serialization match — wants the stylesheet's own output, and that is what it runs with; but where
the two differ, this assertion is settled by the first.

The difference is the html and xhtml output methods. Both add a `meta` element to `head` that the result
tree never held, and the html one writes `<META ...>` and `<BR>` with no closing tag, which no XML parser
will read — so a test whose stylesheet has no `xsl:output` at all and happens to produce an `HTML` document
element was being compared as HTML against an expectation written as XML. `bug-1901`, `sequence-0601` and
`accumulator-040` are that, and their expected results have the `meta` element explicitly removed.

So `assert-xml` looks twice: the result as it was serialized, and, where that does not match, the result
tree serialized again with the default parameters. The second costs a second run of the transformation and
is paid only by a test that would otherwise be reported as failing.

## The XPath run

The 2.0 run stands at **14,553 of 14,577, 99.8%**, and the 3.1 run — `--31`, which takes in the tests marked
`XP30+` and `XP31+` — at **18,268 of 18,285, 99.9%**. The second reads 3,708 more tests than the first and is
level with it, which is not the shape to expect: what it takes in is the newer half, and the newer half
was where the work was until lately.

| area | 2.0 | 3.1 |
|---|---|---|
| `math` | *3.0 and later* | 130 / 130, 100% |
| `misc` | 31 / 31, 100% | 33 / 33, 100% |
| `app` | 331 / 332, 99.7% | 826 / 828, 99.8% |
| `array` | *3.1* | 34 / 34, 100% |
| `op` | 3,225 / 3,225, 100% | 3,445 / 3,445, 100% |
| `prod` | 5,751 / 5,760, 99.8% | 6,189 / 6,192, 100% |
| `fn` | 5,145 / 5,156, 99.8% | 7,385 / 7,394, 99.9% |
| `map` | *3.1* | 112 / 112, 100% |
| `xs` | 70 / 73, 95.9% | 114 / 117, 97.4% |

The failures no longer cluster anywhere. The largest set on either run holds five, and every other one
holds four or fewer. Casting led this list by some way not long ago, then `op/to`, then
`fn/parse-json`; those three sets are empty now, and so are `array`, `map`, `math`, `misc` and `op`
entirely.

What is left divides in two. Rather more than half is the driver rather than the engine: four
`fn/collection` tests it wires up no collections for, three that want a document or a language it does
not supply, and a couple where its own `assert-eq` cannot build the expected value. The rest is the
engine, and thins out into ones and twos: `xs:dateTimeStamp`, which is an XSD 1.1 type this processor
does not have; `fn:distinct-values` over two numeric types, where <code>eq</code> is not transitive and
a single-pass hash cannot reproduce it; two EQName rules whose codes are XQuery's. The compatibility
document keeps the account of what is behind each.

### Where an environment's files are named from

An environment names its source documents with a file path, and where that path is relative to depends on
where the environment was written. One declared in `catalog.xml` names its files from the root of the
suite; one declared inside a test set names them from that test set's own directory, which is how
`fn/collection.xml` reaches `../docs/bib.xml`. Resolving every one of them from the root left **767 tests
skipped** as *context document would not load*, not one of which had anything wrong with it — and the
reason read like a statement about the suite rather than about the driver. The environment now carries the
directory it was read in: 664 of those tests run and 644 of them pass, which is where most of the
difference between this run and the last one is.

The catalogs are read with `LoadOptions.PreserveWhitespace` for a related reason. XLinq throws away a text
node that is whitespace and nothing else, so an `assert-string-value` holding a character reference for a
carriage return arrived as the empty string, and every result but the empty one was reported as differing
from it. Some of the expectation is whitespace, so none of it can be dropped.

### Which XML Schema version a test asks for

XSD 1.1 widened several value spaces this engine reads by 1.0 rules: it added the year zero, allowed a
leading plus on `INF`, and introduced `xs:dateTimeStamp`. A test case that carries
`<dependency type="xsd-version" value="1.1"/>` is asking for the 1.1 answer and would be told the 1.0 one,
so the driver skips it — 26 cases, mostly in `CastExpr` and `CastableExpr`.

Only where the **case** declares it. A whole test set carrying the dependency is saying what its subject
came in with rather than what each case expects, and taking that at face value would hide answers instead
of a version: every one of the 54 cases in `xs/error.xml` passes here, because `xs:error` belongs to
XPath 3.0 as much as to XSD 1.1. Skipping on the set-level declaration cost 35 genuine passes to hide 17
failures, which is the wrong trade in both directions — so the three cases in `xs/dateTimeStamp.xml` are
run and reported as what they are, a type this engine has not implemented.

`assert-eq` builds the value it expects with a small literal parser rather than by evaluating the expectation
through the engine under test. That is deliberate: using the engine to decide what the engine should have
produced would make a suite that agrees with itself. Anything the literal parser cannot build is a skip.

- **`assert-deep-eq` and `assert-permutation` are judged where the expectation is a sequence of literals**,
  built by that same parser and for that same reason. Where it is anything else — a function call, a path, an
  operator — the test is skipped rather than decided by asking the engine what it should have produced.
- **`assert-type` is skipped**, and this one will stay skipped: deciding it means asking this engine's own
  `instance of` whether the result matches the asserted type, and those tests are a test *of* the type
  matcher. `assert-xml` and `assert` are skipped for want of a way to present them.
- **An `any-of` no branch passes and at least one branch was skipped.** The suite writes a good many results
  as *either this value or this error*, because a processor is free to be liberal about an input or to
  refuse it, and both answers are right. Where the branch this engine took is one the driver cannot check —
  an `assert-type`, say — reporting the other branch's complaint would say the engine got it wrong where
  what happened is that the driver could not tell.

A test carrying a `language`, `default-language` or `format-integer-sequence` dependency is **judged, not
excused**, where the thing it asks for is something this engine has: English, and the decimal digit families.
A numbering sequence that is a list of symbols rather than a family of digits — circled digits, Greek letters,
Kanji — is not, and a test wanting one is skipped. `satisfied="false"` is honoured in both directions, so a
test written for a processor *without* a given sequence runs here.

German is not claimed here, though it is the second language this engine spells numbers in, because a QT3
`language` dependency covers the names of months and days as well as numbering: twelve of the fourteen
German tests want `MÄRZ` and `Donnerstag` rather than `zwanzigste`. The XSLT suite's
`languages_for_numbering` asks the narrower question, and that one is answered — see *What language a number
is spelled in* in the compatibility document.

The `xpath-1.0-compatibility` feature is declared **unsupported**, which is honest rather than modest: an
expression is compiled against a stated version here and this driver states 2.0 or 3.0, so there is no mode in
which `format-number('foo', '#')` answers `NaN` rather than raising a type error. A test asking for that mode
is asking about a language this run is not running.

An environment's `decimal-format` declarations are **read and declared** against the static context, which is
the same path a stylesheet's `xsl:decimal-format` takes into the engine — XPath 3.0 made the decimal formats
part of the static context, so there is a public way to declare them without a stylesheet. The prefix in a
`name` is bound on the `decimal-format` element itself, so a name is resolved against that element rather than
against the environment's own `namespace` declarations.

Neither suite covers **XPath 1.0**, which predates them both. The 1.0 behaviour this engine implements is
guarded by the differential tests against `System.Xml.Xsl.XslCompiledTransform` in the unit test project, so
those remain the authority on that half and should not be retired in favour of these.

## The XSLT run

Here the suite asks most of its questions in XPath: `<assert>` holds an expression over the result document,
and it is two-thirds of every assertion in the suite. Answering those means using this engine's own XPath to
judge its own XSLT — which is sound for a reason worth stating, and only for that reason: **the XPath half is
separately measured**, by the run above, so it is a checked instrument rather than an unchecked assumption. An
assertion expression this engine cannot evaluate is skipped and named, because that measures the instrument
rather than the test. The same goes for `serialization-matches`, whose pattern is read by this engine's
`matches()` rather than by .NET's regular expressions directly: the two languages disagree on enough that
reading the suite's pattern with the wrong one would decide tests by the driver's mistake.

What the driver checks: `assert`, `assert-xml`, `assert-string-value`, `assert-serialization`,
`serialization-matches`, `error`, `assert-result-document`, and `all-of` / `any-of` / `not` over any of them.

What it skips, and why:

- **Every assertion about a result as a typed sequence** — `assert-type`, `assert-eq`, `assert-count`,
  `assert-empty`, `assert-deep-eq`. A transformation here writes a document rather than handing back a value,
  so the sequence those ask about is gone by the time the driver can see anything.
- **`assert-message` and `assert-warning`.** Messages arrive through `XsltOptions.MessageWriter` as text, one
  line each, which is not enough to put an assertion to a message *as a document*.
- **A result that is not well-formed XML** cannot carry an XPath assertion. Serialization is the only way out
  of a transformation, so the result is parsed again to be asked about — as a fragment, so several top-level
  nodes are fine and text alone is fine, but HTML deliberately is not.

`assert-xml` compares two canonical forms rather than two strings. Attribute order, namespace declaration
order, the empty-element form and the choice of which characters to escape are all free to a serializer, so a
comparison that saw them would decide tests on nothing. **Whitespace is not free and is kept exactly**: a test
whose stylesheet asks for indentation has an expected result with the indentation in it. When two results do
differ, the driver reports the position of the first difference with the whitespace made visible, because two
documents differing in one space read as identical when both are simply printed.

**Outside the document element it is not content at all.** XML allows whitespace on either side of the
outermost element and nothing in a result tree corresponds to it — not the newline a `.out` file ends with,
nor the one a catalog's CDATA section picks up from being typed on its own line. Comparing that decided
16 tests on how their expectation had been written down; it is dropped, and only where there is an outermost
element to be outside of, so a result that is a bare sequence of text keeps every character.

XSLT 3.0 has three ways in — a source document, a named template, a named function — and the engine offers
the first two, so the driver uses them. A test starting at a named **function** is still skipped rather than
approximated by one of the others, which would answer a different question.

**One transformation is given twenty seconds**, after which the run goes on without it and the test is skipped
with the wait named. The specification sets no time bound, so what ran out there is the driver's patience
rather than the engine's correctness — but without the limit the 3.0 run does not terminate at all.
`function-1031` is why: it asks for `x:fib(92)` declared `cache="yes"`, a request an implementation is free to
ignore, and ignoring it makes that call some ten quintillion invocations. The abandoned thread is not stopped,
there being no safe way to stop one from outside, so it runs until the process exits.

The base URI is a real `file:` URI rather than a path, served by a resolver of the driver's own instead of
`FileResolver`. The suite asks what `base-uri()` and `static-base-uri()` answer, and resolves `document('')`
against the stylesheet that wrote it; neither question has an answer in a path, one because it is not a URI
and the other because an empty reference relative to a directory is that directory.

An initial match selection — `<initial-mode select="…">`, or `<source select="…">` in the environment — is
handed to the engine as `XsltOptions.InitialMatchSelection`, an expression evaluated against the source
document where there is one, whose items templates are first applied to.

**Both ways in carry parameters.** XSLT 3.0 gives the caller the same two sets — ordinary and tunnel —
whether the transformation begins at a named template or by applying templates in a mode. The driver had
been reading the `param` children of an `initial-template` and not those of an `initial-mode`, so a mode's
were declared by the test and never supplied.

## Where the XSLT figure is going

The first thing this suite found was a stylesheet the engine could not survive: a key whose `use` expression
calls `key()` on the key being built recursed until the stack ran out, which cannot be caught, so the process
went with it. That is fixed — it raises `XTDE0640` — and it is why the run exists.

The second was the value model, half-changed: a `select` yielding `(a, b)` was refused for not being `a | b`.
Every instruction that works on a selection takes a sequence now — 49 tests.

The third was a second parser. A pattern's node test is the same production as a path's, and there was a
shortened copy of the path parser reading it — shortened by exactly the kind tests. The copy is gone, and a
pattern may now be anchored on a key as the grammar has allowed since XSLT 1.0 — 66 tests, and 77 pattern
failures down to 5.

The fourth was `xsl:value-of` with content: XSLT 2.0 lets a sequence constructor stand in for the `select`,
and it was required here — 14 tests.

The fifth was `method="xhtml"`, which was refused outright. Implementing it turned up three things that were
not about XHTML: the `xml` prefix was being declared, `cdata-section-elements` was resolving an unprefixed
name into no namespace where it names an element and takes the default one, and `include-content-type` was
not implemented for either HTML or XHTML — 45 tests between them.

The sixth was the `XTSE` static errors — a stylesheet saying something wrong and being answered with silence.
The specification's own table of elements is written out now, so an attribute the element does not have, a
value outside what it may take, and an element in a place it may not stand are all refused with the code the
specification gives them — 103 tests, and the cluster from 123 down to 40.

The seventh was what a declared type settles: a global's `as` was read and then dropped, a sequence could not
hold a parentless attribute, `xsl:document` contributed its children rather than the document node it makes,
and a type error carried XPath's code rather than the one XSLT gives each place an `as` may be written —
36 tests.

The eighth was the largest single fault the suite has found, and it hid behind two hundred different-looking
failures: **whitespace after the document element was becoming a text child of the document node**, so any
stylesheet transforming a file that ended in a newline put one in its result — 209 tests, and one in QT3.

The ninth was the second entry point. XSLT 3.0 lets a transformation start at a **named template** rather
than at a source document, and the engine's public API offered only the first — which held 1,320 tests out of
the run entirely. `Xslt.Transform()` runs a stylesheet with nothing to transform, and `InitialMode` names the
mode to start in. The run grew by a third and the rate went up.

The tenth was `xsl:number`, which had been here from the start with most of the specification's rules about
it missing: a format's punctuation is written even when there are no numbers to put in it, `value` takes a
sequence, and `from` says where numbering restarts rather than what to leave out — 36 tests.

The eleventh cost far more than the rule it broke. A pattern's innermost step is reached down the child axis,
which holds neither an attribute nor the document node, so `match="node()"` matches every node except those
two. It had been matching both, which let a stylesheet's catch-all rule take the document node away from the
built-in one: `apply-templates` never reached the document element, and the result was whatever the catch-all
wrote, with no outermost element at all. The failures it produced read as twenty different faults — 33 tests.

The twelfth was `xsl:result-document` with no `href`, which names the base output URI — the principal result
— and had been going to the resolver under an empty name, so a stylesheet whose whole body was wrapped in one
produced nothing. That, plus `xsl:output` being nameable and `format` naming one, was 59 tests.

The thirteenth was `use-when`, accepted and ignored, so every part of a stylesheet was compiled whether it
was meant for this processor or not — 41 tests. The fourteenth was `xpath-default-namespace`, likewise
accepted and ignored — 21 tests. The fifteenth was `xml:base`, not read at all — 25 tests.

The sixteenth was the name an instruction computes. A lexical QName means nothing on its own — its prefix
stands for whatever the declarations where it was written say it stands for — and `xsl:element` and
`xsl:attribute` were consulting none of them, so `<xsl:element name="p:foo"/>` wrote `<foo>` in no namespace.
With the names read, the eleven errors around them could be named too — 86 tests.

The seventeenth was serialization for a reader that is not an XML parser: `escape-uri-attributes`, the
Content-Type meta replacing one already in the head rather than joining it, the XHTML method being inferred,
and a character the declared encoding cannot carry becoming a reference on every destination rather than only
where this engine owns the bytes — 33 tests.

The eighteenth was backwards compatibility, of which this engine had half. A `version="1.0"` stylesheet on a
2.0 processor reads XPath **2.0** expressions with 1.0 *behaviour*; one flag was answering both questions, so
`1 to 5` and `()` and `current-date()` were refused in a stylesheet entitled to all of them. The behaviour
half is mostly the first-item rule, applied where the specification names it — 42 tests, twelve of them from
a built-in template rule passing on the parameters it was given.

The nineteenth was the static errors again, the table of allowed elements having grown a set of checks it had
no code for: two globals of one name, two parameters of one name on one call, an `xsl:number` with both a
value and a way of counting, a `use-attribute-sets` naming nothing, an `exclude-result-prefixes` written with
commas. Adding the first of them found that the element table had never been applied to the document element
at all, and that `xsl:transform` was missing from it — 64 tests.

The twentieth was the current template rule, which was being set by any template invocation rather than by
the three that make one: `xsl:apply-imports` inside an `xsl:for-each` was continuing whatever rule enclosed
it — 11 tests.

The twenty-first and twenty-second were the same cluster from its two ends. `misc/error` is one test per
error code — what it measures is not whether a stylesheet runs but whether being told *why* it will not says
anything useful — and the answer was often a sentence with no code attached, or the right complaint under the
wrong name. Twenty-two static ones: ten content models the element table could not state, twelve about names
and the declarations that give them meaning. Then the dynamic ones, most of which come back to a single
sentence — **inside a stylesheet function there is no focus at all**, so `/`, `current()` and a two-argument
`key()` were each answering with the caller's — 49 tests, and the cluster from 53 to 4.

Three of those were worth more than a code. **The version had to be checked before anything else was**:
`version="2.0e3"` parsed as two thousand, which reads as a later XSLT, so the stylesheet went
forwards-compatible and *every other check turned off*. **A decimal format is the union of every declaration
of it**, and a disagreement between two is an error only if nothing of higher precedence overrides it — two
reasons the check cannot be made where the declaration is read, and the reason a format declared across two
elements had been losing everything the first one said. And **`xsl:analyze-string` was building a stand-in
tree** for the substring a branch processes, there being no atomic context item when the instruction was
written; there is one now, and a path written in a branch no longer walks a tree that was never in the
stylesheet.

The largest clusters behind the current **5,600 of 5,623, 99.6%**, and no one cause dominates:

| | |
|---|---|
| `decl/output` | 5 |
| `decl/function` | 2 |
| `fn/collection` | 2 |
| `insn/result-document` | 2 |
| `misc/regex-syntax-xslt20` | 2 |

The two in `fn/collection` are the suite's: `collection-005` is a `version="2.0"` stylesheet that writes
`xsl:mode`, and `collection-006` is a package test, both marked as applying to a 2.0 processor, which
cannot run either. `misc/collations` 0128 is the same kind of thing, a `version="2.0"` stylesheet that
writes `xsl:evaluate`.

The largest skip left is not a failure either: **6,518 are XSLT 3.0 tests**, read only under `--xslt --30`.

## What the 3.0 run says

That opt-in run measures the XSLT 3.0 half at **7,906 of 7,924, 99.8%**, from 4,994 of 6,427 when it was first
taken. It reads more tests than it did as well as passing more of them, which is the part worth reading twice:
opening a feature the suite writes *around* stops whole files being skipped, so the denominator moves too — and
the percentage can fall while the work goes forward, which is why the two numbers are always given together.
Most of what 3.0 adds is still skipped for a further reason — 2,900 for streaming, 1,580 for a Unicode version
dependency, 672 for schema-awareness.

Two of those denominator moves were the driver rather than the engine, and both were the same mistake: a test
with neither a source document nor an entry point was being skipped as unrunnable.

For a test whose expected result is an **error**, that was wrong because a static error is raised by
*compiling* the stylesheet, and compiling it is exactly what can be done without either. Thirteen of the
`xsl:expose` tests are that shape, and 162 tests in all. Presenting them as compile-only cannot manufacture a
pass — compiling either raises the code the test names or it does not.

The larger one was a whole entry point. XSLT 3.0 lets a stylesheet say where to begin by declaring a template
called `xsl:initial-template`, needing no arrangement with whoever runs it — and **382 tests** were being set
aside for want of an entry point their own stylesheets had been declaring all along. Reading them cost 1.6
points of pass rate and is worth every one: 211 of them pass, and the 171 that do not are work this run could
not previously see.

That second change also found a crash. Running stylesheets that had never been compiled before reached an
expression nested deep enough to exhaust the stack while *parsing* it, and a stack overflow is not an error a
caller can catch — it took the whole run down with no result at all. The parser now refuses such an expression
with `XPST0003`.

A third, smaller one was the packages. A secondary package is usually declared with the name it answers to
and sometimes with only its file, the name being written inside it on the `xsl:package` element — thirteen
tests were failing as "no package resolver was configured" for want of reading it there. And a library may
declare `xsl:initial-template` and have it accepted into the principal, which is how the accept tests reach
an absent component; the driver now looks for the entry point in the secondary packages too.

Then the package model's versions and its package-local declarations, with `unparsed-entity-uri()` answering
nothing where nothing can exist: `decl/use-package` from 27 failures to 2 and `insn/where-populated`
from 26 to none. The driver now compares an `assert-xml` result with the whitespace between element children
left out, which is layout the expected results are written without. See *What a package keeps to itself* in
the compatibility document.

Then what an override replaces: its signature kept (`XTSE3070`), a variable or an attribute set overridden for
the used package as well as the using one, `xsl:original` resolved per function, and the codes around where a
package may be used from and how it starts. `decl/override` went from 21 failures to none and `decl/package`
from 14 to 3. The driver registers a secondary package under the name it gives itself as well as the
catalog's, and runs a test that expects an error rather than only compiling it. See *What an override
replaces* in the compatibility document.

Then the `json` and `adaptive` output methods for `xsl:output` and `xsl:result-document`, `parameter-document`,
an empty element written `<x/>` under the xml method, and `xsl:for-each-group` grouping any sequence of items by
keys compared as values: `decl/output` from 21 failures to 2 and `insn/for-each-group` from 20 to none. See
*What the json and adaptive methods write, and what a group holds* in the compatibility document.

Then where a result document goes and what a static variable is worth: a base output URI the caller may
give, which an `href` resolves against and `current-output-uri()` answers with; a result document's method
computed all the way to json; `xsl:analyze-string` counting its substrings; and static variables settled in
stylesheet tree order with imports in place, two declarations of one name agreeing or `XTSE3450`.
`insn/result-document` went from 18 failures to 1, `attr/static` from 16 to 2 and
`fn/current-output-uri` from 13 to 1. The driver reads the test's `output` element for the base output
URI. See *What a result document knows, and what a static variable is worth* in the compatibility document.

Then what a built tree is relative to and which mode is a way in: a temporary tree takes the base URI of
the declaration that built it and a parentless copy keeps its original's; only a public, final, default or
— in a stylesheet that declares no modes — rule-named mode is a way in, `XTDE0045` otherwise; `typed="yes"`,
`deep-skip` from a document node, `current()` over atomic values, and `mode="#current"` in a function all
corrected. `fn/base-uri` went from 16 failures to 1 and `attr/mode` from 14 to 2. The driver hands an
initial match selection to the engine as `XsltOptions.InitialMatchSelection`, which it used to skip. See
*What a built tree is relative to, and which mode is a way in* in the compatibility document.

Then what a key finds and what a copy is compared as: a key matching by value rather than by spelling,
several declarations of one name being one key, the third argument of `key()` naming a subtree, a computed
key name resolved where it is written; `fn:deep-equal` comparing nodes as nodes; `id()` answering for
`xml:id`; and the XML declaration ahead of a comment written before the output method is decided. `fn/key`
went from 14 failures to none and `insn/copy` from 12 to 10. See *What a key finds, and what a copy is
compared as* in the compatibility document.

Then what a date is written as and what a zero-length match is: a date component's second presentation
modifier, so that `[D1o]` is `23rd` and `[Dwo]` `twenty-third`; the conventional short name before a name is
cut to its width; `[Language: en]` and `[Calendar: AD]` in front of a fallback; `xsl:analyze-string` taking
one string, admitting a zero-length match from 3.0, keeping its branches in order, and `regex-group()` empty
in a function and in a pattern; a bare `}` refused in a regular expression. `fn/format-date-en` went from 11
failures to none and `insn/analyze-string` from 11 to none. See *What a date is written as, and what a
zero-length match is* in the compatibility document.

Then what a catch is told and what a map leaves alone: an error code keeping its namespace, `$err:module`
and `$err:line-number` reporting where the failing instruction stands, a try that is not temporary output
state, `rollback-output="no"` honoured with `XTDE3530` after output, a missing document as `FODC0002`,
`element-available()` for every 3.0 element; the driver handing a stylesheet over as bytes so that the
encoding it declares is read, and a character map applied before normalization, kept off an escaped URI
attribute, and reaching a string the adaptive method quotes. `insn/try` went from 11 failures to none and
`decl/character-map` from 10 to none. See *What a catch is told, and what a map leaves alone* in the
compatibility document.

Then what a namespace node is and what a bare dot is worth: the namespace axis, which had been a chosen
omission, built as a third id space beside the attributes and made on first use; `namespace-node()` in
paths, patterns and types; a bare `.` pattern at the priority of −1 the specification gives it, where it
had been outranking every kind test; a second attribute of one name replacing the first in a built tree;
and `exclude-result-prefixes` designating namespaces rather than prefixes. `fn/snapshot` went from 18
failures to none. See *What a namespace node is, and what a bare dot is worth* in the compatibility
document.

Then what a date's year and timezone are written as: the year reduced to the width its marker has room for,
a roman year written whole and padded with spaces, the timezone in each shape the specification's table
gives it and the width form the suite reads from XSLT 2.0's erratum, and a processor claiming 2.0 rounding
the fractional seconds where 3.0 cuts them. `fn/format-date` went from 8 failures to none on the 3.0 run
and from 10 to none on the 2.0 one. See *What a date's year and timezone are written as* in the
compatibility document.

Then what a package's version is and what a shadow attribute says: the principal package's own version
checked by the grammar, with XML's fifth-edition name characters in its name part; a shadow attribute
ignoring the plain attribute beside it, `_version` readable without asking itself, and `doc('')` in a
static expression answered with the module; and `xsl:product-version` as the assembly's version.
`attr/package-version` went from 10 failures to 1, the one left waiting on a 3.0 claim that has since
been made. See *What a package's version is, and what a shadow attribute says* in the compatibility
document.

Then what a 2.0 regular expression reads by: XSD 1.0's grammar for a 2.0 processor, where a hyphen in a
class stands for itself and the name escapes follow XML's fourth edition; the fifth edition's name
characters complete at 3.0, where they had stopped at U+200D; and the private-use block reaching its two
supplementary planes. `misc/regex-syntax-xslt20` went from 10 failures to 2, the two left wanting Unicode as
it was in 2001. See *What a 2.0 regular expression reads by* in the compatibility document.

Then what a document type declaration says: the declaration read for its entities, defaults, ID and IDREF
attributes and unparsed entities, with nothing outside the document fetched unless the caller sets
`XsltOptions.EntityResolver`; `id()` over what it types, `idref()`, `unparsed-entity-uri()` answering, a
pattern anchored on `id()`, element content whitespace excluded, and a bare-name fragment followed. The
driver no longer skips the `dtd` feature: `fn/id` went from 5 failures and 35 skipped to 1, and
`fn/unparsed-entity-uri` from 12 skipped to none failing. See *What a document type declaration says* in
the compatibility document.

Then what midnight is and what a duration is written as: `24:00:00` in an `xs:time` coming back to hour zero
where it stands rather than rolling into a day the type has not got; a duration of no length written `PT0S`
unless it counts in months; a duration divided rather than multiplied by the reciprocal, and the scale the
arithmetic arrived at not written; a moment moved by ticks rather than through `AddSeconds`, which takes a
double; and the am/pm marker spelled to the width asked for rather than cut to it. `type/date` went from 7
failures to none on both runs. See *What midnight is, and what a duration is written as* in the
compatibility document.

Then what namespaces an element has: no prefix bound that the stylesheet did not declare, so `fn:` and `xs:`
are `XPST0081` until they are; `*:name` and `Q{uri}name` read as the name tests they are in an
`xsl:strip-space`, with `xpath-default-namespace` deciding what an unprefixed one means; the default
namespace undeclared on an element that has no namespace node for it, which is what
`inherit-namespaces="no"` and a copied undeclaration both come to; and an element giving up its own prefix
to a namespace node that claims it rather than the stylesheet being refused. `type/namespace` went from 7
failures to none on the 3.0 run and from 6 to 1, `decl/strip-space` from 6 to 1 and from 4 to 2. See *What
namespaces an element has, and what a name test may leave out* in the compatibility document.

Then what a union in a match is: `match="p|element(p)"` declaring two template rules with one body, each
with the default priority of its own alternative, so an `xsl:next-match` goes from the one to the next; a
priority written on the template making them one rule again; the current template rule recorded as a pattern
rather than as a template, which is what had made a next match either skip an alternative or loop on one;
kind tests scoring by how much they say, so `element(x)` outranks `node()`; and a match pattern read once
the whole stylesheet is in, so it may call a function declared below it. `insn/next-match` went from 7
failures to none on the 3.0 run and from 6 to none on the 2.0 one. See *What a union in a match is, and what
a next match carries on from* in the compatibility document.

Then what json-to-xml was told to do: a repeated key retained, taken once or rejected as the `duplicates`
option says, with the answers the other function takes refused rather than guessed at; and a character XML
cannot hold either kept in the JSON escape it was written with, the element saying so, or handed to the
`fallback` function, which by default answers with the replacement character. `fn/json-to-xml` went from 9
failures to 1, and three more of its tests run at all. See *What json-to-xml was told to do* in the
compatibility document.

Then what a stylesheet says it will be run against: one `xsl:global-context-item` per package and no more,
with a code for a module that carries two and for two modules that disagree; a library package's declaration
passed over unless it asks for a context item, which it is in no position to do; a source document of the
wrong shape reported as the caller's mistake rather than the stylesheet's; and a global variable seeing what
the declaration said it would see, which had reached only the templates. `decl/global-context-item` went
from 7 failures to none. See *What a stylesheet says it will be run against* in the compatibility document.

Then what a function declaration may say: a parameter that may state it is required, from 3.0 and not
before; `override` and `override-extension-function` written together where they agree; `element-available()`
reading a bare name as an element name and so in the default namespace; and a cached function keying a QName
by the name it is rather than by the way it was written. With them, an empty `xsl:text` became a zero-length
text node at every version rather than only at 3.0 — an item in a sequence though nothing in a tree — which
reached three other sets. `decl/function` went from 7 failures to 2 and from 5 to 2. See *What a function
declaration may say, and what an empty text is* in the compatibility document.

Then what numbering anywhere counts: `level="any"` taking the current node together with its preceding and
ancestor axes, which is a scan over a range of ids and holds no attribute but the one being numbered — where
numbering an attribute had counted the whole document; and a `from` pattern found on the way rather than by
a scan backwards, which is what lets it work for an attribute at all. With them, `xsl:on-completion` lost the
focus it should never have had. `insn/number` went from 6 failures to 2 and from 5 to 2, `insn/iterate` from
6 to 3. See *What numbering anywhere counts, and what an iteration leaves behind* in the compatibility
document.

Then what a type annotation says where nothing was validated: an element test naming `xs:untyped` or a type
it derives from matching an element, and an attribute test naming `xs:untypedAtomic` or one of its
ancestors matching an attribute, with anything else matching nothing; a cast to `xs:QName` resolving its
prefix against the namespaces the cast was written among, which XPath 3.0 allows for a computed string and
2.0 did not; and `fn:resolve-QName()` of something that is not a lexical QName reported as the argument it
is rather than as a failed cast. `type/type` went from 6 failures to 1 and from 5 to 1. See *What a type
annotation says where nothing was validated* in the compatibility document.

Then what a comment in a stylesheet does to the text around it, which is nothing: the comments and
processing instructions come out before the whitespace-only text is stripped, so what stood on either side
of one is a single text node by then and whitespace beside something that is not whitespace stays with it.
That one line settled tests in `misc/whitespace`, `type/type`, `fn/id`, `expr/axes` and `misc/seqtor`. See
*What a comment in a stylesheet does to the text around it* in the compatibility document.

Then `fn:transform()`, the last whole function of 3.0 this engine lacked: a second transformation run from
inside an expression, handing back a map with its principal result under `output` and each secondary one
under the URI its `href` resolved to. `delivery-format` decides whether a document is a tree, a string or
the raw sequence the transformation returned — and one run this way writes nowhere, its result documents
being collected as values rather than resolved to somewhere outside. `fn/transform` went from 9 failures to
none. See *What a transformation run from inside one hands back* in the compatibility document.

Then what one function's type is against another: a function item carries the types it was declared with,
and `instance of function(…) as …` is the subtype judgement over them — contravariant in the arguments and
covariant in the result — rather than a count. A function item handed to a function type it does not
already satisfy is coerced rather than refused, wrapped in one that converts each argument on the way in
and the result on the way out, so the check falls where the item is called. With them: a partial
application has no name, a global variable is not in scope within its own declaration, atomizing a function
item is `FOTY0013`, and a named function reference may ask for as many arguments as `fn:concat` takes.
`expr/higher-order-functions` went from 9 failures to none. See *What one function's type is, against
another* in the compatibility document.

Then what a static expression may see, and where one is looked for. A `use-when` and a shadow attribute are
answered before anything runs, so the functions that read the source document or a running transformation
are out of reach there — and `function-available()` written inside one has to say so, which is the one
place where the answer depends on where the question was written. Under 2.0 no document is available to
such an expression at all. And every `use-when` in a module is now answered rather than only the ones the
compiler walks past: one inside an `xsl:fallback` that will never run still has to be an expression.
`attr/use-when` went from 6 failures to 1 on the 2.0 run and from 4 to 1 on the 3.0 one. See *What a static
expression may see, and where one is looked for* in the compatibility document.

Then what a double really is when it is rounded. A double is a binary fraction and almost never the decimal
it was written as, so `250.025` is really a shade above the half and `150.015` a shade below it — and
scaling by a power of ten to round loses exactly that, landing both on a tie that was never in the value.
Rounding now compares against the half on integers, which lose nothing. With it, `fn:number()` became the
cast to `xs:double` it is from 2.0 on, so `INF`, `-INF` and `NaN` are the numbers they name rather than
words. `expr/math` went from 6 failures to 2 and from 3 to none. See *What a double really is when it is
rounded* in the compatibility document.

Then `fn:stream-available()`, the one streaming function this engine answers. The question it asks is about
the document rather than about the processor — whether it is there and begins as XML — so reading stops at
the first element, and a document that is well formed for its first mile is available however it ends.
`fn/stream-available` went from 6 failures to none. Streaming itself stays the one omission this engine
chose. See *What being available as a stream means to an engine that does not stream* in the compatibility
document.

Then what an attribute is called and what a sequence of nodes is not. An attribute's prefix is a spelling
of its namespace and nothing more, so where the prefix is spoken for on the element the attribute takes one
of its own; HTML's boolean attributes are written as their name alone; a sequence of nodes has no document
node above it, so a leading `/` inside one is an error rather than an empty answer; and `xs:untypedAtomic`
is a type of its own rather than a kind of string, while a comment and a processing instruction atomize to
`xs:string`. `insn/attribute` went from 4 failures to none and `insn/sequence` from 4 to 1. See *What an
attribute is called, and what a sequence of nodes is not* in the compatibility document.

Then what `doc()` is asked and what it answers against: no URI names no document, so `doc(())` is the empty
sequence rather than the module the call was written in; and a relative reference resolves against the base
URI where the call stands, which an `xml:base` may have moved. `fn/document` went from 5 failures to 2 and
from 3 to none. See *What `doc()` is asked, and what it answers against* in the compatibility document.

Then what backwards compatibility converts. It is XSLT 2.0's imitation of XPath 1.0 rather than 1.0 itself,
and where 2.0 checks a type and refuses what does not reach it, 1.0 converts until it fits and cannot fail:
a sequence where one item was expected becomes its first item, a value where a string or a number was
expected is converted to one, and a general comparison with a number on either side compares numerically.
`misc/backwards` went from 4 failures to 1 and from 3 to 1. See *What backwards compatibility converts* in
the compatibility document.

Then what may be said once, and what may not be said at all: four conditions the specifications name and
this engine had let pass. `xsl:param` has no `visibility` attribute, what a component is visible as being
said by an `xsl:expose`; an `xsl:accept` may not name a component the same `xsl:use-package` overrides, the
two contradicting each other about one name; an invocation names a template or a mode and not both, which
is 2.0's error and one of the few conditions decided by the processor's version rather than the
stylesheet's claim; and one transformation does not both read and write a document, in either order.
`misc/error` went from 5 failures to 1 and from 1 to none. See *What may be said once, and what may not be
said at all* in the compatibility document.

Then what a message is, and what a template says it stands on. A message is a document node built by the
rules everything else built from a sequence constructor is built by, its `select` is the first thing it
constructs rather than an alternative to its content, and the content travels with a terminating message as
`$err:value` — as does the third argument of `fn:error()`. A message that cannot be produced does not stop a
3.0 transformation, `terminate` takes the spellings the processor reads rather than the ones the stylesheet
claims, and an `error-code` that is no name leaves the message `XTMM9000`. An `xsl:context-item` declares an
item type, once, before the parameters; and the whitespace before the declarations an element reads for
itself is not part of its sequence constructor. `insn/message` went from 5 failures to none and
`decl/context-item` from 5 to none. See *What a message is, and what a template says it stands on* in the
compatibility document.

Then where an expression ends, and where a node does. A brace inside an XPath comment ends nothing, and
comments nest; the braces may hold no expression at all, which XSLT 3.0 gives the empty sequence; and a run
of text with a comment in the middle is one text node by the time a text value template is found in it. A
comment keeps its hyphens apart and a processing instruction its question mark from its bracket, the
processor inserting the space XSLT says it does rather than refusing what was written. And a collation
written as an attribute value template is read where it is evaluated. `attr/avt` went from 4 failures to
none and from 2 to none, `attr/expand-text` from 5 to none and `insn/construct-node` from 3 to none. See
*Where an expression ends, and where a node does* in the compatibility document.

Then what an array is to a template, and what a map takes as one key. An array is a container, so the
built-in template rule for one applies templates to its members whatever the mode says about no match; a
date or a time keys by the moment it names rather than by the way it was written, while one with no
timezone stays a key of its own; the key of an `xsl:map-entry` is one atomic value by the ordinary
conversion rules, so an empty sequence there is `XPTY0004`; and the three namespaces XSLT 3.0 added to the
reserved list are reserved here too. `type/maps` went from 3 failures to none and `type/arrays` from 3 to
1, both on the 3.0 run alone. See *What an array is to a template, and what a map takes as one key* in the
compatibility document.

Then what a comparison was written among. A `default-collation` is what every comparison in its scope
compares strings by, and it had been read, checked and then dropped: the comparison operators, the string
functions that take a collation, `fn:default-collation()` and an `xsl:sort` that names neither a collation
nor a language all now take the one in scope. The namespaces in scope are what an untyped operand is read
as a name against, so the same comparison answers differently under different bindings of one prefix. And
two regular-expression rules came with them: a `$N` naming more groups than the pattern has contributes
nothing, and the regex group set is no part of what a function item carries. `insn/choose` went from 3
failures to none and from 2 to none, `misc/regex` from 3 to none and from 1 to none. See *What a comparison
was written among* in the compatibility document.

Then what the caller may hand the way in. A transformation started at a named template is a call, and
XsltOptions now carries the arguments — template parameters and tunnel ones, named as the stylesheet
parameters beside them are. XSLT 2.0 had no way to supply them, so a 2.0 processor passes them over and a
required parameter on the way in is the error 2.0 names for that, under the code the processor's version
gives it. The driver reads the `param` children of an `initial-template` and stops presenting a result a
test asked for as a value. `misc/initial-template` went from 3 failures to none. See *What the caller may
hand the way in* in the compatibility document.

Then what a cast may name, and what a next iteration may supply. The three list types a processor without a
schema has — `xs:NMTOKENS`, `xs:IDREFS` and `xs:ENTITIES` — are cast targets, the text split on whitespace
and every token cast to the item type; an `xsl:next-iteration` supplies each parameter once, where two of a
name had been letting the second silently win; and the `as` on one of its `xsl:with-param` elements
converts the value it supplies, where only the parameter's own type had been read. `expr/castable` went
from 3 failures to none and `insn/iterate` from 3 to 1. See *What a cast may name, and what a next
iteration may supply* in the compatibility document.

Then what a source document may be a fragment of: `xsl:source-document` follows a fragment identifier as
`document()` already did, so a bare name selects the element with that ID and the body processes it rather
than the document node. `misc/docbook` went from 3 failures to 2. The two left, and the three of
`decl/package` beside them, are not rules waiting to be implemented — an extension element, an element
count with no document to diff it against, and three tests whose own data or expectations the rest of the
suite contradicts. See *What a source document may be a fragment of* in the compatibility document.

Then what language a number is spelled in: German is the second language this engine spells numbers in, so
`lang="de"` on `xsl:number` and `'de'` to `fn:format-integer` reach a German speller instead of being read
for their errors and dropped — units before tens in one word, the long scale, `SS` for the upper case of
`ß`, and an ordinal built rather than patched, since 201 is `zweihunderteins` and the ordinal
`zweihunderterste`. The ending is carried by the request, because German inflects for what the ordinal
stands before: `ordinal="-er"` and `'Ww;o(-er)'` ask for the same word. The driver now answers the
`languages_for_numbering` and `ordinal_scheme_name` dependencies rather than skipping them, which brought
four tests into each run. `insn/number` went from 2 failures to 1 on each. See *What language a number is
spelled in* in the compatibility document.

Then what backwards compatibility restores and what it does not: an element whose `version` is below 2.0
enables a mode whose results the specification defines by the 2.0 documents and not by reference to the 1.0
ones, so `string(1 div 0)` is `INF` there as it is anywhere else and a large double is written with an
exponent. What the mode does restore is the rules, and one had been missing: the operand of a unary minus
becomes an `xs:double`, so `-0` is a negative zero and `1 div -0` is `-INF`. `expr/xpath-compat` went from 2
failures to none on both runs. See *What backwards compatibility restores, and what it does not* in the
compatibility document.

Then what a declared type admits and what it converts: a type derived from the one declared satisfies it
and keeps the type it had, so an `xs:dayTimeDuration` goes through an `as="xs:duration"` untouched; an
`xs:anyURI` declared as an `xs:string` is promoted, which converts rather than admits, so what comes out is
no longer a URI; and `xs:anyAtomicType` atomizes the node it is given, atomization being the first of the
rules and not one that depends on knowing which type is wanted. `attr/as` went from 3 failures to none on
both runs. See *What a declared type admits, and what it converts* in the compatibility document.

Then what a document URI is a property of: `fn:document-uri()` answers for a document node and for nothing
else, an element being in a document rather than being one; its no-argument form is XPath 3.0's, where
`fn:base-uri()` has had one since 2.0, so a 2.0 processor asked for `document-uri()` raises `XPST0017`; and
the source document is in the document pool under its own URI, so `doc(document-uri(.)) is .` is true
rather than answering with a second tree over the same file. `fn/accessor` went from 2 failures to none on
the 3.0 run and from 3 to none on the 2.0 one. See *What a document URI is a property of* in the
compatibility document.

Then which validation a processor without a schema refuses: `XTSE1660` names a set and the two languages
name different sets — 2.0 refuses everything but `strip`, 3.0 refuses only `strict`, having noticed that
`preserve` and `lax` ask for nothing a processor without a schema cannot give. The line now moves with the
version this engine says it implements, which is the one the driver asks for. `attr/validation` went
from 1 failure to none on the 3.0 run and from 3 to none on the 2.0 one. See *Which validation a processor
without a schema refuses* in the compatibility document.

Then which type names a processor has in scope: `type-available()` asks which types are in scope and not
which ones a value can be built as, and the two come apart at both ends — `xs:anyType` is in scope and
constructs nothing, and at 2.0 `xs:int` constructs something and is not in scope. XSLT 2.0 gives a
processor without a schema a short list and keeps the rest for a schema-aware one; 3.0 gives every
processor the whole of XML Schema Part 2. `fn/type-available` went from 2 failures to none on both runs.
See *Which type names a processor has in scope* in the compatibility document.

Then what a file read as text may hold: text read by `fn:unparsed-text()` has to be characters XML permits,
and a file with a NUL in it is not — `FOUT1190` where it happened, rather than a value that travels to the
end of the transformation and makes the serializer produce something not well-formed. `unparsed-text-lines()`
raises the same error, being built on the same read, and both `-available` forms answer false.
`fn/unparsed-text-lines` went from 2 failures to none on the 3.0 run. See *What a file read as text may
hold* in the compatibility document.

Then what a `function-lookup()` written in a package can find: the functions that package can see, which is
not every function the compilation declared — a library's private function does not exist from outside it,
and the using package's functions do not exist from inside it. Where a package overrides another's
function, what the used package sees under that name is still its own declaration, an override replacing
the component for whoever uses the package rather than for the package itself. `fn/function-lookup` went
from 2 failures to none on the 3.0 run. See *What a function-lookup written in a package can find* in the
compatibility document.

Then what an extension instruction falls back to: an empty `xsl:fallback` is still a fallback, saying to do
nothing rather than saying nothing; reaching an unimplemented extension instruction with no fallback is
`XTDE1450`, which it had been reporting without a code; and a reserved namespace cannot be an extension
namespace, which is `XTSE0085` and static. `fn/extension-functions` went from 1 failure to none on the 3.0
run and `misc/xslt-compat` from 1 to none on both, with three more tests out of the skips on each. See
*What an extension instruction falls back to* in the compatibility document.

Then which whitespace a package strips from what it reads: stripping is local to a package, so a library
that says nothing preserves everything however much the package using it strips. Each package declares into
its own control now, and the pool a document is remembered in is keyed by the stripping it was read under —
so a package that strips something reads its own copy, and every package that strips nothing shares one.
`fn/document` went from 2 failures to none on the 3.0 run. See *Which whitespace a package strips from what
it reads* in the compatibility document.

Then where a variable's scope ends: a variable is in scope for the following siblings of its declaration
and their descendants and for nothing else, so one written inside a literal result element shadows nothing
once that element closes. The compiler had been pushing every local binding onto one stack and never taking
it off; it now marks the stack when a sequence constructor starts and truncates it when the constructor
ends. `decl/param` went from 2 failures to 1 on both runs and `decl/variable` from 1 to none. See *Where a
variable's scope ends* in the compatibility document.

Then what may stand in for an `xsl:value-of`'s select: XSLT 1.0 gave the instruction an empty content
model, 2.0 made its content a sequence constructor and 3.0 let it say neither — and which of those applies
follows the processor rather than the version the element writes. So a `version="1.0"` element still has
its content read, and an empty `xsl:value-of` is an error at 2.0 and an empty result at 3.0. `attr/select`
and `attr/version` are both empty now. See *What may stand in for an xsl:value-of's select* in the
compatibility document.

Then what a picture may carry an exponent by, and what a URI reference may not: `xsl:decimal-format` takes
the `exponent-separator` XPath 3.1 added, and which picture language `format-number` reads follows the
processor rather than the version the stylesheet claims — so a `version="2.0"` stylesheet on a 3.0
processor may write `0.0000E0`. And a URI reference carries at most one fragment, `#` being left out of
what may follow one, so `resolve-uri('##some.uri', …)` raises `FORG0002`. `fn/resolve-uri` and
`fn/format-number` are both empty on the 3.0 run. See *What a picture may carry an exponent by, and what a
URI reference may not* in the compatibility document.

Then what is read of a declaration from a later version: an element in the XSLT namespace standing among the
declarations, which this version does not allow to stand there, is ignored together with its content — and
that is settled before its `use-when` is answered, which had been the one part of an ignored declaration
still able to refuse the stylesheet. A stylesheet written for XSLT 4.0 writes that condition in XPath 4.0.
`misc/forwards` is empty on the 3.0 run. See *What is read of a declaration from a later version* in the
compatibility document.

Then what a processor asked to be 3.0 says it is: `system-property('xsl:version')` reports the version the
processor implements rather than a constant, so a run opted into 3.0 answers `3.0`. What the stylesheet
claims of itself decides only how that answer is read — a number under 1.0's rules and a string from 2.0.
Three tests turn on it and none is about the property: one asks through a text value template, one computes
`static="yes"` from it and then reads the static variable that makes, and one computes an empty
`package-version` from it. `fn/system-property`, `attr/shadow` and `attr/package-version` are all empty on
the 3.0 run. See *What a processor asked to be 3.0 says it is* in the compatibility document.

Then which functions this engine says it has: `function-available()` was never asking two of the libraries
this engine builds calls from — the node-building functions and the JSON ones — and seven functions XSLT 3.0
added were missing from the table of the ones the compiler builds by hand. Each library now answers from the
table its own calls come from, and whether a later language's function is available follows the processor as
well as the version the stylesheet claims. With that, `fn:uri-collection()`, which at the time found nothing
as `fn:collection()` did (both now read `XsltOptions.CollectionResolver`, and the driver serves the
collections a catalog environment declares), and constructor functions for the three list types,
`xs:NMTOKENS('a b c')` being the cast to one under its other spelling. `decl/function` is empty on the 3.0
run. See *Which functions this engine says it has* in the compatibility document.

Then which collation a target expression compares by: the default collation of an `xsl:evaluate` target is
the one in scope where the instruction stands, which was the one line of that expression's static context
this engine was not honouring — the namespaces, the base URI and the default element namespace all came
from the instruction and the collation was always the code point one, so `'XYZ' eq 'xyz'` written down and
evaluated disagreed under a case-blind collation. `insn/evaluate` is empty on the 3.0 run. See *Which
collation a target expression compares by* in the compatibility document.

And a mode is a way in that takes parameters: the driver now reads the `param` children of an
`initial-mode` as well as an `initial-template`'s, tunnel ones among them, which the engine already passed
down that path. `misc/initial-mode` is empty on the 3.0 run.

Then `insn/copy` went from 28 failures to 12: a tree an instruction builds completes its own namespaces
when a start tag closes, `inherit-namespaces="no"` is honoured, a copied document node is one item of a
sequence, `xsl:where-populated` leaves out each item deemed empty rather than all or nothing, and the XML
declaration comes before a leading comment. See *What a copy carries* in the compatibility document.

Then `attr/match`, the set for the `match` attribute, went from 46 failures to 4: a positional
predicate counting within what the predicates before it left, an error inside a pattern meaning a non-match
as §5.5.4 says, the axis a step is written on deciding what it reaches, a selection pattern evaluated from
the candidate's own root with the first step adjusted for a parentless top. Most of it was 2.0 behaviour
the 2.0 run had been failing on too — its own `attr/match` went from 19 to none. See *What a pattern
reaches, and what an error inside one means* in the compatibility document.

The most recent cluster was `xsl:evaluate`, the last whole instruction of 3.0 this engine lacked. Dynamic
evaluation stopped being an absent feature, 42 tests came into the run, and 41 pass; the one left wants
`default-collation` read into comparisons, which nothing here does yet. The instruction builds a static
context of its own from what the specification lists — the parameters, the namespaces, the stylesheet's
public functions, and none of what XSLT adds to the function namespace — and hands the string to the parser
everything else went through; a name the stylesheet never wrote is given a slot then, the one place a
compiled stylesheet changes after compilation. See *What a target expression may see* in the compatibility
document.

| area | | |
|---|---|---|
| `attr` | 988 / 990 | 99.8% |
| `type` | 744 / 746 | 99.7% |
| `fn` | 1,108 / 1,111 | 99.7% |
| `expr` | 649 / 651 | 99.7% |
| `misc` | 1,797 / 1,804 | 99.6% |
| `insn` | 1,339 / 1,347 | 99.4% |
| `decl` | 955 / 969 | 98.6% |

Every instruction and declaration XSLT 3.0 adds is implemented, and so is **every attribute it hung on an
element that already existed** — `composite`, `select` on `xsl:copy`, `default-mode`, `new-each-time`,
`start-at`, `html-version` and the rest. There is no refusal of the form *"this element has no such
attribute"* left anywhere in the 3.0 run, where there were 121 at the point that became the largest single
category of failure.

Two test sets that looked like outstanding 3.0 work were not. `type/arrays` and `insn/where-populated` were
capped by **DTD processing**, which `unparsed-entity-uri()` needs, and `fn/snapshot` by the namespace axis —
the two omissions this engine had chosen, both since built.

It also found a fault in the 2.0 half that the 2.0 run cannot see, those tests being 3.0-only: **a stylesheet
function ignores import precedence**. Two functions of one name and arity are refused outright, where
`XTSE0770` applies only at the *same* precedence — so a module that imports another and redefines one of its
functions is refused, though that is exactly how a template, a variable and an attribute set are already
allowed to be overridden here.

Then collections. `fn:collection()` and `fn:uri-collection()` read `XsltOptions.CollectionResolver`, which
names what a collection holds, and the driver serves the collections a catalog environment declares — a
list of files under a name, the name relative to the test set — so the seventeen tests skipped as
*environment declares a collection* came into the 3.0 run, six in `fn/collection` and eleven in
`insn/merge`, and all seventeen pass. One of them wanted a `#` kept as a fragment identifier, which
`System.Uri` takes for part of a file name; the driver sets it aside and puts it back. Three of the merge
tests had never been seen and found **three gaps in `xsl:merge`**, none of them about collections: two
`xsl:merge-action` children were accepted, `stable` was accepted on an `xsl:merge-key` as if it were an
`xsl:sort`, and `xsl:fallback` was refused inside the instruction, where the content model ends in
`xsl:fallback*` — after the action, not before, which a fourth test asks. Still failing in that set is
`merge-097`, which asks for a collection by a directory-and-query URI the suite's own note calls
non-interoperable; the driver declares no such collection. On the 2.0 run four of the six collection tests
pass, the other two being 3.0 stylesheets marked as applying to a 2.0 processor.

Then the environment. `fn:environment-variable()` and `fn:available-environment-variables()` read the
process's environment where the caller has said they may, through `XsltOptions.EnvironmentVariablesEnabled`,
off by default; neither driver turns it on, and neither suite needs it to, every test being written to pass
whether the environment is visible or not. What the change did move was three QT3 tests: the argument is now
evaluated before the question of visibility arises, so `environment-variable(1)`, `environment-variable(())`
and `environment-variable(true())` are `XPTY0004` as the suite asks, where the old answer of nothing was
given before the argument was looked at. The one test left in that set builds a name a million characters
long with `1 to 1048576`; that was more items than this engine would lay a range out into until a range
stopped being laid out unless something walked it.

Then collations, in two steps. First, `xsl:for-each-group`, `xsl:key` and `xsl:merge-key` take any
collation the engine has, where they had refused everything but the code point one; grouping and keys
file a string under the collation's key, and a `default-collation` in scope reaches them where they name
none, as the specification has it. Second, **collations of the caller's own**: an `XsltCollation` supplied
under a URI through `XsltOptions.CollationResolver`, consulted for whatever the engine does not provide
itself, and reaching everything that names a collation. Both drivers now supply the case-blind collation
each suite declares under a URI of its own, and no environment is skipped for declaring a collation. That
brought 39 tests into the XSLT 3.0 run, 42 of the 43 in `misc/collations` among them, and 29 into the QT3
3.1 run, and all of them pass; on the 2.0 run 38 came in, with one left, a `version="2.0"` stylesheet that
writes `xsl:evaluate`. Two things were found on the way: a merge key's collation was being dropped when
the key's computed attributes were settled, so every merge key ordered by code point whatever it said; and
grouping and keys were ignoring a `default-collation` in scope, which three of the collation tests are
there to check.
