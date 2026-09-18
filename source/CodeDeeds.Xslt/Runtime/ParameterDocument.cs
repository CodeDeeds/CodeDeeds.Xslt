using System;
using System.Collections.Generic;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// The serialization parameters an external document supplies, which <c>xsl:output</c> and
    /// <c>xsl:result-document</c> name with a <c>parameter-document</c> attribute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Serialization §3.1 gives the document its shape — an <c>output:serialization-parameters</c> element
    /// with one child per parameter — and XSLT 3.0 §26.1 says what it is worth: a parameter named there
    /// "takes precedence over a value supplied directly as an attribute", which in turn takes precedence
    /// over the output definition the instruction started from.
    /// </para>
    /// <para>
    /// Read here rather than in the compiler because both reach it. On <c>xsl:output</c> the attribute is
    /// plain text and the document can be read while the stylesheet is; on <c>xsl:result-document</c> it is
    /// an attribute value template, so the stylesheet may work out which document to read from its data,
    /// and then there is nothing to read until the instruction runs.
    /// </para>
    /// </remarks>
    internal static class ParameterDocument
    {
        /// <summary>
        /// Reads the document a <c>parameter-document</c> names, or null where there is none to read.
        /// </summary>
        /// <remarks>
        /// Fetched through the stylesheet resolver rather than the document resolver, whichever of the two
        /// moments it is read at: it settles how the stylesheet's own results are written, so it is part of
        /// the stylesheet's configuration and not data the transformation processes. One that cannot be
        /// found is ignored, which is what the specification says of it — the attribute is a way of
        /// keeping settings outside the stylesheet, not a requirement on the deployment.
        /// </remarks>
        /// <param name="href">The reference as written.</param>
        /// <param name="baseUri">What a relative reference resolves against.</param>
        /// <param name="options">The transformation's options, which say what may be read.</param>
        public static XdmTree? Fetch(string href, string? baseUri, XsltOptions options)
        {
            if (options.StylesheetResolver is null
                || options.StylesheetResolver.Resolve(href.Trim(), baseUri) is not ResolvedResource resolved)
            {
                return null;
            }

            try
            {
                return XdmTreeBuilder.FromXml(
                    resolved.Reader, entityResolver: options.EntityResolver, baseUri: resolved.Uri);
            }
            finally
            {
                resolved.Reader.Dispose();
            }
        }

        /// <summary>
        /// Applies what a parameter document says over the settings given.
        /// </summary>
        /// <param name="settings">The settings to write into, which the document takes precedence over.</param>
        /// <param name="document">The parameter document, already parsed.</param>
        /// <param name="href">The reference it was fetched by, for messages.</param>
        /// <param name="implements30">Whether the JSON and adaptive output methods are available.</param>
        /// <param name="instruction">The instruction that named it, for messages.</param>
        /// <returns>The output method the document named, or null where it named none.</returns>
        /// <exception cref="XsltException"><c>SEPM0017</c> where the document is not one of these.</exception>
        public static OutputMethod? ApplyTo(
            OutputSettings settings, XdmTree document, string href, bool implements30, string instruction)
        {
            int root = -1;

            for (int child = document.FirstChildOf(XdmTree.RootNode); child >= 0; child = document.NextSiblingOf(child))
            {
                if (document.KindOf(child) == NodeKind.Element)
                {
                    root = child;
                    break;
                }
            }

            if (root < 0
                || NamespaceIn(document, root) != Serializer.ParameterNamespace
                || LocalNameIn(document, root) != "serialization-parameters")
            {
                throw XsltErrors.Error(
                    XsltErrorCode.SEPM0017,
                    $"The parameter-document '{href}' of {instruction} does not hold an "
                    + "output:serialization-parameters element.");
            }

            OutputMethod? method = null;

            for (int child = document.FirstChildOf(root); child >= 0; child = document.NextSiblingOf(child))
            {
                if (document.KindOf(child) != NodeKind.Element)
                {
                    continue;
                }

                string name = LocalNameIn(document, child);

                if (NamespaceIn(document, child) != Serializer.ParameterNamespace)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.SEPM0017,
                        $"The parameter-document '{href}' names '{name}' outside the serialization namespace.");
                }

                if (name == "use-character-maps")
                {
                    Dictionary<int, string> entries = new Dictionary<int, string>();

                    for (int map = document.FirstChildOf(child); map >= 0; map = document.NextSiblingOf(map))
                    {
                        if (document.KindOf(map) != NodeKind.Element || LocalNameIn(document, map) != "character-map")
                        {
                            continue;
                        }

                        string character = AttributeIn(document, map, "character") ?? string.Empty;
                        string replacement = AttributeIn(document, map, "map-string") ?? string.Empty;

                        if (character.Length == 0 || char.ConvertToUtf32(character, 0) is int codePoint
                            && character.Length != (codePoint > 0xFFFF ? 2 : 1))
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.SEPM0017,
                                $"A character-map in the parameter-document '{href}' maps '{character}', which "
                                + "is not one character.");
                        }

                        entries[char.ConvertToUtf32(character, 0)] = replacement;
                    }

                    settings.CharacterMap = new CharacterMap(entries);
                    continue;
                }

                string value = AttributeIn(document, child, "value")
                    ?? throw XsltErrors.Error(
                        XsltErrorCode.SEPM0017,
                        $"The parameter '{name}' in the parameter-document '{href}' has no value attribute.");

                method = ApplyOne(settings, document, child, href, name, value, implements30) ?? method;
            }

            return method;
        }

        /// <summary>Applies one parameter, and says which method it named where it named one.</summary>
        private static OutputMethod? ApplyOne(
            OutputSettings settings,
            XdmTree document,
            int node,
            string href,
            string name,
            string value,
            bool implements30)
        {
            bool Flag()
            {
                return value.Trim() switch
                {
                    "yes" or "true" or "1" => true,
                    "no" or "false" or "0" => false,
                    _ => throw XsltErrors.Error(
                        XsltErrorCode.SEPM0017,
                        $"The parameter '{name}' in the parameter-document '{href}' is '{value}', and it takes "
                        + "yes or no."),
                };
            }

            OutputMethod Method()
            {
                return value.Trim() switch
                {
                    "xml" => OutputMethod.Xml,
                    "html" => OutputMethod.Html,
                    "xhtml" => OutputMethod.Xhtml,
                    "text" => OutputMethod.Text,
                    "json" when implements30 => OutputMethod.Json,
                    "adaptive" when implements30 => OutputMethod.Adaptive,
                    _ => throw XsltErrors.Error(
                        XsltErrorCode.XTSE1570,
                        $"The parameter '{name}' in the parameter-document '{href}' is '{value}', which is not "
                        + "an output method."),
                };
            }

            List<(string NamespaceUri, string LocalName)> Names()
            {
                List<(string, string)> names = new();

                foreach (string token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    int colon = token.IndexOf(':');
                    string prefix = colon < 0 ? string.Empty : token[..colon];
                    string uri = colon < 0
                        ? string.Empty
                        : document.ResolvePrefix(node, prefix)
                            ?? throw XsltErrors.Error(
                                XsltErrorCode.SEPM0017,
                                $"The prefix of '{token}' in the parameter-document '{href}' is not declared.");

                    names.Add((uri, token[(colon + 1)..]));
                }

                return names;
            }

            switch (name)
            {
                case "method":
                    settings.Method = Method();
                    settings.MethodSpecified = true;
                    return settings.Method;

                case "json-node-output-method":
                    settings.JsonNodeOutputMethod = Method() is OutputMethod.Json or OutputMethod.Adaptive
                        ? throw XsltErrors.Error(
                            XsltErrorCode.SEPM0017,
                            $"The json-node-output-method in the parameter-document '{href}' is '{value}', and a "
                            + "node inside a JSON string is written with xml, html, xhtml or text.")
                        : Method();
                    break;

                case "indent":
                    settings.Indent = Flag();
                    settings.IndentSpecified = true;
                    break;

                case "omit-xml-declaration": settings.OmitXmlDeclaration = Flag(); break;
                case "byte-order-mark": settings.ByteOrderMark = Flag(); break;
                case "escape-uri-attributes": settings.EscapeUriAttributes = Flag(); break;
                case "include-content-type": settings.IncludeContentType = Flag(); break;
                case "allow-duplicate-names": settings.AllowDuplicateNames = Flag(); break;
                case "build-tree": settings.BuildTree = Flag(); break;
                case "undeclare-prefixes": settings.UndeclarePrefixes = Flag(); break;
                case "standalone": settings.Standalone = value.Trim() == "omit" ? null : Flag(); break;
                case "encoding": settings.Encoding = value.Trim(); break;
                case "version":
                    settings.Version = value.Trim();
                    settings.VersionSpecified = true;
                    break;
                case "media-type": settings.MediaType = value.Trim(); break;
                case "doctype-public": settings.DoctypePublic = value; break;
                case "doctype-system": settings.DoctypeSystem = value; break;
                case "item-separator": settings.ItemSeparator = value; break;
                case "normalization-form": settings.NormalizationForm = ReadNormalizationForm(value); break;

                case "html-version":
                    settings.HtmlVersion = decimal.TryParse(
                        value.Trim(),
                        System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out decimal parsed)
                        ? parsed
                        : throw XsltErrors.Error(
                            XsltErrorCode.SEPM0017,
                            $"The html-version in the parameter-document '{href}' is '{value}', which is not a number.");
                    break;

                case "cdata-section-elements":
                    settings.CDataSectionElements.AddRange(Names());
                    break;

                case "suppress-indentation":
                    settings.SuppressIndentation.AddRange(Names());
                    break;

                default:
                    throw XsltErrors.Error(
                        XsltErrorCode.SEPM0017,
                        $"'{name}' in the parameter-document '{href}' is not a serialization parameter.");
            }

            return null;
        }

        /// <summary>The normalization the parameter names, or null for none.</summary>
        private static System.Text.NormalizationForm? ReadNormalizationForm(string form)
        {
            return form.Trim() switch
            {
                "none" => null,
                "NFC" => System.Text.NormalizationForm.FormC,
                "NFD" => System.Text.NormalizationForm.FormD,
                "NFKC" => System.Text.NormalizationForm.FormKC,
                "NFKD" => System.Text.NormalizationForm.FormKD,
                // SESU0011 is the serializer's own code for a form it does not apply, and the specification
                // requires it to be signalled: "A serialization error results if the value of the
                // normalization-form parameter specifies a normalization form that is not supported by the
                // serializer." fully-normalized is among those: it is a check on the result rather than a
                // transformation of it, and this engine does not make it.
                string other => throw XsltErrors.Error(
                    XsltErrorCode.SESU0011,
                    $"'{other}' is not a normalization form this engine applies. It has NFC, NFD, NFKC, NFKD "
                    + "and none."),
            };
        }

        private static string LocalNameIn(XdmTree tree, int node) => tree.NameTable.GetLocalName(tree.FingerprintOf(node));

        private static string NamespaceIn(XdmTree tree, int node) => tree.NameTable.GetNamespaceUri(tree.FingerprintOf(node));

        private static string? AttributeIn(XdmTree tree, int element, string name)
        {
            for (int i = 0; i < tree.AttributeCountOf(element); i++)
            {
                int attribute = tree.AttributeAt(element, i);

                if (LocalNameIn(tree, attribute) == name && NamespaceIn(tree, attribute).Length == 0)
                {
                    return tree.StringValueOf(attribute);
                }
            }

            return null;
        }
    }
}
