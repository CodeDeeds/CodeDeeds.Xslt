namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:random-number-generator()</c>, XPath 3.1's source of randomness, which is a value
    /// rather than a side effect: a map holding a number, the next generator, and a way to shuffle.
    /// </summary>
    [TestClass]
    public sealed class RandomNumberGeneratorTests
    {
        private const string Namespaces =
            "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
            + " xmlns:map=\"http://www.w3.org/2005/xpath-functions/map\"";

        private static string Stylesheet(string expression, string version)
        {
            return $"<xsl:stylesheet version=\"{version}\" {Namespaces} exclude-result-prefixes=\"xs map\">"
                + "<xsl:template match=\"/\"><out>"
                + $"<xsl:value-of select=\"{expression}\" separator=\",\"/>"
                + "</out></xsl:template></xsl:stylesheet>";
        }

        private static string Writes(string expression)
        {
            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            string stylesheet = Stylesheet(expression, "3.0");
            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        private static string Fails(string expression, string version = "3.0")
        {
            // A 2.0 stylesheet is run on a processor asked to be 2.0, since a 3.0 one gives it the 3.0 library.
            XsltOptions options = new XsltOptions
            {
                OmitXmlDeclaration = true,
                Version = version == "2.0" ? XsltVersion.V20 : XsltVersion.Implemented,
            };

            XsltException error = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(Stylesheet(expression, version), options).TransformXml("<r/>"));

            return error.Code ?? string.Empty;
        }

        [TestMethod]
        public void TheGeneratorIsAMapOfANumberAndTwoFunctions()
        {
            Assert.AreEqual(
                "3,true,true,true,0,1,true",
                Writes(
                    "let $g := random-number-generator(42) return ("
                    + "map:size($g), $g?number instance of xs:double, $g?number ge 0, $g?number lt 1, "
                    + "function-arity($g?next), function-arity($g?permute), empty(function-name($g?next)))"));
        }

        [TestMethod]
        public void TheSeedSettlesEverything()
        {
            // The same seed is the same generator, however many times it is asked for, and a different seed
            // is a different one.
            Assert.AreEqual(
                "true,true",
                Writes(
                    "(random-number-generator(42)?number eq random-number-generator(42)?number, "
                    + "random-number-generator(42)?number ne random-number-generator('forty-two')?number)"));

            // next is a step along one stream: the same step every time it is taken, and not where it started.
            Assert.AreEqual(
                "true,true",
                Writes(
                    "let $g := random-number-generator(7) return "
                    + "($g?next()?number eq $g?next()?number, $g?next()?number ne $g?number)"));

            // Any atomic value seeds it, and the type is part of the seed: the string '1' is not the integer 1.
            Assert.AreEqual(
                "true,true",
                Writes(
                    "(random-number-generator(xs:date('2026-09-12'))?number eq random-number-generator(xs:date('2026-09-12'))?number, "
                    + "random-number-generator('1')?number ne random-number-generator(1)?number)"));
        }

        [TestMethod]
        public void TheNumbersAreSpreadOverTheUnitInterval()
        {
            // Two hundred steps along one stream, taken by folding next over a range: every number in [0, 1),
            // none repeated, and the mean near the middle. A fixed seed, so it is the same two hundred each run.
            Assert.AreEqual(
                "200,200,true,true,true,true",
                Writes(
                    "let $walk := fold-left(1 to 200, map { 'g': random-number-generator(1), 'n': () }, "
                    + "function($acc, $i) { map { 'g': $acc?g?next(), 'n': ($acc?n, $acc?g?number) } }), "
                    + "$n := $walk?n "
                    + "return (count($n), count(distinct-values($n)), min($n) ge 0, max($n) lt 1, avg($n) gt 0.4, avg($n) lt 0.6)"));
        }

        [TestMethod]
        public void PermuteReordersWithoutLosingAnything()
        {
            Assert.AreEqual(
                "true,true,true",
                Writes(
                    "let $p := random-number-generator(3)?permute(1 to 20) return "
                    + "(count($p) eq 20, deep-equal(sort($p), 1 to 20), not(deep-equal($p, 1 to 20)))"));

            // The same generator shuffles the same sequence the same way.
            Assert.AreEqual(
                "true",
                Writes(
                    "let $g := random-number-generator(3) return "
                    + "deep-equal($g?permute(1 to 20), $g?permute(1 to 20))"));

            // What the DocBook stylesheets do with it: the letters of a string, shuffled and joined back.
            Assert.AreEqual(
                "true,8",
                Writes(
                    "let $s := 'abcdefgh', $chars := string-to-codepoints($s) ! codepoints-to-string(.), "
                    + "$shuffled := string-join(random-number-generator()?permute($chars), '') "
                    + "return (string-join(sort(string-to-codepoints($shuffled) ! codepoints-to-string(.)), '') eq $s, string-length($shuffled))"));

            Assert.AreEqual(string.Empty, Writes("random-number-generator(1)?permute(())"));
            Assert.AreEqual("only", Writes("random-number-generator(1)?permute('only')"));
        }

        [TestMethod]
        public void WithoutASeedTheGeneratorIsFixedForTheTransformation()
        {
            // Deterministic within an execution scope, as the specification has it: every seedless call in
            // one transformation is the same generator, and an empty seed is no seed.
            Assert.AreEqual(
                "true,true,true",
                Writes(
                    "let $n := random-number-generator()?number return ("
                    + "$n eq random-number-generator()?number, "
                    + "$n eq random-number-generator(())?number, "
                    + "$n ge 0 and $n lt 1)"));
        }

        [TestMethod]
        public void TheSeedIsOneAtomicValueOrNothing()
        {
            Assert.AreEqual("XPTY0004", Fails("random-number-generator((1, 2))"));
            Assert.AreEqual("XPST0017", Fails("random-number-generator(1, 2)"));

            // A map cannot be atomized, and the complaint is whatever atomizing one raises anywhere else.
            Assert.AreEqual(Fails("number(map { })"), Fails("random-number-generator(map { })"));
        }

        [TestMethod]
        public void ItIsThreePointOnesAndSaysSo()
        {
            Assert.AreEqual(
                "true,true,true,false",
                Writes(
                    "(function-available('random-number-generator'), "
                    + "function-available('random-number-generator', 0), "
                    + "function-available('random-number-generator', 1), "
                    + "function-available('random-number-generator', 2))"));

            // A 2.0 stylesheet calling it means an extension function of its own, and is told so.
            Assert.AreEqual("XPST0017", Fails("random-number-generator()", version: "2.0"));
        }
    }
}
