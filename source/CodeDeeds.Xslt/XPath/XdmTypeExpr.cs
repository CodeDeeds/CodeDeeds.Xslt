using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>How many items a sequence type admits.</summary>
    public enum XdmOccurrence : byte
    {
        /// <summary>Exactly one, written with no indicator.</summary>
        One,

        /// <summary>Zero or one, written <c>?</c>.</summary>
        ZeroOrOne,

        /// <summary>Zero or more, written <c>*</c>.</summary>
        ZeroOrMore,

        /// <summary>One or more, written <c>+</c>.</summary>
        OneOrMore,
    }

    /// <summary>
    /// A sequence type, as written after <c>instance of</c> or <c>treat as</c>.
    /// </summary>
    /// <remarks>
    /// The item types this recognises are the atomic types, the kind tests that match nodes, and
    /// <c>item()</c>, which matches anything. Schema-aware forms such as <c>element(*, xs:date)</c> are not
    /// among them: this engine does not validate, so it has no types to match them against.
    /// </remarks>
    public sealed class XdmSequenceType
    {
        private readonly XdmTypeCode m_atomic;
        private readonly NodeKind? m_nodeKind;
        private readonly bool m_anyItem;
        private readonly bool m_emptyOnly;
        private readonly KindNodeTest? m_kindTest;
        private readonly XPathValueKind? m_functionKind;
        private readonly bool m_anyFunction;
        private readonly int? m_functionArity;
        private readonly string m_written;

        /// <summary>
        /// What every member of an array must be, or <see langword="null"/> where the type says nothing about
        /// its contents.
        /// </summary>
        /// <remarks>
        /// <c>array(xs:string)</c> asks that each member be one string; <c>array(xs:string*)</c> asks only
        /// that each member hold strings. So a member is matched as a whole sequence rather than as an item,
        /// and <c>[("A", "B")]</c> is an instance of the second and not of the first.
        /// </remarks>
        private readonly XdmSequenceType? m_memberType;

        /// <summary>
        /// What every key of a map must be, or <see langword="null"/> where the type says nothing about them.
        /// </summary>
        /// <remarks>
        /// A key is one atomic value and never a sequence, so this is an item type: <c>map(xs:string+, …)</c>
        /// is not written, and the parser refuses the indicator rather than reading it as always satisfied.
        /// </remarks>
        private readonly XdmSequenceType? m_keyType;

        /// <summary>Whether this is <c>xs:anyAtomicType</c>, which every atomic type is derived from.</summary>
        private readonly bool m_anyAtomic;

        /// <summary>Whether this is <c>xs:numeric</c>, the union of the three numeric types.</summary>
        private readonly bool m_anyNumeric;

        /// <summary>
        /// Which derived type was named, where the name is narrower than the representation.
        /// </summary>
        private readonly XdmType.DerivedType m_derivedType;

        /// <summary>
        /// The schema type named, where the name is one from a schema rather than a built-in one: a
        /// user-defined atomic type or a union, which a value is an instance of by its annotation.
        /// </summary>
        internal XdmSchemaType? SchemaType { get; private init; }

        private XdmSequenceType(
            XdmTypeCode atomic,
            NodeKind? nodeKind,
            bool anyItem,
            bool emptyOnly,
            XdmOccurrence occurrence,
            string written,
            KindNodeTest? kindTest = null,
            XPathValueKind? functionKind = null,
            bool anyFunction = false,
            int? functionArity = null,
            XdmSequenceType? memberType = null,
            bool anyAtomic = false,
            XdmSequenceType? keyType = null,
            bool anyNumeric = false,
            XdmType.DerivedType derivedType = XdmType.DerivedType.None)
        {
            m_memberType = memberType;
            m_keyType = keyType;
            m_anyNumeric = anyNumeric;
            m_derivedType = derivedType;
            m_anyAtomic = anyAtomic;
            m_atomic = atomic;
            m_nodeKind = nodeKind;
            m_anyItem = anyItem;
            m_emptyOnly = emptyOnly;
            m_kindTest = kindTest;
            m_functionKind = functionKind;
            m_anyFunction = anyFunction;
            m_functionArity = functionArity;
            Occurrence = occurrence;
            m_written = written;
        }

        /// <summary>Gets how many items the type admits.</summary>
        public XdmOccurrence Occurrence { get; }

        /// <summary>
        /// Whether another sequence type is identical to this one: the same item type and the same
        /// occurrence, however either was written.
        /// </summary>
        public bool SameAs(XdmSequenceType other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (Occurrence != other.Occurrence
                || m_atomic != other.m_atomic
                || m_nodeKind != other.m_nodeKind
                || m_anyItem != other.m_anyItem
                || m_emptyOnly != other.m_emptyOnly
                || m_functionKind != other.m_functionKind
                || m_anyFunction != other.m_anyFunction
                || m_functionArity != other.m_functionArity
                || m_anyAtomic != other.m_anyAtomic
                || m_anyNumeric != other.m_anyNumeric
                || m_derivedType != other.m_derivedType
                || !ReferenceEquals(SchemaType, other.SchemaType)
                || (m_memberType is null) != (other.m_memberType is null)
                || (m_keyType is null) != (other.m_keyType is null)
                || (m_kindTest is null) != (other.m_kindTest is null))
            {
                return false;
            }

            if (m_memberType is not null && !m_memberType.SameAs(other.m_memberType!))
            {
                return false;
            }

            if (m_keyType is not null && !m_keyType.SameAs(other.m_keyType!))
            {
                return false;
            }

            if ((ParameterTypes is null) != (other.ParameterTypes is null))
            {
                return false;
            }

            if (ParameterTypes is not null)
            {
                if (ParameterTypes.Length != other.ParameterTypes!.Length)
                {
                    return false;
                }

                for (int i = 0; i < ParameterTypes.Length; i++)
                {
                    if (!ParameterTypes[i].SameAs(other.ParameterTypes[i]))
                    {
                        return false;
                    }
                }

                if (ResultType is null != (other.ResultType is null)
                    || (ResultType is not null && !ResultType.SameAs(other.ResultType!)))
                {
                    return false;
                }
            }

            // A kind test with a name in it is compared as written, prefixes and all: close enough, and
            // erring towards refusing an override whose element(p:x) was spelled element(q:x).
            return m_kindTest is null
                || string.Equals(m_written.Trim(), other.m_written.Trim(), StringComparison.Ordinal);
        }

        /// <summary>The same item type with another occurrence, which is what parentheses around it allow.</summary>
        /// <param name="occurrence">The occurrence.</param>
        /// <param name="written">The type as written, for messages.</param>
        public XdmSequenceType WithOccurrence(XdmOccurrence occurrence, string written) =>
            new XdmSequenceType(
                m_atomic,
                m_nodeKind,
                m_anyItem,
                m_emptyOnly,
                occurrence,
                written,
                m_kindTest,
                m_functionKind,
                m_anyFunction,
                m_functionArity,
                m_memberType,
                m_anyAtomic,
                m_keyType,
                m_anyNumeric,
                m_derivedType)
            {
                // A function type written inside parentheses so that an indicator can follow it —
                // (function(xs:string) as xs:string)* — is the same type with another occurrence, and
                // losing the signature here would make the parentheses widen it.
                ParameterTypes = ParameterTypes,
                ResultType = ResultType,
            };

        /// <summary>
        /// The atomic type this admits, or <see langword="null"/> where it admits nodes or any item.
        /// </summary>
        /// <remarks>
        /// What the conversion rules need to know: a declared atomic type is something a value can be
        /// converted towards, and a node type is not.
        /// </remarks>
        public XdmTypeCode? AtomicType => m_anyItem || m_emptyOnly || m_nodeKind.HasValue
            || m_atomic == XdmTypeCode.None
                ? null
                : m_atomic;

        /// <summary>
        /// Whether this admits atomic values and nothing else, which is what says a node handed to it is
        /// to be atomized.
        /// </summary>
        /// <remarks>
        /// A narrower question than <see cref="AtomicType"/>, which asks which type a value may be
        /// converted towards and has no answer for <c>xs:anyAtomicType</c> or <c>xs:numeric</c>: neither
        /// names one type. Both still atomize what they are given — that is the first of the conversion
        /// rules and it does not depend on knowing the type — so the two are asked separately.
        /// </remarks>
        public bool WantsAtomic => m_anyAtomic || m_anyNumeric || AtomicType is not null || SchemaType is not null;

        /// <summary>The type matching nothing but the empty sequence.</summary>
        public static XdmSequenceType EmptySequence { get; } =
            new XdmSequenceType(XdmTypeCode.None, null, false, true, XdmOccurrence.ZeroOrMore, "empty-sequence()");

        /// <summary>Creates a type matching any item.</summary>
        public static XdmSequenceType AnyItem(XdmOccurrence occurrence) =>
            new XdmSequenceType(XdmTypeCode.None, null, true, false, occurrence, "item()");

        /// <summary>
        /// Creates a type matching any atomic value, written <c>xs:anyAtomicType</c>.
        /// </summary>
        /// <remarks>
        /// Every atomic type is derived from it, so it admits a boolean and a date as readily as a string.
        /// It is not <c>item()</c>: a node is an item and is not an atomic value, and neither is a function.
        /// </remarks>
        /// <param name="occurrence">How many items the type admits.</param>
        public static XdmSequenceType AnyAtomic(XdmOccurrence occurrence) =>
            new XdmSequenceType(
                XdmTypeCode.None, null, false, false, occurrence, "xs:anyAtomicType", anyAtomic: true);

        /// <summary>
        /// Creates a type matching any number, written <c>xs:numeric</c>.
        /// </summary>
        /// <remarks>
        /// XPath 3.1's name for the union of <c>xs:double</c>, <c>xs:float</c> and <c>xs:decimal</c> — and
        /// so of <c>xs:integer</c> too, which is derived from the last of them. It is how a function says it
        /// takes a number without saying which kind, and it admits nothing else: a string that reads as one
        /// is not a number until something casts it.
        /// </remarks>
        /// <param name="occurrence">How many items the type admits.</param>
        public static XdmSequenceType AnyNumeric(XdmOccurrence occurrence) =>
            new XdmSequenceType(
                XdmTypeCode.None, null, false, false, occurrence, "xs:numeric", anyNumeric: true);

        /// <summary>Creates a type matching one kind of node.</summary>
        /// <param name="kind">The kind, or <see langword="null"/> for any node.</param>
        /// <param name="occurrence">How many items the type admits.</param>
        /// <param name="written">The type as it was written, for diagnostics.</param>
        public static XdmSequenceType Node(NodeKind? kind, XdmOccurrence occurrence, string written) =>
            new XdmSequenceType(XdmTypeCode.None, kind, false, false, occurrence, written);

        /// <summary>Creates a type matching one of the kind tests that may name what it wants.</summary>
        /// <param name="test">The kind test, which asks about the node itself.</param>
        /// <param name="occurrence">How many items the type admits.</param>
        /// <param name="written">The type as it was written, for diagnostics.</param>
        public static XdmSequenceType Kind(KindNodeTest test, XdmOccurrence occurrence, string written) =>
            new XdmSequenceType(XdmTypeCode.None, test.Kind, false, false, occurrence, written, test);

        /// <summary>
        /// Creates a type matching a map or an array, written <c>map(*)</c>, <c>array(*)</c>, or with what
        /// the map or array is required to hold.
        /// </summary>
        /// <remarks>
        /// Unlike a function signature this is answerable from the value alone, because a map and an array
        /// hold what they hold: every entry can be looked at. So <c>map(xs:integer, xs:string)</c> asks of
        /// each entry that its key be one integer and its value one string, and an empty map satisfies every
        /// such type for want of an entry to fail it.
        /// </remarks>
        /// <param name="kind">Which of the two, as <see cref="XPathValueKind"/> names it.</param>
        /// <param name="occurrence">How many items the type admits.</param>
        /// <param name="written">The type as it was written, for diagnostics.</param>
        /// <param name="memberType">What each array member or map value must be, if the type said.</param>
        /// <param name="keyType">What each map key must be, if the type said.</param>
        public static XdmSequenceType MapOrArray(
            XPathValueKind kind, XdmOccurrence occurrence, string written,
            XdmSequenceType? memberType = null, XdmSequenceType? keyType = null) =>
            new XdmSequenceType(
                XdmTypeCode.None, null, false, false, occurrence, written, null, kind,
                memberType: memberType, keyType: keyType);

        /// <summary>
        /// Creates a type matching a function item, written <c>function(*)</c> or with its signature.
        /// </summary>
        /// <remarks>
        /// A signature is kept whole, and a function item declared with types of its own is answered against
        /// it by the subtype judgement (see <see cref="SubtypeOf"/>). An item whose types this engine does
        /// not record — a call into the standard library, a partial application, a map — is admitted by its
        /// arity alone, which is wider than the specification and never narrower.
        /// </remarks>
        /// <param name="arity">The number of arguments required, or <see langword="null"/> for any.</param>
        /// <param name="occurrence">How many items the type admits.</param>
        /// <param name="written">The type as it was written, for diagnostics.</param>
        public static XdmSequenceType FunctionItem(
            int? arity,
            XdmOccurrence occurrence,
            string written,
            XdmSequenceType[]? parameterTypes = null,
            XdmSequenceType? resultType = null) =>
            new XdmSequenceType(
                XdmTypeCode.None, null, false, false, occurrence, written, null, null, true, arity)
            {
                ParameterTypes = parameterTypes,
                ResultType = resultType,
            };

        /// <summary>Creates a type matching one atomic type.</summary>
        /// <param name="code">The atomic type.</param>
        /// <param name="occurrence">How many items the type admits.</param>
        /// <param name="written">The type as it was written, for diagnostics.</param>
        /// <param name="derived">
        /// Which derived type was named, where the name is narrower than the representation — so that
        /// <c>xs:int</c> asks about the name a value was made under rather than about the integer it holds.
        /// </param>
        public static XdmSequenceType Atomic(
            XdmTypeCode code,
            XdmOccurrence occurrence,
            string written,
            XdmType.DerivedType derived = XdmType.DerivedType.None,
            XdmType.BuiltInType? builtIn = null) =>
            new XdmSequenceType(code, null, false, false, occurrence, written, derivedType: derived)
            {
                BuiltIn = builtIn,
            };

        /// <summary>Creates a type matching the values of a type from a schema: an atomic type or a union.</summary>
        /// <param name="type">The schema type.</param>
        /// <param name="occurrence">How many items the type admits.</param>
        /// <param name="written">The type as it was written, for diagnostics.</param>
        internal static XdmSequenceType Schema(XdmSchemaType type, XdmOccurrence occurrence, string written) =>
            new XdmSequenceType(type.Primitive, null, false, false, occurrence, written, derivedType: type.Derived)
            {
                SchemaType = type,
                BuiltIn = type.BuiltIn,
            };

        /// <summary>
        /// The built-in type this was parsed from, where it named one.
        /// </summary>
        /// <remarks>
        /// Kept because <see cref="AtomicType"/> is a representation and not a name: five Gregorian types
        /// share one code, so working back from the code to a type to cast towards lands on none of them.
        /// This is the type the stylesheet actually wrote.
        /// </remarks>
        public XdmType.BuiltInType? BuiltIn { get; private init; }

        /// <summary>
        /// What a function type declares its arguments to be, or <see langword="null"/> where the type is
        /// <c>function(*)</c> or is not a function type at all.
        /// </summary>
        public XdmSequenceType[]? ParameterTypes { get; private init; }

        /// <summary>What a function type declares its result to be, present exactly when the parameters are.</summary>
        public XdmSequenceType? ResultType { get; private init; }

        /// <summary>Whether this is a function type written out with its argument and result types.</summary>
        public bool IsWrittenFunctionType => ParameterTypes is not null;

        /// <summary>
        /// The type as written, with the occurrence indicator that says how many items it admits.
        /// </summary>
        /// <remarks>
        /// The indicator is kept out of <see cref="m_written"/>, which names the item type alone and is
        /// what two kind tests are compared as. It belongs in a message all the same: a diagnostic that
        /// says <c>xs:integer</c> where the stylesheet wrote <c>xs:integer*</c> is telling the reader that
        /// one item was wanted, which is the very thing they are trying to find out.
        /// </remarks>
        public override string ToString()
        {
            string indicator = Occurrence switch
            {
                XdmOccurrence.ZeroOrOne => "?",
                XdmOccurrence.ZeroOrMore => "*",
                XdmOccurrence.OneOrMore => "+",
                _ => string.Empty,
            };

            // empty-sequence() admits any number of items as far as the occurrence goes, and none at all
            // as far as it is concerned; writing it 'empty-sequence()*' would be saying the opposite.
            return m_emptyOnly ? m_written : m_written + indicator;
        }

        /// <summary>Whether the type admits a sequence of this many items, whatever the items are.</summary>
        /// <param name="count">How many items there are.</param>
        public bool Admits(int count)
        {
            if (m_emptyOnly)
            {
                return count == 0;
            }

            return Occurrence switch
            {
                XdmOccurrence.One => count == 1,
                XdmOccurrence.ZeroOrOne => count <= 1,
                XdmOccurrence.OneOrMore => count >= 1,
                _ => true,
            };
        }

        /// <summary>Returns whether a value is of this type.</summary>
        /// <param name="value">The value to test.</param>
        public bool Matches(XPathValue value)
        {
            List<XPathValue> items = XdmSequence.Items(value);

            if (!Admits(items.Count))
            {
                return false;
            }

            foreach (XPathValue item in items)
            {
                if (!MatchesItem(item))
                {
                    return false;
                }
            }

            return true;
        }

        private bool MatchesItem(XPathValue item)
        {
            bool isNode = item.Kind is XPathValueKind.Node or XPathValueKind.NodeSet;

            if (m_anyItem)
            {
                return true;
            }

            // Anything that is neither a node nor a function is an atomic value, and every atomic type is
            // derived from xs:anyAtomicType.
            if (m_anyAtomic)
            {
                return !isNode && !item.IsFunctionItem;
            }

            // xs:numeric names three types at once, so the question is which type the value has rather than
            // which one representation it shares.
            if (m_anyNumeric)
            {
                return !isNode && !item.IsFunctionItem && XdmComparison.IsNumeric(item.TypeCode);
            }

            // A map or an array is an item and nothing else: not a node, and not an atomic value however its
            // contents look. A map type admits only a map and an array type only an array.
            if (m_functionKind is XPathValueKind wanted)
            {
                if (item.Kind != wanted)
                {
                    return false;
                }

                if (m_memberType is null)
                {
                    return true;
                }

                // A map is asked of each entry and an array of each member. The two differ in that a map has
                // a key beside the value, and that the key is one item where the value is a sequence.
                if (wanted == XPathValueKind.Map)
                {
                    foreach (KeyValuePair<XPathValue, XPathValue> entry in item.AsMap().Entries)
                    {
                        if (!m_keyType!.Matches(entry.Key) || !m_memberType.Matches(entry.Value))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                foreach (XPathValue member in item.AsArray().Members)
                {
                    if (!m_memberType.Matches(member))
                    {
                        return false;
                    }
                }

                return true;
            }

            // function(*) admits a map and an array too, XPath 3.1 defining both as functions — a map of the
            // key and an array of the position. So a map is an instance of function(*) and of map(*), and of
            // function(xs:string) as item()* by the arity it has.
            if (m_anyFunction)
            {
                if (!item.IsFunctionItem)
                {
                    return false;
                }

                if (m_functionArity is not int arity)
                {
                    return true;
                }

                XdmFunction function = item.AsFunction();

                if (function.Arity != arity)
                {
                    return false;
                }

                // A written-out signature is answered against the one the item was declared with, where the
                // item has one. A function this engine records no types for — a call into the standard
                // library, a partial application, the function a map stands for — is admitted by its arity
                // as it always was, the alternative being to invent an answer.
                return ParameterTypes is null
                    || function.Signature is not XdmFunctionSignature declared
                    || SignatureWithin(declared);
            }

            if (item.IsFunctionItem)
            {
                return false;
            }

            if (m_nodeKind is not null || IsNodeTest)
            {
                if (!isNode)
                {
                    return false;
                }

                // A kind test that named something asks the test itself; the rest ask only the kind.
                if (m_kindTest is not null)
                {
                    return item.Kind == XPathValueKind.Node
                        ? m_kindTest.Matches(item.NodeTree, item.NodeId)
                        : m_kindTest.Matches(item.AsNodeSet().Tree, item.AsNodeSet()[0]);
                }

                return m_nodeKind is null || KindOf(item) == m_nodeKind;
            }

            if (isNode)
            {
                // A node is not an atomic value, whatever its content looks like. Only a schema-aware
                // processor could say a node is an xs:integer, and this one is not.
                return false;
            }

            // A type from a schema asks about the annotation a value carries, or for a union about its
            // members, which the type itself answers.
            if (SchemaType is not null)
            {
                return !item.IsFunctionItem && SchemaType.Accepts(item);
            }

            // A derived name asks which type the value was made under rather than which one it is held in,
            // so xs:long(1) is not an xs:nonNegativeInteger and a plain integer is not an xs:int. The
            // representation still has to agree, or 'x' would be an xs:byte for want of anything to say no.
            if (m_derivedType != XdmType.DerivedType.None)
            {
                return m_atomic == item.TypeCode
                    && XdmType.DerivesFrom(item.DerivedType, m_derivedType);
            }

            // xs:QName and xs:NOTATION are two primitive types, neither derived from the other, and both
            // are held here as a namespace and a local name. A notation is therefore not an xs:QName,
            // which nothing but the mark it carries could say.
            if (m_atomic == XdmTypeCode.QName
                && XdmType.DerivesFrom(item.DerivedType, XdmType.DerivedType.Notation))
            {
                return false;
            }

            // xs:untypedAtomic is a type of its own and not a kind of string, however alike the two are held
            // here: what a node atomizes to is an instance of the one and not of the other. What is left is
            // the built-in derivations two codes can stand in.
            return m_atomic == item.TypeCode || DerivedFromCode(item.TypeCode, m_atomic);
        }

        /// <summary>
        /// Whether every value of this type is a value of another: the subtype judgement of XPath 3.1
        /// §2.5.6.2, over the types this engine models.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Answered conservatively — true only where the containment follows from what the two types
        /// record. This engine validates nothing, so two named schema types are compared by name and no
        /// hierarchy is consulted; what it keeps less of than the specification does is claimed to be
        /// within nothing but itself and the types that say nothing at all.
        /// </para>
        /// <para>
        /// It is asked by <c>instance of function(…) as …</c>, which compares the item's own declared types
        /// against the ones written rather than looking at a value, and by the coercion of a function item,
        /// which is skipped where the item already satisfies what is asked of it.
        /// </para>
        /// </remarks>
        /// <param name="other">The type this one may be within.</param>
        public bool SubtypeOf(XdmSequenceType other)
        {
            if (ReferenceEquals(this, other) || SameAs(other))
            {
                return true;
            }

            if (m_emptyOnly)
            {
                return other.m_emptyOnly
                    || other.Occurrence is XdmOccurrence.ZeroOrOne or XdmOccurrence.ZeroOrMore;
            }

            return !other.m_emptyOnly && Within(Occurrence, other.Occurrence) && ItemSubtypeOf(other);
        }

        /// <summary>Whether one occurrence indicator admits every count another admits.</summary>
        private static bool Within(XdmOccurrence inner, XdmOccurrence outer)
        {
            return outer switch
            {
                XdmOccurrence.ZeroOrMore => true,
                XdmOccurrence.One => inner == XdmOccurrence.One,
                XdmOccurrence.ZeroOrOne => inner is XdmOccurrence.One or XdmOccurrence.ZeroOrOne,
                _ => inner is XdmOccurrence.One or XdmOccurrence.OneOrMore,
            };
        }

        /// <summary>Whether every item of this type is an item of another's.</summary>
        private bool ItemSubtypeOf(XdmSequenceType other)
        {
            if (other.m_anyItem)
            {
                return true;
            }

            if (m_anyItem)
            {
                return false;
            }

            if (m_anyFunction || m_functionKind is not null)
            {
                return FunctionSubtypeOf(other);
            }

            if (other.m_anyFunction || other.m_functionKind is not null)
            {
                return false;
            }

            if (IsNodeType)
            {
                return other.IsNodeType && NodeSubtypeOf(other);
            }

            return !other.IsNodeType && AtomicSubtypeOf(other);
        }

        /// <summary>Whether a node type is within another node type.</summary>
        private bool NodeSubtypeOf(XdmSequenceType other)
        {
            NodeKind? theirs = other.m_nodeKind ?? other.m_kindTest?.Kind;

            // node() stands above every kind of node and below nothing else.
            if (theirs is null)
            {
                return true;
            }

            NodeKind? mine = m_nodeKind ?? m_kindTest?.Kind;

            if (mine != theirs)
            {
                return false;
            }

            // element() asks the kind and nothing more, so every element test is within it; element(e) asks
            // a name, which a test that asks none cannot promise.
            return other.m_kindTest is null
                || (m_kindTest is not null && m_kindTest.Within(other.m_kindTest));
        }

        /// <summary>
        /// Whether one built-in type code is derived from another.
        /// </summary>
        /// <remarks>
        /// The two places the built-in hierarchy shows through the representation rather than through a
        /// derived name: <c>xs:integer</c> is derived from <c>xs:decimal</c>, and both duration types from
        /// <c>xs:duration</c>. Every other derivation among the types this engine records either lands
        /// straight on <c>xs:anyAtomicType</c>, which is answered elsewhere and by everything, or is a
        /// restriction that <see cref="XdmType.DerivedType"/> already carries. In particular
        /// <c>xs:anyURI</c> is not a kind of <c>xs:string</c> — the two meet only under promotion, which
        /// converts rather than admits.
        /// </remarks>
        /// <param name="derived">The type a value has, or the narrower of the two.</param>
        /// <param name="basic">The type asked for, or the wider of the two.</param>
        private static bool DerivedFromCode(XdmTypeCode derived, XdmTypeCode basic)
        {
            return (basic == XdmTypeCode.Decimal && derived == XdmTypeCode.Integer)
                || (basic == XdmTypeCode.Duration
                    && derived is XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration);
        }

        /// <summary>Whether an atomic type is within another atomic type.</summary>
        private bool AtomicSubtypeOf(XdmSequenceType other)
        {
            if (other.m_anyAtomic)
            {
                return true;
            }

            if (m_anyAtomic)
            {
                return false;
            }

            if (other.m_anyNumeric)
            {
                return m_anyNumeric || XdmComparison.IsNumeric(m_atomic);
            }

            // A type from a schema is within another by derivation, and within a built-in type by the
            // built-in type it is held as; a built-in type is within no schema type.
            if (other.SchemaType is not null)
            {
                return SchemaType is not null && SchemaType.DerivesFrom(other.SchemaType);
            }

            if (SchemaType is not null)
            {
                return SchemaType.Primitive != XdmTypeCode.None
                    && (SchemaType.Primitive == other.m_atomic || DerivedFromCode(SchemaType.Primitive, other.m_atomic))
                    && (other.m_derivedType == XdmType.DerivedType.None
                        || XdmType.DerivesFrom(SchemaType.Derived, other.m_derivedType));
            }

            if (m_anyNumeric
                || (m_atomic != other.m_atomic && !DerivedFromCode(m_atomic, other.m_atomic)))
            {
                return false;
            }

            // A derived name is a narrower type over the same representation, so xs:int is within xs:long
            // and both are within the xs:integer that names none.
            return other.m_derivedType == XdmType.DerivedType.None
                || XdmType.DerivesFrom(m_derivedType, other.m_derivedType);
        }

        /// <summary>Whether a function, map or array type is within another item type.</summary>
        private bool FunctionSubtypeOf(XdmSequenceType other)
        {
            // A map and an array are function items, so each is within function(*); what one says about its
            // contents is compared only against another of its own kind.
            if (m_functionKind is not null)
            {
                if (other.m_functionKind != m_functionKind)
                {
                    return other.m_anyFunction && other.m_functionArity is null;
                }

                return other.m_memberType is null
                    || (m_memberType is not null && m_memberType.SubtypeOf(other.m_memberType));
            }

            if (!other.m_anyFunction || other.m_functionKind is not null)
            {
                return false;
            }

            if (other.m_functionArity is not int arity)
            {
                return true;
            }

            return m_functionArity == arity
                && (other.ParameterTypes is null
                    || (ParameterTypes is not null && SignatureWithin(other, ParameterTypes, ResultType)));
        }

        /// <summary>Whether a function item's declared types satisfy this written-out function type.</summary>
        private bool SignatureWithin(XdmFunctionSignature declared) =>
            SignatureWithin(this, declared.Parameters, declared.Result);

        /// <summary>
        /// Whether a function declared to take and return these types is within a required function type.
        /// </summary>
        /// <remarks>
        /// Contravariant in the arguments and covariant in the result: a function is usable where another is
        /// asked for if it accepts everything that one accepts and returns only what that one promises. A
        /// type left undeclared is <c>item()*</c>, which is what an <c>xsl:function</c> without <c>as</c>
        /// and a parameter without one mean.
        /// </remarks>
        private static bool SignatureWithin(
            XdmSequenceType required, IReadOnlyList<XdmSequenceType?> parameters, XdmSequenceType? result)
        {
            if (required.ParameterTypes!.Length != parameters.Count)
            {
                return false;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                if (!required.ParameterTypes[i].SubtypeOf(parameters[i] ?? Anything))
                {
                    return false;
                }
            }

            return (result ?? Anything).SubtypeOf(required.ResultType ?? Anything);
        }

        /// <summary>What an undeclared parameter or result type means, which is anything at all.</summary>
        private static readonly XdmSequenceType Anything = AnyItem(XdmOccurrence.ZeroOrMore);

        /// <summary>
        /// Coerces a function item to this function type, XPath 3.1 §3.4.2, or leaves it as it is.
        /// </summary>
        /// <remarks>
        /// The wrapper has the required type and calls the item inside it, converting each argument on the
        /// way in and the result on the way out by the function conversion rules. That is what makes a
        /// function returning an <c>xs:integer</c> where an <c>xs:string</c> was asked for fail when it is
        /// called rather than when it is passed — the failure belongs to the call, since the item was never
        /// wrong about itself.
        /// </remarks>
        /// <param name="item">The function item to coerce.</param>
        public XPathValue Coerce(XPathValue item)
        {
            if (ParameterTypes is null || item.Kind != XPathValueKind.Function)
            {
                return item;
            }

            XdmFunction function = item.AsFunction();

            // An arity that does not fit is not coerced but refused, which the match that follows does.
            // An item already of this type is handed on as it is, so that coercion costs nothing where
            // nothing needed converting.
            if (function.Arity != ParameterTypes.Length
                || (function.Signature is XdmFunctionSignature declared && SignatureWithin(declared)))
            {
                return item;
            }

            return XPathValue.FromFunction(
                new XdmCoercedFunction(function, ParameterTypes, ResultType ?? Anything));
        }

        /// <summary>Whether this type is a node type, however the kind was written.</summary>
        private bool IsNodeType => m_nodeKind is not null || m_kindTest is not null || IsNodeTest;

        /// <summary>Whether this type was written as a kind test rather than an atomic type name.</summary>
        private bool IsNodeTest => m_written.EndsWith("()", StringComparison.Ordinal) && !m_anyItem;

        private static NodeKind KindOf(XPathValue item)
        {
            return item.Kind == XPathValueKind.Node
                ? item.NodeTree.KindOf(item.NodeId)
                : item.AsNodeSet().Tree.KindOf(item.AsNodeSet()[0]);
        }
    }

    /// <summary>The <c>instance of</c> operator, which asks whether a value is of a type.</summary>
    public sealed class InstanceOfExpr : Expr
    {
        private readonly Expr m_value;
        private readonly XdmSequenceType m_type;

        /// <summary>Initializes an instance-of test.</summary>
        /// <param name="value">The value to test.</param>
        /// <param name="type">The type to test against.</param>
        public InstanceOfExpr(Expr value, XdmSequenceType type)
        {
            m_value = value;
            m_type = type;
        }

        /// <inheritdoc/>
        internal override bool IsBooleanValued => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_value };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return XPathValue.FromBoolean(m_type.Matches(m_value.Evaluate(ref context)));
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            return m_type.Matches(m_value.Evaluate(ref context));
        }
    }

    /// <summary>
    /// The <c>treat as</c> operator, which asserts a type without converting to it.
    /// </summary>
    /// <remarks>
    /// Unlike <c>cast as</c> this changes nothing: it either passes the value through or fails. It exists to
    /// tell a processor something it could not work out for itself, which matters more to one that checks
    /// types before running than to one that does not.
    /// </remarks>
    public sealed class TreatAsExpr : Expr
    {
        private readonly Expr m_value;
        private readonly XdmSequenceType m_type;

        /// <summary>Initializes a treat-as assertion.</summary>
        /// <param name="value">The value to assert about.</param>
        /// <param name="type">The type it is asserted to have.</param>
        public TreatAsExpr(Expr value, XdmSequenceType type)
        {
            m_value = value;
            m_type = type;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_value };

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue value = m_value.Evaluate(ref context);

            return m_type.Matches(value)
                ? value
                : throw XsltErrors.Error(
                    XsltErrorCode.XPDY0050, $"The value is not an instance of {m_type}.");
        }
    }

    /// <summary>The <c>cast as</c> and <c>castable as</c> operators.</summary>
    /// <remarks>
    /// <c>castable as</c> is <c>cast as</c> with the failure caught: it answers whether the cast would work
    /// rather than performing it, which is how a stylesheet checks a value before relying on it.
    /// </remarks>
    public sealed class CastExpr : Expr
    {
        private readonly Expr m_value;
        private readonly XdmType.BuiltInType m_type;
        private readonly XdmSchemaType? m_schemaType;
        private readonly bool m_allowEmpty;
        private readonly bool m_testOnly;
        private readonly IReadOnlyDictionary<string, string>? m_namespaces;
        private readonly string m_defaultElementNamespace = string.Empty;

        /// <summary>Initializes a cast.</summary>
        /// <param name="value">The value to cast.</param>
        /// <param name="type">The type to cast to.</param>
        /// <param name="allowEmpty">Whether the type was written with <c>?</c>, admitting the empty sequence.</param>
        /// <param name="testOnly">Whether this is <c>castable as</c> rather than <c>cast as</c>.</param>
        /// <param name="namespaces">
        /// The namespace bindings in scope where the cast is written, which a cast to <c>xs:QName</c>
        /// resolves a prefix against and every other cast has no use for.
        /// </param>
        /// <param name="defaultElementNamespace">
        /// The namespace an unprefixed name goes to, which a cast to <c>xs:QName</c> reads.
        /// </param>
        public CastExpr(
            Expr value,
            XdmType.BuiltInType type,
            bool allowEmpty,
            bool testOnly,
            IReadOnlyDictionary<string, string>? namespaces = null,
            string defaultElementNamespace = "")
        {
            m_value = value;
            m_type = type;
            m_allowEmpty = allowEmpty;
            m_testOnly = testOnly;
            m_namespaces = namespaces;
            m_defaultElementNamespace = defaultElementNamespace;
        }

        /// <summary>Initializes a cast to a type from a schema, which is also that type's constructor function.</summary>
        /// <param name="value">The value to cast.</param>
        /// <param name="type">The schema type to cast to.</param>
        /// <param name="allowEmpty">Whether the empty sequence is admitted, which a constructor always does.</param>
        /// <param name="testOnly">Whether this is <c>castable as</c> rather than <c>cast as</c>.</param>
        /// <param name="namespaces">The namespace bindings in scope where the cast is written.</param>
        internal CastExpr(
            Expr value,
            XdmSchemaType type,
            bool allowEmpty,
            bool testOnly,
            IReadOnlyDictionary<string, string>? namespaces = null)
        {
            m_value = value;
            m_schemaType = type;
            m_allowEmpty = allowEmpty;
            m_testOnly = testOnly;
            m_namespaces = namespaces;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_value };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            List<XPathValue> items = XdmSequence.Items(m_value.Evaluate(ref context));

            if (items.Count != 1)
            {
                if (items.Count == 0 && m_allowEmpty)
                {
                    return m_testOnly
                        ? XPathValue.FromBoolean(true)
                        : XPathValue.FromSequence(XdmSequence.Empty);
                }

                return m_testOnly
                    ? XPathValue.FromBoolean(false)
                    : throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        $"A cast takes one value, but was given {items.Count}.");
            }

            // A node casts by way of its typed value: its string-value, untyped, unless it was validated,
            // when it may be nothing at all, which casts as the empty sequence does.
            XPathValue single = items[0].Kind == XPathValueKind.Node
                ? XdmSequence.TypedValueAsOne(items[0], "A cast")
                : items[0];

            if (single.Kind == XPathValueKind.Sequence)
            {
                if (m_allowEmpty)
                {
                    return m_testOnly
                        ? XPathValue.FromBoolean(true)
                        : XPathValue.FromSequence(XdmSequence.Empty);
                }

                return m_testOnly
                    ? XPathValue.FromBoolean(false)
                    : throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        "A cast takes one value, and the node's typed value is empty.");
            }

            if (!m_testOnly)
            {
                return m_schemaType is not null
                    ? m_schemaType.Cast(single, m_namespaces)
                    : XdmType.Cast(single, m_type, m_namespaces, m_defaultElementNamespace);
            }

            try
            {
                if (m_schemaType is not null)
                {
                    m_schemaType.Cast(single, m_namespaces);
                }
                else
                {
                    XdmType.Cast(single, m_type, m_namespaces, m_defaultElementNamespace);
                }

                return XPathValue.FromBoolean(true);
            }
            catch (XsltException)
            {
                return XPathValue.FromBoolean(false);
            }
        }
    }

    /// <summary>
    /// <c>intersect</c> and <c>except</c>, which combine two node-sets by identity.
    /// </summary>
    /// <remarks>
    /// Both yield nodes in document order with duplicates removed, as <c>|</c> does — they are set operations
    /// on nodes, not on the values those nodes hold.
    /// </remarks>
    public sealed class NodeSetOperationExpr : Expr
    {
        private readonly Expr m_left;
        private readonly Expr m_right;
        private readonly bool m_keepShared;

        /// <summary>Initializes a node-set operation.</summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="keepShared">
        /// <see langword="true"/> for <c>intersect</c>, which keeps the nodes in both;
        /// <see langword="false"/> for <c>except</c>, which keeps those only in the left.
        /// </param>
        public NodeSetOperationExpr(Expr left, Expr right, bool keepShared)
        {
            m_left = left;
            m_right = right;
            m_keepShared = keepShared;
        }

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => true;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => m_left.MaySpanDocuments || m_right.MaySpanDocuments;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_left, m_right };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            // Read through NodeSet.Of rather than AsNodeSet, as a union is: from XPath 2.0 a sequence of
            // nodes is nodes, whichever way it was made, and a variable filtered down from a typed one
            // arrives as a sequence.
            NodeSet left = NodeSet.Of(
                m_left.Evaluate(ref context), context.Tree, XsltErrorCode.XPTY0004, "the left of an intersect or except");
            NodeSet right = NodeSet.Of(
                m_right.Evaluate(ref context), context.Tree, XsltErrorCode.XPTY0004, "the right of an intersect or except");

            HashSet<(XdmTree Tree, int Node)> other = new();
            for (int i = 0; i < right.Count; i++)
            {
                other.Add((right.TreeAt(i), right[i]));
            }

            NodeSet result = new NodeSet(left.Tree, left.Count);
            for (int i = 0; i < left.Count; i++)
            {
                if (other.Contains((left.TreeAt(i), left[i])) == m_keepShared)
                {
                    result.Add(left.TreeAt(i), left[i]);
                }
            }

            result.SortAndDeduplicate();
            return XPathValue.FromNodeSet(result);
        }
    }

    /// <summary>
    /// The node comparisons <c>is</c>, <c>&lt;&lt;</c> and <c>&gt;&gt;</c>.
    /// </summary>
    /// <remarks>
    /// These ask about the nodes themselves rather than their values: whether two references are the same
    /// node, and which of two nodes comes first in document order. Each operand must be one node or none, and
    /// an empty operand makes the whole comparison empty.
    /// </remarks>
    public sealed class NodeComparisonExpr : Expr
    {
        private readonly Expr m_left;
        private readonly Expr m_right;
        private readonly int m_kind;

        /// <summary>Initializes a node comparison.</summary>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="kind">0 for <c>is</c>, -1 for <c>&lt;&lt;</c>, 1 for <c>&gt;&gt;</c>.</param>
        public NodeComparisonExpr(Expr left, Expr right, int kind)
        {
            m_left = left;
            m_right = right;
            m_kind = kind;
        }

        /// <inheritdoc/>
        internal override bool IsBooleanValued => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_left, m_right };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            if (!TrySingleNode(m_left.Evaluate(ref context), out XdmTree? leftTree, out int leftNode)
                || !TrySingleNode(m_right.Evaluate(ref context), out XdmTree? rightTree, out int rightNode))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            if (m_kind == 0)
            {
                return XPathValue.FromBoolean(
                    ReferenceEquals(leftTree, rightTree) && leftNode == rightNode);
            }

            // Across documents the answer is whichever document was built first, which is the same ordering
            // a node-set spanning documents is sorted into.
            int sign = ReferenceEquals(leftTree, rightTree)
                ? leftTree!.DocumentOrderKeyOf(leftNode).CompareTo(rightTree!.DocumentOrderKeyOf(rightNode))
                : leftTree!.DocumentOrdinal.CompareTo(rightTree!.DocumentOrdinal);

            return XPathValue.FromBoolean(m_kind < 0 ? sign < 0 : sign > 0);
        }

        private static bool TrySingleNode(XPathValue value, out XdmTree? tree, out int node)
        {
            tree = null;
            node = -1;

            List<XPathValue> items = XdmSequence.Items(value);
            if (items.Count == 0)
            {
                return false;
            }

            if (items.Count > 1 || items[0].Kind != XPathValueKind.Node)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, "A node comparison takes one node on each side.");
            }

            tree = items[0].NodeTree;
            node = items[0].NodeId;
            return true;
        }
    }
}
