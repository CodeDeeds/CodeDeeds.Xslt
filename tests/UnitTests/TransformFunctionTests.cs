namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:transform()</c>, which runs a second transformation and hands back a map of what it
    /// produced: the delivery format that decides what each result document is, the key each goes under, and
    /// the codes for a stylesheet that cannot be found and an option that is not one.
    /// </summary>
    [TestClass]
    public sealed class TransformFunctionTests
    {
        private const string Xsl = "http://www.w3.org/1999/XSL/Transform";

        /// <summary>Serves stylesheets from a dictionary, under absolute URIs so the keys are settled.</summary>
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_modules = new(StringComparer.Ordinal);

            public MapResolver Add(string uri, string stylesheet)
            {
                m_modules[uri] = stylesheet;
                return this;
            }

            /// <summary>How many times the resolver has been asked for anything.</summary>
            public int Asked => m_asked;

            private int m_asked;

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                System.Threading.Interlocked.Increment(ref m_asked);

                return m_modules.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static string Sheet(string body)
        {
            return $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + "<xsl:template name=\"xsl:initial-template\">" + body + "</xsl:template></xsl:stylesheet>";
        }

        private static Xslt Compiled(string body, IXsltResolver resolver)
        {
            return new Xslt(
                Sheet(body),
                new XsltOptions
                {
                    OmitXmlDeclaration = true,
                    Version = XsltVersion.V30,
                    StylesheetResolver = resolver,
                });
        }

        private static string Run(string body, IXsltResolver resolver)
        {
            return Compiled(body, resolver).Transform();
        }

        private static string Refuses(string body, IXsltResolver resolver)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, resolver)).Code ?? string.Empty;
        }

        [TestMethod]
        public void ATransformationIsRunAndComesBackAsAMap()
        {
            // The principal result goes under the key "output", and by default it is a document node: the
            // result tree the transformation built, which the caller can then navigate.
            MapResolver resolver = new MapResolver().Add(
                "file:///s/double.xsl",
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + "<xsl:template match=\".[. instance of xs:integer]\"><in><xsl:value-of select=\". * 2\"/></in>"
                + "</xsl:template></xsl:stylesheet>");

            Assert.AreEqual(
                "<out><in>84</in></out>",
                Run(
                    "<out><xsl:sequence select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/double.xsl',"
                    + " 'initial-match-selection': 42 })?output\"/></out>",
                    resolver));
        }

        [TestMethod]
        public void TheDeliveryFormatDecidesWhatEachResultIs()
        {
            MapResolver resolver = new MapResolver()
                .Add(
                    "file:///s/answer.xsl",
                    $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\""
                    + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                    + "<xsl:template name=\"xsl:initial-template\"><xsl:sequence select=\"xs:integer(7)\"/>"
                    + "</xsl:template></xsl:stylesheet>")
                .Add(
                    "file:///s/wrapped.xsl",
                    $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                    + "<xsl:template name=\"xsl:initial-template\"><n>7</n></xsl:template></xsl:stylesheet>");

            string Deliver(string module, string format, string read)
            {
                return Run(
                    "<out><xsl:sequence select=\"transform(map {"
                    + $" 'stylesheet-location': 'file:///s/{module}.xsl',"
                    + $" 'delivery-format': '{format}' }})?output{read}\"/></out>",
                    resolver);
            }

            // Raw is the sequence the transformation returned, so an integer arrives as an integer and can
            // be told from the string of it.
            Assert.AreEqual("<out>true</out>", Deliver("answer", "raw", " instance of xs:integer"));

            // A document is what the serializer would have been given: the result tree, which the caller
            // navigates rather than reads.
            Assert.AreEqual("<out>true</out>", Deliver("wrapped", "document", " instance of document-node()"));
            Assert.AreEqual("<out><n>7</n></out>", Deliver("wrapped", "document", string.Empty));

            // And serialized is what the serializer wrote, declaration included, as one string.
            Assert.AreEqual("<out>true</out>", Deliver("wrapped", "serialized", " instance of xs:string"));
            Assert.AreEqual(
                "<out>&lt;?xml version=\"1.0\" encoding=\"UTF-8\"?&gt;&lt;n&gt;7&lt;/n&gt;</out>",
                Deliver("wrapped", "serialized", string.Empty));
        }

        [TestMethod]
        public void AResultDocumentComesBackKeyedByItsUri()
        {
            // A transformation run this way writes nowhere: its result documents are collected and handed
            // back in the map, each under the URI its href resolved to, so no result resolver is needed.
            MapResolver resolver = new MapResolver().Add(
                "file:///s/two.xsl",
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template name=\"xsl:initial-template\">"
                + "<xsl:result-document href=\"aside.xml\"><aside>b</aside></xsl:result-document>"
                + "<main>a</main></xsl:template></xsl:stylesheet>");

            Assert.AreEqual(
                "<out><main>a</main><aside>b</aside></out>",
                Run(
                    "<xsl:variable name=\"r\" select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/two.xsl' })\"/>"
                    + "<out><xsl:sequence select=\"$r?output\"/>"
                    + "<xsl:sequence select=\"$r('file:///s/aside.xml')\"/></out>",
                    resolver));
        }

        [TestMethod]
        public void WhatCannotBeRunIsReportedAsItsOwnKindOfFailure()
        {
            MapResolver resolver = new MapResolver();

            // A stylesheet the resolver will not supply is a retrieval failure and nothing more.
            Assert.AreEqual(
                "FOXT0002",
                Refuses(
                    "<out><xsl:sequence select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/absent.xsl' })?output\"/></out>",
                    resolver));

            // An option that is not one, and no stylesheet named at all, are the caller's mistake.
            Assert.AreEqual(
                "FOXT0004",
                Refuses(
                    "<out><xsl:sequence select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/absent.xsl', 'stylesheet-locator': 'x' })?output\"/></out>",
                    resolver));

            Assert.AreEqual(
                "FOXT0004",
                Refuses(
                    "<out><xsl:sequence select=\"transform(map { 'delivery-format': 'raw' })?output\"/></out>",
                    resolver));

            // An entry point this engine has no way to start at is refused as a transformation it cannot
            // carry out, rather than run as though the option had not been written.
            Assert.AreEqual(
                "FOXT0001",
                Refuses(
                    "<out><xsl:sequence select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/absent.xsl',"
                    + " 'initial-function': QName('', 'f') })?output\"/></out>",
                    resolver));
        }

        [TestMethod]
        public void AFunctionItemCarriesTheTransformationItWasMadeIn()
        {
            // What raw delivery may hand back is a function item, and calling it happens after the
            // transformation that made it has ended — so the item carries that transformation with it, or
            // the body could not be run at all.
            MapResolver resolver = new MapResolver().Add(
                "file:///s/negative.xsl",
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\" xmlns:f=\"urn:f\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs f\">"
                + "<xsl:template name=\"xsl:initial-template\"><xsl:sequence select=\"f:negative#1\"/>"
                + "</xsl:template>"
                + "<xsl:function name=\"f:negative\" as=\"xs:boolean\"><xsl:param name=\"in\" as=\"xs:integer\"/>"
                + "<xsl:sequence select=\"$in lt 0\"/></xsl:function></xsl:stylesheet>");

            Assert.AreEqual(
                "<out>true false</out>",
                Run(
                    "<xsl:variable name=\"f\" select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/negative.xsl',"
                    + " 'delivery-format': 'raw' })?output\"/>"
                    + "<out><xsl:value-of select=\"$f(-1), $f(1)\"/></out>",
                    resolver));
        }

        // ---- A stylesheet compiled once and kept ---------------------------------------------------------

        private const string Doubling =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"" + Xsl + "\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
            + "<xsl:template match=\".[. instance of xs:integer]\"><in><xsl:value-of select=\". * 2\"/></in>"
            + "</xsl:template></xsl:stylesheet>";

        [TestMethod]
        public void AStylesheetNamedAgainIsNotReadAndCompiledAgain()
        {
            // A stylesheet that runs another with fn:transform() runs the same one every time it is run
            // itself, and reading and compiling it each time was most of what a short DocBook document
            // cost. It is read once by the call that names it, as a module xsl:import names is read once.
            MapResolver resolver = new MapResolver().Add("file:///s/double.xsl", Doubling);

            Xslt calling = Compiled(
                "<out><xsl:for-each select=\"1 to 3\"><xsl:sequence select=\"transform(map {"
                + " 'stylesheet-location': 'file:///s/double.xsl',"
                + " 'initial-match-selection': . })?output\"/></xsl:for-each></out>",
                resolver);

            Assert.AreEqual("<out><in>2</in><in>4</in><in>6</in></out>", calling.Transform());
            Assert.AreEqual(1, resolver.Asked, "Three calls in one transformation.");

            Assert.AreEqual("<out><in>2</in><in>4</in><in>6</in></out>", calling.Transform());
            Assert.AreEqual(1, resolver.Asked, "And a second transformation with the same compiled stylesheet.");

            // What is kept is kept by the compiled stylesheet that names it, and goes when that does:
            // the same stylesheet compiled again reads what it names again.
            Assert.AreEqual(
                "<out><in>84</in></out>",
                Run(
                    "<out><xsl:sequence select=\"transform(map {"
                    + " 'stylesheet-location': 'file:///s/double.xsl', 'initial-match-selection': 42 })?output\"/></out>",
                    resolver));
            Assert.AreEqual(2, resolver.Asked);

            // Two places that name the one stylesheet each keep their own.
            Xslt twice = Compiled(
                "<out><xsl:sequence select=\"transform(map { 'stylesheet-location': 'file:///s/double.xsl',"
                + " 'initial-match-selection': 1 })?output, transform(map { 'stylesheet-location': 'file:///s/double.xsl',"
                + " 'initial-match-selection': 2 })?output\"/></out>",
                resolver);

            Assert.AreEqual("<out><in>2</in><in>4</in></out>", twice.Transform());
            Assert.AreEqual("<out><in>2</in><in>4</in></out>", twice.Transform());
            Assert.AreEqual(4, resolver.Asked);
        }

        [TestMethod]
        public void ACallerMayAskForAStylesheetToBeReadEveryTime()
        {
            // 'cache': false() is the caller saying the stylesheet may have changed. Without it a change
            // is not seen for as long as the stylesheet that names it stays compiled; with it, it is.
            MapResolver resolver = new MapResolver().Add("file:///s/double.xsl", Doubling);

            Xslt keeping = Compiled(
                "<out><xsl:sequence select=\"transform(map { 'stylesheet-location': 'file:///s/double.xsl',"
                + " 'initial-match-selection': 21 })?output\"/></out>",
                resolver);

            Xslt reading = Compiled(
                "<out><xsl:sequence select=\"transform(map { 'stylesheet-location': 'file:///s/double.xsl',"
                + " 'initial-match-selection': 21, 'cache': false() })?output\"/></out>",
                resolver);

            Assert.AreEqual("<out><in>42</in></out>", keeping.Transform());
            Assert.AreEqual("<out><in>42</in></out>", reading.Transform());
            Assert.AreEqual(2, resolver.Asked);

            resolver.Add("file:///s/double.xsl", Doubling.Replace(". * 2", ". * 3"));

            Assert.AreEqual("<out><in>42</in></out>", keeping.Transform());
            Assert.AreEqual(2, resolver.Asked, "Kept, and so not asked for.");

            Assert.AreEqual("<out><in>63</in></out>", reading.Transform());
            Assert.AreEqual("<out><in>63</in></out>", reading.Transform());
            Assert.AreEqual(4, resolver.Asked, "Read at every call.");
        }

        [TestMethod]
        public void AStylesheetIsKeptForWhatItsStaticParametersWereGiven()
        {
            // A static parameter is what a stylesheet is compiled differently for: this one has an
            // element in it or not by what $shape says. So a stylesheet is found again only by a caller
            // who supplies the same for each static parameter it has, or nothing; what is supplied for
            // an ordinary parameter is read when it runs and compiles nothing.
            const string Shaped =
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"" + Xsl + "\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\">"
                + "<xsl:param name=\"shape\" static=\"yes\" as=\"xs:string\" select=\"'plain'\"/>"
                + "<xsl:param name=\"said\" as=\"xs:string\" select=\"'-'\"/>"
                + "<xsl:template name=\"xsl:initial-template\"><r><b xsl:use-when=\"$shape = 'bold'\"/>"
                + "<xsl:value-of select=\"$shape, $said\"/></r></xsl:template></xsl:stylesheet>";

            MapResolver resolver = new MapResolver().Add("file:///s/shaped.xsl", Shaped);

            static string Call(string options) =>
                "transform(map { 'stylesheet-location': 'file:///s/shaped.xsl'" + options + " })?output";

            Xslt calling = Compiled(
                "<out><xsl:sequence select=\""
                + Call(string.Empty) + ", "
                + Call(", 'static-params': map { QName('', 'shape'): 'bold' }") + ", "
                + Call(", 'static-params': map { QName('', 'shape'): 'plain' }, 'stylesheet-params': map { QName('', 'said'): 'one' }") + ", "
                + Call(", 'static-params': map { QName('', 'shape'): 'bold' }, 'stylesheet-params': map { QName('', 'said'): 'two' }") + ", "
                + Call(", 'stylesheet-params': map { QName('', 'said'): 'three' }")
                + "\"/></out>",
                resolver);

            const string Expected =
                "<out><r>plain -</r><r><b/>bold -</r><r>plain one</r><r><b/>bold two</r><r>plain three</r></out>";

            Assert.AreEqual(Expected, calling.Transform());

            // Each of the five is a place of its own, so each compiles once; the test is of the second
            // transformation, which compiles nothing, and of what each place then hands back.
            int asked = resolver.Asked;
            Assert.AreEqual(Expected, calling.Transform());
            Assert.AreEqual(asked, resolver.Asked);

            // One place, asked for the stylesheet three ways turn about: compiled once for each way a
            // static parameter was supplied, and never again, whatever the ordinary parameter says.
            MapResolver counted = new MapResolver().Add("file:///s/shaped.xsl", Shaped);

            Xslt looping = Compiled(
                "<out><xsl:for-each select=\"1 to 9\"><xsl:variable name=\"n\" select=\".\"/>"
                + "<xsl:sequence select=\"transform(map:merge((map { 'stylesheet-location': 'file:///s/shaped.xsl',"
                + " 'stylesheet-params': map { QName('', 'said'): string($n) } },"
                + " if ($n mod 3 = 1) then map { 'static-params': map { QName('', 'shape'): 'bold' } }"
                + " else if ($n mod 3 = 2) then map { 'static-params': map { QName('', 'shape'): 'plain' } }"
                + " else map { })))?output\" xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\"/>"
                + "</xsl:for-each></out>",
                counted);

            Assert.AreEqual(
                "<out><r><b/>bold 1</r><r>plain 2</r><r>plain 3</r><r><b/>bold 4</r><r>plain 5</r><r>plain 6</r>"
                + "<r><b/>bold 7</r><r>plain 8</r><r>plain 9</r></out>",
                looping.Transform());
            Assert.AreEqual(3, counted.Asked, "Supplied as bold, supplied as plain, and not supplied.");

            looping.Transform();
            Assert.AreEqual(3, counted.Asked);
        }

        [TestMethod]
        public void WhereAStylesheetStartsIsStillTheCallersToSay()
        {
            // The options a transformation is set going with are read for each call, whether or not the
            // stylesheet was compiled for an earlier one.
            MapResolver resolver = new MapResolver().Add(
                "file:///s/two.xsl",
                $"<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"{Xsl}\">"
                + "<xsl:template name=\"one\"><one/></xsl:template><xsl:template name=\"two\"><two/></xsl:template>"
                + "<xsl:template match=\".\" mode=\"m\"><m><xsl:value-of select=\".\"/></m></xsl:template>"
                + "<xsl:template match=\".\"><plain><xsl:value-of select=\".\"/></plain></xsl:template></xsl:stylesheet>");

            Xslt calling = Compiled(
                "<out><xsl:for-each select=\"1 to 2\"><xsl:sequence select=\""
                + "transform(map { 'stylesheet-location': 'file:///s/two.xsl', 'initial-template': QName('', 'one') })?output,"
                + " transform(map { 'stylesheet-location': 'file:///s/two.xsl', 'initial-template': QName('', 'two') })?output,"
                + " transform(map { 'stylesheet-location': 'file:///s/two.xsl', 'initial-match-selection': ., 'initial-mode': QName('', 'm') })?output,"
                + " transform(map { 'stylesheet-location': 'file:///s/two.xsl', 'initial-match-selection': . })?output,"
                + " transform(map { 'stylesheet-location': 'file:///s/two.xsl', 'initial-match-selection': . + 10, 'delivery-format': 'serialized',"
                + " 'serialization-params': map { QName('', 'omit-xml-declaration'): true() } })?output"
                + "\"/></xsl:for-each></out>",
                resolver);

            const string Expected =
                "<out><one/><two/><m>1</m><plain>1</plain>&lt;plain&gt;11&lt;/plain&gt;"
                + "<one/><two/><m>2</m><plain>2</plain>&lt;plain&gt;12&lt;/plain&gt;</out>";

            Assert.AreEqual(Expected, calling.Transform());
            Assert.AreEqual(Expected, calling.Transform());
        }

        [TestMethod]
        public void AStylesheetKeptIsRunByTransformationsAtOnce()
        {
            // A compiled stylesheet is shared between transformations running at once, and so is what one
            // of its calls keeps.
            MapResolver resolver = new MapResolver().Add("file:///s/double.xsl", Doubling);

            Xslt calling = Compiled(
                "<out><xsl:for-each select=\"1 to 20\"><xsl:sequence select=\"transform(map {"
                + " 'stylesheet-location': 'file:///s/double.xsl',"
                + " 'initial-match-selection': . })?output\"/></xsl:for-each></out>",
                resolver);

            string expected = "<out>" + string.Concat(Enumerable.Range(1, 20).Select(n => $"<in>{n * 2}</in>")) + "</out>";
            string[] results = new string[32];

            Parallel.For(0, results.Length, i => results[i] = calling.Transform());

            foreach (string result in results)
            {
                Assert.AreEqual(expected, result);
            }

            // Two that began together may each have compiled it, and no more than began together did.
            Assert.IsTrue(resolver.Asked >= 1 && resolver.Asked <= results.Length, $"Asked {resolver.Asked} times.");

            int settled = resolver.Asked;
            calling.Transform();
            Assert.AreEqual(settled, resolver.Asked);
        }

        [TestMethod]
        public void AStylesheetGivenAsTextIsKeptAndOneGivenAsANodeIsCompiledEachTime()
        {
            // Text is its own name. Nothing can be counted here, the resolver not being asked for either,
            // so this holds only the answers: the same from the first call and from the ninth.
            MapResolver resolver = new MapResolver();
            string text = Doubling.Replace("\"", "'");

            Xslt calling = Compiled(
                "<xsl:variable name=\"text\" select=\"&quot;" + text.Replace("<", "&lt;") + "&quot;\"/>"
                + "<xsl:variable name=\"node\" select=\"parse-xml(replace($text, '[*] 2', '* 5'))\"/>"
                + "<out><xsl:for-each select=\"1 to 3\"><xsl:sequence select=\"transform(map {"
                + " 'stylesheet-text': $text,"
                + " 'initial-match-selection': . })?output, transform(map {"
                + " 'stylesheet-node': $node, 'initial-match-selection': . })?output\"/></xsl:for-each></out>",
                resolver);

            const string Expected = "<out><in>2</in><in>5</in><in>4</in><in>10</in><in>6</in><in>15</in></out>";

            Assert.AreEqual(Expected, calling.Transform());
            Assert.AreEqual(Expected, calling.Transform());
            Assert.AreEqual(Expected, calling.Transform());
            Assert.AreEqual(0, resolver.Asked);
        }
    }
}
