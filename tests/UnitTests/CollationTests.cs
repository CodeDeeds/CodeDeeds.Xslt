namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the collations a string function may be given: the code point one every processor must
    /// have, the ASCII case-insensitive one XPath 3.1 adds, and the UCA collation URI with its parameters.
    /// </summary>
    /// <remarks>
    /// Written as one file rather than spread across the functions because a collation is one thing reaching
    /// into a dozen of them, and what is being tested is the collation rather than the function.
    /// </remarks>
    [TestClass]
    public sealed class CollationTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" exclude-result-prefixes=\"xs\"";

        private const string Codepoint = "http://www.w3.org/2005/xpath-functions/collation/codepoint";
        private const string AsciiCaseBlind =
            "http://www.w3.org/2005/xpath-functions/collation/html-ascii-case-insensitive";

        private const string Primary = "http://www.w3.org/2013/collation/UCA?lang=en;strength=primary";
        private const string Secondary = "http://www.w3.org/2013/collation/UCA?lang=en;strength=secondary";
        private const string Tertiary = "http://www.w3.org/2013/collation/UCA?lang=en;strength=tertiary";

        private static string Writes(string expression, string version = "3.0")
        {
            string stylesheet = $"<xsl:stylesheet version=\"{version}\" {Xsl}>"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) =>
                new XsltOptions { Backend = backend, OmitXmlDeclaration = true };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        [TestMethod]
        public void TheCodePointCollationComparesByCodePointAndNotByUtf16Unit()
        {
            // U+10001 is written as the surrogate pair D800 DC01, so comparing the units puts it before
            // U+FFF0 — which is the wrong way round, and the one thing this collation is required to get
            // right. Every other comparison in the language rests on it.
            Assert.AreEqual(
                "1",
                Writes("compare(codepoints-to-string(65537), codepoints-to-string(65520))"));
            Assert.AreEqual(
                "-1",
                Writes("compare(codepoints-to-string(65520), codepoints-to-string(65537))"));
            Assert.AreEqual(
                "0",
                Writes("compare(codepoints-to-string(65537), codepoints-to-string(65537))"));

            // And it is the default, so naming it explicitly changes nothing.
            Assert.AreEqual(
                "1",
                Writes($"compare(codepoints-to-string(65537), codepoints-to-string(65520), '{Codepoint}')"));
        }

        [TestMethod]
        public void TheUcaCollationHonoursItsStrength()
        {
            // Primary weighs base letters only, so case and accents fall away; secondary brings the accents
            // back; tertiary brings the case back too.
            Assert.AreEqual("0", Writes($"compare('database', 'DATABASE', '{Primary}')"));
            Assert.AreEqual("0", Writes($"compare('database', 'dÃtabase', '{Primary}')"));

            Assert.AreEqual("0", Writes($"compare('database', 'DATABASE', '{Secondary}')"));
            Assert.AreNotEqual("0", Writes($"compare('database', 'dÃtabase', '{Secondary}')"));

            Assert.AreNotEqual("0", Writes($"compare('database', 'DATABASE', '{Tertiary}')"));

            // A difference in the letters themselves outweighs every strength.
            Assert.AreEqual("-1", Writes($"compare('database', 'Databases', '{Primary}')"));
            Assert.AreEqual("1", Writes($"compare('databases', 'Database', '{Secondary}')"));
        }

        [TestMethod]
        public void ACollationReachesTheSubstringFunctionsAsWellAsCompare()
        {
            Assert.AreEqual("true", Writes($"starts-with('database', 'DATA', '{Primary}')"));
            Assert.AreEqual("false", Writes($"starts-with('banana', 'ana', '{Primary}')"));
            Assert.AreEqual("true", Writes($"ends-with('database', 'BASE', '{Primary}')"));
            Assert.AreEqual("true", Writes($"contains('database', 'ATAB', '{Primary}')"));
            Assert.AreEqual("da", Writes($"substring-before('database', 'TAB', '{Primary}')"));
            Assert.AreEqual("ase", Writes($"substring-after('database', 'TAB', '{Primary}')"));
        }

        [TestMethod]
        public void SubstringAfterResumesFromWhatMatchedRatherThanFromWhatWasSought()
        {
            // Four characters matching five is the case that tells the two apart, and it can only happen
            // under a collation that weighs some difference as nothing.
            Assert.AreEqual("se", Writes($"substring-after('dâtabase', 'ba', '{Primary}')"));
            Assert.AreEqual("dâta", Writes($"substring-before('dâtabase', 'ba', '{Primary}')"));
        }

        [TestMethod]
        public void TheAsciiCollationFoldsAsciiLettersAndNothingElse()
        {
            Assert.AreEqual("0", Writes($"compare('Database', 'dataBASE', '{AsciiCaseBlind}')"));
            Assert.AreEqual("true", Writes($"starts-with('Database', 'DATA', '{AsciiCaseBlind}')"));

            // Not the invariant culture's idea of case: an accented letter is left alone, so these two are
            // different strings where a culture-aware fold would call them one.
            Assert.AreNotEqual("0", Writes($"compare('Été', 'été', '{AsciiCaseBlind}')"));
        }

        [TestMethod]
        public void ACollationReachesTheSequenceFunctionsToo()
        {
            Assert.AreEqual("1", Writes($"count(distinct-values(('a', 'A', 'A'), '{Primary}'))"));
            Assert.AreEqual(
                "2",
                Writes("count(distinct-values(('a', 'A', 'A')))"),
                "'A' twice is one value under any collation");

            Assert.AreEqual("1 2", Writes($"index-of(('a', 'A', 'b'), 'A', '{Primary}')"));
            Assert.AreEqual("true", Writes($"deep-equal(('a', 'b'), ('A', 'B'), '{Primary}')"));
            Assert.AreEqual("false", Writes("deep-equal(('a', 'b'), ('A', 'B'))"));

            Assert.AreEqual("B", Writes($"max(('a', 'B'), '{Primary}')"));
            Assert.AreEqual("a", Writes($"min(('a', 'B'), '{Primary}')"));
        }

        [TestMethod]
        public void ADurationIsNotOrderedAsIfItWereText()
        {
            // Durations, dates and QNames are all held as strings inside this engine, so a comparison that
            // asks 'is this a string?' rather than 'is this text?' orders PT10H before PT9H.
            Assert.AreEqual(
                "PT10H",
                Writes("max((xs:dayTimeDuration('PT9H'), xs:dayTimeDuration('PT10H')))"));
            Assert.AreEqual(
                "PT1H",
                Writes("min((xs:dayTimeDuration('PT1H'), xs:dayTimeDuration('PT10H')))"));
            Assert.AreEqual(
                "P10M",
                Writes("max((xs:yearMonthDuration('P9M'), xs:yearMonthDuration('P10M')))"));
        }

        [TestMethod]
        public void SortingTakesACollation()
        {
            Assert.AreEqual("A a B b", Writes($"sort(('B', 'b', 'A', 'a'), '{Primary}')"));

            // The sort is stable, so equal keys keep the order they came in — which is the only way that
            // first result can be read as saying anything about the collation.
            Assert.AreEqual("A B a b", Writes("sort(('B', 'b', 'A', 'a'))"));
        }

        [TestMethod]
        public void ContainsTokenSplitsOnXmlWhitespaceAndNoOther()
        {
            Assert.AreEqual("true", Writes("contains-token('abc def', 'abc')"));
            Assert.AreEqual("true", Writes("contains-token('abc&#9;def', 'def')"));

            // A no-break space holds a token together. .NET's own idea of whitespace would split here.
            Assert.AreEqual(
                "false",
                Writes("contains-token(codepoints-to-string((97, 98, 99, 160, 100, 101, 102)), 'abc')"));

            Assert.AreEqual("true", Writes($"contains-token('abc def', 'ABC', '{Primary}')"));
        }

        [TestMethod]
        public void ACollationKeyAnswersComparisonsWithoutTheCollation()
        {
            // Which is what the function is for: two keys are equal exactly when the collation calls the
            // strings equal, and order the same way, so a caller can sort or index by the key alone.
            Assert.AreEqual("true", Writes("collation-key('abc') eq collation-key('abc')"));
            Assert.AreEqual("false", Writes("collation-key('abc') eq collation-key('123')"));
            Assert.AreEqual("false", Writes("collation-key('abc') eq collation-key('ABC')"));

            Assert.AreEqual("true", Writes($"collation-key('abc', '{Primary}') eq collation-key('ABC', '{Primary}')"));
            Assert.AreEqual(
                "true",
                Writes($"collation-key('abc', '{AsciiCaseBlind}') eq collation-key('ABC', '{AsciiCaseBlind}')"));

            // And the keys order as the strings do, above the basic plane included — the code point
            // collation's key is UTF-8, whose byte order is code point order.
            Assert.AreEqual(
                "true",
                Writes("collation-key(codepoints-to-string((37, 65500, 37))) "
                    + "lt collation-key(codepoints-to-string((37, 100000, 37)))"));

            Assert.AreEqual(
                "true",
                Writes("(collation-key('abc') lt collation-key('ABC')) "
                    + "eq (compare('abc', 'ABC', default-collation()) lt 0)"));
        }

        [TestMethod]
        public void PunctuationWeighsNothingWhereTheCollationSaysItIsVariable()
        {
            // alternate=blanked, which .NET spells IgnoreSymbols.
            const string Blanked =
                "http://www.w3.org/2013/collation/UCA?lang=en;alternate=blanked;strength=primary";

            Assert.AreEqual("true", Writes($"contains('abcdefghi', '-d-e-f-', '{Blanked}')"));
            Assert.AreEqual("true", Writes($"starts-with('abcdefghi', '-a-b-c-', '{Blanked}')"));
            Assert.AreEqual("true", Writes($"ends-with('abcdefghi', '-g-h-i-', '{Blanked}')"));
            Assert.AreEqual("abc", Writes($"substring-before('abcdefghi', '--d-e-', '{Blanked}')"));
            Assert.AreEqual("fghi", Writes($"substring-after('abcdefghi', '--d-e-', '{Blanked}')"));

            // The match may begin past the start of the string and still be a prefix, because what stands in
            // front of it weighs nothing. ICU anchors on where the characters are; the specification anchors
            // on what they weigh.
            Assert.AreEqual("true", Writes($"starts-with('-abcdefghi', 'abc', '{Blanked}')"));
            Assert.AreEqual("true", Writes($"ends-with('abcdefghi-', 'ghi', '{Blanked}')"));

            // And a difference in the letters is still a difference.
            Assert.AreEqual("false", Writes($"starts-with('abcdefghi', '-b-c-', '{Blanked}')"));
        }

        [TestMethod]
        public void AnUnknownCollationIsRefusedRatherThanQuietlyIgnored()
        {
            foreach (string uri in new[]
            {
                "http://example.com/collation/danish",
                "http://www.w3.org/2013/collation/UCA?strength=upside-down",
                "http://www.w3.org/2013/collation/UCA?nonsense",
            })
            {
                XsltException error = Assert.ThrowsExactly<XsltException>(
                    () => Writes($"compare('a', 'b', '{uri}')"), uri);

                Assert.AreEqual("FOCH0002", error.Code, uri);
            }
        }

        [TestMethod]
        public void AParameterThatCannotBeHonouredIsAnErrorOnlyWhereFallbackIsRefused()
        {
            // The specification's simple fallback: a processor may ignore a parameter it does not implement,
            // unless the URI says not to. So the same unsupported parameter is silently dropped in one URI
            // and an error in the other, and that difference is the whole of what fallback means.
            const string Uca = "http://www.w3.org/2013/collation/UCA";

            Assert.AreEqual("0", Writes($"compare('a', 'a', '{Uca}?lang=en;alternate=shifted')"));

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => Writes($"compare('a', 'a', '{Uca}?lang=en;alternate=shifted;fallback=no')"));

            Assert.AreEqual("FOCH0002", error.Code);

            // A parameter that is honoured is no trouble either way.
            Assert.AreEqual("0", Writes($"compare('a', 'A', '{Uca}?strength=secondary;fallback=no')"));
        }

        [TestMethod]
        public void SortingTakesAnyCollationWhereGroupingStillRefusesOneItCannotUse()
        {
            // xsl:sort compares by whatever collation the processor has, the UCA one included: at primary
            // strength case does not tell a from A, so the two pairs keep the order they came in. It had
            // been refusing everything but the code point collation. xsl:for-each-group and xsl:key still
            // run through tables that carry none, and say so rather than order by code point regardless.
            string sorting =
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:template match=\"/\"><out><xsl:for-each select=\"/r/i\">"
                + $"<xsl:sort select=\".\" collation=\"{Primary}\"/><xsl:value-of select=\".\"/>"
                + "</xsl:for-each></out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>AabB</out>",
                new Xslt(sorting, new XsltOptions { OmitXmlDeclaration = true })
                    .TransformXml("<r><i>b</i><i>A</i><i>a</i><i>B</i></r>"));

            string grouping =
                $"<xsl:stylesheet version=\"2.0\" {Xsl}>"
                + "<xsl:template match=\"/\"><out><xsl:for-each-group select=\"/r/i\" group-by=\".\" "
                + $"collation=\"{Primary}\"/></out></xsl:template></xsl:stylesheet>";

            XsltException error = Assert.ThrowsExactly<XsltException>(() => new Xslt(grouping));
            Assert.AreEqual("XTDE1110", error.Code);

            // The code point collation is what they do, so naming it is accepted.
            _ = new Xslt(grouping.Replace(Primary, Codepoint));
        }

        // ---- What a default-collation reaches ---------------------------------------------------------------

        /// <summary>Runs a whole stylesheet body, optionally with a default-collation over all of it.</summary>
        private static string Runs(string body, string? collation = null)
        {
            string stylesheet = "<xsl:stylesheet version=\"3.0\" " + Xsl
                + (collation is null ? string.Empty : " default-collation=\"" + collation + "\"")
                + ">" + body + "</xsl:stylesheet>";

            XsltOptions options = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 };

            return new Xslt(stylesheet, options).TransformXml("<r/>");
        }

        /// <summary>Writes one expression under a default-collation.</summary>
        private static string Compares(string expression, string? collation = null)
        {
            return Runs(
                "<xsl:template match=\"/\"><out><xsl:value-of select=\"" + expression + "\"/></out>"
                + "</xsl:template>",
                collation);
        }

        [TestMethod]
        public void TheDefaultCollationReachesTheComparisonOperators()
        {
            // What every comparison in its scope compares by, and it had been reaching none of them. Case is
            // a tertiary difference, so at secondary strength two strings differing only in case are one.
            Assert.AreEqual("<out>false</out>", Compares("'GREEN23' = 'green23'"));
            Assert.AreEqual("<out>true</out>", Compares("'GREEN23' = 'green23'", Secondary));
            Assert.AreEqual("<out>true</out>", Compares("'GREEN23' eq 'green23'", Secondary));

            // And the ordering operators, which the collation decides as much as it decides equality: by
            // code point 'abc' comes after 'ABD', and at secondary strength it comes before it.
            Assert.AreEqual("<out>false</out>", Compares("'abc' lt 'ABD'"));
            Assert.AreEqual("<out>true</out>", Compares("'abc' lt 'ABD'", Secondary));
        }

        [TestMethod]
        public void TheDefaultCollationReachesTheStringFunctions()
        {
            Assert.AreEqual("<out>false</out>", Compares("starts-with('abc', 'AB')"));
            Assert.AreEqual("<out>true</out>", Compares("starts-with('abc', 'AB')", Secondary));
            Assert.AreEqual("<out>true</out>", Compares("contains('abc', 'B')", Secondary));
            Assert.AreEqual("<out>0</out>", Compares("compare('abc', 'ABc')", Secondary));
            Assert.AreEqual("<out>1</out>", Compares("count(distinct-values(('a', 'A')))", Secondary));

            // A collation the call names itself still wins over the one in scope.
            Assert.AreEqual(
                "<out>false</out>",
                Compares("starts-with('abc', 'AB', '" + Codepoint + "')", Secondary));
        }

        [TestMethod]
        public void TheDefaultCollationIsWhatDefaultCollationAnswers()
        {
            Assert.AreEqual("<out>" + Codepoint + "</out>", Compares("default-collation()"));
            Assert.AreEqual("<out>" + Secondary + "</out>", Compares("default-collation()", Secondary));
        }

        [TestMethod]
        public void TheNearestDefaultCollationInScopeDecides()
        {
            // Inherited down the stylesheet tree and overridable at any depth, which is what lets the same
            // comparison answer differently in two branches of one xsl:choose.
            Assert.AreEqual(
                "<out>Two</out>",
                Runs("<xsl:template match=\"/\"><out><xsl:choose>"
                    + "<xsl:when test=\"'GREEN23' = 'green23'\">One</xsl:when>"
                    + "<xsl:when test=\"'GREEN23' = 'green23'\" default-collation=\"" + Secondary + "\">"
                    + "Two</xsl:when>"
                    + "<xsl:otherwise>Fail</xsl:otherwise></xsl:choose></out></xsl:template>"));

            // The value is a list of candidates, most preferred first: a stylesheet may name a collation it
            // would like and one it can settle for, and the first this engine has is the one taken.
            Assert.AreEqual(
                "<out>true</out>",
                Compares("'abc' = 'ABC'", "urn:no-such-collation " + AsciiCaseBlind));
        }

        [TestMethod]
        public void TheDefaultCollationOrdersASortThatNamesNone()
        {
            // The collation attribute names one, a lang names one, and where neither does the default in
            // scope decides (§13.1.3). By code point 'B' comes before 'a'; by any of the UCA strengths it
            // does not.
            const string Body =
                "<xsl:template match=\"/\"><out><xsl:perform-sort select=\"('B', 'a')\">"
                + "<xsl:sort{0}/></xsl:perform-sort></out></xsl:template>";

            Assert.AreEqual("<out>B a</out>", Runs(Body.Replace("{0}", string.Empty)));
            Assert.AreEqual("<out>a B</out>", Runs(Body.Replace("{0}", string.Empty), Secondary));

            // And a collation the sort names itself still wins.
            Assert.AreEqual(
                "<out>B a</out>",
                Runs(Body.Replace("{0}", " collation=\"" + Codepoint + "\""), Secondary));
        }
    }
}
