namespace CodeDeeds.Xslt
{
    /// <summary>
    /// Configuration for an <see cref="Xslt"/> instance, supplied when it is constructed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Settings arrive through the constructor rather than as properties so that a compiled stylesheet is
    /// genuinely immutable once built. That is what makes one instance safe to share between concurrent
    /// transformations: there is nothing for one thread to change while another is mid-transform.
    /// </para>
    /// <para>
    /// The properties are <see langword="init"/>-only, so an options object cannot be altered after it has
    /// been handed over, and the same instance may safely configure several stylesheets.
    /// </para>
    /// </remarks>
    public sealed class XsltOptions
    {
        /// <summary>The settings used when none are supplied.</summary>
        public static XsltOptions Default { get; } = new XsltOptions();

        private readonly IReadOnlyDictionary<string, object?>? m_parameters;

        /// <summary>
        /// Gets the values supplied for the stylesheet's top-level <c>xsl:param</c> declarations.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A key is a parameter's local name — <c>"depth"</c> — or, for one in a namespace, its expanded name
        /// written as <c>"{urn:example}depth"</c>. Prefixes cannot be used, because the caller's prefixes are
        /// not the stylesheet's and there would be nothing to resolve them against.
        /// </para>
        /// <para>
        /// A value may be a <see cref="string"/>, a <see cref="bool"/>, any of the integer types, a
        /// <see cref="double"/>, <see cref="float"/> or <see cref="decimal"/>, an
        /// <see cref="Model.XdmTree"/> to pass a whole document, an <see cref="XPath.XPathValue"/> already
        /// built, or <see langword="null"/> for the empty sequence. A date or a duration is passed as its
        /// lexical form and read with <c>xs:dateTime($p)</c> in the stylesheet, which is what a parameter
        /// arriving from a command line or a request is anyway.
        /// </para>
        /// <para>
        /// A name the stylesheet does not declare as a parameter is ignored, as the specification requires:
        /// one set of values can then serve several stylesheets. A parameter the caller does not supply falls
        /// back on its default, or fails if it was declared <c>required="yes"</c>.
        /// </para>
        /// </remarks>
        public IReadOnlyDictionary<string, object?>? Parameters
        {
            get => m_parameters;

            // Copied rather than kept, because the claim that one Xslt is safe to share between threads must
            // not rest on the caller never touching the dictionary again.
            init => m_parameters = value is null
                ? null
                : new Dictionary<string, object?>(value, StringComparer.Ordinal);
        }

        private readonly IReadOnlyDictionary<string, object?>? m_templateParameters;
        private readonly IReadOnlyDictionary<string, object?>? m_tunnelParameters;

        /// <summary>
        /// Gets the values supplied for the <c>xsl:param</c> declarations of the template the transformation
        /// starts at.
        /// </summary>
        /// <remarks>
        /// A transformation started at a named template is a call, and XSLT 3.0 §2.3 lets the caller supply
        /// its arguments. Names and values take the same forms as <see cref="Parameters"/>, and for the same
        /// reasons: <c>"{urn:example}depth"</c> because the caller's prefixes are not the stylesheet's, and
        /// a parameter the template does not declare is ignored.
        /// <para>
        /// Only the template the transformation starts at reads these. A parameter the caller wants every
        /// template to see is a tunnel one — see <see cref="TunnelParameters"/> — and one the whole
        /// stylesheet reads is an <c>xsl:param</c> at the top of it, which is <see cref="Parameters"/>.
        /// </para>
        /// </remarks>
        public IReadOnlyDictionary<string, object?>? TemplateParameters
        {
            get => m_templateParameters;

            init => m_templateParameters = value is null
                ? null
                : new Dictionary<string, object?>(value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets the tunnel parameters the transformation starts with.
        /// </summary>
        /// <remarks>
        /// A tunnel parameter passes through every template that does not declare it, so these reach as far
        /// down as the transformation goes rather than stopping at the template it starts in. Supplied the
        /// same way as <see cref="TemplateParameters"/>, and they reach whichever way in the caller chose:
        /// a named template, an initial mode, or applying templates to the source document.
        /// </remarks>
        public IReadOnlyDictionary<string, object?>? TunnelParameters
        {
            get => m_tunnelParameters;

            init => m_tunnelParameters = value is null
                ? null
                : new Dictionary<string, object?>(value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets how expressions are executed. Defaults to <see cref="XsltBackend.Interpreted"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is decided at construction because it determines what the stylesheet compiles to; changing it
        /// means compiling again.
        /// </para>
        /// <para>
        /// The default suits a stylesheet used a handful of times, which is the case that cannot opt out of a
        /// bad choice: emitting IL has to be paid for before the first transform runs, and a caller doing one
        /// transformation would spend several times the whole job on it. Choose
        /// <see cref="XsltBackend.Compiled"/> for a stylesheet that will be reused — from about thirty
        /// transformations onwards it is ahead, and it keeps pulling ahead after that.
        /// </para>
        /// </remarks>
        public XsltBackend Backend { get; init; } = XsltBackend.Interpreted;

        /// <summary>
        /// Gets where <c>xsl:result-document</c> may write, or <see langword="null"/> to refuse it.
        /// </summary>
        /// <remarks>
        /// Null by default, which makes <c>xsl:result-document</c> an error rather than a way for a
        /// stylesheet to create files. Of the three resolvers this is the one that writes, so it is the one
        /// worth being deliberate about.
        /// </remarks>
        public IXsltResultResolver? ResultResolver { get; init; }

        /// <summary>
        /// Gets where <c>xsl:result-document</c> may write bytes, or <see langword="null"/> to refuse it.
        /// </summary>
        /// <remarks>
        /// Null by default, as <see cref="ResultResolver"/> is and for the same reason. Prefer this one where
        /// the destination is a file or any other sink of bytes: only here can a result document's own
        /// <c>xsl:output encoding</c> reach the bytes rather than only the XML declaration that names it.
        /// Takes precedence over <see cref="ResultResolver"/> where both are supplied.
        /// </remarks>
        public IXsltResultStreamResolver? ResultStreamResolver { get; init; }

        /// <summary>
        /// Gets whether to suppress the XML declaration, overriding the stylesheet's
        /// <c>xsl:output omit-xml-declaration</c>.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/>, the default, follows the stylesheet — which in turn defaults to writing the
        /// declaration, as XSLT specifies. <see langword="true"/> suppresses it and <see langword="false"/>
        /// forces it, whatever the stylesheet asked for.
        /// <para>
        /// Whether a result wants a declaration usually depends on what the caller does with it — a standalone
        /// document wants one, a fragment about to be embedded does not — rather than on the transformation.
        /// To serve both from one stylesheet without compiling it twice, see <see cref="Xslt.With"/>.
        /// </para>
        /// </remarks>
        public bool? OmitXmlDeclaration { get; init; }

        /// <summary>
        /// Gets the writer that receives <c>xsl:message</c> output. Messages are discarded when this is
        /// <see langword="null"/>.
        /// </summary>
        public TextWriter? MessageWriter { get; init; }

        /// <summary>
        /// Gets the resolver that locates stylesheets named by <c>xsl:include</c> and <c>xsl:import</c>.
        /// </summary>
        /// <remarks>
        /// <see langword="null"/>, the default, means references cannot be followed at all and a stylesheet
        /// containing one is rejected. Following an <c>href</c> lets the stylesheet decide what this process
        /// reads, so it is something the caller opts into rather than something the engine assumes.
        /// </remarks>
        public IXsltResolver? StylesheetResolver { get; init; }

        /// <summary>
        /// Gets the resolver that locates documents named by the <c>document()</c> function.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="StylesheetResolver"/> on purpose: loading a module means loading code,
        /// while loading a document means loading data, and a caller may reasonably permit one without the
        /// other. <see langword="null"/>, the default, means <c>document()</c> cannot reach anything. The same
        /// resolver may of course be supplied for both.
        /// </remarks>
        public IXsltResolver? DocumentResolver { get; init; }

        /// <summary>
        /// Gets the resolver that says what a collection holds, for <c>fn:collection()</c> and
        /// <c>fn:uri-collection()</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A collection is a set of resources under a name, and what the name means is the caller's to
        /// decide: the specification leaves it to the processor. The resolver turns a collection URI, or the
        /// absence of one, into the URIs of the collection's members. <c>uri-collection()</c> answers with
        /// those, and <c>collection()</c> reads each through <see cref="DocumentResolver"/>, which must
        /// therefore be configured as well: one says what a collection holds, and the other is what reads
        /// it, so that one cache serves both and a document is the same document however it was reached.
        /// </para>
        /// <para>
        /// <see langword="null"/>, the default, means there is no collection, default or named, and either
        /// function is <c>FODC0002</c>. <see cref="FileResolver"/> and <see cref="UriResolver"/> serve a
        /// directory as a collection, so the instance configured as the document resolver can be configured
        /// here too.
        /// </para>
        /// </remarks>
        public IXsltCollectionResolver? CollectionResolver { get; init; }

        /// <summary>
        /// Gets the resolver that supplies collations of the caller's own, by URI.
        /// </summary>
        /// <remarks>
        /// The engine provides the code point collation, the HTML ASCII case-insensitive one and the
        /// Unicode Collation Algorithm with its parameters, and answers those before asking. This is for a
        /// collation the caller already has, under a URI of its choosing: what <c>xsl:sort</c>,
        /// <c>xsl:for-each-group</c>, <c>xsl:key</c>, <c>default-collation</c> and the collation arguments
        /// of the functions then reach. <see langword="null"/>, the default, means a URI the engine does not
        /// provide is an error. A transformation started by <c>fn:transform()</c> inherits the resolver.
        /// </remarks>
        public IXsltCollationResolver? CollationResolver { get; init; }

        /// <summary>
        /// Gets whether the processor is schema-aware: whether a stylesheet may import a schema and name
        /// the types it defines. Defaults to <see langword="false"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Off, the processor is a <em>basic</em> XSLT processor in the specification's sense:
        /// <c>xsl:import-schema</c> is <c>XTSE1650</c>, <c>validation="strict"</c> and a <c>type</c>
        /// attribute are <c>XTSE1660</c>, and <c>system-property('xsl:is-schema-aware')</c> is <c>no</c>.
        /// On, a stylesheet imports schemas through <c>xsl:import-schema</c>, from
        /// <see cref="Schemas"/> or through <see cref="SchemaResolver"/>, and the types they define are in
        /// scope for <c>as</c>, <c>instance of</c>, casts, constructor functions and <c>type-available()</c>.
        /// </para>
        /// <para>
        /// What is in place so far is the type system — user-defined atomic, list and union types, with
        /// their facets enforced by a cast — and typed input: a document validated as it is read, which
        /// <see cref="InputValidation"/> asks for, carries the types validation settled on, and
        /// atomization, the kind tests, <c>schema-element()</c>, <c>nilled()</c> and <c>id()</c> read
        /// them. <c>validation</c> and <c>type</c> on the instructions that construct nodes are not yet
        /// honoured, so those remain refused; a schema-aware transformation is otherwise the same
        /// transformation.
        /// </para>
        /// </remarks>
        public bool SchemaAware { get; init; }

        /// <summary>
        /// Gets the resolver that fetches a schema an <c>xsl:import-schema</c> names by
        /// <c>schema-location</c>, or by namespace alone, and what a schema includes or imports.
        /// </summary>
        /// <remarks>
        /// A fifth kind of reference with a resolver of its own: a schema is a definition of what is
        /// valid, which is neither code nor data, and a caller may reasonably let a stylesheet read one
        /// without letting it read the other two. <see langword="null"/>, the default, means a
        /// <c>schema-location</c> is <c>XTSE0165</c> and only <see cref="Schemas"/> is in scope. Asked
        /// with the namespace URI as the reference where an import names no location.
        /// </remarks>
        public IXsltResolver? SchemaResolver { get; init; }

        /// <summary>
        /// Gets schemas the caller has already loaded, whose types are in scope for every stylesheet
        /// compiled with these options.
        /// </summary>
        /// <remarks>
        /// Copied into a set of the stylesheet's own before anything is imported, so that what a stylesheet
        /// imports does not change this set under the caller. An <c>xsl:import-schema</c> naming a
        /// namespace this set covers imports nothing further.
        /// </remarks>
        public System.Xml.Schema.XmlSchemaSet? Schemas { get; init; }

        /// <summary>
        /// Gets how the document handed to the transformation is validated. Defaults to
        /// <see cref="XsltValidation.Strip"/>, which validates nothing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is about the input the caller supplies and nothing else: a document the stylesheet fetches
        /// for itself through <c>document()</c>, <c>doc()</c> or <c>collection()</c> is read as it stands,
        /// as the specification leaves it (§3.9.1), and is typed only where an instruction validates what
        /// it builds from it.
        /// </para>
        /// <para>
        /// <see cref="XsltValidation.Strict"/> and <see cref="XsltValidation.Lax"/> validate the input
        /// as it is read, against the schemas the stylesheet imported and those in <see cref="Schemas"/>,
        /// and annotate its elements and attributes with the types validation settles on: the typed value
        /// of an element declared <c>xs:integer</c> is an integer, <c>element(*, my:type)</c> matches what
        /// was validated as that type, <c>schema-element(e)</c> matches by declaration, <c>nilled()</c>
        /// answers, and <c>id()</c> follows what a schema typed as ID. Validation stops at the first
        /// fault, reported as the specification has it: <c>XTTE1510</c> for an invalid document under
        /// strict validation and <c>XTTE1515</c> under lax, <c>XTTE1512</c> for a document element strict
        /// validation finds no declaration for. Needs <see cref="SchemaAware"/>; asking for validation
        /// without it is refused when the stylesheet is constructed.
        /// </para>
        /// <para>
        /// The schemas in scope are the whole of what a document is validated against: an
        /// <c>xsi:schemaLocation</c> in the document is not followed, and an inline schema not read, since
        /// either would let a document add to a set every transformation over the stylesheet shares. A
        /// stylesheet declaring <c>input-type-annotations="strip"</c> is still validated, and then reads
        /// its documents untyped, as the specification asks. A tree the caller built and hands to
        /// <see cref="Xslt.Transform(Model.XdmTree)"/> is not validated here.
        /// </para>
        /// </remarks>
        public XsltValidation InputValidation { get; init; }

        /// <summary>
        /// Gets the resolver that finds the library packages named by <c>xsl:use-package</c>.
        /// </summary>
        /// <remarks>
        /// A third resolver rather than a use of <see cref="StylesheetResolver"/>, because what it is handed
        /// is a different kind of thing: <c>xsl:import</c> gives a reference to be resolved against a base
        /// URI, and <c>xsl:use-package</c> gives a package's <em>name</em>, which is an identity and not a
        /// location. A caller that keeps its packages in a database or an assembly has nowhere to put that
        /// name in a file-shaped resolver. <see langword="null"/>, the default, means a stylesheet cannot
        /// use a package it does not contain. A resolver that also implements
        /// <see cref="IXsltPackageResolver"/> is asked which versions it holds, so that an
        /// <c>xsl:use-package</c> naming a range of versions is given the one it takes.
        /// </remarks>
        public IXsltResolver? PackageResolver { get; init; }

        /// <summary>
        /// Gets the resolver that fetches what a document type declaration names outside its document: the
        /// external subset, and the parameter and general entities declared as external.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see langword="null"/>, the default, means nothing a declaration names is fetched. The declaration
        /// is still read for what it holds — its internal subset expands entities, supplies default
        /// attributes, and types attributes as ID for <c>id()</c> and declares unparsed entities for
        /// <c>unparsed-entity-uri()</c> — and an entity declared outside the document expands to nothing.
        /// That is what keeps a document from reading a local file or reaching the network through an
        /// entity reference, which is the oldest trick played on XML parsers, without refusing every
        /// document that carries a declaration.
        /// </para>
        /// <para>
        /// A fourth resolver rather than a use of <see cref="DocumentResolver"/>, because it opens a
        /// different door: that one lets the stylesheet name what is read, and this one lets the
        /// <em>input</em> name it. Applied to every document this engine parses — the stylesheet and its
        /// modules, the input, and what <c>document()</c> loads — since the question is the same for each.
        /// </para>
        /// </remarks>
        public IXsltResolver? EntityResolver { get; init; }

        /// <summary>
        /// Gets the identity of the stylesheet supplied to the constructor, which its own references resolve
        /// against.
        /// </summary>
        /// <remarks>
        /// Only meaningful alongside a <see cref="StylesheetResolver"/>. When it is <see langword="null"/> the
        /// resolver is asked to resolve against no base, which for <see cref="FileResolver"/> and
        /// <see cref="UriResolver"/> means the root directory.
        /// </remarks>
        public string? BaseUri { get; init; }

        /// <summary>
        /// Gets the base output URI: where the principal result is going, which a relative
        /// <c>xsl:result-document</c> <c>href</c> resolves against and what <c>current-output-uri()</c> answers
        /// with outside any result document.
        /// </summary>
        /// <remarks>
        /// Null by default, since a transformation writing to a <see cref="TextWriter"/> is going nowhere in
        /// particular: an <c>href</c> is then handed to the result resolver as written, and
        /// <c>current-output-uri()</c> is the empty sequence. Set, an <c>href</c> reaches the resolver resolved
        /// against it, and an <c>href</c> that resolves to it names the principal result itself.
        /// </remarks>
        public string? BaseOutputUri { get; init; }

        /// <summary>
        /// Gets where the input document came from, which references written <em>in it</em> resolve against.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A different question from <see cref="BaseUri"/>, and it took a conformance run to see it. That one
        /// says where the <em>stylesheet</em> is; this says where the <em>input</em> is, and the two are
        /// usually different directories. <c>document(@href)</c> over a catalogue means each reference
        /// relative to the catalogue, so an engine told only where the stylesheet lives resolves every one of
        /// them in the wrong place.
        /// </para>
        /// <para>
        /// <see langword="null"/>, the default, means the input came from nowhere in particular — which is the
        /// truth when it was handed over as text, and what makes <c>document-uri()</c> return the empty
        /// sequence rather than inventing something. Base URIs still fall back to the stylesheet's, since a
        /// relative reference has to resolve against something.
        /// </para>
        /// </remarks>
        public string? InputUri { get; init; }

        /// <summary>
        /// Gets the version of XSLT the processor claims to be, which decides what a stylesheet naming a
        /// later one is given.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defaults to <see cref="XsltVersion.Implemented"/>, which is 3.0. The setting is not cosmetic: a
        /// processor claiming 2.0 must read a <c>version="3.0"</c> stylesheet <em>forwards-compatibly</em>,
        /// which means an instruction it does not have is refused only when reached and an
        /// <c>xsl:fallback</c> beside it is taken. Set this to <see cref="XsltVersion.V20"/> to be that
        /// processor: <c>system-property('xsl:version')</c> then answers 2.0, the vocabulary is 2.0's, and a
        /// stylesheet written to branch on the answer takes the branch it wrote.
        /// </para>
        /// <para>
        /// The claim is about the whole language, not one feature at a time. A processor claiming 3.0 stops
        /// excusing what it does not implement, and refuses a 3.0 construct it lacks rather than falling
        /// back from it; and it reads a <c>version="2.0"</c> stylesheet with 3.0's vocabulary and library,
        /// since what that attribute asks for is backwards-compatible behaviour rather than a smaller
        /// language.
        /// </para>
        /// </remarks>
        public XsltVersion Version { get; init; } = XsltVersion.Implemented;

        /// <summary>
        /// Gets whether <c>xsl:evaluate</c> may compile and run an expression that arrives as a string while
        /// the transformation runs. Defaults to <see langword="true"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On by default because it opens nothing the stylesheet could not already do: a target expression
        /// sees the XPath library and the stylesheet's own public functions, and none of what XSLT adds —
        /// no <c>document()</c>, no <c>key()</c> — so the reach it has is the reach the caller granted
        /// through <see cref="DocumentResolver"/> and the other resolvers. What it costs is compiling at
        /// run time, and the risk the specification warns of is a stylesheet building an expression out of
        /// untrusted text, which a stylesheet can do with or without this switch.
        /// </para>
        /// <para>
        /// Off, the feature is disabled the way the specification calls <em>statically</em>:
        /// <c>element-available('xsl:evaluate')</c> is false and <c>xsl:supports-dynamic-evaluation</c> is
        /// <c>no</c> wherever asked, an <c>xsl:evaluate</c> with an <c>xsl:fallback</c> takes the fallback,
        /// and one without is <c>XTDE3175</c> when it is reached — an error an <c>xsl:try</c> can catch.
        /// </para>
        /// </remarks>
        public bool DynamicEvaluation { get; init; } = true;

        /// <summary>
        /// Gets whether <c>fn:environment-variable()</c> and <c>fn:available-environment-variables()</c>
        /// may read the process's environment. Defaults to <see langword="false"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Off by default, unlike <see cref="DynamicEvaluation"/>, because it opens something the
        /// stylesheet could not otherwise reach: an environment carries tokens and connection strings as
        /// often as it carries locale settings, and a stylesheet is data as often as it is code. The
        /// specification lets a processor decide whether environment variables are visible at all, and
        /// this one leaves the decision to the caller. Off, both functions answer nothing, which is the
        /// answer the specification gives a processor that provides no access; their arguments are still
        /// checked.
        /// </para>
        /// <para>
        /// On, a stylesheet reads the environment as it stood when the transformation first asked, so that
        /// it is the same environment for the whole transformation, and a <c>use-when</c> reads it when the
        /// stylesheet is compiled. Names are compared as the platform compares them: without regard to
        /// case on Windows, exactly elsewhere. A transformation started by <c>fn:transform()</c> inherits
        /// the setting, as it inherits the resolvers.
        /// </para>
        /// </remarks>
        public bool EnvironmentVariablesEnabled { get; init; }

        /// <summary>
        /// Gets the name of the template the transformation starts at, or <see langword="null"/> to start by
        /// matching the source document.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The second of the ways into a transformation. Instead of applying templates to a document and
        /// letting the patterns decide, the caller names a template and it is called — with whatever
        /// <see cref="Parameters"/> supplies bound to its <c>xsl:param</c> declarations. It is what a
        /// stylesheet that generates rather than transforms wants: there is no input to match against, so
        /// there is nothing for a pattern to be about.
        /// </para>
        /// <para>
        /// Written as a local name, or as an expanded name in the <c>{uri}local</c> notation. A prefix is
        /// refused for the same reason it is in <see cref="Parameters"/>: the prefixes in scope belong to the
        /// stylesheet and the caller has no way to know them.
        /// </para>
        /// </remarks>
        public string? InitialTemplate { get; init; }

        /// <summary>
        /// Gets the name of the mode the transformation starts in, or <see langword="null"/> for the
        /// unnamed mode.
        /// </summary>
        /// <remarks>
        /// Named in the same notation as <see cref="InitialTemplate"/>. A stylesheet whose rules are all in a
        /// mode has no unnamed rules to start from, and without this the transformation would fall through to
        /// the built-in rules and produce the document's text.
        /// </remarks>
        public string? InitialMode { get; init; }

        /// <summary>
        /// Gets the initial match selection: an XPath expression whose value the transformation applies
        /// templates to in place of the source document, or <see langword="null"/> to apply them to the
        /// document itself.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0's way of starting a transformation at something other than a document node — an element
        /// chosen inside the input, the items of a sequence, a value with no input at all. The expression is
        /// evaluated with the source document as its context item where there is one, in the namespaces
        /// declared on the stylesheet's outermost element, and its items are processed in order in the mode
        /// <see cref="InitialMode"/> names, each the context item with its position among them.
        /// </remarks>
        public string? InitialMatchSelection { get; init; }

        /// <summary>
        /// Gets the name of a stylesheet function to call as the whole of the transformation, or
        /// <see langword="null"/> to start in one of the other ways.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XSLT 3.0's third way in, beside a source document and a named template. The function is called
        /// with <see cref="FunctionArguments"/>, and what it returns is the result of the transformation.
        /// There is no source document to match against and no context item inside the function, a function
        /// having none of its own.
        /// </para>
        /// <para>
        /// Written as the caller writes a parameter name: <c>local</c>, or <c>{uri}local</c> for a function
        /// in a namespace. A stylesheet function is always in one, so the bare form names nothing. The
        /// function must be public, a package's private function being no way into it, and the arity is the
        /// number of arguments supplied: naming a function the stylesheet does not declare at that arity is
        /// <c>XTDE0041</c>.
        /// </para>
        /// </remarks>
        public string? InitialFunction { get; init; }

        /// <summary>
        /// Gets the arguments for <see cref="InitialFunction"/>, in order, or <see langword="null"/> for
        /// none.
        /// </summary>
        /// <remarks>
        /// Each takes the same kinds of value as <see cref="Parameters"/>: a string, a boolean, an integer
        /// type, a <see cref="double"/>, <see cref="float"/> or <see cref="decimal"/>, an
        /// <see cref="Model.XdmTree"/>, an <see cref="XPath.XPathValue"/>, or <see langword="null"/> for
        /// the empty sequence. How many there are is the arity the function is looked up by, so supplying
        /// the wrong number names a different function rather than mis-calling this one. Each is converted
        /// to the type the declaration asks for as an ordinary call's argument is, and a value that will
        /// not convert is the type error that conversion raises.
        /// </remarks>
        public IReadOnlyList<object?>? FunctionArguments { get; init; }

        /// <summary>
        /// Returns a copy of these options with a different declaration setting.
        /// </summary>
        /// <param name="omitXmlDeclaration">The new value for <see cref="OmitXmlDeclaration"/>.</param>
        public XsltOptions WithOmitXmlDeclaration(bool? omitXmlDeclaration)
        {
            return new XsltOptions
            {
                Backend = Backend,
                OmitXmlDeclaration = omitXmlDeclaration,
                MessageWriter = MessageWriter,
                StylesheetResolver = StylesheetResolver,
                DocumentResolver = DocumentResolver,
                CollectionResolver = CollectionResolver,
                CollationResolver = CollationResolver,
                PackageResolver = PackageResolver,
                EntityResolver = EntityResolver,
                EnvironmentVariablesEnabled = EnvironmentVariablesEnabled,
                SchemaAware = SchemaAware,
                SchemaResolver = SchemaResolver,
                Schemas = Schemas,
                InputValidation = InputValidation,
                ResultResolver = ResultResolver,
                ResultStreamResolver = ResultStreamResolver,
                BaseUri = BaseUri,
                BaseOutputUri = BaseOutputUri,
                Version = Version,
                Parameters = Parameters,
                TemplateParameters = TemplateParameters,
                TunnelParameters = TunnelParameters,
                InitialTemplate = InitialTemplate,
                InitialMode = InitialMode,
                InitialMatchSelection = InitialMatchSelection,
            };
        }
    }
}
