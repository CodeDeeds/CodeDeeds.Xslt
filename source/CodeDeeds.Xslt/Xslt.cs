using System.Text;
using CodeDeeds.Xslt.Compiler;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt
{
    /// <summary>
    /// Represents a pre-compiled XSLT transformation.
    /// </summary>
    /// <remarks>
    /// This is a special implementation which also accepts JSON as input, as well as XML.
    /// The XSLT transformation is pre-compiled and can be used to transform XML or JSON data into other formats.
    /// <para>
    /// Compilation happens once, in the constructor, and everything configurable is supplied there through
    /// <see cref="XsltOptions"/>. An instance is therefore immutable and can be shared by any number of
    /// concurrent transformations; each call allocates only the state belonging to that one transformation.
    /// Use <see cref="With"/> for a differently configured instance without compiling again.
    /// </para>
    /// XSLT specifications:
    /// https://www.w3.org/TR/xslt-30/
    /// https://www.w3.org/Style/XSL/
    /// Other resources in GitHub: https://github.com/w3c/qtspecs/tree/master
    /// </remarks>
    public class Xslt
    {
        private readonly CompiledStylesheet m_stylesheet;

        private readonly XsltOptions m_options;

        /// <summary>
        /// Initializes a new instance of the Xslt class with the specified XSLT definition.
        /// </summary>
        /// <param name="XsltDefinition"></param>
        /// <param name="options">Configuration for this stylesheet, or <see langword="null"/> for the defaults.</param>
        public Xslt(string XsltDefinition, XsltOptions? options = null)
            : this(new StringReader(XsltDefinition), options)
        {
        }

        /// <summary>
        /// Initializes a new instance of the Xslt class from the bytes of a stylesheet.
        /// </summary>
        /// <remarks>
        /// Preferred where the bytes are what the caller has: a stylesheet says what encoding it is in, through
        /// a byte-order mark or the <c>encoding</c> of its declaration, and only the bytes carry that. A
        /// <see cref="TextReader"/> has already decided.
        /// </remarks>
        /// <param name="stream">The stylesheet, positioned at its beginning. The stream is closed once the instance is initialized.</param>
        /// <param name="options">Configuration for this stylesheet, or <see langword="null"/> for the defaults.</param>
        public Xslt(Stream stream, XsltOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(stream);

            m_options = options ?? XsltOptions.Default;
            CheckOptions(m_options);

            try
            {
                XdmTree stylesheetTree = XdmTreeBuilder.FromXml(
                    stream, locations: true, entityResolver: m_options.EntityResolver, baseUri: m_options.BaseUri);
                m_stylesheet = StylesheetCompiler.Compile(stylesheetTree, m_options);
            }
            finally
            {
                stream.Dispose();
            }
        }

        /// <summary>
        /// Initializes a new instance of the Xslt class with the specified StreamReader containing the XSLT definition.
        /// </summary>
        /// <param name="reader">A TextReader containing the XSLT definition. The reader should be positioned at the beginning of the XSLT definition. The reader will be closed after the Xslt instance is initialized.</param>
        /// <param name="options">Configuration for this stylesheet, or <see langword="null"/> for the defaults.</param>
        public Xslt(TextReader reader, XsltOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(reader);

            m_options = options ?? XsltOptions.Default;
            CheckOptions(m_options);

            try
            {
                XdmTree stylesheetTree = XdmTreeBuilder.FromXml(
                    reader, locations: true, entityResolver: m_options.EntityResolver, baseUri: m_options.BaseUri);
                m_stylesheet = StylesheetCompiler.Compile(stylesheetTree, m_options);
            }
            finally
            {
                reader.Dispose();
            }
        }

        /// <summary>
        /// Compiles a stylesheet that is embedded in a document rather than being one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XSLT 3.0 §3.12: a stylesheet module need not be a document of its own. Its outermost
        /// element — <c>xsl:stylesheet</c>, <c>xsl:transform</c>, <c>xsl:package</c>, or a literal
        /// result element carrying <c>xsl:version</c> — may be a child of some element in a host
        /// document, and the usual reason is that the host document is the one to transform. What is around
        /// it belongs to the host: standard attributes written on its ancestors have no effect on the
        /// stylesheet (§3.4), so a document may say <c>xsl:version</c> or
        /// <c>xsl:xpath-default-namespace</c> about its own content without either reaching in. Namespace
        /// declarations are not standard attributes and are inherited as XML says.
        /// </para>
        /// <para>
        /// Which element is the module may be said two ways. Naming an <paramref name="id"/> says it
        /// outright: the module is the element carrying that identifier, which is an <c>xml:id</c> or an
        /// attribute a DTD or schema typed <c>ID</c>. Leaving it out asks the document, which says so with
        /// an <c>xml-stylesheet</c> processing instruction (<c>[XML Stylesheet]</c>) whose <c>href</c> is a
        /// bare fragment: <c>&lt;?xml-stylesheet type="application/xslt+xml" href="#style1"?&gt;</c>.
        /// That instruction is not part of XSLT and reading it is not required for conformance, which is
        /// why it is a method to call rather than something the ordinary constructors do.
        /// </para>
        /// <para>
        /// The tree is read and not altered, so the same one may be handed to <see cref="Transform(XdmTree)"/>
        /// afterwards — which is the point, a stylesheet embedded in the document it transforms being
        /// what the feature is for. Set <see cref="XsltOptions.BaseUri"/>, or build the tree with a base
        /// URI, for an <c>xsl:import</c> inside the module to have something to resolve against.
        /// </para>
        /// </remarks>
        /// <param name="document">The host document.</param>
        /// <param name="id">
        /// The identifier of the module's outermost element, or <see langword="null"/> to read the
        /// document's own <c>xml-stylesheet</c> instruction.
        /// </param>
        /// <param name="options">Configuration for this stylesheet, or <see langword="null"/> for the defaults.</param>
        /// <returns>The compiled stylesheet.</returns>
        /// <exception cref="XsltException">
        /// The document names no stylesheet, names one that is not in it, or names an element that is not a
        /// stylesheet module.
        /// </exception>
        public static Xslt Embedded(XdmTree document, string? id = null, XsltOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(document);

            options ??= XsltOptions.Default;
            CheckOptions(options);

            string named = id ?? StylesheetNamedBy(document);

            if (!Compiler.IdExpr.BuildIndex(document).TryGetValue(named, out int outermost))
            {
                throw new XsltException(
                    $"The stylesheet named is the element whose identifier is '{named}', and nothing in the "
                    + "document carries that identifier. An identifier is an xml:id, or an attribute a DTD "
                    + "or a schema typed as ID.");
            }

            return new Xslt(StylesheetCompiler.Compile(document, options, outermost), options);
        }

        /// <summary>
        /// The identifier a document's <c>xml-stylesheet</c> instruction names.
        /// </summary>
        /// <remarks>
        /// The instruction's data is pseudo-attributes: <c>type</c>, <c>href</c>, and optionally
        /// <c>alternate</c>, <c>title</c>, <c>media</c> and <c>charset</c>. Only the first instruction
        /// naming an XSLT media type and not marked <c>alternate="yes"</c> is taken, which is what a
        /// document with a CSS stylesheet beside its XSLT one means. The two media types are the one XSLT
        /// registered, <c>application/xslt+xml</c>, and <c>text/xsl</c>, which browsers established before
        /// there was one to register.
        /// </remarks>
        /// <param name="document">The document to ask.</param>
        private static string StylesheetNamedBy(XdmTree document)
        {
            for (int child = document.FirstChildOf(XdmTree.RootNode);
                 child >= 0;
                 child = document.NextSiblingOf(child))
            {
                if (document.KindOf(child) != NodeKind.ProcessingInstruction
                    || document.NameTable.GetLocalName(document.FingerprintOf(child)) != "xml-stylesheet")
                {
                    continue;
                }

                Dictionary<string, string> said = PseudoAttributes(document.StringValueOf(child));

                if (!said.TryGetValue("type", out string? type)
                    || type is not ("application/xslt+xml" or "text/xsl")
                    || (said.TryGetValue("alternate", out string? alternate) && alternate == "yes"))
                {
                    continue;
                }

                if (!said.TryGetValue("href", out string? href) || !href.StartsWith('#'))
                {
                    throw new XsltException(
                        $"The document's xml-stylesheet instruction names '{href ?? string.Empty}', which is "
                        + "not a stylesheet embedded in this document. Only a bare fragment is, which names "
                        + "the element carrying that identifier; read anything else yourself and compile it "
                        + "with a constructor.");
                }

                return href[1..];
            }

            throw new XsltException(
                "The document carries no xml-stylesheet instruction naming an XSLT stylesheet, so there is "
                + "nothing to say which element the embedded module is. Name it by its identifier instead.");
        }

        /// <summary>Reads the pseudo-attributes of an <c>xml-stylesheet</c> instruction.</summary>
        /// <remarks>
        /// Each is a name, an equals sign and a quoted value, separated by whitespace. Anything that is not
        /// that shape ends the reading: the instruction is the document's and this engine is not the one to
        /// decide what a malformed one meant.
        /// </remarks>
        /// <param name="data">The instruction's data.</param>
        private static Dictionary<string, string> PseudoAttributes(string data)
        {
            Dictionary<string, string> said = new(StringComparer.Ordinal);
            int at = 0;

            while (at < data.Length)
            {
                while (at < data.Length && char.IsWhiteSpace(data[at]))
                {
                    at++;
                }

                int name = at;

                while (at < data.Length && data[at] != '=' && !char.IsWhiteSpace(data[at]))
                {
                    at++;
                }

                if (at >= data.Length || data[at] != '=' || at == name)
                {
                    break;
                }

                char quote = ++at < data.Length ? data[at] : '\0';

                if (quote is not ('"' or '\''))
                {
                    break;
                }

                int value = ++at;

                while (at < data.Length && data[at] != quote)
                {
                    at++;
                }

                if (at >= data.Length)
                {
                    break;
                }

                said[data[name..(value - 2)]] = data[value..at++];
            }

            return said;
        }

        private Xslt(CompiledStylesheet stylesheet, XsltOptions options)
        {
            m_stylesheet = stylesheet;
            m_options = options;
        }

        /// <summary>Refuses options that contradict one another before anything is compiled with them.</summary>
        /// <param name="options">The options.</param>
        /// <exception cref="ArgumentException">Input validation is asked for without schema awareness.</exception>
        private static void CheckOptions(XsltOptions options)
        {
            if (options.InputValidation is XsltValidation.Strict or XsltValidation.Lax && !options.SchemaAware)
            {
                throw new ArgumentException(
                    "XsltOptions.InputValidation asks for the documents read to be validated, which needs "
                    + "XsltOptions.SchemaAware: the schemas to validate against are the ones a schema-aware "
                    + "stylesheet imports and the caller supplies.",
                    nameof(options));
            }
        }

        /// <summary>
        /// What the input is validated against, or null where it is read as it is; see
        /// <see cref="XsltOptions.InputValidation"/>.
        /// </summary>
        private TreeValidation? InputValidation => m_stylesheet.InputValidationFor(m_options);

        /// <summary>Gets the configuration this stylesheet was built with.</summary>
        public XsltOptions Options => m_options;

        /// <summary>Gets the serialization method the stylesheet's <c>xsl:output</c> selected.</summary>
        public OutputMethod OutputMethod => m_stylesheet.OutputSettings.Method;

        /// <summary>Gets the encoding named by <c>xsl:output</c>.</summary>
        /// <remarks>
        /// Whether it decides anything depends on where the result goes. Written to a <see cref="Stream"/>
        /// this engine produces it, so the declaration is true; written to a <see cref="TextWriter"/> the
        /// bytes belong to whoever built that writer, and this is only the name the result labels itself
        /// with. Read it to build a writer that agrees, or transform to a stream instead.
        /// </remarks>
        public string OutputEncoding => m_stylesheet.OutputSettings.Encoding;

        /// <summary>
        /// Gets the media type of the result, as named by <c>xsl:output</c> or implied by its method.
        /// </summary>
        /// <remarks>
        /// The reason <c>media-type</c> is worth carrying: it is what a caller puts in a <c>Content-Type</c>
        /// header, and only the stylesheet knows it. Falls back to <c>text/xml</c>, <c>text/html</c> or
        /// <c>text/plain</c> to match the method.
        /// </remarks>
        public string OutputMediaType => m_stylesheet.OutputSettings.EffectiveMediaType;

        /// <summary>
        /// Returns an instance that shares this compiled stylesheet but uses different options.
        /// </summary>
        /// <remarks>
        /// Compiling is the expensive part, and the compiled form does not depend on the serialization
        /// settings, so serving both a standalone document and an embedded fragment from one stylesheet costs
        /// only this object rather than a second compilation.
        /// </remarks>
        /// <param name="options">The options the returned instance uses.</param>
        /// <returns>A new instance over the same compiled stylesheet.</returns>
        /// <exception cref="ArgumentException">
        /// <see cref="XsltOptions.Backend"/> differs from this instance's. The backend determines what the
        /// stylesheet was compiled to, so changing it requires compiling again.
        /// </exception>
        public Xslt With(XsltOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.Backend != m_options.Backend)
            {
                throw new ArgumentException(
                    $"The backend is fixed when a stylesheet is compiled; this one was compiled for "
                    + $"{m_options.Backend}. Construct a new Xslt to use {options.Backend}.",
                    nameof(options));
            }

            CheckOptions(options);
            return new Xslt(m_stylesheet, options);
        }


        public string TransformXml(string xmlInput)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);

            // Gathered in a borrowed buffer sized like the input, so that the result string is the one
            // thing the output allocates; see PooledStringWriter.
            using PooledStringWriter writer = new PooledStringWriter(xmlInput.Length);
            Transform(ParseXmlText(xmlInput), writer);
            return writer.Finish();
        }

        /// <summary>
        /// Runs the stylesheet with no document to transform, starting at the template
        /// <see cref="XsltOptions.InitialTemplate"/> names.
        /// </summary>
        /// <returns>The result.</returns>
        /// <remarks>
        /// <para>
        /// For a stylesheet that generates rather than transforms. There is no input to match against, so
        /// there is nothing for a pattern to be about and nothing to start from — which is why the template
        /// has to be named. Reading the context item inside one is an error rather than an empty answer, the
        /// context item being absent and not empty.
        /// </para>
        /// <para>
        /// Everything else works as it does with a source document: <see cref="XsltOptions.Parameters"/>
        /// binds the stylesheet's parameters, <c>document()</c> reads through the resolver, and the result
        /// is serialized as <c>xsl:output</c> asks.
        /// </para>
        /// </remarks>
        /// <exception cref="XsltException">
        /// No initial template was named, or the stylesheet declares no template of that name.
        /// </exception>
        public string Transform()
        {
            using PooledStringWriter writer = new PooledStringWriter(4096);
            Transform(writer);
            return writer.Finish();
        }

        /// <summary>
        /// Runs the stylesheet with no document to transform, writing the result.
        /// </summary>
        /// <param name="writer">Where to write the result.</param>
        public void Transform(TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);

            Transform(
                XdmTreeBuilder.Empty(),
                writer,
                m_stylesheet.OutputSettings.With(m_options.OmitXmlDeclaration),
                hasSource: false);
        }

        /// <summary>
        /// Runs the stylesheet with no document to transform, writing the result in the encoding the
        /// stylesheet declared.
        /// </summary>
        /// <param name="output">Where to write the result. Left open, and flushed but not disposed.</param>
        public void Transform(Stream output)
        {
            ArgumentNullException.ThrowIfNull(output);

            OutputSettings declared = m_stylesheet.OutputSettings.With(m_options.OmitXmlDeclaration);

            using StreamWriter writer = SerializationEncoding.CreateWriter(
                output, declared, out OutputSettings settings);

            Transform(XdmTreeBuilder.Empty(), writer, settings, hasSource: false);
        }

        public void TransformXml(string xmlInput, TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            ArgumentNullException.ThrowIfNull(writer);

            Transform(ParseXmlText(xmlInput), writer);
        }

        /// <summary>
        /// Transforms an XML document held in a string, giving the result as a tree rather than as
        /// serialized text.
        /// </summary>
        /// <param name="xmlInput">The XML to transform.</param>
        /// <returns>The document node of the result.</returns>
        /// <remarks>
        /// <para>
        /// For a caller who wants to read the result, or to transform it again, rather than to write it
        /// out. Serializing a result and parsing it back is lossy as well as slow: what a schema-aware
        /// transformation validated carries the types validation settled on, and those are in the tree and
        /// not in the text. A tree handed back here keeps them, and <see cref="TransformToTree(XdmTree)"/>
        /// takes it again without a round trip through XML.
        /// </para>
        /// <para>
        /// Nothing is serialized, so <c>xsl:output</c> is not consulted: the result is what the
        /// transformation produced, with atomic values written into it as their string values, exactly as
        /// the content of an <c>xsl:variable</c> becomes a tree. An <c>xsl:result-document</c> is a second
        /// result and goes where it always goes, through
        /// <see cref="XsltOptions.ResultResolver"/>.
        /// </para>
        /// </remarks>
        public XdmTree TransformXmlToTree(string xmlInput)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            return RunToTree(ParseXmlText(xmlInput), hasSource: true, release: true);
        }

        /// <summary>
        /// Transforms an XML document read from a reader, giving the result as a tree rather than as
        /// serialized text.
        /// </summary>
        /// <param name="xmlInput">Where to read the XML from.</param>
        /// <returns>The document node of the result.</returns>
        public XdmTree TransformXmlToTree(TextReader xmlInput)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            return RunToTree(
                XdmTreeBuilder.FromXmlPooled(
                    xmlInput, m_stylesheet.Whitespace, m_options.EntityResolver, m_options.InputUri, InputValidation),
                hasSource: true,
                release: true);
        }

        /// <summary>
        /// Transforms an XML document read from a stream, giving the result as a tree rather than as
        /// serialized text.
        /// </summary>
        /// <param name="xmlInput">Where to read the XML from.</param>
        /// <returns>The document node of the result.</returns>
        public XdmTree TransformXmlToTree(Stream xmlInput)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            return RunToTree(
                XdmTreeBuilder.FromXmlPooled(
                    xmlInput, m_stylesheet.Whitespace, m_options.EntityResolver, m_options.InputUri, InputValidation),
                hasSource: true,
                release: true);
        }

        /// <summary>
        /// Runs the stylesheet with no document to transform, starting at the template
        /// <see cref="XsltOptions.InitialTemplate"/> names, and gives the result as a tree.
        /// </summary>
        /// <returns>The document node of the result.</returns>
        /// <exception cref="XsltException">
        /// No initial template was named, or the stylesheet declares no template of that name.
        /// </exception>
        public XdmTree TransformToTree()
        {
            return RunToTree(XdmTreeBuilder.Empty(), hasSource: false, release: true);
        }

        /// <summary>
        /// Transforms a tree the caller already has, giving the result as a tree: one step of a chain of
        /// transformations, with no serializing between them.
        /// </summary>
        /// <param name="input">The document to transform, which is left as it was.</param>
        /// <returns>The document node of the result.</returns>
        /// <remarks>
        /// The tree is read and not altered, so one document may be transformed by several stylesheets, and
        /// the annotations a validated tree carries are what this transformation sees — which is the point
        /// of chaining without a round trip through text.
        /// </remarks>
        public XdmTree TransformToTree(XdmTree input)
        {
            ArgumentNullException.ThrowIfNull(input);
            return RunToTree(input, hasSource: true, release: false);
        }

        /// <summary>Transforms a tree the caller already has, giving the result as serialized text.</summary>
        /// <param name="input">The document to transform, which is left as it was.</param>
        /// <returns>The result, serialized as <c>xsl:output</c> asks.</returns>
        public string Transform(XdmTree input)
        {
            ArgumentNullException.ThrowIfNull(input);

            using PooledStringWriter writer = new PooledStringWriter(4096);
            TransformCore(input, writer, m_stylesheet.OutputSettings.With(m_options.OmitXmlDeclaration), hasSource: true);
            return writer.Finish();
        }

        /// <summary>
        /// Runs the stylesheet with no document to transform, starting at the template or function the
        /// options name, and gives the result as the sequence of items it is rather than as a tree.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A stylesheet need not produce a document. An initial function returns whatever its body returns,
        /// and <c>build-tree="no"</c> says the same of a template: the result is a sequence, and its items
        /// may be atomic values, parentless attributes or maps as readily as elements. Serializing that
        /// loses what the items were — the integer 42 and the string "42" serialize alike — so a
        /// caller who wants the values asks for them here.
        /// </para>
        /// <para>
        /// What the other methods do is build a document from the same sequence, which is what
        /// <c>build-tree="yes"</c>, the default, asks for. Ask for the sequence when the stylesheet says no.
        /// </para>
        /// </remarks>
        /// <returns>The items of the result, in order.</returns>
        /// <exception cref="XsltException">
        /// No entry point was named, or the stylesheet declares none of that name.
        /// </exception>
        public IReadOnlyList<XPathValue> TransformToSequence()
        {
            return RunToSequence(XdmTreeBuilder.Empty(), hasSource: false, release: true);
        }

        /// <summary>
        /// Transforms XML held in a string, giving the result as the sequence of items it is rather than as
        /// a tree.
        /// </summary>
        /// <param name="xmlInput">The document to transform, as XML text.</param>
        /// <returns>The items of the result, in order.</returns>
        /// <remarks>See <see cref="TransformToSequence()"/> for when a sequence is what to ask for.</remarks>
        public IReadOnlyList<XPathValue> TransformXmlToSequence(string xmlInput)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            return RunToSequence(ParseXmlText(xmlInput), hasSource: true, release: true);
        }

        /// <summary>
        /// Transforms a tree the caller already has, giving the result as the sequence of items it is
        /// rather than as a tree.
        /// </summary>
        /// <param name="input">The document to transform, which is left as it was.</param>
        /// <returns>The items of the result, in order.</returns>
        /// <remarks>See <see cref="TransformToSequence()"/> for when a sequence is what to ask for.</remarks>
        public IReadOnlyList<XPathValue> TransformToSequence(XdmTree input)
        {
            ArgumentNullException.ThrowIfNull(input);
            return RunToSequence(input, hasSource: true, release: false);
        }

        /// <summary>
        /// Runs the transformation into a sequence rather than into a tree or a writer.
        /// </summary>
        /// <param name="input">The document to transform.</param>
        /// <param name="hasSource">Whether there is a source document, or the run starts at an entry point.</param>
        /// <param name="release">
        /// Whether the input's storage may go back to the pool it came from, which it may when this
        /// transformation parsed it and must not when the caller handed it over.
        /// </param>
        private IReadOnlyList<XPathValue> RunToSequence(XdmTree input, bool hasSource, bool release)
        {
            try
            {
                SequenceCaptureTarget output = new SequenceCaptureTarget(baseUri: m_options.InputUri)
                {
                    StandsForFinalOutput = true,
                };

                XsltRuntime runtime = new XsltRuntime(m_stylesheet, input, output, m_options, hasSource)
                {
                    MessageWriter = m_options.MessageWriter,
                    WarningWriter = m_options.WarningWriter,
                };

                runtime.Run();
                return XdmSequence.Items(output.Finish());
            }
            finally
            {
                if (release)
                {
                    input.ReleaseStorage();
                }
            }
        }

        /// <summary>
        /// Runs the transformation into a tree rather than into a writer.
        /// </summary>
        /// <param name="input">The document to transform.</param>
        /// <param name="hasSource">Whether there is a source document, or the run starts at a named template.</param>
        /// <param name="release">
        /// Whether the input's storage may go back to the pool it came from, which it may when this
        /// transformation parsed it and must not when the caller handed it over.
        /// </param>
        private XdmTree RunToTree(XdmTree input, bool hasSource, bool release)
        {
            try
            {
                ResultTreeBuilder output = new ResultTreeBuilder
                {
                    StandsForFinalOutput = true,
                    BaseUri = m_options.InputUri,
                };

                XsltRuntime runtime = new XsltRuntime(m_stylesheet, input, output, m_options, hasSource)
                {
                    MessageWriter = m_options.MessageWriter,
                    WarningWriter = m_options.WarningWriter,
                };

                runtime.Run();
                return output.Finish();
            }
            finally
            {
                if (release)
                {
                    input.ReleaseStorage();
                }
            }
        }

        /// <summary>
        /// Parses XML held in a string, letting the tree size its storage from the text rather than grow
        /// into it.
        /// </summary>
        private XdmTree ParseXmlText(string xmlInput)
        {
            return XdmTreeBuilder.FromXmlPooled(
                xmlInput, m_stylesheet.Whitespace, m_options.EntityResolver, m_options.InputUri, InputValidation);
        }

        public void TransformXml(TextReader xmlInput, TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            ArgumentNullException.ThrowIfNull(writer);

            Transform(XdmTreeBuilder.FromXmlPooled(xmlInput, m_stylesheet.Whitespace, m_options.EntityResolver, m_options.InputUri, InputValidation), writer);
        }

        /// <summary>
        /// Transforms an XML document read from a stream.
        /// </summary>
        /// <param name="xmlInput">The XML to transform. The caller retains ownership and must dispose it.</param>
        /// <param name="writer">Where to write the result.</param>
        /// <remarks>
        /// Preferred over the <see cref="TextReader"/> overload where the bytes are what the caller has. An
        /// XML document says what encoding it is in — through a byte-order mark, or the <c>encoding</c> of
        /// its declaration — and only the bytes carry that: decoding first settles the question before the
        /// declaration is ever read, so a document in <c>ISO-8859-1</c> has to be opened as such by whoever
        /// knew that out of band.
        /// </remarks>
        public void TransformXml(Stream xmlInput, TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            ArgumentNullException.ThrowIfNull(writer);

            Transform(XdmTreeBuilder.FromXmlPooled(xmlInput, m_stylesheet.Whitespace, m_options.EntityResolver, m_options.InputUri, InputValidation), writer);
        }

        /// <summary>
        /// Transforms an XML document, writing the result in the encoding the stylesheet declared.
        /// </summary>
        /// <param name="xmlInput">The XML to transform. The caller retains ownership and must dispose it.</param>
        /// <param name="output">Where to write the result. Left open, and flushed but not disposed.</param>
        /// <remarks>
        /// Writing to a stream is what makes <c>xsl:output encoding</c> mean anything: to a
        /// <see cref="TextWriter"/> the engine can only name an encoding in the XML declaration, not produce
        /// it, so the document may claim one thing and be another. <c>byte-order-mark</c> likewise becomes a
        /// real mark here rather than a U+FEFF character the writer may or may not turn into one.
        /// </remarks>
        public void TransformXml(Stream xmlInput, Stream output)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            ArgumentNullException.ThrowIfNull(output);

            Transform(XdmTreeBuilder.FromXmlPooled(xmlInput, m_stylesheet.Whitespace, m_options.EntityResolver, m_options.InputUri, InputValidation), output);
        }

        /// <summary>
        /// Transforms an XML document, writing the result in the encoding the stylesheet declared.
        /// </summary>
        /// <param name="xmlInput">The XML to transform. The caller retains ownership and must dispose it.</param>
        /// <param name="output">Where to write the result. Left open, and flushed but not disposed.</param>
        public void TransformXml(TextReader xmlInput, Stream output)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            ArgumentNullException.ThrowIfNull(output);

            Transform(XdmTreeBuilder.FromXmlPooled(xmlInput, m_stylesheet.Whitespace, m_options.EntityResolver, m_options.InputUri, InputValidation), output);
        }

        /// <summary>
        /// Transforms an XML document, writing the result in the encoding the stylesheet declared.
        /// </summary>
        /// <param name="xmlInput">The XML to transform.</param>
        /// <param name="output">Where to write the result. Left open, and flushed but not disposed.</param>
        public void TransformXml(string xmlInput, Stream output)
        {
            ArgumentNullException.ThrowIfNull(xmlInput);
            ArgumentNullException.ThrowIfNull(output);

            Transform(ParseXmlText(xmlInput), output);
        }


        public string TransformJson(string jsonInput)
        {
            ArgumentNullException.ThrowIfNull(jsonInput);

            using PooledStringWriter writer = new PooledStringWriter(jsonInput.Length);
            Transform(JsonTreeBuilder.FromJsonPooled(jsonInput), writer);
            return writer.Finish();
        }

        public void TransformJson(string jsonInput, TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(jsonInput);
            ArgumentNullException.ThrowIfNull(writer);

            Transform(JsonTreeBuilder.FromJsonPooled(jsonInput), writer);
        }

        public void TransformJson(TextReader jsonInput, TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(jsonInput);
            ArgumentNullException.ThrowIfNull(writer);

            Transform(JsonTreeBuilder.FromJsonPooled(jsonInput), writer);
        }

        /// <summary>
        /// Transforms a JSON document read from a stream.
        /// </summary>
        /// <param name="jsonInput">The JSON to transform. The caller retains ownership and must dispose it.</param>
        /// <param name="writer">Where to write the result.</param>
        /// <remarks>
        /// Preferred over the text overloads where the bytes are what the caller has. JSON is UTF-8 by
        /// definition, so handing over a string means decoding the whole document and encoding it back to
        /// arrive where it started — this engine parses the bytes.
        /// </remarks>
        public void TransformJson(Stream jsonInput, TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(jsonInput);
            ArgumentNullException.ThrowIfNull(writer);

            Transform(JsonTreeBuilder.FromJsonPooled(jsonInput), writer);
        }

        /// <summary>
        /// Transforms a JSON document, writing the result in the encoding the stylesheet declared.
        /// </summary>
        /// <param name="jsonInput">The JSON to transform. The caller retains ownership and must dispose it.</param>
        /// <param name="output">Where to write the result. Left open, and flushed but not disposed.</param>
        /// <remarks>
        /// Writing to a stream is what makes <c>xsl:output encoding</c> mean anything; see
        /// <see cref="TransformXml(Stream, Stream)"/>.
        /// </remarks>
        public void TransformJson(Stream jsonInput, Stream output)
        {
            ArgumentNullException.ThrowIfNull(jsonInput);
            ArgumentNullException.ThrowIfNull(output);

            Transform(JsonTreeBuilder.FromJsonPooled(jsonInput), output);
        }

        /// <summary>
        /// Transforms a JSON document, writing the result in the encoding the stylesheet declared.
        /// </summary>
        /// <param name="jsonInput">The JSON to transform. The caller retains ownership and must dispose it.</param>
        /// <param name="output">Where to write the result. Left open, and flushed but not disposed.</param>
        public void TransformJson(TextReader jsonInput, Stream output)
        {
            ArgumentNullException.ThrowIfNull(jsonInput);
            ArgumentNullException.ThrowIfNull(output);

            Transform(JsonTreeBuilder.FromJsonPooled(jsonInput), output);
        }

        /// <summary>
        /// Transforms a JSON document, writing the result in the encoding the stylesheet declared.
        /// </summary>
        /// <param name="jsonInput">The JSON to transform.</param>
        /// <param name="output">Where to write the result. Left open, and flushed but not disposed.</param>
        public void TransformJson(string jsonInput, Stream output)
        {
            ArgumentNullException.ThrowIfNull(jsonInput);
            ArgumentNullException.ThrowIfNull(output);

            Transform(JsonTreeBuilder.FromJsonPooled(jsonInput), output);
        }

        /// <summary>
        /// Runs the transformation, writing bytes in the encoding the stylesheet declared.
        /// </summary>
        /// <remarks>
        /// The reason a stream overload exists at all. Writing to a <see cref="TextWriter"/> the engine has
        /// no say in the bytes, so <c>xsl:output encoding="ISO-8859-1"</c> reaches nothing but the text of
        /// the XML declaration and the document ends up claiming an encoding its own bytes contradict. Here
        /// the engine builds the writer, so the declaration is true and <c>byte-order-mark</c> is a mark
        /// rather than a character that may or may not become one.
        /// </remarks>
        private void Transform(XdmTree input, Stream stream)
        {
            OutputSettings declared = m_stylesheet.OutputSettings.With(m_options.OmitXmlDeclaration);

            using StreamWriter writer = SerializationEncoding.CreateWriter(
                stream, declared, out OutputSettings settings);

            Transform(input, writer, settings);
        }

        /// <summary>Transforms a tree the caller already has, writing the result.</summary>
        /// <param name="input">The document to transform.</param>
        /// <param name="writer">Where to write the result.</param>
        /// <remarks>
        /// The other end of <see cref="TransformXmlToTree(string)"/>: a tree that came out of one
        /// transformation, or that the caller built, written out without being parsed from text first.
        /// </remarks>
        public void Transform(XdmTree input, TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(writer);

            Transform(input, writer, m_stylesheet.OutputSettings.With(m_options.OmitXmlDeclaration));
        }

        private void Transform(XdmTree input, TextWriter writer, OutputSettings settings, bool hasSource = true)
        {
            try
            {
                TransformCore(input, writer, settings, hasSource);
            }
            finally
            {
                // The input was built for this run and nothing can hold a node of it once the run is over,
                // so its storage goes back to the pool it came from — a no-op for a tree built any other way.
                input.ReleaseStorage();
            }
        }

        private void TransformCore(XdmTree input, TextWriter writer, OutputSettings settings, bool hasSource)
        {
            // The json and adaptive methods write values rather than a tree: the result is gathered as the
            // sequence it is, and written once the transformation has produced all of it.
            if (settings.Method is OutputMethod.Json or OutputMethod.Adaptive)
            {
                SequenceCaptureTarget capture = new SequenceCaptureTarget();

                XsltRuntime gathering = new XsltRuntime(m_stylesheet, input, capture, m_options, hasSource)
                {
                    MessageWriter = m_options.MessageWriter,
                    WarningWriter = m_options.WarningWriter,
                };

                gathering.Run();
                writer.Write(Serializer.Text(XdmSequence.Items(capture.Finish()), settings));
                return;
            }

            OutputWriter output = new OutputWriter(writer, settings);

            XsltRuntime runtime = new XsltRuntime(m_stylesheet, input, output, m_options, hasSource)
            {
                MessageWriter = m_options.MessageWriter,
                WarningWriter = m_options.WarningWriter,
            };

            runtime.Run();
            output.Flush();
        }
    }
}
