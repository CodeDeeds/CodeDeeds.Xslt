using System.Globalization;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A call to <c>fn:format-number()</c> whose third argument names a decimal format that only evaluation
    /// can settle.
    /// </summary>
    /// <remarks>
    /// A written name is resolved where the call is compiled, and the picture read once against the format it
    /// names. A computed one cannot be, so both the lookup and the picture wait — which is also why the
    /// static context is held here: it is what a name resolves against, and it outlives compilation.
    /// </remarks>
    internal sealed class NamedDecimalFormatExpr : Expr
    {
        private readonly Expr m_value;
        private readonly Expr m_picture;
        private readonly Expr m_name;
        private readonly IXPathStaticContext m_context;
        private readonly XsltVersion m_version;

        /// <summary>Initializes the call.</summary>
        /// <param name="value">The number to format.</param>
        /// <param name="picture">The picture to format it by.</param>
        /// <param name="name">The expression naming the decimal format.</param>
        /// <param name="context">What the name is resolved against.</param>
        /// <param name="version">The version in force.</param>
        public NamedDecimalFormatExpr(
            Expr value, Expr picture, Expr name, IXPathStaticContext context, XsltVersion version)
        {
            m_value = value;
            m_picture = picture;
            m_name = name;
            m_context = context;
            m_version = version;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_value, m_picture, m_name };

        /// <summary>
        /// Finds the decimal format a name stands for.
        /// </summary>
        /// <remarks>
        /// One complaint with two codes, the same split as an unreadable picture. XSLT 2.0 has a
        /// <c>format-number</c> of its own and calls an undeclared format name <c>XTDE1280</c>; XPath 3.0
        /// moved the function into the core library, where it is <c>FODF1280</c>.
        /// <para>
        /// Which one a caller hears follows <see cref="IXPathStaticContext.SyntaxVersion"/> and not the
        /// version the stylesheet claims, because the question is which specification defines the function
        /// being called — and that is the processor's library. The suite pairs these tests over one
        /// stylesheet, a <c>version="2.0"</c> one, and wants the two codes from the two processors.
        /// </para>
        /// </remarks>
        /// <exception cref="XsltException">Nothing is declared under that name.</exception>
        public static DecimalFormat Resolve(string name, IXPathStaticContext context)
        {
            if (DecimalFormat.TryReadName(name, context.ResolvePrefix, out ExpandedName resolved)
                && context.ResolveDecimalFormat(resolved) is DecimalFormat format)
            {
                return format;
            }

            throw XsltErrors.Error(
                context.SyntaxVersion.CompareTo(XsltVersion.V30) < 0
                    ? XsltErrorCode.XTDE1280
                    : XsltErrorCode.FODF1280,
                $"No decimal format called '{name}' is declared here.");
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue name = m_name.Evaluate(ref context);

            // An empty sequence names the unnamed format, exactly as leaving the argument out does.
            DecimalFormat format = Xpath2FunctionExpr.IsEmptySequence(name)
                ? DecimalFormat.Default
                : Resolve(name.ToStringValue(), m_context);

            return new Compiler.FormatNumberExpr(
                m_value, m_picture, format, m_version, m_context.SyntaxVersion).Evaluate(ref context);
        }
    }

    /// <summary>The functions XPath 3.0 adds to the core library, and the whole of the <c>math:</c> one.</summary>
    internal enum Xpath30Function : byte
    {
        /// <summary><c>fn:head</c>.</summary>
        Head,

        /// <summary><c>fn:tail</c>.</summary>
        Tail,

        /// <summary><c>fn:contains-token</c>.</summary>
        ContainsToken,

        /// <summary><c>fn:parse-ietf-date</c>.</summary>
        ParseIetfDate,

        /// <summary><c>fn:collation-key</c>.</summary>
        CollationKey,

        /// <summary><c>fn:serialize</c>.</summary>
        Serialize,

        /// <summary><c>fn:has-children</c>.</summary>
        HasChildren,

        /// <summary><c>fn:path</c>.</summary>
        Path,

        /// <summary><c>fn:innermost</c>.</summary>
        Innermost,

        /// <summary><c>fn:outermost</c>.</summary>
        Outermost,

        /// <summary><c>fn:round</c> with a precision, which XPath 2.0's one-argument form has not.</summary>
        RoundToPrecision,

        /// <summary><c>fn:unparsed-text-lines</c>.</summary>
        UnparsedTextLines,

        /// <summary><c>fn:default-language</c>.</summary>
        DefaultLanguage,

        /// <summary><c>math:pi</c>, which takes nothing and is a constant.</summary>
        Pi,

        /// <summary>One of the <c>math:</c> functions of a single double.</summary>
        Unary,

        /// <summary><c>math:pow</c>.</summary>
        Power,

        /// <summary><c>math:atan2</c>.</summary>
        Atan2,
    }

    /// <summary>
    /// A call to one of the functions XPath 3.0 adds, outside the higher-order ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept apart from the 2.0 library for the reason the higher-order functions are: a stylesheet at
    /// <c>version="2.0"</c> that calls <c>head()</c> means an extension function of its own and should be
    /// told so rather than quietly given this one.
    /// </para>
    /// <para>
    /// The <c>math:</c> namespace is here in full — it is fourteen functions over <see cref="Math"/> and
    /// nothing else, so splitting it into a file of its own would separate one line of dispatch from another.
    /// </para>
    /// </remarks>
    internal sealed class Xpath30FunctionExpr : Expr
    {
        /// <summary>The namespace the mathematical functions are in.</summary>
        public const string MathNamespace = "http://www.w3.org/2005/xpath-functions/math";

        /// <summary>XML's whitespace, which is narrower than .NET's and is what separates tokens.</summary>
        private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n' };

        private readonly Xpath30Function m_function;
        private readonly Expr[] m_arguments;
        private readonly string m_name;

        private Xpath30FunctionExpr(Xpath30Function function, Expr[] arguments, string name)
        {
            m_function = function;
            m_arguments = arguments;
            m_name = name;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override bool ReturnsNodeSet =>
            m_function is Xpath30Function.Innermost or Xpath30Function.Outermost;

        /// <summary>
        /// Creates a call to one of the <c>fn:</c> additions, or returns <see langword="null"/> where the
        /// name is not one.
        /// </summary>
        /// <param name="name">The function's local name.</param>
        /// <param name="arguments">The compiled arguments.</param>
        /// <param name="version">The version in force, for the calls delegated to the 2.0 library.</param>
        public static Expr? TryCreate(string name, Expr[] arguments, XsltVersion version)
        {
            // Two of these are not new functions but new forms of old ones, and are best written as the old
            // one with the argument 3.0 lets you leave out — so that everything else about them, the type
            // checking and the error codes included, stays in one place.
            if (name == "string-join" && arguments.Length == 1)
            {
                return Xpath2FunctionExpr.TryCreate(
                    "string-join", new[] { arguments[0], new StringLiteralExpr(string.Empty) }, version);
            }

            if (name == "tokenize" && arguments.Length == 1)
            {
                // XPath 3.1's one-argument form splits on whitespace, which is the two-argument form given
                // the pattern for it and the string trimmed first — the trim being what keeps the leading
                // and trailing space from making empty tokens at each end.
                return Xpath2FunctionExpr.TryCreate(
                    "tokenize",
                    new Expr[]
                    {
                        FunctionCallExpr.Create("normalize-space", arguments, version),
                        new StringLiteralExpr(" "),
                    },
                    version);
            }

            if (name == "format-integer")
            {
                // Its own class, because a picture is worth reading once and holding rather than re-reading
                // per call, and nothing else here has anything to keep.
                return FormatIntegerExpr.Create(arguments, version);
            }

            if (name == "trace" && arguments.Length == 1)
            {
                // 3.1 made the label optional. The two-argument form stays where it has always been, in the
                // 2.0 library, so this hands that one an empty label — which the message writer leaves off
                // rather than printing a colon with nothing before it.
                return Xpath2FunctionExpr.TryCreate(
                    "trace", new Expr[] { arguments[0], new StringLiteralExpr(string.Empty) }, version);
            }

            if (name == "generate-id")
            {
                // XSLT's function, which 3.0 moved into the standard library, so the class that has served
                // stylesheets since 1.0 answers here too. What differs is the argument: 1.0 took a node-set
                // and used the first node of it, where 3.0 takes one node or none and refuses two.
                if (arguments.Length > 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017, "'generate-id()' takes no more than one argument.");
                }

                Expr[] node = CheckedArgumentExpr.Wrap(
                    arguments, FunctionParameter.Parse("node()?"), name, version);

                return new Compiler.GenerateIdExpr(node.Length == 0 ? null : node[0]);
            }

            if (name == "random-number-generator")
            {
                // Its own class: what it hands back is a map holding two function items, and the generator
                // behind them is nothing the rest of this table has a shape for.
                return RandomNumberGeneratorExpr.Create(arguments, version);
            }

            if (name == "unparsed-text-lines")
            {
                Expr text = Xpath2FunctionExpr.TryCreate("unparsed-text", arguments, version)
                    ?? throw XsltErrors.Error(
                        XsltErrorCode.XPST0017, "fn:unparsed-text-lines() takes one or two arguments.");

                return new Xpath30FunctionExpr(
                    Xpath30Function.UnparsedTextLines, new[] { text }, name);
            }

            if (name == "uri-collection")
            {
                // Beside fn:collection() in a class of its own, since the two ask the collection resolver
                // the same question and differ only in whether the documents are then read.
                return CollectionFunctionExpr.Create(arguments, version, urisOnly: true);
            }

            if (name is "environment-variable" or "available-environment-variables")
            {
                // Their own class, because what they read is outside the transformation and whether they
                // may is the caller's decision, carried in the options rather than in this table.
                return EnvironmentVariableExpr.Create(name, arguments, version);
            }

            return Find(name, arguments.Length) is (Xpath30Function function, int least, int most, string signature)
                ? Build(function, name, arguments, least, most, signature, version)
                : null;
        }

        /// <summary>
        /// Finds the function a name and an argument count denote, or null where there is none.
        /// </summary>
        /// <remarks>
        /// One table, read both by <see cref="TryCreate"/> and by <see cref="TakesArity"/>, so that what
        /// <c>function-available()</c> reports and what a call is allowed to write cannot come apart. The
        /// argument count is a parameter and not only a bound to check, because <c>fn:round</c> is two
        /// functions sharing a name: the one-argument form belongs to the 2.0 library and only the form with
        /// a precision is here.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        /// <param name="arity">How many arguments are on the call, or being asked about.</param>
        private static (Xpath30Function Function, int Least, int Most, string Signature)? Find(
            string name,
            int arity)
        {
            return name switch
            {
                "head" => (Xpath30Function.Head, 1, 1, "item()*"),
                "tail" => (Xpath30Function.Tail, 1, 1, "item()*"),
                "contains-token" => (Xpath30Function.ContainsToken, 2, 3, "xs:string*, xs:string, xs:string"),
                "collation-key" => (Xpath30Function.CollationKey, 1, 2, "xs:string, xs:string"),
                "parse-ietf-date" => (Xpath30Function.ParseIetfDate, 1, 1, "xs:string?"),
                "serialize" => (Xpath30Function.Serialize, 1, 2, "item()*, item()?"),
                "has-children" => (Xpath30Function.HasChildren, 0, 1, "node()?"),
                "path" => (Xpath30Function.Path, 0, 1, "node()?"),
                "innermost" => (Xpath30Function.Innermost, 1, 1, "node()*"),
                "outermost" => (Xpath30Function.Outermost, 1, 1, "node()*"),
                "round" when arity == 2 =>
                    (Xpath30Function.RoundToPrecision, 2, 2, "numeric?, xs:integer"),
                "default-language" => (Xpath30Function.DefaultLanguage, 0, 0, ""),
                _ => null,
            };
        }

        /// <summary>
        /// Returns whether a function XPath 3.0 added will take a given number of arguments.
        /// </summary>
        /// <remarks>
        /// The three names 3.0 added as new <em>forms</em> of older functions are answered here too, because
        /// that is where the new arity lives: the one-argument <c>fn:string-join</c> and <c>fn:tokenize</c>,
        /// and <c>fn:format-integer</c>, which is 3.0's own throughout.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        /// <param name="arity">How many arguments the caller is asking about.</param>
        public static bool TakesArity(string name, int arity)
        {
            if (name is "string-join" or "tokenize")
            {
                return arity == 1;
            }

            if (name == "format-integer")
            {
                return arity is 2 or 3;
            }

            if (name == "trace")
            {
                return arity == 1;
            }

            if (name is "random-number-generator" or "uri-collection")
            {
                return arity is 0 or 1;
            }

            if (name == "environment-variable")
            {
                return arity == 1;
            }

            if (name == "available-environment-variables")
            {
                return arity == 0;
            }

            if (name == "generate-id")
            {
                return arity is 0 or 1;
            }

            if (name == "unparsed-text-lines")
            {
                return arity is 1 or 2;
            }

            return Find(name, arity) is (_, int least, int most, _) && arity >= least && arity <= most;
        }

        /// <summary>
        /// Creates a call to one of the <c>math:</c> functions, or refuses the name.
        /// </summary>
        /// <param name="name">The local name.</param>
        /// <param name="arguments">The compiled arguments.</param>
        /// <param name="version">The version in force.</param>
        /// <exception cref="XsltException">There is no such function, or the argument count is wrong.</exception>
        public static Expr CreateMath(string name, Expr[] arguments, XsltVersion version)
        {
            if (name == "pi")
            {
                return Build(Xpath30Function.Pi, name, arguments, 0, 0, string.Empty, version);
            }

            if (name == "pow")
            {
                return Build(Xpath30Function.Power, name, arguments, 2, 2, "xs:double?, numeric", version);
            }

            if (name == "atan2")
            {
                return Build(Xpath30Function.Atan2, name, arguments, 2, 2, "xs:double, xs:double", version);
            }

            if (Array.IndexOf(s_unary, name) < 0)
            {
                throw XsltErrors.Error(XsltErrorCode.XPST0017, $"There is no function 'math:{name}()'.");
            }

            return Build(Xpath30Function.Unary, name, arguments, 1, 1, "xs:double?", version);
        }

        /// <summary>The <c>math:</c> functions taking one <c>xs:double?</c> and answering one.</summary>
        private static readonly string[] s_unary =
        {
            "exp", "exp10", "log", "log10", "sqrt", "sin", "cos", "tan", "asin", "acos", "atan",
        };

        /// <summary>
        /// Returns whether a <c>math:</c> function will take a given number of arguments.
        /// </summary>
        /// <remarks>
        /// The arities <see cref="CreateMath"/> checks against, and short enough to read at a glance: two
        /// of them take two arguments, <c>math:pi</c> takes none, and everything else takes one. An arity of
        /// -1 asks only whether the name is one of these at all.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        /// <param name="arity">How many arguments the caller is asking about, or -1 for any.</param>
        public static bool TakesArityInMath(string name, int arity)
        {
            int wanted = name switch
            {
                "pi" => 0,
                "pow" or "atan2" => 2,
                _ => Array.IndexOf(s_unary, name) >= 0 ? 1 : -1,
            };

            return wanted >= 0 && (arity < 0 || arity == wanted);
        }

        private static Expr Build(
            Xpath30Function function,
            string name,
            Expr[] arguments,
            int least,
            int most,
            string signature,
            XsltVersion version)
        {
            if (arguments.Length < least || arguments.Length > most)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'{name}()' takes " + (least == most ? $"{least}" : $"{least} to {most}")
                    + $" arguments, and was given {arguments.Length}.");
            }

            return new Xpath30FunctionExpr(
                function,
                CheckedArgumentExpr.Wrap(arguments, FunctionParameter.Parse(signature), name, version),
                name);
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            switch (m_function)
            {
                case Xpath30Function.Head:
                {
                    List<XPathValue> items = Items(0, ref context);
                    return items.Count == 0 ? XPathValue.FromSequence(XdmSequence.Empty) : items[0];
                }

                case Xpath30Function.Tail:
                {
                    List<XPathValue> items = Items(0, ref context);

                    return items.Count <= 1
                        ? XPathValue.FromSequence(XdmSequence.Empty)
                        : XdmSequence.Concatenate(items.GetRange(1, items.Count - 1));
                }

                case Xpath30Function.ContainsToken:
                    return XPathValue.FromBoolean(ContainsToken(ref context));

                case Xpath30Function.ParseIetfDate:
                {
                    XPathValue text = m_arguments[0].Evaluate(ref context);

                    // Declared xs:string? -> xs:dateTime?, so nothing in is nothing out rather than an error.
                    if (Xpath2FunctionExpr.IsEmptySequence(text))
                    {
                        return XPathValue.FromSequence(XdmSequence.Empty);
                    }

                    string written = text.ToStringValue();

                    return IetfDate.TryParse(written, out XdmDateTime parsed)
                        ? XPathValue.FromDateTime(parsed)
                        : throw XsltErrors.Error(
                            XsltErrorCode.FORG0010,
                            $"'{written}' is not a date in any of the forms fn:parse-ietf-date() reads.");
                }

                case Xpath30Function.Serialize:
                    return XPathValue.FromString(
                        Serializer.Serialize(
                            Items(0, ref context),
                            m_arguments.Length > 1
                                ? m_arguments[1].Evaluate(ref context)
                                : XPathValue.FromSequence(XdmSequence.Empty)));

                case Xpath30Function.CollationKey:
                {
                    Collation collation = m_arguments.Length > 1
                        ? Collation.Resolve(m_arguments[1].Evaluate(ref context).ToStringValue(), ref context)
                        : Collation.Codepoint;

                    string key = m_arguments[0].Evaluate(ref context).ToStringValue();

                    return XPathValue.FromBinary(
                        new XdmBinary(collation.KeyBytes(key), XdmTypeCode.Base64Binary));
                }

                case Xpath30Function.HasChildren:
                {
                    XPathValue node = m_arguments.Length == 0
                        ? context.RequireContextItem("has-children()")
                        : m_arguments[0].Evaluate(ref context);

                    if (Xpath2FunctionExpr.IsEmptySequence(node))
                    {
                        return XPathValue.FromBoolean(false);
                    }

                    XPathValue item = XdmSequence.RequireSingleItem(node, "the node fn:has-children() asks about");
                    return XPathValue.FromBoolean(TreeOf(item).FirstChildOf(IdOf(item)) >= 0);
                }

                case Xpath30Function.Path:
                {
                    XPathValue node = m_arguments.Length == 0
                        ? context.RequireContextItem("path()")
                        : m_arguments[0].Evaluate(ref context);

                    if (Xpath2FunctionExpr.IsEmptySequence(node))
                    {
                        return XPathValue.FromSequence(XdmSequence.Empty);
                    }

                    XPathValue item = XdmSequence.RequireSingleItem(node, "the node fn:path() asks about");
                    return XPathValue.FromString(PathTo(TreeOf(item), IdOf(item)));
                }

                case Xpath30Function.Innermost:
                case Xpath30Function.Outermost:
                    return Nesting(ref context, m_function == Xpath30Function.Outermost);

                case Xpath30Function.RoundToPrecision:
                    return RoundToPrecision(ref context);

                case Xpath30Function.UnparsedTextLines:
                {
                    XPathValue text = m_arguments[0].Evaluate(ref context);

                    if (Xpath2FunctionExpr.IsEmptySequence(text))
                    {
                        return text;
                    }

                    return Lines(text.ToStringValue());
                }

                case Xpath30Function.DefaultLanguage:
                {
                    // The default language of the dynamic context, which the specification leaves to the
                    // implementation and this one settles at English: it is the language format-integer()
                    // spells a number in and format-date() names a month in when nobody says otherwise, and
                    // the point of the function is that a caller can pass on what it would have used.
                    XdmType.TryGet("language", out XdmType.BuiltInType language);

                    return XdmType.Cast(XPathValue.FromString("en"), language);
                }

                case Xpath30Function.Pi:
                    return XPathValue.FromNumber(Math.PI);

                case Xpath30Function.Power:
                {
                    XPathValue value = m_arguments[0].Evaluate(ref context);

                    return Xpath2FunctionExpr.IsEmptySequence(value)
                        ? value
                        : XPathValue.FromNumber(Math.Pow(
                            value.ToNumber(), m_arguments[1].Evaluate(ref context).ToNumber()));
                }

                case Xpath30Function.Atan2:
                    // The arguments are y then x, which is the order the mathematics is written in and the
                    // opposite of what the name suggests to anyone reading it as "the angle of x and y".
                    return XPathValue.FromNumber(Math.Atan2(
                        m_arguments[0].Evaluate(ref context).ToNumber(),
                        m_arguments[1].Evaluate(ref context).ToNumber()));

                default:
                {
                    XPathValue value = m_arguments[0].Evaluate(ref context);

                    // Declared xs:double? -> xs:double?, so nothing in is nothing out rather than NaN.
                    return Xpath2FunctionExpr.IsEmptySequence(value)
                        ? value
                        : XPathValue.FromNumber(Unary(value.ToNumber()));
                }
            }
        }

        private double Unary(double x)
        {
            return m_name switch
            {
                "exp" => Math.Exp(x),
                "exp10" => Math.Pow(10.0, x),
                "log" => Math.Log(x),
                "log10" => Math.Log10(x),
                "sqrt" => Math.Sqrt(x),
                "sin" => Math.Sin(x),
                "cos" => Math.Cos(x),
                "tan" => Math.Tan(x),
                "asin" => Math.Asin(x),
                "acos" => Math.Acos(x),
                _ => Math.Atan(x),
            };
        }

        /// <summary>
        /// Whether a token appears in a whitespace-separated list, which is how <c>class</c> attributes and
        /// <c>xsl:</c> attributes such as <c>exclude-result-prefixes</c> are written.
        /// </summary>
        /// <remarks>
        /// The token is trimmed before the search and an empty one is never found, so
        /// <c>contains-token(@class, ' ')</c> is false rather than true of everything.
        /// <para>
        /// What separates one token from the next is XML's whitespace and only that — space, tab, carriage
        /// return and line feed. .NET's own idea of whitespace is wider, and a no-break space is the case
        /// that shows the difference: it holds a token together rather than splitting it.
        /// </para>
        /// </remarks>
        private bool ContainsToken(ref DynamicContext context)
        {
            string token = m_arguments[1].Evaluate(ref context).ToStringValue().Trim(Whitespace);

            Collation collation = m_arguments.Length > 2
                ? Collation.Resolve(m_arguments[2].Evaluate(ref context).ToStringValue(), ref context)
                : Collation.Codepoint;

            if (token.Length == 0)
            {
                return false;
            }

            foreach (XPathValue item in Items(0, ref context))
            {
                foreach (string candidate in XdmSequence.StringValueOf(item)
                    .Split(Whitespace, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (collation.AreEqual(candidate, token))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Keeps the nodes at one end of the nesting: the outermost have no ancestor in the set, the
        /// innermost no descendant.
        /// </summary>
        /// <remarks>
        /// What both are for is the same thing: a path that has selected a subtree and its parts, where only
        /// one of the two levels is wanted. <c>outermost(//section)</c> is the top-level sections of a
        /// document whose sections nest.
        /// </remarks>
        private XPathValue Nesting(ref DynamicContext context, bool outermost)
        {
            List<XPathValue> items = Items(0, ref context);
            NodeSet kept = new NodeSet(items.Count > 0 ? TreeOf(items[0]) : context.Tree, items.Count);

            foreach (XPathValue item in items)
            {
                bool covered = false;

                foreach (XPathValue other in items)
                {
                    // A node covers itself, which would empty the answer, so the pair has to differ first.
                    if (ReferenceEquals(TreeOf(other), TreeOf(item)) && IdOf(other) == IdOf(item))
                    {
                        continue;
                    }

                    if (outermost ? IsAncestor(other, item) : IsAncestor(item, other))
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    kept.Add(TreeOf(item), IdOf(item));
                }
            }

            // Both are defined to answer in document order with no repeats, whatever order they were given
            // in — which is what a node-set already promises, so saying so is all that is needed.
            kept.SortAndDeduplicate();
            return XPathValue.FromNodeSet(kept);
        }

        /// <summary>Whether one node is an ancestor of another, which only holds within one tree.</summary>
        private static bool IsAncestor(XPathValue ancestor, XPathValue node)
        {
            XdmTree tree = TreeOf(node);

            if (!ReferenceEquals(TreeOf(ancestor), tree))
            {
                return false;
            }

            int wanted = IdOf(ancestor);

            for (int at = tree.ParentOf(IdOf(node)); at >= 0; at = tree.ParentOf(at))
            {
                if (at == wanted)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Rounds to a number of digits, which may be negative to round to tens, hundreds and beyond.
        /// </summary>
        /// <remarks>
        /// Halves go towards positive infinity, as they do in the one-argument form: <c>round(2.5)</c> is 3
        /// and <c>round(-2.5)</c> is −2. That is the rule <c>round-half-to-even()</c> exists to escape.
        /// </remarks>
        private XPathValue RoundToPrecision(ref DynamicContext context)
        {
            XPathValue value = m_arguments[0].Evaluate(ref context);

            if (Xpath2FunctionExpr.IsEmptySequence(value))
            {
                return value;
            }

            long digits = m_arguments[1].Evaluate(ref context).ToInteger();

            // An integer rounded to a whole number of digits is itself.
            if (value.TypeCode == XdmTypeCode.Integer && digits >= 0)
            {
                return value;
            }

            // An exact type is rounded exactly. Going through a double would answer 35600.000000000004 for
            // round(35612.25, -2), the scaling back up being a division by a power of ten that is not one.
            if (value.TypeCode is XdmTypeCode.Decimal or XdmTypeCode.Integer && Math.Abs(digits) <= 18)
            {
                try
                {
                    decimal exact = value.TypeCode == XdmTypeCode.Integer
                        ? value.ToInteger()
                        : value.ToDecimal();

                    decimal factor = Power10(Math.Abs(digits));
                    decimal scaled = digits >= 0
                        ? Math.Floor((exact * factor) + 0.5m) / factor
                        : Math.Floor((exact / factor) + 0.5m) * factor;

                    return value.TypeCode == XdmTypeCode.Integer
                        ? XPathValue.FromInteger((long)scaled)
                        : XPathValue.FromDecimal(scaled);
                }
                catch (OverflowException)
                {
                    // A value near the top of the decimal range scaled up by a power of ten leaves it. The
                    // double below is then the best answer available, and it is the answer the same value
                    // written as a double would have got.
                }
            }

            double number = value.ToNumber();

            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                return value;
            }

            double rounded = XdmRounding.At(number, (int)Math.Clamp(digits, -400, 400), halfToEven: false);

            return value.TypeCode == XdmTypeCode.Float
                ? XPathValue.FromFloat((float)rounded)
                : XPathValue.FromNumber(rounded);
        }

        private static decimal Power10(long digits)
        {
            decimal factor = 1m;
            for (long i = 0; i < digits; i++)
            {
                factor *= 10m;
            }

            return factor;
        }

        /// <summary>
        /// Splits text into lines, on any of the three line endings, with no empty line at the end.
        /// </summary>
        /// <remarks>
        /// The trailing newline of a file is a terminator rather than a separator, so a file of three lines
        /// gives three strings whether or not it ends in one. A file that is empty gives none.
        /// </remarks>
        private static XPathValue Lines(string text)
        {
            List<XPathValue> lines = new List<XPathValue>();
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] is not ('\r' or '\n'))
                {
                    continue;
                }

                lines.Add(XPathValue.FromString(text[start..i]));

                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }

            if (start < text.Length)
            {
                lines.Add(XPathValue.FromString(text[start..]));
            }

            return XdmSequence.Concatenate(lines);
        }

        private List<XPathValue> Items(int index, ref DynamicContext context)
        {
            return XdmSequence.Items(m_arguments[index].Evaluate(ref context));
        }

        /// <summary>
        /// Builds the path expression that selects one node, which is what <c>fn:path</c> returns.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A path anyone can evaluate, not a pretty one: every name is written in the braced form so that no
        /// prefix has to be in scope where the answer is used, and every step but an attribute's carries a
        /// position so that the path selects the one node and not its like-named siblings. That is why
        /// <c>/Q{}doc[1]</c> and not <c>/doc</c>.
        /// </para>
        /// <para>
        /// What the path hangs from depends on what the tree is rooted at. A document node is written as the
        /// leading <c>/</c>; a tree rooted at anything else — which is what a sequence constructor with an
        /// <c>as</c> declaration builds — has no <c>/</c> to write, so the specification starts the path
        /// with a call to <c>fn:root()</c> instead. The root itself contributes no step either way.
        /// </para>
        /// </remarks>
        /// <param name="tree">The tree the node is in.</param>
        /// <param name="node">The node.</param>
        private static string PathTo(XdmTree tree, int node)
        {
            int root = tree.RootOf(node);

            if (node == root)
            {
                return tree.KindOf(node) == NodeKind.Root ? "/" : RootCall;
            }

            List<string> steps = new List<string>();

            for (int step = node; step >= 0 && step != root; step = tree.ParentOf(step))
            {
                steps.Add(StepTo(tree, step));
            }

            steps.Reverse();

            // A document root writes itself as the leading slash; anything else has to be named, and then
            // the first step needs a separator of its own.
            return (tree.KindOf(root) == NodeKind.Root ? string.Empty : RootCall)
                + "/" + string.Join("/", steps);
        }

        /// <summary>How <c>fn:path</c> names a tree that is not rooted at a document node.</summary>
        private const string RootCall = "Q{http://www.w3.org/2005/xpath-functions}root()";

        /// <summary>Writes the one step of a path that selects a node from its parent.</summary>
        /// <param name="tree">The tree the node is in.</param>
        /// <param name="node">The node the step is to select.</param>
        private static string StepTo(XdmTree tree, int node)
        {
            int fingerprint = tree.FingerprintOf(node);
            string local = tree.NameTable.GetLocalName(fingerprint);
            string uri = tree.NameTable.GetNamespaceUri(fingerprint);


            if (tree.KindOf(node) == NodeKind.Namespace)
            {
                // Named by its prefix (F&O §14.5.1); the default namespace's node has none to be named by.
                return local.Length == 0 ? "namespace::*[fn:local-name()=\"\"]" : "namespace::" + local;
            }

            if (XdmTree.IsAttribute(node))
            {
                // The one step with no position on it: an element cannot carry two attributes of one name,
                // so the name already selects the one node.
                return uri.Length == 0 ? "@" + local : "@Q{" + uri + "}" + local;
            }

            return tree.KindOf(node) switch
            {
                NodeKind.Element => "Q{" + uri + "}" + local + Position(tree, node),
                NodeKind.Text => "text()" + Position(tree, node),
                NodeKind.Comment => "comment()" + Position(tree, node),
                NodeKind.ProcessingInstruction =>
                    "processing-instruction(" + local + ")" + Position(tree, node),
                _ => "node()" + Position(tree, node),
            };
        }

        /// <summary>
        /// Counts a node's position among the siblings a step of its own shape would select.
        /// </summary>
        /// <remarks>
        /// Which siblings those are depends on the kind. An element or a processing instruction is counted
        /// among its like-named siblings, because the step names it; a text node or a comment among all of
        /// its kind, because the step cannot name one.
        /// </remarks>
        /// <param name="tree">The tree the node is in.</param>
        /// <param name="node">The node to find.</param>
        private static string Position(XdmTree tree, int node)
        {
            NodeKind kind = tree.KindOf(node);
            bool byName = kind is NodeKind.Element or NodeKind.ProcessingInstruction;
            int fingerprint = tree.FingerprintOf(node);
            int parent = tree.ParentOf(node);
            int position = 1;

            for (int sibling = parent < 0 ? node : tree.FirstChildOf(parent);
                sibling >= 0 && sibling != node;
                sibling = tree.NextSiblingOf(sibling))
            {
                if (tree.KindOf(sibling) == kind && (!byName || tree.FingerprintOf(sibling) == fingerprint))
                {
                    position++;
                }
            }

            return "[" + position.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
        }

        private static XdmTree TreeOf(XPathValue item)
        {
            return item.Kind == XPathValueKind.Node ? item.NodeTree : item.AsNodeSet().Tree;
        }

        private static int IdOf(XPathValue item)
        {
            return item.Kind == XPathValueKind.Node ? item.NodeId : item.AsNodeSet()[0];
        }
    }
}
