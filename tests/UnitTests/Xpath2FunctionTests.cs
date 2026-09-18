namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the XPath 2.0 functions that reach outside the expression: documents, text, URIs, and the
    /// ones that report on the context rather than compute from it.
    /// </summary>
    /// <remarks>
    /// Run through a stylesheet rather than through a bare expression, because most of them need a
    /// transformation around them — a resolver to reach anything, a base URI to resolve against, a message
    /// sink to trace to.
    /// </remarks>
    [TestClass]
    public sealed class Xpath2FunctionTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        /// <summary>Serves documents and text from a dictionary.</summary>
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_resources = new(StringComparer.Ordinal);

            public MapResolver Add(string name, string content)
            {
                m_resources[name] = content;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_resources.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static string Writes(
            string expression,
            string input = "<r/>",
            IXsltResolver? documents = null,
            string? baseUri = null,
            TextWriter? messages = null,
            string stylesheetNamespaces = "")
        {
            // Any extra namespace goes on the xsl:value-of rather than on the stylesheet, so that the literal
            // out element does not inherit it and carry it into the result.
            string stylesheet = $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\"{stylesheetNamespaces}/>"
                + "</out></xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                DocumentResolver = documents,
                BaseUri = baseUri,
                MessageWriter = messages,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);

            if (messages is null)
            {
                string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);
                Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            }

            return interpreted;
        }

        private static string Value(string result)
        {
            // An empty result serializes as a self-closing tag, which is the same empty string said shorter.
            return result == "<out/>" ? string.Empty : result["<out>".Length..^"</out>".Length];
        }

        // ---- Documents and text ---------------------------------------------------------------------------

        [TestMethod]
        public void DocReadsADocument()
        {
            MapResolver documents = new MapResolver().Add("side.xml", "<t><i>found</i></t>");

            Assert.AreEqual("found", Value(Writes("doc('side.xml')/t/i", documents: documents)));
        }

        [TestMethod]
        public void DocAvailableAnswersForWhatIsThere()
        {
            MapResolver documents = new MapResolver().Add("side.xml", "<t/>");

            Assert.AreEqual("true", Value(Writes("doc-available('side.xml')", documents: documents)));
            Assert.AreEqual("false", Value(Writes("doc-available('gone.xml')", documents: documents)));
        }

        [TestMethod]
        public void DocAvailableIsFalseRatherThanAnErrorWithoutAResolver()
        {
            // Availability is the whole question it asks, and with no resolver nothing is available.
            Assert.AreEqual("false", Value(Writes("doc-available('side.xml')")));
        }

        [TestMethod]
        public void DocAvailableIsFalseForSomethingThatIsNotXml()
        {
            MapResolver documents = new MapResolver().Add("notes.txt", "just text, no markup <");

            Assert.AreEqual("false", Value(Writes("doc-available('notes.txt')", documents: documents)));
        }

        [TestMethod]
        public void UnparsedTextReadsWhatIsNotXml()
        {
            MapResolver documents = new MapResolver().Add("notes.txt", "a < b & c");

            Assert.AreEqual(
                "<out>a &lt; b &amp; c</out>",
                Writes("unparsed-text('notes.txt')", documents: documents));
        }

        [TestMethod]
        public void UnparsedTextAvailableAnswersForWhatIsThere()
        {
            MapResolver documents = new MapResolver().Add("notes.txt", "text");

            Assert.AreEqual("true", Value(Writes("unparsed-text-available('notes.txt')", documents: documents)));
            Assert.AreEqual("false", Value(Writes("unparsed-text-available('gone.txt')", documents: documents)));
        }

        [TestMethod]
        public void UnparsedTextWithoutAResolverIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("unparsed-text('notes.txt')"));

            StringAssert.Contains(error.Message, "no document resolver was configured");
        }

        [TestMethod]
        public void UnparsedTextRefusesAFragmentIdentifier()
        {
            MapResolver documents = new MapResolver().Add("notes.txt", "text");

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("unparsed-text('notes.txt#part')", documents: documents));

            Assert.AreEqual("FOUT1170", error.Code);
        }

        [TestMethod]
        public void UnparsedTextRefusesACharacterXmlDoesNotPermit()
        {
            // A string in this data model holds XML characters and nothing else, so a file with a NUL in it
            // cannot be read into one. Answering with it anyway produces a result that will not serialize,
            // which is a failure at the far end of a transformation about something that was wrong at the
            // near end of it.
            MapResolver documents = new MapResolver()
                .Add("nul.txt", "before\u0000after")
                .Add("bell.txt", "\u0007")
                .Add("half.txt", "\ud800")
                .Add("fine.txt", "tab\there\r\nand \ud83d\ude00 and \ufffd");

            foreach (string name in new[] { "nul.txt", "bell.txt", "half.txt" })
            {
                Assert.AreEqual(
                    "FOUT1190",
                    Assert.ThrowsExactly<XsltException>(
                        () => Writes($"unparsed-text('{name}')", documents: documents)).Code,
                    name);

                // The -available form answers rather than raising, as it does for anything unreadable.
                Assert.AreEqual(
                    "false",
                    Value(Writes($"unparsed-text-available('{name}')", documents: documents)),
                    name);
            }

            // A tab, the two line endings, a character above the basic plane written as a surrogate pair,
            // and the replacement character are all of them permitted.
            Assert.AreEqual(
                "true", Value(Writes("unparsed-text-available('fine.txt')", documents: documents)));
        }

        // ---- URIs -----------------------------------------------------------------------------------------

        [TestMethod]
        public void StaticBaseUriReportsWhatTheCallerSet()
        {
            Assert.AreEqual(
                "http://example.org/here/",
                Value(Writes("static-base-uri()", baseUri: "http://example.org/here/")));
        }

        [TestMethod]
        public void DocumentUriReportsWhereADocumentCameFrom()
        {
            MapResolver documents = new MapResolver().Add("side.xml", "<t/>");

            Assert.AreEqual(
                "side.xml",
                Value(Writes("document-uri(doc('side.xml'))", documents: documents)));
        }

        [TestMethod]
        public void DocumentUriIsEmptyForATreeThatCameFromNowhere()
        {
            // The input was handed over as text and was never anywhere, so there is nothing to report — and
            // nothing invented in its place.
            Assert.AreEqual(string.Empty, Value(Writes("document-uri(/)")));
        }

        [TestMethod]
        public void ResolveUriMakesARelativeReferenceAbsolute()
        {
            Assert.AreEqual(
                "http://example.org/here/there.xml",
                Value(Writes("resolve-uri('there.xml')", baseUri: "http://example.org/here/")));
        }

        [TestMethod]
        public void ResolveUriLeavesAnAbsoluteReferenceAlone()
        {
            Assert.AreEqual(
                "http://elsewhere.example/x",
                Value(Writes("resolve-uri('http://elsewhere.example/x', 'http://example.org/')")));
        }

        [TestMethod]
        public void ResolveUriWithNoBaseIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes("resolve-uri('there.xml')"));

            Assert.AreEqual("FONS0005", error.Code);
        }

        [TestMethod]
        [DataRow("resolve-uri('http://www.example.com', '')", "http://www.example.com")]
        [DataRow("resolve-uri('HTTP://WWW.EXAMPLE.COM', '')", "HTTP://WWW.EXAMPLE.COM")]
        [DataRow("resolve-uri('%C3%A0.html', 'http://x/%C3%A7.html')", "http://x/%C3%A0.html")]
        [DataRow("resolve-uri('b.html', 'http://x/a.html?q=1')", "http://x/b.html")]
        [DataRow("resolve-uri('', 'http://x/a.html?q=1')", "http://x/a.html?q=1")]
        [DataRow("resolve-uri('../c/d', 'http://x/a/b/e')", "http://x/a/c/d")]
        [DataRow("resolve-uri('../../../g', 'http://x/a/b')", "http://x/g")]
        [DataRow("resolve-uri('//other/p', 'http://x/a')", "http://other/p")]
        [DataRow("resolve-uri('#frag', 'http://x/a')", "http://x/a#frag")]
        public void ResolveUriIsRfc3986AndNothingElse(string expression, string expected)
        {
            // What each of these really tests is that the text was not put through Uri: that class gives an
            // authority the empty path, lower-cases the host and decodes escapes, and a reference needing no
            // resolution has to come back as it was written.
            Assert.AreEqual(expected, Value(Writes(expression)));
        }

        [TestMethod]
        public void ResolveUriRefusesWhatIsNotAUriReference()
        {
            // A relative path is written so it cannot be read as a scheme, so a colon in its first segment
            // makes it no reference at all.
            Assert.AreEqual(
                "FORG0002",
                Assert.ThrowsExactly<XsltException>(() => Writes("resolve-uri(':', 'http://x/')")).Code);

            // A percent sign begins an escape and wants two hexadecimal digits after it.
            Assert.AreEqual(
                "FORG0002",
                Assert.ThrowsExactly<XsltException>(() => Writes("resolve-uri('a', 'http:%%')")).Code);

            // A fragment is what follows the first '#' and may hold no '#' of its own, so a reference
            // carrying two of them is not a reference at all.
            Assert.AreEqual(
                "FORG0002",
                Assert.ThrowsExactly<XsltException>(
                    () => Writes("resolve-uri('##some.uri', 'http://x/')")).Code);

            // A base names a scheme...
            Assert.AreEqual(
                "FORG0002",
                Assert.ThrowsExactly<XsltException>(() => Writes("resolve-uri('a.html', 'b.html')")).Code);

            // ...and carries no fragment, a fragment naming a place inside a document rather than one to be
            // relative to.
            Assert.AreEqual(
                "FORG0002",
                Assert.ThrowsExactly<XsltException>(() => Writes("resolve-uri('b', 'http://x/a#f')")).Code);
        }

        [TestMethod]
        public void ResolveUriAsksNothingOfABaseItDoesNotNeed()
        {
            // Nothing to resolve resolves to nothing, whatever the base says.
            Assert.AreEqual(string.Empty, Value(Writes("resolve-uri((), 'not a uri at all')")));

            // And a reference naming a scheme is already the answer, so an unusable base goes unread.
            Assert.AreEqual(
                "http://example.com/a", Value(Writes("resolve-uri('http://example.com/a', 'b.html')")));
        }

        [TestMethod]
        [DataRow("encode-for-uri('a b/c')", "a%20b%2Fc")]
        [DataRow("iri-to-uri('a b/c')", "a%20b/c")]
        [DataRow("encode-for-uri('~x')", "~x")]

        // escape-html-uri keeps every printable US-ASCII octet, 32 to 126 inclusive — the space among them.
        // What it escapes for is a browser's address bar rather than a URI parser, so it leaves alone what a
        // browser can already show and only what is outside ASCII has to be encoded.
        [DataRow("escape-html-uri('a b')", "a b")]
        [DataRow("escape-html-uri('http://x/a b?q=1&amp;r=(2)')", "http://x/a b?q=1&amp;r=(2)")]
        [DataRow("escape-html-uri('caf&#233;')", "caf%C3%A9")]
        public void TheUriEscapersDifferOverWhatTheyLeaveAlone(string expression, string expected)
        {
            // encode-for-uri takes one component, so a slash in it is data and is escaped; iri-to-uri takes a
            // whole URI, where a slash is structure.
            Assert.AreEqual(expected, Value(Writes(expression)));
        }

        // ---- Reading the context --------------------------------------------------------------------------

        [TestMethod]
        public void DataAtomizesNodes()
        {
            Assert.AreEqual("a b", Value(Writes("data(/r/i)", "<r><i>a</i><i>b</i></r>")));
        }

        [TestMethod]
        public void NilledIsFalseForAnElementAndEmptyForAnythingElse()
        {
            Assert.AreEqual("false", Value(Writes("nilled(/r)", "<r/>")));
            Assert.AreEqual(string.Empty, Value(Writes("nilled(/r/@a)", "<r a='1'/>")));
        }

        [TestMethod]
        public void InScopePrefixesReportsWhatIsDeclared()
        {
            Assert.AreEqual(
                "p xml",
                Value(Writes("string-join(in-scope-prefixes(/r), ' ')", "<r xmlns:p='urn:x'/>")));
        }

        [TestMethod]
        public void NamespaceUriForPrefixResolvesAgainstAnElement()
        {
            Assert.AreEqual(
                "urn:x",
                Value(Writes("namespace-uri-for-prefix('p', /r)", "<r xmlns:p='urn:x'/>")));
        }

        [TestMethod]
        public void TheXmlPrefixIsAlwaysInScope()
        {
            Assert.AreEqual(
                "http://www.w3.org/XML/1998/namespace",
                Value(Writes("namespace-uri-for-prefix('xml', /r)", "<r/>")));
        }

        [TestMethod]
        public void ErrorStopsTheTransformation()
        {
            // The code is an xs:QName rather than a string, and has to be written as one: a string is not
            // silently read as a name, here or anywhere else.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("error(xs:QName('FOO0001'), 'that will not do')"));

            StringAssert.Contains(error.Message, "FOO0001");
            StringAssert.Contains(error.Message, "that will not do");
        }

        [TestMethod]
        public void ErrorNeedsAName()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("error('FOO0001', 'that will not do')"));

            Assert.AreEqual("XPTY0004", error.Code);
        }

        [TestMethod]
        public void ErrorCarriesTheCodeTheStylesheetNamed()
        {
            // The local part of the name reaches XsltException.Code, so a caller catching by code cannot tell
            // a stylesheet's error from one the engine raised — which is what makes the function usable.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("error(xs:QName('FOO0001'), 'that will not do')"));

            Assert.AreEqual("FOO0001", error.Code);
        }

        [TestMethod]
        public void ErrorCanRaiseOneOfTheSpecificationsOwn()
        {
            // Indistinguishable from the engine raising it, deliberately: a stylesheet that detects a bad
            // cast itself should be able to report it the same way.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes(
                    "error(QName('http://www.w3.org/2005/xqt-errors', 'err:FORG0001'), 'not a number')"));

            Assert.AreEqual("FORG0001", error.Code);

            // And the namespace is left out of the message, where it would be noise on every one of these.
            StringAssert.StartsWith(error.Message, "FORG0001: ");
        }

        [TestMethod]
        public void ErrorWithNoNameRaisesTheDefaultOne()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes("error()"));
            Assert.AreEqual("FOER0000", error.Code);

            // The two-argument form takes an empty name, which means the same default.
            XsltException described = Assert.ThrowsExactly<XsltException>(
                () => Writes("error((), 'a description')"));

            Assert.AreEqual("FOER0000", described.Code);
            StringAssert.Contains(described.Message, "a description");
        }

        [TestMethod]
        public void AnErrorInItsOwnNamespaceKeepsItInTheMessage()
        {
            // Two stylesheets may both raise 'invalid'; the code alone cannot separate them, so the namespace
            // stays where a reader will see it.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("error(QName('urn:mine', 'my:invalid'), 'no good')"));

            Assert.AreEqual("invalid", error.Code);
            StringAssert.Contains(error.Message, "{urn:mine}invalid");
        }

        [TestMethod]
        public void TraceWritesToTheMessageSinkAndReturnsItsValue()
        {
            StringWriter messages = new StringWriter();

            Assert.AreEqual("7", Value(Writes("trace(7, 'seven')", messages: messages)));
            StringAssert.Contains(messages.ToString(), "seven: 7");
        }

        // ---- Strings ---------------------------------------------------------------------------------------

        [TestMethod]
        public void CodepointEqualComparesExactly()
        {
            Assert.AreEqual("true", Value(Writes("codepoint-equal('a', 'a')")));
            Assert.AreEqual("false", Value(Writes("codepoint-equal('a', 'A')")));
        }

        [TestMethod]
        public void CodepointEqualIsEmptyWhenEitherSideIs()
        {
            Assert.AreEqual(string.Empty, Value(Writes("codepoint-equal('a', ())")));
        }

        [TestMethod]
        public void NormalizeUnicodeComposesByDefault()
        {
            // e followed by a combining acute becomes the single composed character.
            Assert.AreEqual(
                "1",
                Value(Writes("string-length(normalize-unicode('e&#x301;'))")));
        }

        [TestMethod]
        public void NormalizeUnicodeCanDecompose()
        {
            Assert.AreEqual("2", Value(Writes("string-length(normalize-unicode('&#xE9;', 'NFD'))")));
        }

        [TestMethod]
        public void AnEmptyNormalizationFormLeavesTheStringAlone()
        {
            Assert.AreEqual("2", Value(Writes("string-length(normalize-unicode('e&#x301;', ''))")));
        }

        [TestMethod]
        public void AnUnknownNormalizationFormIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("normalize-unicode('x', 'NFQ')"));

            Assert.AreEqual("FOCH0003", error.Code);
        }

        // ---- Names, bytes and partial dates -----------------------------------------------------------------

        [TestMethod]
        public void ALiteralQNameIsResolvedWhereItIsWritten()
        {
            // The prefix means what it meant in the stylesheet, and nothing at run time would know that.
            Assert.AreEqual(
                "urn:x|p|thing",
                Value(Writes(
                    "concat(namespace-uri-from-QName(xs:QName('p:thing')), '|', "
                    + "prefix-from-QName(xs:QName('p:thing')), '|', "
                    + "local-name-from-QName(xs:QName('p:thing')))",
                    stylesheetNamespaces: " xmlns:p=\"urn:x\"")));
        }

        [TestMethod]
        public void AQNameWithNoPrefixHasNoNamespace()
        {
            Assert.AreEqual("|thing", Value(Writes(
                "concat(namespace-uri-from-QName(xs:QName('thing')), '|', "
                + "local-name-from-QName(xs:QName('thing')))")));
        }

        [TestMethod]
        public void ThePartsOfAQNameThatAreNamesAreTypedAsNames()
        {
            // F&O declares fn:local-name-from-QName and fn:prefix-from-QName "as xs:NCName?", not as a
            // string that happens to be one, and a stylesheet can see the difference. The suite's
            // namespace-2619 asks in as many words. fn:namespace-uri-from-QName is "as xs:anyURI".
            Assert.AreEqual(
                "true true true",
                Value(Writes(
                    "string-join((local-name-from-QName(xs:QName('p:thing')) instance of xs:NCName, "
                    + "prefix-from-QName(xs:QName('p:thing')) instance of xs:NCName, "
                    + "namespace-uri-from-QName(xs:QName('p:thing')) instance of xs:anyURI), ' ')",
                    stylesheetNamespaces: " xmlns:p=\"urn:x\"")));

            // An NCName is a string as well, being derived from one, and everything a string does it does.
            Assert.AreEqual(
                "true thing",
                Value(Writes(
                    "concat(local-name-from-QName(xs:QName('p:thing')) instance of xs:string, ' ', "
                    + "local-name-from-QName(xs:QName('p:thing')))",
                    stylesheetNamespaces: " xmlns:p=\"urn:x\"")));
        }

        [TestMethod]
        public void AnUnboundPrefixInALiteralQNameIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("string(xs:QName('nope:thing'))"));

            Assert.AreEqual("FONS0004", error.Code);
        }

        [TestMethod]
        public void TwoQNamesAreEqualWhateverPrefixTheyWereWrittenWith()
        {
            Assert.AreEqual(
                "true",
                Value(Writes(
                    "xs:QName('p:thing') eq resolve-QName('q:thing', /r)",
                    "<r xmlns:q='urn:x'/>",
                    stylesheetNamespaces: " xmlns:p=\"urn:x\"")));
        }

        [TestMethod]
        public void QNamesCannotBeOrdered()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("xs:QName('a') lt xs:QName('b')"));

            Assert.AreEqual("XPTY0004", error.Code);
        }

        [TestMethod]
        public void NodeNameReportsTheNameOfANode()
        {
            Assert.AreEqual("p:i", Value(Writes("node-name(/r/*[1])", "<r xmlns:p='urn:x'><p:i/></r>")));
        }

        [TestMethod]
        public void NodeNameIsEmptyForANodeThatHasNoName()
        {
            Assert.AreEqual(string.Empty, Value(Writes("node-name(/r/text())", "<r>text</r>")));
        }

        [TestMethod]
        public void QNameBuildsANameFromItsParts()
        {
            Assert.AreEqual(
                "urn:x|p:thing",
                Value(Writes(
                    "concat(namespace-uri-from-QName(QName('urn:x', 'p:thing')), '|', "
                    + "string(QName('urn:x', 'p:thing')))")));
        }

        [TestMethod]
        [DataRow("xs:hexBinary('48656C6C6F')", "48656C6C6F")]
        [DataRow("xs:base64Binary('SGVsbG8=')", "SGVsbG8=")]
        [DataRow("xs:base64Binary(xs:hexBinary('48656C6C6F'))", "SGVsbG8=")]
        [DataRow("xs:hexBinary(xs:base64Binary('SGVsbG8='))", "48656C6C6F")]
        public void BinaryValuesAreBytesWrittenTwoWays(string expression, string expected)
        {
            Assert.AreEqual(expected, Value(Writes(expression)));
        }

        [TestMethod]
        public void TwoSpellingsOfTheSameBytesAreEqual()
        {
            // Held as bytes rather than as text, so case does not enter into it.
            Assert.AreEqual(
                "true",
                Value(Writes("xs:hexBinary('48656c6c6f') eq xs:hexBinary('48656C6C6F')")));
        }

        [TestMethod]
        public void TheTwoBinaryTypesAreNotComparedToEachOther()
        {
            // The same bytes, but xs:hexBinary and xs:base64Binary are two types rather than two spellings of
            // one, and XPath 2.0 defines no comparison between them. Cast one side and the question becomes
            // answerable, which is what the test below does.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("xs:hexBinary('48656c6c6f') eq xs:base64Binary('SGVsbG8=')"));

            Assert.AreEqual("XPTY0004", error.Code);

            Assert.AreEqual(
                "true",
                Value(Writes("xs:hexBinary(xs:base64Binary('SGVsbG8=')) eq xs:hexBinary('48656c6c6f')")));
        }

        [TestMethod]
        public void BinaryTextThatIsNotValidIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes("xs:hexBinary('zz')"));

            Assert.AreEqual("FORG0001", error.Code);
        }

        [TestMethod]
        [DataRow("xs:gYear('2026')", "2026")]
        [DataRow("xs:gYearMonth('2026-08')", "2026-08")]
        [DataRow("xs:gMonth('--08')", "--08")]
        [DataRow("xs:gMonthDay('--08-23')", "--08-23")]
        [DataRow("xs:gDay('---23')", "---23")]
        [DataRow("xs:gYear('2026Z')", "2026Z")]
        [DataRow("xs:gYear('-0044')", "-0044")]
        public void TheGregorianTypesKeepTheirLexicalForm(string expression, string expected)
        {
            Assert.AreEqual(expected, Value(Writes(expression)));
        }

        [TestMethod]
        [DataRow("xs:gYear('26')")]
        [DataRow("xs:gMonth('08')")]
        [DataRow("xs:gMonthDay('--13-01')")]
        public void AGregorianValueOutsideItsLexicalSpaceIsRefused(string expression)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Writes(expression));

            Assert.AreEqual("FORG0001", error.Code);
        }

        // ---- Formatting dates ------------------------------------------------------------------------------

        [TestMethod]
        [DataRow("[Y0001]-[M01]-[D01]", "2026-08-23")]
        [DataRow("[D1] [MNn] [Y]", "23 August 2026")]
        [DataRow("[MNn,3-3] [D1], [Y]", "Aug 23, 2026")]
        [DataRow("[FNn]", "Sunday")]
        [DataRow("[F1]", "7")]
        [DataRow("[MN]", "AUGUST")]
        [DataRow("[Mn]", "august")]
        [DataRow("[M01]/[D01]/[Y,2-2]", "08/23/26")]
        [DataRow("[MI]", "VIII")]
        [DataRow("[D1] of [MNn]", "23 of August")]
        [DataRow("[D1o]", "23rd")]
        [DataRow("[D1o] [MNn] [Y1o]", "23rd August 2026th")]
        [DataRow("[Dwo]", "twenty-third")]
        [DataRow("[DWwo]", "Twenty-Third")]
        [DataRow("[DW]", "TWENTY-THREE")]
        [DataRow("[D1c]", "23")]
        [DataRow("[FNn,3-3]", "Sun")]
        [DataRow("[FNn,*-3]", "Sun")]
        [DataRow("[Y01]", "26")]
        [DataRow("[Y0001]", "2026")]
        [DataRow("[Y#0]", "26")]
        [DataRow("[Yi]", "mmxxvi")]
        [DataRow("[Yi,4-4]", "mmxxvi")]
        [DataRow("[YI,8-8]", "MMXXVI  ")]
        [DataRow("[D1,3-3]", "023")]
        public void FormatDateWritesThePictureItIsGiven(string picture, string expected)
        {
            Assert.AreEqual(
                expected,
                Value(Writes($"format-date(xs:date('2026-08-23'), '{picture}')")));
        }

        [TestMethod]
        [DataRow("2005-12-04", "1")]
        [DataRow("2005-12-07", "2")]
        [DataRow("2005-12-10", "2")]
        [DataRow("2005-12-13", "3")]
        [DataRow("2005-12-31", "5")]
        [DataRow("2006-01-03", "1")]
        [DataRow("2006-01-09", "2")]
        [DataRow("2006-01-30", "5")]
        [DataRow("2006-02-02", "1")]
        [DataRow("2006-02-26", "4")]
        [DataRow("2006-03-19", "3")]
        [DataRow("2006-04-09", "1")]
        [DataRow("2006-04-12", "2")]
        public void TheWeekInAMonthIsCountedTheWayTheWeekInAYearIs(string date, string expected)
        {
            // ISO 8601's rule for a year applied to a month: weeks run Monday to Sunday and week 1 is the
            // one holding the first Thursday. Counting the day of the month in sevens is the obvious
            // reading and is not this one — 7 December 2005 is in the second week and not the first,
            // December having begun on a Thursday, so its first week runs from 28 November. 9 April 2006
            // is a Sunday in the first week of April for the same reason, April having begun on a
            // Saturday: the week of the first Thursday starts on the 3rd.
            Assert.AreEqual(expected, Value(Writes($"format-date(xs:date('{date}'), '[w]')")));
        }

        [TestMethod]
        public void ADayBeforeTheFirstWeekOfItsMonthBelongsToTheMonthBefore()
        {
            // 1 January 2006 was a Sunday, and the week that ends on it holds no Thursday of January, so
            // January's first week begins on the 2nd. The 1st is in the week before that, which is
            // December's fifth — there being no week zero to put it in.
            Assert.AreEqual("5", Value(Writes("format-date(xs:date('2006-01-01'), '[w]')")));
        }

        [TestMethod]
        [DataRow("2026-08-20", "[FNn,3-4]", "Thur")]
        [DataRow("2026-08-20", "[FNn,3-5]", "Thurs")]
        [DataRow("2026-08-20", "[FNn,2-2]", "Th")]
        [DataRow("2026-08-18", "[FNn,3-4]", "Tues")]
        [DataRow("2026-08-19", "[FN,3-4]", "WEDS")]
        [DataRow("2026-08-21", "[FNn,3-4]", "Fri")]
        [DataRow("2026-09-03", "[MNn,3-4]", "Sept")]
        [DataRow("2026-09-03", "[MNn,3-3]", "Sep")]
        [DataRow("2026-06-03", "[MNn,3-3]", "Jun")]
        [DataRow("2026-06-03", "[MNn,3-4]", "June")]
        [DataRow("2026-05-03", "[MNn,6-6]", "May   ")]
        [DataRow("2026-05-03", "[D01,4-4]", "0003")]
        public void ANameIsAbbreviatedConventionallyBeforeItIsCut(string date, string picture, string expected)
        {
            // §9.8.4.2: a name longer than the width is abbreviated, by the conventional short form where
            // one fits and by cutting where none does; one shorter than the width is padded with spaces on
            // the right, where a number is padded with zeros on the left.
            Assert.AreEqual(
                expected,
                Value(Writes($"format-date(xs:date('{date}'), '{picture}')")));
        }

        [TestMethod]
        [DataRow("'hu', ()", "[Language: en]August")]
        [DataRow("'en-GB', ()", "August")]
        [DataRow("'EN', ()", "August")]
        [DataRow("(), ()", "August")]
        [DataRow("'en', 'CB'", "[Calendar: AD]August")]
        [DataRow("'en', 'AD'", "August")]
        [DataRow("'hu', 'CB'", "[Language: en][Calendar: AD]August")]
        public void AnotherLanguageOrCalendarIsAnsweredInEnglishAndSaysSo(string arguments, string expected)
        {
            // §9.8.4.8: a fallback names what it fell back to, in front of the answer.
            Assert.AreEqual(
                expected,
                Value(Writes($"format-date(xs:date('2026-08-23'), '[MNn]', {arguments}, ())")));
        }

        [TestMethod]
        [DataRow("[bla]", "'b' names no component, so this is not a picture at all")]
        [DataRow("[yY]", "and neither does a lower-case y")]
        [DataRow("[Y999#]", "an optional digit may not follow one that has to appear")]
        [DataRow("[Y##9#]", "and again, past a run of them")]
        [DataRow("[Y#.0,4-3]", "a component is no narrower than it is wide")]
        [DataRow("[Y1,0-3]", "a component is at least one character wide")]
        [DataRow("[Y1,*-0]", "including where the minimum is left open")]
        public void APictureComponentOutsideTheGrammarIsRefused(string picture, string why)
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes($"format-date(xs:date('2026-08-23'), '{picture}')"), why);

            // FOFD1340 says the picture is malformed, which is a different complaint from FOFD1350: that one
            // is a component this language has and this value has not, such as [Y] in format-time.
            Assert.AreEqual("FOFD1340", error.Code, why);
        }

        [TestMethod]
        public void ATwoPointZeroProcessorRoundsTheFractionWhereThreeCutsIt()
        {
            // XSLT 2.0 rounded the fraction to the digits the picture has room for; F&O 3.1 cuts it. Which
            // rule applies follows the processor, not the stylesheet: a 2.0 stylesheet run by a 3.0
            // processor is cut too, which is what the suite's format-date-003a asks for.
            static string Under(XsltVersion version, string picture)
            {
                string stylesheet = "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
                    + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"><xsl:template match=\"/\"><out>"
                    + "<xsl:value-of select=\"format-time(xs:time('09:15:06.456'), '" + picture + "')\"/>"
                    + "</out></xsl:template></xsl:stylesheet>";

                return new Xslt(stylesheet, new XsltOptions { OmitXmlDeclaration = true, Version = version })
                    .TransformXml("<r/>");
            }

            Assert.AreEqual("<out>9:15:06.5</out>", Under(XsltVersion.V20, "[H]:[m]:[s].[f,1-1]"));
            Assert.AreEqual("<out>9:15:06.4</out>", Under(XsltVersion.V30, "[H]:[m]:[s].[f,1-1]"));
            Assert.AreEqual("<out>9:15:06.46</out>", Under(XsltVersion.V20, "[H]:[m]:[s].[f01]"));
            Assert.AreEqual("<out>9:15:06.45</out>", Under(XsltVersion.V30, "[H]:[m]:[s].[f01]"));
        }

        [TestMethod]
        [DataRow("[f]", "12:01:01.123", "123")]
        [DataRow("[f1]", "12:01:01.123", "123")]
        [DataRow("[f99]", "12:01:01.123", "12")]
        [DataRow("[f001]", "12:01:01.123", "123")]
        [DataRow("[f99#]", "12:01:01.123", "123")]
        [DataRow("[f99#]", "12:01:01.12", "12")]
        [DataRow("[f777]", "12:01:01.12", "120")]
        [DataRow("[f99]", "12:01:01.999", "99")]
        [DataRow("[f,6-*]", "12:01:01.135", "135000")]
        [DataRow("[f,*-2]", "12:01:01.133", "13")]
        [DataRow("[f,2-4]", "12:01:01", "00")]
        [DataRow("[f111,2-2]", "12:01:01.123", "123")]
        [DataRow("[f1###,3-3]", "12:01:01.1", "100")]
        // The picture is written inside an XPath string literal, so each apostrophe in it is doubled.
        [DataRow("[f0''0''0]", "12:01:01.135", "1'3'5")]
        public void TheFractionalSecondsRunTheOtherWayFromEveryOtherComponent(
            string picture, string time, string expected)
        {
            // Their digits are significant from the left, so they pad and truncate on the right, and their
            // picture bounds them where every other component's is a minimum. A width written as well widens
            // rather than narrows, which is why '[f111,2-2]' keeps three digits.
            Assert.AreEqual(
                expected,
                Value(Writes($"format-time(xs:time('{time}'), '{picture}')")));
        }

        [TestMethod]
        public void TheMeridiemTakesTheCaseItsModifierAsksFor()
        {
            Assert.AreEqual(
                "a.m./A.M./A.M.",
                Value(Writes("format-time(xs:time('09:15:06'), '[Pn]/[PNn]/[PN]')")));
        }

        [TestMethod]
        [DataRow("[PN]", "A.M.")]
        [DataRow("[PN,4-8]", "A.M.")]
        [DataRow("[PN,3-3]", "AM")]
        [DataRow("[PNn,2-2]", "Am")]
        [DataRow("[Pn,2-2]", "am")]
        [DataRow("[PNn,1-1]", "A")]
        public void TheMeridiemIsSpelledToTheWidthAsked(string picture, string expected)
        {
            // The marker has no one spelling, so a width chooses between the spellings rather than cutting
            // one of them: at two characters it is AM, where cutting A.M. would have left 'A.'. Nor is the
            // minimum padded afterwards, which would put a trailing space on every '[PNn,3-3]'.
            Assert.AreEqual(expected, Value(Writes($"format-time(xs:time('09:15:06'), '{picture}')")));
        }

        [TestMethod]
        public void AComponentTheValueHasNotIsADifferentComplaint()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("format-time(xs:time('14:05:09'), '[Y]')"));

            Assert.AreEqual("FOFD1350", error.Code);
        }

        [TestMethod]
        public void ANumericComponentIsWrittenThroughAFormatIntegerPicture()
        {
            // Which is what the specification says its presentation modifier is, so the digit family and the
            // grouping come with it. The comma that introduces a width is the last one that has a width
            // behind it, because a comma also groups.
            Assert.AreEqual(
                "๒๐๒๖",
                Value(Writes("format-date(xs:date('2026-08-23'), '[Y๐๐๐๑]')")));

            Assert.AreEqual(
                "2;026",
                Value(Writes("format-date(xs:date('2026-08-23'), '[Y9;999]')")));

            Assert.AreEqual(
                "2,026",
                Value(Writes("format-date(xs:date('2026-08-23'), '[Y9,999,*]')")));
        }

        [TestMethod]
        [DataRow("[H01]:[m01]:[s01]", "14:05:09")]
        [DataRow("[h]:[m01] [PN]", "2:05 P.M.")]
        [DataRow("[h1]:[m01][Pn]", "2:05p.m.")]
        public void FormatTimeWritesThePictureItIsGiven(string picture, string expected)
        {
            Assert.AreEqual(
                expected,
                Value(Writes($"format-time(xs:time('14:05:09'), '{picture}')")));
        }

        [TestMethod]
        public void FormatDateTimeCanWriteBothHalves()
        {
            Assert.AreEqual(
                "2026-08-23 at 14:05",
                Value(Writes(
                    "format-dateTime(xs:dateTime('2026-08-23T14:05:09'), "
                    + "'[Y0001]-[M01]-[D01] at [H01]:[m01]')")));
        }

        [TestMethod]
        [DataRow("2026-08-23T14:05:09+02:00", "[Z]", "+02:00")]
        [DataRow("2026-08-23T14:05:09+02:00", "[z]", "GMT+02:00")]
        [DataRow("2026-08-23T14:05:09-05:30", "[Z]", "-05:30")]
        [DataRow("2026-08-23T14:05:09Z", "[Z]", "+00:00")]
        [DataRow("2026-08-23T14:05:09-05:00", "[Z0]", "-5")]
        [DataRow("2026-08-23T14:05:09+05:30", "[Z0]", "+5:30")]
        [DataRow("2026-08-23T14:05:09Z", "[Z0]", "+0")]
        [DataRow("2026-08-23T14:05:09-05:00", "[Z0:00]", "-5:00")]
        [DataRow("2026-08-23T14:05:09-05:00", "[Z00:00]", "-05:00")]
        [DataRow("2026-08-23T14:05:09-05:00", "[Z0000]", "-0500")]
        [DataRow("2026-08-23T14:05:09+05:30", "[Z0000]", "+0530")]
        [DataRow("2026-08-23T14:05:09+05:30", "[Z001]", "+530")]
        [DataRow("2026-08-23T14:05:09Z", "[Z00:00t]", "Z")]
        [DataRow("2026-08-23T14:05:09-05:00", "[Z00:00t]", "-05:00")]
        [DataRow("2026-08-23T14:05:09-10:00", "[ZZ]", "W")]
        [DataRow("2026-08-23T14:05:09-05:00", "[ZZ]", "R")]
        [DataRow("2026-08-23T14:05:09Z", "[ZZ]", "Z")]
        [DataRow("2026-08-23T14:05:09+10:00", "[ZZ]", "K")]
        [DataRow("2026-08-23T14:05:09+05:30", "[ZZ]", "+05:30")]
        [DataRow("2026-08-23T14:05:09+13:00", "[ZZ]", "+13:00")]
        [DataRow("2026-08-23T14:05:09", "[ZZ]", "J")]
        [DataRow("2026-08-23T14:05:09-14:00", "[z,2-2]", "GMT-14")]
        [DataRow("2026-08-23T14:05:09-13:30", "[z,2-2]", "GMT-13:30")]
        [DataRow("2026-08-23T14:05:09Z", "[z,2-2]", "GMT+00")]
        [DataRow("2026-08-23T14:05:09-05:00", "[z,6-6]", "GMT-05:00")]
        [DataRow("2026-08-23T14:05:09Z", "[z,6-6]", "GMT+00:00")]
        [DataRow("2026-08-23T14:05:09-00:30", "[z0]", "GMT-0:30")]
        [DataRow("2026-08-23T14:05:09-09:00", "[z0]", "GMT-9")]
        [DataRow("2026-08-23T14:05:09+05:30", "[ZN]", "+05:30")]
        [DataRow("2026-08-23T14:05:09Z", "[z]", "GMT+00:00")]
        [DataRow("2026-08-23T14:05:09", "[Z]", "")]
        public void ATimezoneIsWrittenAsTheComponentAsksFor(string value, string picture, string expected)
        {
            // A value with no timezone has nothing to say about one, which is the last row.
            Assert.AreEqual(
                expected,
                Value(Writes($"format-dateTime(xs:dateTime('{value}'), '{picture}')")));
        }

        [TestMethod]
        [DataRow("14:05:09.125", "125")]
        [DataRow("14:05:09.5", "5")]
        [DataRow("14:05:09", "0")]
        public void FractionalSecondsDropTheirTrailingZeros(string value, string expected)
        {
            Assert.AreEqual(expected, Value(Writes($"format-time(xs:time('{value}'), '[f]')")));
        }

        [TestMethod]
        public void WhitespaceInsideAComponentIsIgnored()
        {
            // It is allowed there for readability, so a marker means the same with or without it.
            Assert.AreEqual(
                "2026-08-23",
                Value(Writes("format-date(xs:date('2026-08-23'), '[Y 0001]-[M 01]-[D 01]')")));

            Assert.AreEqual(
                "Aug 23",
                Value(Writes("format-date(xs:date('2026-08-23'), '[M Nn, 3-3] [D 1]')")));
        }

        [TestMethod]
        public void ABracketIsWrittenByDoublingIt()
        {
            Assert.AreEqual(
                "[2026]",
                Value(Writes("format-date(xs:date('2026-08-23'), '[[[Y0001]]]')")));
        }

        [TestMethod]
        public void FormatDateRefusesAComponentADateDoesNotHave()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("format-date(xs:date('2026-08-23'), '[H01]')"));

            Assert.AreEqual("FOFD1350", error.Code);
        }

        [TestMethod]
        public void AnUnclosedComponentIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("format-date(xs:date('2026-08-23'), '[Y0001')"));

            Assert.AreEqual("FOFD1340", error.Code);
        }

        [TestMethod]
        public void FormattingAnEmptyValueGivesNothing()
        {
            Assert.AreEqual(string.Empty, Value(Writes("format-date((), '[Y0001]')")));
        }

        // ---- Arithmetic on dates and durations --------------------------------------------------------------

        [TestMethod]
        [DataRow("xs:date('2026-08-23') + xs:dayTimeDuration('P1D')", "2026-08-24")]
        [DataRow("xs:date('2026-08-23') - xs:dayTimeDuration('P1D')", "2026-08-22")]
        [DataRow("xs:dayTimeDuration('P1D') + xs:date('2026-08-23')", "2026-08-24")]
        [DataRow("xs:dateTime('2026-08-23T23:00:00') + xs:dayTimeDuration('PT2H')", "2026-08-24T01:00:00")]
        [DataRow("xs:date('2026-01-31') + xs:yearMonthDuration('P1M')", "2026-02-28")]
        public void ADurationMovesADate(string expression, string expected)
        {
            // The month case is the one that matters: months are not all the same length, so adding one is
            // not adding a count of days.
            Assert.AreEqual(expected, Value(Writes(expression)));
        }

        [TestMethod]
        public void SubtractingTwoDatesGivesTheLengthBetweenThem()
        {
            Assert.AreEqual(
                "P2D",
                Value(Writes("xs:date('2026-08-23') - xs:date('2026-08-21')")));
        }

        [TestMethod]
        [DataRow("xs:dayTimeDuration('PT1H') + xs:dayTimeDuration('PT30M')", "PT1H30M")]
        [DataRow("xs:dayTimeDuration('PT1H') - xs:dayTimeDuration('PT30M')", "PT30M")]
        [DataRow("xs:dayTimeDuration('PT1H') * 3", "PT3H")]
        [DataRow("3 * xs:dayTimeDuration('PT1H')", "PT3H")]
        [DataRow("xs:dayTimeDuration('PT1H') div 2", "PT30M")]
        [DataRow("xs:yearMonthDuration('P1Y') + xs:yearMonthDuration('P6M')", "P1Y6M")]
        public void DurationsCombineAndScale(string expression, string expected)
        {
            Assert.AreEqual(expected, Value(Writes(expression)));
        }

        [TestMethod]
        public void ATypedResultSurvivesTheCompiledBackend()
        {
            // Both backends run every test here, so this is really a note about what it is guarding: emitted
            // arithmetic used to be double arithmetic whatever the version, which gave the right answer for
            // XPath 1.0 and quietly the wrong one for everything 2.0 added.
            Assert.AreEqual("999999999999999999", Value(Writes("xs:integer('999999999999999998') + 1")));
        }

        [TestMethod]
        public void DividingADurationByADurationGivesANumber()
        {
            Assert.AreEqual(
                "3",
                Value(Writes("xs:dayTimeDuration('PT3H') div xs:dayTimeDuration('PT1H')")));
        }

        [TestMethod]
        public void ArithmeticWithNoMeaningIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes("xs:date('2026-08-23') * 2"));

            Assert.AreEqual("XPTY0004", error.Code);
        }

        // ---- Adjusting timezones ---------------------------------------------------------------------------

        [TestMethod]
        public void AdjustingToATimezoneKeepsTheInstant()
        {
            Assert.AreEqual(
                "2026-08-23T16:05:09+02:00",
                Value(Writes(
                    "adjust-dateTime-to-timezone(xs:dateTime('2026-08-23T14:05:09Z'), "
                    + "xs:dayTimeDuration('PT2H'))")));
        }

        [TestMethod]
        public void AdjustingWithNoTimezoneUsesTheImplicitOne()
        {
            Assert.AreEqual(
                "2026-08-23T14:05:09Z",
                Value(Writes("adjust-dateTime-to-timezone(xs:dateTime('2026-08-23T14:05:09'))")));
        }

        [TestMethod]
        public void AdjustingToTheEmptySequenceRemovesTheTimezone()
        {
            // The one adjustment that does not keep the instant: what it keeps is what the clock said.
            Assert.AreEqual(
                "2026-08-23T14:05:09",
                Value(Writes("adjust-dateTime-to-timezone(xs:dateTime('2026-08-23T14:05:09Z'), ())")));
        }

        [TestMethod]
        public void AdjustingAValueWithNoTimezoneAttachesOneRatherThanConverting()
        {
            // A value carrying no timezone denotes no instant, so there is no instant to keep. What is kept
            // is the clock reading: 2002-03-07 in −10:00 is that day, not the one before it.
            Assert.AreEqual(
                "2002-03-07-10:00",
                Value(Writes(
                    "adjust-date-to-timezone(xs:date('2002-03-07'), xs:dayTimeDuration('-PT10H'))")));

            // Where there is a timezone the instant is what is kept, and the day may move with it.
            Assert.AreEqual(
                "2002-03-06-10:00",
                Value(Writes(
                    "adjust-date-to-timezone(xs:date('2002-03-07Z'), xs:dayTimeDuration('-PT10H'))")));
        }

        [TestMethod]
        public void DateTimeMadeOfHalfAMomentIsNoMoment()
        {
            // Both halves are declared optional, so nothing in is nothing out rather than an error.
            Assert.AreEqual(string.Empty, Value(Writes("dateTime((), xs:time('12:00:00'))")));
            Assert.AreEqual(string.Empty, Value(Writes("dateTime(xs:date('2026-08-23'), ())")));
            Assert.AreEqual(
                "2026-08-23T12:00:00",
                Value(Writes("dateTime(xs:date('2026-08-23'), xs:time('12:00:00'))")));
        }

        [TestMethod]
        public void TheThreeCurrentFunctionsAgreeWithThemselvesAndEachOther()
        {
            // The clock is read once per evaluation and kept. Asking the operating system twice cannot
            // promise this, and a stylesheet stamping a date on every page could otherwise straddle midnight.
            Assert.AreEqual("true", Value(Writes("current-dateTime() eq current-dateTime()")));
            Assert.AreEqual("true", Value(Writes("current-time() eq current-time()")));
            Assert.AreEqual("true", Value(Writes("current-date() eq current-date()")));
            Assert.AreEqual("PT0S", Value(Writes("current-dateTime() - current-dateTime()")));

            // And the three are the same instant seen three ways.
            Assert.AreEqual(
                "true", Value(Writes("current-date() eq xs:date(current-dateTime())")));
            Assert.AreEqual(
                "true", Value(Writes("current-time() eq xs:time(current-dateTime())")));
        }

        [TestMethod]
        public void ATimezoneMoreThanFourteenHoursOutIsRefused()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes(
                    "adjust-dateTime-to-timezone(xs:dateTime('2026-08-23T14:05:09Z'), "
                    + "xs:dayTimeDuration('PT15H'))"));

            Assert.AreEqual("FODT0003", error.Code);
        }

        [TestMethod]
        public void DefaultCollationIsTheCodepointOne()
        {
            Assert.AreEqual(
                "http://www.w3.org/2005/xpath-functions/collation/codepoint",
                Value(Writes("default-collation()")));
        }

        [TestMethod]
        public void AWidthIsPaddedInThePicturesOwnDigitFamily()
        {
            // Which is the whole point of writing the picture in a family: this asks for ten Thai digits,
            // and padding it from U+0030 answered six in one family and four in another.
            Assert.AreEqual(
                "\u0e50\u0e50\u0e50\u0e50\u0e50\u0e50\u0e52\u0e50\u0e51\u0e52",
                Value(Writes("format-date(xs:date('2012-05-18'), '[Y\u0e50\u0e50\u0e50\u0e51,10]')")));

            // Latin digits pad from Latin zero, as they always did.
            Assert.AreEqual("0002012", Value(Writes("format-date(xs:date('2012-05-18'), '[Y0001,7]')")));
        }

        [TestMethod]
        public void APictureFunctionTakesTwoArgumentsOrFive()
        {
            // Not a range between them: there is no three-argument fn:format-date to call, so naming a
            // language without a calendar and a place is XPST0017 rather than a shorter way of saying it.
            foreach (string call in new[]
            {
                "format-date(xs:date('2026-08-23'), '[Y]', 'en')",
                "format-date(xs:date('2026-08-23'), '[Y]', 'en', ())",
                "format-dateTime(xs:dateTime('2026-08-23T00:00:00'), '[Y]', 'en')",
                "format-time(xs:time('00:00:00'), '[H]', 'en')",
            })
            {
                Assert.AreEqual(
                    "XPST0017",
                    Assert.ThrowsExactly<XsltException>(() => Writes(call)).Code,
                    call);
            }

            // And the place is of its declared type whether or not anything reads it. This engine has no
            // Olson database to honour one with, so it honours none -- but an integer there is still a type
            // error, and it is raised before the picture is read.
            Assert.AreEqual(
                "XPTY0004",
                Assert.ThrowsExactly<XsltException>(
                    () => Writes("format-date(xs:date('2026-08-23'), '[bla]', 'en', (), 5)")).Code);
        }
    }
}
