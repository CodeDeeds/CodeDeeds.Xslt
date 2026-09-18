using System.Buffers;
using System.Text;
using System.Text.Json;

namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// Builds an <see cref="XdmTree"/> from JSON, using the node shape that XSLT 3.0 defines for
    /// <c>json-to-xml()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// JSON becomes elements in the <c>http://www.w3.org/2005/xpath-functions</c> namespace: an object is a
    /// <c>map</c>, an array is an <c>array</c>, and the leaves are <c>string</c>, <c>number</c>,
    /// <c>boolean</c> and <c>null</c>. A member of an object carries its name in a <c>key</c> attribute.
    /// </para>
    /// <para>
    /// Following the specification's shape rather than inventing a mapping means a stylesheet written against
    /// any conformant XSLT 3.0 processor works here unchanged, and it means JSON costs the engine nothing
    /// extra: once the tree exists, every instruction, pattern and axis behaves exactly as it does for XML.
    /// </para>
    /// <para>
    /// The tree is built directly from a <see cref="Utf8JsonReader"/>, with no intermediate document.
    /// </para>
    /// </remarks>
    public static class JsonTreeBuilder
    {
        /// <summary>The namespace that <c>json-to-xml()</c> places its elements in.</summary>
        public const string XPathFunctionsNamespace = "http://www.w3.org/2005/xpath-functions";

        /// <summary>Builds a tree from JSON text.</summary>
        /// <param name="reader">The JSON to parse. The caller retains ownership and must dispose it.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <returns>The parsed tree.</returns>
        /// <exception cref="XsltException">The input is not well-formed JSON.</exception>
        public static XdmTree FromJson(TextReader reader, NameTable? nameTable = null)
        {
            return FromJson(reader.ReadToEnd(), nameTable);
        }

        /// <summary>Builds a tree from JSON text.</summary>
        /// <param name="json">The JSON to parse.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <returns>The parsed tree.</returns>
        /// <exception cref="XsltException">The input is not well-formed JSON.</exception>
        public static XdmTree FromJson(string json, NameTable? nameTable = null)
        {
            return FromUtf8(Encoding.UTF8.GetBytes(json), nameTable, new JsonToXmlOptions { Liberal = true });
        }

        /// <summary>
        /// Builds a tree from JSON text, saying whether to accept what JSON itself does not allow.
        /// </summary>
        /// <remarks>
        /// The entry points that take no such argument are liberal, because a caller handing this a file has
        /// no other way to accept a comment or a trailing comma. <c>fn:json-to-xml()</c> is strict unless the
        /// stylesheet asks otherwise, because the specification says so and because a stylesheet that wants
        /// the other reading can say <c>liberal: true</c>.
        /// </remarks>
        /// <param name="json">The JSON to parse.</param>
        /// <param name="liberal">Whether comments and trailing commas are accepted.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <returns>The parsed tree.</returns>
        /// <exception cref="XsltException"><c>FOJS0001</c> where the input is not well-formed JSON.</exception>
        public static XdmTree FromJson(string json, bool liberal, NameTable? nameTable = null)
        {
            return FromJson(json, new JsonToXmlOptions { Liberal = liberal }, nameTable);
        }

        /// <summary>
        /// Builds a tree from JSON text, by the options <c>fn:json-to-xml()</c> was given.
        /// </summary>
        /// <param name="json">The JSON to parse.</param>
        /// <param name="options">What the call said about liberal parsing, escaping and repeated keys.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <returns>The parsed tree.</returns>
        /// <exception cref="XsltException"><c>FOJS0001</c> where the input is not well-formed JSON.</exception>
        public static XdmTree FromJson(string json, JsonToXmlOptions options, NameTable? nameTable = null)
        {
            return FromText(json, options, nameTable, pooledStorage: false);
        }

        /// <summary>
        /// Builds a tree from JSON text for a transformation that will release it, taking the node storage
        /// from the shared pool; see <see cref="XdmTree.ReleaseStorage"/>.
        /// </summary>
        internal static XdmTree FromJsonPooled(string json)
        {
            return FromText(json, new JsonToXmlOptions { Liberal = true }, null, pooledStorage: true);
        }

        /// <summary>Builds a tree from a reader's JSON for a transformation that will release it.</summary>
        internal static XdmTree FromJsonPooled(TextReader reader)
        {
            return FromText(reader.ReadToEnd(), new JsonToXmlOptions { Liberal = true }, null, pooledStorage: true);
        }

        /// <summary>Builds a tree from a stream's JSON for a transformation that will release it.</summary>
        internal static XdmTree FromJsonPooled(Stream stream)
        {
            return FromStream(stream, null, pooledStorage: true);
        }

        private static XdmTree FromText(string json, JsonToXmlOptions options, NameTable? nameTable, bool pooledStorage)
        {
            // Encoded into borrowed space rather than a fresh array: the bytes are read once, while the tree
            // is built, and a document of any size made them a large-object allocation per parse.
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(json.Length));

            try
            {
                int count = Encoding.UTF8.GetBytes(json, 0, json.Length, buffer, 0);
                return FromUtf8(new ReadOnlySpan<byte>(buffer, 0, count), nameTable, options, pooledStorage);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>What <c>fn:json-to-xml()</c> was told about how to read its input.</summary>
        public readonly struct JsonToXmlOptions
        {
            /// <summary>Whether comments and trailing commas are accepted.</summary>
            public bool Liberal { get; init; }

            /// <summary>
            /// Whether a character XML cannot hold stays in the JSON escape it was written with, marking its
            /// element <c>escaped</c>, rather than going through <see cref="Fallback"/>.
            /// </summary>
            public bool Escape { get; init; }

            /// <summary>
            /// What stands in for a character XML cannot hold, given the escape sequence that named it.
            /// Null for the default, which is the replacement character.
            /// </summary>
            public Func<string, string>? Fallback { get; init; }

            /// <summary>
            /// What a key written twice means: <c>retain</c> keeps both entries, <c>use-first</c> the first,
            /// and <c>reject</c> refuses the document. Null is <c>retain</c>, which is the default.
            /// </summary>
            public string? Duplicates { get; init; }

            /// <summary>
            /// Whether the tree is to be typed, which is what <c>validate</c> asks: every <c>string</c> then
            /// says whether it is <c>escaped</c>, and every element with a <c>key</c> whether the key is,
            /// <c>false</c> as well as <c>true</c>.
            /// </summary>
            /// <remarks>
            /// F&amp;O 3.1 §17.5.3: "If the result is typed, every element named string will have an
            /// attribute named escaped whose value is either true or false, and every element having an
            /// attribute named key will also have an attribute named escaped-key whose value is either true
            /// or false." An untyped result carries the two only where they say <c>true</c>. The schema
            /// declares both with a default of <c>false</c>, so these are the attributes validation would
            /// create; writing them as the tree is built keeps the result the tree that was built, on the
            /// name table it was built on, rather than a copy of it made to add them.
            /// </remarks>
            public bool Typed { get; init; }
        }

        /// <summary>
        /// Builds a tree from JSON bytes.
        /// </summary>
        /// <param name="stream">The JSON to parse. The caller retains ownership and must dispose it.</param>
        /// <param name="nameTable">The table used to intern names, or <see langword="null"/> to create one.</param>
        /// <returns>The parsed tree.</returns>
        /// <exception cref="XsltException">The input is not well-formed JSON.</exception>
        /// <remarks>
        /// <para>
        /// Preferred over the text overloads where the bytes are what the caller has: JSON is UTF-8 by
        /// definition, so decoding to a string only to encode it back is two conversions of the whole
        /// document to arrive where it started.
        /// </para>
        /// <para>
        /// The stream is read forwards only and is never sought, and the whole of it is read: the tree is
        /// built before anything executes, so this saves the intermediate string rather than the document.
        /// </para>
        /// </remarks>
        public static XdmTree FromJson(Stream stream, NameTable? nameTable = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return FromStream(stream, nameTable, pooledStorage: false);
        }

        private static XdmTree FromStream(Stream stream, NameTable? nameTable, bool pooledStorage)
        {
            // Rented rather than allocated, and grown by doubling: a stream need not know its own length, and
            // asking one that does not is how a length-based read comes back short.
            byte[] buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
            int filled = 0;

            try
            {
                while (true)
                {
                    if (filled == buffer.Length)
                    {
                        byte[] larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        Buffer.BlockCopy(buffer, 0, larger, 0, filled);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = larger;
                    }

                    int read = stream.Read(buffer, filled, buffer.Length - filled);
                    if (read == 0)
                    {
                        break;
                    }

                    filled += read;
                }

                return FromUtf8(
                    new ReadOnlySpan<byte>(buffer, 0, filled),
                    nameTable,
                    new JsonToXmlOptions { Liberal = true },
                    pooledStorage);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Builds a tree from the UTF-8 bytes of a JSON document.
        /// </summary>
        /// <remarks>
        /// The one place a <see cref="Utf8JsonReader"/> is constructed, so that every route into this builder
        /// reads the same JSON the same way. The two options matter: without them a comment and a trailing
        /// comma are parse errors, and the same document would be accepted or refused according to which
        /// overload a caller happened to reach for.
        /// </remarks>
        private static XdmTree FromUtf8(
            ReadOnlySpan<byte> utf8, NameTable? nameTable, JsonToXmlOptions settings, bool pooledStorage = false)
        {
            bool liberal = settings.Liberal;

            // A byte-order mark is legal in a file and is not part of the JSON. Utf8JsonReader does not skip
            // one, so a document saved by an editor that writes them would fail on its first byte.
            if (utf8.StartsWith(Utf8ByteOrderMark))
            {
                utf8 = utf8[Utf8ByteOrderMark.Length..];
            }

            JsonReaderOptions options = new JsonReaderOptions
            {
                CommentHandling = liberal ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
                AllowTrailingCommas = liberal,
            };

            (int nodes, int attributes) = Estimate(utf8);
            XdmTreeBuilder builder = new XdmTreeBuilder(nameTable, nodes, attributes, pooledStorage);
            Utf8JsonReader jsonReader = new Utf8JsonReader(utf8, options);
            KeyCache keys = new KeyCache();

            try
            {
                if (!jsonReader.Read())
                {
                    throw XsltErrors.Error(XsltErrorCode.FOJS0001, "The JSON input is empty.");
                }

                WriteValue(ref jsonReader, builder, null, settings, keys);

                if (jsonReader.Read())
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOJS0001,
                        "The JSON input has more after its value than whitespace. A JSON document is one "
                        + "value, and two side by side is not one.");
                }
            }
            catch (JsonException exception)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOJS0001,
                    $"The JSON input could not be parsed: {exception.Message}",
                    exception);
            }

            return builder.Finish();
        }

        /// <summary>The UTF-8 encoding of U+FEFF, which a file may begin with and a JSON document may not.</summary>
        /// <summary>
        /// Guesses how many nodes and attributes the JSON will make, from its punctuation.
        /// </summary>
        /// <remarks>
        /// Every value but the first of a container follows a comma, a scalar makes an element and a text
        /// node, and a container makes an element and is itself a value; every key makes an attribute. So
        /// twice the commas plus three times the opening brackets is close for the flat, record-like
        /// documents that are the common case, and over rather than under for the rest — punctuation
        /// inside strings counts too, and costs only slack. As with XML, an estimate that is off costs a
        /// doubling or a trim, which is what every document paid before there was one.
        /// </remarks>
        private static (int Nodes, int Attributes) Estimate(ReadOnlySpan<byte> utf8)
        {
            long commas = utf8.Count((byte)',');
            long colons = utf8.Count((byte)':');
            long opens = utf8.Count((byte)'{') + utf8.Count((byte)'[');

            return (
                (int)Math.Min(2 * commas + 3 * opens + 8, int.MaxValue / 2),
                (int)Math.Min(colons + 8, int.MaxValue / 2));
        }

        private static ReadOnlySpan<byte> Utf8ByteOrderMark => new byte[] { 0xEF, 0xBB, 0xBF };

        /// <summary>
        /// The buffer a stream is first read into. Large enough that a small document is read in one call and
        /// a large one doubles only a few times, and small enough to be worth pooling.
        /// </summary>
        private const int InitialBufferSize = 16 * 1024;

        /// <summary>
        /// The keys most recently read, so that a key repeated in every record — which is every key of a
        /// record-like document — is one string rather than one per occurrence.
        /// </summary>
        /// <remarks>
        /// The reader compares a token against a string without making one of it, so a hit costs a few
        /// short comparisons and a miss costs what reading the key cost anyway. Escaped keys are read
        /// afresh: what the reader would compare is its own unescaping, and the string kept is this
        /// engine's, which need not agree on malformed input.
        /// </remarks>
        private sealed class KeyCache
        {
            private const int Size = 16;
            private readonly string?[] m_keys = new string?[Size];
            private readonly byte[]?[] m_utf8 = new byte[]?[Size];
            private int m_next;
            private int m_last;

            public string Read(ref Utf8JsonReader reader)
            {
                if (reader.ValueIsEscaped)
                {
                    return JsonText.Read(ref reader);
                }

                // The keys of one record come in the order they came in the last, so the slot after the
                // last hit is tried first, and the comparison is of the raw bytes, which for an unescaped
                // key are the text.
                ReadOnlySpan<byte> value = reader.ValueSpan;

                for (int step = 0; step < Size; step++)
                {
                    int slot = (m_last + 1 + step) % Size;
                    byte[]? bytes = m_utf8[slot];

                    if (bytes is not null && value.SequenceEqual(bytes))
                    {
                        m_last = slot;
                        return m_keys[slot]!;
                    }
                }

                string fresh = JsonText.Read(ref reader);
                m_keys[m_next] = fresh;
                m_utf8[m_next] = value.ToArray();
                m_last = m_next;
                m_next = (m_next + 1) % Size;
                return fresh;
            }
        }

        private static void WriteValue(
            ref Utf8JsonReader reader, XdmTreeBuilder builder, string? key, JsonToXmlOptions settings, KeyCache keys)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                {
                    StartElement(builder, "map", key, settings);

                    // Only where the document says what a repeated key means: retaining both, which is the
                    // default here, costs nothing and needs no record of what has been seen.
                    HashSet<string>? seen = settings.Duplicates is null or "retain"
                        ? null
                        : new HashSet<string>(StringComparer.Ordinal);

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        string propertyName = keys.Read(ref reader);
                        reader.Read();

                        if (seen is not null && !seen.Add(propertyName))
                        {
                            if (settings.Duplicates == "reject")
                            {
                                throw XsltErrors.Error(
                                    XsltErrorCode.FOJS0003,
                                    $"The key '{propertyName}' is written twice in one JSON object, and the "
                                    + "call said a repeated key is to be rejected.");
                            }

                            // use-first: the entry that wins is the one that does not depend on how far the
                            // parser got, so the later one is read past and dropped.
                            reader.Skip();
                            continue;
                        }

                        WriteValue(ref reader, builder, propertyName, settings, keys);
                    }

                    builder.EndElement();
                    return;
                }

                case JsonTokenType.StartArray:
                    StartElement(builder, "array", key, settings);
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        // Array members are positional, so they carry no key.
                        WriteValue(ref reader, builder, null, settings, keys);
                    }

                    builder.EndElement();
                    return;

                case JsonTokenType.String:
                {
                    string text = Represent(JsonText.Read(ref reader), settings.Escape, settings.Fallback, out bool escaped);
                    StartElement(builder, "string", key, settings);

                    if (escaped || settings.Typed)
                    {
                        builder.AddAttribute(string.Empty, string.Empty, "escaped", escaped ? "true" : "false");
                    }

                    builder.AddText(text);
                    builder.EndElement();
                    return;
                }

                case JsonTokenType.Number:
                    StartElement(builder, "number", key, settings);

                    // Preserve the number exactly as written rather than round-tripping it through a double.
                    builder.AddText(Encoding.UTF8.GetString(reader.ValueSpan));
                    builder.EndElement();
                    return;

                case JsonTokenType.True:
                case JsonTokenType.False:
                    StartElement(builder, "boolean", key, settings);
                    builder.AddText(reader.TokenType == JsonTokenType.True ? "true" : "false");
                    builder.EndElement();
                    return;

                case JsonTokenType.Null:
                    StartElement(builder, "null", key, settings);
                    builder.EndElement();
                    return;

                default:
                    throw new XsltException($"Unexpected JSON token '{reader.TokenType}'.");
            }
        }

        private static void StartElement(
            XdmTreeBuilder builder, string localName, string? key, JsonToXmlOptions settings)
        {
            builder.StartElement(string.Empty, XPathFunctionsNamespace, localName);

            if (key is null)
            {
                return;
            }

            string written = Represent(key, settings.Escape, settings.Fallback, out bool escaped);

            if (escaped || settings.Typed)
            {
                builder.AddAttribute(string.Empty, string.Empty, "escaped-key", escaped ? "true" : "false");
            }

            builder.AddAttribute(string.Empty, string.Empty, "key", written);
        }

        /// <summary>
        /// Writes a JSON string as XML can hold it, saying whether anything had to stay escaped.
        /// </summary>
        /// <remarks>
        /// JSON admits characters XML does not: the C0 controls other than tab, newline and return, and an
        /// unpaired surrogate. The call decides which of the two answers it wants. Asking to escape leaves
        /// such a character in the JSON escape it was written with — and doubles every backslash, so that
        /// what comes out can be read back — and marks the element it belongs to. Not asking hands the
        /// escape sequence to the fallback function, which by default answers with the replacement
        /// character (F&amp;O 3.1 §17.5).
        /// </remarks>
        /// <param name="value">The string as JSON meant it, with the escapes already read.</param>
        /// <param name="settings">What the call said.</param>
        /// <param name="escaped">Set where something was left escaped.</param>
        internal static string Represent(
            string value, bool escape, Func<string, string>? fallback, out bool escaped)
        {
            escaped = false;

            if (!NeedsRepresenting(value, escape))
            {
                return value;
            }

            StringBuilder result = new StringBuilder(value.Length + 8);

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool keepEscaped = escape
                    && (!IsXmlCharacter(value, i) || KeptEscaped(character));

                if (!keepEscaped && IsXmlCharacter(value, i))
                {
                    result.Append(character);
                    continue;
                }

                string sequence = EscapeOf(character);

                if (keepEscaped)
                {
                    result.Append(sequence);
                    escaped = true;
                    continue;
                }

                result.Append(fallback is null ? "\uFFFD" : fallback(sequence));
            }

            return result.ToString();
        }

        /// <summary>
        /// Whether a character keeps the escape it arrived in, where <c>escape</c> was asked for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Tab, newline and return are valid XML characters and are still at risk: attribute-value
        /// normalization turns each of them into a space, and a parser normalizes line endings in
        /// content. Keeping the escape is what carries them through unchanged. The backslash is here
        /// for a different reason — XML does nothing to it, but a lone one in the result would read
        /// as the start of an escape that is not there, so it is doubled.
        /// </para>
        /// <para>
        /// The quotation mark is deliberately absent. JSON needs it escaped inside a string and XML
        /// does not, and it is XML the result is being written into: the suite asks outright for
        /// <c>Data with " within it</c> and not for the escape it was written with. The characters
        /// XML cannot carry at all are handled beside this rather than in it.
        /// </para>
        /// </remarks>
        private static bool KeptEscaped(char character)
        {
            return character is '\t' or '\n' or '\r' or '\\';
        }

        /// <summary>Whether a string holds anything the representation has to do something about.</summary>
        private static bool NeedsRepresenting(string value, bool escape)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (!IsXmlCharacter(value, i) || (escape && KeptEscaped(value[i])))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the character at one position is one XML admits, a surrogate pair included.</summary>
        private static bool IsXmlCharacter(string value, int index)
        {
            char character = value[index];

            if (char.IsSurrogate(character))
            {
                return char.IsHighSurrogate(character)
                    ? index + 1 < value.Length && char.IsLowSurrogate(value[index + 1])
                    : index > 0 && char.IsHighSurrogate(value[index - 1]);
            }

            return character is '\t' or '\n' or '\r'
                || (character >= ' ' && character <= '\uD7FF')
                || (character >= '\uE000' && character <= '\uFFFD');
        }

        /// <summary>The JSON escape sequence naming one character, in the shortest form JSON has for it.</summary>
        private static string EscapeOf(char character)
        {
            // The five the grammar gives a two-character form. Written that way rather than as a
            // \u0000-style escape because that is how they came in and how anything reading the
            // result back will expect them.
            return character switch
            {
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\\' => "\\\\",
                _ => "\\u" + ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture),
            };
        }
    }
}
