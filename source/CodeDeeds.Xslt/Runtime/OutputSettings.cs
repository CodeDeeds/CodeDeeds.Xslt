namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// The serialization options declared by <c>xsl:output</c>.
    /// </summary>
    public sealed class OutputSettings
    {
        /// <summary>The defaults that apply when a stylesheet declares no <c>xsl:output</c>.</summary>
        public static OutputSettings Default { get; } = new OutputSettings();

        /// <summary>Gets or sets the serialization method.</summary>
        public OutputMethod Method { get; set; } = OutputMethod.Xml;

        /// <summary>
        /// Gets or sets whether the stylesheet named a method, rather than leaving it to be inferred.
        /// </summary>
        /// <remarks>
        /// XSLT infers <c>html</c> when the result's document element is <c>html</c> in no namespace, and
        /// <c>xml</c> otherwise. The distinction has to be kept because "not stated" and "stated as xml" lead
        /// to different results for an HTML document.
        /// </remarks>
        public bool MethodSpecified { get; set; }

        /// <summary>
        /// Gets or sets whether the stylesheet named an indent setting, rather than leaving it to the method.
        /// </summary>
        /// <remarks>
        /// Indenting defaults to on for the HTML method and off for XML, so an unstated value is not simply
        /// <see langword="false"/>.
        /// </remarks>
        public bool IndentSpecified { get; set; }

        /// <summary>
        /// Gets or sets whether the XML declaration is suppressed.
        /// </summary>
        /// <remarks>
        /// Defaults to <see langword="false"/>, matching the specification's default of
        /// <c>omit-xml-declaration="no"</c>: a document produced with the XML method carries a declaration
        /// unless the stylesheet says otherwise. Whether a particular run wants one usually depends on what the
        /// caller is doing with the result rather than on the stylesheet, which is what
        /// <see cref="Xslt.OmitXmlDeclaration"/> is for.
        /// </remarks>
        public bool OmitXmlDeclaration { get; set; }

        /// <summary>Gets or sets whether elements are indented for readability.</summary>
        public bool Indent { get; set; }

        /// <summary>
        /// Gets or sets the encoding the result is written in, and names itself as.
        /// </summary>
        /// <remarks>
        /// It reaches the bytes only where this engine writes them, which is where the caller transforms to
        /// a <see cref="Stream"/>. To a <see cref="TextWriter"/> it reaches nothing but the text of the XML
        /// declaration, so a result can claim an encoding its own bytes do not support.
        /// </remarks>
        public string Encoding { get; set; } = "UTF-8";

        /// <summary>Gets or sets the version named in the XML declaration.</summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// Gets or sets the value of the XML declaration's <c>standalone</c> pseudo-attribute, or
        /// <see langword="null"/> to leave it out.
        /// </summary>
        public bool? Standalone { get; set; }

        /// <summary>
        /// Gets or sets the media type of the result.
        /// </summary>
        /// <remarks>
        /// Carried rather than acted on: it describes the result to whatever consumes it — a
        /// <c>Content-Type</c> header, most often — and does not change the bytes produced. Read it from
        /// <see cref="Xslt.OutputMediaType"/>, which falls back to the type implied by the method when the
        /// stylesheet names none.
        /// </remarks>
        public string? MediaType { get; set; }

        /// <summary>
        /// Gets the media type to report, falling back to the one implied by <see cref="Method"/>.
        /// </summary>
        public string EffectiveMediaType => MediaTypeFor(Method);

        /// <summary>
        /// Gets the media type to report for one serialization method.
        /// </summary>
        /// <remarks>
        /// Taken as a parameter because a stylesheet that names no method leaves the choice to the result's
        /// own document element, and the serializer settles it. Asking these settings would give the method
        /// that was never stated rather than the one in force.
        /// </remarks>
        /// <param name="method">The method actually being used.</param>
        public string MediaTypeFor(OutputMethod method) => MediaType ?? method switch
        {
            // text/html for XHTML as well as HTML. The other type an XHTML document may be served as is
            // application/xhtml+xml, but this value is what goes into the Content-Type meta, and a meta is
            // an HTML mechanism that a browser reading the page as XHTML never consults.
            OutputMethod.Html or OutputMethod.Xhtml => "text/html",
            OutputMethod.Text => "text/plain",
            _ => "text/xml",
        };

        /// <summary>
        /// Gets or sets whether an unstated method may be inferred as <c>xhtml</c>.
        /// </summary>
        /// <remarks>
        /// True everywhere except the implicit result tree of a stylesheet whose outermost element declares
        /// <c>version="1.0"</c>. XSLT 1.0 had no XHTML method and no <c>meta</c> of its own to add, so a
        /// stylesheet written against it that happens to produce an <c>html</c> element in the XHTML
        /// namespace gets the XML method it would have got then — the escaping and the Content-Type meta
        /// would both be things it never asked for. An <c>xsl:result-document</c> is not the implicit tree
        /// and so is not covered.
        /// </remarks>
        public bool MayInferXhtml { get; set; } = true;

        /// <summary>Gets or sets the public identifier of the emitted document type declaration.</summary>
        /// <remarks>
        /// An empty string is no identifier rather than an empty one, which is how a stylesheet takes
        /// back a document type declaration an imported one asked for (serialization erratum E31).
        /// Writing an empty system identifier would produce a declaration pointing at the document
        /// itself, which is not what any stylesheet writing it means.
        /// </remarks>
        public string? DoctypePublic
        {
            get => m_doctypePublic;
            set => m_doctypePublic = string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>Gets or sets the system identifier of the emitted document type declaration.</summary>
        /// <remarks>Empty is none, as it is for the public identifier above.</remarks>
        public string? DoctypeSystem
        {
            get => m_doctypeSystem;
            set => m_doctypeSystem = string.IsNullOrEmpty(value) ? null : value;
        }

        private string? m_doctypePublic;
        private string? m_doctypeSystem;

        /// <summary>
        /// Gets or sets which version of HTML the <c>html</c> and <c>xhtml</c> methods are writing.
        /// </summary>
        /// <remarks>
        /// A number rather than a flag because the specification writes it as one, and because <c>5</c>,
        /// <c>5.0</c> and <c>5.00</c> have to mean the same thing. Only the step to 5 changes anything here:
        /// HTML 5 replaced the document type declaration with a bare <c>&lt;!DOCTYPE html&gt;</c> and settled
        /// which elements are empty by name rather than by DTD, which is what the conformance notes
        /// (<c>ConformanceNotes.md</c>) describe.
        /// </remarks>
        public decimal? HtmlVersion { get; set; }

        /// <summary>Gets or sets whether a version was named, as against being left at 1.0.</summary>
        /// <remarks>
        /// Which matters to the <c>html</c> and <c>xhtml</c> methods, where §7.4.1 makes the version
        /// parameter the requested HTML version whenever <c>html-version</c> is absent: a result that named
        /// no version at all has not thereby asked for HTML 1.0.
        /// </remarks>
        public bool VersionSpecified { get; set; }

        /// <summary>Gets or sets whether the result undeclares a namespace prefix where it may.</summary>
        /// <remarks>
        /// Only XML 1.1 has a syntax for undeclaring one, and this serializer writes 1.0, so nothing acts
        /// on it. It is kept because asking for it in 1.0 is an error the serializer is required to signal
        /// (§5.1.8, <c>SEPM0010</c>), and that question cannot be asked without the answer.
        /// </remarks>
        public bool UndeclarePrefixes { get; set; }

        /// <summary>Gets or sets whether the caller, rather than the stylesheet, omitted the declaration.</summary>
        /// <remarks>
        /// <c>XsltOptions.OmitXmlDeclaration</c> overrides whatever the stylesheet asked for, and a host
        /// application that asks for no declaration is not asking for two incompatible things: it is
        /// overruling one of them. So the conflict §5.1.6 names — omitting the declaration while
        /// asking for a standalone that only the declaration can carry — is the stylesheet's to make,
        /// and this says when it was not made here.
        /// </remarks>
        public bool OmitXmlDeclarationOverridden { get; set; }

        /// <summary>Gets or sets the elements whose content is never indented, however indent is set.</summary>
        public List<(string NamespaceUri, string LocalName)> SuppressIndentation { get; } = new();

        /// <summary>
        /// Gets or sets what is written between two adjacent items of a sequence.
        /// </summary>
        /// <remarks>
        /// A single space unless said otherwise, which is what atomization does. It is on the serializer
        /// rather than on the instruction because it is a property of the result document: the same sequence
        /// written to two result documents may be separated differently in each.
        /// </remarks>
        public string? ItemSeparator { get; set; }

        /// <summary>
        /// Gets or sets whether the result is built as a tree, or serialized as the sequence it is — each
        /// item on its own, the item separator between them. Null leaves it to the method, which for xml,
        /// html, xhtml and text is a tree.
        /// </summary>
        public bool? BuildTree { get; set; }

        /// <summary>
        /// Gets the elements whose text content is written as CDATA sections rather than escaped, as
        /// (namespace URI, local name) pairs.
        /// </summary>
        public List<(string NamespaceUri, string LocalName)> CDataSectionElements { get; } = new();

        /// <summary>
        /// Gets or sets whether a byte order mark begins the result.
        /// </summary>
        /// <remarks>
        /// Written as bytes where the result goes to a <see cref="Stream"/>, this engine building the writer
        /// and so knowing what the mark is. To a <see cref="TextWriter"/> it is written as the character
        /// U+FEFF instead and left to the writer's encoding, which turns it into the usual three bytes for
        /// UTF-8 and into whatever it turns it into otherwise.
        /// </remarks>
        public bool ByteOrderMark { get; set; }

        /// <summary>
        /// Gets or sets the Unicode normalization applied to the result, or <see langword="null"/> for none.
        /// </summary>
        public System.Text.NormalizationForm? NormalizationForm { get; set; }

        /// <summary>
        /// Gets or sets whether the HTML and XHTML methods put a <c>Content-Type</c> meta into the head.
        /// </summary>
        /// <remarks>
        /// On by default, as the specification has it. A page saved to disk and opened from there arrives
        /// with no <c>Content-Type</c> header, so the encoding it was written in would be a guess; the meta
        /// is where a serializer gets to say. It applies to nothing but the HTML and XHTML methods, an XML
        /// document having its declaration to say the same thing.
        /// </remarks>
        public bool IncludeContentType { get; set; } = true;

        /// <summary>
        /// Gets or sets whether the HTML and XHTML methods percent-escape the URI-valued attributes they
        /// write.
        /// </summary>
        /// <remarks>
        /// On by default, as the specification has it. HTML lets an author write a URI with the characters
        /// of their own language in it, but a URI is a sequence of ASCII characters and what a browser sends
        /// is the escaped form; escaping here is what makes the page mean the same thing to a reader that
        /// does not do the escaping itself. It reaches the attributes HTML defines as URI-valued and no
        /// others, so an attribute holding text that merely looks like a URI is left alone.
        /// </remarks>
        public bool EscapeUriAttributes { get; set; } = true;

        /// <summary>
        /// Whether the json method may write two members of one name, where two keys of a map spell the
        /// same string; otherwise that is <c>SERE0022</c>.
        /// </summary>
        public bool AllowDuplicateNames { get; set; }

        /// <summary>The method the json method writes a node with, inside a JSON string.</summary>
        public OutputMethod JsonNodeOutputMethod { get; set; } = OutputMethod.Xml;

        /// <summary>
        /// Gets or sets the substitutions named by <c>use-character-maps</c>, or <see langword="null"/> when
        /// the stylesheet asked for none.
        /// </summary>
        /// <remarks>
        /// Already merged: several maps may be named, and what reaches here is the one map they amount to.
        /// </remarks>
        public CharacterMap? CharacterMap { get; set; }

        /// <summary>
        /// Returns these settings with the XML declaration suppressed or restored.
        /// </summary>
        /// <remarks>
        /// Returns a copy rather than mutating, because the settings a stylesheet was compiled with are shared
        /// by every transformation running against it, possibly on several threads at once.
        /// </remarks>
        /// <param name="omitXmlDeclaration">
        /// The override to apply, or <see langword="null"/> to keep what the stylesheet declared.
        /// </param>
        public OutputSettings With(bool? omitXmlDeclaration)
        {
            if (omitXmlDeclaration is not bool omit || omit == OmitXmlDeclaration)
            {
                return this;
            }

            OutputSettings copy = new OutputSettings
            {
                Method = Method,
                MethodSpecified = MethodSpecified,
                OmitXmlDeclaration = omit,
                OmitXmlDeclarationOverridden = true,
                Indent = Indent,
                IndentSpecified = IndentSpecified,
                Encoding = Encoding,
                Version = Version,
                Standalone = Standalone,
                MediaType = MediaType,
                DoctypePublic = DoctypePublic,
                DoctypeSystem = DoctypeSystem,
                CharacterMap = CharacterMap,
                ByteOrderMark = ByteOrderMark,
                NormalizationForm = NormalizationForm,
                IncludeContentType = IncludeContentType,
                EscapeUriAttributes = EscapeUriAttributes,
                MayInferXhtml = MayInferXhtml,
                HtmlVersion = HtmlVersion,
                VersionSpecified = VersionSpecified,
                UndeclarePrefixes = UndeclarePrefixes,
                ItemSeparator = ItemSeparator,
                BuildTree = BuildTree,
                AllowDuplicateNames = AllowDuplicateNames,
                JsonNodeOutputMethod = JsonNodeOutputMethod,
            };

            copy.SuppressIndentation.AddRange(SuppressIndentation);
            copy.CDataSectionElements.AddRange(CDataSectionElements);
            return copy;
        }

        /// <summary>
        /// Returns an independent copy, so that a result document can differ from the principal one without
        /// changing it.
        /// </summary>
        public OutputSettings Copy()
        {
            OutputSettings copy = new OutputSettings
            {
                Method = Method,
                MethodSpecified = MethodSpecified,
                OmitXmlDeclaration = OmitXmlDeclaration,
                Indent = Indent,
                IndentSpecified = IndentSpecified,
                Encoding = Encoding,
                Version = Version,
                Standalone = Standalone,
                MediaType = MediaType,
                DoctypePublic = DoctypePublic,
                DoctypeSystem = DoctypeSystem,
                CharacterMap = CharacterMap,
                ByteOrderMark = ByteOrderMark,
                NormalizationForm = NormalizationForm,
                IncludeContentType = IncludeContentType,
                EscapeUriAttributes = EscapeUriAttributes,
                MayInferXhtml = MayInferXhtml,
                HtmlVersion = HtmlVersion,
                VersionSpecified = VersionSpecified,
                UndeclarePrefixes = UndeclarePrefixes,
                OmitXmlDeclarationOverridden = OmitXmlDeclarationOverridden,
                ItemSeparator = ItemSeparator,
                BuildTree = BuildTree,
                AllowDuplicateNames = AllowDuplicateNames,
                JsonNodeOutputMethod = JsonNodeOutputMethod,
            };

            copy.SuppressIndentation.AddRange(SuppressIndentation);
            copy.CDataSectionElements.AddRange(CDataSectionElements);
            return copy;
        }

        /// <summary>Returns whether an element's text content should be written as a CDATA section.</summary>
        /// <param name="namespaceUri">The element's namespace URI.</param>
        /// <param name="localName">The element's local name.</param>
        public bool IsCDataSection(string namespaceUri, string localName)
        {
            foreach ((string uri, string name) in CDataSectionElements)
            {
                if (string.Equals(uri, namespaceUri, StringComparison.Ordinal)
                    && string.Equals(name, localName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
