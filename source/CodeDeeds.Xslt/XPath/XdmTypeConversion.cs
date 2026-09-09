namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// What an <c>as</c> declaration does to a value: check it against the declared type, and make the
    /// conversions the specification allows on the way.
    /// </summary>
    /// <remarks>
    /// These are the function conversion rules, which XSLT applies wherever a type is declared — a variable, a
    /// parameter, a function's argument or its result. They are deliberately few: a node is atomized, an
    /// untyped value is read as the type wanted, a number is promoted to a wider one, and an
    /// <c>xs:anyURI</c> is promoted to an <c>xs:string</c>. Nothing else is converted, so a stylesheet
    /// declaring <c>xs:integer</c> and producing a date hears about it rather than getting a number that
    /// means nothing.
    /// </remarks>
    internal static class XdmTypeConversion
    {
        /// <summary>Applies a declared type to a value.</summary>
        /// <param name="value">The value produced.</param>
        /// <param name="type">The declared type, or <see langword="null"/> where nothing was declared.</param>
        /// <returns>The value, converted where the rules allow it.</returns>
        /// <exception cref="XsltException">The value cannot be made to fit the type.</exception>
        /// <param name="value">The value to check, and to convert where conversion is allowed.</param>
        /// <param name="type">The declared type, or <see langword="null"/> where nothing was declared.</param>
        /// <param name="code">
        /// The error to raise where the value does not match. XPath has one code for a type error and XSLT
        /// has a different one for each place an <c>as</c> may be written, so the caller says which of them
        /// this is: a test asking for <c>XTTE0570</c> is asking about a variable in particular.
        /// </param>
        public static XPathValue Apply(
            XPathValue value,
            XdmSequenceType? type,
            XsltErrorCode code = XsltErrorCode.XPTY0004)
        {
            if (type is null)
            {
                return value;
            }

            // Function coercion happens before anything is matched (XPath 3.1 §3.4.2). An item whose
            // declared types this engine does not record would otherwise match on its arity alone and be
            // handed on unchecked, and being checked when it is called is the whole of what a coercion is
            // for.
            if (type.IsWrittenFunctionType)
            {
                value = CoerceFunctions(value, type);
            }

            if (type.Matches(value))
            {
                return value;
            }

            XPathValue converted;

            try
            {
                converted = Convert(value, type);
            }
            catch (XsltException failed) when (failed.Code == "FORG0001" && code != XsltErrorCode.XPTY0004)
            {
                // An untyped value that cannot be cast to the type declared is a value that does not match
                // it, and where the caller named a code of XSLT's — a variable's XTTE0570, a parameter's
                // XTTE0590 — that code is the answer. A function call keeps XPath's: the cast failed.
                throw XsltErrors.Error(
                    code,
                    $"A value of type {Describe(value)} was produced where {type} was declared, and "
                    + $"cannot be read as one: {failed.Message}",
                    failed);
            }


            return type.Matches(converted)
                ? converted
                : throw XsltErrors.Error(
                    code,
                    $"A value of type {Describe(value)} was produced where {type} was declared.");
        }

        /// <summary>Coerces each function item of a value to a required function type.</summary>
        /// <remarks>
        /// A map and an array are function items too, and are left alone: coercing one would give back
        /// something that is no longer a map, and the required type would have to have asked for that
        /// rather than for a function.
        /// </remarks>
        private static XPathValue CoerceFunctions(XPathValue value, XdmSequenceType type)
        {
            if (value.Kind == XPathValueKind.Function)
            {
                return type.Coerce(value);
            }

            if (value.Kind != XPathValueKind.Sequence)
            {
                return value;
            }

            List<XPathValue> items = XdmSequence.Items(value);
            List<XPathValue> coerced = new List<XPathValue>(items.Count);

            foreach (XPathValue item in items)
            {
                coerced.Add(item.Kind == XPathValueKind.Function ? type.Coerce(item) : item);
            }

            return XdmSequence.Concatenate(coerced);
        }

        private static XPathValue Convert(XPathValue value, XdmSequenceType type)
        {
            List<XPathValue> items = XdmSequence.Items(value);
            List<XPathValue> converted = new List<XPathValue>(items.Count);

            foreach (XPathValue item in items)
            {
                converted.Add(ConvertItem(item, type));
            }

            return XdmSequence.Concatenate(converted);
        }

        private static XPathValue ConvertItem(XPathValue item, XdmSequenceType type)
        {
            if (!type.WantsAtomic)
            {
                // A node type wants nodes, and nothing turns a value into one.
                return item;
            }

            // Atomization: a node contributes its typed value, which here is always untyped.
            XPathValue atomic = item.Kind is XPathValueKind.Node or XPathValueKind.NodeSet
                ? XPathValue.FromUntypedAtomic(XdmSequence.StringValueOf(item))
                : item;

            XdmTypeCode? wanted = type.AtomicType;

            if (wanted is null)
            {
                // xs:anyAtomicType and xs:numeric name no one type to be converted towards, so atomizing is
                // the whole of what the rules can do for them — and what a node atomizes to is an atomic
                // value, which is what the first of the two asked for.
                return atomic;
            }

            if (atomic.TypeCode == wanted)
            {
                return atomic;
            }

            // An untyped value takes the type asked for, which is what makes document content usable against
            // a declaration without the stylesheet casting every reference by hand. The type is the one that
            // was written rather than one worked back from the code: five Gregorian types share a code, so
            // working backwards lands on none of them and xs:gYear content converted to nothing at all.
            if (atomic.TypeCode == XdmTypeCode.UntypedAtomic)
            {
                if (type.BuiltIn is XdmType.BuiltInType written)
                {
                    return XdmType.Cast(atomic, written);
                }

                if (XdmType.TryGet(NameOf(wanted.Value), out XdmType.BuiltInType target))
                {
                    return XdmType.Cast(atomic, target);
                }
            }

            // Numeric promotion, and only upwards: an integer is a decimal is a float is a double.
            if (IsNumeric(atomic.TypeCode) && IsNumeric(wanted.Value) && Width(wanted.Value) >= Width(atomic.TypeCode))
            {
                return XdmType.TryGet(NameOf(wanted.Value), out XdmType.BuiltInType numeric)
                    ? XdmType.Cast(atomic, numeric)
                    : atomic;
            }

            // The other promotion the rules allow: an xs:anyURI where an xs:string was declared. It goes
            // the one way and it really converts, so what comes out is a string and is no longer a URI —
            // which is the whole difference between a promotion and the subtype relation the two do not
            // stand in.
            if (wanted.Value == XdmTypeCode.String && atomic.TypeCode == XdmTypeCode.AnyUri)
            {
                return XPathValue.FromString(atomic.ToStringValue());
            }

            return atomic;
        }

        private static bool IsNumeric(XdmTypeCode type)
        {
            return type is XdmTypeCode.Integer or XdmTypeCode.Decimal or XdmTypeCode.Float
                or XdmTypeCode.Double;
        }

        private static int Width(XdmTypeCode type)
        {
            return type switch
            {
                XdmTypeCode.Integer => 0,
                XdmTypeCode.Decimal => 1,
                XdmTypeCode.Float => 2,
                _ => 3,
            };
        }

        private static string NameOf(XdmTypeCode type)
        {
            return type switch
            {
                XdmTypeCode.String => "string",
                XdmTypeCode.Boolean => "boolean",
                XdmTypeCode.Double => "double",
                XdmTypeCode.Float => "float",
                XdmTypeCode.Decimal => "decimal",
                XdmTypeCode.Integer => "integer",
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
                XdmTypeCode.UntypedAtomic => "untypedAtomic",
                _ => "anyAtomicType",
            };
        }

        internal static string Describe(XPathValue value)
        {
            List<XPathValue> items = XdmSequence.Items(value);

            return items.Count switch
            {
                0 => "the empty sequence",
                1 => items[0].Kind is XPathValueKind.Node or XPathValueKind.NodeSet
                    ? "a node"
                    : items[0].TypeCode.ToString(),
                _ => $"a sequence of {items.Count}",
            };
        }
    }
}
