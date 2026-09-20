# CodeDeeds.Xslt performance review

2026-09-19, commit `5ac67f5`. i7-6700K (4 cores), Windows 10, .NET 10.0.12, Release, default JIT settings unless stated.

## Verdict

The engine is well designed and already carefully tuned. The document model (preorder parallel arrays, integer document order, interned names, pooled storage), template dispatch (indexed by mode, node kind and name), slot-resolved variables and the allocation-free `value-of` path are all the right designs. Scaling is nearly linear: 5.9, 6.0 and 6.8 microseconds per product at 100, 1,000 and 10,000 products. The 14% rise at 10,000 is real. I have not found its cause; at that size the tree's arrays are about 14 MB, which no longer fits the 8 MB cache of the machine it was measured on, and every array is on the large object heap.

Against `System.Xml.Xsl.XslCompiledTransform`, the framework's IL-compiling XSLT 1.0 processor, on the repository's benchmark stylesheet (BenchmarkDotNet, `FrameworkComparisonBenchmarks`):

| End to end | CodeDeeds | XslCompiledTransform | Allocation |
|---|---:|---:|---|
| 100 products, both returning a string | 584 µs | 566 µs | 175 against 421 KB |
| 1,000 products, both returning a string | 5.92 ms | 6.80 ms | 1,471 against 3,555 KB |
| 100 products, both to a do-nothing `TextWriter` | 552 µs | 540 µs | 103 against 281 KB |
| 1,000 products, both to a do-nothing `TextWriter` | 5.45 ms | 6.13 ms | 774 against 2,204 KB |

So it is level with the framework on a small document, where the framework's 2 to 3% lead is inside the noise, and 11 to 13% ahead on a large one. It allocates 2.4 to 2.8 times less. On one very common idiom, the classic identity template, it is 1.24x slower. The two processors' outputs differ by about 4% in length, from indentation and `meta` handling; both emit 1,001 rows.

My timing harness had the two level at 1,000 products. It ran with concurrent garbage collection switched off, which I chose to reduce noise. That flattered the framework, which allocates 2.4 times as much and puts a large `StringWriter` on the large object heap on every call. BenchmarkDotNet runs under the runtime's default settings, which is what an application gets, so its figures are the ones to use.

The most valuable things I found are not micro-optimisations.

**The largest is that a predicate in a match pattern costs the square of the sibling count.** `match="item[@type='a']"` over 16,000 siblings takes 664 ms and allocates 2 GB, where the same test written inside the template takes 4.5 ms. A reviewer found it by reading the code and it was measured last, which is why it is finding 9 below and not finding 1.

The others, in the order I found them:

1. The `Compiled` backend gives no gain on the repository's own benchmark stylesheet, and every transformation benchmark selects it.
2. The classic identity template is 2.3x slower than `on-no-match="shallow-copy"` doing the same job.
3. One transform makes 100,000 `Write` calls averaging 3.6 characters.
4. 30 to 45% of steady-state speed comes from the runtime's dynamic PGO, and the first transform in a process costs 58 ms for a document that takes 0.6 ms once warm.
5. The benchmark suite measures several things other than what its names say.

## How this was done, and what it does not cover

- **Reading.** I read the document model, the emit backend and the entry points myself. Four parallel read-only reviewers covered the runtime and output path, instruction execution, XPath evaluation, and stylesheet compilation. Their findings are from reading code, not from measurement. Below I say which of their claims I checked myself and which I did not.
- **Measuring.** I snapshotted the source at `5ac67f5` into a scratch directory and built a timing harness against it, so nothing was written into the repository's `bin`/`obj`. It reports the median of 15 batches and bytes allocated per operation. It is a differential tool for attributing cost on one machine, not a substitute for BenchmarkDotNet. Run-to-run noise was about 2%, so I do not read anything into differences smaller than about 5%.
- **Measuring again.** Afterwards the same workloads were written as BenchmarkDotNet classes and run once, 53 cases in 16 minutes, from a copy of the working tree. A pattern-predicate class followed, 18 cases in six and a half minutes; it had no harness counterpart. Wherever such a class exists, the figures in this document are BenchmarkDotNet's means and the harness's have been replaced. They agreed in direction everywhere and differed in size in five places, which the last section lists. Figures still marked "harness" have no benchmark class.
- **Few workloads.** Almost everything measured is the products stylesheet and its variants, plus an identity transform and a flat list matched by patterns with predicates. Keys, grouping, sorting, `xsl:number`, temporary trees, large stylesheets and JSON were read but not measured. Findings in those areas are hypotheses until someone measures them.
- **No profiler.** `dotnet-trace` is not installed and I did not install it unasked. Attribution is by subtracting stylesheet variants, which tells you what a feature costs but not which method the time is in.

Source links in this document are relative to `source/CodeDeeds.Xslt/Documentation/`.

### Mistakes I made along the way, which affect how far to trust me

- I explained the compiled backend's 2.6x on predicates under `and` backwards. I had verified that the interpreter's `EvaluateAsBoolean` lacks a version check, then assumed without tracing it that interpreted predicates reach it, and predicted that fixing it would make the interpreter 2.5x faster. The session fixing the bug found the opposite, and a probe confirmed it: the emitted code is the side applying 1.0 rules. My own table already showed it, with the compiled timing identical at both versions and the interpreted timing not. Finding 3 is rewritten.
- I switched concurrent garbage collection off in my harness and did not think about what that did to a comparison between two processors that allocate very differently. It made the framework look level with this engine on large documents, where under default settings it is 13% behind. The verdict above uses the BenchmarkDotNet figures.
- I wrote the section on the benchmark suite without having read the benchmarks' README, which already discloses one of the problems I listed as a finding. That item now says so.
- I reported the first transform in a process as "about 30 ms". That was the mean of my harness's first 400 ms window. Measured properly with a cold-start benchmark, the first call is 58 ms, or 195 ms with the first compile.
- My first draft of this report said every quoted figure came from a settled run. Checking that sentence against the figures showed it was false for about a third of them. I re-ran those and corrected four numbers and one conclusion; see the last section.
- I first read the stored results as "XML is 3.4x slower than JSON for the same 100 products". The JSON stylesheet never reads its input, so that comparison meant nothing.
- I told the runtime reviewer that "Stream output allocates 2.2x" was a known library symptom to explain. It was the benchmark's own `MemoryStream` and `ReadToEnd`. I sent a correction before the reviewer reported.
- Partway through, one row came out at 10.9 ms and 1056 KB against 5.6 ms and 774 KB for the identical operation, repeatably. I proposed an `ArrayPool` capacity explanation; an experiment refuted it. I then proposed a per-path allocation explanation on the strength of an arithmetic coincidence; a second experiment showed it was the JIT's instrumented tier, because my fixed 2-second warm-up was too short. I changed the harness to warm up until time and allocation both settle, and re-measured every section I quote here. Two earlier figures changed as a result (shallow-copy identity 4.5 to 3.5 ms; small-document allocation 118 to 103 KB). The conclusions did not.

## Where the time goes

1,000 products, 24,003 nodes, 300 KB of XML in, 357 KB of HTML out.

| Phase | Time | Notes |
|---|---:|---|
| End to end, to a string | 6.0 to 6.1 ms | 1,471 KB allocated, of which 697 KB is the result string itself |
| End to end, to a do-nothing `TextWriter` | 5.5 ms | 774 KB allocated |
| Parsing (`XdmTreeBuilder.FromXmlText`) | 2.07 ms | a bare `XmlReader` loop reading every value is 1.08 ms of that |
| Transform of a pre-parsed tree | 4.10 ms | |
| of which the five `//product` aggregates | 0.85 ms | 20% (harness) |
| of which the 1,000 row templates | 3.3 to 3.4 ms | 80% (harness) |

For comparison, parsing the same text: `XPathDocument` 2.17 ms, `XDocument.Parse` 1.6 ms (harness).

What a row costs (1,000 template calls, interpreted, pre-parsed tree; `RowCostBenchmarks`):

| Row template body | Time | Step |
|---|---:|---|
| empty | 0.12 ms | dispatch and call: about 115 ns per node |
| `<tr/>` | 0.21 ms | |
| 8 literal elements with static text | 1.21 ms | about 140 ns per element with its text and indentation |
| the same with 7 `xsl:value-of` instead of static text | 1.98 ms | about 110 ns per `value-of select="name"` |
| the same with 2 of them as `format-number` | 2.35 ms | about 190 ns and 118 bytes per `format-number` call |

So serialising literal elements is the largest single component of this workload, then `value-of`, then `format-number`. `indent="no"` saves 0.15 to 0.2 ms. `method="xml"` came out 0 to 4% faster than `method="html"` across runs, which is inside the noise, so HTML-specific handling is not significant here. Allocation outside the result string is already low at about 350 KB per 1,000 rows, and `format-number` accounts for 235 KB of it.

## Findings supported by measurement

### 1. The classic identity template is the weak spot

Identity transform of the large document, end to end, to a do-nothing `TextWriter`:

| Stylesheet | Time | Allocated |
|---|---:|---:|
| `<xsl:copy-of select="."/>` | 2.70 ms | 430 KB |
| `<xsl:mode on-no-match="shallow-copy"/>` | 3.48 ms | 429 KB |
| `match="@*\|node()"` with `xsl:copy` and `apply-templates select="@*\|node()"` | 7.97 ms | 2,250 KB |
| the same stylesheet on `XslCompiledTransform` | 6.42 ms | 2,995 KB |

The identity template plus overrides is probably the most common shape of real-world XSLT, and most of it is written for 1.0, so it will not use `xsl:mode`.

Cause, verified by reading: `UnionExpr` ([Expr.cs:1235](../XPath/Expr.cs)) overrides only `Evaluate`. Each evaluation builds a `NodeSet` for the left operand, one for the right, a third for the result, and then sorts and de-duplicates. That happens once per element, and the measured 233 bytes per element is consistent with it. I have not profiled to confirm that this, rather than pattern matching or `xsl:copy`, dominates the time.

Suggested fix: give `UnionExpr` an `EvaluateNodes` override that collects both operands into one pooled list. Recognise the case where both operands are single steps from the context node on the attribute and child axes; the result is then already in document order with no duplicates, so it needs no sort. The generic rule list is also not split by node kind, so `@*|node()` is tested through the full `Matches` wrapper for every node (instruction reviewer's finding; I did not verify it).

### 2. The `Compiled` backend gives nothing on the benchmark stylesheet, and the documentation overstates it

Pre-parsed large tree, interpreted against compiled:

| Expression or stylesheet | Interpreted | Compiled |
|---|---:|---:|
| Full stylesheet, `version="3.0"` | 4.23 ms | 4.35 ms |
| The same stylesheet at `version="1.0"` | 3.86 ms | 3.81 ms |
| `count(//product[inStock='true'])`, 3.0 | 181 µs | 194 µs |
| `count(//product[inStock='true'])`, 1.0 | 148 µs | 93 µs |
| `count(//product[price>100 and rating>4])`, 3.0 | 313 µs | 121 µs |
| `count(//product[price>100 and rating>4])`, 1.0 | 210 µs | 124 µs |

These are from `BackendBenchmarks`. The full stylesheet there is the form with `avg()` spelt as a sum over a count at both versions, so that the version is the only difference between the first two rows. Allocations are the same in every pair to within 0.02 KB. Constructing the `Xslt` costs 299 µs interpreted and 399 µs compiled, so +33% and +37 KB.

Why, verified by reading:

- `CompiledExpr` routes only `Evaluate` to the emitted code. `EvaluateNodes` and `EvaluateAsBoolean` go straight back to the interpreted source ([ExpressionCompiler.cs:153-162](../Emit/ExpressionCompiler.cs)). `value-of`, `for-each`, `apply-templates` and `test=` use exactly those entry points, so they never run emitted code.
- Under 2.0 and later, `EmitComparison` deliberately emits a call back into the interpreter ([Expr.cs:1074](../XPath/Expr.cs)). Arithmetic and most functions do the same. Only seven `Emit` overrides exist in the whole library.
- A `DynamicMethod` is still built for every such expression and predicate, which is the extra construction cost.

Against the documentation on `XsltBackend.Compiled`:

- "roughly doubles the time to construct": measured 1.33x here. The repository's stored results say 1.36x, 1.05x and 1.02x for its three stylesheets.
- "about twice the interpreter's speed on predicate-bound work": true at 1.0, where it is 1.6x to 1.7x above. At 3.0 it is 7% slower on a plain string-equality predicate, and the 2.6x on predicates under `and` is a wrong answer given quickly (finding 3). On the full stylesheet it gains nothing at either version.
- "the two meet at roughly thirty transformations": on this stylesheet they never meet, because there is no gain to repay the construction cost.

Suggestions:

- Return the original expression when its root emits only the fallback, so nothing is built for no benefit.
- Correct the documentation to say what the gain depends on.
- Benchmark the default backend. Today no transformation benchmark measures `Interpreted`, which is what users get unless they ask.
- Decide whether the backend earns its keep. It cannot work under Native AOT, since it needs `Reflection.Emit`, and at 2.0 and later its one measured gain is a bug: see the next finding. What it demonstrably buys today is 1.6x to 1.7x on predicates in stylesheets that declare version 1.0.

### 3. The boolean entry points apply 1.0 comparison rules at every version: two bugs, and the 2.6x is one of them

These are correctness bugs, confirmed by running them. They belong here because one of them is the whole of the compiled backend's 2.6x in the table above.

Under 2.0 and later an untyped value compared with a string compares as a string, so with `price` = `10` the comparison `price < '5'` is true. Under 1.0 it is numeric and false. At version 3.0:

| Expression | Interpreted | Compiled | Right answer |
|---|---|---|---|
| `select="price < '5'"` | true | true | true |
| `test="price < '5'"` | false | false | true |
| `count(//p[price < '5'])` | 1 | 1 | 1 |
| `count(//p[price < '5' and true()])` | 1 | 0 | 1 |
| `count(//p[price < '5' or false()])` | 1 | 0 | 1 |
| `count(//p[not(price < '5')])` | 1 | 1 | 0 |

At version 1.0 the two backends agree on every row and are correct.

There are two sites, both verified by reading:

- **The interpreter.** `BinaryExpr.Evaluate` chooses between the 1.0 path and the typed 2.0 path by version ([Expr.cs:654-662](../XPath/Expr.cs)). `BinaryExpr.EvaluateAsBoolean` sends every comparison with a node-set operand to the 1.0 path with no version check ([Expr.cs:721-723](../XPath/Expr.cs)). `test=` reaches it, and so does `not(...)`, which is why the last row is wrong on both backends. It is the one row a test that compares the backends with each other cannot catch.
- **The compiled backend.** `BinaryExpr.Emit` guards the inline node comparison behind `CanTakeNodesDirectly`, which requires 1.0 behaviour. `BinaryExpr.EmitAsBoolean` calls the same `TryEmitNodeComparison` with no such guard ([Expr.cs:999](../XPath/Expr.cs)), and the operands of `and` and `or` are emitted through it. Those are the two rows where the backends disagree.

The performance consequence is the reverse of what an earlier version of this document said. The compiled backend's 121 µs on `[price > 100 and rating > 4]` at 3.0 is the emitted 1.0-rules comparison: it is the same code, at the same speed, as at 1.0 (124 µs), and it gives the wrong answer where the rules differ. The interpreter's 313 µs is the correct typed path. So at this commit the compiled backend has no real gain at 3.0 on any row of that table.

What the numbers do say about speed is that the correct typed path is half again as slow as the 1.0 path on the same comparison (313 against 210 µs), which is finding 8's subject.

I raised the first site as a separate task before I understood there were two, and sent that session these reproductions. It has since reported back, and what follows is its account, from an uncommitted worktree, which I have not verified. Both sites are fixed, with the route computed once in the constructor and read by every entry point, and `not()`, `boolean()`, conditions and quantifiers covered by tests that assert the answer and not only that the backends agree. Routing alone cost what the paragraph above implies: the compiled predicate under `and` went from 133 to 362 µs. It then made the typed route cheaper for an untyped tree against a single number or string, and had the compiled backend emit the inline child walk for the 2.0 route too, which brought that predicate back to 131 µs and took the interpreted one from 338 to 237 µs. If that lands, the 3.0 rows of `BackendBenchmarks` will change, and the figures in this finding describe `5ac67f5` only.

### 4. 100,000 `Write` calls per transform

One large transform makes 100,241 calls on its `TextWriter` for 356,892 characters: 3.6 characters per call, and 45,126 of the calls write a single character. With `indent="no"` it is 78,180 calls at 3.0 characters.

Against a do-nothing sink that costs nothing, which is why the output path looks cheap in isolation. Against any real writer each call has overhead:

| Target | Time |
|---|---:|
| do-nothing `TextWriter` | 5.47 ms |
| caller-supplied `StreamWriter`, plain UTF-8, 1K buffer | 6.00 ms |
| the library's own `Stream` overload | 6.05 ms |
| to a string, via `PooledStringWriter` | 6.08 ms |

These are from `OutputTargetBenchmarks`. In the harness a 16K buffer on the `StreamWriter` made no difference, and ASCII-only output showed the same gap, so it is per-call cost and not encoding.

Suggested fix: give `OutputWriter` its own character buffer (4 to 16K from `ArrayPool<char>`) that markup and text are copied into, flushed to the underlying writer in blocks. That turns 100,000 virtual calls into a few dozen. The gap between a sink and a real writer is 0.5 to 0.6 ms of 6.0 ms, so up to 9 or 10% end to end for every target except the do-nothing one. I have not implemented it, and copying into a buffer has a cost of its own, so that is a ceiling and not a forecast.

Related and smaller:

- The library's `Stream` setup allocates 38 KB more than a `TextWriter` target (812 against 774 KB). The runtime reviewer predicted about 39 KB from the custom encoder fallback (`MaxCharCount` of 12) inflating `StreamWriter`'s byte buffer at [SerializationEncoding.cs:83](../Runtime/SerializationEncoding.cs). The prediction matches the measurement, but neither of us verified the framework's behaviour. If right, the same is paid per `xsl:result-document`, and raising the buffer size would make it worse.
- Returning a string is 11% slower than writing to a sink and no slower than a real writer. Part of that is the per-call cost above, and part is that a 697 KB string lands on the large object heap. That is inherent in returning a string. It is worth saying in the documentation that the `TextWriter` and `Stream` overloads are the ones to use for large results.

### 5. Steady-state speed depends heavily on dynamic PGO, and the first call costs 60 to 230 ms

The same large transform, after settling (`JitSensitivityBenchmarks`):

| JIT configuration | Time | Ratio |
|---|---:|---:|
| Default (tiered compilation with dynamic PGO) | 5.50 ms | 1.00 |
| `DOTNET_TieredPGO=0` | 7.20 ms | 1.31 |
| `DOTNET_TieredCompilation=0` (fully optimised at once, no PGO) | 9.66 ms | 1.76 |

The harness's warm-up trace under defaults, as the mean per transform over successive 400 ms windows: 33 ms, 29 ms, 30 ms, 22 ms, 12.6 ms, 7.5 ms, 6.1 ms, then 5.7 ms steady. It takes about 4.7 seconds and a few hundred transforms to settle, and unoptimised code also allocates more (1,917 KB per transform at first against 774 KB).

The first call itself costs far more than the first window's mean. Measured with BenchmarkDotNet's cold-start strategy, one measurement from each of ten fresh processes (`ColdStartBenchmarks`, added with this review):

| First call in a fresh process | Time | Once warm |
|---|---:|---:|
| Compile the stylesheet and transform 100 products | 195 ms | about 0.9 ms |
| Compile the stylesheet and transform 1,000 products | 231 ms | about 6.4 ms |
| The same for 100 products with the IL backend | 202 ms | |
| Transform 100 products, stylesheet compiled during setup | 58 ms | 0.6 ms |
| Transform 1,000 products, stylesheet compiled during setup | 97 ms | 6.1 ms |

Those figures include loading `System.Xml` and compiling every method on the path for the first time, so much of it is the runtime's cost rather than the library's. I have not separated the two.

What follows from that:

- A process that does one transform, such as a CLI tool, a build step or a serverless function, pays about 200 ms where the steady-state figures suggest 1 ms. An earlier draft of this review said "about 30 ms"; that was the mean of my harness's first window, not the first call.
- Anywhere dynamic PGO is off or unavailable the library is 30 to 80% slower. Native AOT has no dynamic PGO, so I would expect it to land near the 9.7 ms figure. That is an inference; I did not build for AOT.
- The 5.5 to 9.7 ms gap is what PGO recovers, which for an interpreter means guarded devirtualisation and inlining of `Expr.Evaluate` and `Instruction.Execute` call sites. Making the hottest call sites statically devirtualisable would make the library fast without PGO and faster to warm up. Candidates are sealed classes and specialised instruction types for the commonest shapes, such as `value-of` of a single child step.
- `ColdStartBenchmarks` and `JitSensitivityBenchmarks` were added with this review so that both are tracked.

### 6. `format-number` is the main source of transform garbage

About 190 ns and 118 bytes per call. It accounts for 235 KB of the roughly 350 KB a 1,000-row transform allocates beyond its result string.

The XPath reviewer traced four to six short strings per call: `new string(digits[first..last])` at `DecimalDigits.cs:126`, slices at `:190` and `:204`, `PadRight` and `PadLeft` at `NumberPattern.cs:377,388`, and a final `ToString`. I did not verify those lines myself, but the measured 118 bytes per call fits. The picture is already parsed once and a thread-static builder is already used, so this is half done.

Suggested fix: keep the digits in a stack-allocated span through rounding, append the integer part, fraction and padding straight into the builder, or better, straight to the output.

### 7. `value-of select="name"` costs about 110 ns, mostly in path machinery

The XPath reviewer's reading of [PathExpr.cs:403-436](../XPath/PathExpr.cs): every path evaluation copies the `DynamicContext` struct (about 144 bytes by field count), rents two lists from the pool on top of the caller's, runs a `try`/`finally`, and ends with an `AddRange`, all to collect what is usually one child. That runs about 9,000 times in this transform.

Suggested fix: a fast path in `Evaluate`, `EvaluateNodes` and `EvaluateAsBoolean` for a path with no start expression and a single step, collecting straight into the caller's list. Separately, moving the seven references in `DynamicContext` that never change during a transform behind one reference would shrink every copy. Three reviewers independently flagged the size of that struct.

I did not verify the code, and the measurement only bounds the possible gain: `value-of` totals about 0.8 ms of the 4.1 ms transform.

### 8. String equality predicates under 2.0 and later are about 2x slower than they need to be

`count(//product[inStock='true'])` takes 181 µs at 3.0, against 93 µs for the 1.0 compiled form of the same work. It allocates nothing in either case, so the difference is per-candidate overhead.

The XPath reviewer attributes it to three things:

- a character-by-character `CompareByCodePoint` for `=` under the codepoint collation (`XdmComparison.cs:236`, `Collation.cs:450-467`), where `string.Equals` with `Ordinal` would do;
- the full context copy per candidate at `Expr.cs:1613`, where setting three integers would do;
- `CanTakeNodesDirectly` and `CanTakeNodesTyped` recomputed on every evaluation (four virtual calls each), where a field set once in the constructor would do. I verified that last one: they are expression-bodied properties over fields that never change ([Expr.cs:600-614](../XPath/Expr.cs)).

### 9. A predicate in a match pattern costs the square of the sibling count

This was first in the list of things found by reading only, with a note that it was the one to measure first. It has now been measured (`PatternPredicateBenchmarks`), and for the stylesheets it touches it outranks every finding above.

One template applied to each item of a flat list, in a tree already parsed. The first row is the same transformation with the test moved out of the pattern into an `xsl:choose`; the setup checks that the first four stylesheets give identical results.

| Pattern | 1,000 items | 4,000 items | 16,000 items |
|---|---:|---:|---:|
| `match="item"`, the test in an `xsl:choose` | 0.29 ms | 1.09 ms | 4.5 ms |
| `match="item[@type='a']"` | 2.40 ms | 33.2 ms | 664 ms |
| `match="*[@type='a']"` | 2.15 ms | 29.7 ms | 1,367 ms |
| `match="item[position() mod 2 = 1]"` | 2.57 ms | 33.5 ms | 650 ms |
| `match="item[1]"` | 2.31 ms | 33.5 ms | 645 ms |
| `match="item[@type='a']"`, the same items ten to a parent | 0.38 ms | 1.50 ms | 6.4 ms |

Each step is four times the items. The first and last rows go up four times a step, which is linear. The others go up thirteen to fourteen times at the first step and nineteen to twenty at the second, forty-six for the wildcard, where the square would be sixteen. At 16,000 siblings a pattern with a predicate is 146 times slower than the same test written in the template, and the wildcard form 300 times. Extrapolating the square alone, 100,000 siblings would be at least 25 seconds against 30 ms; I did not run that size.

The last row is what shows the cause: the same pattern over the same number of items stays linear when no item has more than nine siblings. The cost follows the siblings, not the document. Flat documents are exactly where it bites: rows, records, log entries, transactions.

Cause, verified by reading: `PredicatesHold` ([Pattern.cs:451-479](../Compiler/Pattern.cs)) collects every node the step selects from the anchor and calls `IndexOf` on the result as soon as a step has any predicate at all. `MayBePositional` sits a few lines below and already knows that `[@type='a']` cannot read a position, but it is consulted only for re-enumerating between predicates (line 498), never for the first pass. So a predicate that needs neither a position nor a size pays for both, once per candidate.

Above 4,096 siblings a second cost joins it. `NodeListPool.Return` drops any list whose capacity exceeds 4,096 ([NodeListPool.cs:48](../XPath/NodeListPool.cs)), so each pattern test then grows a fresh list from 16 entries, about 128 KB of doubling arrays with the larger ones on the large object heap. At 16,000 siblings that is **2 GB allocated and 499 generation-0 collections per transformation**, against 51 KB at 4,000. That is why the last step is steeper than sixteen times.

Two things I have not explained: why the wildcard form is twice the named one at 16,000 and slightly faster below it, and how much of the 8x at 1,000 items is the enumeration as opposed to the per-test context copy.

Suggested fix, in order of value:

- Decide once, when the pattern is built, whether any predicate of the step may be positional. When none may, skip the enumeration and evaluate the predicates with position and size of 1. This is the whole of the second and third rows, and the common case in real stylesheets.
- For a literal `[N]` or a predicate that reads `position()` but not `last()`, count the matching preceding siblings and stop at the candidate, instead of collecting all of them. That halves the work and removes the list.
- For what genuinely needs the size, remember the last anchor and its collected list between tests. Templates are applied to siblings in order, so consecutive candidates share an anchor, and one enumeration would serve the whole list. That turns the square into a line for the positional rows too.
- Raise or remove the 4,096 cap in `NodeListPool`, or keep one large list per thread. Any step that selects more than 4,096 nodes pays this, not only patterns.

The same routine serves `xsl:key match`, `xsl:number count` and `from`, and `group-starting-with` and `group-ending-with`, so a fix reaches those as well. I measured only template rules.

**Since fixed, for the common shapes.** The first and third suggestions were made the same day, and are written up in `ConformanceNotes.md` under *A predicate in a pattern, and whether it needs to know where the candidate stands*.

- `PatternStep.CountsPosition` is settled when the step is built, and a step whose predicates cannot read a position has nothing enumerated. A path predicate such as `[@type]` now counts as unable to, which the old classification missed.
- For a step whose first predicate alone may be positional, what the step selected from the last anchor is remembered in the transformation (`StepSelection`, held by `XsltRuntime`), and the next candidate under the same parent is found in it by binary search. Only the nodes an axis and a node test select are kept, never anything a predicate was evaluated to produce, so nothing a predicate can read can make it stale.

| Pattern | 1,000 items | 4,000 | 16,000 | Allocated at 16,000 |
|---|---:|---:|---:|---:|
| `match="item"`, the test in an `xsl:choose` | 0.28 ms | 1.10 ms | 4.51 ms | 320 KB |
| `match="item[@type='a']"` | 2.40 → 0.30 ms | 33.2 → 1.20 ms | 664 → 4.93 ms | 2 GB → 320 KB |
| `match="*[@type='a']"` | 2.15 → 0.29 ms | 29.7 → 1.22 ms | 1,367 → 4.84 ms | 2 GB → 320 KB |
| `match="item[1]"` | 2.31 → 0.27 ms | 33.5 → 1.06 ms | 645 → 4.24 ms | 2 GB → 448 KB |
| `match="item[position() mod 2 = 1]"` | 2.57 → 0.49 ms | 33.5 → 1.96 ms | 650 → 8.11 ms | 2 GB → 6.9 MB |

Every row is linear now. Two shapes are deliberately left as they were and are still the square, neither of them common and neither measured: a step where a later predicate counts among what an earlier one left, such as `foo[@a='c'][2]`, because that list can depend on `current()` and on local variables and so cannot safely be kept; and a step on `descendant::`, whose anchor climbs for every candidate. The cap in `NodeListPool` is also still there, and still costs any path step that selects more than 4,096 nodes.

Auditing what "cannot read a position" means turned up a fault that was there already: `position#0` and `function-lookup()` were invisible to the analysis, so `//x[position#0() = 1]` was rewritten to count across the whole document and answered `a` where `a d` is right. That is fixed with it. After each half, all six conformance configurations were identical line for line with the commit it was made on, and the products and identity transformations, run turn about, were level.

## The benchmark suite measures things other than what it says

All verified by reading, and the third by measurement. The third is already disclosed in the benchmarks' README; the others are not.

1. **The JSON benchmark never reads its data.** `ProductsJsonToHtml.xslt` has one `match="/"` template that emits a fixed page. "Transform 1000 products" is JSON parsing plus static output, and it scales with input size only because of the parse. It needs a stylesheet that walks the products. The benchmarks' README describes this stylesheet as using "XPath 3.0 JSON functions, array operations, distinct-values()"; the file on disk uses none of them, so either the description or the stylesheet was replaced at some point.
2. **The DocBook benchmark measures the network.** `DocBook.xslt` is a single `xsl:import` of `https://cdn.docbook.org/release/xsltng/current/xslt/docbook.xsl`, fetched inside the measured method on every iteration. The 2.0 second mean with a 108 ms standard deviation is mostly HTTPS. `current` in the URL also means the stylesheet under test changes over time. Vendor a pinned copy and load it through a file resolver. How much of the 2 seconds is CPU is unknown until then, and that matters, because the compile reviewer's findings below only pay off if it is a lot.
3. **The `TextWriter` and `Stream` variants measure their own harness, as the README already says.** The README notes under its results that the extra allocation in those rows "belongs to the benchmark itself". I had not read it when I first wrote this item and presented it as a discovery; it is a known and disclosed limitation. What the measurements add is the size of it and one correction. The stored results show 1,470, 2,187 and 3,262 KB for the three targets; with sinks that allocate nothing the library's own figures are 774 and 812 KB, and the string row's 1,471 KB is 774 KB plus the 697 KB result. The correction: the README says the engine's own work is the same in all three, and it is not quite. The `Stream` variant passes a `StringReader`, so it takes a parse path that cannot pre-size the tree, and any real writer costs about 0.5 ms more than a sink (finding 4). `OutputTargetBenchmarks`, added with this review, measures the targets without the harness's allocations.
4. **Every transformation benchmark selects `Backend = Compiled`**, which gives nothing on these stylesheets, and none measures the default.
5. **Coverage gaps**: no identity or copy workload, no keys, grouping, sorting or `xsl:number`, no temporary trees, no cold start, no non-ASCII output. `[SimpleJob(warmupCount: 3)]` overrides BenchmarkDotNet's adaptive warm-up with something shorter; given the 4.7 seconds this library takes to settle, I would drop the override.

## Found by reading only: not measured, ranked by my guess at impact

None of these is exercised by the benchmark stylesheet, which is exactly why they need their own benchmarks before anyone spends time on them. The reviewers marked each as verified by reading the code. Apart from the two compile items and the `NodeListPool` cap, as noted, I have not checked them myself. The one that has since been measured was as bad as it read, which is some evidence for taking the rest seriously and none for their size.

**Could be quadratic on real stylesheets**

- **Any pattern predicate enumerates all siblings per candidate.** Since measured, and confirmed: it is now finding 9.
- **`xsl:number level="any"` rescans from the document start on every execution** (`NumberInstruction.cs:634`): quadratic when every figure or footnote is numbered. `level="single"` walks from the first sibling each time.
- **`//x` has no element-name index.** Six full sweeps per transform is fine here at 17 µs each. `//x` inside a per-node template is quadratic. A lazy index from fingerprint to sorted ids would make `descendant::x` from any origin a binary-search slice.
- **Patterns such as `(a|b)[..]`, `$v/x` and `key(..)` are re-evaluated from the root per candidate** (`Pattern.cs:799-812`). Rare shapes, but they sit in the generic bucket and so are tested against every node.

**Per-call costs in features this workload does not use**

- **Numeric sort keys are re-parsed on every comparison** (`Instruction.cs:1030`): about 2n log n string-to-double parses instead of n. There are also about six allocations per item per key, and culture-aware compares have no precomputed sort keys.
- **`x[@id = $id]` falls off the fast path**, because a variable reference reports `MaySpanDocuments` (`Expr.cs:380`, `610-614`). It then allocates a `NodeSet` per candidate, and `XPathComparison.General` re-atomizes the right-hand side inside the left-hand loop (`XPathComparison.cs:285-287`). This is probably the most common predicate form in real stylesheets.
- **Every temporary tree costs 4 to 5 KB at minimum** (12 arrays of 64, 3 of 32, and its own `NameTable`). Each also gets its own fingerprint map and its own `TemplateIndex` when templates are applied to it, and all of it is retained until the transform ends (`XsltRuntime.cs:529-568`, `OutputTarget.cs:886`). Stylesheets written in the 1.0 result-tree-fragment style pay this per template call.
- **No early exit.** `exists()`, `empty()` and boolean tests of a path materialise the whole sequence, and `[1]` evaluates the literal for every candidate even though `LiteralPosition` exists.
- **`NodeListPool` discards lists above 4,096 entries**, so any step yielding more regrows from 16 each time, reaching the large object heap at about 21,000 nodes. Verified, and measured inside a pattern in finding 9, where it comes to 2 GB per transformation.
- **`key()` allocates five or more objects per lookup**, and building a key index runs every pattern against every node with no kind or name prefilter.
- **Namespace copying allocates an iterator per copied element**, plus a `Dictionary` wherever two or more ancestors declare namespaces (`XdmTree.cs:1128-1152`). Qualified names are concatenated per prefixed element written.
- **Text with any non-ASCII character takes a per-character escape path** (`OutputWriter.cs:1988-2015`). ASCII keeps the vectorised one. This matters for CJK, Cyrillic and accented content.
- **The regex cache is an unbounded static `Dictionary` behind a global lock**, with its key built by string concatenation on every `matches`, `replace` and `tokenize` call (`RegexTranslator.cs:102-127`). With dynamic patterns it grows without limit.

**Compile time**

- **`Version` re-walks the ancestor chain and re-parses a decimal on every read, and the XPath parser reads it several times per operand.** I verified both ends: `Version => VersionOf(m_scopeElement)` at [StylesheetCompiler.cs:436](../Compiler/StylesheetCompiler.cs), and `VersionOf` at line 9479 is an uncached walk. The reviewer's line numbers were about ten lines off; the substance is right. Reading it once in the parser's constructor is a small change.
- **`UriResolver` sends no `Accept-Encoding`, uses HTTP/1.1, fetches modules sequentially and caches nothing.** I verified the handler at [UriResolver.cs:349](../UriResolver.cs). Setting `AutomaticDecompression` is one line.
- **Every unprefixed function call builds a full in-scope-namespaces dictionary that only the `xs:` constructor branch uses**, and asks the host for the function twice with the same arguments.
- **`IsForwardsCompatible` walks ancestors on every child-navigation step**, before the cheap parent test that would rule out almost every element.
- **Linear scans for globals and function packages**, and an attribute-set cycle check with no visited set, which is exponential in the worst case.

## Design options I noticed but did not measure

- **Node storage is 57 bytes per node across 13 arrays.** `m_firstChild` is derivable, because attributes are outside the preorder sequence: a node's first child is `node + 1` whenever `m_subtreeEnd[node] > node`. `m_depth` is rarely read. The four attribute and namespace range arrays are wasted on text nodes, which are over half of an indented document. A slimmer layout would help memory bandwidth on large documents. This is a large change with an uncertain payoff.
- **A per-node fingerprint array** holding -1 for non-elements would turn the `//x` sweep in `CollectElementsByFingerprint` into a single comparison per node, or a vectorised search. But that sweep is already 17 µs for 24,000 nodes, so this is not where the time is today.
- **Parse allocation is dominated by one string per text node.** Storing text as slices of one pooled character buffer would remove most of it. It is a substantial redesign.
- **Half of parse time is `XmlReader` itself** (1.1 of 2.0 ms). The builder's own overhead is about 40 ns per node and has clearly been tuned already: a two-way name cache keyed on the reader's atomized references, whitespace de-duplication, storage pre-sized from the text.

## Noticed in passing, not performance

- The `test=` against `select=` disagreement in finding 3. Confirmed by running. Raised as a task.
- `XdmTree.DocumentOrderKeyOf` reads the `m_namespaceNodesOf` dictionary outside the lock that writers hold ([XdmTree.cs:1026](../Model/XdmTree.cs)). The class is documented as safe for concurrent readers, but a `Dictionary` is not safe to read while another thread inserts into it. It only matters for a tree shared between threads that use the namespace axis. From my own reading; not reproduced.
- The compile reviewer reports that the `DefaultCollation` cache is keyed by element id without the tree (`StylesheetCompiler.cs:463`), so with multiple modules it could return another module's value. I did not verify this.

## What I would do, in order

1. **Stop enumerating siblings for pattern predicates that cannot read a position** (finding 9; done the same day along with positional predicates, see the note at the end of that finding), and lift the cap in `NodeListPool`, which is still open. It is the only measured cost here that grows with the square of the input, it is 146x at 16,000 siblings, and the first part of the fix is a flag computed when the pattern is built.
2. **Fix the two comparison bugs** (finding 3). Correctness first; a separate session reports having done so.
3. **Fix the benchmarks** (items 1, 2 and 4 of that section), and add key, sort, grouping, `xsl:number` and temporary-tree workloads. The remaining findings from reading cannot be prioritised honestly without them. The identity, output-target, backend, framework-comparison, cold-start, JIT-sensitivity and pattern-predicate workloads were added with this review; the existing benchmark classes were left as they were, so their history stays comparable.
4. **Give `UnionExpr` an `EvaluateNodes` path and special-case `@*|node()`.** It is the largest measured gap against the framework, on the most common idiom.
5. **Buffer output inside `OutputWriter`.** Broad, mechanical, up to 9 or 10% for every real target.
6. **Decide what the `Compiled` backend is for**, and at minimum stop building methods that only call back, and correct the documentation.
7. Then the cheaper wins: `format-number` garbage, the single-step path fast path, `BinaryExpr` flags computed once, `Version` read once per parser, `AutomaticDecompression`.

The review itself changed no library code. Afterwards, at the maintainer's request, this document was filed and the measurement workloads were added to `tests/Benchmarks` as BenchmarkDotNet classes.

## Reproducing

**Where the figures above first came from.** A hand-rolled timing harness in a temporary directory, not BenchmarkDotNet. It reported the median of 15 batches after warming up until time and allocation per call had both stopped changing. Its first two runs used a fixed 2-second warm-up, and their first-in-process rows were unreliable; every section was repeated with the settling warm-up and the figures above are from the repeats. When I first drafted this document that was not yet true of the scaling, construction and parse-comparison figures, so I re-ran them; four numbers moved by 3 to 8%, and the comparison with the framework on small documents changed from "parity" to "6% behind when returning a string". The harness was not kept.

**What replaces it.** The same workloads are now BenchmarkDotNet classes in `tests/Benchmarks`, and their full results are in that project's README. BenchmarkDotNet runs each case in its own process, under the runtime's default settings, with adaptive warm-up, and reports the spread, so where the two differed this document now carries its figures. They differed in size in five places:

| | Harness | BenchmarkDotNet |
|---|---|---|
| 1,000 products to a string, this engine against the framework | 6.05 against 6.06 ms | 5.92 against 6.80 ms |
| Identity template against the framework | 8.5 against 6.1 ms, 1.4x | 7.97 against 6.42 ms, 1.24x |
| Cost per product at 10,000 against 1,000 | 3 to 8% more | 14% more |
| `format-number` per call | about 270 ns | about 190 ns |
| Construction, compiled against interpreted | 1.41x | 1.33x |

The first is the one that changed a conclusion, and its cause is in the verdict: the harness ran with concurrent garbage collection off. I have not explained the others. No finding changed direction between the two ways of measuring. One changed direction for another reason, which was my reading and not the measurement: finding 3.

| Section of this review | Benchmark class |
|---|---|
| Where the time goes | `TransformPhaseBenchmarks`, `ScalingBenchmarks` |
| What a row costs; findings 6 and 7 | `RowCostBenchmarks` |
| Finding 1, the identity template | `IdentityTransformBenchmarks` |
| Findings 2, 3 and 8, the backends and predicates | `BackendBenchmarks` |
| Finding 4, output targets | `OutputTargetBenchmarks` |
| Finding 5, PGO and cold start | `JitSensitivityBenchmarks`, `ColdStartBenchmarks` |
| Finding 9, predicates in match patterns | `PatternPredicateBenchmarks` |
| The comparison with the framework | `FrameworkComparisonBenchmarks`, `FrameworkLoadBenchmarks` |

Two things the harness measured have no benchmark, because they are counts and not timings: the 100,241 `Write` calls per transform (the `Calls` counter on the benchmarks' `CountingWriter` reports it), and the `test=` against `select=` probe, which belongs in the unit tests once the bug is fixed.

BenchmarkDotNet finds the benchmark project by name under the solution's folder, and refuses to run if it finds two. A git worktree inside the repository, such as one under `.claude/worktrees`, is a second copy. The benchmarks for this review were run from a copy of the working tree outside the repository for that reason.
