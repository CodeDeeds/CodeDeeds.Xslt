namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the regular-expression language XPath has, against the one .NET has.
    /// </summary>
    /// <remarks>
    /// .NET's language is the larger of the two and reads several constructs differently, so a pattern is
    /// checked and rewritten before .NET sees it. Every case here is one where handing the pattern over
    /// unchanged gave a different answer, or an answer where XPath has none.
    /// </remarks>
    [TestClass]
    public sealed class RegexTests
    {
        private static string Writes(string expression, string version = "2.0")
        {
            const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

            string stylesheet = $"<xsl:stylesheet version=\"{version}\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            // The regular expression language available is the processor's, not the stylesheet's: version on
            // the stylesheet asks for backwards-compatible behaviour and not for a smaller language. So these
            // tests say which processor they are asking, as the 3.0 instruction tests do.
            XsltOptions options = new XsltOptions
            {
                OmitXmlDeclaration = true,
                Version = version == "3.0" ? XsltVersion.V30 : XsltVersion.V20,
            };

            string result = new Xslt(stylesheet, options).TransformXml("<r/>");

            return result == "<out/>" ? string.Empty : result["<out>".Length..^"</out>".Length];
        }

        private static string? CodeOf(string expression, string version = "2.0")
        {
            try
            {
                Writes(expression, version);
                return null;
            }
            catch (XsltException error)
            {
                return error.Code;
            }
        }

        // ---- What XPath refuses and .NET accepts ----------------------------------------------------------

        [TestMethod]
        [DataRow("matches('abcd', '(asd)[\\1]')")]
        [DataRow("matches('abcd', '(asd)[asd\\1]')")]
        [DataRow("matches('abcd', '(asd)[asd\\0]')")]
        public void ABackReferenceCannotAppearInACharacterClass(string expression)
        {
            // A class holds characters, not what was matched elsewhere, so .NET reads '\1' there as an
            // escape and finds a different pattern than the one that was written.
            Assert.AreEqual("FORX0002", CodeOf(expression));
        }

        [TestMethod]
        [DataRow("matches('aa', '(a\\1)')")]
        [DataRow("matches('abcd', '(a)\\2(b)')")]
        [DataRow("matches('#abc#1', '^((#)abc\\1)$')")]
        public void ABackReferenceNamesAGroupCompleteBeforeIt(string expression)
        {
            // A group cannot refer to itself, and one that comes later has captured nothing yet. The third
            // is the one worth having a test for: group 2 is complete, but \1 is inside group 1.
            Assert.AreEqual("FORX0002", CodeOf(expression));
        }

        [TestMethod]
        public void ABraceClosesAQuantifierOrIsEscaped()
        {
            // F&O §5.6.1 corrects XSD 1.0's grammar, which left the braces out of its metacharacters: a bare
            // '}' closes nothing and names nothing, and the character is written '\\}'.
            Assert.AreEqual("FORX0002", CodeOf("matches('a}', 'a}')"));
            Assert.IsNull(CodeOf("matches('a}', 'a\\}')"));
            Assert.IsNull(CodeOf("matches('a}', '[}]')"));
        }

        [TestMethod]
        public void TheNameEscapesFollowTheEditionTheirVersionReadsBy()
        {
            // \i and \c are XML's name characters: the fifth edition's ranges at 3.0, by way of XSD 1.1,
            // and the fourth edition's tables at 2.0, by way of XSD 1.0. The ohm sign and a hiragana letter
            // are name characters in both; the combining bridge above is one only in the fifth.
            Assert.AreEqual("true", Writes("matches('&#x2126;', '^\\i$')"));
            Assert.AreEqual("true", Writes("matches('&#x2126;', '^\\i$')", "3.0"));
            Assert.AreEqual("true", Writes("matches('&#x3041;', '^[\\c]$')"));
            Assert.AreEqual("false", Writes("matches('&#x346;', '^\\c$')"));
            Assert.AreEqual("true", Writes("matches('&#x346;', '^\\c$')", "3.0"));
            Assert.AreEqual("false", Writes("matches('[', '^[\\i]$')"));

            // The private use area is one block under XSD's name, in the basic plane and the two planes
            // kept for it; the tag characters beside them are not in it.
            Assert.AreEqual("true", Writes("matches('&#xE000;', '^\\p{IsPrivateUse}$')"));
            Assert.AreEqual("true", Writes("matches('&#x100000;', '^\\p{IsPrivateUse}+$')"));
            Assert.AreEqual("false", Writes("matches('&#xE007F;', '^\\p{IsPrivateUse}$')"));
        }

        [TestMethod]
        public void AHyphenInAClassIsARangeOrASubtraction()
        {
            // In '[0-9-.]' the hyphen follows a completed range and is not the start of a subtraction, so
            // XSD 1.1's grammar, which XPath 3.0 reads by, has no reading for it. XSD 1.0's, which 2.0 reads
            // by, let a hyphen stand for itself anywhere: '[a-a-x-x]' is two ranges with a hyphen between.
            Assert.AreEqual("FORX0002", CodeOf("matches('input', '[0-9-.]*/')", "3.0"));
            Assert.AreEqual("true", Writes("matches('a-x', '^[a-a-x-x]+$')"));
            Assert.AreEqual("false", Writes("matches('a-b', '^[a-a-x-x]+$')"));
            Assert.AreEqual("true", Writes("matches('-', '^[0-9-.]$')"));

            // The readings it does have are untouched.
            Assert.AreEqual("true", Writes("matches('m', '[a-z]')"));
            Assert.AreEqual("true", Writes("matches('-', '[-a]')"));
            Assert.AreEqual("true", Writes("matches('a', '[a-]')"));
            Assert.AreEqual("true", Writes("matches('b', '[a-z-[aeiou]]')"));
        }

        [TestMethod]
        [DataRow("replace('abracadabra', 'bra', '\\')")]
        [DataRow("replace('abracadabra', 'bra', '$y')")]
        [DataRow("replace('input', '(input)', 'invalid$')")]
        [DataRow("replace('input', 'in', 'invalid\\ ')")]
        [DataRow("replace('a a ', '(a )', 'group: \\1')")]
        public void AReplacementUsesDollarForAGroupAndBackslashForItself(string expression)
        {
            // XPath allows $N and a backslash before a backslash or a dollar, and nothing else. .NET would
            // read '$y' as a literal and '\1' as the digit 1, quietly producing something else.
            Assert.AreEqual("FORX0004", CodeOf(expression));
        }

        [TestMethod]
        [DataRow("replace('a', '', 'b')")]
        [DataRow("replace('abracadabra', '.*?', '$1')")]
        [DataRow("tokenize('abba', '.?')")]
        public void APatternThatMatchesNothingCannotSplitOrReplace(string expression)
        {
            // There would be a match at every position and between every pair of characters, so neither
            // function would move forward. fn:matches has no such difficulty and is not checked.
            Assert.AreEqual("FORX0003", CodeOf(expression));
            Assert.AreEqual("true", Writes("matches('a', '')"));
        }

        [TestMethod]
        [DataRow("matches('ab', '(?:a)b')")]
        [DataRow("matches('ab', '(?&lt;name>a)b')")]
        [DataRow("matches('ab', '(?=a)ab')")]
        [DataRow("matches('ab', '(?!x)ab')")]
        [DataRow("matches('ab', '(?&lt;=a)b')")]
        [DataRow("matches('ab', '(?>a)b')")]
        [DataRow("matches('AB', '(?i)ab')")]
        [DataRow("matches('ab', '(?#a comment)ab')")]
        public void AGroupIsAPlainParenthesisAndNothingElseBeforeThreePointZero(string expression)
        {
            // Non-capturing groups, lookahead and lookbehind, atomic groups, inline options and comments are
            // all .NET's. A pattern using one worked here and would fail on a conformant processor, which is
            // the quietest way to be wrong. 3.0 takes the first of them into the language; see below.
            Assert.AreEqual("FORX0002", CodeOf(expression));
        }

        // ---- What XPath 3.0 adds to the language -----------------------------------------------------------

        [TestMethod]
        public void TheLanguageAvailableIsTheProcessorsAndNotTheStylesheets()
        {
            // A version="2.0" stylesheet asks for backwards-compatible behaviour — how '<' compares, what
            // happens to a sequence where one item was wanted — and not for a smaller regular expression
            // language. So a 3.0 processor reads '(?:' in one, and the W3C suite says so directly: its whole
            // regex test set declares XSLT30+ and uses a version="2.0" stylesheet.
            string stylesheet =
                "<xsl:stylesheet version=\"2.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                + "<xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"matches('ab', '(?:a)b')\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<out>true</out>",
                new Xslt(
                    stylesheet,
                    new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 })
                    .TransformXml("<r/>"));

            // And the other way round: a processor that does not claim 3.0 refuses it however the stylesheet
            // is versioned, because a pattern this engine reads and a conformant 2.0 processor does not is
            // the quietest kind of difference there is.
            string asking30 = stylesheet.Replace("version=\"2.0\"", "version=\"3.0\"", StringComparison.Ordinal);

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(
                    asking30,
                    new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V20 })
                    .TransformXml("<r/>"));

            Assert.AreEqual("FORX0002", error.Code);
        }

        [TestMethod]
        public void ThreePointZeroAddsTheNonCapturingGroup()
        {
            Assert.AreEqual("true", Writes("matches('ab', '(?:a)b')", version: "3.0"));
            Assert.AreEqual("true", Writes("matches('abab', '^(?:ab)+$')", version: "3.0"));

            // Which is the point of it: a group that quantifies without also capturing.
            Assert.AreEqual("true", Writes("matches('', '^(?:)$')", version: "3.0"));
            Assert.AreEqual("false", Writes("matches('a', '^(?:)$')", version: "3.0"));
        }

        [TestMethod]
        [DataRow("matches('ab', '(?&lt;name>a)b')")]
        [DataRow("matches('ab', '(?=a)ab')")]
        [DataRow("matches('ab', '(?!x)ab')")]
        [DataRow("matches('ab', '(?&lt;=a)b')")]
        [DataRow("matches('ab', '(?>a)b')")]
        [DataRow("matches('AB', '(?i)ab')")]
        [DataRow("matches('ab', '(?#a comment)ab')")]
        [DataRow("matches('ab', '(?')")]
        public void AndOnlyThatOneOfTheGroupForms(string expression)
        {
            // '(?:' is the whole of what 3.0 adds. The rest of .NET's family stays out at every version.
            Assert.AreEqual("FORX0002", CodeOf(expression, version: "3.0"));
        }

        [TestMethod]
        public void ANonCapturingGroupTakesNoNumber()
        {
            // Which is what the groups after it depend on: '\1' has to mean the group a conformant processor
            // would give it, and .NET numbers them the same way only because this counts them the same way.
            Assert.AreEqual("true", Writes("matches('abcbc', '^a(?:bc)(bc)\\1?$')", version: "3.0"));
            Assert.AreEqual("false", Writes("matches('abcxx', '^a(?:bc)(bc)\\1?$')", version: "3.0"));

            // And a back-reference may not name one, because there is nothing there to name.
            Assert.AreEqual("FORX0002", CodeOf("matches('aa', '(?:a)\\1')", version: "3.0"));

            // replace() numbers them the same way, so $1 is the capturing group and not the one before it.
            Assert.AreEqual("[b]", Writes("replace('ab', 'a(?:x)?(b)', '[$1]')", version: "3.0"));

            // And so does analyze-string, which reports the groups by number.
            Assert.AreEqual(
                "b",
                Writes(
                    "analyze-string('ab', 'a(?:x)?(b)')//*:group[@nr='1']",
                    version: "3.0"));
        }

        [TestMethod]
        public void ThreePointZeroAddsTheLiteralFlag()
        {
            // 'q' turns the pattern into the characters it is written with, metacharacters and all.
            Assert.AreEqual("true", Writes("matches('a.b', 'a.b', 'q')", version: "3.0"));
            Assert.AreEqual("false", Writes("matches('axb', 'a.b', 'q')", version: "3.0"));

            // It is 3.0's, so a 2.0 stylesheet is told the flag is not one rather than given it.
            Assert.AreEqual("FORX0001", CodeOf("matches('a.b', 'a.b', 'q')"));
        }

        [TestMethod]
        [DataRow(@"matches('a b', 'a\bb')")]
        [DataRow(@"matches('ab', 'a\Bb')")]
        [DataRow(@"matches('a', '\Aa')")]
        [DataRow(@"matches('a', 'a\Z')")]
        [DataRow(@"matches('a', 'a\z')")]
        [DataRow(@"matches('a', '\Ga')")]
        [DataRow(@"matches('A', '\x41')")]
        [DataRow(@"matches('a', '\a')")]
        [DataRow(@"matches('a', '\e')")]
        [DataRow(@"matches('a', '\f')")]
        [DataRow(@"matches('a', '\v')")]
        public void AnEscapeIsOneOfTheOnesThisLanguageHas(string expression)
        {
            // Word boundaries, the .NET anchors, and the character escapes it spells with a backslash. Each
            // means something there and nothing here.
            Assert.AreEqual("FORX0002", CodeOf(expression));
        }

        [TestMethod]
        public void TheEscapesItDoesHaveStillWork()
        {
            // The other half of the rule above, and the reason it is a list rather than a guess.
            Assert.AreEqual("true", Writes(@"matches('a.b', 'a\.b')"));
            Assert.AreEqual("true", Writes(@"matches('a\b', 'a\\b')"));
            Assert.AreEqual("true", Writes(@"matches('a-b', 'a\-b')"));
            Assert.AreEqual("true", Writes(@"matches('a$b', 'a\$b')"));
            Assert.AreEqual("true", Writes(@"matches('a^b', 'a\^b')"));
            Assert.AreEqual("true", Writes(@"matches('a[b', 'a\[b')"));
            Assert.AreEqual("true", Writes(@"matches('a{b', 'a\{b')"));
            Assert.AreEqual("true", Writes(@"matches('a|b', 'a\|b')"));
            Assert.AreEqual("true", Writes(@"matches('a b', 'a\sb')"));
            Assert.AreEqual("true", Writes(@"matches('a1b', 'a\db')"));
            Assert.AreEqual("true", Writes(@"matches('axb', 'a\wb')"));
            Assert.AreEqual("true", Writes(@"matches('name', '\i\c*')"));
            Assert.AreEqual("true", Writes(@"matches('A', '\p{Lu}')"));
            Assert.AreEqual("true", Writes("matches(codepoints-to-string(9), '\\t')"));
        }

        // ---- What the two read differently ----------------------------------------------------------------

        [TestMethod]
        public void ADigitRunNamesAsManyGroupsAsThereAre()
        {
            // '\11' is the eleventh group where there are eleven, and the first followed by a literal 1
            // where there is one. .NET reads the eleventh either way.
            Assert.AreEqual("true", Writes("matches('#abc#1', '^(#)abc\\11$')"));
            Assert.AreEqual("FORX0002", CodeOf("matches('abcdefghijk', '(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)(k\\11)')"));

            // The same rule in a replacement, where $15 is the fifteenth group or the first and a 5.
            Assert.AreEqual("|aa|br|aa|c|aa|d|aa|br|aa|", Writes(
                "replace('abracadabra', '((((( ((((( (((((a))))) ))))) )))))', '|$1$15|', 'x')"));

            Assert.AreEqual("a1b", Writes("replace('ab', '(a)', '$11')"));
        }

        [TestMethod]
        public void AnAnchorMeansTheEndOfTheStringAndNotBeforeItsNewline()
        {
            // .NET's '$' is Perl's and also matches before a newline that ends the string.
            Assert.AreEqual("false", Writes("matches(concat('Mary', codepoints-to-string(10)), 'Mary$')"));
            Assert.AreEqual("true", Writes("matches('Mary', 'Mary$')"));

            // With m, a newline that ends the string does not begin a line after it — so there is no empty
            // line at the end for '^$' to match.
            Assert.AreEqual(
                "false",
                Writes("matches(concat('abcd', codepoints-to-string(10), 'defg', codepoints-to-string(10)), '^$', 'm')"));

            Assert.AreEqual(
                "true",
                Writes("matches(concat('abcd', codepoints-to-string(10), codepoints-to-string(10), 'x'), '^$', 'm')"));
        }

        [TestMethod]
        public void TheDotExcludesTheCarriageReturnAsWellAsTheNewline()
        {
            Assert.AreEqual("false", Writes("matches(concat('a', codepoints-to-string(13), 'b'), 'a.b')"));
            Assert.AreEqual("false", Writes("matches(concat('a', codepoints-to-string(10), 'b'), 'a.b')"));

            // Which the s flag turns off, that being what it is for.
            Assert.AreEqual("true", Writes("matches(concat('a', codepoints-to-string(13), 'b'), 'a.b', 's')"));
        }

        [TestMethod]
        public void IgnoringCaseDoesNotReachACategory()
        {
            // \p{Lu} is the uppercase letters whether or not case is being ignored. .NET folds it and
            // matches a lowercase letter, which inverts both of these.
            Assert.AreEqual("false", Writes("matches('m', '\\p{Lu}', 'i')"));
            Assert.AreEqual("true", Writes("matches('m', '\\P{Lu}', 'i')"));

            // A range in a class is folded, which is what the flag is for.
            Assert.AreEqual("true", Writes("matches('M', '[a-z]', 'i')"));
        }

        [TestMethod]
        public void TheXFlagRemovesWhitespaceWhereverItIsNotInAClass()
        {
            // Escaping does not protect it: the backslash joins the s the space was separating it from.
            Assert.AreEqual("true", Writes("matches('hello world', 'hello\\ sworld', 'x')"));
            Assert.AreEqual("true", Writes("matches('hello world', '\\p{ IsBasicLatin}+', 'x')"));

            // Inside a class it is a character like any other, so this one still matches a space.
            Assert.AreEqual("true", Writes("matches(' ', '[ ]', 'x')"));
        }

        [TestMethod]
        public void TokenizeGivesWhatLiesBetweenTheMatchesAndNothingElse()
        {
            // .NET's Regex.Split also returns whatever the pattern's groups captured, so the things split
            // on would come back among the pieces split into.
            // The empty tokens at each end are real: the string begins and ends with a match.
            Assert.AreEqual("|r|c|d|r|", Writes("string-join(tokenize('abracadabra', '(ab)|(a)'), '|')"));
            Assert.AreEqual("a|b", Writes("string-join(tokenize('a,b', ','), '|')"));

            // Nothing in is nothing out, and a zero-length string counts as nothing rather than as one
            // empty token.
            Assert.AreEqual("0", Writes("count(tokenize('', '\\s+'))"));
            Assert.AreEqual("0", Writes("count(tokenize((), '\\s+'))"));
        }

        // ---- what the grammar admits, where .NET's is wider ------------------------------------------------

        [TestMethod]
        [DataRow("[^[a-b]]", "a bracket inside a class is the character and must be escaped")]
        [DataRow("[[abcd]-[bc]]+", "the same, in a subtraction written the other way round")]
        [DataRow("([[:]+)", "and again, where .NET reads a POSIX class")]
        [DataRow("a]", "a bracket outside a class closes one that was never opened")]
        [DataRow("a[]]b", "a class holds at least one character")]
        [DataRow("a[^]b]c", "a negated one does too")]
        [DataRow("{5", "a quantifier is left open")]
        [DataRow("{5,", "and so is this one")]
        [DataRow("a{,2}", "a quantifier begins with a number")]
        [DataRow("\\p{Nd}{4}-\\[{Nd}{2}", "a brace after an escaped bracket begins no quantifier")]
        [DataRow("[X-\\u0533]+", "there is no \\u escape in this language")]
        [DataRow("[a - c - [ b ] +", "a subtraction ends the class that contains it")]
        [DataRow("[-[e-g]+", "a subtraction has nothing to take from")]
        public void APatternOutsideTheGrammarIsRefused(string pattern, string why)
        {
            // .NET reads every one of these as something, which is what makes them worth refusing: a
            // stylesheet that works here and fails on a conformant processor is the quietest kind of
            // difference there is.
            Assert.AreEqual("FORX0002", CodeOf($"matches('qwerty', '{pattern}')"), why);
        }

        [TestMethod]
        public void ACharacterIsACodePointAndNotHalfOfOne()
        {
            // The whole of the difference between the two languages' alphabets, in one place. Every one of
            // these reads a character above the basic plane, which .NET writes as two surrogates and would
            // otherwise match one of, or neither of, or half of.

            // The dot is one character, so it matches the pair and stops after it.
            string note = "codepoints-to-string(119127)";
            Assert.AreEqual("true", Writes($"matches(concat('abc', {note}, 'def'), '^abc.def$')", "3.0"));

            // So is a class member, and so are the ends of a range.
            Assert.AreEqual("true", Writes($"matches({note}, '^[\\p{{IsMusicalSymbols}}]$')", "3.0"));
            Assert.AreEqual(
                "true",
                Writes(
                    "matches(codepoints-to-string(119127), concat('^[', codepoints-to-string(119126), '-', "
                    + "codepoints-to-string(119128), ']$'))",
                    "3.0"));
            Assert.AreEqual(
                "false",
                Writes(
                    "matches(codepoints-to-string(119129), concat('^[', codepoints-to-string(119126), '-', "
                    + "codepoints-to-string(119128), ']$'))",
                    "3.0"));

            // A negated class holds every character it does not name, which includes all of these.
            Assert.AreEqual("true", Writes($"matches({note}, '^[^a-f]$')", "3.0"));
            Assert.AreEqual(
                "abc#def", Writes($"replace(concat('abc', {note}, 'def'), '[^a-f]', '#')", "3.0"));

            // And a category reaches above the basic plane, where .NET sees two surrogates and no letter.
            Assert.AreEqual("true", Writes("matches(codepoints-to-string(120744), '^\\p{Lu}$')", "3.0"));
            Assert.AreEqual("true", Writes("matches(codepoints-to-string(120777), '^\\p{Ll}$')", "3.0"));
            Assert.AreEqual("true", Writes("matches(codepoints-to-string(120831), '^\\d$')", "3.0"));
            Assert.AreEqual("false", Writes("matches(codepoints-to-string(120831), '^\\D$')", "3.0"));
            Assert.AreEqual("true", Writes("matches(codepoints-to-string(1114109), '^\\p{Co}$')", "3.0"));

            // A quantifier after a literal one reaches both of its halves rather than the second alone.
            Assert.AreEqual(
                "true", Writes($"matches(concat({note}, {note}), concat('^', {note}, '{{2}}$'))", "3.0"));
            Assert.AreEqual(
                "false", Writes($"matches(concat({note}, {note}), concat('^', {note}, '{{3}}$'))", "3.0"));
        }

        [TestMethod]
        public void ABlockAboveTheBasicPlaneIsWrittenAsThePairsThatSpellIt()
        {
            // .NET names only the blocks inside the basic plane, because it matches UTF-16 units and a
            // supplementary character is two of them. Without this the pattern is a hard error.
            Assert.AreEqual(
                "true",
                Writes("matches(codepoints-to-string(119088), '^\\p{IsMusicalSymbols}$')", "3.0"));
            Assert.AreEqual(
                "false",
                Writes("matches(codepoints-to-string(119296), '^\\p{IsMusicalSymbols}$')", "3.0"));

            // A range wide enough to span several leading surrogates takes the three-part form.
            Assert.AreEqual(
                "true",
                Writes("matches(codepoints-to-string(131072), '^\\p{IsCJKUnifiedIdeographsExtensionB}$')", "3.0"));
            Assert.AreEqual(
                "true",
                Writes("matches(codepoints-to-string(173791), '^\\p{IsCJKUnifiedIdeographsExtensionB}$')", "3.0"));
            Assert.AreEqual(
                "false",
                Writes("matches(codepoints-to-string(173792), '^\\p{IsCJKUnifiedIdeographsExtensionB}$')", "3.0"));

            // A block is a set of code points like any other, so it may be a member of a class beside
            // characters that are nothing to do with it, and it may be negated.
            Assert.AreEqual("true", Writes("matches('a', '^[\\p{IsMusicalSymbols}a]$')", "3.0"));
            Assert.AreEqual(
                "true",
                Writes("matches(codepoints-to-string(119088), '^[\\p{IsMusicalSymbols}a]$')", "3.0"));
            Assert.AreEqual(
                "false",
                Writes("matches(codepoints-to-string(119088), '^\\P{IsMusicalSymbols}$')", "3.0"));
            Assert.AreEqual("true", Writes("matches('a', '^\\P{IsMusicalSymbols}$')", "3.0"));
        }

        [TestMethod]
        public void ABlockInsideTheBasicPlaneIsLeftToDotNet()
        {
            // .NET has names for these and this engine has no ranges for them, so they are handed across
            // as written. What the engine still has to do is the half .NET cannot: a negated block takes in
            // every character above the basic plane, where .NET would match one half of one and stop.
            Assert.AreEqual("true", Writes("matches('x', '^[\\p{IsBasicLatin}]$')", "3.0"));
            Assert.AreEqual("false", Writes("matches('é', '^[\\p{IsBasicLatin}]$')", "3.0"));
            Assert.AreEqual("true", Writes("matches('é', '^[^\\p{IsBasicLatin}]$')", "3.0"));
            Assert.AreEqual(
                "true",
                Writes("matches(codepoints-to-string(119088), '^[^\\p{IsBasicLatin}]$')", "3.0"));

            // A subtraction takes text away from text, which .NET does for itself.
            Assert.AreEqual("true", Writes("matches('x', '^[\\p{IsBasicLatin}-[a-f]]$')", "3.0"));
            Assert.AreEqual("false", Writes("matches('c', '^[\\p{IsBasicLatin}-[a-f]]$')", "3.0"));

            // The other way about there is nothing to take away, because the block is a name and not a set.
            Assert.AreEqual("FORX0002", CodeOf("matches('x', '[a-z-[\\p{IsBasicLatin}]]')", "3.0"));

            // And a negation over a block cannot have anything taken from it: XPath takes the subtraction
            // from what is negated, where .NET negates what is left after it.
            Assert.AreEqual("FORX0002", CodeOf("matches('x', '[^\\p{IsBasicLatin}-[a-f]]')", "3.0"));
        }

        [TestMethod]
        public void ASubtractionTakesFromTheGroupAsWrittenNegationIncluded()
        {
            // '[^cde-[ag]]' is everything but c, d and e, and then without a and g — so 'a' is outside it.
            // .NET reads the same text as the negation of what the subtraction leaves, which puts 'a' in.
            Assert.AreEqual("false", Writes("matches('a', '^[^cde-[ag]]$')", "3.0"));
            Assert.AreEqual("false", Writes("matches('c', '^[^cde-[ag]]$')", "3.0"));
            Assert.AreEqual("true", Writes("matches('b', '^[^cde-[ag]]$')", "3.0"));
        }

        [TestMethod]
        public void TheNameEscapesWorkInsideACharacterClass()
        {
            // '\i' and '\c' stand for sets, so inside a class they are written without the brackets they
            // carry outside one — otherwise '[\i\c]' nests a class inside a class and means something else.
            Assert.AreEqual("true", Writes("matches('a:b', '^[\\i\\c]+:[\\i\\c]+$')", "3.0"));
            Assert.AreEqual("true", Writes("matches('name1', '^\\c[\\c\\d]*$')", "3.0"));
            Assert.AreEqual("false", Writes("matches('!', '^[\\i\\c]+$')", "3.0"));

            // Their negations stand for everything outside a set, which is a set too — so a class may hold
            // one, where .NET has no way to negate part of a class.
            Assert.AreEqual("true", Writes("matches('?a?', '^[\\C?a-c]+$')", "3.0"));
            Assert.AreEqual("false", Writes("matches('?d?', '^[\\C\\?a-c\\?]+$')", "3.0"));
            Assert.AreEqual("true", Writes("matches('a1', '^[\\D1]+$')", "3.0"));
            Assert.AreEqual("false", Writes("matches('2', '^[\\D1]+$')", "3.0"));
        }

        [TestMethod]
        public void AWordCharacterIsTheOneSchemaNamesAndNotTheOneDotNetDoes()
        {
            // Schema's '\w' is every character that is not punctuation, not a separator and not one of the
            // others — which puts the underscore outside it, connector punctuation being punctuation.
            // .NET's names the letters, the marks, the digits and the connectors, so it puts it inside.
            Assert.AreEqual("false", Writes(@"matches('first_last@a.cz', '^[\w\-\.]+@.*$')", "3.0"));
            Assert.AreEqual("true", Writes(@"matches('first-last@a.cz', '^[\w\-\.]+@.*$')", "3.0"));
            Assert.AreEqual("true", Writes(@"matches('_', '^\W$')", "3.0"));

            // And '\s' is the four characters Schema names, where .NET's is every Unicode separator and the
            // form feed and vertical tab besides.
            Assert.AreEqual("true", Writes("matches(codepoints-to-string(32), '^\\s$')", "3.0"));
            Assert.AreEqual("false", Writes("matches(codepoints-to-string(160), '^\\s$')", "3.0"));
            Assert.AreEqual("false", Writes("matches(codepoints-to-string(8232), '^\\s$')", "3.0"));
        }

        [TestMethod]
        public void TheQuoteFlagMakesTheReplacementLiteralToo()
        {
            // Not only the pattern. With 'q' there is nothing left to interpret in the replacement, so '$1'
            // is a dollar and a one rather than a group, and a lone backslash is a backslash.
            Assert.AreEqual("$1br$1c$1d$1br$1", Writes("replace('abracadabra', 'a', '$1', 'q')", "3.0"));
            Assert.AreEqual("a\\b\\c", Writes("replace('a/b/c', '/', '\\', 'q')", "3.0"));
            Assert.AreEqual("a$b$c", Writes("replace('a/b/c', '/', '$', 'q')", "3.0"));

            // Without it the same backslash is FORX0004, and a group reference is read as one.
            Assert.AreEqual("FORX0004", CodeOf("replace('a/b/c', '/', '\\')", "3.0"));
            Assert.AreEqual("[a]/[b]/[c]", Writes("replace('a/b/c', '(a|b|c)', '[$1]')", "3.0"));
        }

        [TestMethod]
        public void AnEmojiBlockCanBeNamed()
        {
            // .NET has no name for a block above the basic plane, so the range is written out as a pair of
            // surrogates — and a quantifier after it has to take the whole pair, not the trailing half.
            Assert.AreEqual(
                "true", Writes("matches(codepoints-to-string(128512), '^\\p{IsEmoticons}+$')", "3.0"));
            Assert.AreEqual(
                "false", Writes("matches(codepoints-to-string(128156), '^\\p{IsEmoticons}+$')", "3.0"));

            // 128156 is a purple heart, which lives in the block next door.
            Assert.AreEqual(
                "true",
                Writes(
                    "matches(codepoints-to-string(128156), '^\\p{IsMiscellaneousSymbolsandPictographs}$')",
                    "3.0"));
        }

        /// <summary>Runs a whole stylesheet body, for what a single expression cannot show.</summary>
        private static string Runs(string body)
        {
            const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

            XsltOptions options = new XsltOptions { OmitXmlDeclaration = true, Version = XsltVersion.V30 };

            return new Xslt($"<xsl:stylesheet version=\"3.0\" {Xsl}>{body}</xsl:stylesheet>", options)
                .TransformXml("<r/>");
        }

        [TestMethod]
        public void AGroupThePatternHasNotGotContributesNothing()
        {
            // A $N naming more groups than the pattern has is replaced by a zero-length string. .NET leaves
            // the reference standing as text instead, so this used to write a literal ${5} into the result.
            Assert.AreEqual("[b]##c", Writes("replace('abc', '(a)(b)', '[$2]#$5#')"));

            // A number is still read as far as it names a group and no further: with two groups, $12 is the
            // first of them followed by a 2, and not the twelfth.
            Assert.AreEqual("a2", Writes("replace('ab', '(a)(b)', '$12')"));
        }

        [TestMethod]
        public void ADynamicCallOnRegexGroupSeesNoGroupsAtAll()
        {
            // The regex group set is no part of what a function item carries, and a call made through one
            // does not borrow the caller's either, so a captured regex-group#1 answers with a zero-length
            // string wherever it is called (§5.3.4). Called directly it reads the groups as it always did.
            const string Inner =
                "<xsl:analyze-string select=\"'111222333'\" regex=\"(1+)(2+)(3+)\">"
                + "<xsl:matching-substring><xsl:value-of select=\"{0}\"/></xsl:matching-substring>"
                + "</xsl:analyze-string>";

            string Sheet(string inner) =>
                "<xsl:template match=\"/\"><out>"
                + "<xsl:analyze-string select=\"'aaabbbccc'\" regex=\"(a+)(b+)(c+)\">"
                + "<xsl:matching-substring>"
                + "<xsl:variable name=\"g\" select=\"regex-group#1\"/>"
                + inner
                + "</xsl:matching-substring></xsl:analyze-string></out></xsl:template>";

            Assert.AreEqual("<out/>", Runs(Sheet(Inner.Replace("{0}", "$g(2)"))));
            Assert.AreEqual("<out>222</out>", Runs(Sheet(Inner.Replace("{0}", "regex-group(2)"))));
        }
    }
}
