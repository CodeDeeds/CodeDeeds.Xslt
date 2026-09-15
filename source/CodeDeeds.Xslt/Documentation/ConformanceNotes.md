# Conformance notes

The journal of the conformance work: what each decision was, why it was taken, and what the W3C suites said
before and after. It was written as the work was done, so a statement here that something is not implemented
may since have been overtaken; where that was noticed, a note says so. For the current picture of what is and
is not supported, read [XsltCompatibility.md](XsltCompatibility.md), which is kept short and kept current.

## XSLT 1.0

Everything not listed below is implemented and verified against `System.Xml.Xsl.XslCompiledTransform`.

### Not implemented

| Feature | Notes |
| --- | --- |
| `id()` | It answers for `xml:id` attributes, which are IDs by their own specification wherever they are written, and for the attributes the document's type declaration types `ID`; a schema could type others, and this engine reads none. A pattern may be anchored on it. `idref()` answers for the attributes typed `IDREF` or `IDREFS`, and `unparsed-entity-uri()` and `unparsed-entity-public-id()` for the entities declared `NDATA` (see *What a document type declaration says*); all three still check what they are given, an unparsed-entity function asked with no context node or about a node in a tree not rooted at a document node being `XTDE1370` or `XTDE1380`. |
| Extension elements and functions | There is no mechanism for registering an external implementation, so every extension element and function is unavailable. What surrounds them is implemented: `extension-element-prefixes` is read, `element-available()` and `function-available()` answer for them, and `xsl:fallback` runs in their place. Reaching one with nothing to fall back to is an error at that moment, not when the stylesheet is compiled. |

### Partly implemented

| Feature | Supported | Not supported |
| --- | --- | --- |
| `xsl:output` | All attributes | — |
| `xsl:sort` | `select`, `data-type`, `order`, `lang`, `case-order` | — |
| `xsl:number` | `value`, `level`, `count`, `from`, `format`, `grouping-separator`, `grouping-size`, `letter-value`, `lang` | — |
| `document()` | All of it, including a node-set argument naming several distinct documents | — |
| Forwards-compatible processing | Instructions. A stylesheet whose `version` is later than 1.0 may contain instructions this engine has never heard of; each falls back when reached, and is an error only if reached with no `xsl:fallback`. | Expression syntax from a later version. Every XPath expression in the stylesheet is parsed when it is compiled, so one that is not XPath 1.0 is rejected even if it would never have been evaluated. |

### Deliberate differences

| Area | Behaviour |
| --- | --- |
| DTD processing | The declaration is read for what it holds — entities expanded, default attributes supplied, ID and IDREF attributes typed, unparsed entities declared, element content whitespace excluded — and nothing outside the document is fetched unless `XsltOptions.EntityResolver` is set: with none, an external entity reference expands to nothing and an external subset goes unread, so a document cannot be used to read local files or reach the network. Entity expansion is capped at ten million characters. |
| `xsl:include` / `xsl:import` | An `href` is only followed when `XsltOptions.StylesheetResolver` is set. With no resolver, a stylesheet cannot reach the file system at all. `FileResolver` serves a directory; `UriResolver` serves the web over HTTP and HTTPS as well as a directory, each gated on its own. |
| `document()` | Requires `XsltOptions.DocumentResolver`, which is separate from the stylesheet resolver: loading a module is loading code, loading a document is loading data, and a caller may permit one without the other. |
| `xsl:result-document` | Requires a result resolver — `XsltOptions.ResultStreamResolver` for bytes, or `XsltOptions.ResultResolver` for text — the only resolvers that write. It is the one instruction by which a stylesheet can create something outside the transformation, and a stylesheet is data as often as it is code, so with neither it cannot reach the file system however it is written. The caller decides what a destination means: a file, or an entry in a dictionary held in memory. Prefer the stream one, which is what lets a result document's own `encoding` reach its bytes. |
| Tunnel parameters and `xsl:function` | A stylesheet function starts with an empty tunnel set, so templates it invokes do not see what the caller was tunnelling. The specification never settled this corner — the working group's own record notes it does not say whether `xsl:function/xsl:param/@tunnel` is even disallowed. The reasoning here: a function is meant to be a function of its arguments, which is what permits a call to be hoisted out of a loop, evaluated once or skipped altogether, and a value arriving through the side would make two identical calls return different answers. Declaring `tunnel="yes"` on a function's parameter is refused rather than ignored. |
| A character mapped twice | The specification settles this only halfway: where a character is substituted more than once after `use-character-maps` has been expanded, the last mapping wins — but it does not say whether a map's own `xsl:output-character` children come before or after the maps it draws in, and published readings differ. This engine reads the children last, so a map overrides what it draws in, which is how every other override in XSLT works and the only reading under which drawing a map in and adjusting it is possible. |
| Character maps and unescaped output | The map applies to text written with `disable-output-escaping`, and inside a CDATA section, because it applies to every text node and attribute value. In a CDATA section the replacement cannot be written as it stands, so the section is closed around it and opened again after — which leaves what a parser reads back unchanged. |
| Result tree fragments | Represented as real trees, so they can be navigated with XPath. XSLT 1.0 only permits converting them to a string; this accepts more than the specification requires and rejects nothing it allows. |
| Recursion depth | A template or function invocation is refused, and reported as an error, once it is either 2000 levels deep or close to exhausting the stack — whichever comes first. The stack check is the one that matters: a depth limit is calibrated against a frame size that every feature added to the engine changes, and being wrong about it is not survivable, because a stack overflow cannot be caught and takes the process with it. A call that is the last thing a template or function does is not a level: an `xsl:call-template` that ends a template's body, directly or as the last instruction of an `xsl:if` or `xsl:choose` branch, and a call that is the whole value of a function whose body is one `xsl:sequence` — directly, or as a branch of `if` or the return of `let` — is made in the caller's own place once the caller has returned. A template or function that recurses that way is a loop, and runs for as many iterations as it likes; one that does so with no terminating case runs forever rather than being refused, as it would under any processor that treats the idiom this way. |
| Error timing | Errors are reported when the stylesheet is compiled wherever that is possible: a reference to a template that does not exist, an attribute set that draws in itself, an unknown unprefixed function, an element in the XSLT namespace that XSLT 1.0 does not define in a stylesheet declaring `version="1.0"`. What the fallback mechanism covers is deliberately left to run time — an unimplemented instruction under forwards-compatible processing, a designated extension element, a call to an extension function — because a stylesheet may contain those quite legitimately and never reach them. |
| `function-available('id')` | Reports `true`, since `id()` is there and answers for `xml:id` and for what a document type declaration types. It reported `false` while the function was absent altogether, which was what let a stylesheet fall back to `key()`. |
| `element-available('xsl:result-document')` | Reports `true` even when no result resolver is configured, so a stylesheet that guards the instruction still writes one and is told plainly what to set if the caller permitted nothing: a destination is the caller's decision, taken after the stylesheet was compiled and changeable through `Xslt.With` without compiling it again. |
| Indentation detail | Where exactly an indented result breaks lines is left to the processor by the specification, and this engine does not reproduce another's choices. It uses `\n` rather than `\r\n`, and indents uniformly where the framework's HTML writer suppresses breaks around some elements. Which elements are indented at all follows the specification: never one containing text. |
| `xsl:vendor-url` | Reported as an empty string, which is the specified answer for a property the processor does not provide. No public address for this engine has been settled on, and inventing one would be worse than saying nothing. `xsl:vendor` reports `CodeDeeds`; both are constants in `SystemProperty`. |
| `xsl:sort` without `lang` | Text is compared ordinally. Collating by the machine's current culture would make one stylesheet and one input produce different orderings on different machines; writing `lang` asks for a named language's collation and gets it. |
| Document order between documents | A node-set may hold nodes from several documents — `document()` given several URIs, or a union of two loaded documents. The specification requires such a set to have a consistent order but leaves the order itself to the processor; this engine orders by the sequence documents were built in, so the nodes of each document stay together and earlier-loaded documents come first. It is stable within a transformation, which is what `<` and `>` on node-sets, and the string-value of a node-set, depend on. |

### Notes

`media-type` does not change the bytes produced — it describes the result to whatever consumes it. Read it from
`Xslt.OutputMediaType`, which falls back to `text/xml`, `text/html` or `text/plain` to match the method when the
stylesheet names none. `Xslt.OutputMethod` and `Xslt.OutputEncoding` are exposed alongside it, since only the
stylesheet knows them and a caller setting a `Content-Type` header needs them.

`XsltOptions.Parameters` supplies values for the stylesheet's top-level `xsl:param` declarations. A key is the
parameter's local name, or `{uri}local` for one in a namespace; a prefix is refused, because the caller's
prefixes are not the stylesheet's and there would be nothing to resolve them against. A value may be a string,
a boolean, any of the integer types, a `double`, `float` or `decimal`, an `XdmTree` to pass a whole document
without needing a resolver, an `XPathValue` already built, or `null` for the empty sequence. Anything else is
refused rather than stringified — a caller who passed an object where a string was meant should hear about it
instead of getting a result nobody asked for — and a date or duration is passed as its lexical form and read
with `xs:dateTime($p)` or its relatives in the stylesheet.

A name the stylesheet does not declare as a parameter is ignored, which the specification requires and which
lets one set of values serve several stylesheets. A name matching an `xsl:variable` is ignored for the same
reason: only `xsl:param` declares something a caller may set. Declaration order does not matter — every
supplied value is installed before any global is evaluated, so a variable declared above a parameter and
referring to it still sees what the caller passed. The dictionary is copied when the options object is built,
so changing it afterwards cannot change what a shared `Xslt` does.

`omit-xml-declaration` follows the specification and defaults to `no`. `XsltOptions.OmitXmlDeclaration`
overrides it per instance — `null` follows the stylesheet, `true` suppresses, `false` forces — so one compiled
stylesheet can serve both a standalone document and an embedded fragment. Use `Xslt.With` to obtain a
differently configured instance without compiling again.

## XSLT 2.0 Compatibility

The instruction set is complete — `xsl:import-schema` is refused rather than implemented, which is what a
processor that cannot validate is required to do — and XSLT 2.0 is now **claimed**:
`system-property('xsl:version')` reports `2`. This is the non-schema-aware conformance level, which is the one
the specification defines for a processor with no validator; there are no schema-aware sequence types and no
collation of a caller's own devising, both noted below.

Claiming it changes two things, and neither is cosmetic.

A stylesheet declaring `version="2.0"` is now taken at its word rather than processed forwards-compatibly, so
a construct this engine does not implement is an error where it stands instead of a silent fallback at the
point it is reached. That is the stricter reading and the conformant one, but a stylesheet that was relying on
the lenient one will now hear about it.

And forwards-compatible processing means *later than this processor*, which no longer includes `version="1.1"`.
A version between one and two now selects the 1.0 reading of everything 2.0 redefined — which it always did —
without also suppressing errors. `XslCompiledTransform` still falls back there, because it implements 1.0 and
this does not; that divergence is the same fact as the different answer to `system-property('xsl:version')`,
and the differential tests record it rather than paper over it.

XSLT 3.0 is claimed too, since the work below reached it (see *The claim moves to 3.0*):
`system-property('xsl:version')` answers `3` for a caller who names no version, and `2` for one who sets
`XsltOptions.Version` to 2.0 — under which a `version="3.0"` stylesheet gets forwards-compatible processing
exactly as a `version="2.0"` one used to here.

Instructions implemented: `xsl:for-each-group` in all four forms with `current-group()` and
`current-grouping-key()`, `xsl:analyze-string` with `regex-group()`, `xsl:sequence`, `xsl:perform-sort` and
`xsl:next-match`, `xsl:namespace`, `xsl:document` and `xsl:function`. `xsl:value-of` writes the whole
sequence with a `separator`, where under 1.0 it writes only the first node, and it may say what to write with
its **content** in place of a `select`. The content is a sequence constructor rather than a tree, so
`<xsl:value-of><xsl:sequence select="1 to 3"/></xsl:value-of>` writes three items joined and not the string
value of something built from them; the separator then defaults to nothing rather than to a space, which is
what makes content of `a` and `b` write `ab`. Saying both, or neither, is `XTSE0870`.
`element-available()` reports every one of these
instructions, whatever version the stylesheet declares: it asks what this engine can instantiate, which does
not move with the `version` attribute, so a 1.0 stylesheet guarding `xsl:for-each-group` finds it and does not
fall back for no reason.

A function is identified by its name together with how many parameters it takes, and its name must be in a
namespace so that it can never shadow one from the core library. When its body is a single `xsl:sequence` it
returns that value as it is, which is how it returns a number rather than the text of one; any other body is
captured as a result tree, which is what a function building elements wants.

`xsl:result-document` writes a secondary result, with an `href` that is an attribute value template and any
serialization attributes it wants; what it does not mention it inherits from `xsl:output`, so one
transformation can write XML and HTML at once. It requires `XsltOptions.ResultStreamResolver` or
`XsltOptions.ResultResolver` and is an error without either — see the deliberate difference noted below.
Writing the same destination twice is refused.

Tunnel parameters are implemented. `xsl:with-param tunnel="yes"` puts a value into a set that every template
invocation passes on without being asked, and `xsl:param tunnel="yes"` reads it back however many templates
lie in between; the built-in template rules pass it on too, so a value survives a stretch of document that has
no templates at all. `xsl:apply-imports` accordingly takes `xsl:with-param`, which XSLT 1.0 did not allow. The
two kinds of parameter are separate namespaces: a tunnelled value never binds to an ordinary declaration of
the same name, nor the reverse. A `tunnel` attribute is refused on anything but a template parameter, and
refused outright if it is neither `yes` nor `no` — a misspelling would otherwise leave the stylesheet running
and quietly reading a default.

`xsl:document` builds a document node from its content. Written into a result it contributes its children and
nothing else, since no document node can be a child of an element, so what reaches the output is what the same
content would have produced in place. What it earns is a boundary: an attribute or a namespace created
directly inside it belongs to the document node, which carries neither, and is refused rather than attaching
itself to whatever element encloses the instruction. Where a document node is a value rather than output —
several of them in one sequence — this engine cannot tell them apart, because the result of a sequence
constructor is always captured as a single tree; that waits on the sequence-valued variables `as` declarations
bring.

`validation="strict"` and `validation="lax"`, and a `type` attribute, are refused wherever the specification
allows them to be written: on `xsl:element`, `xsl:attribute`, `xsl:copy`, `xsl:copy-of`, `xsl:document`,
`xsl:result-document`, and on a literal result element, which carries them in the XSLT namespace as
`xsl:validation` and `xsl:type`. A processor that cannot validate has to say so rather than hand an
unvalidated result back to a stylesheet that asked to be told when its result does not fit the schema, and a
value that is none of the four is refused as well. `preserve` and `strip` are accepted, since with no type
annotations anywhere both amount to what this engine does already. The refusal carries `XTSE1660`, and a
value that is none of the four carries `XTSE0020`. Only the XSLT-namespaced form is a request
to the processor: an ordinary `type` attribute on a literal result element is content, so `<input
type="text"/>` is unaffected.

`xsl:namespace` puts a namespace node on the element being built, taking the prefix from an attribute value
template and the URI from a `select` or from its content — so neither half of the binding need be known until
the transformation runs, which is the only reason to write one rather than let a literal result element carry
its own. It refuses what XML itself would not allow: an empty URI, since XML has no way to undeclare a
namespace; the prefix `xmlns`; separating `xml` from the namespace it names; binding one prefix twice on one
element, which would serialize as the same attribute written twice; and any binding that would change what the
element's own name means, which covers a default namespace on an element that is in none and a re-binding of
the prefix the element was written with. The last two are checked where the result is serialized, since only
there is the element's own name known.

`xsl:character-map` substitutes characters as the result is serialized, and the replacement is written exactly
as it stands: mapping a no-break space to `&nbsp;` produces that entity reference rather than an escaped
rendering of it. That makes it the supported replacement for `disable-output-escaping` — declared once, where
it can be read, rather than at every place the text is written. A map is named from `use-character-maps` on
`xsl:output` or on `xsl:result-document`, and may draw in other maps by the same attribute on itself; a map
that reaches itself, a name that no map answers to, and two maps of one name at the same import precedence are
all refused. Substitution reaches text nodes and attribute values, and nothing else: not element or attribute
names, not comments, not processing instructions, not namespace declarations. As the specification warns, a
map can produce a result that is no longer well-formed — that is what asking for one means.

`required="yes"` on `xsl:param` says the caller must supply a value, and a required parameter may not also
carry a default — the two contradict each other, so writing both is refused. Where the invocation names its
template, which is `xsl:call-template`, a missing parameter is reported when the stylesheet is compiled; the
check runs once every template body is known, so a call to a template declared further down is checked too.
Everything else — `xsl:apply-templates`, `xsl:next-match`, `xsl:apply-imports` — is settled by the pattern
that matched and so is reported when the invocation happens. A required *tunnel* parameter is always left to
run time, since a value may be put in flight by any invocation further out and no single call site knows what
will arrive. `required` on a function's parameter is refused: every parameter of a function is required
already.

A required *stylesheet* parameter is satisfied by `XsltOptions.Parameters`, described among the 1.0 notes
above. Declaring one and supplying nothing fails with the error the specification gives for a value that was
not supplied.

`xsl:attribute`, `xsl:comment` and `xsl:processing-instruction` take a `select` in place of content, and
`xsl:attribute` a `separator` to go with it, defaulting to a space. Writing both a `select` and content is
refused rather than one quietly winning. `xsl:copy` and `xsl:copy-of` take `copy-namespaces="no"`, which
leaves behind the declarations nothing in the copy uses — a prefix the copied names do need is still declared,
because the writer supplies what a name requires as it goes. `xsl:number` takes a `select`, so the node being
numbered need not be the context node, and an `ordinal` attribute, which produces `1st`, `2nd`, `13th` in
English and `3.` in German: the specification leaves what `ordinal` means to the language, and the value of
the attribute is the ending where the language inflects one (see *What language a number is spelled in*).
What else `xsl:number` was getting wrong is below.

Modes: a template may name several — `mode="a b"`, with `#default` standing for the mode a template with no
`mode` attribute belongs to — or `mode="#all"` for every mode there is. `#all` is expanded once the whole
stylesheet is compiled, when the set of modes is complete, since a mode comes into existence the first time
anything names it and the last thing to name one may be an `xsl:apply-templates` deep in a template body.
That keeps dispatch exactly as it was: one bucket per mode, and nothing extra to consult while matching.
`xsl:apply-templates mode="#current"` stays in the mode in force, which now belongs to the invocation rather
than to the template — one template may serve several modes, and `xsl:call-template` changes the mode not at
all, so a named helper called from a mode still applies templates in it.

`as` declares a type on a variable, a parameter, a function's parameter or a function's result, and it does
two things. It checks — by the function conversion rules, which are deliberately few: a node is atomized, an
untyped value is read as the type asked for, and a number is promoted to a wider one. Nothing else converts,
so a stylesheet passing the string `'7'` where `xs:integer` was declared hears about it rather than being
guessed at; only document content, which is untyped, takes the declared type silently, and that is the case
the rule exists for.

The other thing it does is decide what a sequence constructor's content amounts to. Without a declaration
everything the constructor produces is built into a single document node, so three `xsl:sequence` instructions
yielding integers arrive as the text `1 2 3` and count as one. With a declaration the content is the sequence
it produced: three integers, which `sum()` adds. Nodes are still built — a literal result element inside such
a constructor is an element node — and take their place in the sequence alongside the atomic items, with
adjacent text becoming one text node as it does anywhere else. A sequence holding nodes can be filtered by a
predicate, copied by `xsl:copy-of`, and returned from a function.

`xsl:import-schema` is **refused**, with code `XTSE1650`, rather than ignored. Not implementing it is
permitted; passing over it is not. Every other declaration this engine does not implement is ignored so that
the stylesheet still runs to the extent it can, because dropping it costs the stylesheet nothing it asked for.
A schema import is the exception: the stylesheet that writes one is written against the types in it, and
running with the import dropped would answer typed questions with untyped values and never say so. Same
reasoning as the refusal of `validation="strict"` below.

The rest of the groundwork:

| | |
| --- | --- |
| `XsltVersion` | The version in force is resolved per element, from the nearest `version` or `xsl:version` in scope, and drives both directions of compatibility. Forwards-compatible processing already uses it; backwards-compatible behaviour will select between the 1.0 and 2.0 readings of the constructs XSLT 2.0 redefined. |
| `XdmTypeCode` | Atomic values carry an XPath 2.0 type. `xs:untypedAtomic` is the one that matters: a node's value is untyped and takes its meaning from what it is compared against, which is why `@price < 10` reads the same in both versions while `'10' < '9'` does not. |
| Relational operators | Both readings are implemented and selected by the version an expression was compiled against. |
| Type constructors | `xs:integer()`, `xs:double()`, `xs:decimal()`, `xs:float()`, `xs:string()`, `xs:boolean()` and the derived integer types, which share a representation and differ in the range they accept. An integer keeps eighteen digits of precision, as the specification requires — more than a double carries. The date, time and duration types are implemented, along with their component functions; the binary and gregorian types are not. |
| Sequences | `()`, `(a, b, c)` and `1 to 5`, flattened as the data model requires and keeping order and duplicates. Node-sets are unchanged, so a path still produces one. |
| Value comparisons | `eq`, `ne`, `lt`, `le`, `gt`, `ge`. Unlike `=` these compare one value with one, and yield the empty sequence when an operand is empty. |
| Predeclared prefixes | None. XPath binds `xs` and `fn` for you and XSLT does not, so a stylesheet declares what it uses; `fn:count(…)` is the same call as `count(…)` once `fn` is declared. |

Dates and durations have arithmetic of their own, where the operands' types decide the result's rather than a
common type being found: a date minus a date is a `xs:dayTimeDuration`, a date plus a duration is a date, a
duration times a number is a duration, and a duration divided by a duration is a number. Months are added as
months and not as a count of days, because they are not all the same length — one month after 31 January is
28 February. Anything with no meaning, such as a date times a number, is refused rather than coerced.

Under the IL backend, arithmetic compiled against 2.0 falls back to the interpreter for the operation itself.
Emitted arithmetic is double arithmetic, which is the whole of XPath 1.0's numeric model and only one of
2.0's answers; the alternative to falling back is emitting the wrong answer quickly. Everything around the
operation is still emitted.

Arithmetic follows the 2.0 promotion rules: integer, then decimal, then float, then double, so `1 + 1` is an `xs:integer` and `1 div 2` is `0.5` rather than `0`. Overflow of `xs:integer` is reported rather than wrapped, which is what a bounded implementation must do. `idiv` truncates.

**Four functions that count return an `xs:integer` from 2.0 on**: `count`, `last`, `position` and
`string-length`. XPath 1.0 declared them only "number" and meant a double, and the difference is visible both
to `instance of` and to arithmetic — `count(*) div 2` is an `xs:decimal` from an integer and a double from a
double. A backwards-compatible expression therefore keeps the double it was written against, rather than
having a type appear underneath it that the language it was written in does not have. The IL backend emits
`position()` and `last()` to this rule as well, which it did not always do — see *What the emitted backend
was never asked*.

**An integer literal past what a 64-bit one holds is a wider integer**, `xs:integer` being unbounded in
the specification. It was `FOAR0002` here for a long time, which the specification permits so long as
the limit is reported rather than absorbed; what it does not permit is reading the literal as an
`xs:double`, which would answer with a different number than was written and do it silently. The value
is now held instead: a `long` wherever it fits one and a `BigInteger` where it does not, narrowed back
the moment it fits again.

**The string functions count code points, not UTF-16 units.** A character above the basic plane is two units
in a .NET string and one character to XPath, so `string-length('abc𝅖def')` is 7 where `String.Length` is 8.
Every position `substring` takes is counted the same way — otherwise it could land between the halves of a
pair and return a lone surrogate, which is no character at all and cannot be written into a result — and
`translate` maps character for character, so a pair is one entry on either side. Each of these has a fast path
for text holding no surrogate pair, which is nearly all text. The regular-expression functions count the same
way, by translating every construct that names a *set* of characters into the surrogate pairs that spell it —
see [Regular expressions](#regular-expressions) for what that costs and what it does not reach.

Function library: the sequence functions (`empty`, `exists`, `reverse`, `distinct-values`, `subsequence`, `insert-before`, `remove`, `index-of`, `unordered`, `zero-or-one`, `one-or-more`, `exactly-one`, `deep-equal`), the numeric ones (`abs`, `round-half-to-even`, `min`, `max`, `avg`), the string ones (`upper-case`, `lower-case`, `ends-with`, `string-join`, `compare`, `codepoints-to-string`, `string-to-codepoints`) and the regular expression ones (`matches`, `replace`, `tokenize`), whose XML Schema syntax is translated to .NET. The date and duration functions are the component accessors (`year-from-date` and its twenty relatives, down
to `timezone-from-time`), `current-date`, `current-time`, `current-dateTime`, `implicit-timezone` and
`dateTime`. Arithmetic between dates and durations is implemented, as are `format-date`, `format-time`,
`format-dateTime` and the `adjust-*-to-timezone` family, all described below.

**`fn:unparsed-text()` can be answered without a transformation.** It needs a resolver and a base URI,
which a transformation brings and an expression evaluated on its own does not, so it used to refuse
outright — which made nine QT3 tests fail for want of a file rather than for anything about the
function. `DynamicContext.TextLoader` is what a caller lends it, the companion of the `DocumentLoader`
that already answered `doc()` for a static expression at compile time. Whatever the loader raises
becomes `FOUT1170`: `fn:unparsed-text-available()` is defined as this function not raising and answers
by catching what it does, so a loader failing in its own words would be no answer at all rather than a
false one.

And `fn:unparsed-text(())` reads nothing and returns nothing, which `fn:json-doc(())` rests on: json-doc
is parse-json over unparsed-text, so an empty argument has to travel the whole way.

The functions that reach outside the expression: `doc` and `doc-available`, `unparsed-text` and
`unparsed-text-available`, `base-uri`, `document-uri`, `static-base-uri` and `resolve-uri`. All of them go
through `XsltOptions.DocumentResolver`, since reading a file as text is reading data by another name and a
caller who allowed documents has already answered the question; only the parsing differs. The two
`*-available` functions answer `false` where their partner would fail — including when no resolver is
configured, which is what makes them worth calling. `base-uri` and `document-uri` report the URI a tree was
loaded from, and the empty sequence where there is none; what `xml:base` does to the first, and what a tree
the stylesheet built is relative to, is under *`xml:base` and where a relative reference points* below.

`resolve-uri` is RFC 3986 §5.2 written out rather than `System.Uri` asked. That class resolves and normalises
in one step and offers no way to have only the first: it gives an authority that had no path an empty one, so
`http://example.com` comes back as `http://example.com/`; it lower-cases the scheme and host; and it decodes
percent-escapes. Each of those changes a URI the function is required to return unchanged. Written out, a
reference that needs no resolving comes back character for character. Two things make text no URI reference
here — a percent sign not followed by two hexadecimal digits, and a colon in the first segment of a relative
path, which is how `a:b` is told from a scheme — and both are `FORG0002`, as is a base that names no scheme
or carries a fragment. Characters outside the URI set are let through, an IRI being a URI reference for this
purpose. A base is only examined when it is needed: `resolve-uri('http://x/a', 'nonsense')` answers, because
a reference naming a scheme is already its own answer.

`format-dateTime`, `format-date` and `format-time` write a value against a picture string —
`[Y0001]-[M01]-[D01]`, or `[D1] [MNn] [Y]` for `23 August 2026`. A numeric component's presentation modifier
is a **`format-integer` picture**, which the specification says outright, so `[Y๐๐๐๑]` answers in Thai digits
and `[Y9;999]` groups by semicolons; the roman and alphabetic sequences still go through `xsl:number`'s
formatter, which is where they were already. The names are English, and a call asking for another language
is answered in English and says so in front of the answer, `[Language: en]`, as one asking for a calendar
other than the Gregorian one is answered with `[Calendar: AD]` (see *What a date is written as, and what a
zero-length match is*).

Two ways for a component to be wrong, and they are different complaints. `[bla]` and `[y]` name no component
of any date or time — the picture is malformed, which is `FOFD1340`, along with a width below one or narrower
than itself, a digit pattern mixing families, and an optional digit on the wrong side of a mandatory one
(the left for every component but the fractional seconds, which pad from the other end). `[Y]` in
`format-time` names a component this language has and this value has not, which is `FOFD1350`.

The comma that introduces a width is the **last** one with a width behind it, a comma being a perfectly good
grouping separator: `[Y9,999,*]` groups by thousands and asks for no maximum.

**The fractional seconds run the other way from everything else.** Their digits are significant from the left,
so the component pads and truncates on the *right*: `[f777]` writes .12 as `120` and `[f99]` writes .123 as
`12`, cutting rather than rounding — under 3.0; a processor claiming 2.0 rounds, as XSLT 2.0 had it (see
*What a date's year and timezone are written as*). Its picture bounds it as well as padding it, where every other component
takes the picture as a minimum — the exception being a picture of one digit and no optional sign, `[f1]`,
which is the plain form and asks for no maximum. A width written as well *widens* rather than narrows, so
`[f111,2-2]` keeps the three digits its picture asked for.

`[E]` is refused by `format-time`, a time having no era; `[PNn]` is title case, so the meridiem reads `A.M.`
where `[Pn]` reads `a.m.` and `[PN]` reads `A.M.` too.

The `adjust-*-to-timezone` family moves a value between timezones, keeping the instant — but only where there
is an instant to keep. A value carrying **no** timezone denotes none, so the timezone is *attached* and the
clock reading left alone: `adjust-date-to-timezone(xs:date('2002-03-07'), -PT10H)` is that day in −10:00 and
not the day before, which is what converting would have made of it. Adjusting to the empty sequence goes the
other way, removing the timezone and keeping what the clock said, for the same reason.

**A value moved by a duration or into a timezone comes back as its own type**, which the specification
says the long way round and means literally. Adding an `xs:dayTimeDuration` to an `xs:date` is defined as
taking the date as a dateTime at midnight, moving that, and casting the result back to `xs:date`; the
`adjust-*-to-timezone` family is defined the same way. The cast at the end is not decoration. It is what
drops the hours the duration carried, and a value that keeps them is wrong in a way that hides: a date
prints no time of day, so `xs:date('1999-08-12') + xs:dayTimeDuration('P23DT09H32M59S')` wrote itself as
`1999-09-04` and then compared unequal to `xs:date('1999-09-04')`. The same step is what wraps an
`xs:time` around the clock, a time having no day to roll into: twenty-three days and nine hours later is
nine hours later. And it is what gives an adjusted date the instant its new timezone implies, so that
`adjust-date-to-timezone(xs:date('2002-03-07Z'), PT10H)` begins ten hours earlier than the day it names
did in UTC, rather than at the same moment with a different label on it.

**The three `current-*` functions read the clock once and keep the reading**, shared by everything in the same
execution scope — one transformation, or one expression evaluated outside a transformation. The specification
requires it and the reason is plain: two templates asking the time must be told the same time, or a stylesheet
stamping a date on every page could straddle midnight. It is also what makes them agree with one another, so
`current-date()` really is the date part of the instant `current-dateTime()` reports. `fn:dateTime` builds one
from a date and a time; both halves are optional, and a moment made of half a moment is no moment.

Also implemented: `data`, `nilled` (always false, since nothing carries a type annotation), `error`, `trace`,
`in-scope-prefixes`, `namespace-uri-for-prefix`, `encode-for-uri`, `iri-to-uri`, `escape-html-uri`,
`codepoint-equal`, `normalize-unicode` and `default-collation`.

Bindings and conditionals: `for $x in E return E`, `some`/`every $x in E satisfies E`, and
`if (E) then E else E`. A variable bound this way lives in its own storage, assigned by how deeply the
bindings nest, so it never collides with one the stylesheet declared. Clauses nest rather than run in
parallel — `for $x in A, $y in B` is the product, and a later clause sees the variables bound before it.

The words `for`, `if`, `some`, `every`, `to`, `eq` and the rest are keywords only where one can appear;
elsewhere they are ordinary names, so an element may be called any of them and a path still reaches it.

The type and node-set operators: `instance of`, `cast as`, `castable as`, `treat as`, `intersect`, `except`, `union` as a word, and the node comparisons `is`, `<<` and `>>`. Sequence types cover the atomic types, the kind tests and `item()`, with the occurrence indicators; the schema-aware forms such as `element(*, xs:date)` are not among them, since this engine does not validate.

**A value carries the type it was made under**, which for the derived integers and the string-derived types is
narrower than the type it is *held* in — every one of the first family is an `xs:integer` in memory and every
one of the second a string, because that is what they are. The distinction matters because `instance of` asks
which type a value was given rather than which types could have held it. So `xs:long(1) instance of
xs:nonNegativeInteger` is **false** although 1 is a non-negative integer: the two are siblings under
`xs:integer`, not one above the other. `12678967543233 instance of xs:int` is false for the same reason, a
literal being a plain `xs:integer`. What *is* true is derivation upwards — `xs:ID('n')` is an `xs:NCName`, an
`xs:Name`, an `xs:token` and an `xs:string`. The name is read by the type tests and by nothing else:
arithmetic gives back the type it works in, so `xs:int(5) + 1` is an `xs:integer`, and casting to a name that
is not a derived one clears what the value arrived carrying. It costs nothing to hold — `XPathValue` was
already padded out around a reference and a double, and this is a third byte in the same wasted space.

**`xs:numeric`** is XPath 3.1's name for the union of `xs:double`, `xs:float` and `xs:decimal` — and so of
`xs:integer` too, which is derived from the last. It is how a function says it takes a number without saying
which kind. A cast to it keeps a value already of one of its members, so `17 cast as xs:numeric` stays an
`xs:integer` rather than becoming `17e0`; anything that is no number at all becomes an `xs:double`, the first
member that admits it, which is where `true() cast as xs:numeric` gets `1e0`.

`xs:QName` holds a name in a namespace together with the prefix it was written with, which takes no part in
identity: two QNames are the same name when their namespace and local part agree. A literal — `xs:QName('p:x')`
— is resolved where it is written, since a prefix means whatever it meant there and nothing at run time knows
that; `resolve-QName()` does the same against an element in the input, and `QName()` builds one from parts.
`node-name()`, `local-name-from-QName()`, `namespace-uri-from-QName()` and `prefix-from-QName()` read them
back.

The binary types hold bytes rather than text, so `xs:hexBinary('FF')` and `xs:hexBinary('ff')` are one value
and casting to `xs:base64Binary` is a re-spelling rather than a conversion. The five gregorian types —
`xs:gYear`, `xs:gYearMonth`, `xs:gMonth`, `xs:gMonthDay`, `xs:gDay` — keep the parts of the calendar they
name, with their timezone if written. None of these three families has an ordering: they compare for equality
and `lt` on one is refused, because one name or one string of bytes does not come before another.

### Type errors

The largest behavioural difference between the two languages, and the least visible. XPath 1.0 converts
operands until they are comparable, so `"1" = 1` is true and `"a" = 1` is false; XPath 2.0 has a table saying
which pairs of types a comparison is defined for, and raises `XPTY0004` for the rest. Nothing here produces a
wrong answer a reader would notice — which is the argument for it, since a stylesheet built on the conversion
keeps working until the day the data changes shape.

What is checked, all under 2.0 only and none of it reaching a stylesheet that declares `version="1.0"`:

| | |
|---|---|
| **Comparison** | The operator mapping. Numbers compare with numbers, strings with strings, a date with a date but not with a `dateTime`. `xs:hexBinary` against `xs:base64Binary` is two types rather than two spellings, and has no comparison. |
| **Untyped operands** | A *general* comparison (`=`, `<`) reads an untyped operand as whatever the other side is, so `@price > 10` means what it looks like. A *value* comparison (`eq`, `lt`) reads it as a string, so `@price gt 10` is a type error — which is what `eq` is for. |
| **Ordering** | Narrower than equality: two names, two partial dates, and two durations of different halves are equal or not and have no order. |
| **Numeric promotion** | The ladder is decimal, then float, then double, and a comparison climbs only as far as it must. A decimal beside a float becomes that float rather than the double neither of them is, so `xs:decimal(1.01) eq xs:float(1.01)` is true where reading both as doubles would have them differ. The conversion is made from each value's own representation rather than by way of a double, so it rounds once. |
| **Equality without an order** | `fn:deep-equal` and `fn:index-of` ask `eq`, and where `eq` is not defined between two types the answer is that the values *differ* rather than that the question was wrong: a date is not equal to a string by being incomparable with it. They part company over `NaN` — `deep-equal` asks whether two sequences are the same and a sequence holding `NaN` is the same as itself, while `index-of` asks `eq` outright and so finds no `NaN`, not even the one it was given to look for. |
| **Casting** | The casting table. Where it is blank the cast is a type error, not an attempt: without it `xs:gYear('1999') cast as xs:float` would go by way of the text and come back with a number — a re-reading of how the value was written rather than a conversion of what it is. Everything still casts to and from `xs:string`, so a stylesheet that wants the re-reading can ask for it. `xs:anyAtomicType` and its relatives cannot be cast to at all (`XPST0080`, raised where the type is parsed). |
| **Arithmetic** | Only numbers and untyped values. `1 + '2'` was 3 and `1 + 'x'` was NaN; both are `XPTY0004`. An empty operand gives the empty sequence rather than NaN. A leading sign follows the same rule, so `-'a string'` is refused rather than answered NaN. |
| **Durations** | A year-month duration and a day-time one have no common scale, so no sum and no ratio. A plain `xs:duration` may carry both halves and is out of the arithmetic altogether — only its two halves are operands. A time has no date, so months mean nothing to it. |
| **Effective boolean value** | A date, a duration, a name, a string of bytes and a sequence of two atomic values are none of them true or false, and say so with `FORG0006` instead of coming back true. |
| **Binary ordering** | XPath 3.1 gives `xs:hexBinary` and `xs:base64Binary` an order that 2.0 left undefined: octet by octet, unsigned, a prefix first. **This engine applies it at every version**, so a 2.0 expression comparing two of them with `lt` gets an answer where the specification says a type error. Gating it would mean threading the version through the comparison machinery to reach four call sites, two of which have no version to thread; the deviation is one operator on one pair of types, and it is the later specification's answer rather than an invention. The two types are still never compared *to each other*. |
| **Aggregates** | `avg`, `sum`, `min` and `max` raise `FORG0006` over a mixture: there is no comparison between a string and a number to be the larger under, and no common scale on which to add a year-month duration to a day-time one. Untyped values become doubles, so an aggregate over document content is unchanged. The result takes the type promotion gives it — a sum of integers is an `xs:integer`, and `min((1, xs:float(2)))` an `xs:float` rather than the integer that happened to be smallest — rather than coming back a double whatever went in. |
| **`idiv`** | An `xs:integer` whatever its operands were, being the operator that asks how many times one number goes into another. It is the one whose result type does not follow from promoting the operands. |
| **Division by zero** | Three answers, and the codes distinguish them. `1 idiv 0e0` is `FOAR0001`: a zero divisor is the same error whatever type the operands have, and doubles do not make it an overflow. A duration divided by zero is `FODT0002`, the answer being a duration and there being no infinite one for it to be. Dividing by `NaN` is `FOCA0005`. |
| **Ranges** | `1.1 to 3` is `XPTY0004`. A range counts, so its ends are counting numbers. |
| **Function arguments** | Every function in the core library is declared with a type per parameter, and the function conversion rules are applied before the call. `fn:abs(xs:string('1'))` and `translate(1, '-', 'x')` are `XPTY0004`; `substring('abc', @n)` still works, because an untyped argument is read as the declared type. Cardinality counts too, so `string(/r/i)` over two nodes is refused rather than quietly taking the first. |
| **Path steps** | A step is taken from a node, so `(10)/child::*` is `XPTY0019` — the error XPath names for this in particular, rather than the general `XPTY0004`. A path may start from a sequence of nodes as readily as from a node-set, which is what `for $h in /works/employee return $h/text()` needs. A step may be any expression and not only an axis — `employee/(status|overtime)/day` and `name/string()` — and a last step giving both nodes and values is `XPTY0018`, there being no order that would suit both. |
| **The context item** | An item, which is a node *or* an atomic value. A step needs the node — `(1)[foo]` is `XPTY0020` — and reading the item where there is no focus at all is `XPDY0002` rather than an empty answer. |
| **Kind tests** | `element(name)`, `attribute(name)` and `document-node(element(name))` say which kind of node they want, where `*` takes its meaning from the axis. So `child::attribute()` selects nothing and `child::*` selects every element. The same tests serve as types, so `instance of element(r)` asks exactly what `child::element(r)` asks. |
| **Nothing in, nothing out** | A parameter declared with `?` takes the empty sequence and gives one back. `xs:string(/r/@id)` over a missing attribute is nothing, not the empty string — so `'abc' eq xs:string(/r/@id)` is nothing rather than false, which is what makes `eq` worth using on data that may be absent. |

**A leading sign binds differently in the two languages.** XPath 1.0 applies it to a union expression, which
puts it outside everything below; XPath 2.0 puts `UnaryExpr` between `CastExpr` and `ValueExpr`, so it binds
tighter than `cast as` and `instance of`. `-129 castable as xs:byte` asks whether −129 is an `xs:byte` and
not for the negative of a boolean. XPath 2.0 also has a **unary plus**, which 1.0 does not: it gives a number
back with its type intact, and refuses what is not one.

**The context item may be an atomic value.** XPath 1.0's is always a node, which left `.` nothing to mean in
`(1 to 25)[. ge 10]`; a node-set cannot hold an integer, so the predicate was reading an empty one and the
filter quietly kept everything. `.` is now a primary expression rather than `self::node()`, which is what the
2.0 grammar makes it, and a filter over anything that is not a node-set filters it as the sequence it is.
A node context item still yields a node-set of one, so the 1.0 half is unchanged down to its fast paths.

Seven errors say seven different things about a cast that failed, and the codes are not interchangeable.
The division that runs through all of them is **whose limit was reached**: a value the type does not have
is the type's limit and `FORG0001`, while a value the type has and this engine cannot hold is the engine's
and gets an overflow code.

`FODT0001` is a date or time whose form is good and whose value this engine cannot hold — `xs:date('2004-02-30')`
names no day and never will, where `xs:date('1000000000-01-01')` names one perfectly well and the year is
held in an `int`. The rest of the form is still checked past the sign, so a bad month in a bad era is
still `FORG0001`, and February keeps its length in a year too long to hold. An out-of-range year is reported
as one only where the rest of the text reads: `'99999999999999999999999999999-XX' cast as xs:gYearMonth` has
a month that is no month, and naming the year's size would send a reader after the wrong half of it.
`FODT0002` is the durations' overflow, on the same reading: `xs:yearMonthDuration('P100000000000Y')` says
plainly what it means and the months are held in a 32-bit signed integer here.

`FOAR0002` was the integers' overflow and has nothing left to say, `xs:integer` no longer being bounded
and the last two places that rendered through a fixed-width number no longer doing so (see *What a
number that will not fit sixty-four bits is rounded and written as*). A cast now fails only where the
target type excludes the value, whatever its size, so
`xs:unsignedLong('18446744073709551615')` is that type's maximum and is held, while
`xs:unsignedLong('18446744073709551616')` and `xs:long('9223372036854775808')` are each one past the
type itself and are `FORG0001`. The rest:
`FORG0001` is text outside the type's lexical space, and also a value outside a *derived* type's range —
`xs:byte(300)` is not an overflow but an integer that is not an `xs:byte`. `FOCA0003` is a number too large
to be an `xs:integer` at all and `FOCA0001` too large to be an `xs:decimal`, both of which are this engine's
storage speaking. `FOCA0002` is an invalid lexical value where nothing was being cast: `xs:decimal(xs:double('INF'))`,
where infinity has no decimal spelling to be too large for, and `QName('', 'a:b')`, where a prefix was given
nothing to stand for.

A constructor is declared `xs:anyAtomicType? -> T?`, so more than one value in is the argument being of the
wrong type rather than a value that will not convert: `xs:integer((1, 2))` is `XPTY0004`, not `FORG0001`.

**`xs:anyURI` is a type of its own**, not an alias for `xs:string`. It holds text and every string operation
works on it — the function conversion rules promote it, so `substring-after(base-uri(.), '/')` means what it
looks like — and it compares with strings, which the operator mapping puts it alongside. Where the type is
the question it differs: `xs:anyURI('1') cast as xs:integer` is `XPTY0004`, because the casting table has no
route from a URI to anything but text, and reading the digits would answer a question about how the value was
written rather than about what it is. `base-uri()`, `document-uri()`, `static-base-uri()`, `resolve-uri()`,
`namespace-uri-from-QName()` and `namespace-uri-for-prefix()` all return one. `namespace-uri()` still returns
a string, because it is shared with XPath 1.0, where the type does not exist.

### Lexical spaces

A schema type is a value space and a *lexical space* — the texts that denote a value of it — and the second
is as much part of the type as the first. `xs:language` exists to mean "a language tag and nothing else", so
accepting `a*a` tells a stylesheet something untrue. Each type reads only what it is defined to read:

| | |
|---|---|
| `xs:language` | `[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*`. Nine letters is not a language tag, however much it looks like one. |
| `xs:Name`, `xs:NCName`, `xs:NMTOKEN`, `xs:ID`, `xs:IDREF`, `xs:ENTITY` | The XML productions of those names. A Name may carry a colon and an NCName may not. |
| `xs:token`, `xs:normalizedString` | Whitespace collapsed and normalized respectively, which changes the value rather than refusing it. Every type above derives from `xs:token`, so the text is collapsed before it is matched: `xs:NMTOKEN(' f f')` becomes `f f`, which is still not one token. |
| `xs:anyURI` | A percent sign must introduce two hex digits, and a leading colon names an empty scheme. Deliberately narrower than a full RFC 3986 parse, which would refuse relative references and other legitimate values — so this is weaker than a schema-aware processor's check. |
| `xs:double`, `xs:float`, `xs:decimal`, `xs:integer` | The schema forms exactly. The three special values are `NaN`, `INF` and `-INF` as spelled; `Infinity`, `nan` and a thousands separator are text. |
| durations | A digit each side of the decimal point in the seconds. |
| dates and the gregorian types | A year longer than four digits must not begin with a zero — `02004` is not `2004` written differently — and a day must exist in its month, February taking 29 since no year is named. |
| `xs:base64Binary` | The padding bits of a short final group must be zero, so `AP8=` is a value and `AP9=` is not. |

**A number is written differently by the two languages**, and the lexical space is the reason: `Infinity` is
not in `xs:double`'s at all, so writing it produces text this engine itself refuses to read. Under 2.0 the
special values are `INF`, `-INF` and `NaN`, negative zero keeps its sign, and a magnitude outside 0.000001 to
1000000 is written in exponential notation with a mantissa between one and ten that always carries a decimal
point — `1.0E6`, not `1E+06`. An `xs:float` is written as a float: this engine holds one as a double that has
been through `float`, and asking the double for its shortest round trip answers `3.299999952316284` where the
value is 3.3.

That is the form this processor writes, and backwards compatibility does not change it (see *What backwards
compatibility restores, and what it does not*). XPath 1.0's own form — `Infinity`, one zero, and never an
exponent — is still what `ToStringValue` produces, and it is still what a value that is not a double or a
float takes, every other type having one lexical form and one route to it.

Most of these were accepted because .NET's own parsers are more generous than XML Schema:
`double.TryParse` reads `nan` and `Infinity`, `decimal.TryParse` reads `.5` and `30.`, and
`Convert.FromBase64String` discards padding bits the lexical space requires to be zero. Each would have let
this engine invent a value from text the specification says is not one.

**Untyped values become `xs:double` by a real cast**, wherever one is wanted — in arithmetic, in a general
comparison against a number, in `avg` and `min` and `max`, and in a function argument declared numeric. XPath
1.0 turns text that is not a number into NaN and carries on, so `@count + 1` over the text `three` quietly
produces NaN and prints as one; 2.0 raises `FORG0001`, on the grounds that nothing downstream will make sense
of NaN either.

One range narrower than the specification's, reported as such rather than passed off as a bad value: a
**year past 999,999,999** either side of the common era is `FODT0001`. The integers used to be a second
such range and are not any more.

Two consequences worth knowing. Under 2.0 a comparison no longer takes the node-list fast path, and the IL
backend hands comparisons, arithmetic and negation to the interpreter, because which operation a pair of
operands calls for depends on both their types. Neither affects 1.0, where the fast paths and the emitted
code are unchanged — and where `system-property('xsl:version')` still puts every stylesheet by default.

One cost that does reach 1.0: the context item being an item rather than a node widened `DynamicContext` by
24 bytes, and that struct is copied at every path step. A report-style transform of a 455 KB document went
from 5.57 ms to 5.68 ms, about 2%, measured against `reader-only` and `reference-full` as controls. It bought
`(1 to 25)[. ge 10]` meaning what it says.

### Comments

`(: like this :)`, XPath 2.0's only comment. It goes wherever whitespace goes — including between a name and
the parenthesis of its call — and it nests, so a stretch of expression that already had a comment in it can be
commented out whole. Not gated on the version: no XPath 1.0 expression contains `(:`, a colon being unable to
follow a parenthesis, so reading it as a comment takes nothing away from 1.0.

Every failure the scanner reports now carries `XPST0003`. Each of them is the same kind — text that no
expression could contain — and the grammar is what refuses it, so there was never a second kind to
distinguish it from.

### Wildcards in a name test

Both halves of XPath 2.0's pair. `prefix:*` is every name in one namespace; **`*:local`** is that name in
whatever namespace, which is the useful half in practice — it is how a stylesheet reaches into a document
whose namespace it does not know, or would rather not declare a prefix for. Unlike a bare name, which is in
*no* namespace, `*:local` matches whatever namespace the node is in, including none.

`*` is still multiplication where an operand precedes it: a colon cannot follow a number, so the two readings
never compete.

### Regular expressions

XPath's regular expressions are XML Schema's with a few additions, and .NET's are Perl's. The two agree on
most of their syntax, which is what makes the disagreements worth listing: .NET's language is the larger, so
a pattern XPath has no reading for is one .NET will happily compile into something else. Each of these was a
pattern that worked here and would have failed, or answered differently, on a conformant processor.

| | |
|---|---|
| **Back-references** | `\N` names a group that is *complete to its left*. `(a\1)` refers to the group it is inside and `(a)\2(b)` to one that has captured nothing yet; both are `FORX0002`, where .NET accepts them. A back-reference in a character class is refused outright — `[\1]` holds characters, and .NET reads the `\1` there as something else entirely. |
| **Digit runs** | `\11` is the eleventh group where there are eleven, and the first followed by a literal `1` where there is one; the same rule governs `$15` in a replacement. .NET reads the longest run either way, so it takes a group that is not there. |
| **Anchors** | .NET's `$` also matches before a newline that ends the string, so `matches('Mary&#10;', 'Mary$')` is true there and false here. Without `m` both anchors mean the two ends of the string exactly. With `m`, a newline that ends the string does not begin a line after it, where .NET's `^` matches there too. |
| **`.`** | One character, and so a surrogate pair where the subject holds one. Excludes the carriage return as well as the newline, unless the `s` flag says otherwise. |
| **`i` and categories** | `\p{Lu}` is the uppercase letters whether or not case is being ignored. .NET folds it, which makes `matches('m', '\p{Lu}', 'i')` true and its negation false — both inverted. |
| **`\s`** | The four characters Schema names — space, tab, newline, carriage return. .NET's is every Unicode separator and the form feed and vertical tab besides, so `matches('&#160;', '\s')` is true there and false here. |
| **`\w`** | Everything outside the categories `P`, `Z` and `C`, which puts the underscore *outside* it and the symbols inside. .NET's is `[\p{L}\p{Mn}\p{Nd}\p{Pc}]`, which does the opposite on both counts. |
| **Subtraction** | Takes from the group as written, negation included: `[^cde-[ag]]` is everything but `c`, `d` and `e`, and then without `a` and `g`. .NET reads the same text as the negation of what the subtraction leaves, which puts `a` back in. |
| **`x`** | Removes whitespace everywhere outside a character class, escaped or not: `hello\ sworld` becomes `hello\sworld`. .NET's own flag is a different rule — it treats `#` as starting a comment, and leaves whitespace inside `\p{ IsBasicLatin }`. |
| **Hyphens** | A hyphen in a character class is part of a range or the start of a subtraction. The one in `[0-9-.]` is neither, and is refused — from 3.0, whose grammar is XSD 1.1's. A 2.0 processor reads by XSD 1.0's, which let a hyphen stand for itself anywhere: `[a-a-x-x]` is two ranges with a hyphen between (see *What a 2.0 regular expression reads by*). |
| **Replacements** | `$N` names a group and a backslash may precede only a backslash or a dollar. Everything else is `FORX0004`, where .NET would read `$y` as a literal and `\1` as the digit 1. |
| **Zero-width patterns** | `replace`, `tokenize`, `fn:analyze-string` and — below 3.0 — `xsl:analyze-string` refuse a pattern that matches a zero-length string: there would be a match at every position and between every pair of characters. XSLT 3.0 admits one on `xsl:analyze-string` and says what each such match is (see *What a date is written as, and what a zero-length match is*). `matches` is content to say that `matches('a', '')` is true. |
| **`tokenize`** | Gives what lies between the matches and nothing else. .NET's `Regex.Split` also returns whatever the pattern's groups captured, so the things split on came back among the pieces split into. A zero-length input gives no tokens rather than one empty one. |
| **`(?…)`** | `(?:…)` is in the language **from 3.0** and nothing else in the family is, at any version. Lookahead and lookbehind, atomic groups, named groups, inline options and comments are all .NET's. Below 3.0 `(?:` is refused too, which is what 2.0 says. |
| **Escapes** | The metacharacters so each can mean itself, `\n`, `\r`, `\t`, the multi-character escapes `\s \S \i \I \c \C \d \D \w \W`, the categories `\p{…}` and `\P{…}`, and the back-references. Nothing else: `\b` and `\B`, `\A`, `\Z`, `\z`, `\G`, `\k`, and the character escapes `\a \e \f \v \xNN \uNNNN \cX` all mean something in .NET and nothing here. |

The last two are a deliberate narrowing. A pattern using `\b` worked here and would fail on a conformant
processor, which is the quietest way for a stylesheet to be wrong — but a stylesheet using one and working
today stops working. No test in the suite moved either way when they were added, in either direction: nothing
there uses these constructs expecting them to work, and nothing asserts an error for them. The tests in
`RegexTests` are what holds them.

**XPath 3.0 adds exactly two things to this language**: the non-capturing group `(?:…)` and the `q` flag,
which makes the whole pattern mean the characters it is written with. Both are refused below 3.0, for the
reason above in the other direction. The non-capturing group takes no number, which is what the groups after
it depend on: `\1`, `$1` in a replacement, and the `nr` attribute `analyze-string` reports all have to name
the group a conformant processor would, and .NET numbers them the same way only because this engine counts
them the same way.

That one construct was worth 629 tests. `fn/matches.re.xml` is 1,012 cases generated from the Microsoft
regular-expression suite, and every one of them is written with `(?:…)` — so refusing it failed the whole set
and hid whatever else is in there.

**The class grammar is narrower than .NET's**, in four ways the suite named. A `[` inside a character class is
the character itself and must be written `\[`, so `[[abcd]-[bc]]`, `[a[:xyz:]` and `([[:]+)` name no class here
even though .NET reads all three. A `]` outside a class closes one that was never opened, and a class holds at
least one character, so `a]`, `[]]` and `[^]b]` are all refused where POSIX and .NET read them. A subtraction
is the last thing in the class containing it. And an unescaped `{` begins a quantifier and nothing else, where
.NET reads `a{,2}` and `{5` as the characters they are written with.

**A character is a code point, where .NET's is a UTF-16 unit.** This is the largest difference between the two
languages and the one everything below is a consequence of. Above the basic plane a character is written as
two units, so .NET's `.` matches half of one, its `[^a]` matches half of one, and its `\p{Lu}` matches neither
half — both are surrogates, and a surrogate is not a letter.

Everything that names a *set* of characters is therefore worked out here as a set of code points and written
again as the pairs that spell it: character classes, the category escapes, the name escapes, and the dot. A
class is parsed into ranges rather than handed across, which is what makes union, subtraction and negation
mean what XPath says they mean rather than what .NET says. Three things fall out of doing it properly:

- **A negated set never holds a lone surrogate.** `[^a]` is one character, so it matches a surrogate pair
  whole and cannot match half of one and stop there.
- **`\C`, `\D`, `\W`, `\S`, `\I` and `\P{…}` may be members of a class.** Each is the complement of a set,
  which is a set; .NET has no way to negate part of a class, so these used to be refused there.
- **`\w`, `\W` and `\s` are XPath's**, not .NET's. They were passed across as written before, and the two
  languages read all three differently — see the table above.

The one thing this does not reach is the **block names inside the basic plane**. .NET has names for those and
this engine has no ranges for them, so `\p{IsBasicLatin}` is still handed across as the text .NET reads. That
costs two things, both refused rather than answered wrongly: a subtraction cannot take a block away from
anything (`[a-z-[\p{IsBasicLatin}]]`), and a negated class naming a block cannot have anything taken from it
(`[^\p{IsBasicLatin}-[a-f]]`), because .NET would negate what the subtraction leaves where XPath takes from
what is negated. A negated block on its own works, and takes in every character above the basic plane, which
is the half .NET cannot do.

**The blocks above the basic plane** have no .NET name at all and are written out as ranges. The table is XML
Schema 1.0's own block list, which was fixed in 2001, plus the symbol and pictograph blocks Unicode has added
since — `\p{IsEmoticons}` is how a stylesheet matches an emoji and there is no other way to say it. Blocks
added since 2001 for *scripts* are not listed: a name the table does not have is refused rather than answered
wrongly.

**What the categories cost.** `\p{Lu}` and its relatives need to know which code points are in a category over
the whole of Unicode, and .NET answers that one code point at a time. The table is read out of
`CharUnicodeInfo` in one pass over all 1,114,112 code points — about 4 ms, once per process, and only for a
process whose stylesheet uses `\p`, `\d` or `\w` at all. Reading it rather than writing it down here is what
keeps the engine's idea of a category the same as the framework's, with no second Unicode version to drift.
After that a pattern using one costs about 0.5 ms more to compile than it did, once, because the pattern .NET
is given is larger; every use after the first is a cache hit. Measured, not assumed.

**The `q` flag makes the replacement literal too**, not only the pattern. That is what makes it worth having:
with it `$1` is a dollar and a one rather than a group, and a lone backslash is a backslash rather than
`FORX0004`. Without it, `fn:replace` allows `$N` and a backslash before a backslash or a dollar, and nothing
else — .NET would read `$y` as a literal and `\1` as the digit 1, quietly producing something else.

**`\i` and `\c`** stand for the characters an XML name may begin with and may hold: XML 1.0's
`NameStartChar` and `NameChar` in their fifth edition, complete, at 3.0, and the fourth edition's tables at
2.0 (see *What a 2.0 regular expression reads by*). The set had stopped at U+200D, which the suite did
move: the ohm sign and a hiragana letter are name characters and were not being.

What is left in `fn/matches.re.xml` is three, and they are the hyphen question below.

One case the suite does not settle: `[0-9-.]` is expected to be `false` by `K2-MatchesFunc-16` and to raise
`FORX0002` by `K2-MatchesFunc-16a`, which are the same expression, and `re00056`/`re00056a` and
`re00086`/`re00086a` are two more such pairs. A processor passes exactly one of each, so the score is nearly
identical either way — measured, not assumed. This engine refuses it, which is the reading XML Schema 1.0's
grammar supports; XML Schema 1.1 relaxed it, which is what `re00102` asks for and this engine does not give.

### Collations

Three are provided.

**The code point collation** is the one every processor must have, and it compares by code point rather than
by UTF-16 unit. Those disagree above the basic plane — U+10001 is written as a surrogate pair beginning at
U+D800, so comparing the units sorts it before U+FFF0, which is the wrong way round. It is also the default
where no collation is named, so this is the ordering under everything else in the language.

**The HTML ASCII case-insensitive collation** that XPath 3.1 adds folds `A-Z` onto `a-z` and nothing else. Not
the invariant culture's idea of case: `É` and `é` stay different strings under it.

**The UCA collation URI**, `http://www.w3.org/2013/collation/UCA`, asks for the Unicode Collation Algorithm
with parameters. .NET's `CompareInfo` is backed by ICU, which is an implementation of that algorithm, so what
was left was reading the parameters. Honoured: `lang`, `strength` (primary, secondary, tertiary, identical),
`caseLevel`, `normalization`, and `caseFirst=lower`, which is what ICU already does for a language with no
rule of its own. Refused: `alternate`, `backwards`, `caseFirst=upper`, `maxVariable`, `numeric`, `reorder`,
`version` and quaternary strength, none of which `CompareOptions` can express.

Refusing is not the same as failing. The specification calls this the **simple fallback**: with
`fallback=yes`, the default, a processor may ignore a parameter it does not implement, and with `fallback=no`
it must raise `FOCH0002` instead. So the same unsupported parameter is silently dropped in one URI and an
error in the other, and that difference is the whole of what `fallback` means. A language tag that is
well-formed but unknown is one gap here: .NET invents a culture for it rather than refusing, so it falls back
silently even under `fallback=no`.

A collation reaches `compare()`, `contains()`, `starts-with()`, `ends-with()`, `substring-before()`,
`substring-after()`, `distinct-values()`, `index-of()`, `deep-equal()`, `min()`, `max()`, `contains-token()`,
`fn:sort()` and `array:sort()`. The four substring functions gained the argument in 2.0 and do not take it
under 1.0, where three arguments to `contains()` is still a stylesheet's mistake. `substring-after()` resumes
from the end of what actually matched rather than from the length of what was sought, which is the only place
a collation matching four characters against five can be seen.

**`xsl:sort`, `xsl:key` and `xsl:for-each-group` refuse anything but the code point collation.** *Since
superseded: all three take any collation the engine or the caller has, `xsl:merge-key` with them, and a
caller supplies its own through `XsltOptions.CollationResolver`; what follows describes the engine before
that.* They run through comparators and tables that do not carry one, and accepting the name to then order
by code point anyway would answer a question about Danish ordering with an answer about Unicode, with
nothing in the result to say so. `xsl:sort` still takes `lang` for language ordering, which is what it is
for.

`key()` takes a third argument naming the document to search, which is what makes a key usable against a
document loaded by `document()` — under 1.0 a key could only ever look in the document the instruction was
already reading. `type-available()` answers for the built-in schema types this engine can construct.

**`xsl:output encoding` reaches the bytes only where this engine writes them**, which is where the caller
transforms to a `Stream`. To a `TextWriter` the bytes belong to whoever built that writer, so the encoding
reaches nothing but the text of the XML declaration and a result can claim `ISO-8859-1` while being UTF-8 —
there is a test asserting each half of that, since the difference is the reason the stream overloads exist.
Writing to a stream, the declaration is true, `byte-order-mark` is a mark rather than a U+FEFF character that
may or may not become one, UTF-16 carries the mark XML requires of it whatever the stylesheet says, and a
character the encoding cannot hold is written as a character reference rather than substituted with `?`. An
encoding this process does not have is refused rather than approximated; UTF-8, UTF-16, UTF-32 and
ISO-8859-1 are always present, and the legacy code pages need the application to register
`CodePagesEncodingProvider` — a library has no business doing that to a process behind its back.

`xsl:result-document` gets the same through `IXsltResultStreamResolver`, which hands back a `Stream` where
`IXsltResultResolver` hands back a `TextWriter` — and a result document may declare an `xsl:output` of its
own, so its encoding is its own to honour. The older interface is unchanged and still works, with the older
limitation; where both are configured the stream one is used, being the only one an encoding can reach the
bytes through. Neither is configured by default: adding a second way to say yes must not become a way to say
yes by default, since a stylesheet is data as often as it is code.

`xsl:output` also takes `normalization-form` for NFC, NFD, NFKC, NFKD or none — `fully-normalized` is refused
rather than approximated with a different form. `undeclare-prefixes` has no effect on XML 1.0 output, which is
what this engine writes, and is accepted as the specification says to. `escape-uri-attributes` and
`include-content-type`, both HTML-method refinements, were not implemented when this was written; both are
read and honoured now, by the HTML and XHTML methods alike.

Not implemented: the schema-aware forms of everything. The list types (`xs:NMTOKENS` and its relatives) were
absent when this was written and are here now, with their constructor functions and casts.
`xs:NOTATION`, `xs:anyAtomicType`, `xs:anySimpleType` and `xs:untyped` have no constructor function, which the
specification withholds from them — `xs:NOTATION('a:b')` is `XPST0017`, there being no function of the name,
and not `XPST0051`, which says a type is one this engine has not implemented.

`xs:error` is implemented, which for a type holding no values means implemented as a type code no value ever
carries. Everything then follows from the ordinary rules rather than from a test written for this one name:
nothing is an instance of it, nothing is castable to it, `treat as xs:error` always raises `XPDY0050`, and
`xs:error(1)` raises **`FORG0001`** — the value is outside the type's space, which is what that code says,
where `XPTY0004` would claim the two types have no route between them. The empty sequence is not a value, so
`() instance of xs:error?` is true and `() cast as xs:error?` gives back the empty sequence.

`schema-element(name)` and `schema-attribute(name)` parse and then say there is no such declaration
(`XPST0008`), which is what an undeclared name gets — not a claim that the syntax is wrong. A type after the
name in `element(name, T)` is checked for being a type and then matches nothing, no node here carrying an
annotation to compare it against.

Errors carry the code the specification names, in `XsltException.Code` — `XPTY0004` for a type error,
`FORG0001` for a value outside a type's lexical space, `FOAR0002` for numeric overflow, `XTSE1650` for a
schema import, and so on. Not every error has one yet; where this engine has not been given a code, `Code` is
null rather than a guess.

`fn:error()` carries the code a stylesheet named for itself. The argument is an `xs:QName`, and its local
part is what reaches `Code`: `error(QName('http://www.w3.org/2005/xqt-errors', 'err:FORG0001'), '…')` reports
`FORG0001`, indistinguishable from the engine raising it, which is what makes the function worth having — a
stylesheet that detects a bad value itself reports it the same way everything else does. A name in some other
namespace keeps the namespace in the message, since two stylesheets may both raise `invalid`. Calling
`error()` with no name, or with an empty one, raises `FOER0000`. This is the only code the engine does not
choose, and the only one built from a string rather than from the `XsltErrorCode` enumeration.

Conformance against the W3C QT3 suite is measured by `CodeDeeds.Xslt.Conformance`; see its README. The
XPath 2.0-applicable subset currently passes **99.0%** of 14,175 tests: 100% of the worked examples, 99.1% of
the grammar productions, 99.3% of the function library, 98.5% of the operator tests and 92.2% of the type
constructors.

The type errors and lexical spaces above are most of the distance from the 77.9% this said before them, and
the casting table alone was 6 points of it. What remains, in order of size:

**Negative years** were the largest cluster here until the year stopped being a `System.DateTime`'s.
A value is now held as a *proxy* date whose year stands in for the real one, the real year being a whole
number of 400-year cycles away. The proleptic Gregorian calendar repeats exactly over 400 years — the
leap rule does, and so does the day of the week, 146,097 days being divisible by seven — so the proxy
carries the value's month, day, time of day, day of week and day of year, and only the year is asked for
separately. Everything that reads those fields goes on reading a `DateTime`; what changed is the handful
of places that read the year, compare two values, or move one by a duration.

Inside, years are counted **continuously**: 0 is 1 BCE and −1 is 2 BCE, so the timeline has no gap and
arithmetic across the era boundary is ordinary arithmetic. XML Schema 1.0 spells the same years with no
zero, 1 BCE being `-0001`, and that spelling goes on and comes off at the lexical edge alone:
`year-from-date(xs:date('-0002-06-01'))` is −2, and `xs:date('0001-01-01') - xs:date('-0001-12-31')` is
one day rather than the year and a day a gap at zero would have made of it.

One consequence worth naming, because it is a change and no test in either suite covers it: the leap rule
applies to the year counted continuously and not to the digits written. `-0001-02-29` is a date, 1 BCE
being year zero and zero divisible by 400; `-0004-02-29` is not, 4 BCE being year −3. That is the
proleptic Gregorian reading of XML Schema 1.0's own convention, where reading the digits would have made
4 BCE leap for looking like 4 CE.

**`op/to` has nothing left in it.** Half of it was `xs:integer` being sixty-four bits, and the rest was a
range being built as an array of items: four tests asking for between a million and five hundred
million of them while only wanting to know whether one number was among them. A range is now held as
where it starts and how long it is, so that question costs nothing, and so does counting one.

The rest is a long tail with no one cause behind it, `fn/doc` and `fn/subsequence` at 5 apiece being the
largest of it.

### Where an XPath function is built

**`fn:id`, `fn:idref` and `fn:element-with-id` are XPath's own functions, and were reachable only through
XSLT.** All three were built by the stylesheet compiler, along with `key()` and `document()` and the rest of
what XSLT adds — but unlike those they need nothing that was in scope where they were written, only a node
to start from and the IDs of the document it is in. The consequence was that an expression evaluated outside
a stylesheet could not call one at all: `id((), ())` answered `XPST0017`, *no such function*, where the
specification asks for a type error about the argument. Twelve QT3 tests on each run said so, and the XSLT
suite could not have: every expression it evaluates is written in a stylesheet.

They are built in `FunctionLibrary` now, with the rest of the core library, and the compiler falls through to
it. That is the same lesson as the two libraries `function-available()` was never asking (see *Which functions
this engine says it has*) — a function this engine has, unreachable down one of the two paths that build one
— and it is worth stating as a rule: what belongs in the compiler is what is built against the stylesheet,
not what merely happened to be written there first.

Two type rules came with them. The second argument names the document to search and is declared `node()`,
which is one node: an empty sequence is `XPTY0004` rather than a search of nothing that finds nothing. And
the one-argument form reads the context item, which has to be a node — `XPTY0004` where it is an atomic
value, `XPDY0002` where there is no focus at all, which `DynamicContext.RequireContextNode` already told
apart and nothing had been asking.

`fn/id` and `fn/idref` are empty on both QT3 runs.

That figure once went *down* — from 51.7% to 41.7% when error codes were introduced — and the fall was the
point. A test expecting a particular error used to pass if any error was raised at all; it passes now only
when the code matches, and is skipped where this engine raises the error without a code. Around 1,500 tests
moved from passing to skipped, which means the earlier figure was that much of an overstatement.

### Measuring the instructions

QT3 measures expressions. It says nothing at all about template dispatch, modes, grouping, sorting, secondary
results or serialization, and `System.Xml.Xsl.XslCompiledTransform` — the differential oracle the 1.0 work was
built against — can only speak for the 1.0 subset. So every XSLT 2.0 instruction here was written against the
specification with nothing to check it. That gap is now closed by the
[W3C XSLT 3.0 suite](https://github.com/w3c/xslt30-test), which the same driver runs under `--xslt`.

The XSLT 2.0-applicable subset stands at **5,288 of 5,319, 99.4%**, from 67.7% of 3,782 tests when this
section was written. Where the QT3 figure is a long tail, this one has causes behind it, and the largest are
named in the driver's README rather than here, because they are a work queue rather than a compatibility
statement.

Both halves of that fraction moved, and the denominator is the more interesting half, because what it used to
leave out was the driver's limit rather than the engine's and is not a limit any more. **A transformation may
begin at a named template** — XSLT 3.0's second way in, which this section once said the engine's public
entry point did not offer. It does, and so does the driver, so the tests written that way are run rather than
skipped; that is where a good part of the extra 1,537 in the denominator came from (see *The other ways into
a transformation*).

The third way in is still missing and it is the small one — **38 tests start at a named function**. What the
2.0 figure leaves out beyond that is not a limit at all: **6,518 are XSLT 3.0 tests**, read only under
`--xslt --30`.

The first thing the suite found was a stylesheet the engine could not survive: a key whose `use` expression
calls `key()` on the key being built sent it into recursion that no call-depth limit sees, because the
recursion is not through a template. It ended as a stack overflow, which cannot be caught, so it took the
process with it. A key being indexed is now remembered, and a definition that reaches itself — directly, or
through a second key that reaches back — raises **`XTDE0640`**, which is what the specification names it.

### A select expression yields a sequence

The second thing it found was the value model, half-changed. XSLT 2.0's `select` yields a **sequence**, and
only some sequences are node-sets — but `xsl:for-each`, `xsl:apply-templates`, `xsl:perform-sort`,
`xsl:for-each-group`, `xsl:number/@select` and `key()` all demanded the 1.0 kind and refused everything else
with *a node-set was required*. `select="(a, b)"` was an error where `select="a | b"` was not, and
`select="1 to 5"` was an error outright.

They take a sequence now. Three things follow, and only the first is obvious:

- **`xsl:for-each` iterates items**, so the context item may be an atomic value: `select="1 to 5"` and
  `select="('a','b')"` both work, and `position()` and `last()` count the sequence.
- **Order and duplicates survive.** `(b, a, a)` is three items in that order, where the node-set the
  instruction used to demand would have sorted them into document order and dropped the repeat. Only a path
  result is in document order.
- **`xsl:apply-templates` still wants nodes** — there is no template to match against the number 7 — but a
  sequence of nodes is nodes. A sequence holding anything else raises **`XTTE0520`**.

The conversion is written once, in `NodeSet.Of`, rather than at each of those places. Having it at each of
them is how one of them came to take only the first shape.

One limit remains, and it is narrow: **adjacent atomic values written by separate instructions are not
separated by a space** — the rule for constructing simple content — so `xsl:perform-sort` over numbers writes
them run together. It is visible in the suite and is not a value-model question any more. (`xsl:for-each-group`
grouped nodes only for a long while, a group being a list of node ids in a tree; it groups any sequence now — see
*What the json and adaptive methods write, and what a group holds*.)

### A pattern is not a smaller language than a path

The third thing was a second parser. A pattern's node test is the same production as a path's, and this
engine had a shortened copy of the path parser to read it — shortened by exactly the kind tests. So
`match="element()"`, `match="document-node()/doc/element(a)"` and `match="*:a"` were all refused, while a
`select` two lines away read every one of them. The copy is gone; a pattern step now reads its node test with
the code an expression uses, and `processing-instruction(go)` works as well as
`processing-instruction('go')`, which is what the grammar has said since XPath 1.0.

A pattern may also be **anchored on a key**, which has been in the grammar since XSLT 1.0 and had never been
parsed here: `match="key('k', 'a')"` matches the nodes that key associates with that value, and steps may
hang below it — `key('k','a')/title`, `key('k','a')//item`. The key is looked up from the root of the tree
the candidate is in, never from wherever the transformation has got to, because a pattern says which nodes it
describes and that cannot depend on when it is asked. For the same reason the value must be **written out** —
a literal or a variable reference — so `key('k', concat('a','b'))` is refused rather than computed.

A pattern may be anchored on `id()` as on a key, its argument held to the same grammar, and the elements it
selects are those an `xml:id` or an attribute the document's type declaration types `ID` names (see *What a
document type declaration says*). XSLT 3.0's second argument, the document searched, is a variable reference,
and is `XTSE0340` on a 2.0 run. It was refused outright while no declaration was read.

A `match` attribute that does not fit the pattern grammar now raises **`XTSE0340`** rather than XPath's
`XPST0003`. The part of a pattern that is an expression is read by the expression parser, so a step it cannot
read comes back as a syntax error in XPath; written in a `match` attribute it is not that.

### The other ways into a transformation

XSLT 3.0 §2.3 gives three ways to start one: apply templates to a source document, call a named template,
call a named function. Two of them are here now.

```csharp
Xslt stylesheet = new Xslt(text, new XsltOptions { InitialTemplate = "go" });
string result = stylesheet.Transform();
```

`Xslt.Transform()` runs a stylesheet with **nothing to transform** — for a stylesheet that generates rather
than transforms, where there is no input to match against and so nothing for a pattern to be about. There is
then no context item **at all**, which is not the same as an empty one: reading it is an error, and a good
many stylesheets are written to rely on that. A source document supplied alongside a named template *is* the
context item, a caller supplying one having supplied something for the template to read.

`XsltOptions.InitialMode` names the mode to start in. A stylesheet whose rules are all in a mode has no
unnamed rules to start from, and without it the transformation falls through to the built-in rules and
produces the document's text.

Both are named as a local name or as `{uri}local`, the notation `Parameters` already used and for the same
reason: the prefixes in scope belong to the stylesheet and the caller has no way to know them. So XSLT's own
entry point is `{http://www.w3.org/1999/XSL/Transform}initial-template`. A name that is not declared raises
**`XTDE0040`** for a template and **`XTDE0045`** for a mode.

The third way — starting at a named function — is not implemented.

### A newline at the end of a file was in the result

The largest single fault the suite found, and it hid behind two hundred different-looking failures. XML's
grammar allows comments and processing instructions around the document element and **no character data at
all**, so the newline a text editor leaves at the end of a file is not part of the document. The tree builder
kept it, making a document node with a text child — and the built-in template rule then copied it out. Any
stylesheet transforming a file that ended in a newline put one in its result.

It moved **209 tests** in the XSLT suite and one in QT3. Worth stating plainly because nothing about it was
visible from inside: every affected test failed for a reason of its own, and the pattern only appeared once
the results were compared character by character.

### What a declared type settles

An `as` declaration decides what a sequence constructor's content amounts to as well as checking it, and
three things about that were wrong.

**A global's `as` was read and then dropped**, so a global variable was whatever its content happened to
make — three integers arriving as the text of three. It reaches the compiled stylesheet now, and applies to
a value the *caller* supplied for a parameter as much as to a default the stylesheet wrote.

**A sequence may hold an attribute with no element to belong to.** That is the whole reason for declaring
`as="attribute()"`: the attribute *is* the value rather than something to attach. It used to be dropped,
leaving an empty sequence where the stylesheet had plainly produced something. The data model holds one now,
with its owner recorded as none, so `$a/..` is empty as the specification says. Without a declared type the
content is still built into a document node, which carries no attributes, and putting one there is still an
error.

**`xsl:document` contributes the document node it makes**, where the target holds a sequence. It used to copy
the content out, which contributes the children and loses the one node the instruction exists to make.

A type error against a declared type now carries the code XSLT gives it rather than XPath's `XPTY0004`:
**`XTTE0570`** for a variable or parameter, **`XTTE0590`** for a value supplied for one, **`XTTE0780`** for a
function's result and **`XTTE0790`** for its argument. XPath has one code for a type error and XSLT has a
different one for each place an `as` may be written.

One conversion was working backwards. An untyped value takes the type an `as` declared, and the type to
convert towards was being worked back from the *representation* — but five Gregorian types share one
representation, so `<xsl:variable as="xs:gYear">2004</xsl:variable>` converted to none of them. The type
written is carried now.

### What an XSLT element is allowed to be

A stylesheet is now checked against the specification's own table of elements, in
`Compiler/XsltElements.cs`. **Nothing here changes what a correct stylesheet does.** What it changes is what
an incorrect one hears: a misspelled attribute used to be read as absent and the stylesheet compiled around
the hole, so `<xsl:sort ordr="descending"/>` sorted ascending and said nothing at all.

Three codes for the three ways of being wrong:

- **`XTSE0090`** — an attribute the specification does not define for that element. This also catches the
  standard attributes written with the prefix: `version`, `use-when`, `xpath-default-namespace` and their
  three companions take `xsl:` on a *literal result element* and never on an XSLT element, so
  `xsl:xpath-default-namespace` on `xsl:value-of` is an error rather than a long-winded way of writing the
  same thing. An attribute in any other namespace is allowed and ignored, which is how a processor's own
  extensions are written.
- **`XTSE0020`** — a value outside what the attribute may take. The yes-or-no attributes and the
  enumerations (`order`, `level`, `case-order`, `letter-value`, `validation`, `standalone`) are checked, and
  so are the attributes that must hold a QName *written out*: `<xsl:variable name="x/y">` and
  `<xsl:decimal-format name="{concat('f','f')}">` are both refused, where `<xsl:element name="{$x}">`
  computes its name and is not. A value holding a curly brace is left for the run to decide wherever the
  attribute is an attribute value template. These values are token-typed, so `" no "` is `no`.
- **`XTSE0010`** — an element in a place it may not stand, without an attribute it requires, or holding
  content it may not hold. `xsl:apply-imports` at the top level, `xsl:key` inside a template, `xsl:for-each`
  with no `select`, text inside `xsl:choose`, an `xsl:fallback` inside `xsl:apply-imports`, and an
  `xsl:stylesheet` with no `version`. Two ordering rules come under it as well: an `xsl:param` after the
  first instruction of a template is a parameter that was never declared, and an `xsl:sort` after the first
  instruction of an `xsl:for-each` is a sort that never happened. Both used to be skipped where they stood.

An element or attribute this engine has never heard of is **not** refused under forwards-compatible
processing. There the stylesheet was written for a later version of XSLT, where it may be perfectly ordinary,
and refusing it would be refusing the stylesheet for being newer. The reverse also holds: an XSLT 3.0
instruction in a `version="2.0"` stylesheet — `xsl:iterate`, `xsl:evaluate` — is `XTSE0010`, which is what a
2.0 processor is required to say about it.

Checking costs nothing measurable: compiling the 41 KB stylesheet in `decomp` moved from 2.87 ms to 2.86 ms,
which is noise.

### The XHTML output method

`method="xhtml"` is implemented. The whole of its difference from `xml` is which empty elements collapse: a
browser reading `<p/>` as HTML sees an unclosed paragraph, and one reading `<br></br>` sees two line breaks.
So the thirteen elements HTML 4 calls empty — `area`, `base`, `basefont`, `br`, `col`, `frame`, `hr`, `img`,
`input`, `isindex`, `link`, `meta`, `param` — are written `<br />`, with the space old parsers needed, and
every other element gets both tags. Deliberately not the HTML 5 additions the `html` method also knows: the
XHTML method is defined against a fixed list.

The rules apply **only in the XHTML namespace**. An element outside it is serialized as XML, so an empty one
in no namespace is still `<x/>`.

Three things came out of implementing it, none of them about XHTML:

- **The `xml` prefix was being declared.** `<out xml:lang="en">` came out carrying
  `xmlns:xml="http://www.w3.org/XML/1998/namespace"`, which is not a redundant declaration but an ill-formed
  document — the prefix is bound everywhere by definition and may never be declared.
- **`cdata-section-elements` names an *element*, so the default namespace applies to an unprefixed name** —
  unlike the QName of a template or a variable, where it deliberately does not. A stylesheet declaring a
  default namespace and asking for `cdata-section-elements="note"` means the `note` it is producing, and
  reading the name as no-namespace matched nothing at all. `XslCompiledTransform` agrees, and now so does
  this. `xsl:result-document` reads the attribute too, where it had been ignoring it.
- **`include-content-type`** is implemented, for both the HTML and the XHTML methods: a
  `<meta http-equiv="Content-Type">` goes first in the `head`, since a page saved to disk arrives with no
  header to say what encoding it is in. `include-content-type="no"` turns it off.

### `xml:base` and where a relative reference points

`xml:base` is how a document assembled from several places says where each part came from, so a relative
reference inside a part resolves against where *that part* was rather than against the file it now lives in.
It was not read at all: `base-uri()` answered the document's own URI wherever it was asked.

- **`base-uri()`** collects the `xml:base` declarations from the node outwards and resolves them against one
  another, ending at the document's URI. An attribute, text, comment or processing-instruction node with **no
  parent** is relative to nothing and answers the empty sequence — it never belonged to a document, and there
  is no element above it to ask.
- **`document-uri()`** is unmoved by any of this. It is where the document came from and nothing else, which
  is why a tree the stylesheet built has none.
- **The static base URI** — what `fn:static-base-uri()` reports and what a one-argument `fn:resolve-uri()`
  and `document()` resolve against — is the stylesheet's URI as `xml:base` in the stylesheet has moved it.
  Static, so it is settled where the expression is compiled; that is also what makes it answerable from a
  `use-when`.
- **A node in a result tree** takes its base URI from the declaration whose content built it — the
  `xsl:variable`, `xsl:param`, `xsl:with-param`, `xsl:document` or `xsl:function`, as `xml:base` on it or
  above it has moved that — since that is where it was written. A relative reference put into a result is
  relative to the stylesheet rather than to wherever the result is later saved. A parentless copy keeps the
  base URI of what it copies. See *What a built tree is relative to, and which mode is a way in*.

### `xpath-default-namespace`

XPath puts an unprefixed name in **no namespace** whatever `xmlns` says. That is right, and it is also the
thing everyone trips over — a stylesheet reaching into a namespaced document has to declare a prefix and use
it in every path. `xpath-default-namespace` is how a stylesheet asks for a default anyway, and it was
accepted and ignored.

It sets the *default element/type namespace*, which reaches exactly two things: an unprefixed **element**
name in a path or a pattern, and an unprefixed **type** name. Not an attribute — the attribute axis has no
default namespace, so `@n` still means the no-namespace attribute an unprefixed attribute in the document
actually is. Not a variable, a function or a processing-instruction target either. The nearest declaration in
scope wins, and an empty value puts names back in no namespace, which is how a stylesheet turns it off for
one subtree.

A type name had been resolving through the *document's* default namespace, since that is what asking a
stylesheet to resolve the empty prefix answers. Those are two different defaults and only one of them is
XPath's.

### `use-when`

Answered as the stylesheet is compiled, and a false answer takes the element and everything in it out of the
**stylesheet** — not out of the result. What is inside is therefore never compiled and may be anything at
all: syntax from a later version, an extension element this processor has never heard of, or an outright
mistake. That is the point of the attribute, which exists so that one stylesheet can be written for several
processors and each read only the part meant for it. On a literal result element it is spelled
`xsl:use-when`.

An `xsl:include` it excludes is never followed, and a template it excludes is never named — the question is
asked at the first sight of a declaration, before anything else is done with it.

**Nothing the stylesheet declares is in scope inside one.** A `use-when` decides whether a piece of the
stylesheet exists, and it is answered before the rest of the stylesheet has been read, so a variable or a
function it named could itself be inside a piece another `use-when` removed. A variable reference is
`XPST0008` and a call to a stylesheet or extension function is `XPST0017` — there is no "only if it is
reached" to fall back on here, since it is being evaluated now. There is no context item either, and reading
one is `XPDY0002`.

On the `xsl:stylesheet` element itself the attribute cannot remove the element — there would be no
stylesheet left to have written it — so what it removes is everything inside, leaving a stylesheet with no
rules.

Two things it needs came out of implementing it. `fn:system-property('xsl:version')` is a **string** from
2.0, where XSLT 1.0 made it a number, so a stylesheet compares the answer the way its own version says to;
and `fn:function-available()` knows the XPath 2.0 library, `fn:doc` and `doc` naming the same function
because the core library lives in the namespace an unprefixed call already means. `fn:static-base-uri()` is
folded where the expression is compiled rather than read from the transformation, which is what its name
says it is and what makes it answerable from a `use-when`.

### What the processor says it is

The whole set of system properties XSLT 3.0 defines, and four of them are this engine's omissions written
where a stylesheet can read them:

| property | answer | |
|---|---|---|
| `xsl:version` | `2.0`, or `3.0` where the caller asked for 3.0 | The version the *processor* implements, which is the one the caller chose (see *What a processor asked to be 3.0 says it is*). A number under 1.0's rules and a string from 2.0: the only one whose *type* depends on the version in force, which is a different question from what it answers. |
| `xsl:xpath-version` | `3.1` | See below. |
| `xsl:xsd-version` | `1.0` | |
| `xsl:vendor` | `CodeDeeds` | |
| `xsl:product-name` | `CodeDeeds.Xslt` | |
| `xsl:vendor-url`, `xsl:product-version` | *empty* | The specified answer for a property the processor does not provide. Neither an address nor a version has been settled on, and inventing one to satisfy a test would be worse than answering honestly. |
| `xsl:supports-serialization` | `yes` | |
| `xsl:supports-backwards-compatibility` | `yes` | |
| `xsl:supports-higher-order-functions` | `yes` | |
| `xsl:is-schema-aware` | `no` | Nothing here validates. |
| `xsl:supports-streaming` | `no` | |
| `xsl:supports-dynamic-evaluation` | `yes` | `no` when the caller set `XsltOptions.DynamicEvaluation` to false; see *What a target expression may see*. |
| `xsl:supports-namespace-axis` | `no` | |

Saying `no` is the point of the function rather than a concession: a stylesheet that asks can take another
route, and one told `yes` and then failed has been misled. A name the processor does not recognise is an
empty string and not an error, for the same reason.

**`xsl:xpath-version` is `3.1`**, and it is one number that cannot express a partial answer. Maps, arrays,
`||`, `!`, `fn:sort` and `fn:apply` are all 3.1 and all here, so 3.0 would disown a good deal that works;
the question is which number misleads less. Where it cannot be precise, `function-available()` can — it
answers for one name and one arity — and that is the division of labour between a coarse probe and a fine
one.

**`supports-higher-order-functions` obliges the library to be there.** The suite checks the pair directly:
reporting `yes` and then not answering `function-available('filter', 2)` is a contradiction a test is written
to catch, so the higher-order names are in the availability union at the arities the specification gives
them.

### Where `xsl:result-document` writes

**An `xsl:result-document` with no `href` — or an empty one — writes the *principal* result.** The `href` is
resolved against the base output URI, and with nothing to resolve it *is* the base output URI, which is where
the transformation's own result goes. It had been going to the resolver under an empty name, which meant a
stylesheet whose whole body was wrapped in one produced nothing at all. No resolver is involved on that path
and none is needed: nothing is being created outside the transformation, so the posture that makes a
secondary result opt-in does not apply.

The serialization the instruction asked for still applies to what it writes, so
`<xsl:result-document method="html">` writes HTML to the principal destination. That is a second writer over
the same destination, which is safe precisely because only one of the two may ever write:

**Two final result trees may not share a URI — `XTDE1490`.** What is written outside any
`xsl:result-document` goes to the base output URI too, so doing both is the error, in either order. Writing
one `href` twice is the same error and now carries the same code.

**`xsl:output` may be named, and `xsl:result-document` may name it as its `format`.** A named output
definition stands on its own: it neither starts from what the unnamed one settled nor contributes to it —
which had been the bug, a named `method="text"` quietly turning the principal result into text. Several
declarations may share a name and are merged. A `format` naming no declaration is **`XTDE1460`**.

Named definitions are read where they are *used* rather than where they stand, because one may name a
character map declared further down the stylesheet. The checking is not deferred with them: a `method` that
is not a name an output method could have is **`XTSE1570`** wherever it is written, reachable or not.

`standalone` has three values rather than two — `omit` leaves the declaration without a `standalone` at all,
which is not the same as saying the document is not standalone.

Missing here when this was written, and since done: the serialization attributes of `xsl:result-document`,
`format` included, are attribute value templates and are read as such (see *Two more things a result document
settles*).

### A pattern's own axis, and where a focus stops

Three things about *which node is being talked about* were wrong, and each of them showed up as an answer
that looked nothing like the question.

**A pattern's innermost step is reached down the child axis, and the child axis holds neither an attribute
nor the document node.** So `match="node()"` matches every node *except* those two — XSLT 1.0 §5.2 says so
outright. It had been matching both, which meant a stylesheet with a `node()` catch-all took the document
node away from the built-in rule: `apply-templates` never reached the document element and the result was
whatever the catch-all wrote, with no outermost element at all. The one kind test that names the document
node deliberately — `document-node()`, which has a default priority of its own — is of course still
reachable, and so is `document-node()/doc/element(a)`, where the document node is an *outer* step rather
than the node being matched.

**The axes a pattern may name.** XSLT 1.0 allows `child::` and `attribute::` and nothing else; XSLT 3.0 adds
`self::`, `descendant::` and `descendant-or-self::`, which is what a pattern can afford because all three
still reach *downwards* from an anchor. Matching is bottom-up here — the node is tested first and the steps
to its left are then sought above it — so each step records not the axis but where the step to its left has
to hold: the parent, the very same node, or any ancestor of either. The connector and the axis fold into that
one answer, because `a/descendant::b` and `a//b` are the same statement and should reach the same matcher.
`ancestor::`, `following-sibling::` and the rest are refused; `namespace::` is open to a 3.0 pattern, as the
specification has it, now that the axis exists here (see *What a namespace node is, and what a bare dot is
worth*).

**A positional predicate counts within what the anchor selects, so it cannot be asked before the anchor is
known.** `chapter/descendant::foo[2]` is "the second `foo` under the chapter", and the chapter is not reached
until the search upwards finds one — so the predicates are evaluated inside that search and not before it.
The trap is the pair that look alike: `chapter//footnote[1]` expands to
`chapter/descendant-or-self::node()/child::footnote[1]`, where the predicate's anchor is the node that
intervening step introduced — the footnote's own parent — so it means "first among its siblings", not "first
under the chapter". Written as `descendant::` there is no intervening step and the anchor really is the
chapter. Reading the two the same way is what broke a pattern that had worked for years.

**The shapes that cannot be walked upwards are matched by selecting instead.** A pattern rooted on a variable
(`$v/baz`), a parenthesised expression standing where a step would (`(foo|baz)[*]`, `(doc/descendant::foo)[2]`),
`intersect` and `except` between two paths, and `doc()` or `root()` as the thing the pattern hangs from — none
of these has a "left" for the bottom-up matcher to look at. For those the definition is taken literally: a node
matches where the pattern, read as an ordinary expression, selects it. The pattern text is already a valid
expression in every one of these shapes, so it is handed to the expression parser as it stands; what has to be
added is the anchor, and a pattern that does not begin at the root is prefixed with
`descendant-or-self::node()/` — every node it could have been written relative to. That costs one evaluation
of the pattern per candidate node, which is the bargain the key anchor has always struck.

Which shapes take that route is written out rather than guessed at: it is exactly the delta between the two
grammars. Reaching for the expression parser whenever the strict one failed would have been simpler and wrong —
`ancestor::doc` would then have started matching something instead of being refused, and so would
`concat('a','b')`. One shape has to be refused by hand for the same reason: `(.[…])` reads as a perfectly good
expression, and the grammar puts the predicate pattern beside the union rather than inside it, so `.` may not
be bracketed.

**All of this is 3.0 vocabulary, and vocabulary follows the processor.** A run claiming 2.0 reads the 2.0
pattern grammar however new the stylesheet says it is: the three added axes, the selecting shapes, and `union`
spelled as a word are all `XTSE0340` there. The suite is explicit about it — half a dozen tests marked
`XSLT20` exactly exist to check that a 2.0 processor refuses `$v//baz`, `doc(…)` and `and union or`.

**`current()` in a pattern is the node being tested.** See the `xsl:number` section below; the same
reasoning applies wherever a pattern is evaluated against a candidate.

**A stylesheet function has no focus of its own.** XSLT 2.0 §10.3 makes the context item, position and size
absent inside one, and this engine was leaving the caller's in place — so a function body reading `.` got
whatever node the call site happened to be standing on, and the same arguments could give different answers
in different places. Reading the context item inside a function is `XPDY0002` now, and `xsl:copy` there
raises **`XTTE0945`** rather than a message with no code.

### What `xsl:number` counts, and what its format says

The instruction was here from the start and most of what the specification says about it was not.

**A format's punctuation belongs to the format, not to the numbers in it.** The prefix and the suffix are
written even when the list of numbers is empty, so `format="A."` over a node with no matching ancestors is
`.` and `format="(1.)"` is `(.)`. And a format made *only* of punctuation has one token that is both the
first and the last, so `format="*"` brackets: `*1*`. Getting this wrong dropped the leading punctuation from
every line of a numbered document, which was 20 tests on its own.

**`value` takes a sequence.** `value="5 to 8"` numbers four levels at once, rendered through the same format
as a `level="multiple"` count would be — `(5.6.7.8)`. Each item is atomized and rounded, and an item that is
not an integer, or is negative, is **`XTDE0980`**. Under backwards-compatible processing the old rule holds
instead: the value is converted with `number()` and whatever comes back is formatted, so `value="'fizz'"`
reads as `NaN` rather than being refused.

**`from` names where numbering restarts, not what to leave out.** The node it matches is the last one
considered *and is counted itself* where it also matches `count`, so `from="chapter" count="chapter|section"`
still contributes the chapter's own number. The same holds for `level="any"`, where the counted range starts
*at* the last matching node rather than after it. Reading `from` as an exclusion is the obvious reading and
it is the wrong one.

**`current()` in a `count` or `from` pattern is the node being tested**, not the node the instruction is
numbering. That is what makes `count="*[name()=name(current())]/*"` mean "an element whose parent shares its
name" — a pattern is asked about a node, and its own subject is the node it is asked about.

**The sequences `fn:format-integer` reads are the sequences `xsl:number` writes.** `format="w"`, `"W"` and
`"Ww"` give `twenty-one`, `TWENTY-ONE` and `Twenty-One`, and a token written in another digit family renders
in that family. The four lettered sequences and the Latin digits keep their own path here, which renders into
a caller's buffer; everything else goes to the picture reader rather than being implemented twice. Grouping
is what that route loses — `grouping-separator` applies to the Latin digits.

`ordinal` now asks the *sequence* for its ordinal form rather than suffixing the result, so `format="w"` with
`ordinal="yes"` is `third` rather than `three`. English only, at the time; German came later (see *What
language a number is spelled in*).

The errors: **`XTTE0990`** where the instruction numbers the context item and there is none or it is not a
node, **`XTTE1000`** where `select` gives other than a single node, and **`XTSE0020`** / **`XTDE0030`** for a
`lang` that is not a language code — written and computed respectively. Not having the language a valid code
names is not the same as the code being misspelt: the first is a fallback to English and the second is
refused.

### A computed name is read where it was written

`xsl:element` and `xsl:attribute` compute their names at run time, but a lexical QName means nothing on its
own: its prefix stands for whatever the namespace declarations *where it was written* say it stands for. Those
are the declarations in scope on the stylesheet element, and they now travel with the instruction from
compilation. Before this they were not consulted at all — `<xsl:element name="p:foo"/>` wrote `<foo>` in no
namespace, dropping both the prefix and the namespace it stood for.

The rule elements and attributes do not share: **an unprefixed element name takes the default namespace, an
unprefixed attribute name is in no namespace.** So `<xsl:element name="foo"/>` written under `xmlns="urn:x"`
is in `urn:x`, while `<xsl:attribute name="foo"/>` written in the same place is not. That is XML's rule rather
than XSLT's, and it is why the two are resolved separately.

A `namespace` attribute settles the namespace and leaves the prefix as no more than a suggestion; an empty one
puts the name in no namespace, where no prefix can reach it.

The errors, each named for the instruction that made it rather than for the library that noticed:

| | `xsl:element` | `xsl:attribute` |
|---|---|---|
| the name is not a lexical QName | `XTDE0820` | `XTDE0850` |
| its prefix is bound by nothing in scope | `XTDE0830` | `XTDE0860` |
| the `namespace` is one no name may go in | `XTDE0835` | `XTDE0865` |

`xsl:processing-instruction` refuses a target with a colon in it, or `xml` in any mixture of cases, with
**`XTDE0890`**. `xsl:namespace` refuses a name that is not an XML name with **`XTDE0920`**, an empty value
with **`XTDE0930`**, a value that is not a URI or is the namespace the declarations themselves belong to with
**`XTDE0905`**, and a wrong binding of `xml` or `xmlns` with **`XTDE0925`**.

The same principle settles several codes elsewhere. A collation this engine does not have is one refusal with
a code apiece for where it was written — **`XTDE1035`** on `xsl:sort`, **`XTDE1110`** on
`xsl:for-each-group`, **`XTSE1210`** on `xsl:key`. A regular expression on `xsl:analyze-string` is XPath's
language but XSLT's complaint: **`XTDE1140`** for one it cannot read, **`XTDE1145`** for a letter that is not
a flag, **`XTDE1150`** for one matching the zero-length string below 3.0. A `format-number` naming a decimal format that
is not declared is **`XTDE1280`** below 3.0 and `FODF1280` from it, the same split as an unreadable picture.
And `group-starting-with` or `group-ending-with` over something that is not a node is **`XTTE1120`** — the one
grouping form the specification requires nodes for.

**A prefix this engine invents is numbered from zero** — `ns0`, `ns1` — and skips any number a declaration in
scope has already taken, so a generated prefix never rebinds one the result is using.

### What the HTML and XHTML methods owe a browser

Seven serialization rules that exist because a browser is not an XML parser.

**`escape-uri-attributes`** is implemented, and defaults to on as the specification has it. A URI-valued
attribute — `a/@href`, `img/@src`, `form/@action`, and the rest of HTML's own list — has every character
outside #x20 to #x7E replaced by the percent-encoded UTF-8 bytes of that character. Printable ASCII is left
exactly as written, so a space, a quotation mark and an already-escaped `%20` all survive; re-escaping an
escape would change where the link points. The value is composed to normalization form C first, because a
letter with an accent can be written as one character or as two and escaping them as they came would give two
different URIs for one address.

The list is of **element/attribute pairs**, not attribute names: `script/@for` holds a URI and every other
`for` does not, and escaping an attribute that merely looks like one would corrupt it.

**A character between #x7F and #x9F is written as a character reference**, whatever the encoding can carry.
Those code points are unassigned controls in Unicode, and the characters a browser shows for them are
Windows-1252's — so a literal one means different things in different readers and a reference means one thing.

**The Content-Type meta replaces one already in the head** rather than joining it, so a page does not end up
claiming two encodings. Its media type is `text/html` for XHTML as well as for HTML: the other type an XHTML
document may be served as is `application/xhtml+xml`, but a `meta` is an HTML mechanism that a browser reading
the page as XHTML never consults.

**An unstated method is inferred as `xhtml`** when the result's document element is `html` in the XHTML
namespace, alongside the existing rule that infers `html` for `html` in no namespace. With one exception the
specification names: not for the implicit result tree of a stylesheet whose outermost element declares
`version="1.0"`. XSLT 1.0 had no XHTML method and no meta of its own to add, so a 1.0 stylesheet that happens
to produce XHTML gets the XML method it would have got then. An `xsl:result-document` is not the implicit tree
and is not covered.

### What a declared encoding now settles

**A character the declared encoding cannot carry is written as a character reference**, on every destination
rather than only where this engine owns the bytes. It used to be done by an encoder fallback, which meant a
caller transforming to a `TextWriter` — or to a string — got a result declaring an encoding its own characters
were not in. The serializer decides it now, and the fallback remains as a backstop for what the serializer
deliberately does not escape, chiefly text `disable-output-escaping` asked to be written as it stands.

Inside a **CDATA section** a character reference means nothing, so the section stops for such a character and
starts again after it — the same treatment the terminator `]]>` already gets. A **character map does not reach
into a CDATA section** at all: the two mechanisms want opposite things of the same text, one replacing
characters on the way out and the other saying this text appears exactly as it stands, and the specification
gives it to the section.

### Two more things `xsl:output` settles

**`cdata-section-elements` accumulates.** It is the one output attribute that does: every other one is a
choice, so a second `xsl:output` naming a different value overrides or conflicts, while this one is a list and
several declarations amount to their union.

**A `doctype-public` without a `doctype-system` writes no declaration** under the XML and XHTML methods, where
the external subset a declaration points at is the system identifier and there is nothing to point at without
it. The HTML method writes it, having a document type it can name on its own. An identifier holding a
quotation mark is delimited with apostrophes, neither identifier being something character references reach;
and a `doctype-public` holding a character XML does not allow in a public identifier is **`XTSE0020`**, since
it could not be written at all.

`byte-order-mark` is written ahead of whatever the result's first thing turns out to be, rather than as part
of a prolog — which the text method does not have, and which a result with no elements never reaches.

**An empty element is written `<x/>` under the xml method and `<x />` under xhtml.** Both are the same element
and the choice is the serializer's; the space is what XHTML written for browsers reading it as HTML needs, and
the xml method has no use for it. (It had been kept everywhere for one code path, which the conformance
suite's serialization tests do not allow: they compare text.)

### What backwards compatibility is, and what it is not

A stylesheet saying `version="1.0"` is being run by a 2.0 processor. XSLT 2.0 §3.9 is explicit about what
that means, and this engine had half of it: **the expressions are XPath 2.0 expressions with backwards
compatible behaviour switched on.** The grammar and the function library are 2.0's; what changes is how the
values are read.

So `1 to 5`, `()`, `(a, b)`, `instance of` and `current-date()` are all available to a 1.0 stylesheet, where
they used to be syntax errors and unknown functions. `XsltVersion.IsBackwardsCompatible` had been standing in
for two different questions — which language may be written, and which rules the values follow — and only the
second is what a `version` attribute settles.

**The first-item rule** is most of what remains. XSLT 1.0 had nothing that could carry more than one thing at
a time, so where a 2.0 expression now yields a sequence, the first item is the answer and the rest are
discarded. It applies where the specification names it, and nowhere else:

| | 1.0 | 2.0 |
|---|---|---|
| `<xsl:value-of select="1 to 5"/>` | `1` | `1 2 3 4 5` |
| `<x a="{1 to 5}"/>` | `a="1"` | `a="1 2 3 4 5"` |
| `<xsl:number value="1 to 5"/>` | `1` | `1.2.3.4.5` |
| `1 + (6 to 10)` | `7` | a type error |
| `<xsl:sort select="(., 'x')"/>` | sorts by `.` | a type error |

**A `separator` asks for the whole sequence whatever the version.** It is a 2.0 attribute, and writing one
between items a 1.0 reading would have thrown away says which reading was meant.

Two smaller things follow from the same reading. **An integer literal too large for `xs:integer` is a double
under 1.0**, which is what it was there — the specification leaves a too-large literal to the implementation,
and refusing one a 1.0 stylesheet has always been allowed to write would be the compatibility mode failing at
its one job. And **the version attribute is truncated to the hundredth rather than rounded**: `1.999999999` is
a version below 2.0 and has to stay one, where rounding makes it exactly 2.0 and switches off the very thing
it asked for.

Left as it is: `number('5.00e0')` is `NaN` here, XPath 1.0's lexical form for a number having no exponent.
XPath 2.0's `fn:number` does accept one, and a 1.0 stylesheet on a 2.0 processor is entitled to that reading —
but the conversion is reached from a hundred places that do not carry a version, and threading one through
them all would cost more than the two tests it earns.

**A built-in template rule passes on the parameters it was given**, tunnel and ordinary alike. An element the
stylesheet wrote no rule for is not a reason for a parameter to stop: what the caller meant was for the
templates it eventually reaches to have it, and an intervening wrapper is not something the stylesheet chose
to put in the way.

### What a stylesheet may not say, and what it is told

Twelve more static checks, each with the code the specification gives it. Every one of them was previously
accepted and then quietly did nothing, or did one of the two things it asked for.

| | |
|---|---|
| text between the declarations of a stylesheet | `XTSE0120` |
| an `xsl:include` or `xsl:import` below the top level | `XTSE0170`, `XTSE0190` |
| an XSLT element that must be empty, and is not | `XTSE0260` |
| a `mode` that is not a list of distinct mode names | `XTSE0550` |
| two globals of one name at one import precedence | `XTSE0630` |
| two `xsl:with-param` of one name on one call | `XTSE0670` |
| a `use-attribute-sets` naming a set nothing declares | `XTSE0710` |
| an `exclude-result-prefixes` naming an unbound prefix, or `#default` where there is none | `XTSE0808`, `XTSE0809` |
| an `extension-element-prefixes` naming an unbound prefix | `XTSE1430` |
| an `xsl:number` with both a `value` and a way of counting | `XTSE0975` |
| a `zero-digit` that is not a digit whose value is zero | `XTSE1295` |
| an `xsl:decimal-format` giving one character two roles | `XTSE1300` |

Three of these are worth a sentence each. **`exclude-result-prefixes` and `extension-element-prefixes` are
whitespace-separated**, and the commonest way to write one wrongly is with commas: `"one, two"` is two
prefixes called `one,` and `two`, neither of them bound — which used to exclude nothing at all and say
nothing about it. **`#all`** on `exclude-result-prefixes` is implemented at the same time: it stands for every
prefix in scope where the attribute is written, and had been read as a prefix of that name, so it excluded
nothing either. **A `use-attribute-sets`** is collected as it is read and checked once the whole stylesheet is
compiled, because a set may be declared after the element that draws it in, in another module, or inside a
template not yet reached.

`element-available`, `function-available`, `type-available` and `system-property` refuse a name that could not
be one — **`XTDE1440`**, **`XTDE1400`**, **`XTDE1425`**, **`XTDE1390`** — where the answer used to be "no". The
answer for a malformed name is not no; the question was malformed. It stays a *dynamic* error even for a
literal argument, which is what lets the guarded-call pattern keep compiling: a stylesheet asking about a
function on a branch nothing takes is entitled to be read.

### The current template rule

`xsl:apply-imports` and `xsl:next-match` both mean "carry on down the list of rules that matched this node",
so both need there to be such a list. **The current template rule is set by `xsl:apply-templates`,
`xsl:apply-imports` and `xsl:next-match`, and by nothing else.** It had been set by any template invocation at
all, which made it non-null in three places the specification says it is absent:

- the named template a transformation starts at;
- inside `xsl:for-each` and `xsl:for-each-group`, where the focus moves to something the rules did not choose;
- inside a stylesheet function, which has no focus of its own at all.

Either instruction in one of those is **`XTDE0560`**. `xsl:call-template` is deliberately not on that list: it
changes the current rule not at all, so a named helper called from a matched template can still say
`xsl:apply-imports` and mean the rule that reached it.

Four more refusals alongside it. **`key()` compares the whole name**, not the local part — two keys may share a
local name in different namespaces, so a prefix bound to somewhere no key is names no key, which is
**`XTDE1260`**. **Two `xsl:output` declarations of one definition at one import precedence may not give one
attribute two values** (**`XTSE1560`**): several declarations are ordinary and useful, but nothing decides
between two that disagree, and taking the later would make the result depend on the order they happened to be
written in. The two list-valued attributes are exempt, several declarations of those amounting to their union.
And a **variable, parameter or `xsl:with-param` with both a `select` and content** is **`XTSE0620`**, the check
that already covered `xsl:value-of` and its neighbours having never been applied to the bindings.

### Twenty-two more codes, and where they come from

The `misc/error` cluster of the XSLT 2.0 test suite is one test per error code. What it measures is not
whether a stylesheet runs but whether being told *why* it will not run says anything useful, and the answer
here was often a sentence with no code attached, or the right complaint under the wrong name.

Ten are content models the table of allowed elements could not state. Each is an element saying two things
where only one of them can be acted on:

| | |
|---|---|
| an `xsl:otherwise` before an `xsl:when`, or a second one | `XTSE0010` |
| an `xsl:text` holding anything but text | `XTSE0010` |
| an `xsl:template` with no `match` carrying a `mode` or a `priority`, or with neither `match` nor `name` | `XTSE0500` |
| two sibling `xsl:param` of one name | `XTSE0580` |
| an `xsl:param` of an `xsl:function` given a default value | `XTSE0760` |
| an `xsl:sort` with both a `select` and content | `XTSE1015` |
| a `stable` on an `xsl:sort` that is not the first | `XTSE1017` |
| an `xsl:perform-sort` with both a `select` and content of its own | `XTSE1040` |
| an `xsl:analyze-string` with neither substring branch | `XTSE1130` |
| an `xsl:key` with both a `use` and content, or with neither | `XTSE1205` |

`xsl:text` needed a distinction the table did not have. An element with an empty content model is *required
to be empty*, and content in one of those is `XTSE0260` — but `xsl:text` holds text and only text, so it is
not that, and a nested element in one is the general `XTSE0010`. The same distinction settles the other
direction: whitespace inside an element required to be empty is layout and says nothing, *unless*
`xml:space="preserve"` is in scope, which the specification names as its own example of content that may not
be there. A stylesheet tree keeps all its whitespace, so that attribute is what tells the two apart.

Twelve more are names, namespaces, and the declarations that give them meaning:

| | |
|---|---|
| a name declared in a namespace the specifications reserve | `XTSE0080` |
| a `version` that is not the decimal number the attribute is declared to be | `XTSE0110` |
| a `default-collation` naming no collation this engine has | `XTSE0125` |
| an element among the declarations of a stylesheet that is in no namespace | `XTSE0130` |
| a `decimal-separator` and its neighbours given more than one character, or a `mode` written as though it were an attribute value template | `XTSE0020` |
| a parameter supplied to a template that declares no parameter of that name | `XTSE0680` |
| an attribute in the XSLT namespace on a literal result element that XSLT does not define | `XTSE0805` |
| two `xsl:namespace-alias` of one precedence sending one namespace two ways | `XTSE0810` |
| `current-group()` or `current-grouping-key()` written in a pattern | `XTSE1060`, `XTSE1070` |
| an `xsl:for-each-group` with a `collation` and no grouping key to compare | `XTSE1090` |
| two `xsl:decimal-format` of one name and precedence giving one property two values | `XTSE1290` |

Four of them are worth more than a row.

**The version had to be checked before anything else was.** `version="2.0e3"` parsed as two thousand, which
this engine reads as naming an XSLT later than it implements — so the stylesheet went into
forwards-compatible processing and *every other check turned off*. A value that is not a decimal is not a
later version; it is an author who has not said what they wrote against. The check therefore lives where the
attribute is read rather than with the rest of the attributes. Reading it there also settled a smaller
confusion: `version` on `xsl:output` is the version of the *output method* — HTML 4.01, XML 1.1 — and was
being read as the version of XSLT.

**A reserved namespace has exceptions, and they are the point of it.** The XSLT namespace is reserved so that
a stylesheet cannot invent names in it, but XSLT itself puts a handful there for a stylesheet to declare —
`xsl:initial-template` above all, which is how a caller names an entry point without knowing the stylesheet's
prefixes. Refusing those would refuse the mechanism they exist for.

**A decimal format is the union of every declaration of it**, and a disagreement between two of them is an
error only if nothing overrides it. That is two reasons the check cannot be made where the declaration is
read: no single declaration is the format, and the module of higher precedence that settles a conflict may
not have been reached yet. So the properties are gathered as they are read — highest precedence per property
wins, a tie recorded rather than thrown — and the formats are built when every module is in. The same shape
as `XTSE1560` on `xsl:output`, one step further along: `xsl:output` conflicts are decided per precedence, and
a decimal format's are decided per property across all of them.

**`XTSE0680` runs in the direction the required-parameter check did not.** A call was already refused for
omitting a parameter the template requires; the opposite — supplying one the template does not declare — was
discarded in silence, and is almost always a misspelling or a call to the wrong template. It is checked with
the required ones, after every body is compiled, since neither side is known until then. XSLT 1.0 did discard
it, so a `version="1.0"` stylesheet keeps the 1.0 reading.


### What a stylesheet function is standing in, and what it is not

The dynamic half of the same cluster, and most of it comes back to one sentence in the specification: inside
the body of a stylesheet function, **there is no focus at all**. Not an empty one — none. What a function sees
has to arrive through its arguments, which is the whole reason a call to one can be lifted out of a loop,
evaluated once, or skipped.

Four things were reading a focus that is not there, each with its own code:

| | |
|---|---|
| `/` and `//`, which are the root of *the tree the context node is in* | `XPDY0002`, or `XPTY0020` where the context item is not a node |
| `current()`, which is the item being processed | `XTDE1360` |
| a two-argument `key()`, which searches the tree the context node is in | `XTDE1270` |
| `xsl:result-document`, which produces a document of the transformation's own | `XTDE1480` |

`RootExpr` had been answering `/` with the root of whatever tree the context happened to carry, which inside a
function is the source document — an answer to a question the expression had not asked. The two codes are two
different mistakes and the message says which: nothing in focus at all, or something in focus that is not a
node.

`xsl:result-document` is the same shape from the other end. It is refused wherever the output is going into a
temporary tree — a variable, a function's result, a captured sequence — because otherwise what reaches the
file system would depend on whether a variable was ever read.

**`xsl:analyze-string` was building a stand-in tree.** Inside either branch the context item is the piece of
text, and this engine had wrapped that piece in a one-text-node tree and moved the context to it, there being
no atomic context item when the instruction was written. There is one now. The difference shows exactly where
the specification says it should: a path written in a branch used to walk the stand-in tree and quietly find
nothing, where the step in fact has no node to take.

That left `current()`, which in a branch is the substring — so it is no longer always a node. It reads the
substring from the **runtime**, alongside the regex groups those same branches already read from there, rather
than from the context. That is not tidiness: `DynamicContext` is a `ref struct` copied at every focus change,
and putting a 24-byte `XPathValue` on it cost **1.7% of the whole transform**, measured interleaved against
the same commit. Off the context, the same measurement came back to 0.2% with the sign flipping between pairs.
A field that one instruction in the language ever writes is not worth widening every copy of the context by.

Five more, unrelated to each other:

- **A parameter's default and a missing one are different complaints.** A default that was *written* and does
  not fit the declared type is `XTTE0600`. A parameter with no default has the empty sequence for one, and
  where the declared type does not admit that, the specification does not call the declaration wrong — it
  says the parameter is required after all, so what is wrong is the call that left it out. That is
  **`XTDE0610`**, dynamic because whether it happens depends on the caller.
- **A sort key is one value per item** (**`XTTE1020`**), and a `group-adjacent` is one value per item
  (**`XTTE1100`**). Several values would give several orderings; none decides nothing at all. Backwards
  compatibility keeps the 1.0 reading of the first, which is to take the first item.
- **`terminate` is an attribute value template**, so what it says is not known until the message is reached.
  That makes the fixed set of values it may take a *dynamic* error there — **`XTDE0030`** — where a value
  written out is the static `XTSE0020`. It had been read at compile time, so `terminate="{$x}"` was quietly
  not "yes" and the transformation carried on past a stylesheet asking it to stop.
- **A `document()` reference with a fragment identifier** that is not a bare name is **`XTRE1160`**. A bare
  name is a shorthand pointer to the element with that ID and is followed (see *What a document type
  declaration says*); which nodes any other form selects is settled by the document's media type, which this
  engine is not told — so returning the whole document would answer a narrower question with a wider answer.
- **A document node carries neither an attribute nor a namespace node**, which is **`XTDE0420`**. An
  `xsl:copy` of a document node runs its content straight into whatever is already open, a document node
  contributing its children and nothing else wherever it is copied — so there is no reason to build one and
  copy it out again. The one thing that separates the two readings is this, and without the check the
  attribute landed on the element outside.

Four tests of that cluster are left, and none of them is a missing code:

- **`error-0047a`** asks for `XTDE0047`, both an initial mode and an initial template. This engine allows the
  combination deliberately, and the suite's own note records that the error was **removed in XSLT 3.0**
  ("error does not exist anymore in 3.0"); the test is marked `XSLT20` only. `XsltOptions.InitialMode` beside
  `InitialTemplate` is what makes `mode="#current"` in the named template mean the mode the caller asked for.
- **`error-1030a`** wants `XTDE1030`, sort key values that cannot be compared. Nothing can be uncomparable
  while every key without a `data-type` is compared as text, so this belongs with `insn/sort` and XSLT 2.0's
  rule that a sort key compares by the value's own type.
- **`error-1270b`** and **`error-1270c`** want `XTDE1270` for a context node whose root is not a document
  node. There is no such node here: a variable declared `as="element()"` still holds a node in a tree with a
  document root, this engine's result tree fragments being real trees. Parentless elements are a data-model
  change, not an error code.


## XSLT 3.0 Compatibility

Most of XSLT 3.0 is implemented, and what is not is listed under *The rest of 3.0* at the end of this
section, with the numbers. The JSON mapping came first: `Xslt.TransformJson` builds the node structure that
XSLT 3.0 defines for `json-to-xml()` — `map`, `array`, `string`, `number`, `boolean` and `null` elements in
`http://www.w3.org/2005/xpath-functions`, with `key` attributes — and then runs an ordinary transformation
over it. A stylesheet written against that structure therefore behaves the same here as on a conformant
XSLT 3.0 processor.

### Maps and arrays

The XPath 3.1 data model addition. Both are **items** — that is the whole of what they are for. A sequence
does not nest, so `(1, (2, 3))` is three items and there was no way to hold a list of lists or a value under
a key; an array is one item however many members it has, and a map one item however many entries.

An array's members are sequences rather than items, which is why `[(1, 2), 3]` has two members and
`array:get()` can hand back several items or none.

Read with the lookup operator: `$a?2` takes a member by position, `$m?name` takes an entry by key,
`$m?(expr)` computes the key, and `?*` takes every value. A lookup applies to each item of its operand and
joins the answers, so `$maps?name` reads the same entry out of every map in a sequence.

Written four ways, and the two array forms are not the same. `[1, 2, 3]` takes one expression per member, so
`[(1, 2), 3]` has two members; `array { 1, 2, 3 }` takes one sequence in total and gives a member per item,
so `array { (1, 2), 3 }` has three. `array { () }` is therefore empty where `[()]` has one member, that
member being the empty sequence. A map is written `map { 'a': 1, 'b': 2 }`, with any expression for either
half, and a key written twice is refused — `map { 1: 'x', 1.0: 'y' }` is a duplicate, which is easy to miss.

Three things had to give way for the braces. The scanner now returns a lone `:` as a token rather than
refusing it, and a colon separates a prefix only when a name follows, so `map { a: 1 }` is the element `a`
under a colon and not a name `a:` with nothing after it. And an attribute value template counts the braces
inside its expression rather than ending at the first one, so `v="{map { 'k': 1 }?k}"` closes where it
should. The first two of those turned out to be worth more than the feature: thirty QT3 tests had been
failing in their setup on an expression containing a colon the scanner would not take, and the pass rate
went from 97.6% to 97.7%.

A node used as a key is atomized, which is what lets one come out of the input: `map { @id: 1 }` keys on the
attribute's text as `xs:untypedAtomic`, and that shares a key family with `xs:string`, so `?k` reads it back.

**None of this syntax is available below `version="3.0"`.** `[1]` alone is a syntax error in XPath 2.0 —
a predicate with nothing in front of it — and reading it as an array would take that error away. `?` stays
the occurrence indicator there and nothing else. The QT3 suite is what settled this: two grammar tests in
`prod/Predicate.xml` require the error, and they failed while the syntax was ungated.

Keys are compared by `op:same-key`, which is coarser than `eq` in three ways that matter. `NaN` is the same
key as `NaN`, where `eq` says it equals nothing including itself — otherwise a map could hold an entry nobody
could read back. `xs:string`, `xs:anyURI` and `xs:untypedAtomic` are one family, so the text decides. And the
numeric types are one family, so `1`, `1.0` and `1e0` are one key. A whole number keys as an integer rather
than as a double, because this engine carries eighteen digits of `xs:integer` and a double carries fifteen:
keying by the double would quietly merge two integers that are not equal. Everything else keys by its type
together with its canonical form, which gives the specification's answer for dates without a rule per type —
a value with a timezone writes differently from one without, and so is a different key.

Both are immutable: `map:put` returns a new map. That is what lets one be a value rather than an object, and
what makes a map in a global variable as safe to share between threads as a compiled stylesheet.

**Both libraries are complete** — the ten functions XPath 3.1 puts in `map:` and the eighteen it puts in
`array:`, including the ones taking a function, which arrived with function items. The sequence types `map(*)`
and `array(*)` as well. The prefixes are not bound automatically as XSLT 3.0 binds them, since this engine
does not claim 3.0; a stylesheet declares `xmlns:map` and `xmlns:array` itself.

`array(T)` says every member of an array is a `T`, and `map(K, V)` says the same of every entry of a map. Both
are answered by looking, because a map and an array hold what they hold — unlike a function signature, which
would need the item to carry the types it was declared with. A member and a map's value are matched as whole
sequences, so `[("A", "B")]` is an instance of `array(xs:string*)` and not of `array(xs:string)`; a key is one
atomic value, so `K` is an *item* type and `map(xs:string+, …)` is refused as `XPST0003` rather than read as
a condition always met. An empty map is an instance of every map type, having no entry to fail one.

The **unary lookup** `?K` is the binary one with the context item on its left, so `$maps[?name eq 'x']` asks
each map in turn. `?1.0` names no key — the grammar asks for digits there, and a whole number written as a
decimal is not one — and `?()` looks up nothing, which is an empty result rather than a malformed picture.
A lookup key is an *NCName*, a name with no namespace at all, so `?Q{}x` is not one however empty its
namespace is.

**A name may be written `Q{uri}local`**, carrying its namespace instead of resolving a prefix, and goes
wherever a name goes: a step, an attribute, a variable, a function, a type. The URI is whitespace-collapsed,
being an `xs:anyURI` — trimmed at the ends and each run inside reduced to one space, so `Q{ urn:a b }n` and
`Q{urn:a   b}n` are the same name. Nothing else about it is read: `Q{a%20b}` and `Q{a b}` are two namespaces,
the per-cent escape being a character the URI happens to contain, and a URI holds no brace of its own.

No function item — map, array or function — has an **effective boolean value**, so `boolean([1])` and
`if ($m) then …` are `FORG0006`: an empty map is no more false than a full one, and the question a condition
would be asking is not one any of the three answers. `fn:deep-equal` goes inside an array member by member and
a map entry by entry, and refuses two functions with `FOTY0015`.

Neither has a string-value, and asking for one is `FOTY0014` rather than a rendering: there is text that would
show either, but none that means the same, and a stylesheet might go on to compare it. **This engine differs
from the specification here for arrays**, which XPath 3.1 says atomize to their flattened members, so
`xsl:value-of` of an array is an error rather than its text. `array:flatten()` is the way to ask for that
meanwhile. The strict reading is the safe one to be wrong in, and it will change when atomization is done
properly.

### Function items

The XPath 3.0 addition that everything else in 3.1 leans on: a function can be a value. Held in a variable,
passed to another function, returned from one.

Written four ways. **Inline**, `function($x as xs:integer) as xs:integer { $x * 2 }`, with the types optional
on either side. **Named**, `fn:concat#3` — the name and the arity, which together are a function's identity,
so `concat#2` and `concat#3` are two functions. **Called dynamically**, `$f(1, 2)`. And through the **arrow**,
`$s => upper-case() => tokenize(' ')`, which is `tokenize(upper-case($s), ' ')` written in the order the steps
happen rather than inside out.

A named reference works for anything callable by name: a built-in, a type constructor (`xs:date#1`), and a
function the stylesheet declared with `xsl:function` (`my:total#1`). It is one mechanism — the call the name
stands for is compiled once with a variable in each argument position, and calling the value fills them in.

**A map and an array are function items too.** XPath 3.1 does not merely allow `$m('a')`, it defines a map as
the function from key to value and an array as the function from position to member. So both are instances of
`function(*)`, both report arity 1 from `fn:function-arity`, and both can be handed to a higher-order function
with nothing written around them: `for-each(('a','b'), $m)` looks up two keys.

An inline function is a **closure**: it remembers the variables in scope where it was written, not where it is
called, so `for $x in 1 to 3 return function() { $x }` yields three functions answering 1, 2 and 3. This costs
a copy of the two variable stores when the closure is made, and it is not optional — both stores are reused by
the expressions that own them, so holding a reference would answer 3 three times.

**The focus is absent inside an inline function body**, as the specification requires: `.` there is `XPDY0002`
rather than quietly meaning whatever the caller was positioned on. A function that wants a node is given one.

**A named reference to a function that reads the focus takes the focus with it.** `(/r/a)/name#0` called on
`b` still says `a`, because the specification has the reference capture the dynamic context of the place it
was written, and reading whatever happened to be in focus at the call would make the value mean different
things in different hands. This costs an allocation where the reference is evaluated, so only the references
that can read the focus pay it — every zero-argument form, and the few that take an argument and still read
the context node or its document: `lang`, `id`, `idref`, `element-with-id`, `key`, `regex-group`,
`current-merge-group` and the accumulator functions. Every other named reference is a constant, made once when
the expression is compiled. This engine had been reading the focus at the call, which the suite's
`accumulator-062` is written to catch.

Implemented from the core library: `fn:function-arity`, `fn:function-name`, `fn:for-each`, `fn:filter`,
`fn:fold-left`, `fn:fold-right`, `fn:for-each-pair`, `fn:sort` and `fn:apply`. `fn:sort` is stable, which is
what makes sorting twice a way to sort by two things, and it computes each key once rather than once per
comparison. Its collation argument is honoured, the strings among the sort keys being ordered by it.

A sort key is a whole *sequence*, not one value, because the default key function is `fn:data` and 3.1 has
that flatten an array. So the key of `[1, 2]` is `(1, 2)` and the key of `1` is `(1)`; two keys are compared
item by item, and where one runs out while they still agree, the shorter comes first. An array of nothing has
an empty key and sorts before everything. Where two key items cannot be compared the caller hears the type
error the comparison raised — `XPTY0004` — rather than a report that sorting failed.

The sequence type `function(*)` matches any function item. A written-out signature such as
`function(xs:string) as xs:string` is **read for its arity and no further** — a deliberate difference. Checking
the argument and result types would need the function item to carry what it was declared with, and nothing
here records that, so the answer would have to be invented; inventing "no" refuses working stylesheets. The
type is therefore wider than the specification's and never narrower.

Not implemented when this was written, and both since: `fn:function-lookup`, which needs a name resolved against
the static context at run time, and partial application (`concat('a', ?)`).

### Three pieces of 3.0 syntax

**`let $x := E return F`**, which looks like `for` and is its opposite: `for $x in (1, 2, 3)` runs its body
three times, once per item, and `let $x := (1, 2, 3)` runs it once with the whole sequence bound. That is what
makes it the way to name a subexpression you are about to use twice — and naming it is the only way to
evaluate it once, XPath having no other place to put an intermediate result inside an expression. The bindings
nest, so `let $a := 1, $b := $a + 1 return $b` works, and a binding cannot see itself: in
`let $x := 1 return let $x := $x + 1 return $x` the inner one is bound from the outer.

**The simple map operator, `E ! F`**, which is almost `/` and differs in the three ways that matter. A path
insists its steps select nodes, sorts the result into document order, and removes duplicates; this does none
of them. So `a/string-length()` is an error and `a ! string-length()` is a number per `a`, and
`(1, 1, 1) ! .` is three items where a path would have refused the atomic values outright. It binds tighter
than everything but a path itself, so `a ! b + 1` is `(a ! b) + 1`.

**The string concatenation operator, `E || F`**, which is `fn:concat` written as an operator and nothing
more — so the atomization, the empty sequence reading as the empty string, and the refusal of a sequence of
two all come from `concat` rather than being restated for it. Its place in the precedence chain is between
comparison and range, which two tests pin exactly: `12 || 34 - 50` is `"12-16"`, so arithmetic binds tighter,
and `"1234" eq 12 || 34` is true, so comparison binds looser. The scanner reads `||` as one token wherever it
appears, there being no competing reading: a union operand cannot be empty, so two pipes in a row were never
anything else.

None of the three is available below `version="3.0"`. `let` is a keyword only where a variable follows it, so
an element may still be called `let` and a path still reaches it — the same rule that lets `if` be an element
name.

### Finding a function by a name that is a value

`fn:function-lookup` is handed an `xs:QName` and an integer while the transformation runs, and has to find
the function then. Everything else that names a function names it in the source, where what it means can be
settled on the spot — so the library dispatch, which had lived in the parser because that was the only place
it was needed, was pulled out into a class whose every input is a value: the two versions and the legacy
flag. Nothing there reads the stylesheet, so nothing there goes stale, and a compiled stylesheet holding a
reference to it holds no state at all. The parser now goes through the same dispatch, so there is one of it
rather than two to keep in step.

The functions **XSLT** adds are the exception and could not follow. `system-property` and its like are
compiled against the namespaces in scope where they were written, which is exactly what a library is not
given — and `function-lookup(xs:QName('fn:system-property'), 1)('xsl:vendor')` has to resolve that `xsl`
prefix against the scope of the *lookup*, which is what the specification says to use. So those are built at
the `function-lookup` call site, one item per name and arity, and handed over ready. Eleven names, none above
three arguments: a couple of dozen small expression trees, built only where a stylesheet writes
`function-lookup` at all.

Capturing the compiler in the expression instead would have been shorter and wrong twice over — it would
keep the whole parsed stylesheet alive for the life of the compiled one, and it would read a scope that had
moved on since.

A name or arity nothing has gives the **empty sequence**, not an error; so does a name a library has and will
not build, such as a type with no constructor. That is what makes the function usable for probing, and it is
the same posture as `function-available` answering `false` rather than refusing.

### Whether a call can be written, and not only whether a name exists

`function-available` takes a second argument from XSLT 2.0 — the arity. A name alone asks whether there is any
function of that name; a name with an arity asks whether one can be **called** that way, which is the question
a stylesheet guarding a call actually has. So `function-available('translate', 3)` is true and
`function-available('translate', 2)` is false, and `function-available('concat', 17)` is true because a
variadic function takes as many as you like above its minimum.

Every answer comes from the table the call itself is built against — the core registry, the 2.0 registry, the
3.0 lookup, the `map:`/`array:` tables, the `math:` list, the eleven XSLT-specific names, and the functions
this stylesheet declares. That is the property worth paying for: what is reported available is what a call may
be written at, and what is reported unavailable would be refused. A second list of arities to keep in step
would answer worse than no list at all, because it would answer *plausibly* while drifting. Two names show
why it has to be a union across the libraries rather than a lookup in the first that has the name: `fn:round`
takes one argument in the 2.0 library and two in the 3.0 one, and `fn:string-join` the reverse.

Three details the suite pins down. **A name without a prefix means the default function namespace**, so
`function-available('abs')` asks about `fn:abs` — which is what makes `Q{}abs` a different question with a
different answer, since that says outright the function is in no namespace and nothing here is. **The braced
form is read** wherever one of these functions takes a name, prefix or no prefix. And **a type is a function**
where it has a constructor: calling `xs:integer($x)` constructs one, so `function-available('xs:integer', 1)`
is true — while `xs:anyAtomicType` and the three other types that name a place in the hierarchy rather than a
set of values have no constructor, which is exactly where this function and `type-available` come apart.

A **stylesheet function** counts as available, at the arity it was declared with. That is why this one call is
never folded at compile time the way `element-available` and `type-available` are for a literal name: whether
a function of a given name is declared is not settled until every module has been read, which is after the
expression naming it was built.

### The rest of the 3.0 function library

The `math:` namespace **in full** — `math:pi`, `math:exp`, `math:exp10`, `math:log`, `math:log10`,
`math:pow`, `math:sqrt`, `math:sin`, `math:cos`, `math:tan`, `math:asin`, `math:acos`, `math:atan` and
`math:atan2`. Each takes `xs:double?` and answers one, so nothing in is nothing out rather than `NaN`, and the
edges answer what IEEE 754 says: `math:sqrt(-1)` is `NaN` and `math:log(0)` is `-INF`.

Also `fn:head`, `fn:tail`, `fn:contains-token`, `fn:has-children`, `fn:innermost`, `fn:outermost`,
`fn:unparsed-text-lines`, the one-argument `fn:string-join`, and `fn:round` with a precision — which rounds
exactly where the value is an `xs:decimal` or an `xs:integer`, so `round(35612.25, -2)` is `35600` and not
`35600.000000000004`.

`fn:path` returns a path expression that selects the node it was given, and what it returns is meant to be
evaluated rather than read: every name is written in the braced form, so no prefix has to be in scope where
the answer is used, and every step but an attribute's carries a position, so the path selects the one node
and not its like-named siblings. Hence `/Q{}doc[1]/text()[1]` where a person would have written `/doc/text()`.
An element or a processing instruction is counted among its like-named siblings, because the step names it; a
text node or a comment among all of its kind, because the step cannot name one.

What the path hangs from depends on what the tree is rooted at. A document node is the leading `/`; a tree
rooted at anything else — which is what a sequence constructor with an `as` declaration builds — has no `/`
to write, so the path begins `Q{http://www.w3.org/2005/xpath-functions}root()` instead. A **namespace node**
is written `namespace::p` by its prefix, and the default namespace's node, which has no name, as
`namespace::*[fn:local-name()=""]`, both as F&O §14.5.1 has them.

`fn:environment-variable` and `fn:available-environment-variables` answer **nothing**, always. *Since
superseded: `XsltOptions.EnvironmentVariablesEnabled`, off by default, lets a caller open the environment to
a stylesheet; what follows describes the engine before it.* The specification lets a processor decide
whether environment variables are visible, and this one says they are not: the same posture as the opt-in
resolvers, which is that a stylesheet cannot reach outside its input without being handed a way.

**The prefixes `map`, `array` and `math` are declared by the stylesheet like any others**, as are `xs` and
`fn`. XPath's static context predeclares all five and XSLT's does not, replacing that component with what is
in scope where the expression is written (see *What namespaces an element has, and what a name test may
leave out*). A stylesheet using `map` for a namespace of its own is thereby using its own, and one that
forgets to declare the library's is told the prefix is unbound.

**`format-number` reads two different picture languages, chosen by version**, and deliberately. XSLT 1.0
hands the job to Java's `DecimalFormat`; 2.0 replaced that with an algorithm of its own and 3.1 added to it.
Where they differ:

- **What counts as a digit sign.** From 2.0 every member of the zero digit's family is one, so `9,999.99`
  formats 12.34 as `0,012.34` — four places that have to be filled. At 1.0 only the zero digit itself counts
  and a `9` is ordinary text, which is what the reference does with it.
- **Which ten digits are written.** The family is taken from the digits in the picture rather than from
  `zero-digit`, so a picture written in Osmanya digits is answered in Osmanya digits with nothing declared
  anywhere. `zero-digit` still names the family for a picture that writes no digit of its own.
- **Irregular grouping separators.** From 2.0 this is the rule `fn:format-integer` states in the same words —
  separators all of one character, at multiples of one interval, with none of those multiples missing, repeat
  leftwards; anything else stays where it was written. So `000,00,00` gives `12345,67,89`. Under 1.0 the
  rightmost separator sets one interval that repeats and the others are ignored, giving `1,23,45,67,89`.
- **Separators after the decimal separator.** From 2.0 they group the fraction, counted outwards from the
  point and never repeating — a fraction has no leftward habit to carry on — so `#.#,##,#` gives
  `12345.6,78,9`. At 1.0 they group nothing.
- **A picture with no digit that has to appear.** From 2.0, `#` formats zero as `0` and `.#` as `.0`, the
  zero landing on whichever side of the decimal separator has a part to hold it — so `#.#` is also `.0`.
  Under 1.0 the result is the empty string.
- **The constraints.** From 2.0 a picture may hold only the number between its first digit and its last, may
  not put a grouping separator beside the decimal separator or another of itself or at the end, may not follow
  a mandatory digit with an optional one before the point or the reverse after it, and must have somewhere to
  put a digit. Breaking any of these is `FODF1310`, raised where the picture is used rather than where it is
  written, because it is a dynamic error. At 1.0 none of them are checked.
- **An exponent part**, which XPath 3.1 added: `e` followed by digits, where a mantissa precedes it. The
  mantissa is scaled to show exactly as many integer digits as the picture insists on, and where it insists on
  none the mantissa lands between a tenth and one — but the leading zero is still written so long as the
  picture has an integer part to write it in, which is why `#.#e0` gives `0.2e0` where `.#e0` gives `.2e0`.
  Rounding may carry the mantissa back over one and the exponent is not reconsidered when it does, so `.#e0`
  formats 0.99999999 as `1.0e0`. An `e` with no digits after it is ordinary text, which is what lets a picture
  end in `eDog`.

**The digits a value is printed from are its own type's.** An `xs:integer` of eighteen digits and an
`xs:decimal` of twenty-eight are printed as written rather than as the nearest double; a double is printed
from the shortest digits that read back as itself, so `1e30` is one followed by thirty zeros rather than the
`1000000000000000019884624838656` its bits hold. Shifting, rounding and padding are done on the digits
themselves, which is exact where a `double` would lose them and a `decimal` would run out of range at 7.9×10²⁸.
Scaling by a percent sign is the exception: for a double that is double arithmetic, and a percentage of 1e308
overflows to infinity and is reported as one, which is what the suite asks for.

The split is not a guess. `XslCompiledTransform` was asked about thirty-nine pictures spanning every rule
above and agreed on thirty-five; the four it did not are `9.` and `.9`, which it refuses to compile, `1e30`,
whose literal it cannot parse, and `#,##9.99`, where its own answer is `12999` — the picture's characters
coming out in the result. Each is a picture that means nothing at 1.0. The cases that do mean something are
differential tests; the 2.0-and-above behaviour is what QT3 asserts.

One test is knowingly failed: `numberformat128` asserts that `9.9999e999` is `FODF1310`, which is true for a
processor implementing XPath 3.0 and not 3.1. This engine implements 3.1's exponent notation, so it formats
the number instead.

**`fn:format-number` is in the core library** from 3.0, where XSLT 1.0 had it as an XSLT function. XPath 3.0
also made the decimal formats part of the *static context*, so `DecimalFormat` is a public type and
`XPathStaticContext.DeclareDecimalFormat` declares one: a host evaluating a bare expression has the same
right to name a format that a stylesheet has. Declaring none still leaves the unnamed one, being the symbols
the specification names as the default.

**A decimal format is named by its expanded name**, not by its local part — one local name in two namespaces
is two formats, and two prefixes bound to one namespace name the same one. A name may be written as a lexical
QName or as an `EQName` (`Q{uri}local`), and surrounding whitespace is not part of it. A name written as a
literal is resolved where the call is compiled, so the picture can be read once against the format it belongs
to; from 3.0 the name may also be computed, and then both the lookup and the picture wait for a value. A name
that stands for nothing is `FODF1280`.

**`fn:parse-xml`, `fn:parse-xml-fragment` and `fn:analyze-string`** make nodes out of text, which before 3.0
only `document()` and a result tree fragment could do. A *fragment* is what an external parsed entity may be —
several top-level elements, or text with no element at all — so the document node it answers with has more
children than one out of a file ever does, and `fn:parse-xml()` refuses both shapes. Both parse as every other
document this engine reads does — the declaration read, nothing outside the text fetched, there being no
base for a reference in it to resolve against.

`fn:analyze-string` is `xsl:analyze-string` with the two branches replaced by a fixed structure, so it shares
the regular expression translation and the refusal of a zero-width pattern. Capturing groups nest in the
result as they nest in the pattern, which has to be rebuilt from where each group landed: a match reports the
spans and nothing about which group is inside which.

`fn:collation-key` hands back the key a collation sorts by, as `xs:base64Binary`. The bytes are made to sort
as the strings do — that being the point of the function, which is to let a caller index or sort by the key
alone — so the code point collation's key is the string's UTF-8, whose byte order *is* code point order, and
the UCA one's is ICU's own sort key. At `strength=identical` the code points follow the sort key, that being
the tie the key cannot break.

`fn:parse-ietf-date` reads the date formats email and HTTP headers are written in. The specification gives a
grammar rather than a list of formats, and it is wider than RFC 5322's: the day name is optional and may be
spelled out, its comma is optional, the separators between day, month and year may be hyphens, the seconds
and their fraction are optional, the year may be two digits (meaning the twentieth century), the timezone may
be a name from a fixed list of eleven or a numeric offset written any of six ways, and the whole may instead
be written in `asctime` order with the year last. The grammar is read directly rather than a list of patterns
being tried, because the patterns would number in the hundreds and a failure would say nothing about which
part went wrong. The day name is read and dropped without being checked against the date — `Sun, 20 Aug 2014`
is a Wednesday, and the specification still asks for it. A date with no timezone is UTC. Anything the grammar
does not cover is `FORG0010`, including a lexical `xs:dateTime`, which is not one of these forms.

`fn:serialize` writes a sequence out as text. The `xml`, `html` and `text` methods go straight to the writer a
stylesheet's own result goes through; `xhtml` is written as XML, this engine having three writers rather than
four, which is a difference in the empty-element and escaping conventions and nothing else. What is new is the
two methods that write values rather than a tree, which an `xsl:output` may ask for as well (see *What the json
and adaptive methods write, and what a group holds*):

- **`json`** writes a map as an object and an array as an array. JSON holds one value, so the function takes
  one item — a sequence of two is `SERE0023`, and so is a map entry or an array member holding one, the same
  rule reaching inwards. A map may be keyed by anything and JSON only by strings, so two keys that differ as
  values may name one field once written, which is `SERE0022` unless `allow-duplicate-names` says otherwise.
  Infinity and not-a-number have no JSON form and are `SERE0020`. Everything that is not a map, an array, a
  number or a boolean becomes a string, a node by being written as markup first. The solidus is escaped,
  which JSON does not require and this specification does ask for.
- **`adaptive`** writes whatever it is given in a form meant to be read by a person, including the things no
  other method will write at all: a map, an array, a function, a free-standing attribute node. The
  specification says outright that the exact text is the processor's to choose.

The parameters come in either shape: XPath 3.0's `output:serialization-parameters` element with a child per
parameter, or 3.1's map. The difference is not only spelling — in the element form every value is written as
text, so `yes` is how a boolean is spelled, and in a map the value carries a type and a string is the wrong
one. A parameter element that is wrong in any way is `SEPM0017`: one named twice, one that is not a parameter,
one with no `value`, one carrying content. It is a document written by hand and read once, so a typo in it is
worth reporting rather than passing over.

The xml method writes an empty element as `<a/>`, which is what `serialize-adaptive-002` expects; for a while it
wrote `<a />`, `XmlWriter`'s convention, and that test was knowingly failed. The space stays under the xhtml
method, where it is needed.

Nothing F+O 3.0 defines is absent now. The suite's `function-1901` generates its questions from that
specification and finds none missing (see *Which functions this engine says it has*); `fn:path`,
`fn:element-with-id` and `fn:uri-collection`, which this line used to name, are all here.
`fn:load-xquery-module` is not, being 3.1's and needing an XQuery processor this engine is not.

`fn:random-number-generator`, also 3.1's, is here. The map it returns is built over SplitMix64: a 64-bit state
stepped by the golden-ratio increment and mixed on the way out, which is what `number` reads, what `next`
steps, and what `permute` shuffles from with Fisher–Yates. The seed settles everything — the same seed gives
the same numbers and the same permutation, which is what the specification requires and what lets a
transformation be repeated — and a seed of any atomic type is hashed together with its type, so `'1'` and `1`
are different seeds as they are different values. Called without one, the generator is seeded from the same
reading of the clock that `current-dateTime()` reports, so that every seedless call in one transformation is
one generator, as the specification asks, and two transformations get different ones.

`fn:generate-id` is in the core library from 3.0, having been XSLT's own since 1.0. The two readings differ in
what they take: 1.0 takes a node-set and identifies the first node of it, where 3.0 declares `node()?` and so
refuses two nodes with `XPTY0004`. Which reading applies is decided by the version in scope, the same way
every other backwards-compatible difference is, so a `version="1.0"` stylesheet keeps the first-node reading
of a call a `version="3.0"` stylesheet would be told about. The identifier is built from the node's index in
its tree and the tree's own number, so it is stable for the length of a transformation and distinct across
documents without any bookkeeping.

### `fn:format-integer`

Its picture is **not** the `format` attribute of `xsl:number`, and the difference is the point. `xsl:number`
renders a *list* of numbers, so its format alternates tokens with the literal text between them and `1.1`
means two numbers separated by a full stop. `fn:format-integer` renders *one* number, so every character in
the picture belongs to it and the punctuation between digits means grouping. The two meet only at the named
sequences — `a`, `A`, `i`, `I` — which are rendered by the same code.

The picture is a primary format token, optionally followed by a semicolon and a modifier. The separating
semicolon is the **last** one, because a semicolon is itself a perfectly good grouping separator: `#;##1;`
groups with semicolons and has no modifier at all.

**Digit patterns.** `#` is an optional digit and only means anything in front of the digits that must appear,
so `#,##0` is a pattern and `0#` is `FODF1310`. The digits in the pattern choose the family the output is
written in — `١` gives Arabic-Indic digits, and the Osmanya digits at U+104A0 work too, which is why pictures
and results are both read a code point at a time rather than a `char` at a time. Mixing families is
`FODF1310`. A grouping separator is punctuation or a space, which is wide enough for the comma, the space,
the Armenian hyphen and the Aegean word separator, and narrow enough that `1o` is the mistake it looks like.

**Grouping separators repeat only when they are regular**, and that distinction is the one subtle rule here.
A picture whose separators are all the same character, all at multiples of one interval, with none of those
multiples missing, describes a *habit*: `#,##0` groups every three digits however long the number turns out
to be. Anything else describes a *shape*, and its separators stay exactly where they were written while a
longer number runs off the left. So `00,00,00` gives `1,23,45,67,89` and `000,00,00` — which a regular
pattern of two would also have wanted a separator after the sixth digit for — gives `12345,67,89`. A
separator that would land at the very front of the number is dropped rather than left dangling.

**Words.** `w`, `W` and `Ww` are implemented **for English only**, in the dialect the specification's own
examples use: `one hundred and twenty-three`, with the `and`, and `one million and one` where a final
remainder under a hundred rejoins what came before it. `Ww` capitalises every word including `and`, and
counts a hyphen as a word boundary, so 21 is `Twenty-First`. Ordinals change only the last word —
`one thousandth`, `one hundred and twenty-first`.

The third argument is read for its effects and then makes no difference. The specification asks a processor
that does not have the language requested to use one it does have rather than raise an error, so a stylesheet
asking for German ordinals gets English ones. `o(-er)` and the other variations are parsed far enough to know
the picture is well formed and then ignored, English having one ordinal form; `a` and `t` are accepted and
change nothing for the same reason.

**Unrecognised tokens are not errors.** The specification leaves the set of numbering sequences to the
processor and says an unknown one falls back to `1`, so `#`, `bb` and a bare newline all produce plain
digits — and the modifier still applies to the fallback, which is why `()Ww;o` gives `1234th`. Circled
digits, Greek letters and Kanji are among the sequences this engine does not have.

`FODF1310` is raised where the picture is *read*, not where it is written. A literal picture is parsed once
at compile time as an optimisation, but one that will not parse is left for evaluation to raise, so an
invalid picture in a branch that never runs does not stop the expression compiling.

### JSON

Four functions, and two ways in, because the specification gives both and they answer different questions.
`fn:parse-json()` yields maps and arrays, which is what you want when the JSON *is* your data.
`fn:json-to-xml()` yields the node tree of `map`, `array`, `string`, `number`, `boolean` and `null` elements,
which is what you want when you have a stylesheet and would rather write templates than lookups. `fn:json-doc()`
is `fn:parse-json()` over a retrieved resource, so it needs a `DocumentResolver` like everything else that
reads one. `fn:xml-to-json()` writes the structure back out.

This engine had the second direction before it had maps: `Xslt.TransformJson` has built exactly that structure
since the JSON support landed, so `fn:json-to-xml()` is that builder given a name.

**Both read the same options and now answer alike about a string.** `escape` and `fallback` are defined
once and apply to either, and only the shape each builds differs — but `fn:parse-json()` reads through
a different parser, and checked the two options without ever consulting them. An option that is accepted
and then does nothing is worse than one that is refused, and it took the W3C suite to notice.

What `escape` keeps is the escape a character arrived in, for the characters that would not survive
being written into XML as themselves. Those are the ones XML cannot carry at all — the C0 controls
other than tab, newline and return, and an unpaired surrogate — together with tab, newline and return
themselves, which are valid and still at risk: attribute-value normalization turns each of them into a
space, and a parser normalizes line endings in content. The backslash is doubled for a different reason,
XML doing nothing to it but a lone one reading as the start of an escape that is not there.

The quotation mark is not kept, and the distinction is worth naming because the obvious reading gets it
wrong. JSON insists on escaping a quotation mark inside a string, so a rule of "keep what JSON needs
escaped" looks right and passes most of the suite; it then fails `json-to-xml-escape-003`, which asks
outright for `Data with " within it`. The rule is about what XML will carry, not about what JSON will.

**The document `fn:json-to-xml()` builds carries the base URI of the call.** It came from nowhere — no
file was read and no parser saw a location — so the only base URI it can have is where the expression
that made it stands, which is what the specification says of it and what lets a relative reference
inside the JSON mean what one written beside it would.

That is the *static* base URI, a property of where an expression is written rather than of the
transformation running it: a module read from elsewhere, or an `xml:base`, moves the first and leaves
the second. `IXPathStaticContext.StaticBaseUri` is where it now lives, so the XPath parser hands it to
every call that asks rather than the compiler remembering each such call by name — which is also how
`fn:static-base-uri()` came to answer with the transformation's base URI instead of the expression's,
the two being the same often enough for it to pass unnoticed.

Three things are worth knowing about the mapping:

- **`null` is the empty sequence.** The data model has no null, and inventing one would put a value into every
  sequence that a stylesheet would then have to test for. The cost is that an entry bound to `null` looks like
  one bound to nothing, which `map:contains()` can still tell apart.
- **Every JSON number is an `xs:double`.** JSON has one numeric type, so reading `1` as an `xs:integer` would
  make the XPath type depend on how the number happened to be written on the other side of the wire.
- **Half a surrogate pair becomes U+FFFD.** `"\uD834"` alone is well-formed JSON naming a character that
  cannot exist; the replacement character is what the specification substitutes, and one unrepresentable
  character then costs that character rather than the document.

Options: `liberal`, which accepts the comments and trailing commas JSON itself does not, and `duplicates`,
which takes `use-first` (the default), `use-last`, `use-any` or `reject`. `fn:xml-to-json()` takes `indent`. An
option that is there is checked as an argument would be, by the option parameter conventions: one item of
the type asked for, an untyped value cast to it, a string where a boolean was wanted a type error however it
reads — `XPTY0004`, not `FOJS0005`, which is for a value of the right type that means nothing. Not
implemented: `escape` and `fallback`, and `fn:serialize()` with `method="json"`.

**What `fn:xml-to-json()` writes, and what it refuses.** The input is checked as the schema for
`fn:json-to-xml()`'s output has it, and everything that is not that structure is `FOJS0006`: a document
holding two elements or text beside one, a `null` with content, a `string` with an element inside it, two
members of one map whose keys unescape to the same string, an attribute in no namespace the element does not
have, an attribute claiming the functions namespace, an `escaped` that is not a boolean. An attribute in any
other namespace — `xsi:type`, `xml:space` — is none of the structure's business and passes, and a `key` on an
element outside a map is allowed and ignored, the schema giving every element one. A string is escaped as the
specification says: the two-character escapes where JSON has them, `/` for the solidus as the erratum has it,
and `XXXX` in upper-case hex for the rest of the control range, C1 and DEL included. Text that says it is
`escaped` already keeps the escapes it holds — copied through when JSON knows them, `FOJS0007` when it does
not — and has everything else escaped as usual, so a bare quotation mark is still `"`. A number is an
`xs:double`, written as `fn:string()` writes one, so `007` is `7`, `.001` is `0.001` and `1E6` is `1.0E6`,
which is what `fn:json-to-xml()` would have made of the same text; NaN and infinity are not numbers JSON has.
The suite's `fn/xml-to-json` set went from 39 failures to 1 on this, and the one left runs the W3C's own
stylesheet implementation of the function, which is a different matter.

### Static variables, and what a `use-when` can see

The first piece of XSLT 3.0 proper. `static="yes"` on a top-level `xsl:variable` or `xsl:param` says the value
is settled **while the stylesheet is being read** rather than when it is run — which is what makes it the one
kind of variable a `use-when` can ask about, `use-when` being answered before there is a transformation to
speak of.

That single sentence decides the whole design. A static variable is not a global with a slot: a reference to
one compiles to the value itself, in a `use-when` and everywhere else alike, so there is nothing left to
evaluate and nothing for a transformation-time parameter of the same name to override. It is settled **in the
middle of the walk that gathers the top-level declarations**, because that walk's own advance is what answers
the next declaration's `use-when` — so by the time the walk steps past a static variable, it has to be worth
something.

Reading order is then the whole of the scoping rule, without a rule having to be enforced. A static variable
may name the static variables declared before it and nothing else: not one declared after it, which has not
happened yet, and not an ordinary global, which has nothing to be evaluated against. Both are `XPST0008`, the
ordinary "not in scope", because that is what they are.

A **static parameter is supplied where the stylesheet is constructed**, through `XsltOptions.Parameters`, not
where it is run — the point of one being that what it says decides which parts of the stylesheet exist at all.
Required and not supplied is `XTDE0050`. A static variable may not have content, there being no result tree to
build one from yet, and two of one name are `XTSE0630` like any other pair of globals.

**Not implemented when this was written, and since:** a static variable declared in an *imported* module was
not visible to a `use-when` in the module importing it, because imports were followed after the importing
module's declarations had been gathered. The statics are now settled on a walk of their own, in stylesheet
tree order with every import and include in place, before any module is loaded.

### What a mode does with a node no rule matched

`xsl:mode` and its `on-no-match`. Before 3.0 there was one built-in rule set and no way to ask for another —
containers recurse, text and attributes give up their value, everything else produces nothing — which is
`text-only-copy` here and stays the answer for any mode a stylesheet says nothing about. A mode exists as soon
as a template rule names it, so `xsl:mode` declares nothing into being; it says something *about* a mode.

The other five exist because that one answer is wrong for most of what stylesheets are written to do. A
stylesheet changing three elements of a document wants **`shallow-copy`** and can then be three template rules
long; one extracting three elements wants **`shallow-skip`**, and gets exactly what its rules wrote and nothing
else. **`deep-copy`** takes a subtree whole, so a rule for something inside it never runs — which is the
difference between it and `shallow-copy` in one sentence. **`deep-skip`** produces nothing and goes no further
— except from a document node, whose built-in rule applies templates to its children whatever the mode says
(§6.7.1), or such a mode could never be entered from the top.
**`fail`** refuses the transformation with `XTDE0555`, for a stylesheet that means to have covered everything.

The three that descend go through the **attributes** as well as the children, which the 2.0 built-in rule does
not: an attribute is not a child, and a mode copying a document shallowly has to reach them or the copy loses
them.

Two `xsl:mode` declarations of one mode at one import precedence are `XTSE0035` — but only if nothing of higher
precedence settles it, which is the same rule `xsl:decimal-format` follows and for the same reason: a stylesheet
may import two modules that declare one mode differently and then declare it itself, and that is not a conflict
but the ordinary way of overriding one. So the disagreement is recorded and raised at the end, if it survives.

### Text value templates

`expand-text="yes"` makes the braces in a text node expressions rather than characters, so `<out>{1 + 1}</out>`
writes `2`. It is inherited down the stylesheet tree exactly as `version` and `xml:space` are, and may be turned
back off at any depth; the prefixed `xsl:expand-text` is what a literal result element carries, an unprefixed
`expand-text` there being an ordinary attribute of the element being produced. A doubled brace is a literal one,
a sequence is joined with spaces, and the whole thing is the attribute value template parser reading content
instead of an attribute — so text with no brace in it compiles to the same constant it always did.

It is recognised inside `xsl:text` too: that element says how text is treated, not whether it is text.

**Ignoring it was the worst kind of gap**, and the reason it came before any 3.0 instruction. An unimplemented
instruction is refused when it is reached; `expand-text` accepted and ignored writes the stylesheet's own braces
into the result and says nothing.

### What a template says it is standing on

`xsl:context-item`, and `xsl:global-context-item` for the transformation as a whole. Three things a template
can say: that it needs an item (`use="required"`), that it has none (`use="absent"`), or what kind it needs
(`as`).

`use="absent"` **takes the focus away rather than checking for it**. What the template promises is that it does
not read its surroundings — that is a promise to the reader as much as to the processor — so a caller that has
a context item is not an error; the body simply cannot see it, and reading it is `XPDY0002` as it would be
anywhere else. A template *rule* cannot say it, being reached by matching a node, and saying it is `XTSE0020`.

The declared type is **matched, not converted**, which is the whole difference between this and an `as` on a
parameter. A context item is something the template was handed, not something it is asking to have made for it:
an element where `xs:string` was declared is the wrong item, and atomizing it into one would answer a question
the declaration never asked. That mismatch is `XTTE0590`; no item at all where one was required is `XTTE3090`,
and the same two one level up for the source document are `XTTE3086` and `XTDE3086`.

### The version a processor claims is now a setting

`XsltOptions.Version`, defaulting to `XsltVersion.Implemented` — 2.0 when this was written, and 3.0 since
the suite reached 99.5% (see *The claim moves to 3.0*). This was the piece that made everything above
reachable without changing what an existing caller got, and it is not cosmetic.

A processor claiming 2.0 must read a `version="3.0"` stylesheet **forwards-compatibly**: an instruction it does
not have is refused only when reached, and an `xsl:fallback` written beside it is taken. A stylesheet that
wrote such a fallback is entitled to it. So implementing `xsl:try` and then acting on it in a 2.0-claiming
processor takes that fallback away — which is exactly what happened, and what four `insn/try` tests caught. A
3.0 construct is therefore acted on only when **both** are true: the stylesheet says 3.0, and the processor
claims 3.0.

`XsltElement.Since` says which version defines each element, so `xsl:iterate` in a `version="2.0"` stylesheet
is `XTSE0010` — not an element at all — whatever this engine has since implemented.

**XSLT 3.0 widened every boolean attribute** from `yes|no` to the six spellings `xs:boolean` has. The table said
`yes|no` while `ReadDeclarationFlag` had always accepted all six, so the table was disagreeing with the code
that read it; 126 tests turned on that alone.

What this cost is worth stating plainly. The 3.0 run had been reporting **79.2%** while the engine still
claimed 2.0 — a figure inflated by forwards compatibility excusing every gap in it. Claiming 3.0 in that run
turns the excuses off and the honest figure is **78.3%**, over 107 more tests that now reach a verdict instead
of falling back. The number went down because the measurement stopped flattering it.

### Validation is off while the version claim says 2.0

Worth stating on its own, because it caught a value check that looked like it was working. `ValidateXsltElement`
returns immediately under forwards-compatible processing, which is exactly what a `version="3.0"` stylesheet
gets here — so the element table's list of allowed attribute values is **not consulted at all** in the
stylesheets these 3.0 features are written in. Every 3.0 feature landed before the version claim moves has to
validate what it reads itself, or an unrecognised value falls into whichever branch happens to be last:
`on-no-match="deep-fry"` was quietly becoming `fail`.

### A stylesheet function obeys import precedence

Found by the 3.0 run and fixed in the 2.0 half, those tests being 3.0-only. Two `xsl:function` declarations of
one name and arity were refused outright. `XTSE0770` applies only at the **same** import precedence — so a
module that imports another and redefines one of its functions was being rejected, though a template, a
variable and an attribute set could already be overridden exactly that way. Modules are read in increasing
precedence, so what arrives second wins; the shadowed function's body is still compiled, because a function
nothing can call is still one the stylesheet has to have written correctly.

### Recovering from an error, and carrying a value between iterations

`xsl:try` and `xsl:catch`. The content is built into a buffer and written out only once it has finished, which
is what `rollback-output` asks for and the only honest way to give it: an error half way through means half an
element has already been written, and there is no taking that back from a serializer. The buffer is the
guarantee rather than an optimisation of it, and stands for the output it will be written to;
`rollback-output="no"` gives it up (see *What a catch is told, and what a map leaves alone*).

Only a **dynamic** error is caught. A static one is a fault in the stylesheet and was raised long before any of
this ran — `$nope` in an `xsl:try` is still `XPST0008`, not something a catch clause gets a look at. The
`errors` attribute takes the four shapes of name test, and the six `err:` variables are declared into the
clause's own scope so a clause that reads none of them costs nothing.

`xsl:iterate` is what `xsl:for-each` cannot be. Each iteration of a for-each is independent, so a running total
has to be a recursive template or nothing; here the parameters declared at the top are rebound by
`xsl:next-iteration`, which is the loop's only way of talking to the next round. There is still no assignment,
and a parameter not mentioned keeps what it had. `xsl:break` ends the whole thing and `xsl:on-completion` runs
only where it did not — that asymmetry is the point of having both.

`xsl:next-iteration` computes every value before binding any, so `$a` in the expression for `$b` is the old
`$a` whichever order the `xsl:with-param` elements were written in. Binding as it went would make the order of
the stylesheet's lines part of the meaning.

Both need a way out of however many `xsl:if` and `xsl:choose` stand between the instruction and the loop. That
is a flag on the runtime read by `ExecuteAll` after each instruction, rather than an exception: the branch
predicts perfectly in every stylesheet that never uses the feature, which is most of them.

### Markup around content that may not be there

Three instructions and one question between them: did anything come of this? Written longhand the alternative
is an `xsl:if` whose test repeats the selection the body is about to make — the duplication and the second
traversal are what these remove.

`xsl:where-populated` throws away what it produced if what it produced was nothing much. **Attributes do not
save an element**: a `<div class="x"/>` wrapped around nothing is exactly the empty markup a stylesheet is
trying not to write, so a single element or document node with no children does not count.

`xsl:on-empty` and `xsl:on-non-empty` ask the same question about their **siblings** — what the rest of this
sequence constructor produced — and there the test is looser: an empty element counts, because an empty element
is something a neighbour wrote. Two predicates, and which applies depends on which question is being asked.

Two things follow from asking about siblings. A constructor holding either of them cannot be run straight
through, so it compiles to segments that are buffered and then written; a constructor holding neither — nearly
all of them — is untouched and pays nothing. And **every segment is evaluated in the order it was written**, the
conditional ones included: an `xsl:on-non-empty` may name a variable declared just above it and rebound just
below, and deferring it would read the later value. What is conditional is whether the result is kept, not
whether it is computed.

`xsl:on-empty` has to come last, which the specification settled by making anything else `XTSE0010` rather than
choosing between two readings of what "the rest" means.

"This sequence constructor" is any of them. The body of an `xsl:for-each`, an `xsl:try`, an `xsl:iterate`, a
template with parameters — every one is a sequence constructor, and an `xsl:on-empty` in any of them answers
for that body; this engine had been answering only inside element content and refusing the rest as standing
outside any constructor. What counts as empty follows the specification's steps: zero-length text nodes and
zero-length strings are nothing, a document node stands for its children so an empty one is nothing, and a
namespace node written beside the `xsl:on-empty` is an item like any other, so `<xsl:namespace>` keeps the
`xsl:on-empty` from firing — the suite's `on-empty-107`, which needed a namespace written where no element
was open to become a value at all. And an empty result is replaced *whole*: the zero-length strings that made
it empty are not written beside the replacement, where they would stand between atomic values and put spaces
where `on-empty-114b` has none. That last test also settled a rule of the tree-building above: **a zero-length
text node ends a run of atomic values**, so the space between two of them is for values with nothing at all
between — the earlier reading, that such a node was discarded before adjacency was looked at, was wrong.
`insn/on-empty` went from 20 failures to none.

### Building a map from a sequence constructor

`xsl:map` and `xsl:map-entry`, which exist beside the XPath `map { … }` constructor for one reason: that one
takes a fixed list of entries written out, and a map whose shape depends on the document cannot be written as a
literal. An entry is a one-entry map rather than a pair type of its own, which is what lets the entries come
from an `xsl:for-each`, an `xsl:apply-templates`, or a choice — `xsl:map` takes maps, and an entry is the
smallest of them.

Two entries of one key are `XTDE3365` rather than a silent choice: nothing in the instruction says which was
meant, and keeping one quietly would make the answer depend on the order the content happened to run in.

### Walking several ordered sequences at once

`xsl:merge`. What separates it from concatenating the sources and using `xsl:for-each-group` is that the
grouping never has to hold more than one item per source in hand: a merge of ten sorted logs reads each of them
once, in order, and never has all ten in memory. That is why the instruction exists — and why the *streamable*
form of it is the one the specification cares most about.

**This implementation is not streaming.** It materialises each source and then merges, so what it delivers is
the semantics rather than the memory profile. The sources are sorted here whatever `sort-before-merge` says:
that attribute is a promise the caller makes about the input, and sorting something already in order costs a
comparison per item and cannot be wrong. `for-each-stream` is refused with `XTSE3430` — it reads a document
without building one, and this engine builds every document it reads.

A source names its population in one of three ways, and they are alternatives rather than a list:
`for-each-item` gives a context per item, `for-each-source` a context per document — which is what lets one
`xsl:merge-source` stand for a whole collection of files that share a shape — and neither means the `select` is
evaluated once where the instruction stands. `current-merge-group()` gives the whole group and
`current-merge-group('name')` only what that source contributed, which is how the action tells apart items
that merged to the same key after the merge has interleaved them.

### Three that are shorter than what they replace

`xsl:assert` is not `xsl:message terminate="yes"` written shorter. The difference is what it says to the
reader: an assertion is a claim about what the stylesheet *expects*, so a processor that trusts its input may
turn assertions off wholesale, and a message may not. Failing is `XTMM9000`.

`xsl:fork` is a promise about streamability and nothing else. A streaming processor uses it to make several
passes over a document it can only read once; one holding the whole document already has that freedom and has
nothing to arrange, so the branches run in order and their results are concatenated — which is what the
specification says the result is either way.

`xsl:source-document` is the streaming counterpart of binding `doc($href)` to a variable, and without streaming
that is exactly what it amounts to. What it buys a streaming processor is that the document need never exist in
memory at all, which is the one thing this engine cannot offer: its whole model is a tree of integer-indexed
nodes.

### Packages

`xsl:package` is a fourth spelling of `xsl:stylesheet` as far as reading the declarations goes. What makes it a
package is that it has an **outside**: a name, a version, and components with a visibility.

`xsl:use-package` brings another package in at a **lower import precedence**, which is exactly what
`xsl:import` does — and that is not a simplification of the specification but the shape of it. The difference
between importing and using is what each module is *allowed to see* of the other, which is visibility, rather
than how the declarations combine once seen. A package is used once however often it is named: a package is a
thing, not a text to be spliced in, so naming it twice cannot mean two copies.

The name goes to a resolver of its own, `XsltOptions.PackageResolver`, because what it is handed is a different
kind of thing: `xsl:import` gives a reference to resolve against a base URI, and `xsl:use-package` gives an
**identity**. A caller keeping its packages in a database has nowhere to put that name in a file-shaped
resolver. As with the other two, null means a stylesheet cannot reach what the caller has not opted into.

`xsl:override` declares components that belong to the *overriding* package, so its children are spliced in as
that package's own top-level declarations — and land at its precedence, which is all the overriding there is to
do. `xsl:original` is what makes an override a wrapper rather than a replacement, and it is resolved **against
the template being compiled** rather than looked up by name: a shared entry under that name would give two
overrides in one package whichever component was overridden last.

**Visibility** defaults to private inside a package and public outside one — a stylesheet has no boundary for
it to be about, and treating its components as private would make every named template of every ordinary
stylesheet ineligible as an entry point. A package's private template is not a way into it: the caller is
outside by definition, so what it may start at is what the package said it offers, and asking for anything else
is `XTDE0040`.

`xsl:expose` says once what a whole package offers, rather than repeating a `visibility` attribute on every
declaration; `xsl:accept`, inside an `xsl:use-package`, is the same statement from the other side. Both name
components by a list of tests — an exact name, `prefix:*`, `*:local`, `Q{uri}local`, or `*` — and where a
component matches two of them the more particular one settles it, so `names="v1"` beats `names="*"`.

A function is named by **name and arity together**: `p:f#0` identifies a component and `p:f` identifies none,
which is erratum E36 and not an oversight. Only a function has an arity, so `t1#0` names no template either.

What the two may say about a component that stated its own visibility is limited in one direction. An
`xsl:expose` naming a component outright may hold it back from what its declaration offers and cannot hand out
more (`XTSE3010`), with `private` < `final` < `public`; an `xsl:accept` may want less of a component than the
package offered and cannot award itself more (`XTSE3040`). A **wildcard** is not a disagreement at all: writing
`names="*" visibility="public"` over a template the package deliberately kept private is how library packages
are ordinarily written, and reading that as a contradiction would refuse them. `abstract` sits outside that
scale — it says the declaration has no body for a using package to supply, which is a fact about the
declaration rather than about who may see it, so an `xsl:expose` can neither confer it nor take it away
(`XTSE3025`).

The four errors are checked in a fixed order, and the order is not arbitrary: the syntax of the attributes
first (`XTSE0020`, `XTSE3022`/`XTSE3032`), then the visibilities of what was matched (`XTSE3010`, `XTSE3025`),
and only then the names that matched nothing (`XTSE3020`/`XTSE3030`). The last two being that way round is what
erratum E36 costs. E36 made an arity compulsory, which turned a line written in half the conformance tests for
this area into a name matching nothing — so checking names before visibilities answers nine of them with
`XTSE3020` where the fault they were written to show is a visibility.

**An `xsl:expose` is about its own package and no other.** Modules are otherwise flattened into one
compilation, which is right for everything except this: letting a used package's `names="*" visibility="public"`
reach the package that used it made a private template there into an entry point.

**Abstract components.** A declaration with `visibility="abstract"` names a component without defining one and
leaves that to a using package. Reaching one that nobody supplied is `XTDE3052` — a code of its own rather than
letting the absence surface as an empty sequence failing a declared type, which describes the symptom and not
the cause. It is reported for a variable, a named template, a function and an attribute set, and only on
*reaching* it: a package may leave a component unsupplied as long as nothing asks for it. That last part cost
something. Globals are forced up front, which is an optimisation, and an optimisation may not decide whether a
transformation fails — so a global that turns out to read an abstract component is put back the way it was and
left for the demand that may never come.

### What one package may see of another

The compilation is still flat — every package's declarations land in one set of tables, and precedence
settles which declaration a name reaches — but **what a name is allowed to reach is now the package's own
affair**, checked at the point of use. A component is visible in the package that declares it, whatever its
visibility, and in a package that uses that one only if the used package *offers* it: holds it as public,
final or abstract. What the using package then holds it as is what its `xsl:accept` said, or a default that
keeps it to itself — a public component accepted without a word becomes **private** in the package that took
it, so using a package does not re-offer what it offers, and a package wanting to pass a component on has to
say so. Followed through a chain of packages, each step asking the next what it holds the component as.

A reference to a component the referring package cannot see gets the answer an unknown name gets, in whichever
code that kind of reference has for it: `XTSE0650` for a template, `XPST0017` for a function, `XPST0008` for a
variable, `XTSE0710` for an attribute set. That is the whole of what visibility is for — a library's private
components are its own business, and a package naming one is naming something that, from where it stands,
does not exist. References inside the library to its own private components are untouched, because a body
knows which package it belongs to; so is `xsl:original`, which is not a name in any package's space.

**Absent is not hidden**, and the suite is precise about the difference. A component `xsl:accept` hid outright
is invisible from the package that hid it, so naming it is the static error above. An abstract component that
package took *without supplying* — accepted as `absent`, or accepted with nothing said, which comes to the
same — is still there to be named, in the package that took it and in the library that declared it, and is an
error only for whoever reaches it: `XTDE3052`. Taking it as `abstract` has to be said in as many words, and
then a package meant to be run holds an abstract component with a reference to it, which is `XTSE3080` before
anything runs. A plain stylesheet that declares something abstract and reaches it is not a package meant to be
run and hears about it when it is reached.

Four rules follow from the same table, and the codes are the specification's:

| | |
|---|---|
| a package left able to see two components of one name and hiding neither — two used packages offering it, or one offering what the using package also declares outside `xsl:override` | `XTSE3050` |
| an `xsl:override` declaring something the used package has no component for | `XTSE3058` |
| an `xsl:override` of a component the used package holds as private or final, or of the unnamed mode, which is private to its package by definition | `XTSE3060` |
| an executable package holding a reference to a component it holds as abstract | `XTSE3080` |

An override *replaces* the component it names, and that is what makes both the first and the last of those
come out right: a package that overrides a component holds its own declaration under that name, the one it
replaced is not in view there nor through it anywhere beyond, so an accepted homonym does not count and an
abstract component supplied by an override is supplied. An overriding template rule claims each mode it is
written for, resolved as a rule's mode is resolved anywhere — a `default-mode` on the `xsl:override` or on the
package is what `#default` means there.

Three smaller decisions. **Among several `xsl:expose` or `xsl:accept` that match one component, the name
decides first** — an exact name over a namespace wildcard over a bare one — **and the kind decides between names
of one strength**, `component="template"` over `component="*"`; two equal on both leave the later one
standing, which is what lets a package open with a blanket statement and then say otherwise about one thing,
and what lets a package that uses another twice, hiding a component the first time and taking it the second,
mean the second. **A stylesheet parameter is public unless it says otherwise**, the other way round from every
other component, because it exists to be set from outside and a package using another could not otherwise set
the used one's parameters. And an `xsl:accept` may say `abstract`, but only of a component that is — abstract
being a fact about a declaration and not a degree of visibility — which is `XTSE3040` otherwise.

**Not implemented when this was written:** the signature check on an override (`XTSE3070`), choosing among
several versions of a used package by `package-version`, `xsl:original` for a variable or an attribute set, and
re-exposing through `xsl:expose` a component a package accepted rather than declared. The first three are
since done; the last is not, and is `XTSE3020`.

### Shadow attributes

`_name="{…}"` is the ordinary attribute with an underscore in front, and its value is computed **while the
stylesheet is being read** rather than when the instruction runs. That is the distinction worth keeping: an
attribute value template is evaluated during the transformation, so it cannot decide a template's *name*, a
mode, or a package's version — things that must be settled before there is a transformation to compute anything
in. A shadow attribute is evaluated like a `use-when`, against the static variables and nothing else.

It is allowed exactly where the ordinary attribute is, and only at 3.0. Finding one costs a single scan of each
module's attributes; every stylesheet that writes none then pays one field read wherever an attribute is simply
absent, which is what keeps this off the compiler's hot path.

### A value that exists at every node

`xsl:accumulator`. The value starts at an initial value and is rewritten by a rule each time the walk reaches a
node the rule matches, so there is an answer at every node of the document. What it buys is the running total,
the section number, the "which chapter is this in" — every question whose answer depends on everything that
came before, and which otherwise costs a reverse axis or a second pass.

Two values per node, not one: `accumulator-before` asks what it was worth once the node had *begun* — its own
start rule having fired — and `accumulator-after` what it was worth once the node had *ended*, every descendant
having been walked between the two answers. So a leaf answers both alike, and the pre-descent value is the one
that numbers the very figure it is asked about: `accumulator-before('figNr')` on the third `fig` says 3. This
engine had been saying 2 there, recording the value *before* the start rule rather than after it, which the
suite's `accumulator-001` reads as "Figure 0" and twenty more tests read the same way. A rule's `$value` is the
value before that rule fired, which is what makes a rule a rewriting rather than an assignment, and what lets
`$value + 1` be a whole counter. When more than one rule of a phase matches a node, the **last** one declared
fires: rules have no priority and there is no error, which is `accumulator-081`.

**Accumulators were designed around streaming and this engine does not stream**, so the trade here is the
opposite one: a preorder sweep of the document on first use, and two `XPathValue` per node for as long as the
transformation lasts. That is affordable only because the flat tree numbers nodes in preorder already — the
starts come out by counting upwards, and a stack of open nodes gives the ends, since a node's end is reached
when the walk passes the last id in its subtree. One pass produces both orders.

**A circular accumulator is `XTDE3400`**, and the guard is not optional. Without it the recursion exhausts the
stack, which cannot be caught and takes the process with it — the same failure the suite first found in
`xsl:key`, and caught here the same way, by a flag per accumulator rather than per document: circularity is a
property of the definitions and not of what they are applied to.

An accumulator with no rule is refused: it would keep its initial value at every node of every document, which
is a constant written the long way round.

The name handed to `accumulator-before()` and `accumulator-after()` is checked where it is written when it is
written out, and where it is computed when it is computed: a `$name`, or a `'Q{urn:p}n'` written with its
namespace in it, is looked up against the same declarations with the prefixes that were in scope at the call,
and `XTDE3340` names what was not found. A reference `accumulator-before#1` carries the focus of the place it
was written, as every reference to a function that reads the focus now does — see *Function items* — which is
how `accumulator-062` binds `../accumulator-before#1` to a parameter and reads the parent from inside the child.

Two `xsl:accumulator` of one name at one import precedence are `XTSE3350` — unless a declaration at a higher
precedence overrides them both, which is a stylesheet importing a module that declared the accumulator twice
and then declaring it itself. The clash is noted when it is seen and raised only once every module has been
read, the same shape as `xsl:mode` below. And `streamable` on the declaration, which nothing here reads
further, is still refused a value it may not take: `streamable="No"` is `XTSE0020`, shadow attribute or not.

**Which accumulators apply is a property of the document**, fixed when the document is made available and not
afterwards. `use-accumulators` says which — on the `xsl:mode` the transformation begins in for the source
document, and on the `xsl:source-document` or `xsl:merge-source` that read any other — and **saying nothing
means none**. Every accumulator a document is to carry has to be asked for by name, or all of them by `#all`.
Asking for one that does not apply is `XTDE3362` rather than the initial value, because those are different
answers: the accumulator was never run over this document at all.

The documents nothing has to ask for are the ones nothing could stream: what `doc()` and `document()` read,
what the stylesheet builds for itself, and a source document handed over by the caller to a transformation
that starts at a named template — all of which carry every accumulator without being told. That last one is
worth stating, because it looks like an inconsistency and is not. A transformation that starts by applying
templates to its source document has an initial mode, and that mode's declaration is what made the document
available; one that starts at a named template has no initial mode in play, and the document is merely the
global context item, which nothing in the stylesheet said anything about. The suite's `mode-1511` through
`mode-1514` read the source document that way with only a named mode declared, and expect an answer.

That the set belongs to the document rather than to whatever is reading it is the whole of the rule. An
`xsl:source-document` naming one accumulator does not widen again inside a template it applies, however the
mode that template runs in was declared — the document was made available once, and that is when the question
was settled.

Enforcing it costs this engine something and buys it nothing, which is exactly why it has to be enforced. The
rule exists for streaming: an accumulator has to be computed during the single pass that reads the document,
so a processor has to know before it starts which ones it will be asked for. This engine builds them on demand
from a tree it is already holding and would happily build any of them — so a stylesheet relying on an
accumulator it never asked for would work here and fail where it mattered.

The names are resolved after every module has been read, not where the declaration was written: an `xsl:mode`
may name an accumulator that a later module declares. That is also what lets the declarations of one mode be
merged properly, and they are merged **attribute by attribute**: each attribute is settled by the highest
import precedence that writes it, so an import saying `on-no-match="shallow-skip"` and the module importing it
saying `use-accumulators="a"` together describe one mode that does both, which is `accumulator-023`. This
engine had been taking the highest-precedence declaration whole, and losing what the import alone had said.

Two declarations at the settling precedence must agree about that attribute, and what they have to agree about
is *what it means* — `use-accumulators="a b"` and `use-accumulators="b a"` are the same declaration, and so are
two that write one name with different prefixes bound to the same namespace. Two that disagree are
`XTSE0545`, which is the code for disagreeing about any attribute; this engine had been giving `XTSE0035`
there, which is not a code the specification has. A declaration that leaves an attribute out says nothing
about it, and disagrees with no one.

### A copy that remembers where it came from

`copy-accumulators="yes"`, on `xsl:copy` and `xsl:copy-of`. Without it a copy is a fresh tree: it carries every
accumulator, as anything the stylesheet builds does, and each is computed over the copy from the beginning —
so the third figure of a document, copied on its own, becomes the first figure of the copy. With it, **a
copied node answers as the original did**: the same values, and the same applicability, which is how a
`copy-3002` written against a source document with no accumulators fails with `XTDE3362` while its twin
`copy-3003`, which declares one, answers.

The values are not copied. Copying would mean computing every applicable accumulator over the whole source
document at the moment of the copy, most of which nothing will ever ask for — so the copy remembers, node by
node, which node it came from, and the question is put to the original when it is asked. A copy of a copy
follows the chain. Only the copied nodes are listed; a node built beside them belongs to the new tree alone,
and the tree carries no list at all unless something was copied with the attribute, so nothing else pays.

The one thing the copy has to get right is *which* node is the most recently built when the note is taken,
and two things can make a copy add no node: whitespace the builder strips, and text that merges into the text
before it. Neither must overwrite what the node before them remembered, and neither does.

### Copying a node away from where it came from

`fn:copy-of()`. The point of it is **identity, not content**: the copy is equal to the original under
`deep-equal()` and is never the same node under `is`. That is what makes it the way to put a node into a
sequence without its provenance coming too — a copy has no parent, no siblings and no document to be found in,
so nothing downstream can navigate out of it into the tree it came from.

Each node is copied into a capture of its own rather than all of them into one, because a copy is made **per
item**: two adjacent text nodes copied into a single capture would be flushed as one, and the result would be
a sequence one item shorter than the argument. An atomic value has no identity to give it a new one of, so it
is itself.

One function, and it was blocking 85 tests across five test sets — `fn/outermost` failed on it *entirely*, and
most of `insn/where-populated` did too. Worth recording as the shape it is: an absent function that nothing
names in its own right, but that half the suite writes around whatever else it is testing.

### A rule that matches something which is not a node

The predicate pattern, `.[…]`. Every other pattern names an axis and a node test, so it can only ever match a
node; this one asks a question about the **item** itself. That is what lets a template rule match an atomic
value, a map or an array — and so what lets `xsl:apply-templates` be used over something that is not a tree.

A node is an item too, so the two kinds of rule are not two dispatches: an ordinary pattern and a predicate
pattern compete for the same sequence in the ordinary way, at the priority each carries. What differs is the
index — a predicate pattern cannot be bucketed by name or by node kind, so it sits in the per-mode list and is
considered for every candidate in its mode.

**The grammar puts it beside the union rather than inside it.** A pattern is either a union of node patterns or
one `.` with predicates, never a mixture, so `element(foo) | .[. instance of xs:integer]` is `XTSE0340` however
reasonable it looks. Reading it as one branch among several is the obvious mistake, and two tests in
`attr/match` exist to catch exactly that.

Where nothing matches an item, the built-in rule writes it — the same answer a text node gets, and for the same
reason: there is nothing else to usefully do with a value it was handed. Below 3.0 none of this applies and
`xsl:apply-templates` over an atomic value is the type error it always was, there being no pattern that could
match one.

**Not implemented when this was written, and since:** `xsl:sort` over a selection that is not all nodes (see
*What a sort compares*).

### Keeping just enough of where a node came from

`fn:snapshot()`, and the difference from `fn:copy-of()` is the whole of it. A copy has no provenance, which is
what makes it safe to hand on; a snapshot keeps just enough of one to still answer questions about where the
node sat — its **ancestors**, with their attributes and namespaces. What it leaves behind is the **siblings**,
so a snapshot of one row of a large table is that row and its context, not the table.

That is why the two exist side by side: a streaming processor can afford a snapshot of the node it is looking
at, because the ancestors are the part of the document it still has in hand, and cannot afford to keep the
siblings it has already gone past.

The built tree is a single path — each ancestor contributes its name, namespaces and attributes but only the
one child that leads on — with the node's own subtree hanging off the end. Which is what makes the copy
findable: it is reached by taking the first child once for each ancestor, **and once more**. That last step is
the part worth stating, because getting it wrong is silent: `snapshot(/*)` then returns the document node
instead of the element, and only `exists($v/..)` notices.

A snapshot carries the accumulator values the original node had, as the specification says, and so does what
`fn:copy-of()` returns: both are copies made with `copy-accumulators="yes"` in all but name, remembering node
by node where they came from, which is what lets `accumulator-064` through `-067` count on a copy as they would
on the original. `fn:snapshot()` of a namespace node keeps the element it hangs from, as of an attribute
(see *What a namespace node is, and what a bare dot is worth*), which is what the rest of `fn/snapshot`
turned on.

### Where a result document may not be written

`xsl:result-document` produces a document of the transformation's own, so writing one while a **temporary
tree** is being built is `XTDE1480`: whether the file appeared would otherwise depend on whether a variable
was ever read, and the specification refuses that rather than leaving the order to the processor.

XSLT 3.0 draws the line in a narrower place. Building the string content of an attribute, a comment, a
processing instruction, a namespace, an `xsl:value-of` or an `xsl:message` is no longer temporary output
state — nothing there is a tree that might or might not be read, the content is being turned into a string
there and then. Building a temporary tree or sequence still is: a variable, a function's result, a key value,
a sort key, a merge key, an accumulator.

The two look identical from inside the instruction, which is why the output target carries the answer. Most
of the string-bound ones write into a target that only ever builds a string, so they say so by their type;
`xsl:value-of` is the exception and has to be asked, because it collects a *sequence* first so that a
separator can go between the items, and only then joins them. Reading that as a temporary tree would have
left one of the six still refused for a reason that has nothing to do with the rule.

### A key that needs more than an attribute

XSLT 2.0 already lets `xsl:key` take a sequence constructor in place of its `use` attribute, and it is not sugar: a
constructor can declare a variable, sort, choose and call a template, none of which fits in an attribute. What
it produces is kept as a **sequence** rather than built into a tree — the values are what the node is filed
under, and a tree would file every node under one string. Its own local variables get a frame of their own,
exactly as a global's body does. One or the other, never both and never neither: `XTSE1205`.

That error was already in the table below and not in the code, which had been refusing a body with a message
carrying no code at all. Worth naming, because of what it did to the measurement rather than to any
stylesheet: the conformance driver *skips* a test whose expected error cannot be compared, so eight tests
were being quietly set aside rather than counted. Giving the refusal its code brought them back — six of
them pass.

### Several values that make one key

`composite="yes"`, on `xsl:for-each-group` and on `xsl:key`. Both already accepted a key expression giving
several values, and both read it the same way: each value is a key of its own, and the node is filed under
every one of them. That is what you want for `use="@author"` on a document with several authors — the paper
is found under each of them.

It is not what you want for `use="@surname, @forename"`. There the pair identifies the person and neither half
does, and the default reading files everyone twice under names that only half-match. `composite` says the
values are **one key together**, so the node is filed once under the pair and the lookup takes a pair too.
The same for grouping: `group-by="@country, @year"` is two independent groupings by default and one grouping
by the combination under `composite`.

Three things follow that are worth stating.

`current-grouping-key()` returns the **whole sequence** under composite, so the parts can be told apart again:
`current-grouping-key()[2]` is the year. Its values are atomized first, because they are being compared with
each other rather than navigated, and nodes compared by identity would put every item in a group of its own.

`group-adjacent` ordinarily refuses a key that gives more than one value — what it decides is whether an item
continues the run before it, and two answers decide nothing. Under composite there is always exactly one
answer however many values went into it, so the restriction does not apply.

And the identities have to survive **variable length**. Both indexes are keyed on a string, and a composite key
written by joining on a separator loses `("a", "bc")` into `("ab", "c")` as soon as the separator appears in a
value — which any separator may. Each part therefore carries its length in front of it, and the count leads,
so a key of two values can never read as the first two of a key of three.

Composite on `group-starting-with` or `group-ending-with` is `XTSE1090`: it says how to read a key's values,
and a pattern gives none.

Fixing this turned up a bug older than the attribute. `xsl:key use="@a, @b"` — a **sequence** rather than a
union — was indexed by reading the whole sequence as one string, so nothing was ever found under either value.
The lookup side had always handled it; the indexing side never had.

### Saying what to copy

`select` on `xsl:copy`, which until 3.0 could only copy the context item. Three things come with it.

It **names the item**, so the instruction no longer depends on where the focus happens to be. That is what
lets `xsl:copy` be written inside a stylesheet function, which has no context item at all — a shallow-copy
function was simply not expressible before.

It **gives the body a focus of its own**: the selected item, at position 1 of 1. So the body reads the same
whether the item was one of many or the only one.

And because the focus is one the *rules* did not choose, it also sets the **current template rule to null**,
exactly as `xsl:for-each` does. This is the part that is not a matter of taste. Skip it and an `xsl:next-match`
in the body matches against wherever the `select` moved to — for `select=".."`, the parent — and a rule
matching the parent as well never stops. It showed up as a test moving from *failed* to *skipped*, the skip
reason being the driver's twenty-second limit, which is why the denominator is worth reading alongside the
pass count.

XSLT 3.0 also changes what `xsl:copy` does with an item that is **not a node**. There is nothing to
shallow-copy about an atomic value, so the copy is the value itself and the body is not evaluated — there is
no element for it to fill. XSLT 2.0 has no such reading and calls it `XTTE0945`, which is still what a 2.0
stylesheet gets.

### Saying which mode, once

`default-mode`, a standard attribute in 3.0 — so it is allowed on every XSLT element, and on a literal result
element as `xsl:default-mode`. It supplies the value of a `mode` attribute that was not written, and it is
lexically scoped: the nearest one in scope wins.

What makes it more than shorthand is that it settles **two questions that look separate**. On
`xsl:apply-templates` it says which mode to apply *in*; on `xsl:template` it says which mode the rule *belongs
to*. So a stylesheet that keeps a group of rules in a named mode says so once, at the top, rather than on
every rule and every call — and the omission it prevents is a silent one, since a rule that forgot its
`mode="x"` lands somewhere nothing reaches.

It also decides **where the run begins**. The initial mode, where the caller names none, is the default mode of
the outermost element of the principal module. That has to be so for the same reason: the rule matching the
root is in the default mode like every other rule, and starting in the unnamed mode would reach none of them
and let the built-in rules run the whole document out as text. An `InitialMode` from the caller still wins.

Two smaller things came with it. XSLT 3.0 also has to have a way of *naming* the unnamed mode — once
`#default` can mean a named mode, nothing else says "the one with no name" — so `#unnamed` is accepted in
`mode` on `xsl:template` and on `xsl:apply-templates`, and as the value of `default-mode` itself. And the
literal result element's list of XSLT directives is now version-aware: it had been one list for every version,
so `xsl:default-mode` and `xsl:expand-text` there would have been accepted below 3.0 and then quietly ignored.

### Which version's vocabulary a processor reads

Worth stating on its own, because it is the rule the whole attribute cluster turns on — and, since the
elements were brought under it, the rule everything turns on.

**What a stylesheet may say is the vocabulary of the version the processor implements.** A `version="2.0"`
stylesheet is asking for backwards-compatible *behaviour* — how `<` compares, what a node-set-as-boolean
means — not for a smaller language. The specification has a 3.0 processor treat such a stylesheet as a 3.0
stylesheet with that behaviour switched on, so a 3.0 processor reading it honours `html-version`,
`new-each-time` and `start-at`, reads its `xsl:accumulator`, `xsl:mode` and `xsl:try`, accepts `true` where
2.0 wanted `yes`, and takes `Q{uri}local` as a name. Only a processor that does not claim 3.0 refuses any of
that. The suite says so directly: `output-0724` is marked `XSLT20+`, writes `version="2.0"`, and expects
`html-version="5"` to take effect; `for-each-group-089` writes `version="2.0"` and declares an accumulator;
`backwards-045` writes `version="2.0"` inside an `xsl:package`.

The elements followed the stylesheet's claim for longer than the attributes did, on an argument that read well
and was wrong: that a construct this engine reads and a conformant 2.0 processor does not is the quietest
difference there is, and the wider the construct the worse the trade. What that argument missed is that the
comparison is with a conformant *3.0* processor, which is what this run claims to be — and a 3.0 processor
that refuses `xsl:iterate` because the stylesheet said 2.0 is the one that disagrees with every other.

What still follows the *stylesheet's* claim is behaviour that changed for a construct which already existed —
`xsl:copy` on an atomic value being a type error at 2.0 and a copy at 3.0, `xsl:apply-templates` selecting
one, an `xsl:result-document` inside a variable. Two questions, two rules: `Implements30` for vocabulary,
`Claims30` for behaviour. There are three `Claims30` left in the compiler and each is one of those.

Getting this wrong is quiet in the direction that matters least and loud in the other: too strict and the
stylesheet is refused, which is at least visible. It cost fourteen tests here before the shadow attribute form
`_name` was moved to the same rule.

### A function that promises the same answer

`new-each-time` and `cache` on `xsl:function`, which are one feature from two directions.

`new-each-time="no"` is a promise about **node identity**, not about speed. A function returning
`<e>{$n}</e>` builds a new element per call, so ten calls with seven distinct arguments give ten distinct
nodes and a union of them counts ten; under `new-each-time="no"` the union counts seven. That is observable,
so it cannot be left to whether the processor felt like memoizing — the attribute is what decides it. `maybe`,
which is the default, is no promise, and is read here as "build afresh".

`cache="yes"` asks for the memoization outright, and there the identity is a side effect. The point is that
`fib(92)` written the obvious way is not a slow answer without it but no answer at all; the suite has such a
test, and before this it was the reason the whole 3.0 run had to be given a per-test time limit.

Arguments are keyed by **node identity** where they are nodes and by type-and-string where they are atomic, so
two elements that read alike are two different calls. A map, an array or a function item declines the cache
rather than being given an invented identity: memoization is an optimization everywhere except for node
identity, and a function taking a map is not the case where identity is being promised. Results are recorded
on the way out, so a function that reaches itself records nothing partial and one that throws records nothing
at all.

`override-extension-function` is 3.0's spelling of `override`, the old name having said nothing about what it
does; both at once is `XTSE0020`. `streamable` on `xsl:attribute-set` and `streamability` on `xsl:function` are
accepted and do nothing, streaming not being implemented.

### Counting from something other than one

`start-at` on `xsl:number`. It is a **shift**, not a starting value: every level moves by the same amount, so
`start-at="0"` on a numbering that reached 5 gives 4 rather than 0. One integer per level, and where there are
more levels than integers the last integer serves for the rest — which is what makes `start-at="0"` mean
"from zero at every level" rather than "at the first level only".

### Writing HTML 5

`html-version` on `xsl:output` and `xsl:result-document`. A number rather than a flag, because the
specification writes it as one and because `5`, `5.0` and `5.00` have to mean the same thing. Only the step to
5 changes anything, and it changes two.

**The document type declaration.** HTML 5 has no DTD, so there is nothing for a declaration to point at and
the declaration is the bare `<!DOCTYPE html>`. Three conditions, each of them a test in the suite: the version
says 5; `doctype-system` is absent, because a stylesheet naming a DTD means that DTD; and the document element
is `html`, since a document rooted at something else is not an HTML 5 document and declaring it one would
misdescribe what follows. A `doctype-public` on its own does *not* stop it — there is nothing to point at
without a system identifier, so the bare form is still the honest answer. The name is matched without regard to
case and written as the element actually spells it, so `<HTML>` gets `<!DOCTYPE HTML>`.

**Which elements are empty.** HTML 5 settled this by name rather than by DTD, and its list is not HTML 4's:
`embed`, `source`, `track` and `wbr` joined it. Under version 5 the XHTML method also stops asking about the
namespace. The suite puts the same list of names in the XHTML namespace and in no namespace and wants the same
answers from both — `<title></title>` either way, where plain XML would give `<title/>` — so the name alone
decides.

**Prefix normalization**, which is the third thing and the one with a reason behind it rather than a rule.
An element in the XHTML, SVG or MathML namespace is written with **no prefix**, in the default namespace, and
a declaration binding a *prefix* to one of those three is **removed** rather than left standing unused.

Why it has to happen rather than merely being tidier: an HTML 5 parser has no general namespace mechanism. It
recognises `svg` and `math` by name and puts them in their namespaces itself. So `<s:svg>` is not SVG to it at
all — it is an unknown element called `s:svg`, and the document silently stops being the document the
stylesheet wrote.

Three details follow.

The document type declaration names the element **as it will be written**, so a root of `h:html` still gets
`<!DOCTYPE html>`. Naming `h:html` there would name something the document does not contain.

An **attribute** in one of those namespaces keeps its prefix, because an attribute has no default namespace to
be in — an unprefixed attribute is in no namespace at all. Its declaration is then written on the element
carrying it, rather than inherited from an ancestor this rule has just stripped it from.

And a binding that would **rebind the prefix the element's own name uses** is dropped. That case cannot arise
from the data model, where an element's namespace node for its own prefix always agrees with its name; it
arises because serialization has just chosen a different prefix from the one the node was written against. An
SVG element inside `<html xmlns="…xhtml">` takes the default prefix for itself, and copying the inherited
`xmlns` on would write it twice and let the second one decide what `svg` means.

The specification states all of this for the XHTML method and leaves the HTML method's version of it unclear —
unclear enough that the test suite's own author records in the suite that the rules are inadequate, that rules
1 and 2 are directly contradictory, and that he is treating the test results as definitive. Both methods
behave alike here, which is what he concluded they should do.

### Two more things a result document settles

`suppress-indentation` names elements whose content is never indented, however `indent` is set — and it reaches
everything below them, not only their immediate children. What it is for is the elements where inserted
whitespace is not cosmetic: a line break added inside a `p` for tidiness is a space the reader sees.

`item-separator` belongs to the result document rather than to the instruction, so one sequence written to
two result documents may be separated differently in each; the value `#absent` means no separator was
specified, an attribute having no way to be written absent. A separator named is written between **every**
top-level item of the result — a comment beside a number as much as two numbers — which is the sequence
serialization the specification describes, and what the suite's `result-document-1408` expects with
`build-tree` left unsaid; inside an element it still goes between adjacent atomic values, as a tree has it.
With none named, a tree's rule: a single space between adjacent atomic values and nothing beside a node.
`build-tree="no"` is accepted, and an empty result document is still a document — an XML one gets its
declaration, `<?xml version="1.0" encoding="UTF-8"?>` and nothing after it.

**Every serialization attribute of `xsl:result-document` is an attribute value template**, and so is
`format`. One written as plain text is read when the stylesheet is; one computed is read when the instruction
runs, over the settings the rest produced — or, where the format itself is computed, over that format's,
every written attribute being applied then too. A computed value the attribute may not take is the
serializer's `SEPM0016`, where a written one is `XTSE0020`; a computed format naming no `xsl:output` is
`XTDE1460`. A map or a function item reaching the xml, html, xhtml or text method is `SENR0001`, an array
being its members. The `json` and `adaptive` output methods are output methods here too, and
`json-node-output-method` and `allow-duplicate-names` do what they say (see *What the json and adaptive
methods write, and what a group holds*).

### An accessor that reads the context item

`data()`, `node-name()` and `nilled()` gained a form in XPath 3.0 that takes no argument and reads the context
item instead. What that buys is that a path can **end** in one: `PRICE/data()` reads better than
`data(PRICE)` and composes where the other does not, since in `a/b/data()` the function applies to each `b` as
the path reaches it. `base-uri()`, `document-uri()`, `has-children()`, `string()`, `name()` and the rest were
already reachable that way.

It is written as a widening of the declared minimum arity rather than as a second registry entry, because that
is what it is: the same function, reachable with one fewer argument in a later language.

### What a sort compares

A sort key is one value per item, atomized, and from 2.0 the values are compared by what they are: numbers
as numbers, dates as dates, text by the collation named or the language and case order asked for. So
`<xsl:sort select="number(.)"/>` puts 3 before 10 with nothing said about `data-type`, where XSLT 1.0, which
had no types to sort by, orders every key as text unless `data-type="number"` says otherwise. A 1.0
stylesheet gets 2.0's reading here as well, `data-type` being what the language keeps for the other one
(see *What compatibility settles about a sort, and what a context item may be*). Two values that do not
order against each other, an untyped attribute beside a
date, are `XTDE1030`; `data-type="text"` would have compared them as strings. NaN sorts before every other
number and the empty sequence before everything. This engine had been comparing every key as text unless
`data-type="number"` was written, whatever version the stylesheet claimed, which is the suite's `sort-013`
ordering 1001001001 between 100 and 2.

The five ordering attributes — `order`, `data-type`, `lang`, `case-order`, `collation` — are attribute
value templates, settled once per sort with the focus of the instruction that sorts, which is what makes
`order="{}"` read the direction from the document. A written value the attribute may not take is
`XTSE0020`, and `lang="'de'"` is one, the quotes not being part of a language tag; a computed one is
`XTDE0030` when it is computed. A named `collation` is any the processor has, the Unicode Collation
Algorithm ones included, and it decides everything, `case-order` applying only where no collation is named;
this engine had been refusing every collation but the code point one on `xsl:sort`, which grouping and keys
still do. The key may be written as content rather than a `select`, and `xsl:perform-sort` may sort its
content rather than a `select`, its `xsl:sort` children aside.

One more thing the sort tests turned up was not about sorting at all. **A stylesheet function without an
`as` returns the sequence its body made** — `item()*` being the default — and not the text of a tree built
around it. This engine had been building the tree, so a function whose body was an `xsl:perform-sort` handed
back one text node with every item run together, and `xsl:value-of` had nothing to separate. That one line
turned fifteen tests outside `insn/sort` green on each run, `decl/function` and `type/date` among them.

### What a merge refuses

`xsl:merge` was implemented and almost entirely unpoliced. The declarations now say what they must:

| | |
|---|---|
| an `xsl:merge-source` with no `select` | `XTSE0010` — it does not say what it contributes |
| an `xsl:merge-source` with no `xsl:merge-key` | `XTSE0010` — a merge walks its sources in step, and nothing keeps them in step |
| both `for-each-item` and `for-each-source` | `XTSE3195` — two answers to one question |
| an `xsl:merge-key` with both a `select` and content | `XTSE3200`, the same fault `xsl:sort` has `XTSE1015` for |
| `sort-before-merge` that is neither yes nor no | `XTSE0020` |
| `current-merge-group()` or `current-merge-key()` in a pattern | `XTSE3470`, `XTSE3500` |
| either of them outside an `xsl:merge-action` | `XTDE3480`, `XTDE3510` |
| `current-merge-group($name)` naming no source | `XTDE3490` |
| two sources with different numbers of `xsl:merge-key` | `XTSE2200` — no keys to correspond |
| corresponding keys disagreeing about `order`, `data-type`, `lang`, `case-order` or `collation` | `XTDE2210` |
| a source whose items are not in the order its keys put them | `XTDE2220` |
| keys of two sources that cannot be compared with one another | `XTTE2230` |

**The merge functions have a narrower scope than the grouping ones**, and that difference is the whole of
`XTDE3480`. `current-group()` stays readable in a template the body calls, which is what lets one template
serve callers on both sides of a grouping; `current-merge-group()` belongs to the `xsl:merge-action`'s own
sequence constructor and to nothing it invokes. It is implemented by recording the call depth alongside the
group and comparing, rather than by clearing the group on the way into a call — `InvokeTemplate`'s frame is
paid for once per level of a recursion, and it has about three per cent of headroom.

`current-merge-group($name)` naming no source is an error rather than an empty answer, because a source that
contributed nothing to this group *does* answer with nothing, and the two would otherwise be
indistinguishable.

### What a merge takes on trust

The last four of those rules are one idea. A merge walks its sources **in step**, taking one item at a time
from whichever source has the lowest key — which is the whole reason the instruction exists, ten sorted logs
being readable together without ten logs in memory. Every one of those rules is a condition that walking in
step depends on, and each says which condition failed.

`sort-before-merge` therefore stopped being decoration. This engine used to sort every source whatever the
attribute said, on the grounds that sorting something already in order cannot be wrong. It can: it hides the
case where the source was *not* in order, which is a stylesheet asking for something it has not got, and
`XTDE2220` is the specification's name for it. So the order is now **checked** rather than imposed, and only a
source that says `sort-before-merge="yes"` — the caller withdrawing the promise — is sorted here.

Checking rather than sorting also settled something the sort had been quietly deciding. A comparison sort may
permute items whose keys are equal, and the group an `xsl:merge-action` sees is a sequence whose order a
stylesheet can observe. Items now reach it in the order they were read, and the sort that runs when one is
asked for is stable.

**Two ways the sources were being misread came out of this**, and both were found by the new rule complaining
about input that was perfectly in order:

- **A merge key that is a number is ordered as a number.** Merge keys were compared as text unless
  `data-type="number"` said otherwise, which is XSLT 1.0's rule for a language that had no types. Read as
  text, `1 to 50` runs 1, 10, 11, … 2 — so a merge over it believed its own source was out of order.
  Anything untyped or textual keeps the string comparison, that being both what the specification asks for an
  untyped key and where the key's `lang`, `case-order` and collation live.
- **A source is not always one sequence.** `for-each-item` and `for-each-source` run the `select` once per
  anchor item, and *each run is an input sequence in its own right* — which is the point of them, one
  `xsl:merge-source` standing for a whole collection of files that share a shape. Running them together into
  one sequence claimed they were in order end to end, which nothing promised: two separately sorted classes
  of pupils are two inputs to the merge, not one unsorted one.

`XTDE2210` is a rule about **effective** values, so the five attributes it names are read when the merge runs.
They are attribute value templates — `order="{$asc_or_desc}"` is a merge deciding at run time which way it
goes — and comparing them as written would refuse two sources that were about to agree. (`xsl:sort` reads the
same five attributes literally; that they are templates there too is a gap this did not close.)

Two smaller decisions. Presence counts as a value, so `order="ascending"` on one source and nothing on the
other is a disagreement even though the default is `ascending` — that is the 3.0 rule as written, and the
working group corrected it in 4.0 rather than in errata. And `collation` is compared for agreement and then
not used: this engine collates by code point and refuses any other collation where the key is compiled, so
the only value that can reach the comparison is the one it would have used anyway.

`XTTE2230` asks the types rather than every pair of values, comparability following from the pair of types
alone. An ordinary merge has one type per key per source, so it is a handful of comparisons however long the
sources are. It is asked **before** the ordering, not after: whether a sequence of durations is sorted is not
a question worth answering while it is still open whether those durations can be compared with the integers
in the source beside them.

### The focus a merge key is evaluated with

**A singleton focus**: the item, at position one, of one. Not the position in the sequence being ordered,
which is what an `xsl:sort` key sees, and the difference is the same reason `xsl:merge` exists at all. A
merge is meant to be readable one item at a time, and an item arriving on its own does not know where in its
source it stands or how many follow it — a key that could ask would be a key no streaming processor could
evaluate.

So `<xsl:merge-key select="position()"/>` gives every item the key 1, and every item of every source lands in
a single group. That reads like a mistake and is the specification working: this engine had been giving the
position in the sequence, which is the ordinary sort key rule applied one instruction too far.

**The action's focus is the other half of the same question**, and it is the focus `xsl:for-each-group` gives
its body: the context item is the item that opened the group, the position is the group's own place among the
groups, and the size is how many groups there are. So `position()` in an `xsl:merge-action` counts groups
rather than items, and an `xsl:copy` there copies the item the group began with. This engine had been handing
the action whatever focus enclosed the `xsl:merge` — the document node the template matched, in every test
that noticed.

`last()` being the number of groups is why every group is now taken before any action runs. Nothing is
materialised twice for it: a group holds the same items the sources already hold, and this implementation had
already read every source in full. A streaming one could not answer `last()` so cheaply, which is what the
working group's own bug report about it was about.

### A stylesheet that says where to begin

XSLT 3.0's default entry point: with no source document to apply templates to and no template named by the
caller, a stylesheet may still say where to start by declaring one called `xsl:initial-template`. That is how
a stylesheet which generates rather than transforms is written, and it needs no arrangement with whoever runs
it.

It is also, it turned out, how a large part of the conformance suite is written — 382 tests were being
skipped for want of an entry point that their own stylesheets had been declaring all along.

### Where a document reference resolves from

Three questions that had been one field, and a conformance run separated them.

**`document()` takes a sequence.** `document(('a.xml', 'b.xml'))` names two documents, and so does
`document(@href)` over a set of attributes. Anything that was not a node-set had been read as a single
string, which turned two URIs into one with a space in it — a URI no resolver was ever going to find, and 18
tests saying so. Every item of the argument now names a document, however the items arrived; `document(())`
is an empty node-set, and a URI named twice is one document, because it is loaded once and the union removes
the duplicate.

**A node resolves against where that node came from.** A relative reference written in a document means what
it means *there*, so `document(@file)` over a catalogue reads each entry relative to the catalogue. A string
carries no origin of its own and still resolves against the stylesheet. The second argument, where the call
has one, replaces the base for every item with the base URI of the first node it selects — which is what it
is for, and it had been read and discarded.

**The input has a URI of its own, and it is not the stylesheet's.** `XsltOptions.InputUri` is new, and its
absence was the reason the rule above could not work: `document-uri()` on the input answered with the
stylesheet's path, and every reference inside the input resolved from the stylesheet's directory. Saying
nothing still means the input came from nowhere — the truth when it was handed over as text, and what makes
`document-uri()` return the empty sequence rather than inventing something. Base URIs still fall back to the
stylesheet's, since a relative reference has to resolve against something.

`xsl:merge-source`'s `for-each-source` had the same hole and now resolves from where it was written.

### Where a relative reference is resolved from

`xsl:source-document href` resolves a relative reference against the **base URI of the element that wrote it**
— the same rule `document()` follows, and for the same reason: the reference was written in a stylesheet
module and means what it means there, not what it would mean from wherever the run happened to start.

Getting this wrong is quiet in an unhelpful way. A resolver rooted at a directory answers "not found" for a
reference that escapes it, so the failure looks like a missing file rather than like a reference resolved
against the wrong thing.

### An array written into a result

Two rules, and they differ in a way worth keeping straight.

**Atomized** by `xsl:value-of`, `xsl:attribute`, `xsl:comment` and `xsl:processing-instruction`, all of which
the specification says atomize what they are given. XPath 3.1 made arrays atomizable — the same change that
makes `sum([1, 2, 3])` six — so `[1, 2]` contributes two values rather than being a thing with no
string-value. A **map** still has none, and now says so with `FOTY0013` rather than `FOTY0014`: the two codes
divide by the question asked, and writing something into a result asks for its typed value, not for its
string-value.

**Flattened** by `xsl:copy-of` and `xsl:sequence`, which copy items rather than atomizing them. A result tree
has no way to hold an array, so one written into it contributes its members, recursively — and its members
stay what they are, so `xsl:copy-of select="[copy-of(a), copy-of(b)]"` copies two elements where atomizing
would have written their text.

### A body that declares variables of its own

Three places run a sequence constructor that may declare local variables, and two of them had nowhere to put
them. A **global variable's** body was evaluated with no frame at all, so a local variable inside one indexed
into nothing. An **attribute set's** body ran in the *calling template's* frame.

The second is the one worth dwelling on, because the crash was the good outcome. Where the caller's frame
happened to be large enough, no exception was raised: the attribute set wrote its own variable over whatever
the caller had at that slot, and the caller carried on with the wrong value. Both now get a frame sized by
what their own body was compiled against, which is what a template has always had.

Neither is a 3.0 feature. A local variable inside a global variable is XSLT 1.0, and both bugs are in the
suite as *bug regression* tests — `bug-2001` records it against Saxon 5.3.2 and `bug-5302` against Saxon 6.5.
The 2.0 run gained five tests from fixing them.

### What a predicate pattern is worth, and what its predicates mean

Two corrections to `.[…]`, both found by tests that had been crashing before `xsl:next-match` could reach
them at all.

Its **default priority is 1**, not the 0.5 a node test with a predicate gets. A predicate pattern says nothing
about a name or a kind and everything about the item itself, so the specification puts it above the patterns
that merely narrowed a kind rather than beside them.

Its predicates follow the **ordinary predicate rule**, so a predicate whose value is a *number* selects by
position rather than being read as a boolean. A pattern is asked about one item at a time, which makes the
position 1 — so `.[$n]` with `$n` of 2 matches nothing, where reading 2 as true would have matched everything.

And `xsl:next-match` and `xsl:apply-imports` now work where the context item is not a node, which they have to:
a predicate pattern matches an atomic value, so a rule that matched one can contain either instruction. With
no context item at all both raise `XTDE0560` — the specification names that alongside the missing template
rule in one error, and for one reason: each means "carry on down the list of rules that matched *this*", and
with nothing in focus there is no this.

### The language a processor offers, and the behaviour a stylesheet asks for

The rule the XSLT attributes settled, carried down into XPath — and the static context already had the shape
of it. `IXPathStaticContext.LegacySyntax` has always separated *which grammar and function library are
available* from *how values are read*: a `version="1.0"` stylesheet on a 2.0 processor gets the 2.0 grammar
and 2.0 library, and what changes is the reading. `SyntaxVersion` says the same thing one version up.

So the two things XPath 3.0 adds to the **regular expression** language — the non-capturing group `(?:` and
the `q` flag — are available in a `version="2.0"` stylesheet run by a 3.0 processor, and the suite says so
directly: its whole `misc/regex-syntax` set declares `XSLT30+` and uses a `version="2.0"` stylesheet. The same
goes for the arity a function may be called with, which is why `PRICE/data()` works there too.

What still follows the stylesheet's own version is behaviour: how `<` compares two strings, what happens to a
sequence where one item was wanted, and how a function's arguments are converted. One call site now takes both
versions rather than one, which is the honest shape of it.

For the **XPath** language itself — `let`, `||`, `!`, maps and arrays, function items, and the 3.0 function
library — either version saying 3.0 is enough, and the two say it for different reasons. A processor that
implements 3.0 has the 3.0 language whatever the stylesheet claims, which is the rule above and which the
suite tests directly: the `fn:path` tests are written in a `version="2.0"` stylesheet and expect the function
to be there. A stylesheet that itself says 3.0 gets it too, and that direction is this engine's own offer
rather than the specification's — it is what let XPath 3.0 be used while `XsltVersion.Implemented` still said
2.0, and what a caller who asks for 2.0 now keeps. Reading the gate as the processor's version *alone* took
XPath 3.0 away from every stylesheet using it, which is how the second reason came to be written down.

The **XSLT** vocabulary follows the processor alone, and no longer needs the stylesheet to agree: an element or
attribute 3.0 added is read wherever the processor claims 3.0, whatever the stylesheet says (see *Which
version's vocabulary a processor reads*). The one direction that keeps the stylesheet's claim in play is the
other one — a stylesheet claiming 3.0 on a processor that claims 2.0 is forwards-compatible, and there an
unknown element falls back rather than being refused, as it always has.

### The codes the suite asks for

The `misc/error` set is one test per error the specification names, and what it measures is whether this
engine draws each line where the specification does and says so by the same code. A stretch against it
settled the following, most of them a check that had been missing rather than one that was wrong:

- **Static, refused when the stylesheet is read.** One NameTest in both an `xsl:strip-space` and an
  `xsl:preserve-space` at one precedence is `XTSE0270` from 3.0, and recovered by taking the later one for a
  2.0 stylesheet, where the specification made it recoverable. Two `xsl:key` of one name disagreeing about
  `composite` are `XTSE1222`. An `xsl:break` or `xsl:next-iteration` has to be in a tail position — last in
  its constructor, reached from the `xsl:iterate` through nothing but `xsl:if`, `xsl:choose` and `xsl:try` —
  or it is `XTSE3120`; one no `xsl:iterate` encloses at all, in a template the iterate calls, is `XTSE0010`.
  Both a `select` and content is `XTSE3125` on `xsl:break` and `xsl:on-completion`, `XTSE3140` on `xsl:try`,
  `XTSE3150` on `xsl:catch` and `XTSE3280` on `xsl:map-entry`. Two sibling `xsl:merge-source` of one name
  are `XTSE3190`. An `xsl:apply-imports` in a rule declared inside an `xsl:override` is `XTSE3460`. A
  parameter of `xsl:iterate` with no value and a type that admits no empty sequence is `XTSE3520`, there
  being nobody to supply it on the first iteration. A written `start-at` that is not integers is `XTSE0020`.
  And `on-multiple-match` is read at last: two declarations of a mode disagreeing about it are `XTSE0545`.
- **Dynamic, refused when the instruction runs.** A mode declared `on-multiple-match="fail"` makes two
  rules of one precedence and priority both matching a node `XTDE0540`, where the later one otherwise wins;
  another pattern of the same template is not a rival. A map or a function item where a node is being built
  is `XTDE0450`, and one reaching the serializer at the top is `SENR0001`. `current-group()` outside any
  group is `XTDE1061` and `current-grouping-key()` outside any, or in a group made without a key, is
  `XTDE1071` — the last for a 3.0 stylesheet, a 2.0 one still getting the empty sequence there. A
  `document()` told to resolve a relative reference against a node with no base URI — a parentless text node
  — is `XTDE1162`. An initial mode named with no source document to apply templates to is `XTDE0044`, and
  one that is not a way in — nobody declares it, or it is private — is `XTDE0045` first. The `select` of `xsl:copy` yielding more than one item is
  `XTTE3180`, and none copies nothing. An accumulator function called with an atomic value, or an attribute,
  as the context item is `XTTE3360`; this engine had been answering for the attribute's element.
  `type-available()` given no name is `XTDE1428`, `fn:json-to-xml()` asked to validate is `FOJS0004`, and a
  terminating `xsl:message` raises the code its `error-code` names, in any spelling a name has.
- **A template declares the type of its result.** `as` on `xsl:template` had been read and ignored. The
  template's result is now a sequence, captured whole — a call its body ends in included, made inside the
  capture rather than deferred — checked against the type as `XTTE0505`, and written as a sequence is. A
  text node written with output escaping disabled keeps that on its way straight to the result, which is
  what a template's declared result is on; held in a variable or handed back by a function it goes through
  the copier and loses it, as the specification allows, which is the suite's `doe-0182` against its
  `doe-0184`. And `element(*, xs:untyped)` is now a type the parser reads, the annotation being checked and
  then matching nothing, as a processor with no schema can honestly say.

Of the nine tests in the set still failing, six ask for the package model's remaining pieces, and the rest
are single cases — a write to a URI the transformation read, a circular static parameter set from outside,
and `current-grouping-key()` in a group made without a key.

### Codes a later specification renamed

Four errors have two names, and which one a caller hears follows the **processor**, not the version the
stylesheet claims. The suite settles this by pairing the tests over one `version="2.0"` stylesheet and asking
for a code apiece from the two runs.

| | 2.0 | 3.0 |
|---|---|---|
| an unreadable `format-number` picture | `XTDE1310` | `FODF1310` |
| an undeclared decimal format | `XTDE1280` | `FODF1280` |
| a parameter whose type excludes the empty sequence, left out by its caller | `XTDE0610` | `XTDE0700` |
| an argument a stylesheet function's parameter type cannot take | `XTTE0790` | `XPTY0004` |

The first two are the same move: XPath 3.0 took `format-number` into the core library, where its failures are
F&O codes. The question "what is this failure called" is really "which specification defines the function I
called", and that is the processor's library. The third and fourth are XSLT 3.0 dropping a code in favour of
one it already had, the two being one complaint reached two ways — and the fourth is the one an `xsl:evaluate`
test reaches, a stylesheet function called with the wrong kind of argument from a target expression.

What does **not** follow the processor is how the picture is *read*. XSLT 1.0 formats through `DecimalFormat`
and 2.0 through the algorithm that replaced it, and that is behaviour, so it follows the stylesheet. Passing
one version for both would have bought the tests and quietly changed what a 1.0 stylesheet prints.

### What a processor that cannot validate says

`XTSE1660`, for a `[xsl:]type` attribute or a `validation` asking for `strict` or `lax` — including
`default-validation`, whose grammar admits only `preserve` and `strip` so that `strict` is outside the grammar
*and* a request for schema awareness. The specification names the second, and naming the first instead would
tell a stylesheet its spelling was wrong when the spelling was fine and this processor simply cannot do what
was asked.

It is checked **before the required attributes**, which is the part that took finding: `xsl:element
type="xs:untyped"` is also missing its `name`, and a stylesheet asking for validation should hear what it
cannot have rather than being sent to fix the other thing first.

### A package that declares the modes it uses

`XTSE3085`. Inside an `xsl:package`, every mode a template rule or an `xsl:apply-templates` names must be
declared by an `xsl:mode`, unless the package says `declared-modes="no"`. The point is that a misspelt mode
name is otherwise a new mode that nothing reaches — a mistake with no symptom, which is exactly what a package
boundary exists to catch. The unnamed mode counts: a package whose rules are all in named modes and which then
writes one rule without a mode has almost certainly forgotten one.

Two things narrow it, and both were found by the check firing where it should not. A **named** template
belongs to no mode at all, so the check is about `match` rather than about `xsl:template` — a package's named
templates say nothing about which modes it uses. And an `xsl:mode` with no name declares the **unnamed** mode
whatever `default-mode` is in scope: routing that through the default mode meant a package which set one could
not declare the unnamed mode at all, which is a bug the `default-mode` work introduced and this check
surfaced.

`XTSE3440` is the neighbouring rule. A template rule inside an `xsl:override` is redefining part of a *mode*,
and a mode is a component only where it has a name — so `#all`, `#unnamed`, and `#default` or nothing where
the default mode is the unnamed one, are all refused. The last clause is what makes it worth checking rather
than obvious: the same rule written under `default-mode="x"` is perfectly good.

### A node an `as` declaration builds has no parent

The other half of what an `as` declaration decides, and the half that was missing. Without one, a sequence
constructor builds a **document** and everything it makes is inside it. With one it produces a **sequence**,
and the nodes of a sequence are parentless — each is the root of its own tree.

So `<xsl:variable name="e" as="element()"><e/></xsl:variable>` gives an element with `$e/..` empty and
`root($e)` equal to `$e`, where before both answered about a document node the stylesheet never asked for.
The same content with no `as` attribute is still a result tree fragment, a document with the element inside
it, and that comparison is what makes the rule mean anything.

The document node stays in the arrays as node zero, unreferenced. Renumbering to remove it would mean
rewriting every parent, sibling and subtree index in the tree to save one slot, and nothing reaches it once it
has no children. What did have to change is `fn:root()`, which answered with node zero outright: it walks
parents now, because not every tree here is rooted at a document.

Three things fell out of it, none of them 3.0's.

A **pattern names what a node is, not what it hangs from**, so a rule for `e` applies to one that hangs from
nothing. That worked already — except for `*:local`, which was not in the table of tests that admit only
elements, so it was recorded as matching any kind and therefore as needing a parent. The same omission gave
it the default priority of a bare `*` rather than of a half-wildcard, which let a later `*` rule win over one
that had named something.

And `key()` can now say what the specification says: a key indexes a document, so looking one up from a tree
rooted at anything else is `XTDE1270`. That check was not worth writing before, because there was no way to
have such a tree.

### Leaving an argument open

XPath 3.1's partial application: a `?` in an argument list says which argument is *not* being supplied, and
the call yields a function waiting for it. `substring-before(?, '-')` is a function of one argument.

The part that makes it worth having rather than a shorthand is that the **supplied arguments are values**.
They are evaluated once, where the `?` was written, and the function carries them — so
`serialize(?, $output)` reads `$output` now and serializes whatever it is handed later.

A named call and a dynamic one reduce to the same thing: something that yields a function item, and a list of
argument slots some of which are open. So `f(?, 3)` compiles to a partial application over exactly the item
`f#2` would have produced, and the two forms agree by construction rather than by two implementations
happening to match.

A `?` is only read this way where the language has function items at all. At 2.0 it is an occurrence
indicator and nothing else, and reading it as a placeholder would accept a stylesheet a conformant 2.0
processor refuses.

### Names that carry their namespace

`Q{uri}local`, which XSLT 3.0 allows wherever a QName is written out. It is what lets a stylesheet name
something in a namespace it has not declared a prefix for — and what lets a *generated* stylesheet avoid
inventing prefixes it would then have to keep unique.

### What a sequence constructor's items become

A sequence constructor produces a sequence of items, and what becomes of them depends on where they are going.

**Into a tree** (XSLT 3.0 §5.7.1) — the content of an element or a document, a variable without `as` — adjacent
atomic values are joined with single spaces into one text node, so `<xsl:sequence select="1 to 3"/>` writes
`1 2 3` and two `xsl:sequence` of `''` in a row write one space. A text node with characters in it breaks the
run, which is why `<xsl:value-of select="1"/><xsl:value-of select="2"/>` writes `12`: a `value-of` makes a text
node, not a value. A zero-length text node is discarded before adjacency is looked at, and so breaks nothing.
This engine concatenated adjacent atomic values, and `xsl:copy-of` alone got it right; the rule now lives in
one place, where the nodes are built, and every instruction that writes an atomic value — `xsl:sequence`,
`xsl:copy`, `xsl:copy-of`, a template declaring the type of what it stands on — goes through it.

**Into a string** (§5.7.2) — the content of `xsl:attribute`, `xsl:comment` and `xsl:processing-instruction`,
an attribute value template, the `select` of `xsl:value-of` — zero-length text nodes are discarded, adjacent
text nodes merged, and the items' string values joined with a separator. The separator is a single space,
except that `xsl:attribute` and `xsl:value-of` with *content* join with nothing unless their own `separator`
says otherwise: `<xsl:attribute name="a"><xsl:sequence select="1"/><xsl:sequence select="2"/></xsl:attribute>`
is `12` where the same content in a comment is `1 2`, which is what the suite's `seqtor-036a` and
`seqtor-039d` settle between them. A document node copied in is one item, atomized to its string value however
many text nodes it holds; an element is one item, and a comment inside it is no part of its value. Merging
adjacent text nodes first is what makes a function returning the three text nodes `[`, `0` and `]` read `[0]`
in an attribute value template rather than `[ 0 ]`.

**As a sequence** — a variable or a function with `as`, the value an `xsl:sequence` passes on — the items are
exactly what the instructions made. Two `xsl:text` are two text nodes, adjacent or not, and an empty
`xsl:text` is a zero-length text node, which `seqtor-041` counts; merging is what building a tree does, and a
sequence is not a tree. This engine had been gathering adjacent text into one node there.

Two smaller things settled in the same stretch. From 3.0 an `xsl:sequence` may have content in place of a
`select`, producing what the content produces — the point being what can then be put around it, an
`xsl:on-empty` or an `xsl:fallback` — and an `xsl:fallback` is the one child a `select` may stand beside; both
a `select` and content is `XTSE3185`. And a value that cannot be cast to the type a variable or a parameter
declares is `XTTE0570` or `XTTE0590` rather than the cast's own `FORG0001`: the specification names the failure
after what asked for the type, and `sequence-0132` expects it so. A function call keeps XPath's code.

### What a target expression may see

`xsl:evaluate` takes an XPath expression that is a *string* when the transformation runs — computed, or read
out of a document — and evaluates it. Everything else in a stylesheet is compiled before the first node is
read, and the engine is built on that: a name test is a slot resolved against the input document's name
table when the transform starts, a variable is a slot in a frame of known size, a function call is bound to
its declaration. A target expression has none of that settled, so the instruction builds a static context of
its own from what §10.4.1 says the expression may see, and hands the string to the same parser everything
else went through. What comes back is an ordinary expression, evaluated in a frame holding the parameters
and nothing else.

What it sees, and what it does not:

- **Variables**: the parameters supplied, by `xsl:with-param` or in the `with-params` map, and no other —
  not the stylesheet's globals, not the locals in scope where the instruction stands, not a static variable.
  A name in both takes the map's value. A map that is not one map, or one keyed by anything but `xs:QName`,
  is `XTTE3165`; a name nothing supplied is `XPST0008`, as it would be anywhere.
- **Namespaces**: the prefixes in scope on the instruction, the default namespace *left out* — the
  expression's default element namespace is `xpath-default-namespace` and not the `xmlns` of the literal
  result elements around it. With `namespace-context`, the in-scope namespaces of the node named take their
  place, default namespace included, so an expression read out of a document means what it meant there. The
  node has to be one node, or it is `XTTE3170`.
- **Functions**: the XPath library, the `map`, `array` and `math` functions, the constructors, and the
  stylesheet's own functions whose visibility is `public` or `final` — a function that said nothing is
  private, in a stylesheet as in a package. Nothing XSLT adds to the function namespace is there:
  `current()`, `key()`, `document()`, `system-property()`, `regex-group()` and the rest are the
  specification's own list of exclusions, and a call to any of them is `XTDE3160` when the expression is
  compiled, as is a call to a private function or an expression that is not one.
- **The focus**: the one item `context-item` selects, with position and size 1, or none at all — not the
  instruction's. `position()` with no focus is `XPDY0002`, which had been a zero nothing asked for, inside a
  stylesheet function as much as here; more than one item is `XTTE3210`.
- **The rest**: the decimal formats in scope where the instruction stands, and the base URI, which is the
  instruction's own unless `base-uri` says otherwise — `static-base-uri()` and a one-argument `resolve-uri()`
  answer with it. `schema-aware` is read for its spelling and nothing else, there being no schema for `yes`
  to bring in. The result is converted to the `as` by the function conversion rules, `XPTY0004` when it
  cannot be. The default collation is the one thing not carried: this engine does not yet read
  `[xsl:]default-collation` into comparisons anywhere, which is the one test of the set left failing and a
  separate piece of work.

A static error in the target expression is a *dynamic* one for the stylesheet, and `xsl:try` catches it.
The codes the suite settles on are `XTDE3160` for an expression that is not one or that calls what it may
not, and XPath's own for the rest — `XPST0008`, `XPST0081`, `XPTY0004` — which is the finer answer where
there is one.

**A name the stylesheet never wrote.** The target expression may test a name no name test in the stylesheet
mentioned, and that is a slot the name-slot table did not have. The table grows for it: slots are only ever
added, so one handed out stays good, and the table takes a lock so that two transformations sharing the
compiled stylesheet may ask at once. A slot-to-fingerprint mapping built before the table grew is short
rather than wrong, and the runtime builds it again when it sees one behind the table. That is the whole of
what the instruction asked of the architecture, and the thing to remember about it: it is the one place a
compiled stylesheet changes after compilation.

**Compiled once per context.** The compiled form is kept, as the specification suggests, keyed by the string
together with everything the compilation depended on — base URI, namespace bindings, default element
namespace, the parameter names in slot order. A loop that changes its parameter names between calls
compiles anew, and one that does not compiles once.

**Switching it off.** `XsltOptions.DynamicEvaluation`, true by default because the instruction opens nothing
the stylesheet could not already do: a target expression reaches no further than the resolvers the caller
supplied. Off, the feature is disabled the way the specification calls *statically*:
`element-available('xsl:evaluate')` is false and `system-property('xsl:supports-dynamic-evaluation')` is
`no` wherever asked, an `xsl:evaluate` with an `xsl:fallback` takes it, and one without is `XTDE3175` when
reached.

Two things came along. **`element-available()` answers for the 3.0 instructions**, which it had not been
listing — `xsl:try`, `xsl:iterate`, `xsl:merge` and the rest — and answers no for them when the processor
claims 2.0, which is what a stylesheet writing `use-when="element-available('xsl:try')"` relies on; from 3.0
it answers for every element the specification defines, `xsl:catch` included (see *What a catch is told,
and what a map leaves alone*). And an
**`xsl:with-param` with an `as`** converts the value it supplies where it supplies it, `XTTE0590` when it
cannot, which had been left entirely to the callee's own declaration.

### What a pattern reaches, and what an error inside one means

A pass over `attr/match`, the suite's set for the `match` attribute, settled eight things about how a
pattern is matched:

- **A positional predicate counts within what the predicates before it left.** `foo[@a='c'][2]` is the
  second `foo` among those with the attribute, not the second `foo` that happens to have it. The siblings
  are filtered predicate by predicate — but only where a later predicate could read a position, which a
  comparison, a logical operator or `not()` cannot, so the common pattern costs what it cost. And a
  predicate whose value is a sequence of one number, `[1 to 1]`, selects by position as the number would.
- **An error inside a pattern means the item does not match** (§5.5.4): a dynamic or type error while
  evaluating a pattern against an item is a non-match and nothing more, since a stylesheet cannot predict
  which predicates of which patterns will be evaluated against what. So `.[. = 'xxxi']` asked about an
  integer is a rule that does not apply rather than an `XPTY0004`. Three errors are still reported: a
  circularity, which the specification requires reported wherever it is found; a message that terminates;
  and a key the pattern names that nothing declares, a mistake in the stylesheet and not in the data.
- **The axis a step is written on decides what it can reach.** A bare `document-node()` stands on the self
  axis, so a document node matches it; `child::document-node()` names an axis no document node is on and
  matches nothing. `attribute()` with no axis written is on the attribute axis, as XPath's abbreviated
  syntax has it — which also mended `copy-of select="attribute()"`, which had been selecting children that
  could never be attributes — and `child::attribute()` reaches no attribute at all. The child axis, written
  or implied, is child-or-top: a parentless element, text, comment or processing instruction is matched by
  the step that would have matched it under a parent.
- **A pattern that begins at the root needs a document node there.** `//a` hangs from a document node, so
  it does not match an element a variable's `as` declaration built, whose root is itself; `a` does.
- **The outer parentheses of a pattern are stripped**, so `(doc|cod)` is the two rules `doc` and `cod` at
  priority 0 each — level with a plain `doc`, the later declaration winning, and the two rivals under
  `on-multiple-match="fail"`.
- **A selection pattern is evaluated from the candidate's own root**, `root(N)` and not node zero of its
  tree, with the specification's adjustment of the first step: from a parentless top, a child step selects
  the top itself. Its result is read as a sequence and searched for the node, so a union on the right of a
  slash (`x/(a|b)`), an `except` between two axes (`descendant::a except child::a`, which matches an `a`
  with a grandparent and not the one under the top), and a variable holding nodes and other things besides
  all match what they select. `intersect` and `except` take a sequence of nodes as a union does.
- **The grammar reads what XPath reads**: `Q{uri}*` is `prefix:*` with the namespace written out, and a
  `for` in a predicate has a wildcard after `in`, not a multiplication.
- **An unknown function is a static error from 2.0.** Under XSLT 1.0 behaviour a call to a function nothing
  provides waits to be reached, which is what lets a `function-available()` guard mean anything; from 2.0
  the specification makes it `XPST0017` when the stylesheet is read, and gives `use-when` to guard with.

Two things came with it. A 3.0 processor applies templates to items that are not nodes for a
`version="2.0"` stylesheet as for a 3.0 one, there being no 2.0 compatibility mode to fall back into; and
the built-in rule for such an item follows the mode's `on-no-match` — the item itself under the copies, its
string value under text-only-copy, nothing under a skip, `XTDE0555` under fail — where it had written the
value whatever the mode said.

`attr/match` went from 46 failures to 4 on the 3.0 run and from 19 to none on the 2.0 one, and the
same fixes reached `attr/select`, `fn/accessor` and `attr/as`: 66 tests on the 3.0 run, 38 on the
2.0 one. What is left in the set is the namespace axis, the json output method and `xsl:for-each-group`
over atomic values.

### What a copy carries

A pass over `insn/copy`, the suite's set for `xsl:copy` and `xsl:copy-of`, settled the following:

- **A tree an instruction builds completes its own namespaces.** An element copied with
  `copy-namespaces="no"` arrives with no namespace nodes, and the data model still requires its in-scope
  namespaces to cover its name and every prefixed attribute; so when its start tag closes — the first child,
  or the end — the tree builder declares what nothing in scope binds, and undeclares the default namespace
  for an element in none under a parent that has one. The serializer had always done this on the way out;
  now `in-scope-prefixes()` on the tree says the same. A document being parsed is consistent as read and
  pays nothing.
- **`inherit-namespaces="no"` is honoured**, on `xsl:copy`, `xsl:element` and a literal result element:
  the children built inside such an element take none of its namespaces, which a tree records as an
  undeclaration on each child of every namespace the child did not declare for itself. A serializer can
  undeclare only the default namespace, XML 1.0 having no way to undeclare a prefix, and writes `xmlns=""`
  on a child that has no default namespace node where something above it has one (see *What namespaces an
  element has, and what a name test may leave out*).
- **A copied document node is one item** where a sequence is being collected — a document node with the
  copied content as its children — which is what a variable declared `document-node()*` and filled by
  `xsl:copy` over documents is asking for, and what a function returning `xsl:copy-of` of a document
  hands back. It contributes its children and nothing else anywhere an element is open, as before.
- **The floor a document copy sets belongs to its output**: no attribute or namespace may be written at it,
  but a body captured into another target to see what it produced starts a depth of its own, and the floor
  says nothing about it. An `xsl:namespace` on an element inside an `xsl:copy` of the document node with
  an `xsl:on-empty` beside it had been refused as written to the document.
- **`xsl:where-populated` leaves out each item deemed empty**, item by item as the specification writes it
  (§5.7.3): a document or element with no children, any other node — a comment, a processing instruction,
  an attribute — whose string value is zero-length, an atomic value that casts to a zero-length string, an
  empty map, an array with nothing in it that is not itself deemed empty. Not the question `xsl:on-empty`
  asks, which counts an element as something whatever it holds; the two are defined separately, and an
  element with no children is where they part.
- **The XML declaration comes before a leading comment** or processing instruction, not after it: a
  document that begins with a comment is still a document, and the declaration is the first thing in one
  or it is not a declaration at all. It waits for the method to be settled, a comment being no evidence of
  whether HTML is on its way.
- **A global variable has no context item without a source document**, so an `xsl:copy` reached from one
  is `XTTE0945`, as it is in a template reached with no focus; it had been copying the empty document the
  engine holds in place of a source.
- **`serialize()` writes no XML declaration unless asked.** What it hands back is markup for the stylesheet
  to put somewhere, and a declaration in the middle of a document is a mistake the stylesheet would then
  have to strip; `omit-xml-declaration` in the parameters says otherwise, and the QT3 tests that set it
  either way pass as before.

`insn/copy` went from 28 failures to 12 on the 3.0 run. What is left in the set is the namespace axis and
two assertions that call a function the test's own stylesheet declares, which no driver can offer them.

### What a package keeps to itself

Two more pieces of the package model, from `decl/use-package`.

**Which version of a package an `xsl:use-package` is given.** A `package-version` on the declaration is a
range in the grammar of §3.5.2 — one version, a prefix such as `3.5.*`, a bound such as `1.3+` or `to 4.0`, a
span such as `2.7.0-a to 5.0.0-gamma`, any of those separated by commas, or `*` — and one that is not is
`XTSE0020`. Versions are ordered as §3.5.1 orders them, portion by portion with trailing zeros dropped, a
name before an integer where the two meet, so that `2.0-rc1` is before `2.0` and `1.2` before `1.2.5`. A
resolver that knows which versions it holds — one that implements `IXsltPackageResolver` — is asked for
them, and the highest the range takes is loaded; the specification leaves the choice to the processor, and
the latest is what a caller naming a range means. A resolver that knows none supplies what it has, and what
the package declares of itself is checked against the range, a package declaring nothing being version 1 as
the specification says. Nothing in the range is `XTSE3000`. A package's own `package-version` is never
checked for its own sake: the suite writes shadow attributes there, whose static expressions are nobody's
business until the version is asked for.

**What stays local to a package** (§3.5.3): its decimal formats, its keys, its namespace aliases, its
character maps and its output definitions, named and unnamed. A library's `format-number()` reads the
library's formats and the principal's the principal's, however alike the names; each package's `key()` finds
its own keys, a computed key name included; an alias declared in one package moves nothing in another; a
character map the library declared is not one the principal's `xsl:output` can name (`XTSE1590`); and the
unnamed output definition of the top-level package alone settles the principal result, a library's settling
the result documents its own instructions write without naming a format. The compilation still flattens
every package into one set of tables, and rather than a second key on every table the name is marked with
the package it belongs to, in a spelling no namespace can have; the top-level package's names stay as
written, so a stylesheet that uses no package pays nothing.

Two things came with it. **A literal result element's namespace nodes follow §11.1.4 under an alias**: a
node for an alias's literal namespace is not copied, and one for its target namespace is copied whether or
not that namespace is excluded, which is what leaves the element with a binding for the result prefix and
none for the stand-in. And **a constructor function casts a node by its string value**, as `cast as` always
did: `xs:boolean(/flag)` had been reading the node as a number, which it is not, and answering false.

The driver, for its part, now compares an `assert-xml` result with the whitespace between element children
left out on both sides: that whitespace is layout — indentation the serializer added, or whitespace the
source carried between elements and the built-in rules copied — and the expected results are written
without it. Whitespace that is an element's whole content stays significant.

`decl/use-package` went from 27 failures to 2 on the 3.0 run, and `insn/where-populated` — whose tests
all call `unparsed-entity-uri()` in one stylesheet — from 26 to none.

### What an override replaces

The rest of `decl/override` and `decl/package`, which is to say what §3.5.3 makes of an `xsl:override`.

**An override keeps the signature of what it overrides** (`XTSE3070`). A function's parameter types and
result type are compared as types rather than as text, so that `xs:string` under another prefix is the same
type, and its effective `new-each-time` has to be the same. A named template's every non-tunnel parameter
has to reappear with the same type and the same `required`; a tunnel parameter need not reappear, but
reappearing it stays a tunnel parameter of the same type; a parameter the override adds may not be
required; and the two have to say the same about the context item, `use` and type both, an absent
`xsl:context-item` being `use="optional"` and `item()`. A variable's declared type has to be the same.
Nothing is checked for an attribute set, its only rule being about streaming.

**An override replaces the component for everyone, the used package included.** A template or a function
already did, by precedence. A global variable now does it by taking over the slot its original had: a
pattern or a body the used package compiled against `$v` before the override existed reads the override
from then on, and the original moves to a slot of its own, reachable as `$xsl:original` from the override's
value and from nowhere else. An attribute set does it by being put where the original was in the table:
every `use-attribute-sets` that named the used package's set finds the override,
`use-attribute-sets="xsl:original"` on the override draws the original in first, and an override that does
not say so replaces the whole of it, the original's own `use-attribute-sets` included. And `xsl:original`
for a function is resolved against the function being compiled rather than kept under a shared name, so
that two overrides of one arity in one `xsl:override` each mean their own original — a named reference
`xsl:original#2` and a partial application `xsl:original(?, $n)` included.

**Attribute sets and accumulators belong to the package that declared them.** A private attribute set of a
used package and one of the same name in the using package are two sets: a reference resolves to the
package's own declaration first and then to what a used package offers, by the same marking that keeps keys
and decimal formats apart (see *What a package keeps to itself*). An accumulator is local to its package
outright, a computed name included.

**Where a package may be used from, and what a package may hold.** An `xsl:use-package` stands in the
principal module of a package or in a module it includes, not in one reached by `xsl:import` (`XTSE3008`):
an imported module is at a precedence of its own, and a used package has none. An `xsl:import` or
`xsl:include` of an `xsl:package` is `XTSE0165`; a package is not a module. Text inside `xsl:override` is
`XTSE0010`. A top-level package holding an abstract component that nothing supplied — declared by it, or
accepted as abstract from a package it uses — is `XTSE3080` whether or not anything refers to it; a plain
`xsl:stylesheet` that declares one is still let be, and hears about it only when the component is reached.
A template rule written in a used package's public mode outside `xsl:override` implicitly declares a mode of
that name in the using package, which is two components of one name in view (`XTSE3050`); inside
`xsl:override` it is added to the used package's mode. And the `default-mode` an `xsl:package` names is a
mode it has to declare (`XTSE3085`), checked before anything runs.

**How a package starts.** `xsl:initial-template` is an entry point only where it is public or final, the
same rule as for a template the caller names (`XTDE0040`), and a package with no source document and no
such template is `XTDE0040` too, with the code rather than without. An initial mode of `#default` asks for
the package's default mode, the one `default-mode` names, where `#unnamed` asks for the unnamed mode
whatever the package said. And a library's global variables have no context item (§2.3.2): the global
context item is the top-level package's, so a library reading `//*` at the top level is `XPDY0002`.

The driver registers a secondary package under the name it gives itself as well as the one the catalog
gave it, one environment disagreeing with its package, and runs rather than only compiles a test that
expects an error and supplies nothing to start at, since the error it expects may be the engine's refusal to
start.

`decl/override` went from 21 failures to none on the 3.0 run and `decl/package` from 14 to 3. The three:
two tests whose used package declares a function named `me:function1#0`, which is no name at all and is
refused as an `xsl:accept` naming nothing (`XTSE3030`) where the tests expect the homonym error that would
have come next; and one writing `package-version="'1.0.0'"`, quotes and all, which is not a range and is
`XTSE0020` here, where the test expects the package not to be found.

### What the json and adaptive methods write, and what a group holds

`decl/output` and `insn/for-each-group`, which have one thing in common: a value that is not a tree.

**`xsl:output` and `xsl:result-document` take `method="json"` and `method="adaptive"`.** Both were already
written for `fn:serialize()` (see above), and a result now goes through the same writer: what the transformation,
or the instruction, produces is gathered as the sequence it is rather than built into a tree, and written once it
is all there. The json method follows Serialization 3.1 §9: a map is an object, an array an array, a number a
number (`SERE0020` for infinity or NaN), a boolean its token, an empty sequence `null`, anything else atomic a
string, and a node a string holding its markup, written with the method `json-node-output-method` names — xml
unless told otherwise — with no declaration, no indentation and nothing else passed down. Two keys that spell
one string are `SERE0022` unless `allow-duplicate-names` says otherwise; a sequence of two items is `SERE0023`;
a function is `SERE0021`. A character map applies to every JSON string, keys included, and a character it covers
is written as the map says and not escaped, which is how a stylesheet keeps `/` from becoming `\/`. The
adaptive method (§10) writes each item on a line of its own, or between the `item-separator`s: a map or an
array in its constructor syntax, a string in quotes, `true()` and `false()`, an attribute as it would stand on
an element, a function as `fn:name#1`, and a node with the xml method under the parameters in force, character
maps and the declaration included.

**`parameter-document`** names a document holding an `output:serialization-parameters` element, resolved
against the base URI of the `xsl:output` or `xsl:result-document` that names it — which for a declaration in
an included module is the module's own URI, as it now is for everything else a module names — and fetched
through the stylesheet resolver, since it is read while the stylesheet is compiled and settles how the
stylesheet's results are written. Its parameters take precedence over the attributes written alongside
(§26.1), `use-character-maps` there holding its `output:character-map` children inline; one that cannot be
found is ignored, as the specification says; one that is not a parameter document is `SEPM0017`. A named
output definition reads its own.

**Three smaller things in serialization.** The xml method writes an empty element as `<x/>`; the space stays
under the xhtml method, where the browsers it is written for need it. This reverses a choice recorded further
up, made for one code path when the differential tests against `XslCompiledTransform` were the measure: they
compare canonical XML, and the suite's serialization tests compare text. `indent` and `omit-xml-declaration`
on `xsl:output` are read as the booleans they are, in every spelling 3.0 allows, where they had been compared
with `yes` and `no` — an `xsl:output method="xhtml" omit-xml-declaration="false"` had been omitting the
declaration.

**A group is any sequence of items.** `xsl:for-each-group` grouped nodes, a group being a list of node ids in
one tree; it now groups whatever `select` gives it, in population order, and a group of nodes from two
documents is one group. The context item in the body is the group's first item, atomic or not; `current-group()`
is the items in population order, a node set where they are all nodes; a pattern in `group-starting-with` or
`group-ending-with` matches an atomic value the way a 3.0 pattern does, and under a 2.0 stylesheet an atomic
value there is still `XTTE1120`. **Keys are compared as values**: two `xs:dateTime`s naming one instant in two
time zones are one key, a date and the string that spells it are two keys and no error, a QName is its
namespace and local name whatever the prefix, NaN is its own key, and a float or a decimal is compared with
each group's key the way a value comparison compares them — which is not transitive, and the suite's example
from erratum E25 comes out as the erratum says. **The group is absent where the specification says it is**: a
group a pattern made has no key (`XTDE1071`); the attribute value templates of a contained `xsl:sort` see no
group under 3.0 (`XTDE1061`, `XTDE1071`), that being a change 3.0 made; and `current-group#0`,
`current-grouping-key#0`, `current-merge-group#0` and `current-merge-key#0` are function items whose call is
the error, a dynamic call being inside no instruction. The two grouping functions are no longer declared to
return a node set, a group not needing to be one.

`decl/output` went from 21 failures to 2 on the 3.0 run and `insn/for-each-group` from 20 to none; on the 2.0
run, `insn/for-each-group` from 11 to 1. The two left in `decl/output` expect a quotation mark in an
xhtml attribute value written as `&#34;`, where this engine writes `&quot;`, which the serialization
specification allows and those two tests do not.

### What a result document knows, and what a static variable is worth

`insn/result-document`, `attr/static` and, alongside, `fn/current-output-uri`.

**The base output URI is a thing the caller may say** — `XsltOptions.BaseOutputUri`, where the principal
result is going. Null by default, since a transformation writing to a `TextWriter` is going nowhere in
particular; then an `href` reaches the result resolver as written, as it always did. Set, an `href` is
resolved against it before the resolver sees it, two spellings of one place are one document (`XTDE1490`),
and an `href` that resolves to the base output URI itself names the principal result, which is the same
error if the principal has been written. **`current-output-uri()`** answers with it, or with the URI of the
result document being written, and with nothing at all in temporary output state: inside a variable, a
function, a function item called dynamically, a sort key, a pattern — none of which is writing anywhere
(§20.3). In a static expression it is `XPST0017`, there being no output yet to name.

**A result document's serialization may be computed all the way to its method**: `method="js{$o}n"` reaches
the json method, `allow-duplicate-names` and `json-node-output-method` are attribute value templates like
the rest, and a json or adaptive result document with no `href` — the principal result — is gathered and
written as text into the principal output. `item-separator="#absent"` on the instruction takes back the
separator its format declared. `use-character-maps` on the instruction adds to the format's maps rather
than replacing them, the instruction's winning where both map one character (§26.2), the one serialization
attribute that is a union. A result document holding items written with a separator, or a comment at the
top, has content and no element to write a prolog before; it no longer gets the XML declaration at its
end. And `xsl:merge-key` takes content as `xsl:sort` does, evaluated in temporary output state, which is
what makes an `xsl:result-document` reached from it `XTDE1480` (§18.2).

**`xsl:analyze-string` counts its substrings.** `position()` and `last()` in a branch are the substring's
place among all substrings, matching and not, and how many there are (§13.1): a branch naming a result
document after `position()` writes each to a name of its own, where every match had been the first.

**A static variable is settled in stylesheet tree order, imports and includes in place** (§9.7). They are
settled on a walk of their own before any module is loaded — loading follows imports first and declarations
after, which is the wrong order for a declaration after an `xsl:import` that reads a static the imported
module declared — and each module is read once, the tree the walk settled being the tree loaded after it.
Two declarations of one name settle by import precedence; a higher-precedence declaration that comes after
a lower one in tree order has to agree with it, the same kind of declaration and the same value, or it is
`XTSE3450`; an import of the same module twice is the same declaration, not two. A non-static declaration
of the name at a higher precedence is what a body's reference means, the static one shadowed as any global
is. A static declaration with no `select` and no type is a zero-length string, as for any variable; with a
type, the empty sequence where the type takes it, and a parameter whose type does not is mandatory and
`XTDE0700` unsupplied, one whose default does not fit its type `XTDE0050`, one supplied a value that does
not convert `XTTE0590`. `required="yes"` with a default is `XTSE0010`, a `visibility` attribute on a static
declaration `XTSE0090`, `static="yes"` on a template's parameter `XTSE0090`, `select` with content
`XTSE0620`. A static variable is its package's own, keyed by it. And a static value may hold the nodes of a
tree its own expression built — `json-to-xml(...)//` — which had been coming out empty for want of the
name table when the expression was evaluated.

The driver reads the test's `output` element for the base output URI — a file relative to the test's
directory, the directory itself where the file is empty, none at all for `#absent` — applies a parameter's
`as` by constructing the value in it, and gives a result document it parses for an assertion the URI it was
written to, which is what `base-uri()` of its nodes says.

`insn/result-document` went from 18 failures to 1 on the 3.0 run, `attr/static` from 16 to 2 and
`fn/current-output-uri` from 13 to 1. What is left: the two `attr/static` tests need the namespace axis;
`result-document-1406` writes its `parameter-document` as an attribute value template, which would mean
reading the document while the transformation runs rather than while the stylesheet is compiled; and
`current-output-uri-009` sorts atomic values under `xsl:apply-templates`, which this engine leaves unsorted
(see the note in `ApplyTemplatesInstruction`) and so never reaches the two-item sort key the test expects to
be refused.

### What a built tree is relative to, and which mode is a way in

`fn/base-uri` and `attr/mode`, and with them the initial match selection the suite starts a run from.

**A tree the stylesheet builds takes the base URI of the declaration whose content built it** (§9.4): the
`xsl:variable`, `xsl:param`, `xsl:with-param`, `xsl:document` or `xsl:function`, as `xml:base` on it or above
it has moved that — rather than the stylesheet's URI as a whole, which every built tree had been answering.
The document node says so, an element inside inherits it, and an `xml:base` inside resolves against it. With
an `as` declaration the nodes are parentless, and each takes the same. **A parentless copy keeps what it
copies**: `xsl:copy` of an element, or of a document node, into a sequence has no parent to inherit from and
keeps the original's base URI (§11.9.1); `xsl:copy-of` brings the `xml:base` attribute with the element and
resolves it afresh, against the base URI of the instruction copying it (§11.9.2); under a parent, either takes
the parent's like any node. The built-in `shallow-copy` rule reaching a document node makes a document node
of it, as `xsl:copy` would, where it had only run the children. `XdmTree.BaseUri` is where a built tree
remembers all this, and the runtime reads it before falling back to the stylesheet. And **`document()`
resolves from where it is written**: the cache was keyed on the reference alone, so `document('')` in three
templates carrying three `xml:base` attributes read one document three times; the key holds the base URI as
well now, and a reference resolving to a document already read is still that document's nodes.

**Which modes are a way in** (§2.3.4). The unnamed mode and the package's default mode always are; otherwise
a mode has to be declared public or final in the top-level package, accepted into it as such, or — where the
package declares no modes, which a plain `xsl:stylesheet` never does — named by one of its template rules and
declared nothing else, an `xsl:expose` making it private counting as declaring. Anything else is
`XTDE0045`, where before any mode a template mentioned would do. A plain stylesheet's `xsl:mode` without a
`visibility` is public, as its other declarations are; a package's is private. **`xsl:mode` says only what it
may**: `visibility` is public, private or final, never abstract, and the unnamed mode, private to its package
by definition, cannot be declared otherwise (`XTSE0020`); two declarations at one precedence disagreeing
about it are `XTSE0545`, as for any other attribute. **`typed="yes"`** is read, and a mode so declared refuses
every element and attribute it is applied to with `XTTE3100` — this engine validates nothing, so every node
it holds is untyped. And a `visibility` on a template rule with no name is `XTSE0500`.

**Three corrections to what runs where.** `mode="#current"` inside `xsl:function` is the unnamed mode
(erratum XT.E19): a function is not called in any mode. The built-in rule for a document node applies
templates to its children whatever the mode's `on-no-match` says (§6.7.1) — `deep-skip` had been skipping the
document too, so that such a mode could never be entered from the top. And **`current()` is the atomic value
being walked** where `xsl:for-each`, `xsl:apply-templates` or a sort key is walking atomic values; it had been
an error there for want of a node. The context carries only a node, so the runtime records the atomic item
beside it; a node made current by an inner walk over nodes takes precedence, and a function sees none of it.

**The initial match selection** is new to the API: `XsltOptions.InitialMatchSelection`, an XPath expression
whose items templates are first applied to — an element chosen inside the input, the items of a sequence, a
value with no input at all — in the mode `InitialMode` names, each the context item with its position among
them. It is compiled against the principal module's namespaces, functions and decimal formats, evaluated with
the source document as the context item where there is one, and is `XPST0003` where it does not parse. The
driver hands it `<initial-mode select="…">` and `<source select="…">`, which it used to skip: 26 tests across
six test sets.

**And a mode is a way in that takes parameters.** XSLT 3.0 gives the caller the same two sets of them —
ordinary and tunnel — whether the transformation begins at a named template or by applying templates in a
mode, and the engine passes both down either path. The driver had been reading them only from the test's
`initial-template`, so the ones an `initial-mode` declared were never supplied: one test, `initial-mode-004`,
and `misc/initial-mode` is empty with it.

`fn/base-uri` went from 16 failures to 1 on the 3.0 run and `attr/mode` from 14 to 2. What is left:
`base-uri-052` needs XInclude, which nothing here reads; `mode-0009` and `mode-0013` need the namespace axis.

### What a key finds, and what a copy is compared as

`fn/key` and `insn/copy`, and what the two turned up beside them.

**A key matches by value, not by spelling** (§20.2.2). The index was keyed on the string value of whatever
`use` produced, so `key('k', 4)` and `key('k', '4')` found the same nodes, a date in another time zone found
nothing, and `NaN` found itself. It is keyed on a typed identity now — the same spelling `xsl:for-each-group`
groups by — under which every numeric type spells a number one way, an untyped atomic value is the string it
is, a boolean is itself, a date or time is its instant, and a node contributes its string value as an untyped
one. So the integer 4 finds a node filed under `string-length()` and the string '4' does not, `xs:double('NaN')`
finds nothing, and a node filed under `xs:untypedAtomic('7')` is found by '7' and not by 7. A 1.0 stylesheet
keeps what it had: under backwards-compatible behaviour every value filed and every value looked up is a
string first, so `key('k', 1.0)` still finds '1'.

**Several declarations of one name are one key** (§20.2.1). Each files what it matches under what it reads,
and a lookup finds what any of them filed — where before only the first declaration of a name was consulted.
A node filed twice under one value, by two of its own values being equal or by two declarations, is found
once. `composite` has to agree across them (`XTSE1222`). And a key's content — the sequence constructor XSLT
2.0 already allowed in place of `use` — is read under 2.0 as well as 3.0; a declaration written inside it is
`XTSE0010` before it is content. **The third argument names a subtree**: only what stands at or below the
node is found, in whichever document the node is in, where before the whole document was searched however
small a subtree was named. And **a computed name is resolved where it is written**: `key(9, …)` with `9`
holding `p:k` means the key in the namespace `p` is bound to at the call, and `XTDE1260` where that is no
key, rather than any key whose local name is `k`.

**`fn:deep-equal` compares nodes as nodes** (F&O §14.2.4). It had been atomizing both sides, so two elements
with one string value passed as the same whatever their names, attributes and children. Two nodes are equal
now when they are of one kind, have one name where they have a name, carry the same attributes by name and
value, and have children that are pairwise equal once comments and processing instructions are left out; a
node against an atomic value is never equal. That is what `insn/copy`'s `xsl:on-empty` test was asking, and
five of `type/type`'s tests besides.

**`id()` answers for `xml:id`.** The function had been absent on the argument that knowing which attributes
are of type ID means reading the DTD; `xml:id` is an ID by its own specification wherever it is written, so
there is something honest to answer. `id()` and `element-with-id()` take a string of whitespace-separated IDs,
or several, and return the elements carrying any of them in document order, once each; a second argument
names the document to search, a temporary tree included, and a node not in a document is `FODC0001`.
`function-available('id')` now says `true`. What a DTD declares was left unread and a pattern anchored on
`id()` refused until the declaration was read (see *What a document type declaration says*).

**The XML declaration goes first.** With no output method declared the first element decides it — html for an
html document element — and a comment or processing instruction before that element was being written
before the decision, and so before the declaration. It waits for the decision now: a document that is a
comment and nothing else is XML with its declaration first, an html document element still makes the method
html with the comment ahead of it, and text after a held-back comment keeps its place behind it.

Also here: a function called by its expanded name, `Q{uri}local(…)`, was not being looked up among the
stylesheet's own functions.

`fn/key` went from 14 failures to none on the 3.0 run and `insn/copy` from 12 to 10, the ten needing the
namespace axis; `fn/deep-equal` from 1 to none. Of `fn/id`, 35 of the 43 tests need a DTD; five of the rest
anchor a pattern on `id()`, and pass only with a DTD as well.

### What a date is written as, and what a zero-length match is

`fn/format-date-en` and `insn/analyze-string`, both to none.

**A date component has a second presentation modifier** (F&O §9.8.4.1). After the first modifier — the
digits, `Nn`, `w` — one more letter may follow: `o` for an ordinal, `c` for a cardinal, `a` and `t` for
alphabetic and traditional numbering. It is the last character of the presentation when that character can be
one and something stands before it, so `[D1o]` is the day as `23rd`, `[Dwo]` is `twenty-third` and `[YWwo]`
is `Two Thousand And Twenty-Sixth`, while a lone `[Do]` is a first modifier that names nothing and falls back
to digits. English has one form of every number, so only `o` changes what is written, and the ordinal goes
through the `format-integer` machinery the numeric modifiers already went through. Before this the trailing
letter was read as part of the picture, and the day came out as `01`.

**A name is abbreviated before it is cut** (§9.8.4.2). A name longer than its maximum width was cut to the
width, so `[FNn,3-4]` on a Tuesday gave `Tues` by luck and on a Wednesday `Wedn`. The conventional short form
comes first now — `Mon`, `Tues`, `Weds`, `Thurs`, `Fri`, `Sat`, `Sun`, and `Jan` through `Dec` with `June`,
`July` and `Sept` — and is cut only where it is still too long: `[FNn,3-5]` is `Thurs`, `[FNn,3-4]` is
`Thur`, `[MNn,3-3]` is `Sep`. And a name shorter than its minimum width is padded with spaces on the right,
where a number is padded with zeros on the left; both had been getting the zeros.

**Another language is answered in English, and says so** (§9.8.4.8). The language argument was accepted and
ignored. The names here are English and the calendar the Gregorian one, and the specification's rule for a
fallback is that the answer identifies what it fell back to: a call asking for `'de'` gets
`[Language: en]August`, one asking for the calendar `'CB'` gets `[Calendar: AD]03`, and one asking for both
gets both prefixes. A language is English by its first subtag, so `'en-GB'` and `'EN'` are answered without
comment; `'AD'` and `'ISO'` are the calendars answered without one.

**`xsl:analyze-string` takes one string** (§17.1). Its select is `xs:string` — `xs:string?` from 3.0 — read
by the function conversion rules, and it was being read by `string()`: a number, a date, or three strings went
through as text. A node is atomized and an untyped value is a string, and anything else is `XPTY0004`; below
3.0 so is an empty sequence. A 1.0 `version` keeps the 1.0 conversion.

**A zero-length match is a match, from 3.0.** XSLT 2.0 refused a regex that matches the zero-length string
(`XTDE1150`), and 3.0 admits it and says what happens: a zero-length match is a matching substring of its own,
and the character after it begins the next non-matching one, so the walk moves on. `regex=""` over `abcdef`
is seven matches with `a` through `f` between them, and `^[\t ]*$` under `m` finds every blank line, a line
of spaces being a match of its own width and an empty line a match of none. That is the walk .NET takes over
the matches already, so the refusal is all that moved, and it stays below 3.0. `fn:analyze-string` still
refuses such a pattern (`FORX0003`), which F&O 3.1 keeps for the function.

**`regex-group()` is empty in a function and in a pattern** (§17.2). The captured substrings are a
dynamically scoped variable, and the specification lists where it is empty: in a non-matching branch, in a
pattern, and in a stylesheet function — a function called from a matching branch is not processing that
substring, and a `group-starting-with` pattern written in one is not either. Both were seeing the caller's
groups. **The branches come in order**: matching first, non-matching second, `xsl:fallback` only after
either, and neither twice — `XTSE0010` otherwise, where `XTSE1130` stays the code for having neither.

Also here: **a bare `}` in a regular expression is `FORX0002`**. XSD 1.0's grammar left both braces out of
its list of metacharacters by mistake, which F&O corrects (§5.6.1): `{` begins a quantifier, `}` closes one,
and the character itself is written `\}`. The translator was already refusing a `{` that began no quantifier
and letting a `}` stand, as .NET does.

`fn/format-date-en` went from 11 failures to none on both runs, and `insn/analyze-string` from 11 to none on
the 3.0 run and from 6 to none on the 2.0 one.

### What a catch is told, and what a map leaves alone

`insn/try` and `decl/character-map`, both to none on the 3.0 run.

**An error code keeps its namespace.** `fn:error()` may raise a code in any namespace or in none, and the
exception was carrying the local name alone, so every code came out of a catch in the `err` namespace:
`error(QName('', 'too-late'))` was not caught by `errors="too-late"`, and `$err:code eq $my-err` was false
for the very name that had been raised. The namespace travels with the code now, and a clause matches the
whole name — an unprefixed name in `errors` being in no namespace whatever `xpath-default-namespace` says
(§8.3).

**A catch is told where the error stands.** `$err:module`, `$err:line-number` and `$err:column-number` had
been the empty sequence, on the argument that the engine did not carry them. It carries them now: a
stylesheet is parsed with the line and column of every node recorded — two words a node, paid by stylesheets
and not by documents — and the compiler stamps each instruction with where its element stands. When an error
passes up through a sequence constructor, the innermost one knows which of its instructions was running and
marks the error with that place, once; every constructor above leaves the mark alone. So an error in a
`select` is at the instruction carrying it, which is what the suite's line numbers ask for. `$err:value`
stays empty. A stylesheet handed over as text has no module URI beyond the base URI the caller gave, and that
is what `$err:module` reports for it.

**A try is not temporary output state** (§2.3.2). The body of a try is buffered so that an error can take
back what it wrote, and the buffer was the same capture a variable uses — so an `xsl:result-document` inside
a try was `XTDE1480`, as if it stood in a variable, and two result documents to one URI from inside a try
reported that rather than `XTDE1490`. The buffer now says what it stands for: final output where the try
stands in final output, temporary where the try stands in a variable. A variable inside a try is still
temporary, and `current-output-uri()` inside a try still answers.

**`rollback-output="no"` gives the buffer up** (§8.3.1). It had been accepted and ignored, the buffer being
there anyway. Now the body writes straight to the output, and whether a catch then succeeds is what the
specification leaves to the processor; this one draws the line at whether anything was written. An error
before the first write — a source document that cannot be opened — is caught as if the buffer had been
there; one after it is `XTDE3530`, the output being in a state nothing can take back. **A document that
cannot be found is `FODC0002`**, where it had been an error with no code, so `errors="err:FODC0002"` catches
it. And **`element-available()` answers for every element XSLT 3.0 defines**, `xsl:catch` included, which is
the wider question 3.0 made of it (§24.2.2); below 3.0 it is still the instructions alone.

**A stylesheet is read in the encoding it declares.** Six of the character-map failures were one bug, and
not in the character maps: the suite's stylesheets declaring `ISO-8859-1` were being read as UTF-8, every
non-ASCII character in them becoming U+FFFD, so a map on `«` and `»` became one entry on the replacement
character with the last string winning. A stylesheet handed over as bytes — the new `Xslt(Stream)`
constructor, which the conformance driver now uses — is decoded as its declaration says; one handed over as
text was decoded by whoever opened it, as `XdmTreeBuilder.FromXml` already said.

**What a character map leaves alone** (Serialization §11). A map is applied to the characters as they stand,
and what it substitutes is not normalized, not escaped and not mapped again; three places had that wrong.
Under `normalization-form="NFD"` the text was normalized before the map saw it, so a map on `c` met the `c`
that decomposing `ç` leaves behind, and the `ç` the map wrote was decomposed in turn. The map goes first now,
and normalization applies to the runs between its substitutions — in attribute values as well as text, which
had not been normalized at all. A URI attribute the HTML and XHTML methods escape is not mapped, and is a
mapped attribute again under `escape-uri-attributes="no"`. And a string the adaptive method writes in
quotation marks is mapped, a map's key included.

Three things the run turned up beside. A name in an expression is made of **XML's name characters, not
.NET's letters**: the micro sign is a letter to `char.IsLetter` and not a name character to XML, so
`select="µ"` is `XPST0003` — which showed only once the stylesheet declaring itself `ISO-8859-1` was read
as one, the same bytes read as UTF-8 having put U+FFFD there, which nothing accepts. `document()` over a
**parentless text node** is `XTDE1162` for a relative reference, the node having no base URI to resolve it
against, where the reference had gone to the resolver with the stylesheet's. And `for-each-source` on
`xsl:merge-source` is `XPTY0004` for a value that is not a string. Both of the last two had been reaching
the resolver and coming back as a document that could not be found — an error without a code, which the
suite's driver skips rather than judges. Those two were skips, and are passes now.

`insn/try` went from 11 failures to none, and `decl/character-map` from 10 to none on the 3.0 run and from
10 to 1 on the 2.0 one — the one asking a 2.0 processor for the adaptive output method.

### What a namespace node is, and what a bare dot is worth

`fn/snapshot`, to none, and what lay behind it.

**The namespace axis exists.** It was the third of this engine's chosen omissions, refused everywhere with
`XPST0010`, on the argument that nothing here needed to reach a namespace as a node. `fn/snapshot` needed to:
every one of its eighteen tests compares the built-in against the specification's own implementation, which
walks `namespace::*`, matches `namespace-node()` and grafts namespace nodes onto copies — and so did tests
in eight other sets. A namespace node is a node now. `namespace::*` on an element gives one for each
namespace in scope, the `xml` namespace always among them (XDM §6.4). `namespace-node()` is a kind test, in
a path, a pattern and a sequence type, and with no axis written it stands on the namespace axis as
`attribute()` stands on the attribute one (XPath §3.3.2.1). Its name is its prefix and its string value the
URI, so `name()` and `local-name()` answer the prefix, `namespace-uri()` nothing, and `node-name()` nothing
at all for the default namespace's node; its parent is its element. `xsl:copy-of` writes it back out as
the declaration it stands for, or as a parentless namespace node where no element is open; `fn:path()`
writes it as `namespace::p`; `fn:snapshot()` of one keeps the element it hangs from; and a template may
`match="namespace-node()"` or `match="namespace::p"`, the axis being open to a 3.0 pattern (§5.5.3).

**How it is held.** The tree already recorded every element's namespace declarations and the scope they
reach; what it lacked was an identity for each binding at each element, since `namespace::a` on two
elements is two nodes even where the binding is one. Namespace nodes take ids in the attribute id space,
beyond the attributes themselves, and are made on first use — an element's whole set at once, under a
lock, and the same ids from then on, which is what lets `is` and `generate-id()` answer for them. Every
accessor that already knew what to do with an attribute's id does the same for a namespace node's; only
the kind and the document order tell the two apart, a namespace node standing after its element and before
its attributes. A tree never asked for one pays nothing, which was the whole point of not having them: a
document's bookkeeping is unchanged. A name test on the axis compares the prefix as written, the map from
the stylesheet's names to the tree's having been built before the node's name was interned; and the set an
element gets is completed from its own name and its attributes' names, so a tree built without recording
declarations — `json-to-xml()`'s — has the namespace nodes its names imply.

**A bare `.` is worth −1.** The specification's implementation of `fn:snapshot` begins with `match="."`,
to pass atomic values through, and expects `match="namespace-node()"` to beat it. Here `.` was a predicate
pattern of priority 1 — the priority a predicate pattern has, but only with predicates. With none it says
nothing about the item, and the specification gives it −1 (§6.5), below every pattern that narrowed
anything. A stylesheet matching `.` beside `@*` or `text()` had been sending everything to the dot.

**Two things the reference implementation turned up.** A second attribute of one name written into a
built tree — `xsl:copy-of` of `@*` and then of one of them again — was a second attribute, where the
specification says it replaces the first (§5.7.1); the serializer already did that, and the tree builder
now does too, the replacement standing last as the serializer has it. And `exclude-result-prefixes` was
excluding by prefix where the specification designates a namespace URI, resolved where the attribute is
written (§11.1.4): an ancestor excluding `c` bound to one URI was excluding a descendant's `c` bound to
another, which `namespace-0911` catches now that its assertions can walk the axis.

`fn/snapshot` went from 18 failures to none, and the axis let through the `attr/match`, `attr/mode`,
`attr/static`, `fn/accessor`, `decl/accumulator`, `misc/seqtor` and `type/namespace` tests that had been
waiting on it. `copy-1221` is left: a namespace that fixup adds to a copied element is inherited by the
copy's children here, where the specification keeps a copied element's fixup to itself (§5.7.1), and telling
a fixup-added declaration from a written one is a change to the builder for another day.

### What a date's year and timezone are written as

`fn/format-date`, to none on both runs.

**The year is reduced to what the marker has room for** (F&O §9.8.4.4). `[Y01]` asks for a two-digit year
and was getting all four. The year is written modulo ten to the maximum width where one is given, else to
the number of digit signs in a decimal pattern of two or more, else in full — so `[Y01]` and `[Y,2-2]`
write 2003 as `03`, `[Y0001]` and `[Y]` write it whole, and a pattern in another family, `[Y๐๑]` or one in
the Osmanya digits, does the same by the same count.

**A roman year is written whole and padded with spaces.** `[Yi,4-4]` was cutting `dcccxvii` to `xvii` and
zero-padding `miv` to `0miv`: the width rules for a number keep its low-order digits, and were being applied
to what is not a number. A sequence — roman numerals, letters, words — is never cut, there being no
meaningful way to (§9.8.4.3), and is padded on the right with spaces, as a name is: `dcccxvii` stands,
`mmiii` stands at five characters against a width of four, and `miv` becomes `miv `.

**The timezone takes the shapes the specification's table gives it** (§9.8.4.6). `[Z]` had written a zero
offset as `Z` and everything else as `+01:00`; now the first presentation modifier says the shape. One or
two digits is the hours, with the minutes only where there are any — `[Z0]` is `-5`, `+0` and `+5:30`;
digits with a separator is hours and minutes always, `[Z0:00]` being `-5:00` and `[Z00:00]` `-05:00`;
three or four digits is both with nothing between, `[Z0000]` being `-0500`; `[ZZ]` is the military letter,
`W` for -10:00, `Z` for UTC, `J` for a value with no timezone and the `+05:30` fallback for an offset with
no letter; and `[ZN]`, a name, which this engine has none of, is the `+01:01` fallback the specification
names for that case. The second modifier `t` writes a zero offset as `Z`, so `[Z00:00t]` is what `[Z]`
used to be. `[z]` prefixes `GMT`. No modifier at all is `+00:00`, as the table has it. A width with no
modifier is where the specification stops, and the suite expects `[z,2-2]` to be `GMT-14` and `[z,6-6]`
`GMT-14:00`: XSLT 2.0's erratum E29, the hours with the minutes only where the offset has any, and the
minutes coming always once the minimum width leaves room for them. The tests' own comment calls that the
only way to their expectation, and it is the reading taken here.

**A processor claiming 2.0 rounds the fraction.** F&O 3.1 cuts the fractional seconds to the digits the
picture has room for, and this engine did; XSLT 2.0 rounded, and its tests say so: `.456` written with
`[f,1-1]` is `.5` there and `.4` under 3.0. Which rule applies follows the processor claimed, not the
stylesheet's version — the suite runs a 2.0 stylesheet under both and expects each processor's own answer.
A fraction that would round up past what its digits can hold stays at the nines rather than carrying into
the seconds.

`fn/format-date` went from 8 failures to none on the 3.0 run and from 10 to none on the 2.0 one.

### What a package's version is, and what a shadow attribute says

`attr/package-version`, from 10 failures to 1.

**A package's own version is checked.** `package-version` on a used package was read where it was used,
against the range the `xsl:use-package` asked for, and the principal package's was never read at all — so
a principal declaring `package-version="1."`, `"34..99"`, `"-5"`, `"34E9"`, or nothing between the quotes,
compiled and ran. It is `XTSE0020` now, by the grammar of §3.5.1: integers separated by dots, then a name
after a hyphen if wanted. The name is an NCName by XML 1.0's name characters in their fifth edition, which
is the edition the suite leans on: the ideographic description characters and the CJK punctuation it
writes into one version are name characters there and not in the fourth edition's tables that .NET keeps,
and the seven characters of a private-use plane it writes into another are name characters in neither.

**A shadow attribute stands in for the plain one.** Where both `package-version` and `_package-version`
were written, the plain one was being read; the specification says it is ignored (§3.13.2), whatever it
says, and the shadow's static expression is what counts — an error in it, `{1 div 0}`, is the error the
stylesheet gets, and an empty result is no version at all. Two things such an expression may do had nothing
to answer them. `_version`, the version attribute itself computed by a static expression, asked — to be
parsed — for the version in force, which is what it was computing; while it is being read the version is
the processor's own, a shadow attribute existing at no other. And `doc('')` in a static expression, which is
how a stylesheet reads its own attributes before any transformation runs, is answered by the compiler: the
module itself for the empty reference, and the document resolver for any other.

**`xsl:product-version` is the assembly's.** It had been the empty string, on the argument that no version
had been settled on; the suite sets a package version from it, which an empty string is not. It is the
assembly's version in three parts, which is one.

`attr/package-version` went from 10 failures to 1. The one left, `package-version-910`, computes an empty
version when `system-property('xsl:version')` is 3 or more and a valid one otherwise, and expects the
error: it needed the property to answer 3, which was the claim this document deferred. That is decided since
(see *What a processor asked to be 3.0 says it is*), and the set is empty.

### What a 2.0 regular expression reads by

`misc/regex-syntax-xslt20`, from 10 failures to 2, on the 2.0 control.

**A 2.0 pattern is read by XSD 1.0's grammar.** XPath 2.0 defines its regular expressions by XML Schema
1.0 and XPath 3.0 by XML Schema 1.1, and the two differ in three places the suite reaches. A hyphen inside
a character class that is neither a range nor a subtraction — `[a-c-1-4]`, `[0-9-.]` — is refused by
1.1's grammar, and was refused here at every version; 1.0's grammar let a hyphen stand for itself anywhere,
so a 2.0 processor now reads `[a-a-x-x]` as two ranges with a hyphen between, and matches `a-x`. `\i` and
`\c` are XML's name characters, which 1.1 takes from XML 1.0's fifth edition and 1.0 from the fourth: the
ranges here had been the fifth edition's and truncated — nothing above U+200D, so neither the ohm sign nor
a hiragana letter was a name character — and are complete now, while a 2.0 processor reads the fourth
edition's tables instead, which .NET keeps; under those the combining bridge above, U+0346, is not a name
character, and the suite asks for exactly that. And `\p{IsPrivateUse}` is one block under XSD's name three
times over, in the basic plane and in the two planes kept for private use; .NET's block is the first alone,
so the other two are added by hand.

`misc/regex-syntax-xslt20` went from 10 failures to 2. The two left ask for Unicode as it was in 2001:
`[\w]` matching the ceiling brackets U+2308 and U+2309, which were mathematical symbols then and are
punctuation now, and `[\d]` matching the Ethiopic digits, which were decimal digits then and are not now.
This engine classifies characters by .NET's current tables, and answering for a Unicode twenty years gone
is not a thing to build.

### What a document type declaration says

`fn/id`, `fn/unparsed-entity-uri` and the sixty-odd tests across the suite that declared the `dtd` feature,
on both runs.

**The declaration is read; nothing outside the document is fetched unless the caller says so.** DTD
processing had been prohibited outright, on the argument that an external entity is the oldest way of making
a parser read a local file or reach the network. The argument was right about the resolver and wrong about
the declaration. The resolver is the one channel through which a document can cause anything to be read, and
with none an external entity reference expands to nothing and an external subset goes unread — while refusing
the declaration itself turned away every document that carries one, XHTML and DocBook included, for a danger
the null resolver already covered. So the internal subset is parsed now: entities are expanded, default
attributes supplied, and expansion is capped at ten million characters, which is what stops a few declarations
that expand into a billion. What is outside the document is fetched only through `XsltOptions.EntityResolver`,
a fourth resolver beside the three, because it opens a different door — `DocumentResolver` lets the stylesheet
name what is read, and this one lets the *input* name it. It applies to every document the engine parses,
the stylesheet and its modules included, since the question is the same for each; a resource it cannot find
is an error, the caller having said the references were to be followed.

**What the declaration says is kept.** The reader that parses the document applies the declaration but says
nothing afterwards about attribute types or entity declarations, so those are read here from the text of the
internal subset, and of the external one where a resolver fetched it — parameter entities expanded as XML
expands them, conditional sections included or ignored, the first declaration of a name the one that stands.
Three things come of it. Attributes typed `ID` are what `id()` and `element-with-id()` answer for alongside
`xml:id`, and what a pattern anchored on `id()`, refused until now, selects; XSLT 3.0's `id('x', $doc)` names
the document searched, and is `XTSE0340` on a 2.0 run. Attributes typed `IDREF` or `IDREFS` are what
`idref()`, absent until now, answers with — each argument one ID rather than a list, so a string that is not
an NCName refers to nothing. And an entity declared `NDATA` is what `unparsed-entity-uri()` and
`unparsed-entity-public-id()` answer with: the system identifier resolved against the base of the entity that
declared it, the public identifier with its whitespace normalized. A copy of a document node keeps its
unparsed entities (§5.7.1), however it was copied, and the two functions asked with no context node say so
under their own codes, `XTDE1370` and `XTDE1380`, rather than the general one for a missing focus.

**Two things the data model says of a document read with its declaration.** Whitespace-only text inside an
element the declaration gives element content only is element content whitespace, which the data model
excludes (XDM §6.7.3): under `<!ELEMENT doc (item*)>` the newlines between the items are not text nodes,
where in mixed content they are text like any other. And an element parsed out of an external entity has
the entity's URI for its base (XDM §6.2.3) — a processing instruction or a comment as much as an element —
which is what `base-uri()` answers for it, what a relative reference inside it means, and what
`static-base-uri()` is for a static expression written in an external entity of the stylesheet.

**A fragment identifier that is a bare name is followed.** `document('x.xml#name')` had been `XTRE1160`
whatever followed the `#`, there being no IDs to follow it to. A bare name is a shorthand pointer to the
element with that ID and selects it, or nothing where no element has it; any other form is still refused,
and the document's identity is what stands before the `#`, so two references into one document load it once.

The driver no longer skips the `dtd` feature, and resolves what an inline source's declaration names against
the test set's directory. `fn/id` went from 5 failures and 35 skipped to 1 failure — the one left being the
stylesheet-whitespace rule settled later (see *What a comment in a stylesheet does to the text around it*) —
and `fn/unparsed-entity-uri` from 12 skipped to none failing.
Not read, and not to be: an external subset or entity where no resolver was given, which is the point; and
XInclude, which is not a declaration (`fn/base-uri`'s 052). `misc/docbook`'s 001 and 002 ran for the first
time, their stylesheets carrying a declaration, and want `exsl:node-set()`, which this engine does not
provide. The driver now reads a serialization assertion's expected file in the encoding it declares, where
read as UTF-8 a Latin-1 byte was a replacement character and `attr/select`'s 6101 was decided by that.

### What midnight is, and what a duration is written as

`type/date`, to none on both runs. A cluster of five arithmetic and lexical faults, each of them small and
each of them visible in the answer rather than in an error.

**Midnight at the end of a day is a time of none of it.** `24:00:00` is the last moment a day has, and an
`xs:time` has no day to carry it into: the hour comes back to zero where it stands, so `xs:time('24:00:00')`
is *before* `23:59:59` and not a second after it, and subtracting the one from the other is
`-PT23H59M59S`. An `xs:dateTime` does have a day, and there the same hour moves on to the next one, which is
why `1999-12-31T24:00:00` is `2000-01-01T00:00:00` and the same subtraction is `PT1S`. A timezone can then
put two times on opposite sides of the reference day: `24:00:00Z` is that day's own midnight and
`19:00:00-05:00` the next one, so the two are not equal.

**A duration of no length is written in seconds.** `PT0S` for `xs:duration` and `xs:dayTimeDuration` alike,
whichever components the lexical form used to say nothing — `xs:duration('P0M')` and `xs:duration('PT0M')`
are both `PT0S`. Only `xs:yearMonthDuration` says `P0M`, that being the half it counts in and the only half
it can write (XSD 1.0 §3.2.6.2).

**A duration is divided, not multiplied by the reciprocal.** A third is
`0.3333333333333333333333333333` in a decimal and not a third, so the average of `P1DT12H`, `PT12H30M` and
`-PT20M` came out `PT16H3M19.999999999999999999999994S` where dividing gives the `PT16H3M20S` it is. The
scale the arithmetic arrives at is not written either: `PT10.02S` halved is `5.010` in a decimal, which
carries the scale of both operands, and the canonical form has no trailing zeros in its fraction.

**A year-month duration scaled lands on a whole month, rounded the way `fn:round` rounds.** It is a
count of months and nothing finer, so multiplying or dividing one has to choose a month; the
specification says the nearest, and for a value exactly between two, the one nearer positive infinity.
Neither of the two obvious library calls does that. `Math.Round` sends a half to its even neighbour by
default, which made `P2Y11M * 2.3` — 80.5 months — come out `P6Y8M` instead of `P6Y9M`. And
`MidpointRounding.ToPositiveInfinity`, which reads like the rule, is not a rule about halves at all but a
ceiling: it carries 3.1 months up to 4 along with them. What the rule actually is is
`floor(months + 0.5)`, which is how `fn:round` is written here too.

Upwards means towards positive infinity and not away from zero, so the two signs do not mirror one
another: `P1M * 0.5` is `P1M` and `P1M * -0.5` is `P0M`, and `P5M div -2` is −2.5 months and so
`-P2M` rather than `-P3M`. None of this touches a day-time duration, which holds a fraction of a second
and has nothing to round to: scaling one is exact.

**A moment moves by ticks and not by seconds.** `DateTime.AddSeconds` takes a double, and 446400.3 is not
one: adding `P5DT4H0M0.3S` to `00:12:00Z` truncated to 4,464,002,999,999 ticks and landed at
`04:12:00.2999999Z` rather than the tenth of a second the duration names. The seconds are held in a decimal
for exactly this reason and are now converted to ticks in it.

**The am/pm marker is spelled to the width asked for.** The marker has no one spelling, so a width chooses
between the spellings rather than cutting one of them: `[PN]` is `A.M.` and `[PN,3-3]` is `AM`, where
cutting would have left `A.M`. Nor is a minimum padded afterwards, which would put a trailing space on every
`[PNn,3-3]`. A maximum below two still cuts, to `A`.

`type/date` went from 7 failures to none on both runs, the same seven on each.

### What namespaces an element has, and what a name test may leave out

`type/namespace` to none on the 3.0 run and one on the 2.0 one, `decl/strip-space` to one and two. Four
things, each of them about which namespaces are in play where.

**No prefix is bound that the stylesheet did not declare.** XPath's own static context predeclares `xs` and
`fn`, and 3.0 adds `map`, `array` and `math`; XSLT replaces that component of the context with the in-scope
namespaces of the element the expression is written in, and nothing besides (XSLT 3.0 §5.4.1). So
`fn:current-dateTime()` where no `fn` was declared is `XPST0081` and not a call, which `type/namespace`'s
6202 asks for. The unprefixed spelling needs nothing declared, the standard function namespace being the
default for a function name, and that is the spelling almost every stylesheet writes. The same rule holds
for the expression an `xsl:evaluate` is handed, whose namespaces are what its `namespace-context` carried or
what was in scope where it was written. This engine had bound all five without being asked, and said so.

**A name test in `xsl:strip-space` is a name test.** The `elements` attribute holds name tests and not
names, so `*:a` is the element of that local name in any namespace at all — not a prefix the stylesheet
forgot to declare, which is what it had been refused as — and `Q{uri}a` says the same thing without a prefix
in scope to say it with. An unprefixed one is in the namespace `xpath-default-namespace` declares rather
than in none. `*:a` and `p:*` are equally specific, between a test that gives both halves and a bare `*`,
which is the order of the priorities a name test carries as a pattern. And naming one test in both an
`xsl:strip-space` and an `xsl:preserve-space` at one import precedence follows the **processor**: the later
declaration wins at 2.0 and it is `XTSE0270` from 3.0, the suite running one stylesheet under both and
expecting each processor's own answer.

**An element that has no default namespace node says so.** The one namespace XML 1.0 can undeclare is the
default, and there are two ways for an element to be without it while something above it has one. Under
`inherit-namespaces="no"` the children take none of the element's namespaces, so a child written with a
prefix carries `xmlns=""`; and an element copied out of a document that undeclared the default keeps that
undeclaration, its namespaces being its own. What decides the second is where the copy was attached, not
what the copied parent declared: `xsl:copy-of` of a whole document attaches it to a document node, which has
no namespace nodes to inherit, while an `xsl:copy` that rebuilds the parent makes the inner element the
child of a newly constructed one and it takes that element's namespaces, the default among them — which is
why `type/namespace`'s 3001 keeps the undeclaration and its 3002, 3003 and 3004 lose it (XSLT 3.0 §11.9.2).
The serializer holds the start tag open until nothing can change it, which is also what the next paragraph
needs.

**A namespace node may take the prefix the element's own name was written with.** Three tests, three
answers. Where the element has a namespace node of its own for that prefix — a literal result element
carries the stylesheet's — a second one of the same name and a different value is a contradiction:
`XTDE0430`, which is 2618. Where it has none, because `exclude-result-prefixes` took it away or because
`xsl:element` copies no namespaces, the node stands and the element's name gives way to a prefix nothing is
using, `p` becoming `p_0`: that is 2614 and 2615, and the specification asks for it rather than for a
refusal (§11.7). And a default namespace given to an element that is in no namespace is neither, having a
code of its own: `XTDE0440`. All three had been raised with no code at all, which the suite counted as a
skip rather than as a pass.

`type/namespace` went from 7 failures to none on the 3.0 run and from 6 to 1, and `decl/strip-space` from 6
to 1 and from 4 to 2. Five more tests ran on each, and all five pass: an error raised without a code is
neither right nor wrong to a driver that was asked for one, so it was counted as unjudged, and the three
codes above take those tests out of the skips.

What is left: `decl/strip-space`'s 023 wants an initial context item that space-stripping removed; its 025
asks a 2.0 processor to read the `Q{uri}local` name form, which here follows the processor's version and not
the stylesheet's claim (see *Which version's vocabulary a processor reads*). `type/namespace`'s 0912 is a 3.0
stylesheet read by the 2.0 run, where `xsl:mode` is ignored and the built-in rules answer instead.

### What a union in a match is, and what a next match carries on from

`insn/next-match`, to none on both runs. One rule of the specification, and everything in the cluster came
back to it.

**A union in a `match` is several template rules and not one.** `match="p|element(p)"` declares two rules
with one body, each taking the default priority of its own alternative (XSLT 3.0 §6.4) — which is the whole
point of the rule, since the alternatives are rarely equally specific and a single rule would have to pick
one number for both. So the two are entered separately, and an `xsl:next-match` in the body goes from the
one to the next before it reaches another template at all. A priority written on the template applies to
every alternative, and then there is nothing left to tell them apart: they are one rule again, which is what
keeps `xsl:next-match` from entering the same body twice and keeps every matching node from being a conflict
between a rule and itself.

**The current template rule is one pattern and not one template.** The engine had recorded which template
was running, so a next match passed over every alternative of it at once, and — where the running template
had two alternatives that both matched — restarted at the first of them each time, which is a loop. It now
records the rule: the pattern, the template and the mode. That is also what `xsl:apply-imports` and a mode's
`on-multiple-match` are answered from.

**A kind test scores by how much it says** (XSLT 3.0 §6.5). `element(x)` names one of the two things it
could and takes the 0 a name test takes; `element(x, T)` names both and takes 0.25, above anything else a
single step can be; `element()` and `element(*)` name neither and are wildcards over their kind at -0.5.
`document-node(E)` is as specific as the element test inside it, and `processing-instruction('p')` scores as
a name test where a bare `processing-instruction()` does not. All of them had been -0.5, so a rule that
named an element lost to one that named any node.

**A match pattern is read once the whole stylesheet is in.** A pattern is an expression like any other and
may call a stylesheet function, and a stylesheet's declarations are not written in an order a reader is
entitled to depend on — so a template whose pattern called a function declared below it was refused. The
patterns are now read in a pass of their own, after every declaration and before any body, where an
`xsl:key`'s own pattern was already being read.

The set went from 7 failures to none on the 3.0 run and from 6 to none on the 2.0 one, and `attr/match`'s
260 came with them: a mode declared `on-multiple-match="fail"` now refuses two rules of equal rank over an
item that is not a node, as it already did over a node.

### What json-to-xml was told to do

`fn/json-to-xml`, to one failure. Everything in this cluster is an option the function takes and had been
reading past.

**A repeated key means what the call says it means, and the two functions do not offer the same answers.**
A map holds one entry per key, so `fn:parse-json()` has to choose between two and defaults to `use-first`,
the answer that does not depend on how far the parser got. The XML representation has no such difficulty —
two elements can carry one key — so `fn:json-to-xml()` keeps both by default and takes `retain` where the
other takes `use-last` and `use-any` (F&O 3.1 §17.5). `reject` is `FOJS0003` in both, an answer belonging to
the other function is `FOJS0005`, and something that is not a string at all is `XPTY0004`.

**A character XML cannot hold is escaped or replaced, and the call says which.** JSON admits the C0 controls
and an unpaired surrogate; XML admits neither. Asked to `escape`, the string keeps the JSON escape the
character was written with — and every backslash doubles, so that what comes out can be read back — and the
element says `escaped="true"`, or `escaped-key="true"` where it was the key. Not asked, the escape sequence
goes to the `fallback` function, which by default answers with the replacement character. Both options are
read by the option parameter conventions, so an empty sequence, two items, or the wrong type is `XPTY0004`
rather than the option being ignored. A form feed had been written into the tree as itself, which is not a
character an XML serializer can write: three tests the suite could not put an assertion to now run.

`fn/json-to-xml` went from 9 failures to 1 on the 3.0 run, and three of its tests moved out of the skips. The one
left asks a processor that is not schema-aware to accept an `xsl:import-schema` and then refuse the
`validate` option; this engine refuses the declaration, which XSLT 3.0 §3.14 gives `XTSE1650` for.

### What a stylesheet says it will be run against

`decl/global-context-item`, to none. Four rules of one declaration, each of them a code the engine was
giving something else for or not giving at all.

**One declaration per package and no more.** A module carrying two `xsl:global-context-item` declarations is
refused whether or not they agree, and two modules of one package that disagree are refused as well, there
being no rule for which of them the package meant: `XTSE3087` for both (XSLT 3.0 §3.5.2). Saying the item is
absent and declaring a type for it is a contradiction, and the two elements that can say it have a code
each — `XTSE3089` on `xsl:global-context-item` and `XTSE3088` on `xsl:context-item`, which is what the suite
settled on and this engine had given to both.

**A library package's declaration is not the transformation's.** What the transformation is run against is
the top-level package's business, so a used package's declaration is passed over — unless it says the item
is *required*, which is a promise a library is in no position to make, and is `XTTE0590`.

**What the caller handed in is the caller's mistake.** A source document that does not match the declared
type is `XTTE0590` and not `XTDE3086`, which is the code for a stylesheet that needs a source document and
was started without one. The two are different complaints and only the second is about the stylesheet.

**A global variable sees what the declaration said it would see.** A global is evaluated in a context of its
own, built where it is forced, and `use="absent"` had reached only the context the templates run in — so a
stylesheet that declared it would be given no context item could still name the source document from a
global. It cannot now, and `/` there is `XPDY0002`, which is one of the two answers the suite accepts.

`decl/global-context-item` went from 7 failures to none.

### What a function declaration may say, and what an empty text is

`decl/function`, to two failures on each run. Five small things, and one of them reached rather further than
the set it was found in.

**A function's parameter is required, and may say so from 3.0.** Every parameter of a function is required
already — a call supplies one argument for each, or it calls a different function — so the only thing
`required` can say is so. XSLT 3.0 admits the attribute for that; 2.0 does not admit it at all and refuses
it as an attribute the element has not got, `XTSE0090`. `required="no"` says something a function's parameter
cannot be, and is `XTSE0020`.

**`override` and `override-extension-function` may both be written.** They are one attribute under two names,
3.0 having renamed the old one for saying what it does not mean — what it settles is whether the stylesheet's
function is preferred to an extension function of the same name, and nothing about overriding another
stylesheet function. Writing both is how one stylesheet serves a 2.0 processor and a 3.0 one; what it may not
do is write two different answers.

**`element-available()` reads a name with no prefix as an element name.** So it means whatever an element
written that way would mean where the call is: the default namespace, and not no namespace. Everything else
that takes a name this way is asking about something an element declaration says nothing about, and reads a
bare name as being in no namespace still.

**A cached function keys a QName by the name it is.** `cache="yes"` remembers what a call answered, keyed on
the arguments; a QName was keyed on its lexical form, so two elements called `x:alpha` in different
namespaces were one key between them and the second call was answered with the first one's result.

**An empty `xsl:text` is a zero-length text node at every version.** It is nothing once in a tree — an empty
text node has nothing to contribute to one — but it is an item in a sequence, which is what a function
returning three of them returns three of. This engine had made it a node only at 3.0, and that one line
reached `insn/construct-node`, `attr/select` and `misc/bug` as well as the set it was found in.

`decl/function` went from 7 failures to 2 on the 3.0 run and from 5 to 2 on the 2.0 one. Both of what is
left on each side are inventories: 1901 and 2001 list the F&O and XSLT functions a 3.0 processor should
have, which is the work remaining rather than a fault, and 0302 and 1902 ask a 2.0 processor to read 3.0
syntax — `||` in an XPath expression and `expand-text` on an `xsl:message` — which this engine reads by the
version it is claiming (see *Which version's vocabulary a processor reads*).

### What numbering anywhere counts, and what an iteration leaves behind

`insn/number`, to two failures on each run, and `insn/iterate` with it.

**`level="any"` counts the current node and its preceding and ancestor axes** (XSLT 3.0 §12.4.2), which in
this engine's preorder numbering is every node whose id is below the current one — a following node's id is
above it and a descendant's is inside its range, so the whole set is a scan over a range and never a tree
walk. What that scan had missed is that an attribute is numbered outside the preorder sequence, so its id
says nothing about where it stands: numbering an attribute counted the whole document rather than what
precedes the element it hangs off. Neither axis holds an attribute or a namespace node either, so one is
counted where it is the node being numbered and nowhere else — which is what makes `count="chapter"` over an
attribute say which chapter it is in.

**A `from` pattern is found on the way rather than by a scan backwards.** The last node matching it before
the current one is simply the last one the walk meets, and the count restarts there — counting that node
itself where it also matches `count`, since `from` says where the numbering begins again and not which node
to leave out. Written this way it works for an attribute as it does for anything else, which the backwards
scan over ids could not.

**`xsl:on-completion` has no focus** (§8.3). The iteration has finished, so there is no item it is standing
on, and letting the one the loop was entered with show through let an `xsl:number` in there number a node
the instruction is not about. What it does have is the parameters, which is the whole point of the element.

`insn/number` went from 6 failures to 2 on the 3.0 run and from 5 to 2 on the 2.0 one, and `insn/iterate`
from 6 to 3. What is left: 0111 asks for integers beyond the eighteen digits a 64-bit one holds, which is
the same limit the QT3 run reports; 2506 asks for German number words, which English was the one language for
when this was written and German has joined since (see *What language a number is spelled in*); and 1004 on
the 2.0 run wants a 2.0 processor
to read an `xsl:iterate`.

### What a type annotation says where nothing was validated

`type/type`, to one failure on each run. Three things about naming a type and naming a name.

**An element test may say what the node was validated against.** Nothing here was validated, so every
element is annotated `xs:untyped` and every attribute `xs:untypedAtomic` (XDM §5.2). `element(*, xs:untyped)`
and `element(*, xs:anyType)` therefore match an element, `attribute(*, xs:untypedAtomic)` and everything
`xs:untypedAtomic` derives from match an attribute, and a test naming anything else matches nothing at all —
which is the honest answer from a processor with no schema to have validated against. The two names of the
types nothing carries, `xs:anyType` and `xs:anySimpleType`, are admitted in a test for that reason though
neither is a type a value can be.

**A cast to `xs:QName` resolves its prefix where the cast is written.** A prefix means whatever it meant
there, so XPath 2.0 would cast only a string literal — the one thing the compiler could read — and XPath 3.0
kept the static context as the answer and dropped the restriction. The bindings in scope now travel with the
cast, so ` cast as xs:QName` works from 3.0 and is `XPTY0004` below it. Which version is on offer decides,
not the version the stylesheet claims: the suite runs one stylesheet under both and expects each processor's
own answer.

**`fn:resolve-QName()` of something that is not a lexical QName is `FOCA0002`.** Nothing is being cast there,
so the `FORG0001` of a failed cast was the wrong complaint.

`type/type` went from 6 failures to 1 on the 3.0 run and from 5 to 1 on the 2.0 one. The one left was 0125,
which turns on text either side of a comment in the stylesheet being one text node — settled next, and with
it `misc/whitespace`'s 012 and 013 and `fn/id`'s 016.

### What a comment in a stylesheet does to the text around it

Nothing, which is the point, and it took six tests across five sets to say so.

A stylesheet is prepared by **removing its comments and processing instructions and only then stripping
whitespace-only text** (XSLT 3.0 §4.2). The order is what matters. A tree holds no two adjacent text nodes,
so by the time the stripping happens what stood on either side of a removed comment is one text node — and
whitespace beside something that is not whitespace is kept along with it. So
`<e>   h<!--c-->   </e>` writes `   h   `, exactly as the same element written without the comment does, and
`<e>   <?pi?>h</e>` writes `   h`. This engine had stripped each text node on its own, so a comment in the
middle of some text silently took the whitespace on one side of it away.

An element does divide one run from the next, being a node the preparation leaves standing, so the
whitespace on either side of one goes as it always did.

`misc/whitespace` went from 3 failures to 1 on each run, and the same line settled `type/type`'s 0125,
`fn/id`'s 016, `expr/axes`'s 090 and `misc/seqtor`'s 043h — five sets that had each been reporting the one
rule in its own words.

### What a transformation run from inside one hands back

`fn:transform()` runs a second transformation from inside an expression and hands back a map of what it
produced. It is the last whole function of 3.0 this engine lacked, and the whole of its coverage is the
nine tests of `fn/transform`.

**The map is keyed by where each result document would have gone.** The principal result goes under
`output` and every secondary one under the URI its `href` resolved to, which is what makes `$r?output` and
`$r('http://example.com/out.xml')` the two halves of one answer. A transformation run this way writes
nowhere: `xsl:result-document` builds a value and puts it in the map, so no `XsltOptions.ResultResolver` is
involved and none is needed.

**`delivery-format` decides what a document is**, the same choice for the principal result and for every
secondary one. `document` is the result tree, a document node to navigate; `serialized` is what the
serializer wrote, declaration and all, as one string; `raw` is the sequence the transformation returned,
whatever is in it — an integer stays an integer, and a function item stays callable.

The stylesheet is named by `stylesheet-location`, `stylesheet-text`, `stylesheet-node` or a `package-name`
with a `package-version` range, exactly one of them, and is found through the resolvers the calling
stylesheet was configured with and no others: what the transformation may reach is what its caller could
reach. A stylesheet that cannot be retrieved is `FOXT0002`. One named twice, or not named at all, or an
option that is not one of the specification's, or one whose value is of the wrong type, is `FOXT0004` —
though a value is atomized first, since an option written as the content of an `xsl:map-entry` rather than
in its `select` is a text node and what it says is its string value. An error the transformation itself
raises is reported as itself: a private named template asked for as an entry point is `XTDE0040` from
inside, and a code of `fn:transform`'s own wrapped round it would hide which of the two stylesheets was
wrong.

**An option this processor cannot honour is refused rather than passed over**, with `FOXT0001`, the code
for a transformation it cannot carry out: `initial-function` and `function-params`, which start at a
function rather than at a template, and `post-process` and `requested-properties`. Accepting an option and
doing nothing about it would answer a question that was never asked. Everything else the specification
lists is honoured — `source-node`, `initial-template`, `initial-mode`, `initial-match-selection`,
`template-params`, `tunnel-params`, `stylesheet-params`, `static-params`, `base-output-uri`,
`serialization-params`, `enable-messages`, and `xslt-version` checked against what this processor
implements. The two parameter maps are one channel here, the engine taking a supplied parameter at
whichever point the declaration reads it; and where no `base-output-uri` was given a relative `href`
resolves against the stylesheet's own URI, the specification leaving that to the processor.

Two things below the function had to change with it, and both are worth having on their own.

**A function item carries the transformation it was made in.** Raw delivery may hand back `f:negative#1`,
and calling it happens after the transformation that made it has ended — in a `use-when`, say, which is
answered before anything runs at all. So a reference to a function the stylesheet declares is made where it
is written, as a reference that reads the focus already was, and takes the runtime and the global variables
with it. It had been a compile-time constant whose body had nothing to run in.

**A template declaring its result type is not in temporary output state.** The type is a check on what the
body produced, not a variable to produce it into, so an `xsl:result-document` inside such a template writes
a document of the transformation's own. The capture the check is made through had made the body look
temporary and refused it — which any stylesheet would have met, not only one run this way.

### What one function's type is, against another

A function item carries the types it was declared with, and two questions turn on them: whether it is an
instance of a function type, and what happens when it is handed to one.

**`instance of function(A, B) as R` is a subtype judgement and not a count of arguments.** XPath 3.1
§2.5.6.2 makes a function type **contravariant in its arguments and covariant in its result**: one function
is within another where it accepts everything that one accepts and returns no more than that one promises.
So a function of type `function(xs:long, xs:NCName) as element(e)` is within
`function(xs:int, xs:NCName) as element(e)` — a *narrower* required argument answers true — and is not
within `function(item()*, item()*) as element(e)`, which reads backwards until you ask what would happen if
it were the other way round. The occurrence indicator counts as much as the item type: a function returning
`element(e)?` is not one returning `element()`.

A type nobody wrote is `item()*`. An `xsl:function` without `as`, and a parameter without one, therefore
give a function that is an instance of `function(*)` and of `function(item()*) as item()*` and of nothing
narrower.

**An item whose types this engine does not record is admitted by its arity**, as it always was: a call into
the standard library, a partial application, the function a map or an array stands for. The alternative
would be to invent an answer, and inventing "no" refuses working stylesheets.

**A function item handed to a function type is coerced, not refused** (§3.4.2). It is wrapped in a function
that *is* of the required type and calls the item inside it, converting each argument on the way in and the
result on the way out by the function conversion rules. So `string-length#1` passed where
`function(xs:string) as xs:string` was asked for is accepted, and fails with `XPTY0004` when it is called
and returns an integer — the check moves from where the item was passed to where it is called, and a
function that is never called never fails at all. The argument is converted to the required type before the
function inside ever sees it, which is what makes `function($x as xs:float) {…}` refuse the `xs:double` that
a `function(xs:double) as xs:double` hands it, where the `xs:decimal` written at the call site would have
been promoted to a float quite happily.

Four smaller things settled with them.

**A partial application is nobody's named function.** `contains(?, 'e')` is a new function that nobody
named, so `fn:function-name()` of it is the empty sequence; this engine had been passing the underlying
function's name through.

**A global variable is not in scope within its own declaration** (XSLT 3.0 §9.7), so an inline function
bound to a global cannot recurse by naming the global it is being bound to. That is `XPST0008` — a
reference to something not declared where it stands — rather than a definition that quietly works, and it
is what `decl/variable`'s 0118 asks as well.

**Atomizing a function item is `FOTY0013`.** A value comparison atomizes each operand before comparing
them, and this engine had been comparing the item itself and reporting the pair as one that names no
comparison. What is wrong there is the operand, not the pairing.

**A named function reference may ask for as many arguments as the function takes.** `fn:concat` takes any
number of them, so `concat#123456` names a function that exists and the item is that call built with one
argument position per unit of arity. Past a million of them the reference names nothing this processor will
build, which is `XPST0017`.

And one thing found on the way: a kind test's type annotation now reaches the written form of the test, so
that `element(e)` and `element(e, xs:anyType)` are told apart where two tests are compared as written. They
are different tests — the first is nillable and the second is not — and had been comparing equal.

`expr/higher-order-functions` went from 9 failures to none, and `decl/variable` from 1 to none.

### What a static expression may see, and where one is looked for

A `use-when` and a shadow attribute are answered while the stylesheet is being prepared, before anything
runs. Two things follow from that, and this engine had been lax about both.

**The static context of a static expression is a narrower one.** Every function that reads the source
document or the state of a running transformation has nothing to read there, so calling one is `XPST0017`
where it is written: `current()`, `key()`, `unparsed-entity-uri()`, `unparsed-entity-public-id()`,
`regex-group()`, `current-output-uri()`, the current group and the current merge group, and the accumulator
functions. `generate-id()` is the one the version decides — it was XSLT's own function about the document
being transformed until 3.0 made it `fn:generate-id`, a function of the node it is given and of nothing
else — so a 2.0 processor refuses it in a static expression and a 3.0 processor does not.

**`function-available()` written inside one has to agree.** It is asking what *that expression* may call,
not what the stylesheet at large may, and the two static contexts are different. This is the one place
where the answer depends on where the question was written, and it is the whole of what the suite's
use-when-0407 checks: the same six names answered true in a template body and false in the `use-when`
beside it.

**Under XSLT 2.0 a static expression is answered with no documents available at all** (§3.13.2), so
`doc()` finds nothing there and `doc-available()`, which asks by trying, answers false however readable the
document is. XSLT 3.0 lifted that, and lifting it is what lets a shadow attribute read `doc('')` — the
module it stands in (see *What a package's version is, and what a shadow attribute says*).

**And every `use-when` in a module is answered, not only the ones the compiler walks past.** XSLT 3.0 §3.11
answers them while the stylesheet is being prepared, so an element inside an `xsl:fallback` that nothing
will ever fall back to still carries one — the instruction it stands in is implemented, the fallback will
never run, and a mistake in the `use-when` is a mistake all the same. They are now settled on a walk of
each module's declarations, which stops at an element the attribute removes: what is inside a removed
element is never read at all, which is the whole point of the attribute.

`attr/use-when` went from 4 failures to 1 on the 3.0 run and from 6 to 1, with `fn/function-lookup`'s 003
on the same line. The one left on each is 0106, which turns on `system-property('xsl:vendor-url')`
containing `http`: this engine answers the empty string, there being no public address for it to give, and
that is a choice recorded above rather than a rule it gets wrong.

### What a double really is when it is rounded

**A double is a binary fraction and almost never the decimal it was written as**, and rounding is where the
difference shows. `250.025` is really 250.0250000000000056843…, which is *above* the half and rounds up;
`150.015` is really 150.0149999999999863…, which is below it and rounds down; `13.65` is really
13.6500000000000003552…, so `round(13.65, 1)` is 13.7 and `round(-13.65, 1)` is −13.7 rather than −13.6.
None of the three is the tie it looks like.

Scaling by a power of ten and rounding cannot see any of that. Multiplying `250.025` by a hundred lands it
exactly on 25002.5, because the difference is smaller than the spacing of doubles at that size, and the
answer is then decided by a tie that was never in the value. So the value is taken as the rational it is —
a mantissa over a power of two — and compared against the half on integers, which lose nothing; the result
goes back through its decimal text rather than through a division, so it is the double nearest the rounded
value rather than one arrived at by dividing by a power of ten that is not itself exact. At the point,
where there is no scaling to do, the fractional part answers on its own: `Math.Floor(x + 0.5)` is not that,
since adding a half to the double just below one carries into the next integer.

`round()`, `round-half-to-even()` and the two-argument forms of both go through it. An `xs:decimal` and an
`xs:integer` are still rounded in their own arithmetic, which is exact already and keeps the type.

**`fn:number()` is a cast to `xs:double` from XPath 2.0 on**, and the lexical space of `xs:double` has
`INF`, `-INF` and `NaN` in it. XPath 1.0's own grammar for a number has a sign, digits and a point and
nothing else, so a stylesheet at 1.0 reads all three as the words they are and answers NaN — which is also
what the cast answers when it fails, that being the one thing the function keeps from 1.0.

`expr/math` went from 6 failures to 2 on the 3.0 run and from 3 to none on the 2.0 one. The two left are
both about something other than arithmetic. 3601 rounds a twenty-one-digit integer, which the engine now
holds; what it still does not do is keep it through `math:pow`, which works in doubles and hands back
`1.2345678901234578E20` where the digits were wanted. 3702 writes
`extension-element-prefixes="xs"` with `xs` bound to the schema namespace and wants `XTSE0085` for it —
and the suite asks for three different codes for that one construct: `XTSE0800` from
`fn/extension-functions`'s 0105, where an element in that namespace is then used as an instruction, and
`XPST0017` from `decl/function`'s 1023, where the declaration is incidental and a later expression is what
the test is about. This engine raises the error where the extension element is used, which is what the two
of the three that are *about* the construct ask for.

### What being available as a stream means to an engine that does not stream

`fn:stream-available()` is the one streaming function this engine answers, and it answers it properly.

**The question is about the document, not about the processor.** A stylesheet calls it to pick a route
before committing to one, and a processor that answered false to everything would be answering a question
nobody asked — the suite's own six tests for it carry no streaming dependency and expect four different
documents to be told apart. So the answer is whether the document is *there* and *begins as XML*.

**Reading stops at the first element and goes no further.** That is what makes a document which is well
formed for its first mile available: a truncated document and one with two top-level elements are both
available, because whatever is wrong with them lies past the point a streamed read would have reached
before it had to answer. A file that is not XML at all is unavailable, and so is one that is a document
type declaration and nothing else — there is no element in it to reach.

Nothing is available without an `XsltOptions.DocumentResolver`, as nothing is readable without one, and a
call in a static expression has no resolver to ask and answers false.

`fn/stream-available` went from 6 failures to none. It changes nothing about **streaming itself**, which
stays the one omission this engine chose: 2,900 tests ask for the feature and are skipped, and every
accumulator is still computed over a tree already held.

### What an attribute is called, and what a sequence of nodes is not

**An attribute's prefix is a spelling of its namespace and nothing more.** `xsl:attribute` may be given a
prefixed name and a `namespace` that do not go together — <code>name="p:local" namespace="urn:two"</code>
where `p` is bound to something else — and the namespace is the part that was meant. Where the prefix is
already spoken for on the element, one element cannot declare it twice, so the attribute takes a prefix of
its own, keeping what was written as a stem. The same collision on an element's own name is settled the
same way, and both the serializer and the tree builder now settle it, since a stylesheet can ask what an
attribute is called before anything is written.

**HTML's boolean attributes are written as their name alone.** The only value such an attribute is allowed
is its own name, so `checked="checked"` says nothing the bare `checked` does not, and the minimized form is
one of the things the HTML output method exists to produce (XSLT 1.0 §16.2). XHTML is XML and takes no such
shortcut.

**A sequence of nodes is not a document to navigate from.** A sequence constructor with a declared type
produces a sequence, and the nodes in a sequence are parentless — so an element built by
<code>&lt;xsl:variable as="element()"&gt;</code> has no document node above it, and a leading `/` inside it
names nothing. That is `XPDY0050` rather than an empty answer, `/` being `fn:root(self::node())` *treated
as* a document node (XPath 2.0 §3.2) and this being the treat failing.

**And a sequence of nodes is not a set of strings.** `xs:untypedAtomic` is a type of its own, not a kind of
string: what an element, an attribute, a text node or a document node atomizes to is an instance of the one
and not of the other. A comment, a processing instruction and a namespace node are the other way about —
their typed value is an `xs:string` (XDM §5), being the three whose content was never a candidate for
validation, so there is nothing a schema could have said about them and the type they have is the type they
always have.

**An `xsl:sequence` with a `select` may hold nothing but an `xsl:fallback`.** XSLT 2.0 gave its content no
meaning at all, so content beside a select is content where the element allows none — `XTSE0010`, where 3.0
has a code of its own for the same mistake. Content with no select is read as 3.0 reads it at either
version, refusing it being the wrong answer to a stylesheet the suite expects to run and then fail on its
type.

The driver changed with them: **a source document is read in the encoding it declares**. The bytes are what
the file holds and an XML document says what they mean; read as UTF-8, a Latin-1 byte above 0x7F became a
replacement character and a test was decided by the driver's mistake rather than by the stylesheet.

`insn/attribute` went from 4 failures to none on each run and `insn/sequence` from 4 to 1 and from 5 to 1 —
the one left over being a driver limit rather than an engine one: an `assert-xml` on a stylesheet whose
output method is HTML compares the serialized HTML, and that carries the `meta` element the method is
required to add while the expected result is the tree before serialization. Beyond the two sets,
`misc/bug`, `insn/sort` and `attr/strip-type-annotations` came with them, and seven more tests on each run
stopped being skipped for a source document that would not parse.

### What `doc()` is asked, and what it answers against

**No URI names no document.** `fn:doc()` takes an `xs:string?`, so the empty sequence is a value it may be
given, and the answer is the empty sequence — `doc(())` is an instance of `document-node()?` and of
`empty-sequence()` both. Reading the argument's string value instead made `doc(())` the same call as
`doc('')`, which is the module the expression was written in: a document, and the wrong one.
`doc-available(())` is false for the same reason.

**A relative reference resolves against the base URI where the call is written**, which an `xml:base` may
have moved. That is the base `document()` has always read one against; `doc()` was reaching the library
form, which is built from values alone and had no stylesheet to ask, so it resolved against wherever the
transformation started — the principal stylesheet, whatever module the call actually stood in. The base is
now handed to it where the call is compiled.

`fn/document` went from 5 failures to 2 on the 3.0 run and from 3 to none on the 2.0 one. The two left are
`document()` called from two packages that declare different `xsl:strip-space`, which asks for the same
document to be stripped differently in each: whitespace control here belongs to the stylesheet rather than
to the package, packages being flattened into one at compile time (see *What a package keeps to itself*),
and telling them apart would mean a control per package and a document cache keyed by it.

### What backwards compatibility converts

Backwards compatibility is XSLT 2.0's imitation of XPath 1.0 within 2.0's data model, and not 1.0 itself.
Where 2.0 checks a type and refuses what does not reach it, 1.0 **converts until it fits and cannot fail**
— which is the whole of what the mode is for, since a stylesheet written for 1.0 never had a type error to
meet here. This engine had been doing neither: an argument in a backwards-compatible call was passed
through untouched.

**A function's arguments are converted by XPath 1.0's rules** (XPath 2.0 §3.1.5), which are three:

- A sequence where one item was expected is replaced by its **first item**. So `base-uri(//item)` is the
  first item's base URI rather than a type error, and `string-length(('abc','de'))` is three rather than
  five. A position that takes a sequence takes the sequence, the rule being written for an expected type of
  one item or none: `string-join(nodes, '-')` still joins the nodes.
- A value where a **string** was expected is converted by `fn:string()`.
- A value where a **number** was expected is converted by `fn:number()`.

**A general comparison with a number on either side compares numerically**, and the conversion is to
`xs:double` — whose lexical space has an exponent in it, so `1 = '1.0e0'` is true here. XPath 1.0's own
grammar for a number has no exponent at all and would read that string as NaN, which is what `number()`
called in the same stylesheet still does: the language converting an operand on the stylesheet's behalf and
the stylesheet calling a function are two different things, and only the first is 2.0's.

The driver changed with them: **a source document written into the catalogue is told where it was written**.
Such a document has no file of its own, and what it is relative to is the catalogue it stands in, so
`base-uri()` of a node in one now answers something rather than nothing.

`misc/backwards` went from 4 failures to 1 on the 3.0 run and from 3 to 1 on the 2.0 one, and
`expr/xpath-compat` and `misc/xslt-compat` came with it. The one left over was 012, which sorts a numeric
key in a 1.0 stylesheet and wants it ordered as a number. That was read here as the mode restoring XSLT
1.0's default of `data-type="text"`, and it is not: the specification leaves `data-type` in the language
for that very purpose and gives the mode one effect on a sort, which is a different one. Corrected under
*What compatibility settles about a sort, and what a context item may be*.

### What may be said once, and what may not be said at all

Four conditions the specifications name and this engine had let pass. None of them is a feature: each is a
stylesheet or an invocation saying something that has no answer, and what it gets back is a code rather
than a guess.

**`xsl:param` has no `visibility` attribute.** A stylesheet parameter is a component of its package like
any other, but what a component is visible as is said by an `xsl:expose` and not on the declaration
(§3.5.2) — so `visibility` written there is an attribute the element does not have, which is `XTSE0090`.
`xsl:variable` does have one, and that is what makes this worth a check rather than a shrug: it is the
element that decides and not the spelling.

**An `xsl:accept` may not name a component the same `xsl:use-package` overrides.** The accept says what to
make of the component as the package it comes from wrote it; the override replaces it with one this package
writes. About one symbolic name in one `xsl:use-package` the two contradict each other, and the
specification refuses the pair rather than settling which of them wins (`XTSE3051`). A wildcard accept is a
blanket statement about whatever is there and says nothing about any component in particular, so it stands.

**An invocation names one way in.** XSLT 2.0 let a transformation begin at a named template or in a named
mode and settled no order between them, so naming both is `XTDE0047`. 3.0 dropped the error — a mode is
worth naming alongside a template for what the template's own `xsl:apply-templates` does with it — which
makes this one of the few conditions that has to be checked against the *processor's* version rather than
the stylesheet's claim.

**One transformation does not both read and write a document.** Whether the read saw what the write put
there would depend on an order nothing settles, so `XTDE1500` refuses the pair — in either order, and under
whichever URI the two resolve to. Reading is recorded where a document is loaded and writing where a result
document's `href` is resolved, which is the same place a second write to one URI is already caught.

`misc/error` went from 5 failures to 1 on the 3.0 run and from 1 to none on the 2.0 one, with `decl/accept`
and `decl/param` coming with them. The one left over is 0640o-2, which supplies a value for a static
parameter whose default refers forwards to a variable declared below it, and wants the forward reference
reported all the same. Reporting it costs `static-003a`, whose own description says that such a reference
"is not an error anymore" when a value is supplied: the two tests ask for opposite answers about one shape,
and the value supplied is what this engine goes by.

### What a message is, and what a template says it stands on

**A message is a document node.** Its content is built by the rules everything else built from a sequence
constructor is built by (§5.7.1) — adjacent atomic values separated by a space, adjacent text merged, nodes
copied — rather than flattened to a string on the way out. A stylesheet may therefore be asked what is *in*
a message, which is what the suite's version-017 does: it writes `<xsl:message select="//b">Another
message</xsl:message>` and then asks for `/b[='3']` and for the text node after it.

**The `select` is the first thing the message constructs, not an alternative to the content.** Here
`xsl:message` is unlike `xsl:value-of` and `xsl:comment`, which take one or the other and refuse both: a
message written `<xsl:message select="'message 1: '">A message</xsl:message>` reads *message 1: A message*.
The `select` had been ignored outright, so every message given as an expression was empty.

**The content travels with a terminating message**, as `$err:value` for whoever catches it — the sequence
the instruction built and not a rendering of it, so an `xsl:catch` may go into what the message said. The
third argument of `fn:error()` arrives there for the same reason and had been ignored in the same way. An
error this engine raises on its own account still carries a message and nothing else.

**A message that cannot be produced does not stop the transformation**, which XSLT 3.0 says in as many
words. The guard covers rendering as well as evaluation, a map being a value the instruction may be given
and no value a message can be made of; what is reported in its place is left open, and saying why there is
no message is more use than saying nothing. The guarantee is 3.0's: a 2.0 processor reports the error, which
is what keeps an `xsl:result-document` inside a message `XTDE1480`.

**`terminate` is a computed boolean, so its spellings follow the processor.** The attribute is a value
template, and the element table — which is what holds a 2.0 stylesheet to `yes` and `no` — cannot see what a
value template comes to. All six spellings are read where the processor implements 3.0 and two where it does
not, which is the same rule the table applies to a value written out. **An `error-code` that is no name at
all** leaves the message the code it would have had: the specification names none for that, the instruction
already having `XTMM9000`.

`xsl:context-item` was read for less than it says. **Its `as` is an item type**: the context item is one
item, so an occurrence indicator is writing about a sequence that cannot be there, and a type nothing
declares is the same complaint about the same attribute — `XTSE0020` for both, which is what the
specification names for an attribute whose value is not a permitted one. **One declaration, before the
parameters**: two would be two answers to one question, and a declaration after an `xsl:param` is read out
of order, neither of which a processor may put right on the stylesheet's behalf. Both are `XTSE0010`, and
the check is general — the leading elements of any element come in the order its content model lists them,
and the ones it allows only one of are allowed only one, which reaches `xsl:on-completion` as well.

**And the whitespace before those declarations is not content.** A sequence constructor begins after the
declarations its element reads for itself, so the layout standing between and before them is layout. It
survives to be asked about only under `xml:space="preserve"`, which is where the difference shows: a
template holding two spaces, an `xsl:context-item` and two more spaces produces two spaces and not four,
the pair before the declaration being no part of what the template writes. The same rule reaches an
`xsl:sort` at the top of an `xsl:for-each`, which is `misc/whitespace`'s 015.

`insn/message` went from 5 failures to none and `decl/context-item` from 5 to none, with `misc/whitespace`,
`attr/version` and `insn/xsl-document` alongside. The 3.0 run reads two tests fewer for it: message-0102 and
message-0103 stop failing by being answered correctly, and what they then ask is that the message itself be
checked, which this driver has no way to present.

### Where an expression ends, and where a node does

**A brace inside a comment ends nothing.** An attribute value template is found by scanning for the brace
that closes it, and the scan already stepped over string literals — but not over XPath's own comments, which
nest and may hold anything at all. The suite writes `{('exp', (: comments isn't }fun{ :) 23)}`, where the
apostrophe, both braces and everything between them stand inside a comment and say nothing about where the
expression ends.

**The braces may hold nothing.** XSLT 3.0 lets the expression be absent and gives it the empty sequence, so
`x{}y` is `xy` and `{ (:nothing:) }` is nothing at all. XPath has no empty expression, so this is asked
before the parser is, which can only report a syntax error for something the language allows. It is the
grammar rather than the behaviour, so it follows the processor's version (see *Which version's vocabulary a
processor reads*): a 2.0 processor reads `{}` as the error it is there.

**And a run of text is what a text value template is found in.** A stylesheet's comments and processing
instructions are removed before it is read, and a tree holds no two adjacent text nodes, so
`{str<!--c-->ing($p)}` is a single text node holding a single expression by the time anything looks at it
(§4.2). The whitespace-stripping rule already read a run that way (see *What a comment in a stylesheet does
to the text around it*); a template has to have the text itself, the brace that opens an expression and the
brace that closes it being free to stand in different pieces of the run.

**A comment keeps its hyphens apart, and a processing instruction its question mark from its bracket.** XML
has no escaping inside either, so two hyphens cannot stand together in a comment, a comment cannot end with
a hyphen, and `?>` cannot appear in a processing instruction at all. XSLT does not refuse the content for
that: the processor inserts a space (§11.7, §11.8), which keeps what the stylesheet wrote legible and the
result well formed, so `<xsl:comment select="'--Valid comment--'"/>` writes `<!--- -Valid comment- - -->`.
Data ending in a single question mark needs nothing, XML ending an instruction at the first `?` that is
followed by `>` and there being none.

**A computed collation is read where it is evaluated.** The `collation` of an `xsl:for-each-group` is an
attribute value template, so what it names is not known until the instruction runs, and the question of
whether this engine can order by it belongs there rather than on the text as written. One written outright
is still refused where the stylesheet is read. Neither is quietly given code point ordering instead.

`attr/avt` went from 4 failures to none on the 3.0 run and from 2 to none on the 2.0 one,
`attr/expand-text` from 5 to none and `insn/construct-node` from 3 to none on both, with `type/node` and
`attr/shadow` alongside.

### What an array is to a template, and what a map takes as one key

**An array is a container, and the built-in rule for a container is to go into it.** Applying templates to
an array applies them to its members — the sequence `?*` gives — and it is the same rule whatever the mode
says about no match, `shallow-skip` included. There is nothing else it could sensibly be: the built-in rule
for an item that is not a node writes the string value, and an array has none to write. A member holding
several items contributes each of them, the members being processed as the one sequence they make.

**A date or a time keys by the moment it names.** Two values are the same key when `eq` says so, and `eq`
compares these as moments — so `18:15:00-05:00` and `23:15:00Z` are one time and therefore one key, where
the two lexical forms are two. One with no timezone is a different matter and stays a key of its own,
however the implicit timezone would settle it: a map whose keys collided or not according to a setting
outside it would not be a map anyone could reason about, and the suite pins that side as well.

**The key of an `xsl:map-entry` is one atomic value by the ordinary conversion rules**, the attribute having
a required type of `xs:anyAtomicType` — so the empty sequence there is that conversion failing, which is
`XPTY0004`. `XTTE3375` is the other thing, an `xsl:map` whose content is not maps.

**And XSLT 3.0 reserved three more namespaces.** `math`, `map` and `array` belong to the specifications as
`fn` and `xs` always did, so a stylesheet declaring a function of its own in one of them is `XTSE0080` —
the same complaint, three namespaces later.

`type/maps` went from 3 failures to none and `type/arrays` from 3 to 1, both on the 3.0 run alone: maps and
arrays are 3.0's, and neither set has anything the 2.0 control reads. The one left over is
square-array-201, which puts a node built by a global variable and the nodes of a document opened while the
transformation runs into one path expression and asks for a particular order between them. **The relative
order of nodes in different trees is implementation-dependent** (XPath 3.1 §2.4.1). This engine orders trees
by when they were built and builds the globals before the transformation starts, so the temporary tree
comes first where the test wants it last; the test's other alternative is a streamability error, which is
the one thing this engine does not analyse for.

### What a comparison was written among

A comparison is not only its two operands. Two things about the place it stands decide what it answers, and
this engine had been reading neither, so a stylesheet could say what it wanted and be answered as though it
had said nothing.

**`default-collation` is what every comparison in its scope compares strings by.** It was read, checked and
then dropped: `=`, `eq` and the ordering operators compared by code point wherever they stood, and so did
`starts-with`, `contains`, `compare`, `distinct-values` and the rest of the functions that take a collation
as their last argument. All of them now take the one in scope where none is named, `fn:default-collation()`
answers with it, and an `xsl:sort` that names neither a collation nor a language orders by it (§13.1.3). It
is inherited down the stylesheet tree and may be overridden at any depth, which is what lets the same
comparison answer differently in two branches of one `xsl:choose`; and its value is a list of candidates,
most preferred first, of which the first this engine has is the one taken.

Not `xsl:for-each-group`, `xsl:key` or `xsl:merge-key`, which group and order by code point and refuse a
`collation` attribute that says otherwise. A default collation reaches the comparisons written inside them
and not the grouping or the ordering itself. *Since superseded: the three honour a `collation` attribute,
and a merge key without one is ordered by the default collation in scope as a sort key is.*

**And the namespaces in scope are what an untyped operand is read as a name against.** A general comparison
reads an untyped operand as the other operand's type, and where that type is `xs:QName` the cast has to
resolve whatever prefix it finds — against the namespaces the comparison was written among, there being
nowhere else to look. XPath 2.0 allowed that cast only over a literal and 3.0 allows it over a computed
string and over an untyped value, which is what an attribute out of a document gives. The suite's
choose-0106 writes the same comparison three times under three bindings of one prefix and expects the third
to be the one that matches.

Both travel with the compiled comparison rather than being looked up while it runs, and the collation is
held as nothing at all wherever it is the code point one — which is every comparison written without a
`default-collation` above it, so the ordinary path is one null check away from the ordinal comparison it
always was. The emitted form needed nothing: a comparison under XPath 2.0 rules already falls back to the
interpreter, and one under 1.0 rules has no collation to read.

Two things about regular expressions came with them. **A `$N` in a replacement naming more groups than the
pattern has contributes nothing**, where .NET leaves the reference standing and wrote a literal `${5}` into
the result. And **the regex group set is no part of what a function item carries**: `regex-group#1` captured
in one `xsl:analyze-string` and called inside another answers with a zero-length string rather than
borrowing the groups in force where it was called (§5.3.4).

`insn/choose` went from 3 failures to none on the 3.0 run and from 2 to none on the 2.0 one, `misc/regex`
from 3 to none and from 1 to none, with `misc/collations` and `fn/function-lookup` alongside.

### What the caller may hand the way in

A transformation started at a named template is a call, and XSLT 3.0 §2.3 lets whoever runs it supply the
call's arguments. This engine had the machinery — `fn:transform()` has passed template parameters since it
was written — and no way for a caller to reach it, so a stylesheet whose entry point took a parameter could
be run only from inside another one.

**`XsltOptions.TemplateParameters` and `XsltOptions.TunnelParameters`** are that way in. Names and values
take the same forms as the stylesheet parameters beside them, and for the same reasons: `{urn:x}depth`
rather than a prefix, because the caller's prefixes are not the stylesheet's; a name the template does not
declare ignored, so one set of values can serve several entry points. A tunnel parameter passes through
every template that does not declare it, so it reaches as far down as the transformation goes rather than
stopping where it went in, and it reaches whichever way in the caller chose.

**XSLT 2.0 had no way to supply one at all**, so a 2.0 processor has nowhere to put what the caller offered
and passes it over. A required parameter on the way in is then unsupplied, which is exactly the error 2.0
names for it — and the code is one of those a later specification renamed, `XTDE0060` becoming `XTDE0700` in
3.0, so the processor's version decides which is reported. It had been reported with no code at all, which
is the one thing an error that a caller may want to catch must not be.

The driver reads the `param` children of an `initial-template`, a `tunnel="yes"` among them going to the
tunnel set, and resolves a prefixed parameter name against the element that wrote it — the suite declares
the prefix on the `param` itself. It also stops presenting a result the test asked for **as a value**: an
`output` element naming a `result-var` wants the typed items back, which a transformation that writes a
document no longer has, and that is the limit every assertion about a typed sequence is already skipped for.

`misc/initial-template` went from 3 failures to none on the 3.0 run, one of the three by being set aside
rather than answered. Both denominators moved up as well — the coded error let four tests on the 3.0 run
and five on the 2.0 one be read where they had been skipped for an error with no code to check.

### What a cast may name, and what a next iteration may supply

**The three list types are cast targets.** `xs:NMTOKENS`, `xs:IDREFS` and `xs:ENTITIES` are the only list
types a processor without a schema has, and XPath 3.0 defines a cast to each of them (§19.4): the text is
split on whitespace and every token cast to the item type, so what comes back is a sequence rather than a
single value — the one cast that produces more than one item. All three have a minimum length of one, which
is what makes `'' castable as xs:NMTOKENS` false where `'a b c'` is true; and a list type is made from text
and from nothing else, so a number is castable to none of them.

They are kept apart from the atomic types rather than added among them, because a list type is not one a
value can be an *instance* of here: it names a sequence, and this engine reads no schema, so nothing it
holds is ever annotated with one. `castable as xs:NMTOKENS` is a question about the text and answerable
without a schema; `instance of xs:NMTOKENS` is a question about a type annotation nothing here carries, and
stays `XPST0051`.

**An `xsl:next-iteration` supplies each parameter once**, which is the rule every other call already
followed: two `xsl:with-param` of one name there had been letting the second silently win, where which of
two values the iteration was meant to start again with is not a processor's to decide. `XTSE0670`, the same
code as anywhere else.

**And the `as` on one of those `xsl:with-param` elements converts what it supplies.** Two types apply where
the stylesheet writes two — the one on the `xsl:with-param` converts the value the call hands over, by the
function conversion rules, and the one on the `xsl:param` says what the parameter holds — and only the
second had been read. So an `xsl:param` with no type of its own may now be handed an `xs:double*`, which is
what the suite's iterate-039 asks for.

`expr/castable` went from 3 failures to none and `insn/iterate` from 3 to 1, both on the 3.0 run alone. The
one left over is 024, whose stylesheet holds two static errors at once — an attribute `xsl:param` does not
have, and an `xsl:on-completion` outside any `xsl:iterate` — and wants the second reported. This engine
validates each element completely before moving to the next, so the one written first is the one heard;
reporting the other would mean a placement pass over the whole stylesheet before any attribute was looked
at, which is a great deal of machinery for a stylesheet that is wrong twice over.

### What a source document may be a fragment of

**`xsl:source-document` follows a fragment identifier**, as `document()` already did: a bare name names the
element with that ID (XPointer §3.2), which an `xml:id` gives without any schema or document type
declaration, and that element rather than the document node is what the body processes. The instruction had
been refusing a fragment outright. A name no element carries leaves it nothing to process, which is
`XTRE1160`.

That is the whole of what the two sets this was reached through gave up. Both hold tests this engine cannot
answer, and it is worth saying which and why, because none of the five is a rule waiting to be implemented.

**`misc/docbook`** runs the DocBook 1.79.1 stylesheets over a real article, which is the nearest thing the
suite has to a field test. Two of its four are out of reach. 001 wants `exsl:node-set()`, and past that
`exsl:document` — an extension *element* that writes a result document, which is `xsl:result-document`
under another name. The function would cost nothing here, a result tree fragment being a real tree already
and the function therefore the identity, but it opens onto the element, which is a feature and not a
one-liner; neither was added for a test that needs both. 002 produces an XSL-FO document whose element
count differs from the 619 the test asserts, and the assertion is a count: there is no expected document to
diff against, no second processor here to differ from, and a hundred files of imported stylesheet between
the input and the number. That is the oracle problem in its purest form, and guessing at it would be worse
than leaving it.

**`decl/package`**'s three are not this engine's to fix. 021err declares `<xsl:function
name="me:function1#0">` and 022err writes `component="function#0"` on an `xsl:accept` — the arity in the
name of the declaration and in the kind of the component, where erratum E36 puts it in the `names` of an
`xsl:accept` or `xsl:expose` and nowhere else. Neither is an EQName or one of the six values `component`
may take, so both are refused with `XTSE0020` before the conflict the tests are about can arise. And 200
writes `package-version="'1.0.0'"` on an `xsl:use-package` and wants `XTSE3000`, where `use-package`'s own
291 to 294 write `2.0.0-alpha:beta`, `TotallyInvalid`, `-3.6` and `-alpha` and want `XTSE0020` for the same
thing. Four tests against one, and `XTSE0020` is what the attribute's grammar says: the four are what this
engine answers.

`misc/docbook` went from 3 failures to 2 on the 3.0 run and stands at 2 on the 2.0 one, `decl/package` at 3.

### What language a number is spelled in

**German is the second language this engine spells numbers in**, and the first that is not the fallback.
`lang="de"` on `xsl:number`, and `'de'` as the third argument of `fn:format-integer`, reach a German speller
rather than being read for their errors and then dropped. Everything else still falls back to English, which
is what the specification asks of a processor for a language it does not have: use one it does, and raise
nothing over it.

The shape of the language is where the work is. German writes everything below a million as a single word
with the units before the tens — 134,816 is `einhundertvierunddreißigtausendachthundertsechzehn` — and
starts a word of its own at each million, counting on the long scale, where a `Milliarde` stands between a
million and a `Billion`. One is `eins` only where the word ends there: `zweihunderteins`, but
`einundzwanzig` and `eintausend`. And the upper case of `ß` is `SS`, which .NET's simple case mapping does
not give, so `format="W"` over thirty had to be taught to write `DREISSIG` rather than leave a lower-case
letter in the middle of a shouted word.

**An ordinal is not the cardinal with something added.** 201 is `zweihunderteins` where the ordinal is
`zweihunderterste`; below twenty the stem takes a `t` and from twenty up an `st`; and either way the ending
goes on the last piece of the word, which for 134 is the `dreißig` and for 115 the `fünfzehn`. So it is
built rather than patched, by telling each step whether it is writing the piece that carries the ending.

**And the ending is asked for rather than known.** German inflects an ordinal for the case, number and
gender of what it stands before, none of which a processor can see, so both places that ask carry the
ending itself: the `ordinal` attribute of `xsl:number` and the parenthesised variation of a
`fn:format-integer` picture, where `ordinal="-er"` and `'Ww;o(-er)'` are the same request and both give
`erster`. A variation beginning with a per cent sign names a CLDR rule set instead, and the only
spelled-ordinal set German has makes no such distinction, so `%spellout-ordinal` and an absent variation
both give the plain `-e`. English has one ordinal form and ignores the whole question, which is why the
attribute had been read as a flag; it is a value now, and English is what still reads it as one.

The driver answers for the language as well. A test declaring `languages_for_numbering` or
`ordinal_scheme_name` had been skipped as a property it could not speak to; it now answers `en` and `de` for
the first and both schemes for the second, which brought four German numbering tests into each run, all of
them passing. The one Italian test stays skipped: a fallback to English is not what a test that names
Italian is asking for.

`insn/number` went from 2 failures to 1 on each run, and neither of the two left is a rule waiting to be
implemented. On the 3.0 run, 0111 multiplies 1,234,567,890 by itself twice and wants all twenty-eight
digits of the answer. The arithmetic now keeps them; `xsl:number` does not, rendering through a
fixed-width number as `format-integer` does. Widening those means widening the sequences they render
through — the words for a quintillion and past it, the alphabetic and roman fallbacks — which is a
piece of the numbering library rather than anything about how an integer is held. On the 2.0 run, 1004 puts an `xsl:number` with no context item inside an `xsl:on-completion` and
wants `XTTE0990`, but its stylesheet says `version="2.0"` and `xsl:iterate` is an XSLT 3.0 instruction: a
2.0 processor cannot reach the error the test is about, and the test's own dependency should read XSLT30+
rather than XSLT20+. It passes on the 3.0 run, where the instruction exists.

### What backwards compatibility restores, and what it does not

**A backwards-compatible expression writes a number the way this processor writes one.** It had been writing
it the way XPath 1.0 does — `Infinity`, one zero, and never an exponent — on the reasoning that a stylesheet
saying `version="1.0"` should read as `XslCompiledTransform` reads it. The specification says otherwise, in
as many words: an element with a `version` below 2.0 enables *backwards-compatible behaviour*, and "the
result of evaluating instructions or expressions with backwards compatible behavior is fully defined in the
XSLT 2.0 and XPath 2.0 specifications, it is not defined by reference to the XSLT 1.0 and XPath 1.0
specifications" (XSLT 2.0 §3.8). What the mode restores is a list of *rules* — one numeric type, the first
item where a sequence is given, a general comparison read the 1.0 way, a fallback for a function that is not
there — and how a number is written is not on it.

So `string(1 div 0)` is `INF` under `version="1.0"` as it is anywhere else, and a double outside the window
from 0.000001 to 1000000 is written with an exponent there too. That is visible, and worth being plain
about: the 1.0 idiom of a template that calls itself to add up a column now writes `5.00005E9` where an XSLT
1.0 processor writes `5000050000`. A stylesheet that wants the digits has `format-number()`, which is what
the suite's own `xpath-compat-0104` uses — and which still writes `Infinity`, that spelling being a property
of the decimal format rather than of the number.

**And the operand of a unary minus becomes an `xs:double`**, which is the same rule the binary operators
here already followed and which the suite settles for both: its `xpath-compat-0108` has `xs:integer div
xs:integer` come out an `xs:double` under `version="1.0"`, exactly and even where the division is exact. It
shows in the one value a double has and an integer has not — `-0` is a negative zero, so `1 div -0` is
`-INF` where before the minus sign left an integer zero behind and the answer came out positive. That is a
rule the mode does restore, and one this engine had been missing.

`expr/xpath-compat` went from 2 failures to none on both runs, the whole set now passing: 0101 writes
`INF`, `-INF`, `-0`, `1.0E-8` and `1.0E7` from five `xs:float` constructors under a `version="1.0"`
template, and 0109 does the same for a float and a double side by side.

### What a declared type admits, and what it converts

An `as` declaration applies the function conversion rules, and three of them had been missing, all in the
same place: what the rules will do for a value that is not already of the type written.

**A type derived from the one declared satisfies it, and keeps the type it had.** `xs:dayTimeDuration` and
`xs:yearMonthDuration` are derived from `xs:duration`, so a value of either is an instance of `xs:duration`
and goes through a declaration of it untouched — subtype substitution, which converts nothing. Only one
derivation of this shape was answered before, `xs:integer` within `xs:decimal`; the two durations are the
rest of them, every other derivation among the types this engine records either landing straight on
`xs:anyAtomicType` or being a restriction that the derived name already carries.

**An `xs:anyURI` declared as an `xs:string` is promoted**, which is the other thing the rules do and is not
the same thing at all. `xs:anyURI` is *not* derived from `xs:string` — the two meet only here — so the value
is converted rather than admitted, and what comes out is a string that no longer answers to being a URI.
That is what the suite's as-0116 asks in as many words: `instance of xs:anyURI` false, `instance of
xs:string` true, from one variable.

**And `xs:anyAtomicType` atomizes the node it is given.** Atomization is the first of the conversion rules
and does not depend on knowing which type is wanted, but it had been reached only through the question
*which atomic type is this*, which `xs:anyAtomicType` has no answer to and `xs:numeric` has three. They are
two questions now: whether the type admits atomic values at all, which says whether to atomize, and which
type to convert towards, which may have no answer. So `<xsl:variable select="/doc" as="xs:anyAtomicType"/>`
holds the untyped atomic value the document node atomizes to, rather than being refused for holding a node.

`attr/as` went from 3 failures to none on both runs, the whole set now passing, and `decl/variable`'s 0121
came with it: `namespace-uri-for-prefix()` answers an `xs:anyURI`, and the test declares `xs:string`.

### What a document URI is a property of

**`fn:document-uri()` answers for a document node and for nothing else.** It had been answering for any
node, with the URI of whatever document enclosed it — but the document URI is a property of the document
itself, and an element is *in* a document rather than being one. So the answer for an element, an
attribute, a comment, a processing instruction or a text node is the empty sequence. `fn:base-uri()` is the
one that answers for every node, and the two had been sharing more than they should: they still share the
walk that finds the node, and part ways on what to do with it.

**And its no-argument form is XPath 3.0's.** `fn:base-uri()` has had one since 2.0, `fn:document-uri()` did
not, so a 2.0 processor asked for `document-uri()` has no such function — `XPST0017`, a static error about
a function that is not there rather than an empty answer. That is the same widening the engine already made
for `data()`, `nilled()` and `node-name()`, whose context forms arrived in 3.0, and it is recorded the same
way: as one function reachable with one fewer argument in a later language, not as a second entry.

**The source document is in the document pool under its own URI.** A URI names one document for a whole
transformation, and `doc()` asked for where the source came from had been reading the file a second time
and answering with a second tree over the same content — so `doc(document-uri(.)) is .` was false where the
specification makes it true. The principal input now goes into the pool as it is built, which is the pool
the secondary documents already shared.

`fn/accessor` went from 2 failures to none on the 3.0 run and from 3 to none on the 2.0 one, the whole set
now passing.

### Which validation a processor without a schema refuses

**`XTSE1660` names a set, and the two languages name different sets.** An XSLT 2.0 basic processor must
refuse an `[xsl:]validation` or `default-validation` of anything but `strip`; a 3.0 non-schema-aware
processor refuses only `strict`. The relaxation is the specification noticing what `preserve` and `lax`
actually ask for: with no type annotations anywhere, preserving them and stripping them come to the same
thing, and validating laxly against no declaration at all validates nothing. This engine had been drawing
one line for both — refusing `strict` and `lax`, allowing `preserve` — which was too permissive at 2.0 and
too strict at 3.0, and wrong on the same four tests from both ends.

So the line moves with the version this engine says it implements, as the rest of the vocabulary does. It is
visible to a caller who says nothing: `XsltVersion.Implemented` is 2.0, so `validation="preserve"` is now
refused unless `XsltOptions.Version` asks for 3.0. That is what a 2.0 processor is required to do, and a
stylesheet that means "leave the annotations alone" on a processor that has none can say `strip` and mean
the same thing.

A value outside the grammar stays a different complaint. `validation="strick"` is `XTSE0020` — the spelling
is wrong rather than the processor unable — and that is so whichever version is running. The narrower
grammar of `default-validation`, which takes only `preserve` and `strip`, leaves `strict` both outside it
and a request for schema awareness; the specification names the second, which is what the suite's
validation-0104 asks for.

`attr/validation` went from 1 failure to none on the 3.0 run and from 3 to none on the 2.0 one, the whole
set now passing.

### Which type names a processor has in scope

**`type-available()` asks which types are in scope, not which ones a value can be built as**, and this
engine had been answering the second question. The two come apart at both ends. `xs:anyType`,
`xs:anySimpleType` and `xs:untyped` name a place in the type hierarchy rather than a set of values, so
nothing constructs one and the table of constructible types does not hold them — but every processor has
all three in scope, and the answer for each is true. And at XSLT 2.0 `xs:int` goes the other way: this
engine can build one, and a processor without a schema does not have the name in scope at all.

**Which names are in scope is the processor's version to say.** XSLT 2.0 §3.13 gives a basic processor the
primitive types except `xs:NOTATION`, then `xs:integer`, `xs:anyType`, `xs:anySimpleType` and the five
XPath adds — `xs:yearMonthDuration`, `xs:dayTimeDuration`, `xs:anyAtomicType`, `xs:untyped`,
`xs:untypedAtomic` — and keeps everything else for a schema-aware one: the other derived types, the list
types, and `xs:NOTATION` itself. XSLT 3.0 §3.15 dropped the distinction and gives every processor the whole
of XML Schema Part 2. So the answer follows the version this engine implements, as the rest of the
vocabulary does, and the suite writes the same test twice to say so — its 0148 asks a 2.0 processor for
`xs:int` and wants false, and is marked `XSLT20` and not `XSLT20+`.

`fn:function-available()` still asks its own question, which is whether a constructor of that name can be
called. It is the closer answer for what this engine will actually do — `xs:int('3')` works whichever
version is running — and the four types with no constructor are where the specification itself says the two
questions differ.

`fn/type-available` went from 2 failures to none on both runs, the whole set now passing.

### What a file read as text may hold

**Text read by `fn:unparsed-text()` has to be characters XML permits**, and a file with a NUL byte in it is
not. A string in this data model holds XML characters and nothing else, so there is no string for the
function to answer with — `FOUT1190`, which the specification gives for exactly this alongside octets that
will not decode. This engine had been answering with the text as it stood, which pushed the failure to the
far end of the transformation: the value went into the result and the serializer produced something that is
not well-formed, a complaint about the wrong thing in the wrong place.

The check is on the whole string, so `unparsed-text-lines()` raises the same error, being built on the same
read, and both `-available` forms answer false rather than raising, which is what they already did for
anything unreadable. A surrogate pair is one character above the basic plane and is allowed; half of one is
an encoding artefact and is not, which falls out of asking about the code point rather than the code unit.

`fn/unparsed-text-lines` went from 2 failures to none on the 3.0 run, the whole set now passing, and the
two are a pair: one wants the error caught by an `xsl:catch` naming `*:FOUT1190`, and the other wants it
raised.

### What a function-lookup written in a package can find

**`fn:function-lookup()` finds the functions the package it is written in can see**, which is not the same
set as every function the compilation declared. A package sees its own functions whatever visibility they
carry, and of another package's only what that package offers it — so a library's private function does not
exist from outside the library, and the using package's functions do not exist from inside it. The call had
been handed the whole table and found everything.

The set is built from the components rather than from that table, and the difference matters where a
package overrides another's function. The table is keyed by name and arity and holds one entry for each, so
after an override that entry is the override; but what the used package sees under that name is still its
own declaration, and a `function-lookup()` written inside the library has to find that one. An override
replaces the component for whoever uses the package, not for the package itself. The components record
which declaration each came from, so the per-package view can be built from them, and the existing
visibility judgement — the one that already decides what an `xsl:accept` may ask for — answers the rest.

An abstract declaration is left out. It names a component with no implementation, so there is nothing to
hand back for it, and the suite asks for exactly that: its 005 declares `f:subtract` abstract in the used
package, supplies it in the using one, and wants the lookup inside the used package to find nothing.

Nothing has to be deferred to make this work: every component is declared before the first body is
compiled, so the view a call needs is complete by the time the call is read.

`fn/function-lookup` went from 2 failures to none on the 3.0 run, the whole set now passing.

### What an extension instruction falls back to

**An empty `xsl:fallback` is still a fallback.** It says to do nothing, which is a thing to do — and this
engine had been deciding by what the fallback *compiled to*, so an `xsl:fallback` with no content was
indistinguishable from no `xsl:fallback` at all and the instruction was refused. Whether one was written is
now recorded apart from what it produced. The suite's `xslt-compat-013` says so in its own comment: "Remove
this and the error clears", pointing at an empty fallback.

**And reaching an unimplemented extension instruction with no fallback is `XTDE1450`**, which it had been
reporting without a code. Two tests in `misc/error` are about that code alone, on both runs.

**A reserved namespace cannot be an extension namespace.** The specifications have already given those
namespaces a meaning — the XSLT one, the function and math and map ones, `xml`, the schema ones — so an
element in one is what they say it is rather than an instruction some processor might implement, and an
`xsl:fallback` does not make it otherwise. It is a static error, `XTSE0800`, raised before the fallback is
even looked for.

What is *not* implemented is the companion rule the suite's `math-3702` asks for: `XTSE0085` for an
`extension-element-prefixes` that names a reserved namespace, where no element of that namespace is then
used. The two rules collide on `extension-functions-0105`, which designates the schema namespace *and* uses
an element from it: the attribute is read before the body, so an engine raising `XTSE0085` reports that,
where the suite wants `XTSE0800`. Both tests were written by the same author four years apart and this
engine can satisfy either but not both; it takes the one that describes what the stylesheet actually did.

`fn/extension-functions` went from 1 failure to none on the 3.0 run and `misc/xslt-compat` from 1 to none on
both, with three more tests out of the skips.

### Which whitespace a package strips from what it reads

**Whitespace stripping is local to a package.** XSLT 3.0 §3.6.5 says it plainly: an `xsl:strip-space` or
`xsl:preserve-space` in a library package affects only the `doc()` and `document()` calls written in that
package, and one in the top-level package additionally strips the source document. So a library that says
nothing preserves everything however much the package using it strips, and one that says something does not
impose it on anybody else. This engine had one set of declarations for the whole compilation, so whichever
package declared last decided for all of them.

Each package now declares into its own control, and the same name test in two packages is no longer two
declarations in conflict — each is answered in its own. The top-level package's control is still the one
that strips the source document, which is the other half of what the specification says.

**One file read by two packages can be two documents**, and that reaches the pool a document is remembered
in. The key is now the reference together with the stripping it was read under, so a package that strips
something reads its own copy and every package that strips nothing shares one — which keeps the rule that a
URI names one document wherever it can be kept, and gives it up only where the two packages genuinely
disagree about what the document contains. A package with no declarations answers with the shared control
that strips nothing, which is what makes that sharing fall out rather than needing to be arranged.

`fn/document` went from 2 failures to none on the 3.0 run, the whole set now passing. Its two are the same
test written for `document()` and for `doc()`: a library that strips nothing and a package that strips
everything, each counting the text nodes of one file, and the answers are 4 and 0.

### Where a variable's scope ends

**A variable's scope ends with the sequence constructor it was declared in.** §9.7 puts it as the following
siblings of the declaration and their descendants, and nothing else — so an `xsl:variable` written inside a
literal result element shadows nothing once that element closes, and the name goes back to meaning what it
meant before. This engine had been pushing every local binding onto one stack and never taking it off, so a
variable declared three elements deep still shadowed a template's parameter after those elements had ended.

The fix is where it should be: the compiler marks the stack when it starts a constructor and truncates it
when it finishes. Every binding a constructor declares belongs to that constructor, which is what the rule
says, and the nested declarations inside it still shadow each other in order — the second `xsl:variable` of
one name reads the first, which is what makes `select="concat(,'c')"` mean what it looks like.

Two tests turned on it, on both runs: `decl/param`'s 0107, which is written to test exactly this and walks
four shadowing declarations in and out of an element, and `decl/variable`'s 1702.

What is left in `decl/param` is 0301, and it is not about scope. It declares a global in terms of a
function whose body binds a local variable back to that global, and asks that no circularity be reported —
because nothing ever reads that local. This engine evaluates a local variable where it is declared, so the
circularity is reached and `XTDE0640` is raised. Both are conformant readings of a specification that lets
a processor evaluate variables lazily without requiring it, and the lazy one is what the test asks for; it
would mean a thunk in every local slot, which is the hot path this engine's measurements were taken on.

### What may stand in for an xsl:value-of's select

**What an `xsl:value-of` may say instead of a `select` is the processor's question**, not the stylesheet's
claim about itself. XSLT 1.0 gave the instruction an empty content model, 2.0 made its content a sequence
constructor, and 3.0 let it say neither. This engine had been reading both rules off the `version` in scope,
so a `version="1.0"` element refused its own content and a `version="2.0"` one refused to be empty on a 3.0
processor — where the vocabulary follows the processor everywhere else.

Both are the same one line. A `version` below 2.0 asks for backwards-compatible *behaviour* — the first
item of a sequence, a general comparison read the 1.0 way — and does not narrow the content model back:
the suite's `version-021` writes `<xsl:value-of version="1.0">` around another `xsl:value-of` and wants the
inner one instantiated, which only happens if the content is read. And an empty `xsl:value-of` is judged by
what the processor implements: `select-7502b` is the same `version="2.0"` stylesheet as `select-7502a`,
handed to a 3.0 processor, and wants an empty result where the other wants `XTSE0870`.

`attr/select` and `attr/version` are both empty now, on both runs.

### What a picture may carry an exponent by, and what a URI reference may not

**`xsl:decimal-format` takes an `exponent-separator`**, which XPath 3.1 added along with the exponent part
of the `format-number` picture language. The symbol was already carried and already checked against the
other roles; only the attribute was missing from what the element admits, so a stylesheet naming it was
refused before anything read it.

**And which picture language is read follows the processor.** The exponent part is 3.1's, and this engine
had been deciding by the `version` in scope — so a `version="2.0"` stylesheet could not write `0.0000E0` on
a 3.0 processor. That is the same question the class already answered the other way round for the *code* an
unreadable picture carries: `XTDE1310` where XSLT defines the function and `FODF1310` where the core library
does, decided by the processor. Both now come off the same version, and what the stylesheet says of itself
still decides the 1.0 behaviour the picture is *evaluated* with.

**A URI reference carries at most one fragment.** RFC 3986 leaves `#` out of the production for what
follows one, so `##some.uri` is not a reference and `fn:resolve-uri()` given it raises `FORG0002`. The
parser had been taking everything after the first `#` as the fragment without looking at it again.

`fn/resolve-uri` and `fn/format-number` are both empty on the 3.0 run, and `fn/resolve-uri` on the 2.0 one.

### What is read of a declaration from a later version

**A declaration a later version of XSLT defines is ignored before anything it carries is read.** XSLT 3.0
§3.11 says it in one sentence — an element in the XSLT namespace standing among the declarations, which this
version does not allow to stand there, is ignored together with its content — and the sentence is about the
whole element rather than about whatever the compiler happens to walk past later. This engine ignored the
element and its content but not its `use-when`, which is answered while the stylesheet is being prepared,
before the element's name has been looked at by anybody. That made the attribute the one part of an ignored
declaration still able to refuse the stylesheet.

It is also the part that can least be read. A stylesheet written for XSLT 4.0 writes its condition in
XPath 4.0, in whatever namespaces and with whatever functions that version hands it: the suite's
`forwards-008` writes `use-when="fn:new-function()"` under `version="4.0"`, and refusing it for an unbound
prefix is refusing a stylesheet for being newer — the one thing forwards-compatible processing exists to
prevent.

So the rule is decided where the exclusion is. An element in the XSLT namespace, standing directly inside the
`xsl:stylesheet`, which the version in scope puts beyond this processor, is not in the stylesheet at all —
the same answer a `use-when` of `false()` gives, reached without answering one. That covers a name this
version has never heard of, a name it knows only from a version later than it implements, and a name it knows
perfectly well but does not allow among the declarations: `xsl:when` at the top level of a `version="444"`
stylesheet is `forwards-007`, and a later XSLT is as free to allow it there as to invent something new. A
declaration this version *does* allow is untouched, and its `use-when` is answered as it always was, however
late the version the stylesheet claims.

`misc/forwards` is empty on the 3.0 run.

### The claim moves to 3.0

`XsltVersion.Implemented` is 3.0, so a caller who names no version gets a 3.0 processor:
`system-property('xsl:version')` answers `3.0`, the vocabulary and the library are 3.0's whatever a stylesheet
says of itself, and a `version="3.0"` stylesheet is read as what it is rather than forwards-compatibly. The
measures that decided it are the ones above — 99.5% of the 3.0 suite, 98.9% of QT3 at 3.1, and a function
library complete but for `fn:load-xquery-module` — and the cost is the one this document warned of: what 3.0
lacks here is refused rather than fallen back from, which is the package-model corners, starting at a named
function, and the override signature check.

A 2.0 processor is still there for the asking, `XsltOptions.Version = XsltVersion.V20`, and is what the 2.0
half of the conformance run measures; the unit tests that pin down what a 2.0 processor refuses ask for it by
name. Two things moved with the default. A `version="3.0"` stylesheet no longer stands for "newer than this
processor" in a test of forwards-compatible processing, so those tests say `version="4.0"`. And a 3.0
processor follows every import twice — once to settle static variables in tree order, once to load — so the
compiler now remembers what each reference resolved to and asks the resolver once per reference. Over the
network the difference is plain: the DocBook xslTNG stylesheets had been fetched twice over, 100 requests for
50 modules.

### What a processor asked to be 3.0 says it is

**`system-property('xsl:version')` reports the version the processor implements, and this engine is told
which to be.** §18.2.2 asks for exactly that — *the version of XSLT implemented by the processor* — and the
answer had been a constant, `XsltVersion.Implemented`, which was 2.0 and stayed 2.0 while it was what a caller
who asked for nothing got. A caller who asked for 3.0 got a processor that read 3.0's vocabulary, refused
what 3.0 refuses and passed 99.5% of the 3.0 suite, and it was still answering `2.0` when asked what it was.

The version the stylesheet claims of itself is a different question, and the function needs both: what the
stylesheet says decides *how the answer is read* — 1.0 made the property a number and 2.0 made every property
a string — and what the processor is decides *what the answer is*. They had been one parameter.

It is the question a stylesheet asks in order to decide what it may use, so a wrong answer does not stop at
the answer. Three tests turn on it and none of them is about the property. `system-property-025` asks through
a text value template and wants `Run with 3.0`. `shadow-002` computes
`_static="{if (system-property('xsl:version') = '3.0') then 'yes' else 'no'}"` and then reads, from a later
shadow attribute, the variable that answer makes static — so answering `2.0` leaves `$N` out of scope
somewhere else entirely. And `package-version-910` computes an empty `package-version` where the property is
3 or more, which is the `XTSE0020` the test is written for; that is the one this document had been deferring
until the claim was decided, and this is the claim, made where it is true rather than everywhere.

`fn/system-property`, `attr/shadow` and `attr/package-version` are all empty on the 3.0 run.

### Which functions this engine says it has

`function-available()` is how a stylesheet decides whether to call something or to take another route, so an
answer of *no* about a function that is there is not a small error: it sends working code down a fallback
path. The suite generates two tests from two specifications — `function-1901` from F+O 3.0 and
`function-2001` from XSLT 3.0 — and asks the question of every function in each. Between them they named
twenty-three signatures as missing, and only five were absent.

**Two libraries were never being asked.** The probe consulted the core library, the 2.0 additions, the
higher-order functions, the 3.0 ones, maps, arrays and math — and not the two that came after it was
written: the node-building functions (`fn:analyze-string`, `fn:parse-xml`, `fn:parse-xml-fragment`) and the
JSON ones (`fn:parse-json`, `fn:json-doc`, `fn:json-to-xml`, `fn:xml-to-json`). Every one is built here and
every one answered *no*. Both libraries now answer from the same table their calls are built from, which is
what the class already claimed of the others and the only arrangement in which the two cannot come apart.

**Seven XSLT-defined functions were missing from the table** the compiler builds by hand rather than from a
library: `accumulator-before` and `accumulator-after`, `copy-of`, `snapshot`, `current-merge-group`,
`current-merge-key` and `current-output-uri`. They arrived with 3.0 and their build sites ask about the
*processor*, so the answer asks the same thing — a list beside the table says which entries that applies to,
and `available-system-properties`, `stream-available` and `transform` are in it too: they were in the table
already, already 3.0-only, and already answering *yes* on a 2.0 processor that would refuse the call.

**And a library a later language brought is reachable when *either* version is 3.0** — the processor's,
because that is the language it reads, or the stylesheet's own claim, because a 3.0 stylesheet on a 2.0
processor is processed forwards-compatibly and its calls are built. That is the test the compiler makes
before consulting one, and it is the test made here now. A `version="2.0"` stylesheet on a 3.0 processor had
been told there was no `fn:snapshot()` to call.

**The three list types have constructors.** `xs:NMTOKENS`, `xs:IDREFS` and `xs:ENTITIES` are held apart from
the atomic types because nothing may be an *instance* of one — a list type names a sequence, and this engine
reads no schema — but the cast to one was already defined, splitting the text and casting each token, and a
constructor function is that cast under its other spelling. `xs:NMTOKENS('a b c')` is three items now rather
than `XPST0051`. `xs:anyType` keeps the code: it is not one of the four the specification withholds a
constructor from, and there are no values of it for one to build.

**`fn:uri-collection()` is there to be called.** *Since implemented: both functions now read
`XsltOptions.CollectionResolver`, and what follows describes the engine before it had one.* It finds
nothing, raising `FODC0003` where the default collection was meant and `FODC0002` where one was named —
exactly what `fn:collection()` does, and for the same reason. The function exists so that a stylesheet
calling it is told what happened rather than that the name is unknown, which is a different thing and sends
whoever reads it looking for a typo.

`decl/function` is empty on the 3.0 run.

### Which collation a target expression compares by

**The default collation of an `xsl:evaluate` target expression is the one in scope where the instruction
stands.** The specification says it in one line of the table that defines the target's static context, and
it was the only line of that table this engine was not honouring: the namespaces, the base URI, the default
element namespace and the narrowed function library all came from the instruction, and the collation came
from nowhere — always the code point one.

Which made the two spellings of one comparison disagree. Under
`default-collation="…/UCA?strength=secondary"`, where case is not a difference, `'XYZ' eq 'xyz'` written
down is true and the same expression handed to `xsl:evaluate` was false. The suite's `evaluate-049` is that
comparison and nothing else.

What has not changed is the initial match selection a caller supplies, which is still compared by code
point. That expression was written by the caller rather than in the stylesheet, so there is no point in it
for a `default-collation` to have been declared at.

`insn/evaluate` is empty on the 3.0 run.

### What the picture functions check, and what they will not claim

Four small things the QT3 suite found in one pass, each of them the engine saying the wrong thing rather
than doing the wrong thing.

**A width is padded in the picture's own digit family.** `[Y๐๐๐๑,10]` asks for ten Thai digits, and the
padding came from `U+0030` — six digits in one family and four in another. The family is the first digit of
the presentation less its own value, which the picture parser already works out to refuse a picture that
mixes two.

**`format-date` and its two siblings take two arguments or five.** Not a range between them: there is no
three-argument form to call, so naming a language without a calendar and a place is `XPST0017` rather than a
shorter way of saying the same thing. The registry held a minimum and a maximum, which admits three and four.

**And the place is of its declared type whether or not anything reads it.** This engine has no Olson
database to honour one with, so it honours none — but an argument nobody evaluates is an argument nobody
type-checks, and `format-date(…, 'en', (), 5)` was reading the picture and complaining about that instead of
about the 5. It is evaluated now and its value discarded, which is what makes the type error the one
reported. The driver declares the `olson-timezone` feature unsupported with it: the offset .NET could answer
from `TimeZoneInfo`, the abbreviation — `EST`, `CET` — it has no API for, and half of that is worse than
none of it.

**A range is held as its bounds, and only laid out where something walks it.** `1 to 10000000` is
ten million items and two numbers, and which of those it costs depends on what is asked: counting it
is a subtraction, asking whether a number is among it walks the positions without building any of
them, and indexing one is an index. A predicate that is simply a number is applied as an index too,
so `(1 to 10000000)[5000000]` reads one item rather than five million.

What is left to refuse is laying one out: 4,194,304 items, about sixty-four megabytes of them,
and `XPDY0130` past that. XPath 3.0 gives that its own code so a stylesheet can tell a processor
declining on grounds of scale from an expression being wrong, and the suite takes either the answer
or the code. The cap was a tenth of its present size while every range was built whether its items
were wanted or not; reached only by a caller walking them, a million is a number a stylesheet can
mean, and the suite builds a name a megabyte long with one. A range longer than an `int` counts is
refused where it is made, having no position it could be asked about.

**And `fn:subsequence` rounds its positions the way `fn:round` does.** Halves go towards positive infinity,
where `Math.Round` sends them to even, so a length of 2.5 took two items where the specification asks for
three. The specification defines the function in the same words as `fn:substring`, which had it right all
along and shares the helper now.

`fn/subsequence` is empty on both QT3 runs, and `fn/format-date` and `fn/format-dateTime` are down to the
year 654321, which `System.DateTime` does not reach.

### What a map key is, and what a repeated one is called

**A duration is one key however it was written.** A map key is tagged by what it measures rather than by the
way it was spelt or by which of the three duration types wrote it: `xs:duration('P1Y')` and
`xs:yearMonthDuration('P12M')` are twelve months and no seconds either way, so they are one key and the
subtypes share the tag with the supertype. Dates and times were already tagged by the moment they name for
the same reason; durations had been falling through to the type name and the canonical text, where they
never met.

**A key written twice in a map constructor is `XQDY0137`.** Three ways lead to the same shape and the
specification gives each its own code: a constructor written down, `map:merge` told to reject, and
`xsl:map` with two entries of a key. The third was already `XTDE3365`; the first two were both `FOJS0003`,
which is `map:merge`'s and JSON's. Twelve tests turn on the distinction, which is what a stylesheet reacting
to one of them needs in order to know which it was.

**`fn:default-language()` is `en`.** The specification leaves the default language of the dynamic context to
the implementation, and this is the one `format-integer()` spells a number in and `format-date()` names a
month in when nobody says otherwise. The point of the function is that a caller can pass on what would have
been used anyway, so asking for it by name answers what leaving it out answers.

`map` goes from 89.0% to 93.2% on the 3.1 run. What is left there is two tests about numeric keys that
differ in the twenty-first digit — `xs:double('1.00000000001')` and
`xs:decimal('1.0000000000100000000001')` are distinct keys and this engine keys both through a double — and
four the driver cannot present, an `assert-deep-eq` whose expected value is an array literal.

### What an any-of with an unpresentable branch means

**A test offering two acceptable answers, one of which this driver cannot check, is not a failed test.** The
QT3 suite writes a good many results as `any-of`: *either* a value of this type *or* this error, because a
processor is free to be liberal about an input or to refuse it, and both answers are right.
`parse-json('[1,2,3,]', map{'liberal':true()})` is the shape — a trailing comma a liberal parser may accept,
so the answers are "an `item()?`" and `FOJS0001`.

This engine accepts it, which is the first branch; but the first branch is an `assert-type`, and every
`assert-type` is skipped here for want of the data model to present one. The driver was then reporting the
*second* branch's complaint — *expected error FOJS0001, but the expression succeeded* — as a failure, which
says the engine got it wrong where what happened is that the driver could not tell.

An `any-of` no branch passes and at least one branch was skipped is a skip now. That is the rule the rest of
the driver already follows, applied one level in: a test it cannot present fairly is skipped with a reason
rather than counted either way.

**It moved 28 tests on the 3.1 run and 7 on the 2.0 one, and moved none of them into the pass column** —
they leave the denominator instead. The rate rises because the measurement got honest and not because the
engine did anything, which is exactly why the two numbers are always given together. `fn/parse-json` goes
from 14 failures to 7 and `map` from 93.2% to 98.2%.

What is left in `fn/parse-json` is the `escape` option, which this engine reads for `fn:json-to-xml` and not
for `fn:parse-json`, and five tests that read their JSON through `fn:unparsed-text()`. The second looks like
the `fn:id` case and is not the same fix: `unparsed-text()` needs a resolver, resolvers are opt-in and reach
the engine through `XsltOptions`, and outside a stylesheet there is no transformation running to carry one.

### Which collection an empty sequence asks for

**`collection(())` asks for the default collection.** No argument and an empty one are the same question —
the specification says so in as many words — so it is not a collection named by the empty URI, which is what
the message used to call it. `fn:uri-collection()` reads the same way, having been written beside it.

The code is `FODC0002` both ways round now, and that is a choice rather than a reading. The suite draws a
line this engine cannot stand on either side of by turns: `FODC0002` where no default collection is declared
at all, which `collection-901` and `-903` assert outright, and `FODC0003` where one is declared and the
processor will not serve it, which `collection-001` to `-003` allow as the alternative to returning the
documents. With no resolver for a collection there is never one declared here, so the first is what is true
of this engine and the second would be claiming a collection exists that it then withholds. *Since then a
collection resolver has been added, `XsltOptions.CollectionResolver`, and a declared collection is served;
`FODC0002` remains the code where none is declared, or none is configured.*

It is worth writing down that this costs as much as it gains: one test on the 2.0 run, one on the 3.1 one,
in opposite directions. The count was not the reason.

### What the emitted backend was never asked

The conformance driver had never named a backend, so all **12,880** XSLT tests ran interpreted and the code
that emits IL was covered by nothing but the unit tests — 28 of 68 files, at most 73% of the test methods,
and none of the QT3 runs, whose runner evaluates expressions directly and has no `XsltOptions` in its path to
carry a backend. `--compiled` runs the suite the other way. The two must reach the same verdict on every
test, because an expression the emitted backend cannot express calls back into the interpreted node it was
compiled from: what the suite checks is exactly the part that was emitted, and nothing else.

It found three failures, and they were one bug. **`position()` and `last()` were emitted as `xs:double` at
every version.** The interpreter reads both through a single helper that returns a double under 1.0 and an
`xs:integer` from 2.0 on — the rule stated above, under the four functions that count — and the emitted form
had only ever had the 1.0 half of it. So a variable declared `as="xs:integer"` was told a Double had been
produced, and `1 to $pos` raised `XPTY0004` where `to` found a double on its right.

The fix is small and the two lessons in it are not.

**It was not a gap in what the backend emits, but a fault in what it did emit.** The fallback is correct by
construction — an unspecialised expression is evaluated by the very node it was compiled from — so a bug can
only live in a path someone specialised, and those are the paths written when the engine was an XPath 1.0
engine. *Every number is a double* was true of the language the emitter was written for. Nothing announced
that it had stopped being true.

**And it had been reachable the whole time.** Any caller passing `XsltBackend.Compiled` to a 2.0 stylesheet
that binds `position()` to a typed variable would have hit it from the moment the engine's default version
moved to 2.0. It went unnoticed because the one thing that would have found it — 12,880 tests already
written, already passing, already run twice a session — was never pointed at that backend. Both runs now
report the same figures under either backend, and a diff of the two failure name-sets is empty at both
versions.

### What compatibility settles about a sort, and what a context item may be

Two things the mode was taken to restore and does not. Both were one test each, and both came from reading
*backwards-compatible behaviour* as *XSLT 1.0*, which §3.8 says in as many words that it is not.

**A sort key without a `data-type` is compared by what it is, whatever version the stylesheet claims.**
XSLT 2.0 §13.1.2 is explicit about which half of this the mode owns: the values are compared by the rules
of their type, untyped ones cast to `xs:string`, and “for backwards compatibility with XSLT 1.0, the
`data-type` attribute remains available”. The attribute is the compatibility. What the mode itself does to
a sort is one rule and a different one — `XTTE1020`, a key that atomizes to more than one item, which is a
type error at 2.0 and under the mode is the first item of the sequence.

This engine had the mode force text instead, and with it the key was not atomized at all: it sorted the
node. So `backwards-012`, which is `xsl:perform-sort` over `1 to 5` with a key of `(-., 'banana')`, gave up
the banana as it should and then collated `-1` to `-5` as text, which is ascending. Taking the two apart
— the first item for the value, `data-type` alone for the comparison — sorts it descending, as the test asks.

That a 1.0 stylesheet sorts differently here from how a 1.0 processor sorts it is real, and is one of the
incompatibilities XSLT 2.0 lists rather than an oversight. It is also nearly invisible: a stylesheet 1.0
could have written sorts on nodes, a node atomizes to untyped text, and untyped text is collated. It takes
a key something 1.0 could not write has typed — `number(.)`, or `perform-sort` over a range — for the
difference to show, and `data-type="text"` asks for the old order in as many words.

**And `.` under the mode is not known to be a node.** XPath 1.0 defines it as `self::node()`, which holds
for exactly as long as a context item has nothing else it could be. A 1.0 stylesheet on a 2.0 processor can
write `<xsl:for-each select="(3,1,2)"/>` and stand on an integer with the mode still on. `ContextItemExpr`
claimed a node-set whenever the mode was enabled, which opened the 1.0 comparison fast paths and the
`xsl:value-of` one that reads nodes' text straight off the tree — and then raised `XPTY0004` the moment the
item turned out to be atomic, which is `xslt-compat-010`: a 1.0 stylesheet iterating `xs:double*` with
`version="2.0"` on the one `xsl:sort` inside it.

A promise that can be broken is not one, so the claim is gone. What it costs is the fast path for `.` and
nothing beyond it: the context item is a single item, and one node atomized against a value answers what a
node-set of one answers.

The 3.0 run went from 7,888 of 7,924 to **7,890**, the 2.0 run from 5,590 of 5,622 to **5,592**, and the
schema-aware run from 8,449 of 8,526 to **8,451**; the two backends agree test for test, the two XPath runs
are unmoved, and nothing anywhere fails that was passing. One unit test changed with the code rather than
against it: it had a 1.0 stylesheet sort `number(.)` as text, which is what a 1.0 processor does and not
what this mode does.

### What a document node contributes, and what it still is

A space goes between two atomic values that a sequence constructor produces one after the other, and any
node between them ends the run — a zero-length text node included, which is discarded but discarded after
the space has been decided. The engine had that, and the suite's `on-empty-113a` is why. What it did not
have is the document node, which is the one item it wrote nothing at all for.

`xsl:document` with empty content returned before writing anything, on the reasoning that an empty
document node contributes nothing. It contributes no *children*. It is still an item, which is what a
declaration of `document-node()` asks for and what two atomic values on either side of it are not adjacent
across. Neither the serializer nor the tree builder noted the boundary for a document node with content
either, so a document holding one empty text node was invisible in the same way.

`seqtor-017` is a test of exactly this: a hundred iterations, each wrapping its value between two
`xsl:document` instructions that are empty except at the two ends. It wants `-- START --12 {3} 45 {6}`,
where the spaces fall only where nothing stood between two values, and read `-- START --1 2 { 3 } 4 5`
with a space everywhere.

The 3.0 run goes from 7,890 of 7,924 to **7,891** and the schema-aware run from 8,451 of 8,526 to
**8,452**; the 2.0 and XPath runs are unmoved, the two backends agree test for test, and nothing that was
passing fails.

### What the driver was comparing against

Three failures that were the driver's reading of the tests and not the engine's of the language. The
catalog defines `assert-xml` as a serialization of the result with the *default* parameters —
`method="xml" indent="no" omit-xml-declaration="yes"` — and the driver was comparing the result as the
stylesheet asked for it, which for every other assertion is the right thing and for this one is not.

It matters where the output method is html or xhtml. Both add a `meta` element to `head` that the result
tree never held, and html writes `<META>` with no closing tag, which no XML parser reads. A test needs no
`xsl:output` to get there: the default method is html when the document element is `html` in no namespace,
and xhtml when it is `html` in the XHTML namespace and the stylesheet does not say 1.0. `sequence-0601`
produces `<HTML>` and `bug-1901` an XHTML `html`, and both expected results have the `meta` element removed
by hand, which is the suite recording the same reading.

`assert-xml` now looks twice: the result as it was serialized, and, where that does not match, the result
tree serialized again with the default parameters. Nothing that matched before stops matching, and the
second serialization costs a second run paid only by a test that would otherwise have been reported failing.

The 3.0 run goes from 7,891 of 7,924 to **7,894**, the 2.0 run from 5,592 of 5,622 to **5,594** and the
schema-aware run from 8,452 of 8,526 to **8,455**. The XPath runs read no XSLT and are unmoved.

### What a number that will not fit sixty-four bits is rounded and written as

`xs:integer` stopped being bounded here some while ago, and four places went on holding one in a
<code>long</code> anyway. Each of them was found by a test that asked for a number wider than that.

**Rounding at a precision went through a `decimal`.** `round($x, -1)` and `round-half-to-even($x, -1)`
scaled the value down, rounded and scaled back up in `decimal`, then narrowed the result to a `long`.
A `decimal` holds 28 digits and a `long` 19, so `round(123456789012345789011, -1)` overflowed the
narrowing — silently in the first, which caught the overflow and fell back to a `double` and answered
`1.2345678901234578E20`, and not at all in the second, which let an `OverflowException` out of the engine.
An integer is now rounded as an integer: the division, the remainder and the comparison against the half
are all `BigInteger` and lose nothing. That is `math-3601`, which rounds a 21-digit literal at the tens.

**`xsl:number` counted in an `int`.** The value was rounded to an `int` and clamped to `int.MaxValue` past
it, so `number-0111` — which numbers 1234567890 cubed — wrote `-2:147483647`. Clamping is the one thing a
bounded path must never do: it answers a question about a different number. The numbers are `BigInteger`
now, from the value through `start-at` to the format token, and the narrow path is still the narrow path
for everything that fits it.

**`format-integer` and `format-number` refused.** Both asked for the value as a `long` and raised
`FOAR0002` where it would not go, which was honest but not required of them: the digits of a value are its
digits however many there are, and the padding and grouping are counted from the right either way. The
sequences that genuinely cannot present a number that size — roman numerals, which stop at 4999, and words,
which would fill pages — fall back to the digits, which is what the specification asks of a sequence that
cannot render a value and what they already did above `int.MaxValue`.

One thing came with it that no test asked for and the specification does: **`grouping-separator` and
`grouping-size` group a digit token whatever family its digits are from**. They were applied on the fast
path, which writes Latin digits, and dropped on the path that reads the token as a `format-integer`
picture, which is where every other family goes. `number-0111` wants the same number three times, the
third in Arabic-Indic digits, and all three grouped.

The 3.0 run goes from 7,894 of 7,924 to **7,896** and the schema-aware run from 8,455 of 8,526 to
**8,457**; the 2.0 and XPath runs are unmoved, the two backends agree test for test, and nothing that was
passing fails. One unit test changed with the code: it recorded the `FOAR0002` refusal as the property
those two forms were meant to keep, and what they keep now is the number.

### What upper case is, when a letter has no capital of its own

`fn:upper-case` and `fn:lower-case` are defined against Unicode's *default case operations*, which are
the **full** case mappings without tailoring for any language. .NET's `ToUpperInvariant` applies the
*simple* mappings, which are one character to one character. The difference is every letter whose capital
is more than one character: the sharp s uppercases to `SS`, the ffi ligature to `FFI`, j with caron to a
capital J and a combining caron, and a Greek letter with a subscript iota to the letter and a capital
iota beside it. There are 102 of them, and .NET left all 102 alone.

The table here is the unconditional part of the Unicode Character Database's `SpecialCasing`: every
character whose full mapping is longer than one character and does not depend on context. The conditional
entries are left out for the reason the specification gives — the language-specific ones are excluded by a
function defined to be locale-insensitive — except the Greek final sigma, which is neither conditional on a
language nor decidable from the character alone, and is not implemented.

The substitution runs before the simple mapping rather than instead of it. Every replacement is text the
simple mapping then leaves alone, so the two compose; and letting .NET map the rest is what keeps a
surrogate pair a pair, which a character-at-a-time loop would not.

`string-135` uppercases every character of Latin-1 and reads back the code points. The only one of the
102 that lives there is the sharp s, and that one character was the whole of the failure.

The 3.0 run goes from 7,896 of 7,924 to **7,897** and the schema-aware run from 8,457 of 8,526 to
**8,458**; the 2.0 and XPath runs are unmoved and nothing that was passing fails.

### The rest of 3.0

Where XSLT 3.0 stands here, as of 7 September 2026. The suite measures this half under `--xslt --30`, and it
stands at **7,524 of 7,561, 99.5%** — from 4,994 of 6,427 when the run was first taken, and against a 2.0
control that has never been higher, **5,288 of 5,319, 99.4%**. The two numbers are always given together
because the denominator moves: opening a feature the suite writes *around* stops whole files being skipped,
which is why the rate can fall while the work goes forward. It did so twice on the way here, once for 162
compile-only error tests and once for the 382 tests whose own stylesheets declare `xsl:initial-template`.
Both figures are now the same under either backend, the suite having been pointed at the emitted one as well
— see *What the emitted backend was never asked*. Unit tests stand at 2,516.

**Every instruction and declaration XSLT 3.0 adds is implemented, and every attribute it hung on an element
that already existed.** There is no refusal of the form *"this element has no such attribute"* anywhere in
the 3.0 run, and the whole vocabulary — elements, attributes, functions, the six boolean spellings, the
`Q{uri}local` name form — follows the processor's version rather than the stylesheet's claim (see *Which
version's vocabulary a processor reads*). The most recent stretch closed the last of the whole features:
`xsl:merge` refuses what it must and orders as it should; an accumulator applies only to a document that
asked for it, answers for a node once that node's own rule has fired, and travels with a copy, a
`copy-of()` and a `snapshot()` alike; the declarations of one mode merge attribute by attribute across import
precedence; a reference to a function that reads the focus takes the focus with it; and a package sees of
another exactly what that package offers — a private component of a library does not exist from outside it,
and an abstract one nobody supplied is absent. `decl/accumulator` went from 37 failures to 3 on that, and the
three left are the namespace axis, an HTML serialization detail and a test whose entry point is not public.
And a sequence constructor's items become text by the rules of §5.7.1 and §5.7.2 (see *What a sequence
constructor's items become*), which took `misc/seqtor` from 49 failures to 24 and reached 77 tests across the
3.0 run and 26 across the 2.0 one, `insn/sequence` among them. Then `fn:xml-to-json()` came to check and
write what the specification says (see *What `fn:xml-to-json()` writes, and what it refuses* under JSON),
39 failures to 1, and with it a parenthesized item type — `(function(*) as xs:string)?` — which the parser
had no production for and fourteen `expr/higher-order-functions` tests turn on, and a function called by a
name that carries its namespace, `Q{urn:f}twice(2)`, which had been split at the first colon of the URI.
`fn:available-system-properties()` then arrived: the names of every property `system-property()` answers,
`xsl:version` and the thirteen fixed ones, as QNames and as a constant, since nothing about the processor
changes while a transformation runs. Its twenty-eight tests reach it by every spelling a function has —
bare, prefixed, `Q{…}`, `function-lookup()` — and the prefixed and braced ones had been going straight to the
XPath library without the stylesheet's own functions getting first refusal, which they now do for a name in
the functions namespace as they always did for a bare one. And `xsl:sort` came to compare keys by what
they are and to take its ordering attributes as the templates they may be (see *What a sort compares*),
which with a function without `as` returning the sequence its body made was 42 tests on the 3.0 run and 37
on the 2.0 one, `insn/sort` down to one test whose expected output is in an encoding the driver misreads.
Then `xsl:result-document` took every serialization attribute as the template it is, the format included,
and a named `item-separator` came to separate every top-level item (see *Two more things a result document
settles*): 27 tests, `insn/result-document` from 35 failures to 19. The driver also began recognising an
entry point declared under a prefix other than `xsl`, which put thirteen more tests into the run, eleven of
them asking for the json and adaptive output methods this engine does not have. Then `xsl:on-empty` came to
answer for any sequence constructor, with the rest of what *Markup around content that may not be there*
above describes, and a copied document node became one item of simple content: 50 tests, the whole of
`insn/on-empty` among them, and the driver came to read a text result's string value and to bind `$result`
for an assertion, which two more tests turn on. Then the error set itself (see *The codes the suite asks for*):
thirty-one codes drawn where the specification draws them, `misc/error` from 48 failures to 17, and a
template's `as` honoured at last, which reached tests in a dozen other sets — 86 on the 3.0 run and twelve
on the 2.0 one. The driver now skips a test whose initial mode brings its own match selection, an atomic
value to apply templates to, which is a way in this engine does not have; ten tests moved from failing to
skipped on that, none of them passing before. Then `xsl:evaluate` itself (see *What a target expression
may see*), the last whole instruction of 3.0 this engine lacked: 42 tests came into the run and 41 pass, and
with it `position()` refusing where there is no focus, `element-available()` answering for the 3.0
instructions, an `xsl:with-param` honouring its own `as`, and a function argument's type error given the
code 3.0 renamed it to — 19 more on the 3.0 run, seven on the 2.0 one, and `misc/error` from 17 failures to
9. Then `attr/match` (see *What a pattern reaches, and what an error inside one means*): 66 tests on the
3.0 run and 38 on the 2.0 one, the set itself from 46 failures to 4 and, on the 2.0 run, from 19 to
none. Then `insn/copy` (see *What a copy carries*): 20 tests on the 3.0 run and 5 on the 2.0 one,
the set from 28 failures to 12. Then the package model's versions and package-local declarations, with
`unparsed-entity-uri()` answering nothing (see *What a package keeps to itself*): 70 tests on the 3.0
run, 11 on the 2.0 one, `decl/use-package` from 27 failures to 2 and `insn/where-populated` from 26
to none. Then what an override replaces (see *What an override replaces*): 36 tests on the 3.0 run,
`decl/override` from 21 failures to none and `decl/package` from 14 to 3. Then the json and adaptive output
methods, with a group of anything at all (see *What the json and adaptive methods write, and what a group
holds*): 49 tests on the 3.0 run and 13 on the 2.0 one, `decl/output` from 21 failures to 2 and
`insn/for-each-group` from 20 to none. Then where a result document goes and what a static variable is
worth (see *What a result document knows, and what a static variable is worth*): 49 tests on the 3.0
run and 5 on the 2.0 one, `insn/result-document` from 18 failures to 1, `attr/static` from 16 to
2 and `fn/current-output-uri` from 13 to 1. Then what a built tree is relative to and which mode is a way
in (see *What a built tree is relative to, and which mode is a way in*): 34 tests on the 3.0 run and
18 on the 2.0 one, `fn/base-uri` from 16 failures to 1 and `attr/mode` from 14 to 2. Then what a key finds
and what a copy is compared as (see *What a key finds, and what a copy is compared as*): 25 tests on
the 3.0 run and 35 on the 2.0 one, `fn/key` from 14 failures to none and `insn/copy` from 12 to 10. Then
what a date is written as and what a zero-length match is (see *What a date is written as, and what a
zero-length match is*): 22 tests on the 3.0 run and 17 on the 2.0 one, `fn/format-date-en` from 11
failures to none and `insn/analyze-string` from 11 to none. Then what a catch is told and what a map
leaves alone (see *What a catch is told, and what a map leaves alone*): 23 tests on the 3.0 run and
9 on the 2.0 one, `insn/try` from 11 failures to none and `decl/character-map` from 10 to none. Then what
a namespace node is and what a bare dot is worth (see *What a namespace node is, and what a bare dot is
worth*): 40 tests on the 3.0 run and 2 on the 2.0 one, `fn/snapshot` from 18 failures to none. Then what a
date's year and timezone are written as (see *What a date's year and timezone are written as*): 8
tests on the 3.0 run and 10 on the 2.0 one, `fn/format-date` from 8 failures to none. Then what a package's
version is and what a shadow attribute says (see *What a package's version is, and what a shadow attribute
says*): 13 tests on the 3.0 run, `attr/package-version` from 10 failures to 1. Then what a 2.0 regular
expression reads by (see *What a 2.0 regular expression reads by*): 8 tests on the 2.0 run and 2
on the 3.0 one, `misc/regex-syntax-xslt20` from 10 failures to 2. Then what a document type declaration
says (see *What a document type declaration says*): 82 more tests passing on the 3.0 run and 69 on the 2.0
one, 5 and 4 of them failures before and the rest kept out by the `dtd` feature, which is no longer skipped;
`fn/id` from 5 failures and 35 skipped to 1, `fn/unparsed-entity-uri` from 12 skipped to none failing.
Then what midnight is and what a duration is written as (see *What midnight is, and what a duration is
written as*): 7 tests on each run, `type/date` from 7 failures to none on both. Then what namespaces an
element has and what a name test may leave out (see *What namespaces an element has, and what a name test
may leave out*): 14 tests on the 3.0 run and 9 on the 2.0 one, `type/namespace` from 7 failures
to none and from 6 to 1, `decl/strip-space` from 6 to 1 and from 4 to 2. Then what a union in a match is and
what a next match carries on from (see *What a union in a match is, and what a next match carries on from*):
8 tests on the 3.0 run and 6 on the 2.0 one, `insn/next-match` from 7 failures to none and from
6 to none. Then what json-to-xml was told to do (see *What json-to-xml was told to do*): 12 more tests passing on
the 3.0 run, `fn/json-to-xml` from 9 failures to 1 with three of its own out of the skips. Then what a
stylesheet says it will be run against (see *What a stylesheet says it will be run against*): 7 tests
on the 3.0 run, `decl/global-context-item` from 7 failures to none. Then what a function declaration may say
and what an empty text is (see *What a function declaration may say, and what an empty text is*): 5
tests on the 3.0 run and 8 on the 2.0 one, `decl/function` from 7 failures to 2 and from 5 to 2. Then what
numbering anywhere counts and what an iteration leaves behind (see *What numbering anywhere counts, and what
an iteration leaves behind*): 7 tests on the 3.0 run and 3 on the 2.0 one, `insn/number` from 6
failures to 2 and from 5 to 2, `insn/iterate` from 6 to 3. Then what a type annotation says where nothing was
validated (see *What a type annotation says where nothing was validated*): 6 tests on the 3.0 run and
5 on the 2.0 one, `type/type` from 6 failures to 1 and from 5 to 1. Then what a comment in a stylesheet does
to the text around it (see *What a comment in a stylesheet does to the text around it*): 6 tests on
the 3.0 run and 5 on the 2.0 one, across five sets. Then `fn:transform()`, which runs a transformation from
inside an expression and hands back a map of what it produced (see *What a transformation run from inside
one hands back*): 9 tests on the 3.0 run, the whole of `fn/transform`. Then what one function's type is
against another (see *What one function's type is, against another*): 10 tests on the 3.0 run, the whole of
`expr/higher-order-functions` and `decl/variable`. Then what a static expression may see and where one is
looked for (see *What a static expression may see, and where one is looked for*): 4 tests on the 3.0 run
and 5 on the 2.0 one, `attr/use-when` down to the one test that asks for a vendor URL. Then what a double
really is when it is rounded (see *What a double really is when it is rounded*): 4 tests on the 3.0 run and
3 on the 2.0 one, `expr/math` down to two tests that are about something other than arithmetic. Then what
being available as a stream means to an engine that does not stream (see *What being available as a stream
means to an engine that does not stream*): 6 tests on the 3.0 run, the whole of `fn/stream-available`.
Then what an attribute is called and what a sequence of nodes is not (see *What an attribute is called, and
what a sequence of nodes is not*): 13 tests on the 3.0 run and 14 on the 2.0 one, with seven more on each
out of the skips. Then what `doc()` is asked and what it answers against (see *What `doc()` is asked, and
what it answers against*): 3 tests on each run. Then what backwards compatibility converts (see *What
backwards compatibility converts*): 5 tests on the 3.0 run and 4 on the 2.0 one. Then what may be said
once and what may not be said at all (see *What may be said once, and what may not be said at all*): 6
tests on the 3.0 run and 1 on the 2.0 one. Then what a message is and what a template says it stands on
(see *What a message is, and what a template says it stands on*): 11 tests on the 3.0 run and 1 on the 2.0
one, and two off the 3.0 run's denominator. Then where an expression ends and where a node does (see *Where
an expression ends, and where a node does*): 14 tests on the 3.0 run and 6 on the 2.0 one. Then what an
array is to a template and what a map takes as one key (see *What an array is to a template, and what a map
takes as one key*): 5 tests on the 3.0 run. Then what a comparison was written among (see *What a
comparison was written among*): 8 tests on the 3.0 run and 3 on the 2.0 one. Then what the caller may hand
the way in (see *What the caller may hand the way in*): 3 tests on the 3.0 run, and nine more read across
the two that had been skipped. Then what a cast may name and what a next iteration may supply (see *What a
cast may name, and what a next iteration may supply*): 5 tests on the 3.0 run. Then what a source document
may be a fragment of (see *What a source document may be a fragment of*): 1 test on the 3.0 run. Then
what language a number is spelled in (see *What language a number is spelled in*): 1 test on each run, and
four more in each out of the skips. Then what backwards compatibility restores and what it does not (see
*What backwards compatibility restores, and what it does not*): 2 tests on each run, the whole of
`expr/xpath-compat`. Then what a declared type admits and what it converts (see *What a declared type
admits, and what it converts*): 4 tests on the 3.0 run and 3 on the 2.0 one, the whole of `attr/as`. Then
what a document URI is a property of (see *What a document URI is a property of*): 2 tests on the 3.0 run
and 3 on the 2.0 one, the whole of `fn/accessor`. Then which validation a processor without a schema
refuses (see *Which validation a processor without a schema refuses*): 1 test on the 3.0 run and 3 on the
2.0 one, the whole of `attr/validation`. Then which type names a processor has in scope (see *Which type
names a processor has in scope*): 2 tests on each run, the whole of `fn/type-available`. Then what a file read as text may hold
(see *What a file read as text may hold*): 2 tests on the 3.0 run, the whole of `fn/unparsed-text-lines`.
Then what a function-lookup written in a package can find (see *What a function-lookup written in a package
can find*): 2 tests on the 3.0 run, the whole of `fn/function-lookup`. Then what an extension instruction
falls back to (see *What an extension instruction falls back to*): 5 tests on the 3.0 run and 3 on the 2.0
one, three of each out of the skips. Then which whitespace a package strips from what it reads (see *Which
whitespace a package strips from what it reads*): 2 tests on the 3.0 run, the whole of `fn/document`. Then where a variable's scope ends (see *Where
a variable's scope ends*): 2 tests on each run. Then what may stand in for an xsl:value-of's select (see
*What may stand in for an xsl:value-of's select*): 2 tests on the 3.0 run and 1 on the 2.0 one. Then what a picture may carry an exponent by and what
a URI reference may not (see *What a picture may carry an exponent by, and what a URI reference may not*):
2 tests on the 3.0 run and 1 on the 2.0 one. Then what is read of a declaration from a later version (see
*What is read of a declaration from a later version*): 1 test on the 3.0 run, the whole of `misc/forwards`.
Then what a processor asked to be 3.0 says it is (see *What a processor asked to be 3.0 says it is*): 3 tests
on the 3.0 run, the whole of `fn/system-property`, `attr/shadow` and `attr/package-version`. Then which
functions this engine says it has (see *Which functions this engine says it has*): 2 tests on the 3.0 run,
the whole of `decl/function`. Then which collation a target expression compares by (see *Which collation a
target expression compares by*): 1 test on the 3.0 run, the whole of `insn/evaluate`. And a mode is a way
in that takes parameters, which the driver had been reading only from an `initial-template` (see *What a
built tree is relative to, and which mode is a way in*): 1 test on the 3.0 run, the whole of
`misc/initial-mode`.

**What is not implemented, and will not be**, is the one omission this engine chose: **streaming**
(2,900 tests skipped, and every accumulator is computed over a tree already held). The namespace axis and
DTD processing were two more, until `fn/snapshot` showed what the first cost (see *What a namespace node
is, and what a bare dot is worth*) and the second turned out to be guarding against something the null
resolver already covered (see *What a document type declaration says*). Schema-awareness was never on the
table; 672 tests ask for it and are skipped.

**What is not implemented yet**, in the order it shows up in the run:

- **The rest of the package model.** Visibility across packages, versions, what a package keeps to itself
  and what an override replaces are settled (see *What one package may see of another*, *What a package
  keeps to itself* and *What an override replaces*). What is not is a package used by two others that each
  override it differently, which this flattened model cannot hold two copies of, and re-exposing through
  `xsl:expose` a component a package accepted rather than declared. That is `decl/override`, `decl/package`
  and `decl/use-package` together: 5 failures, down from 121.

- **Five one-offs that were looked at and left**, each for a reason worth writing down rather than
  rediscovering. `sort-079` asks for the UCA `alternate` option — variable weighting, where a space or a
  hyphen is either ignored outright or sorted before every ordinary character — which
  `System.Globalization.CompareInfo` does not expose; the test's own author records that he is not
  convinced by the expected results either. `base-uri-052` reads its source through XInclude, which nothing
  here does. `doe-0191` writes `disable-output-escaping` inside an `xsl:try`, whose content is built as a
  tree so that the catch can discard it, and the flag is a serialization property no tree carries; the
  test's comment says Saxon cannot do it either. `accumulator-038` starts at a named template of an
  `xsl:package` that does not say `visibility="public"` — which the suite itself decided is not an eligible
  entry point, `package-version-001` having been changed in 2019 to say exactly that — so the `XTDE0040`
  raised here is the suite's own rule applied to a test written before it. And `sort-078` writes
  `stable="YES"` in a `version="3.0"` stylesheet, which a 2.0 processor reads forwards-compatibly: §3.11
  names three things forwards compatibility changes and the value of an attribute the element does have is
  not among them, but refusing it would be refusing a stylesheet for being newer in exactly the way 3.0
  widening yes/no to the six boolean spellings already showed can happen.

**`XsltVersion.Implemented` said `V20` when this was written**, so a caller who named no version got a 2.0
processor: it answered `2` to `system-property('xsl:version')` and read a `version="3.0"` stylesheet
forwards-compatibly, where an `xsl:mode` at the top level and `expand-text="yes"` were ignored. Moving the
default was the last thing and not the first, and the condition set for it — every 3.0 element and attribute
in `XsltElements`, the list above down to corners — was met with the 99.5% figure; it says `V30` now (see
*The claim moves to 3.0*).

### What a stale skip list hides

Each conformance driver holds a hand-written list of the suite's feature names it does not claim, and skips
every test that declares one. Nothing checks the list against the engine, so an entry outlives the omission
it describes: the tests go on being skipped, and the summary reports them as a feature this engine has not
got rather than as a measurement nobody took.

`namespace_axis` in the XSLT driver and `namespace-axis` in the XPath driver had both outlived the axis by a
long way (see *The namespace axis exists*). Taking the two entries out brought 71 XSLT tests and 9 XPath tests
into the runs. Fifty-nine of the 71 passed unchanged. **Twelve failed, and every one was a real defect** that
the suite had been ready to report for as long as the entry stood. They came to five causes:

- **`xmlns:xml` was written out.** The binding of the `xml` prefix is implicit in every XML document, so a
  namespace node carrying it belongs to the data model and never to the output. Walking the namespace axis is
  what brings one along, which is why nothing had ever noticed: `namespace-0601`, `namespace-0603`,
  `namespace-2602` and `copy-3501` all differed from their expected results by that one attribute.
- **`base-uri()` of a namespace node answered the element's.** A namespace node is not a place in the document
  that a relative reference could be written from, and the data model gives it the empty sequence. Four tests
  in `fn/accessor` and `fn/base-uri` asked exactly that and were told the parent's URI instead.
- **`xsl:key` did not index namespace nodes.** From 3.0 a match pattern may name `namespace-node()`, and the
  index was built over elements and their attributes only, so such a key filed nothing and `key()` answered
  nothing. Namespace nodes are made on demand rather than held in the tree, so they are now walked for a key
  whose pattern requires that kind and for no other — the cost falls only on a stylesheet that asked.
- **`current()` inside a key's `use` was not the node being indexed.** There is no other current node while an
  index is built, and `key-058` reaches for it to read an attribute of the node from an expression that has
  moved the context on to a namespace node.
- **A copied namespace node taking the element's own prefix was dropped.** The rename was already written for
  `xsl:namespace`, which reports a clash it cannot move; the copy path silently preferred the element's name.
  Namespace fixup says the other way round: the node keeps the prefix it came with and the element takes
  another for the namespace it is in, which is what `namespace-2001` asserts.

The three entries that looked as stale and were not are worth as much. `higherOrderFunctions`,
`fn-transform-XSLT` and `fn-transform-XSLT30` name `function-lookup()` and `transform()`, which this engine
has — inside a stylesheet. The XPath driver evaluates an expression on its own, with no `XsltOptions` and no
stylesheet to carry a scope, so neither function is in the static context it builds. Removing the three ran
1,676 more tests and failed 807 of them, and they went back in. That limit is the driver's, and it belongs
beside `schemaImport` and `schemaValidation`, which are in the list because the driver cannot load an
environment's schemas rather than because the engine cannot validate.

### Measuring the 3.0 work

The QT3 driver runs the XPath 2.0 subset by default, asking the engine to be 2.0 for it. `--31` takes in
the tests marked `XP30+` and `XP31+` as well, which is how the work above is measured:

```
dotnet run --project CodeDeeds.Xslt.Conformance -- --31 <path-to-qt3tests>
```

That reads 17,629 tests where the 2.0 run reads 14,173, and both stand at **99.8%**. The
gap is mostly the one 3.1 function listed as absent above — `fn:load-xquery-module` — and the
schema-aware forms, which this engine will never have. The unbounded `xs:integer`, the years before the
common era, the JSON options and the whole of `op/to` were all on this list and none of them is now;
`array`, `math` and `misc` are at a hundred per cent, and no test set on either run holds more than
five failures.

What is left divides in two, and rather more than half of it is the driver. It wires up no collections,
so `fn:collection()` has none to find; it supplies no document for `fn:doc-available()` and no language
but English; and its `assert-eq` builds the expected value with a small literal parser rather than
through the engine, which for half a dozen tests cannot build what the expectation names and reports a
mismatch between two texts that read identically. Those are limits of how the tests are presented and
not of what the engine does.

The engine's own share is now ones and twos with no cause in common. `xs:dateTimeStamp` is an XSD 1.1
type and this processor reads XSD 1.0. `op:same-key`, which maps are keyed by, is not `eq` and is
implemented as though it were, so a float and a decimal that differ past the float's precision share a
key. A few reserved-name and EQName rules are not enforced at parse time. And `fn:distinct-values` still
keys a decimal apart from the float it is equal to, numeric equality across those two types being
decided at the narrower precision and a key having no way to know what it will be compared against.

The driver reads the `decimal-format` declarations in a test's environment and declares them against the
static context, which is the same path a stylesheet's `xsl:decimal-format` takes into the engine.

It also reads the **`+`** in a spec dependency, which is not decoration. `XP20` is a test about XPath 2.0 and
no later version, which is where the suite puts the things a later version changed its mind about:
`tokenize` with one argument is an arity error there and a function here, and `string-join` of integers is a
type error there and a string here. Both readings are in the suite as separate tests, and reading the earlier
one in the 3.1 run measured the wrong language. Thirty-two tests moved to skipped when this was fixed, which
is why the denominator fell.

The driver reports **where** the failures are as well as what they are, by test set. The two are different
questions and the first is the one that decides what to do next: one absent function shows up as a dozen
unrelated-looking reasons, while a reason shared across twenty files is nobody's next task. It was that
report which found the 629 regex tests above, all of them behind a single construct and none of them
visible in the summary by reason.

The driver **judges** the `language`, `default-language` and `format-integer-sequence` dependencies rather
than skipping every test that carries one. English is a language this engine has, and the decimal digit
families are numbering sequences it has, so those tests are ones it should be measured on; the sequences that
are a list of symbols rather than a family of digits are not, and a test that asks for one is skipped as
before. The `satisfied="false"` form is honoured in both directions, so a test that applies only to a
processor *without* a given sequence now runs here.

It declares the `xpath-1.0-compatibility` feature **unsupported**, which is honest rather than modest: an
expression is compiled against a stated version here and the driver states 2.0 or 3.0, so there is no mode in
which `format-number('foo', '#')` answers `NaN` rather than raising a type error. Two tests ask for that mode
and are skipped as asking about a language this run is not running.

The largest remaining blind spot is `assert-type`, 411 tests the driver skips rather than judges. Checking one
would mean asking this engine's own `instance of` whether the result is of the asserted type, which is
circular in precisely the wrong direction: those tests are a test *of* the type matcher.


