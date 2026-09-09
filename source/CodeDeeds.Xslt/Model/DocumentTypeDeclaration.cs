using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// An unparsed entity a document's type declaration declares, which is what
    /// <c>unparsed-entity-uri()</c> and <c>unparsed-entity-public-id()</c> answer with.
    /// </summary>
    /// <param name="SystemId">
    /// The system identifier, resolved against the base URI of the entity that declared it where that base
    /// was known, and as written where it was not.
    /// </param>
    /// <param name="PublicId">The public identifier with its whitespace normalized, or null where none was given.</param>
    /// <param name="Notation">The notation named after <c>NDATA</c>.</param>
    public readonly record struct UnparsedEntity(string SystemId, string? PublicId, string Notation);

    /// <summary>
    /// What a document type declaration says that the data model keeps: which attributes are of type ID
    /// and which IDREF, which elements hold element content only, and which entities are unparsed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reader that parses the document reads the same declaration, and expands entities and supplies
    /// default attributes from it — but it says nothing afterwards about attribute types or entity
    /// declarations, so those are read here from the text of the internal subset it hands over, and from the
    /// external subset where a resolver was given to fetch it. Read once per document, and only for a
    /// document that has a declaration.
    /// </para>
    /// <para>
    /// This is a reading of the declaration for two facts, not a validation of it: a declaration the
    /// reader accepted is scanned leniently, a markup declaration this does not understand is skipped to its
    /// end, and a parameter entity that cannot be reached — an external one with no resolver to fetch it —
    /// is dropped where it is referenced. Parameter entities are expanded as XML expands them, including
    /// inside entity values and conditional sections, so a DTD that composes its attribute lists out of
    /// them reads as it should.
    /// </para>
    /// </remarks>
    internal sealed class DocumentTypeDeclaration
    {
        /// <summary>How much text parameter entities may add in all before the declaration is refused.</summary>
        private const int ExpansionBudget = 4_000_000;

        /// <summary>How deep parameter entity references may nest.</summary>
        private const int NestingLimit = 64;

        private readonly Func<string, string?, string?>? m_fetch;
        private readonly Dictionary<string, ParameterEntity> m_parameterEntities = new(StringComparer.Ordinal);
        private readonly HashSet<(string Element, string Attribute)> m_declaredAttributes = new();
        private readonly HashSet<string> m_generalEntities = new(StringComparer.Ordinal);
        private int m_expanded;

        /// <summary>A parameter entity: its text, or the system identifier and base to fetch it by.</summary>
        private sealed class ParameterEntity
        {
            public string? Text;
            public string? SystemId;
            public string? BaseUri;
            public bool Fetched;
        }

        private DocumentTypeDeclaration(Func<string, string?, string?>? fetch)
        {
            m_fetch = fetch;
        }

        /// <summary>The attributes declared of type ID, each as the element's and the attribute's name as written.</summary>
        public HashSet<(string Element, string Attribute)> IdAttributes { get; } = new();

        /// <summary>The attributes declared of type IDREF or IDREFS, in the same form.</summary>
        public HashSet<(string Element, string Attribute)> IdrefAttributes { get; } = new();

        /// <summary>
        /// The elements declared to hold element content only — a content model that is neither mixed,
        /// <c>EMPTY</c> nor <c>ANY</c> — whose whitespace-only text is element content whitespace, which the
        /// data model excludes.
        /// </summary>
        public HashSet<string> ElementOnlyContent { get; } = new(StringComparer.Ordinal);

        /// <summary>The unparsed entities, by name; where one name is declared twice the first declaration stands.</summary>
        public Dictionary<string, UnparsedEntity> UnparsedEntities { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Reads a document type declaration.
        /// </summary>
        /// <param name="internalSubset">The text between the brackets of the declaration, or an empty string.</param>
        /// <param name="systemId">The system identifier of the external subset, or null where none was named.</param>
        /// <param name="baseUri">The base URI of the document, which relative identifiers resolve against.</param>
        /// <param name="fetch">
        /// What fetches an external entity by reference and base, or null where nothing external is to be
        /// read — the external subset is then not read, and an external parameter entity is dropped.
        /// </param>
        public static DocumentTypeDeclaration Read(
            string internalSubset,
            string? systemId,
            string? baseUri,
            Func<string, string?, string?>? fetch)
        {
            DocumentTypeDeclaration declaration = new DocumentTypeDeclaration(fetch);

            // The internal subset first, then the external: a declaration in the internal subset takes
            // precedence over one in the external, which the first-declaration-wins rule then gives.
            declaration.Scan(internalSubset, baseUri);

            if (systemId is not null && fetch is not null && fetch(systemId, baseUri) is string external)
            {
                declaration.Scan(external, ResolveAgainst(systemId, baseUri));
            }

            return declaration;
        }

        /// <summary>Resolves a reference against a base, or leaves it as written where it cannot be.</summary>
        internal static string ResolveAgainst(string reference, string? baseUri)
        {
            if (baseUri is null || !UriReference.TryParse(reference, out UriReference parsed))
            {
                return reference;
            }

            if (parsed.Scheme is not null)
            {
                return reference;
            }

            if (UriReference.TryParse(baseUri, out UriReference root) && root.Scheme is not null && root.Fragment is null)
            {
                return UriReference.Resolve(parsed, root);
            }

            // A base that is a path rather than a URI, as a file resolver hands back: the platform knows
            // how to make a file URI of it.
            return Uri.TryCreate(baseUri, UriKind.Absolute, out Uri? absolute)
                && Uri.TryCreate(absolute, reference, out Uri? resolved)
                ? resolved.AbsoluteUri
                : reference;
        }

        // ---- The scan ------------------------------------------------------------------------------------

        private void Scan(string text, string? baseUri)
        {
            Cursor cursor = new Cursor(this, text, baseUri);

            while (true)
            {
                SkipSpaceAndReferences(cursor);

                int c = cursor.Peek();

                if (c < 0)
                {
                    break;
                }

                if (cursor.StartsWith("<!--"))
                {
                    cursor.SkipPast("-->");
                }
                else if (cursor.StartsWith("<?"))
                {
                    cursor.SkipPast("?>");
                }
                else if (cursor.StartsWith("<!["))
                {
                    ReadConditionalSection(cursor);
                }
                else if (cursor.StartsWith("]]>"))
                {
                    // The end of an included section, whose contents were read as ordinary declarations.
                    cursor.Skip(3);
                }
                else if (cursor.StartsWith("<!ENTITY") && IsSpace(cursor.PeekAt(8)))
                {
                    ReadEntity(cursor);
                }
                else if (cursor.StartsWith("<!ATTLIST") && IsSpace(cursor.PeekAt(9)))
                {
                    ReadAttributeList(cursor);
                }
                else if (cursor.StartsWith("<!ELEMENT") && IsSpace(cursor.PeekAt(9)))
                {
                    ReadElement(cursor);
                }
                else if (c == '<')
                {
                    // An element or notation declaration, or one this does not understand.
                    SkipDeclaration(cursor);
                }
                else
                {
                    // The bracket that closes an internal subset, or anything else standing alone.
                    cursor.Next();
                }
            }
        }

        private void ReadConditionalSection(Cursor cursor)
        {
            cursor.Skip(3);
            SkipSpaceAndReferences(cursor);
            string keyword = ReadName(cursor);
            SkipSpaceAndReferences(cursor);

            if (cursor.Peek() == '[')
            {
                cursor.Next();
            }

            if (keyword != "IGNORE")
            {
                // An included section's declarations are read as they stand; its ']]>' is passed over
                // when the scan reaches it.
                return;
            }

            int depth = 1;

            while (cursor.Peek() >= 0)
            {
                if (cursor.StartsWith("<!["))
                {
                    cursor.Skip(3);
                    depth++;
                }
                else if (cursor.StartsWith("]]>"))
                {
                    cursor.Skip(3);

                    if (--depth == 0)
                    {
                        break;
                    }
                }
                else
                {
                    cursor.Next();
                }
            }
        }

        private void ReadEntity(Cursor cursor)
        {
            cursor.Skip(8);
            SkipSpaceAndReferences(cursor);

            bool parameter = false;

            if (cursor.Peek() == '%')
            {
                cursor.Next();
                parameter = true;
                SkipSpaceAndReferences(cursor);
            }

            string name = ReadName(cursor);
            string? declaredAt = cursor.BaseUri;
            SkipSpaceAndReferences(cursor);

            if (name.Length == 0)
            {
                SkipDeclaration(cursor);
                return;
            }

            if (IsQuote(cursor.Peek()))
            {
                string value = ReadLiteral(cursor, expandParameterEntities: true);

                if (parameter)
                {
                    m_parameterEntities.TryAdd(name, new ParameterEntity { Text = value, BaseUri = declaredAt, Fetched = true });
                }
                else
                {
                    m_generalEntities.Add(name);
                }

                SkipDeclaration(cursor);
                return;
            }

            string keyword = ReadName(cursor);
            string? publicId = null;

            if (keyword == "PUBLIC")
            {
                SkipSpaceAndReferences(cursor);

                if (!IsQuote(cursor.Peek()))
                {
                    SkipDeclaration(cursor);
                    return;
                }

                publicId = NormalizePublicId(ReadLiteral(cursor, expandParameterEntities: false));
            }
            else if (keyword != "SYSTEM")
            {
                SkipDeclaration(cursor);
                return;
            }

            SkipSpaceAndReferences(cursor);

            if (!IsQuote(cursor.Peek()))
            {
                SkipDeclaration(cursor);
                return;
            }

            string systemId = ReadLiteral(cursor, expandParameterEntities: false);
            SkipSpaceAndReferences(cursor);

            if (parameter)
            {
                m_parameterEntities.TryAdd(name, new ParameterEntity { SystemId = systemId, BaseUri = declaredAt });
            }
            else if (cursor.StartsWith("NDATA") && IsSpace(cursor.PeekAt(5)))
            {
                cursor.Skip(5);
                SkipSpaceAndReferences(cursor);
                string notation = ReadName(cursor);

                if (m_generalEntities.Add(name))
                {
                    UnparsedEntities[name] = new UnparsedEntity(ResolveAgainst(systemId, declaredAt), publicId, notation);
                }
            }
            else
            {
                m_generalEntities.Add(name);
            }

            SkipDeclaration(cursor);
        }

        private void ReadElement(Cursor cursor)
        {
            cursor.Skip(9);
            SkipSpaceAndReferences(cursor);
            string element = ReadName(cursor);
            SkipSpaceAndReferences(cursor);

            // A content model in parentheses is mixed where it opens with #PCDATA and element content
            // otherwise; EMPTY and ANY are neither.
            if (element.Length != 0 && cursor.Peek() == '(')
            {
                cursor.Next();
                SkipSpaceAndReferences(cursor);

                if (!cursor.StartsWith("#PCDATA"))
                {
                    ElementOnlyContent.Add(element);
                }
            }

            SkipDeclaration(cursor);
        }

        private void ReadAttributeList(Cursor cursor)
        {
            cursor.Skip(9);
            SkipSpaceAndReferences(cursor);
            string element = ReadName(cursor);

            while (true)
            {
                SkipSpaceAndReferences(cursor);
                int c = cursor.Peek();

                if (c < 0)
                {
                    return;
                }

                if (c == '>')
                {
                    cursor.Next();
                    return;
                }

                string attribute = ReadName(cursor);

                if (attribute.Length == 0)
                {
                    SkipDeclaration(cursor);
                    return;
                }

                SkipSpaceAndReferences(cursor);
                string type;

                if (cursor.Peek() == '(')
                {
                    SkipParenthesized(cursor);
                    type = string.Empty;
                }
                else
                {
                    type = ReadName(cursor);

                    if (type == "NOTATION")
                    {
                        SkipSpaceAndReferences(cursor);
                        SkipParenthesized(cursor);
                    }
                }

                SkipSpaceAndReferences(cursor);

                if (cursor.Peek() == '#')
                {
                    cursor.Next();
                    string keyword = ReadName(cursor);

                    if (keyword == "FIXED")
                    {
                        SkipSpaceAndReferences(cursor);

                        if (IsQuote(cursor.Peek()))
                        {
                            ReadLiteral(cursor, expandParameterEntities: false);
                        }
                    }
                }
                else if (IsQuote(cursor.Peek()))
                {
                    ReadLiteral(cursor, expandParameterEntities: false);
                }
                else
                {
                    SkipDeclaration(cursor);
                    return;
                }

                // The first declaration of an attribute is the one that stands (XML §3.3).
                if (m_declaredAttributes.Add((element, attribute)))
                {
                    if (type == "ID")
                    {
                        IdAttributes.Add((element, attribute));
                    }
                    else if (type is "IDREF" or "IDREFS")
                    {
                        IdrefAttributes.Add((element, attribute));
                    }
                }
            }
        }

        /// <summary>
        /// Passes over whitespace and expands parameter entity references, which may stand wherever
        /// whitespace may in the external subset, and between declarations in the internal one.
        /// </summary>
        private void SkipSpaceAndReferences(Cursor cursor)
        {
            while (true)
            {
                int c = cursor.Peek();

                if (IsSpace(c))
                {
                    cursor.Next();
                }
                else if (c == '%' && IsNameStart(cursor.PeekAt(1)))
                {
                    cursor.Next();
                    string name = ReadName(cursor);

                    if (cursor.Peek() == ';')
                    {
                        cursor.Next();
                    }

                    // Referenced outside a literal, the replacement text is padded with a space on each
                    // side (XML §4.4.8), so two declarations an entity holds do not run into what surrounds it.
                    if (ReplacementOf(name) is string replacement)
                    {
                        cursor.Push(string.Concat(" ", replacement, " "), name, m_parameterEntities[name].BaseUri);
                    }
                }
                else
                {
                    return;
                }
            }
        }

        /// <summary>The replacement text of a parameter entity, fetched on first use where it is external, or null where it cannot be had.</summary>
        private string? ReplacementOf(string name)
        {
            if (!m_parameterEntities.TryGetValue(name, out ParameterEntity? entity))
            {
                return null;
            }

            if (!entity.Fetched)
            {
                entity.Fetched = true;

                if (entity.SystemId is not null && m_fetch is not null)
                {
                    entity.Text = m_fetch(entity.SystemId, entity.BaseUri);
                    entity.BaseUri = ResolveAgainst(entity.SystemId, entity.BaseUri);
                }
            }

            return entity.Text;
        }

        /// <summary>
        /// Reads a quoted literal, which ends at the matching quote in the entity it began in — a quote
        /// inside a parameter entity's replacement text does not close a literal written outside it.
        /// </summary>
        private string ReadLiteral(Cursor cursor, bool expandParameterEntities)
        {
            int quote = cursor.Next();
            int depth = cursor.Depth;
            StringBuilder builder = new StringBuilder();

            while (true)
            {
                int c = cursor.Peek();

                if (c < 0)
                {
                    break;
                }

                if (c == quote && cursor.Depth == depth)
                {
                    cursor.Next();
                    break;
                }

                if (expandParameterEntities && c == '%' && IsNameStart(cursor.PeekAt(1)))
                {
                    cursor.Next();
                    string name = ReadName(cursor);

                    if (cursor.Peek() == ';')
                    {
                        cursor.Next();
                    }

                    // Inside a literal the replacement text is included as it is, without the padding.
                    if (ReplacementOf(name) is string replacement)
                    {
                        cursor.Push(replacement, name, m_parameterEntities[name].BaseUri);
                    }

                    continue;
                }

                builder.Append((char)cursor.Next());
            }

            return builder.ToString();
        }

        private static string ReadName(Cursor cursor)
        {
            StringBuilder builder = new StringBuilder();

            while (IsNameChar(cursor.Peek()))
            {
                builder.Append((char)cursor.Next());
            }

            return builder.ToString();
        }

        /// <summary>Skips to the end of the markup declaration the cursor stands in, quoted text included.</summary>
        private static void SkipDeclaration(Cursor cursor)
        {
            while (true)
            {
                int c = cursor.Next();

                if (c < 0 || c == '>')
                {
                    return;
                }

                if (IsQuote(c))
                {
                    while (cursor.Peek() >= 0 && cursor.Next() != c)
                    {
                    }
                }
            }
        }

        private void SkipParenthesized(Cursor cursor)
        {
            int depth = 0;

            while (true)
            {
                SkipSpaceAndReferences(cursor);
                int c = cursor.Next();

                if (c < 0)
                {
                    return;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')' && --depth <= 0)
                {
                    return;
                }
            }
        }

        /// <summary>Public identifiers compare with their whitespace normalized (XML §4.2.2).</summary>
        private static string NormalizePublicId(string publicId)
        {
            return string.Join(' ', publicId.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool IsSpace(int c) => c is ' ' or '\t' or '\n' or '\r';

        private static bool IsQuote(int c) => c is '"' or '\'';

        private static bool IsNameStart(int c)
        {
            return c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' or ':' or > 0x7F;
        }

        private static bool IsNameChar(int c)
        {
            return IsNameStart(c) || c is (>= '0' and <= '9') or '-' or '.';
        }

        /// <summary>
        /// A position in the declaration's text, with the replacement text of each parameter entity being
        /// read pushed above the text that referenced it.
        /// </summary>
        private sealed class Cursor
        {
            private readonly DocumentTypeDeclaration m_owner;
            private readonly List<Segment> m_segments = new();

            private sealed class Segment
            {
                public required string Text;
                public int Position;
                public string? Name;
                public string? BaseUri;
            }

            public Cursor(DocumentTypeDeclaration owner, string text, string? baseUri)
            {
                m_owner = owner;
                m_segments.Add(new Segment { Text = text, BaseUri = baseUri });
            }

            /// <summary>How many entities are being read, the declaration's own text counting as one.</summary>
            public int Depth => m_segments.Count;

            /// <summary>The base URI of the entity being read.</summary>
            public string? BaseUri => m_segments[^1].BaseUri;

            private Segment Current => m_segments[^1];

            public int Peek()
            {
                while (true)
                {
                    Segment top = Current;

                    if (top.Position < top.Text.Length)
                    {
                        return top.Text[top.Position];
                    }

                    if (m_segments.Count == 1)
                    {
                        return -1;
                    }

                    m_segments.RemoveAt(m_segments.Count - 1);
                }
            }

            /// <summary>The character some way ahead in the entity being read, or -1 where it ends first.</summary>
            public int PeekAt(int offset)
            {
                Peek();
                Segment top = Current;
                return top.Position + offset < top.Text.Length ? top.Text[top.Position + offset] : -1;
            }

            public int Next()
            {
                int c = Peek();

                if (c >= 0)
                {
                    Current.Position++;
                }

                return c;
            }

            public void Skip(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Next();
                }
            }

            public bool StartsWith(string text)
            {
                Peek();
                Segment top = Current;
                return string.CompareOrdinal(top.Text, top.Position, text, 0, text.Length) == 0
                    && top.Position + text.Length <= top.Text.Length;
            }

            public void SkipPast(string terminator)
            {
                while (Peek() >= 0)
                {
                    if (StartsWith(terminator))
                    {
                        Skip(terminator.Length);
                        return;
                    }

                    Next();
                }
            }

            /// <summary>
            /// Starts reading an entity's replacement text, unless it is already being read — an entity
            /// referring to itself is not well-formed, and would otherwise never end — or the nesting or the
            /// budget is exhausted.
            /// </summary>
            public void Push(string text, string name, string? baseUri)
            {
                if (m_segments.Count >= NestingLimit)
                {
                    return;
                }

                foreach (Segment segment in m_segments)
                {
                    if (segment.Name == name)
                    {
                        return;
                    }
                }

                m_owner.m_expanded += text.Length;

                if (m_owner.m_expanded > ExpansionBudget)
                {
                    throw new XmlException(
                        "The document type declaration expands its parameter entities into more text than "
                        + "this engine will read.");
                }

                m_segments.Add(new Segment { Text = text, Name = name, BaseUri = baseUri });
            }
        }
    }
}
