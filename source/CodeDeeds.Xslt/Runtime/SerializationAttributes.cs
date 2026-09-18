using System.Globalization;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// The serialization attributes of <c>xsl:result-document</c> that may be attribute value templates,
    /// applied to a document's settings once their values are known.
    /// </summary>
    /// <remarks>
    /// Every serialization attribute of the instruction is a template — the specification writes them all
    /// in braces — so a stylesheet may decide the method, the indentation or the item separator of a result
    /// document from its data. One written as plain text is read when the stylesheet is, with the codes a
    /// stylesheet error has; one computed is read here, when the instruction runs, and a value the attribute
    /// may not take is then the serializer's own <c>SEPM0016</c>.
    /// </remarks>
    internal static class SerializationAttributes
    {
        /// <summary>The attributes this reader applies, which are the ones that may be templates.</summary>
        public static readonly string[] Templated =
        {
            "method", "indent", "omit-xml-declaration", "encoding", "version", "output-version",
            "standalone", "media-type", "doctype-public", "doctype-system", "byte-order-mark",
            "include-content-type", "escape-uri-attributes", "normalization-form", "html-version",
            "item-separator", "build-tree", "undeclare-prefixes", "cdata-section-elements",
            "suppress-indentation", "allow-duplicate-names", "json-node-output-method",
        };

        /// <summary>Applies one attribute's computed value to a document's settings.</summary>
        /// <param name="settings">The settings, changed in place.</param>
        /// <param name="name">The attribute's name.</param>
        /// <param name="value">Its value, as the template came to.</param>
        /// <param name="resolvePrefix">What a prefix stood for where the instruction was written.</param>
        public static void Apply(
            OutputSettings settings, string name, string value, Func<string, string?> resolvePrefix)
        {
            string trimmed = value.Trim();

            switch (name)
            {
                case "method":
                    settings.Method = trimmed switch
                    {
                        "xml" => OutputMethod.Xml,
                        "html" => OutputMethod.Html,
                        "text" => OutputMethod.Text,
                        "xhtml" => OutputMethod.Xhtml,
                        "json" => OutputMethod.Json,
                        "adaptive" => OutputMethod.Adaptive,
                        _ => throw Refused(name, value, "xml, html, xhtml, text, json or adaptive"),
                    };
                    settings.MethodSpecified = true;
                    break;
                case "json-node-output-method":
                    settings.JsonNodeOutputMethod = trimmed switch
                    {
                        "xml" => OutputMethod.Xml,
                        "html" => OutputMethod.Html,
                        "text" => OutputMethod.Text,
                        "xhtml" => OutputMethod.Xhtml,
                        _ => throw Refused(name, value, "xml, html, xhtml or text"),
                    };
                    break;
                case "allow-duplicate-names":
                    settings.AllowDuplicateNames = Flag(name, value);
                    break;

                case "indent":
                    settings.Indent = Flag(name, value);
                    settings.IndentSpecified = true;
                    break;

                case "omit-xml-declaration":
                    settings.OmitXmlDeclaration = Flag(name, value);
                    break;

                case "encoding":
                    settings.Encoding = trimmed;
                    break;

                case "version":
                case "output-version":
                    settings.Version = trimmed;
                    settings.VersionSpecified = true;
                    break;

                case "standalone":
                    // Three values rather than two: "omit" leaves the declaration without a standalone at
                    // all, which is not the same as saying it is not standalone.
                    settings.Standalone = trimmed == "omit" ? null : Flag(name, value);
                    break;

                case "media-type":
                    settings.MediaType = trimmed;
                    break;

                case "doctype-public":
                    foreach (char c in trimmed)
                    {
                        if (!IsPublicIdentifierCharacter(c))
                        {
                            throw Refused(name, value, "the characters XML allows in a public identifier");
                        }
                    }

                    settings.DoctypePublic = trimmed;
                    break;

                case "doctype-system":
                    settings.DoctypeSystem = trimmed;
                    break;

                case "byte-order-mark":
                    settings.ByteOrderMark = Flag(name, value);
                    break;

                case "include-content-type":
                    settings.IncludeContentType = Flag(name, value);
                    break;

                case "escape-uri-attributes":
                    settings.EscapeUriAttributes = Flag(name, value);
                    break;

                case "normalization-form":
                    settings.NormalizationForm = trimmed switch
                    {
                        "NFC" => System.Text.NormalizationForm.FormC,
                        "NFD" => System.Text.NormalizationForm.FormD,
                        "NFKC" => System.Text.NormalizationForm.FormKC,
                        "NFKD" => System.Text.NormalizationForm.FormKD,
                        "none" => null,
                        _ => throw Refused(name, value, "NFC, NFD, NFKC, NFKD or none"),
                    };
                    break;

                case "html-version":
                    settings.HtmlVersion = decimal.TryParse(
                        trimmed,
                        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out decimal parsed)
                        ? parsed
                        : throw Refused(name, value, "a number naming a version of HTML");
                    break;

                case "item-separator":
                    // "#absent" is how a result document says it wants no separator specified rather than
                    // an empty one, since an attribute cannot be written to be absent.
                    settings.ItemSeparator = value == "#absent" ? null : value;
                    break;

                case "build-tree":
                    settings.BuildTree = Flag(name, value);
                    break;

                case "undeclare-prefixes":
                    // Kept for its form and for one error: this serializer writes XML 1.0, which has no way to
                    // undeclare a prefix, and asking for it there is SEPM0010.
                    settings.UndeclarePrefixes = Flag(name, value);
                    break;

                case "cdata-section-elements":
                    foreach (string token in trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        (string uri, string local) = Name(token, resolvePrefix, name);

                        if (!settings.CDataSectionElements.Contains((uri, local)))
                        {
                            settings.CDataSectionElements.Add((uri, local));
                        }
                    }

                    break;

                case "suppress-indentation":
                    foreach (string token in trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        (string uri, string local) = Name(token, resolvePrefix, name);

                        if (!settings.SuppressIndentation.Contains((uri, local)))
                        {
                            settings.SuppressIndentation.Add((uri, local));
                        }
                    }

                    break;
            }
        }

        /// <summary>A yes or no, in any of the six spellings <c>xs:boolean</c> has.</summary>
        private static bool Flag(string name, string value)
        {
            return value.Trim() switch
            {
                "yes" or "true" or "1" => true,
                "no" or "false" or "0" => false,
                _ => throw Refused(name, value, "yes or no"),
            };
        }

        /// <summary>A name written in a list attribute, resolved as the instruction's prefixes had it.</summary>
        private static (string Uri, string Local) Name(
            string token, Func<string, string?> resolvePrefix, string attribute)
        {
            int colon = token.IndexOf(':');

            if (colon < 0)
            {
                return (string.Empty, token);
            }

            string prefix = token[..colon];

            return (
                resolvePrefix(prefix)
                    ?? throw Refused(attribute, token, "names whose prefixes are bound where the instruction is"),
                token[(colon + 1)..]);
        }

        /// <summary>Whether a character may appear in a public identifier, as XML has it.</summary>
        private static bool IsPublicIdentifierCharacter(char c)
        {
            return c is ' ' or '\r' or '\n'
                || char.IsAsciiLetterOrDigit(c)
                || "-'()+,./:=?;!*#@$_%".Contains(c);
        }

        private static XsltException Refused(string name, string value, string wanted)
        {
            return XsltErrors.Error(
                XsltErrorCode.SEPM0016,
                $"'{value}' is not a value the {name} of xsl:result-document may take, which is {wanted}. It "
                + "was computed, so the stylesheet could not have been told sooner.");
        }
    }
}
