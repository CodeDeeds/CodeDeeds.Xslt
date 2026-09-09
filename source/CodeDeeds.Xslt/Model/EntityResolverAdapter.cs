using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace CodeDeeds.Xslt.Model
{
    /// <summary>
    /// Presents an <see cref="IXsltResolver"/> to the XML reader as the <see cref="XmlResolver"/> it fetches
    /// external entities through: the external subset of a document type declaration, and the parameter
    /// and general entities it declares as external.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reader asks in two steps, <see cref="ResolveUri"/> and then <see cref="GetEntity"/> with what
    /// the first answered, so the first does the fetching and the second hands over what was fetched. The
    /// URI handed between them only has to be an identity the two agree on: the resolver's own identity
    /// for the resource where it parses as one, and a placeholder where it does not, which a resolver
    /// addressing by path may hand back.
    /// </para>
    /// <para>
    /// What is fetched is kept as text, so that <see cref="DocumentTypeDeclaration"/> can read the external
    /// subset the reader has just read without fetching it a second time, and so that a text declaration
    /// naming an encoding is not applied twice — the resolver decoded the bytes, and the reader is handed
    /// characters.
    /// </para>
    /// </remarks>
    internal sealed class EntityResolverAdapter : XmlResolver
    {
        private readonly IXsltResolver m_resolver;
        private readonly string? m_documentBase;
        private readonly Dictionary<Uri, Fetched> m_byIdentity = new();
        private readonly Dictionary<string, Fetched> m_byReference = new(StringComparer.Ordinal);
        private int m_placeholders;

        private sealed record Fetched(Uri Key, string Identity, string Text);

        /// <summary>Initializes the adapter.</summary>
        /// <param name="resolver">The resolver that fetches what a declaration names.</param>
        /// <param name="documentBase">The base URI of the document, which its own references resolve against.</param>
        public EntityResolverAdapter(IXsltResolver resolver, string? documentBase)
        {
            m_resolver = resolver;
            m_documentBase = documentBase;
        }

        /// <inheritdoc/>
        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
        {
            // Before opening an entity declared in a subset it fetched, the reader asks for that subset's
            // URI as though it were a reference — and it is one this adapter answered already.
            if (baseUri is null
                && relativeUri is not null
                && Uri.TryCreate(relativeUri, UriKind.Absolute, out Uri? known)
                && m_byIdentity.ContainsKey(known))
            {
                return known;
            }

            string? against = baseUri is null
                ? m_documentBase
                : m_byIdentity.TryGetValue(baseUri, out Fetched? fetched) ? fetched.Identity : baseUri.OriginalString;

            return Fetch(relativeUri ?? string.Empty, against).Key;
        }

        /// <inheritdoc/>
        public override bool SupportsType(Uri absoluteUri, Type? type)
        {
            return type is null || type == typeof(TextReader) || type == typeof(Stream);
        }

        /// <inheritdoc/>
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (!m_byIdentity.TryGetValue(absoluteUri, out Fetched? fetched))
            {
                fetched = Fetch(absoluteUri.OriginalString, m_documentBase);
            }

            return ofObjectToReturn == typeof(Stream)
                ? new MemoryStream(Encoding.UTF8.GetBytes(fetched.Text))
                : new StringReader(fetched.Text);
        }

        /// <summary>
        /// The text of an external entity, by the reference and base its declaration gave — the same fetch
        /// the reader's goes through, so an entity the reader has read is not read again.
        /// </summary>
        public string TextOf(string href, string? baseUri)
        {
            return Fetch(href, baseUri).Text;
        }

        private Fetched Fetch(string href, string? baseUri)
        {
            string reference = string.Concat(baseUri, "\n", href);

            if (m_byReference.TryGetValue(reference, out Fetched? known))
            {
                return known;
            }

            ResolvedResource resolved = m_resolver.Resolve(href, baseUri)
                ?? throw new XsltException(
                    $"The external entity '{href}' named by a document type declaration could not be found.");

            string text;

            using (resolved.Reader)
            {
                text = resolved.Reader.ReadToEnd();
            }

            Uri key = Uri.TryCreate(resolved.Uri, UriKind.Absolute, out Uri? parsed)
                ? parsed
                : new Uri("urn:codedeeds-xslt:entity:" + ++m_placeholders);

            Fetched fetched = new Fetched(key, resolved.Uri, text);
            m_byReference[reference] = fetched;
            m_byIdentity[key] = fetched;
            return fetched;
        }
    }
}
