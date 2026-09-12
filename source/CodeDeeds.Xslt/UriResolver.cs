using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CodeDeeds.Xslt
{
    /// <summary>
    /// Serves referenced resources by URI: over HTTP and HTTPS from the network, and from a single directory
    /// tree on the local file system.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FileResolver"/> for a stylesheet that also reaches the web — one that imports a published
    /// library, say — and the two halves are gated separately. The network half is open as soon as the
    /// resolver exists, since reaching a URL is what it is for; how far it may reach is decided by the
    /// <see cref="HttpClient"/> it sends through, which a caller may restrict with a handler of its own. The
    /// file half is closed until a root directory is named, and is then contained the way
    /// <see cref="FileResolver"/> contains it: a resolved path is canonicalized and checked to be inside the
    /// root, and one that is not is refused rather than clamped, so the failure is visible.
    /// </para>
    /// <para>
    /// A relative reference resolves as a URI reference against the identity of the resource it was written
    /// in. A module fetched from <c>https://example.org/xsl/main.xsl</c> that imports <c>modules/part.xsl</c>
    /// is therefore asking for <c>https://example.org/xsl/modules/part.xsl</c>, and a module read from the
    /// root directory that imports one by <c>https://</c> gets it from the network — each half by its own
    /// rule, whichever half the reference was written in. Where a response was redirected, the identity is
    /// where the resource ended up, so that what it refers to resolves from there.
    /// </para>
    /// <para>
    /// A reference with no base to resolve against — one in the stylesheet the caller supplied directly, with
    /// no <see cref="XsltOptions.BaseUri"/> — resolves against the root directory where there is one, as it
    /// does for <see cref="FileResolver"/>, and must otherwise be absolute.
    /// </para>
    /// <para>
    /// Requests go through the client's synchronous <see cref="HttpClient.Send(HttpRequestMessage, HttpCompletionOption)"/>,
    /// because <see cref="IXsltResolver"/> is synchronous; a handler that supports only the asynchronous path
    /// is waited on instead.
    /// </para>
    /// </remarks>
    public sealed class UriResolver : IXsltResolver
    {
        private static readonly Lazy<HttpClient> s_sharedClient = new Lazy<HttpClient>(CreateSharedClient);

        private readonly string? m_root;
        private readonly Uri? m_rootUri;
        private readonly HttpClient m_client;
        private readonly Encoding? m_encoding;

        /// <summary>
        /// Initializes a resolver that fetches over HTTP and, if a directory is named, reads from it.
        /// </summary>
        /// <param name="rootDirectory">
        /// The directory referenced files are read from, or <see langword="null"/> to refuse every reference
        /// to a file.
        /// </param>
        /// <param name="httpClient">
        /// The client to fetch with, or <see langword="null"/> for one shared by every resolver not given its
        /// own. The caller keeps ownership of a client it supplies, and decides through it what the resolver
        /// may reach: a timeout, a proxy, credentials, or a handler that refuses hosts it should not visit.
        /// </param>
        /// <param name="encoding">
        /// The encoding to read with, or <see langword="null"/> to take it from a response's
        /// <c>Content-Type</c> or a byte order mark, and to assume UTF-8 where neither says.
        /// </param>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        public UriResolver(string? rootDirectory = null, HttpClient? httpClient = null, Encoding? encoding = null)
        {
            if (rootDirectory is not null)
            {
                if (!Directory.Exists(rootDirectory))
                {
                    throw new DirectoryNotFoundException(
                        $"The resource directory '{rootDirectory}' does not exist.");
                }

                // Resolved once so that the containment check compares two canonical paths, and kept as a
                // URI with a trailing separator so that a relative reference resolves inside it rather than
                // beside it.
                m_root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
                m_rootUri = new Uri(m_root + Path.DirectorySeparatorChar);
            }

            m_client = httpClient ?? s_sharedClient.Value;
            m_encoding = encoding;
        }

        /// <inheritdoc/>
        public ResolvedResource? Resolve(string href, string? baseUri)
        {
            ArgumentNullException.ThrowIfNull(href);

            Uri target = Locate(href, baseUri);

            if (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps)
            {
                return Fetch(target, href);
            }

            if (target.IsFile)
            {
                return Open(target, href);
            }

            throw new XsltException(
                $"The reference '{href}' resolves to '{target}', and this resolver does not follow "
                + $"'{target.Scheme}:' references.");
        }

        /// <summary>Works out which URI a reference names: absolute as written, or resolved against its base.</summary>
        private Uri Locate(string href, string? baseUri)
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute))
            {
                return absolute;
            }

            Uri? against = BaseOf(baseUri);

            if (against is null)
            {
                throw new XsltException(
                    $"The reference '{href}' is relative, and there is nothing to resolve it against: no base "
                    + "URI was given and no root directory was configured.");
            }

            if (!Uri.TryCreate(against, href, out Uri? resolved))
            {
                throw new XsltException($"'{href}' is not a usable reference.");
            }

            return resolved;
        }

        /// <summary>The URI a relative reference resolves against, or <see langword="null"/> where there is none.</summary>
        private Uri? BaseOf(string? baseUri)
        {
            if (baseUri is null)
            {
                return m_rootUri;
            }

            if (Uri.TryCreate(baseUri, UriKind.Absolute, out Uri? absolute))
            {
                return absolute;
            }

            // A base that is itself relative — a bare file name handed to XsltOptions.BaseUri — is relative
            // to the root directory, as the reference would have been without it.
            return m_rootUri is not null && Uri.TryCreate(m_rootUri, baseUri, out Uri? resolved)
                ? resolved
                : null;
        }

        private ResolvedResource? Open(Uri target, string href)
        {
            if (m_root is null)
            {
                throw new XsltException(
                    $"The reference '{href}' names a file, and this resolver was given no directory to read "
                    + "files from.");
            }

            string path;

            try
            {
                path = Path.GetFullPath(target.LocalPath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                throw new XsltException($"'{href}' is not a usable file reference.", exception);
            }

            if (!FileResolver.IsInside(path, m_root))
            {
                throw new XsltException(
                    $"The reference '{href}' resolves outside '{m_root}' and was refused.");
            }

            if (!File.Exists(path))
            {
                return null;
            }

            StreamReader reader = m_encoding is null
                ? new StreamReader(path, detectEncodingFromByteOrderMarks: true)
                : new StreamReader(path, m_encoding);

            // Identified by the canonical path as a URI, so that the same file reached by two spellings is
            // one resource, and a reference inside it resolves as a URI reference against it.
            return new ResolvedResource(reader, new Uri(path).AbsoluteUri);
        }

        private ResolvedResource? Fetch(Uri target, string href)
        {
            HttpResponseMessage response;

            try
            {
                response = Send(target);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                // Wrapped so that a failure to reach the network is the resolver refusing the resource,
                // which is what the *-available functions and fn:transform() know how to answer.
                throw new XsltException(
                    $"'{href}' could not be fetched from '{target}': {exception.Message}", exception);
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // The server's way of saying the file does not exist, which for a file is null.
                response.Dispose();
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                string answer = $"{(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
                response.Dispose();
                throw new XsltException(
                    $"'{href}' could not be fetched from '{target}': the server answered {answer}.");
            }

            // Where the request was redirected, the resource lives where it ended up, and that is what a
            // reference inside it resolves against.
            Uri identity = response.RequestMessage?.RequestUri ?? target;

            // The reader owns the content stream, and disposing that is what returns the connection; the
            // response message holds nothing else worth releasing.
            Stream content = response.Content.ReadAsStream();
            Encoding? encoding = m_encoding ?? DeclaredEncoding(response.Content.Headers.ContentType);

            StreamReader reader = encoding is null
                ? new StreamReader(content, detectEncodingFromByteOrderMarks: true)
                : new StreamReader(content, encoding, detectEncodingFromByteOrderMarks: true);

            return new ResolvedResource(reader, identity.AbsoluteUri);
        }

        private HttpResponseMessage Send(Uri target)
        {
            try
            {
                return m_client.Send(
                    new HttpRequestMessage(HttpMethod.Get, target), HttpCompletionOption.ResponseHeadersRead);
            }
            catch (NotSupportedException)
            {
                // A handler written for the asynchronous path alone — a test double, usually — refuses the
                // synchronous one. A request message cannot be sent twice, so the second attempt is a new one.
                return m_client
                    .SendAsync(new HttpRequestMessage(HttpMethod.Get, target), HttpCompletionOption.ResponseHeadersRead)
                    .GetAwaiter()
                    .GetResult();
            }
        }

        /// <summary>The encoding a response's <c>Content-Type</c> names, if it names one this platform knows.</summary>
        private static Encoding? DeclaredEncoding(MediaTypeHeaderValue? contentType)
        {
            string? charset = contentType?.CharSet?.Trim('"');

            if (string.IsNullOrEmpty(charset))
            {
                return null;
            }

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                // A charset nobody has heard of is no guide; the byte order mark and the UTF-8 default are.
                return null;
            }
        }

        private static HttpClient CreateSharedClient()
        {
            // One client for every resolver not given its own, since each client keeps a connection pool
            // and a resolver is made about as often as a stylesheet is compiled. The pooled-connection
            // lifetime is what lets a client that lives as long as the process notice a DNS change.
            HttpClient client = new HttpClient(
                new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) },
                disposeHandler: true);

            // Named, because a server fronted by a CDN commonly refuses a request that does not say who it is.
            string version = typeof(UriResolver).Assembly.GetName().Version?.ToString(2) ?? "1.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CodeDeeds.Xslt", version));

            return client;
        }
    }
}
