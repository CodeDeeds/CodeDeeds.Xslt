using System.Xml;
using System.Xml.Schema;

namespace CodeDeeds.Xslt.UnitTests
{
    /// <summary>
    /// Tests for the first half of schema awareness: importing schemas, and naming the types they define in
    /// sequence types, casts, constructor functions and <c>type-available()</c>.
    /// </summary>
    [TestClass]
    public sealed class SchemaAwarenessTests
    {
        private const string Xsl = "xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
            + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:my=\"urn:my\" xmlns:t=\"urn:t\" exclude-result-prefixes=\"xs my t\"";

        /// <summary>
        /// A schema for documents: a person with typed attributes, an ID, a reference, a list, a union, a
        /// nillable date, and a substitution group.
        /// </summary>
        private const string PeopleSchema =
            "<xs:schema targetNamespace=\"urn:t\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:t=\"urn:t\" elementFormDefault=\"qualified\">"
            + "<xs:simpleType name=\"age\"><xs:restriction base=\"xs:int\">"
            + "<xs:minInclusive value=\"0\"/><xs:maxInclusive value=\"150\"/></xs:restriction></xs:simpleType>"
            + "<xs:simpleType name=\"ages\"><xs:list itemType=\"t:age\"/></xs:simpleType>"
            + "<xs:simpleType name=\"ageOrName\"><xs:union memberTypes=\"t:age xs:NCName\"/></xs:simpleType>"
            + "<xs:complexType name=\"personType\"><xs:sequence>"
            + "<xs:element name=\"name\" type=\"xs:string\"/>"
            + "<xs:element name=\"born\" type=\"xs:date\" nillable=\"true\" minOccurs=\"0\"/>"
            + "<xs:element ref=\"t:pet\" minOccurs=\"0\" maxOccurs=\"unbounded\"/>"
            + "</xs:sequence>"
            + "<xs:attribute name=\"id\" type=\"xs:ID\"/><xs:attribute name=\"age\" type=\"t:age\"/>"
            + "<xs:attribute name=\"kids\" type=\"t:ages\"/><xs:attribute name=\"tag\" type=\"t:ageOrName\"/>"
            + "<xs:attribute name=\"boss\" type=\"xs:IDREF\"/>"
            + "</xs:complexType>"
            + "<xs:element name=\"pet\" type=\"xs:string\"/>"
            + "<xs:element name=\"dog\" substitutionGroup=\"t:pet\"/>"
            + "<xs:element name=\"people\"><xs:complexType><xs:sequence>"
            + "<xs:element name=\"person\" type=\"t:personType\" maxOccurs=\"unbounded\"/>"
            + "</xs:sequence></xs:complexType></xs:element>"
            + "</xs:schema>";

        private const string PeopleImport = "<xsl:import-schema namespace=\"urn:t\">" + PeopleSchema + "</xsl:import-schema>";

        /// <summary>A document valid against <see cref="PeopleSchema"/>.</summary>
        private const string People =
            "<people xmlns=\"urn:t\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
            + "<person id=\"p1\" age=\"30\" kids=\"3 5\" tag=\"7\" boss=\"p2\"><name>Ann</name><born>1990-01-02</born><dog>Rex</dog></person>"
            + "<person id=\"p2\" age=\"52\" tag=\"bob\"><name>Bob</name><born xsi:nil=\"true\"/></person>"
            + "</people>";

        /// <summary>Serves the people document to <c>document()</c> and a second module to <c>xsl:import</c>.</summary>
        private sealed class Files : IXsltResolver
        {
            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                return href switch
                {
                    "people.xml" => new ResolvedResource(new StringReader(People), "urn:test:people.xml"),
                    "preserve.xsl" => new ResolvedResource(
                        new StringReader($"<xsl:stylesheet version=\"3.0\" {Xsl} input-type-annotations=\"preserve\"/>"),
                        "urn:test:preserve.xsl"),
                    _ => null,
                };
            }
        }

        /// <summary>A schema with an atomic, a list, a union and a complex type.</summary>
        private const string Schema =
            "<xs:schema targetNamespace=\"urn:my\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:my=\"urn:my\">"
            + "<xs:simpleType name=\"age\"><xs:restriction base=\"xs:int\">"
            + "<xs:minInclusive value=\"0\"/><xs:maxInclusive value=\"150\"/></xs:restriction></xs:simpleType>"
            + "<xs:simpleType name=\"part\"><xs:restriction base=\"xs:string\">"
            + "<xs:pattern value=\"\\d{3}-[A-Z]{2}\"/></xs:restriction></xs:simpleType>"
            + "<xs:simpleType name=\"parts\"><xs:list itemType=\"my:part\"/></xs:simpleType>"
            + "<xs:simpleType name=\"partOrAge\"><xs:union memberTypes=\"my:part my:age\"/></xs:simpleType>"
            + "<xs:complexType name=\"person\"><xs:sequence><xs:element name=\"name\" type=\"xs:string\"/></xs:sequence></xs:complexType>"
            + "</xs:schema>";

        private const string InlineImport = "<xsl:import-schema namespace=\"urn:my\">" + Schema + "</xsl:import-schema>";

        /// <summary>Serves the schema as a file a stylesheet may name, and counts the asking.</summary>
        private sealed class Schemas : IXsltResolver
        {
            public List<(string Href, string? Base)> Asked { get; } = new();

            public string? Text { get; init; } = Schema;

            public ResolvedResource? Resolve(string href, string? baseUri)
            {
                Asked.Add((href, baseUri));
                return href == "types.xsd" && Text is not null ? new ResolvedResource(new StringReader(Text), "urn:test:types.xsd") : null;
            }
        }

        private static string Sheet(string body, string declarations = InlineImport)
        {
            return $"<xsl:stylesheet version=\"3.0\" {Xsl}>"
                + declarations
                + $"<xsl:template match=\"/\"><out>{body}</out></xsl:template></xsl:stylesheet>";
        }

        private static XsltOptions Options(
            XsltBackend backend,
            bool schemaAware = true,
            IXsltResolver? resolver = null,
            XmlSchemaSet? schemas = null,
            XsltValidation validation = XsltValidation.Strip)
        {
            return new XsltOptions
            {
                Backend = backend,
                OmitXmlDeclaration = true,
                SchemaAware = schemaAware,
                SchemaResolver = resolver,
                Schemas = schemas,
                InputValidation = validation,
                DocumentResolver = new Files(),
                StylesheetResolver = new Files(),
            };
        }

        /// <summary>Options that validate the input strictly, for the typed-input tests.</summary>
        private static XsltOptions Validating(XsltBackend backend) => Options(backend, validation: XsltValidation.Strict);

        /// <summary>A stylesheet importing the people schema, with the body inside the out element.</summary>
        private static string PeopleSheet(string body, string declarations = "", string attributes = "")
        {
            // The declarations first, since an xsl:import among them has to come before everything else;
            // the schema is in scope for them whatever the order, the imports being read first.
            return $"<xsl:stylesheet version=\"3.0\" {Xsl} {attributes}>"
                + declarations
                + PeopleImport
                + $"<xsl:template match=\"/\"><out>{body}</out></xsl:template></xsl:stylesheet>";
        }

        /// <summary>Runs on both backends, which must agree, and returns what the out element holds.</summary>
        private static string Both(string stylesheet, Func<XsltBackend, XsltOptions>? options = null, string input = "<r n=\"42\"/>")
        {
            options ??= backend => Options(backend);
            string interpreted = new Xslt(stylesheet, options(XsltBackend.Interpreted)).TransformXml(input);
            string compiled = new Xslt(stylesheet, options(XsltBackend.Compiled)).TransformXml(input);

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted == "<out/>" ? string.Empty : interpreted["<out>".Length..^"</out>".Length];
        }

        private static string Fails(string stylesheet, Func<XsltBackend, XsltOptions>? options = null, string input = "<r n=\"42\"/>")
        {
            options ??= backend => Options(backend);
            string? interpreted = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, options(XsltBackend.Interpreted)).TransformXml(input)).Code;
            string? compiled = Assert.ThrowsExactly<XsltException>(
                () => new Xslt(stylesheet, options(XsltBackend.Compiled)).TransformXml(input)).Code;

            Assert.AreEqual(interpreted, compiled, "the compiled backend disagreed with the interpreter");
            return interpreted ?? string.Empty;
        }

        private static string Value(string expression)
        {
            return $"<xsl:value-of select=\"{expression}\" separator=\",\"/>";
        }

        private static XmlSchemaSet Compiled()
        {
            XmlSchemaSet set = new XmlSchemaSet();
            set.Add(null, XmlReader.Create(new StringReader(Schema)));
            set.Compile();
            return set;
        }

        [TestMethod]
        public void AnInlineSchemaBringsItsTypesIntoScope()
        {
            // A value of a user-defined type is held as the built-in type beneath it and carries its own:
            // an instance of my:age, of xs:int and of xs:integer, and an integer to arithmetic.
            Assert.AreEqual(
                "true,true,true,false,31,yes",
                Both(Sheet(Value(
                    "my:age(30) instance of my:age, my:age(30) instance of xs:int, "
                    + "my:age(30) instance of xs:integer, my:age(30) instance of my:part, "
                    + "my:age(30) + 1, system-property('xsl:is-schema-aware')"))));

            // type-available() answers for every type the schema defines, complex ones included.
            Assert.AreEqual(
                "true,true,false,true",
                Both(Sheet(Value(
                    "type-available('my:age'), type-available('my:person'), type-available('my:nobody'), "
                    + "type-available('xs:integer')"))));
        }

        [TestMethod]
        public void FacetsAreEnforcedByACastAndAConstructor()
        {
            Assert.AreEqual(
                "true,false,true,0,true",
                Both(Sheet(Value(
                    "'123-AB' castable as my:part, 'abc' castable as my:part, "
                    + "('123-AB' cast as my:part) instance of my:part, count(my:age(())), "
                    + "(xs:integer(5) cast as my:age) instance of my:age"))));

            Assert.AreEqual("FORG0001", Fails(Sheet(Value("my:age(200)"))));
            Assert.AreEqual("FORG0001", Fails(Sheet(Value("'abc' cast as my:part"))));

            // A complex type has no values for a cast to make.
            Assert.AreEqual("XPST0051", Fails(Sheet(Value("'x' cast as my:person"))));
        }

        [TestMethod]
        public void AListTypeCastsToASequenceAndAUnionToAMember()
        {
            Assert.AreEqual(
                "2,true,true,true,true,false",
                Both(Sheet(Value(
                    "count(my:parts('111-AA 222-BB')), my:parts('111-AA 222-BB')[2] instance of my:part, "
                    + "my:partOrAge('7') instance of my:age, my:partOrAge('123-AB') instance of my:part, "
                    + "my:age(7) instance of my:partOrAge, 7 instance of my:partOrAge"))));

            Assert.AreEqual("FORG0001", Fails(Sheet(Value("my:partOrAge('zzz')"))));
            Assert.AreEqual("FORG0001", Fails(Sheet(Value("my:parts('111-AA nope')"))));

            // A list type names a sequence, and a sequence type names an item.
            Assert.AreEqual("XPST0051", Fails(Sheet(Value("'a' instance of my:parts"))));
        }

        [TestMethod]
        public void ADeclaredTypeMayBeASchemaType()
        {
            // An untyped value converts to the declared type by a cast, facets and all; a typed value of
            // another type does not convert, as the function conversion rules have it.
            Assert.AreEqual(
                "true,42,true",
                Both(Sheet(
                    "<xsl:variable name=\"a\" as=\"my:age\" select=\"/r/@n\"/>"
                    + "<xsl:variable name=\"p\" as=\"my:part\" select=\"my:part('123-AB')\"/>"
                    + Value("$a instance of my:age, $a, $p instance of my:part"))));

            // A facet failure in the conversion is reported as the variable's own mismatch, as a failing
            // cast to xs:integer would be.
            Assert.AreEqual(
                "XTTE0570",
                Fails(Sheet("<xsl:variable name=\"a\" as=\"my:age\" select=\"/r/@n\"/>" + Value("$a")), input: "<r n=\"999\"/>"));

            // A string is not untyped, so it is not cast, and the variable does not match its type.
            Assert.AreEqual(
                "XTTE0570",
                Fails(Sheet("<xsl:variable name=\"a\" as=\"my:age\" select=\"'5'\"/>" + Value("$a"))));

            // A function parameter and result too, and a union: '7' from a node is a my:age.
            Assert.AreEqual(
                "true,8",
                Both(Sheet(
                    Value("my:older(/r/@n) instance of my:age, my:older(/r/@n)"),
                    declarations: InlineImport
                        + "<xsl:function name=\"my:older\" as=\"my:age\"><xsl:param name=\"a\" as=\"my:partOrAge\"/>"
                        + "<xsl:sequence select=\"my:age($a + 1)\"/></xsl:function>"),
                    input: "<r n=\"7\"/>"));
        }

        [TestMethod]
        public void NotSchemaAwareUnlessAsked()
        {
            XsltOptions Basic(XsltBackend backend) => Options(backend, schemaAware: false);

            Assert.AreEqual("XTSE1650", Fails(Sheet(Value("1")), Basic));
            Assert.AreEqual(
                "no,false",
                Both(
                    Sheet(Value("system-property('xsl:is-schema-aware'), type-available('my:age')"), declarations: string.Empty),
                    Basic));

            // And a type nobody defined is refused whichever way it is asked for: as a type, and as the
            // function nobody declared either.
            Assert.AreEqual("XPST0051", Fails(Sheet(Value("1 instance of my:nobody"))));
            Assert.AreEqual("XPST0017", Fails(Sheet(Value("my:nobody(1)"))));
        }

        [TestMethod]
        public void ASchemaIsReachedThroughTheSchemaResolver()
        {
            const string ByLocation = "<xsl:import-schema namespace=\"urn:my\" schema-location=\"types.xsd\"/>";

            Schemas schemas = new Schemas();
            Assert.AreEqual(
                "true",
                Both(Sheet(Value("my:age(3) instance of my:age"), ByLocation), backend => Options(backend, resolver: schemas)));
            Assert.AreEqual("types.xsd", schemas.Asked[0].Href);

            // Without a resolver the location cannot be followed, and with one that has nothing the schema
            // is not found: both are XTSE0165, which is about reaching the document. A schema that is
            // reached and is for another namespace is not one that fits, which is XTSE0220.
            Assert.AreEqual("XTSE0165", Fails(Sheet(Value("1"), ByLocation)));
            Assert.AreEqual(
                "XTSE0165",
                Fails(Sheet(Value("1"), ByLocation), backend => Options(backend, resolver: new Schemas { Text = null })));
            Assert.AreEqual(
                "XTSE0220",
                Fails(
                    Sheet(Value("1"), "<xsl:import-schema namespace=\"urn:other\" schema-location=\"types.xsd\"/>"),
                    backend => Options(backend, resolver: new Schemas())));

            // A location and an inline schema are two answers to where the schema is.
            Assert.AreEqual(
                "XTSE0215",
                Fails(
                    Sheet(Value("1"), "<xsl:import-schema namespace=\"urn:my\" schema-location=\"types.xsd\">" + Schema + "</xsl:import-schema>"),
                    backend => Options(backend, resolver: new Schemas())));

            // An inline schema that is not a schema document at all, and one that reads as XML but does
            // not compile: both are a fault in the schema rather than in reaching it, so both are
            // XTSE0220, and both are static.
            Assert.AreEqual(
                "XTSE0220",
                Fails(Sheet(
                    Value("1"),
                    "<xsl:import-schema namespace=\"urn:my\"><xs:schema targetNamespace=\"urn:my\"><xs:nonsense/></xs:schema></xsl:import-schema>")));
            Assert.AreEqual(
                "XTSE0220",
                Fails(Sheet(
                    Value("1"),
                    "<xsl:import-schema namespace=\"urn:my\"><xs:schema targetNamespace=\"urn:my\" xmlns:my=\"urn:my\">"
                    + "<xs:element name=\"e\" type=\"my:nobody\"/></xs:schema></xsl:import-schema>")));
        }

        [TestMethod]
        public void SchemasTheCallerSuppliesAreInScope()
        {
            XmlSchemaSet supplied = Compiled();
            XsltOptions With(XsltBackend backend) => Options(backend, schemas: supplied);

            // Imported by namespace alone, which the supplied set answers; and in scope without any import
            // at all, since the caller put them there.
            Assert.AreEqual(
                "true",
                Both(Sheet(Value("my:age(3) instance of my:age"), "<xsl:import-schema namespace=\"urn:my\"/>"), With));
            Assert.AreEqual("true", Both(Sheet(Value("my:age(3) instance of my:age"), string.Empty), With));

            // A namespace nobody supplies and no resolver knows is not an error until a component of it
            // is named: the stylesheet only said it relies on the namespace.
            Assert.AreEqual("1", Both(Sheet(Value("1"), "<xsl:import-schema namespace=\"urn:nobody\"/>"), With));
            Assert.AreEqual(
                "XPST0051",
                Fails(Sheet(Value("1 instance of Q{urn:nobody}t"), "<xsl:import-schema namespace=\"urn:nobody\"/>"), With));
        }

        [TestMethod]
        public void TheTypesReachAnEvaluatedExpressionAndATransformationStartedByTheStylesheet()
        {
            // The imported types reach a target expression only where schema-aware is yes; that is the
            // specification's default of no made explicit.
            Assert.AreEqual(
                "true",
                Both(Sheet("<xsl:evaluate xpath=\"'my:age(3) instance of my:age'\" schema-aware=\"yes\"/>")));

            // Without it a schema type in the target is XTDE3160, not the XPST0017 an unknown function gets.
            Assert.AreEqual(
                "XTDE3160",
                Fails(Sheet("<xsl:evaluate xpath=\"'my:age(3) instance of my:age'\"/>")));

            // The schemas the caller supplied reach the transformation the stylesheet starts, which imports
            // the namespace and finds it there; the outer stylesheet imports by namespace too, since
            // importing the same schema again inline would declare every type twice (XTSE0220).
            string stylesheet = Sheet(
                Value("transform(map{'stylesheet-text': $sheet, 'initial-template': xs:QName('xsl:initial-template')})?output/v/string()"),
                declarations:
                    "<xsl:import-schema namespace=\"urn:my\"/>"
                    + "<xsl:variable name=\"sheet\" as=\"xs:string\"><![CDATA["
                    + "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:my=\"urn:my\">"
                    + "<xsl:import-schema namespace=\"urn:my\"/>"
                    + "<xsl:template name=\"xsl:initial-template\"><v><xsl:value-of select=\"my:age(3) instance of my:age\"/></v></xsl:template>"
                    + "</xsl:stylesheet>"
                    + "]]></xsl:variable>");

            Assert.AreEqual("true", Both(stylesheet, backend => Options(backend, schemas: Compiled())));
        }

        [TestMethod]
        public void ANodeTestMayNameASchemaTypeAndMatchesNothingYet()
        {
            // Nothing carries an annotation yet, so the test is accepted and matches no node; the built-in
            // names go on answering as they did.
            Assert.AreEqual(
                "0,1,0",
                Both(Sheet(Value(
                    "count(/r/self::element(*, my:person)), count(/r/self::element(*, xs:untyped)), "
                    + "count(/r/@n/self::attribute(*, my:age))"))));
        }

        [TestMethod]
        public void AValidatedInputCarriesItsTypes()
        {
            // Atomization gives what the schema says: an integer to arithmetic, a date, two values for a
            // list, the member a union settled on; the string value is the text as ever.
            Assert.AreEqual(
                "31,true,true,8,true,true,30,true",
                Both(
                    PeopleSheet(Value(
                        "/t:people/t:person[1]/@age + 1, data(/t:people/t:person[1]/@age) instance of t:age, "
                        + "data(/t:people/t:person[1]/t:born) instance of xs:date, sum(/t:people/t:person[1]/@kids), "
                        + "data(/t:people/t:person[1]/@tag) instance of t:age, data(/t:people/t:person[2]/@tag) instance of xs:NCName, "
                        + "string(/t:people/t:person[1]/@age), /t:people/t:person[1]/@age eq 30")),
                    Validating,
                    People));

            // A typed integer is no longer untyped text, so it does not compare with a string.
            Assert.AreEqual(
                "XPTY0004",
                Fails(PeopleSheet(Value("/t:people/t:person[1]/@age = '30'")), Validating, People));

            // A type holding elements and no text has no typed value.
            Assert.AreEqual("FOTY0012", Fails(PeopleSheet(Value("data(/t:people)")), Validating, People));

            // A document the stylesheet fetches for itself is not what the caller asked to be validated,
            // so it is read as it stands: its content is untyped, and arithmetic on it reads the text.
            Assert.AreEqual(
                "31,true",
                Both(
                    PeopleSheet(Value(
                        "document('people.xml')/t:people/t:person[1]/@age + 1, "
                        + "data(document('people.xml')/t:people/t:person[1]/@age) instance of xs:untypedAtomic")),
                    Validating,
                    People));
        }

        [TestMethod]
        public void TypedNodeTestsAndPatternsMatchAnnotations()
        {
            // Eight elements, of which element(*, xs:anyType) matches seven: written without the '?' it
            // does not admit the nilled one, where element(*, xs:anyType?) and element() would.
            Assert.AreEqual(
                "2,2,0,7,0,1,1,1,2,2",
                Both(
                    PeopleSheet(Value(
                        "count(//element(*, t:personType)), count(//attribute(*, t:age)), "
                        + "count(//element(*, xs:untyped)), count(//element(*, xs:anyType)), "
                        + "count(//t:person/@*/self::attribute(*, xs:untypedAtomic)), count(//schema-element(t:pet)), "
                        + "count(//schema-element(t:people)), count(//element(t:born, xs:date)), "
                        + "count(//element(t:born, xs:date?)), count(//element(t:born))")),
                    Validating,
                    People));

            // As patterns: a declaration through its substitution group, and a nilled element left to
            // the test that admits one.
            Assert.AreEqual(
                "born;pet:Rex;nilborn;",
                Both(
                    PeopleSheet(
                        "<xsl:apply-templates select=\"//t:born | //t:dog\"/>",
                        "<xsl:template match=\"schema-element(t:pet)\">pet:<xsl:value-of select=\".\"/>;</xsl:template>"
                        + "<xsl:template match=\"element(t:born, xs:date)\">born;</xsl:template>"
                        + "<xsl:template match=\"t:born\">nilborn;</xsl:template>"),
                    Validating,
                    People));

            // A declaration the schemas do not have is not a syntax error but a missing name.
            Assert.AreEqual("XPST0008", Fails(PeopleSheet(Value("count(//schema-element(t:nobody))")), Validating, People));
        }

        [TestMethod]
        public void NilledAndIdsFollowTheSchema()
        {
            Assert.AreEqual(
                "true,false,0,Bob,1,Ann,Ann",
                Both(
                    PeopleSheet(Value(
                        "nilled(/t:people/t:person[2]/t:born), nilled(/t:people/t:person[1]/t:born), "
                        + "count(data(/t:people/t:person[2]/t:born)), id('p2')/t:name, count(idref('p2')), "
                        + "idref('p2')/../t:name, element-with-id('p1')/t:name")),
                    Validating,
                    People));
        }

        [TestMethod]
        public void AnInvalidOrUndeclaredDocumentIsRefused()
        {
            string invalid = People.Replace("age=\"30\"", "age=\"200\"");
            XsltOptions Lax(XsltBackend backend) => Options(backend, validation: XsltValidation.Lax);

            Assert.AreEqual("XTTE1510", Fails(PeopleSheet(Value("1")), Validating, invalid));
            Assert.AreEqual("XTTE1512", Fails(PeopleSheet(Value("1")), Validating, "<nobody/>"));
            Assert.AreEqual("XTTE1515", Fails(PeopleSheet(Value("1")), Lax, invalid));

            // Lax validation leaves an undeclared document untyped rather than refusing it.
            Assert.AreEqual(
                "1,true",
                Both(PeopleSheet(Value("count(//element(*, xs:untyped)), data(/nobody/@x) instance of xs:untypedAtomic")), Lax, "<nobody x=\"1\"/>"));

            // Validation needs the schemas a schema-aware processor has.
            Assert.ThrowsExactly<ArgumentException>(
                () => new Xslt(PeopleSheet(Value("1")), Options(XsltBackend.Interpreted, schemaAware: false, validation: XsltValidation.Strict)));
        }

        [TestMethod]
        public void InputTypeAnnotationsStripReadsTheDocumentUntyped()
        {
            // Validated still, and then read as a basic processor would: untyped, and nothing nilled.
            Assert.AreEqual(
                "8,true,false,31",
                Both(
                    PeopleSheet(
                        Value(
                            "count(//element(*, xs:untyped)), data(/t:people/t:person[1]/@age) instance of xs:untypedAtomic, "
                            + "nilled(/t:people/t:person[2]/t:born), /t:people/t:person[1]/@age + 1"),
                        attributes: "input-type-annotations=\"strip\""),
                    Validating,
                    People));

            Assert.AreEqual(
                "XTTE1510",
                Fails(PeopleSheet(Value("1"), attributes: "input-type-annotations=\"strip\""), Validating, People.Replace("age=\"30\"", "age=\"200\"")));

            // Two modules cannot ask for opposite things.
            Assert.AreEqual(
                "XTSE0265",
                Fails(PeopleSheet(Value("1"), "<xsl:import href=\"preserve.xsl\"/>", "input-type-annotations=\"strip\""), Validating, People));
        }

        /// <summary>A schema with a global element, a simple type with a facet, an ID and a container.</summary>
        private const string ConstructSchema =
            "<xs:schema targetNamespace=\"urn:c\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:c=\"urn:c\">"
            + "<xs:simpleType name=\"code\"><xs:restriction base=\"xs:string\"><xs:pattern value=\"\\d{3}\"/></xs:restriction></xs:simpleType>"
            + "<xs:element name=\"item\"><xs:complexType><xs:sequence><xs:element name=\"n\" type=\"xs:int\"/></xs:sequence>"
            + "<xs:attribute name=\"id\" type=\"xs:ID\"/><xs:attribute name=\"code\" type=\"c:code\"/></xs:complexType></xs:element>"
            + "<xs:element name=\"box\"><xs:complexType><xs:sequence><xs:element ref=\"c:item\" maxOccurs=\"unbounded\"/></xs:sequence></xs:complexType></xs:element>"
            + "</xs:schema>";

        /// <summary>A stylesheet importing the construction schema, with the body inside the out element.</summary>
        private static string ConstructSheet(string body)
        {
            return "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:c=\"urn:c\" exclude-result-prefixes=\"xs c\">"
                + "<xsl:import-schema namespace=\"urn:c\">" + ConstructSchema + "</xsl:import-schema>"
                + "<xsl:template match=\"/\"><out>" + body + "</out></xsl:template></xsl:stylesheet>";
        }

        [TestMethod]
        public void AConstructedElementIsValidatedAndTyped()
        {
            // A valid element validates strict and its content carries the schema's types.
            Assert.AreEqual(
                "true,true,6",
                Both(ConstructSheet(
                    "<xsl:variable name=\"v\" as=\"element()\"><c:item xsl:validation=\"strict\" id=\"a1\" code=\"123\"><n>5</n></c:item></xsl:variable>"
                    + Value("$v instance of schema-element(c:item), $v/n instance of element(n, xs:int), data($v/n) + 1"))));

            // A facet violation is XTTE1510; an element the schema does not declare is XTTE1512.
            Assert.AreEqual(
                "XTTE1510",
                Fails(ConstructSheet("<c:item xsl:validation=\"strict\" code=\"12\"><n>5</n></c:item>")));
            Assert.AreEqual(
                "XTTE1512",
                Fails(ConstructSheet("<c:nope xsl:validation=\"strict\"/>")));

            // The same through xsl:element, and a named type on it validates against that type.
            Assert.AreEqual(
                "true",
                Both(ConstructSheet(
                    "<xsl:variable name=\"v\" as=\"element()\">"
                    + "<xsl:element name=\"c:item\" namespace=\"urn:c\" validation=\"strict\">"
                    + "<xsl:attribute name=\"code\">321</xsl:attribute><n>1</n></xsl:element></xsl:variable>"
                    + Value("$v instance of schema-element(c:item)"))));
        }

        [TestMethod]
        public void AConstructedAttributeIsValidatedAgainstItsType()
        {
            Assert.AreEqual(
                "true,8",
                Both(ConstructSheet(
                    "<xsl:variable name=\"e\" as=\"element()\"><wrap><xsl:attribute name=\"a\" type=\"xs:int\" select=\"'7'\"/></wrap></xsl:variable>"
                    + Value("$e/@a instance of attribute(a, xs:int), $e/@a + 1"))));

            Assert.AreEqual(
                "XTTE1540",
                Fails(ConstructSheet("<wrap><xsl:attribute name=\"a\" type=\"xs:int\" select=\"'x'\"/></wrap>")));
        }

        [TestMethod]
        public void CopyOfValidatesTheDocumentAndItsIds()
        {
            // Copying a document with a duplicate ID under strict validation is XTTE1555.
            string bad =
                "<xsl:variable name=\"d\"><c:box><c:item id=\"a1\" code=\"123\"><n>1</n></c:item>"
                + "<c:item id=\"a1\" code=\"124\"><n>2</n></c:item></c:box></xsl:variable>";
            Assert.AreEqual("XTTE1555", Fails(ConstructSheet(bad + "<xsl:copy-of select=\"$d\" validation=\"strict\"/>")));

            // A well-formed one validates, and id() then follows the schema-typed ID.
            string good =
                "<xsl:variable name=\"d\"><c:box><c:item id=\"a1\" code=\"123\"><n>1</n></c:item>"
                + "<c:item id=\"a2\" code=\"124\"><n>2</n></c:item></c:box></xsl:variable>"
                + "<xsl:variable name=\"v\"><xsl:copy-of select=\"$d\" validation=\"strict\"/></xsl:variable>";
            Assert.AreEqual("1,123", Both(ConstructSheet(good + Value("count(id('a1', $v)), id('a1', $v)/@code"))));
        }

        [TestMethod]
        public void StripAndPreserveKeepOrDropTheCopiedTypes()
        {
            // A typed element copied with preserve stays typed; with strip it is untyped.
            string typed =
                "<xsl:variable name=\"v\" as=\"element()\"><c:item xsl:validation=\"strict\" code=\"123\"><n>5</n></c:item></xsl:variable>";
            Assert.AreEqual(
                "true,false",
                Both(ConstructSheet(
                    typed
                    + "<xsl:variable name=\"p\" as=\"element()\"><xsl:copy-of select=\"$v\" validation=\"preserve\"/></xsl:variable>"
                    + "<xsl:variable name=\"s\" as=\"element()\"><xsl:copy-of select=\"$v\" validation=\"strip\"/></xsl:variable>"
                    + Value("$p instance of schema-element(c:item), $s instance of schema-element(c:item)"))));
        }

        /// <summary>A schema whose attribute is a type derived from <c>xs:NOTATION</c> by enumeration.</summary>
        private const string NotationSchema =
            "<xs:schema targetNamespace=\"urn:n\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:n=\"urn:n\""
            + " elementFormDefault=\"qualified\">"
            + "<xs:notation name=\"mp3\" public=\"audio/mpeg\" system=\"play.exe\"/>"
            + "<xs:notation name=\"wav\" public=\"audio/wav\" system=\"play.exe\"/>"
            + "<xs:simpleType name=\"kind\"><xs:restriction base=\"xs:NOTATION\">"
            + "<xs:enumeration value=\"n:mp3\"/><xs:enumeration value=\"n:wav\"/></xs:restriction></xs:simpleType>"
            + "<xs:element name=\"items\"><xs:complexType><xs:sequence>"
            + "<xs:element name=\"item\" maxOccurs=\"unbounded\"><xs:complexType>"
            + "<xs:attribute name=\"k\" type=\"n:kind\"/><xs:attribute name=\"name\" type=\"xs:string\"/>"
            + "</xs:complexType></xs:element></xs:sequence></xs:complexType></xs:element>"
            + "</xs:schema>";

        /// <summary>Items whose notation values differ by prefix, by namespace and by local name.</summary>
        private const string NotationDocument =
            "<items xmlns=\"urn:n\" xmlns:n=\"urn:n\" xmlns:alt=\"urn:n\">"
            + "<item k=\"n:mp3\" name=\"a\"/><item k=\"alt:mp3\" name=\"b\"/>"
            + "<item k=\"n:wav\" name=\"c\"/><item k=\"mp3\" name=\"d\"/></items>";

        private static string NotationSheet(string body, string declarations = "")
        {
            return "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
                + " xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:n=\"urn:n\" exclude-result-prefixes=\"xs n\">"
                + "<xsl:import-schema namespace=\"urn:n\">" + NotationSchema + "</xsl:import-schema>"
                + declarations
                + "<xsl:template match=\"/\"><out>" + body + "</out></xsl:template></xsl:stylesheet>";
        }

        [TestMethod]
        public void ANotationTypedAttributeIsANotationAndComparesByExpandedName()
        {
            // The typed value of a NOTATION attribute is a name, not text: it is an xs:NOTATION, it is not
            // an xs:QName, and it keeps the prefix it was written with for its string value.
            Assert.AreEqual(
                "true,false,n:mp3,true,true,false,2,n:mp3 n:wav",
                Both(
                    NotationSheet(Value(
                        "data(/n:items/n:item[1]/@k) instance of xs:NOTATION, "
                        + "data(/n:items/n:item[1]/@k) instance of xs:QName, "
                        + "string(/n:items/n:item[1]/@k), "
                        + "/n:items/n:item[1]/@k eq /n:items/n:item[2]/@k, "
                        + "/n:items/n:item[1]/@k eq /n:items/n:item[4]/@k, "
                        + "/n:items/n:item[1]/@k eq /n:items/n:item[3]/@k, "
                        + "count(distinct-values(/n:items/n:item/@k)), "
                        + "string-join(distinct-values(/n:items/n:item/@k), ' ')")),
                    Validating,
                    NotationDocument));

            // Two names are equal when their namespace and local name are, whatever prefix was written;
            // an unprefixed one takes the default namespace in scope where it stands.
            Assert.AreEqual(
                "true,true,false",
                Both(
                    NotationSheet(Value(
                        "/n:items/n:item[1]/@k eq /n:items/n:item[2]/@k, "
                        + "/n:items/n:item[1]/@k eq /n:items/n:item[4]/@k, "
                        + "/n:items/n:item[1]/@k eq /n:items/n:item[3]/@k")),
                    Validating,
                    NotationDocument));

            // Grouping and distinct-values see the same equality, so the four items make two names.
            Assert.AreEqual(
                "2,n:mp3 n:wav",
                Both(
                    NotationSheet(Value(
                        "count(distinct-values(/n:items/n:item/@k)), "
                        + "string-join(distinct-values(/n:items/n:item/@k), ' ')")),
                    Validating,
                    NotationDocument));

            // xsl:for-each-group and xsl:key file a node under its typed value too, so the three items
            // whose name is n:mp3 group together and are found under it however each was written.
            Assert.AreEqual(
                "a|c|,a b d",
                Both(
                    NotationSheet(
                        "<xsl:for-each-group select=\"/n:items/n:item\" group-by=\"@k\">"
                        + "<xsl:value-of select=\"@name\"/><xsl:text>|</xsl:text></xsl:for-each-group>"
                        + "<xsl:text>,</xsl:text>"
                        + Value("string-join(key('byKind', data(/n:items/n:item[1]/@k))/@name, ' ')"),
                        "<xsl:key name=\"byKind\" match=\"n:item\" use=\"@k\"/>"),
                    Validating,
                    NotationDocument));
        }

        [TestMethod]
        public void ValidationAndTypeAreRefusedTogetherAndAnUnknownTypeIsRejected()
        {
            Assert.AreEqual(
                "XTSE1505",
                Fails(ConstructSheet("<c:item xsl:validation=\"strict\" xsl:type=\"c:code\"/>")));
            Assert.AreEqual(
                "XTSE1520",
                Fails(ConstructSheet("<c:item xsl:type=\"c:nosuch\"/>")));

            // Without schema awareness the same asks are refused as before.
            Assert.AreEqual(
                "XTSE1660",
                Fails("<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">"
                    + "<xsl:template match=\"/\"><e xsl:validation=\"strict\"/></xsl:template></xsl:stylesheet>",
                    backend => Options(backend, schemaAware: false)));
        }

        /// <summary>A schema in a namespace no other test uses, so its type numbers can be counted exactly.</summary>
        private const string CountedSchema =
            "<xs:schema targetNamespace=\"urn:counted\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\""
            + " xmlns:c=\"urn:counted\" elementFormDefault=\"qualified\">"
            + "<xs:simpleType name=\"weight\"><xs:restriction base=\"xs:int\">"
            + "<xs:minInclusive value=\"0\"/></xs:restriction></xs:simpleType>"
            + "<xs:complexType name=\"boxType\"><xs:sequence>"
            + "<xs:element name=\"label\" type=\"xs:string\"/></xs:sequence>"
            + "<xs:attribute name=\"weight\" type=\"c:weight\"/></xs:complexType>"
            + "<xs:element name=\"box\" type=\"c:boxType\"/>"
            + "</xs:schema>";

        [TestMethod]
        public void TheTypeNumbersAreSpentOncePerSchemaAndGivenBack()
        {
            // Each type that annotates a value carries a number out of a table with room for 65,534, and the
            // table is the whole process's. A stylesheet compiled again over the same schema must not spend a
            // second set: a server compiling one stylesheet per request would otherwise exhaust them, and
            // would hold every schema it had ever read for as long as it ran.
            Assert.AreEqual(0, TypeNumbersHeldFor("urn:counted"), "no other test has used this namespace");

            // The compiles happen in a frame of their own, so that the schema is out of reach by the time
            // the last count is taken rather than held by a local nothing reads again.
            (int afterTen, int afterTwenty) = SpendNumbersOverOneSchema();

            Assert.IsGreaterThan(0, afterTen, "the compiles annotated with the schema's own types");

            Assert.AreEqual(
                afterTen,
                afterTwenty,
                "ten further compiles over the same schema spent no further numbers");

            // And the numbers come back once the schema does not exist any more, which is what keeps a long
            // run from filling the table with schemas nothing is using. The built-in types keep theirs,
            // being made once and shared for the life of the process.
            Assert.AreEqual(
                0,
                TypeNumbersHeldFor("urn:counted"),
                "dropping the schema gave the numbers of its types back");
        }

        /// <summary>
        /// Compiles one stylesheet twenty times over a single caller-supplied schema, counting the numbers
        /// its types hold after ten and after twenty.
        /// </summary>
        private static (int AfterTen, int AfterTwenty) SpendNumbersOverOneSchema()
        {
            XmlSchemaSet shared = new XmlSchemaSet();
            shared.Add(XmlSchema.Read(new StringReader(CountedSchema), null)!);
            shared.Compile();

            // The schema comes from the caller rather than an inline xsl:import-schema, so every compile
            // reads the same compiled definitions and should see the same wrappers over them.
            const string Sheet =
                "<xsl:stylesheet version=\"3.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\""
                + " xmlns:c=\"urn:counted\"><xsl:template match=\"/\"><out>"
                + "<xsl:value-of select=\"/c:box/@weight + 1\"/>"
                + "<xsl:value-of select=\"/c:box/@weight instance of attribute(*, c:weight)\"/>"
                + "</out></xsl:template></xsl:stylesheet>";

            const string Input = "<box xmlns=\"urn:counted\" weight=\"3\"><label>a</label></box>";

            for (int i = 0; i < 10; i++)
            {
                new Xslt(Sheet, Options(XsltBackend.Interpreted, schemas: shared, validation: XsltValidation.Strict))
                    .TransformXml(Input);
            }

            int afterTen = TypeNumbersHeldFor("urn:counted");

            for (int i = 0; i < 10; i++)
            {
                new Xslt(Sheet, Options(XsltBackend.Interpreted, schemas: shared, validation: XsltValidation.Strict))
                    .TransformXml(Input);
            }

            int afterTwenty = TypeNumbersHeldFor("urn:counted");

            // Both counts are about a schema that is still in use, so it must not be collected before the
            // second one is taken.
            GC.KeepAlive(shared);
            return (afterTen, afterTwenty);
        }

        /// <summary>
        /// How many type numbers stand for a type of one namespace that still exists, read from the table
        /// itself after a collection so that what has been dropped has actually gone.
        /// </summary>
        private static int TypeNumbersHeldFor(string namespaceUri)
        {
            // Several passes, because what is being asked about is a chain of weak links: dropping a schema
            // set frees its definitions, freeing them clears the entries keyed on them, and only then do the
            // wrappers those entries held become collectable.
            for (int pass = 0; pass < 4; pass++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            GC.Collect();

            Array table = (Array)typeof(Xslt).Assembly
                .GetType("CodeDeeds.Xslt.XPath.XdmSchemaType")!
                .GetField("s_registered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!;

            int held = 0;

            foreach (object? entry in table)
            {
                if (entry is null)
                {
                    continue;
                }

                object?[] target = new object?[1];

                if (!(bool)entry.GetType().GetMethod("TryGetTarget")!.Invoke(entry, target)!)
                {
                    continue;
                }

                object type = target[0]!;

                if (string.Equals(
                    (string?)type.GetType().GetProperty("NamespaceUri")!.GetValue(type),
                    namespaceUri,
                    StringComparison.Ordinal))
                {
                    held++;
                }
            }

            return held;
        }
    }
}
