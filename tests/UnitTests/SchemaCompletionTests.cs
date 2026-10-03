using System.Xml;
using System.Xml.Schema;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the schema work that finished the feature: the public way to validate a document or a tree
    /// and read the types back, the account of .NET's date range, a document's own schema hints, and the
    /// schema-aware rules the QT3 suite found once its schema environments were read.
    /// </summary>
    [TestClass]
    public sealed class SchemaCompletionTests
    {
        private const string XsdNamespace = "http://www.w3.org/2001/XMLSchema";

        private const string OrderSchema =
            "<xs:schema targetNamespace=\"urn:o\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:o=\"urn:o\" elementFormDefault=\"qualified\">"
            + "<xs:simpleType name=\"idList\"><xs:list itemType=\"xs:ID\"/></xs:simpleType>"
            + "<xs:simpleType name=\"idOrNumber\"><xs:union memberTypes=\"xs:ID xs:integer\"/></xs:simpleType>"
            + "<xs:element name=\"order\"><xs:complexType><xs:sequence>"
            + "<xs:element name=\"qty\" type=\"xs:int\"/>"
            + "<xs:element name=\"when\" type=\"xs:date\" minOccurs=\"0\"/>"
            + "<xs:element name=\"ref\" type=\"o:idList\" minOccurs=\"0\" maxOccurs=\"unbounded\"/>"
            + "<xs:element name=\"alt\" type=\"o:idOrNumber\" minOccurs=\"0\" maxOccurs=\"unbounded\"/>"
            + "</xs:sequence><xs:attribute name=\"id\" type=\"xs:ID\"/></xs:complexType></xs:element>"
            + "</xs:schema>";

        private const string Order = "<order xmlns=\"urn:o\" id=\"o1\"><qty>3</qty><when>2020-02-03</when></order>";

        private static XdmSchemas Schemas(string schema = OrderSchema)
        {
            XmlSchemaSet set = new XmlSchemaSet();
            set.Add(null, XmlReader.Create(new StringReader(schema)));
            return new XdmSchemas(set);
        }

        private static int Child(XdmTree tree, int parent, int index)
        {
            int child = tree.FirstChildOf(parent);

            for (; index > 0; index--)
            {
                child = tree.NextSiblingOf(child);
            }

            return child;
        }

        // ---- The public API ------------------------------------------------------------------------------

        /// <summary>A document read through <see cref="XdmSchemas"/> carries the types validation settled on.</summary>
        [TestMethod]
        public void AParsedDocumentIsTyped()
        {
            XdmTree tree = Schemas().Parse(Order);
            int order = tree.FirstChildOf(XdmTree.RootNode);

            Assert.IsTrue(tree.HasTypeAnnotations);
            Assert.AreEqual((XsdNamespace, "int"), tree.TypeNameOf(Child(tree, order, 0)));
            Assert.AreEqual((XsdNamespace, "date"), tree.TypeNameOf(Child(tree, order, 1)));
            Assert.AreEqual((XsdNamespace, "ID"), tree.TypeNameOf(tree.AttributeAt(order, 0)));
            Assert.IsFalse(tree.IsNilled(order));
        }

        /// <summary>A tree no schema touched has no annotation to read.</summary>
        [TestMethod]
        public void AnUnvalidatedTreeIsUntyped()
        {
            XdmTree tree = XdmTreeBuilder.FromXml(new StringReader(Order));

            Assert.IsFalse(tree.HasTypeAnnotations);
            Assert.IsNull(tree.TypeNameOf(tree.FirstChildOf(XdmTree.RootNode)));
        }

        /// <summary>A tree the caller built is validated, and comes back typed without being changed.</summary>
        [TestMethod]
        public void ATreeTheCallerBuiltCanBeValidated()
        {
            XdmTree plain = XdmTreeBuilder.FromXml(new StringReader(Order));
            XdmTree typed = Schemas().Validate(plain);

            int order = typed.FirstChildOf(XdmTree.RootNode);

            Assert.IsFalse(plain.HasTypeAnnotations, "the caller's tree was changed");
            Assert.AreEqual((XsdNamespace, "int"), typed.TypeNameOf(Child(typed, order, 0)));
        }

        /// <summary>An invalid document is the error the specification names for strict validation.</summary>
        [TestMethod]
        public void AnInvalidDocumentIsRefused()
        {
            XsltException failed = Assert.ThrowsExactly<XsltException>(
                () => Schemas().Parse("<order xmlns=\"urn:o\"><qty>three</qty></order>"));

            Assert.AreEqual("XTTE1510", failed.Code);

            failed = Assert.ThrowsExactly<XsltException>(
                () => Schemas().Parse("<order xmlns=\"urn:o\"><qty>three</qty></order>", XsltValidation.Lax));

            Assert.AreEqual("XTTE1515", failed.Code);
        }

        /// <summary>Strict validation wants a declaration for the document element, and lax leaves it untyped.</summary>
        [TestMethod]
        public void AnUndeclaredDocumentElementIsStrictAndLax()
        {
            const string stranger = "<stranger xmlns=\"urn:o\"/>";

            XsltException undeclared = Assert.ThrowsExactly<XsltException>(() => Schemas().Parse(stranger));
            Assert.AreEqual("XTTE1512", undeclared.Code, undeclared.Message);

            XdmTree lax = Schemas().Parse(stranger, XsltValidation.Lax);
            Assert.IsNull(lax.TypeNameOf(lax.FirstChildOf(XdmTree.RootNode)));
        }

        /// <summary>Strip and preserve are not validation, and are refused here rather than quietly ignored.</summary>
        [TestMethod]
        public void OnlyStrictAndLaxAreModes()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Schemas().Parse(Order, XsltValidation.Strip));
            Assert.ThrowsExactly<ArgumentException>(() => Schemas().Validate(XdmTreeBuilder.FromXml(new StringReader(Order)), XsltValidation.Preserve));
        }

        // ---- .NET's date range ---------------------------------------------------------------------------

        /// <summary>
        /// A year .NET cannot hold is refused as invalid, and the message says the validator is the cause,
        /// which XSD 1.0 allows and this engine's own dates reach.
        /// </summary>
        [TestMethod]
        public void ADateOutsideDateTimeIsRefusedWithAnExplanation()
        {
            XsltException failed = Assert.ThrowsExactly<XsltException>(
                () => Schemas().Parse("<order xmlns=\"urn:o\"><qty>1</qty><when>-0012-12-03-05:00</when></order>"));

            Assert.AreEqual("XTTE1510", failed.Code);
            StringAssert.Contains(failed.Message, "System.DateTime");
            StringAssert.Contains(failed.Message, "1 to 9999");

            failed = Assert.ThrowsExactly<XsltException>(
                () => Schemas().Parse("<order xmlns=\"urn:o\"><qty>1</qty><when>12345-01-01</when></order>"));

            StringAssert.Contains(failed.Message, "System.DateTime");
        }

        /// <summary>A date that is wrong by any reckoning is not given the excuse.</summary>
        [TestMethod]
        public void ARealMistakeInADateIsNotExcused()
        {
            XsltException failed = Assert.ThrowsExactly<XsltException>(
                () => Schemas().Parse("<order xmlns=\"urn:o\"><qty>1</qty><when>2020-13-45</when></order>"));

            Assert.IsFalse(failed.Message.Contains("System.DateTime", StringComparison.Ordinal), failed.Message);
        }

        // ---- Schema hints --------------------------------------------------------------------------------

        private sealed class Hints : IXsltResolver
        {
            public List<string> Asked { get; } = new();

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                Asked.Add(href);
                return href.EndsWith("order.xsd", StringComparison.Ordinal)
                    ? new ResolvedResource(new StringReader(OrderSchema), "urn:test:order.xsd")
                    : null;
            }
        }

        private const string HintedOrder =
            "<order xmlns=\"urn:o\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\""
            + " xsi:schemaLocation=\"urn:o order.xsd\"><qty>3</qty></order>";

        private const string AskTypes =
            "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
            + " xmlns:o=\"urn:o\" exclude-result-prefixes=\"xs o\"><xsl:output omit-xml-declaration=\"yes\"/>"
            + "<xsl:template match=\"/\"><xsl:value-of select=\"data(/o:order/o:qty) instance of xs:int\"/></xsl:template>"
            + "</xsl:stylesheet>";

        /// <summary>By default a hint in the document is not followed, so strict validation has nothing to go on.</summary>
        [TestMethod]
        public void AHintIsNotFollowedUnlessAsked()
        {
            Hints hints = new Hints();
            XsltOptions options = new XsltOptions
            {
                SchemaAware = true,
                SchemaResolver = hints,
                InputValidation = XsltValidation.Strict,
            };

            Assert.AreEqual("XTTE1512", Assert.ThrowsExactly<XsltException>(
                () => new Xslt(AskTypes, options).TransformXml(HintedOrder)).Code);
            Assert.IsEmpty(hints.Asked);
        }

        /// <summary>Asked to, the engine fetches what the document names through the schema resolver and types it.</summary>
        [TestMethod]
        [DataRow(XsltBackend.Interpreted)]
        [DataRow(XsltBackend.Compiled)]
        public void AHintIsFollowedWhenAsked(XsltBackend backend)
        {
            Hints hints = new Hints();
            XsltOptions options = new XsltOptions
            {
                Backend = backend,
                SchemaAware = true,
                SchemaResolver = hints,
                FollowSchemaLocation = true,
                InputValidation = XsltValidation.Strict,
            };

            Xslt xslt = new Xslt(AskTypes, options);

            Assert.AreEqual("true", xslt.TransformXml(HintedOrder));
            Assert.AreEqual("true", xslt.TransformXml(HintedOrder), "the second read found the shared set changed");
            Assert.IsGreaterThanOrEqualTo(1, hints.Asked.Count);
        }

        /// <summary>A hint that names an invalid document is still invalid.</summary>
        [TestMethod]
        public void AFollowedHintStillValidates()
        {
            XsltOptions options = new XsltOptions
            {
                SchemaAware = true,
                SchemaResolver = new Hints(),
                FollowSchemaLocation = true,
                InputValidation = XsltValidation.Strict,
            };

            string bad = HintedOrder.Replace(">3<", ">three<", StringComparison.Ordinal);

            Assert.AreEqual("XTTE1510", Assert.ThrowsExactly<XsltException>(
                () => new Xslt(AskTypes, options).TransformXml(bad)).Code);
        }

        /// <summary>Following hints needs a resolver to fetch them through, and refuses without one.</summary>
        [TestMethod]
        public void FollowingHintsNeedsAResolver()
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => new Xslt(AskTypes, new XsltOptions { SchemaAware = true, FollowSchemaLocation = true }));
            Assert.ThrowsExactly<ArgumentException>(
                () => new Xslt(AskTypes, new XsltOptions { SchemaResolver = new Hints(), FollowSchemaLocation = true }));
        }

        // ---- What QT3's schema environments found --------------------------------------------------------

        private static string Evaluate(string select, string input)
        {
            string sheet =
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " xmlns:o=\"urn:o\" exclude-result-prefixes=\"xs o\"><xsl:output omit-xml-declaration=\"yes\"/>"
                + $"<xsl:template match=\"/\"><xsl:value-of select=\"{select}\" separator=\",\"/></xsl:template></xsl:stylesheet>";

            XmlSchemaSet set = new XmlSchemaSet();
            set.Add(null, XmlReader.Create(new StringReader(OrderSchema)));

            return new Xslt(
                sheet,
                new XsltOptions { SchemaAware = true, Schemas = set, InputValidation = XsltValidation.Strict })
                .TransformXml(input);
        }

        /// <summary>An array is atomized by <c>cast as</c> and <c>castable as</c>; a map is FOTY0013, not false.</summary>
        [TestMethod]
        public void CastAndCastableAtomizeAnArray()
        {
            Assert.AreEqual("true", Evaluate("[5] castable as xs:integer", Order));
            Assert.AreEqual("true", Evaluate("[[], (), [[3, ()]]] castable as xs:integer", Order));
            Assert.AreEqual("false", Evaluate("[1, 2] castable as xs:integer", Order));
            Assert.AreEqual("5", Evaluate("[5] cast as xs:integer", Order));
            Assert.AreEqual("FOTY0013", Assert.ThrowsExactly<XsltException>(
                () => Evaluate("map{} castable as xs:integer", Order)).Code);
        }

        /// <summary>An element of a list type is an ID only if the list has one item; a union only if the ID member took it.</summary>
        [TestMethod]
        public void AnIdInAListOrUnionIsRecognizedByItsValue()
        {
            const string input =
                "<order xmlns=\"urn:o\"><qty>1</qty>"
                + "<ref>one</ref><ref>two three</ref><alt>four</alt><alt>853</alt></order>";

            Assert.AreEqual(
                "ref,alt",
                Evaluate("(id('one'), id('four'))/local-name()", input));
            Assert.AreEqual("0", Evaluate("count(id('two'))", input));
            Assert.AreEqual("0", Evaluate("count(id('853'))", input));
        }

        /// <summary>xs:numeric is a union of three, so an element validated as one of them is an instance of it.</summary>
        [TestMethod]
        public void NumericIsAnElementType()
        {
            Assert.AreEqual("true", Evaluate("/o:order/o:qty instance of element(*, xs:numeric)", Order));
            Assert.AreEqual("false", Evaluate("/o:order/o:when instance of element(*, xs:numeric)", Order));
        }

        /// <summary>A schema-aware processor types what fn:analyze-string() returns, with no import.</summary>
        [TestMethod]
        public void AnalyzeStringIsTypedWhereTheProcessorIsSchemaAware()
        {
            Assert.AreEqual(
                "true,false",
                Evaluate(
                    "let $r := analyze-string('banana', '(b)(anana)') return"
                    + " (($r//@nr)[1] instance of attribute(nr, xs:positiveInteger), $r instance of element(*, xs:untyped))",
                    Order));
        }

        // ---- An inline schema that includes another ------------------------------------------------------

        private sealed class Parts : IXsltResolver
        {
            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return href.EndsWith("part.xsd", StringComparison.Ordinal)
                    ? new ResolvedResource(
                        new StringReader(
                            "<xs:schema targetNamespace=\"urn:i\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
                            + "<xs:simpleType name=\"small\"><xs:restriction base=\"xs:int\"><xs:maxInclusive value=\"9\"/>"
                            + "</xs:restriction></xs:simpleType></xs:schema>"),
                        "urn:test:part.xsd")
                    : null;
            }
        }

        /// <summary>An inline schema may include another by a relative location, fetched through the schema resolver.</summary>
        [TestMethod]
        public void AnInlineSchemaCanIncludeAnotherThroughTheResolver()
        {
            const string sheet =
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
                + " xmlns:i=\"urn:i\" exclude-result-prefixes=\"xs i\"><xsl:output omit-xml-declaration=\"yes\"/>"
                + "<xsl:import-schema namespace=\"urn:i\"><xs:schema targetNamespace=\"urn:i\"><xs:include schemaLocation=\"part.xsd\"/></xs:schema></xsl:import-schema>"
                + "<xsl:template match=\"/\"><xsl:value-of select=\"(5 cast as i:small, 12 castable as i:small)\" separator=\",\"/></xsl:template>"
                + "</xsl:stylesheet>";

            Assert.AreEqual(
                "5,false",
                new Xslt(sheet, new XsltOptions { SchemaAware = true, SchemaResolver = new Parts() }).TransformXml("<r/>"));
        }


        // ---- Two serialization rules ---------------------------------------------------------------------

        /// <summary>A character map key that is not one character is SEPM0016, in a parameter map.</summary>
        [TestMethod]
        public void ACharacterMapKeyOfTwoCharactersIsSEPM0016()
        {
            Assert.AreEqual("SEPM0016", Assert.ThrowsExactly<XsltException>(
                () => Evaluate("serialize(., map{'use-character-maps': map{'$$': 'x'}})", Order)).Code);
        }

        /// <summary>Standalone is a boolean or exactly the string "omit".</summary>
        [TestMethod]
        public void StandaloneIsABooleanOrOmit()
        {
            Assert.AreEqual("XPTY0004", Assert.ThrowsExactly<XsltException>(
                () => Evaluate("serialize(., map{'standalone': ' omit '})", Order)).Code);
            StringAssert.Contains(
                Evaluate("serialize(., map{'omit-xml-declaration': false(), 'standalone': true()})", Order),
                "standalone=\"yes\"");
            StringAssert.DoesNotMatch(
                Evaluate("serialize(., map{'omit-xml-declaration': false(), 'standalone': 'omit'})", Order),
                new System.Text.RegularExpressions.Regex("standalone"));
        }
    }
}
