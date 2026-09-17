using System.Xml;
using System.Xml.Schema;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>How a schema type holds its values: one atomic value, a list of them, one of several types, or content.</summary>
    internal enum XdmSchemaVariety
    {
        /// <summary>One atomic value, built-in or derived from one by restriction.</summary>
        Atomic,

        /// <summary>A whitespace-separated list of values of one item type.</summary>
        List,

        /// <summary>A value of any one of several member types.</summary>
        Union,

        /// <summary>A complex type: attributes and content, which no atomic value is of.</summary>
        Complex,
    }

    /// <summary>
    /// What resolves a type name that is not one of the built-in types: the schema components a stylesheet
    /// imported, or a caller supplied.
    /// </summary>
    /// <remarks>
    /// Internal, and beside <see cref="IXPathStaticContext"/> rather than on it, because what it hands back
    /// is an internal type: a static context that has schema components implements this as well, and the
    /// parser asks for it where a type name is not one it knows.
    /// </remarks>
    internal interface ISchemaTypeProvider
    {
        /// <summary>Finds the type a name denotes among the in-scope schema components, or null.</summary>
        /// <param name="namespaceUri">The type's namespace URI.</param>
        /// <param name="localName">The type's local name.</param>
        XdmSchemaType? ResolveSchemaType(string namespaceUri, string localName);

        /// <summary>Finds the top-level element declaration a name denotes, or null.</summary>
        /// <param name="namespaceUri">The element's namespace URI.</param>
        /// <param name="localName">The element's local name.</param>
        XdmSchemaDeclaration? ResolveElementDeclaration(string namespaceUri, string localName);

        /// <summary>Finds the top-level attribute declaration a name denotes, or null.</summary>
        /// <param name="namespaceUri">The attribute's namespace URI.</param>
        /// <param name="localName">The attribute's local name.</param>
        XdmSchemaDeclaration? ResolveAttributeDeclaration(string namespaceUri, string localName);
    }

    /// <summary>
    /// A top-level element or attribute declaration, which <c>schema-element()</c> and
    /// <c>schema-attribute()</c> name: the name declared, the type declared for it, whether an element
    /// may be nilled, and the names that may stand in for an element's through its substitution group.
    /// </summary>
    internal sealed class XdmSchemaDeclaration
    {
        private readonly IReadOnlySet<(string Uri, string Local)>? m_substitutes;

        internal XdmSchemaDeclaration(
            string namespaceUri,
            string localName,
            XdmSchemaType type,
            bool nillable,
            bool isAttribute,
            IReadOnlySet<(string Uri, string Local)>? substitutes)
        {
            NamespaceUri = namespaceUri;
            LocalName = localName;
            Type = type;
            Nillable = nillable;
            IsAttribute = isAttribute;
            m_substitutes = substitutes;
        }

        /// <summary>The declared name's namespace URI.</summary>
        public string NamespaceUri { get; }

        /// <summary>The declared name's local part.</summary>
        public string LocalName { get; }

        /// <summary>The type the declaration gives its element or attribute.</summary>
        public XdmSchemaType Type { get; }

        /// <summary>Whether an element of this declaration may carry <c>xsi:nil="true"</c>.</summary>
        public bool Nillable { get; }

        /// <summary>Whether this declares an attribute rather than an element.</summary>
        public bool IsAttribute { get; }

        /// <summary>
        /// Whether a name is the declared one or, for an element, one in its substitution group.
        /// </summary>
        /// <param name="namespaceUri">The name's namespace URI.</param>
        /// <param name="localName">The name's local part.</param>
        public bool AdmitsName(string namespaceUri, string localName)
        {
            return (string.Equals(localName, LocalName, StringComparison.Ordinal)
                    && string.Equals(namespaceUri, NamespaceUri, StringComparison.Ordinal))
                || (m_substitutes is not null && m_substitutes.Contains((namespaceUri, localName)));
        }

        /// <summary>The declared name as a test would write it, for diagnostics.</summary>
        public string Written => NamespaceUri.Length == 0 ? LocalName : "Q{" + NamespaceUri + "}" + LocalName;
    }

    /// <summary>
    /// A schema type as this engine sees it: its name, what it derives from, how it holds its values, and
    /// the built-in type beneath it that decides how a value of it is represented.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A small immutable object around <see cref="XmlSchemaType"/>, so that the rest of the engine reads one
    /// shape whether a type is built in or came from a schema. The built-in types are these too, made once
    /// and shared, so that one derivation check serves <c>xs:int</c> and a type restricting it alike: a
    /// user-defined type's base chain runs through the built-in wrappers down to <c>xs:anyType</c>.
    /// </para>
    /// <para>
    /// A value of a user-defined atomic type is held as its nearest built-in ancestor — an integer, a
    /// string, a date — and carries the type beside it, read by the type tests and by nothing else. Each
    /// type that annotates a value is given a small number for the purpose, since the value has room for a
    /// number and not for a reference. The number is handed out on first use and given back when the schema
    /// that defined the type is dropped, so a process that compiles many schemas over its life does not run
    /// out of numbers or hold every schema it ever read.
    /// </para>
    /// </remarks>
    internal sealed class XdmSchemaType
    {
        private static readonly object s_lock = new();
        private static readonly Dictionary<string, XdmSchemaType> s_builtIns = new(StringComparer.Ordinal);
        private static readonly NameTable s_names = new NameTable();

        // The types that have annotated a value or a node, by number. Read without a lock on every node
        // test over a typed tree, so the table is replaced rather than grown in place: a reader holding
        // the old one still finds every number it could have been handed.
        private static WeakReference<XdmSchemaType>?[] s_registered = new WeakReference<XdmSchemaType>?[64];
        private static readonly Queue<int> s_freed = new();
        private static int s_registeredCount = 1;

        private static XdmSchemaType? s_idType;
        private static XdmSchemaType? s_idrefType;

        private volatile ushort m_id;

        internal XdmSchemaType(
            string namespaceUri,
            string localName,
            XmlSchemaType? definition,
            XdmSchemaVariety variety,
            XdmSchemaType? baseType,
            XdmType.BuiltInType? builtIn,
            XdmSchemaType? itemType,
            IReadOnlyList<XdmSchemaType>? memberTypes)
        {
            NamespaceUri = namespaceUri;
            LocalName = localName;
            Definition = definition;
            Variety = variety;
            BaseType = baseType;
            BuiltIn = builtIn;
            ItemType = itemType;
            MemberTypes = memberTypes ?? Array.Empty<XdmSchemaType>();
        }

        /// <summary>The type's namespace URI, empty for a type in no namespace or an anonymous one.</summary>
        public string NamespaceUri { get; }

        /// <summary>The type's local name, empty for an anonymous type.</summary>
        public string LocalName { get; }

        /// <summary>The compiled definition, for facet checking and, later, validation; null for a few built-in names .NET has no object for.</summary>
        public XmlSchemaType? Definition { get; }

        /// <summary>How the type holds its values.</summary>
        public XdmSchemaVariety Variety { get; }

        /// <summary>The type this one derives from, or null for <c>xs:anyType</c>.</summary>
        public XdmSchemaType? BaseType { get; }

        /// <summary>
        /// The built-in atomic type a value of this type is held as: the type's own entry where it is built
        /// in, and its nearest built-in ancestor's where it is not. Null for a list, a union, a complex type
        /// and the four names that stand for a family of types.
        /// </summary>
        public XdmType.BuiltInType? BuiltIn { get; }

        /// <summary>The item type, for a list type.</summary>
        public XdmSchemaType? ItemType { get; }

        /// <summary>The member types, for a union type, in the order the schema lists them.</summary>
        public IReadOnlyList<XdmSchemaType> MemberTypes { get; }

        /// <summary>
        /// What a complex type holds: nothing, text alone, elements alone, or both. Mixed for a simple
        /// type, which the question is not asked of.
        /// </summary>
        public XmlSchemaContentType Content { get; init; } = XmlSchemaContentType.Mixed;

        /// <summary>The simple type a complex type with simple content holds its text as, or null.</summary>
        public XdmSchemaType? SimpleContent { get; init; }

        /// <summary>Whether a value of this type is an ID: <c>xs:ID</c>, or a type restricting it.</summary>
        public bool IsIdType
        {
            get
            {
                s_idType ??= BuiltInNamed("ID");
                return Variety == XdmSchemaVariety.Atomic && s_idType is not null && DerivesFrom(s_idType);
            }
        }

        /// <summary>
        /// Whether a value of this type refers to an ID: <c>xs:IDREF</c> or a restriction of it, or a
        /// list of such, which <c>xs:IDREFS</c> is.
        /// </summary>
        public bool IsIdrefType
        {
            get
            {
                s_idrefType ??= BuiltInNamed("IDREF");

                if (s_idrefType is null)
                {
                    return false;
                }

                return Variety switch
                {
                    XdmSchemaVariety.Atomic => DerivesFrom(s_idrefType),
                    XdmSchemaVariety.List => ItemType is not null && ItemType.IsIdrefType,
                    _ => false,
                };
            }
        }

        /// <summary>
        /// Whether a value of this type may be a name, so that its typed value needs the namespaces in
        /// scope where it was written to be made.
        /// </summary>
        public bool UsesQNames
        {
            get
            {
                switch (Variety)
                {
                    case XdmSchemaVariety.List:
                        return ItemType is not null && ItemType.UsesQNames;

                    case XdmSchemaVariety.Union:
                        foreach (XdmSchemaType member in MemberTypes)
                        {
                            if (member.UsesQNames)
                            {
                                return true;
                            }
                        }

                        return false;

                    case XdmSchemaVariety.Complex:
                        return SimpleContent is not null && SimpleContent.UsesQNames;

                    default:
                        return Primitive == XdmTypeCode.QName;
                }
            }
        }

        /// <summary>Whether this is one of the types every processor has, in the schema namespace.</summary>
        public bool IsBuiltIn => NamespaceUri == XdmType.SchemaNamespace;

        /// <summary>Whether the type has no name of its own, being defined inside a declaration.</summary>
        public bool IsAnonymous => LocalName.Length == 0;

        /// <summary>The representation a value of this type is held in, or <see cref="XdmTypeCode.None"/>.</summary>
        public XdmTypeCode Primitive => BuiltIn?.Code ?? XdmTypeCode.None;

        /// <summary>The nearest built-in derived name a value of this type carries.</summary>
        public XdmType.DerivedType Derived => BuiltIn?.Derived ?? XdmType.DerivedType.None;

        /// <summary>The type as a stylesheet would write it, for diagnostics.</summary>
        public string Written => IsBuiltIn
            ? "xs:" + LocalName
            : IsAnonymous ? "an anonymous type" : "Q{" + NamespaceUri + "}" + LocalName;

        /// <summary>
        /// The number a value annotated with this type carries, handed out on first use.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A value has room for a number and not for a reference, so the table below is what turns the one
        /// back into the other. It holds each type weakly: a schema set that has been dropped takes its
        /// types with it, and the numbers they held are handed out again. Nothing that could still be
        /// carrying such a number is alive by then — a value or an annotated tree is reachable only from a
        /// transformation, which holds the compiled stylesheet, which holds the components that made the
        /// wrapper, so the wrapper cannot be collected while a carrier of its number exists.
        /// </para>
        /// <para>
        /// Sweeping is done only when the table is full, before it would otherwise grow, which keeps the
        /// cost off the ordinary path: a run that annotates with a few hundred types never sweeps at all.
        /// </para>
        /// </remarks>
        /// <exception cref="XsltException">More types have annotated values than the number can tell apart.</exception>
        public ushort Id
        {
            get
            {
                if (m_id == 0)
                {
                    lock (s_lock)
                    {
                        if (m_id == 0)
                        {
                            m_id = Register(this);
                        }
                    }
                }

                return m_id;
            }
        }

        /// <summary>Gives a type a number, reclaiming one from a collected type before taking a new one.</summary>
        private static ushort Register(XdmSchemaType type)
        {
            if (s_freed.Count == 0 && s_registeredCount == s_registered.Length)
            {
                Sweep();
            }

            if (s_freed.Count != 0)
            {
                int reused = s_freed.Dequeue();
                s_registered[reused] = new WeakReference<XdmSchemaType>(type);
                return (ushort)reused;
            }

            if (s_registeredCount >= ushort.MaxValue)
            {
                throw new XsltException(
                    "More than 65,534 schema types are annotating values or nodes in this process at once, "
                    + "which is more than an annotation can tell apart. Sharing one XmlSchemaSet across the "
                    + "stylesheets that use it keeps a schema's types to one set of numbers.");
            }

            if (s_registeredCount == s_registered.Length)
            {
                WeakReference<XdmSchemaType>?[] grown =
                    new WeakReference<XdmSchemaType>?[Math.Min(s_registered.Length * 2, ushort.MaxValue)];

                Array.Copy(s_registered, grown, s_registered.Length);
                Volatile.Write(ref s_registered, grown);
            }

            s_registered[s_registeredCount] = new WeakReference<XdmSchemaType>(type);
            return (ushort)s_registeredCount++;
        }

        /// <summary>Collects the numbers of types nothing holds any more. Called under the lock.</summary>
        private static void Sweep()
        {
            WeakReference<XdmSchemaType>?[] table = s_registered;

            for (int i = 1; i < s_registeredCount; i++)
            {
                if (table[i] is WeakReference<XdmSchemaType> entry && !entry.TryGetTarget(out _))
                {
                    table[i] = null;
                    s_freed.Enqueue(i);
                }
            }
        }

        /// <summary>The type a value's or a node's number stands for, or null for no annotation.</summary>
        public static XdmSchemaType? ById(ushort id)
        {
            if (id == 0)
            {
                return null;
            }

            WeakReference<XdmSchemaType>?[] table = Volatile.Read(ref s_registered);

            return id < table.Length && table[id] is WeakReference<XdmSchemaType> entry
                && entry.TryGetTarget(out XdmSchemaType? type)
                ? type
                : null;
        }

        /// <summary>
        /// The built-in type with a local name, made once and shared, or null where no built-in type has it.
        /// </summary>
        /// <param name="localName">The name written after <c>xs:</c>.</param>
        public static XdmSchemaType? BuiltInNamed(string localName)
        {
            lock (s_builtIns)
            {
                if (s_builtIns.TryGetValue(localName, out XdmSchemaType? known))
                {
                    return known;
                }
            }

            XdmSchemaType? built = BuildBuiltIn(localName);

            if (built is null)
            {
                return null;
            }

            lock (s_builtIns)
            {
                if (!s_builtIns.TryAdd(localName, built))
                {
                    built = s_builtIns[localName];
                }
            }

            return built;
        }

        private static XdmSchemaType? BuildBuiltIn(string localName)
        {
            XmlQualifiedName name = new XmlQualifiedName(localName, XdmType.SchemaNamespace);
            XmlSchemaType? definition = (XmlSchemaType?)XmlSchemaType.GetBuiltInSimpleType(name)
                ?? XmlSchemaType.GetBuiltInComplexType(name);

            bool family = localName is "anyType" or "anySimpleType" or "anyAtomicType" or "untyped";
            XdmType.BuiltInType entry = default;
            bool hasEntry = !family && XdmType.TryGet(localName, out entry);
            bool isList = !family && !hasEntry && XdmType.TryGetList(localName, out entry);

            if (!hasEntry && !isList && !family && definition is null)
            {
                return null;
            }

            XdmSchemaVariety variety = localName is "anyType" or "untyped"
                ? XdmSchemaVariety.Complex
                : isList ? XdmSchemaVariety.List : XdmSchemaVariety.Atomic;

            // The hierarchy the data model draws (XDM §2.7): anyType above everything, anySimpleType below
            // it, anyAtomicType below that and every atomic type below that, which is one step more than
            // .NET's own chain records, and untyped and untypedAtomic where the data model puts them.
            string? baseName = localName switch
            {
                "anyType" => null,
                "untyped" or "anySimpleType" => "anyType",
                "anyAtomicType" => "anySimpleType",
                "untypedAtomic" => "anyAtomicType",
                _ => definition?.BaseXmlSchemaType?.QualifiedName.Name is string declared
                    && declared is not ("anySimpleType" or "anyType")
                    ? declared
                    : isList ? "anySimpleType" : "anyAtomicType",
            };

            return new XdmSchemaType(
                XdmType.SchemaNamespace,
                localName,
                definition,
                variety,
                baseName is null ? null : BuiltInNamed(baseName),
                hasEntry ? entry : null,
                isList ? BuiltInNamed(entry.ItemType!) : null,
                null)
            {
                // The two complex types every processor has hold anything, text included.
                Content = XmlSchemaContentType.Mixed,
            };
        }

        /// <summary>
        /// The typed value of a node annotated with this type, made from the node's string value
        /// (XDM 3.1 §5.1, §5.2).
        /// </summary>
        /// <remarks>
        /// The value a validator settled the text as, made by this engine's own parsing of the text by the
        /// type beneath it: what the validator returned would be .NET's representation rather than this
        /// engine's. The facets an atomic type adds are not checked again, the text having been validated;
        /// a union is cast member by member, facets and all, since which member holds the value is what
        /// the facets decide. A list gives one value per token, a complex type with simple content gives
        /// what its simple type does, one with empty content gives nothing, mixed content gives the text
        /// untyped, and element-only content has no typed value to give.
        /// </remarks>
        /// <param name="text">The node's string value.</param>
        /// <param name="namespaces">The namespaces in scope at the node, wanted where the value is a name.</param>
        /// <exception cref="XsltException"><c>FOTY0012</c> for element-only content.</exception>
        public XPathValue TypedValue(string text, IReadOnlyDictionary<string, string>? namespaces)
        {
            switch (Variety)
            {
                case XdmSchemaVariety.Complex:
                    switch (Content)
                    {
                        case XmlSchemaContentType.TextOnly:
                            return SimpleContent is null
                                ? XPathValue.FromUntypedAtomic(text)
                                : SimpleContent.TypedValue(text, namespaces);

                        case XmlSchemaContentType.Empty:
                            return XPathValue.FromSequence(XdmSequence.Empty);

                        case XmlSchemaContentType.ElementOnly:
                            throw XsltErrors.Error(
                                XsltErrorCode.FOTY0012,
                                $"An element of type {Written} holds elements and no text, so it has no "
                                + "typed value. Ask for its children, or for string() of it.");

                        default:
                            return XPathValue.FromUntypedAtomic(text);
                    }

                case XdmSchemaVariety.Union:
                    return CastToUnion(XPathValue.FromUntypedAtomic(text), namespaces);

                case XdmSchemaVariety.List:
                {
                    XdmSchemaType item = ItemType
                        ?? throw XsltErrors.Error(XsltErrorCode.FORG0001, $"{Written} has no item type.");

                    List<XPathValue> items = new();

                    foreach (string token in text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        items.Add(item.TypedValue(token, namespaces));
                    }

                    return XdmSequence.Concatenate(items);
                }
            }

            // A family name rather than a type, which nothing is validated as: the text stands untyped.
            if (BuiltIn is not XdmType.BuiltInType held)
            {
                return XPathValue.FromUntypedAtomic(text);
            }

            // A name written in a document with no prefix is in the default namespace in scope where it
            // stands, which is what an xs:QName or xs:NOTATION in validated content means by it (XSD §3.2.18)
            // and what tells 'mp3' and 'n:mp3' apart from a name in no namespace.
            if (held.Code == XdmTypeCode.QName && namespaces is not null)
            {
                string bare = text.Trim();

                if (bare.Length != 0
                    && bare.IndexOf(':') < 0
                    && namespaces.TryGetValue(string.Empty, out string? defaultNamespace)
                    && defaultNamespace.Length != 0)
                {
                    XPathValue name = XPathValue
                        .FromQName(new XdmQName(string.Empty, defaultNamespace, bare))
                        .AsDerived(held.Derived);

                    return IsBuiltIn ? name : name.AsSchemaType(this);
                }
            }

            XPathValue value = XdmType.Cast(XPathValue.FromUntypedAtomic(text), held, namespaces);
            return IsBuiltIn ? value : value.AsSchemaType(this);
        }

        /// <summary>
        /// Whether a union type has atomic types alone among its members, at any remove: what XPath calls
        /// a pure union type, whose members' values are its values.
        /// </summary>
        public bool IsPureUnion
        {
            get
            {
                if (Variety != XdmSchemaVariety.Union)
                {
                    return false;
                }

                foreach (XdmSchemaType member in MemberTypes)
                {
                    if (member.Variety == XdmSchemaVariety.List
                        || (member.Variety == XdmSchemaVariety.Union && !member.IsPureUnion))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Whether two types are identical in the sense XSLT's compatibility rules use.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 §3.5.3.3: "Types S and T are considered identical for the purpose of these rules if
        /// and only if <c>subtype(S, T)</c> and <c>subtype(T, S)</c> both hold", with a note drawing out
        /// the consequence that matters: "two plain union types are considered identical if they have the
        /// same set of member types, even if the union types have different names or the ordering of the
        /// member types is different." A union is a set of types written in some order under some name,
        /// and neither the order nor the name is part of what it accepts — so an override may declare its
        /// own union of the same members and still present the interface the original did.
        /// </remarks>
        /// <param name="other">The type compared with.</param>
        public bool IdenticalTo(XdmSchemaType other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return IsPureUnion && other.IsPureUnion && Covers(this, other) && Covers(other, this);
        }

        /// <summary>Whether every member of one union is a member of the other.</summary>
        private static bool Covers(XdmSchemaType one, XdmSchemaType other)
        {
            foreach (XdmSchemaType member in one.MemberTypes)
            {
                bool found = false;

                foreach (XdmSchemaType candidate in other.MemberTypes)
                {
                    if (SameMember(member, candidate))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether two member types of a union are the same type.</summary>
        /// <remarks>
        /// Two schemas naming <c>xs:date</c> usually reach the same object, but need not, so a named type
        /// is compared by its name; an anonymous one has no name to compare and is the object it is.
        /// </remarks>
        private static bool SameMember(XdmSchemaType one, XdmSchemaType other)
        {
            if (ReferenceEquals(one, other))
            {
                return true;
            }

            if (one.IsAnonymous || other.IsAnonymous)
            {
                return one.IdenticalTo(other);
            }

            return string.Equals(one.NamespaceUri, other.NamespaceUri, StringComparison.Ordinal)
                && string.Equals(one.LocalName, other.LocalName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether this type is another, or derives from it (XPath 3.1 §2.5.5): the other is on the base
        /// chain, or is a pure union type this one is a member of, at any remove.
        /// </summary>
        /// <param name="other">The type asked about.</param>
        public bool DerivesFrom(XdmSchemaType other)
        {
            for (XdmSchemaType? type = this; type is not null; type = type.BaseType)
            {
                if (Same(type, other))
                {
                    return true;
                }
            }

            // A union with a list among its members is not its members' supertype: a value of the list
            // type is several values, and the union is what was validated against, not what the value is.
            if (other.IsPureUnion)
            {
                foreach (XdmSchemaType member in other.MemberTypes)
                {
                    if (DerivesFrom(member))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Same(XdmSchemaType first, XdmSchemaType second)
        {
            return ReferenceEquals(first, second)
                || (!first.IsAnonymous
                    && first.LocalName == second.LocalName
                    && first.NamespaceUri == second.NamespaceUri);
        }

        /// <summary>
        /// Whether an atomic value is an instance of this type: annotated with it or a type derived from it,
        /// or, for a built-in type, held as it.
        /// </summary>
        /// <param name="item">The item, which is not a node.</param>
        public bool Accepts(XPathValue item)
        {
            if (item.Kind is XPathValueKind.Node or XPathValueKind.NodeSet || item.IsFunctionItem)
            {
                return false;
            }

            if (item.SchemaType is XdmSchemaType annotated)
            {
                return annotated.DerivesFrom(this);
            }

            switch (Variety)
            {
                case XdmSchemaVariety.Union:
                    foreach (XdmSchemaType member in MemberTypes)
                    {
                        if (member.Accepts(item))
                        {
                            return true;
                        }
                    }

                    return false;

                case XdmSchemaVariety.List:
                case XdmSchemaVariety.Complex:
                    return false;
            }

            // A value carrying no annotation was made as a built-in type, and is an instance of that type
            // and its ancestors alone.
            if (!IsBuiltIn)
            {
                return false;
            }

            if (LocalName is "anyAtomicType" or "anySimpleType")
            {
                return true;
            }

            if (BuiltIn is not XdmType.BuiltInType entry)
            {
                return false;
            }

            XdmTypeCode code = item.TypeCode;
            bool held = code == entry.Code
                || (entry.Code == XdmTypeCode.Decimal && code == XdmTypeCode.Integer)
                || (entry.Code == XdmTypeCode.Duration
                    && code is XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration);

            // A notation is not an xs:QName and a name is not an xs:NOTATION: two primitive types held
            // alike, which only the mark the value carries tells apart.
            if (entry.Code == XdmTypeCode.QName
                && entry.Derived != XdmType.DerivedType.Notation
                && XdmType.DerivesFrom(item.DerivedType, XdmType.DerivedType.Notation))
            {
                return false;
            }

            return held
                && (entry.Derived == XdmType.DerivedType.None
                    || XdmType.DerivesFrom(item.DerivedType, entry.Derived));
        }

        /// <summary>
        /// Casts a value to this type, as <c>cast as</c> and the type's constructor function do.
        /// </summary>
        /// <remarks>
        /// A user-defined atomic type is cast to by casting to the built-in type beneath it and then
        /// checking the facets the schema added, which .NET's own datatype enforces; the result is held as
        /// the built-in type and annotated with this one. A union is cast to member by member, in the order
        /// the schema lists them, and a list by splitting the text and casting each token to the item type.
        /// </remarks>
        /// <param name="value">The value, one atomic value or a node.</param>
        /// <param name="namespaces">The namespaces in scope where the cast is written, for a QName.</param>
        /// <exception cref="XsltException"><c>FORG0001</c> where the value is not one of the type's.</exception>
        public XPathValue Cast(XPathValue value, IReadOnlyDictionary<string, string>? namespaces)
        {
            // A node casts by way of its typed value: its string value, untyped, unless it was validated.
            if (value.Kind is XPathValueKind.Node or XPathValueKind.NodeSet)
            {
                List<XPathValue> items = XdmSequence.Items(value);

                if (items.Count == 1)
                {
                    value = XdmSequence.TypedValueAsOne(items[0], "A cast");

                    if (value.Kind == XPathValueKind.Sequence)
                    {
                        return value;
                    }
                }
            }

            switch (Variety)
            {
                case XdmSchemaVariety.Union:
                    return CastToUnion(value, namespaces);

                case XdmSchemaVariety.List:
                    return CastToList(value, namespaces);

                case XdmSchemaVariety.Complex:
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004, $"{Written} is a complex type, and nothing casts to one.");
            }

            if (IsBuiltIn)
            {
                return BuiltIn is XdmType.BuiltInType own
                    ? XdmType.Cast(value, own, namespaces)
                    : throw XsltErrors.Error(
                        XsltErrorCode.FORG0001,
                        $"{Written} names a family of types rather than one a value can be made into.");
            }

            XdmType.BuiltInType ancestor = BuiltIn
                ?? throw XsltErrors.Error(
                    XsltErrorCode.FORG0001, $"{Written} has no built-in type beneath it to hold a value as.");

            XPathValue held = XdmType.Cast(value, ancestor, namespaces);

            // The facets are checked against the lexical form: the text as written where there was text,
            // and the value's own form where a number or a date arrived typed.
            string lexical = value.TypeCode is XdmTypeCode.String or XdmTypeCode.UntypedAtomic
                ? value.ToStringValue()
                : held.ToStringValue();

            CheckFacets(lexical, namespaces);
            return held.AsSchemaType(this);
        }

        private XPathValue CastToUnion(XPathValue value, IReadOnlyDictionary<string, string>? namespaces)
        {
            XsltException? first = null;

            foreach (XdmSchemaType member in MemberTypes)
            {
                try
                {
                    return member.Cast(value, namespaces);
                }
                catch (XsltException failed)
                {
                    first ??= failed;
                }
            }

            throw XsltErrors.Error(
                XsltErrorCode.FORG0001,
                $"'{value.ToStringValue()}' is not a value of any member type of {Written}."
                + (first is null ? string.Empty : " " + first.Message),
                first!);
        }

        private XPathValue CastToList(XPathValue value, IReadOnlyDictionary<string, string>? namespaces)
        {
            if (value.TypeCode is not (XdmTypeCode.String or XdmTypeCode.UntypedAtomic))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"Only a string casts to the list type {Written}, whose text is split into items.");
            }

            XdmSchemaType item = ItemType
                ?? throw XsltErrors.Error(XsltErrorCode.FORG0001, $"{Written} has no item type.");

            string text = value.ToStringValue();
            CheckFacets(text, namespaces);

            List<XPathValue> items = new();

            foreach (string token in text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                items.Add(item.Cast(XPathValue.FromUntypedAtomic(token), namespaces));
            }

            return XdmSequence.Concatenate(items);
        }

        /// <summary>Checks a lexical form against the type's facets, through .NET's datatype.</summary>
        private void CheckFacets(string lexical, IReadOnlyDictionary<string, string>? namespaces)
        {
            if (Refuses(lexical, namespaces) is string problem)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FORG0001, $"'{lexical}' is not a value of {Written}: {problem}");
            }
        }

        /// <summary>
        /// Why a lexical form is not a value of this simple type, facets and all, or null where it is one:
        /// what validating an attribute's value, or an element's text, against the type asks.
        /// </summary>
        /// <param name="lexical">The text.</param>
        /// <param name="namespaces">The namespaces in scope where the text stands, for a name.</param>
        public string? Refuses(string lexical, IReadOnlyDictionary<string, string>? namespaces)
        {
            XmlSchemaDatatype? datatype = Definition?.Datatype;

            if (datatype is null)
            {
                return null;
            }

            try
            {
                datatype.ParseValue(lexical, s_names, namespaces is null ? null : new NamespaceResolver(namespaces));
                return null;
            }
            catch (Exception failed)
                when (failed is XmlSchemaException or XmlException or FormatException or OverflowException or ArgumentException)
            {
                return failed.Message;
            }
        }

        /// <summary>The namespaces in scope where a cast is written, as the datatype parser wants them.</summary>
        private sealed class NamespaceResolver : IXmlNamespaceResolver
        {
            private readonly IReadOnlyDictionary<string, string> m_namespaces;

            public NamespaceResolver(IReadOnlyDictionary<string, string> namespaces)
            {
                m_namespaces = namespaces;
            }

            public IDictionary<string, string> GetNamespacesInScope(XmlNamespaceScope scope)
            {
                return new Dictionary<string, string>(m_namespaces, StringComparer.Ordinal);
            }

            public string? LookupNamespace(string prefix)
            {
                return m_namespaces.TryGetValue(prefix, out string? uri) ? uri : null;
            }

            public string? LookupPrefix(string namespaceName)
            {
                foreach (KeyValuePair<string, string> binding in m_namespaces)
                {
                    if (binding.Value == namespaceName)
                    {
                        return binding.Key;
                    }
                }

                return null;
            }
        }
    }
}
