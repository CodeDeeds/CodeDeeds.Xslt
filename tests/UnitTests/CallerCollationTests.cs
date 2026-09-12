namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for collations of the caller's own: an <see cref="XsltCollation"/> supplied under a URI
    /// through <see cref="XsltOptions.CollationResolver"/>, and what in a stylesheet then reaches it.
    /// </summary>
    [TestClass]
    public sealed class CallerCollationTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private const string CaseBlind = "urn:test:caseblind";
        private const string Reversed = "urn:test:reversed";
        private const string Input = "<r><i>b</i><i>A</i><i>a</i><i>B</i></r>";

        /// <summary>Case does not tell two strings apart; makes a key; matches substrings.</summary>
        private sealed class CaseBlindCollation : XsltCollation
        {
            public override int Compare(string first, string second) =>
                string.Compare(first, second, StringComparison.OrdinalIgnoreCase);

            public override string? Key(string value) => value.ToUpperInvariant();

            public override bool SupportsSubstringMatching => true;

            public override bool StartsWith(string subject, string prefix) =>
                subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            public override bool EndsWith(string subject, string suffix) =>
                subject.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

            public override int IndexOf(string subject, string sought, out int length)
            {
                length = sought.Length;
                return subject.IndexOf(sought, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Resolves the two test URIs, and counts how often it is asked.</summary>
        private sealed class Collations : IXsltCollationResolver
        {
            public int Asked { get; private set; }

            public XsltCollation? Resolve(string uri)
            {
                Asked++;

                return uri switch
                {
                    CaseBlind => new CaseBlindCollation(),
                    // Ordinal order reversed: compares and nothing else, being built over a comparer.
                    Reversed => XsltCollation.FromComparer(
                        Comparer<string>.Create((a, b) => string.CompareOrdinal(b, a))),
                    _ => null,
                };
            }
        }

        private static string Sheet(string body, string declarations = "", string? defaultCollation = null)
        {
            return $"<xsl:stylesheet version=\"3.0\" {Xsl}"
                + (defaultCollation is null ? string.Empty : $" default-collation=\"{defaultCollation}\"")
                + ">" + declarations
                + $"<xsl:template match=\"/\"><out>{body}</out></xsl:template></xsl:stylesheet>";
        }

        private static XsltOptions Options(XsltBackend backend, IXsltCollationResolver? collations)
        {
            return new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                CollationResolver = collations,
            };
        }

        /// <summary>Runs on both backends, which must agree, and returns what the out element holds.</summary>
        private static string Both(string stylesheet, IXsltCollationResolver? collations, string input = Input)
        {
            string interpreted = new Xslt(stylesheet, Options(XsltBackend.Interpreted, collations)).TransformXml(input);
            string compiled = new Xslt(stylesheet, Options(XsltBackend.Compiled, collations)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        private static string Fails(string stylesheet, IXsltCollationResolver? collations, string input = Input)
        {
            string? interpreted = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, Options(XsltBackend.Interpreted, collations)).TransformXml(input)).Code;
            string? compiled = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, Options(XsltBackend.Compiled, collations)).TransformXml(input)).Code;

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted ?? string.Empty;
        }

        private static string Value(string expression)
        {
            return $"<xsl:value-of select=\"{expression}\" separator=\",\"/>";
        }

        [TestMethod]
        public void TheFunctionsReachACollationOfTheCallersOwn()
        {
            Collations collations = new Collations();

            // Reversed, 'a' comes after 'b', so compare() answers 1.
            Assert.AreEqual(
                "0,true,true,x,1,2",
                Both(
                    Sheet(Value(
                        $"compare('abc', 'ABC', '{CaseBlind}'), "
                        + $"contains('xABCx', 'abc', '{CaseBlind}'), starts-with('ABC', 'ab', '{CaseBlind}'), "
                        + $"substring-after('xABCx', 'abc', '{CaseBlind}'), "
                        + $"compare('a', 'b', '{Reversed}'), count(distinct-values(('a', 'A', 'b'), '{CaseBlind}'))")),
                    collations));
        }

        [TestMethod]
        public void SortingGroupingKeysAndMergeUseIt()
        {
            Collations collations = new Collations();

            // Sorted case-blind, the sort is stable within each pair; grouped case-blind, four items are two
            // groups in population order; keyed case-blind, a lookup in either case finds both.
            Assert.AreEqual(
                "A,a,b,B|b,B;A,a;|A,a",
                Both(
                    Sheet(
                        $"<xsl:for-each select=\"/r/i\"><xsl:sort select=\".\" collation=\"{CaseBlind}\"/>"
                        + "<xsl:if test=\"position() gt 1\">,</xsl:if><xsl:value-of select=\".\"/></xsl:for-each>"
                        + "<xsl:text>|</xsl:text>"
                        + $"<xsl:for-each-group select=\"/r/i\" group-by=\".\" collation=\"{CaseBlind}\">"
                        + "<xsl:value-of select=\"current-group()\" separator=\",\"/><xsl:text>;</xsl:text></xsl:for-each-group>"
                        + "<xsl:text>|</xsl:text>"
                        + "<xsl:value-of select=\"key('k', 'a')\" separator=\",\"/>",
                        declarations: $"<xsl:key name=\"k\" match=\"i\" use=\".\" collation=\"{CaseBlind}\"/>"),
                    collations));

            // The reversed comparer orders a sort backwards, and a merge key takes a collation as a sort
            // key does: case-blind, A and a are one group.
            Assert.AreEqual(
                "b,a,B,A|A,a;b,B;",
                Both(
                    Sheet(
                        $"<xsl:for-each select=\"/r/i\"><xsl:sort select=\".\" collation=\"{Reversed}\"/>"
                        + "<xsl:if test=\"position() gt 1\">,</xsl:if><xsl:value-of select=\".\"/></xsl:for-each>"
                        + "<xsl:text>|</xsl:text>"
                        + "<xsl:merge><xsl:merge-source select=\"/r/i\" sort-before-merge=\"yes\">"
                        + $"<xsl:merge-key select=\".\" collation=\"{CaseBlind}\"/>"
                        + "</xsl:merge-source><xsl:merge-action>"
                        + "<xsl:value-of select=\"current-merge-group()\" separator=\",\"/><xsl:text>;</xsl:text>"
                        + "</xsl:merge-action></xsl:merge>"),
                    collations));
        }

        [TestMethod]
        public void ADefaultCollationMayBeTheCallers()
        {
            Collations collations = new Collations();

            // In scope for the comparison operators, the functions and a sort that names none, which is
            // then stable within each case-blind pair.
            Assert.AreEqual(
                "true,true,A,a,b,B",
                Both(
                    Sheet(
                        Value("'abc' = 'ABC', contains('xABCx', 'abc')")
                        + "<xsl:text>,</xsl:text>"
                        + "<xsl:for-each select=\"/r/i\"><xsl:sort select=\".\"/><xsl:value-of select=\".\"/>"
                        + "<xsl:if test=\"position() lt last()\">,</xsl:if></xsl:for-each>",
                        defaultCollation: CaseBlind),
                    collations));

            // The resolver is asked once per URI, however often the stylesheet asks.
            Assert.AreEqual(1, collations.Asked);
        }

        [TestMethod]
        public void WhatACollationCannotDoIsRefusedWithACode()
        {
            Collations collations = new Collations();

            // Built over a comparer alone: it compares and sorts, and nothing else.
            Assert.AreEqual("1", Both(Sheet(Value($"compare('a', 'b', '{Reversed}')")), collations));
            Assert.AreEqual("FOCH0004", Fails(Sheet(Value($"contains('ab', 'a', '{Reversed}')")), collations));
            Assert.AreEqual("FOCH0004", Fails(Sheet(Value($"distinct-values(('a', 'b'), '{Reversed}')")), collations));
            Assert.AreEqual("FOCH0004", Fails(Sheet(Value($"collation-key('a', '{Reversed}')")), collations));
            Assert.AreEqual(
                "FOCH0004",
                Fails(
                    Sheet($"<xsl:for-each-group select=\"/r/i\" group-by=\".\" collation=\"{Reversed}\"><g/></xsl:for-each-group>"),
                    collations));

            // A URI neither the engine nor the resolver has, with the code of whatever asked.
            Assert.AreEqual("FOCH0002", Fails(Sheet(Value("compare('a', 'b', 'urn:test:nobody')")), collations));
            Assert.AreEqual(
                "XTSE1210",
                Fails(
                    Sheet(Value("1"), declarations: "<xsl:key name=\"k\" match=\"i\" use=\".\" collation=\"urn:test:nobody\"/>"),
                    collations));
            Assert.AreEqual(
                "XTDE1035",
                Fails(
                    Sheet("<xsl:for-each select=\"/r/i\"><xsl:sort select=\".\" collation=\"urn:test:nobody\"/></xsl:for-each>"),
                    collations));

            // And with no resolver at all, the caller's URI is nobody's.
            Assert.AreEqual("FOCH0002", Fails(Sheet(Value($"compare('a', 'b', '{CaseBlind}')")), null));
        }

        [TestMethod]
        public void TwoDeclarationsOfOneKeyFileUnderOneCollation()
        {
            Collations collations = new Collations();

            Assert.AreEqual(
                "XTSE1220",
                Fails(
                    Sheet(
                        Value("1"),
                        declarations:
                            $"<xsl:key name=\"k\" match=\"i\" use=\".\" collation=\"{CaseBlind}\"/>"
                            + "<xsl:key name=\"k\" match=\"r\" use=\"'x'\"/>"),
                    collations));
        }

        [TestMethod]
        public void ACollationKeyOfTheCallersOwnAnswersEquality()
        {
            Collations collations = new Collations();

            Assert.AreEqual(
                "true,false",
                Both(
                    Sheet(Value(
                        $"collation-key('abc', '{CaseBlind}') eq collation-key('ABC', '{CaseBlind}'), "
                        + $"collation-key('abc', '{CaseBlind}') eq collation-key('abd', '{CaseBlind}')")),
                    collations));
        }

        [TestMethod]
        public void ATransformationStartedByTheStylesheetInheritsTheResolver()
        {
            Collations collations = new Collations();

            string stylesheet = Sheet(
                Value("transform(map{'stylesheet-text': $sheet, 'initial-template': xs:QName('xsl:initial-template')})?output/v/string()"),
                declarations:
                    "<xsl:variable name=\"sheet\" as=\"xs:string\"><![CDATA["
                    + "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                    + "<xsl:template name=\"xsl:initial-template\">"
                    + $"<v><xsl:value-of select=\"compare('a', 'A', '{CaseBlind}')\"/></v>"
                    + "</xsl:template></xsl:stylesheet>"
                    + "]]></xsl:variable>");

            Assert.AreEqual("0", Both(stylesheet, collations));
        }

        [TestMethod]
        public void AnEvaluatedExpressionSeesItToo()
        {
            Collations collations = new Collations();

            Assert.AreEqual(
                "0",
                Both(
                    Sheet($"<xsl:evaluate xpath=\"'compare(''a'', ''A'', ''{CaseBlind}'')'\"/>"),
                    collations));
        }
    }
}
