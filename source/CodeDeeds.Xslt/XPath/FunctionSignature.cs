using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// What a built-in function accepts in one argument position, and the conversion that gets a value there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 2.0 declares every function in the core library with a type for each parameter, and applies the
    /// <em>function conversion rules</em> to each argument before the call: atomize where an atomic type is
    /// wanted, read an untyped value as the declared type, promote a number to a wider one — and refuse
    /// anything the rules do not reach, with <c>XPTY0004</c>. Without the declarations there is nothing to
    /// refuse against, and <c>fn:abs(xs:string('1'))</c> quietly answers 1.
    /// </para>
    /// <para>
    /// Only three conversions exist, which is what makes the rules worth having: atomization, untyped to the
    /// declared type, and numeric promotion. A string is never read as a number, so a stylesheet that means a
    /// number has to say so. That is the same rule the arithmetic operators follow, applied at the other place
    /// a value crosses into typed territory.
    /// </para>
    /// <para>
    /// Written as text — <c>"xs:string?, xs:string, xs:string"</c> — so the table of signatures reads like the
    /// specification it comes from, and parsed once at start-up. The type names are the ones
    /// <see cref="XdmType"/> already knows, plus <c>item()</c>, <c>node()</c>, <c>element()</c> and
    /// <c>numeric</c> for the union of the four numeric types.
    /// </para>
    /// </remarks>
    internal sealed class FunctionParameter
    {
        /// <summary>The <c>item()*</c> parameter, which accepts anything and converts nothing.</summary>
        internal static readonly FunctionParameter Anything = new FunctionParameter(Shape.Item, default, true, true);

        /// <summary>What broad kind of thing this position accepts.</summary>
        private enum Shape
        {
            /// <summary><c>item()</c>: a node or an atomic value, taken as it comes.</summary>
            Item,

            /// <summary><c>node()</c>, which is not atomized.</summary>
            Node,

            /// <summary><c>element()</c>.</summary>
            Element,

            /// <summary>The union of the four numeric types, which the specification writes as <c>numeric</c>.</summary>
            Numeric,

            /// <summary>One named atomic type.</summary>
            Atomic,
        }

        private readonly Shape m_shape;
        private readonly XdmType.BuiltInType m_type;

        private FunctionParameter(Shape shape, XdmType.BuiltInType type, bool optional, bool repeating)
        {
            m_shape = shape;
            m_type = type;
            Optional = optional;
            Repeating = repeating;
        }

        /// <summary>Gets whether an empty sequence is acceptable here.</summary>
        internal bool Optional { get; }

        /// <summary>Gets whether more than one item is acceptable here.</summary>
        internal bool Repeating { get; }

        /// <summary>Gets whether this position wants atomic values, and so atomizes what it is given.</summary>
        internal bool Atomizes => m_shape is Shape.Numeric or Shape.Atomic;

        /// <summary>
        /// Parses one signature, written as the specification writes it.
        /// </summary>
        /// <param name="signature">
        /// A comma-separated list of parameter types, each optionally suffixed with <c>?</c>, <c>*</c> or
        /// <c>+</c>. An empty string means the function takes no arguments.
        /// </param>
        /// <returns>One entry per parameter, in order.</returns>
        internal static FunctionParameter[] Parse(string signature)
        {
            if (signature.Length == 0)
            {
                return Array.Empty<FunctionParameter>();
            }

            string[] parts = signature.Split(',');
            FunctionParameter[] parameters = new FunctionParameter[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                parameters[i] = ParseOne(parts[i].Trim());
            }

            return parameters;
        }

        private static FunctionParameter ParseOne(string text)
        {
            bool optional = false;
            bool repeating = false;

            if (text.Length != 0)
            {
                switch (text[^1])
                {
                    case '?':
                        optional = true;
                        text = text[..^1];
                        break;

                    case '*':
                        optional = true;
                        repeating = true;
                        text = text[..^1];
                        break;

                    case '+':
                        repeating = true;
                        text = text[..^1];
                        break;
                }
            }

            switch (text)
            {
                case "item()":
                    return optional && repeating
                        ? Anything
                        : new FunctionParameter(Shape.Item, default, optional, repeating);

                case "node()":
                    return new FunctionParameter(Shape.Node, default, optional, repeating);

                case "element()":
                    return new FunctionParameter(Shape.Element, default, optional, repeating);

                case "numeric":
                    return new FunctionParameter(Shape.Numeric, default, optional, repeating);

                default:
                {
                    string name = text.StartsWith("xs:", StringComparison.Ordinal) ? text[3..] : text;

                    if (!XdmType.TryGet(name, out XdmType.BuiltInType type))
                    {
                        throw new ArgumentException($"'{text}' is not a type a signature can name.");
                    }

                    return new FunctionParameter(Shape.Atomic, type, optional, repeating);
                }
            }
        }

        /// <summary>
        /// Applies the function conversion rules to one argument.
        /// </summary>
        /// <param name="value">The argument's value.</param>
        /// <param name="function">The function's name, for the message.</param>
        /// <param name="position">The one-based argument position, for the message.</param>
        /// <returns>The converted value, ready for the function to read.</returns>
        /// <exception cref="XsltException">
        /// <c>XPTY0004</c> where no conversion reaches the declared type or the number of items is wrong, and
        /// <c>FORG0001</c> where an untyped value is not in the declared type's lexical space.
        /// </exception>
        internal XPathValue Convert(XPathValue value, string function, int position)
        {
            // One atomic value is the common case by a wide margin — a literal, a variable, a number that
            // arithmetic just produced — and expanding it into a list first would put an allocation on every
            // argument of every call.
            if (value.Kind is not (XPathValueKind.Sequence or XPathValueKind.NodeSet or XPathValueKind.Node))
            {
                return ConvertItem(value, function, position);
            }

            // One node is the next commonest — format-number(price, …) once per element — and its
            // conversion needs no list either: the item is the node, and the answer is what it became, or
            // the node-set as it arrived where nothing happened to it.
            if (value.Kind == XPathValueKind.NodeSet && value.AsNodeSet().Count == 1)
            {
                NodeSet nodes = value.AsNodeSet();
                XPathValue item = XPathValue.FromNode(nodes.TreeAt(0), nodes[0]);
                XPathValue converted = ConvertItem(item, function, position);
                return Same(converted, item) ? value : converted;
            }

            // A node-set of many nodes for a parameter that takes many — avg(//product/price) — converts
            // node by node into an array of the exact size, which the sequence then holds: no list to fill
            // first and no second array to copy it into.
            if (value.Kind == XPathValueKind.NodeSet && Repeating && value.AsNodeSet().Count > 1)
            {
                NodeSet nodes = value.AsNodeSet();
                XPathValue[] converted = new XPathValue[nodes.Count];
                bool anyChanged = false;

                for (int i = 0; i < converted.Length; i++)
                {
                    XPathValue item = XPathValue.FromNode(nodes.TreeAt(i), nodes[i]);
                    converted[i] = ConvertItem(item, function, position);
                    anyChanged |= !Same(converted[i], item);

                    // A typed node's value may be several values or none, which the sequence cannot hold
                    // as one item: the conversions so far, and the rest, are gathered the general way.
                    if (converted[i].Kind == XPathValueKind.Sequence)
                    {
                        List<XPathValue> gathered = new List<XPathValue>(converted.Length);

                        for (int j = 0; j <= i; j++)
                        {
                            gathered.Add(converted[j]);
                        }

                        for (int j = i + 1; j < converted.Length; j++)
                        {
                            gathered.Add(ConvertItem(XPathValue.FromNode(nodes.TreeAt(j), nodes[j]), function, position));
                        }

                        return XdmSequence.Concatenate(gathered);
                    }
                }

                return anyChanged ? XPathValue.FromSequence(new XdmSequence(converted)) : value;
            }

            List<XPathValue> items = XdmSequence.Items(value);

            if (items.Count == 0)
            {
                return Optional
                    ? value
                    : throw Refuse(function, position, "was given nothing, and needs a value");
            }

            if (items.Count > 1 && !Repeating)
            {
                throw Refuse(function, position, $"takes one value, but was given {items.Count}");
            }

            bool changed = false;

            for (int i = 0; i < items.Count; i++)
            {
                XPathValue converted = ConvertItem(items[i], function, position);

                if (!changed && !Same(converted, items[i]))
                {
                    changed = true;
                }

                items[i] = converted;
            }

            // A value nothing happened to is passed on as it arrived, so that a node-set stays the node-set
            // the callee may be counting on rather than becoming a sequence that holds the same nodes.
            return changed ? XdmSequence.Concatenate(items) : value;
        }

        /// <summary>
        /// Converts an argument by XPath 1.0's rules, which are what a backwards-compatible call asks for.
        /// </summary>
        /// <remarks>
        /// XPath 2.0 §3.1.5, and three rules with no fourth. A sequence where one item was expected is
        /// replaced by its first item, which is what makes <c>base-uri(//item)</c> the first item's base URI
        /// rather than a type error and <c>string-length(('abc','de'))</c> three rather than five. A value
        /// where a string was expected is converted by <c>fn:string()</c>, and one where a number was
        /// expected by <c>fn:number()</c>. Neither of those can fail, which is the point of them: XPath 1.0
        /// had no type error to raise here, and a stylesheet written for it is entitled to the answers it
        /// had. The <c>fn:number()</c> is 2.0's all the same, as it is wherever this mode converts, so
        /// <c>round('2.6e0')</c> is 3 — see <see cref="XdmType.FirstItemAsDoubleOrNaN"/>.
        /// </remarks>
        /// <param name="value">The argument's value.</param>
        internal XPathValue ConvertBackwards(XPathValue value)
        {
            // A position that takes a sequence takes the sequence: the rules below are written for an
            // expected type of one item or none, and string-join(nodes, '-') joins the nodes rather than
            // the first of them.
            if (Repeating)
            {
                return value;
            }

            if (value.Kind is XPathValueKind.Sequence or XPathValueKind.NodeSet)
            {
                List<XPathValue> items = XdmSequence.Items(value);

                if (items.Count > 1)
                {
                    value = items[0];
                }
            }

            // Nothing at all is left alone: a function declaring the position optional takes it, and one
            // that does not says so itself in the reading it would have given XPath 1.0's empty node-set.
            if (Xpath2FunctionExpr.IsEmptySequence(value))
            {
                return value;
            }

            if (m_shape == Shape.Numeric
                || (m_shape == Shape.Atomic && XdmComparison.IsNumeric(m_type.Code)))
            {
                return XPathValue.FromNumber(XdmType.FirstItemAsDoubleOrNaN(value));
            }

            return m_shape == Shape.Atomic && m_type.Code == XdmTypeCode.String
                ? XPathValue.FromString(XdmSequence.StringValueOf(value))
                : value;
        }

        /// <summary>Whether a conversion left an item alone, so that the original value can be kept.</summary>
        private static bool Same(XPathValue converted, XPathValue original)
        {
            return converted.Kind == original.Kind && converted.TypeCode == original.TypeCode;
        }

        private XPathValue ConvertItem(XPathValue item, string function, int position)
        {
            bool isNode = item.Kind is XPathValueKind.Node or XPathValueKind.NodeSet;

            switch (m_shape)
            {
                case Shape.Item:
                    return item;

                case Shape.Node:
                case Shape.Element:
                    return isNode
                        ? item
                        : throw Refuse(function, position, "needs a node, and was given a value");
            }

            // Atomization: a node contributes its typed value, which is its text untyped unless the node
            // was validated, and may then be several values, each converted on its own, or none.
            XPathValue atomic = isNode ? XdmSequence.TypedValueOf(item) : item;

            if (atomic.Kind == XPathValueKind.Sequence)
            {
                XdmSequence several = atomic.AsSequence();

                if (several.Count > 1 && !Repeating)
                {
                    throw Refuse(function, position, $"takes one value, and the node's typed value is {several.Count} values");
                }

                List<XPathValue> converted = new List<XPathValue>(several.Count);

                for (int i = 0; i < several.Count; i++)
                {
                    converted.Add(ConvertItem(several[i], function, position));
                }

                return XdmSequence.Concatenate(converted);
            }

            // An untyped value is read as whatever was declared — the rule that keeps document content usable
            // without a cast at every reference. It may fail, as any cast may, and that failure is the answer.
            if (atomic.TypeCode == XdmTypeCode.UntypedAtomic && !AcceptsUntyped)
            {
                // The one declared type an untyped value is not read as: a name is a namespace and a local
                // part, and text carries neither — the prefix in it would have to mean something, and
                // where the text came from is not where the function is. The specification gives that its
                // own code rather than letting the cast fail (F&O: XPTY0117).
                if (m_type.Code == XdmTypeCode.QName)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0117,
                        $"Argument {position} of {function}() is declared xs:{m_type.Name}, and untyped "
                        + "content is not read as one: a name written as text has no namespace to be in.");
                }

                return m_shape == Shape.Numeric
                    ? XdmType.UntypedAsDouble(atomic)
                    : XdmType.Cast(atomic, m_type);
            }

            if (m_shape == Shape.Numeric)
            {
                return XdmComparison.IsNumeric(atomic.TypeCode)
                    ? atomic
                    : throw Refuse(function, position, $"needs a number, and was given {Spell(atomic)}");
            }

            return Promote(atomic) ?? throw Refuse(
                function, position, $"needs an xs:{m_type.Name}, and was given {Spell(atomic)}");
        }

        /// <summary>
        /// Whether the declared type is one an untyped value already satisfies, and so is not cast to.
        /// </summary>
        private bool AcceptsUntyped =>
            m_shape == Shape.Atomic
            && (m_type.Code == XdmTypeCode.UntypedAtomic
                || string.Equals(m_type.Name, "anyAtomicType", StringComparison.Ordinal));

        /// <summary>
        /// Returns the item as the declared type, promoting where the rules allow, or <see langword="null"/>
        /// where it is not that type at all.
        /// </summary>
        /// <remarks>
        /// Two things happen here that look alike and are not. <em>Substitution</em> is a value already being
        /// of the declared type because its own type is derived from it — an <c>xs:integer</c> is an
        /// <c>xs:decimal</c>, and a <c>xs:dayTimeDuration</c> is an <c>xs:duration</c> — and changes nothing.
        /// <em>Promotion</em> converts: an integer passed where a double is declared becomes one. The numeric
        /// widths promote, and only ever wider, and so does <c>xs:anyURI</c> to <c>xs:string</c> — which is
        /// what lets <c>substring-after(base-uri(.), '/')</c> mean anything, a URI not being a string.
        /// </remarks>
        private XPathValue? Promote(XPathValue item)
        {
            if (string.Equals(m_type.Name, "anyAtomicType", StringComparison.Ordinal))
            {
                return item;
            }

            XdmTypeCode from = item.TypeCode;

            if (from == m_type.Code)
            {
                return item;
            }

            switch (m_type.Code)
            {
                case XdmTypeCode.String:
                    return from == XdmTypeCode.AnyUri ? XPathValue.FromString(item.ToStringValue()) : null;

                case XdmTypeCode.Double:
                    return XdmComparison.IsNumeric(from) ? XPathValue.FromNumber(item.ToNumber()) : null;

                case XdmTypeCode.Float:
                    return from is XdmTypeCode.Integer or XdmTypeCode.Decimal
                        ? XPathValue.FromFloat((float)item.ToNumber())
                        : null;

                case XdmTypeCode.Decimal:
                    // An integer is a decimal already; nothing converts, and the value keeps its own type.
                    return from == XdmTypeCode.Integer ? item : null;

                case XdmTypeCode.Duration:
                    return from is XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration ? item : null;

                case XdmTypeCode.Gregorian:
                    return from == XdmTypeCode.Gregorian
                        && string.Equals(item.AsGregorian().Name, m_type.Name, StringComparison.Ordinal)
                            ? item
                            : null;

                default:
                    return null;
            }
        }

        private static string Spell(XPathValue value)
        {
            return value.TypeCode switch
            {
                XdmTypeCode.UntypedAtomic => "an untyped value",
                XdmTypeCode.Gregorian => "an xs:" + value.AsGregorian().Name,
                XdmTypeCode.String => "an xs:string",
                XdmTypeCode.Boolean => "an xs:boolean",
                XdmTypeCode.Integer => "an xs:integer",
                XdmTypeCode.Decimal => "an xs:decimal",
                XdmTypeCode.Float => "an xs:float",
                XdmTypeCode.Double => "an xs:double",
                XdmTypeCode.QName => "an xs:QName",
                XdmTypeCode.AnyUri => "an xs:anyURI",
                _ => "an xs:" + char.ToLowerInvariant(value.TypeCode.ToString()[0])
                    + value.TypeCode.ToString()[1..],
            };
        }

        private static XsltException Refuse(string function, int position, string what)
        {
            return XsltErrors.Error(
                XsltErrorCode.XPTY0004, $"Argument {position} of fn:{function}() {what}.");
        }
    }

    /// <summary>
    /// One argument of a built-in function call, with the function conversion rules applied to it.
    /// </summary>
    /// <remarks>
    /// Wrapping the argument expression rather than checking inside each function puts the rules in one place
    /// and applies them however the function goes on to read its argument — as a value, as a sequence, or as
    /// a string. It also keeps them out of XPath 1.0 entirely: a 1.0 call is never wrapped, because 1.0
    /// converts whatever it is given and has no type errors to report.
    /// </remarks>
    internal sealed class CheckedArgumentExpr : Expr
    {
        private readonly Expr m_argument;
        private readonly FunctionParameter m_parameter;
        private readonly string m_function;
        private readonly int m_position;

        private readonly bool m_backwards;

        private CheckedArgumentExpr(
            Expr argument, FunctionParameter parameter, string function, int position, bool backwards)
        {
            m_argument = argument;
            m_parameter = parameter;
            m_function = function;
            m_position = position;
            m_backwards = backwards;
        }

        /// <summary>
        /// Wraps a call's arguments in the conversions its signature calls for, by the rules of the version
        /// the call was written under.
        /// </summary>
        /// <remarks>
        /// The two versions convert differently rather than one of them not converting at all. XPath 2.0
        /// checks the type and refuses what does not reach it; XPath 1.0 converts until it fits, and cannot
        /// fail — see <see cref="FunctionParameter.ConvertBackwards"/>.
        /// </remarks>
        /// <param name="arguments">The argument expressions, which are not modified.</param>
        /// <param name="parameters">The function's declared parameters.</param>
        /// <param name="function">The function's name, for the message.</param>
        /// <param name="version">The version the call was written under.</param>
        internal static Expr[] Wrap(
            Expr[] arguments,
            FunctionParameter[] parameters,
            string function,
            XsltVersion version)
        {
            if (parameters.Length == 0)
            {
                return arguments;
            }

            bool backwards = version.IsBackwardsCompatible;

            Expr[] wrapped = arguments;

            for (int i = 0; i < arguments.Length; i++)
            {
                // A variadic function repeats its last declared parameter, which is how concat() is written.
                FunctionParameter parameter = parameters[Math.Min(i, parameters.Length - 1)];

                // item()* accepts everything and converts nothing, so wrapping it would only cost a call.
                if (ReferenceEquals(parameter, FunctionParameter.Anything))
                {
                    continue;
                }

                if (ReferenceEquals(wrapped, arguments))
                {
                    wrapped = (Expr[])arguments.Clone();
                }

                wrapped[i] = new CheckedArgumentExpr(arguments[i], parameter, function, i + 1, backwards);
            }

            return wrapped;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_argument };

        /// <inheritdoc/>
        public override bool MaySpanDocuments => m_argument.MaySpanDocuments;

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => !m_parameter.Atomizes && m_argument.ReturnsNodeSet;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue value = m_argument.Evaluate(ref context);

            return m_backwards
                ? m_parameter.ConvertBackwards(value)
                : m_parameter.Convert(value, m_function, m_position);
        }
    }
}
