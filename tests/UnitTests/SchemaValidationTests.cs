namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for what validation settles about a constructed node: the annotation it carries, whether it is
    /// nilled, what a document node's children may be, and what the serializer may add inside it.
    /// </summary>
    /// <remarks>
    /// The other half of schema awareness from <see cref="SchemaAwarenessTests"/>, which is about naming a
    /// schema's types. Here a node is built and measured against a schema, and what comes back is a
    /// difference the stylesheet can see: <c>nilled()</c>, an <c>instance of</c>, an error code.
    /// </remarks>
    [TestClass]
    public sealed class SchemaValidationTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:t=\"urn:t\" exclude-result-prefixes=\"xs t\"";

        /// <summary>A nillable element of a numeric type, and one whose content model is mixed.</summary>
        private const string Schema =
            "<xs:schema targetNamespace=\"urn:t\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
            + " xmlns:t=\"urn:t\" elementFormDefault=\"qualified\">"
            + "<xs:element name=\"count\" type=\"xs:integer\" nillable=\"true\"/>"
            + "<xs:element name=\"note\" type=\"t:noteType\"/>"
            + "<xs:complexType name=\"noteType\" mixed=\"true\">"
            + "<xs:sequence><xs:element name=\"em\" type=\"xs:string\" maxOccurs=\"unbounded\"/></xs:sequence>"
            + "</xs:complexType>"
            + "<xs:element name=\"list\"><xs:complexType><xs:sequence>"
            + "<xs:element ref=\"t:count\" maxOccurs=\"unbounded\"/></xs:sequence></xs:complexType></xs:element>"
            + "</xs:schema>";

        private const string Import = "<xsl:import-schema namespace=\"urn:t\">" + Schema + "</xsl:import-schema>";

        /// <summary>A document with one nilled element and one that holds a value.</summary>
        private const string Counts =
            "<list xmlns=\"urn:t\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
            + "<count xsi:nil=\"true\"/><count>7</count></list>";

        private static XsltOptions Options(XsltBackend backend, bool validateInput = false)
        {
            return new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                SchemaAware = true,
                InputValidation = validateInput ? XsltValidation.Strict : XsltValidation.Strip,
            };
        }

        /// <summary>Runs a whole stylesheet on both backends, which must agree.</summary>
        private static string Run(string stylesheet, string input, bool validateInput = false)
        {
            string interpreted = new Xslt(stylesheet, Options(XsltBackend.Interpreted, validateInput))
                .TransformXml(input);
            string compiled = new Xslt(stylesheet, Options(XsltBackend.Compiled, validateInput))
                .TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted;
        }

        /// <summary>The code a stylesheet is refused with, the same on both backends.</summary>
        private static string Refuses(string stylesheet, string input, bool validateInput = false)
        {
            string? interpreted = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, Options(XsltBackend.Interpreted, validateInput))
                    .TransformXml(input)).Code;
            string? compiled = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, Options(XsltBackend.Compiled, validateInput))
                    .TransformXml(input)).Code;

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted ?? string.Empty;
        }

        /// <summary>A stylesheet whose one template writes the body inside an out element.</summary>
        private static string Sheet(string body, string declarations = Import, string attributes = "")
        {
            return $"<xsl:stylesheet version=\"3.0\" {Xsl} {attributes}>"
                + declarations
                + $"<xsl:template match=\"/\"><out>{body}</out></xsl:template></xsl:stylesheet>";
        }

        // ---- What a type attribute names -------------------------------------------------------------------

        [TestMethod]
        public void AnElementValidatedAgainstUntypedAtomicCarriesThatAnnotation()
        {
            // §25.4.1: validating against xs:untypedAtomic "is the same as specifying [xsl:]type='xs:string'
            // except that when validation succeeds, the returned element or attribute has a type annotation
            // of xs:untypedAtomic". No schema defines that type, so it is not found by looking one up.
            Assert.AreEqual(
                "<out>true true</out>",
                Run(
                    Sheet(
                        "<xsl:variable name=\"v\" as=\"node()*\">"
                        + "<xsl:attribute name=\"a\" select=\"'abcd'\" type=\"xs:untypedAtomic\"/>"
                        + "<z xsl:type=\"xs:untypedAtomic\">abcd</z></xsl:variable>"
                        + "<xsl:value-of select=\"$v[1] instance of attribute(*, xs:untypedAtomic), "
                        + "$v[2] instance of element(*, xs:untypedAtomic)\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void AnElementWithElementChildrenIsNotAnUntypedAtomicOne()
        {
            // The rest of that sentence: "Validation fails in the case of an element with element children."
            // An element with element children has no simple content to be a value of any simple type.
            Assert.AreEqual(
                "XTTE1540",
                Refuses(Sheet("<z xsl:type=\"xs:untypedAtomic\">abcd<a/>wxyz</z>"), "<r/>"));
        }

        // ---- What xsi:nil says -----------------------------------------------------------------------------

        [TestMethod]
        public void ANilledElementIsValidatedAsNilledRatherThanAsEmptyContent()
        {
            // xsi:nil is a property of the element as far as the validator is concerned, not content to be
            // validated. Left out of what the validator is told, a nillable element of a numeric type is
            // refused for holding nothing at all, which is the one thing xsi:nil says it may do.
            Assert.AreEqual(
                "<out>true false</out>",
                Run(
                    Sheet(
                        "<xsl:variable name=\"v\" as=\"document-node()\">"
                        + "<xsl:copy-of select=\".\" validation=\"strict\"/></xsl:variable>"
                        + "<xsl:value-of select=\"nilled($v/t:list/t:count[1]), nilled($v/t:list/t:count[2])\"/>"),
                    Counts));
        }

        [TestMethod]
        public void AShallowCopyUnderPreserveKeepsNeitherTheTypeNorTheNilling()
        {
            // §25.4.1: a shallow copy of an element "will have a type annotation of xs:anyType (because
            // this instruction does not copy the content of the element, it would be wrong to assume that the
            // type is unchanged)", and its nilled property is handled as xsl:element's is, which is to say
            // false. The first two answers together are what says it is annotated xs:anyType rather than left
            // alone: it is no longer an xs:integer, and it is not xs:untyped either, which is what an element
            // carrying no annotation would be. xsl:copy-of, which does copy the content, keeps the nilling.
            Assert.AreEqual(
                "<out>false false false true</out>",
                Run(
                    Sheet(
                        "<xsl:variable name=\"s\" as=\"element()\">"
                        + "<xsl:copy select=\"/t:list/t:count[1]\" validation=\"preserve\">"
                        + "<xsl:copy-of select=\"@*\"/></xsl:copy></xsl:variable>"
                        + "<xsl:variable name=\"d\" as=\"element()\">"
                        + "<xsl:copy-of select=\"/t:list/t:count[1]\" validation=\"preserve\"/></xsl:variable>"
                        + "<xsl:value-of select=\""
                        + "$s instance of element(*, xs:integer), $s instance of element(*, xs:untyped), "
                        + "nilled($s), nilled($d)\"/>"),
                    Counts,
                    validateInput: true));
        }

        // ---- What a result document is validated as --------------------------------------------------------

        [TestMethod]
        public void AValidatedResultDocumentIsNormalisedWithTheItemSeparator()
        {
            // §25.1: validation "is applied to the document node produced as the result of sequence
            // normalization", and §2.3.6.1 says what that process does — it puts the item-separator between
            // every pair of items. A comment and an element are a document node's children; a separator
            // between them is a text node, which a validated document may not have.
            string body = "<xsl:template match=\"/\"><xsl:result-document validation=\"strict\">"
                + "<xsl:comment>c</xsl:comment><t:count>7</t:count></xsl:result-document></xsl:template>";

            Assert.AreEqual(
                "<!--c--><t:count xmlns:t=\"urn:t\">7</t:count>",
                Run($"<xsl:stylesheet version=\"3.0\" {Xsl}>{Import}{body}</xsl:stylesheet>", "<r/>"));

            Assert.AreEqual(
                "XTTE1550",
                Refuses(
                    $"<xsl:stylesheet version=\"3.0\" {Xsl}><xsl:output item-separator=\"+++\"/>"
                    + $"{Import}{body}</xsl:stylesheet>",
                    "<r/>"));
        }

        // ---- What the serializer may add -------------------------------------------------------------------

        [TestMethod]
        public void NoWhitespaceIsAddedInsideATypedElementWhoseContentIsMixed()
        {
            // Serialization §5.1.4 permits added whitespace in the immediate content of an element annotated
            // xs:untyped or xs:anyType that has element children, and of one whose content model is element
            // only; it says whitespace SHOULD NOT be added in the immediate content of an element annotated
            // otherwise whose content model is mixed. Indenting from what has been written so far cannot
            // tell: in <note><em>a</em> and b</note> the text arrives one child too late.
            string sheet = $"<xsl:stylesheet version=\"3.0\" {Xsl}><xsl:output indent=\"yes\"/>{Import}"
                + "<xsl:template match=\"/\"><xsl:result-document validation=\"strict\">"
                + "<t:note><t:em>a</t:em> and b</t:note></xsl:result-document></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<t:note xmlns:t=\"urn:t\"><t:em>a</t:em> and b</t:note>",
                Run(sheet, "<r/>").Replace("\r\n", "\n"));

            // The element-only case still indents, which is where the whitespace is this engine's to add.
            string listing = $"<xsl:stylesheet version=\"3.0\" {Xsl}><xsl:output indent=\"yes\"/>{Import}"
                + "<xsl:template match=\"/\"><xsl:result-document validation=\"strict\">"
                + "<t:list><t:count>1</t:count></t:list></xsl:result-document></xsl:template></xsl:stylesheet>";

            Assert.AreEqual(
                "<t:list xmlns:t=\"urn:t\">\n  <t:count>1</t:count>\n</t:list>",
                Run(listing, "<r/>").Replace("\r\n", "\n"));
        }
    }
}
