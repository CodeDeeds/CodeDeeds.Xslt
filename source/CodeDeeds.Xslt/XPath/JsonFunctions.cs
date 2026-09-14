using System.Globalization;
using System.Text;
using System.Text.Json;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The four functions XPath 3.1 gives for reading and writing JSON.</summary>
    internal enum JsonFunction : byte
    {
        /// <summary><c>fn:parse-json</c>, and <c>fn:json-doc</c>, which is this over a retrieved resource.</summary>
        ParseJson,

        /// <summary><c>fn:json-to-xml</c>.</summary>
        JsonToXml,

        /// <summary><c>fn:xml-to-json</c>.</summary>
        XmlToJson,
    }

    /// <summary>
    /// A call to one of the JSON functions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There are two ways to bring JSON into XPath and the specification gives both, because they answer
    /// different questions. <c>fn:parse-json</c> yields maps and arrays, which is what you want when the JSON
    /// <em>is</em> your data. <c>fn:json-to-xml</c> yields a node tree of <c>map</c>, <c>array</c>,
    /// <c>string</c>, <c>number</c>, <c>boolean</c> and <c>null</c> elements, which is what you want when you
    /// have a stylesheet and would rather write templates than lookups.
    /// </para>
    /// <para>
    /// This engine had the second before it had maps: <c>Xslt.TransformJson</c> builds exactly that structure
    /// and has since the JSON support landed. So <c>fn:json-to-xml</c> is that builder given a name, and
    /// <c>fn:parse-json</c> is the same reader over the data model maps and arrays added.
    /// </para>
    /// </remarks>
    internal sealed class JsonFunctionExpr : Expr
    {
        private readonly JsonFunction m_function;
        private readonly Expr[] m_arguments;
        private readonly string m_name;

        private JsonFunctionExpr(JsonFunction function, Expr[] arguments, string name)
        {
            m_function = function;
            m_arguments = arguments;
            m_name = name;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <summary>The function a name denotes, with the types its arguments are converted to.</summary>
        /// <remarks>
        /// One table, read both by <see cref="TryCreate"/> and by <see cref="TakesArity"/>, so that what
        /// <c>fn:function-available()</c> reports and what a call may be written cannot come apart.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        private static (JsonFunction Function, string Signature)? Find(string name)
        {
            return name switch
            {
                "parse-json" => (JsonFunction.ParseJson, "xs:string?, item()*"),
                "json-doc" => (JsonFunction.ParseJson, "xs:string?, item()*"),
                "json-to-xml" => (JsonFunction.JsonToXml, "xs:string?, item()*"),
                "xml-to-json" => (JsonFunction.XmlToJson, "node()?, item()*"),
                _ => null,
            };
        }

        /// <summary>Whether a function of this name will take a given number of arguments.</summary>
        /// <remarks>
        /// All four take the value and an optional map of options, so the arity is the same for each of
        /// them; only the name has to be looked up.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        /// <param name="arity">How many arguments the caller is asking about.</param>
        public static bool TakesArity(string name, int arity)
        {
            return arity is 1 or 2 && Find(name) is not null;
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => m_function == JsonFunction.JsonToXml;

        /// <summary>
        /// Creates a call, or returns <see langword="null"/> where the name is not one of these.
        /// </summary>
        /// <param name="name">The function's local name.</param>
        /// <param name="arguments">The compiled arguments.</param>
        /// <param name="version">The version in force, for the call <c>fn:json-doc</c> delegates.</param>
        public static Expr? TryCreate(string name, Expr[] arguments, XsltVersion version)
        {
            if (Find(name) is not (JsonFunction function, string signature))
            {
                return null;
            }

            if (arguments.Length is < 1 or > 2)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'fn:{name}()' takes one or two arguments, and was given {arguments.Length}.");
            }

            // json-doc is parse-json over a retrieved resource, and writing it that way means the resolver,
            // the encoding handling and the errors for a missing document are the ones unparsed-text already
            // has rather than a second set that would drift from them.
            if (name == "json-doc")
            {
                Expr text = Xpath2FunctionExpr.TryCreate(
                    "unparsed-text", new[] { arguments[0] }, version)!;

                arguments = arguments.Length == 1 ? new[] { text } : new[] { text, arguments[1] };
            }

            return new JsonFunctionExpr(
                function,
                CheckedArgumentExpr.Wrap(
                    arguments, FunctionParameter.Parse(signature), name, version),
                name);
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XdmMap options = ReadOptions(ref context);

            if (m_function == JsonFunction.XmlToJson)
            {
                XPathValue node = m_arguments[0].Evaluate(ref context);

                return Xpath2FunctionExpr.IsEmptySequence(node)
                    ? node
                    : XPathValue.FromString(WriteJson(node, options));
            }

            XPathValue json = m_arguments[0].Evaluate(ref context);

            // Declared xs:string? in both, so nothing in is nothing out — which for json-doc means a
            // document that was not there, unparsed-text() having already refused what it could not read.
            if (Xpath2FunctionExpr.IsEmptySequence(json))
            {
                return json;
            }

            string text = json.ToStringValue();
            bool liberal = Flag(options, "liberal", false);

            if (m_function == JsonFunction.JsonToXml)
            {
                bool validate = Flag(options, "validate", false);

                // Validating the result against the schema needs a schema-aware processor with the schema
                // for the XPath functions namespace in scope; without one, asking for it is the error the
                // specification gives (FOJS0004).
                Compiler.SchemaComponents? schemas = validate ? context.Runtime?.Schemas : null;

                if (validate && (schemas is null || schemas.FindElement("http://www.w3.org/2005/xpath-functions", "map") is null))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOJS0004,
                        "fn:json-to-xml() was asked to validate its result, and the schema for the XPath "
                        + "functions namespace is not in scope. Make the processor schema-aware and import "
                        + "that namespace's schema.");
                }

                JsonTreeBuilder.JsonToXmlOptions settings = new JsonTreeBuilder.JsonToXmlOptions
                {
                    Liberal = liberal,
                    Escape = Flag(options, "escape", false),
                    Fallback = Fallback(options, ref context),
                    Duplicates = Duplicates(options, "retain", "reject", "use-first", "retain"),
                };

                XdmTree tree = JsonTreeBuilder.FromJson(
                    text, settings, context.Runtime is null ? null : context.Tree.NameTable);

                if (validate)
                {
                    // The XML a well-formed JSON document makes is valid against the schema, so this
                    // annotates rather than refuses; a validity failure would be FOJS0004.
                    Model.TypeOverlay overlay;

                    try
                    {
                        overlay = new Compiler.NodeValidator(schemas!).ValidateDocument(tree, strict: true);
                    }
                    catch (XsltException failed)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.FOJS0004,
                            $"fn:json-to-xml() produced a result that is not valid against the JSON schema: {failed.Message}",
                            failed);
                    }

                    tree = tree.WithTypeAnnotations(overlay);
                }

                return XPathValue.FromNodeSet(NodeSet.Singleton(tree, XdmTree.RootNode));
            }

            return ParseJson(
                text, liberal, Duplicates(options, "use-first", "reject", "use-first", "use-last", "use-any"));
        }

        // ---- Options -----------------------------------------------------------------------------------

        /// <summary>Reads the options map, which every one of these functions takes as a second argument.</summary>
        private XdmMap ReadOptions(ref DynamicContext context)
        {
            if (m_arguments.Length < 2)
            {
                return XdmMap.Empty;
            }

            XPathValue value = m_arguments[1].Evaluate(ref context);

            if (value.Kind == XPathValueKind.Map)
            {
                return value.AsMap();
            }

            throw XsltErrors.Error(
                XsltErrorCode.XPTY0004,
                $"The second argument of fn:{m_name}() is a map of options, and this is not a map.");
        }

        private bool Flag(XdmMap options, string name, bool fallback)
        {
            XPathValue key = XPathValue.FromString(name);

            if (!options.Contains(key))
            {
                return fallback;
            }

            // An option that is there is checked as an argument would be: exactly one xs:boolean, an untyped
            // value cast to one — so '2' fails as a cast does — and a string is a type error however it reads.
            // Absent means the default; an empty sequence written in does not, as the suite's C102 has it.
            List<XPathValue> items = XdmSequence.Items(options.Get(key));

            if (items.Count != 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"The '{name}' option of fn:{m_name}() is one xs:boolean, and this is {items.Count} items.");
            }

            XPathValue value = items[0];

            if (value.TypeCode == XdmTypeCode.UntypedAtomic && XdmType.TryGet("boolean", out XdmType.BuiltInType boolean))
            {
                value = XdmType.Cast(value, boolean);
            }

            if (value.Kind == XPathValueKind.Boolean)
            {
                return value.ToBoolean();
            }

            throw XsltErrors.Error(
                XsltErrorCode.XPTY0004,
                $"The '{name}' option of fn:{m_name}() is an xs:boolean, and this is not one.");
        }

        /// <summary>
        /// Reads the <c>duplicates</c> option, which says what a key written twice means.
        /// </summary>
        /// <remarks>
        /// The two functions do not admit the same answers, and cannot. A map holds one entry per key, so
        /// <c>fn:parse-json()</c> has to say which of the two wins and defaults to <c>use-first</c>, the
        /// answer that does not depend on how far the parser got. The XML representation has no such
        /// difficulty — two elements can carry one key — so <c>fn:json-to-xml()</c> keeps both by default and
        /// takes <c>retain</c> where the other takes <c>use-last</c> and <c>use-any</c> (F&amp;O 3.1 §17.5).
        /// </remarks>
        /// <param name="options">The options map.</param>
        /// <param name="fallback">What the option means where it is not written.</param>
        /// <param name="allowed">The answers this function admits.</param>
        private string Duplicates(XdmMap options, string fallback, params string[] allowed)
        {
            XPathValue key = XPathValue.FromString("duplicates");

            if (!options.Contains(key))
            {
                return fallback;
            }

            List<XPathValue> items = XdmSequence.Items(options.Get(key));

            if (items.Count == 0)
            {
                return fallback;
            }

            if (items.Count != 1 || !IsTextual(items[0]))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"The 'duplicates' option of fn:{m_name}() is one xs:string, and this is not one.");
            }

            string chosen = items[0].ToStringValue();

            return Array.IndexOf(allowed, chosen) >= 0
                ? chosen
                : throw XsltErrors.Error(
                    XsltErrorCode.FOJS0005,
                    $"'{chosen}' is not one of the ways fn:{m_name}() handles a repeated key.");
        }

        /// <summary>Whether a value is a string, or the untyped that a string is what one reads it as.</summary>
        private static bool IsTextual(XPathValue value)
        {
            return value.Kind == XPathValueKind.String
                || value.TypeCode is XdmTypeCode.UntypedAtomic or XdmTypeCode.AnyUri;
        }

        /// <summary>
        /// Reads the <c>fallback</c> option: what stands in for a character XML cannot hold.
        /// </summary>
        /// <remarks>
        /// One function of one argument, called with the JSON escape sequence that named the character and
        /// answering with what to write instead. It is called from inside the tree being built, where there
        /// is no focus to speak of and none is wanted: the specification asks for a function of its argument
        /// and nothing else, so a context of the caller's tree is all it is given.
        /// </remarks>
        /// <param name="options">The options map.</param>
        /// <param name="context">The context the function is called in.</param>
        private Func<string, string>? Fallback(XdmMap options, ref DynamicContext context)
        {
            XPathValue key = XPathValue.FromString("fallback");

            if (!options.Contains(key))
            {
                return null;
            }

            List<XPathValue> items = XdmSequence.Items(options.Get(key));

            if (items.Count != 1 || !items[0].IsFunctionItem)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"The 'fallback' option of fn:{m_name}() is one function of one argument, and this is "
                    + "not one.");
            }

            XdmFunction function = items[0].AsFunction();
            XdmTree tree = context.Tree;
            int[] fingerprints = context.FingerprintMap;
            XsltRuntime? runtime = context.Runtime;

            return escape =>
            {
                // Built here rather than captured: a context is a ref struct and cannot be held by a
                // closure, and there is nothing of the focus this call is entitled to anyway.
                DynamicContext inner = new DynamicContext(tree, DynamicContext.NotANode, fingerprints)
                {
                    Runtime = runtime,
                };

                return function.Call(new[] { XPathValue.FromString(escape) }, ref inner).ToStringValue();
            };
        }

        // ---- Reading -----------------------------------------------------------------------------------

        /// <summary>
        /// Parses JSON into the data model: an object is a map, an array is an array, and <c>null</c> is the
        /// empty sequence.
        /// </summary>
        /// <remarks>
        /// <c>null</c> becoming nothing rather than a value of its own is the one mapping that surprises.
        /// The data model has no null, and inventing one would put a value into every sequence that a
        /// stylesheet would then have to test for; the empty sequence is what "there is nothing here" already
        /// means. The cost is that a map entry bound to <c>null</c> is indistinguishable from one bound to
        /// nothing, which <c>map:contains()</c> can still tell apart.
        /// </remarks>
        private static XPathValue ParseJson(string text, bool liberal, string duplicates)
        {
            JsonReaderOptions options = new JsonReaderOptions
            {
                CommentHandling = liberal ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
                AllowTrailingCommas = liberal,
            };

            Utf8JsonReader reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), options);

            try
            {
                if (!reader.Read())
                {
                    throw XsltErrors.Error(XsltErrorCode.FOJS0001, "The JSON text is empty.");
                }

                XPathValue value = ReadValue(ref reader, duplicates);

                if (reader.Read())
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOJS0001,
                        "The JSON text has more after its value than whitespace. A JSON document is one "
                        + "value, and two side by side is not one.");
                }

                return value;
            }
            catch (JsonException exception)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOJS0001,
                    $"The JSON text could not be parsed: {exception.Message}",
                    exception);
            }
        }

        private static XPathValue ReadValue(ref Utf8JsonReader reader, string duplicates)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                {
                    List<KeyValuePair<XPathValue, XPathValue>> entries =
                        new List<KeyValuePair<XPathValue, XPathValue>>();

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        string key = JsonText.Read(ref reader);
                        reader.Read();

                        entries.Add(new KeyValuePair<XPathValue, XPathValue>(
                            XPathValue.FromString(key), ReadValue(ref reader, duplicates)));
                    }

                    return XPathValue.FromMap(XdmMap.Build(entries, duplicates));
                }

                case JsonTokenType.StartArray:
                {
                    List<XPathValue> members = new List<XPathValue>();

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        members.Add(ReadValue(ref reader, duplicates));
                    }

                    return XPathValue.FromArray(
                        members.Count == 0 ? XdmArray.Empty : new XdmArray(members.ToArray()));
                }

                case JsonTokenType.String:
                    return XPathValue.FromString(JsonText.Read(ref reader));

                case JsonTokenType.Number:
                    // Every JSON number becomes an xs:double, whatever it looks like: JSON has one numeric
                    // type and reading 1 as an integer would make the type depend on how it happened to be
                    // written on the other side of the wire.
                    return XPathValue.FromNumber(ReadNumber(ref reader));

                case JsonTokenType.True:
                    return XPathValue.FromBoolean(true);

                case JsonTokenType.False:
                    return XPathValue.FromBoolean(false);

                case JsonTokenType.Null:
                    return XPathValue.FromSequence(XdmSequence.Empty);

                default:
                    throw XsltErrors.Error(
                        XsltErrorCode.FOJS0001, $"'{reader.TokenType}' is not where a JSON value can start.");
            }
        }

        private static double ReadNumber(ref Utf8JsonReader reader)
        {
            string written = Encoding.UTF8.GetString(reader.ValueSpan);

            // Beyond the range of a double the answer is an infinity rather than a failure, which is what
            // casting the same digits to xs:double gives.
            return double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                ? number
                : written.StartsWith('-') ? double.NegativeInfinity : double.PositiveInfinity;
        }

        // ---- Writing -----------------------------------------------------------------------------------

        /// <summary>
        /// Writes the JSON a node structure stands for, which is <c>fn:json-to-xml</c> read backwards.
        /// </summary>
        private string WriteJson(XPathValue node, XdmMap options)
        {
            bool indent = Flag(options, "indent", false);

            XdmTree tree = node.Kind == XPathValueKind.Node ? node.NodeTree : node.AsNodeSet().Tree;
            int id = node.Kind == XPathValueKind.Node ? node.NodeId : node.AsNodeSet()[0];

            // A document node stands for the element it holds, so that the result of json-to-xml() goes
            // straight back in without a step being written to reach inside it.
            if (tree.KindOf(id) == NodeKind.Root)
            {
                id = OnlyElementChildOf(tree, id)
                    ?? throw Invalid("A document node stands for the one element in it, and this one holds "
                        + "no element, or more than one, or text beside it.");
            }

            StringBuilder json = new StringBuilder();
            Write(tree, id, json, indent, 0, inMap: false);
            return json.ToString();
        }

        /// <summary>Writes one element as the JSON value it stands for.</summary>
        /// <param name="tree">The tree.</param>
        /// <param name="id">The element.</param>
        /// <param name="json">Where to write.</param>
        /// <param name="indent">Whether to lay the result out.</param>
        /// <param name="depth">How deep the value is, for the layout.</param>
        /// <param name="inMap">Whether the element is a member of a map, which is where a key belongs.</param>
        private void Write(XdmTree tree, int id, StringBuilder json, bool indent, int depth, bool inMap)
        {
            if (tree.KindOf(id) != NodeKind.Element
                || !string.Equals(
                    NamespaceOf(tree, id),
                    JsonTreeBuilder.XPathFunctionsNamespace,
                    StringComparison.Ordinal))
            {
                throw Invalid(
                    "Only an element in the XPath functions namespace stands for JSON, and this is not one.");
            }

            string local = LocalNameOf(tree, id);
            CheckAttributes(tree, id, local, inMap);

            switch (local)
            {
                case "map":
                    WriteBraced(tree, id, json, indent, depth, '{', '}', named: true);
                    return;

                case "array":
                    WriteBraced(tree, id, json, indent, depth, '[', ']', named: false);
                    return;

                case "string":
                    RequireTextOnly(tree, id, local);
                    WriteString(tree, id, json, "escaped");
                    return;

                case "number":
                {
                    RequireTextOnly(tree, id, local);
                    string written = tree.StringValueOf(id).Trim();

                    // The number is an xs:double, and is written as XPath writes one — 007 is 7, .001 is
                    // 0.001 and 1E6 is 1.0E6 — which is what fn:json-to-xml would have made of the same
                    // value. JSON has no NaN and no infinity, so those are not numbers here.
                    XPathValue value;
                    try
                    {
                        value = XdmType.TryGet("double", out XdmType.BuiltInType type)
                            ? XdmType.Cast(XPathValue.FromUntypedAtomic(written), type)
                            : XPathValue.FromNumber(double.NaN);
                    }
                    catch (XsltException)
                    {
                        throw Invalid($"'{written}' is not a number JSON can hold.");
                    }

                    double number = value.ToNumber();
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw Invalid($"'{written}' is not a number JSON can hold.");
                    }

                    json.Append(value.ToCanonicalString());
                    return;
                }

                case "boolean":
                {
                    RequireTextOnly(tree, id, local);
                    json.Append(ReadBoolean(tree.StringValueOf(id), "a boolean") ? "true" : "false");
                    return;
                }

                case "null":
                    // Empty, apart from whatever whitespace and comments the layout left: a null with
                    // content in it is not a null.
                    if (ElementChildOf(tree, id) is not null || tree.StringValueOf(id).Trim().Length != 0)
                    {
                        throw Invalid("A null holds nothing, and this one has content.");
                    }

                    json.Append("null");
                    return;

                default:
                    throw Invalid($"'{local}' is not one of the six elements JSON maps to.");
            }
        }

        /// <summary>
        /// Refuses an attribute the structure does not have: one in no namespace that is not the element's,
        /// or one claiming the functions namespace. An attribute in any other namespace — <c>xsi:type</c>,
        /// <c>xml:space</c> — is not the structure's business and passes.
        /// </summary>
        private void CheckAttributes(XdmTree tree, int element, string local, bool inMap)
        {
            int count = tree.AttributeCountOf(element);

            for (int i = 0; i < count; i++)
            {
                int attribute = tree.AttributeAt(element, i);
                string uri = NamespaceOf(tree, attribute);
                string name = LocalNameOf(tree, attribute);

                if (uri.Length != 0)
                {
                    if (string.Equals(uri, JsonTreeBuilder.XPathFunctionsNamespace, StringComparison.Ordinal))
                    {
                        throw Invalid($"There is no '{name}' attribute in the functions namespace.");
                    }

                    continue;
                }

                bool allowed = name switch
                {
                    // A key is what the schema gives every element, member of a map or not; only a map reads it.
                    "key" or "escaped-key" => true,
                    "escaped" => local == "string",
                    _ => false,
                };

                if (!allowed)
                {
                    throw Invalid($"A '{local}'{(inMap ? "" : " that is not a member of a map")} has no '{name}' attribute.");
                }

                if (name != "key")
                {
                    ReadBoolean(tree.StringValueOf(attribute), $"the '{name}' attribute");
                }
            }
        }

        /// <summary>An xs:boolean as written — true, false, 1 or 0, whitespace around it or not.</summary>
        private bool ReadBoolean(string written, string what)
        {
            return written.Trim() switch
            {
                "true" or "1" => true,
                "false" or "0" => false,
                _ => throw Invalid($"'{written}' is neither true nor false, and {what} is one or the other."),
            };
        }

        /// <summary>Whether a boolean attribute is there and true.</summary>
        private bool FlagOn(XdmTree tree, int element, string name)
        {
            return AttributeOf(tree, element, name) is string written && ReadBoolean(written, $"the '{name}' attribute");
        }

        /// <summary>Refuses an element child where only text belongs.</summary>
        private void RequireTextOnly(XdmTree tree, int element, string local)
        {
            if (ElementChildOf(tree, element) is not null)
            {
                throw Invalid($"A '{local}' holds text, and this one has an element in it.");
            }
        }

        private void WriteBraced(
            XdmTree tree, int id, StringBuilder json, bool indent, int depth, char open, char close, bool named)
        {
            json.Append(open);
            bool first = true;
            HashSet<string>? keys = named ? new HashSet<string>(StringComparer.Ordinal) : null;

            for (int child = tree.FirstChildOf(id); child >= 0; child = tree.NextSiblingOf(child))
            {
                if (tree.KindOf(child) != NodeKind.Element)
                {
                    // Whitespace between the elements is formatting and not content; anything else in there
                    // means the structure is not what it claims to be.
                    if (tree.KindOf(child) == NodeKind.Text && tree.StringValueOf(child).Trim().Length != 0)
                    {
                        throw Invalid("Text between the members of a JSON structure is not part of it.");
                    }

                    continue;
                }

                if (!first)
                {
                    json.Append(',');
                }

                first = false;
                Indent(json, indent, depth + 1);

                if (named)
                {
                    // Two members of one map may not share a key, and the key is the string the escapes
                    // stand for: "\u0031" written as escaped is the key "1" all over again.
                    string key = AttributeOf(tree, child, "key")
                        ?? throw Invalid("A member of a JSON map needs a 'key' attribute, and this one has none.");

                    if (!keys!.Add(FlagOn(tree, child, "escaped-key") ? Unescape(key) : key))
                    {
                        throw Invalid($"Two members of one map have the key '{key}'.");
                    }

                    WriteString(tree, child, json, "escaped-key", attribute: "key");
                    json.Append(':');

                    if (indent)
                    {
                        json.Append(' ');
                    }
                }

                Write(tree, child, json, indent, depth + 1, inMap: named);
            }

            if (!first)
            {
                Indent(json, indent, depth);
            }

            json.Append(close);
        }

        private static void Indent(StringBuilder json, bool indent, int depth)
        {
            if (!indent)
            {
                return;
            }

            json.Append('\n').Append(' ', depth * 2);
        }

        /// <summary>
        /// Writes a JSON string, either from an element's text or from the named attribute.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <param name="id">The element.</param>
        /// <param name="json">Where to write.</param>
        /// <param name="escapedFlag">
        /// The attribute saying the text already holds JSON escapes, which are then copied through rather
        /// than escaped again — the only way a stylesheet can put a character in that XML cannot carry.
        /// </param>
        /// <param name="attribute">The attribute to write, or <see langword="null"/> for the element's text.</param>
        private void WriteString(
            XdmTree tree, int id, StringBuilder json, string escapedFlag, string? attribute = null)
        {
            string text = attribute is null
                ? tree.StringValueOf(id)
                : AttributeOf(tree, id, attribute)
                    ?? throw Invalid("A member of a JSON map needs a 'key' attribute, and this one has none.");

            json.Append('"');
            Escape(text, json, FlagOn(tree, id, escapedFlag));
            json.Append('"');
        }

        /// <summary>Escapes what JSON does not allow inside a string, or what it is better without.</summary>
        /// <remarks>
        /// The two-character escapes where JSON has one, <c>\/</c> for the solidus as the specification's
        /// erratum has it, and <c>\u</c> for the rest of the control range — C0 and C1 both, and DEL between
        /// them — in upper-case hex, which is what the suite compares against. Everything else is written as
        /// it stands: JSON is UTF-8, so a character above ASCII needs no escape. Text that says it is escaped
        /// already has its backslashes read as the escapes they start, copied through when JSON knows them and
        /// refused with <c>FOJS0007</c> when it does not; everything else in it is escaped as usual, so a bare
        /// quotation mark is still <c>\"</c>.
        /// </remarks>
        private static void Escape(string text, StringBuilder json, bool alreadyEscaped)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                switch (c)
                {
                    case '"':
                        json.Append("\\\"");
                        break;

                    case '\\':
                        if (!alreadyEscaped)
                        {
                            json.Append("\\\\");
                            break;
                        }

                        int length = EscapeLength(text, i);
                        if (length == 0)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.FOJS0007,
                                $"'{text}' says it is escaped already, and the backslash at {i} starts no escape "
                                + "JSON knows.");
                        }

                        json.Append(text, i, length);
                        i += length - 1;
                        break;

                    case '/':
                        json.Append("\\/");
                        break;

                    case '\b':
                        json.Append("\\b");
                        break;

                    case '\f':
                        json.Append("\\f");
                        break;

                    case '\n':
                        json.Append("\\n");
                        break;

                    case '\r':
                        json.Append("\\r");
                        break;

                    case '\t':
                        json.Append("\\t");
                        break;

                    default:
                        if (c < ' ' || (c >= '\u007f' && c <= '\u009f'))
                        {
                            json.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            json.Append(c);
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// How long the escape starting at a backslash is — two characters, or six for <c>\uXXXX</c> — or
        /// zero where it is not one.
        /// </summary>
        private static int EscapeLength(string text, int backslash)
        {
            if (backslash + 1 >= text.Length)
            {
                return 0;
            }

            switch (text[backslash + 1])
            {
                case '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't':
                    return 2;

                case 'u':
                    if (backslash + 6 > text.Length)
                    {
                        return 0;
                    }

                    for (int i = backslash + 2; i < backslash + 6; i++)
                    {
                        if (!Uri.IsHexDigit(text[i]))
                        {
                            return 0;
                        }
                    }

                    return 6;

                default:
                    return 0;
            }
        }

        /// <summary>The string that escaped text stands for, which is what two keys are compared as.</summary>
        private static string Unescape(string text)
        {
            if (text.IndexOf('\\') < 0)
            {
                return text;
            }

            StringBuilder plain = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\\')
                {
                    plain.Append(text[i]);
                    continue;
                }

                int length = EscapeLength(text, i);
                if (length == 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOJS0007,
                        $"'{text}' says it is escaped already, and the backslash at {i} starts no escape JSON "
                        + "knows.");
                }

                plain.Append(text[i + 1] switch
                {
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'u' => (char)Convert.ToInt32(text.Substring(i + 2, 4), 16),
                    char other => other,
                });

                i += length - 1;
            }

            return plain.ToString();
        }

        /// <summary>The one element child of a node, or null where there is none, or more than one, or text.</summary>
        private static int? OnlyElementChildOf(XdmTree tree, int id)
        {
            int? found = null;

            for (int child = tree.FirstChildOf(id); child >= 0; child = tree.NextSiblingOf(child))
            {
                switch (tree.KindOf(child))
                {
                    case NodeKind.Element when found is null:
                        found = child;
                        break;

                    case NodeKind.Element:
                        return null;

                    case NodeKind.Text when tree.StringValueOf(child).Trim().Length != 0:
                        return null;
                }
            }

            return found;
        }

        private static int? ElementChildOf(XdmTree tree, int id)
        {
            for (int child = tree.FirstChildOf(id); child >= 0; child = tree.NextSiblingOf(child))
            {
                if (tree.KindOf(child) == NodeKind.Element)
                {
                    return child;
                }
            }

            return null;
        }

        private static string? AttributeOf(XdmTree tree, int element, string name)
        {
            int count = tree.AttributeCountOf(element);

            for (int i = 0; i < count; i++)
            {
                int attribute = tree.AttributeAt(element, i);

                if (NamespaceOf(tree, attribute).Length == 0
                    && string.Equals(LocalNameOf(tree, attribute), name, StringComparison.Ordinal))
                {
                    return tree.StringValueOf(attribute);
                }
            }

            return null;
        }

        private static string LocalNameOf(XdmTree tree, int id)
        {
            return tree.NameTable.GetLocalName(tree.FingerprintOf(id));
        }

        private static string NamespaceOf(XdmTree tree, int id)
        {
            return tree.NameTable.GetNamespaceUri(tree.FingerprintOf(id));
        }

        private static XsltException Invalid(string what)
        {
            return XsltErrors.Error(
                XsltErrorCode.FOJS0006,
                what + " fn:xml-to-json() takes the structure fn:json-to-xml() produces, and nothing else.");
        }
    }
}
