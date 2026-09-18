using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// The schema components in scope for a stylesheet: what <c>xsl:import-schema</c> brought in and what
    /// the caller supplied, compiled together and looked up by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Over <see cref="XmlSchemaSet"/>, which loads, includes, imports and compiles schema documents and
    /// exposes the component model; this wraps what it compiles as <see cref="XdmSchemaType"/>, one per
    /// definition, so that the rest of the engine reads one small shape. The set is compiled when first
    /// asked and again after a later import, which the set allows.
    /// </para>
    /// <para>
    /// A schema is reached only through <see cref="XsltOptions.SchemaResolver"/>, as a document is only
    /// through the document resolver: loading a definition of what is valid is a third kind of trust,
    /// separate from loading code and loading data. Without a resolver, a <c>schema-location</c> is an
    /// error and an <c>xs:include</c> inside a schema is one too.
    /// </para>
    /// </remarks>
    internal sealed class SchemaComponents
    {
        private readonly XmlSchemaSet m_set;
        private readonly IXsltResolver? m_resolver;
        private readonly XmlResolver? m_locations;
        private readonly List<string> m_problems = new();

        // Guarded by m_lock: the components are read by every transformation over the stylesheet at
        // once, wrapping the types a validated document turns out to hold, and two transformations must
        // not wrap one definition twice.
        private readonly object m_lock = new();
        private readonly Dictionary<XmlQualifiedName, XdmSchemaDeclaration?> m_elements = new();
        private readonly Dictionary<XmlQualifiedName, XdmSchemaDeclaration?> m_attributes = new();
        private bool m_compiled;

        /// <summary>
        /// The wrapper for each compiled type, shared by every set of components in the process and held
        /// only as long as the definition itself is.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A wrapper is a pure reading of its definition, so two stylesheets compiled over one
        /// <see cref="XmlSchemaSet"/> should see one wrapper rather than two. Each wrapper that annotates a
        /// value is given a number out of a table with room for 65,534, and a per-stylesheet cache spent a
        /// fresh set of numbers on every compile — the same schema, compiled a few hundred times, would
        /// exhaust them. Sharing by the identity of the definition spends them once.
        /// </para>
        /// <para>
        /// Weak in its keys, so dropping a schema set drops its definitions, their wrappers and the numbers
        /// they held. A caller who hands the same set to every stylesheet pays for its types once; one who
        /// builds a set per compile pays again each time, and gets the memory back.
        /// </para>
        /// </remarks>
        private static readonly ConditionalWeakTable<XmlSchemaType, XdmSchemaType> s_wrapped = new();

        /// <summary>Guards <see cref="s_wrapped"/>, which is shared and written under recursion.</summary>
        private static readonly object s_wrapLock = new();

        /// <summary>Initializes the components with what the caller supplied, if anything.</summary>
        /// <param name="supplied">Schemas the caller had already loaded, or null.</param>
        /// <param name="resolver">What fetches a schema by location, or null for nothing.</param>
        public SchemaComponents(XmlSchemaSet? supplied, IXsltResolver? resolver)
        {
            m_resolver = resolver;
            m_locations = resolver is null ? null : new SchemaLocationResolver(resolver);
            m_set = new XmlSchemaSet(new NameTable()) { XmlResolver = m_locations };

            m_set.ValidationEventHandler += (_, e) =>
            {
                if (e.Severity == XmlSeverityType.Error)
                {
                    m_problems.Add(e.Message);
                }
            };

            if (supplied is not null)
            {
                // Added to a set of this stylesheet's own, so that what the stylesheet imports does not
                // change the caller's set under it.
                m_set.Add(supplied);
            }
        }

        /// <summary>
        /// Imports a schema, as one <c>xsl:import-schema</c> asks: by location, inline, or by namespace
        /// alone.
        /// </summary>
        /// <param name="targetNamespace">The <c>namespace</c> attribute, or null where absent.</param>
        /// <param name="location">The <c>schema-location</c> attribute, or null.</param>
        /// <param name="inlineSchema">The <c>xs:schema</c> written inside the declaration, serialized, or null.</param>
        /// <param name="baseUri">What a relative location resolves against: where the declaration stands.</param>
        /// <exception cref="XsltException">
        /// <c>XTSE0215</c> for both a location and an inline schema; <c>XTSE0165</c> for a schema that
        /// cannot be found or read.
        /// </exception>
        public void Import(string? targetNamespace, string? location, string? inlineSchema, string? baseUri)
        {
            if (location is not null && inlineSchema is not null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0215,
                    "An xsl:import-schema has both a schema-location and an inline xs:schema, and the "
                    + "schema comes from one or the other.");
            }

            // A namespace already in scope, from the caller or an earlier import, is the whole of what
            // this declaration asks for. §3.14: the namespace attribute "indicates that a schema for the
            // given namespace is required by the stylesheet" and "may be enough on its own to enable an
            // implementation to locate the required schema components", while schema-location "gives a
            // hint indicating where a schema document ... may be found". There is nothing left for the
            // hint to do here, and following it anyway would mean reading a second document for a
            // namespace that already has one — which XSD allows only where the two do not conflict.
            //
            // An inline schema is not covered: it is written where it stands and is read where it stands.
            // Neither is an import that names no namespace, where "already in scope" would be nothing more
            // than some other no-namespace schema having been imported.
            if (inlineSchema is null && targetNamespace is { Length: > 0 } named && m_set.Contains(named))
            {
                return;
            }

            XmlSchema schema;

            if (inlineSchema is not null)
            {
                schema = Read(new StringReader(inlineSchema), baseUri, "the inline schema");
            }
            else if (location is not null)
            {
                ResolvedResource resolved = Fetch(location, baseUri);
                schema = Read(resolved.Reader, resolved.Uri, $"the schema at '{location}'");
            }
            else
            {
                string wanted = targetNamespace ?? string.Empty;

                // Already in scope with no namespace named, which the check above does not cover: still
                // nothing to fetch, the declaration then only saying the stylesheet relies on it.
                if (m_set.Contains(wanted))
                {
                    return;
                }

                // A namespace is a name and not a place, but a resolver may know where a schema for it
                // lives, as a catalog does; asked with the namespace as the reference. One nobody has a
                // schema for is not an error (§3.14): the declaration says the stylesheet relies on the
                // namespace's components, and a component it then names is what is not found. The null
                // namespace is nothing a resolver can be asked for, and the xml namespace's own schema
                // every processor has (XSLT 3.0 §3.14): it is built in, for a resolver that has no other.
                ResolvedResource? located = null;

                if (m_resolver is not null && wanted.Length != 0)
                {
                    try
                    {
                        located = m_resolver.Resolve(wanted, baseUri);
                    }
                    catch (XsltException failed)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.XTSE0165, $"The schema for '{wanted}' could not be read: {failed.Message}", failed);
                    }
                }

                if (located is null && BuiltInSchemaFor(wanted) is string builtIn)
                {
                    located = new ResolvedResource(new StringReader(builtIn), "urn:codedeeds-xslt:schema:" + wanted);
                }

                if (located is null)
                {
                    return;
                }

                schema = Read(located.Reader, located.Uri, $"the schema for '{wanted}'");
            }

            string actual = schema.TargetNamespace ?? string.Empty;

            // What the declaration says the schema is for has to be what the schema says it is for. An
            // xsl:import-schema with no namespace attribute asks for the null namespace, so a document
            // fetched by location must have no target namespace either; an inline schema is left to say
            // for itself, which is how a stylesheet writes one without naming its namespace twice.
            string? declared = targetNamespace ?? (location is not null ? string.Empty : null);

            if (declared is not null && !string.Equals(actual, declared, StringComparison.Ordinal))
            {
                // XTSE0215 is the inline case's own code, the declaration contradicting what it holds;
                // a fetched schema for another namespace is one that does not fit what is in scope.
                throw XsltErrors.Error(
                    inlineSchema is not null ? XsltErrorCode.XTSE0215 : XsltErrorCode.XTSE0220,
                    $"The xsl:import-schema asks for the namespace "
                    + (declared.Length == 0 ? "with no name" : $"'{declared}'")
                    + $", and the schema it imports has the target namespace "
                    + (actual.Length == 0 ? "with no name." : $"'{actual}'."));
            }

            // The same document again, already in scope from the caller or an earlier import, is nothing
            // to add: a schema document imported twice is one schema (XSD §4.2.3), where two documents
            // declaring one type would be a conflict.
            foreach (XmlSchema existing in m_set.Schemas(actual))
            {
                if (SameDocument(existing.SourceUri, schema.SourceUri))
                {
                    return;
                }
            }

            try
            {
                m_set.Add(schema);
            }
            catch (XmlSchemaException failed)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0220,
                    $"The imported schema does not agree with what is already in scope: {failed.Message}",
                    failed);
            }

            // What the set found wrong while adding — a name declared twice in the one document, say —
            // it reports through the handler rather than by throwing, having been given one.
            if (m_problems.Count > 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0220,
                    $"The imported schema does not agree with what is already in scope: {m_problems[0]}");
            }

            m_compiled = false;
        }

        /// <summary>Finds the type a name denotes, built-in or imported, or null where there is none.</summary>
        /// <param name="namespaceUri">The type's namespace URI.</param>
        /// <param name="localName">The type's local name.</param>
        public XdmSchemaType? FindType(string namespaceUri, string localName)
        {
            if (namespaceUri == XdmType.SchemaNamespace)
            {
                return XdmSchemaType.BuiltInNamed(localName);
            }

            EnsureCompiled();

            return m_set.GlobalTypes[new XmlQualifiedName(localName, namespaceUri)] is XmlSchemaType found
                ? Wrap(found)
                : null;
        }

        /// <summary>Whether a type name is in scope, which is what <c>type-available()</c> asks.</summary>
        public bool IsTypeAvailable(string namespaceUri, string localName)
        {
            return FindType(namespaceUri, localName) is not null;
        }

        /// <summary>
        /// The compiled set, for validating a document against: what the stylesheet imported and the
        /// caller supplied, together.
        /// </summary>
        /// <remarks>
        /// Read by every transformation over the stylesheet at once, which a compiled set allows; nothing
        /// is added to it after the stylesheet is compiled, and a validating reader is given no leave to
        /// add what a document's <c>xsi:schemaLocation</c> names.
        /// </remarks>
        public XmlSchemaSet ValidatingSet
        {
            get
            {
                EnsureCompiled();
                return m_set;
            }
        }

        /// <summary>The number a node validated as a type is annotated with; see <see cref="XdmSchemaType.Id"/>.</summary>
        /// <param name="definition">The type the validator settled on.</param>
        public ushort TypeIdOf(XmlSchemaType definition)
        {
            return Wrap(definition).Id;
        }

        /// <summary>Finds a top-level element declaration, or null where the schemas declare none of the name.</summary>
        /// <param name="namespaceUri">The element's namespace URI.</param>
        /// <param name="localName">The element's local name.</param>
        public XdmSchemaDeclaration? FindElement(string namespaceUri, string localName)
        {
            EnsureCompiled();
            XmlQualifiedName name = new XmlQualifiedName(localName, namespaceUri);

            lock (m_lock)
            {
                if (m_elements.TryGetValue(name, out XdmSchemaDeclaration? known))
                {
                    return known;
                }

                XdmSchemaDeclaration? declaration = null;

                if (m_set.GlobalElements[name] is XmlSchemaElement element && element.ElementSchemaType is XmlSchemaType type)
                {
                    declaration = new XdmSchemaDeclaration(
                        namespaceUri, localName, Wrap(type), element.IsNillable, isAttribute: false, SubstitutesOf(name));
                }

                m_elements[name] = declaration;
                return declaration;
            }
        }

        /// <summary>Finds a top-level attribute declaration, or null where the schemas declare none of the name.</summary>
        /// <param name="namespaceUri">The attribute's namespace URI.</param>
        /// <param name="localName">The attribute's local name.</param>
        public XdmSchemaDeclaration? FindAttribute(string namespaceUri, string localName)
        {
            EnsureCompiled();
            XmlQualifiedName name = new XmlQualifiedName(localName, namespaceUri);

            lock (m_lock)
            {
                if (m_attributes.TryGetValue(name, out XdmSchemaDeclaration? known))
                {
                    return known;
                }

                XdmSchemaDeclaration? declaration = null;

                if (m_set.GlobalAttributes[name] is XmlSchemaAttribute attribute
                    && attribute.AttributeSchemaType is XmlSchemaType type)
                {
                    declaration = new XdmSchemaDeclaration(
                        namespaceUri, localName, Wrap(type), nillable: false, isAttribute: true, null);
                }

                m_attributes[name] = declaration;
                return declaration;
            }
        }

        /// <summary>
        /// The names of the top-level elements substitutable for one, through its substitution group at
        /// any remove, or null where none is.
        /// </summary>
        /// <param name="head">The head of the group.</param>
        private HashSet<(string Uri, string Local)>? SubstitutesOf(XmlQualifiedName head)
        {
            HashSet<(string Uri, string Local)>? members = null;

            foreach (XmlSchemaElement element in m_set.GlobalElements.Values)
            {
                XmlQualifiedName name = element.QualifiedName;

                if (name == head)
                {
                    continue;
                }

                // Up the chain of heads, with a bound against a schema whose groups loop.
                XmlQualifiedName group = element.SubstitutionGroup;

                for (int step = 0; step < 64 && !group.IsEmpty; step++)
                {
                    if (group == head)
                    {
                        (members ??= new HashSet<(string, string)>()).Add((name.Namespace, name.Name));
                        break;
                    }

                    group = m_set.GlobalElements[group] is XmlSchemaElement above
                        ? above.SubstitutionGroup
                        : XmlQualifiedName.Empty;
                }
            }

            return members;
        }

        /// <summary>The engine's view of a compiled type, made once per definition.</summary>
        /// <param name="definition">The compiled type.</param>
        public XdmSchemaType Wrap(XmlSchemaType definition)
        {
            XmlQualifiedName name = definition.QualifiedName;

            if (name.Namespace == XdmType.SchemaNamespace
                && !name.IsEmpty
                && XdmSchemaType.BuiltInNamed(name.Name) is XdmSchemaType builtIn)
            {
                return builtIn;
            }

            lock (s_wrapLock)
            {
                if (s_wrapped.TryGetValue(definition, out XdmSchemaType? known))
                {
                    return known;
                }

                XdmSchemaType? baseType = definition.BaseXmlSchemaType is XmlSchemaType declaredBase
                    ? Wrap(declaredBase)
                    : null;

                XdmSchemaVariety variety;
                XdmSchemaType? itemType = null;
                List<XdmSchemaType>? members = null;
                XdmType.BuiltInType? held = null;
                XmlSchemaContentType content = XmlSchemaContentType.Mixed;
                XdmSchemaType? simpleContent = null;

                if (definition is XmlSchemaSimpleType simple)
                {
                    variety = simple.Datatype?.Variety switch
                    {
                        XmlSchemaDatatypeVariety.List => XdmSchemaVariety.List,
                        XmlSchemaDatatypeVariety.Union => XdmSchemaVariety.Union,
                        _ => XdmSchemaVariety.Atomic,
                    };

                    switch (variety)
                    {
                        case XdmSchemaVariety.List:
                            itemType = simple.Content is XmlSchemaSimpleTypeList list && list.BaseItemType is XmlSchemaSimpleType item
                                ? Wrap(item)
                                : baseType?.ItemType;
                            break;

                        case XdmSchemaVariety.Union:
                            members = simple.Content is XmlSchemaSimpleTypeUnion union && union.BaseMemberTypes is not null
                                ? union.BaseMemberTypes.Select(Wrap).ToList()
                                : baseType?.MemberTypes.ToList();
                            break;

                        default:
                            // Held as the nearest built-in ancestor is, which the chain hands down.
                            held = baseType?.BuiltIn;
                            break;
                    }
                }
                else
                {
                    variety = XdmSchemaVariety.Complex;

                    if (definition is XmlSchemaComplexType complex)
                    {
                        content = complex.ContentType;

                        if (content == XmlSchemaContentType.TextOnly)
                        {
                            simpleContent = SimpleContentOf(complex, baseType);
                        }
                    }
                }

                XdmSchemaType wrapped = new XdmSchemaType(
                    name.Namespace, name.Name, definition, variety, baseType, held, itemType, members)
                {
                    Content = content,
                    SimpleContent = simpleContent,
                };

                s_wrapped.AddOrUpdate(definition, wrapped);
                return wrapped;
            }
        }

        /// <summary>
        /// The simple type a complex type with simple content holds its text as: the type it extends
        /// or restricts, followed down until a simple type is reached, or the anonymous simple type a
        /// restriction writes inline.
        /// </summary>
        private XdmSchemaType? SimpleContentOf(XmlSchemaComplexType complex, XdmSchemaType? baseType)
        {
            if (complex.ContentModel is XmlSchemaSimpleContent { Content: XmlSchemaSimpleContentRestriction { BaseType: XmlSchemaSimpleType inline } })
            {
                return Wrap(inline);
            }

            for (XdmSchemaType? type = baseType; type is not null; type = type.BaseType)
            {
                if (type.Variety != XdmSchemaVariety.Complex)
                {
                    return type;
                }

                if (type.SimpleContent is not null)
                {
                    return type.SimpleContent;
                }
            }

            // The datatype .NET settled on names the built-in type beneath whatever was written.
            return complex.Datatype is XmlSchemaDatatype datatype
                && datatype.TypeCode != XmlTypeCode.None
                && XmlSchemaType.GetBuiltInSimpleType(datatype.TypeCode) is XmlSchemaSimpleType builtIn
                ? Wrap(builtIn)
                : null;
        }

        /// <summary>
        /// Compiles what has been imported so far, which is where a schema that reads as XML but is not a
        /// valid schema is found out; called after a module's imports so that the error is static.
        /// </summary>
        /// <exception cref="XsltException"><c>XTSE0220</c> where the schemas do not make a valid whole.</exception>
        public void EnsureCompiled()
        {
            if (m_compiled)
            {
                return;
            }

            lock (m_lock)
            {
                if (m_compiled)
                {
                    return;
                }

                try
                {
                    m_set.Compile();
                }
                catch (XmlSchemaException failed)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0220,
                        $"The imported schemas do not make a valid schema together: {failed.Message}",
                        failed);
                }

                if (m_problems.Count > 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0220,
                        $"The imported schemas do not make a valid schema together: {m_problems[0]}");
                }

                m_compiled = true;
            }
        }

        private ResolvedResource Fetch(string reference, string? baseUri)
        {
            if (m_resolver is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0165,
                    $"The xsl:import-schema asks for '{reference}', and no schema resolver was configured. "
                    + "Set XsltOptions.SchemaResolver to say where schemas may be read from, or supply the "
                    + "schemas in XsltOptions.Schemas.");
            }

            ResolvedResource? resolved;

            try
            {
                resolved = m_resolver.Resolve(reference, baseUri);
            }
            catch (XsltException failed)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0165, $"The schema '{reference}' could not be read: {failed.Message}", failed);
            }

            return resolved
                ?? throw XsltErrors.Error(XsltErrorCode.XTSE0165, $"The schema '{reference}' could not be found.");
        }

        private XmlSchema Read(TextReader text, string? identity, string what)
        {
            List<string> problems = new();

            XmlReaderSettings settings = new XmlReaderSettings
            {
                // A schema document may carry a document type declaration, as the schema for schemas
                // does; what it names is fetched through the same resolver the schema was, or not at all.
                DtdProcessing = DtdProcessing.Parse,
                XmlResolver = m_locations,
                CloseInput = true,
            };

            try
            {
                using XmlReader reader = identity is null
                    ? XmlReader.Create(text, settings)
                    : XmlReader.Create(text, settings, identity);

                XmlSchema? schema = XmlSchema.Read(reader, (_, e) =>
                {
                    if (e.Severity == XmlSeverityType.Error)
                    {
                        problems.Add(e.Message);
                    }
                });

                // Retrieved, and not a schema: that is a fault in the schema rather than in reaching it,
                // which is what tells XTSE0220 from the XTSE0165 of a document nothing could fetch.
                if (schema is null || problems.Count > 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTSE0220,
                        $"{Capitalized(what)} is not a schema document: {(problems.Count > 0 ? problems[0] : "nothing was read")}");
                }

                return schema;
            }
            catch (Exception failed) when (failed is XmlException or XmlSchemaException)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTSE0220, $"{Capitalized(what)} could not be read: {failed.Message}", failed);
            }
        }

        private static string Capitalized(string text)
        {
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
        }

        /// <summary>The schema this engine has built in for a namespace, or null where it has none.</summary>
        /// <param name="namespaceUri">The namespace an <c>xsl:import-schema</c> named with no location.</param>
        internal static string? BuiltInSchemaFor(string namespaceUri)
        {
            return namespaceUri switch
            {
                Model.XdmTree.XmlNamespaceUri => XmlNamespaceSchema,
                "http://www.w3.org/2005/xpath-functions" => JsonSchema,
                _ => null,
            };
        }

        /// <summary>
        /// The schema for the XML representation of JSON that <c>fn:json-to-xml()</c> validates against,
        /// as F&amp;O 3.1 publishes it in C.2.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 publishes another, in B.1, and the two are not the same. The one that matters is that
        /// B.1 gives a keyed <c>map</c> inside a map a <c>key</c> and nothing else, where every other keyed
        /// element takes the group that adds <c>escaped-key</c>: so the result of an <c>escape</c> call whose
        /// nested map has a backslash in its key, which §17.5.3 requires to say <c>escaped-key="true"</c>, is
        /// invalid against it, and the typed result the same section describes — every keyed element saying
        /// whether its key is escaped — cannot be had. C.2 names a type for each keyed element and gives all
        /// six the group, with <c>key</c> required. It also types <c>boolean</c> as a <c>booleanType</c>
        /// derived from <c>xs:boolean</c>, makes <c>numberType</c> a complex type over
        /// <c>finiteNumberType</c>, and lets every element carry attributes in other namespaces. What the
        /// two suites ask of a typed result holds against it: XSLT's <c>element(j:boolean, xs:boolean)</c>
        /// by derivation, and QT3's <c>element(fn:boolean, fn:booleanType)</c>, which B.1 has no type to
        /// answer.
        /// </remarks>
        private const string JsonSchema =
            "<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" elementFormDefault=\"qualified\""
            + " targetNamespace=\"http://www.w3.org/2005/xpath-functions\" xmlns:j=\"http://www.w3.org/2005/xpath-functions\">"
            + "<xs:element name=\"map\" type=\"j:mapType\"><xs:unique name=\"unique-key\">"
            + "<xs:selector xpath=\"*\"/><xs:field xpath=\"@key\"/><xs:field xpath=\"@escaped-key\"/></xs:unique></xs:element>"
            + "<xs:element name=\"array\" type=\"j:arrayType\"/>"
            + "<xs:element name=\"string\" type=\"j:stringType\"/>"
            + "<xs:element name=\"number\" type=\"j:numberType\"/>"
            + "<xs:element name=\"boolean\" type=\"j:booleanType\"/>"
            + "<xs:element name=\"null\" type=\"j:nullType\"/>"
            + "<xs:complexType name=\"nullType\"><xs:sequence/>"
            + "<xs:anyAttribute processContents=\"skip\" namespace=\"##other\"/></xs:complexType>"
            + "<xs:complexType name=\"booleanType\"><xs:simpleContent><xs:extension base=\"xs:boolean\">"
            + "<xs:anyAttribute processContents=\"skip\" namespace=\"##other\"/></xs:extension></xs:simpleContent></xs:complexType>"
            + "<xs:complexType name=\"stringType\"><xs:simpleContent><xs:extension base=\"xs:string\">"
            + "<xs:attribute name=\"escaped\" type=\"xs:boolean\" use=\"optional\" default=\"false\"/>"
            + "<xs:anyAttribute processContents=\"skip\" namespace=\"##other\"/></xs:extension></xs:simpleContent></xs:complexType>"
            + "<xs:simpleType name=\"finiteNumberType\"><xs:restriction base=\"xs:double\">"
            + "<xs:minExclusive value=\"-INF\"/><xs:maxExclusive value=\"INF\"/></xs:restriction></xs:simpleType>"
            + "<xs:complexType name=\"numberType\"><xs:simpleContent><xs:extension base=\"j:finiteNumberType\">"
            + "<xs:anyAttribute processContents=\"skip\" namespace=\"##other\"/></xs:extension></xs:simpleContent></xs:complexType>"
            + "<xs:complexType name=\"arrayType\"><xs:choice minOccurs=\"0\" maxOccurs=\"unbounded\">"
            + "<xs:element ref=\"j:map\"/><xs:element ref=\"j:array\"/><xs:element ref=\"j:string\"/>"
            + "<xs:element ref=\"j:number\"/><xs:element ref=\"j:boolean\"/><xs:element ref=\"j:null\"/></xs:choice>"
            + "<xs:anyAttribute processContents=\"skip\" namespace=\"##other\"/></xs:complexType>"
            + "<xs:complexType name=\"mapWithinMapType\"><xs:complexContent><xs:extension base=\"j:mapType\">"
            + "<xs:attributeGroup ref=\"j:key-group\"/></xs:extension></xs:complexContent></xs:complexType>"
            + "<xs:complexType name=\"arrayWithinMapType\"><xs:complexContent><xs:extension base=\"j:arrayType\">"
            + "<xs:attributeGroup ref=\"j:key-group\"/></xs:extension></xs:complexContent></xs:complexType>"
            + "<xs:complexType name=\"stringWithinMapType\"><xs:simpleContent><xs:extension base=\"j:stringType\">"
            + "<xs:attributeGroup ref=\"j:key-group\"/></xs:extension></xs:simpleContent></xs:complexType>"
            + "<xs:complexType name=\"numberWithinMapType\"><xs:simpleContent><xs:extension base=\"j:numberType\">"
            + "<xs:attributeGroup ref=\"j:key-group\"/></xs:extension></xs:simpleContent></xs:complexType>"
            + "<xs:complexType name=\"booleanWithinMapType\"><xs:simpleContent><xs:extension base=\"j:booleanType\">"
            + "<xs:attributeGroup ref=\"j:key-group\"/></xs:extension></xs:simpleContent></xs:complexType>"
            + "<xs:complexType name=\"nullWithinMapType\"><xs:attributeGroup ref=\"j:key-group\"/></xs:complexType>"
            + "<xs:complexType name=\"mapType\"><xs:choice minOccurs=\"0\" maxOccurs=\"unbounded\">"
            + "<xs:element name=\"map\" type=\"j:mapWithinMapType\"><xs:unique name=\"unique-key-2\">"
            + "<xs:selector xpath=\"*\"/><xs:field xpath=\"@key\"/></xs:unique></xs:element>"
            + "<xs:element name=\"array\" type=\"j:arrayWithinMapType\"/>"
            + "<xs:element name=\"string\" type=\"j:stringWithinMapType\"/>"
            + "<xs:element name=\"number\" type=\"j:numberWithinMapType\"/>"
            + "<xs:element name=\"boolean\" type=\"j:booleanWithinMapType\"/>"
            + "<xs:element name=\"null\" type=\"j:nullWithinMapType\"/></xs:choice>"
            + "<xs:anyAttribute processContents=\"skip\" namespace=\"##other\"/></xs:complexType>"
            + "<xs:attributeGroup name=\"key-group\"><xs:attribute name=\"key\" type=\"xs:string\" use=\"required\"/>"
            + "<xs:attribute name=\"escaped-key\" type=\"xs:boolean\" use=\"optional\" default=\"false\"/></xs:attributeGroup>"
            + "</xs:schema>";

        /// <summary>
        /// The schema for the <c>xml</c> namespace, as the W3C publishes it: the four attributes every
        /// document may carry, and the group a schema refers to them by.
        /// </summary>
        private const string XmlNamespaceSchema =
            "<xs:schema targetNamespace=\"http://www.w3.org/XML/1998/namespace\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">"
            + "<xs:attribute name=\"lang\"><xs:simpleType><xs:union memberTypes=\"xs:language\">"
            + "<xs:simpleType><xs:restriction base=\"xs:string\"><xs:enumeration value=\"\"/></xs:restriction></xs:simpleType>"
            + "</xs:union></xs:simpleType></xs:attribute>"
            + "<xs:attribute name=\"space\"><xs:simpleType><xs:restriction base=\"xs:NCName\">"
            + "<xs:enumeration value=\"default\"/><xs:enumeration value=\"preserve\"/></xs:restriction></xs:simpleType></xs:attribute>"
            + "<xs:attribute name=\"base\" type=\"xs:anyURI\"/>"
            + "<xs:attribute name=\"id\" type=\"xs:ID\"/>"
            + "<xs:attributeGroup name=\"specialAttrs\">"
            + "<xs:attribute ref=\"xml:base\"/><xs:attribute ref=\"xml:lang\"/><xs:attribute ref=\"xml:space\"/><xs:attribute ref=\"xml:id\"/>"
            + "</xs:attributeGroup>"
            + "</xs:schema>";

        /// <summary>Whether two source URIs name one document, allowing for two spellings of a file URI.</summary>
        private static bool SameDocument(string? first, string? second)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
            {
                return false;
            }

            if (string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Uri.TryCreate(first, UriKind.Absolute, out Uri? a)
                && Uri.TryCreate(second, UriKind.Absolute, out Uri? b)
                && a.IsFile
                && b.IsFile
                && string.Equals(a.LocalPath, b.LocalPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Lets the schema set fetch what a schema includes or imports through the caller's resolver.
        /// </summary>
        private sealed class SchemaLocationResolver : XmlResolver
        {
            private readonly IXsltResolver m_inner;

            public SchemaLocationResolver(IXsltResolver inner)
            {
                m_inner = inner;
            }

            public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
            {
                try
                {
                    return base.ResolveUri(baseUri, relativeUri);
                }
                catch (UriFormatException)
                {
                    return new Uri("urn:codedeeds-xslt:schema:" + Uri.EscapeDataString(relativeUri ?? string.Empty));
                }
            }

            public override bool SupportsType(Uri absoluteUri, Type? type)
            {
                return type is null || type == typeof(TextReader) || type == typeof(Stream);
            }

            public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
            {
                // A file URI is handed over as a path, which is what a directory-rooted resolver reads;
                // anything else as the URI it is.
                string reference = absoluteUri.IsFile ? absoluteUri.LocalPath : absoluteUri.OriginalString;

                ResolvedResource resolved = m_inner.Resolve(reference, null)
                    ?? throw new XsltException($"The schema '{reference}' could not be found.");

                if (ofObjectToReturn == typeof(TextReader))
                {
                    return resolved.Reader;
                }

                string text;

                using (resolved.Reader)
                {
                    text = resolved.Reader.ReadToEnd();
                }

                return new MemoryStream(Encoding.UTF8.GetBytes(text));
            }
        }
    }
}
