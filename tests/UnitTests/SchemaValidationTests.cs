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
            + "<xs:element name=\"z\" type=\"t:zType\"/>"
            + "<xs:complexType name=\"zType\">"
            + "<xs:attribute name=\"price\" type=\"xs:decimal\"/>"
            + "<xs:attribute name=\"cost\" type=\"xs:decimal\" default=\"20.01\"/>"
            + "</xs:complexType>"
            + "<xs:element name=\"tagged\"><xs:complexType><xs:sequence>"
            + "<xs:element name=\"item\" maxOccurs=\"unbounded\"><xs:complexType>"
            + "<xs:attribute name=\"code\" type=\"xs:string\"/></xs:complexType></xs:element>"
            + "</xs:sequence></xs:complexType>"
            + "<xs:unique name=\"oneCode\"><xs:selector xpath=\"t:item\"/><xs:field xpath=\"@code\"/></xs:unique>"
            + "</xs:element>"
            + "<xs:element name=\"tag\" type=\"t:tagType\"/>"
            + "<xs:complexType name=\"tagType\"><xs:simpleContent>"
            + "<xs:extension base=\"xs:ID\"/></xs:simpleContent></xs:complexType>"
            + "<xs:element name=\"tags\"><xs:complexType><xs:sequence>"
            + "<xs:element ref=\"t:tag\" maxOccurs=\"unbounded\"/></xs:sequence></xs:complexType></xs:element>"
            + "<xs:element name=\"list\"><xs:complexType><xs:sequence>"
            + "<xs:element ref=\"t:count\" maxOccurs=\"unbounded\"/></xs:sequence></xs:complexType></xs:element>"
            + "</xs:schema>";

        private const string Import = "<xsl:import-schema namespace=\"urn:t\">" + Schema + "</xsl:import-schema>";

        /// <summary>A document with one nilled element and one that holds a value.</summary>
        private const string Counts =
            "<list xmlns=\"urn:t\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
            + "<count xsi:nil=\"true\"/><count>7</count></list>";

        /// <summary>Two elements whose own content a schema types as an ID.</summary>
        private const string Tags = "<tags xmlns=\"urn:t\"><tag>a1</tag><tag>a2</tag></tags>";

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
        // ---- What strip and preserve leave on a constructed node --------------------------------------------

        [TestMethod]
        public void AnElementStripsTheAnnotationsOfWhatIsBuiltInsideIt()
        {
            // §25.4.1: strip gives "the new node and each of the contained nodes" the untyped annotation, and
            // "any previous type annotation present on a contained element or attribute node ... is also
            // replaced". Strip is the default, so an xsl:attribute that named a type inside an ordinary
            // literal result element loses it again on the way in.
            Assert.AreEqual(
                "<out>false true</out>",
                Run(
                    Sheet(
                        "<xsl:variable name=\"v\">"
                        + "<e><xsl:attribute name=\"id\" type=\"xs:ID\">A001</xsl:attribute></e></xsl:variable>"
                        + "<xsl:value-of select=\"$v/@id instance of attribute(*, xs:ID), exists($v/id('A001'))\"/>"),
                    "<r/>"));

            // preserve keeps them, and annotates the element it builds xs:anyType rather than leaving it
            // untyped: "the new element has a type annotation of xs:anyType".
            Assert.AreEqual(
                "<out>true false true</out>",
                Run(
                    Sheet(
                        "<xsl:variable name=\"v\" as=\"element()\">"
                        + "<e xsl:default-validation=\"preserve\">"
                        + "<xsl:attribute name=\"id\" type=\"xs:ID\">A001</xsl:attribute></e></xsl:variable>"
                        + "<xsl:value-of select=\"$v/@id instance of attribute(*, xs:ID), "
                        + "$v instance of element(*, xs:untyped), $v instance of element(*, xs:anyType)\"/>"),
                    "<r/>"));
        }

        [TestMethod]
        public void StrippingLeavesBehindWhatMakesAnAttributeAnId()
        {
            // The rest of that rule: "In the case of elements the nilled property is set to false. The values
            // of the is-id and is-idrefs properties are unchanged." So id() still finds the attribute whose
            // annotation has just been taken off it, which is what the suite's import-schema-005 measures.
            Assert.AreEqual(
                "<out>e4</out>",
                Run(
                    Sheet(
                        "<xsl:variable name=\"v\">"
                        + "<x validation=\"strip\">"
                        + "<e3><xsl:attribute name=\"id\" type=\"xs:ID\">A003</xsl:attribute></e3>"
                        + "<e4><xsl:attribute name=\"id\" type=\"xs:ID\">A004</xsl:attribute></e4>"
                        + "</x></xsl:variable>"
                        + "<xsl:value-of select=\"$v/id('A004')/local-name()\"/>"),
                    "<r/>"));
        }

        // ---- What validation adds and what it checks -------------------------------------------------------

        [TestMethod]
        public void ValidationSuppliesTheAttributesTheSchemaDeclaresADefaultFor()
        {
            // §25.4.1: "If default values for elements or attributes are defined in the schema, the validation
            // process will where necessary create new nodes containing these default values." So validating
            // is not only a check: the element comes out of it carrying an attribute it never wrote.
            Assert.AreEqual(
                "<out><t:z xmlns:t=\"urn:t\" price=\"2.50\" cost=\"20.01\"/></out>",
                Run(Sheet("<t:z price=\"2.50\" xsl:validation=\"strict\"/>"), "<r/>"));
        }

        [TestMethod]
        public void AConstructedElementIsHeldToItsIdentityConstraintsAndNotToIdUniqueness()
        {
            // §25.4.1 divides the document-level rules in two for a constructed element. "Validation Root
            // Valid (ID/IDREF)" is not applied, so two equal IDs inside the subtree are not a failure;
            // "Identity-constraint Satisfied" should be, so a broken xs:unique is the element being invalid.
            Assert.AreEqual(
                "XTTE1510",
                Refuses(
                    Sheet("<t:tagged xsl:validation=\"strict\"><t:item code=\"a\"/><t:item code=\"a\"/></t:tagged>"),
                    "<r/>"));

            Assert.AreEqual(
                "<out><t:tagged xmlns:t=\"urn:t\"><t:item code=\"a\"/><t:item code=\"b\"/></t:tagged></out>",
                Run(
                    Sheet("<t:tagged xsl:validation=\"strict\"><t:item code=\"a\"/><t:item code=\"b\"/></t:tagged>"),
                    "<r/>"));
        }

        [TestMethod]
        public void ADocumentTestWantsOneElementAndNothingElseBesideIt()
        {
            // XPath 3.1 §2.5.5.2: document-node(E) "matches any document node that contains exactly one
            // element node, optionally accompanied by one or more comment and processing instruction nodes".
            // The list is exhaustive, so a second element is a document this does not describe.
            Assert.AreEqual(
                "<out>true false</out>",
                Run(
                    Sheet(
                        "<xsl:variable name=\"one\" as=\"document-node()\">"
                        + "<xsl:document><t:count>1</t:count></xsl:document></xsl:variable>"
                        + "<xsl:variable name=\"two\" as=\"document-node()\">"
                        + "<xsl:document validation=\"preserve\">"
                        + "<t:count>1</t:count><t:count>2</t:count></xsl:document></xsl:variable>"
                        + "<xsl:value-of select=\"$one instance of document-node(element(t:count)), "
                        + "$two instance of document-node(element(t:count))\"/>"),
                    "<r/>"));
        }
        [TestMethod]
        public void AnElementThatIsAnIdStaysOneWhenItsAnnotationIsStripped()
        {
            // §4.4: input-type-annotations="strip" replaces the annotations of the input, and keeps the
            // is-id and is-idrefs properties of what it strips. An element whose own typed value is an
            // xs:ID is one of the things id() finds, and finding it is otherwise a matter of reading the
            // annotation -- which is why, once the annotation is gone, it has to have been remembered.
            const string Asked =
                "<xsl:value-of select=\"data(/t:tags/t:tag[1]) instance of xs:ID, exists(id('a2'))\"/>";

            Assert.AreEqual(
                "<out>false true</out>",
                Run(Sheet(Asked, Import, "input-type-annotations=\"strip\""), Tags, validateInput: true));

            // With the annotation kept, the type says it as well.
            Assert.AreEqual(
                "<out>true true</out>",
                Run(Sheet(Asked), Tags, validateInput: true));
        }
    }
}
