using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <see cref="UriResolver"/>: what it fetches over HTTP, what it reads from its root directory,
    /// and how a reference written in one half resolves into the other.
    /// </summary>
    [TestClass]
    public sealed class UriResolverTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Answers requests from a dictionary, so that no test touches the network.</summary>
        private sealed class Server : HttpMessageHandler
        {
            private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> m_routes =
                new(StringComparer.Ordinal);

            public List<string> Requested { get; } = new();

            /// <summary>Whether the handler answers the synchronous <c>Send</c>, as a real one does.</summary>
            public bool Synchronous { get; init; } = true;

            public Server Text(string uri, string body, Encoding? encoding = null)
            {
                return Route(uri, _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, encoding ?? Encoding.UTF8, "application/xml"),
                });
            }

            public Server Bytes(string uri, byte[] body, string? contentType)
            {
                return Route(uri, _ =>
                {
                    ByteArrayContent content = new ByteArrayContent(body);
                    content.Headers.ContentType = contentType is null ? null : new MediaTypeHeaderValue(contentType);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
                });
            }

            public Server Status(string uri, HttpStatusCode status)
            {
                return Route(uri, _ => new HttpResponseMessage(status));
            }

            /// <summary>Answers as a real client would after following a redirect to <paramref name="finalUri"/>.</summary>
            public Server Redirect(string uri, string finalUri, string body)
            {
                return Route(uri, _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/xml"),
                    RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri),
                });
            }

            public Server Route(string uri, Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                m_routes[uri] = respond;
                return this;
            }

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (!Synchronous)
                {
                    throw new NotSupportedException("This handler answers asynchronously only.");
                }

                return Respond(request);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(Respond(request));
            }

            private HttpResponseMessage Respond(HttpRequestMessage request)
            {
                string uri = request.RequestUri!.AbsoluteUri;
                Requested.Add(uri);

                HttpResponseMessage response = m_routes.TryGetValue(uri, out Func<HttpRequestMessage, HttpResponseMessage>? respond)
                    ? respond(request)
                    : new HttpResponseMessage(HttpStatusCode.NotFound);

                response.RequestMessage ??= request;
                return response;
            }
        }

        /// <summary>A directory under the temporary directory, deleted when the test is done with it.</summary>
        private sealed class TempRoot : IDisposable
        {
            public TempRoot()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xslt-uri-" + Guid.NewGuid().ToString("N"));
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

            public void Dispose()
            {
                Directory.Delete(Path, recursive: true);
            }
        }

        private static string Sheet(string body)
        {
            return $"<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"{Xsl}\">{body}</xsl:stylesheet>";
        }

        private static string Template(string result)
        {
            return Sheet($"<xsl:template match=\"/\">{result}</xsl:template>");
        }

        private static string Run(string stylesheet, UriResolver resolver, string? baseUri = null)
        {
            return new Xslt(
                stylesheet,
                new XsltOptions
                {
                    StylesheetResolver = resolver,
                    DocumentResolver = resolver,
                    OmitXmlDeclaration = true,
                    BaseUri = baseUri,
                }).TransformXml("<r/>");
        }

        private static UriResolver Over(Server server, string? rootDirectory = null, Encoding? encoding = null)
        {
            return new UriResolver(rootDirectory, new HttpClient(server), encoding);
        }

        // ---- The network half ----------------------------------------------------------------------------

        [TestMethod]
        public void AModuleFetchedOverHttpResolvesItsOwnReferencesAgainstWhereItCameFrom()
        {
            Server server = new Server()
                .Text("https://example.test/xsl/main.xsl", Sheet("<xsl:import href=\"modules/part.xsl\"/>"))
                .Text("https://example.test/xsl/modules/part.xsl", Template("<from-http/>"));

            Assert.AreEqual(
                "<from-http/>",
                Run(Sheet("<xsl:import href=\"https://example.test/xsl/main.xsl\"/>"), Over(server)));

            CollectionAssert.AreEqual(
                new[] { "https://example.test/xsl/main.xsl", "https://example.test/xsl/modules/part.xsl" },
                server.Requested);
        }

        [TestMethod]
        public void ARedirectedModuleIsIdentifiedByWhereItEndedUp()
        {
            // The identity a redirect leaves the resource with is what its relative references resolve
            // against — a "current" alias pointing at a versioned directory, typically.
            Server server = new Server()
                .Redirect(
                    "https://example.test/current/main.xsl",
                    "https://example.test/1.2/main.xsl",
                    Sheet("<xsl:include href=\"part.xsl\"/>"))
                .Text("https://example.test/1.2/part.xsl", Template("<versioned/>"));

            Assert.AreEqual(
                "<versioned/>",
                Run(Sheet("<xsl:import href=\"https://example.test/current/main.xsl\"/>"), Over(server)));

            CollectionAssert.Contains(server.Requested, "https://example.test/1.2/part.xsl");
            CollectionAssert.DoesNotContain(server.Requested, "https://example.test/current/part.xsl");
        }

        [TestMethod]
        public void AMissingResourceIsNotFoundAndAServerFailureIsReported()
        {
            Server server = new Server()
                .Status("https://example.test/gone.xsl", HttpStatusCode.Gone)
                .Status("https://example.test/broken.xsl", HttpStatusCode.InternalServerError);

            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Sheet("<xsl:import href=\"https://example.test/missing.xsl\"/>"), Over(server))).Message,
                "could not be found");

            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Sheet("<xsl:import href=\"https://example.test/gone.xsl\"/>"), Over(server))).Message,
                "could not be found");

            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Sheet("<xsl:import href=\"https://example.test/broken.xsl\"/>"), Over(server))).Message,
                "500");
        }

        [TestMethod]
        public void TheResponseCharsetDecidesTheDecoding()
        {
            const string Module = "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:template match=\"/\"><o><xsl:value-of select=\"'café'\"/></o></xsl:template></xsl:stylesheet>";

            Server server = new Server()
                .Text("https://example.test/latin.xsl", Module, Encoding.Latin1)
                .Bytes("https://example.test/plain.xsl", Encoding.UTF8.GetBytes(Module), "application/xml")
                .Bytes("https://example.test/untyped.xsl", Encoding.UTF8.GetBytes(Module), null);

            foreach (string name in new[] { "latin", "plain", "untyped" })
            {
                Assert.AreEqual(
                    "<o>café</o>",
                    Run(Sheet($"<xsl:import href=\"https://example.test/{name}.xsl\"/>"), Over(server)),
                    name);
            }

            // An encoding the caller names wins over what the server says.
            Assert.AreEqual(
                "<o>café</o>",
                Run(
                    Sheet("<xsl:import href=\"https://example.test/plain.xsl\"/>"),
                    Over(new Server().Bytes("https://example.test/plain.xsl", Encoding.Latin1.GetBytes(Module), "application/xml"), encoding: Encoding.Latin1)));
        }

        [TestMethod]
        public void AHandlerThatAnswersOnlyAsynchronouslyIsWaitedOn()
        {
            Server server = new Server { Synchronous = false }
                .Text("https://example.test/main.xsl", Template("<async/>"));

            Assert.AreEqual(
                "<async/>",
                Run(Sheet("<xsl:import href=\"https://example.test/main.xsl\"/>"), Over(server)));
        }

        [TestMethod]
        public void DocumentIsLoadedOverHttp()
        {
            Server server = new Server()
                .Text("https://example.test/data/items.xml", "<items><item>one</item><item>two</item></items>");

            Assert.AreEqual(
                "<o>2</o>",
                Run(
                    Template("<o><xsl:value-of select=\"count(document('https://example.test/data/items.xml')/items/item)\"/></o>"),
                    Over(server)));

            // A relative reference in document() resolves against the stylesheet's base URI, wherever that is.
            Assert.AreEqual(
                "<o>one</o>",
                Run(
                    Template("<o><xsl:value-of select=\"document('items.xml')/items/item[1]\"/></o>"),
                    Over(server),
                    baseUri: "https://example.test/data/main.xsl"));
        }

        [TestMethod]
        public void AnUnsupportedSchemeIsRefused()
        {
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Sheet("<xsl:import href=\"ftp://example.test/main.xsl\"/>"), Over(new Server()))).Message,
                "ftp");
        }

        // ---- The file half -------------------------------------------------------------------------------

        [TestMethod]
        public void ARelativeReferenceResolvesAgainstTheRootAndThenAgainstTheModuleItIsWrittenIn()
        {
            using TempRoot root = new TempRoot();
            root.Write(Path.Combine("sub", "main.xsl"), Sheet("<xsl:include href=\"part.xsl\"/>"));
            root.Write(Path.Combine("sub", "part.xsl"), Template("<from-sub/>"));

            // With no base URI, the first reference is relative to the root; the one inside sub/main.xsl is
            // relative to sub/, which is where it was written.
            Assert.AreEqual(
                "<from-sub/>",
                Run(Sheet("<xsl:import href=\"sub/main.xsl\"/>"), new UriResolver(root.Path)));

            // A base URI given as a plain path is understood, and so is a file URI.
            Assert.AreEqual(
                "<from-sub/>",
                Run(Sheet("<xsl:import href=\"main.xsl\"/>"), new UriResolver(root.Path), Path.Combine(root.Path, "sub", "any.xsl")));

            Assert.AreEqual(
                "<from-sub/>",
                Run(Sheet("<xsl:import href=\"main.xsl\"/>"), new UriResolver(root.Path), new Uri(Path.Combine(root.Path, "sub", "any.xsl")).AbsoluteUri));

            // As is a bare file name for the base, which is relative to the root like everything else.
            Assert.AreEqual(
                "<from-sub/>",
                Run(Sheet("<xsl:import href=\"main.xsl\"/>"), new UriResolver(root.Path), "sub/any.xsl"));
        }

        [TestMethod]
        public void AFileOutsideTheRootIsRefusedAndNoFileIsReadWithoutOne()
        {
            using TempRoot root = new TempRoot();
            string outside = root.Write(Path.Combine("..", "outside-" + Path.GetFileName(root.Path) + ".xsl"), Template("<secret/>"));
            root.Write(Path.Combine("inside", "ok.xsl"), Template("<ok/>"));

            try
            {
                UriResolver resolver = new UriResolver(root.Path);

                StringAssert.Contains(
                    Assert.ThrowsExactly<XsltException>(
                        () => Run(Sheet($"<xsl:import href=\"../{Path.GetFileName(outside)}\"/>"), resolver)).Message,
                    "refused");

                StringAssert.Contains(
                    Assert.ThrowsExactly<XsltException>(
                        () => Run(Sheet($"<xsl:import href=\"{new Uri(outside).AbsoluteUri}\"/>"), resolver)).Message,
                    "refused");

                Assert.AreEqual("<ok/>", Run(Sheet("<xsl:import href=\"inside/ok.xsl\"/>"), resolver));

                // A file that is not there is not found, rather than refused.
                StringAssert.Contains(
                    Assert.ThrowsExactly<XsltException>(
                        () => Run(Sheet("<xsl:import href=\"inside/missing.xsl\"/>"), resolver)).Message,
                    "could not be found");

                // Without a root there is no file half at all: a relative reference has nothing to resolve
                // against, and an absolute one names a file this resolver was not given leave to read.
                UriResolver networkOnly = new UriResolver();

                StringAssert.Contains(
                    Assert.ThrowsExactly<XsltException>(
                        () => Run(Sheet("<xsl:import href=\"inside/ok.xsl\"/>"), networkOnly)).Message,
                    "no root directory");

                StringAssert.Contains(
                    Assert.ThrowsExactly<XsltException>(
                        () => Run(Sheet($"<xsl:import href=\"{new Uri(Path.Combine(root.Path, "inside", "ok.xsl")).AbsoluteUri}\"/>"), networkOnly)).Message,
                    "no directory");
            }
            finally
            {
                File.Delete(outside);
            }
        }

        [TestMethod]
        public void TheRootMustExist()
        {
            Assert.ThrowsExactly<DirectoryNotFoundException>(
                () => new UriResolver(Path.Combine(Path.GetTempPath(), "xslt-uri-" + Guid.NewGuid().ToString("N"))));
        }

        // ---- Across the two halves -----------------------------------------------------------------------

        [TestMethod]
        public void EachHalfMayReferToTheOtherAndEachIsGatedByItsOwnRule()
        {
            using TempRoot root = new TempRoot();
            root.Write("main.xsl", Sheet("<xsl:import href=\"https://example.test/lib/lib.xsl\"/>"));
            root.Write("local.xsl", Template("<local/>"));
            string localUri = new Uri(Path.Combine(root.Path, "local.xsl")).AbsoluteUri;

            // A local module imports one from the web, which imports one from the root by file URI.
            Server server = new Server()
                .Text("https://example.test/lib/lib.xsl", Sheet($"<xsl:import href=\"{localUri}\"/>"));

            Assert.AreEqual("<local/>", Run(Sheet("<xsl:import href=\"main.xsl\"/>"), Over(server, root.Path)));

            // A relative reference in a web module stays on the web: it does not fall back to the root.
            Server relative = new Server()
                .Text("https://example.test/lib/lib.xsl", Sheet("<xsl:import href=\"local.xsl\"/>"));

            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Sheet("<xsl:import href=\"main.xsl\"/>"), Over(relative, root.Path))).Message,
                "could not be found");
            CollectionAssert.Contains(relative.Requested, "https://example.test/lib/local.xsl");

            // And a web module naming a file outside the root is refused like any other reference would be.
            Server escaping = new Server()
                .Text("https://example.test/lib/lib.xsl", Sheet($"<xsl:import href=\"{new Uri(Path.Combine(root.Path, "..", "elsewhere.xsl")).AbsoluteUri}\"/>"));

            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(
                    () => Run(Sheet("<xsl:import href=\"main.xsl\"/>"), Over(escaping, root.Path))).Message,
                "refused");
        }
    }
}
