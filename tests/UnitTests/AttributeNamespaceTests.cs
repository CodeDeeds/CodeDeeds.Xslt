namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the prefix an attribute ends up with, and for the two things a sequence of nodes is not:
    /// a document to navigate from, and a set of strings.
    /// </summary>
    /// <remarks>
    /// An attribute's prefix is a spelling of its namespace and nothing more. <c>xsl:attribute</c> may name
    /// a prefix and a namespace that do not go together, and the namespace is the part that was meant — so
    /// where the prefix is spoken for on the element, it is the prefix that gives way.
    /// </remarks>
    [TestClass]
    public sealed class AttributeNamespaceTests
    {
        private const string Xsl =
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:p=\"urn:one\""
            + " exclude-result-prefixes=\"xs\"";

        private static string Run(string body, string version = "3.0")
        {
            string stylesheet = $"<xsl:stylesheet version=\"{version}\" {Xsl}>"
                + "<xsl:template name=\"xsl:initial-template\">" + body + "</xsl:template></xsl:stylesheet>";

            return new Xslt(
                stylesheet,
                new XsltOptions { Version = XsltVersion.V30, OmitXmlDeclaration = true }).Transform();
        }

        private static string Refuses(string body, string version = "3.0")
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(body, version)).Code ?? string.Empty;
        }

        [TestMethod]
        public void AnAttributeGivesUpAPrefixThatIsSpokenFor()
        {
            // The element carries xmlns:p="urn:one" from the stylesheet, and the attribute was told to be
            // in urn:two. One element cannot declare p twice, so the attribute takes a prefix of its own —
            // and the namespace, which is the part that was asked for, is kept.
            Assert.AreEqual(
                "<out xmlns:p=\"urn:one\" xmlns:p_1=\"urn:two\" p_1:local=\"content\"/>",
                Run("<out><xsl:attribute name=\"p:local\" namespace=\"urn:two\">content</xsl:attribute></out>"));

            // Where nothing has claimed it, the prefix that was written is the prefix that is used.
            Assert.AreEqual(
                "<out xmlns:p=\"urn:one\" p:local=\"content\"/>",
                Run("<out><xsl:attribute name=\"p:local\" namespace=\"urn:one\">content</xsl:attribute></out>"));
        }

        [TestMethod]
        public void ATreeBuiltAsASequenceRenamesTheClashingPrefixToo()
        {
            // The same collision, seen from the tree rather than from the serializer: what the attribute is
            // called has to be answerable before anything is written, since a stylesheet can ask.
            Assert.AreEqual(
                "<out xmlns:p=\"urn:one\" ns=\"urn:two\" prefixed=\"false\"/>",
                Run(
                    "<xsl:variable name=\"e\" as=\"element()\">"
                    + "<p:box><xsl:attribute name=\"p:local\" namespace=\"urn:two\">c</xsl:attribute></p:box>"
                    + "</xsl:variable>"
                    + "<out ns=\"{namespace-uri($e/@*)}\""
                    + " prefixed=\"{starts-with(name($e/@*), 'p:')}\"/>"));
        }

        [TestMethod]
        public void ASequenceOfNodesIsNoDocumentToNavigateFrom()
        {
            // A sequence constructor with a declared type produces a sequence, and the nodes in a sequence
            // are parentless: there is no document node above them for '/' to name. A leading '/' is
            // fn:root(self::node()) treated as a document node, and this is that treat failing.
            const string detached =
                "<xsl:variable name=\"e\" as=\"element()\"><foo><bar/></foo></xsl:variable>"
                + "<out><xsl:for-each select=\"$e\">{0}</xsl:for-each></out>";

            Assert.AreEqual("XPDY0050", Refuses(string.Format(detached, "<xsl:copy-of select=\"/\"/>")));
            Assert.AreEqual("XPDY0050", Refuses(string.Format(detached, "<xsl:copy-of select=\"/bar\"/>")));

            // A descendant of that element is in the same tree and no better off.
            Assert.AreEqual(
                "XPDY0050",
                Refuses(
                    "<xsl:variable name=\"e\" as=\"element()\"><foo><bar/></foo></xsl:variable>"
                    + "<out><xsl:for-each select=\"$e/bar\"><xsl:copy-of select=\"/\"/></xsl:for-each></out>"));

            // Without a declared type the constructor builds a document, and '/' names it as it always did.
            Assert.AreEqual(
                "<out xmlns:p=\"urn:one\"><foo><bar/></foo></out>",
                Run(
                    "<xsl:variable name=\"e\"><foo><bar/></foo></xsl:variable>"
                    + "<out><xsl:for-each select=\"$e\"><xsl:copy-of select=\"/\"/></xsl:for-each></out>"));
        }

        [TestMethod]
        public void ACommentAndAProcessingInstructionAtomizeToStrings()
        {
            // XDM §5: the typed value of a comment, a processing instruction and a namespace node is an
            // xs:string, where an element, an attribute, a text node and a document node in a tree nothing
            // validated give an xs:untypedAtomic. The three that answer xs:string are the ones whose
            // content was never a candidate for validation — there is nothing a schema could have said
            // about the text of a comment, so the type it has is the type it always has.
            Assert.AreEqual(
                "<out xmlns:p=\"urn:one\" c=\"true\" i=\"true\" e=\"true\" a=\"true\"/>",
                Run(
                    "<xsl:variable name=\"d\"><doc a=\"x\">"
                    + "<xsl:comment>c</xsl:comment>"
                    + "<xsl:processing-instruction name=\"pi\">v</xsl:processing-instruction>"
                    + "t</doc></xsl:variable>"
                    + "<out c=\"{data($d/doc/comment()) instance of xs:string}\""
                    + " i=\"{data($d/doc/processing-instruction()) instance of xs:string}\""
                    + " e=\"{data($d/doc) instance of xs:untypedAtomic}\""
                    + " a=\"{data($d/doc/@a) instance of xs:untypedAtomic}\"/>"));
        }

        [TestMethod]
        public void AnUntypedValueIsNotAString()
        {
            // xs:untypedAtomic is a type of its own and not a kind of string, however alike the two are
            // held: what a node atomizes to is an instance of the one and not of the other.
            Assert.AreEqual(
                "<out xmlns:p=\"urn:one\" a=\"false\" b=\"true\" c=\"true\" d=\"false\"/>",
                Run(
                    "<out a=\"{xs:untypedAtomic('u') instance of xs:string}\""
                    + " b=\"{xs:untypedAtomic('u') instance of xs:untypedAtomic}\""
                    + " c=\"{'s' instance of xs:string}\""
                    + " d=\"{'s' instance of xs:untypedAtomic}\"/>"));
        }
    }
}
