# Schema awareness: a plan

Status: phases 0 to 4 done, September 2026. The engine reads schemas, types its input, validates what it
constructs, and validates the result of `fn:json-to-xml()`, behind `XsltOptions.SchemaAware` and
`XsltOptions.InputValidation`. What is left is a scatter of edge cases and the `xs:NOTATION` corner, which
the type-code model cannot tell from `xs:QName` without more work than the few tests warrant. This is the
plan the work followed.

## Where the engine stands

What the code does today, which is what the plan starts from:

- **No node carries a type annotation.** `XdmTree` holds a node's kind, name, value and structure and
  nothing about its type; every element atomizes to `xs:untypedAtomic`, as if annotated `xs:untyped`.
- **Atomic values are typed by `XdmTypeCode`**, the primitive types plus `xs:integer` and
  `xs:untypedAtomic`, with `XdmType.DerivedType` naming a built-in derived type (`xs:int`, `xs:NCName`)
  a value was made as. There is no representation for a user-defined atomic type.
- **`xsl:import-schema` is `XTSE1650`**, deliberately: a stylesheet written against types would otherwise
  be run as though the import were absent and answer typed questions with untyped answers.
- **`validation` and `type` are `XTSE1660`** on every instruction that takes them, save that a 3.0
  processor accepts `strip`, `preserve` and `lax`, as a basic processor may.
- **`schema-element()` and `schema-attribute()` are `XPST0008`** at parse time, `element(N, T)` accepts
  only `xs:untyped` and `xs:anyType`, and `type-available()` answers for built-in types only.
- **`system-property('xsl:is-schema-aware')` is `no`.**

What the suites hold back because of it:

| Suite | Held back |
| --- | --- |
| XSLT 3.0, `--30` run | 686 tests skipped as *needs feature schema_aware*; 948 tests carry the feature across 33 test sets that declare a schema, the difference being streaming tests skipped for streaming first. |
| XSLT 2.0 run | The same tests, where they apply to 2.0. |
| QT3, `--31` run | 140 tests skipped as *environment declares a schema*; 429 tests depend on `schemaImport` and 56 on `schemaValidation`, mostly in `prod/CastExpr`, `CastableExpr` and `InstanceofExpr`. |

The XSLT tests by test set, largest first: `decl/import-schema` 205, `attr/match` 103, `attr/as` 92,
`attr/streamable` 75 (streaming, stays skipped), `attr/validation` 67, `misc/error` 38, `expr/nodetest` 35,
`attr/strip-type-annotations` 27, `type/notation` 23, `type/type` 19, `insn/copy` 16, `insn/evaluate` 14,
`insn/source-document` 12, `fn/json-to-xml` 9, and a tail of a few each. About 200 of the 948 are
streaming tests and stay out whatever this plan does.

## What a schema-aware processor must do

XSLT 3.0 §3.14 and §27 define the conformance level. Grouped by what they touch in this engine:

1. **Schema components in scope.** `xsl:import-schema` with `namespace`, `schema-location`, and an inline
   `xs:schema`; the in-scope schema components are the types, element declarations and attribute
   declarations of every imported schema, and the built-in types. A type a stylesheet names that is not
   in scope is `XTSE1520`.
2. **Typed values.** Nodes carry type annotations; atomizing a typed element or attribute yields its typed
   value, a list type yielding several values, an element with element-only content refusing
   (`FOTY0012`). `nilled()`, `data()`, `id()` over schema-typed IDs, `deep-equal()` over typed values.
3. **Sequence types.** `schema-element(E)`, `schema-attribute(A)`, `element(N, T)`, `attribute(N, T)`,
   `document-node(schema-element(E))`, user-defined atomic types in `as`, `instance of`, `treat as`,
   `cast as`, `castable as`, and in patterns; substitution groups under `schema-element`; derivation
   by restriction and extension; `xs:untyped` and `xs:anyType` as before.
4. **Constructor functions** for user-defined atomic types, with facets enforced.
5. **Validation of constructed nodes.** `validation="strict|lax|preserve|strip"` and `type` on
   `xsl:element`, `xsl:attribute`, `xsl:copy`, `xsl:copy-of`, `xsl:document`, `xsl:result-document`
   and literal result elements; `default-validation` on the stylesheet; the `XTTE151x`, `XTTE154x` and
   `XTTE155x` family of errors; `xsl:copy` and `xsl:copy-of` keeping or stripping annotations as told.
6. **Input documents.** Validated input carries annotations; `input-type-annotations="strip"` removes
   them; a processor may honour `xsi:schemaLocation` hints. The suite marks 100 source documents
   `validation="strict"` and 92 carry `xsi:schemaLocation`.
7. **Reporting.** `xsl:is-schema-aware` is `yes`; `type-available()` answers for schema types.

## Foundation: System.Xml.Schema

.NET ships a complete XSD 1.0 implementation, and the plan builds on it rather than beside it:

- `XmlSchemaSet` loads, includes, imports and compiles schemas, and exposes the compiled component model:
  `XmlSchemaType` with its base type, derivation method and content type, `XmlSchemaSimpleType` with its
  variety (atomic, list, union), facets and `Datatype` (whose `TypeCode` maps onto `XmlTypeCode`, which
  maps onto `XdmTypeCode`), `XmlSchemaElement` with its substitution group and `IsNillable`,
  `XmlSchemaAttribute`. `XmlSchemaType.IsDerivedFrom` answers derivation.
- A validating `XmlReader` (`ValidationType.Schema`) yields PSVI per node through `IXmlSchemaInfo`:
  the element or attribute declaration, the type, `MemberType` for a union, `IsNil`, `Validity`. This is
  how input documents get annotations, at the point where `XdmTreeBuilder` already reads a reader.
- `XmlSchemaValidator` is the push-model validator, fed start-element, attributes, text and end-element,
  with `ValidateElement`'s partial-validation parameter for validating against a named type or
  declaration. This is how a constructed tree is validated: build it, walk it, annotate it.
- `XmlSchemaDatatype.ParseValue` checks a lexical form against a simple type, facets included, which is
  what a constructor function or a cast to a user-defined atomic type needs.

What it does not do: **XSD 1.1**. No assertions, conditional type assignment, open content, or
`xs:error`. A spike on 12 September 2026 compiled every schema the XSLT suite references: 75 of 82
compile; six fail on `xs:assert` and one on a DTD in the schema document, which schema loading can permit
where document loading does not. The 17 tests marked `XSD_1.1` stay skipped. Nothing in the spike
suggests writing an XSD implementation of our own.

## Design

### Switching it on

`XsltOptions.SchemaAware`, default `false`, in the manner of `EnvironmentVariablesEnabled`. Off, the
engine is exactly what it is today, refusals included: a caller who never imports a schema pays nothing
and sees no change. On, `xsl:import-schema` is honoured, `validation="strict"` and `type` are accepted,
`xsl:is-schema-aware` is `yes`, and the rest of this design applies.

Two things arrive with the flag:

- `XsltOptions.SchemaResolver`, an `IXsltResolver` for `schema-location` and for schemas a document's
  `xsi:schemaLocation` names, separate from the document and stylesheet resolvers as those are from each
  other: reaching a schema is loading a definition of what is valid, which is a third kind of trust.
  `xsl:import-schema` with a `namespace` and no location asks the resolver for the namespace URI.
- `XsltOptions.Schemas`, an `XmlSchemaSet` the caller has already loaded, for a caller that holds its
  schemas in memory. Everything the stylesheet imports is added to a copy, so the caller's set is not
  changed under it.

### Schema components

A per-stylesheet `SchemaComponents` wraps the compiled `XmlSchemaSet`: what the in-scope components are,
looked up by expanded name. Around each `XmlSchemaType` it keeps an `XdmSchemaType` — the name, the base
type, whether it is atomic, list or union, the primitive `XdmTypeCode` it bottoms out in, the member
types of a union, the item type of a list — so that the rest of the engine reads a small immutable object
of its own rather than the `System.Xml.Schema` tree. The built-in types are `XdmSchemaType`s too, made
once, so that one derivation check serves `xs:int` and `my:age` alike.

Static checks at compile time, where the specification puts them: a type named in `as`, `type`,
`instance of`, a cast or a constructor call that is not in scope is `XTSE1520`; an `xsl:import-schema`
that cannot be loaded is `XTSE0165`; two imports of one namespace must agree (`XTSE0220`).

### Type annotations on nodes

`XdmTree` gains an optional annotation array per node and per attribute: an index into the tree's own
table of `XdmSchemaType`s, with 0 meaning untyped, plus a nilled flag. The arrays are `null` until a
tree is validated, so an untyped tree costs what it costs today. The tree builders that produce result
trees — for variables, `xsl:document`, `xsl:result-document`, `xsl:copy-of` — carry annotations across a
copy with `validation="preserve"` and drop them with `strip`.

`xs:untyped` is the annotation of every element in an unvalidated tree and `xs:untypedAtomic` of every
attribute, as the data model says. `input-type-annotations="strip"` makes a validated input look like an
unvalidated one, and `preserve` refuses a document that arrives without them.

### Typed atomic values

`XPathValue` keeps its shape — a kind, a type code, a number and a reference — and a user-defined atomic
type is carried the way a built-in derived type is now: beside the primitive the value is held as, read
by the type tests and by nothing else. `XdmType.DerivedType` becomes an `XdmSchemaType?` reference, so
that `my:age(30) instance of xs:integer` is true, `instance of my:age` is true, and `+ 1` sees an
integer. A list type atomizes to a sequence, each item annotated with the list's item type, and a union
to a value annotated with the member type that accepted it — which is what PSVI's `MemberType` says.

### Sequence types and patterns

The parser already reads `schema-element()` and `schema-attribute()` and refuses them; it will build a
`KindNodeTest` that asks the components for the declaration and matches a node whose annotation is the
declared type or derives from it, and, for `schema-element`, whose name is the declaration's or in its
substitution group. `element(N, T)` and `attribute(N, T)` match an annotation that is `T` or derived from
it; `element(N, T?)` admits a nilled node. The same tests serve patterns, so `match="schema-element(x)"`
and `match="element(*, my:type)"` follow. Atomic type names in a sequence type resolve through the
components, and `instance of`, `treat as`, `cast as` and `castable as` follow from that.

### Atomization and the functions

`XdmSequence.Atomize` reads the annotation: untyped as now; a simple type through the typed value, a list
type into several; a complex type with simple content through its simple type; element-only content is
`FOTY0012`; mixed content is `xs:untypedAtomic`. The typed value is computed from the string value on
demand through the `XdmSchemaType`, since keeping a parsed value per node would cost every tree for the
benefit of the few that are typed. `data()`, `nilled()`, `deep-equal()`, `id()` and `idref()` over
schema-typed ID attributes and elements, `distinct-values()`, grouping and keys all take typed values
through the paths they already have, since a typed value is an `XPathValue` like any other.

### Validation

Three ways a node gets an annotation, and one validator behind them:

1. **Input documents** through the validating reader, where the caller asks for it: a new
   `XdmTreeBuilder` entry point taking a mode (`strict`, `lax`, or the document's own
   `xsi:schemaLocation` hints) and the schemas, and `XsltOptions.InputValidation` for the principal
   input and for `document()`. Validity is reported as the specification's `XTTE1545`.
2. **Constructed nodes** through `XmlSchemaValidator`: an instruction with `validation="strict"` or
   `"lax"` or a `type` builds its result as a tree first, walks it into the validator, and annotates it
   from what the validator says. `type="T"` validates against `T` by partial validation; an attribute's
   `type` validates its value against a simple type. Strict validation of an element with no
   declaration is `XTTE1510`, lax finds a declaration or leaves the node untyped, an invalid value is
   `XTTE1510` or `XTTE1540`, a type not in scope is `XTSE1520`, and validation of a document node's
   children is `XTTE1545`.
3. **Copying**: `xsl:copy` and `xsl:copy-of` with `preserve` keep annotations and with `strip` drop
   them, per the specification's tables; literal result elements take `default-validation`.

`XmlSchemaValidator` wants a namespace resolver and the element's in-scope namespaces, which the tree
has; it wants text in the lexical form the tree holds; and it reports the declaration and type it
settled on, which is what the annotation records.

### Casting and constructors

A user-defined atomic type is a constructor function of the same name, resolved when the stylesheet is
compiled against the components in scope, and a cast target. Both go through the `XdmSchemaType`: the
lexical form is checked by `XmlSchemaDatatype.ParseValue`, which enforces facets, and the value is then
built as its primitive and annotated with the type. `castable as` is the same without the error.
Casting a typed value to a built-in type sees only the primitive, as the specification's casting table
has it.

### Backends

The compiled backend calls back into the interpreted expression for anything it does not emit, and
`XPathValue` keeps its shape, so nothing about typed values needs emitting. Annotation-aware node tests
are new `KindNodeTest` variants, which the emitted path already treats as opaque predicates. The
conformance driver runs both backends and must keep reporting the same set.

## Phase 0: what the spike found

Done on 12 September 2026, as a scratch console project run against the suite's
`attr/match/variousTypesSchemaMatch.xsd` and its documents. Every question the phase asked has an
answer, and none of them changes the design.

**A validated read gives PSVI per node, at the point the tree builder already reads.** A validating
`XmlReader` reports, on every element start and every attribute, the declaration (`SchemaElement`,
`SchemaAttribute`), the type, the member type a union settled on, and `IsNil`. An element's validity
is *not known* at its start and known at its end, so the builder annotates at the start and lets a
validation error abort or annotate untyped, per the mode. Over the sample document every element and
every declared attribute came back typed: `partNumberType`, `myListType` as a list, `partIntegerUnion`
with member `integer`, mixed and element-only complex types, complex simple content with its
`xs:decimal` datatype, and an empty complex type.

**Typed values are best made by the engine, not taken from .NET.** `XmlSchemaDatatype.ParseValue`
returns .NET values — `Uri`, `String`, `String[]` for a list, `Decimal` for `xs:integer` and for an
integer union member, `Int32` for `xs:int` — which is not the engine's representation and loses the
distinction between `xs:integer` and `xs:decimal`. The engine already parses every built-in lexical
form; so the annotation carries the type, and the typed value is the engine's own cast of the string
value by that type's primitive, with `ParseValue` used only where facets have to be enforced, which is
casting and constructor functions. A union's typed value is cast by the member type PSVI names.

**Push validation works for every case a constructed node needs.** `XmlSchemaValidator` validated an
in-memory tree against a global element declaration, against a named simple type, list type, union
type and complex type by partial validation, and against a global element declaration by partial
validation. It reported the declaration and type settled on, the union member, validity per node, and
typed values on end-element. Errors arrive through the event handler with the offending element and a
message; strict validation of an undeclared element, an invalid simple value, a facet violation and a
wrong child were each reported and each is distinguishable by what was being validated when it
happened, which is how the `XTTE15xx` codes will be chosen rather than by message text. One quirk:
`ValidateText` with an empty string on an `xsi:nil` element is an error, so an empty element must not
report text at all.

**`xsi:schemaLocation` hints work through `ProcessSchemaLocation`**, which makes the suite's 92
documents carrying them typed without the driver naming a schema; whether the engine honours hints by
default or only when asked is a setting, since following one reaches a file the document names.

**Cost.** Reading 200,000 elements with two typed attributes each took 27 ms plain and 143 ms
validating, 5.3 times the read; the tree build and the transformation dwarf the read, so the whole is
expected to move far less, and only for a caller who asks. Annotation arrays at one `int` per node
would add 0.8 MB for those elements and 1.5 MB for their attributes, and nothing for an unvalidated
tree.

**The compiled type model is enough.** `XmlSchemaType.IsDerivedFrom` answers derivation; list types
expose their item type and unions their member types; complex types with simple content expose the
datatype; facets are readable. `XdmSchemaType` wraps exactly that.

**Two limits to carry forward.** .NET validates `xs:date` and its relatives through `DateTime`, so a
value outside its range — the suite's `as-17.xml` has `-0012-12-03-05:00` — is reported invalid where
XSD allows it; the engine's own date parsing has no such limit, and phase 2 should decide whether to
validate built-in date lexical forms itself before handing the node to the validator. And XSD 1.1 is
out, as the schema-loading spike had already shown.

**Settled representation.** `XdmTree` gains three arrays, all `null` until a tree is validated: an
`int` per element and an `int` per attribute indexing a per-tree table of `XdmSchemaType`, index 0
being untyped, and a nilled flag per element. Types compare by expanded name for named types and by
reference for anonymous ones, and derivation walks the `XdmSchemaType` base chain, so a tree validated
by the caller against one schema set and a stylesheet that imported another agree about `xs:integer`
and about `my:age`.

## Phases

Each phase is shippable on its own and is measured by what it brings into the conformance runs.

**Phase 0, the spike.** Done; see above.

**Phase 1, components and static typing.** Done. `SchemaAware`, `SchemaResolver` and `Schemas` on the
options; `xsl:import-schema` by location, inline and by namespace, with `XTSE0165`, `XTSE0215` and
`XTSE0220`; `SchemaComponents` over `XmlSchemaSet` and `XdmSchemaType` over each compiled type, the
built-in types wrapped once and shared; `type-available()` and `xsl:is-schema-aware`; user-defined
atomic, list and union types in sequence types, `instance of`, `treat as`, casts, `castable as`,
constructor functions and the function conversion rules, with facets enforced by .NET's datatype; the
types in scope for `xsl:evaluate`, the initial match selection and a transformation `fn:transform()`
starts. A value of a user-defined atomic type carries a two-byte type number beside the built-in type it
is held as, in the struct's padding, so `XPathValue` did not grow. A kind test may name a schema type and
matches nothing until phase 2 annotates nodes. The conformance driver runs schema-aware under `--schema`,
loading an environment's schemas, so that the phases to come are measured against the suite; the
headline run is unchanged at 7,580 of 7,618. On landing, the schema-aware run brought 590 tests in, of
which 93 pass on the type system alone and 497 wait for phases 2 and 3: 179 in `decl/import-schema`
and 43 in `attr/validation` ask for `type` and `validation` on constructed nodes, and 81 in
`attr/match`, 50 in `attr/as` and 34 in `expr/nodetest` ask for annotated input.

**Phase 2, typed input.** Done. `XdmTree` carries a `ushort` type number per element and per attribute
and a set of nilled elements, all null until a tree is validated, the number being the one
`XdmSchemaType` already handed out for values, read without a lock. `XdmTreeBuilder` reads through
.NET's validating reader under a `TreeValidation` — the compiled set, strict or lax, how a type is
numbered, whether to record — annotating each element and attribute from `SchemaInfo` at its start
tag, dropping whitespace in element-only content as the data model asks, and reporting an invalid
document as `XTTE1510` or `XTTE1515` and an undeclared document element under strict validation as
`XTTE1512`. `XsltOptions.InputValidation` asks for it, for the input and everything `document()`,
`doc()` and `collection()` read, and is refused without `SchemaAware`. `XdmSchemaType` gained the
content kind and simple content of complex types, the typed value of a node by its type (a list to
several values, a union member by member, a nilled or empty element to nothing, element-only content
`FOTY0012`), the pure-union rule of `derives-from`, and `IsIdType`; `SchemaComponents` gained
top-level element and attribute declarations with their substitution groups, thread-safe wrapping and
the validating set. Atomization goes through one `TypedValueOf`, and the fifteen places that had
atomized a node by hand — comparisons, arithmetic, casts, map keys, function arguments, `xsl:value-of`'s
fast path — now ask it, each handling a typed value that is several values or none. `KindNodeTest`
matches by derivation for built-in and schema types alike, honours the `?` that admits a nilled node,
and matches `schema-element(E)` and `schema-attribute(A)` by declaration; an unprefixed name in an
element test takes the default element namespace, as the specification has it. `nilled()`, `id()`,
`element-with-id()`, `idref()` and the `element-with-id()` pattern follow schema-typed IDs;
`xsl:mode typed="yes|strict|lax"` and `typed="no"` are checked (`XTTE3100`, `XTTE3110`);
`input-type-annotations="strip"` validates and then reads untyped, keeping the ID properties, with
`XTSE0265` for modules that disagree. An `xsl:import-schema` naming a namespace nobody has a schema
for is no longer an error, and an inline schema whose target namespace contradicts the declaration is
`XTSE0215`. The schema-aware run went from 7,673 to 7,822 of 8,208 (95.3%), both backends agreeing;
`expr/nodetest` fell from 34 failures to none, `attr/match` from 81 to 27, `attr/as` from 50 to 33; 293
of the 386 left ask for validation of constructed nodes. The headline runs are unchanged.

**Phase 3, validation of constructed nodes.** Done. `validation="strict"`, `"lax"`, `"preserve"`,
`"strip"` and `type` on `xsl:element`, `xsl:attribute`, `xsl:copy`, `xsl:copy-of`, `xsl:document`,
`xsl:result-document` and literal result elements, and `default-validation` on any element, inherited.
An instruction that validates builds its result into a tree first, walks it into .NET's push validator
(`NodeValidator` over `XmlSchemaValidator`), and writes it out carrying what validation settled — an
element construction validated as an element without the document-level ID constraints, a document node
and a copy validated as a document with them, an attribute against its declaration or named type;
`preserve` keeps the annotations of what a copy copies and `strip` drops them, keeping the ID
properties. The `XTTE15xx` codes are chosen by what was being validated: `XTTE1510`/`XTTE1515` for an
invalid element under strict and lax, `XTTE1512` for an undeclared one, `XTTE1540` for a named type,
`XTTE1535` for a complex type on an attribute, `XTTE1545` for a QName-built attribute type, `XTTE1550`
for a malformed document node, `XTTE1555` for an ID or identity-constraint failure; `XTSE1505` for both
type and validation, `XTSE1520` for a type not in scope. The `TypeOverlay` keeps what validation
settled beside the tree, so the originals a variable or the input holds are not annotated by a copy.
The `xml` namespace's own schema is built in, for an `xsl:import-schema` that names it and no other.
On landing (14 September 2026), the schema-aware run reached **8,101 of 8,207, 98.7%** on both backends,
from 7,822 after phase 2; 293 tests that had refused validation now pass. The headline runs are
unchanged: XSLT 3.0 7,580 of 7,618, XSLT 2.0 5,329 of 5,364.

**Phase 4, the tail.** Done in part. `fn:json-to-xml($json, map{'validate':true()})` validates its
result against the schema for the XPath functions namespace, which is built in (as the `xml` namespace's
schema is), and annotates it, `FOJS0004` where no schema is in scope; `xsl:evaluate schema-aware="no"`,
the default, hides the imported types from the target and raises `XTDE3160` where it names one, told from
a genuinely unknown type by whether the target would compile with the schemas in scope. That took the
schema-aware run to **8,110 of 8,207, 98.8%**, both backends, headline unchanged. Left, and deferred:
`xs:NOTATION` (`type/notation`, 5), whose typed value is an `xs:QName` the `XdmTypeCode` model cannot
tell from a plain one without routing `NOTATION` through the schema-type path; a schema's `xs:include`
reached by relative location from an inline schema (`decl/import-schema` 185, 202); per-document input
validation (`attr/validation` 1201, 1203); and `xsl:mode typed="strict"`'s static pattern check
(`XTSE3105`, 1). About 97 tests remain, some of them unrelated to schema awareness — `expr/math`,
`decl/package`, `decl/strip-space` — that the `--schema` environments happen to surface.

Rough weight: phase 1 is the smallest and phase 2 the largest, since annotations touch the tree, the
builders, atomization and every node test; phase 3 is the validator and the seven constructors that
feed it.

## Out of scope

- **XSD 1.1**: `xs:assert`, conditional type assignment, open content. 17 tests, and the six schemas
  that use `xs:assert`. A processor claiming XSD 1.1 would need its own implementation, which this plan
  does not propose.
- **Streaming**, schema-aware or not: about 200 of the 948 tests.
- **Static typing** in the XQuery sense (`XPST0005` at compile time from type analysis): QT3's
  `staticTyping` feature, 35 tests, is a different conformance level.
- **`xsl:import-schema` over the network** except through a caller's `SchemaResolver`, as with every
  other reference.

## Risks and open questions

- **Cost on untyped trees.** Annotation arrays must stay `null` for trees nobody validates, and the
  atomization fast path must stay the fast path; the DocBook benchmarks are the check. Phase 2 put one
  `HasTypeAnnotations` read in front of each fast path and nothing else.
- **`XPathValue` shape.** Settled in phase 1: a two-byte type number in the struct's padding rather
  than a reference, so nothing grew. The number is handed out from a process-wide registry, which a
  node annotation reads without a lock; the registry holds every type that has annotated a value or a
  node for the life of the process, so a process compiling schema-aware stylesheets without end grows
  it without end, and 65,534 is the ceiling. A stylesheet's types are registered once per compile, not
  per document, so an ordinary process never approaches it.
- **Which documents are validated.** `InputValidation` is one setting for the whole transformation,
  where the suite declares validation per source: an environment validating its principal source
  strictly and reading an unvalidated secondary one through `doc()` is judged wrong by two tests
  (`attr/validation` 1201 and 1203). A per-document choice would need the document resolver to say so,
  which is a larger change than the two tests are worth for now.
- **`xsi:schemaLocation` hints** are not followed: following one would add to the compiled set every
  transformation over the stylesheet shares. A per-transformation copy of the set would allow it, at
  the cost of compiling the schemas again per transformation.
- **PSVI fidelity.** Union member types and list item types come through as the spike showed;
  `xs:NOTATION`, `xsi:type` and the date range limit above still need checking against what the tests
  expect.
- **Error codes.** The suite asserts the `XTTE15xx` codes individually, and `System.Xml.Schema` reports
  one `XmlSchemaValidationException`; mapping its message categories onto codes needs a table and
  tests of its own.
- **Where validation runs.** Validating a constructed tree after building it is simplest and matches
  how `xsl:try` already buffers content; validating while writing would be faster and is not proposed.
- **The DTD prohibition.** Schema documents may carry DTDs (the suite's `XMLSchema.xsd` does); loading
  a schema may permit a DTD where loading a document does not, under the same entity resolver rules.
- **Whether to validate at all by default.** `SchemaAware` on does not validate input by itself; a
  caller asks for that with `InputValidation`. This matches the specification, which leaves source
  validation to the caller, and keeps the flag cheap.

## Next steps

1. `xs:NOTATION`: route it through the schema-type path so a `NOTATION`-typed value is told from a plain
   `xs:QName`, which the `XdmTypeCode` model cannot do on its own. Five tests (`type/notation`).
2. The remaining validation edge cases: nested strict validation not catching what it should
   (`import-schema` 118-120), a schema's `xs:include` reached by relative location from an inline schema
   (`import-schema` 185, 202), per-document input validation (`attr/validation` 1201, 1203), and the
   `xsl:mode typed="strict"` static pattern check (`XTSE3105`).
3. Keep the DocBook benchmarks as the check that an untyped transformation has not slowed.
4. The QT3 driver still skips its 140 schema environments: it evaluates XPath outside a stylesheet, and
   validating a source there needs the tree builder's validated entry point reached through something
   public. A small addition now that the entry point stands.
