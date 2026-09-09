using System;
using System.Collections.Generic;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// Reads the name an instruction computed, and says what it names.
    /// </summary>
    /// <remarks>
    /// A name written on <c>xsl:element</c> or <c>xsl:attribute</c> is a lexical QName like any other, and a
    /// lexical QName means nothing on its own: its prefix stands for whatever the namespace declarations
    /// where it was written say it stands for. The instruction computes the text at run time, but the
    /// declarations it is read against are the ones in scope on the stylesheet element — so those travel with
    /// the instruction from compilation.
    /// <para>
    /// Elements and attributes differ in one place and the difference matters: an unprefixed element name
    /// takes the default namespace, an unprefixed attribute name is in no namespace. That is XML's own rule
    /// rather than XSLT's, and it is why the two have separate entry points here.
    /// </para>
    /// </remarks>
    internal static class ComputedName
    {
        /// <summary>The namespace no name may be put in, reserved for the declarations themselves.</summary>
        public const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

        /// <summary>The namespace the <c>xml</c> prefix is bound to, everywhere and permanently.</summary>
        public const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

        /// <summary>
        /// Reads the name for <c>xsl:element</c>.
        /// </summary>
        /// <param name="qualifiedName">The effective value of <c>name</c>.</param>
        /// <param name="namespaceUri">The effective value of <c>namespace</c>, or null where none was written.</param>
        /// <param name="inScope">The namespace declarations in scope where the instruction was written.</param>
        /// <returns>The prefix to write, the namespace it stands for, and the local part.</returns>
        public static (string Prefix, string Uri, string LocalName) ForElement(
            string qualifiedName,
            string? namespaceUri,
            IReadOnlyDictionary<string, string> inScope)
        {
            (string prefix, string localName) = Split(
                qualifiedName, XsltErrorCode.XTDE0820, "xsl:element");

            if (namespaceUri is null)
            {
                // No namespace attribute: the prefix means what it means here, and an unprefixed name takes
                // the default declaration — which is why <xsl:element name="foo"/> under xmlns="…" is in
                // that namespace rather than in none.
                return (prefix, Bound(prefix, inScope, XsltErrorCode.XTDE0830, "xsl:element"), localName);
            }

            RequireUsableNamespace(namespaceUri, XsltErrorCode.XTDE0835, "xsl:element");

            // The namespace attribute settles the namespace and leaves the prefix as no more than a
            // suggestion. An empty one puts the element in no namespace, where no prefix can reach it.
            return namespaceUri.Length == 0
                ? (string.Empty, string.Empty, localName)
                : (prefix, namespaceUri, localName);
        }

        /// <summary>
        /// Reads the name for <c>xsl:attribute</c>.
        /// </summary>
        /// <param name="qualifiedName">The effective value of <c>name</c>.</param>
        /// <param name="namespaceUri">The effective value of <c>namespace</c>, or null where none was written.</param>
        /// <param name="inScope">The namespace declarations in scope where the instruction was written.</param>
        /// <returns>The prefix to write, the namespace it stands for, and the local part.</returns>
        public static (string Prefix, string Uri, string LocalName) ForAttribute(
            string qualifiedName,
            string? namespaceUri,
            IReadOnlyDictionary<string, string> inScope)
        {
            (string prefix, string localName) = Split(
                qualifiedName, XsltErrorCode.XTDE0850, "xsl:attribute");

            if (prefix.Length == 0 && localName == "xmlns")
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0855,
                    "An xsl:attribute cannot be called 'xmlns'. A namespace declaration is not an attribute "
                    + "here; xsl:namespace is what writes one.");
            }

            if (namespaceUri is null)
            {
                // An unprefixed attribute is in no namespace, always. The default declaration reaches
                // element names only, so there is nothing to look up.
                return prefix.Length == 0
                    ? (string.Empty, string.Empty, localName)
                    : (prefix, Bound(prefix, inScope, XsltErrorCode.XTDE0860, "xsl:attribute"), localName);
            }

            RequireUsableNamespace(namespaceUri, XsltErrorCode.XTDE0865, "xsl:attribute");

            return namespaceUri.Length == 0
                ? (string.Empty, string.Empty, localName)
                : (prefix, namespaceUri, localName);
        }

        /// <summary>
        /// Checks the target of <c>xsl:processing-instruction</c>, which is a name and not a QName.
        /// </summary>
        /// <remarks>
        /// A processing instruction is not in a namespace, so a colon in its target has nothing to mean.
        /// <c>xml</c> in any mixture of cases is reserved by XML itself for the declaration.
        /// </remarks>
        /// <param name="target">The effective value of <c>name</c>.</param>
        /// <returns>The target.</returns>
        public static string ForProcessingInstruction(string target)
        {
            if (!XdmQName.TrySplit(target, out string prefix, out string localName)
                || prefix.Length != 0
                || string.Equals(localName, "xml", StringComparison.OrdinalIgnoreCase))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0890,
                    $"'{target}' cannot be the target of a processing instruction. A target is a name "
                    + "without a colon in it, and 'xml' in any case is reserved.");
            }

            return localName;
        }

        /// <summary>Splits a computed name, naming the failure the way the instruction naming it does.</summary>
        private static (string Prefix, string LocalName) Split(
            string qualifiedName,
            XsltErrorCode code,
            string instruction)
        {
            if (!XdmQName.TrySplit(qualifiedName, out string prefix, out string localName))
            {
                throw XsltErrors.Error(
                    code,
                    $"'{qualifiedName}' is not a name an {instruction} can use. A name is an optional prefix, "
                    + "a colon, and a local part, each of them an XML name.");
            }

            return (prefix, localName);
        }

        /// <summary>Finds what a prefix stands for where the instruction was written.</summary>
        private static string Bound(
            string prefix,
            IReadOnlyDictionary<string, string> inScope,
            XsltErrorCode code,
            string instruction)
        {
            if (prefix.Length == 0)
            {
                return inScope.TryGetValue(string.Empty, out string? fallback) ? fallback : string.Empty;
            }

            if (prefix == "xml")
            {
                return XmlNamespace;
            }

            if (inScope.TryGetValue(prefix, out string? uri) && uri.Length != 0)
            {
                return uri;
            }

            throw XsltErrors.Error(
                code,
                $"The {instruction} asked for the prefix '{prefix}', which no namespace declaration in scope "
                + "where it is written binds. Bind it there, or give the instruction a namespace attribute.");
        }

        /// <summary>
        /// Refuses a namespace no name may be put in.
        /// </summary>
        /// <remarks>
        /// The namespace attribute takes whatever it is given, with two exceptions the specification names:
        /// the URI reserved for namespace declarations themselves, and text that is not a URI reference at
        /// all. A result carrying either would not be a document any reader could make sense of.
        /// </remarks>
        private static void RequireUsableNamespace(
            string namespaceUri,
            XsltErrorCode code,
            string instruction)
        {
            if (namespaceUri == XmlnsNamespace)
            {
                throw XsltErrors.Error(
                    code,
                    $"An {instruction} cannot put a name in '{XmlnsNamespace}', which belongs to the "
                    + "namespace declarations themselves.");
            }

            if (!IsUriReference(namespaceUri))
            {
                throw XsltErrors.Error(
                    code,
                    $"'{namespaceUri}' is not a URI, so an {instruction} cannot use it as a namespace.");
            }
        }

        /// <summary>
        /// Says whether text is a URI reference.
        /// </summary>
        /// <remarks>
        /// Deliberately narrow. A namespace URI is compared for equality and never dereferenced, so almost
        /// any text will serve; what will not is text XML cannot carry in an <c>xmlns</c> attribute or that
        /// no parser would read back as one URI — a space in the middle, a control character, or more than
        /// one fragment marker. Anything else is left alone rather than judged against a grammar the
        /// specification does not require it to meet.
        /// </remarks>
        /// <param name="text">The candidate URI.</param>
        /// <returns><see langword="true"/> when nothing in it rules it out.</returns>
        public static bool IsUriReference(string text)
        {
            int fragments = 0;

            foreach (char character in text)
            {
                if (character == '#' && ++fragments > 1)
                {
                    return false;
                }

                if (character <= ' ' || character is '<' or '>' or '"' or '{' or '}' or '|' or '\\' or '^' or '`')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
