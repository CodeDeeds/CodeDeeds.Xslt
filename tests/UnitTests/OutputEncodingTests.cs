using System.Text;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for writing the result in the encoding <c>xsl:output</c> declared.
    /// </summary>
    /// <remarks>
    /// To a <see cref="TextWriter"/> the engine can only name an encoding in the XML declaration; the bytes
    /// belong to whoever built the writer, so the result can claim one thing and be another. Writing to a
    /// stream is what closes that gap, and these tests are about the difference.
    /// </remarks>
    [TestClass]
    public sealed class OutputEncodingTests
    {
        private static Xslt Compile(string output, string body = "<r>café</r>")
        {
            return new Xslt(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + output
                + $"<xsl:template match=\"/\">{body}</xsl:template>"
                + "</xsl:stylesheet>");
        }

        private static byte[] TransformToBytes(Xslt sheet)
        {
            using MemoryStream output = new MemoryStream();
            sheet.TransformXml("<in/>", output);
            return output.ToArray();
        }

        [TestMethod]
        public void TheDeclarationTellsTheTruthAboutTheBytes()
        {
            byte[] result = TransformToBytes(Compile("<xsl:output encoding=\"ISO-8859-1\"/>"));

            // Decoded as what it says it is, the document reads back intact.
            string text = Encoding.Latin1.GetString(result);
            StringAssert.Contains(text, "encoding=\"ISO-8859-1\"");
            StringAssert.Contains(text, "<r>café</r>");

            // And it really is those bytes: é is the single byte 0xE9 in Latin-1, where UTF-8 would spell it
            // as the pair 0xC3 0xA9.
            Assert.IsTrue(Holds(result, 0xE9), "é should be one Latin-1 byte");
            Assert.IsFalse(Holds(result, 0xC3, 0xA9), "nothing should be encoded as UTF-8");
        }

        /// <summary>Whether a byte sequence occurs in the result.</summary>
        private static bool Holds(byte[] result, params byte[] wanted)
        {
            for (int i = 0; i + wanted.Length <= result.Length; i++)
            {
                if (result.AsSpan(i, wanted.Length).SequenceEqual(wanted))
                {
                    return true;
                }
            }

            return false;
        }

        [TestMethod]
        public void ATextWriterDecidesItsOwnBytesAndTheDeclarationCanOnlyClaim()
        {
            // The gap the stream overload exists to close, asserted rather than described. The declaration
            // says ISO-8859-1 because the stylesheet does; the writer emits UTF-8 because it was built that
            // way, and nothing reconciles the two.
            StringWriter writer = new StringWriter();
            Compile("<xsl:output encoding=\"ISO-8859-1\"/>").TransformXml("<in/>", writer);

            StringAssert.Contains(writer.ToString(), "encoding=\"ISO-8859-1\"");

            byte[] asUtf8 = Encoding.UTF8.GetBytes(writer.ToString());
            Assert.IsTrue(Holds(asUtf8, 0xC3, 0xA9), "the writer encoded as UTF-8, whatever was declared");
        }

        [TestMethod]
        public void AByteOrderMarkIsWrittenAsBytes()
        {
            byte[] with = TransformToBytes(
                Compile("<xsl:output encoding=\"UTF-8\" byte-order-mark=\"yes\"/>"));

            CollectionAssert.AreEqual(
                Encoding.UTF8.GetPreamble(), with.Take(3).ToArray(), "the result should begin with a BOM");

            byte[] without = TransformToBytes(Compile("<xsl:output encoding=\"UTF-8\"/>"));

            Assert.AreNotEqual(0xEF, without[0], "no BOM was asked for");
        }

        [TestMethod]
        public void TheMarkIsWrittenOnceAndNotTwice()
        {
            // The encoding's preamble writes it. Were byte-order-mark also left set on the settings the
            // writer sees, U+FEFF would be written as a character on top of it.
            byte[] result = TransformToBytes(
                Compile("<xsl:output encoding=\"UTF-8\" byte-order-mark=\"yes\"/>"));

            Assert.AreEqual(0x3C, result[3], "the declaration should follow the mark directly");
        }

        [TestMethod]
        public void Utf16CarriesAMarkWhetherOrNotItIsAskedFor()
        {
            // XML 1.0 requires one on a UTF-16 document that carries no external encoding declaration, so it
            // is not the stylesheet's to switch off.
            byte[] result = TransformToBytes(Compile("<xsl:output encoding=\"UTF-16\"/>"));

            Assert.AreEqual(0xFF, result[0]);
            Assert.AreEqual(0xFE, result[1]);
            StringAssert.Contains(Encoding.Unicode.GetString(result), "<r>café</r>");
        }

        [TestMethod]
        public void ACharacterTheEncodingCannotHoldBecomesAReference()
        {
            // An em dash is not in ISO-8859-1. XSLT asks for a character reference; .NET's own default would
            // substitute '?' and lose it.
            byte[] result = TransformToBytes(
                Compile("<xsl:output encoding=\"ISO-8859-1\"/>", "<r>a—b</r>"));

            StringAssert.Contains(Encoding.Latin1.GetString(result), "<r>a&#x2014;b</r>");
        }

        [TestMethod]
        public void AnEncodingThisProcessDoesNotHaveIsRefused()
        {
            // Silently writing different bytes than the declaration promises is the bug this whole path
            // exists to fix, so an encoding that cannot be produced is said rather than approximated.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => TransformToBytes(Compile("<xsl:output encoding=\"NoSuchEncoding\"/>")));

            StringAssert.Contains(error.Message, "NoSuchEncoding");
        }

        [TestMethod]
        public void TheStreamIsLeftOpenForTheCaller()
        {
            using MemoryStream output = new MemoryStream();
            Compile("<xsl:output encoding=\"UTF-8\"/>").TransformXml("<in/>", output);

            Assert.IsTrue(output.CanWrite, "the caller owns the stream and may keep writing to it");
            Assert.IsGreaterThan(0, output.Length);
        }

        /// <summary>Collects secondary results as bytes, which is what a caller writing files has.</summary>
        private sealed class CollectingStreamResolver : IXsltResultStreamResolver
        {
            public Dictionary<string, MemoryStream> Documents { get; } = new(StringComparer.Ordinal);

            public Stream Resolve(string href, string? baseUri)
            {
                MemoryStream stream = new MemoryStream();
                Documents[href] = stream;
                return stream;
            }
        }

        /// <summary>Collects them as text, which cannot carry an encoding.</summary>
        private sealed class CollectingTextResolver : IXsltResultResolver
        {
            public Dictionary<string, StringWriter> Documents { get; } = new(StringComparer.Ordinal);

            public TextWriter Resolve(string href, string? baseUri)
            {
                StringWriter writer = new StringWriter();
                Documents[href] = writer;
                return writer;
            }
        }

        private const string SideDocument =
            "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
            + "<xsl:template match=\"/\">"
            + "<xsl:result-document href=\"side.xml\" encoding=\"ISO-8859-1\" byte-order-mark=\"yes\">"
            + "<side>café—</side></xsl:result-document>main</xsl:template>"
            + "</xsl:stylesheet>";

        [TestMethod]
        public void AResultDocumentHonoursItsOwnEncoding()
        {
            CollectingStreamResolver results = new CollectingStreamResolver();
            new Xslt(SideDocument, new XsltOptions { ResultStreamResolver = results })
                .TransformXml("<r/>");

            byte[] side = results.Documents["side.xml"].ToArray();
            string text = Encoding.Latin1.GetString(side);

            StringAssert.Contains(text, "encoding=\"ISO-8859-1\"");
            StringAssert.Contains(text, "<side>café&#x2014;</side>");

            // é as one Latin-1 byte, and the em dash — which Latin-1 has no room for — as a reference.
            Assert.IsTrue(Holds(side, 0xE9), "é should be one Latin-1 byte");

            // ISO-8859-1 has no byte-order mark to write, so asking for one gets nothing rather than a
            // stray U+FEFF: that character is itself unrepresentable here and would have arrived as
            // '&#xFEFF;' at the head of the document.
            Assert.AreEqual((byte)'<', side[0]);
        }

        [TestMethod]
        public void AResultDocumentGetsTheMarkWhereItsEncodingHasOne()
        {
            CollectingStreamResolver results = new CollectingStreamResolver();

            new Xslt(
                "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:template match=\"/\">"
                + "<xsl:result-document href=\"side.xml\" encoding=\"UTF-8\" byte-order-mark=\"yes\">"
                + "<side/></xsl:result-document></xsl:template></xsl:stylesheet>",
                new XsltOptions { ResultStreamResolver = results })
                .TransformXml("<r/>");

            CollectionAssert.AreEqual(
                Encoding.UTF8.GetPreamble(), results.Documents["side.xml"].ToArray().Take(3).ToArray());
        }

        [TestMethod]
        public void ATextResolverStillWorksAndItsDeclarationIsNowTrue()
        {
            // The older interface hands over characters rather than bytes, so nothing encodes them and the
            // declared encoding used to be a claim the text did not keep. The serializer writes the
            // reference itself now, which makes the declaration honest on this route too: an em dash is not
            // in ISO-8859-1, and what the resolver receives no longer contains one.
            CollectingTextResolver results = new CollectingTextResolver();
            new Xslt(SideDocument, new XsltOptions { ResultResolver = results })
                .TransformXml("<r/>");

            string side = results.Documents["side.xml"].ToString();

            StringAssert.Contains(side, "encoding=\"ISO-8859-1\"");
            StringAssert.Contains(side, "<side>café&#x2014;</side>");
        }

        [TestMethod]
        public void TheStreamResolverWinsWhereBothAreSupplied()
        {
            CollectingStreamResolver streams = new CollectingStreamResolver();
            CollectingTextResolver texts = new CollectingTextResolver();

            new Xslt(
                SideDocument,
                new XsltOptions { ResultStreamResolver = streams, ResultResolver = texts })
                .TransformXml("<r/>");

            Assert.IsTrue(streams.Documents.ContainsKey("side.xml"));
            Assert.IsEmpty(texts.Documents, "the text resolver should not have been asked");
        }

        [TestMethod]
        public void WithNeitherResolverAResultDocumentIsStillRefused()
        {
            // The posture that matters: a stylesheet is data as often as it is code, and adding a second way
            // to say yes must not become a way to say yes by default.
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(SideDocument).TransformXml("<r/>"));

            StringAssert.Contains(error.Message, "ResultStreamResolver");
        }

        [TestMethod]
        public void TheResultStreamIsLeftOpenForTheCaller()
        {
            CollectingStreamResolver results = new CollectingStreamResolver();
            new Xslt(SideDocument, new XsltOptions { ResultStreamResolver = results })
                .TransformXml("<r/>");

            Assert.IsTrue(
                results.Documents["side.xml"].CanWrite,
                "the caller owns whatever its resolver opened");
        }

        [TestMethod]
        public void JsonTransformsToAStreamToo()
        {
            Xslt sheet = new Xslt(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" "
                + "xmlns:j=\"http://www.w3.org/2005/xpath-functions\" exclude-result-prefixes=\"j\">"
                + "<xsl:output encoding=\"ISO-8859-1\"/>"
                + "<xsl:template match=\"/\"><r><xsl:value-of select=\"j:map/j:string\"/></r></xsl:template>"
                + "</xsl:stylesheet>");

            using MemoryStream output = new MemoryStream();
            sheet.TransformJson("{\"a\":\"café\"}", output);

            StringAssert.Contains(Encoding.Latin1.GetString(output.ToArray()), "<r>café</r>");
        }
    }
}
