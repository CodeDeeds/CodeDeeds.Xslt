using System.Globalization;
using System.Numerics;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// Rounding a double at a number of decimal places, exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scaling by a power of ten and rounding cannot answer this. A double is a binary fraction and almost
    /// never the decimal it was written as: <c>250.025</c> is really 250.0250000000000056843…, which is
    /// <em>above</em> the half and rounds up, and <c>13.65</c> is really 13.6500000000000003552…, so
    /// <c>round(-13.65, 1)</c> is −13.7 and not −13.6. Multiplying either by ten or a hundred lands it
    /// exactly on a half, because the difference is smaller than the spacing of doubles at that size, and
    /// the answer is then decided by a tie that was never there.
    /// </para>
    /// <para>
    /// So the value is taken as the rational it is — a mantissa over a power of two — and compared against
    /// the half on integers, which lose nothing. The result goes back through its decimal text rather than
    /// through a division, so it is the double nearest the rounded value rather than one arrived at by
    /// dividing by a power of ten that is not itself exact.
    /// </para>
    /// </remarks>
    internal static class XdmRounding
    {
        /// <summary>
        /// Rounds a double at a number of decimal places.
        /// </summary>
        /// <param name="value">The value to round.</param>
        /// <param name="places">
        /// How many decimal places to keep; negative to round to tens, hundreds and beyond.
        /// </param>
        /// <param name="halfToEven">
        /// Whether a half goes to the nearest even digit, which is <c>round-half-to-even()</c>; otherwise it
        /// goes towards positive infinity, which is <c>round()</c>.
        /// </param>
        public static double At(double value, int places, bool halfToEven)
        {
            if (!double.IsFinite(value) || value == 0.0)
            {
                return value;
            }

            // No double has a digit below the smallest subnormal, about 5e-324, so keeping more places than
            // that changes nothing; and none is larger than about 1.8e308, so rounding above that leaves a
            // zero of the value's own sign.
            if (places > 340)
            {
                return value;
            }

            if (places < -340)
            {
                return double.IsNegative(value) ? -0.0 : 0.0;
            }

            if (places == 0)
            {
                return Whole(value, halfToEven);
            }

            // The value as mantissa × 2^exponent, which is what it is.
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;

            if (exponent == 0)
            {
                exponent = -1074;
            }
            else
            {
                mantissa |= 1L << 52;
                exponent -= 1075;
            }

            BigInteger numerator = mantissa;
            BigInteger denominator = BigInteger.One;

            if (exponent >= 0)
            {
                numerator <<= exponent;
            }
            else
            {
                denominator <<= -exponent;
            }

            if (places > 0)
            {
                numerator *= BigInteger.Pow(10, places);
            }
            else
            {
                denominator *= BigInteger.Pow(10, -places);
            }

            BigInteger whole = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
            int half = (remainder * 2).CompareTo(denominator);
            bool negative = double.IsNegative(value);

            // The magnitude is what is being rounded, so towards positive infinity means up for a positive
            // value and down for a negative one.
            if (half > 0 || (half == 0 && (halfToEven ? !whole.IsEven : !negative)))
            {
                whole += BigInteger.One;
            }

            double rounded = double.Parse(
                whole.ToString(CultureInfo.InvariantCulture) + "E"
                    + (-places).ToString(CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture);

            return negative ? -rounded : rounded;
        }

        /// <summary>
        /// Rounds at the point, where no scaling is needed and nothing can be lost.
        /// </summary>
        /// <remarks>
        /// The fractional part of a double is itself a double and exact, so comparing it against a half
        /// compares two values that are each exactly what they say. <c>Math.Floor(value + 0.5)</c> is not
        /// this: adding a half to the double just below it carries into the next integer.
        /// </remarks>
        private static double Whole(double value, bool halfToEven)
        {
            double floor = Math.Floor(value);
            double part = value - floor;

            bool up = part > 0.5
                || (part == 0.5 && (!halfToEven || Math.Abs(floor % 2.0) == 1.0));

            double whole = up ? floor + 1.0 : floor;

            // Both zeros are values of their own and write differently, so rounding −0.2 gives −0.
            return whole == 0.0 && double.IsNegative(value) ? -0.0 : whole;
        }
    }

    /// <summary>
    /// Arithmetic under XPath 2.0, where the type of the result follows from the types of the operands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 1.0 has one numeric type, so <c>1 + 1</c> is a double and there is nothing to decide. XPath 2.0
    /// has four, and an operator first promotes both operands to the least type that can hold them both:
    /// integer, then decimal, then float, then double. So <c>1 + 1</c> is an <c>xs:integer</c> and
    /// <c>1 + 1.5</c> an <c>xs:decimal</c>, and the difference shows in the result's lexical form as much as
    /// in its value — an integer never grows a decimal point.
    /// </para>
    /// <para>
    /// Division is the exception that catches people out: <c>1 div 2</c> is <c>0.5</c>, not <c>0</c>, because
    /// dividing two integers yields an <c>xs:decimal</c>. Truncating division is spelled <c>idiv</c>.
    /// </para>
    /// <para>
    /// An operand drawn from a node is untyped, and untyped operands become doubles — which is what keeps
    /// arithmetic on document content behaving as it did under 1.0.
    /// </para>
    /// </remarks>
    internal static class XdmArithmetic
    {
        /// <summary>Applies an arithmetic operator under XPath 2.0's type rules.</summary>
        /// <param name="op">The operator.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <returns>The result, typed by promotion.</returns>
        public static XPathValue Apply(BinaryOperator op, XPathValue left, XPathValue right)
        {
            // An operand is atomized first, and an empty one makes the whole expression empty rather than
            // NaN: there is nothing to add, so there is no number to give back.
            if (!TryAtomizeOperand(left, op, out left) || !TryAtomizeOperand(right, op, out right))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            // An untyped operand becomes a double before anything else looks at it, and text that is not a
            // number says so here rather than turning the sum into NaN.
            left = XdmType.UntypedAsDouble(left);
            right = XdmType.UntypedAsDouble(right);

            // Dates and durations are not numbers and do not promote to one. They have arithmetic of their
            // own, where the types of the operands decide the type of the result rather than a common type
            // being found: a date minus a date is a duration, and a date plus a duration is a date.
            if (IsTemporal(left) || IsTemporal(right))
            {
                return TemporalArithmetic(op, left, right);
            }

            RequireNumeric(op, left, right);

            XdmTypeCode promoted = Promote(TypeOf(left), TypeOf(right));

            return promoted switch
            {
                XdmTypeCode.Integer => IntegerArithmetic(op, left, right),
                XdmTypeCode.Decimal => DecimalArithmetic(op, left.ToDecimal(), right.ToDecimal()),
                XdmTypeCode.Float => FloatArithmetic(op, left.ToNumber(), right.ToNumber()),
                _ => DoubleResult(op, left.ToNumber(), right.ToNumber()),
            };
        }

        /// <summary>
        /// Reduces an operand to the single atomic value arithmetic needs.
        /// </summary>
        /// <returns><see langword="false"/> when the operand is empty, which makes the whole result empty.</returns>
        /// <exception cref="XsltException">The operand holds more than one item.</exception>
        private static bool TryAtomizeOperand(XPathValue value, BinaryOperator op, out XPathValue single)
        {
            if (value.Kind is not (XPathValueKind.Sequence or XPathValueKind.NodeSet or XPathValueKind.Node))
            {
                single = value;
                return true;
            }

            List<XPathValue> items = XdmSequence.Items(value);

            if (items.Count != 1)
            {
                single = default;

                return items.Count == 0
                    ? false
                    : throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        $"'{Spell(op)}' takes one value on each side, but was given {items.Count}.");
            }

            // A node contributes its typed value: its text, untyped and so a double below, unless the
            // node was validated, when it is what its type says, and may be nothing at all.
            single = items[0].Kind == XPathValueKind.Node
                ? XdmSequence.TypedValueAsOne(items[0], $"'{Spell(op)}'")
                : items[0];

            if (single.Kind == XPathValueKind.Sequence)
            {
                single = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Refuses operands that arithmetic is not defined for.
        /// </summary>
        /// <remarks>
        /// Only the four numeric types and untyped values take part. A string does not: XPath 1.0 would read
        /// <c>1 + "2"</c> as 3 and <c>1 + "x"</c> as NaN, and 2.0 declines both, on the grounds that a
        /// stylesheet adding a string to a number has made a mistake worth hearing about. An untyped value is
        /// the exception that keeps document content working, since it is what every node atomizes to.
        /// </remarks>
        private static void RequireNumeric(BinaryOperator op, XPathValue left, XPathValue right)
        {
            if (!IsNumericOperand(left) || !IsNumericOperand(right))
            {
                throw Refuse(op, left, right);
            }
        }

        /// <summary>
        /// Reduces the operand of a unary sign to a number, refusing anything that is not one.
        /// </summary>
        /// <remarks>
        /// The same rule the binary operators follow, and it has to be applied here too or
        /// <c>-'a string'</c> comes back NaN — a value where the specification says there is none.
        /// </remarks>
        /// <param name="value">The operand.</param>
        /// <param name="symbol">The sign as written, for the message.</param>
        /// <returns>The operand as a number, or an empty sequence where the operand was empty.</returns>
        /// <exception cref="XsltException">The operand is not a number and cannot be read as one.</exception>
        internal static XPathValue RequireNumericOperand(XPathValue value, string symbol)
        {
            if (value.Kind is XPathValueKind.Sequence or XPathValueKind.NodeSet or XPathValueKind.Node)
            {
                List<XPathValue> items = XdmSequence.Items(value);

                if (items.Count == 0)
                {
                    return XPathValue.FromSequence(XdmSequence.Empty);
                }

                if (items.Count != 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        $"'{symbol}' takes one value, but was given {items.Count}.");
                }

                value = items[0].Kind == XPathValueKind.Node
                    ? XdmSequence.TypedValueAsOne(items[0], $"'{symbol}'")
                    : items[0];

                if (value.Kind == XPathValueKind.Sequence)
                {
                    return XPathValue.FromSequence(XdmSequence.Empty);
                }
            }

            if (!IsNumericOperand(value))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"'{symbol}' takes a number, and was given a value of type {value.TypeCode}.");
            }

            return XdmType.UntypedAsDouble(value);
        }

        private static bool IsNumericOperand(XPathValue value)
        {
            return value.TypeCode is XdmTypeCode.Integer or XdmTypeCode.Decimal or XdmTypeCode.Float
                or XdmTypeCode.Double or XdmTypeCode.UntypedAtomic or XdmTypeCode.None;
        }

        private static bool IsTemporal(XPathValue value)
        {
            return value.TypeCode is XdmTypeCode.Date or XdmTypeCode.Time or XdmTypeCode.DateTime
                or XdmTypeCode.Duration or XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration;
        }

        private static bool IsDuration(XPathValue value)
        {
            return value.TypeCode is XdmTypeCode.Duration or XdmTypeCode.YearMonthDuration
                or XdmTypeCode.DayTimeDuration;
        }

        /// <summary>
        /// Arithmetic on dates, times and durations.
        /// </summary>
        /// <remarks>
        /// Five shapes, and each yields a different type: date minus date is a duration, date plus or minus a
        /// duration is a date, duration plus or minus a duration is a duration, duration times a number is a
        /// duration, and duration divided by duration is a number. Anything else — a date times anything, a
        /// number plus a duration — has no meaning and is refused rather than coerced into one.
        /// </remarks>
        private static XPathValue TemporalArithmetic(BinaryOperator op, XPathValue left, XPathValue right)
        {
            bool leftDuration = IsDuration(left);
            bool rightDuration = IsDuration(right);

            if (leftDuration && rightDuration)
            {
                return DurationAndDuration(op, left, right);
            }

            if (!leftDuration && !rightDuration)
            {
                // Two moments subtract, and only when they are the same kind of moment: a date names a day
                // and a time names an hour of an unstated one, so there is no gap between them to measure.
                if (op != BinaryOperator.Subtract || left.TypeCode != right.TypeCode)
                {
                    throw Refuse(op, left, right);
                }

                // The difference between two instants is a length of time, and never a number of months:
                // months are not all the same length, so no whole number of them names the gap.
                decimal seconds = XdmDateTime.SecondsBetween(left.AsDateTime(), right.AsDateTime());

                return XPathValue.FromDuration(
                    new XdmDuration(0, seconds, XdmTypeCode.DayTimeDuration));
            }

            if (leftDuration && !IsTemporal(right) && op is BinaryOperator.Multiply or BinaryOperator.Divide)
            {
                return ScaleDuration(op, left, right);
            }

            if (rightDuration && !IsTemporal(left) && op == BinaryOperator.Multiply)
            {
                return ScaleDuration(BinaryOperator.Multiply, right, left);
            }

            if (!leftDuration && op is BinaryOperator.Add or BinaryOperator.Subtract)
            {
                RequireShiftable(op, left, right);
                return Shift(left.AsDateTime(), right.AsDuration(), op == BinaryOperator.Subtract);
            }

            if (leftDuration && op == BinaryOperator.Add && IsTemporal(right))
            {
                RequireShiftable(op, right, left);
                return Shift(right.AsDateTime(), left.AsDuration(), subtract: false);
            }

            throw Refuse(op, left, right);
        }

        /// <summary>
        /// Refuses a duration that cannot move the moment it is being applied to.
        /// </summary>
        /// <remarks>
        /// Two cases. A plain <c>xs:duration</c> may carry both months and seconds and takes part in no
        /// arithmetic at all — only its two halves do, which is what they exist for. And a time has no date
        /// to move within, so a number of months means nothing to it.
        /// </remarks>
        private static void RequireShiftable(BinaryOperator op, XPathValue moment, XPathValue duration)
        {
            bool half = duration.TypeCode is XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration;

            if (!half
                || (moment.TypeCode == XdmTypeCode.Time
                    && duration.TypeCode == XdmTypeCode.YearMonthDuration))
            {
                throw Refuse(op, moment, duration);
            }
        }

        private static XPathValue DurationAndDuration(BinaryOperator op, XPathValue left, XPathValue right)
        {
            // Only two durations of one half combine. Adding a year-month duration to a day-time one has no
            // answer, because the result would carry both and no scale relates them; and a plain xs:duration
            // may already carry both, so it is not an operand here either.
            if (left.TypeCode != right.TypeCode
                || left.TypeCode is not (XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration))
            {
                throw Refuse(op, left, right);
            }

            XdmDuration a = left.AsDuration();
            XdmDuration b = right.AsDuration();

            switch (op)
            {
                case BinaryOperator.Add:
                case BinaryOperator.Subtract:
                {
                    int sign = op == BinaryOperator.Subtract ? -1 : 1;
                    return XPathValue.FromDuration(new XdmDuration(
                        a.Months + (sign * b.Months),
                        a.Seconds + (sign * b.Seconds),
                        a.Type));
                }

                case BinaryOperator.Divide:
                {
                    // Dividing a length by a length asks how many times one goes into the other, which is a
                    // number rather than a duration.
                    bool months = a.Type == XdmTypeCode.YearMonthDuration;
                    decimal divisor = months ? b.Months : b.Seconds;
                    decimal dividend = months ? a.Months : a.Seconds;

                    if (divisor == 0m)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.FOAR0001, "A duration cannot be divided by a zero-length duration.");
                    }

                    try
                    {
                        return XPathValue.FromDecimal(dividend / divisor);
                    }
                    catch (OverflowException)
                    {
                        // A very long duration over a very short one: the count of one in the other is a
                        // number, and this one is larger than any this engine holds.
                        throw XsltErrors.Error(
                            XsltErrorCode.FOAR0002,
                            $"'{a}' divided by '{b}' is outside the range of xs:decimal.");
                    }
                }

                default:
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        $"Two durations cannot be combined with '{Spell(op)}'.");
            }
        }

        private static XPathValue ScaleDuration(BinaryOperator op, XPathValue value, XPathValue by)
        {
            // A plain xs:duration does not scale: multiplying one that carries both months and seconds would
            // have to scale each half separately, and the two do not stay in step.
            if (value.TypeCode is not (XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration)
                || !IsNumericOperand(by))
            {
                throw Refuse(op, value, by);
            }

            return ScaleDuration(op, value.AsDuration(), by.ToNumber());
        }

        /// <summary>
        /// Multiplies or divides a duration by a number.
        /// </summary>
        /// <remarks>
        /// The arithmetic is exact wherever it can be, because fractional seconds are the point of holding
        /// them in a decimal: <c>PT3.1S * 3</c> is <c>PT9.3S</c> and not the 9.299999999999999 a double
        /// would give. A factor too large for a decimal is handled apart rather than converted, since
        /// converting it is what overflows — and the two operations want opposite answers there. Dividing by
        /// something enormous leaves no duration at all, which is a value; multiplying by it leaves one no
        /// representation can hold, which is an error.
        /// </remarks>
        private static XPathValue ScaleDuration(BinaryOperator op, XdmDuration duration, double factor)
        {
            if (double.IsNaN(factor))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOCA0005, "A duration cannot be scaled by a value that is not a number.");
            }

            // Dividing by zero leaves a duration longer than any that can be held, which is an overflow in
            // the duration space rather than the FOAR0001 a numeric division by zero raises. Nothing here is
            // a number: the answer would be a duration, and there is no infinite one to be the answer.
            if (op == BinaryOperator.Divide && factor == 0.0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FODT0002,
                    $"Dividing '{duration}' by zero gives a duration outside the range one can hold.");
            }

            if (double.IsInfinity(factor) || Math.Abs(factor) > (double)decimal.MaxValue)
            {
                // A duration of no length scaled by anything at all is still of no length. Nothing overflows
                // because nothing grows, and the answer is the operand — which is the one case where a
                // factor this large is not a question about range.
                if (duration.Months == 0 && duration.Seconds == 0m)
                {
                    return XPathValue.FromDuration(duration);
                }

                return op == BinaryOperator.Divide
                    ? XPathValue.FromDuration(new XdmDuration(0, 0m, duration.Type))
                    : throw Overflow(duration, factor);
            }

            try
            {
                // Divided rather than multiplied by the reciprocal, which is not the same number: a third
                // is 0.3333333333333333333333333333 in a decimal, and multiplying by that leaves
                // PT16H3M19.999999999999999999999994S where dividing leaves the PT16H3M20S it is.
                decimal scale = (decimal)factor;
                bool divide = op == BinaryOperator.Divide;
                decimal months = divide ? duration.Months / scale : duration.Months * scale;
                decimal seconds = divide ? duration.Seconds / scale : duration.Seconds * scale;

                // A whole number of months, rounded the way fn:round rounds: to the nearest, and a half to
                // whichever of the two is nearer positive infinity. P1M times 0.5 is P1M and not P0M,
                // and P5M divided by -2 is -P2M and not -P3M.
                //
                // Written out rather than asked of Math.Round, whose default sends a half to the even
                // neighbour and whose MidpointRounding.ToPositiveInfinity is not a rule about halves at
                // all: it is a ceiling, and would carry 3.1 months up to 4 along with the halves.
                //
                // Seconds are not rounded: a dayTimeDuration holds a fraction of a second, so there is
                // nothing there to round to.
                return XPathValue.FromDuration(new XdmDuration(
                    (int)decimal.Floor(months + 0.5m), seconds, duration.Type));
            }
            catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
            {
                throw Overflow(duration, factor);
            }
        }

        private static XsltException Overflow(XdmDuration duration, double factor)
        {
            return XsltErrors.Error(
                XsltErrorCode.FODT0002,
                $"Scaling '{duration}' by {factor} gives a duration outside the range one can hold.");
        }

        /// <summary>
        /// Moves a date, time or dateTime by a duration.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Months are added as months rather than as a number of days, because they are not all the same
        /// length: one month after 31 January is 28 February, which no count of days would produce.
        /// </para>
        /// <para>
        /// A moment can be moved off the end of the range this engine holds, and that is an overflow in a
        /// date operation rather than an argument being wrong — which is all the bare
        /// <see cref="ArgumentOutOfRangeException"/> reaching the caller would say about it.
        /// </para>
        /// </remarks>
        private static XPathValue Shift(XdmDateTime value, XdmDuration duration, bool subtract)
        {
            int sign = subtract ? -1 : 1;

            try
            {
                // The seconds are moved as ticks rather than through AddSeconds, which takes a double:
                // 446400.3 is not a double, and the 4464002999999 ticks it truncates to would put
                // 00:12:00Z plus P5DT4H0M0.3S at 04:12:00.2999999Z rather than at the tenth it names.
                return XPathValue.FromDateTime(value.Moved(
                    sign * duration.Months,
                    (long)decimal.Round(sign * duration.Seconds * TimeSpan.TicksPerSecond)));
            }
            catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FODT0001,
                    $"Moving '{value}' by '{duration}' leaves the range this engine holds dates in, which "
                    + $"runs to the year {XdmDateTime.MaxYear} either side of the common era.",
                    error);
            }
        }

        private static XdmTypeCode Wider(XdmTypeCode left, XdmTypeCode right)
        {
            // Two durations of one kind stay that kind; mixing them loses the distinction, since the result
            // may carry both months and seconds and only xs:duration can say so.
            return left == right ? left : XdmTypeCode.Duration;
        }

        private static XsltException Refuse(BinaryOperator op, XPathValue left, XPathValue right)
        {
            return XsltErrors.Error(
                XsltErrorCode.XPTY0004,
                $"'{Spell(op)}' has no meaning between {left.TypeCode} and {right.TypeCode}.");
        }

        private static string Spell(BinaryOperator op)
        {
            return op switch
            {
                BinaryOperator.Add => "+",
                BinaryOperator.Subtract => "-",
                BinaryOperator.Multiply => "*",
                BinaryOperator.Divide => "div",
                BinaryOperator.IntegerDivide => "idiv",
                _ => "mod",
            };
        }

        /// <summary>
        /// The numeric type an operand contributes. Anything that is not already numeric — a string, a node's
        /// untyped value, a boolean — arrives as a double, as the specification's promotion rules require.
        /// </summary>
        private static XdmTypeCode TypeOf(XPathValue value)
        {
            return value.Kind == XPathValueKind.Number
                && value.TypeCode is XdmTypeCode.Integer or XdmTypeCode.Decimal or XdmTypeCode.Float
                ? value.TypeCode
                : XdmTypeCode.Double;
        }

        /// <summary>The least type that can hold both operands.</summary>
        private static XdmTypeCode Promote(XdmTypeCode left, XdmTypeCode right)
        {
            if (left == XdmTypeCode.Double || right == XdmTypeCode.Double)
            {
                return XdmTypeCode.Double;
            }

            if (left == XdmTypeCode.Float || right == XdmTypeCode.Float)
            {
                return XdmTypeCode.Float;
            }

            return left == XdmTypeCode.Integer && right == XdmTypeCode.Integer
                ? XdmTypeCode.Integer
                : XdmTypeCode.Decimal;
        }

        /// <summary>
        /// Integer arithmetic, which overflows loudly.
        /// </summary>
        /// <remarks>
        /// <c>xs:integer</c> is unbounded in the specification, and an implementation is allowed to support
        /// only part of that range — but only on condition that exceeding it is reported rather than silently
        /// wrapped. An unchecked multiply would give a wrong answer, which is the one outcome not permitted,
        /// so every operation here is checked.
        /// </remarks>
        private static XPathValue IntegerArithmetic(BinaryOperator op, XPathValue first, XPathValue second)
        {
            // An operand already too wide for 64 bits goes the wide way without trying the narrow one,
            // which would refuse to hand over either number in the first place.
            if (first.IsWideInteger || second.IsWideInteger)
            {
                return WideIntegerArithmetic(op, first.ToBigInteger(), second.ToBigInteger());
            }

            long left = first.ToInteger();
            long right = second.ToInteger();

            try
            {
                checked
                {
                    switch (op)
                    {
                        case BinaryOperator.Add:
                            return XPathValue.FromInteger(left + right);

                        case BinaryOperator.Subtract:
                            return XPathValue.FromInteger(left - right);

                        case BinaryOperator.Multiply:
                            return XPathValue.FromInteger(left * right);

                        case BinaryOperator.IntegerDivide:
                            return right == 0
                                ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Integer division by zero.")
                                : XPathValue.FromInteger(left / right);

                        case BinaryOperator.Modulo:
                            return right == 0
                                ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Integer division by zero.")
                                : XPathValue.FromInteger(left % right);

                        default:
                            // Dividing two integers yields a decimal, not an integer: 1 div 2 is 0.5.
                            return right == 0
                                ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Division by zero.")
                                : XPathValue.FromDecimal((decimal)left / right);
                    }
                }
            }
            catch (OverflowException)
            {
                // Past what 64 bits hold, which xs:integer is not bounded by: the answer is a wider
                // integer rather than an error. Reached only where the narrow arithmetic actually
                // overflowed, so nothing that fits pays for it.
                return WideIntegerArithmetic(op, left, right);
            }
        }

        /// <summary>
        /// The same arithmetic for integers that no 64-bit one holds.
        /// </summary>
        /// <remarks>
        /// Division is the one operation that does not stay among the integers — <c>1 div 2</c> is the
        /// decimal 0.5 — so it is handed to the decimal arithmetic, which refuses in its own words if
        /// the operands are wider than a decimal reaches.
        /// </remarks>
        private static XPathValue WideIntegerArithmetic(
            BinaryOperator op, System.Numerics.BigInteger left, System.Numerics.BigInteger right)
        {
            switch (op)
            {
                case BinaryOperator.Add:
                    return XPathValue.FromInteger(left + right);

                case BinaryOperator.Subtract:
                    return XPathValue.FromInteger(left - right);

                case BinaryOperator.Multiply:
                    return XPathValue.FromInteger(left * right);

                case BinaryOperator.IntegerDivide:
                    return right.IsZero
                        ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Integer division by zero.")
                        : XPathValue.FromInteger(left / right);

                case BinaryOperator.Modulo:
                    return right.IsZero
                        ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Integer division by zero.")
                        : XPathValue.FromInteger(left % right);

                default:
                    return right.IsZero
                        ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Division by zero.")
                        : DecimalArithmetic(op, ToDecimalBound(left, op), ToDecimalBound(right, op));
            }
        }

        /// <summary>An integer as a decimal, saying which operation could not be carried out if it will not fit.</summary>
        private static decimal ToDecimalBound(System.Numerics.BigInteger value, BinaryOperator op)
        {
            try
            {
                return (decimal)value;
            }
            catch (OverflowException)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOAR0002,
                    $"'{Symbol(op)}' gives an xs:decimal here, and {value} is outside the range of one.");
            }
        }

        private static XPathValue DecimalArithmetic(BinaryOperator op, decimal left, decimal right)
        {
            try
            {
                switch (op)
                {
                    case BinaryOperator.Add:
                        return XPathValue.FromDecimal(left + right);

                    case BinaryOperator.Subtract:
                        return XPathValue.FromDecimal(left - right);

                    case BinaryOperator.Multiply:
                        return XPathValue.FromDecimal(left * right);

                    case BinaryOperator.IntegerDivide:
                        return right == 0m
                            ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Integer division by zero.")
                            : XPathValue.FromInteger((long)decimal.Truncate(left / right));

                    case BinaryOperator.Modulo:
                        return right == 0m
                            ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Division by zero.")
                            : XPathValue.FromDecimal(left % right);

                    default:
                        return right == 0m
                            ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Division by zero.")
                            : XPathValue.FromDecimal(left / right);
                }
            }
            catch (OverflowException)
            {
                throw XsltErrors.Error(XsltErrorCode.FOAR0002,
                    $"The result of {left} {Symbol(op)} {right} is outside the range of xs:decimal.");
            }
        }

        /// <summary>
        /// Float arithmetic, carried out in double and rounded back, so that the result is a value a float can
        /// actually hold.
        /// </summary>
        private static XPathValue FloatArithmetic(BinaryOperator op, double left, double right)
        {
            double result = DoubleArithmetic(op, left, right);

            return op == BinaryOperator.IntegerDivide
                ? Quotient(result, left, right)
                : XPathValue.FromFloat((float)result);
        }

        /// <summary>
        /// The result of <c>idiv</c>, which is an <c>xs:integer</c> however wide its operands were.
        /// </summary>
        /// <remarks>
        /// The one operator whose result type does not follow from promoting the operands: it asks how many
        /// times one number goes into another, and that is a count.
        /// </remarks>
        private static XPathValue DoubleResult(BinaryOperator op, double left, double right)
        {
            double result = DoubleArithmetic(op, left, right);

            return op == BinaryOperator.IntegerDivide
                ? Quotient(result, left, right)
                : XPathValue.FromNumber(result);
        }

        private static XPathValue Quotient(double result, double left, double right)
        {
            if (result is < long.MinValue or > long.MaxValue)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOAR0002,
                    $"The result of {left} idiv {right} is outside the range of xs:integer.");
            }

            return XPathValue.FromInteger((long)result);
        }

        private static double DoubleArithmetic(BinaryOperator op, double left, double right)
        {
            return op switch
            {
                BinaryOperator.Add => left + right,
                BinaryOperator.Subtract => left - right,
                BinaryOperator.Multiply => left * right,
                BinaryOperator.Modulo => left % right,

                // Unlike div, idiv is defined only where the result is an integer, so infinity and NaN are
                // errors here rather than results. A zero divisor is its own error, and the same one it is
                // for the integer types: the operands being doubles does not make it an overflow.
                BinaryOperator.IntegerDivide => right == 0.0
                    ? throw XsltErrors.Error(XsltErrorCode.FOAR0001, "Integer division by zero.")
                    : double.IsNaN(left) || double.IsNaN(right) || double.IsInfinity(left)
                        ? throw XsltErrors.Error(XsltErrorCode.FOAR0002, "idiv requires finite operands.")
                        : Math.Truncate(left / right),

                _ => left / right,
            };
        }

        private static string Symbol(BinaryOperator op)
        {
            return op switch
            {
                BinaryOperator.Add => "+",
                BinaryOperator.Subtract => "-",
                BinaryOperator.Multiply => "*",
                BinaryOperator.IntegerDivide => "idiv",
                BinaryOperator.Modulo => "mod",
                _ => "div",
            };
        }
    }
}
