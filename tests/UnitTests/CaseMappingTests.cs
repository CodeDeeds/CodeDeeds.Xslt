namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for <c>fn:upper-case</c> and <c>fn:lower-case</c>, which are defined against Unicode's full
    /// case mappings and so may change the length of a string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// F&amp;O 5.4.7 asks for the mappings in Unicode's <em>default case operations</em>, which are the full
    /// mappings with no tailoring for a language. .NET applies the <em>simple</em> ones, which are one
    /// character to one character: they have no way to say that the sharp s uppercases to two letters, and
    /// they leave it alone instead.
    /// </para>
    /// <para>
    /// The conditional mappings are not here, and the specification is why: the language-specific ones are
    /// excluded by a function that is defined to be locale-insensitive, and the Greek final sigma depends on
    /// what stands either side of it rather than on the character alone.
    /// </para>
    /// </remarks>
    [TestClass]
    public sealed class CaseMappingTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"";

        private static string Writes(string expression)
        {
            string stylesheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + $"<xsl:template match=\"/\"><out><xsl:value-of select=\"{expression}\"/></out></xsl:template>"
                + "</xsl:stylesheet>";

            XsltOptions For(XsltBackend backend) => new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
            };

            string interpreted = new Xslt(stylesheet, For(XsltBackend.Interpreted)).TransformXml("<r/>");
            string compiled = new Xslt(stylesheet, For(XsltBackend.Compiled)).TransformXml("<r/>");

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        [TestMethod]
        public void UpperCasingMayMakeAStringLonger()
        {
            // The sharp s, U+00DF, has no upper-case letter of its own and maps to two.
            Assert.AreEqual("SS", Writes("upper-case('&#xDF;')"));
            Assert.AreEqual("STRASSE", Writes("upper-case('stra&#xDF;e')"));
            Assert.AreEqual("2", Writes("string-length(upper-case('&#xDF;'))"));

            // And the ligatures map to the letters they are made of: U+FB03 is ffi.
            Assert.AreEqual("FFI", Writes("upper-case('&#xFB03;')"));
            Assert.AreEqual("FL", Writes("upper-case('&#xFB02;')"));
        }

        [TestMethod]
        public void SomeLettersUpperCaseToALetterAndAMark()
        {
            // U+01F0 is j with caron, and there is no capital form of it: the mapping is a capital J and
            // the combining caron, U+030C.
            Assert.AreEqual("J̌", Writes("upper-case('&#x1F0;')"));

            // U+1F80 is the Greek small alpha with psili and ypogegrammeni, whose upper-case form writes
            // the subscript iota as a capital iota beside the letter.
            Assert.AreEqual("ἈΙ", Writes("upper-case('&#x1F80;')"));
        }

        [TestMethod]
        public void LowerCasingMayMakeAStringLongerToo()
        {
            // U+0130 is the capital I with a dot above, whose lower-case form keeps the dot as a combining
            // mark because a small i already carries one of its own.
            Assert.AreEqual("i̇", Writes("lower-case('&#x130;')"));
            Assert.AreEqual("2", Writes("string-length(lower-case('&#x130;'))"));
        }

        [TestMethod]
        public void TheTwoAreNotInversesOfEachOther()
        {
            // Which the specification says outright, and the sharp s is the plainest example there is.
            Assert.AreEqual("ss", Writes("lower-case(upper-case('&#xDF;'))"));
            Assert.AreEqual("false", Writes("lower-case(upper-case('&#xDF;')) eq '&#xDF;'"));
        }

        [TestMethod]
        public void EverythingElseIsTheSimpleMappingStill()
        {
            // The table is a short list of exceptions and the rest of Unicode goes the way it always did,
            // including the one-to-one mappings that are not the obvious letter: U+00B5, the micro sign,
            // uppercases to the Greek capital mu, and U+00FF to Y with diaeresis.
            Assert.AreEqual("ABCD0", Writes("upper-case('abCd0')"));
            Assert.AreEqual("Μ", Writes("upper-case('&#xB5;')"));
            Assert.AreEqual("Ÿ", Writes("upper-case('&#xFF;')"));
            Assert.AreEqual("abcd0", Writes("lower-case('ABCD0')"));

            // Including a character outside the basic plane, which is a surrogate pair and must stay one:
            // U+10428 is Deseret small ah, whose capital is U+10400.
            Assert.AreEqual("𐐀", Writes("upper-case('&#x10428;')"));
        }
    }
}
