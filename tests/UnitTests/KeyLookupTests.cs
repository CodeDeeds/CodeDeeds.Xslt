using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what <c>key()</c> finds under XSLT 2.0 and 3.0: values compared as values, several
    /// declarations of one name, the subtree a third argument names, a computed name resolved where it is
    /// written — and <c>id()</c> over <c>xml:id</c>.
    /// </summary>
    [TestClass]
    public sealed class KeyLookupTests
    {
        private const string Towns =
            "<doc><town name=\"Enfield\" state=\"NH\" pop=\"4\"/><town name=\"Enfield\" state=\"CT\" pop=\"4.0\"/>"
            + "<town name=\"Salem\" state=\"NH\" pop=\"NaN\"/><town name=\"Bristol\" state=\"RI\" pop=\"7\"/></doc>";

        private static string Sheet(string declarations, string body, string version = "3.0")
        {
            return $"<xsl:stylesheet version=\"{version}\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:k=\"urn:k\" exclude-result-prefixes=\"xs k\">"
                + declarations + "<xsl:template match=\"/\"><out>" + body + "</out></xsl:template></xsl:stylesheet>";
        }

        private static string Run(string stylesheet, string input = Towns, XsltVersion? version = null)
        {
            return new Xslt(stylesheet, new XsltOptions { Version = version ?? XsltVersion.V30, OmitXmlDeclaration = true })
                .TransformXml(input);
        }

        private static string Refuses(string stylesheet, string input = Towns)
        {
            return Assert.ThrowsExactly<XsltException>(() => Run(stylesheet, input)).Code ?? string.Empty;
        }

        [TestMethod]
        public void AKeyMatchesByValueNotBySpelling()
        {
            // An integer finds a node filed under a number of any numeric type, and a string does not;
            // an untyped value filed is a string; a boolean is itself; NaN is found by nothing.
            const string Keys =
                "<xsl:key name=\"pop\" match=\"town\" use=\"xs:double(@pop)\"/>"
                + "<xsl:key name=\"len\" match=\"town\" use=\"string-length(@name)\"/>"
                + "<xsl:key name=\"u\" match=\"town\" use=\"xs:untypedAtomic(string-length(@name))\"/>"
                + "<xsl:key name=\"big\" match=\"town\" use=\"@pop &gt; 5\"/>";

            Assert.AreEqual(
                "<out>NH CT|NH CT||NH CT RI|NH CT RI|RI|NH CT RI</out>",
                Run(Sheet(
                    Keys,
                    "<xsl:value-of select=\"key('pop', 4)/@state\"/>|<xsl:value-of select=\"key('pop', 4.0)/@state\"/>|"
                    + "<xsl:value-of select=\"key('pop', '4')/@state\"/>|<xsl:value-of select=\"key('len', 7)/@state\"/>|"
                    + "<xsl:value-of select=\"key('u', '7')/@state\"/>|<xsl:value-of select=\"key('big', true())/@state\"/>|"
                    + "<xsl:value-of select=\"key('u', 7)/@state, key('len', '7')/@state, key('pop', xs:double('NaN'))/@state, key('u', '7')/@state\"/>")));

            // A date finds a date written in another zone, and a node contributes its string value.
            Assert.AreEqual(
                "<out>b|a b</out>",
                Run(Sheet(
                    "<xsl:key name=\"when\" match=\"e\" use=\"xs:dateTime(@t)\"/><xsl:key name=\"name\" match=\"e\" use=\"@n\"/>",
                    "<xsl:value-of select=\"key('when', xs:dateTime('2002-10-05T22:00:00-05:00'))/@id\"/>|"
                    + "<xsl:value-of select=\"key('name', /doc/e/@n)/@id\"/>"),
                    input: "<doc><e id=\"a\" n=\"x\" t=\"2002-10-05T00:00:00Z\"/><e id=\"b\" n=\"y\" t=\"2002-10-06T03:00:00Z\"/></doc>"));
        }

        [TestMethod]
        public void SeveralDeclarationsOfOneNameAreOneKey()
        {
            // A node any declaration matches is filed under what that declaration reads from it, and a node
            // two of them file under one value is found once. A 2.0 stylesheet may file by content too.
            Assert.AreEqual(
                "<out>NH CT NH RI|Enfield Enfield|1</out>",
                Run(Sheet(
                    "<xsl:key name=\"k\" match=\"town\" use=\"@state\"/>"
                    + "<xsl:key name=\"k\" match=\"town[@state='NH']\" use=\"string-length(@name)\"/>"
                    + "<xsl:key name=\"k\" match=\"town\"><xsl:sequence select=\"'all'\"/></xsl:key>"
                    + "<xsl:key name=\"same\" match=\"town\" use=\"@name\"/><xsl:key name=\"same\" match=\"town\" use=\"@name\"/>",
                    "<xsl:value-of select=\"key('k', 'all')/@state\"/>|<xsl:value-of select=\"key('same', 'Enfield')/@name\"/>|"
                    + "<xsl:value-of select=\"count(key('k', ('NH', 7))[@name='Salem'])\"/>",
                    version: "2.0"),
                    version: XsltVersion.V20));

            // Declarations disagreeing about composite are refused.
            Assert.AreEqual(
                "XTSE1222",
                Refuses(Sheet(
                    "<xsl:key name=\"c\" match=\"town\" use=\"@state\" composite=\"yes\"/><xsl:key name=\"c\" match=\"town\" use=\"@name\"/>",
                    string.Empty)));
        }

        [TestMethod]
        public void TheThirdArgumentNamesTheSubtreeSearched()
        {
            // Only what stands at or below the node is found, in the document that node is in — a temporary
            // tree included — and the two-argument form still searches the whole document.
            Assert.AreEqual(
                "<out>CT|NH ME CT|NH CT</out>",
                Run(Sheet(
                    "<xsl:key name=\"town\" match=\"town\" use=\"@name\"/>",
                    "<xsl:variable name=\"tree\"><state name=\"north\"><town name=\"Enfield\" state=\"NH\"/><town name=\"Enfield\" state=\"ME\"/></state>"
                    + "<state name=\"south\"><town name=\"Enfield\" state=\"CT\"/></state></xsl:variable>"
                    + "<xsl:value-of select=\"key('town', 'Enfield', $tree/state[@name='south'])/@state\"/>|"
                    + "<xsl:value-of select=\"$tree/state[@name='south']/key('town', 'Enfield')/@state\"/>|"
                    + "<xsl:value-of select=\"key('town', 'Enfield', /)/@state\"/>")));
        }

        [TestMethod]
        public void AComputedNameIsResolvedWhereItIsWritten()
        {
            // 'k:x' means the key in the namespace k is bound to at the call, and nothing else declares
            // that name; an unprefixed name is in no namespace, and finds the key declared without one.
            Assert.AreEqual(
                "XTDE1260",
                Refuses(Sheet(
                    "<xsl:key name=\"x\" match=\"town\" use=\"@state\"/><xsl:variable name=\"n\" select=\"'k:x'\"/>",
                    "<xsl:value-of select=\"key($n, 'NH')/@name\"/>")));
            Assert.AreEqual(
                "<out>Enfield Salem|Bristol</out>",
                Run(Sheet(
                    "<xsl:key name=\"x\" match=\"town\" use=\"@state\"/><xsl:key name=\"k:x\" match=\"town\" use=\"@state\"/>"
                    + "<xsl:variable name=\"plain\" select=\"'x'\"/><xsl:variable name=\"prefixed\" select=\"'k:x'\"/>",
                    "<xsl:value-of select=\"key($plain, 'NH')/@name\"/>|<xsl:value-of select=\"key($prefixed, 'RI')/@name\"/>")));

            // A declaration inside the key is misplaced before it is content.
            Assert.AreEqual(
                "XTSE0010",
                Refuses(Sheet(
                    "<xsl:key name=\"x\" match=\"town\" use=\"@state\"><xsl:template match=\"/\"/></xsl:key>",
                    string.Empty)));
        }

        [TestMethod]
        public void IdFindsElementsByXmlId()
        {
            // Each argument holds whitespace-separated IDs; the answer is in document order, once each; a
            // second argument names the document, a temporary tree included; a parentless node is refused.
            const string Input = "<doc><a xml:id=\"one\">1</a><b xml:id=\"two\"><c xml:id=\"three\">3</c></b><d xml:id=\"one\">dup</d></doc>";

            Assert.AreEqual(
                "<out>a c|a b c|Damson|3</out>",
                Run(Sheet(
                    "<xsl:variable name=\"lookup\"><code xml:id=\"d\">Damson</code><code xml:id=\"e\">Elder</code></xsl:variable>",
                    "<xsl:value-of select=\"id('three one')/name()\"/>|<xsl:value-of select=\"id(('one', 'two three', 'one'))/name()\"/>|"
                    + "<xsl:value-of select=\"id('d', $lookup)\"/>|<xsl:value-of select=\"element-with-id('three')\"/>"),
                    input: Input));
            Assert.AreEqual(
                "FODC0001",
                Refuses(Sheet(
                    "<xsl:variable name=\"alone\" as=\"element()\"><e xml:id=\"x\"/></xsl:variable>",
                    "<xsl:value-of select=\"id('x', $alone)\"/>"),
                    input: Input));
        }
    }
}
