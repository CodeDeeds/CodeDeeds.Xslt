using System.Text;
using CodeDeeds.Xslt.Model;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for transforming from a stream rather than from text.
    /// </summary>
    /// <remarks>
    /// The point is not only that it saves a conversion. An XML document says what encoding it is in, and
    /// only the bytes carry that: decoding to text first settles the question before the declaration is ever
    /// read. These tests are mostly about the difference that makes.
    /// </remarks>
    [TestClass]
    public sealed class StreamInputTests
    {
        private const string Json = "http://www.w3.org/2005/xpath-functions";

        private static Xslt Identity()
        {
            return new Xslt(
                "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:template match=\"/\"><out><xsl:value-of select=\"/r\"/></out></xsl:template>"
                + "</xsl:stylesheet>",
                new XsltOptions { OmitXmlDeclaration = true });
        }

        private static string TransformXmlBytes(byte[] bytes)
        {
            using MemoryStream stream = new MemoryStream(bytes);
            StringWriter writer = new StringWriter();
            Identity().TransformXml(stream, writer);
            return writer.ToString();
        }

        [TestMethod]
        public void TheDocumentSaysWhatEncodingItIsIn()
        {
            // 'café' in ISO-8859-1 is one byte per character, and 0xE9 is not valid UTF-8 at all. Read as
            // bytes, the declaration is honoured and the text comes back intact.
            byte[] latin1 = Encoding.Latin1.GetBytes(
                "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><r>café</r>");

            Assert.AreEqual("<out>café</out>", TransformXmlBytes(latin1));
        }

        [TestMethod]
        public void DecodingFirstSettlesTheQuestionBeforeTheDeclarationIsRead()
        {
            // The same bytes through a TextReader, which is what the text overloads get. Whoever opened the
            // reader has already chosen — here wrongly — and the declaration inside the document cannot
            // undo that. This is the difference the stream overload exists for, so it is asserted rather
            // than described.
            byte[] latin1 = Encoding.Latin1.GetBytes(
                "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><r>café</r>");

            using MemoryStream stream = new MemoryStream(latin1);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            StringWriter writer = new StringWriter();
            Identity().TransformXml(reader, writer);

            Assert.AreNotEqual("<out>café</out>", writer.ToString());
            StringAssert.Contains(writer.ToString(), "�");
        }

        [TestMethod]
        public void AByteOrderMarkIsNotPartOfTheDocument()
        {
            // Legal at the head of a file and not part of the XML or the JSON. XmlReader skips one; the
            // JSON reader does not, so this engine has to.
            byte[] xml = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes("<r>text</r>"))
                .ToArray();

            Assert.AreEqual("<out>text</out>", TransformXmlBytes(xml));

            byte[] json = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes("{\"a\":1}"))
                .ToArray();

            using MemoryStream stream = new MemoryStream(json);
            XdmTree tree = JsonTreeBuilder.FromJson(stream);

            Assert.AreEqual("map", tree.NameTable.GetLocalName(tree.FingerprintOf(1)));
        }

        [TestMethod]
        public void AStreamAndAStringAgree()
        {
            const string Document = "<r>plain</r>";

            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(Document));
            StringWriter fromStream = new StringWriter();
            Identity().TransformXml(stream, fromStream);

            StringWriter fromText = new StringWriter();
            Identity().TransformXml(new StringReader(Document), fromText);

            Assert.AreEqual(fromText.ToString(), fromStream.ToString());
        }

        [TestMethod]
        public void ADocumentLargerThanTheReadBufferIsReadWhole()
        {
            // The JSON buffer starts at 16 KB and doubles, so this exercises the growth path several times.
            string[] names = Enumerable.Range(0, 8000).Select(i => $"\"n{i}\":{i}").ToArray();
            byte[] json = Encoding.UTF8.GetBytes("{" + string.Join(',', names) + "}");

            Assert.IsGreaterThan(64 * 1024, json.Length, "the document should outgrow the buffer twice over");

            using MemoryStream stream = new MemoryStream(json);
            XdmTree tree = JsonTreeBuilder.FromJson(stream);

            Assert.AreEqual(8000, CountChildren(tree));
        }

        [TestMethod]
        public void AStreamThatAnswersShortIsStillReadWhole()
        {
            // A stream is allowed to return fewer bytes than asked for, and a network one routinely does.
            // Reading once and trusting the count is how a document arrives truncated.
            byte[] json = Encoding.UTF8.GetBytes("{\"a\":1,\"b\":2,\"c\":3}");

            using OneByteAtATimeStream stream = new OneByteAtATimeStream(json);
            XdmTree tree = JsonTreeBuilder.FromJson(stream);

            Assert.AreEqual(3, CountChildren(tree));
        }

        private static int CountChildren(XdmTree tree)
        {
            int count = 0;
            for (int child = tree.FirstChildOf(1); child >= 0; child = tree.NextSiblingOf(child))
            {
                count++;
            }

            return count;
        }

        /// <summary>A stream that answers one byte at a time, as a stream is entitled to.</summary>
        private sealed class OneByteAtATimeStream : MemoryStream
        {
            public OneByteAtATimeStream(byte[] bytes)
                : base(bytes)
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return base.Read(buffer, offset, Math.Min(count, 1));
            }
        }
    }
}
