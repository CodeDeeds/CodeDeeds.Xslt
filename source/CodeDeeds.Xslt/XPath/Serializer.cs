using System.Globalization;
using System.Text;
using CodeDeeds.Xslt.Compiler;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// <c>fn:serialize</c>, which writes a sequence out as text the way a stylesheet's result would be
    /// written.
    /// </summary>
    /// <remarks>
    /// The work is not the writing — <see cref="OutputWriter"/> already does that, and this hands the
    /// <c>xml</c>, <c>html</c>, <c>xhtml</c> and <c>text</c> methods straight to it. What is here is the two
    /// methods a tree cannot carry: <c>json</c>, which writes a map or an array as JSON, and
    /// <c>adaptive</c>, which writes whatever it is given in a form meant to be read by a person.
    /// <para>
    /// The parameters arrive in either of two shapes. XPath 3.0 takes an
    /// <c>output:serialization-parameters</c> element with a child per parameter; 3.1 adds a map, which is
    /// what nearly every test uses. Both are read here into one set.
    /// </para>
    /// </remarks>
    internal static class Serializer
    {
        /// <summary>The namespace the parameter element and its children are in.</summary>
        public const string ParameterNamespace = "http://www.w3.org/2010/xslt-xquery-serialization";

        /// <summary>What the parameters said, beyond what <see cref="OutputSettings"/> holds.</summary>
        private sealed class Parameters
        {
            // The declaration is left out unless asked for: what serialize() hands back is markup for the
            // stylesheet to put somewhere, and a declaration in the middle of a document is a mistake it
            // would then have to strip. An omit-xml-declaration in the parameters says otherwise.
            public OutputSettings Settings { get; init; } = new OutputSettings { OmitXmlDeclaration = true };

            /// <summary>What goes between two items, or null where nothing was asked for.</summary>
            public string? ItemSeparator { get; set; }

            /// <summary>Whether a map may repeat a key once its keys are turned into strings.</summary>
            public bool AllowDuplicateNames { get; set; }
        }

        public static string Serialize(List<XPathValue> items, XPathValue options)
        {
            return Write(items, ReadParameters(options));
        }

        /// <summary>
        /// Serializes a result sequence under the settings an <c>xsl:output</c> or an
        /// <c>xsl:result-document</c> settled, for the two methods that write values rather than a tree.
        /// </summary>
        internal static string Text(List<XPathValue> items, OutputSettings settings)
        {
            return Write(
                items,
                new Parameters
                {
                    Settings = settings,
                    ItemSeparator = settings.ItemSeparator,
                    AllowDuplicateNames = settings.AllowDuplicateNames,
                });
        }

        private static string Write(List<XPathValue> items, Parameters parameters)
        {
            return parameters.Settings.Method switch
            {
                OutputMethod.Json => Json(items, parameters),
                OutputMethod.Adaptive => Adaptive(items, parameters),
                _ => Markup(items, parameters),
            };
        }

        // ---- The parameters ------------------------------------------------------------------------------

        private static Parameters ReadParameters(XPathValue options)
        {
            Parameters parameters = new Parameters();

            if (Xpath2FunctionExpr.IsEmptySequence(options))
            {
                return parameters;
            }

            if (options.Kind == XPathValueKind.Map)
            {
                foreach (KeyValuePair<XPathValue, XPathValue> entry in options.AsMap().Entries)
                {
                    Apply(parameters, entry.Key.ToStringValue(), entry.Value);
                }

                return parameters;
            }

            if (options.Kind is XPathValueKind.Node or XPathValueKind.NodeSet)
            {
                ReadParameterElement(parameters, options);
                return parameters;
            }

            throw XsltErrors.Error(
                XsltErrorCode.XPTY0004,
                "fn:serialize()'s second argument is a map of parameters or the element that names them, "
                + $"and a {options.TypeCode} is neither.");
        }

        /// <summary>
        /// Reads the parameters from an <c>output:serialization-parameters</c> element.
        /// </summary>
        /// <remarks>
        /// Everything about the element is checked: a child outside the serialization namespace, a parameter
        /// named twice, one this engine does not offer, or one carrying anything but a <c>value</c> attribute
        /// is <c>SEPM0017</c>. It is a document written by hand and read once, so a typo in it is worth
        /// reporting rather than passing over.
        /// </remarks>
        private static void ReadParameterElement(Parameters parameters, XPathValue options)
        {
            NodeSet nodes = options.Kind == XPathValueKind.Node
                ? NodeSet.Singleton(options.NodeTree, options.NodeId)
                : options.AsNodeSet();

            if (nodes.Count != 1)
            {
                throw Bad("the parameters are one element, not a set of them");
            }

            XdmTree tree = nodes.TreeAt(0);
            int element = nodes[0];

            if (tree.KindOf(element) != NodeKind.Element
                || NamespaceOf(tree, element) != ParameterNamespace
                || LocalOf(tree, element) != "serialization-parameters")
            {
                throw Bad("they are named by an output:serialization-parameters element");
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            for (int child = tree.FirstChildOf(element); child >= 0; child = tree.NextSiblingOf(child))
            {
                if (tree.KindOf(child) == NodeKind.Text)
                {
                    if (XdmSequence.StringValueOf(XPathValue.FromNode(tree, child)).Trim().Length > 0)
                    {
                        throw Bad("only elements name parameters, and there is text between them");
                    }

                    continue;
                }

                if (tree.KindOf(child) != NodeKind.Element)
                {
                    continue;
                }

                if (NamespaceOf(tree, child) != ParameterNamespace)
                {
                    throw Bad($"'{LocalOf(tree, child)}' is not in the serialization namespace");
                }

                string name = LocalOf(tree, child);

                if (!seen.Add(name))
                {
                    throw Bad($"'{name}' is named twice");
                }

                if (tree.FirstChildOf(child) >= 0)
                {
                    throw Bad($"'{name}' carries content, where it takes a value attribute and nothing else");
                }

                string? value = AttributeOf(tree, child, "value");

                if (value is null)
                {
                    throw Bad($"'{name}' has no value attribute");
                }

                Apply(parameters, name, XPathValue.FromString(value), element: true);
            }
        }

        private static string? AttributeOf(XdmTree tree, int element, string name)
        {
            for (int i = 0; i < tree.AttributeCountOf(element); i++)
            {
                int attribute = tree.AttributeAt(element, i);

                if (LocalOf(tree, attribute) == name && NamespaceOf(tree, attribute).Length == 0)
                {
                    return tree.StringValueOf(attribute);
                }
            }

            return null;
        }

        private static string LocalOf(XdmTree tree, int node)
        {
            return tree.NameTable.GetLocalName(tree.FingerprintOf(node));
        }

        private static string NamespaceOf(XdmTree tree, int node)
        {
            return tree.NameTable.GetNamespaceUri(tree.FingerprintOf(node));
        }

        private static void Apply(Parameters parameters, string name, XPathValue value, bool element = false)
        {
            OutputSettings settings = parameters.Settings;

            switch (name)
            {
                case "method":
                    settings.MethodSpecified = true;
                    settings.Method = value.ToStringValue() switch
                    {
                        "html" => OutputMethod.Html,
                        "text" => OutputMethod.Text,
                        "xhtml" => OutputMethod.Xhtml,
                        "json" => OutputMethod.Json,
                        "adaptive" => OutputMethod.Adaptive,
                        _ => OutputMethod.Xml,
                    };

                    break;

                case "json-node-output-method":
                    settings.JsonNodeOutputMethod = value.ToStringValue() switch
                    {
                        "html" => OutputMethod.Html,
                        "text" => OutputMethod.Text,
                        "xhtml" => OutputMethod.Xhtml,
                        _ => OutputMethod.Xml,
                    };

                    break;

                case "indent":
                    settings.Indent = Boolean(name, value, element);
                    settings.IndentSpecified = true;
                    break;

                case "omit-xml-declaration":
                    settings.OmitXmlDeclaration = Boolean(name, value, element);
                    break;

                case "encoding":
                    settings.Encoding = value.ToStringValue();
                    break;

                case "version":
                    settings.Version = value.ToStringValue();
                    break;

                case "media-type":
                    settings.MediaType = value.ToStringValue();
                    break;

                case "doctype-public":
                    settings.DoctypePublic = value.ToStringValue();
                    break;

                case "doctype-system":
                    settings.DoctypeSystem = value.ToStringValue();
                    break;

                case "byte-order-mark":
                    settings.ByteOrderMark = Boolean(name, value, element);
                    break;

                case "standalone":
                    settings.Standalone = value.Kind == XPathValueKind.Boolean ? value.ToBoolean() : null;
                    break;

                case "item-separator":
                    parameters.ItemSeparator = value.ToStringValue();
                    break;

                case "allow-duplicate-names":
                    parameters.AllowDuplicateNames = Boolean(name, value, element);
                    break;

                case "cdata-section-elements" or "escape-uri-attributes"
                    or "include-content-type" or "normalization-form" or "undeclare-prefixes"
                    or "suppress-indentation" or "html-version" or "parameter-document":
                    // Read and not acted on, which is what a parameter this engine has no use for gets:
                    // refusing it would fail a document that names it and asks for nothing unusual.
                    break;

                case "use-character-maps":
                    // The one parameter fn:serialize may not be given at all, a character map being
                    // something only a stylesheet declares.
                    throw Bad("'use-character-maps' names something only a stylesheet can declare");

                default:
                    if (element)
                    {
                        throw Bad($"'{name}' is not a serialization parameter");
                    }

                    break;
            }
        }

        /// <summary>
        /// Reads a parameter declared <c>xs:boolean</c>, which in a map has to be one.
        /// </summary>
        /// <remarks>
        /// In the element form every value is written as text, so <c>yes</c> and <c>no</c> are what it says;
        /// in a map the value carries a type and a string is the wrong one, which the suite checks with
        /// <c>"indent":"true"</c> as well as <c>"indent":23</c>.
        /// </remarks>
        private static bool Boolean(string name, XPathValue value, bool element)
        {
            if (value.Kind == XPathValueKind.Boolean)
            {
                return value.ToBoolean();
            }

            // Only in the element form, where every value is written as text and 'yes' is how a boolean is
            // spelled. In a map the value carries a type and a string is the wrong one, which is why the
            // suite checks "indent":"true" as well as "indent":23.
            if (element)
            {
                string text = value.ToStringValue().Trim();

                if (text is "yes" or "true" or "1")
                {
                    return true;
                }

                if (text is "no" or "false" or "0")
                {
                    return false;
                }
            }

            throw XsltErrors.Error(
                XsltErrorCode.XPTY0004,
                $"the serialization parameter '{name}' is a boolean, and this one is not.");
        }

        private static XsltException Bad(string why)
        {
            return XsltErrors.Error(
                XsltErrorCode.SEPM0017, $"fn:serialize() cannot use these parameters, because {why}.");
        }

        // ---- The markup methods --------------------------------------------------------------------------

        private static string Markup(List<XPathValue> items, Parameters parameters)
        {
            StringWriter text = new StringWriter();
            OutputWriter writer = new OutputWriter(text, parameters.Settings);

            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    // Without a separator asked for, the rule is the one that builds a document from a
                    // sequence: two atomic values side by side are separated by a space, and a node brings
                    // its own boundaries.
                    writer.WriteText(
                        parameters.ItemSeparator
                        ?? (items[i].Kind == XPathValueKind.Node || items[i - 1].Kind == XPathValueKind.Node
                            ? string.Empty
                            : " "));
                }

                Write(writer, items[i]);
            }

            writer.Flush();
            return text.ToString();
        }

        private static void Write(OutputWriter writer, XPathValue item)
        {
            if (item.Kind != XPathValueKind.Node)
            {
                if (item.IsFunctionItem)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.SENR0001,
                        "a function, a map or an array has no serialized form under this method; the json "
                        + "and adaptive methods are the ones that write them.");
                }

                writer.WriteText(item.ToStringValue());
                return;
            }

            NodeKind kind = item.NodeTree.KindOf(item.NodeId);

            if (kind is NodeKind.Attribute or NodeKind.Namespace)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.SENR0001,
                    $"a free-standing {(kind == NodeKind.Attribute ? "attribute" : "namespace")} node "
                    + "cannot be serialized: there is no element for it to belong to.");
            }

            NodeCopier.CopyDeep(item.NodeTree, item.NodeId, writer);
        }

        // ---- The JSON method -----------------------------------------------------------------------------

        /// <summary>
        /// Writes one item as JSON.
        /// </summary>
        /// <remarks>
        /// JSON holds one value, so this takes one item: a sequence of two is <c>SERE0023</c> and so is a
        /// map entry or an array member holding one, which is the same rule reaching inwards.
        /// </remarks>
        private static string Json(List<XPathValue> items, Parameters parameters)
        {
            if (items.Count > 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.SERE0023,
                    "the json method writes one value, and was given a sequence of "
                    + items.Count.ToString(CultureInfo.InvariantCulture) + " items.");
            }

            StringBuilder json = new StringBuilder();
            AppendJson(json, items.Count == 0 ? XPathValue.FromSequence(XdmSequence.Empty) : items[0], parameters);
            return json.ToString();
        }

        private static void AppendJson(StringBuilder json, XPathValue value, Parameters parameters)
        {
            switch (value.Kind)
            {
                case XPathValueKind.Map:
                {
                    XdmMap map = value.AsMap();
                    HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                    json.Append('{');
                    bool first = true;

                    foreach (KeyValuePair<XPathValue, XPathValue> entry in map.Entries)
                    {
                        // A map may be keyed by anything, and JSON only by strings, so two keys that are
                        // different values may name one field once written.
                        string name = entry.Key.ToStringValue();

                        if (!names.Add(name) && !parameters.AllowDuplicateNames)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.SERE0022,
                                $"the key '{name}' appears twice once the map's keys are written as "
                                + "strings, which JSON does not allow unless allow-duplicate-names says so.");
                        }

                        if (!first)
                        {
                            json.Append(',');
                        }

                        first = false;
                        AppendJsonString(json, name, parameters.Settings.CharacterMap);
                        json.Append(':');
                        AppendJson(json, Single(entry.Value), parameters);
                    }

                    json.Append('}');
                    return;
                }

                case XPathValueKind.Array:
                {
                    XdmArray array = value.AsArray();
                    json.Append('[');

                    for (int i = 0; i < array.Count; i++)
                    {
                        if (i > 0)
                        {
                            json.Append(',');
                        }

                        AppendJson(json, Single(array.Members[i]), parameters);
                    }

                    json.Append(']');
                    return;
                }

                case XPathValueKind.Sequence when XdmSequence.Items(value).Count == 0:
                    json.Append("null");
                    return;

                case XPathValueKind.Boolean:
                    json.Append(value.ToBoolean() ? "true" : "false");
                    return;

                case XPathValueKind.Number:
                {
                    double number = value.ToNumber();

                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.SERE0020,
                            $"JSON has no way to write {value.ToStringValue()}, there being no infinity and "
                            + "no not-a-number among its values.");
                    }

                    json.Append(value.ToStringValue());
                    return;
                }

                case XPathValueKind.Node:
                {
                    // A node is written with the method json-node-output-method names, xml unless it says
                    // otherwise, with no declaration and no other parameter passed down (§9.1), and the
                    // result put in a JSON string.
                    Parameters nested = new Parameters();
                    nested.Settings.Method = parameters.Settings.JsonNodeOutputMethod;
                    nested.Settings.MethodSpecified = true;
                    nested.Settings.OmitXmlDeclaration = true;
                    nested.Settings.Indent = false;
                    nested.Settings.IndentSpecified = true;
                    AppendJsonString(json, Markup(new List<XPathValue> { value }, nested), null);
                    return;
                }

                default:
                    if (value.IsFunctionItem)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.SERE0021,
                            "a function has no JSON form: JSON holds data, and a function is not data.");
                    }

                    AppendJsonString(json, value.ToStringValue(), parameters.Settings.CharacterMap);
                    return;
            }
        }

        /// <summary>The one item a JSON value may be, or the error for a sequence that is not one.</summary>
        /// <remarks>
        /// A node-set is unwrapped here as well as a sequence: a map entry holding one document is holding
        /// one item, and reading its string value instead would put the text where the markup belongs.
        /// </remarks>
        private static XPathValue Single(XPathValue value)
        {
            if (value.Kind is not (XPathValueKind.Sequence or XPathValueKind.NodeSet))
            {
                return value;
            }

            List<XPathValue> items = XdmSequence.Items(value);

            if (items.Count == 1)
            {
                return items[0];
            }

            if (items.Count == 0)
            {
                return value;
            }

            throw XsltErrors.Error(
                XsltErrorCode.SERE0023,
                "a JSON value is one thing, and this one is a sequence of "
                + items.Count.ToString(CultureInfo.InvariantCulture) + ".");
        }

        /// <summary>
        /// Writes a JSON string, escaping what JSON does not allow and what the encoding cannot carry.
        /// </summary>
        /// <remarks>
        /// The solidus is escaped as well, which JSON does not require and this specification does ask for:
        /// the suite expects <c>http:\/\/www.w3.org\/</c>.
        /// </remarks>
        private static void AppendJsonString(StringBuilder json, string text, CharacterMap? map)
        {
            json.Append('"');

            for (int at = 0; at < text.Length; at++)
            {
                char character = text[at];

                // A character the character map covers is written as the map says, unescaped: that is how
                // a stylesheet keeps a solidus from being written as \/ (Serialization 3.1 §9).
                if (map is not null)
                {
                    int codePoint = char.IsHighSurrogate(character) && at + 1 < text.Length
                        ? char.ConvertToUtf32(character, text[at + 1])
                        : character;

                    if (map.Find(codePoint) is string mapped)
                    {
                        json.Append(mapped);
                        at += codePoint > 0xFFFF ? 1 : 0;
                        continue;
                    }
                }

                switch (character)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '/': json.Append("\\/"); break;
                    case '\b': json.Append("\\b"); break;
                    case '\f': json.Append("\\f"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;

                    default:
                        if (character < ' ' || char.IsSurrogate(character))
                        {
                            json.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            json.Append(character);
                        }

                        break;
                }
            }

            json.Append('"');
        }

        // ---- The adaptive method -------------------------------------------------------------------------

        /// <summary>
        /// Writes each item in a form meant to be read by a person, which is what the adaptive method is for.
        /// </summary>
        /// <remarks>
        /// It is the only method that will write anything at all: a map, an array, a function, a bare
        /// attribute node. The specification says outright that the exact text is the processor's to choose,
        /// so what is here is the shape the suite's examples take.
        /// </remarks>
        private static string Adaptive(List<XPathValue> items, Parameters parameters)
        {
            StringBuilder text = new StringBuilder();
            string separator = parameters.ItemSeparator ?? "\n";

            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(separator);
                }

                AppendAdaptive(text, items[i], parameters);
            }

            return text.ToString();
        }

        private static void AppendAdaptive(StringBuilder text, XPathValue value, Parameters parameters)
        {
            switch (value.Kind)
            {
                case XPathValueKind.Map:
                {
                    text.Append("map{");
                    bool first = true;

                    foreach (KeyValuePair<XPathValue, XPathValue> entry in value.AsMap().Entries)
                    {
                        if (!first)
                        {
                            text.Append(',');
                        }

                        first = false;
                        AppendAdaptive(text, entry.Key, parameters);
                        text.Append(':');
                        AppendAdaptiveSequence(text, entry.Value, parameters);
                    }

                    text.Append('}');
                    return;
                }

                case XPathValueKind.Array:
                {
                    XdmArray array = value.AsArray();
                    text.Append('[');

                    for (int i = 0; i < array.Count; i++)
                    {
                        if (i > 0)
                        {
                            text.Append(',');
                        }

                        AppendAdaptiveSequence(text, array.Members[i], parameters);
                    }

                    text.Append(']');
                    return;
                }

                case XPathValueKind.Boolean:
                    text.Append(value.ToBoolean() ? "true()" : "false()");
                    return;

                case XPathValueKind.Node:
                {
                    XdmTree tree = value.NodeTree;
                    int node = value.NodeId;

                    if (tree.KindOf(node) == NodeKind.Attribute)
                    {
                        // An attribute has no serialized form of its own under any other method, and here
                        // it is written the way it would be written on an element.
                        text.Append(LocalOf(tree, node)).Append("=\"")
                            .Append(tree.StringValueOf(node).Replace("\"", "&quot;", StringComparison.Ordinal))
                            .Append('"');

                        return;
                    }

                    // Written with the xml method under the parameters in force, character maps and the
                    // declaration included: what an xsl:output asked for, it gets.
                    OutputSettings settings = parameters.Settings.Copy();
                    settings.Method = OutputMethod.Xml;
                    settings.MethodSpecified = true;

                    Parameters nested = new Parameters { Settings = settings, ItemSeparator = parameters.ItemSeparator };
                    text.Append(Markup(new List<XPathValue> { value }, nested));
                    return;
                }

                case XPathValueKind.String:
                    if (value.TypeCode is XdmTypeCode.String or XdmTypeCode.UntypedAtomic
                        or XdmTypeCode.AnyUri)
                    {
                        AppendQuoted(text, value.ToStringValue(), parameters.Settings.CharacterMap);
                        return;
                    }

                    text.Append(value.ToStringValue());
                    return;

                default:
                    if (value.IsFunctionItem)
                    {
                        // A function is its name and arity, the way a named function reference spells it;
                        // one with no name is said to have none (§10).
                        XdmFunction function = value.AsFunction();
                        XdmQName? named = function.FunctionName;

                        text.Append(
                            named is null ? "(anonymous-function)"
                            : named.Value.Prefix.Length > 0 ? named.Value.Prefix + ":" + named.Value.LocalName
                            : named.Value.NamespaceUri == XdmType.FunctionNamespace ? "fn:" + named.Value.LocalName
                            : "Q{" + named.Value.NamespaceUri + "}" + named.Value.LocalName);
                        text.Append('#').Append(function.Arity.ToString(CultureInfo.InvariantCulture));
                        return;
                    }

                    text.Append(value.ToStringValue());
                    return;
            }
        }

        /// <summary>
        /// Writes a string in quotation marks, a quotation mark in it doubled, and a character the map
        /// covers written as the map says: a character map applies to any value written as a quoted string
        /// (Serialization §11), and what it substitutes is not escaped.
        /// </summary>
        private static void AppendQuoted(StringBuilder text, string value, CharacterMap? map)
        {
            text.Append('"');

            for (int at = 0; at < value.Length; at++)
            {
                char character = value[at];

                if (map is not null)
                {
                    int codePoint = char.IsHighSurrogate(character) && at + 1 < value.Length
                        ? char.ConvertToUtf32(character, value[at + 1])
                        : character;

                    if (map.Find(codePoint) is string mapped)
                    {
                        text.Append(mapped);
                        at += codePoint > 0xFFFF ? 1 : 0;
                        continue;
                    }
                }

                if (character == '"')
                {
                    text.Append('"');
                }

                text.Append(character);
            }

            text.Append('"');
        }

        /// <summary>Writes what stands in one place of a map or an array, which may be a sequence.</summary>
        private static void AppendAdaptiveSequence(StringBuilder text, XPathValue value, Parameters parameters)
        {
            List<XPathValue> items = XdmSequence.Items(value);

            if (items.Count == 1)
            {
                AppendAdaptive(text, items[0], parameters);
                return;
            }

            text.Append('(');

            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }

                AppendAdaptive(text, items[i], parameters);
            }

            text.Append(')');
        }
    }
}
