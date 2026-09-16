using System.Text;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>The serialization methods of <c>xsl:output</c>.</summary>
    public enum OutputMethod : byte
    {
        /// <summary>Well-formed XML.</summary>
        Xml = 0,

        /// <summary>HTML, with void elements left unclosed and script content unescaped.</summary>
        Html = 1,

        /// <summary>Text nodes only, with no markup and no escaping.</summary>
        Text = 2,

        /// <summary>
        /// XML that an HTML browser can read: well-formed, but with the empty-element form chosen to suit
        /// HTML's parsing rather than XML's.
        /// </summary>
        /// <remarks>
        /// The whole of the difference from <see cref="Xml"/> is which empty elements collapse. A browser
        /// reading <c>&lt;p/&gt;</c> as HTML sees an unclosed paragraph, and one reading
        /// <c>&lt;br&gt;&lt;/br&gt;</c> sees two line breaks — so the elements HTML calls empty are written
        /// <c>&lt;br /&gt;</c>, with the space old parsers needed, and every other element gets both tags.
        /// </remarks>
        Xhtml = 3,

        /// <summary>JSON, for a result that is a map, an array or an atomic value (Serialization 3.1 §9).</summary>
        Json = 4,

        /// <summary>Whatever the result is, in a form meant to be read by a person (Serialization 3.1 §10).</summary>
        Adaptive = 5,
    }

    /// <summary>
    /// Serializes the result of a transformation.
    /// </summary>
    /// <remarks>
    /// Writes directly to a <see cref="TextWriter"/> and performs its own escaping rather than going through
    /// <see cref="System.Xml.XmlWriter"/>, which would add a layer of validation and buffering that a
    /// transformation's output does not need — the instructions that drive it already produce a well-ordered
    /// element stream.
    /// <para>
    /// Namespace declarations are emitted lazily: a prefix is declared only when the binding it needs is not
    /// already in scope, so repeated elements in the same namespace declare it once on their common ancestor.
    /// </para>
    /// </remarks>
    public sealed class OutputWriter : OutputTarget
    {
        private static readonly HashSet<string> s_htmlVoidElements = new(StringComparer.OrdinalIgnoreCase)
        {
            "area", "base", "basefont", "br", "col", "embed", "frame", "hr", "img", "input",
            "isindex", "link", "meta", "param", "source", "track", "wbr",
        };

        /// <summary>
        /// Whether a name is one of HTML's void elements, remembered by the name string itself. A literal
        /// result element hands the writer the same string instance every time it is written, so after the
        /// first time the answer is one reference lookup rather than a case-insensitive hash of the name,
        /// which was taken twice for every element of an HTML result. Bounded, because a computed name
        /// could be different every time.
        /// </summary>
        private readonly Dictionary<string, bool> m_htmlVoidByName = new(ReferenceEqualityComparer.Instance);

        private bool IsHtmlVoid(string localName)
        {
            if (m_htmlVoidByName.TryGetValue(localName, out bool isVoid))
            {
                return isVoid;
            }

            isVoid = s_htmlVoidElements.Contains(localName);

            if (m_htmlVoidByName.Count < 256)
            {
                m_htmlVoidByName[localName] = isVoid;
            }

            return isVoid;
        }

        private static readonly HashSet<string> s_htmlUnescapedElements = new(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style",
        };

        /// <summary>
        /// The attributes HTML defines as holding a URI, as <c>element/attribute</c>.
        /// </summary>
        /// <remarks>
        /// Named by the pair rather than by the attribute alone, because whether a name holds a URI is a
        /// property of the element it is on: <c>a/@href</c> is one and <c>base/@href</c> is one, while
        /// <c>script/@for</c> is and every other <c>for</c> is not. Escaping an attribute that merely looks
        /// like a URI would corrupt it.
        /// </remarks>
        private static readonly HashSet<string> s_uriAttributes = new(StringComparer.OrdinalIgnoreCase)
        {
            "a/href", "applet/codebase", "area/href", "base/href", "blockquote/cite", "body/background",
            "del/cite", "form/action", "frame/longdesc", "frame/src", "head/profile", "iframe/longdesc",
            "iframe/src", "img/longdesc", "img/src", "img/usemap", "input/src", "input/usemap", "ins/cite",
            "link/href", "object/archive", "object/classid", "object/codebase", "object/data",
            "object/usemap", "q/cite", "script/for", "script/src",
        };

        /// <summary>
        /// The attribute names that appear in <see cref="s_uriAttributes"/> at all, asked first: nearly
        /// every attribute written — class, id, style — is none of them, and the pair need only be spelled
        /// out, which makes a string, for the few that are.
        /// </summary>
        private static readonly HashSet<string> s_uriAttributeNames = new(
            s_uriAttributes.Select(pair => pair[(pair.IndexOf('/') + 1)..]),
            StringComparer.OrdinalIgnoreCase);

        /// <summary>The namespace the XHTML method's rules apply to, and only to.</summary>
        private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";

        /// <summary>
        /// The three namespaces HTML 5 writes without a prefix.
        /// </summary>
        /// <remarks>
        /// An HTML 5 parser recognises <c>&lt;svg&gt;</c> and <c>&lt;math&gt;</c> by name and puts them in
        /// their namespaces itself, having no general namespace mechanism to do it with. So these three are
        /// the ones a document may contain and still be HTML, and writing any of them with a prefix produces
        /// a document that parses as XML and not as HTML.
        /// </remarks>
        private static readonly string[] s_html5Namespaces =
        {
            XhtmlNamespace,
            "http://www.w3.org/2000/svg",
            "http://www.w3.org/1998/Math/MathML",
        };

        /// <summary>The namespace the <c>xml</c> prefix is bound to, everywhere and without declaring it.</summary>
        private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

        /// <summary>
        /// The elements the XHTML method may write as <c>&lt;br /&gt;</c>.
        /// </summary>
        /// <remarks>
        /// HTML 4's empty elements, and deliberately not the HTML 5 additions that
        /// <see cref="s_htmlVoidElements"/> also carries. The XHTML method is defined against a fixed list,
        /// and collapsing an element outside it produces markup an HTML parser reads as still open.
        /// </remarks>
        private static readonly HashSet<string> s_xhtmlEmptyElements = new(StringComparer.Ordinal)
        {
            "area", "base", "basefont", "br", "col", "frame", "hr", "img", "input",
            "isindex", "link", "meta", "param",
        };

        private TextWriter m_writer;
        private readonly List<string> m_elementNames = new();
        private readonly List<string> m_elementLocalNames = new();
        private readonly List<string> m_elementNamespaces = new();
        private readonly List<(string Prefix, string Uri)> m_namespaces = new();
        private readonly List<int> m_namespaceMarks = new();
        private readonly List<ElementNamespaces> m_elementNamespaceState = new();

        /// <summary>
        /// The name of the element whose start tag has been opened and not yet written out, and the
        /// namespaces declared on it, held until the tag is closed.
        /// </summary>
        /// <remarks>
        /// Held because a namespace node added by <c>xsl:namespace</c> can take the prefix the element's own
        /// name was written with, and the answer to that is to give the element another prefix rather than to
        /// refuse the stylesheet (XSLT 3.0 §11.7). Nothing else goes between the name and the first attribute,
        /// so holding both costs one string and a short list per open element.
        /// </remarks>
        private string? m_pendingName;

        private readonly List<(string Prefix, string Uri)> m_pendingNamespaces = new();
        private readonly List<PendingAttribute> m_pendingAttributes = new();
        private bool m_startTagOpen;
        private int m_generatedPrefixCount;

        /// <summary>The depth of an open <c>head</c> still owing a Content-Type meta, or -1 for none.</summary>
        private int m_contentTypeDepth = -1;

        /// <summary>
        /// The depth of the outermost open element named by <c>suppress-indentation</c>, or -1 for none.
        /// </summary>
        /// <remarks>
        /// The outermost, so that leaving a nested one does not resume indenting inside the one still open.
        /// </remarks>
        private int m_suppressDepth = -1;

        /// <summary>
        /// The depth of a <c>head</c> whose Content-Type meta this serializer has already written, or -1.
        /// </summary>
        /// <remarks>
        /// The serialization rules say the serializer's meta <em>replaces</em> a Content-Type meta already
        /// in the result rather than joining it, so a page saying what encoding it was authored in does not
        /// end up claiming two. This is what says a <c>meta</c> arriving now is a candidate for that.
        /// </remarks>
        private int m_contentTypeWritten = -1;

        /// <summary>Where a held element is being written, or null when nothing is held.</summary>
        private StringWriter? m_held;

        /// <summary>The depth of the held element, or -1 when nothing is held.</summary>
        private int m_heldDepth = -1;

        /// <summary>The real destination, kept while <see cref="m_writer"/> points at the hold.</summary>
        private TextWriter? m_destination;

        private readonly OutputSettings m_settings;
        private readonly CharacterMap? m_characterMap;
        private readonly List<bool> m_elementHasText = new();
        private readonly List<bool> m_elementHasChildElements = new();
        private bool m_prologWritten;

        /// <summary>
        /// Whether an item separator was named, in which case it goes between every top-level item of the
        /// result — a comment beside a number as much as two numbers — rather than only between adjacent
        /// atomic values, which is what a tree does with a single space.
        /// </summary>
        private readonly bool m_sequence;

        /// <summary>How many top-level items a sequence result has had written.</summary>
        private int m_itemsWritten;

        /// <summary>
        /// Whether a document with nothing in it still gets its XML declaration, which a result document
        /// does: it is a document, and an empty one is still one.
        /// </summary>
        internal bool DeclaresWhenEmpty { get; set; }
        private bool m_markWritten;

        /// <summary>The encoding the result must fit in, or null where it can hold anything.</summary>
        private readonly Encoding? m_limit;

        /// <summary>What has already been asked of that encoding, or null until something is.</summary>
        private Dictionary<string, bool>? m_encodable;

        /// <summary>Initializes a writer.</summary>
        /// <param name="writer">The destination. Not disposed by this class.</param>
        /// <param name="settings">The serialization options.</param>
        public OutputWriter(TextWriter writer, OutputSettings? settings = null)
        {
            m_writer = writer;
            m_settings = settings ?? OutputSettings.Default;
            m_characterMap = m_settings.CharacterMap;
            m_limit = LimitOf(m_settings.Encoding);
            m_method = m_settings.Method;
            m_methodDecided = m_settings.MethodSpecified;
            m_sequence = m_settings.ItemSeparator is not null;

            if (m_methodDecided)
            {
                ApplyIndentDefault();
            }
        }

        /// <summary>
        /// Returns a writer over the same destination, serializing by different rules.
        /// </summary>
        /// <remarks>
        /// For <c>xsl:result-document</c> naming the base output URI, which is the destination the
        /// transformation's own result goes to but with the serialization that instruction asked for. Only
        /// one of the two may write anything — writing to both would be two result trees with one URI — so
        /// there is no question of two prologs.
        /// </remarks>
        /// <param name="settings">How the new writer is to serialize.</param>
        internal OutputWriter WithSettings(OutputSettings settings) => new OutputWriter(m_writer, settings);

        /// <summary>Initializes a writer with only a method specified.</summary>
        /// <param name="writer">The destination. Not disposed by this class.</param>
        /// <param name="method">The serialization method.</param>
        public OutputWriter(TextWriter writer, OutputMethod method)
            : this(writer, new OutputSettings { Method = method })
        {
        }

        private OutputMethod m_method;
        private bool m_methodDecided;
        private bool m_indent;

        /// <summary>Gets the serialization method in force.</summary>
        /// <remarks>
        /// Not settled until the first element is written when the stylesheet named no method, since the
        /// choice depends on what that element turns out to be.
        /// </remarks>
        public OutputMethod Method => m_method;

        /// <summary>
        /// Settles the method, and with it the indent default, from the result's document element.
        /// </summary>
        /// <remarks>
        /// XSLT picks the HTML method when the result's document element is <c>html</c> in no namespace,
        /// whatever its case, and from 2.0 the XHTML method when it is <c>html</c> in the XHTML namespace.
        /// It is why a stylesheet producing a web page needs no <c>xsl:output</c> to have its void elements
        /// written correctly. The XHTML test is case-sensitive where the HTML one is not, because XML names
        /// are.
        /// </remarks>
        private void DecideMethod(string namespaceUri, string localName)
        {
            m_methodDecided = true;

            if (namespaceUri.Length == 0 && string.Equals(localName, "html", StringComparison.OrdinalIgnoreCase))
            {
                m_method = OutputMethod.Html;
            }
            else if (namespaceUri == XhtmlNamespace && localName == "html" && m_settings.MayInferXhtml)
            {
                m_method = OutputMethod.Xhtml;
            }

            ApplyIndentDefault();
        }

        private void ApplyIndentDefault()
        {
            m_indent = m_settings.IndentSpecified ? m_settings.Indent : m_method == OutputMethod.Html;
        }

        /// <summary>Gets the depth of currently open elements.</summary>
        public int Depth => m_elementNames.Count;

        /// <inheritdoc/>
        public override int OpenElementDepth => m_elementNames.Count;

        /// <summary>
        /// Writes the start of an element. Attributes may be written until the first child appears.
        /// </summary>
        /// <param name="prefix">The preferred prefix, or an empty string.</param>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        public override void StartElement(string prefix, string namespaceUri, string localName)
        {
            m_lastWasAtomic = false;
            StartOutput();
            BeginItem();
            Touch();
            CloseStartTag();

            if (!m_methodDecided)
            {
                DecideMethod(namespaceUri, localName);
            }

            if (m_contentTypeWritten >= 0 && m_heldDepth < 0 && IsHtmlMeta(namespaceUri, localName))
            {
                // Held rather than written, because whether this element survives depends on an attribute
                // that has not arrived yet. Its indentation is inside the hold too, so a meta that is
                // dropped takes the whitespace in front of it with it.
                m_heldDepth = m_elementNames.Count + 1;
                m_held = new StringWriter();
                m_destination = m_writer;
                m_writer = m_held;
            }

            if (Method == OutputMethod.Text)
            {
                // The text method emits character data only; elements contribute nothing themselves. The
                // bookkeeping still has to balance, because EndElement pops all of it.
                m_elementNames.Add(localName);
                m_elementLocalNames.Add(localName);
                m_elementNamespaces.Add(namespaceUri);
                m_elementHasText.Add(false);
                m_elementHasChildElements.Add(false);
                m_namespaceMarks.Add(m_namespaces.Count);
                m_elementNamespaceState.Add(default);
                return;
            }

            WriteProlog(prefix, localName, namespaceUri);
            WriteIndentBeforeChild();

            m_namespaceMarks.Add(m_namespaces.Count);

            string effectivePrefix = ChoosePrefix(prefix, namespaceUri, forAttribute: false, out bool declare);
            string qualifiedName = Qualify(effectivePrefix, localName);

            // An element written without a prefix binds the default namespace by its own name, and nothing
            // may say otherwise; one written with a prefix takes whatever default it inherits.
            string inherited = m_elementNamespaceState.Count == 0
                ? string.Empty
                : m_elementNamespaceState[^1].NoInherit
                    ? string.Empty
                    : m_elementNamespaceState[^1].Default;

            bool ownsDefault = effectivePrefix.Length == 0;
            m_elementNamespaceState.Add(new ElementNamespaces
            {
                Inherited = inherited,
                Default = ownsDefault ? namespaceUri : inherited,
                OwnsDefault = ownsDefault,
            });

            m_pendingName = qualifiedName;
            m_elementNames.Add(qualifiedName);

            if (declare)
            {
                WriteNamespaceDeclarationCore(effectivePrefix, namespaceUri);
            }


            m_elementLocalNames.Add(localName);
            m_elementNamespaces.Add(namespaceUri);
            m_elementHasText.Add(false);
            m_elementHasChildElements.Add(false);
            m_startTagOpen = true;

            if (m_settings.IncludeContentType && IsHtmlHead(namespaceUri, localName))
            {
                m_contentTypeDepth = m_elementNames.Count;
            }

            if (m_suppressDepth < 0 && m_settings.SuppressIndentation.Contains((namespaceUri, localName)))
            {
                m_suppressDepth = m_elementNames.Count;
            }
        }

        /// <summary>
        /// Whether this element could be a <c>meta</c> the serializer's own Content-Type replaces.
        /// </summary>
        /// <remarks>
        /// Only inside a head this serializer is writing a Content-Type for. Elsewhere a <c>meta</c> is an
        /// ordinary element and nothing about it needs holding back.
        /// </remarks>
        private bool IsHtmlMeta(string namespaceUri, string localName)
        {
            // The depth is -1 everywhere but inside a head this serializer has written a meta for, which is
            // the cheapest way to say "not here" for the elements of every other document.
            if (m_contentTypeWritten != m_elementNames.Count)
            {
                return false;
            }

            return Method switch
            {
                OutputMethod.Html => string.Equals(localName, "meta", StringComparison.OrdinalIgnoreCase),
                OutputMethod.Xhtml => localName == "meta" && namespaceUri == XhtmlNamespace,
                _ => false,
            };
        }

        /// <summary>Whether this element is the <c>head</c> the Content-Type meta belongs in.</summary>
        private bool IsHtmlHead(string namespaceUri, string localName)
        {
            return Method switch
            {
                // HTML is case-insensitive about its element names and has no namespaces.
                OutputMethod.Html => string.Equals(localName, "head", StringComparison.OrdinalIgnoreCase),
                OutputMethod.Xhtml => localName == "head" && namespaceUri == XhtmlNamespace,
                _ => false,
            };
        }

        /// <summary>
        /// Writes a document type declaration's identifier in quotes it does not itself contain.
        /// </summary>
        /// <remarks>
        /// Neither identifier may be escaped — a document type declaration is not markup that character
        /// references reach — so the only thing a serializer may vary is which quote it uses. A system
        /// identifier may hold an apostrophe or a quotation mark, and XML allows either delimiter for that
        /// reason. One holding both cannot be written at all, and is left in double quotes to be refused by
        /// whatever reads the result rather than silently altered here.
        /// </remarks>
        /// <param name="identifier">The public or system identifier.</param>
        private void WriteQuoted(string identifier)
        {
            char quote = identifier.Contains('"') && !identifier.Contains('\'') ? '\'' : '"';

            m_writer.Write(quote);
            m_writer.Write(identifier);
            m_writer.Write(quote);
        }

        /// <summary>
        /// Writes the byte order mark, once, before anything else the result contains.
        /// </summary>
        /// <remarks>
        /// Not part of the prolog, which the text method does not have and which an element-less result never
        /// reaches: a mark says what encoding the bytes are in, and that is a question about the file rather
        /// than about the markup. So it is written ahead of whatever the first thing written turns out to be.
        /// <para>
        /// The character, not the bytes: what those turn out to be is the encoding's business, and the
        /// encoding belongs to whoever supplied the writer.
        /// </para>
        /// </remarks>
        /// <summary>
        /// Marks the start of a top-level item of a sequence result, writing the item separator before every
        /// one but the first. A tree result has no items to separate, and nothing happens.
        /// </summary>
        private void BeginItem()
        {
            if (!m_sequence || m_elementNames.Count != 0)
            {
                return;
            }

            if (m_itemsWritten++ > 0)
            {
                CloseStartTag();
                m_writer.Write(m_settings.ItemSeparator ?? " ");
            }
        }

        /// <inheritdoc/>
        public override bool TryAppendValue(XPath.XPathValue value)
        {
            // A map or a function item has no serialization under xml, html, xhtml or text, and the
            // specification makes writing one a serialization error rather than text of some kind. An array
            // is its members, which the caller writes one by one.
            foreach (XPath.XPathValue item in XPath.XdmSequence.ContentItems(value))
            {
                if (item.Kind is XPath.XPathValueKind.Map or XPath.XPathValueKind.Function)
                {
                    // At the top it is the serializer that has no way to write one; inside an element it is
                    // the content that has no place for one, which is the other code.
                    throw m_elementNames.Count == 0
                        ? XsltErrors.Error(
                            XsltErrorCode.SENR0001,
                            "A map or a function item cannot be serialized: the xml, html, xhtml and text "
                            + "output methods have no way to write one.")
                        : XsltErrors.Error(
                            XsltErrorCode.XTDE0450,
                            "A map or a function item cannot be written into the content of a node.");
                }
            }

            return false;
        }

        private void StartOutput()
        {
            if (m_markWritten)
            {
                return;
            }

            m_markWritten = true;

            if (m_settings.ByteOrderMark)
            {
                m_writer.Write('﻿');
            }
        }

        private bool m_declarationWritten;

        /// <summary>
        /// Comments, processing instructions and whitespace written at the top before the output method
        /// is known, held back so that the XML declaration can go ahead of them once it is.
        /// </summary>
        /// <remarks>
        /// With no method declared, the first element decides it — html for an html document element —
        /// and a comment before that element is allowed to precede it (§26). The declaration has to come
        /// first all the same, so what arrives before the decision waits for it.
        /// </remarks>
        private System.Text.StringBuilder? m_deferredTop;

        /// <summary>
        /// Whether something written at the top has to wait for the method decision: nothing has decided
        /// it, no declaration is out, and the output is a document rather than a separated sequence.
        /// </summary>
        private bool DefersAtTop =>
            m_elementNames.Count == 0 && !m_methodDecided && !m_declarationWritten && !m_sequence;

        /// <summary>
        /// Writes the XML declaration, once. Before the first element ordinarily, and before a comment or
        /// processing instruction that comes first, since the declaration is the first thing in a document
        /// or it is not a declaration at all — provided the method is settled, a comment being no evidence
        /// of whether HTML is on its way.
        /// </summary>
        private void WriteXmlDeclaration()
        {
            if (m_declarationWritten || !m_methodDecided)
            {
                return;
            }

            m_declarationWritten = true;

            if (!m_settings.OmitXmlDeclaration && Method is OutputMethod.Xml or OutputMethod.Xhtml)
            {
                m_writer.Write($"<?xml version=\"{m_settings.Version}\" encoding=\"{m_settings.Encoding}\"");

                if (m_settings.Standalone is bool standalone)
                {
                    m_writer.Write(standalone ? " standalone=\"yes\"" : " standalone=\"no\"");
                }

                m_writer.Write("?>");

                if (m_indent)
                {
                    m_writer.Write('\n');
                }
            }

            // Whatever waited for the decision goes out now, after the declaration or in its place.
            if (m_deferredTop is not null)
            {
                m_writer.Write(m_deferredTop.ToString());
                m_deferredTop = null;
            }
        }

        /// <summary>
        /// Writes the XML declaration and document type declaration, once, before the first element.
        /// </summary>
        private void WriteProlog(string prefix, string localName, string namespaceUri)
        {
            if (m_prologWritten || Method == OutputMethod.Text)
            {
                return;
            }

            m_prologWritten = true;
            WriteXmlDeclaration();

            // HTML 5 replaced the document type declaration with a bare one that names no DTD, there being
            // no DTD to name. It is written only where the document element is html — a document whose root
            // is something else is not an HTML 5 document, and saying it is would be a lie about what
            // follows. An explicit doctype-system still wins: a stylesheet naming a DTD means that DTD.
            if (WritesHtml5Doctype(localName, namespaceUri))
            {
                // Named after the document element as it will actually be written: with the spelling it has,
                // so <HTML> gets <!DOCTYPE HTML>, and without the prefix that prefix normalization is about
                // to take off it. A declaration naming h:html and an element written as html would name
                // something the document does not contain.
                m_writer.Write("<!DOCTYPE ");
                m_writer.Write(NormalizesPrefix(namespaceUri) ? localName : Qualify(prefix, localName));
                m_writer.Write('>');

                if (m_indent)
                {
                    m_writer.Write('\n');
                }

                return;
            }

            // A public identifier without a system one is written by HTML, which has a document type it can
            // name on its own, and ignored by XML and XHTML, where the external subset a declaration points
            // at is the system identifier and there is nothing to point at without it.
            bool publicOnly = m_settings.DoctypeSystem is null;

            if (m_settings.DoctypePublic is null
                ? m_settings.DoctypeSystem is null
                : publicOnly && Method is not OutputMethod.Html)
            {
                return;
            }

            // The document type declaration names the document element, so it can only be written once that
            // element is known.
            m_writer.Write("<!DOCTYPE ");
            m_writer.Write(Qualify(prefix, localName));

            if (m_settings.DoctypePublic is not null)
            {
                m_writer.Write(" PUBLIC ");
                WriteQuoted(m_settings.DoctypePublic);

                if (m_settings.DoctypeSystem is not null)
                {
                    m_writer.Write(' ');
                    WriteQuoted(m_settings.DoctypeSystem);
                }
            }
            else
            {
                m_writer.Write(" SYSTEM ");
                WriteQuoted(m_settings.DoctypeSystem!);
            }

            m_writer.Write('>');

            if (m_indent)
            {
                m_writer.Write('\n');
            }
        }

        /// <summary>
        /// Writes a line break and indentation before a child element.
        /// </summary>
        /// <remarks>
        /// Only when the parent holds no text. Indenting an element that also contains text would change the
        /// document's meaning, since that whitespace is part of the content.
        /// </remarks>
        private void WriteIndentBeforeChild()
        {
            if (!m_indent || Method == OutputMethod.Text || m_elementNames.Count == 0)
            {
                return;
            }

            // Inside an element named by suppress-indentation, and inside anything below it: the whole point
            // is elements where inserted whitespace is not cosmetic, and a line break added inside a nested
            // span is as visible to the reader as one added directly.
            if (m_suppressDepth >= 0)
            {
                return;
            }

            if (m_elementHasText[^1])
            {
                return;
            }

            m_elementHasChildElements[^1] = true;
            m_writer.Write('\n');
            WriteIndent(m_elementNames.Count);
        }

        /// <summary>
        /// Writes two spaces per level of nesting, reusing cached strings for the depths a document actually
        /// reaches rather than building one per element.
        /// </summary>
        private void WriteIndent(int depth)
        {
            if (depth <= 0)
            {
                return;
            }

            if (depth < s_indents.Length)
            {
                m_writer.Write(s_indents[depth] ??= new string(' ', 2 * depth));
                return;
            }

            m_writer.Write(new string(' ', 2 * depth));
        }

        private static readonly string?[] s_indents = new string?[64];

        /// <summary>
        /// Writes an attribute on the element whose start tag is still open.
        /// </summary>
        /// <param name="prefix">The preferred prefix, or an empty string.</param>
        /// <param name="namespaceUri">The namespace URI, or an empty string for no namespace.</param>
        /// <param name="localName">The local part of the name.</param>
        /// <param name="value">The attribute value.</param>
        /// <exception cref="XsltException">No start tag is open.</exception>
        public override void WriteAttribute(string prefix, string namespaceUri, string localName, string value)
        {
            m_lastWasAtomic = false;
            if (Method == OutputMethod.Text)
            {
                return;
            }

            if (!m_startTagOpen)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0410,
                    $"An attribute ('{localName}') cannot be added after the element's content has started.");
            }

            // Attributes are held until the start tag closes rather than written as they arrive, because
            // setting one twice must replace it. That happens whenever an element overrides something an
            // attribute set supplied, and writing both would produce a document no parser will accept.
            for (int i = 0; i < m_pendingAttributes.Count; i++)
            {
                PendingAttribute existing = m_pendingAttributes[i];
                if (existing.LocalName == localName && existing.NamespaceUri == namespaceUri)
                {
                    // Overwriting moves the attribute to the end rather than keeping its original position,
                    // which is what the framework's processor does. Order carries no meaning in XML, but there
                    // is no reason to differ from it.
                    m_pendingAttributes.RemoveAt(i);
                    break;
                }
            }

            m_pendingAttributes.Add(new PendingAttribute(prefix, namespaceUri, localName, value));
        }

        /// <summary>Writes the attributes collected for the open start tag.</summary>
        /// <summary>Writes the held name and the declarations made on it, once nothing can change them.</summary>
        private void FlushStartTag()
        {
            if (m_pendingName is null)
            {
                return;
            }

            m_writer.Write('<');
            m_writer.Write(m_pendingName);
            m_pendingName = null;

            foreach ((string prefix, string uri) in m_pendingNamespaces)
            {
                WriteNamespaceDeclarationText(prefix, uri);
            }

            m_pendingNamespaces.Clear();
        }

        private void WritePendingAttributes()
        {
            FlushStartTag();

            foreach (PendingAttribute attribute in m_pendingAttributes)
            {
                string effectivePrefix = ChoosePrefix(
                    attribute.Prefix, attribute.NamespaceUri, forAttribute: true, out bool declare);

                if (declare)
                {
                    WriteNamespaceDeclarationCore(effectivePrefix, attribute.NamespaceUri);
                }

                m_writer.Write(' ');
                m_writer.Write(Qualify(effectivePrefix, attribute.LocalName));

                // A boolean attribute is written as its name alone. Its only allowed value is that name, so
                // checked="checked" says nothing the bare checked does not, and the minimized form is one of
                // the things the HTML output method exists to produce (XSLT 1.0 §16.2). XHTML is XML and
                // takes no such shortcut.
                if (IsMinimizedInHtml(attribute))
                {
                    continue;
                }

                m_writer.Write("=\"");

                // A character map is not applied to a URI attribute that has been escaped (Serialization
                // §11): what the escaping made is a URI, and mapping characters in it would unmake one. With
                // a map the normalization waits for it, the map applying to the characters as they stand.
                string value = ForUri(attribute, out bool uriEscaped);
                CharacterMap? map = uriEscaped ? null : m_characterMap;

                if (map is null && m_settings.NormalizationForm is System.Text.NormalizationForm attributeForm)
                {
                    value = value.Normalize(attributeForm);
                }

                WriteEscapedAttributeValue(value, map);
                m_writer.Write('"');
            }

            m_pendingAttributes.Clear();
        }

        /// <summary>
        /// Whether an attribute is one HTML writes as its name alone.
        /// </summary>
        /// <remarks>
        /// The value has to say what the name says, which is the definition of a boolean attribute:
        /// anything else written there is not the single allowed value, and writing it out is the honest
        /// answer. The names are HTML 4.01's together with the ones HTML 5 added — the same set either way,
        /// since a name that is not an attribute of the element at all will not be reached with its own
        /// name as a value.
        /// </remarks>
        private bool IsMinimizedInHtml(PendingAttribute attribute)
        {
            return Method == OutputMethod.Html
                && attribute.NamespaceUri.Length == 0
                && string.Equals(attribute.Value, attribute.LocalName, StringComparison.OrdinalIgnoreCase)
                && s_htmlBooleanAttributes.Contains(attribute.LocalName);
        }

        /// <summary>The attributes HTML defines as boolean, whose only allowed value is their own name.</summary>
        private static readonly HashSet<string> s_htmlBooleanAttributes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "allowfullscreen", "async", "autofocus", "autoplay", "checked", "compact", "controls",
                "declare", "default", "defer", "disabled", "formnovalidate", "hidden", "ismap", "itemscope",
                "loop", "multiple", "muted", "nohref", "noresize", "noshade", "novalidate", "nowrap",
                "open", "readonly", "required", "reversed", "scoped", "seamless", "selected",
                "typemustmatch",
            };

        private readonly record struct PendingAttribute(
            string Prefix,
            string NamespaceUri,
            string LocalName,
            string Value);

        /// <summary>
        /// Percent-escapes an attribute that holds a URI, and returns any other attribute unchanged.
        /// </summary>
        /// <remarks>
        /// Only for the HTML and XHTML methods, and only where the element is HTML's own: an <c>href</c> in
        /// some other vocabulary is that vocabulary's business. The escaping is applied before the value is
        /// written as an attribute, so what comes out of it is escaped again for the markup — a percent sign
        /// this produces is already a percent sign and needs nothing, and a character it leaves alone is
        /// protected the ordinary way.
        /// </remarks>
        /// <param name="attribute">The attribute about to be written.</param>
        /// <returns>The value to write.</returns>
        private string ForUri(PendingAttribute attribute, out bool escaped)
        {
            escaped = false;

            if (!m_settings.EscapeUriAttributes
                || Method is not (OutputMethod.Html or OutputMethod.Xhtml)
                || attribute.NamespaceUri.Length != 0
                || m_elementNamespaces.Count == 0)
            {
                return attribute.Value;
            }

            string element = m_elementNamespaces[^1] switch
            {
                "" when Method == OutputMethod.Html => m_elementLocalNames[^1],
                XhtmlNamespace => m_elementLocalNames[^1],
                _ => string.Empty,
            };

            escaped = element.Length != 0
                && s_uriAttributeNames.Contains(attribute.LocalName)
                && s_uriAttributes.Contains($"{element}/{attribute.LocalName}");
            return escaped ? EscapeUri(attribute.Value) : attribute.Value;
        }

        /// <summary>
        /// Escapes every character a URI may not carry, leaving printable ASCII alone.
        /// </summary>
        /// <remarks>
        /// The rule <c>fn:escape-html-uri</c> states: everything outside #x20 to #x7E becomes the
        /// percent-encoded UTF-8 bytes of that character, and everything inside it is left exactly as
        /// written. That is deliberately less than a URI grammar would ask for — a space, a quotation mark
        /// and an already-escaped <c>%20</c> all survive — because the value is what the author wrote and
        /// re-escaping an escape would change where the link points.
        /// </remarks>
        /// <param name="value">The attribute's value.</param>
        /// <returns>The escaped value, or the same string where nothing needed escaping.</returns>
        internal static string EscapeUri(string value)
        {
            int at = 0;
            while (at < value.Length && value[at] is >= ' ' and <= '~')
            {
                at++;
            }

            if (at == value.Length)
            {
                return value;
            }

            // Composed before it is escaped. A letter with an accent can be written as one character or as
            // the letter and a combining mark, and the two are the same letter but not the same bytes — so
            // escaping them as they came would give two different URIs for one address. Composing first is
            // what the conversion from an IRI to a URI is defined to do.
            value = Composed(value);

            StringBuilder escaped = new StringBuilder(value.Length + 16);
            byte[] bytes = new byte[4];
            at = 0;

            for (; at < value.Length; at++)
            {
                if (value[at] is >= ' ' and <= '~')
                {
                    escaped.Append(value[at]);
                    continue;
                }

                // A character outside the basic plane is written as one character in UTF-8's four bytes, so
                // the surrogate pair is encoded together rather than each half being escaped as itself.
                int length = char.IsHighSurrogate(value[at]) && at + 1 < value.Length
                    ? Encoding.UTF8.GetBytes(value.AsSpan(at, 2), bytes)
                    : Encoding.UTF8.GetBytes(value.AsSpan(at, 1), bytes);

                for (int i = 0; i < length; i++)
                {
                    escaped.Append('%').Append(bytes[i].ToString("X2"));
                }

                if (length == 4)
                {
                    at++;
                }
            }

            return escaped.ToString();
        }

        /// <summary>Normalization form C, or the text unchanged where it is not text that can be normalized.</summary>
        private static string Composed(string value)
        {
            try
            {
                return value.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                // An unpaired surrogate, which normalization refuses and escaping can still describe.
                return value;
            }
        }

        /// <summary>
        /// Declares a namespace on the element whose start tag is still open, unless the binding is already in
        /// scope, in which case the declaration would be redundant.
        /// </summary>
        /// <param name="prefix">The prefix being bound, or an empty string for the default namespace.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        public override void WriteNamespaceDeclaration(string prefix, string namespaceUri)
        {
            m_lastWasAtomic = false;
            if (Method == OutputMethod.Text || !m_startTagOpen)
            {
                return;
            }

            // Whether the element's own prefix had already been offered a binding, read before the note
            // below records this one, since this call is itself an offer.
            bool ownPrefixAlreadyOffered = m_elementNamespaceState[^1].OwnPrefixOffered;

            // Noted before the reasons it may not be written, because what the element's namespace nodes
            // are is a question about the data model and not about what the serializer does with them.
            {
                ElementNamespaces state = m_elementNamespaceState[^1];

                if (prefix.Length == 0 && !state.OwnsDefault)
                {
                    state.Default = namespaceUri;
                    state.Offered = true;
                }

                if (prefix.Length != 0 && prefix == PrefixOfOpenElement())
                {
                    state.OwnPrefixOffered = true;
                }

                m_elementNamespaceState[^1] = state;
            }

            // Prefix normalization removes these bindings rather than carrying them: an element in one of the
            // three namespaces is written without a prefix, so a prefix for it is a declaration nothing uses,
            // and the specification says to take it out rather than leave it standing. An attribute that does
            // need one has it written where the attribute is.
            if (prefix.Length != 0 && NormalizesPrefix(namespaceUri))
            {
                return;
            }

            if (string.Equals(LookupUri(prefix), namespaceUri, StringComparison.Ordinal))
            {
                return;
            }

            // A binding that would rebind the prefix the element's own name uses is dropped rather than
            // written: it would change what that name means when the result is read back. The data model
            // cannot produce such a node — an element's namespace node for its own prefix always agrees with
            // its name — but serialization can, by choosing a different prefix from the one the node was
            // written against. Prefix normalization does exactly that, taking the default prefix for an SVG
            // element that inherited a default binding to XHTML.
            string qualifiedName = m_elementNames[^1];
            int colon = qualifiedName.IndexOf(':');
            string elementPrefix = colon < 0 ? string.Empty : qualifiedName[..colon];

            if (string.Equals(elementPrefix, prefix, StringComparison.Ordinal)
                && !string.Equals(m_elementNamespaces[^1], namespaceUri, StringComparison.Ordinal))
            {
                // Where the name has not gone out yet, the node keeps the prefix it came with and the
                // element takes another for the namespace it is in. That is namespace fixup: a copied
                // namespace node is part of the element's namespace nodes, and the element's own name
                // gives way to it rather than the other way round.
                if (prefix.Length != 0 && m_pendingName is not null && !ownPrefixAlreadyOffered)
                {
                    RenameElementPrefix(prefix);
                }
                else
                {
                    return;
                }
            }

            WriteNamespaceDeclarationCore(prefix, namespaceUri);
        }

        /// <inheritdoc/>
        public override void MarkNoInheritedNamespaces()
        {
            if (m_elementNamespaceState.Count == 0)
            {
                return;
            }

            ElementNamespaces state = m_elementNamespaceState[^1];
            state.NoInherit = true;
            m_elementNamespaceState[^1] = state;
        }

        /// <inheritdoc/>
        public override void MarkOwnNamespaces(bool root)
        {
            if (m_elementNamespaceState.Count == 0)
            {
                return;
            }

            ElementNamespaces state = m_elementNamespaceState[^1];
            state.Copied = true;

            // Below the top of a copy the namespaces come from where the copy was attached, which is what
            // the element above holds on to for exactly this. At the top they come from that element itself,
            // which is what StartElement already worked out.
            if (!root && m_elementNamespaceState.Count > 1)
            {
                ElementNamespaces above = m_elementNamespaceState[^2];

                if (above.Copied)
                {
                    state.Inherited = above.NoInherit ? string.Empty : above.Inherited;

                    if (!state.OwnsDefault && !state.Offered)
                    {
                        state.Default = state.Inherited;
                    }
                }
            }

            m_elementNamespaceState[^1] = state;
        }

        /// <summary>
        /// Writes the declaration that makes the default namespace in force what the element's namespace
        /// nodes say it is, where the two have come apart.
        /// </summary>
        /// <remarks>
        /// They come apart where an element has no default namespace node and something above it does: an
        /// element written with a prefix inside <c>inherit-namespaces="no"</c>, or one copied out of a
        /// document that undeclared the default. XML 1.0 can undeclare the default namespace and no other,
        /// so this is the whole of what serialization can say about a namespace an element does not have.
        /// </remarks>
        private void SettleDefaultNamespace()
        {
            if (Method is OutputMethod.Html or OutputMethod.Text || m_elementNamespaceState.Count == 0)
            {
                return;
            }

            string declared = m_elementNamespaceState[^1].Default;

            if (!string.Equals(declared, LookupUri(string.Empty) ?? string.Empty, StringComparison.Ordinal))
            {
                WriteNamespaceDeclarationCore(string.Empty, declared);
            }
        }

        /// <summary>What an open element's namespace nodes say about the default namespace.</summary>
        private struct ElementNamespaces
        {
            /// <summary>The default namespace the element takes from where it was attached.</summary>
            public string Inherited;

            /// <summary>The default namespace among the element's own namespace nodes.</summary>
            public string Default;

            /// <summary>Whether the element's own name binds the default prefix, so nothing may rebind it.</summary>
            public bool OwnsDefault;

            /// <summary>Whether a default binding was declared for the element.</summary>
            public bool Offered;

            /// <summary>Whether the element was written by a copy, which declares its whole set.</summary>
            public bool Copied;

            /// <summary>Whether the element's children take none of its namespaces.</summary>
            public bool NoInherit;

            /// <summary>
            /// Whether a namespace node was declared for the prefix the element's own name carries, which
            /// makes a second one of that name a conflict rather than something fixup can move out of.
            /// </summary>
            public bool OwnPrefixOffered;
        }

        /// <inheritdoc/>
        public override void CreateNamespace(string prefix, string namespaceUri)
        {
            if (Method == OutputMethod.Text)
            {
                // The text method emits character data only, so there is nothing for a namespace node to be
                // attached to and nothing lost by leaving it out.
                return;
            }

            if (!m_startTagOpen)
            {
                throw new XsltException(
                    $"A namespace declaration ({Describe(prefix)}) cannot be added after the element's "
                    + "content has started.");
            }

            // An element's own name is a binding too. Re-binding the prefix it was written with — or giving a
            // default namespace to an element that has none — would change what the element itself means when
            // the result is read back, which is never what the stylesheet meant to say.
            string qualifiedName = m_elementNames[^1];
            int colon = qualifiedName.IndexOf(':');
            string elementPrefix = colon < 0 ? string.Empty : qualifiedName[..colon];

            if (string.Equals(elementPrefix, prefix, StringComparison.Ordinal)
                && !string.Equals(m_elementNamespaces[^1], namespaceUri, StringComparison.Ordinal))
            {
                if (prefix.Length == 0 || m_pendingName is null
                    || m_elementNamespaceState[^1].OwnPrefixOffered)
                {
                    bool nameless = m_elementNamespaces[^1].Length == 0;
                    string owner = nameless ? "is in no namespace" : $"is in '{m_elementNamespaces[^1]}'";

                    // A default namespace on an element that is in none has a code of its own: it is not
                    // two namespace nodes disagreeing but one that the element could never have carried.
                    throw XsltErrors.Error(
                        nameless && prefix.Length == 0 ? XsltErrorCode.XTDE0440 : XsltErrorCode.XTDE0430,
                        $"The element '{qualifiedName}' {owner}, so binding {Describe(prefix)} to "
                        + $"'{namespaceUri}' would change what its own name means.");
                }

                // The namespace node wins and the element's own name gives way: the prefix it was written
                // with is no longer available to it, so it takes another. That is what the specification
                // asks for where the two collide (XSLT 3.0 §11.7), the alternative being to refuse a
                // stylesheet that has said nothing contradictory.
                RenameElementPrefix(prefix);
            }

            // Re-binding a prefix that an ancestor bound differently is ordinary and required; doing it twice
            // on one element is not, and would serialize as the same attribute written twice.
            for (int i = m_namespaceMarks[^1]; i < m_namespaces.Count; i++)
            {
                if (!string.Equals(m_namespaces[i].Prefix, prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(m_namespaces[i].Uri, namespaceUri, StringComparison.Ordinal))
                {
                    return;
                }

                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0430,
                    $"One element cannot bind {Describe(prefix)} to both '{m_namespaces[i].Uri}' and "
                    + $"'{namespaceUri}'.");
            }

            WriteNamespaceDeclaration(prefix, namespaceUri);
        }

        /// <summary>
        /// Gives the element whose start tag is still open a prefix nothing else is using, keeping the
        /// namespace its name is in.
        /// </summary>
        /// <param name="taken">The prefix something else has claimed.</param>
        private void RenameElementPrefix(string taken)
        {
            string namespaceUri = m_elementNamespaces[^1];
            string localName = m_elementLocalNames[^1];
            string chosen = UnusedPrefixLike(taken, 0);

            // The declaration the element's own name was written against moves to the new prefix, where
            // there was one; where the name took the binding from an ancestor, the new prefix needs one.
            for (int i = 0; i < m_pendingNamespaces.Count; i++)
            {
                if (m_pendingNamespaces[i].Prefix == taken)
                {
                    m_pendingNamespaces[i] = (chosen, m_pendingNamespaces[i].Uri);
                    namespaceUri = string.Empty;
                    break;
                }
            }

            for (int i = m_namespaceMarks[^1]; i < m_namespaces.Count; i++)
            {
                if (m_namespaces[i].Prefix == taken)
                {
                    m_namespaces[i] = (chosen, m_namespaces[i].Uri);
                    break;
                }
            }

            if (namespaceUri.Length != 0)
            {
                WriteNamespaceDeclarationCore(chosen, namespaceUri);
            }

            m_pendingName = Qualify(chosen, localName);
            m_elementNames[^1] = m_pendingName;
        }

        /// <summary>The prefix the open element's name is written with, empty where it has none.</summary>
        private string PrefixOfOpenElement()
        {
            string qualifiedName = m_elementNames[^1];
            int colon = qualifiedName.IndexOf(':');

            return colon < 0 ? string.Empty : qualifiedName[..colon];
        }

        /// <summary>Whether a prefix has already been declared on the element whose start tag is open.</summary>
        private bool IsDeclaredHere(string prefix)
        {
            for (int i = m_namespaceMarks[^1]; i < m_namespaces.Count; i++)
            {
                if (m_namespaces[i].Prefix == prefix)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(string prefix)
        {
            return prefix.Length == 0 ? "the default namespace" : $"the prefix '{prefix}'";
        }

        /// <summary>Closes the innermost open element.</summary>
        /// <exception cref="XsltException">No element is open.</exception>
        public override void EndElement()
        {
            m_lastWasAtomic = false;
            if (m_elementNames.Count == 0)
            {
                throw new XsltException("There is no open element to close.");
            }

            // The Content-Type machinery is idle in every document that is not a web page, and both fields
            // say so with one comparison apiece — so the ordinary element pays nothing for it.
            if (m_heldDepth < 0 && m_contentTypeWritten < 0)
            {
                EndElementCore();
                return;
            }

            // A meta whose start tag never closed — the empty form, which is how one is nearly always
            // written — is decided here instead, while its attributes are still pending.
            if (m_heldDepth == m_elementNames.Count && m_startTagOpen && !SaysContentType())
            {
                Release(discard: false);
            }

            bool held = m_heldDepth == m_elementNames.Count;

            if (m_contentTypeWritten == m_elementNames.Count)
            {
                m_contentTypeWritten = -1;
            }

            EndElementCore();

            // Still held means it said Content-Type, and this serializer has written that meta itself.
            if (held)
            {
                Release(discard: true);
            }
        }

        private void EndElementCore()
        {
            // An empty head still owes its Content-Type meta, so the start tag is closed properly rather than
            // collapsed — which is also what the HTML and XHTML methods would do with it anyway.
            if (m_startTagOpen && m_contentTypeDepth == m_elementNames.Count)
            {
                CloseStartTag();
            }

            // Buffered attributes belong to this element and must be written while its namespace scope is
            // still in force; flushing after the scope was popped would make an attribute re-declare the very
            // prefix its own element had already declared.
            if (m_startTagOpen && Method != OutputMethod.Text)
            {
                SettleDefaultNamespace();
                WritePendingAttributes();
            }

            string qualifiedName = m_elementNames[^1];
            string localName = m_elementLocalNames[^1];
            string namespaceUri = m_elementNamespaces[^1];
            bool hadChildElements = m_elementHasChildElements[^1];
            bool hadText = m_elementHasText[^1];

            if (m_suppressDepth == m_elementNames.Count)
            {
                m_suppressDepth = -1;
            }

            m_elementNames.RemoveAt(m_elementNames.Count - 1);
            m_elementLocalNames.RemoveAt(m_elementLocalNames.Count - 1);
            m_elementNamespaces.RemoveAt(m_elementNamespaces.Count - 1);
            m_elementHasChildElements.RemoveAt(m_elementHasChildElements.Count - 1);
            m_elementHasText.RemoveAt(m_elementHasText.Count - 1);

            m_elementNamespaceState.RemoveAt(m_elementNamespaceState.Count - 1);

            int mark = m_namespaceMarks[^1];
            m_namespaceMarks.RemoveAt(m_namespaceMarks.Count - 1);
            m_namespaces.RemoveRange(mark, m_namespaces.Count - mark);

            if (Method == OutputMethod.Text)
            {
                return;
            }

            if (Method == OutputMethod.Html && IsHtmlVoid(localName))
            {
                // HTML void elements are never closed and never self-closed.
                if (m_startTagOpen)
                {
                    WritePendingAttributes();
                    m_writer.Write('>');
                    m_startTagOpen = false;
                }

                return;
            }

            if (m_startTagOpen)
            {
                WritePendingAttributes();

                // An empty element collapses, except where a browser would misread the result: in HTML never,
                // and in XHTML only for the elements HTML itself calls empty.
                if (Method == OutputMethod.Html
                    || (Method == OutputMethod.Xhtml && !CollapsesInXhtml(namespaceUri, localName)))
                {
                    m_writer.Write("></");
                    m_writer.Write(qualifiedName);
                    m_writer.Write('>');
                }
                else
                {
                    // The space before the slash is XHTML's, for browsers reading it as HTML; XML has no
                    // use for it, and the empty-element tag is written the way everything else writes it.
                    m_writer.Write(Method == OutputMethod.Xhtml ? " />" : "/>");
                }

                m_startTagOpen = false;
                return;
            }

            // A container of elements gets its closing tag on its own line; one holding text does not, so the
            // text is not altered.
            if (m_indent && hadChildElements && !hadText)
            {
                m_writer.Write('\n');
                WriteIndent(m_elementNames.Count);
            }

            m_writer.Write("</");
            m_writer.Write(qualifiedName);
            m_writer.Write('>');
        }

        /// <summary>
        /// Settles what has to be settled before text goes out at the top of a document, and says whether
        /// the text was held back rather than written.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two things happen above the document element and nowhere else. Whitespace waits with whatever
        /// else is waiting for the method decision, an html document element being still able to follow it,
        /// and anything else settles the method as xml — an html document element cannot follow text (§26).
        /// </para>
        /// <para>
        /// Then the declaration goes out, because it is the first thing in a document or it is not a
        /// declaration at all. A comment and a processing instruction have always done this; text had not,
        /// so a stylesheet writing its own document type declaration with
        /// <c>disable-output-escaping</c> — which is how the DocBook XHTML5 stylesheets write
        /// <c>&lt;!DOCTYPE html&gt;</c> — got the two the wrong way round and a result nothing would parse.
        /// </para>
        /// </remarks>
        /// <param name="text">The text about to be written.</param>
        private bool SettleTopBefore(string text)
        {
            if (m_elementNames.Count != 0)
            {
                return false;
            }

            if (DefersAtTop)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Touch();
                    (m_deferredTop ??= new System.Text.StringBuilder()).Append(text);
                    return true;
                }

                m_methodDecided = true;
                ApplyIndentDefault();
            }

            WriteXmlDeclaration();
            return false;
        }

        /// <summary>Whether the last thing written was an atomic value, with nothing after it yet.</summary>
        private bool m_lastWasAtomic;

        /// <inheritdoc/>
        /// <remarks>
        /// The separator is the result document's, which is a single space unless <c>item-separator</c> said
        /// otherwise — the one place the two settings meet, a sequence written straight to a result being
        /// both the sequence constructor's product and the document's content.
        /// </remarks>
        public override void WriteAtomic(string text)
        {
            // At the top of a separated result every item is separated already; inside an element, two
            // adjacent atomic values still get the separator between them, as a tree has it.
            if (m_lastWasAtomic && (!m_sequence || m_elementNames.Count != 0))
            {
                WriteText(ItemSeparator);
            }

            WriteText(text);
            m_lastWasAtomic = true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Nothing is written for the document node itself, and that is the point of overriding this: it
        /// is an item all the same, so a run of atomic values ends here and the next one takes no
        /// separator. Sequence normalization puts the separator between adjacent strings, and a document
        /// node between two of them is what stops them being adjacent, even where it is empty.
        /// </remarks>
        public override void StartDocumentCopy()
        {
            m_lastWasAtomic = false;
        }

        /// <inheritdoc/>
        public override bool IsFinalOutput => true;

        /// <summary>Writes character data, escaping it as the output method requires.</summary>
        /// <param name="text">The characters to write.</param>
        public override void WriteText(string text)
        {
            StartOutput();
            m_lastWasAtomic = false;

            if (text.Length == 0)
            {
                return;
            }

            if (SettleTopBefore(text))
            {
                return;
            }

            BeginItem();

            Touch();
            CloseStartTag();

            // With a character map the normalization waits for the map, which is applied to the characters
            // as they stand and whose substitutions are left alone (Serialization §11).
            if (m_characterMap is null && m_settings.NormalizationForm is System.Text.NormalizationForm form)
            {
                text = text.Normalize(form);
            }

            if (m_elementHasText.Count > 0)
            {
                m_elementHasText[^1] = true;
            }

            if (Method == OutputMethod.Text || IsInsideUnescapedHtmlElement())
            {
                WriteUnescaped(text);
                return;
            }

            if (m_elementNamespaces.Count > 0
                && m_settings.IsCDataSection(m_elementNamespaces[^1], m_elementLocalNames[^1]))
            {
                WriteCDataSection(text);
                return;
            }

            WriteEscapedText(text);
        }

        /// <summary>
        /// Writes text as a CDATA section, splitting it if it contains the terminator.
        /// </summary>
        /// <remarks>
        /// A CDATA section cannot contain <c>]]&gt;</c>, so content carrying it is emitted as consecutive
        /// sections split across the sequence — the standard way to keep the text intact.
        /// <para>
        /// A character map does not reach here. The two mechanisms want opposite things of the same text —
        /// one replaces characters before serialization, the other says this text is to appear exactly as it
        /// stands — and the specification settles it in favour of the section.
        /// </para>
        /// </remarks>
        private void WriteCDataSection(string text)
        {
            if (m_limit is null)
            {
                WriteCDataSectionCore(text);
                return;
            }

            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < (char)0x80 || OutsideTheEncoding(text, i, out int length) is not string reference)
                {
                    continue;
                }

                // A character reference is the one way to write a character the encoding cannot carry, and a
                // CDATA section is the one place a reference means nothing — so the section stops for it and
                // starts again after, which leaves what a parser reads back unchanged. That is the same
                // treatment the terminator already gets.
                WriteCDataSectionCore(text.AsSpan(start, i - start));
                m_writer.Write(reference);
                i += length - 1;
                start = i + 1;
            }

            WriteCDataSectionCore(text.AsSpan(start));
        }

        /// <summary>Writes one run of text as CDATA sections, splitting it around any terminator.</summary>
        private void WriteCDataSectionCore(ReadOnlySpan<char> text)
        {
            while (!text.IsEmpty)
            {
                int terminator = text.IndexOf("]]>".AsSpan());

                m_writer.Write("<![CDATA[");
                if (terminator < 0)
                {
                    m_writer.Write(text);
                    m_writer.Write("]]>");
                    return;
                }

                // Break after "]]" so the ">" starts the next section.
                m_writer.Write(text[..(terminator + 2)]);
                m_writer.Write("]]>");
                text = text[(terminator + 2)..];
            }
        }

        /// <summary>How a character that the map leaves alone is written.</summary>
        private enum EscapeStyle
        {
            /// <summary>Verbatim, for the text method and for content escaping was disabled on.</summary>
            None,

            /// <summary>As character data, so that markup characters cannot start markup.</summary>
            Text,

            /// <summary>As an attribute value, which also protects quotes and whitespace.</summary>
            Attribute,
        }

        /// <summary>
        /// Writes text through a character map: a mapped character becomes its replacement, written exactly as
        /// it stands, and everything else is escaped as the style requires.
        /// </summary>
        /// <remarks>
        /// A separate loop from the escaping paths, rather than a test folded into them, because those run for
        /// every character of every result and almost no stylesheet declares a character map. Reaching this at
        /// all is the price of asking for one.
        /// </remarks>
        private void WriteMapped(string value, CharacterMap map, EscapeStyle style)
        {
            int start = 0;

            for (int i = 0; i < value.Length; i++)
            {
                if (map.TryMap(value, i, out string? mapped, out int length))
                {
                    WriteRun(value, start, i, style);
                    m_writer.Write(mapped);

                    // A mapped character may be a surrogate pair, in which case both units are consumed.
                    i += length - 1;
                    start = i + 1;
                }
            }

            WriteRun(value, start, value.Length, style);
        }

        /// <summary>
        /// Writes the characters between two mapped ones: normalized where the output asks for it, and
        /// escaped as the style says.
        /// </summary>
        /// <remarks>
        /// Normalization comes here rather than before the map is consulted, because the map is applied to
        /// the characters as they stand and what it substitutes is left alone (Serialization §11): a map on
        /// <c>c</c> under NFD must not meet the <c>c</c> that decomposing <c>ç</c> leaves behind, and the
        /// <c>ç</c> the map writes must stay composed.
        /// </remarks>
        private void WriteRun(string value, int start, int end, EscapeStyle style)
        {
            if (end <= start)
            {
                return;
            }

            if (m_settings.NormalizationForm is System.Text.NormalizationForm form)
            {
                string run = value[start..end].Normalize(form);
                WriteEscaping(run, 0, run.Length, style);
                return;
            }

            WriteEscaping(value, start, end, style);
        }

        /// <summary>Writes a stretch of characters, escaping each as the style says.</summary>
        /// <summary>The characters that are markup in character data, and those that are in an attribute value.</summary>
        private static readonly System.Buffers.SearchValues<char> s_textMarkup =
            System.Buffers.SearchValues.Create("&<>");

        private static readonly System.Buffers.SearchValues<char> s_attributeMarkup =
            System.Buffers.SearchValues.Create("&<>\"\n\r\t");

        /// <summary>
        /// Whether a stretch of characters holds anything the style might have to escape: one of its
        /// markup characters, or anything at or above U+007F, which is where the rules about the output
        /// method and the encoding begin.
        /// </summary>
        /// <remarks>
        /// Asked once per stretch, with the vectorized searches, before the character-by-character loop
        /// that decides what each needs. Nearly every text node and attribute value of nearly every result
        /// holds none of them, and for those the loop is skipped altogether and the stretch written whole.
        /// </remarks>
        private static bool MayNeedEscaping(ReadOnlySpan<char> text, EscapeStyle style)
        {
            return text.ContainsAny(style == EscapeStyle.Attribute ? s_attributeMarkup : s_textMarkup)
                || text.ContainsAnyExceptInRange((char)0, (char)0x7E);
        }

        private void WriteEscaping(string value, int start, int end, EscapeStyle style)
        {
            if (style == EscapeStyle.None || !MayNeedEscaping(value.AsSpan(start, end - start), style))
            {
                m_writer.Write(value.AsSpan(start, end - start));
                return;
            }

            int from = start;

            for (int i = start; i < end; i++)
            {
                string? replacement = EscapeFor(value, i, style, out int consumed);

                if (replacement is null)
                {
                    continue;
                }

                m_writer.Write(value.AsSpan(from, i - from));
                m_writer.Write(replacement);
                i += consumed - 1;
                from = i + 1;
            }

            m_writer.Write(value.AsSpan(from, end - from));
        }

        /// <summary>
        /// Writes character data without escaping, implementing <c>disable-output-escaping</c>.
        /// </summary>
        /// <param name="text">The characters to write verbatim.</param>
        public override void WriteRawText(string text)
        {
            StartOutput();
            m_lastWasAtomic = false;

            if (text.Length == 0)
            {
                return;
            }

            if (SettleTopBefore(text))
            {
                return;
            }

            BeginItem();

            Touch();
            CloseStartTag();
            WriteUnescaped(text);
        }

        /// <summary>
        /// Writes text that is not to be escaped, which is still subject to the character map.
        /// </summary>
        /// <remarks>
        /// A character map applies to every text node and attribute value, whether or not escaping is in
        /// force. The two mechanisms answer different questions — one says which characters to replace, the
        /// other whether to protect markup — and the serialization rules apply the map first, then skip the
        /// escaping of whatever it replaced.
        /// </remarks>
        private void WriteUnescaped(string text)
        {
            if (m_characterMap is null)
            {
                m_writer.Write(text);
                return;
            }

            WriteMapped(text, m_characterMap, EscapeStyle.None);
        }

        /// <summary>Writes a comment.</summary>
        /// <param name="text">The comment's character data.</param>
        public override void WriteComment(string text)
        {
            m_lastWasAtomic = false;
            StartOutput();

            if (DefersAtTop && Method != OutputMethod.Text)
            {
                Touch();
                (m_deferredTop ??= new System.Text.StringBuilder()).Append("<!--").Append(text).Append("-->");
                return;
            }

            if (m_elementNames.Count == 0)
            {
                WriteXmlDeclaration();
            }

            BeginItem();
            Touch();
            CloseStartTag();
            if (Method == OutputMethod.Text)
            {
                return;
            }

            m_writer.Write("<!--");
            m_writer.Write(text);
            m_writer.Write("-->");
        }

        /// <summary>Writes a processing instruction.</summary>
        /// <param name="target">The instruction's target.</param>
        /// <param name="data">The instruction's character data.</param>
        public override void WriteProcessingInstruction(string target, string data)
        {
            m_lastWasAtomic = false;

            if (DefersAtTop && Method != OutputMethod.Text)
            {
                StartOutput();
                Touch();
                System.Text.StringBuilder deferred = m_deferredTop ??= new System.Text.StringBuilder();
                deferred.Append("<?").Append(target);

                if (data.Length != 0)
                {
                    deferred.Append(' ').Append(data);
                }

                deferred.Append("?>");
                return;
            }

            if (m_elementNames.Count == 0)
            {
                StartOutput();
                WriteXmlDeclaration();
            }

            StartOutput();
            BeginItem();
            Touch();
            CloseStartTag();
            if (Method == OutputMethod.Text)
            {
                return;
            }

            m_writer.Write("<?");
            m_writer.Write(target);
            if (data.Length != 0)
            {
                m_writer.Write(' ');
                m_writer.Write(data);
            }

            m_writer.Write(Method == OutputMethod.Html ? ">" : "?>");
        }

        /// <summary>
        /// Writes text already serialized by another method — a JSON or adaptive result document written to
        /// the principal output — as it stands, ahead of nothing and after nothing.
        /// </summary>
        internal void WriteSerialized(string text)
        {
            StartOutput();
            m_prologWritten = true;
            m_declarationWritten = true;
            Touch();
            m_writer.Write(text);
        }

        /// <summary>Closes any open start tag and flushes the underlying writer.</summary>
        public void Flush()
        {
            // Nothing decided the method, and something waited for it: a document of a comment, or a
            // processing instruction, and nothing else. It is XML, and the declaration goes first.
            if (m_deferredTop is not null && !m_methodDecided)
            {
                m_methodDecided = true;
                ApplyIndentDefault();
                WriteXmlDeclaration();
            }

            // A result document that nothing was written into is still a document, and an XML one carries
            // its declaration. One that holds items written with a separator, or a comment or text at the
            // top, has content and no element to write a prolog before; it does not get the declaration at
            // its end.
            if (DeclaresWhenEmpty && !m_prologWritten && !HasContent && !m_settings.OmitXmlDeclaration
                && Method is OutputMethod.Xml or OutputMethod.Xhtml)
            {
                StartOutput();
                m_prologWritten = true;
                m_writer.Write($"<?xml version=\"{m_settings.Version}\" encoding=\"{m_settings.Encoding}\"?>");
            }

            CloseStartTag();
            m_writer.Flush();
        }

        private void CloseStartTag()
        {
            if (!m_startTagOpen)
            {
                return;
            }

            // A held meta is decided here, where its attributes are all in: one saying Content-Type stays
            // held to the end and is thrown away, since this serializer has written that meta itself.
            bool release = m_heldDepth == m_elementNames.Count && !SaysContentType();

            SettleDefaultNamespace();
            WritePendingAttributes();
            m_writer.Write('>');
            m_startTagOpen = false;

            if (release)
            {
                Release(discard: false);
            }

            // The meta goes immediately after the head's own start tag, so it can only be written once that
            // tag is closed — which is here, whatever closed it.
            if (m_contentTypeDepth == m_elementNames.Count)
            {
                m_contentTypeDepth = -1;
                m_contentTypeWritten = m_elementNames.Count;
                WriteContentTypeMeta();
            }
        }

        /// <summary>Whether the start tag being closed carries <c>http-equiv="Content-Type"</c>.</summary>
        private bool SaysContentType()
        {
            foreach (PendingAttribute attribute in m_pendingAttributes)
            {
                if (string.Equals(attribute.LocalName, "http-equiv", StringComparison.OrdinalIgnoreCase)
                    && attribute.NamespaceUri.Length == 0
                    && string.Equals(attribute.Value.Trim(), "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Ends a hold, either writing what was held or dropping it.</summary>
        /// <param name="discard">Whether to throw the held text away rather than write it.</param>
        private void Release(bool discard)
        {
            if (m_held is null)
            {
                return;
            }

            m_writer = m_destination!;
            m_destination = null;

            if (!discard)
            {
                m_writer.Write(m_held.GetStringBuilder());
            }

            m_held = null;
            m_heldDepth = -1;
        }

        /// <summary>
        /// Writes the <c>Content-Type</c> meta the HTML and XHTML methods put at the top of the head.
        /// </summary>
        /// <remarks>
        /// Written with the head's own prefix, so that a page whose XHTML namespace is bound to a prefix
        /// rather than to the default gets a meta in the same namespace as everything around it rather than
        /// one in no namespace.
        /// </remarks>
        private void WriteContentTypeMeta()
        {
            string qualifiedHead = m_elementNames[^1];
            int colon = qualifiedHead.IndexOf(':');
            string prefix = colon < 0 ? string.Empty : qualifiedHead[..colon];

            m_writer.Write('<');
            m_writer.Write(Qualify(prefix, "meta"));
            m_writer.Write(" http-equiv=\"Content-Type\" content=\"");
            WriteEscapedAttributeValue(
                $"{m_settings.MediaTypeFor(Method)}; charset={m_settings.Encoding}", m_characterMap);

            m_writer.Write(Method == OutputMethod.Xhtml ? "\" />" : "\">");
        }

        /// <inheritdoc/>
        public override string ItemSeparator => m_settings.ItemSeparator ?? " ";

        /// <summary>Whether the <c>html</c> or <c>xhtml</c> method is writing HTML 5 rather than HTML 4.</summary>
        private bool WritesHtml5 =>
            m_settings.HtmlVersion >= 5.0m && Method is OutputMethod.Html or OutputMethod.Xhtml;

        /// <summary>
        /// Whether <em>prefix normalization</em> applies to a name in this namespace.
        /// </summary>
        /// <remarks>
        /// <para>
        /// HTML 5's rule for the three namespaces it recognises: an element in one of them is written with no
        /// prefix, in the default namespace, and a declaration binding a <em>prefix</em> to one of them is
        /// dropped rather than written. What makes it necessary rather than tidy is that an HTML 5 parser has
        /// no general namespace mechanism — it recognises <c>svg</c> and <c>math</c> by name — so
        /// <c>&lt;s:svg&gt;</c> is not SVG to it but an unknown element called <c>s:svg</c>.
        /// </para>
        /// <para>
        /// The specification states this for the XHTML method and leaves the HTML method's version of it
        /// unclear enough that the test suite's own author calls the rules inadequate and says the two should
        /// behave alike. They do here.
        /// </para>
        /// <para>
        /// It applies to elements and not to attributes. An attribute cannot be in the default namespace at
        /// all — an unprefixed attribute is in no namespace — so <c>svg:att</c> keeps its prefix, and the
        /// declaration it needs is written on the element carrying it rather than inherited from an ancestor
        /// this rule has stripped it from.
        /// </para>
        /// </remarks>
        private bool NormalizesPrefix(string namespaceUri)
        {
            return WritesHtml5 && Array.IndexOf(s_html5Namespaces, namespaceUri) >= 0;
        }

        /// <summary>
        /// Whether the bare HTML 5 document type declaration is what this document gets.
        /// </summary>
        /// <remarks>
        /// Three conditions, and each of them is a test in the suite. The version has to say 5; a
        /// <c>doctype-system</c> has to be absent, because a stylesheet naming a DTD means that DTD and HTML
        /// 5 has none to name; and the document element has to be <c>html</c>, since a document rooted at
        /// something else is not an HTML 5 document and declaring it one would misdescribe what follows. A
        /// <c>doctype-public</c> on its own does not stop it: there is nothing to point at without a system
        /// identifier, so the bare form is still the honest answer.
        /// </remarks>
        private bool WritesHtml5Doctype(string localName, string namespaceUri)
        {
            if (!WritesHtml5 || m_settings.DoctypeSystem is not null)
            {
                return false;
            }

            // The XHTML method requires the namespace as well as the name, HTML being an XML vocabulary
            // there; the HTML method has no namespaces to speak of and takes the name alone. The name is
            // matched without regard to case in both, which is what says <HTML> is still an HTML document.
            return !string.Equals(localName, "html", StringComparison.OrdinalIgnoreCase)
                ? false
                : Method != OutputMethod.Xhtml || namespaceUri == XhtmlNamespace;
        }

        /// <summary>
        /// Whether an empty element may be written in the collapsed form under the XHTML method.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only for an element HTML calls empty, and only in the XHTML namespace: the method changes nothing
        /// about an element outside it, which is still serialized as XML. An element in no namespace at all
        /// is therefore <c>&lt;x/&gt;</c> here and not <c>&lt;x&gt;&lt;/x&gt;</c>.
        /// </para>
        /// <para>
        /// XHTML 5 drops the namespace half of that. The suite tests the same list of names in the XHTML
        /// namespace and in no namespace and wants the same answer from both — <c>&lt;title&gt;&lt;/title&gt;</c>
        /// either way, where plain XML would give <c>&lt;title/&gt;</c> — so under version 5 the name alone
        /// decides. It also uses HTML 5's list of void elements rather than HTML 4's, which is the list that
        /// gained <c>embed</c>, <c>source</c>, <c>track</c> and <c>wbr</c>.
        /// </para>
        /// </remarks>
        private bool CollapsesInXhtml(string namespaceUri, string localName)
        {
            if (WritesHtml5)
            {
                return IsHtmlVoid(localName);
            }

            return namespaceUri != XhtmlNamespace || s_xhtmlEmptyElements.Contains(localName);
        }

        private bool IsInsideUnescapedHtmlElement()
        {
            return Method == OutputMethod.Html
                && m_elementLocalNames.Count > 0
                && s_htmlUnescapedElements.Contains(m_elementLocalNames[^1]);
        }

        private static string Qualify(string prefix, string localName)
        {
            return prefix.Length == 0 ? localName : string.Concat(prefix, ":", localName);
        }

        /// <summary>
        /// Decides which prefix to serialize a name under, and whether a declaration must accompany it.
        /// </summary>
        /// <remarks>
        /// An attribute in a namespace cannot use the default declaration, because an unprefixed attribute is
        /// always in no namespace. Such an attribute is given a generated prefix when it does not already have
        /// a usable one.
        /// </remarks>
        private string ChoosePrefix(string prefix, string namespaceUri, bool forAttribute, out bool declare)
        {
            // The xml prefix is bound everywhere by definition and must never be declared: writing
            // xmlns:xml="…" is not a redundant declaration but an ill-formed document.
            if (namespaceUri == XmlNamespace)
            {
                declare = false;
                return "xml";
            }

            if (namespaceUri.Length == 0)
            {
                // A name in no namespace must not inherit a default declaration that binds one.
                declare = !forAttribute && LookupUri(string.Empty) is { Length: > 0 };
                return string.Empty;
            }

            // An element in one of HTML 5's three namespaces is written with no prefix, whatever prefix it
            // arrived with — and declares the default namespace unless that is already what is in scope.
            if (!forAttribute && NormalizesPrefix(namespaceUri))
            {
                declare = !string.Equals(LookupUri(string.Empty), namespaceUri, StringComparison.Ordinal);
                return string.Empty;
            }

            if (forAttribute && prefix.Length == 0)
            {
                string? existing = LookupPrefixFor(namespaceUri, requireNonEmpty: true);
                if (existing is not null)
                {
                    declare = false;
                    return existing;
                }

                declare = true;
                return NextGeneratedPrefix();
            }

            // An attribute in one of those namespaces falls through to the ordinary path below and keeps its
            // prefix, an attribute having no default namespace it could be in. That path asks what is in
            // scope rather than assuming, which is what makes it declare the prefix again on the element
            // carrying the attribute after the rule above stripped it from an ancestor.
            if (string.Equals(LookupUri(prefix), namespaceUri, StringComparison.Ordinal))
            {
                declare = false;
                return prefix;
            }

            // A prefix already declared on this very element binds another namespace there, and one element
            // cannot declare a prefix twice: writing the second declaration would produce a document no
            // parser will accept. So the name gives way to a prefix nothing has claimed, which is what an
            // xsl:attribute naming a namespace of its own asks for.
            declare = true;
            return IsDeclaredHere(prefix) ? UnusedPrefixLike(prefix, 1) : prefix;
        }

        /// <summary>
        /// Invents a prefix for a namespace nothing in scope has one for.
        /// </summary>
        /// <remarks>
        /// Numbered from zero, and skipping any number a declaration in scope has already taken: reusing a
        /// prefix the result is using would rebind it on the element being written, quietly changing what
        /// every name spelled with it there means.
        /// </remarks>
        /// <summary>
        /// A prefix nothing in scope is using, derived from the one that was asked for.
        /// </summary>
        /// <remarks>
        /// Keeping what was written as the stem rather than inventing something unrelated: the prefix is
        /// arbitrary either way, and one that still reads as the author's is easier to follow in the result
        /// than <c>ns0</c>. Which is also why the number it starts from is a parameter: the suite pins the
        /// name in both places this is reached from, and pins a different one in each — an element that
        /// gives up its prefix becomes <c>p_0</c> and an attribute that needs one of its own becomes
        /// <c>p_1</c>. Nothing in the specification decides either.
        /// </remarks>
        /// <param name="taken">The prefix something else has claimed.</param>
        /// <param name="from">The number to try first.</param>
        private string UnusedPrefixLike(string taken, int from)
        {
            string chosen = taken + "_" + from.ToString(System.Globalization.CultureInfo.InvariantCulture);

            for (int suffix = from + 1; LookupUri(chosen) is not null || IsDeclaredHere(chosen); suffix++)
            {
                chosen = taken + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return chosen;
        }

        private string NextGeneratedPrefix()
        {
            while (LookupUri($"ns{m_generatedPrefixCount}") is not null)
            {
                m_generatedPrefixCount++;
            }

            return $"ns{m_generatedPrefixCount++}";
        }

        private void Declare(string prefix, string namespaceUri)
        {
            m_namespaces.Add((prefix, namespaceUri));
        }

        private void WriteNamespaceDeclarationCore(string prefix, string namespaceUri)
        {
            // The binding of the xml prefix is implicit in every XML document, so a namespace node carrying
            // it is part of the data model but never part of the output. Serialization says not to write it,
            // and a copied element brings one along whenever the namespace axis is walked.
            if (string.Equals(prefix, "xml", StringComparison.Ordinal)
                && string.Equals(namespaceUri, XmlNamespace, StringComparison.Ordinal))
            {
                return;
            }

            // Recording the binding here, and only here, is what stops a declaration being repeated on every
            // descendant that happens to use the same prefix.
            Declare(prefix, namespaceUri);

            if (m_pendingName is not null)
            {
                m_pendingNamespaces.Add((prefix, namespaceUri));
                return;
            }

            WriteNamespaceDeclarationText(prefix, namespaceUri);
        }

        /// <summary>Writes one namespace declaration into the start tag being written out.</summary>
        private void WriteNamespaceDeclarationText(string prefix, string namespaceUri)
        {
            if (prefix.Length == 0)
            {
                m_writer.Write(" xmlns=\"");
            }
            else
            {
                m_writer.Write(" xmlns:");
                m_writer.Write(prefix);
                m_writer.Write("=\"");
            }

            WriteEscapedAttributeValue(namespaceUri, null);
            m_writer.Write('"');
        }

        private string? LookupUri(string prefix)
        {
            for (int i = m_namespaces.Count - 1; i >= 0; i--)
            {
                if (string.Equals(m_namespaces[i].Prefix, prefix, StringComparison.Ordinal))
                {
                    return m_namespaces[i].Uri;
                }
            }

            return null;
        }

        private string? LookupPrefixFor(string namespaceUri, bool requireNonEmpty)
        {
            for (int i = m_namespaces.Count - 1; i >= 0; i--)
            {
                (string Prefix, string Uri) binding = m_namespaces[i];
                if (requireNonEmpty && binding.Prefix.Length == 0)
                {
                    continue;
                }

                if (string.Equals(binding.Uri, namespaceUri, StringComparison.Ordinal))
                {
                    return binding.Prefix;
                }
            }

            return null;
        }

        private void WriteEscapedText(string text)
        {
            if (m_characterMap is not null)
            {
                WriteMapped(text, m_characterMap, EscapeStyle.Text);
                return;
            }

            WriteEscaping(text, 0, text.Length, EscapeStyle.Text);
        }

        /// <summary>
        /// Writes a value in attribute syntax, escaping it.
        /// </summary>
        /// <param name="value">The characters to write.</param>
        /// <param name="map">
        /// The character map to apply, or <see langword="null"/> for none. Passed rather than taken from the
        /// settings because a namespace declaration goes through here too, and a character map applies to
        /// attribute nodes — which a namespace declaration is not.
        /// </param>
        private void WriteEscapedAttributeValue(string value, CharacterMap? map)
        {
            if (map is not null)
            {
                WriteMapped(value, map, EscapeStyle.Attribute);
                return;
            }

            WriteEscaping(value, 0, value.Length, EscapeStyle.Attribute);
        }

        /// <summary>
        /// What one character has to be written as, or <see langword="null"/> where it may stand as it is.
        /// </summary>
        /// <remarks>
        /// The one place that says what escaping is, so that the three loops calling it — mapped, text and
        /// attribute — cannot drift apart. In an attribute a line ending or a tab must become a reference
        /// too, or a parser would normalize it to a space and the value would come back changed.
        /// <para>
        /// The HTML and XHTML methods add one rule of their own: a character between #x7F and #x9F is
        /// written as a reference rather than as itself, whatever the encoding can carry. Those code points
        /// are unassigned controls in Unicode and the characters a browser shows for them are Windows-1252's,
        /// so a literal one means different things in different readers and a reference means one thing.
        /// </para>
        /// </remarks>
        /// <param name="value">The text being written.</param>
        /// <param name="at">Where in it the character stands.</param>
        /// <param name="style">What is being written.</param>
        /// <param name="length">How many characters were consumed, which is two for a surrogate pair.</param>
        private string? EscapeFor(string value, int at, EscapeStyle style, out int length)
        {
            length = 1;
            char character = value[at];

            switch (character)
            {
                case '&' when style != EscapeStyle.None: return "&amp;";
                case '<' when style != EscapeStyle.None: return "&lt;";
                case '>' when style != EscapeStyle.None: return "&gt;";

                // Numeric, where the three above are named. The specification requires the delimiter to be
                // escaped and does not say how, and both spellings are correct XML; what settles it is that
                // the suite asks twice and asks for this one. output-0102c and 0103c write a quotation mark
                // in a URI attribute and match the result against a pattern admitting '&#34;' and '&#x22;'
                // and nothing else, and no test anywhere asks for '&quot;'. It is the more consistent
                // answer as well: the same attribute has this engine writing '&#150;' beside it, every
                // character that needs a reference here taking one.
                case '"' when style == EscapeStyle.Attribute: return "&#34;";
                case '\n' when style == EscapeStyle.Attribute: return "&#xA;";
                case '\r' when style == EscapeStyle.Attribute: return "&#xD;";
                case '\t' when style == EscapeStyle.Attribute: return "&#x9;";

                default:
                    // Everything below #x7F is written as it stands, which is nearly every character of
                    // nearly every result. The two rules that are not about markup live in their own method
                    // so that this one stays small enough to be inlined into the loops that call it.
                    return character < (char)0x7F || style == EscapeStyle.None
                        ? null
                        : Unusual(value, at, out length);
            }
        }

        /// <summary>
        /// The reference standing in for a character the markup rules do not reach, or null for one that
        /// needs none.
        /// </summary>
        /// <remarks>
        /// Two rules meet here, and neither applies to a character below #x7F. The HTML and XHTML methods
        /// write #x7F to #x9F as references whatever the encoding can carry — those code points are
        /// unassigned controls in Unicode and the characters a browser shows for them are Windows-1252's, so
        /// a literal one means different things in different readers. And any character the declared
        /// encoding cannot carry is written as a reference, on every method.
        /// </remarks>
        /// <param name="value">The text being written.</param>
        /// <param name="at">Where in it the character stands.</param>
        /// <param name="length">How many characters were consumed, which is two for a surrogate pair.</param>
        private string? Unusual(string value, int at, out int length)
        {
            length = 1;
            char character = value[at];

            if (character <= (char)0x9F && Method is OutputMethod.Html or OutputMethod.Xhtml)
            {
                return $"&#{(int)character};";
            }

            return m_limit is null || character < (char)0x80
                ? null
                : OutsideTheEncoding(value, at, out length);
        }

        /// <summary>
        /// The character reference standing in for a character the encoding cannot carry, or null where it
        /// can carry it.
        /// </summary>
        /// <remarks>
        /// A result declaring an encoding it then writes characters outside is a result nothing can read
        /// back: the bytes would be a substitution mark, and what the stylesheet produced would be gone with
        /// nothing saying so. A reference says the same thing in characters the encoding does have.
        /// <para>
        /// A character above the basic plane arrives as two, and its reference is the code point rather than
        /// either half, so the pair is consumed together.
        /// </para>
        /// </remarks>
        /// <param name="value">The text being written.</param>
        /// <param name="at">Where in it the character stands.</param>
        /// <param name="length">How many characters the code point occupies.</param>
        private string? OutsideTheEncoding(string value, int at, out int length)
        {
            length = char.IsHighSurrogate(value[at]) && at + 1 < value.Length ? 2 : 1;

            int code = length == 2 ? char.ConvertToUtf32(value[at], value[at + 1]) : value[at];
            return Encodable(value.Substring(at, length)) ? null : $"&#x{code:X};";
        }

        /// <summary>Whether the declared encoding can carry one code point, remembering the answer.</summary>
        /// <param name="text">The code point, as one character or a surrogate pair.</param>
        private bool Encodable(string text)
        {
            m_encodable ??= new Dictionary<string, bool>(StringComparer.Ordinal);

            if (m_encodable.TryGetValue(text, out bool known))
            {
                return known;
            }

            // Encoded and read back rather than asked: an encoding substitutes a question mark for what it
            // cannot carry, so a round trip that comes back changed is one that lost something. Cheaper than
            // an exception fallback, and it answers for a surrogate pair as readily as for one character.
            byte[] bytes = m_limit!.GetBytes(text);
            bool encodable = m_limit.GetString(bytes) == text;

            m_encodable.Add(text, encodable);
            return encodable;
        }

        /// <summary>
        /// The encoding a character must fit in, or null where it can hold every character there is.
        /// </summary>
        /// <remarks>
        /// Null for the Unicode encodings, which is nearly every result, so the check costs nothing where
        /// there is nothing to check. Also null for a name .NET does not know: this engine writes to a
        /// <see cref="TextWriter"/> whose own encoding does the real work, and inventing references for an
        /// encoding that may not be the one in use would corrupt a result that was fine.
        /// </remarks>
        /// <param name="name">The declared encoding's name.</param>
        private static Encoding? LimitOf(string name)
        {
            try
            {
                Encoding encoding = Encoding.GetEncoding(name);

                return encoding.CodePage is 65001 or 1200 or 1201 or 12000 or 12001 ? null : encoding;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }
}
