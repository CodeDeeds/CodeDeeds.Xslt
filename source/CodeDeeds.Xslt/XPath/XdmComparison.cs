namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Comparison under XPath 2.0, where a pair of operand types either names a comparison or does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 1.0 will compare anything to anything: it converts both operands until they are the same kind of
    /// thing, so <c>"1" = 1</c> is true and <c>"a" = 1</c> is false. XPath 2.0 replaces that with a table —
    /// the <em>operator mapping</em> — saying which pairs of types a comparison is defined for. A number and
    /// a string are not among them, so <c>"1" = 1</c> is a type error rather than an answer. This is the
    /// single largest behavioural difference between the two languages, and the reason a 2.0 expression that
    /// looks like it works may be refused outright.
    /// </para>
    /// <para>
    /// Untyped operands — anything drawn from a document, which is everything without a schema — are the
    /// exception, and the two families of operator treat them differently. A <em>general</em> comparison
    /// (<c>=</c>, <c>&lt;</c>) reads an untyped operand as whatever the other side is, so <c>@price &gt;
    /// 10</c> still means what it looks like. A <em>value</em> comparison (<c>eq</c>, <c>lt</c>) reads it as a
    /// string and nothing else, so <c>@price gt 10</c> is a type error. That asymmetry is deliberate: the
    /// value comparisons are the ones that promise to tell you when a stylesheet is guessing.
    /// </para>
    /// <para>
    /// Ordering is narrower than equality. Two names, two strings of bytes and two partial dates such as
    /// <c>xs:gMonth</c> can be equal or not, but nothing puts one before another. Neither does a year-month
    /// duration order against a day-time one: without knowing which months, no answer exists.
    /// </para>
    /// </remarks>
    internal static class XdmComparison
    {
        /// <summary>The sign of a comparison between values that stand in no relation at all.</summary>
        /// <remarks>
        /// NaN is the only value this arises for. It is not less than, equal to, or greater than anything,
        /// itself included — so every operator is false against it except <c>ne</c>, which is true. A sentinel
        /// rather than an out-of-range sign, because <c>2</c> read as a sign makes <c>ge</c> come out true.
        /// </remarks>
        private const int Unordered = int.MinValue;

        /// <summary>What kind of comparison a pair of operand types calls for.</summary>
        private enum Kind
        {
            /// <summary>None: the two types have no comparison defined between them.</summary>
            None,

            /// <summary>Two numbers, compared by value across the four numeric types.</summary>
            Numeric,

            /// <summary>Two strings, compared by code point.</summary>
            Text,

            /// <summary>Two booleans, where false comes before true.</summary>
            Boolean,

            /// <summary>Two dates, times or dateTimes, compared as the instants they denote.</summary>
            Moment,

            /// <summary>Two durations, compared as the lengths they denote.</summary>
            Duration,

            /// <summary>Two binary values, ordered by their octets.</summary>
            Binary,

            /// <summary>A pair that is equal or not, and has no order: a name, some bytes, a partial date.</summary>
            Identity,
        }

        /// <summary>
        /// Applies a value comparison — <c>eq</c>, <c>ne</c>, <c>lt</c>, <c>le</c>, <c>gt</c> or <c>ge</c> —
        /// to two atomic values.
        /// </summary>
        /// <param name="left">The left operand, already atomized to a single item.</param>
        /// <param name="right">The right operand, already atomized to a single item.</param>
        /// <param name="op">The operator.</param>
        /// <param name="where">The static context the comparison was written in, or null for the default.</param>
        /// <exception cref="XsltException">No comparison is defined between the operands' types.</exception>
        public static bool Value(
            XPathValue left, XPathValue right, BinaryOperator op, ComparisonContext? where = null)
        {
            // An untyped operand is read as a string here and never as the other operand's type. That is the
            // whole difference from the general comparison, and it is why 'eq' catches what '=' does not.
            return Apply(SignOf(AsStringIfUntyped(left), AsStringIfUntyped(right), op, where), op);
        }

        /// <summary>
        /// Applies a general comparison — <c>=</c>, <c>!=</c>, <c>&lt;</c> and the rest — to one pair of
        /// atomic values drawn from the two operand sequences.
        /// </summary>
        /// <param name="left">One item from the left operand.</param>
        /// <param name="right">One item from the right operand.</param>
        /// <param name="op">The operator.</param>
        /// <param name="where">The static context the comparison was written in, or null for the default.</param>
        /// <exception cref="XsltException">No comparison is defined between the operands' types.</exception>
        public static bool Pair(
            XPathValue left, XPathValue right, BinaryOperator op, ComparisonContext? where = null)
        {
            XPathValue a = ReadUntypedAs(left, right, where);
            XPathValue b = ReadUntypedAs(right, left, where);

            return Apply(SignOf(a, b, op, where), op);
        }

        /// <summary>
        /// Puts two atomic values in the order the XPath comparison rules give them, and says when there is
        /// no such order.
        /// </summary>
        /// <remarks>
        /// The question a sort asks rather than the question an operator asks. An operator wants a yes or a
        /// no and raises a type error where the pair has no ordered comparison; something ordering a list
        /// wants to know whether there is an order at all, so that it can fall back on whatever it orders
        /// the rest of its values by.
        /// </remarks>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <param name="sign">Negative, zero or positive; zero where the two stand in no relation.</param>
        /// <returns>Whether the pair has an ordered comparison at all.</returns>
        public static bool TryOrder(XPathValue left, XPathValue right, out int sign)
        {
            XPathValue a = AsStringIfUntyped(left);
            XPathValue b = AsStringIfUntyped(right);

            if (Classify(a, b, out bool ordered) == Kind.None || !ordered)
            {
                sign = 0;
                return false;
            }

            int order = SignOf(a, b, BinaryOperator.LessThan, null);

            // NaN is in no relation to anything, itself included. Zero rather than the sentinel, so that a
            // sort reading this puts it where it found it instead of somewhere that depends on the order the
            // comparisons happened to be made in.
            sign = order == Unordered ? 0 : order;
            return true;
        }

        /// <summary>Casts an untyped operand to <c>xs:string</c>, and leaves anything else alone.</summary>
        private static XPathValue AsStringIfUntyped(XPathValue value)
        {
            return value.TypeCode == XdmTypeCode.UntypedAtomic
                ? XPathValue.FromString(value.ToStringValue())
                : value;
        }

        /// <summary>
        /// Reads an untyped operand as the type the other operand has, which is what a general comparison does
        /// before consulting the table.
        /// </summary>
        /// <remarks>
        /// Against a number it becomes an <c>xs:double</c> rather than the other side's exact numeric type,
        /// which is what keeps <c>@n = 1</c> and <c>@n = 1.0</c> answering alike. Against anything else it is
        /// cast, and a cast that cannot be made is an error in the ordinary way — <c>@d = xs:date(...)</c>
        /// where the attribute does not hold a date has gone wrong, and saying false would hide it.
        /// </remarks>
        private static XPathValue ReadUntypedAs(
            XPathValue value, XPathValue other, ComparisonContext? where)
        {
            if (value.TypeCode != XdmTypeCode.UntypedAtomic)
            {
                return value;
            }

            if (other.TypeCode is XdmTypeCode.UntypedAtomic or XdmTypeCode.String or XdmTypeCode.AnyUri
                or XdmTypeCode.None)
            {
                return XPathValue.FromString(value.ToStringValue());
            }

            if (IsNumeric(other.TypeCode))
            {
                return XdmType.UntypedAsDouble(value);
            }

            // The namespaces are the comparison's own, which is what lets an untyped operand be read as a
            // name at all: 'my:problem' is a different QName in each branch of an xsl:choose that binds the
            // prefix differently, and the cast has no other place to look.
            return XdmType.TryGet(PrimitiveNameOf(other), out XdmType.BuiltInType type)
                ? XdmType.Cast(value, type, where?.Namespaces)
                : XPathValue.FromString(value.ToStringValue());
        }

        /// <summary>The name of the built-in type an operand belongs to, as written after <c>xs:</c>.</summary>
        private static string PrimitiveNameOf(XPathValue value)
        {
            return value.TypeCode switch
            {
                XdmTypeCode.Boolean => "boolean",
                XdmTypeCode.Date => "date",
                XdmTypeCode.Time => "time",
                XdmTypeCode.DateTime => "dateTime",
                XdmTypeCode.Duration => "duration",
                XdmTypeCode.YearMonthDuration => "yearMonthDuration",
                XdmTypeCode.DayTimeDuration => "dayTimeDuration",
                XdmTypeCode.QName => "QName",
                XdmTypeCode.AnyUri => "anyURI",
                XdmTypeCode.HexBinary => "hexBinary",
                XdmTypeCode.Base64Binary => "base64Binary",
                XdmTypeCode.Gregorian => value.AsGregorian().Name,
                _ => "string",
            };
        }

        /// <summary>
        /// Compares two operands whose untyped halves have already been read, giving the sign of the result.
        /// </summary>
        /// <exception cref="XsltException">The pair names no comparison, or names one with no order.</exception>
        private static int SignOf(
            XPathValue left, XPathValue right, BinaryOperator op, ComparisonContext? where)
        {
            Kind kind = Classify(left, right, out bool ordered);

            if (kind == Kind.None)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"There is no comparison between {Spell(left)} and {Spell(right)}. XPath 2.0 compares "
                    + "values of compatible types and refuses the rest rather than converting one to the "
                    + "other; cast one side if that is what was meant.");
            }

            if (!ordered && op is not (BinaryOperator.Equal or BinaryOperator.NotEqual))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"{Spell(left)} and {Spell(right)} can be compared for equality but not put in order.");
            }

            switch (kind)
            {
                case Kind.Text:
                    // By the collation in scope where the comparison was written, and by code point where
                    // that is what it is — which is the answer wherever a stylesheet has not said otherwise.
                    return where?.Collation is Collation collation
                        ? collation.Compare(left.ToStringValue(), right.ToStringValue())
                        : Math.Sign(string.CompareOrdinal(left.ToStringValue(), right.ToStringValue()));

                case Kind.Boolean:
                    return left.ToBoolean().CompareTo(right.ToBoolean());

                case Kind.Moment:
                    return left.AsDateTime().CompareTo(right.AsDateTime());

                case Kind.Binary:
                    return Math.Sign(left.AsBinary().CompareTo(right.AsBinary()));

                case Kind.Duration:
                    // Two durations of different halves are still equal or not — they simply cannot be put in
                    // order, which the check above has already settled. P1Y and P1D are not the same length,
                    // and saying so needs no common scale.
                    return ordered
                        ? left.AsDuration().CompareTo(right.AsDuration())
                        : left.AsDuration().Equals(right.AsDuration()) ? 0 : 1;

                case Kind.Identity:
                    return SameValue(left, right) ? 0 : 1;

                default:
                    return NumericSign(left, right);
            }
        }

        /// <summary>
        /// Decides which comparison a pair of operand types calls for, and whether it has an order.
        /// </summary>
        private static Kind Classify(XPathValue left, XPathValue right, out bool ordered)
        {
            XdmTypeCode a = left.TypeCode;
            XdmTypeCode b = right.TypeCode;
            ordered = true;

            if (IsNumeric(a) && IsNumeric(b))
            {
                return Kind.Numeric;
            }

            if (IsText(a) && IsText(b))
            {
                return Kind.Text;
            }

            if (a == XdmTypeCode.Boolean && b == XdmTypeCode.Boolean)
            {
                return Kind.Boolean;
            }

            // A date is comparable to a date and a time to a time, but a date is not comparable to a dateTime:
            // one names a day and the other a moment within one, and neither contains the other.
            if (a is XdmTypeCode.Date or XdmTypeCode.Time or XdmTypeCode.DateTime && b == a)
            {
                return Kind.Moment;
            }

            if (IsDuration(a) && IsDuration(b))
            {
                // Only two durations of one kind share a scale. A year-month duration against a day-time one
                // has no order, because how long a month is depends on which month; and xs:duration itself may
                // carry both halves, so two of those have none either.
                ordered = a == b && a != XdmTypeCode.Duration;
                return Kind.Duration;
            }

            // Two binary values of one type order by their octets, which XPath 3.1 defines and 2.0 left
            // undefined. The two types are still not compared to each other: xs:hexBinary and
            // xs:base64Binary are distinct types with no conversion between them, however alike their
            // contents look.
            if (a == b && a is XdmTypeCode.HexBinary or XdmTypeCode.Base64Binary)
            {
                return Kind.Binary;
            }

            if (a == b && a == XdmTypeCode.QName)
            {
                ordered = false;
                return Kind.Identity;
            }

            // The five gregorian types share one representation here, so the name is what tells a gYear from a
            // gMonth — and there is no comparison between the two.
            if (a == XdmTypeCode.Gregorian && b == XdmTypeCode.Gregorian
                && string.Equals(left.AsGregorian().Name, right.AsGregorian().Name, StringComparison.Ordinal))
            {
                ordered = false;
                return Kind.Identity;
            }

            return Kind.None;
        }

        /// <summary>Compares two values that are equal or not and have no order.</summary>
        private static bool SameValue(XPathValue left, XPathValue right)
        {
            return left.TypeCode switch
            {
                XdmTypeCode.QName => left.AsQName().Denotes(right.AsQName()),
                XdmTypeCode.Gregorian => left.AsGregorian().SameValue(right.AsGregorian()),
                _ => left.AsBinary().SameBytes(right.AsBinary()),
            };
        }

        /// <summary>
        /// Compares two numbers in the widest type either of them needs, so that two integers too large for a
        /// double to hold still compare exactly.
        /// </summary>
        private static int NumericSign(XPathValue left, XPathValue right)
        {
            if (left.TypeCode == XdmTypeCode.Integer && right.TypeCode == XdmTypeCode.Integer)
            {
                return left.ToInteger().CompareTo(right.ToInteger());
            }

            if (left.TypeCode is XdmTypeCode.Integer or XdmTypeCode.Decimal
                && right.TypeCode is XdmTypeCode.Integer or XdmTypeCode.Decimal)
            {
                return left.ToDecimal().CompareTo(right.ToDecimal());
            }

            // The promotion ladder is decimal, then float, then double, and a comparison climbs only as far
            // as it must. So a decimal beside a float becomes that float and not the double neither of them
            // is: xs:decimal(1.01) and xs:float(1.01) are equal, where read as doubles they would not be.
            if (left.TypeCode is XdmTypeCode.Integer or XdmTypeCode.Decimal or XdmTypeCode.Float
                && right.TypeCode is XdmTypeCode.Integer or XdmTypeCode.Decimal or XdmTypeCode.Float)
            {
                float x = AsFloat(left);
                float y = AsFloat(right);

                return float.IsNaN(x) || float.IsNaN(y) ? Unordered : x < y ? -1 : x > y ? 1 : 0;
            }

            double a = left.ToNumber();
            double b = right.ToNumber();

            if (double.IsNaN(a) || double.IsNaN(b))
            {
                return Unordered;
            }

            return a < b ? -1 : a > b ? 1 : 0;
        }

        /// <summary>
        /// Reads a number as an <see cref="float"/>, from its own representation rather than by way of a
        /// double, so that the value is rounded once instead of twice.
        /// </summary>
        private static float AsFloat(XPathValue value)
        {
            return value.TypeCode switch
            {
                XdmTypeCode.Decimal => (float)value.ToDecimal(),
                XdmTypeCode.Integer => value.ToInteger(),
                _ => (float)value.ToNumber(),
            };
        }

        /// <summary>Reads the sign of a comparison through the operator that asked for it.</summary>
        private static bool Apply(int sign, BinaryOperator op)
        {
            if (sign == Unordered)
            {
                // NaN is in no relation to anything, so only 'not equal' holds.
                return op == BinaryOperator.NotEqual;
            }

            return op switch
            {
                BinaryOperator.Equal => sign == 0,
                BinaryOperator.NotEqual => sign != 0,
                BinaryOperator.LessThan => sign < 0,
                BinaryOperator.LessThanOrEqual => sign <= 0,
                BinaryOperator.GreaterThan => sign > 0,
                _ => sign >= 0,
            };
        }

        /// <summary>Names an operand's type for a message, since the type is what the reader needs.</summary>
        private static string Spell(XPathValue value)
        {
            return value.TypeCode switch
            {
                XdmTypeCode.None => "a node",
                XdmTypeCode.UntypedAtomic => "an untyped value",
                XdmTypeCode.String => "an xs:string",
                XdmTypeCode.Integer => "an xs:integer",
                XdmTypeCode.Decimal => "an xs:decimal",
                XdmTypeCode.Float => "an xs:float",
                XdmTypeCode.Double => "an xs:double",
                XdmTypeCode.Gregorian => "an xs:" + value.AsGregorian().Name,
                _ => "an xs:" + PrimitiveNameOf(value),
            };
        }

        /// <summary>Whether a type is one of the four numeric ones.</summary>
        internal static bool IsNumeric(XdmTypeCode type)
        {
            return type is XdmTypeCode.Integer or XdmTypeCode.Decimal or XdmTypeCode.Float
                or XdmTypeCode.Double;
        }

        /// <summary>
        /// Whether a type compares as text. <c>xs:anyURI</c> is a type of its own everywhere else, and here
        /// it is not: the operator mapping puts it with the strings, so a URI and a string compare.
        /// </summary>
        private static bool IsText(XdmTypeCode type)
        {
            return type is XdmTypeCode.String or XdmTypeCode.UntypedAtomic or XdmTypeCode.AnyUri;
        }

        /// <summary>Whether a type is one of the three duration ones.</summary>
        private static bool IsDuration(XdmTypeCode type)
        {
            return type is XdmTypeCode.Duration or XdmTypeCode.YearMonthDuration
                or XdmTypeCode.DayTimeDuration;
        }
    }
}
