using System.Text;

namespace CodeDeeds.Xslt
{
    /// <summary>
    /// Serves referenced resources from a single directory tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every resolved path is canonicalized and then checked to be inside the root directory, so an
    /// <c>href</c> cannot escape it — not with <c>../</c>, not with an absolute path, and not through a
    /// symbolic link, since the check runs against the fully resolved path rather than the text of the
    /// reference. A reference that would leave the root is refused rather than being quietly clamped, so the
    /// failure is visible.
    /// </para>
    /// <para>
    /// The root is the entire trust decision: a stylesheet supplied by an untrusted party can read any
    /// stylesheet beneath it, and nothing else.
    /// </para>
    /// <para>
    /// A directory under the root is a collection, for <c>fn:collection()</c> and <c>fn:uri-collection()</c>:
    /// its files, in name order, are what the collection holds, and the default collection is the root
    /// itself. A query on the collection URI narrows that, <c>data?select=*.xml;recurse=yes</c> being every
    /// XML file under <c>data</c>. What is handed back are file paths, which is what this resolver
    /// identifies a document by, so the same instance serves as <see cref="XsltOptions.DocumentResolver"/>
    /// to read them.
    /// </para>
    /// </remarks>
    public sealed class FileResolver : IXsltResolver, IXsltCollectionResolver
    {
        private readonly string m_root;
        private readonly Encoding? m_encoding;

        /// <summary>
        /// Initializes a resolver rooted at a directory.
        /// </summary>
        /// <param name="rootDirectory">The directory referenced stylesheets are read from.</param>
        /// <param name="encoding">The encoding to read with, or <see langword="null"/> to detect it.</param>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        public FileResolver(string rootDirectory, Encoding? encoding = null)
        {
            ArgumentNullException.ThrowIfNull(rootDirectory);

            if (!Directory.Exists(rootDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"The stylesheet directory '{rootDirectory}' does not exist.");
            }

            // Resolving the root once means the containment check below compares two canonical paths.
            m_root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
            m_encoding = encoding;
        }

        /// <inheritdoc/>
        public ResolvedResource? Resolve(string href, string? baseUri)
        {
            ArgumentNullException.ThrowIfNull(href);

            string candidate = Locate(href, baseUri, "stylesheet reference");

            if (!File.Exists(candidate))
            {
                return null;
            }

            StreamReader reader = m_encoding is null
                ? new StreamReader(candidate, detectEncodingFromByteOrderMarks: true)
                : new StreamReader(candidate, m_encoding);

            return new ResolvedResource(reader, candidate);
        }

        /// <inheritdoc/>
        public IReadOnlyList<string>? ResolveCollection(string? uri, string? baseUri)
        {
            if (uri is null)
            {
                return DirectoryCollection.List(m_root, DirectoryCollection.Query.Everything, path => path);
            }

            string reference = DirectoryCollection.Split(uri, out DirectoryCollection.Query query);

            // A query alone names the directory the call was written in, which is what nothing before the
            // question mark means.
            string directory = Locate(reference.Length == 0 ? "." : reference, baseUri, "collection reference");

            if (!Directory.Exists(directory))
            {
                return null;
            }

            return DirectoryCollection.List(directory, query, path => path);
        }

        /// <summary>
        /// Works out which path a reference names, and refuses one outside the root.
        /// </summary>
        /// <param name="reference">The reference as written.</param>
        /// <param name="baseUri">The path of the resource it was written in, or null for the root.</param>
        /// <param name="what">What kind of reference it is, for what a refusal says.</param>
        private string Locate(string reference, string? baseUri, string what)
        {
            // A reference is relative to the stylesheet that made it, so that a subdirectory can refer to its
            // own neighbours.
            string directory = baseUri is null ? m_root : Path.GetDirectoryName(baseUri) ?? m_root;
            string candidate;

            try
            {
                candidate = Path.GetFullPath(Path.Combine(directory, reference));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                throw new XsltException($"'{reference}' is not a usable {what}.", exception);
            }

            if (!IsInside(candidate, m_root))
            {
                throw new XsltException(
                    $"The {what} '{reference}' resolves outside '{m_root}' and was refused.");
            }

            return candidate;
        }

        /// <summary>
        /// Whether a canonical path lies inside a canonical root directory, or is the root itself.
        /// </summary>
        /// <remarks>
        /// Shared with <see cref="UriResolver"/>, which contains its file half the same way.
        /// </remarks>
        internal static bool IsInside(string fullPath, string root)
        {
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // A prefix match alone would let "C:\rootevil" pass for root "C:\root", so the next character
            // must actually be a separator — or the path must be the root itself.
            return fullPath.Length == root.Length
                || fullPath[root.Length] == Path.DirectorySeparatorChar
                || fullPath[root.Length] == Path.AltDirectorySeparatorChar;
        }
    }
}
