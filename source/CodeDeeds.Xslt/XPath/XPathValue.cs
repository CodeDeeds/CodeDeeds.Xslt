using System.Globalization;
using System.Runtime.CompilerServices;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The four value types of the XPath 1.0 data model.</summary>
    public enum XPathValueKind : byte
    {
        /// <summary>A set of nodes.</summary>
        NodeSet = 0,

        /// <summary>A boolean.</summary>
        Boolean = 1,

        /// <summary>An IEEE 754 double-precision number.</summary>
        Number = 2,

        /// <summary>A string.</summary>
        String = 3,

        /// <summary>
        /// A sequence of items, which XPath 2.0 adds and XPath 1.0 has no equivalent of.
        /// </summary>
        Sequence = 4,

        /// <summary>
        /// A single node, as an item of a sequence. Distinct from a one-node <see cref="NodeSet"/>, which
        /// carries the promise of being sorted and duplicate-free that a sequence does not make.
        /// </summary>
        Node = 5,

        /// <summary>
        /// A map, which XPath 3.1 adds. An item, but not an atomic value and not a node.
        /// </summary>
        Map = 6,

        /// <summary>
        /// An array, which XPath 3.1 adds. An item, but not an atomic value and not a node.
        /// </summary>
        Array = 7,

        /// <summary>
        /// A function item, which XPath 3.0 adds: a function held as a value.
        /// </summary>
        /// <remarks>
        /// A map and an array are function items too, and keep their own kinds because most rules ask which
        /// of the three it is. <see cref="XPathValue.IsFunctionItem"/> is the question that covers all three.
        /// </remarks>
        Function = 8,
    }

    /// <summary>
    /// The type of an atomic value, in the sense XPath 2.0 gives the word.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 1.0 has no such thing — a value is a string, a number, a boolean or a node-set, and where it came
    /// from is forgotten. XPath 2.0 keeps the type, and several of its rules turn on it. The one that bites
    /// hardest is that <c>'10' &lt; '9'</c> is false in 1.0, because both sides become numbers, and true in
    /// 2.0, because two strings compare as strings.
    /// </para>
    /// <para>
    /// <see cref="UntypedAtomic"/> is what separates the two: the value of a node in a document that was never
    /// validated against a schema is untyped, and an untyped operand is cast to whatever the other side needs
    /// — to a number against a number, to a string against a string. That is what keeps
    /// <c>@price &lt; 10</c> numeric in both versions while <c>'10' &lt; '9'</c> changes meaning.
    /// </para>
    /// <para>
    /// Only the types this engine can produce are listed. The rest of the built-in hierarchy — the date and
    /// time types, the binary types, <c>xs:QName</c> — belongs with the functions that construct them, and
    /// XSLT 3.0's maps and arrays are items but not atomic values, so they will need a kind of their own
    /// rather than an entry here.
    /// </para>
    /// </remarks>
    public enum XdmTypeCode : byte
    {
        /// <summary>Not an atomic value: a node-set, or a value whose type is not tracked.</summary>
        None = 0,

        /// <summary>The value of a node in a document that has not been schema-validated.</summary>
        UntypedAtomic = 1,

        /// <summary><c>xs:string</c>.</summary>
        String = 2,

        /// <summary><c>xs:boolean</c>.</summary>
        Boolean = 3,

        /// <summary><c>xs:double</c>, and the only numeric type XPath 1.0 has.</summary>
        Double = 4,

        /// <summary><c>xs:integer</c>.</summary>
        Integer = 5,

        /// <summary><c>xs:decimal</c>.</summary>
        Decimal = 6,

        /// <summary><c>xs:float</c>, held as a double that has been through <see cref="float"/>.</summary>
        Float = 7,

        /// <summary><c>xs:date</c>.</summary>
        Date = 8,

        /// <summary><c>xs:time</c>.</summary>
        Time = 9,

        /// <summary><c>xs:dateTime</c>.</summary>
        DateTime = 10,

        /// <summary><c>xs:duration</c>, which may hold both months and seconds.</summary>
        Duration = 11,

        /// <summary><c>xs:yearMonthDuration</c>.</summary>
        YearMonthDuration = 12,

        /// <summary><c>xs:dayTimeDuration</c>.</summary>
        DayTimeDuration = 13,

        /// <summary><c>xs:QName</c>.</summary>
        QName = 14,

        /// <summary><c>xs:hexBinary</c>.</summary>
        HexBinary = 15,

        /// <summary><c>xs:base64Binary</c>.</summary>
        Base64Binary = 16,

        /// <summary>One of the gregorian types: <c>xs:gYear</c> and its four relatives.</summary>
        Gregorian = 17,

        /// <summary>
        /// <c>xs:anyURI</c>, which holds text but is not an <c>xs:string</c>.
        /// </summary>
        /// <remarks>
        /// It shares the string representation and every string operation works on it, but it is a type of
        /// its own in the casting table and in the operator mapping: <c>xs:anyURI('…') cast as xs:float</c>
        /// is a type error where the same text as an <c>xs:string</c> would be read as a number. The
        /// function conversion rules promote it to <c>xs:string</c>, so a string function still takes one.
        /// </remarks>
        AnyUri = 18,

        /// <summary>
        /// <c>xs:error</c>, the type with no values at all.
        /// </summary>
        /// <remarks>
        /// No <see cref="XPathValue"/> ever carries this, and that is the point of it: it is here so that
        /// <c>instance of xs:error</c> can be false of everything and <c>cast as xs:error</c> can fail for
        /// everything, both by the ordinary rules rather than by a special case. XSD 1.1 introduced it and
        /// XPath 3.0 uses it to say that a function never returns, or that a branch is unreachable.
        /// </remarks>
        Error = 19,

        /// <summary>
        /// <c>xs:numeric</c>, the union of <c>xs:double</c>, <c>xs:float</c> and <c>xs:decimal</c>.
        /// </summary>
        /// <remarks>
        /// Like <see cref="Error"/>, no value carries this: it names a set of types rather than one a value
        /// is held in. A number is an <c>xs:integer</c> or an <c>xs:double</c> and answers to this name as
        /// well, which is what makes it useful for declaring a parameter that takes any number at all.
        /// </remarks>
        Numeric = 20,
    }

    /// <summary>
    /// An XPath 1.0 value.
    /// </summary>
    /// <remarks>
    /// A struct rather than a class hierarchy, so that numbers and booleans — which dominate predicate
    /// evaluation, the hottest path in most stylesheets — never box. Strings and node-sets share one reference
    /// field, discriminated by <see cref="Kind"/>.
    /// </remarks>
    public readonly struct XPathValue
    {
        private readonly double m_number;
        private readonly object? m_reference;

        private XPathValue(XPathValueKind kind, XdmTypeCode type, double number, object? reference)
        {
            Kind = kind;
            TypeCode = type;
            m_number = number;
            m_reference = reference;
        }

        /// <summary>
        /// Gets which derived built-in type this value was made under, where that is narrower than the type
        /// it is held in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>xs:int</c>, <c>xs:nonNegativeInteger</c> and their relatives are all held as
        /// <see cref="XdmTypeCode.Integer"/>, and the string-derived types all as text, because that is what
        /// they are. But <c>instance of</c> asks which type a value <em>was given</em>, so
        /// <c>xs:long(1) instance of xs:nonNegativeInteger</c> is false although 1 is one.
        /// </para>
        /// <para>
        /// Read by the type tests and by nothing else — arithmetic, comparison and every function still see
        /// the integer or the string. It costs nothing to carry: the struct is padded out to hold a
        /// reference beside a double, and this is a third byte in the same wasted space as the first two.
        /// </para>
        /// </remarks>
        public XdmType.DerivedType DerivedType { get; private init; }

        /// <summary>Returns this value carrying the derived type it was made under.</summary>
        /// <param name="derived">The derived type, or <see cref="XdmType.DerivedType.None"/> for none.</param>
        public XPathValue AsDerived(XdmType.DerivedType derived) =>
            derived == DerivedType ? this : this with { DerivedType = derived };

        /// <summary>Gets which of the four XPath types this value holds.</summary>
        public XPathValueKind Kind { get; }

        /// <summary>
        /// Gets the XPath 2.0 type of this value, or <see cref="XdmTypeCode.None"/> for a node-set.
        /// </summary>
        /// <remarks>
        /// Carried alongside <see cref="Kind"/> rather than replacing it, because the two answer different
        /// questions and XPath 1.0 only ever asks the first. It costs nothing to keep: the struct was already
        /// padded out to hold a reference and a double, so a second byte fits in space that was going to waste.
        /// </remarks>
        public XdmTypeCode TypeCode { get; }

        /// <summary>Creates a number, typed as <c>xs:double</c>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromNumber(double value)
        {
            return new XPathValue(XPathValueKind.Number, XdmTypeCode.Double, value, null);
        }

        /// <summary>
        /// Creates a map, which is an item but not an atomic value: <see cref="TypeCode"/> stays
        /// <see cref="XdmTypeCode.None"/>, as it does for a node.
        /// </summary>
        /// <param name="value">The map.</param>
        public static XPathValue FromMap(XdmMap value)
        {
            return new XPathValue(XPathValueKind.Map, XdmTypeCode.None, 0.0, value);
        }

        /// <summary>Creates an array, which like a map is an item but not an atomic value.</summary>
        /// <param name="value">The array.</param>
        public static XPathValue FromArray(XdmArray value)
        {
            return new XPathValue(XPathValueKind.Array, XdmTypeCode.None, 0.0, value);
        }

        /// <summary>Gets the map this value holds.</summary>
        /// <exception cref="InvalidCastException">The value is not a map.</exception>
        public XdmMap AsMap() => (XdmMap)m_reference!;

        /// <summary>Gets the array this value holds.</summary>
        /// <exception cref="InvalidCastException">The value is not an array.</exception>
        public XdmArray AsArray() => (XdmArray)m_reference!;

        /// <summary>Creates a function item.</summary>
        /// <param name="value">The function.</param>
        public static XPathValue FromFunction(XdmFunction value)
        {
            return new XPathValue(XPathValueKind.Function, XdmTypeCode.None, 0.0, value);
        }

        /// <summary>
        /// Gets this value as a function, which a map and an array also are.
        /// </summary>
        /// <remarks>
        /// XPath 3.1 defines a map as the function from key to value and an array as the function from
        /// position to member, so <c>$m('a')</c> and <c>$a(2)</c> are ordinary calls. Answering for all three
        /// here is what lets every higher-order function take any of them without asking which it got.
        /// </remarks>
        /// <exception cref="XsltException"><c>XPTY0004</c> where the value is not a function item at all.</exception>
        public XdmFunction AsFunction()
        {
            return Kind switch
            {
                XPathValueKind.Function => (XdmFunction)m_reference!,
                XPathValueKind.Map => new XdmMapFunction((XdmMap)m_reference!),
                XPathValueKind.Array => new XdmArrayFunction((XdmArray)m_reference!),
                _ => throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, "A function was wanted here, and this value is not one."),
            };
        }

        /// <summary>
        /// Gets whether this value is a function item: a function, a map or an array.
        /// </summary>
        /// <remarks>
        /// The three share what matters to most rules — none has a string-value, none can be atomized, none
        /// can be a map key — which is why one question covers them.
        /// </remarks>
        public bool IsFunctionItem =>
            Kind is XPathValueKind.Map or XPathValueKind.Array or XPathValueKind.Function;

        /// <summary>Creates a boolean.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromBoolean(bool value)
        {
            return new XPathValue(XPathValueKind.Boolean, XdmTypeCode.Boolean, value ? 1.0 : 0.0, null);
        }

        /// <summary>Creates a string, typed as <c>xs:string</c>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromString(string value)
        {
            return new XPathValue(XPathValueKind.String, XdmTypeCode.String, 0.0, value);
        }

        /// <summary>Creates a URI, typed as <c>xs:anyURI</c>.</summary>
        /// <param name="value">The URI as text, which is what the value is.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromAnyUri(string value)
        {
            return new XPathValue(XPathValueKind.String, XdmTypeCode.AnyUri, 0.0, value);
        }

        /// <summary>
        /// Creates a string carrying the type of an unvalidated node's value.
        /// </summary>
        /// <remarks>
        /// The same string as <see cref="FromString"/> so far as XPath 1.0 is concerned, and a different thing
        /// entirely to XPath 2.0: an untyped operand takes its meaning from whatever it is compared against,
        /// where an <c>xs:string</c> insists on being a string.
        /// </remarks>
        /// <param name="value">The node's string-value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromUntypedAtomic(string value)
        {
            return new XPathValue(XPathValueKind.String, XdmTypeCode.UntypedAtomic, 0.0, value);
        }

        /// <summary>
        /// Creates an <c>xs:integer</c>.
        /// </summary>
        /// <remarks>
        /// The <see cref="long"/> is kept in the bit pattern of the double field rather than converted to one.
        /// XPath 2.0 requires at least eighteen digits of integer precision, and a double carries fifteen, so
        /// storing it as a number would quietly lose the top of the range that the specification insists on.
        /// Reinterpreting the bits costs nothing and keeps the value exact.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromInteger(long value)
        {
            return new XPathValue(
                XPathValueKind.Number, XdmTypeCode.Integer, BitConverter.Int64BitsToDouble(value), null);
        }

        /// <summary>Creates an <c>xs:float</c>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromFloat(float value)
        {
            return new XPathValue(XPathValueKind.Number, XdmTypeCode.Float, value, null);
        }

        /// <summary>
        /// Creates an <c>xs:decimal</c>.
        /// </summary>
        /// <remarks>
        /// Boxed, because a <see cref="decimal"/> is sixteen bytes and will not fit beside the type in a
        /// struct that is kept small for the sake of the values that matter to evaluation speed. Decimals are
        /// rare enough in practice that an allocation apiece is the right trade.
        /// </remarks>
        public static XPathValue FromDecimal(decimal value)
        {
            return new XPathValue(XPathValueKind.Number, XdmTypeCode.Decimal, 0.0, value);
        }

        /// <summary>Creates a sequence.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromSequence(XdmSequence value)
        {
            return new XPathValue(XPathValueKind.Sequence, XdmTypeCode.None, 0.0, value);
        }

        /// <summary>Creates a single node, as an item of a sequence.</summary>
        /// <param name="tree">The tree the node belongs to.</param>
        /// <param name="node">The node.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromNode(Model.XdmTree tree, int node)
        {
            return new XPathValue(XPathValueKind.Node, XdmTypeCode.None, node, tree);
        }

        /// <summary>Creates a date, a time or a date and time.</summary>
        /// <param name="value">The value, which carries its own type.</param>
        public static XPathValue FromDateTime(XdmDateTime value)
        {
            return new XPathValue(XPathValueKind.String, value.Type, 0.0, value);
        }

        /// <summary>Creates a duration.</summary>
        /// <param name="value">The value, which carries its own type.</param>
        public static XPathValue FromDuration(XdmDuration value)
        {
            return new XPathValue(XPathValueKind.String, value.Type, 0.0, value);
        }

        /// <summary>Returns a value holding a name in a namespace.</summary>
        /// <param name="value">The name.</param>
        public static XPathValue FromQName(XdmQName value)
        {
            return new XPathValue(XPathValueKind.String, XdmTypeCode.QName, 0.0, value);
        }

        /// <summary>Returns a value holding bytes, in one of the two binary types.</summary>
        /// <param name="value">The bytes and which type spells them.</param>
        public static XPathValue FromBinary(XdmBinary value)
        {
            return new XPathValue(XPathValueKind.String, value.Type, 0.0, value);
        }

        /// <summary>Returns a value holding one of the gregorian types.</summary>
        /// <param name="value">The value.</param>
        public static XPathValue FromGregorian(XdmGregorian value)
        {
            return new XPathValue(XPathValueKind.String, XdmTypeCode.Gregorian, 0.0, value);
        }

        /// <summary>Gets the name this value holds.</summary>
        /// <exception cref="XsltException">The value is not a QName.</exception>
        public XdmQName AsQName()
        {
            return m_reference is XdmQName value
                ? value
                : throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, $"An xs:QName was required, but the value is a {TypeCode}.");
        }

        /// <summary>Gets the bytes this value holds.</summary>
        /// <exception cref="XsltException">The value is not a binary value.</exception>
        public XdmBinary AsBinary()
        {
            return m_reference as XdmBinary
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, $"A binary value was required, but the value is a {TypeCode}.");
        }

        /// <summary>Gets the gregorian value this value holds.</summary>
        /// <exception cref="XsltException">The value is not one.</exception>
        public XdmGregorian AsGregorian()
        {
            return m_reference as XdmGregorian
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, $"A gregorian value was required, but the value is a {TypeCode}.");
        }

        /// <summary>Gets the date or time this value holds.</summary>
        /// <exception cref="XsltException">The value is not one.</exception>
        public XdmDateTime AsDateTime()
        {
            return m_reference is XdmDateTime value
                ? value
                : throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, $"A date or time was required, but the value is a {TypeCode}.");
        }

        /// <summary>Gets the duration this value holds.</summary>
        /// <exception cref="XsltException">The value is not one.</exception>
        public XdmDuration AsDuration()
        {
            return m_reference is XdmDuration value
                ? value
                : throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, $"A duration was required, but the value is a {TypeCode}.");
        }

        /// <summary>Gets the sequence this value holds.</summary>
        /// <exception cref="XsltException">The value is not a sequence.</exception>
        public XdmSequence AsSequence()
        {
            return Kind == XPathValueKind.Sequence
                ? (XdmSequence)m_reference!
                : throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, $"A sequence was required, but the value is a {Kind}.");
        }

        /// <summary>Gets the tree of a value holding a single node.</summary>
        public Model.XdmTree NodeTree => (Model.XdmTree)m_reference!;

        /// <summary>Gets the node id of a value holding a single node.</summary>
        public int NodeId => (int)m_number;

        /// <summary>Gets the value as an <c>xs:integer</c>, which it must already be.</summary>
        public long ToInteger()
        {
            return TypeCode switch
            {
                XdmTypeCode.Integer => BitConverter.DoubleToInt64Bits(m_number),
                XdmTypeCode.Decimal => (long)decimal.Truncate((decimal)m_reference!),
                _ => (long)ToNumber(),
            };
        }

        /// <summary>Gets the value as an <c>xs:decimal</c>, which it must already be.</summary>
        public decimal ToDecimal()
        {
            return TypeCode switch
            {
                XdmTypeCode.Decimal => (decimal)m_reference!,
                XdmTypeCode.Integer => BitConverter.DoubleToInt64Bits(m_number),
                _ => (decimal)ToNumber(),
            };
        }

        /// <summary>Creates a node-set.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static XPathValue FromNodeSet(NodeSet value)
        {
            return new XPathValue(XPathValueKind.NodeSet, XdmTypeCode.None, 0.0, value);
        }

        /// <summary>
        /// Returns the node-set this value holds.
        /// </summary>
        /// <exception cref="XsltException">The value is not a node-set.</exception>
        public NodeSet AsNodeSet()
        {
            if (Kind != XPathValueKind.NodeSet)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"A node-set was required, but the value is a {Kind.ToString().ToLowerInvariant()}.");
            }

            return (NodeSet)m_reference!;
        }

        /// <summary>
        /// Converts to boolean: the <em>effective boolean value</em>, which is what a condition reads.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A node-set is true when it is non-empty — <em>not</em> when its string-value is non-empty. A number
        /// is true when it is neither zero nor NaN, and a string when it is not empty.
        /// </para>
        /// <para>
        /// Beyond those there is no answer, and XPath 2.0 says so rather than inventing one: a date, a
        /// duration, a name, or a sequence of two atomic values is neither true nor false, and a condition
        /// written on one has gone wrong somewhere earlier. None of these can arise from an XPath 1.0
        /// expression, so the refusal costs a 1.0 stylesheet nothing.
        /// </para>
        /// </remarks>
        /// <exception cref="XsltException">The value has no effective boolean value.</exception>
        public bool ToBoolean()
        {
            switch (Kind)
            {
                case XPathValueKind.Boolean:
                    return m_number != 0.0;

                case XPathValueKind.Number:
                {
                    double number = ToNumber();
                    return number != 0.0 && !double.IsNaN(number);
                }

                case XPathValueKind.String:
                    // A date, a duration, a name and a string of bytes are all held here too, and none of
                    // them is a string: only a real one answers.
                    return m_reference is string text
                        ? text.Length != 0
                        : throw NoEffectiveBooleanValue(TypeCode.ToString());

                case XPathValueKind.Node:
                    return true;

                case XPathValueKind.Sequence:
                {
                    XdmSequence sequence = (XdmSequence)m_reference!;

                    if (sequence.Count == 0)
                    {
                        return false;
                    }

                    // A sequence beginning with a node is true however long it is. Otherwise only a single
                    // item can be read, because two atomic values together say nothing about a condition.
                    if (sequence[0].Kind is XPathValueKind.Node or XPathValueKind.NodeSet)
                    {
                        return true;
                    }

                    return sequence.Count == 1
                        ? sequence[0].ToBoolean()
                        : throw NoEffectiveBooleanValue($"a sequence of {sequence.Count} values");
                }

                case XPathValueKind.Map:
                case XPathValueKind.Array:
                case XPathValueKind.Function:
                    // A function item has no effective boolean value, an empty map being no more false than
                    // a full one: what a condition would be asking is not a question the item answers. The
                    // specification names this one FORG0006, the same as a sequence of two.
                    throw NoEffectiveBooleanValue(Kind switch
                    {
                        XPathValueKind.Map => "a map",
                        XPathValueKind.Array => "an array",
                        _ => "a function",
                    });

                default:
                    return ((NodeSet)m_reference!).Count != 0;
            }
        }

        private static XsltException NoEffectiveBooleanValue(string what)
        {
            return XsltErrors.Error(
                XsltErrorCode.FORG0006,
                $"{what} is neither true nor false. A condition reads a node, a boolean, a number or a "
                + "string; anything else has to be turned into one of those first.");
        }

        /// <summary>
        /// Converts to a number, per the XPath 1.0 <c>number()</c> function. Values that are not numeric
        /// produce <see cref="double.NaN"/> rather than throwing.
        /// </summary>
        public double ToNumber()
        {
            switch (Kind)
            {
                case XPathValueKind.Number:
                    // An integer keeps its exact value in the bit pattern rather than as a double, and a
                    // decimal is off in the reference field, so neither can simply be read out.
                    return TypeCode switch
                    {
                        XdmTypeCode.Integer => BitConverter.DoubleToInt64Bits(m_number),
                        XdmTypeCode.Decimal => (double)(decimal)m_reference!,
                        _ => m_number,
                    };

                case XPathValueKind.Boolean:
                    return m_number;

                case XPathValueKind.String:
                    // A date has no numeric value; asking for one gives NaN rather than an error, as it does
                    // for any other string that is not a number.
                    return m_reference is string number ? ParseNumber(number) : double.NaN;

                case XPathValueKind.Node:
                    return ParseNumber(((Model.XdmTree)m_reference!).StringValueOf((int)m_number));

                case XPathValueKind.Sequence:
                {
                    XdmSequence sequence = (XdmSequence)m_reference!;
                    return sequence.Count == 1 ? sequence[0].ToNumber() : double.NaN;
                }

                case XPathValueKind.Map:
                case XPathValueKind.Array:
                case XPathValueKind.Function:
                    // NaN is what everything else that is not a number answers, and it would be the wrong
                    // answer here: NaN says "read as a number and was not one", and a function item is not
                    // something that can be read at all. Atomizing it is the error, and this is where the
                    // attempt lands.
                    throw XsltErrors.Error(
                        XsltErrorCode.FOTY0013,
                        "A function item, a map or an array has no typed value, so there is no number to "
                        + "read from it.");

                default:
                    return ParseNumber(((NodeSet)m_reference!).StringValue());
            }
        }

        /// <summary>
        /// Converts to a string, per the XPath 2.0 <c>string()</c> function.
        /// </summary>
        /// <remarks>
        /// Differs from <see cref="ToStringValue"/> only for <c>xs:double</c> and <c>xs:float</c>, which the
        /// two languages write differently — <c>INF</c> against <c>Infinity</c>, exponential notation
        /// against three hundred digits, <c>-0</c> against <c>0</c>. Everything else has one lexical form and
        /// takes the same route.
        /// </remarks>
        public string ToCanonicalString()
        {
            return Kind == XPathValueKind.Number && TypeCode is XdmTypeCode.Double or XdmTypeCode.Float
                ? CanonicalNumberToString(m_number, TypeCode == XdmTypeCode.Float)
                : ToStringValue();
        }

        /// <summary>
        /// Converts to a string, per the XPath 1.0 <c>string()</c> function.
        /// </summary>
        public string ToStringValue()
        {
            switch (Kind)
            {
                case XPathValueKind.String:
                    // Dates and durations write their canonical lexical form, which is what their ToString
                    // produces; everything else here really is a string already.
                    return m_reference as string ?? m_reference!.ToString()!;

                case XPathValueKind.Boolean:
                    return m_number != 0.0 ? "true" : "false";

                case XPathValueKind.Number:
                    // Each numeric type has its own lexical form: an integer never grows a decimal point, and
                    // a decimal keeps the digits it was given rather than being rendered as a double.
                    return TypeCode switch
                    {
                        XdmTypeCode.Integer => BitConverter.DoubleToInt64Bits(m_number)
                            .ToString(CultureInfo.InvariantCulture),
                        XdmTypeCode.Decimal => DecimalToString((decimal)m_reference!),
                        _ => NumberToString(m_number),
                    };

                case XPathValueKind.Node:
                    return ((Model.XdmTree)m_reference!).StringValueOf((int)m_number);

                case XPathValueKind.Sequence:
                {
                    // Atomized and joined by a space, which is what XPath 2.0 does when a sequence is asked
                    // for as text. XPath 1.0 would take only the first item, and never sees a sequence.
                    XdmSequence sequence = (XdmSequence)m_reference!;
                    if (sequence.Count == 0)
                    {
                        return string.Empty;
                    }

                    if (sequence.Count == 1)
                    {
                        return XdmSequence.StringValueOf(sequence[0]);
                    }

                    string[] parts = new string[sequence.Count];
                    for (int i = 0; i < sequence.Count; i++)
                    {
                        parts[i] = XdmSequence.StringValueOf(sequence[i]);
                    }

                    return string.Join(' ', parts);
                }

                case XPathValueKind.Map:
                case XPathValueKind.Array:
                case XPathValueKind.Function:
                    // A function item is an item with no string-value, and a map and an array are function
                    // items. There is text that would render any of them, but none that means the same thing,
                    // so the specification makes asking an error rather than answering with something a
                    // stylesheet might go on to compare.
                    // FOTY0014 rather than FOTY0013, the two dividing by the question asked rather than by
                    // what was asked: fn:data of a function is the one, fn:string of it is this.
                    throw XsltErrors.Error(
                        XsltErrorCode.FOTY0014,
                        Kind switch
                        {
                            XPathValueKind.Map => "A map has no string-value. Ask it for an entry instead.",
                            XPathValueKind.Array =>
                                "An array has no string-value. Ask it for a member, or flatten it.",
                            _ => "A function has no string-value. Call it, and ask its result instead.",
                        });

                default:
                    return ((NodeSet)m_reference!).StringValue();
            }
        }

        /// <summary>
        /// Writes an <c>xs:decimal</c>, which never uses exponential notation and keeps at least one digit
        /// after the point only when it has one.
        /// </summary>
        private static string DecimalToString(decimal value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);

            if (text.IndexOf('.') < 0)
            {
                return text;
            }

            // Trailing zeros are not part of the value, but a decimal remembers the scale it was parsed with.
            text = text.TrimEnd('0');
            return text.EndsWith('.') ? text[..^1] : text;
        }

        /// <summary>
        /// Parses a string as an XPath number.
        /// </summary>
        /// <remarks>
        /// XPath 1.0 accepts only optional surrounding whitespace, an optional minus sign, and digits with an
        /// optional decimal point. Exponent notation, a leading plus, hexadecimal and the special names
        /// <c>Infinity</c> and <c>NaN</c> are all rejected, so this cannot defer to
        /// <see cref="double.TryParse(string, NumberStyles, IFormatProvider, out double)"/> without filtering first.
        /// </remarks>
        /// <param name="text">The text to parse.</param>
        /// <returns>The number, or <see cref="double.NaN"/> if the text is not a valid XPath number.</returns>
        public static double ParseNumber(string text)
        {
            ReadOnlySpan<char> span = text.AsSpan().Trim(" \t\r\n".AsSpan());
            if (span.Length == 0)
            {
                return double.NaN;
            }

            int index = 0;
            bool negative = span[0] == '-';
            if (negative)
            {
                index++;
            }

            // The scan that validates the syntax also accumulates the digits, so the common case never has to
            // be read a second time by a general-purpose parser.
            long mantissa = 0;
            int fractionDigits = 0;
            bool sawDigit = false;
            bool exact = true;

            while (index < span.Length && char.IsAsciiDigit(span[index]))
            {
                sawDigit = true;
                if (mantissa <= MaximumExactMantissa / 10)
                {
                    mantissa = (mantissa * 10) + (span[index] - '0');
                }
                else
                {
                    exact = false;
                }

                index++;
            }

            if (index < span.Length && span[index] == '.')
            {
                index++;
                while (index < span.Length && char.IsAsciiDigit(span[index]))
                {
                    sawDigit = true;
                    if (exact && mantissa <= MaximumExactMantissa / 10)
                    {
                        mantissa = (mantissa * 10) + (span[index] - '0');
                        fractionDigits++;
                    }
                    else
                    {
                        exact = false;
                    }

                    index++;
                }
            }

            if (!sawDigit || index != span.Length)
            {
                return double.NaN;
            }

            // Both the mantissa and the power of ten are exactly representable here, so a single division is
            // correctly rounded and gives the same result the general parser would.
            if (exact && fractionDigits < s_powersOfTen.Length)
            {
                double value = fractionDigits == 0 ? mantissa : mantissa / s_powersOfTen[fractionDigits];
                return negative ? -value : value;
            }

            return double.TryParse(span, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double result)
                ? result
                : double.NaN;
        }

        /// <summary>
        /// The largest mantissa a <see cref="double"/> holds without loss. Beyond this the fast path defers to
        /// the general parser rather than risk a rounding difference.
        /// </summary>
        private const long MaximumExactMantissa = 1L << 53;

        /// <summary>
        /// Exact powers of ten. Ten to the twenty-second is the last one representable exactly as a
        /// <see cref="double"/>, which bounds how far the fast path can reach.
        /// </summary>
        private static readonly double[] s_powersOfTen =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
            1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        /// <summary>
        /// Formats a number as a string, per the XPath 1.0 <c>string()</c> function.
        /// </summary>
        /// <remarks>
        /// XPath never uses exponent notation and never writes a decimal point for an integral value, so the
        /// framework's round-trip format has to be expanded when it produces one.
        /// </remarks>
        /// <param name="value">The number to format.</param>
        /// <returns>The XPath string representation.</returns>
        public static string NumberToString(double value)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }

            if (double.IsPositiveInfinity(value))
            {
                return "Infinity";
            }

            if (double.IsNegativeInfinity(value))
            {
                return "-Infinity";
            }

            // Covers both zeroes; negative zero must print as "0", not "-0".
            if (value == 0.0)
            {
                return "0";
            }

            if (value == Math.Floor(value) && Math.Abs(value) < 1e18)
            {
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            }

            string round = value.ToString("R", CultureInfo.InvariantCulture);
            return round.IndexOf('E') < 0 ? round : ExpandExponent(round);
        }

        /// <summary>
        /// Writes a number in the lexical form XPath 2.0 gives it, which is not the form XPath 1.0 gives it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The two languages disagree about the same value. XPath 1.0 writes infinity as <c>Infinity</c> and
        /// never uses an exponent, so <c>1e300</c> comes out as three hundred digits; XPath 2.0 writes
        /// <c>INF</c> and switches to exponential notation outside the range where a plain decimal is
        /// readable. Negative zero is <c>0</c> in one and <c>-0</c> in the other.
        /// </para>
        /// <para>
        /// The digits are the fewest that read back as the same value, and for an <c>xs:float</c> that
        /// question is asked of a float — this engine holds one as a double that has been through
        /// <see cref="float"/>, so asking the double gives <c>3.299999952316284</c> where the value is 3.3.
        /// </para>
        /// </remarks>
        /// <param name="value">The number.</param>
        /// <param name="isFloat">Whether the value is an <c>xs:float</c> rather than an <c>xs:double</c>.</param>
        internal static string CanonicalNumberToString(double value, bool isFloat)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }

            if (double.IsInfinity(value))
            {
                return value > 0 ? "INF" : "-INF";
            }

            if (value == 0.0)
            {
                // Unlike 1.0, which has one zero to write: the sign survives here because the two are
                // different values and a round trip through the text has to keep them apart.
                return double.IsNegative(value) ? "-0" : "0";
            }

            string round = isFloat
                ? ((float)value).ToString("R", CultureInfo.InvariantCulture)
                : value.ToString("R", CultureInfo.InvariantCulture);

            return Recompose(round);
        }

        /// <summary>
        /// Rewrites .NET's shortest round-trip form as XPath's canonical one.
        /// </summary>
        /// <remarks>
        /// .NET switches to an exponent at its own threshold and writes it as <c>E+30</c>; XPath switches
        /// outside 0.000001 to 1000000 and writes <c>E30</c>, with a mantissa between one and ten that
        /// always carries a decimal point. So the digits are taken and the form rebuilt, rather than the
        /// text being patched.
        /// </remarks>
        private static string Recompose(string round)
        {
            bool negative = round[0] == '-';
            ReadOnlySpan<char> body = negative ? round.AsSpan(1) : round.AsSpan();

            int exponent = 0;
            int marker = body.IndexOf('E');
            if (marker >= 0)
            {
                exponent = int.Parse(
                    body[(marker + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

                body = body[..marker];
            }

            int point = body.IndexOf('.');
            int fraction = point < 0 ? 0 : body.Length - point - 1;

            Span<char> buffer = stackalloc char[body.Length];
            int length = 0;
            foreach (char character in body)
            {
                if (character != '.')
                {
                    buffer[length++] = character;
                }
            }

            // The value is now 'digits' scaled by a power of ten. Leading and trailing zeros carry no
            // information, and dropping them is what leaves the mantissa in the range XPath asks for.
            ReadOnlySpan<char> digits = buffer[..length];
            int scale = exponent - fraction;

            int lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0')
            {
                lead++;
            }

            digits = digits[lead..];

            int last = digits.Length;
            while (last > 1 && digits[last - 1] == '0')
            {
                last--;
                scale++;
            }

            digits = digits[..last];

            // Where the point falls after the first digit: the value is digits[0].digits[1…] times ten to
            // this, which is also the test for which of the two notations to use.
            int magnitude = scale + digits.Length - 1;

            // A double's shortest round-trip form carries at most seventeen digits, and what is written around
            // them is a sign, a point and an exponent of at most four characters, so this never grows.
            CharStringBuilder result = new CharStringBuilder(stackalloc char[32]);
            if (negative)
            {
                result.Append('-');
            }

            if (magnitude is >= -6 and < 6)
            {
                AppendPlain(ref result, digits, magnitude);
            }
            else
            {
                result.Append(digits[0]);
                result.Append('.');

                // The mantissa always carries a point, so a lone digit grows a zero after it.
                result.Append(digits.Length > 1 ? digits[1..] : "0".AsSpan());
                result.Append('E');
                result.Append(magnitude);
            }

            return result.ToString();
        }

        /// <summary>Writes the digits without an exponent, placing the point where the magnitude says.</summary>
        private static void AppendPlain(
            ref CharStringBuilder result,
            ReadOnlySpan<char> digits,
            int magnitude)
        {
            if (magnitude < 0)
            {
                result.Append("0.");
                result.Append('0', -magnitude - 1);
                result.Append(digits);
                return;
            }

            if (magnitude >= digits.Length - 1)
            {
                result.Append(digits);
                result.Append('0', magnitude - digits.Length + 1);
                return;
            }

            result.Append(digits[..(magnitude + 1)]);
            result.Append('.');
            result.Append(digits[(magnitude + 1)..]);
        }

        private static string ExpandExponent(string text)
        {
            int exponentIndex = text.IndexOf('E');
            int exponent = int.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture);

            ReadOnlySpan<char> mantissa = text.AsSpan(0, exponentIndex);
            bool negative = mantissa.Length > 0 && mantissa[0] == '-';
            if (negative)
            {
                mantissa = mantissa[1..];
            }

            int pointIndex = mantissa.IndexOf('.');
            Span<char> digits = stackalloc char[mantissa.Length];
            int digitCount = 0;
            for (int i = 0; i < mantissa.Length; i++)
            {
                if (mantissa[i] != '.')
                {
                    digits[digitCount++] = mantissa[i];
                }
            }

            // Position of the decimal point measured in digits from the left.
            int pointPosition = (pointIndex < 0 ? mantissa.Length : pointIndex) + exponent;

            // XPath 1.0 never writes an exponent, so 1e300 really is three hundred digits here. The common
            // case is far shorter than that, and the exact length is known either way.
            int needed = digitCount + Math.Abs(exponent) + 3;
            Span<char> scratch = needed <= 64 ? stackalloc char[64] : new char[needed];

            CharStringBuilder builder = new CharStringBuilder(scratch);
            if (negative)
            {
                builder.Append('-');
            }

            if (pointPosition <= 0)
            {
                builder.Append("0.");
                builder.Append('0', -pointPosition);
                builder.Append(digits[..digitCount]);
            }
            else if (pointPosition >= digitCount)
            {
                builder.Append(digits[..digitCount]);
                builder.Append('0', pointPosition - digitCount);
            }
            else
            {
                builder.Append(digits[..pointPosition]);
                builder.Append('.');
                builder.Append(digits[pointPosition..digitCount]);
            }

            return builder.ToString();
        }
    }
}
