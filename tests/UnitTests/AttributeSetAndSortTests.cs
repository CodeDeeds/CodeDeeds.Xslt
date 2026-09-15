using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>xsl:attribute-set</c>, and for the <c>xsl:sort</c> and <c>xsl:number</c> attributes beyond
    /// the basics.
    /// </summary>
    [TestClass]
    public sealed class AttributeSetAndSortTests
    {
        private sealed class MapResolver : IXsltResolver
        {
            private readonly Dictionary<string, string> m_modules = new(StringComparer.Ordinal);

            public MapResolver Add(string name, string text)
            {
                m_modules[name] = text;
                return this;
            }

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return m_modules.TryGetValue(href, out string? text)
                    ? new ResolvedResource(new StringReader(text), href)
                    : null;
            }
        }

        private static string Sheet(string body)
        {
            return "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + body
                + "</xsl:stylesheet>";
        }

        private static string Run(string stylesheet, string input, IXsltResolver? resolver = null)
        {
            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                StylesheetResolver = resolver,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml(input);

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

            Assert.AreEqual(
                Normalize(output.ToString()),
                Normalize(Run(stylesheet, input)),
                "output differed from XslCompiledTransform");
        }

        private static string Normalize(string fragment)
        {
            return XmlComparison.Normalize(fragment);
        }

        // ---- xsl:attribute-set ---------------------------------------------------------------------------

        [TestMethod]
        public void AttributeSetAppliesToLiteralResultElements()
        {
            AssertMatchesReference(
                Sheet("<xsl:attribute-set name=\"box\">"
                    + "<xsl:attribute name=\"border\">1</xsl:attribute>"
                    + "<xsl:attribute name=\"pad\">4</xsl:attribute>"
                    + "</xsl:attribute-set>"
                    + "<xsl:template match=\"/\"><table xsl:use-attribute-sets=\"box\"/></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void AttributeSetAppliesToElementAndCopy()
        {
            AssertMatchesReference(
                Sheet("<xsl:attribute-set name=\"box\"><xsl:attribute name=\"border\">1</xsl:attribute>"
                    + "</xsl:attribute-set>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:element name=\"made\" use-attribute-sets=\"box\"/>"
                    + "<xsl:for-each select=\"/r\"><xsl:copy use-attribute-sets=\"box\"/></xsl:for-each>"
                    + "</out></xsl:template>"),
                "<r keep=\"yes\"/>");
        }

        [TestMethod]
        public void AnElementsOwnAttributesOverrideTheSet()
        {
            AssertMatchesReference(
                Sheet("<xsl:attribute-set name=\"box\">"
                    + "<xsl:attribute name=\"border\">1</xsl:attribute>"
                    + "<xsl:attribute name=\"pad\">4</xsl:attribute>"
                    + "</xsl:attribute-set>"
                    + "<xsl:template match=\"/\">"
                    + "<table xsl:use-attribute-sets=\"box\" border=\"9\"/></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void AttributeValuesAreEvaluatedAgainstTheCurrentNode()
        {
            AssertMatchesReference(
                Sheet("<xsl:attribute-set name=\"ident\">"
                    + "<xsl:attribute name=\"id\"><xsl:value-of select=\"@n\"/></xsl:attribute>"
                    + "</xsl:attribute-set>"
                    + "<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//i\"><cell xsl:use-attribute-sets=\"ident\"/></xsl:for-each>"
                    + "</out></xsl:template>"),
                "<r><i n=\"1\"/><i n=\"2\"/></r>");
        }

        [TestMethod]
        public void SeveralSetsApplyInOrderWithLaterOnesWinning()
        {
            AssertMatchesReference(
                Sheet("<xsl:attribute-set name=\"a\"><xsl:attribute name=\"k\">from-a</xsl:attribute>"
                    + "<xsl:attribute name=\"only-a\">1</xsl:attribute></xsl:attribute-set>"
                    + "<xsl:attribute-set name=\"b\"><xsl:attribute name=\"k\">from-b</xsl:attribute>"
                    + "</xsl:attribute-set>"
                    + "<xsl:template match=\"/\"><e xsl:use-attribute-sets=\"a b\"/></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void AttributeSetsCanDrawInOtherSets()
        {
            AssertMatchesReference(
                Sheet("<xsl:attribute-set name=\"base\"><xsl:attribute name=\"border\">1</xsl:attribute>"
                    + "</xsl:attribute-set>"
                    + "<xsl:attribute-set name=\"fancy\" use-attribute-sets=\"base\">"
                    + "<xsl:attribute name=\"shadow\">yes</xsl:attribute></xsl:attribute-set>"
                    + "<xsl:template match=\"/\"><e xsl:use-attribute-sets=\"fancy\"/></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void ASetUsingItselfIsReportedAtCompileTime()
        {
            // Left undetected this would recurse until the stack ran out, at run time and far from the cause.
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(() => new Xslt(Sheet(
                    "<xsl:attribute-set name=\"a\" use-attribute-sets=\"b\"/>"
                    + "<xsl:attribute-set name=\"b\" use-attribute-sets=\"a\"/>"
                    + "<xsl:template match=\"/\"><e xsl:use-attribute-sets=\"a\"/></xsl:template>"))).Message,
                "itself");
        }

        [TestMethod]
        public void DeclarationsOfOneNameAreMergedAcrossAnImport()
        {
            // The specification merges same-named sets rather than letting one replace the other, with the
            // higher precedence winning attribute by attribute.
            MapResolver resolver = new MapResolver().Add(
                "base.xsl",
                Sheet("<xsl:attribute-set name=\"box\">"
                    + "<xsl:attribute name=\"border\">imported</xsl:attribute>"
                    + "<xsl:attribute name=\"only-imported\">yes</xsl:attribute>"
                    + "</xsl:attribute-set>"));

            // The imported declaration runs first, then the local one overrides 'border' — which moves it to
            // the end — and adds its own.
            Assert.AreEqual(
                "<e only-imported=\"yes\" border=\"local\" only-local=\"yes\"/>",
                Run(Sheet("<xsl:import href=\"base.xsl\"/>"
                    + "<xsl:attribute-set name=\"box\">"
                    + "<xsl:attribute name=\"border\">local</xsl:attribute>"
                    + "<xsl:attribute name=\"only-local\">yes</xsl:attribute>"
                    + "</xsl:attribute-set>"
                    + "<xsl:template match=\"/\"><e xsl:use-attribute-sets=\"box\"/></xsl:template>"),
                    "<r/>",
                    resolver));
        }

        [TestMethod]
        public void AnAttributeSetMayOnlyContainAttributes()
        {
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(() => new Xslt(Sheet(
                    "<xsl:attribute-set name=\"a\"><wrong/></xsl:attribute-set>"
                    + "<xsl:template match=\"/\"><e/></xsl:template>"))).Message,
                "xsl:attribute");
        }

        // ---- xsl:sort lang and case-order ----------------------------------------------------------------

        [TestMethod]
        public void SortWithoutLangIsOrdinalAndSoIsMachineIndependent()
        {
            // Deliberately ordinal by default: collating by the machine's culture would make the same
            // stylesheet and input order differently on different machines.
            // Ordinal puts every capital before every lower-case letter, and "ape" before "apple" at the
            // third character.
            Assert.AreEqual(
                "<out><n>Zebra</n><n>ape</n><n>apple</n></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//w\"><xsl:sort select=\".\"/>"
                    + "<n><xsl:value-of select=\".\"/></n></xsl:for-each>"
                    + "</out></xsl:template>"),
                    "<r><w>apple</w><w>Zebra</w><w>ape</w></r>"));
        }

        [TestMethod]
        public void SortWithLangUsesThatLanguagesCollation()
        {
            // Norwegian places æ, ø and å after z; an ordinal sort would not.
            Assert.AreEqual(
                "<out><n>and</n><n>bok</n><n>zebra</n><n>ærlig</n><n>øy</n><n>ål</n></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//w\"><xsl:sort select=\".\" lang=\"nb-NO\"/>"
                    + "<n><xsl:value-of select=\".\"/></n></xsl:for-each>"
                    + "</out></xsl:template>"),
                    "<r><w>øy</w><w>bok</w><w>ål</w><w>and</w><w>ærlig</w><w>zebra</w></r>"));
        }

        [TestMethod]
        public void CaseOrderDecidesBetweenValuesThatDifferOnlyInCase()
        {
            const string Stylesheet =
                "<xsl:template match=\"/\"><out>"
                + "<xsl:for-each select=\"//w\"><xsl:sort select=\".\" lang=\"en\" case-order=\"{0}\"/>"
                + "<n><xsl:value-of select=\".\"/></n></xsl:for-each>"
                + "</out></xsl:template>";

            string input = "<r><w>b</w><w>A</w><w>a</w><w>B</w></r>";

            Assert.AreEqual(
                "<out><n>A</n><n>a</n><n>B</n><n>b</n></out>",
                Run(Sheet(string.Format(Stylesheet, "upper-first")), input));

            Assert.AreEqual(
                "<out><n>a</n><n>A</n><n>b</n><n>B</n></out>",
                Run(Sheet(string.Format(Stylesheet, "lower-first")), input));
        }

        [TestMethod]
        public void AnUnknownLanguageIsIgnoredRatherThanRejected()
        {
            // The specification's recovery for a language the processor does not support.
            Assert.AreEqual(
                "<out><n>a</n><n>b</n></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<xsl:for-each select=\"//w\"><xsl:sort select=\".\" lang=\"zz-ZZ-nonsense\"/>"
                    + "<n><xsl:value-of select=\".\"/></n></xsl:for-each>"
                    + "</out></xsl:template>"),
                    "<r><w>b</w><w>a</w></r>"));
        }

        [TestMethod]
        public void AnInvalidCaseOrderIsReported()
        {
            StringAssert.Contains(
                Assert.ThrowsExactly<XsltException>(() => new Xslt(Sheet(
                    "<xsl:template match=\"/\"><xsl:for-each select=\"//w\">"
                    + "<xsl:sort case-order=\"sideways\"/></xsl:for-each></xsl:template>"))).Message,
                "sideways");
        }

        // ---- xsl:number grouping and letter-value --------------------------------------------------------

        [TestMethod]
        public void NumberGroupsDigitsWhenBothSeparatorAndSizeAreGiven()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:number value=\"1234567\" grouping-separator=\",\" grouping-size=\"3\"/></a>"
                    + "<b><xsl:number value=\"1234567\" grouping-separator=\" \" grouping-size=\"3\"/></b>"
                    + "<c><xsl:number value=\"1234567\" grouping-separator=\",\" grouping-size=\"2\"/></c>"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void GroupingNeedsBothAttributesToTakeEffect()
        {
            AssertMatchesReference(
                Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:number value=\"1234567\" grouping-separator=\",\"/></a>"
                    + "<b><xsl:number value=\"1234567\" grouping-size=\"3\"/></b>"
                    + "</out></xsl:template>"),
                "<r/>");
        }

        [TestMethod]
        public void LetterValueChoosesBetweenAlphabeticAndRomanForAmbiguousTokens()
        {
            // The token "i" could mean roman numerals or the letter sequence starting at i; letter-value says
            // which, and roman is the default.
            Assert.AreEqual(
                "<out><a>iv</a><b>l</b><c>IV</c></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<a><xsl:number value=\"4\" format=\"i\"/></a>"
                    + "<b><xsl:number value=\"4\" format=\"i\" letter-value=\"alphabetic\"/></b>"
                    + "<c><xsl:number value=\"4\" format=\"I\" letter-value=\"traditional\"/></c>"
                    + "</out></xsl:template>"),
                    "<r/>"));
        }

        [TestMethod]
        public void GroupingAppliesToCountedNumbersToo()
        {
            Assert.AreEqual(
                "<out><n>1,234</n></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<n><xsl:number value=\"count(//i) + 1233\" grouping-separator=\",\" "
                    + "grouping-size=\"3\"/></n>"
                    + "</out></xsl:template>"),
                    "<r><i/></r>"));
        }

        [TestMethod]
        public void AGroupingSeparatorMayBeOutsideTheBasicPlane()
        {
            // One character is not always one char. Taking the first half of a surrogate pair produced an
            // unpaired surrogate, which the writer refused outright rather than writing the character asked
            // for.
            Assert.AreEqual(
                "<out><n>1\U00010100234\U00010100567</n></out>",
                Run(Sheet("<xsl:template match=\"/\"><out>"
                    + "<n><xsl:number value=\"1234567\" grouping-separator=\"&#x10100;\" "
                    + "grouping-size=\"3\"/></n>"
                    + "</out></xsl:template>"),
                    "<r/>"));
        }

        // ---- A body that declares variables of its own ---------------------------------------------------

        [TestMethod]
        public void AnAttributeSetMayDeclareLocalVariables()
        {
            // Its body is a sequence constructor like any other, so it may. This used to index into whatever
            // frame the calling template had, and crashed where that frame was smaller.
            Assert.AreEqual(
                "<out><o a=\"1\"/></out>",
                Run(
                    Sheet("<xsl:attribute-set name=\"s\">"
                        + "<xsl:attribute name=\"a\">"
                        + "<xsl:variable name=\"v\" select=\"1\"/>"
                        + "<xsl:value-of select=\"$v\"/>"
                        + "</xsl:attribute></xsl:attribute-set>"
                        + "<xsl:template match=\"/\"><out>"
                        + "<o xsl:use-attribute-sets=\"s\"/>"
                        + "</out></xsl:template>"),
                    "<r/>"));
        }

        [TestMethod]
        public void AnAttributeSetDoesNotOverwriteTheCallersVariables()
        {
            // Which is why it needs a frame of its own rather than a larger shared one. Sharing is worse than
            // the crash where the caller's frame happens to be big enough: the set writes its own variable
            // over whatever the caller had at that slot, and the caller carries on with it.
            Assert.AreEqual(
                "<out><o a=\"inner\"/>caller</out>",
                Run(
                    Sheet("<xsl:attribute-set name=\"s\">"
                        + "<xsl:attribute name=\"a\">"
                        + "<xsl:variable name=\"v\" select=\"'inner'\"/>"
                        + "<xsl:value-of select=\"$v\"/>"
                        + "</xsl:attribute></xsl:attribute-set>"
                        + "<xsl:template match=\"/\"><out>"
                        + "<xsl:variable name=\"mine\" select=\"'caller'\"/>"
                        + "<o xsl:use-attribute-sets=\"s\"/>"
                        + "<xsl:value-of select=\"$mine\"/>"
                        + "</out></xsl:template>"),
                    "<r/>"));
        }

        [TestMethod]
        public void AGlobalVariableMayDeclareLocalVariables()
        {
            // The same gap in the same shape: a global's body is a sequence constructor too, and its frame
            // was never allocated at all.
            Assert.AreEqual(
                "<out>1+2</out>",
                Run(
                    Sheet("<xsl:variable name=\"pair\">"
                        + "<xsl:variable name=\"ps\" select=\"//p\"/>"
                        + "<xsl:value-of select=\"concat($ps[1], '+', $ps[2])\"/>"
                        + "</xsl:variable>"
                        + "<xsl:template match=\"/\"><out>"
                        + "<xsl:value-of select=\"$pair\"/>"
                        + "</out></xsl:template>"),
                    "<r><p>1</p><p>2</p></r>"));
        }

        [TestMethod]
        public void AGlobalParameterMayDeclareLocalVariablesToo()
        {
            Assert.AreEqual(
                "<out>2+3</out>",
                Run(
                    Sheet("<xsl:param name=\"pair\">"
                        + "<xsl:variable name=\"ps\" select=\"//p\"/>"
                        + "<xsl:value-of select=\"concat($ps[2], '+', $ps[3])\"/>"
                        + "</xsl:param>"
                        + "<xsl:template match=\"/\"><out>"
                        + "<xsl:value-of select=\"$pair\"/>"
                        + "</out></xsl:template>"),
                    "<r><p>1</p><p>2</p><p>3</p></r>"));
        }

        // ---- What a sort compares, and how it is told to -------------------------------------------------

        private const string Xsl2 =
            "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:f=\"urn:f\" exclude-result-prefixes=\"xs f\">";

        [TestMethod]
        public void ASortKeyIsComparedByWhatItIs()
        {
            // From 2.0 a number is a number without data-type saying so: 3 before 10. Backwards
            // compatibility does not put that back: XSLT 2.0 §13.1.2 keeps data-type as the one way to ask
            // for 1.0's ordering, and the mode's whole effect on a sort is elsewhere. So the same key at
            // version 1.0 answers the same, and it takes data-type="text" to put 10 first.
            const string Body =
                "<xsl:template match=\"/\"><out><xsl:for-each select=\"/r/i\">"
                + "<xsl:sort select=\"number(.)\"/><xsl:value-of select=\".\"/>,</xsl:for-each></out>"
                + "</xsl:template></xsl:stylesheet>";
            const string Input = "<r><i>10</i><i>3</i></r>";

            Assert.AreEqual("<out>3,10,</out>", Run(Xsl2 + Body, Input));
            Assert.AreEqual("<out>3,10,</out>", Run(Sheet(string.Empty).Replace("</xsl:stylesheet>", Body), Input));
            Assert.AreEqual(
                "<out>10,3,</out>",
                Run(Xsl2 + Body.Replace("number(.)\"/>", "number(.)\" data-type=\"text\"/>"), Input));

            // Two values that do not order against each other — an untyped attribute and a date — are
            // XTDE1030, where data-type="text" would have compared them as strings.
            XsltException error = Assert.ThrowsExactly<XsltException>(() => Run(
                Xsl2
                + "<xsl:template match=\"/\"><out><xsl:for-each select=\"(/r/i/@d, xs:date('2011-12-31'))\">"
                + "<xsl:sort select=\".\"/><xsl:value-of select=\".\"/></xsl:for-each></out></xsl:template>"
                + "</xsl:stylesheet>",
                "<r><i d=\"2017-01-05\"/></r>"));

            Assert.AreEqual("XTDE1030", error.Code);
        }

        [TestMethod]
        public void TheOrderingAttributesOfASortMayBeComputed()
        {
            // order, data-type, lang, case-order and collation are attribute value templates, settled once per
            // sort with the focus of the instruction that sorts. A computed value the attribute may not take
            // is XTDE0030; a written one was refused when the stylesheet was read, XTSE0020.
            string Sorted(string attributes) => Run(
                Xsl2
                + "<xsl:param name=\"o\" select=\"'descending'\"/>"
                + "<xsl:template match=\"/\"><out><xsl:for-each select=\"/r/i\">"
                + "<xsl:sort select=\"number(.)\" " + attributes + "/><xsl:value-of select=\".\"/>,"
                + "</xsl:for-each></out></xsl:template></xsl:stylesheet>",
                "<r><i>3</i><i>10</i><i>2</i></r>");

            Assert.AreEqual("<out>10,3,2,</out>", Sorted("order=\"{$o}\""));
            Assert.AreEqual("<out>10,2,3,</out>", Sorted("data-type=\"{'text'}\""));
            Assert.AreEqual(
                "XTDE0030", Assert.ThrowsExactly<XsltException>(() => Sorted("order=\"{'sideways'}\"")).Code);
            Assert.AreEqual(
                "XTSE0020", Assert.ThrowsExactly<XsltException>(() => Sorted("lang=\"'de'\"")).Code);
        }

        [TestMethod]
        public void ASortKeyMayBeContentAndAPerformSortMaySortItsContent()
        {
            // The key of an xsl:sort may be a sequence constructor, and so may what an xsl:perform-sort
            // sorts, its xsl:sort children aside.
            Assert.AreEqual(
                "<out>a,bb,ccc,</out>",
                Run(
                    Xsl2
                    + "<xsl:template match=\"/\"><out><xsl:for-each select=\"/r/i\">"
                    + "<xsl:sort><xsl:sequence select=\"string-length(.)\"/></xsl:sort>"
                    + "<xsl:value-of select=\".\"/>,</xsl:for-each></out></xsl:template></xsl:stylesheet>",
                    "<r><i>ccc</i><i>a</i><i>bb</i></r>"));

            Assert.AreEqual(
                "<out>1 2 3</out>",
                Run(
                    Xsl2
                    + "<xsl:template match=\"/\"><out><xsl:perform-sort><xsl:sort select=\".\"/>"
                    + "<xsl:sequence select=\"3, 1, 2\"/></xsl:perform-sort></out></xsl:template></xsl:stylesheet>",
                    "<r/>"));
        }

        [TestMethod]
        public void AFunctionWithoutADeclaredTypeReturnsTheSequenceItsBodyMade()
        {
            // as="item()*" is the default: the body's items, not the text of a tree built around them —
            // which is what lets a function sort a sequence and hand it back for xsl:value-of to separate.
            // This engine had been building a tree, and every such function returned one text node.
            Assert.AreEqual(
                "<out>1,2,3</out>",
                Run(
                    Xsl2
                    + "<xsl:function name=\"f:sorted\"><xsl:param name=\"p\"/>"
                    + "<xsl:perform-sort select=\"$p\"><xsl:sort select=\".\"/></xsl:perform-sort></xsl:function>"
                    + "<xsl:template match=\"/\"><out><xsl:value-of select=\"f:sorted((3, 1, 2))\" separator=\",\"/>"
                    + "</out></xsl:template></xsl:stylesheet>",
                    "<r/>"));
        }
    }
}
