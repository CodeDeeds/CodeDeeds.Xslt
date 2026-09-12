namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:collection()</c> and <c>fn:uri-collection()</c>: what a collection resolver names,
    /// how a directory is one, and what happens without a resolver.
    /// </summary>
    [TestClass]
    public sealed class CollectionTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>A directory under the temporary directory, deleted when the test is done with it.</summary>
        private sealed class TempRoot : IDisposable
        {
            public TempRoot()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xslt-coll-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public string Write(string relativePath, string text)
            {
                string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relativePath));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
                return full;
            }

            /// <summary>A root holding the documents most of these tests read.</summary>
            public TempRoot WithDocuments()
            {
                Write("data/a.xml", "<doc n=\"a\"/>");
                Write("data/b.xml", "<doc n=\"b\"/>");
                Write("data/notes.txt", "not xml");
                Write("data/sub/c.xml", "<doc n=\"c\"/>");
                Write("top.xml", "<doc n=\"top\"/>");
                return this;
            }

            public void Dispose()
            {
                Directory.Delete(Path, recursive: true);
            }
        }

        /// <summary>
        /// A resolver over a dictionary: what a caller keeping its collections somewhere other than a
        /// directory writes.
        /// </summary>
        private sealed class NamedCollections : IXsltCollectionResolver
        {
            private readonly Dictionary<string, string[]> m_named = new(StringComparer.Ordinal);

            public string[]? Default { get; init; }

            public List<(string? Uri, string? Base)> Asked { get; } = new();

            public NamedCollections Add(string name, params string[] uris)
            {
                m_named[name] = uris;
                return this;
            }

            public IReadOnlyList<string>? ResolveCollection(string? uri, string? baseUri)
            {
                Asked.Add((uri, baseUri));
                return uri is null ? Default : m_named.TryGetValue(uri, out string[]? members) ? members : null;
            }
        }

        /// <summary>Documents by name, identified by the name they were asked for by.</summary>
        private sealed class Documents : IXsltResolver
        {
            private readonly Dictionary<string, string> m_documents = new(StringComparer.Ordinal);

            public Documents Add(string uri, string xml)
            {
                m_documents[uri] = xml;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_documents.TryGetValue(href, out string? xml)
                    ? new ResolvedResource(new StringReader(xml), href)
                    : null;
            }
        }

        private static string Sheet(string body, string version = "3.0", string declarations = "")
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"{Xsl}\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " xmlns:err=\"http://www.w3.org/2005/xqt-errors\""
                + " xmlns:f=\"urn:f\" exclude-result-prefixes=\"xs err f\">"
                + declarations
                + $"<xsl:template match=\"/\"><out>{body}</out></xsl:template></xsl:stylesheet>";
        }

        private static XsltOptions Options(
            XsltBackend backend,
            IXsltResolver? documents,
            IXsltCollectionResolver? collections,
            string? baseUri = null,
            IXsltResolver? stylesheets = null,
            XsltVersion? version = null)
        {
            return new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                DocumentResolver = documents,
                CollectionResolver = collections,
                StylesheetResolver = stylesheets,
                BaseUri = baseUri,
                Version = version ?? XsltVersion.Implemented,
            };
        }

        /// <summary>Runs on both backends, which must agree, and returns what the out element holds.</summary>
        private static string Both(string stylesheet, Func<XsltBackend, XsltOptions> options)
        {
            string interpreted = new Xslt(stylesheet, options(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, options(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        private static string Names(string expression)
        {
            // The last segment of each URI, whichever separator the resolver identifies files with.
            return $"<xsl:value-of select=\"for $u in {expression} return replace($u, '^.*[/\\\\]', '')\" separator=\",\"/>";
        }

        [TestMethod]
        public void ADirectoryIsACollectionOfItsFiles()
        {
            using TempRoot root = new TempRoot().WithDocuments();
            FileResolver files = new FileResolver(root.Path);
            XsltOptions With(XsltBackend backend) => Options(backend, files, files);

            // Everything in the directory, in name order, and nothing from below it.
            Assert.AreEqual("a.xml,b.xml,notes.txt", Both(Sheet(Names("uri-collection('data')")), With));

            // The documents are read through the document resolver; a select leaves the text file out.
            Assert.AreEqual(
                "a,b",
                Both(Sheet("<xsl:value-of select=\"collection('data?select=*.xml')/doc/@n\" separator=\",\"/>"), With));

            // recurse=yes descends.
            Assert.AreEqual(
                "a,b,c",
                Both(Sheet("<xsl:value-of select=\"collection('data?select=*.xml;recurse=yes')/doc/@n\" separator=\",\"/>"), With));

            // What uri-collection() hands back is xs:anyURI, and each is something doc() can read.
            Assert.AreEqual(
                "true,a",
                Both(
                    Sheet(
                        "<xsl:value-of select=\"uri-collection('data?select=a.xml') instance of xs:anyURI+, "
                        + "doc(uri-collection('data?select=a.xml'))/doc/@n\" separator=\",\"/>"),
                    With));

            // Without a select, the text file is asked to be XML and is not.
            Assert.AreEqual("FODC0002", Fails(Sheet("<xsl:value-of select=\"count(collection('data'))\"/>"), With));
        }

        [TestMethod]
        public void TheDefaultCollectionOfADirectoryResolverIsItsRoot()
        {
            using TempRoot root = new TempRoot().WithDocuments();
            FileResolver files = new FileResolver(root.Path);
            XsltOptions With(XsltBackend backend) => Options(backend, files, files);

            Assert.AreEqual("top.xml", Both(Sheet(Names("uri-collection()")), With));
            Assert.AreEqual("top", Both(Sheet("<xsl:value-of select=\"collection()/doc/@n\"/>"), With));
            Assert.AreEqual("top", Both(Sheet("<xsl:value-of select=\"collection(())/doc/@n\"/>"), With));

            // A query alone names the directory the call was written in.
            Assert.AreEqual(
                "a.xml,b.xml",
                Both(
                    Sheet(Names("uri-collection('?select=*.xml')")),
                    backend => Options(backend, files, files, baseUri: Path.Combine(root.Path, "data", "main.xsl"))));
        }

        [TestMethod]
        public void ACollectionIsStableWithinATransformation()
        {
            using TempRoot root = new TempRoot().WithDocuments();
            FileResolver files = new FileResolver(root.Path);
            XsltOptions With(XsltBackend backend) => Options(backend, files, files);

            // The same nodes every time it is asked for, so a union of two askings is one collection, and a
            // document reached through the collection is the document reached by name.
            Assert.AreEqual(
                "2,true,true",
                Both(
                    Sheet(
                        "<xsl:value-of select=\"count(collection('data?select=*.xml') | collection('data?select=*.xml')), "
                        + "collection('data?select=*.xml')[1] is collection('data?select=*.xml')[1], "
                        + "collection('data?select=*.xml')[1] is document('data/a.xml')\" separator=\",\"/>"),
                    With));

            // The resolver is asked once per transformation for a reference, however often the stylesheet asks.
            NamedCollections named = new NamedCollections().Add("things", Path.Combine(root.Path, "top.xml"));

            Assert.AreEqual(
                "1,1,1",
                Both(
                    Sheet(
                        "<xsl:value-of select=\"count(collection('things')), count(uri-collection('things')), "
                        + "count(collection('things'))\" separator=\",\"/>"),
                    backend => Options(backend, files, named)));

            Assert.AreEqual(2, named.Asked.Count, "once for each of the two transformations");
        }

        [TestMethod]
        public void ARelativeCollectionUriResolvesWhereTheCallIsWritten()
        {
            using TempRoot root = new TempRoot().WithDocuments();
            root.Write("lib/docs/x.xml", "<doc n=\"x\"/>");
            root.Write(
                "lib/helper.xsl",
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\">"
                + "<xsl:function name=\"f:docs\" as=\"xs:anyURI*\"><xsl:sequence select=\"uri-collection('docs')\"/></xsl:function>"
                + "<xsl:function name=\"f:names\" as=\"xs:string*\"><xsl:sequence select=\"collection('docs')/doc/@n/string()\"/></xsl:function>"
                + "</xsl:stylesheet>");

            FileResolver files = new FileResolver(root.Path);
            XsltOptions With(XsltBackend backend) =>
                Options(backend, files, files, baseUri: Path.Combine(root.Path, "main.xsl"), stylesheets: files);

            // From inside the included module, 'docs' is lib/docs. From the principal stylesheet beside the
            // root there is no such directory.
            Assert.AreEqual(
                "1,x",
                Both(
                    Sheet(
                        "<xsl:value-of select=\"count(f:docs()), f:names()\" separator=\",\"/>",
                        declarations: "<xsl:include href=\"lib/helper.xsl\"/>"),
                    With));

            Assert.AreEqual("FODC0002", Fails(Sheet("<xsl:value-of select=\"count(uri-collection('docs'))\"/>"), With));
        }

        [TestMethod]
        public void WithoutAResolverThereIsNoCollection()
        {
            XsltOptions With(XsltBackend backend) => Options(backend, null, null);

            Assert.AreEqual("FODC0002", Fails(Sheet("<xsl:value-of select=\"count(collection())\"/>"), With));
            Assert.AreEqual("FODC0002", Fails(Sheet("<xsl:value-of select=\"count(collection('x'))\"/>"), With));
            Assert.AreEqual("FODC0002", Fails(Sheet("<xsl:value-of select=\"count(uri-collection())\"/>"), With));
            Assert.AreEqual("FODC0002", Fails(Sheet("<xsl:value-of select=\"count(uri-collection('x'))\"/>"), With));

            // The message says which option to set, and which function asked.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(Sheet("<xsl:value-of select=\"count(uri-collection('x'))\"/>"), With(XsltBackend.Interpreted))
                    .TransformXml("<r/>"));

            StringAssert.Contains(error.Message, "XsltOptions.CollectionResolver");
            StringAssert.Contains(error.Message, "fn:uri-collection()");
            StringAssert.Contains(error.Message, "'x'");
        }

        [TestMethod]
        public void ACollectionNeedsADocumentResolverToBeRead()
        {
            NamedCollections named = new NamedCollections().Add("things", "one.xml", "two.xml");
            XsltOptions With(XsltBackend backend) => Options(backend, null, named);

            // The URIs are there to be listed without one.
            Assert.AreEqual(
                "one.xml,two.xml",
                Both(Sheet("<xsl:value-of select=\"uri-collection('things')\" separator=\",\"/>"), With));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(Sheet("<xsl:value-of select=\"count(collection('things'))\"/>"), With(XsltBackend.Interpreted))
                    .TransformXml("<r/>"));

            StringAssert.Contains(error.Message, "XsltOptions.DocumentResolver");

            // An empty collection asks nothing of the document resolver.
            Assert.AreEqual(
                "0",
                Both(
                    Sheet("<xsl:value-of select=\"count(collection())\"/>"),
                    backend => Options(backend, null, new NamedCollections { Default = Array.Empty<string>() })));
        }

        [TestMethod]
        public void AnUnknownCollectionIsAnErrorAStylesheetCanCatch()
        {
            NamedCollections named = new NamedCollections().Add("things");
            Documents documents = new Documents();
            XsltOptions With(XsltBackend backend) => Options(backend, documents, named);

            Assert.AreEqual("FODC0002", Fails(Sheet("<xsl:value-of select=\"count(collection('nope'))\"/>"), With));

            // No default collection either, since the resolver has none.
            Assert.AreEqual("FODC0002", Fails(Sheet("<xsl:value-of select=\"count(uri-collection())\"/>"), With));

            Assert.AreEqual(
                "FODC0002,0",
                Both(
                    Sheet(
                        "<xsl:try select=\"count(collection('nope'))\">"
                        + "<xsl:catch select=\"local-name-from-QName($err:code)\"/></xsl:try>"
                        + "<xsl:text>,</xsl:text><xsl:value-of select=\"count(collection('things'))\"/>"),
                    With));
        }

        [TestMethod]
        public void ACollectionMayHoldAFragmentOfADocument()
        {
            // A member URI with a fragment identifier names the element with that ID, as document() reads
            // one, rather than the whole document; and a document named twice is one document.
            Documents documents = new Documents()
                .Add("doc.xml", "<r><a xml:id=\"frag\"><inner/></a><b/></r>")
                .Add("other.xml", "<r><c/></r>");
            NamedCollections named = new NamedCollections()
                .Add("mixed", "doc.xml#frag", "other.xml", "doc.xml#frag", "doc.xml#missing");
            XsltOptions With(XsltBackend backend) => Options(backend, documents, named);

            Assert.AreEqual(
                "a,r|2|4",
                Both(
                    Sheet(
                        "<xsl:value-of select=\"collection('mixed') ! (if (. instance of element()) then name() else 'r')\" separator=\",\"/>"
                        + "<xsl:text>|</xsl:text><xsl:value-of select=\"count(collection('mixed'))\"/>"
                        + "<xsl:text>|</xsl:text><xsl:value-of select=\"count(uri-collection('mixed'))\"/>"),
                    With));
        }

        [TestMethod]
        public void AUriResolverServesADirectoryAsFileUris()
        {
            using TempRoot root = new TempRoot().WithDocuments();
            UriResolver uris = new UriResolver(root.Path);
            XsltOptions With(XsltBackend backend) => Options(backend, uris, uris);

            Assert.AreEqual(
                "true,a,b,true",
                Both(
                    Sheet(
                        "<xsl:value-of select=\"every $u in uri-collection('data') satisfies starts-with($u, 'file:///'), "
                        + "collection('data?select=*.xml')/doc/@n, "
                        + "collection('data?select=*.xml')[1] is doc('data/a.xml')\" separator=\",\"/>"),
                    With));

            // An absolute file URI names a directory too, and the default collection is the root.
            string dataUri = new Uri(Path.Combine(root.Path, "data")).AbsoluteUri;

            Assert.AreEqual(
                "3,top.xml",
                Both(Sheet($"<xsl:value-of select=\"count(uri-collection('{dataUri}'))\"/>,{Names("uri-collection()")}"), With));

            // A server does not list what it holds.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(Sheet("<xsl:value-of select=\"count(uri-collection('http://example.org/data/'))\"/>"), With(XsltBackend.Interpreted))
                    .TransformXml("<r/>"));

            StringAssert.Contains(error.Message, "cannot be listed");
            Assert.IsNull(error.Code);

            // And without a root directory there is nothing to be a collection.
            Assert.AreEqual(
                "FODC0002",
                Fails(
                    Sheet("<xsl:value-of select=\"count(uri-collection())\"/>"),
                    backend => Options(backend, uris, new UriResolver())));
        }

        [TestMethod]
        public void ADirectoryOutsideTheRootIsRefused()
        {
            using TempRoot root = new TempRoot().WithDocuments();
            FileResolver files = new FileResolver(root.Path);
            UriResolver uris = new UriResolver(root.Path);

            foreach (IXsltCollectionResolver resolver in new IXsltCollectionResolver[] { files, uris })
            {
                // From data/, '..' is the root itself, which is inside; '../..' is not.
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(
                            Sheet("<xsl:value-of select=\"count(uri-collection('../..'))\"/>"),
                            Options(XsltBackend.Interpreted, files, resolver, baseUri: Path.Combine(root.Path, "data", "main.xsl")))
                        .TransformXml("<r/>"));

                StringAssert.Contains(error.Message, "outside");
                Assert.IsNull(error.Code);

                Assert.AreEqual(
                    "top.xml",
                    Both(
                        Sheet(Names("uri-collection('..')")),
                        backend => Options(backend, files, resolver, baseUri: Path.Combine(root.Path, "data", "main.xsl"))));

                // A directory that is not there is a collection that is not there.
                Assert.AreEqual(
                    "FODC0002",
                    Fails(
                        Sheet("<xsl:value-of select=\"count(uri-collection('nowhere'))\"/>"),
                        backend => Options(backend, files, resolver)));
            }
        }

        [TestMethod]
        public void AQueryTheDirectoryDoesNotUnderstandIsRefused()
        {
            using TempRoot root = new TempRoot().WithDocuments();
            FileResolver files = new FileResolver(root.Path);

            foreach ((string query, string complaint) in new[]
            {
                ("data?stable=yes", "'stable'"),
                ("data?recurse=maybe", "'recurse=maybe'"),
            })
            {
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => new Xslt(
                            Sheet($"<xsl:value-of select=\"count(uri-collection('{query}'))\"/>"),
                            Options(XsltBackend.Interpreted, files, files))
                        .TransformXml("<r/>"));

                StringAssert.Contains(error.Message, complaint);
            }
        }

        [TestMethod]
        public void ATwoPointZeroProcessorHasCollectionButNotUriCollection()
        {
            using TempRoot root = new TempRoot().WithDocuments();
            FileResolver files = new FileResolver(root.Path);
            XsltOptions With(XsltBackend backend) => Options(backend, files, files, version: XsltVersion.V20);

            Assert.AreEqual(
                "a,b",
                Both(Sheet("<xsl:value-of select=\"collection('data?select=*.xml')/doc/@n\" separator=\",\"/>", "2.0"), With));

            Assert.AreEqual(
                "XPST0017",
                Fails(Sheet("<xsl:value-of select=\"count(uri-collection('data'))\"/>", "2.0"), With));
        }

        [TestMethod]
        public void WhatIsNotAUriNamesNoCollection()
        {
            NamedCollections named = new NamedCollections().Add("things");
            XsltOptions With(XsltBackend backend) => Options(backend, new Documents(), named);

            Assert.AreEqual("FODC0004", Fails(Sheet("<xsl:value-of select=\"count(collection('http://'))\"/>"), With));
            Assert.AreEqual("FODC0004", Fails(Sheet("<xsl:value-of select=\"count(uri-collection('http://'))\"/>"), With));
        }

        private static string Fails(string stylesheet, Func<XsltBackend, XsltOptions> options)
        {
            string? interpreted = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, options(XsltBackend.Interpreted)).TransformXml("<r/>")).Code;
            string? compiled = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, options(XsltBackend.Compiled)).TransformXml("<r/>")).Code;

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted ?? string.Empty;
        }
    }
}
