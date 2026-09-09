using System.Text;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A URI reference split into the five parts RFC 3986 gives it, and the resolution of one against another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out rather than handed to <see cref="Uri"/>, because resolving and normalising are different
    /// operations and that class does both at once. It gives an authority that had no path an empty one, so
    /// <c>http://example.com</c> comes back as <c>http://example.com/</c>; it lower-cases the scheme and the
    /// host; and it decodes percent-escapes. Each of those changes a URI that <c>fn:resolve-uri</c> is
    /// required to return unchanged, and no option turns them off.
    /// </para>
    /// <para>
    /// RFC 3986 section 5.2 is a page of pseudocode that says exactly what to do, so it is written out here
    /// and nothing else is done to the text. What that buys is that a URI needing no resolution comes back
    /// character for character as it was given.
    /// </para>
    /// <para>
    /// A part that is absent is not a part that is empty. <c>http://x</c> has no query and <c>http://x?</c>
    /// has an empty one; they are different URIs, and the algorithm asks which of the two it is holding. So
    /// every optional part is a nullable string, and <see langword="null"/> means the delimiter was not there.
    /// </para>
    /// </remarks>
    internal readonly struct UriReference
    {
        private UriReference(string? scheme, string? authority, string path, string? query, string? fragment)
        {
            Scheme = scheme;
            Authority = authority;
            Path = path;
            Query = query;
            Fragment = fragment;
        }

        /// <summary>Gets the scheme without its colon, or <see langword="null"/> for a relative reference.</summary>
        public string? Scheme { get; }

        /// <summary>Gets the authority without its slashes, or <see langword="null"/> where there was none.</summary>
        public string? Authority { get; }

        /// <summary>Gets the path, which is the one part always present and may be empty.</summary>
        public string Path { get; }

        /// <summary>Gets the query without its question mark, or <see langword="null"/> where there was none.</summary>
        public string? Query { get; }

        /// <summary>Gets the fragment without its hash, or <see langword="null"/> where there was none.</summary>
        public string? Fragment { get; }

        /// <summary>
        /// Reads a URI reference, or reports that the text is not one.
        /// </summary>
        /// <remarks>
        /// Three things are checked, all of them ones the specification's tests ask about: a percent sign
        /// begins an escape and must be followed by two hexadecimal digits; a reference carries at most one
        /// fragment, the character introducing one not being allowed inside it; and a relative reference
        /// whose path does not start with a slash may not carry a colon in its first segment, that being
        /// how a path is told from a scheme. Characters outside the URI set are let through, because an IRI
        /// is a URI reference here and refusing them would refuse a path named in anything but ASCII.
        /// </remarks>
        /// <param name="text">The text to read.</param>
        /// <param name="reference">The parts, where the text is a URI reference.</param>
        public static bool TryParse(string text, out UriReference reference)
        {
            reference = default;

            if (!EscapesAreWellFormed(text))
            {
                return false;
            }

            string rest = text;
            string? fragment = null;
            string? query = null;
            string? scheme = null;
            string? authority = null;

            int hash = rest.IndexOf('#');
            if (hash >= 0)
            {
                fragment = rest[(hash + 1)..];
                rest = rest[..hash];

                // A fragment is what follows the first '#' and may hold no '#' of its own — RFC 3986 leaves
                // the character out of the production — so a reference carrying two is not a reference.
                if (fragment.IndexOf('#') >= 0)
                {
                    return false;
                }
            }

            int question = rest.IndexOf('?');
            if (question >= 0)
            {
                query = rest[(question + 1)..];
                rest = rest[..question];
            }

            int colon = SchemeLength(rest);
            if (colon > 0)
            {
                scheme = rest[..colon];
                rest = rest[(colon + 1)..];
            }

            if (rest.StartsWith("//", StringComparison.Ordinal))
            {
                int end = rest.IndexOf('/', 2);
                authority = end < 0 ? rest[2..] : rest[2..end];
                rest = end < 0 ? string.Empty : rest[end..];
            }

            // A relative path is written so that it cannot be mistaken for a scheme, which means no colon in
            // its first segment. This is what makes a lone ':' not a URI reference at all.
            if (scheme is null && authority is null && !rest.StartsWith("/", StringComparison.Ordinal))
            {
                int slash = rest.IndexOf('/');

                if ((slash < 0 ? rest : rest[..slash]).IndexOf(':') >= 0)
                {
                    return false;
                }
            }

            reference = new UriReference(scheme, authority, rest, query, fragment);
            return true;
        }

        /// <summary>
        /// Resolves a reference against a base URI, by RFC 3986 section 5.2.2.
        /// </summary>
        /// <remarks>
        /// Where the reference names a scheme nothing is read from the base at all, which is why an absolute
        /// reference can be answered with no base to resolve against, and why a base that could not be used
        /// is not an error until something needs to use it.
        /// </remarks>
        /// <param name="reference">The reference to resolve.</param>
        /// <param name="baseUri">The base to resolve against, unread where the reference has a scheme.</param>
        public static string Resolve(UriReference reference, UriReference baseUri)
        {
            string? scheme;
            string? authority;
            string path;
            string? query;

            if (reference.Scheme is not null)
            {
                scheme = reference.Scheme;
                authority = reference.Authority;
                path = RemoveDotSegments(reference.Path);
                query = reference.Query;
            }
            else
            {
                scheme = baseUri.Scheme;

                if (reference.Authority is not null)
                {
                    authority = reference.Authority;
                    path = RemoveDotSegments(reference.Path);
                    query = reference.Query;
                }
                else
                {
                    authority = baseUri.Authority;

                    if (reference.Path.Length == 0)
                    {
                        // An empty reference names the base document itself, so it keeps the base's query
                        // unless it wrote one of its own — and an empty query is one it wrote.
                        path = baseUri.Path;
                        query = reference.Query ?? baseUri.Query;
                    }
                    else
                    {
                        path = reference.Path[0] == '/'
                            ? RemoveDotSegments(reference.Path)
                            : RemoveDotSegments(Merge(baseUri, reference.Path));

                        query = reference.Query;
                    }
                }
            }

            return Compose(scheme, authority, path, query, reference.Fragment);
        }

        /// <summary>Puts a relative path where the base's last segment stood, by section 5.3.</summary>
        private static string Merge(UriReference baseUri, string path)
        {
            if (baseUri.Authority is not null && baseUri.Path.Length == 0)
            {
                return "/" + path;
            }

            int slash = baseUri.Path.LastIndexOf('/');
            return slash < 0 ? path : string.Concat(baseUri.Path.AsSpan(0, slash + 1), path);
        }

        /// <summary>
        /// Works <c>.</c> and <c>..</c> out against the segments before them, by section 5.2.4.
        /// </summary>
        /// <remarks>
        /// The output holds whole segments with their leading slash, so undoing a <c>..</c> is dropping the
        /// last of them — which is what the specification's "remove the last segment and its preceding
        /// separator" describes. A <c>..</c> with nothing before it is discarded rather than climbing past
        /// the root, a path having nothing above it to name.
        /// </remarks>
        private static string RemoveDotSegments(string path)
        {
            if (path.IndexOf('.') < 0)
            {
                return path;
            }

            string input = path;
            List<string> output = new List<string>();

            while (input.Length > 0)
            {
                if (input.StartsWith("../", StringComparison.Ordinal))
                {
                    input = input[3..];
                }
                else if (input.StartsWith("./", StringComparison.Ordinal))
                {
                    input = input[2..];
                }
                else if (input.StartsWith("/./", StringComparison.Ordinal))
                {
                    input = input[2..];
                }
                else if (input == "/.")
                {
                    input = "/";
                }
                else if (input.StartsWith("/../", StringComparison.Ordinal))
                {
                    input = input[3..];
                    Drop(output);
                }
                else if (input == "/..")
                {
                    input = "/";
                    Drop(output);
                }
                else if (input is "." or "..")
                {
                    input = string.Empty;
                }
                else
                {
                    int next = input.IndexOf('/', input[0] == '/' ? 1 : 0);

                    if (next < 0)
                    {
                        output.Add(input);
                        input = string.Empty;
                    }
                    else
                    {
                        output.Add(input[..next]);
                        input = input[next..];
                    }
                }
            }

            return string.Concat(output);
        }

        private static void Drop(List<string> output)
        {
            if (output.Count > 0)
            {
                output.RemoveAt(output.Count - 1);
            }
        }

        /// <summary>Writes the five parts back out as one string, by section 5.3.</summary>
        private static string Compose(
            string? scheme, string? authority, string path, string? query, string? fragment)
        {
            StringBuilder text = new StringBuilder();

            if (scheme is not null)
            {
                text.Append(scheme).Append(':');
            }

            if (authority is not null)
            {
                text.Append("//").Append(authority);
            }

            text.Append(path);

            if (query is not null)
            {
                text.Append('?').Append(query);
            }

            if (fragment is not null)
            {
                text.Append('#').Append(fragment);
            }

            return text.ToString();
        }

        /// <summary>
        /// Returns how many characters of a scheme stand at the start, or 0 where there is none.
        /// </summary>
        /// <remarks>
        /// A scheme is a letter followed by letters, digits and three punctuation marks, ending at the first
        /// colon. Anything else before that colon means there is no scheme, which is how the authority in
        /// <c>//host:80/x</c> avoids being read as one.
        /// </remarks>
        private static int SchemeLength(string text)
        {
            if (text.Length == 0 || !char.IsAsciiLetter(text[0]))
            {
                return 0;
            }

            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];

                if (c == ':')
                {
                    return i;
                }

                if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
                {
                    return 0;
                }
            }

            return 0;
        }

        private static bool EscapesAreWellFormed(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '%')
                {
                    continue;
                }

                if (i + 2 >= text.Length
                    || !char.IsAsciiHexDigit(text[i + 1])
                    || !char.IsAsciiHexDigit(text[i + 2]))
                {
                    return false;
                }

                i += 2;
            }

            return true;
        }
    }
}
