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
                };

                gathering.Run();
                writer.Write(Serializer.Text(XdmSequence.Items(capture.Finish()), settings));
                return;
            }

            OutputWriter output = new OutputWriter(writer, settings);

            XsltRuntime runtime = new XsltRuntime(m_stylesheet, input, output, m_options, hasSource)
            {
                MessageWriter = m_options.MessageWriter,
            };

            runtime.Run();
            output.Flush();
        }
    }
}
