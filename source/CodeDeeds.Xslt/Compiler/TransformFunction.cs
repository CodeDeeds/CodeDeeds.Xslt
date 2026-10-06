using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>How <c>fn:transform</c> hands back each result document the transformation produced.</summary>
    internal enum TransformDelivery
    {
        /// <summary>As a document node, which is the result tree the transformation built.</summary>
        Document,

        /// <summary>As the string the serializer wrote.</summary>
        Serialized,

        /// <summary>As the sequence the transformation returned, whatever is in it.</summary>
        Raw,
    }

    /// <summary>Where one result document of a transformation run by <c>fn:transform</c> is being built.</summary>
    internal readonly struct TransformDestination
    {
        /// <summary>Initializes a destination.</summary>
        /// <param name="uri">The absolute URI the document will be keyed by.</param>
        /// <param name="target">Where the instruction writes.</param>
        /// <param name="text">The string being written to, where the delivery format is serialized.</param>
        public TransformDestination(string uri, OutputTarget target, StringWriter? text)
        {
            Uri = uri;
            Target = target;
            Text = text;
        }

        /// <summary>The absolute URI the document will be keyed by.</summary>
        public string Uri { get; }

        /// <summary>Where the instruction writes.</summary>
        public OutputTarget Target { get; }

        /// <summary>The string being written to, where the delivery format is serialized.</summary>
        public StringWriter? Text { get; }
    }

    /// <summary>
    /// Collects the result documents of a transformation that <c>fn:transform</c> is running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <c>xsl:result-document</c> ordinarily creates something outside the transformation, and this is the
    /// one case where it does not: the caller asked for the documents as values, so each is built in memory
    /// and handed back in the map rather than going to a resolver. That is why the runtime consults this
    /// before <see cref="XsltRuntime.ResolveResultDocument"/> — with a collector in place a stylesheet needs
    /// no result resolver at all, because nothing is being written anywhere.
    /// </para>
    /// <para>
    /// The delivery format decides what a document is: a document node, a serialized string, or the raw
    /// sequence. It is the same choice for the principal result and for every secondary one, since the map
    /// they arrive in makes no distinction between them.
    /// </para>
    /// </remarks>
    internal sealed class TransformResults
    {
        private readonly Dictionary<string, XPathValue> m_documents = new(StringComparer.Ordinal);
        private readonly TransformDelivery m_delivery;
        private readonly NameTable? m_names;

        /// <summary>Initializes a collection.</summary>
        /// <param name="delivery">What each document is handed back as.</param>
        /// <param name="names">
        /// The name table the results are built in, which is the calling transformation's own, so that what
        /// it reads back costs it no mapping of its own; null where there is no calling transformation.
        /// </param>
        public TransformResults(TransformDelivery delivery, NameTable? names)
        {
            m_delivery = delivery;
            m_names = names;
        }

        /// <summary>What the transformation has produced so far, keyed by absolute URI.</summary>
        public IReadOnlyDictionary<string, XPathValue> Documents => m_documents;

        /// <summary>
        /// Opens somewhere for a result document to be built, claiming its URI.
        /// </summary>
        /// <param name="uri">The absolute URI the document goes under.</param>
        /// <param name="settings">How it would be serialized, which only the serialized form uses.</param>
        /// <exception cref="XsltException">The URI has already been written.</exception>
        public TransformDestination Open(string uri, OutputSettings settings)
        {
            if (m_documents.ContainsKey(uri))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE1490,
                    $"The result document '{uri}' was written more than once by one transformation, and a "
                    + "URI may carry one final result tree only.");
            }

            // Claimed as it is opened, so that a stylesheet writing the same URI twice is caught by the
            // second write rather than by the second write silently replacing the first in the map.
            m_documents.Add(uri, XPathValue.FromSequence(XdmSequence.Empty));

            return NewDestination(uri, settings);
        }

        /// <summary>Records what was built, once the instruction's body has run.</summary>
        /// <param name="destination">The destination <see cref="Open"/> returned.</param>
        public void Close(TransformDestination destination)
        {
            m_documents[destination.Uri] = Finish(destination);
        }

        /// <summary>
        /// Builds somewhere to write one result document, in the shape the delivery format asks for.
        /// </summary>
        /// <remarks>
        /// Each target says it stands for final output, which is what lets an <c>xsl:result-document</c> be
        /// written into it: the document being built is a final result of the transformation and not a
        /// temporary tree, however it is going to be delivered.
        /// </remarks>
        /// <param name="uri">The document's URI, which is also the base URI of what is built there.</param>
        /// <param name="settings">How it would be serialized.</param>
        public TransformDestination NewDestination(string uri, OutputSettings settings)
        {
            switch (m_delivery)
            {
                case TransformDelivery.Raw:
                    return new TransformDestination(
                        uri,
                        new SequenceCaptureTarget(baseUri: uri, names: m_names) { StandsForFinalOutput = true },
                        null);

                case TransformDelivery.Document:
                    return new TransformDestination(
                        uri,
                        new ResultTreeBuilder(m_names) { BaseUri = uri, StandsForFinalOutput = true },
                        null);

                default:
                {
                    StringWriter text = new StringWriter();

                    // The json and adaptive methods write values rather than a tree, so what the
                    // transformation produces is gathered as a sequence and serialized once it is all
                    // there — the same division the engine makes when writing to a stream.
                    if (settings.Method is OutputMethod.Json or OutputMethod.Adaptive)
                    {
                        return new TransformDestination(
                            uri,
                            new SequenceCaptureTarget(names: m_names) { StandsForFinalOutput = true },
                            text);
                    }

                    return new TransformDestination(
                        uri,
                        new OutputWriter(text, settings) { DeclaresWhenEmpty = true },
                        text);
                }
            }
        }

        /// <summary>Turns what was built at a destination into the value the map holds.</summary>
        /// <param name="destination">The destination, whose body has run.</param>
        public XPathValue Finish(TransformDestination destination)
        {
            switch (destination.Target)
            {
                case ResultTreeBuilder tree:
                    return XPathValue.FromNodeSet(NodeSet.Singleton(tree.Finish(), XdmTree.RootNode));

                case SequenceCaptureTarget capture when destination.Text is null:
                    return capture.Finish();

                case SequenceCaptureTarget gathered:
                    // A serialized json or adaptive result: the sequence, written out by the serializer.
                    gathered.Finish();
                    return XPathValue.FromString(destination.Text!.ToString());

                case OutputWriter writer:
                    writer.Flush();
                    return XPathValue.FromString(destination.Text!.ToString());

                default:
                    return XPathValue.FromSequence(XdmSequence.Empty);
            }
        }
    }

    /// <summary>
    /// The stylesheets one call of <c>fn:transform</c> has compiled, kept so that being asked for the same
    /// one again does not compile it again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stylesheet that runs others with <c>fn:transform()</c> runs the same ones every time it is run
    /// itself. The DocBook stylesheets put a document through four of their own before formatting it,
    /// thirty-one modules between them, and reading and compiling those was 70 milliseconds and 40
    /// megabytes of every transformation, whatever the document: more than formatting a short one costs.
    /// </para>
    /// <para>
    /// One of these belongs to one call, which is to say one place in a compiled stylesheet where
    /// <c>fn:transform()</c> is written, and lives as long as that stylesheet does. So what it keeps was
    /// compiled with the resolvers and the settings that stylesheet was compiled with, and is kept no
    /// longer than the stylesheet that names it is itself kept compiled.
    /// </para>
    /// <para>
    /// A compiled stylesheet is found again by two things. Where it came from and how it was set going,
    /// which <c>TransformRequest.SourceKey</c> writes as one string. And what the caller supplied for
    /// each of its static parameters, a static parameter being what a stylesheet is compiled differently
    /// for: the stylesheet says which it has once it is compiled
    /// (<see cref="CompiledStylesheet.StaticParameters"/>), and a caller who supplies the same for each
    /// of them, or nothing, is one compiling again would hand the same stylesheet to. A value that is
    /// not atomic cannot be told from another by writing it, so a stylesheet supplied one is not kept.
    /// </para>
    /// <para>
    /// It holds a few and then no more, rather than letting go of the oldest: a call that names more
    /// stylesheets than that, by text that differs every time for one, is not one that keeping them
    /// helps. A compiled stylesheet is shared between transformations running at once, so this is read
    /// and written from several threads; two that compile the same stylesheet at the same moment both
    /// run what they compiled, and one of the two is kept.
    /// </para>
    /// </remarks>
    internal sealed class TransformCache
    {
        private const int MostSources = 16;
        private const int MostOfOneSource = 4;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Kept[]> m_bySource =
            new System.Collections.Concurrent.ConcurrentDictionary<string, Kept[]>(StringComparer.Ordinal);

        /// <summary>One compiled stylesheet, with what was supplied for each of its static parameters.</summary>
        private sealed record Kept(CompiledStylesheet Compiled, string? Uri, string?[] Supplied);

        /// <summary>Finds the stylesheet compiled from a source for a caller who supplied these parameters.</summary>
        /// <param name="source">Where the stylesheet came from and how it was set going.</param>
        /// <param name="supplied">The parameters this caller supplies, static and otherwise.</param>
        /// <param name="compiled">The stylesheet, where one is kept.</param>
        /// <param name="uri">The URI it was read from, which its options are built with.</param>
        public bool TryFind(
            string source,
            IReadOnlyDictionary<string, object?>? supplied,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CompiledStylesheet? compiled,
            out string? uri)
        {
            if (m_bySource.TryGetValue(source, out Kept[]? kept))
            {
                foreach (Kept one in kept)
                {
                    if (SuppliedTheSame(one, supplied))
                    {
                        compiled = one.Compiled;
                        uri = one.Uri;
                        return true;
                    }
                }
            }

            compiled = null;
            uri = null;
            return false;
        }

        /// <summary>Keeps a stylesheet just compiled, where there is room and it can be found again.</summary>
        /// <param name="source">Where the stylesheet came from and how it was set going.</param>
        /// <param name="compiled">The stylesheet.</param>
        /// <param name="uri">The URI it was read from.</param>
        /// <param name="supplied">The parameters it was compiled with.</param>
        public void Keep(
            string source, CompiledStylesheet compiled, string? uri, IReadOnlyDictionary<string, object?>? supplied)
        {
            string?[] written = new string?[compiled.StaticParameters.Length];

            for (int i = 0; i < written.Length; i++)
            {
                if (!TryWrite(supplied, compiled.StaticParameters[i], out written[i]))
                {
                    return;
                }
            }

            Kept kept = new Kept(compiled, uri, written);

            if (m_bySource.TryGetValue(source, out Kept[]? already))
            {
                if (already.Length < MostOfOneSource)
                {
                    Kept[] grown = new Kept[already.Length + 1];
                    already.CopyTo(grown, 0);
                    grown[^1] = kept;
                    m_bySource[source] = grown;
                }
            }
            else if (m_bySource.Count < MostSources)
            {
                m_bySource[source] = new[] { kept };
            }
        }

        private static bool SuppliedTheSame(Kept kept, IReadOnlyDictionary<string, object?>? supplied)
        {
            ExpandedName[] parameters = kept.Compiled.StaticParameters;

            for (int i = 0; i < parameters.Length; i++)
            {
                if (!TryWrite(supplied, parameters[i], out string? now)
                    || !string.Equals(now, kept.Supplied[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Writes what was supplied for a static parameter so that two values are written alike only
        /// where they are the same items of the same types; null where nothing was supplied.
        /// </summary>
        /// <returns>False where the value cannot be written so, something in it not being atomic.</returns>
        private static bool TryWrite(
            IReadOnlyDictionary<string, object?>? supplied, ExpandedName name, out string? written)
        {
            written = null;

            if (supplied is null || !StylesheetCompiler.TryFindSuppliedParameter(supplied, name, out object? given))
            {
                return true;
            }

            if (given is not XPathValue value)
            {
                return false;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();

            foreach (XPathValue item in XdmSequence.Items(value))
            {
                if (item.Kind is not (XPathValueKind.String or XPathValueKind.Number or XPathValueKind.Boolean))
                {
                    return false;
                }

                string text = XdmSequence.StringValueOf(item);
                builder.Append((int)item.TypeCode).Append(':').Append(text.Length).Append(':').Append(text).Append(';');
            }

            written = builder.ToString();
            return true;
        }
    }

    /// <summary>
    /// <c>fn:transform</c>, which runs a second transformation and hands back what it produced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The function is XSLT's before it is XPath's, which is why it is built here rather than in the function
    /// library: running a transformation means reaching the resolvers the caller configured, and the library
    /// knows of no caller. Everything it needs is settled where the call is written — the options this
    /// stylesheet was compiled with, and the base URI a <c>stylesheet-location</c> resolves against — so the
    /// call also works where there is no transformation to ask, which is what a <c>static</c> variable
    /// calling it needs.
    /// </para>
    /// <para>
    /// The transformation it runs is a transformation of its own: its own compilation, its own runtime, its
    /// own result documents. Nothing of the caller's reaches it but the resolvers and the options the map
    /// named, and nothing of it reaches the caller but the map returned.
    /// </para>
    /// <para>
    /// Its own compilation, but not a new one for every call: what a call has compiled it keeps, and runs
    /// again when it is asked for the same stylesheet by a caller who supplies the same. See
    /// <see cref="TransformCache"/>.
    /// </para>
    /// </remarks>
    internal sealed class TransformExpr : Expr
    {
        private readonly Expr m_options;
        private readonly XsltOptions m_host;
        private readonly string? m_baseUri;

        /// <summary>The stylesheets this call has compiled, kept for when it is asked for one again.</summary>
        private readonly TransformCache m_compiled = new TransformCache();

        /// <summary>Initializes a call.</summary>
        /// <param name="options">The map of options.</param>
        /// <param name="host">The options the calling stylesheet was compiled with, for its resolvers.</param>
        /// <param name="baseUri">The base URI where the call is written, which a location resolves against.</param>
        public TransformExpr(Expr options, XsltOptions host, string? baseUri)
        {
            m_options = options;
            m_host = host;
            m_baseUri = baseUri;
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_options };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            IReadOnlyList<XPathValue> given = XdmSequence.Items(m_options.Evaluate(ref context));

            if (given.Count != 1 || given[0].Kind != XPathValueKind.Map)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0004,
                    "fn:transform() takes one map of options, and "
                    + (given.Count == 1 ? "the value given is not a map." : $"{given.Count} items were given."));
            }

            return new TransformRequest(given[0].AsMap(), m_host, m_baseUri, m_compiled, context.Runtime?.TemporaryNames).Run();
        }
    }

    /// <summary>One call of <c>fn:transform</c>: what its option map asked for, and the running of it.</summary>
    internal sealed class TransformRequest
    {
        /// <summary>The key the principal result goes under, the secondary ones being keyed by their URI.</summary>
        private const string PrincipalKey = "output";

        /// <summary>Every option name the specification defines, so that a misspelt one is told apart.</summary>
        private static readonly HashSet<string> s_known = new(StringComparer.Ordinal)
        {
            "xslt-version", "stylesheet-location", "stylesheet-node", "stylesheet-text", "stylesheet-base-uri",
            "stylesheet-params", "static-params", "package-name", "package-version", "package-location",
            "package-text", "package-node", "source-node", "initial-mode", "initial-template",
            "initial-match-selection", "initial-function", "function-params", "template-params",
            "tunnel-params", "base-output-uri", "delivery-format", "serialization-params", "enable-assertions",
            "enable-messages", "enable-trace", "cache", "vendor-options", "requested-properties",
            "post-process",
        };

        private readonly Dictionary<string, XPathValue> m_options = new(StringComparer.Ordinal);
        private readonly XsltOptions m_host;
        private readonly string? m_baseUri;
        private readonly TransformCache m_compiled;
        private readonly NameTable? m_names;

        /// <summary>Reads the option map, refusing what is not an option.</summary>
        /// <param name="options">The map the call was given.</param>
        /// <param name="host">The options the calling stylesheet was compiled with.</param>
        /// <param name="baseUri">The base URI where the call is written.</param>
        /// <param name="compiled">What the call has compiled before, and where to keep what it compiles now.</param>
        /// <param name="names">The calling transformation's name table, which the results are built in.</param>
        public TransformRequest(XdmMap options, XsltOptions host, string? baseUri, TransformCache compiled, NameTable? names = null)
        {
            m_names = names;
            m_host = host;
            m_baseUri = baseUri;
            m_compiled = compiled;

            foreach (KeyValuePair<XPathValue, XPathValue> entry in options.Entries)
            {
                if (!IsAtomic(entry.Key))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOXT0004,
                        "The options of fn:transform() are named by strings, and this map has a key that is "
                        + "not one.");
                }

                string name = entry.Key.ToStringValue();

                if (!s_known.Contains(name))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOXT0004,
                        $"'{name}' is not an option fn:transform() defines.");
                }

                m_options[name] = entry.Value;
            }
        }

        /// <summary>Runs the transformation and returns the map of what it produced.</summary>
        public XPathValue Run()
        {
            RefuseUnimplemented();
            CheckVersion();

            TransformDelivery delivery = Delivery();
            (CompiledStylesheet compiled, XsltOptions inner) = Load();

            XdmTree? source = SourceTree(out XPathValue? selection);
            TransformResults results = new TransformResults(delivery, m_names);
            OutputSettings settings = Serialization(compiled);

            string principalUri = inner.BaseOutputUri ?? inner.BaseUri ?? PrincipalKey;
            TransformDestination principal = results.NewDestination(principalUri, settings);

            XsltRuntime runtime = new XsltRuntime(
                compiled,
                source ?? XdmTreeBuilder.Empty(),
                principal.Target,
                inner,
                hasSourceDocument: source is not null)
            {
                MessageWriter = inner.MessageWriter,
                WarningWriter = inner.WarningWriter,
                Results = results,
                InitialSelection = selection,
                InitialParameters = TemplateParameters(),
            };

            runtime.Run();

            List<KeyValuePair<XPathValue, XPathValue>> entries =
                new List<KeyValuePair<XPathValue, XPathValue>>
                {
                    new KeyValuePair<XPathValue, XPathValue>(
                        XPathValue.FromString(PrincipalKey), results.Finish(principal)),
                };

            foreach (KeyValuePair<string, XPathValue> document in results.Documents)
            {
                entries.Add(new KeyValuePair<XPathValue, XPathValue>(
                    XPathValue.FromString(document.Key), document.Value));
            }

            return XPathValue.FromMap(XdmMap.Build(entries, "use-last"));
        }

        /// <summary>
        /// Refuses an option this engine cannot honour, rather than running a transformation that quietly
        /// ignores what the caller asked for.
        /// </summary>
        /// <remarks>
        /// <c>FOXT0001</c> is the code for a transformation this processor cannot carry out, which is what an
        /// option it does not implement amounts to. The alternative — accepting the option and doing nothing
        /// about it — would answer a question that was never asked.
        /// </remarks>
        private void RefuseUnimplemented()
        {
            foreach (string option in new[] { "initial-function", "function-params", "post-process", "enable-trace" })
            {
                if (m_options.ContainsKey(option))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOXT0001,
                        $"fn:transform() was given the option '{option}', which this processor does not "
                        + "implement.");
                }
            }

            // A property the caller requires of the processor. Nothing here can be promised beyond what the
            // defaults are, so a request for any of them is refused rather than passed over.
            if (m_options.TryGetValue("requested-properties", out XPathValue requested)
                && RequireMap("requested-properties", requested).Count != 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0001,
                    "fn:transform() was asked for processor properties, and this processor makes no "
                    + "promises beyond its defaults.");
            }
        }

        /// <summary>Checks the version of XSLT the caller asked to be run against.</summary>
        private void CheckVersion()
        {
            if (!m_options.TryGetValue("xslt-version", out XPathValue asked))
            {
                return;
            }

            double version = RequireAtomic("xslt-version", asked).ToNumber();

            if (double.IsNaN(version) || version > XsltVersion.V30.Number)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0001,
                    $"fn:transform() was asked for XSLT {asked.ToStringValue()}, and this processor "
                    + $"implements {XsltVersion.V30.Number}.");
            }
        }

        /// <summary>What each result document is handed back as.</summary>
        private TransformDelivery Delivery()
        {
            if (!m_options.TryGetValue("delivery-format", out XPathValue asked))
            {
                return TransformDelivery.Document;
            }

            return RequireAtomic("delivery-format", asked).ToStringValue() switch
            {
                "document" => TransformDelivery.Document,
                "serialized" => TransformDelivery.Serialized,
                "raw" => TransformDelivery.Raw,
                string other => throw XsltErrors.Error(
                    XsltErrorCode.FOXT0004,
                    $"'{other}' is not a delivery format. One is document, serialized or raw."),
            };
        }

        /// <summary>
        /// Locates and compiles the stylesheet the options name.
        /// </summary>
        /// <remarks>
        /// Exactly one of the six ways of naming it may be written. A location goes to the stylesheet
        /// resolver, resolved against the base URI of the call, as an <c>xsl:import</c> would be; a package
        /// name goes to the package resolver, which is asked what versions it holds so that the highest the
        /// range takes is the one loaded.
        /// </remarks>
        private (CompiledStylesheet Compiled, XsltOptions Options) Load()
        {
            string[] named = new[]
            {
                "stylesheet-location", "stylesheet-node", "stylesheet-text",
                "package-location", "package-node", "package-text", "package-name",
            };

            List<string> written = new List<string>();

            foreach (string option in named)
            {
                if (m_options.ContainsKey(option))
                {
                    written.Add(option);
                }
            }

            if (written.Count != 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0004,
                    written.Count == 0
                        ? "fn:transform() was given no stylesheet to run: name one with "
                            + "stylesheet-location, stylesheet-node, stylesheet-text or package-name."
                        : $"fn:transform() was given {written.Count} ways to find its stylesheet "
                            + $"({string.Join(", ", written)}), and exactly one is allowed.");
            }

            // Compiled before by this call, from the same place and for a caller who supplied the same:
            // then it is the stylesheet that compiling again would make, and is run as it stands. A
            // stylesheet that puts a document through others of its own, as the DocBook stylesheets do
            // through four, otherwise compiles every one of them again at every transformation.
            IReadOnlyDictionary<string, object?>? supplied = Parameters();
            string? source = SourceKey(written[0]);

            if (source is not null
                && m_compiled.TryFind(source, supplied, out CompiledStylesheet? kept, out string? keptUri))
            {
                return (kept, InnerOptions(keptUri, supplied));
            }

            (TextReader reader, string? uri) = Locate(written[0]);
            XsltOptions inner = InnerOptions(uri, supplied);
            XdmTree tree;

            try
            {
                tree = XdmTreeBuilder.FromXml(
                    reader, locations: true, entityResolver: m_host.EntityResolver, baseUri: uri);
            }
            finally
            {
                reader.Dispose();
            }

            CompiledStylesheet compiled = StylesheetCompiler.Compile(tree, inner);

            if (source is not null)
            {
                m_compiled.Keep(source, compiled, uri, supplied);
            }

            return (compiled, inner);
        }

        /// <summary>
        /// What a compiled stylesheet is kept under: everything the call says that the compiling of it
        /// reads, the parameters apart; or null where it is not to be kept.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Where the stylesheet is, by the reference as written and the base it resolves against, so that
        /// the resolver is not asked again: a stylesheet named by location is read once by the call that
        /// names it, as a module an <c>xsl:import</c> names is read once by the stylesheet that imports
        /// it. <c>'cache': false()</c> is the caller saying it may have changed, and reads it every time.
        /// </para>
        /// <para>
        /// The options the transformation is set going with are in the key as well, though most of them
        /// decide nothing about the compiled form: they are few and seldom differ from one call to the
        /// next, and a key that says too much costs a compilation where one that says too little would
        /// cost a wrong answer. The parameters are not in it, being many and different every time: which
        /// of them the compiled form depends on is asked of the stylesheet once it is compiled.
        /// </para>
        /// <para>
        /// A stylesheet given as a node is not kept. It would have to be written out to be told from
        /// another, which is most of what reading it costs.
        /// </para>
        /// </remarks>
        private string? SourceKey(string option)
        {
            if (m_options.TryGetValue("cache", out XPathValue cache) && !RequireAtomic("cache", cache).ToBoolean())
            {
                return null;
            }

            string named;

            switch (option)
            {
                case "stylesheet-node":
                case "package-node":
                    return null;

                case "package-name":
                    named = RequireAtomic(option, m_options[option]).ToStringValue()
                        + Separator
                        + (m_options.TryGetValue("package-version", out XPathValue version)
                            ? RequireAtomic("package-version", version).ToStringValue()
                            : "*");
                    break;

                default:
                    named = RequireAtomic(option, m_options[option]).ToStringValue();

                    // A stylesheet written out in full is its own name, and one long enough is not worth
                    // holding twice for the chance of being handed it again.
                    if (named.Length > LongestTextKept)
                    {
                        return null;
                    }

                    break;
            }

            return string.Join(
                Separator,
                option,
                named,
                m_baseUri,
                BaseUriOption(),
                EntryPoint("initial-template"),
                EntryPoint("initial-mode"),
                m_options.TryGetValue("base-output-uri", out XPathValue output)
                    ? RequireAtomic("base-output-uri", output).ToStringValue()
                    : null,
                m_options.TryGetValue("enable-messages", out XPathValue messages)
                    && !RequireAtomic("enable-messages", messages).ToBoolean() ? "quiet" : null);
        }

        /// <summary>What stands between the parts of a key, and in none of them: no URI or name has it.</summary>
        private const string Separator = "\u0001";

        /// <summary>The longest stylesheet given as text that is kept once compiled.</summary>
        private const int LongestTextKept = 64 * 1024;

        /// <summary>Finds the stylesheet text behind whichever option named it.</summary>
        private (TextReader Reader, string? Uri) Locate(string option)
        {
            switch (option)
            {
                case "stylesheet-text":
                case "package-text":
                    return (
                        new StringReader(RequireAtomic(option, m_options[option]).ToStringValue()),
                        BaseUriOption() ?? m_baseUri);

                case "stylesheet-node":
                case "package-node":
                {
                    XPathValue node = RequireNode(option, m_options[option]);

                    // Written out and read back: a stylesheet is compiled from a tree of its own, and the
                    // node given belongs to the calling transformation's tree, where it may be one element
                    // among many. What is lost by the round trip is what the serializer loses, which for a
                    // stylesheet is nothing that decides what it means.
                    return (
                        new StringReader(Serializer.Text(
                            XdmSequence.Items(node), new OutputSettings { OmitXmlDeclaration = true })),
                        BaseUriOption() ?? BaseUriOfNode(node) ?? m_baseUri);
                }

                case "package-name":
                    return LocatePackage();

                default:
                {
                    string href = RequireAtomic(option, m_options[option]).ToStringValue();

                    if (m_host.StylesheetResolver is null)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.FOXT0002,
                            $"fn:transform() was told to run '{href}', and no stylesheet resolver was "
                            + "configured for it to be found through.");
                    }

                    ResolvedResource resolved = Retrieve(() => m_host.StylesheetResolver.Resolve(href, m_baseUri), href);

                    return (resolved.Reader, resolved.Uri);
                }
            }
        }

        /// <summary>Finds the package the options name, at the highest version the range takes.</summary>
        private (TextReader Reader, string? Uri) LocatePackage()
        {
            string name = RequireAtomic("package-name", m_options["package-name"]).ToStringValue();
            string written = m_options.TryGetValue("package-version", out XPathValue asked)
                ? RequireAtomic("package-version", asked).ToStringValue()
                : "*";

            PackageVersionRange range = PackageVersionRange.TryParse(written)
                ?? throw XsltErrors.Error(
                    XsltErrorCode.FOXT0004,
                    $"'{written}' is not a package version range, which is what package-version takes.");

            if (m_host.PackageResolver is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0002,
                    $"fn:transform() was told to run the package '{name}', and no package resolver was "
                    + "configured for it to be found through.");
            }

            string? chosen = range.Best(m_host.PackageResolver as IXsltPackageResolver, name, out bool offered);

            if (offered && chosen is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0002,
                    $"No version of the package '{name}' that fn:transform() takes "
                    + $"(package-version=\"{written}\") could be found.");
            }

            ResolvedResource resolved = Retrieve(() => m_host.PackageResolver.Resolve(name, chosen), name);

            return (resolved.Reader, resolved.Uri);
        }

        /// <summary>
        /// Asks a resolver for something, and reports not finding it as the retrieval failure it is.
        /// </summary>
        /// <remarks>
        /// <c>FOXT0002</c> covers the resource not being there and the reading of it failing alike; an
        /// <see cref="XsltException"/> a resolver raises for a reference it refuses is a refusal to supply
        /// the resource, which is the same answer as not having it.
        /// </remarks>
        private static ResolvedResource Retrieve(Func<ResolvedResource?> resolve, string what)
        {
            ResolvedResource? resolved;

            try
            {
                resolved = resolve();
            }
            catch (Exception error) when (error is XsltException or IOException or UnauthorizedAccessException)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0002,
                    $"fn:transform() could not retrieve '{what}': {error.Message}");
            }

            return resolved
                ?? throw XsltErrors.Error(
                    XsltErrorCode.FOXT0002,
                    $"fn:transform() could not retrieve '{what}'.");
        }

        /// <summary>The options the transformation runs with, built from the caller's and the map's.</summary>
        /// <remarks>
        /// The resolvers are the caller's, because the transformation being run is being run on the caller's
        /// behalf and may reach no further than the caller could. Everything else the map decides.
        /// </remarks>
        private XsltOptions InnerOptions(string? uri, IReadOnlyDictionary<string, object?>? supplied)
        {
            bool messages = !m_options.TryGetValue("enable-messages", out XPathValue enabled)
                || RequireAtomic("enable-messages", enabled).ToBoolean();

            return new XsltOptions
            {
                StylesheetResolver = m_host.StylesheetResolver,
                DocumentResolver = m_host.DocumentResolver,
                CollectionResolver = m_host.CollectionResolver,
                CollationResolver = m_host.CollationResolver,
                PackageResolver = m_host.PackageResolver,
                EntityResolver = m_host.EntityResolver,
                Version = m_host.Version,
                DynamicEvaluation = m_host.DynamicEvaluation,
                EnvironmentVariablesEnabled = m_host.EnvironmentVariablesEnabled,
                SchemaAware = m_host.SchemaAware,
                SchemaResolver = m_host.SchemaResolver,
                Schemas = m_host.Schemas,
                InputValidation = m_host.InputValidation,
                MessageWriter = messages ? m_host.MessageWriter : TextWriter.Null,
                WarningWriter = messages ? m_host.WarningWriter : TextWriter.Null,
                BaseUri = BaseUriOption() ?? uri,
                Parameters = supplied,
                InitialTemplate = EntryPoint("initial-template"),
                InitialMode = EntryPoint("initial-mode"),

                // Where a secondary result document's href is resolved to, and so what it is keyed by in the
                // map. Absent, the stylesheet's own URI: the specification leaves the value to the processor,
                // and a relative href has to resolve against something for two spellings of one place to be
                // one document.
                BaseOutputUri = m_options.TryGetValue("base-output-uri", out XPathValue output)
                    ? RequireAtomic("base-output-uri", output).ToStringValue()
                    : uri,
            };
        }

        /// <summary>The base URI the caller declared for the stylesheet, where one was.</summary>
        private string? BaseUriOption()
        {
            return m_options.TryGetValue("stylesheet-base-uri", out XPathValue declared)
                ? RequireAtomic("stylesheet-base-uri", declared).ToStringValue()
                : null;
        }

        /// <summary>The base URI of a node, which a stylesheet given as one is compiled against.</summary>
        private static string? BaseUriOfNode(XPathValue node)
        {
            return Xpath2FunctionExpr.BaseUriOf(node.NodeTree, node.NodeId, node.NodeTree.BaseUri);
        }

        /// <summary>
        /// The parameters the transformation starts with: the run-time ones and the static ones together.
        /// </summary>
        /// <remarks>
        /// One channel serves both, because the engine takes a supplied parameter at whichever point the
        /// declaration reads it — a <c>static</c> declaration while it compiles, an ordinary one when it
        /// runs. The specification keeps the two maps apart so that a processor may compile before it is told
        /// the run-time values, which is a distinction this engine has no need of.
        /// </remarks>
        private IReadOnlyDictionary<string, object?>? Parameters()
        {
            Dictionary<string, object?> supplied = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (string option in new[] { "static-params", "stylesheet-params" })
            {
                if (!m_options.TryGetValue(option, out XPathValue given))
                {
                    continue;
                }

                foreach (KeyValuePair<XPathValue, XPathValue> entry in RequireMap(option, given).Entries)
                {
                    if (entry.Key.TypeCode != XdmTypeCode.QName)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.FOXT0004,
                            $"The {option} of fn:transform() are named by QNames, and this map has a key "
                            + "that is not one.");
                    }

                    XdmQName name = entry.Key.AsQName();
                    supplied["{" + name.NamespaceUri + "}" + name.LocalName] = entry.Value;
                }
            }

            return supplied.Count == 0 ? null : supplied;
        }

        /// <summary>The parameters the initial template is called with, ordinary and tunnelled.</summary>
        private ParameterValue[]? TemplateParameters()
        {
            List<ParameterValue> parameters = new List<ParameterValue>();

            foreach ((string option, bool tunnel) in new[] { ("template-params", false), ("tunnel-params", true) })
            {
                if (!m_options.TryGetValue(option, out XPathValue given))
                {
                    continue;
                }

                foreach (KeyValuePair<XPathValue, XPathValue> entry in RequireMap(option, given).Entries)
                {
                    if (entry.Key.TypeCode != XdmTypeCode.QName)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.FOXT0004,
                            $"The {option} of fn:transform() are named by QNames, and this map has a key "
                            + "that is not one.");
                    }

                    XdmQName name = entry.Key.AsQName();

                    parameters.Add(new ParameterValue(
                        new ExpandedName(name.NamespaceUri, name.LocalName), entry.Value, tunnel));
                }
            }

            return parameters.Count == 0 ? null : parameters.ToArray();
        }

        /// <summary>A template or mode the transformation is to start at, in the engine's notation.</summary>
        private string? EntryPoint(string option)
        {
            if (!m_options.TryGetValue(option, out XPathValue named))
            {
                return null;
            }

            XPathValue name = RequireAtomic(option, named);

            if (name.TypeCode != XdmTypeCode.QName)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0004,
                    $"The {option} of fn:transform() is an xs:QName, and '{name.ToStringValue()}' is not one.");
            }

            XdmQName written = name.AsQName();

            return "{" + written.NamespaceUri + "}" + written.LocalName;
        }

        /// <summary>
        /// The document to transform, and what templates are first applied to within it.
        /// </summary>
        /// <remarks>
        /// A source node and an initial match selection are the same thing from two directions: the node is
        /// the selection where no selection was written, and the selection is what the transformation begins
        /// on. A selection of atomic values needs no document at all, which is why the tree may be absent
        /// while the selection is not.
        /// </remarks>
        private XdmTree? SourceTree(out XPathValue? selection)
        {
            XdmTree? tree = null;
            selection = null;

            if (m_options.TryGetValue("source-node", out XPathValue given))
            {
                XPathValue node = RequireNode("source-node", given);
                tree = node.NodeTree;

                // Named as the selection only where it is not the document the transformation was handed:
                // applying templates to the root is what a transformation does by default, and saying so
                // again would put the root through the initial selection instead.
                if (node.NodeId != XdmTree.RootNode)
                {
                    selection = node;
                }
            }

            if (m_options.TryGetValue("initial-match-selection", out XPathValue chosen))
            {
                selection = chosen;

                foreach (XPathValue item in XdmSequence.Items(chosen))
                {
                    if (item.Kind == XPathValueKind.Node && tree is null)
                    {
                        tree = item.NodeTree;
                    }
                }
            }

            return tree;
        }

        /// <summary>How the principal result is serialized, where the delivery format is serialized.</summary>
        private OutputSettings Serialization(CompiledStylesheet compiled)
        {
            OutputSettings settings = compiled.OutputSettings.Copy();

            if (!m_options.TryGetValue("serialization-params", out XPathValue given))
            {
                return settings;
            }

            foreach (KeyValuePair<XPathValue, XPathValue> entry in RequireMap("serialization-params", given).Entries)
            {
                if (entry.Key.TypeCode != XdmTypeCode.QName)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOXT0004,
                        "The serialization-params of fn:transform() are named by QNames, and this map has a "
                        + "key that is not one.");
                }

                XdmQName name = entry.Key.AsQName();

                if (name.NamespaceUri.Length != 0)
                {
                    continue;
                }

                SerializationAttributes.Apply(settings, name.LocalName, Written(entry.Value), _ => null);
            }

            return settings;
        }

        /// <summary>A serialization parameter's value, as the attribute spelling of it.</summary>
        private static string Written(XPathValue value)
        {
            List<string> parts = new List<string>();

            foreach (XPathValue item in XdmSequence.Items(value))
            {
                parts.Add(XdmSequence.StringValueOf(item));
            }

            return string.Join(" ", parts);
        }

        /// <summary>Whether a value is one atomic item, which is what an option name and most values are.</summary>
        private static bool IsAtomic(XPathValue value)
        {
            return value.Kind is XPathValueKind.String or XPathValueKind.Number or XPathValueKind.Boolean;
        }

        /// <summary>Reads an option that is one atomic value.</summary>
        private static XPathValue RequireAtomic(string option, XPathValue value)
        {
            IReadOnlyList<XPathValue> items = XdmSequence.Items(value);

            // A node is atomized, as the function conversion rules would do to it: an option written as the
            // content of an xsl:map-entry rather than in its select is a text node, and what it says is its
            // string value.
            if (items.Count == 1 && items[0].Kind is XPathValueKind.Node or XPathValueKind.NodeSet)
            {
                return XdmSequence.TypedValueAsOne(items[0], $"The option '{option}'");
            }

            if (items.Count != 1
                || items[0].Kind is XPathValueKind.Map or XPathValueKind.Array
                || items[0].IsFunctionItem)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0004,
                    $"The {option} of fn:transform() is one atomic value, and what was given is not.");
            }

            return items[0];
        }

        /// <summary>Reads an option that is one node.</summary>
        private static XPathValue RequireNode(string option, XPathValue value)
        {
            IReadOnlyList<XPathValue> items = XdmSequence.Items(value);

            if (items.Count != 1 || items[0].Kind != XPathValueKind.Node)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0004,
                    $"The {option} of fn:transform() is one node, and what was given is not.");
            }

            return items[0];
        }

        /// <summary>Reads an option that is one map.</summary>
        private static XdmMap RequireMap(string option, XPathValue value)
        {
            IReadOnlyList<XPathValue> items = XdmSequence.Items(value);

            if (items.Count != 1 || items[0].Kind != XPathValueKind.Map)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOXT0004,
                    $"The {option} of fn:transform() is one map, and what was given is not.");
            }

            return items[0].AsMap();
        }
    }
}
