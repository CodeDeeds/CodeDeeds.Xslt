using System.Globalization;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// The built-in schema types this engine can construct, and the rules for casting to them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 2.0 turns every built-in atomic type into a function of the same name, so <c>xs:integer("5")</c>
    /// is a call like any other. The types form a hierarchy: <c>xs:int</c> is an <c>xs:long</c> is an
    /// <c>xs:integer</c> is an <c>xs:decimal</c>. Values of the derived integer types are held as
    /// <see cref="XdmTypeCode.Integer"/> and differ only in the range they accept, which is what
    /// <see cref="BuiltInType"/> records.
    /// </para>
    /// <para>
    /// The date, time and duration types are not here. They need their own value representation and a great
    /// deal of lexical parsing, and they are worth doing as a piece rather than half at a time.
    /// </para>
    /// </remarks>
    public static class XdmType
    {
        /// <summary>The XML Schema namespace, which the <c>xs</c> prefix is bound to.</summary>
        public const string SchemaNamespace = "http://www.w3.org/2001/XMLSchema";

        /// <summary>The namespace of the XPath function library, which the <c>fn</c> prefix is bound to.</summary>
        public const string FunctionNamespace = "http://www.w3.org/2005/xpath-functions";

        /// <summary>
        /// Which built-in type a value was made as, where that is narrower than the type it is held in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every derived integer type is held as an <see cref="XdmTypeCode.Integer"/> and every one of the
        /// string-derived types as a string, because that is what they are — <c>xs:int</c> restricts
        /// <c>xs:long</c> and adds nothing to it. But <c>instance of</c> asks which type a value <em>was
        /// given</em>, not which values it could belong to, and those are different questions:
        /// <c>xs:long(1) instance of xs:nonNegativeInteger</c> is false although 1 is one, because the value
        /// was made an <c>xs:long</c> and <c>xs:nonNegativeInteger</c> is not above it in the hierarchy.
        /// </para>
        /// <para>
        /// So the name a value was made under is carried beside the type it is held in. It is read by the
        /// type tests and by nothing else: arithmetic, comparison and every function still see the integer
        /// or the string, which is what keeps this from touching anything.
        /// </para>
        /// </remarks>
        public enum DerivedType : byte
        {
            /// <summary>None: the value is of the type it is held in, and no narrower.</summary>
            None = 0,

            /// <summary><c>xs:long</c>.</summary>
            Long,

            /// <summary><c>xs:int</c>.</summary>
            Int,

            /// <summary><c>xs:short</c>.</summary>
            Short,

            /// <summary><c>xs:byte</c>.</summary>
            Byte,

            /// <summary><c>xs:nonPositiveInteger</c>.</summary>
            NonPositiveInteger,

            /// <summary><c>xs:negativeInteger</c>.</summary>
            NegativeInteger,

            /// <summary><c>xs:nonNegativeInteger</c>.</summary>
            NonNegativeInteger,

            /// <summary><c>xs:positiveInteger</c>.</summary>
            PositiveInteger,

            /// <summary><c>xs:unsignedLong</c>.</summary>
            UnsignedLong,

            /// <summary><c>xs:unsignedInt</c>.</summary>
            UnsignedInt,

            /// <summary><c>xs:unsignedShort</c>.</summary>
            UnsignedShort,

            /// <summary><c>xs:unsignedByte</c>.</summary>
            UnsignedByte,

            /// <summary><c>xs:normalizedString</c>.</summary>
            NormalizedString,

            /// <summary><c>xs:token</c>.</summary>
            Token,

            /// <summary><c>xs:language</c>.</summary>
            Language,

            /// <summary><c>xs:Name</c>.</summary>
            Name,

            /// <summary><c>xs:NMTOKEN</c>.</summary>
            NmToken,

            /// <summary><c>xs:NCName</c>.</summary>
            NCName,

            /// <summary><c>xs:ID</c>.</summary>
            Id,

            /// <summary><c>xs:IDREF</c>.</summary>
            IdRef,

            /// <summary><c>xs:ENTITY</c>.</summary>
            Entity,
        }

        /// <summary>
        /// What a string-derived type does to the text it is given, and what it will not accept.
        /// </summary>
        /// <remarks>
        /// These types all share one representation — a string is a string however it was arrived at — and
        /// differ only in the text they admit. Two things happen. Whitespace is <em>normalized</em> or
        /// <em>collapsed</em>, which changes the value rather than refusing it; and the result is matched
        /// against a pattern, which refuses. A type that is nothing but a name for a restriction is worth
        /// nothing if the restriction is not applied.
        /// </remarks>
        public enum StringFacet : byte
        {
            /// <summary>None: <c>xs:string</c> itself takes any text as it comes.</summary>
            None,

            /// <summary>Tab, newline and carriage return become spaces, as <c>xs:normalizedString</c>.</summary>
            Normalized,

            /// <summary>Whitespace is collapsed and nothing else restricted, as <c>xs:token</c>.</summary>
            Collapsed,

            /// <summary>A language tag: letters, then hyphenated groups of letters and digits.</summary>
            Language,

            /// <summary>An XML Name, which may carry a colon.</summary>
            Name,

            /// <summary>An XML NCName: a Name with no colon, which is what <c>xs:ID</c> and its kin are.</summary>
            NCName,

            /// <summary>An XML Nmtoken: name characters, with nothing required of the first.</summary>
            NmToken,

            /// <summary>A URI reference.</summary>
            Uri,
        }

        /// <summary>One built-in type: what it produces, and what it will accept.</summary>
        /// <param name="Name">The local name, as written after <c>xs:</c>.</param>
        /// <param name="Code">The representation a value of this type is held in.</param>
        /// <param name="Minimum">The smallest value allowed, for the derived integer types.</param>
        /// <param name="Maximum">The largest value allowed.</param>
        /// <param name="Facet">The text restriction, for the string-derived types.</param>
        /// <param name="Derived">
        /// Which derived type this is, where the name is narrower than the representation. This is what a
        /// value made under the name carries, and what <c>instance of</c> asks about.
        /// </param>
        /// <param name="ItemType">
        /// The type each token becomes, for the three list types, and null for everything else. A list type
        /// is not a type a value carries: it names a sequence, and casting to one produces several items.
        /// </param>
        public readonly record struct BuiltInType(
            string Name,
            XdmTypeCode Code,
            decimal Minimum,
            decimal Maximum,
            StringFacet Facet = StringFacet.None,
            DerivedType Derived = DerivedType.None,
            string? ItemType = null);

        /// <summary>
        /// The list types, which a processor without a schema has three of.
        /// </summary>
        /// <remarks>
        /// Kept apart from the atomic types rather than among them, because a list type is not one a value
        /// can be an instance of here: it names a sequence, and this engine reads no schema, so nothing it
        /// holds is ever annotated with one. What it is good for is a cast, which XPath 3.0 defines for
        /// exactly these three (§19.4) — so <c>castable as xs:NMTOKENS</c> is a question about the text and
        /// answerable without a schema, while <c>instance of xs:NMTOKENS</c> is a question about a type
        /// annotation nothing here carries.
        /// </remarks>
        private static readonly Dictionary<string, BuiltInType> s_listTypes = new(StringComparer.Ordinal)
        {
            ["NMTOKENS"] = new BuiltInType(
                "NMTOKENS", XdmTypeCode.String, decimal.MinValue, decimal.MaxValue, ItemType: "NMTOKEN"),
            ["IDREFS"] = new BuiltInType(
                "IDREFS", XdmTypeCode.String, decimal.MinValue, decimal.MaxValue, ItemType: "IDREF"),
            ["ENTITIES"] = new BuiltInType(
                "ENTITIES", XdmTypeCode.String, decimal.MinValue, decimal.MaxValue, ItemType: "ENTITY"),
        };

        private static readonly Dictionary<string, BuiltInType> s_types = BuildTypes();

        private static Dictionary<string, BuiltInType> BuildTypes()
        {
            Dictionary<string, BuiltInType> types = new(StringComparer.Ordinal);

            void Add(string name, XdmTypeCode code, decimal minimum = decimal.MinValue, decimal maximum = decimal.MaxValue)
            {
                types.Add(name, new BuiltInType(name, code, minimum, maximum));
            }

            void AddInteger(string name, DerivedType derived, decimal minimum, decimal maximum)
            {
                types.Add(name, new BuiltInType(
                    name, XdmTypeCode.Integer, minimum, maximum, StringFacet.None, derived));
            }

            void AddText(
                string name,
                StringFacet facet,
                XdmTypeCode code = XdmTypeCode.String,
                DerivedType derived = DerivedType.None)
            {
                types.Add(name, new BuiltInType(
                    name, code, decimal.MinValue, decimal.MaxValue, facet, derived));
            }

            Add("string", XdmTypeCode.String);
            Add("boolean", XdmTypeCode.Boolean);
            Add("double", XdmTypeCode.Double);
            Add("float", XdmTypeCode.Float);
            Add("decimal", XdmTypeCode.Decimal);
            Add("untypedAtomic", XdmTypeCode.UntypedAtomic);

            // xs:anyAtomicType and the string-derived types share the string representation, and differ only
            // in which text they admit.
            Add("anyAtomicType", XdmTypeCode.String);
            Add("untyped", XdmTypeCode.String);

            // xs:error holds no values, so its code is one no value carries. Nothing is an instance of it and
            // nothing casts to it, both of which then follow from the ordinary comparison rather than from a
            // test written for this one type.
            Add("error", XdmTypeCode.Error);

            // xs:numeric names three types rather than one, so no value carries its code either. What a
            // value of it actually is depends on the value, which both the type test and the cast ask.
            Add("numeric", XdmTypeCode.Numeric);

            AddText("anyURI", StringFacet.Uri, XdmTypeCode.AnyUri);
            AddText("normalizedString", StringFacet.Normalized, derived: DerivedType.NormalizedString);
            AddText("token", StringFacet.Collapsed, derived: DerivedType.Token);
            AddText("language", StringFacet.Language, derived: DerivedType.Language);
            AddText("Name", StringFacet.Name, derived: DerivedType.Name);
            AddText("NMTOKEN", StringFacet.NmToken, derived: DerivedType.NmToken);

            // xs:ID, xs:IDREF and xs:ENTITY are each an xs:NCName with a role attached, and the role is
            // something a schema enforces rather than the lexical form. They admit the same text and differ
            // only in the name they were made under, which is exactly what 'instance of' asks about.
            AddText("NCName", StringFacet.NCName, derived: DerivedType.NCName);
            AddText("ID", StringFacet.NCName, derived: DerivedType.Id);
            AddText("IDREF", StringFacet.NCName, derived: DerivedType.IdRef);
            AddText("ENTITY", StringFacet.NCName, derived: DerivedType.Entity);

            Add("date", XdmTypeCode.Date);
            Add("time", XdmTypeCode.Time);
            Add("dateTime", XdmTypeCode.DateTime);
            Add("duration", XdmTypeCode.Duration);
            Add("yearMonthDuration", XdmTypeCode.YearMonthDuration);
            Add("dayTimeDuration", XdmTypeCode.DayTimeDuration);

            Add("QName", XdmTypeCode.QName);
            Add("NOTATION", XdmTypeCode.QName);
            Add("hexBinary", XdmTypeCode.HexBinary);
            Add("base64Binary", XdmTypeCode.Base64Binary);

            foreach (string name in new[] { "gYear", "gYearMonth", "gMonth", "gMonthDay", "gDay" })
            {
                Add(name, XdmTypeCode.Gregorian);
            }

            Add("integer", XdmTypeCode.Integer, long.MinValue, long.MaxValue);
            AddInteger("long", DerivedType.Long, long.MinValue, long.MaxValue);
            AddInteger("int", DerivedType.Int, int.MinValue, int.MaxValue);
            AddInteger("short", DerivedType.Short, short.MinValue, short.MaxValue);
            AddInteger("byte", DerivedType.Byte, sbyte.MinValue, sbyte.MaxValue);
            AddInteger("nonPositiveInteger", DerivedType.NonPositiveInteger, long.MinValue, 0);
            AddInteger("negativeInteger", DerivedType.NegativeInteger, long.MinValue, -1);
            AddInteger("nonNegativeInteger", DerivedType.NonNegativeInteger, 0, long.MaxValue);
            AddInteger("positiveInteger", DerivedType.PositiveInteger, 1, long.MaxValue);
            AddInteger("unsignedLong", DerivedType.UnsignedLong, 0, ulong.MaxValue);
            AddInteger("unsignedInt", DerivedType.UnsignedInt, 0, uint.MaxValue);
            AddInteger("unsignedShort", DerivedType.UnsignedShort, 0, ushort.MaxValue);
            AddInteger("unsignedByte", DerivedType.UnsignedByte, 0, byte.MaxValue);

            return types;
        }

        /// <summary>
        /// Each derived type's immediate parent, which is the hierarchy XML Schema Part 2 lays out.
        /// </summary>
        /// <remarks>
        /// A <see cref="DerivedType.None"/> parent means the type restricts the one it is held in —
        /// <c>xs:long</c> restricts <c>xs:integer</c> and <c>xs:normalizedString</c> restricts
        /// <c>xs:string</c> — so climbing stops there and the representation answers the rest.
        /// </remarks>
        private static readonly DerivedType[] s_parents = BuildParents();

        private static DerivedType[] BuildParents()
        {
            DerivedType[] parents = new DerivedType[Enum.GetValues<DerivedType>().Length];

            void Under(DerivedType derived, DerivedType parent) => parents[(int)derived] = parent;

            // The integers form two chains from xs:integer: one narrowing by width, one by sign.
            Under(DerivedType.Int, DerivedType.Long);
            Under(DerivedType.Short, DerivedType.Int);
            Under(DerivedType.Byte, DerivedType.Short);
            Under(DerivedType.NegativeInteger, DerivedType.NonPositiveInteger);
            Under(DerivedType.PositiveInteger, DerivedType.NonNegativeInteger);
            Under(DerivedType.UnsignedLong, DerivedType.NonNegativeInteger);
            Under(DerivedType.UnsignedInt, DerivedType.UnsignedLong);
            Under(DerivedType.UnsignedShort, DerivedType.UnsignedInt);
            Under(DerivedType.UnsignedByte, DerivedType.UnsignedShort);

            // And the string-derived types form one chain from xs:string, branching at xs:token.
            Under(DerivedType.Token, DerivedType.NormalizedString);
            Under(DerivedType.Language, DerivedType.Token);
            Under(DerivedType.Name, DerivedType.Token);
            Under(DerivedType.NmToken, DerivedType.Token);
            Under(DerivedType.NCName, DerivedType.Name);
            Under(DerivedType.Id, DerivedType.NCName);
            Under(DerivedType.IdRef, DerivedType.NCName);
            Under(DerivedType.Entity, DerivedType.NCName);

            return parents;
        }

        /// <summary>
        /// Whether a value made under one derived type is an instance of another.
        /// </summary>
        /// <remarks>
        /// True where the two are the same, or where the wanted type stands above the value's own in the
        /// hierarchy. Nothing that was made under no derived name is an instance of one: a plain
        /// <c>xs:integer</c> is not an <c>xs:int</c> however small it is, because <c>instance of</c> asks
        /// which type the value was given rather than which types could have held it.
        /// </remarks>
        /// <param name="value">The type a value was made under.</param>
        /// <param name="wanted">The type being asked about.</param>
        public static bool DerivesFrom(DerivedType value, DerivedType wanted)
        {
            while (value != DerivedType.None)
            {
                if (value == wanted)
                {
                    return true;
                }

                value = s_parents[(int)value];
            }

            return false;
        }

        /// <summary>
        /// Whether a built-in type has a constructor function of its own name.
        /// </summary>
        /// <remarks>
        /// XPath 2.0 §3.10.4 withholds one from four types. <c>xs:NOTATION</c> is abstract — a schema derives
        /// from it and nothing is ever of it directly — and the other three name a place in the hierarchy
        /// rather than a set of values, so there is nothing for a constructor to build.
        /// </remarks>
        /// <param name="localName">The name written after <c>xs:</c>.</param>
        public static bool HasConstructor(string localName)
        {
            return localName is not ("NOTATION" or "anyAtomicType" or "anySimpleType" or "untyped");
        }

        /// <summary>Looks up a built-in type by its local name.</summary>
        /// <param name="localName">The name written after <c>xs:</c>.</param>
        /// <param name="type">On success, the type.</param>
        /// <returns><see langword="true"/> if the name is a type this engine can construct.</returns>
        public static bool TryGet(string localName, out BuiltInType type)
        {
            return s_types.TryGetValue(localName, out type);
        }

        /// <summary>Looks up one of the three list types a cast may name.</summary>
        /// <param name="localName">The name written after <c>xs:</c>.</param>
        /// <param name="type">On success, the type.</param>
        /// <returns><see langword="true"/> if the name is a list type.</returns>
        public static bool TryGetList(string localName, out BuiltInType type)
        {
            return s_listTypes.TryGetValue(localName, out type);
        }

        /// <summary>
        /// Casts a value to a built-in type, as the type's constructor function does.
        /// </summary>
        /// <param name="value">The value to cast.</param>
        /// <param name="type">The type to cast it to.</param>
        /// <returns>The converted value.</returns>
        /// <exception cref="XsltException">The value is not in the type's lexical space or its range.</exception>
        /// <summary>
        /// Casts text to a name, resolving any prefix against the namespaces the cast was written among.
        /// </summary>
        /// <remarks>
        /// A prefix means whatever it meant where the cast stands, so the bindings have to travel with the
        /// expression: XPath 2.0 would not have it at all outside a literal, and 3.0 settled that the static
        /// context is what answers. Where no bindings came with the cast — a context that has none to give —
        /// only a name without a prefix can be cast.
        /// </remarks>
        /// <param name="text">The lexical name.</param>
        /// <param name="namespaces">The bindings in scope where the cast was written.</param>
        /// <summary>
        /// Refuses a cast to <c>xs:QName</c> from anything but a literal, which is all XPath 2.0 allowed.
        /// </summary>
        /// <remarks>
        /// A prefix is resolved against the static context, so XPath 2.0 would only cast what the compiler
        /// could see: a string literal, folded where it stands. XPath 3.0 kept the static context as the
        /// answer and dropped the restriction, so this is a question about the version the processor is
        /// offering and not about the value (XPath 3.0 §19.1).
        /// </remarks>
        /// <param name="type">The type being cast to.</param>
        /// <param name="value">The expression giving the value.</param>
        /// <param name="syntaxVersion">The version whose grammar is on offer.</param>
        internal static void RequireLiteralNameBelowThree(
            BuiltInType type, Expr value, XsltVersion syntaxVersion)
        {
            if (type.Code != XdmTypeCode.QName
                || value is StringLiteralExpr
                || syntaxVersion.CompareTo(XsltVersion.V30) >= 0)
            {
                return;
            }

            throw XsltErrors.Error(
                XsltErrorCode.XPTY0004,
                "Casting to xs:QName takes a string literal before XPath 3.0: a prefix means what it meant "
                + "where it was written, and only a literal was there to be read then.");
        }

        internal static XPathValue CastToQName(string text, IReadOnlyDictionary<string, string>? namespaces)
        {
            if (!XdmQName.TrySplit(text, out string prefix, out string localName))
            {
                throw XsltErrors.Error(XsltErrorCode.FORG0001, $"'{text}' is not a valid xs:QName.");
            }

            if (prefix.Length == 0)
            {
                return XPathValue.FromQName(new XdmQName(string.Empty, string.Empty, localName));
            }

            if (prefix == "xml")
            {
                return XPathValue.FromQName(new XdmQName(prefix, Model.XdmTree.XmlNamespaceUri, localName));
            }

            if (namespaces is not null && namespaces.TryGetValue(prefix, out string? uri))
            {
                return XPathValue.FromQName(new XdmQName(prefix, uri, localName));
            }

            throw XsltErrors.Error(
                XsltErrorCode.FONS0004,
                $"'{text}' carries the prefix '{prefix}', and nothing where the cast is written binds it.");
        }

        public static XPathValue Cast(
            XPathValue value, BuiltInType type, IReadOnlyDictionary<string, string>? namespaces = null)
        {
            // A node casts by way of its typed value — its string value, untyped, unless the node was
            // validated — for the constructor function as for 'cast as'. Left as a node, xs:boolean() read
            // it as a number, which it is not, and said no.
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

            // The result carries the name it was made under, which is narrower than the type it is held in
            // for the derived integers and the string-derived types. Nothing but 'instance of' reads it, and
            // casting to a name that is not a derived one clears whatever the value arrived with.
            return Convert(value, type, namespaces).AsDerived(type.Derived);
        }

        private static XPathValue Convert(
            XPathValue value, BuiltInType type, IReadOnlyDictionary<string, string>? namespaces)
        {
            // xs:error admits no values at all, so every cast to it fails. FORG0001 rather than XPTY0004:
            // the complaint is that the value is outside the type's space, which is what that code says, and
            // there is no pair of types here for the "no route between them" reading to be about.
            if (type.Code == XdmTypeCode.Error)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FORG0001,
                    "xs:error holds no values, so nothing can be cast to it.");
            }

            // A cast to a union type keeps a value that is already one of its members, so 17 stays an
            // xs:integer here rather than becoming 17e0 — the value is of the type asked for, and a cast has
            // nothing to do. Anything that is no number at all is converted to xs:double, the first member
            // of the union that admits it, which is where 'true() cast as xs:numeric' gets 1e0.
            if (type.Code == XdmTypeCode.Numeric)
            {
                return XdmComparison.IsNumeric(value.TypeCode)
                    ? value
                    : Cast(value, s_types["double"]);
            }

            // A list type is built by splitting the text and casting each token, which is the one cast
            // that produces more than a single item.
            if (type.ItemType is string item)
            {
                return CastToList(value, type, item);
            }

            if (!IsCastable(value, type))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"There is no cast from {NameOf(value)} to xs:{type.Name}. The two types have no route "
                    + "between them, so this is refused rather than answered by way of the text.");
            }

            switch (type.Code)
            {
                // The canonical form throughout: casting is a 2.0 operation, and a cast to text is where a
                // number's lexical form is decided. It is also the one every function argument declared
                // xs:string goes through, so routing it here is what keeps string(1 div 0) and
                // concat(1 div 0, '') saying the same thing.
                case XdmTypeCode.String:
                    return XPathValue.FromString(ApplyFacet(value.ToCanonicalString(), type));

                case XdmTypeCode.AnyUri:
                    return XPathValue.FromAnyUri(ApplyFacet(value.ToCanonicalString(), type));

                case XdmTypeCode.UntypedAtomic:
                    return XPathValue.FromUntypedAtomic(value.ToCanonicalString());

                case XdmTypeCode.Boolean:
                    return XPathValue.FromBoolean(CastToBoolean(value));

                case XdmTypeCode.Double:
                    return XPathValue.FromNumber(CastToDouble(value, "xs:double"));

                case XdmTypeCode.Float:
                    // Held as a double that has been through float, so that the value is one a float can hold.
                    return XPathValue.FromFloat((float)CastToDouble(value, "xs:float"));

                case XdmTypeCode.Decimal:
                    return XPathValue.FromDecimal(CastToDecimal(value));

                case XdmTypeCode.Date:
                case XdmTypeCode.Time:
                case XdmTypeCode.DateTime:
                {
                    // A value that is already of the type passes through; anything else is read from its
                    // lexical form, which is the only way into these types short of the component functions.
                    if (value.TypeCode == type.Code)
                    {
                        return value;
                    }

                    // Between the three, the value is kept and the parts the target does not have are
                    // dropped: casting a dateTime to a date keeps the day and forgets the hour. Going the
                    // other way, a date becomes midnight — the only time of day it can be said to name.
                    if (value.TypeCode is XdmTypeCode.Date or XdmTypeCode.Time or XdmTypeCode.DateTime)
                    {
                        return XPathValue.FromDateTime(NarrowMoment(value.AsDateTime(), type.Code));
                    }

                    string lexical = value.ToStringValue();

                    return XdmDateTime.Read(lexical, type.Code, out XdmDateTime moment) switch
                    {
                        XdmDateTime.Reading.Value => XPathValue.FromDateTime(moment),

                        // A moment this engine cannot hold is an overflow rather than a bad value, and the
                        // two carry different codes so that a limit is not read as a typo.
                        XdmDateTime.Reading.OutOfRange => throw XsltErrors.Error(
                            XsltErrorCode.FODT0001,
                            $"'{lexical}' names a moment outside the range this engine holds dates in, "
                            + "which is the common era up to the year 9999."),

                        _ => throw XsltErrors.Error(
                            XsltErrorCode.FORG0001, $"'{lexical}' is not a valid xs:{type.Name}."),
                    };
                }

                case XdmTypeCode.Duration:
                case XdmTypeCode.YearMonthDuration:
                case XdmTypeCode.DayTimeDuration:
                {
                    if (value.TypeCode == type.Code)
                    {
                        return value;
                    }

                    // Casting between the duration types keeps only the half the target has, which is the one
                    // place where dropping part of a value is what the specification asks for.
                    if (value.TypeCode is XdmTypeCode.Duration or XdmTypeCode.YearMonthDuration
                        or XdmTypeCode.DayTimeDuration)
                    {
                        XdmDuration source = value.AsDuration();

                        return XPathValue.FromDuration(new XdmDuration(
                            type.Code == XdmTypeCode.DayTimeDuration ? 0 : source.Months,
                            type.Code == XdmTypeCode.YearMonthDuration ? 0m : source.Seconds,
                            type.Code));
                    }

                    return XdmDuration.TryParse(value.ToStringValue(), type.Code, out XdmDuration duration)
                        ? XPathValue.FromDuration(duration)
                        : throw XsltErrors.Error(
                            XsltErrorCode.FORG0001,
                            $"'{value.ToStringValue()}' is not a valid xs:{type.Name}.");
                }

                case XdmTypeCode.QName:
                {
                    if (value.TypeCode == XdmTypeCode.QName)
                    {
                        return value;
                    }

                    return CastToQName(value.ToStringValue(), namespaces);
                }

                case XdmTypeCode.HexBinary:
                case XdmTypeCode.Base64Binary:
                {
                    // Re-spelling rather than converting: the two types are two ways of writing the same
                    // bytes, so a cast between them keeps the value and changes only how it reads.
                    if (value.TypeCode is XdmTypeCode.HexBinary or XdmTypeCode.Base64Binary)
                    {
                        return XPathValue.FromBinary(new XdmBinary(value.AsBinary().Bytes, type.Code));
                    }

                    return XdmBinary.TryParse(value.ToStringValue(), type.Code, out XdmBinary? binary)
                        ? XPathValue.FromBinary(binary!)
                        : throw XsltErrors.Error(
                            XsltErrorCode.FORG0001,
                            $"'{value.ToStringValue()}' is not a valid xs:{type.Name}.");
                }

                case XdmTypeCode.Gregorian:
                {
                    if (value.TypeCode == XdmTypeCode.Gregorian
                        && value.AsGregorian().Name == type.Name)
                    {
                        return value;
                    }

                    // A date or dateTime holds every component the five gregorian types ask for, so the cast
                    // is a matter of keeping the ones the target names.
                    if (value.TypeCode is XdmTypeCode.Date or XdmTypeCode.DateTime)
                    {
                        return XPathValue.FromGregorian(PartOf(value.AsDateTime(), type.Name));
                    }

                    return XdmGregorian.TryParse(value.ToStringValue(), type.Name, out XdmGregorian? gregorian)
                        ? XPathValue.FromGregorian(gregorian!)
                        : throw XsltErrors.Error(
                            XsltErrorCode.FORG0001,
                            $"'{value.ToStringValue()}' is not a valid xs:{type.Name}.");
                }

                default:
                    return XPathValue.FromInteger(CastToInteger(value, type));
            }
        }

        /// <summary>
        /// Normalizes text for a string-derived type and refuses what that type does not admit.
        /// </summary>
        /// <remarks>
        /// Whitespace first, because the pattern is matched against the collapsed form: <c>xs:NMTOKEN(' f
        /// f')</c> collapses to <c>f f</c>, which still holds a space and so is not one token.
        /// </remarks>
        /// <param name="text">The text as given.</param>
        /// <param name="type">The type being cast to.</param>
        /// <exception cref="XsltException">The text is not in the type's lexical space.</exception>
        private static string ApplyFacet(string text, BuiltInType type)
        {
            switch (type.Facet)
            {
                case StringFacet.None:
                    return text;

                case StringFacet.Normalized:
                    return Replace(text);

                case StringFacet.Collapsed:
                    return Collapse(text);
            }

            // Every remaining facet derives from xs:token, so the text is collapsed before it is judged.
            string collapsed = type.Facet == StringFacet.Uri ? text.Trim() : Collapse(text);

            bool valid = type.Facet switch
            {
                StringFacet.Language => IsLanguage(collapsed),
                StringFacet.Name => IsName(collapsed, allowColon: true),
                StringFacet.NCName => IsName(collapsed, allowColon: false),
                StringFacet.NmToken => IsNmToken(collapsed),
                _ => IsUriReference(collapsed),
            };

            return valid
                ? collapsed
                : throw XsltErrors.Error(
                    XsltErrorCode.FORG0001, $"'{text}' is not a valid xs:{type.Name}.");
        }

        /// <summary>Replaces tab, newline and carriage return with spaces, as <c>xs:normalizedString</c>.</summary>
        private static string Replace(string text)
        {
            return text.IndexOfAny(new[] { '\t', '\n', '\r' }) < 0
                ? text
                : string.Create(text.Length, text, static (span, source) =>
                {
                    for (int i = 0; i < source.Length; i++)
                    {
                        span[i] = source[i] is '\t' or '\n' or '\r' ? ' ' : source[i];
                    }
                });
        }

        /// <summary>Collapses runs of whitespace to one space and trims the ends, as <c>xs:token</c>.</summary>
        private static string Collapse(string text)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(text.Length);
            bool pending = false;

            foreach (char character in text)
            {
                if (character is ' ' or '\t' or '\n' or '\r')
                {
                    pending = builder.Length != 0;
                    continue;
                }

                if (pending)
                {
                    builder.Append(' ');
                    pending = false;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Whether text is an <c>xs:language</c>: <c>[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*</c>.
        /// </summary>
        /// <remarks>
        /// Written out rather than matched with a regular expression, because the pattern is short, the check
        /// runs per cast, and the subtag length limit is easy to lose sight of in one line of regex.
        /// </remarks>
        private static bool IsLanguage(string text)
        {
            int start = 0;

            for (int part = 0; ; part++)
            {
                int end = text.IndexOf('-', start);
                int length = (end < 0 ? text.Length : end) - start;

                if (length is < 1 or > 8)
                {
                    return false;
                }

                for (int i = start; i < start + length; i++)
                {
                    bool letter = char.IsAsciiLetter(text[i]);

                    // Only the first subtag names a language, and a language is letters. Anything after it
                    // is a country or variant, where digits are how UN M.49 region codes are written.
                    if (!letter && !(part > 0 && char.IsAsciiDigit(text[i])))
                    {
                        return false;
                    }
                }

                if (end < 0)
                {
                    return true;
                }

                start = end + 1;
            }
        }

        /// <summary>Whether text is an XML Name, or an NCName when a colon is not allowed.</summary>
        private static bool IsName(string text, bool allowColon)
        {
            if (text.Length == 0)
            {
                return false;
            }

            if (!System.Xml.XmlConvert.IsStartNCNameChar(text[0]) && !(allowColon && text[0] == ':'))
            {
                return false;
            }

            for (int i = 1; i < text.Length; i++)
            {
                if (!System.Xml.XmlConvert.IsNCNameChar(text[i]) && !(allowColon && text[i] == ':'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether text is an XML Nmtoken: one or more name characters, with nothing asked of the first.
        /// </summary>
        private static bool IsNmToken(string text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            foreach (char character in text)
            {
                if (!System.Xml.XmlConvert.IsNCNameChar(character) && character != ':')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether text is an <c>xs:anyURI</c>.
        /// </summary>
        /// <remarks>
        /// Deliberately narrow. XML Schema admits very nearly any text here, and a full RFC 3986 parse would
        /// refuse relative references and other things that are legitimately values of this type. What is
        /// checked is what is unambiguously wrong: a percent sign that does not introduce two hex digits, and
        /// a leading colon, which would name an empty scheme. Everything else is accepted, so this is a
        /// weaker check than a schema-aware processor would make.
        /// </remarks>
        private static bool IsUriReference(string text)
        {
            if (text.StartsWith(':'))
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '%')
                {
                    continue;
                }

                if (i + 2 >= text.Length
                    || !char.IsAsciiHexDigit(text[i + 1])
                    || !char.IsAsciiHexDigit(text[i + 2]))
                {
                    return false;
                }

                i += 2;
            }

            return true;
        }

        /// <summary>
        /// Whether text is in the lexical space of <c>xs:decimal</c>, or of <c>xs:double</c> once the
        /// exponent is allowed.
        /// </summary>
        /// <remarks>
        /// A sign, then digits with at most one point somewhere among them, then optionally an exponent. The
        /// check exists because <c>double.TryParse</c> is far more generous than XML Schema: it takes
        /// thousands separators, <c>Infinity</c>, currency symbols under some styles, and a decimal comma in
        /// the wrong culture. Every one of those would be a value this engine invented from text the
        /// specification says is not a number.
        /// </remarks>
        private static bool IsDecimalLexical(string text, bool allowExponent)
        {
            int index = 0;

            if (index < text.Length && text[index] is '+' or '-')
            {
                index++;
            }

            int digits = 0;
            bool point = false;

            while (index < text.Length)
            {
                if (char.IsAsciiDigit(text[index]))
                {
                    digits++;
                }
                else if (text[index] == '.' && !point)
                {
                    point = true;
                }
                else
                {
                    break;
                }

                index++;
            }

            if (digits == 0)
            {
                return false;
            }

            if (index == text.Length)
            {
                return true;
            }

            if (!allowExponent || text[index] is not ('e' or 'E'))
            {
                return false;
            }

            index++;

            if (index < text.Length && text[index] is '+' or '-')
            {
                index++;
            }

            // An exponent is at least one digit, and digits only.
            int exponentDigits = 0;

            for (; index < text.Length; index++)
            {
                if (!char.IsAsciiDigit(text[index]))
                {
                    return false;
                }

                exponentDigits++;
            }

            return exponentDigits != 0;
        }

        /// <summary>Casts a date, time or dateTime to another of the three.</summary>
        private static XdmDateTime NarrowMoment(XdmDateTime value, XdmTypeCode target)
        {
            return value.As(target);
        }

        /// <summary>
        /// Takes the components one of the gregorian types names out of a date or dateTime.
        /// </summary>
        private static XdmGregorian PartOf(XdmDateTime value, string name)
        {
            int year = value.Value.Year;
            int month = value.Value.Month;
            int day = value.Value.Day;

            return name switch
            {
                "gYear" => new XdmGregorian(name, year, null, null, value.Offset),
                "gYearMonth" => new XdmGregorian(name, year, month, null, value.Offset),
                "gMonth" => new XdmGregorian(name, null, month, null, value.Offset),
                "gMonthDay" => new XdmGregorian(name, null, month, day, value.Offset),
                _ => new XdmGregorian(name, null, null, day, value.Offset),
            };
        }

        /// <summary>The families of type that the casting table is written in terms of.</summary>
        private enum CastFamily
        {
            /// <summary>Text: <c>xs:string</c>, <c>xs:untypedAtomic</c> and the name types.</summary>
            Text,

            /// <summary><c>xs:anyURI</c>, which is text but casts only from text.</summary>
            Uri,

            /// <summary>The four numeric types.</summary>
            Numeric,

            /// <summary><c>xs:boolean</c>.</summary>
            Boolean,

            /// <summary>The three duration types.</summary>
            Duration,

            /// <summary><c>xs:date</c>, <c>xs:time</c> and <c>xs:dateTime</c>.</summary>
            Moment,

            /// <summary>The five gregorian types.</summary>
            Gregorian,

            /// <summary><c>xs:QName</c> and <c>xs:NOTATION</c>.</summary>
            QName,

            /// <summary><c>xs:hexBinary</c> and <c>xs:base64Binary</c>.</summary>
            Binary,
        }

        /// <summary>
        /// Whether a cast between two types exists at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XPath 2.0 §17 gives casting as a table, and where the table is blank the cast is a type error
        /// rather than something to attempt. It matters because almost everything has a lexical form: without
        /// the table, <c>xs:gYear("1999") cast as xs:float</c> would go by way of the text <c>1999</c> and
        /// come back with a number, which is not a conversion of the value but a re-reading of how it was
        /// written.
        /// </para>
        /// <para>
        /// Every type casts to and from <c>xs:string</c> and <c>xs:untypedAtomic</c>, which is the row and
        /// column that make the rest of the table meaningful — a stylesheet that wants the re-reading can ask
        /// for it in as many words.
        /// </para>
        /// </remarks>
        private static bool IsCastable(XPathValue value, BuiltInType target)
        {
            // A list type is made from text and from nothing else: the value is split on whitespace and each
            // token cast to the item type, and there is no such reading of a number or a date.
            if (target.ItemType is not null)
            {
                return FamilyOf(value.TypeCode) == CastFamily.Text;
            }

            CastFamily to = FamilyOf(target.Code);

            // Text is the common currency: anything may be written as a string, and a string may be read as
            // anything.
            if (to == CastFamily.Text)
            {
                return true;
            }

            CastFamily from = FamilyOf(value.TypeCode);

            if (from == CastFamily.Text)
            {
                // A name comes from a string or from an untyped value and from no other text: those are the
                // two XPath 3.0 allows to be cast to xs:QName, resolving whatever prefix they hold against
                // the namespaces the cast was written among. A URI is text and nothing else (see below), so
                // it is not among them.
                return to != CastFamily.QName
                    || value.TypeCode is XdmTypeCode.String or XdmTypeCode.UntypedAtomic;
            }

            switch (from)
            {
                case CastFamily.Numeric:
                case CastFamily.Boolean:
                    return to is CastFamily.Numeric or CastFamily.Boolean;

                case CastFamily.Uri:
                    // A URI is text and nothing else. Everything has a lexical form, so without this row the
                    // text of a URI would be read as a number or a date whenever one was asked for — which is
                    // a reading of how the value was written, not a conversion of what it is.
                    return to == CastFamily.Uri;

                case CastFamily.Duration:
                    return to == CastFamily.Duration;

                case CastFamily.Moment:
                    return IsCastableFromMoment(value.TypeCode, target, to);

                case CastFamily.Gregorian:
                    // Only to itself: a gYear and a gMonth name different parts of a calendar, and neither
                    // holds what the other needs.
                    return to == CastFamily.Gregorian
                        && string.Equals(value.AsGregorian().Name, target.Name, StringComparison.Ordinal);

                case CastFamily.QName:
                    return to == CastFamily.QName;

                default:
                    return to == CastFamily.Binary;
            }
        }

        /// <summary>
        /// Whether a date, time or dateTime casts to a given type.
        /// </summary>
        /// <remarks>
        /// A dateTime holds both halves, so it yields either. A date holds no time of day, so it cannot
        /// become one — but it can become a dateTime, which starts it at midnight. A time holds no date at
        /// all, so it becomes nothing but another time.
        /// </remarks>
        /// <summary>
        /// Casts text to one of the list types: the tokens in it, each cast to the item type.
        /// </summary>
        /// <remarks>
        /// The text is split on whitespace, which is the same thing as collapsing it and splitting on single
        /// spaces, so leading and trailing space says nothing. All three types have a minimum length of one,
        /// so text holding no tokens at all is a value of none of them — which is what makes
        /// <c>'' castable as xs:NMTOKENS</c> false where <c>'a b c'</c> is true.
        /// </remarks>
        /// <param name="value">The value being cast, which must be text.</param>
        /// <param name="type">The list type.</param>
        /// <param name="itemName">The type each token is cast to.</param>
        private static XPathValue CastToList(XPathValue value, BuiltInType type, string itemName)
        {
            if (!IsCastable(value, type))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"There is no cast from {NameOf(value)} to xs:{type.Name}. A list type is made from text "
                    + "by splitting it, and there is no such reading of this value.");
            }

            string[] tokens = value.ToStringValue().Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FORG0001,
                    $"xs:{type.Name} holds at least one item, and this text holds no tokens at all.");
            }

            BuiltInType item = s_types[itemName];
            XPathValue[] items = new XPathValue[tokens.Length];

            for (int i = 0; i < tokens.Length; i++)
            {
                items[i] = Cast(XPathValue.FromString(tokens[i]), item);
            }

            return XdmSequence.Concatenate(items);
        }

        private static bool IsCastableFromMoment(XdmTypeCode from, BuiltInType target, CastFamily to)
        {
            if (from == XdmTypeCode.Time)
            {
                return target.Code == XdmTypeCode.Time;
            }

            if (to == CastFamily.Gregorian)
            {
                return true;
            }

            return from == XdmTypeCode.DateTime
                ? to == CastFamily.Moment
                : target.Code is XdmTypeCode.Date or XdmTypeCode.DateTime;
        }

        /// <summary>Places a type in its family.</summary>
        private static CastFamily FamilyOf(XdmTypeCode code)
        {
            return code switch
            {
                XdmTypeCode.AnyUri => CastFamily.Uri,
                XdmTypeCode.Integer or XdmTypeCode.Decimal or XdmTypeCode.Float or XdmTypeCode.Double
                    => CastFamily.Numeric,
                XdmTypeCode.Boolean => CastFamily.Boolean,
                XdmTypeCode.Duration or XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration
                    => CastFamily.Duration,
                XdmTypeCode.Date or XdmTypeCode.Time or XdmTypeCode.DateTime => CastFamily.Moment,
                XdmTypeCode.Gregorian => CastFamily.Gregorian,
                XdmTypeCode.QName => CastFamily.QName,
                XdmTypeCode.HexBinary or XdmTypeCode.Base64Binary => CastFamily.Binary,
                _ => CastFamily.Text,
            };
        }

        /// <summary>Names a value's type for a message.</summary>
        private static string NameOf(XPathValue value)
        {
            return value.TypeCode switch
            {
                XdmTypeCode.None => "a node",
                XdmTypeCode.UntypedAtomic => "an untyped value",
                XdmTypeCode.Gregorian => "xs:" + value.AsGregorian().Name,
                XdmTypeCode.YearMonthDuration => "xs:yearMonthDuration",
                XdmTypeCode.DayTimeDuration => "xs:dayTimeDuration",
                XdmTypeCode.HexBinary => "xs:hexBinary",
                XdmTypeCode.Base64Binary => "xs:base64Binary",
                XdmTypeCode.QName => "xs:QName",
                _ => "xs:" + char.ToLowerInvariant(value.TypeCode.ToString()[0])
                    + value.TypeCode.ToString()[1..],
            };
        }

        /// <summary>
        /// Reads an untyped value as an <c>xs:double</c>, which is what every place wanting a number does
        /// with one, and leaves anything else alone.
        /// </summary>
        /// <remarks>
        /// It is a cast, and fails as a cast does. XPath 1.0 turns text that is not a number into NaN and
        /// carries on, so <c>@count + 1</c> over the text <c>three</c> quietly produces NaN and prints as
        /// one. XPath 2.0 raises <c>FORG0001</c> instead, on the grounds that nothing downstream will make
        /// sense of NaN either and the stylesheet would rather hear about it here.
        /// </remarks>
        /// <param name="value">The value, untyped or not.</param>
        /// <exception cref="XsltException">The text is not in <c>xs:double</c>'s lexical space.</exception>
        internal static XPathValue UntypedAsDouble(XPathValue value)
        {
            return value.TypeCode == XdmTypeCode.UntypedAtomic
                ? XPathValue.FromNumber(CastToDouble(value, "xs:double"))
                : value;
        }

        private static bool CastToBoolean(XPathValue value)
        {
            if (value.Kind == XPathValueKind.Boolean)
            {
                return value.ToBoolean();
            }

            if (value.TypeCode is XdmTypeCode.String or XdmTypeCode.UntypedAtomic)
            {
                // The lexical space of xs:boolean is exactly these four; a non-empty string is not enough.
                return value.ToStringValue().Trim() switch
                {
                    "true" or "1" => true,
                    "false" or "0" => false,
                    _ => throw XsltErrors.Error(
                        XsltErrorCode.FORG0001, $"'{value.ToStringValue()}' is not a valid xs:boolean."),
                };
            }

            // A number is true when it is neither zero nor NaN, as the specification defines the cast.
            double number = value.ToNumber();
            return number != 0.0 && !double.IsNaN(number);
        }

        /// <summary>
        /// Reads a value as an <c>xs:double</c>, answering NaN where it cannot be read as one at all.
        /// </summary>
        /// <remarks>
        /// What <c>fn:number()</c> does from XPath 2.0 on, where the function is defined as a cast whose
        /// failure is answered rather than raised. The lexical space is the type's, so <c>INF</c>,
        /// <c>-INF</c> and <c>NaN</c> are read as the values they name. Only the cast's own failure is
        /// answered: a value that cannot be atomized at all — a function item, a map — has not failed to
        /// be read as a number, it has failed to be a value the question can be asked of.
        /// </remarks>
        /// <param name="value">The value to read.</param>
        internal static double AsDoubleOrNaN(XPathValue value)
        {
            try
            {
                return CastToDouble(value, "xs:double");
            }
            catch (XsltException failed) when (failed.Code == nameof(XsltErrorCode.FORG0001))
            {
                return double.NaN;
            }
        }

        private static double CastToDouble(XPathValue value, string typeName)
        {
            if (value.Kind == XPathValueKind.Boolean)
            {
                return value.ToBoolean() ? 1.0 : 0.0;
            }

            if (value.TypeCode is XdmTypeCode.String or XdmTypeCode.UntypedAtomic
                || value.Kind is XPathValueKind.NodeSet or XPathValueKind.Node)
            {
                string text = value.ToStringValue().Trim();

                // XPath 1.0's number() has no INF or NaN in its lexical space; a cast to xs:double does. The
                // spellings are exactly these — 'nan' and 'Infinity' are not among them, however readily
                // .NET would read them. '+INF' is the one XML Schema 1.1 added to 1.0's two.
                switch (text)
                {
                    case "INF":
                    case "+INF":
                        return double.PositiveInfinity;
                    case "-INF":
                        return double.NegativeInfinity;
                    case "NaN":
                        return double.NaN;
                }

                if (!IsDecimalLexical(text, allowExponent: true)
                    || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    throw XsltErrors.Error(XsltErrorCode.FORG0001, $"'{value.ToStringValue()}' is not a valid {typeName}.");
                }

                return parsed;
            }

            return value.ToNumber();
        }

        private static decimal CastToDecimal(XPathValue value)
        {
            if (value.Kind == XPathValueKind.Boolean)
            {
                return value.ToBoolean() ? 1m : 0m;
            }

            if (value.TypeCode == XdmTypeCode.Decimal)
            {
                return value.ToDecimal();
            }

            if (value.TypeCode == XdmTypeCode.Integer)
            {
                return value.ToInteger();
            }

            if (value.TypeCode is XdmTypeCode.String or XdmTypeCode.UntypedAtomic
                || value.Kind == XPathValueKind.NodeSet)
            {
                string text = value.ToStringValue().Trim();

                // xs:decimal has no exponent in its lexical space, unlike xs:double.
                if (!IsDecimalLexical(text, allowExponent: false)
                    || !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
                {
                    throw XsltErrors.Error(XsltErrorCode.FORG0001, $"'{value.ToStringValue()}' is not a valid xs:decimal.");
                }

                return parsed;
            }

            return FromDouble(value.ToNumber(), "xs:decimal");
        }

        private static long CastToInteger(XPathValue value, BuiltInType type)
        {
            long result;

            if (value.Kind == XPathValueKind.Boolean)
            {
                result = value.ToBoolean() ? 1 : 0;
            }
            else if (value.TypeCode == XdmTypeCode.Integer)
            {
                result = value.ToInteger();
            }
            else if (value.TypeCode == XdmTypeCode.Decimal)
            {
                decimal truncated = decimal.Truncate(value.ToDecimal());

                if (truncated < long.MinValue || truncated > long.MaxValue)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOCA0003, $"{truncated} is too large to hold as an xs:integer.");
                }

                result = (long)truncated;
            }
            else if (value.TypeCode is XdmTypeCode.String or XdmTypeCode.UntypedAtomic
                || value.Kind == XPathValueKind.NodeSet)
            {
                string text = value.ToStringValue().Trim();

                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                {
                    // A decimal or exponent form is not in xs:integer's lexical space, even though the value
                    // it names might be an integer.
                    throw XsltErrors.Error(XsltErrorCode.FORG0001, $"'{value.ToStringValue()}' is not a valid xs:{type.Name}.");
                }
            }
            else
            {
                double number = value.ToNumber();
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    throw XsltErrors.Error(XsltErrorCode.FOCA0002, $"{number} cannot be cast to xs:{type.Name}.");
                }

                double truncated = Math.Truncate(number);

                // A double reaches far past what an integer is held in here, and converting one that does not
                // fit is undefined rather than an error in C#: it would quietly answer long.MinValue, which
                // the range check below would then wave through for xs:integer.
                if (truncated < long.MinValue || truncated > long.MaxValue)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOCA0003, $"{number} is too large to hold as an xs:integer.");
                }

                result = (long)truncated;
            }

            // Reaching here, the value is an integer; whether it is a value of *this* type is the type's own
            // restriction on the integers, and failing that restriction is an invalid value rather than an
            // overflow. FOCA0003 above says the number was too large to be an integer at all, which is a
            // different complaint from xs:byte(300), where 300 is a perfectly good integer.
            if (result < type.Minimum || result > type.Maximum)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FORG0001, $"{result} is outside the range of xs:{type.Name}.");
            }

            return result;
        }

        private static decimal FromDouble(double number, string typeName)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                // Not a magnitude that overflows, but a value with no decimal spelling at all: xs:decimal has
                // no infinity and no NaN, so there is nothing to be too large for.
                throw XsltErrors.Error(XsltErrorCode.FOCA0002, $"{number} cannot be cast to {typeName}.");
            }

            if (number > (double)decimal.MaxValue || number < (double)decimal.MinValue)
            {
                throw XsltErrors.Error(XsltErrorCode.FOCA0001, $"{number} is too large to hold as {typeName}.");
            }

            return (decimal)number;
        }
    }

    /// <summary>A call to a built-in type's constructor function, such as <c>xs:integer("5")</c>.</summary>
    public sealed class TypeConstructorExpr : Expr
    {
        private readonly Expr m_argument;
        private readonly XdmType.BuiltInType m_type;
        private readonly IReadOnlyDictionary<string, string>? m_namespaces;

        /// <summary>Initializes a constructor call.</summary>
        /// <param name="argument">The single argument, whose value is cast.</param>
        /// <param name="type">The type to cast to.</param>
        /// <param name="namespaces">
        /// The namespace bindings in scope where the call is written, which <c>xs:QName()</c> resolves a
        /// prefix against and every other constructor has no use for.
        /// </param>
        public TypeConstructorExpr(
            Expr argument,
            XdmType.BuiltInType type,
            IReadOnlyDictionary<string, string>? namespaces = null)
        {
            m_argument = argument;
            m_type = type;
            m_namespaces = namespaces;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_argument };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref Runtime.DynamicContext context)
        {
            XPathValue argument = m_argument.Evaluate(ref context);

            // Every constructor is declared xs:anyAtomicType? -> T?, so nothing in is nothing out. Without
            // this, xs:anyURI(()) would take the string-value of an empty sequence and come back with an
            // empty URI, which is a value where there was none — and xs:string(/r/@id) over a missing
            // attribute would be the empty string rather than nothing at all.
            if (Xpath2FunctionExpr.IsEmptySequence(argument))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            return XdmType.Cast(argument, m_type, m_namespaces);
        }
    }
}
