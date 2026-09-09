using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:key</c> and the <c>key()</c> function.
    /// </summary>
    /// <remarks>
    /// Keys are what turn a cross-reference from a scan into a lookup, so as well as producing the right nodes
    /// they have to produce them in document order, without duplicates, and only from the document being
    /// transformed.
    /// </remarks>
    [TestClass]
    public sealed class KeyTests
    {
        private const string Catalogue =
            "<data>"
            + "<items>"
            + "<item id=\"1\" cat=\"a\"/><item id=\"2\" cat=\"b\"/><item id=\"3\" cat=\"a\"/>"
            + "</items>"
            + "<cats>"
            + "<cat id=\"a\" name=\"Alpha\"/><cat id=\"b\" name=\"Beta\"/>"
            + "</cats>"
            + "</data>";

        private static string Sheet(string body)
        {
            return "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + body
                + "</xsl:stylesheet>";
        }

        /// <summary>Compiles a stylesheet with the XML declaration suppressed, since these tests compare fragments.</summary>
        private static Xslt Compile(string stylesheet, XsltBackend backend = XsltBackend.Interpreted)
        {
            return new Xslt(stylesheet, new XsltOptions { Backend = backend, OmitXmlDeclaration = true });
        }

        private static string Run(string stylesheet, string input)
        {
            string interpreted = Compile(stylesheet, XsltBackend.Interpreted).TransformXml(input);
            string compiled = Compile(stylesheet, XsltBackend.Compiled).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        private static void AssertMatchesReference(string stylesheet, string input)
        {
            XslCompiledTransform transform = new XslCompiledTransform();
            using (XmlReader reader = XmlReader.Create(new StringReader(stylesheet)))
            {
                transform.Load(reader);
            }

            StringWriter output = new StringWriter();
            XmlWriterSettings settings = transform.OutputSettings!.Clone();
            settings.OmitXmlDeclaration = true;
            settings.ConformanceLevel = ConformanceLevel.Auto;

            using (XmlWriter writer = XmlWriter.Create(output, settings))
            using (XmlReader reader = XmlReader.Create(new StringReader(input)))
            {
                transform.Transform(reader, null, writer);
            }

            string expected = Normalize(output.ToString());
            string actual = Normalize(Run(stylesheet, input));

            Assert.AreEqual(expected, actual, "output differed from XslCompiledTransform");
        }

        private static string Normalize(string fragment)
        {
            return XmlComparison.Normalize(fragment);
        }

        [TestMethod]
        public void KeyResolvesACrossReference()
        {
            AssertMatchesReference(
                Sheet("<xsl:key name=\"catById\" match=\"cat\" use=\"@id\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//item\">"
                    + "<i id=\"{@id}\" cat=\"{key('catById', @cat)/@name}\"/>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                Catalogue);
        }

        [TestMethod]
        public void KeyReturnsEveryMatchingNodeInDocumentOrder()
        {
            AssertMatchesReference(
                Sheet("<xsl:key name=\"itemByCat\" match=\"item\" use=\"@cat\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"key('itemByCat', 'a')\">"
                    + "<i><xsl:value-of select=\"@id\"/></i>"
                    + "</xsl:for-each>"
                    + "<count><xsl:value-of select=\"count(key('itemByCat','a'))\"/></count>"
                    + "</out></xsl:template>"),
                Catalogue);
        }

        [TestMethod]
        public void KeyWithNoMatchYieldsAnEmptyNodeSet()
        {
            AssertMatchesReference(
                Sheet("<xsl:key name=\"itemByCat\" match=\"item\" use=\"@cat\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<n><xsl:value-of select=\"count(key('itemByCat','zzz'))\"/></n>"
                    + "<empty><xsl:value-of select=\"boolean(key('itemByCat','zzz'))\"/></empty>"
                    + "</out></xsl:template>"),
                Catalogue);
        }

        [TestMethod]
        public void KeyLookupByNodeSetUnionsTheResults()
        {
            // A node-set second argument looks up each of its members' string-values and unions the results,
            // with no duplicates even when two members resolve to the same node.
            AssertMatchesReference(
                Sheet("<xsl:key name=\"itemByCat\" match=\"item\" use=\"@cat\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"key('itemByCat', //item/@cat)\">"
                    + "<i><xsl:value-of select=\"@id\"/></i>"
                    + "</xsl:for-each>"
                    + "</out></xsl:template>"),
                Catalogue);
        }

        [TestMethod]
        public void KeyCanIndexUnderSeveralValuesPerNode()
        {
            // A use expression selecting several nodes indexes the matched node under each of their values.
            string input = "<r><p tags=\"x\"><t>red</t><t>blue</t></p><p tags=\"y\"><t>blue</t></p></r>";

            AssertMatchesReference(
                Sheet("<xsl:key name=\"byTag\" match=\"p\" use=\"t\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<blue><xsl:for-each select=\"key('byTag','blue')\">"
                    + "<p><xsl:value-of select=\"@tags\"/></p></xsl:for-each></blue>"
                    + "<red><xsl:for-each select=\"key('byTag','red')\">"
                    + "<p><xsl:value-of select=\"@tags\"/></p></xsl:for-each></red>"
                    + "</out></xsl:template>"),
                input);
        }

        [TestMethod]
        public void KeyCanMatchAttributesAndUseTheParent()
        {
            AssertMatchesReference(
                Sheet("<xsl:key name=\"idAttr\" match=\"@id\" use=\".\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<n><xsl:value-of select=\"count(key('idAttr','a'))\"/></n>"
                    + "<owner><xsl:value-of select=\"name(key('idAttr','a')/..)\"/></owner>"
                    + "</out></xsl:template>"),
                Catalogue);
        }

        [TestMethod]
        public void KeyIsUsableInAPatternPredicateAndInATemplateMatch()
        {
            AssertMatchesReference(
                Sheet("<xsl:key name=\"itemByCat\" match=\"item\" use=\"@cat\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"
                    + "<xsl:template match=\"item[@id = '2']\"><two/></xsl:template>"
                    + "<xsl:template match=\"item\"><other id=\"{@id}\"/></xsl:template>"),
                Catalogue);
        }

        [TestMethod]
        public void GroupingByKeyWorks()
        {
            // The Muenchian grouping idiom, which is the main reason keys exist in XSLT 1.0.
            AssertMatchesReference(
                Sheet("<xsl:key name=\"itemByCat\" match=\"item\" use=\"@cat\"/>"
                    + "<xsl:template match=\"/\"><groups>"
                    + "<xsl:for-each select=\"//item[generate-id() = generate-id(key('itemByCat',@cat)[1])]\">"
                    + "<group cat=\"{@cat}\" n=\"{count(key('itemByCat',@cat))}\"/>"
                    + "</xsl:for-each>"
                    + "</groups></xsl:template>"),
                Catalogue);
        }

        [TestMethod]
        public void AnUndeclaredKeyIsReportedAtCompileTime()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(Sheet(
                    "<xsl:template match=\"/\"><xsl:value-of select=\"key('nope','x')\"/></xsl:template>")));

            StringAssert.Contains(error.Message, "nope");
        }

        [TestMethod]
        public void KeyRequiresTwoArguments()
        {
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(() => new Xslt(Sheet(
                    "<xsl:key name=\"k\" match=\"item\" use=\"@cat\"/>"
                    + "<xsl:template match=\"/\"><xsl:value-of select=\"key('k')\"/></xsl:template>")))
                    .Message,
                "key()");
        }

        [TestMethod]
        public void IndexIsBuiltOncePerDocumentAndReusedAcrossLookups()
        {
            // Correctness check for the caching: repeated lookups must keep returning the same nodes, and a
            // second document transformed by the same stylesheet must get its own index.
            Xslt stylesheet = Compile(Sheet(
                "<xsl:key name=\"itemByCat\" match=\"item\" use=\"@cat\"/>"
                + "<xsl:template match=\"/\"><out>"
                + "<a><xsl:value-of select=\"count(key('itemByCat','a'))\"/></a>"
                + "<b><xsl:value-of select=\"count(key('itemByCat','a'))\"/></b>"
                + "</out></xsl:template>"));

            Assert.AreEqual("<out><a>2</a><b>2</b></out>", stylesheet.TransformXml(Catalogue));

            string other = "<data><items><item id=\"9\" cat=\"a\"/></items></data>";
            Assert.AreEqual("<out><a>1</a><b>1</b></out>", stylesheet.TransformXml(other));
        }

        /// <remarks>
        /// A pattern may be anchored on a key — <c>IdKeyPattern</c> has been in the grammar since XSLT 1.0 —
        /// and this engine had never parsed one. That makes <c>XslCompiledTransform</c> the oracle for these,
        /// which is worth having: the matching rule is subtle enough that a reading of the specification is
        /// not the same as a second opinion.
        /// </remarks>
        [TestMethod]
        public void APatternMayBeAnchoredOnAKey()
        {
            AssertMatchesReference(
                Sheet("<xsl:key name=\"catById\" match=\"cat\" use=\"@id\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//cat\"/></out></xsl:template>"
                    + "<xsl:template match=\"key('catById', 'a')\">[alpha]</xsl:template>"
                    + "<xsl:template match=\"cat\">[other]</xsl:template>"),
                Catalogue);
        }

        [TestMethod]
        public void StepsMayHangFromAKeyAnchor()
        {
            AssertMatchesReference(
                Sheet("<xsl:key name=\"itemByCat\" match=\"item\" use=\"@cat\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//@id\"/></out></xsl:template>"
                    + "<xsl:template match=\"key('itemByCat', 'a')/@id\">[a:<xsl:value-of select=\".\"/>]</xsl:template>"
                    + "<xsl:template match=\"@id\">[other:<xsl:value-of select=\".\"/>]</xsl:template>"),
                Catalogue);
        }

        [TestMethod]
        public void AKeyAnchorReachesAnyDescendantWhenWrittenWithTwoSlashes()
        {
            AssertMatchesReference(
                Sheet("<xsl:key name=\"byName\" match=\"data\" use=\"@name\"/>"
                    + "<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//item\"/></out></xsl:template>"
                    + "<xsl:template match=\"key('byName', 'x')//item\">[deep]</xsl:template>"
                    + "<xsl:template match=\"item\">[shallow]</xsl:template>"),
                "<data name=\"x\"><items><item id=\"1\"/></items></data>");
        }

        [TestMethod]
        public void AnElementMayStillBeCalledKey()
        {
            // 'key' is only a key pattern when a parenthesis follows it, the same rule that already lets an
            // element be called 'div'.
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out><xsl:apply-templates select=\"//key\"/></out></xsl:template>"
                    + "<xsl:template match=\"key\">[element]</xsl:template>"),
                "<data><key/></data>");
        }

        [TestMethod]
        public void TheValueAKeyPatternLooksUpMustBeWrittenOutRatherThanComputed()
        {
            // XSLT restricts it to a literal or a variable: a pattern says which nodes it describes, and it
            // may not say so in terms of where the transformation has got to.
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Compile(Sheet(
                "<xsl:key name=\"k\" match=\"item\" use=\"@cat\"/>"
                + "<xsl:template match=\"key('k', concat('a', 'b'))\">x</xsl:template>")));

            Assert.AreEqual("XTSE0340", error.Code);
        }

        [TestMethod]
        public void APatternThatDoesNotFitTheGrammarIsAStaticErrorInTheStylesheet()
        {
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Compile(Sheet(
                "<xsl:template match=\"name/1223\">x</xsl:template>")));

            Assert.AreEqual("XTSE0340", error.Code);
        }

        [TestMethod]
        public void AKeyDefinedInTermsOfItselfIsRefusedRatherThanFollowed()
        {
            // Indexing the document asks the use expression for a value, which asks for the key being built,
            // which starts indexing again. Nothing stops that on its own: the recursion is not through a
            // template, so the call-depth limit never sees it, and it ends as a stack overflow — which cannot
            // be caught, so the process goes with it.
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Compile(Sheet(
                "<xsl:key name=\"self\" match=\"item\" use=\"key('self', @cat)/@id\"/>"
                + "<xsl:template match=\"/\">"
                + "<out><xsl:value-of select=\"count(key('self','a'))\"/></out>"
                + "</xsl:template>")).TransformXml(Catalogue));

            Assert.AreEqual("XTDE0640", error.Code);
        }

        [TestMethod]
        public void TwoKeysDefinedInTermsOfEachOtherAreRefusedToo()
        {
            // The same cycle a step longer. Catching it needs the key being built to be remembered rather
            // than the one being asked for, which is why the guard is a flag per key and not a check at the
            // call site.
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Compile(Sheet(
                "<xsl:key name=\"one\" match=\"item\" use=\"key('two', @cat)/@id\"/>"
                + "<xsl:key name=\"two\" match=\"cat\" use=\"key('one', @id)/@cat\"/>"
                + "<xsl:template match=\"/\">"
                + "<out><xsl:value-of select=\"count(key('one','a'))\"/></out>"
                + "</xsl:template>")).TransformXml(Catalogue));

            Assert.AreEqual("XTDE0640", error.Code);
        }

        [TestMethod]
        public void OneKeyMayStillBeDefinedInTermsOfAnother()
        {
            // The guard has to be lifted once a key is built, or a key that legitimately reads another would
            // be refused the moment anything asked for it twice.
            Assert.AreEqual(
                "<out><ids>1 3</ids><again>1 3</again></out>",
                Run(Sheet(
                    "<xsl:key name=\"catById\" match=\"cat\" use=\"@id\"/>"
                    + "<xsl:key name=\"itemByCatName\" match=\"item\" use=\"key('catById', @cat)/@name\"/>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<ids><xsl:for-each select=\"key('itemByCatName','Alpha')\">"
                    + "<xsl:if test=\"position()>1\"><xsl:text> </xsl:text></xsl:if>"
                    + "<xsl:value-of select=\"@id\"/></xsl:for-each></ids>"
                    + "<again><xsl:for-each select=\"key('itemByCatName','Alpha')\">"
                    + "<xsl:if test=\"position()>1\"><xsl:text> </xsl:text></xsl:if>"
                    + "<xsl:value-of select=\"@id\"/></xsl:for-each></again>"
                    + "</out></xsl:template>"),
                    Catalogue));
        }
    }
}
