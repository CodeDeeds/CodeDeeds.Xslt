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

The version the engine claims, which is 2.0 for a caller who names none. `--31` opts the QT3 run into the
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

## The XPath run

The 2.0 run stands at **14,039 of 14,175, 99.0%**, and the 3.1 run — `--31`, which takes in the tests marked
`XP30+` and `XP31+` — at **17,374 of 17,573, 98.9%**. The second reads 3,398 more tests than the first and is
a tenth of a point behind it, which is the shape to expect: what it takes in is the newer half, and the
newer half is where the work is.

| area | 2.0 | 3.1 |
|---|---|---|
| `math` | *3.0 and later* | 130 / 130, 100% |
| `misc` | 31 / 31, 100% | 33 / 33, 100% |
| `app` | 330 / 330, 100% | 770 / 775, 99.4% |
| `prod` | 5,472 / 5,521, 99.1% | 5,889 / 5,952, 98.9% |
| `fn` | 4,988 / 5,021, 99.3% | 6,936 / 7,003, 99.0% |
| `op` | 3,147 / 3,195, 98.5% | 3,360 / 3,413, 98.4% |
| `xs` | 71 / 77, 92.2% | 115 / 121, 95.0% |
| `map` | *3.1* | 110 / 112, 98.2% |
| `array` | *3.1* | 31 / 34, 91.2% |

The failures cluster in few places. Casting is the largest on both runs — `prod/CastExpr`, `CastableExpr` and
`CastExpr.derived`, 46 between them — and most of that is dates before the common era, which
`System.DateTime` does not reach. Then `op/to`, almost all of it the 64-bit `xs:integer`; and, on the 3.1 run
only, `fn/parse-json`. The compatibility document keeps the account of what is behind each.

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

The largest clusters behind the current **99.4% of 5,319**, and no one cause dominates:

| | |
|---|---|
| `decl/output` | 5 |
| `misc/regex-syntax-xslt20` | 2 |
| `misc/docbook` | 2 |

The largest skip left is not a failure either: **6,518 are XSLT 3.0 tests**, read only under `--xslt --30`.

## What the 3.0 run says

That opt-in run measures the XSLT 3.0 half at **7,524 of 7,561, 99.5%**, from 4,994 of 6,427 when it was first
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
version this engine says it implements, which is 2.0 unless a caller asks for 3.0. `attr/validation` went
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
namespace, which is `XTSE0800` and static. `fn/extension-functions` went from 1 failure to none on the 3.0
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
well as the version the stylesheet claims. With that, `fn:uri-collection()`, which finds nothing as
`fn:collection()` does, and constructor functions for the three list types, `xs:NMTOKENS('a b c')` being the
cast to one under its other spelling. `decl/function` is empty on the 3.0 run. See *Which functions this
engine says it has* in the compatibility document.

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
| `fn` | 1,102 / 1,105 | 99.7% |
| `expr` | 649 / 651 | 99.7% |
| `misc` | 1,761 / 1,767 | 99.7% |
| `insn` | 1,325 / 1,333 | 99.4% |
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
