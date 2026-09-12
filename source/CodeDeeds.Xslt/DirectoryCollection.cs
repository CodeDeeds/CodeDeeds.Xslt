namespace CodeDeeds.Xslt
{
    /// <summary>
    /// A directory read as a collection, which is what <see cref="FileResolver"/> and
    /// <see cref="UriResolver"/> make of one for <c>fn:collection()</c> and <c>fn:uri-collection()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The collection holds the directory's files, every one of them, in name order so that it is the same
    /// sequence on every file system and on every run. A query on the collection URI narrows that:
    /// <c>select</c> is a file pattern with <c>*</c> and <c>?</c> as wildcards, and <c>recurse=yes</c>
    /// descends into subdirectories. So <c>data?select=*.xml;recurse=yes</c> is every XML file under
    /// <c>data</c>, and <c>data</c> alone is whatever is in it. The parameters are the ones other
    /// processors read on a directory collection, so that a stylesheet written for one need not be
    /// rewritten for this.
    /// </para>
    /// <para>
    /// Everything in the directory rather than only what looks like XML, because <c>uri-collection()</c>
    /// is how a stylesheet finds resources it then reads as text or JSON, and a collection that silently
    /// left those out would be answering a narrower question than was asked. A stylesheet wanting only
    /// the XML says so with <c>select</c>.
    /// </para>
    /// </remarks>
    internal static class DirectoryCollection
    {
        /// <summary>What the query on a collection URI asks: which files, and whether to descend.</summary>
        internal readonly record struct Query(string Select, bool Recurse)
        {
            /// <summary>Every file directly in the directory, which is what no query asks for.</summary>
            public static Query Everything => new Query("*", false);
        }

        /// <summary>
        /// Splits a collection reference into the directory it names and the query on it.
        /// </summary>
        /// <param name="uri">The collection URI as written.</param>
        /// <param name="query">What the query part asked, or everything where there was none.</param>
        /// <returns>The reference without its query.</returns>
        internal static string Split(string uri, out Query query)
        {
            int mark = uri.IndexOf('?');

            if (mark < 0)
            {
                query = Query.Everything;
                return uri;
            }

            query = Parse(uri[(mark + 1)..]);
            return uri[..mark];
        }

        /// <summary>Reads the query part of a collection URI.</summary>
        /// <param name="query">The text after the <c>?</c>.</param>
        /// <exception cref="XsltException">A parameter this collection does not take, or a value it cannot read.</exception>
        internal static Query Parse(string query)
        {
            string select = "*";
            bool recurse = false;

            foreach (string part in query.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = part.IndexOf('=');
                string key = equals < 0 ? part : part[..equals];
                string value = equals < 0 ? string.Empty : Uri.UnescapeDataString(part[(equals + 1)..]);

                switch (key)
                {
                    case "select":
                        select = value;
                        break;

                    case "recurse":
                        recurse = value switch
                        {
                            "yes" => true,
                            "no" => false,
                            _ => throw new XsltException(
                                $"'{part}' in a collection URI: 'recurse' is 'yes' or 'no'."),
                        };
                        break;

                    default:
                        // Refused rather than ignored, so that a misspelling is visible instead of being a
                        // collection quietly wider than the one asked for.
                        throw new XsltException(
                            $"'{key}' is not a parameter a directory collection takes: 'select' and "
                            + "'recurse' are.");
                }
            }

            return new Query(select, recurse);
        }

        /// <summary>Lists the files a directory holds as a collection.</summary>
        /// <param name="directory">The canonical path of the directory.</param>
        /// <param name="query">Which files, and whether to descend.</param>
        /// <param name="identity">How the resolver identifies a file: as a path, or as a <c>file:</c> URI.</param>
        /// <returns>The identities, in name order.</returns>
        internal static IReadOnlyList<string> List(string directory, Query query, Func<string, string> identity)
        {
            string[] files;

            try
            {
                // The default enumeration options skip hidden and system files and whatever cannot be
                // read, which is what a listing means to a person looking at the directory.
                files = Directory.GetFiles(
                    directory, query.Select, new EnumerationOptions { RecurseSubdirectories = query.Recurse });
            }
            catch (ArgumentException exception)
            {
                throw new XsltException(
                    $"'{query.Select}' is not a usable file pattern for a collection.", exception);
            }

            Array.Sort(files, StringComparer.Ordinal);

            string[] uris = new string[files.Length];

            for (int index = 0; index < files.Length; index++)
            {
                uris[index] = identity(Path.GetFullPath(files[index]));
            }

            return uris;
        }
    }
}
