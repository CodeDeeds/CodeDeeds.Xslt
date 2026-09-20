using System.Text;
using CodeDeeds.Xslt.Emit;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The XPath 1.0 core functions supported by this engine.</summary>
    public enum XPathFunction : byte
    {
        /// <summary><c>last()</c></summary>
        Last,

        /// <summary><c>position()</c></summary>
        Position,

        /// <summary><c>count(node-set)</c></summary>
        Count,

        /// <summary><c>local-name(node-set?)</c></summary>
        LocalName,

        /// <summary><c>namespace-uri(node-set?)</c></summary>
        NamespaceUri,

        /// <summary><c>name(node-set?)</c></summary>
        Name,

        /// <summary><c>string(object?)</c></summary>
        String,

        /// <summary><c>concat(string, string, string*)</c></summary>
        Concat,

        /// <summary><c>starts-with(string, string)</c></summary>
        StartsWith,

        /// <summary><c>contains(string, string)</c></summary>
        Contains,

        /// <summary><c>substring-before(string, string)</c></summary>
        SubstringBefore,

        /// <summary><c>substring-after(string, string)</c></summary>
        SubstringAfter,

        /// <summary><c>substring(string, number, number?)</c></summary>
        Substring,

        /// <summary><c>string-length(string?)</c></summary>
        StringLength,

        /// <summary><c>normalize-space(string?)</c></summary>
        NormalizeSpace,

        /// <summary><c>translate(string, string, string)</c></summary>
        Translate,

        /// <summary><c>boolean(object)</c></summary>
        Boolean,

        /// <summary><c>not(object)</c></summary>
        Not,

        /// <summary><c>true()</c></summary>
        True,

        /// <summary><c>false()</c></summary>
        False,

        /// <summary><c>number(object?)</c></summary>
        Number,

        /// <summary><c>sum(node-set)</c></summary>
        Sum,

        /// <summary><c>floor(number)</c></summary>
        Floor,

        /// <summary><c>ceiling(number)</c></summary>
        Ceiling,

        /// <summary><c>round(number)</c></summary>
        Round,

        /// <summary><c>lang(string)</c></summary>
        Lang,
    }

    /// <summary>A call to one of the XPath core functions.</summary>
    public sealed class FunctionCallExpr : Expr
    {
        /// <summary>One function's arity and the types it declares for its parameters.</summary>
        /// <param name="Function">Which function this is.</param>
        /// <param name="MinArgs">The fewest arguments it takes.</param>
        /// <param name="MaxArgs">The most it takes under XPath 1.0.</param>
        /// <param name="Parameters">The types XPath 2.0 declares for its parameters.</param>
        /// <param name="Collation">
        /// Whether XPath 2.0 gives it one more argument than 1.0 did, naming a collation. Four of the string
        /// functions gained one, and a 1.0 stylesheet passing three arguments to <c>contains()</c> is still
        /// making a mistake.
        /// </param>
        /// <param name="ExtraIn20">
        /// How many further arguments XPath 2.0 allows that 1.0 did not. A collation is one of them;
        /// <c>sum()</c> gained the value to return for an empty sequence, and <c>lang()</c> the node to ask
        /// about. Passing one to a 1.0 stylesheet is still a mistake, which is why the arity is not simply
        /// widened for everyone.
        /// </param>
        private sealed record Entry(
            XPathFunction Function,
            int MinArgs,
            int MaxArgs,
            FunctionParameter[] Parameters,
            bool Collation = false,
            int ExtraIn20 = 0);

        /// <summary>
        /// The core functions, with the signatures XPath 2.0 declares for them.
        /// </summary>
        /// <remarks>
        /// XPath 1.0 declares none of this — it converts whatever it is given, so <c>substring(1, 2)</c> is a
        /// perfectly good call. The signatures therefore bind only under 2.0, where they are what makes
        /// <c>translate(1, '-', 'x')</c> a type error rather than an answer; see
        /// <see cref="FunctionParameter"/>.
        /// </remarks>
        private static readonly Dictionary<string, Entry> s_registry = BuildRegistry();

        private static Dictionary<string, Entry> BuildRegistry()
        {
            Dictionary<string, Entry> registry = new(StringComparer.Ordinal);

            void Add(
                string name,
                XPathFunction function,
                int minimum,
                int maximum,
                string signature,
                bool collation = false,
                int extraIn20 = 0)
            {
                registry.Add(
                    name,
                    new Entry(
                        function,
                        minimum,
                        maximum,
                        FunctionParameter.Parse(collation ? signature + ", xs:string" : signature),
                        collation,
                        collation ? 1 : extraIn20));
            }

            Add("last", XPathFunction.Last, 0, 0, "");
            Add("position", XPathFunction.Position, 0, 0, "");
            Add("count", XPathFunction.Count, 1, 1, "item()*");
            Add("local-name", XPathFunction.LocalName, 0, 1, "node()?");
            Add("namespace-uri", XPathFunction.NamespaceUri, 0, 1, "node()?");
            Add("name", XPathFunction.Name, 0, 1, "node()?");
            Add("string", XPathFunction.String, 0, 1, "item()?");
            Add("concat", XPathFunction.Concat, 2, int.MaxValue, "xs:anyAtomicType?");
            Add("starts-with", XPathFunction.StartsWith, 2, 2, "xs:string?, xs:string?", collation: true);
            Add("contains", XPathFunction.Contains, 2, 2, "xs:string?, xs:string?", collation: true);
            Add("substring-before", XPathFunction.SubstringBefore, 2, 2, "xs:string?, xs:string?",
                collation: true);
            Add("substring-after", XPathFunction.SubstringAfter, 2, 2, "xs:string?, xs:string?",
                collation: true);
            Add("substring", XPathFunction.Substring, 2, 3, "xs:string?, xs:double, xs:double?");
            Add("string-length", XPathFunction.StringLength, 0, 1, "xs:string?");
            Add("normalize-space", XPathFunction.NormalizeSpace, 0, 1, "xs:string?");
            Add("translate", XPathFunction.Translate, 3, 3, "xs:string?, xs:string, xs:string");
            Add("boolean", XPathFunction.Boolean, 1, 1, "item()*");
            Add("not", XPathFunction.Not, 1, 1, "item()*");
            Add("true", XPathFunction.True, 0, 0, "");
            Add("false", XPathFunction.False, 0, 0, "");
            Add("number", XPathFunction.Number, 0, 1, "xs:anyAtomicType?");
            Add("sum", XPathFunction.Sum, 1, 1, "xs:anyAtomicType*, xs:anyAtomicType?", extraIn20: 1);
            Add("floor", XPathFunction.Floor, 1, 1, "numeric?");
            Add("ceiling", XPathFunction.Ceiling, 1, 1, "numeric?");
            Add("round", XPathFunction.Round, 1, 1, "numeric?");
            Add("lang", XPathFunction.Lang, 1, 1, "xs:string?, node()", extraIn20: 1);

            return registry;
        }

        private readonly XPathFunction m_function;
        private readonly Expr[] m_arguments;
        private readonly XsltVersion m_version;

        private FunctionCallExpr(XPathFunction function, Expr[] arguments, XsltVersion version)
        {
            m_function = function;
            m_arguments = arguments;
            m_version = version;
        }

        /// <summary>
        /// The collation a <c>default-collation</c> in scope puts there, or null for the code point one.
        /// </summary>
        /// <remarks>
        /// Set where the call is compiled: what the string functions that take an optional collation use
        /// where the caller supplies none. The four that do — <c>starts-with</c>, <c>contains</c> and the
        /// two substring functions — are XPath 1.0's, and gained the argument in 2.0 along with the default.
        /// </remarks>
        internal Collation? DefaultCollation { get; set; }

        /// <summary>
        /// Gets or sets the base URI the call was written at, which a collation named by a relative
        /// URI is resolved against. Null where the caller named none.
        /// </summary>
        internal string? StaticBaseUri { get; set; }

        /// <summary>
        /// Creates a call to a named function, validating that the name is known and the arity is legal.
        /// </summary>
        /// <param name="name">The function name as written.</param>
        /// <param name="arguments">The argument expressions.</param>
        /// <returns>The compiled call.</returns>
        /// <exception cref="XsltException">The function is unknown, or the argument count is wrong.</exception>
        public static FunctionCallExpr Create(string name, Expr[] arguments)
        {
            return Create(name, arguments, XsltVersion.V10);
        }

        /// <summary>
        /// Creates a call to a named function, compiled against a particular version of XPath.
        /// </summary>
        /// <param name="name">The function name as written.</param>
        /// <param name="arguments">The argument expressions.</param>
        /// <param name="version">
        /// The version in force where the call was written. Under 2.0 the arguments are checked against the
        /// function's declared parameter types; under 1.0 there are no declarations to check against.
        /// </param>
        /// <returns>The compiled call.</returns>
        /// <exception cref="XsltException">The function is unknown, or the argument count is wrong.</exception>
        public static FunctionCallExpr Create(string name, Expr[] arguments, XsltVersion version)
        {
            if (!s_registry.TryGetValue(name, out Entry? entry))
            {
                throw XsltErrors.Error(XsltErrorCode.XPST0017, $"Unknown function '{name}()'.");
            }

            // Several functions gained an argument in 2.0 — a collation for four of the string ones, the
            // empty-sequence value for sum(), the node for lang().
            int maximum = version.IsBackwardsCompatible
                ? entry.MaxArgs
                : entry.MaxArgs + entry.ExtraIn20;

            if (arguments.Length < entry.MinArgs || arguments.Length > maximum)
            {
                string expected = maximum == int.MaxValue
                    ? $"at least {entry.MinArgs}"
                    : entry.MinArgs == maximum
                        ? entry.MinArgs.ToString()
                        : $"{entry.MinArgs} to {maximum}";

                throw XsltErrors.Error(XsltErrorCode.XPST0017,
                    $"Function '{name}()' expects {expected} argument(s), but {arguments.Length} were supplied.");
            }

            return new FunctionCallExpr(
                entry.Function,
                CheckedArgumentExpr.Wrap(arguments, entry.Parameters, name, version),
                version);
        }

        /// <summary>Returns whether a name denotes a supported core function.</summary>
        /// <param name="name">The function name to test.</param>
        public static bool IsKnown(string name)
        {
            return s_registry.ContainsKey(name);
        }

        /// <summary>
        /// Returns whether a core function of this name will take a given number of arguments.
        /// </summary>
        /// <remarks>
        /// Read by <c>function-available()</c> in its two-argument form. The range comes from the same
        /// entry <see cref="Create"/> checks against, so what the function reports available is what a call
        /// would actually be allowed to write — the alternative was a second table of arities to keep in
        /// step, and a table that drifts answers worse than no table at all.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        /// <param name="arity">How many arguments the caller is asking about.</param>
        /// <param name="version">The version the question is asked under.</param>
        public static bool TakesArity(string name, int arity, XsltVersion version)
        {
            if (!s_registry.TryGetValue(name, out Entry? entry))
            {
                return false;
            }

            int maximum = version.IsBackwardsCompatible
                ? entry.MaxArgs
                : entry.MaxArgs + entry.ExtraIn20;

            return arity >= entry.MinArgs && arity <= maximum;
        }

        /// <summary>
        /// Finds the collation named by a third argument, or the default one where there is none.
        /// </summary>
        /// <summary>A collation URI made absolute against the base URI the call was written at.</summary>
        /// <remarks>
        /// A collation is named by a URI and a URI in an expression is relative to where the expression
        /// stands, so <c>collation/codepoint</c> written at
        /// <c>http://www.w3.org/2005/xpath-functions/</c> is the code point collation and the same
        /// three words written anywhere else name nothing.
        /// </remarks>
        /// <param name="uri">The collation URI as the call wrote it.</param>
        private string Absolute(string uri)
        {
            return StaticBaseUri is string written
                && Uri.TryCreate(written, UriKind.Absolute, out Uri? baseUri)
                && Uri.TryCreate(baseUri, uri, out Uri? resolved)
                    ? resolved.ToString()
                    : uri;
        }

        private Collation Collation(ref DynamicContext context)
        {
            return m_arguments.Length > 2
                ? XPath.Collation.Resolve(
                    Absolute(m_arguments[2].Evaluate(ref context).ToStringValue()), ref context)
                : DefaultCollation ?? XPath.Collation.Codepoint;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <summary>Whether this is <c>position()</c> or <c>last()</c>, which read where the focus stands.</summary>
        internal override bool ReadsFocusPosition => m_function is XPathFunction.Position or XPathFunction.Last;

        /// <summary>Whether the function yields a boolean, whatever it is given.</summary>
        internal override bool IsBooleanValued => m_function is XPathFunction.Not
            or XPathFunction.True
            or XPathFunction.False
            or XPathFunction.Boolean
            or XPathFunction.Contains
            or XPathFunction.StartsWith;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            switch (m_function)
            {
                // Four of these count things, and 2.0 declares them xs:integer where 1.0 said only "number".
                case XPathFunction.Last:
                    return Counted(EmitHelpers.SizeOfFocus(ref context));

                case XPathFunction.Position:
                    return Counted(EmitHelpers.PositionOfFocus(ref context));

                case XPathFunction.True:
                    return XPathValue.FromBoolean(true);

                case XPathFunction.False:
                    return XPathValue.FromBoolean(false);

                case XPathFunction.Count:
                    return Counted(CountNodes(ref context));

                case XPathFunction.Boolean:
                    return XPathValue.FromBoolean(m_arguments[0].EvaluateAsBoolean(ref context));

                case XPathFunction.Not:
                    return XPathValue.FromBoolean(!m_arguments[0].EvaluateAsBoolean(ref context));

                case XPathFunction.String:
                    return XPathValue.FromString(Text(ArgumentOrContext(0, ref context)));

                case XPathFunction.Number:
                {
                    // fn:number is a cast to xs:double from XPath 2.0 on, and the lexical space of
                    // xs:double takes an exponent, a leading plus, INF, -INF and NaN. A backwards
                    // compatible call reads the same lexical space, the function being 2.0's there too —
                    // XPath 2.0 Appendix I.1 names number() among what the mode does not give back — and
                    // differs only in taking the first item of a sequence. What the cast will not read
                    // is NaN either way, which is the one thing the function keeps from 1.0.
                    XPathValue argument = ArgumentOrContext(0, ref context);

                    return XPathValue.FromNumber(
                        m_version.IsBackwardsCompatible
                            ? XdmType.FirstItemAsDoubleOrNaN(argument)
                            : XdmType.AsDoubleOrNaN(argument));
                }

                case XPathFunction.StringLength:
                    return Counted(CodePoints.Count(Text(ArgumentOrContext(0, ref context))));

                case XPathFunction.NormalizeSpace:
                    return XPathValue.FromString(NormalizeSpace(Text(ArgumentOrContext(0, ref context))));

                case XPathFunction.Floor:
                case XPathFunction.Ceiling:
                case XPathFunction.Round:
                    return Rounding(m_arguments[0].Evaluate(ref context));

                case XPathFunction.Sum:
                    return m_version.IsBackwardsCompatible
                        ? XPathValue.FromNumber(Sum(m_arguments[0].Evaluate(ref context)))
                        : TypedSum(m_arguments[0].Evaluate(ref context), ref context);

                case XPathFunction.Lang:
                    return XPathValue.FromBoolean(MatchesLanguage(
                        m_arguments[0].Evaluate(ref context).ToStringValue(), ref context));

                case XPathFunction.Concat:
                    return XPathValue.FromString(Concat(ref context));

                case XPathFunction.StartsWith:
                {
                    string subject = m_arguments[0].Evaluate(ref context).ToStringValue();
                    string prefix = m_arguments[1].Evaluate(ref context).ToStringValue();
                    return XPathValue.FromBoolean(Collation(ref context).StartsWith(subject, prefix));
                }

                case XPathFunction.Contains:
                {
                    string subject = m_arguments[0].Evaluate(ref context).ToStringValue();
                    string sought = m_arguments[1].Evaluate(ref context).ToStringValue();
                    return XPathValue.FromBoolean(Collation(ref context).Contains(subject, sought));
                }

                case XPathFunction.SubstringBefore:
                {
                    string subject = m_arguments[0].Evaluate(ref context).ToStringValue();
                    string sought = m_arguments[1].Evaluate(ref context).ToStringValue();
                    int index = Collation(ref context).IndexOf(subject, sought, out _);
                    return XPathValue.FromString(index < 0 ? string.Empty : subject[..index]);
                }

                case XPathFunction.SubstringAfter:
                {
                    string subject = m_arguments[0].Evaluate(ref context).ToStringValue();
                    string sought = m_arguments[1].Evaluate(ref context).ToStringValue();
                    int index = Collation(ref context).IndexOf(subject, sought, out int matched);

                    // Resumed from the end of what matched rather than from the length of what was sought:
                    // a collation that ignores diacritics can match four characters against five.
                    return XPathValue.FromString(index < 0 ? string.Empty : subject[(index + matched)..]);
                }

                case XPathFunction.Substring:
                    return XPathValue.FromString(Substring(ref context));

                case XPathFunction.Translate:
                {
                    string subject = m_arguments[0].Evaluate(ref context).ToStringValue();
                    string from = m_arguments[1].Evaluate(ref context).ToStringValue();
                    string to = m_arguments[2].Evaluate(ref context).ToStringValue();
                    return XPathValue.FromString(Translate(subject, from, to));
                }

                default:
                    return EvaluateNameFunction(ref context);
            }
        }

        /// <inheritdoc/>
        internal override void Emit(EmitContext context)
        {
            switch (m_function)
            {
                case XPathFunction.Position:
                case XPathFunction.Last:
                    EmitCounted(context);
                    return;

                case XPathFunction.True:
                case XPathFunction.False:
                    context.IL.LoadInt(m_function == XPathFunction.True ? 1 : 0);
                    context.IL.Call(EmitHelpers.FromBoolean);
                    return;

                case XPathFunction.Not:
                    EmitAsBoolean(context);
                    context.IL.Call(EmitHelpers.FromBoolean);
                    return;

                default:
                    context.EmitInterpreterFallback(this);
                    return;
            }
        }

        /// <summary>
        /// Emits <c>position()</c> or <c>last()</c> as a value, typed the way <see cref="Counted"/> types it.
        /// </summary>
        /// <remarks>
        /// A double under 1.0, where every number is one, and an <c>xs:integer</c> under 2.0, where the
        /// distinction is real: a variable declared <c>as="xs:integer"</c> must accept what this returns, and
        /// a range must find an integer on the side that reads it back. Emitting a double at every version
        /// was the 1.0 shape of the language outliving the version that made it true — correct for as long
        /// as the emitted backend only ever saw 1.0 stylesheets, and wrong once it saw a 2.0 one.
        /// </remarks>
        /// <remarks>
        /// What reaches this is only what was compiled as an expression in its own right — an
        /// <c>xsl:variable</c>'s <c>select</c>, a <c>test</c>. Written inline as <c>1 to position()</c> the
        /// range is the top-level expression, ranges are not emitted, and the whole of it falls back, so the
        /// interpreter answers and the type was never wrong there.
        /// </remarks>
        /// <param name="context">The compilation context.</param>
        private void EmitCounted(EmitContext context)
        {
            if (m_version.IsBackwardsCompatible)
            {
                EmitAsNumber(context);
                context.IL.Call(EmitHelpers.FromNumber);
                return;
            }

            context.LoadContext();
            context.IL.Call(m_function == XPathFunction.Position ? EmitHelpers.PositionOf : EmitHelpers.SizeOf);
            context.IL.UnaryOperation(System.Reflection.Emit.OpCodes.Conv_I8, "conv.i8");
            context.IL.Call(EmitHelpers.FromInteger);
        }

        /// <inheritdoc/>
        internal override void EmitAsNumber(EmitContext context)
        {
            // position() and last() are integers sitting in the context, read through the one helper the
            // interpreter reads them through, so that an absent focus is refused the same way in both.
            switch (m_function)
            {
                case XPathFunction.Position:
                    context.LoadContext();
                    context.IL.Call(EmitHelpers.PositionOf);
                    context.IL.UnaryOperation(System.Reflection.Emit.OpCodes.Conv_R8, "conv.r8");
                    return;

                case XPathFunction.Last:
                    context.LoadContext();
                    context.IL.Call(EmitHelpers.SizeOf);
                    context.IL.UnaryOperation(System.Reflection.Emit.OpCodes.Conv_R8, "conv.r8");
                    return;

                default:
                    base.EmitAsNumber(context);
                    return;
            }
        }

        /// <inheritdoc/>
        internal override void EmitAsBoolean(EmitContext context)
        {
            switch (m_function)
            {
                case XPathFunction.True:
                    context.IL.LoadInt(1);
                    return;

                case XPathFunction.False:
                    context.IL.LoadInt(0);
                    return;

                case XPathFunction.Not:
                {
                    m_arguments[0].EmitAsBoolean(context);
                    context.IL.LoadInt(0);
                    context.IL.BinaryOperation(System.Reflection.Emit.OpCodes.Ceq, "not");
                    return;
                }

                case XPathFunction.Boolean:
                    m_arguments[0].EmitAsBoolean(context);
                    return;

                default:
                    base.EmitAsBoolean(context);
                    return;
            }
        }

        /// <summary>
        /// Types a count as the version in force asks for.
        /// </summary>
        /// <remarks>
        /// <c>count</c>, <c>last</c>, <c>position</c> and <c>string-length</c> all return a whole number, and
        /// 2.0 declares them <c>xs:integer</c> where 1.0 said only "number" and meant a double. The
        /// difference is visible to <c>instance of</c>, and to arithmetic: <c>count(*) div 2</c> is an
        /// <c>xs:decimal</c> from an integer and a double from a double. So a backwards-compatible
        /// expression keeps the double it was written against, rather than having a type appear underneath
        /// it that the language it was written in does not have.
        /// </remarks>
        /// <param name="count">The number counted.</param>
        private XPathValue Counted(double count)
        {
            return m_version.IsBackwardsCompatible
                ? XPathValue.FromNumber(count)
                : XPathValue.FromInteger((long)count);
        }

        /// <summary>
        /// Counts the nodes an argument selects. The count never needs the node-set object itself, so the
        /// nodes go into a pooled list instead.
        /// </summary>
        private int CountNodes(ref DynamicContext context)
        {
            Expr argument = m_arguments[0];
            if (!argument.ReturnsNodeSet)
            {
                XPathValue value = argument.Evaluate(ref context);

                // Under XPath 2.0 the argument is a sequence, which may hold atomic values as well as nodes
                // and is what every function returning one hands back.
                // Counted rather than laid out and then measured: a list of ten million items to learn
                // that there are ten million of them is the allocation nobody asked for, and for a
                // range it is the one the range exists to avoid.
                return value.Kind == XPathValueKind.NodeSet
                    ? value.AsNodeSet().Count
                    : XdmSequence.ItemCount(value);
            }

            List<int> nodes = NodeListPool.Rent();
            try
            {
                argument.EvaluateNodes(ref context, nodes);
                return nodes.Count;
            }
            finally
            {
                NodeListPool.Return(nodes);
            }
        }

        private XPathValue EvaluateNameFunction(ref DynamicContext context)
        {
            int node = m_arguments.Length != 0 ? context.Node : context.RequireContextNode($"fn:{m_function}()");
            XdmTree tree = context.Tree;

            if (m_arguments.Length != 0)
            {
                // Under XPath 2.0 the argument is a sequence, whose first item may be a node of another
                // document; under 1.0 it is a node-set. Both arrive here, so the tree is taken from whatever
                // actually turned up rather than assumed to be the context's.
                XPathValue value = m_arguments[0].Evaluate(ref context);

                if (value.Kind == XPathValueKind.NodeSet)
                {
                    NodeSet nodes = value.AsNodeSet();
                    node = nodes.FirstNode();
                    if (node < 0)
                    {
                        return XPathValue.FromString(string.Empty);
                    }

                    tree = nodes.TreeAt(0);
                }
                else
                {
                    List<XPathValue> items = XdmSequence.Items(value);
                    if (items.Count == 0 || items[0].Kind != XPathValueKind.Node)
                    {
                        return XPathValue.FromString(string.Empty);
                    }

                    node = items[0].NodeId;
                    tree = items[0].NodeTree;
                }
            }

            int nameCode = tree.NameCodeOf(node);
            if (nameCode == NameTable.NoNameCode)
            {
                return XPathValue.FromString(string.Empty);
            }

            return m_function switch
            {
                XPathFunction.LocalName =>
                    XPathValue.FromString(tree.NameTable.GetLocalName(tree.FingerprintOf(node))),
                XPathFunction.NamespaceUri =>
                    XPathValue.FromString(tree.NameTable.GetNamespaceUri(tree.FingerprintOf(node))),
                _ => XPathValue.FromString(tree.NameTable.GetQualifiedName(nameCode)),
            };
        }

        /// <summary>
        /// The string-value of a value, in the lexical form this processor writes.
        /// </summary>
        /// <remarks>
        /// These functions are declared <c>item()?</c> rather than <c>xs:string</c>, so the conversion rules
        /// have not already cast the argument and settled the question. It is the 2.0 form even where
        /// backwards compatibility is in force: what a backwards-compatible expression produces is defined
        /// by the 2.0 specifications and not by reference to the 1.0 ones (XSLT 2.0 §3.8), and how a number
        /// is written is not among the things that mode restores.
        /// </remarks>
        private static string Text(XPathValue value)
        {
            return value.ToCanonicalString();
        }

        private XPathValue ArgumentOrContext(int index, ref DynamicContext context)
        {
            if (m_arguments.Length > index)
            {
                return m_arguments[index].Evaluate(ref context);
            }

            // No argument means the context item, which XPath 2.0 lets be an atomic value — and which may not
            // be there at all, where reading it is an error rather than an empty string.
            return XPathValue.FromString(Text(context.RequireContextItem($"fn:{m_function}()")));
        }

        private string Concat(ref DynamicContext context)
        {
            // Declared xs:anyAtomicType? rather than xs:string, so the conversion rules leave a number a
            // number and this is where its lexical form is decided.
            CharStringBuilder builder = new CharStringBuilder(stackalloc char[StackLimit]);
            foreach (Expr argument in m_arguments)
            {
                builder.Append(Text(argument.Evaluate(ref context)));
            }

            return builder.ToString();
        }

        private string Substring(ref DynamicContext context)
        {
            string subject = m_arguments[0].Evaluate(ref context).ToStringValue();
            double start = Round(m_arguments[1].Evaluate(ref context).ToNumber());

            double end = m_arguments.Length > 2
                ? start + Round(m_arguments[2].Evaluate(ref context).ToNumber())
                : double.PositiveInfinity;

            // NaN anywhere — including the infinity-minus-infinity case — selects nothing.
            if (double.IsNaN(start) || double.IsNaN(end))
            {
                return string.Empty;
            }

            // Positions are one-based and the range is half-open, so clamp against 1 and length + 1. The
            // length is in code points, a character above the basic plane being one position and not two —
            // otherwise substring() could land between the halves of a pair and take a lone surrogate.
            double low = Math.Max(start, 1.0);
            double high = Math.Min(end, CodePoints.Count(subject) + 1.0);
            if (high <= low)
            {
                return string.Empty;
            }

            return CodePoints.Slice(subject, (int)low - 1, (int)(high - low));
        }

        /// <summary>
        /// Implements <c>lang()</c>: whether the context node's inherited <c>xml:lang</c> matches a language.
        /// </summary>
        /// <remarks>
        /// The comparison is case-insensitive, and a request for <c>en</c> is satisfied by <c>en-GB</c> — a
        /// sub-language is a kind of the language it refines. The reverse does not hold.
        /// </remarks>
        private bool MatchesLanguage(string wanted, ref DynamicContext context)
        {
            XdmTree tree = context.Tree;
            int start;

            if (m_arguments.Length > 1)
            {
                // XPath 2.0 lets the node be named, where 1.0 could only ask about the context node.
                List<XPathValue> items = XdmSequence.Items(m_arguments[1].Evaluate(ref context));

                if (items.Count == 0 || items[0].Kind != XPathValueKind.Node)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004, "The second argument of fn:lang() is the node to ask about.");
                }

                start = items[0].NodeId;
                tree = items[0].NodeTree;
            }
            else
            {
                // The language is read off the node's ancestors, so there has to be one to read.
                start = context.RequireContextNode("fn:lang()");
            }

            int langFingerprint = tree.NameTable.LookupFingerprint(XdmTree.XmlNamespaceUri, "lang");

            if (langFingerprint == NameTable.NoFingerprint)
            {
                return false;
            }

            for (int node = start; node >= 0; node = tree.ParentOf(node))
            {
                int attribute = tree.FindAttribute(node, langFingerprint);
                if (attribute < 0)
                {
                    continue;
                }

                string actual = tree.StringValueOf(attribute);

                if (string.Equals(actual, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return actual.Length > wanted.Length
                    && actual[wanted.Length] == '-'
                    && actual.AsSpan(0, wanted.Length).Equals(wanted.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Adds up a sequence under XPath 2.0's rules, where a sum has a type and not everything has a sum.
        /// </summary>
        /// <remarks>
        /// Folded through the ordinary <c>+</c>, so the result takes the type promotion gives it: a sum of
        /// integers is an <c>xs:integer</c>, and a sum of durations is a duration. What cannot be added at
        /// all — a string, or a year-month duration alongside a day-time one — is <c>FORG0006</c>, the same
        /// answer <c>avg</c> gives, rather than a number arrived at by reading the text as one.
        /// </remarks>
        private XPathValue TypedSum(XPathValue value, ref DynamicContext context)
        {
            List<XPathValue> prepared = Xpath2FunctionExpr.AggregateItems(
                XdmSequence.Items(value), "sum", numbersAndDurationsOnly: true);

            // The sum of nothing is zero, and an xs:integer zero unless the caller named something else —
            // which is what the second argument is for, a sum of durations having no integer zero.
            if (prepared.Count == 0)
            {
                return m_arguments.Length > 1
                    ? m_arguments[1].Evaluate(ref context)
                    : XPathValue.FromInteger(0);
            }

            XPathValue total = prepared[0];
            for (int i = 1; i < prepared.Count; i++)
            {
                total = XdmArithmetic.Apply(BinaryOperator.Add, total, prepared[i]);
            }

            return total;
        }

        /// <summary>
        /// Adds up a node-set or a sequence, which is the same operation over items of different shapes.
        /// </summary>
        /// <remarks>
        /// The backwards-compatible sum: every item a number by <c>fn:number</c>, so one that is not a
        /// number makes the total NaN rather than an error. The text of a node is read as
        /// <c>xs:double</c> writes one, as <c>number()</c> and arithmetic read it, so
        /// <c>sum(a)</c>, <c>number(a)</c> and <c>a + 0</c> agree over one node.
        /// </remarks>
        private static double Sum(XPathValue value)
        {
            double total = 0.0;

            if (value.Kind == XPathValueKind.NodeSet)
            {
                NodeSet nodes = value.AsNodeSet();
                for (int i = 0; i < nodes.Count; i++)
                {
                    total += XdmType.TextAsDoubleOrNaN(nodes.TreeAt(i).StringValueOf(nodes[i]));
                }

                return total;
            }

            foreach (XPathValue item in XdmSequence.Items(value))
            {
                total += XdmType.FirstItemAsDoubleOrNaN(item);
            }

            return total;
        }

        /// <summary>
        /// Applies <c>floor()</c>, <c>ceiling()</c> or <c>round()</c>, keeping the argument's numeric type.
        /// </summary>
        /// <remarks>
        /// XPath 1.0 has one numeric type and one answer: a double. XPath 2.0 declares these
        /// <c>numeric? -&gt; numeric?</c>, so the result is the type that went in — rounding an
        /// <c>xs:decimal</c> gives an <c>xs:decimal</c>, and rounding nothing gives nothing rather than NaN.
        /// An integer is already whole, so it comes back untouched.
        /// </remarks>
        private XPathValue Rounding(XPathValue value)
        {
            if (m_version.IsBackwardsCompatible)
            {
                return XPathValue.FromNumber(RoundDouble(value.ToNumber()));
            }

            // The declared type is numeric?, so nothing in means nothing out.
            if (value.Kind == XPathValueKind.Sequence && value.AsSequence().Count == 0)
            {
                return value;
            }

            switch (value.TypeCode)
            {
                case XdmTypeCode.Integer:
                    return value;

                case XdmTypeCode.Decimal:
                {
                    decimal exact = value.ToDecimal();

                    return XPathValue.FromDecimal(m_function switch
                    {
                        XPathFunction.Floor => decimal.Floor(exact),
                        XPathFunction.Ceiling => decimal.Ceiling(exact),
                        _ => decimal.Floor(exact + 0.5m),
                    });
                }

                case XdmTypeCode.Float:
                    return XPathValue.FromFloat((float)RoundDouble(value.ToNumber()));

                default:
                    return XPathValue.FromNumber(RoundDouble(value.ToNumber()));
            }
        }

        /// <summary>
        /// Rounds a double or a float, keeping the sign of a zero.
        /// </summary>
        /// <remarks>
        /// These types have two zeros and the specification keeps them apart: rounding −0.2 gives negative
        /// zero, not zero. It matters because they are different values and write differently — <c>-0</c>
        /// against <c>0</c> — so losing the sign here shows up in the result.
        /// </remarks>
        private double RoundDouble(double value)
        {
            double result = m_function switch
            {
                XPathFunction.Floor => Math.Floor(value),
                XPathFunction.Ceiling => Math.Ceiling(value),
                _ => Round(value),
            };

            return result == 0.0 && double.IsNegative(value) ? -0.0 : result;
        }

        /// <summary>
        /// Rounds half towards positive infinity, as XPath requires. This differs from
        /// <see cref="Math.Round(double)"/>, which rounds halves to even.
        /// </summary>
        private static double Round(double value)
        {
            return XdmRounding.At(value, 0, halfToEven: false);
        }

        /// <summary>
        /// The result length at which building moves off the stack. Beyond it the exact length is allocated,
        /// which is what a <see cref="StringBuilder"/> sized to the input did before.
        /// </summary>
        private const int StackLimit = 128;

        private static string NormalizeSpace(string text)
        {
            // Never longer than the input, so one buffer of that size is enough and never has to grow.
            Span<char> scratch = text.Length <= StackLimit
                ? stackalloc char[StackLimit]
                : new char[text.Length];

            CharStringBuilder builder = new CharStringBuilder(scratch);
            bool pendingSpace = false;

            foreach (char c in text)
            {
                if (c is ' ' or '\t' or '\r' or '\n')
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        private static string Translate(string subject, string from, string to)
        {
            // A character above the basic plane is one character to translate and not two halves of one, so
            // where any of the three strings holds a surrogate pair the mapping is made in code points. That
            // is rare enough to be worth a slower path of its own rather than slowing the usual one.
            if (CodePoints.Count(from) != from.Length
                || CodePoints.Count(to) != to.Length
                || CodePoints.Count(subject) != subject.Length)
            {
                return TranslateCodePoints(subject, from, to);
            }

            Span<char> scratch = subject.Length <= StackLimit
                ? stackalloc char[StackLimit]
                : new char[subject.Length];

            CharStringBuilder builder = new CharStringBuilder(scratch);
            foreach (char c in subject)
            {
                int index = from.IndexOf(c);
                if (index < 0)
                {
                    builder.Append(c);
                }
                else if (index < to.Length)
                {
                    builder.Append(to[index]);
                }

                // A character present in `from` but beyond the end of `to` is removed.
            }

            return builder.ToString();
        }

        /// <summary>The same mapping, made over code points rather than over UTF-16 units.</summary>
        private static string TranslateCodePoints(string subject, string from, string to)
        {
            List<int> wanted = new List<int>(CodePoints.Of(from));
            List<int> replacements = new List<int>(CodePoints.Of(to));
            StringBuilder builder = new StringBuilder(subject.Length);

            foreach (int point in CodePoints.Of(subject))
            {
                int index = wanted.IndexOf(point);

                if (index < 0)
                {
                    builder.Append(char.ConvertFromUtf32(point));
                }
                else if (index < replacements.Count)
                {
                    builder.Append(char.ConvertFromUtf32(replacements[index]));
                }

                // A character present in `from` but beyond the end of `to` is removed.
            }

            return builder.ToString();
        }
    }
}
