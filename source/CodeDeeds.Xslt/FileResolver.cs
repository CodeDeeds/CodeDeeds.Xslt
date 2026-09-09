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
    /// </remarks>
    public sealed class FileResolver : IXsltResolver
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

            // A reference is relative to the stylesheet that made it, so that a subdirectory can refer to its
            // own neighbours.
            string directory = baseUri is null ? m_root : Path.GetDirectoryName(baseUri) ?? m_root;
            string candidate;

            try
            {
                candidate = Path.GetFullPath(Path.Combine(directory, href));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                throw new XsltException($"'{href}' is not a usable stylesheet reference.", exception);
            }

            if (!IsInsideRoot(candidate))
            {
                throw new XsltException(
                    $"The stylesheet reference '{href}' resolves outside '{m_root}' and was refused.");
            }

            if (!File.Exists(candidate))
            {
                return null;
            }

            StreamReader reader = m_encoding is null
                ? new StreamReader(candidate, detectEncodingFromByteOrderMarks: true)
                : new StreamReader(candidate, m_encoding);

            return new ResolvedResource(reader, candidate);
        }

        private bool IsInsideRoot(string fullPath)
        {
            if (!fullPath.StartsWith(m_root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // A prefix match alone would let "C:\rootevil" pass for root "C:\root", so the next character
            // must actually be a separator — or the path must be the root itself.
            return fullPath.Length == m_root.Length
                || fullPath[m_root.Length] == Path.DirectorySeparatorChar
                || fullPath[m_root.Length] == Path.AltDirectorySeparatorChar;
        }
    }
}
