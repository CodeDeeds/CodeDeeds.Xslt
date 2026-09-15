using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The functions XPath 2.0 adds to the core library that this engine implements.</summary>
    public enum Xpath2Function : byte
    {
        /// <summary><c>empty(sequence)</c></summary>
        Empty,

        /// <summary><c>exists(sequence)</c></summary>
        Exists,

        /// <summary><c>reverse(sequence)</c></summary>
        Reverse,

        /// <summary><c>distinct-values(sequence)</c></summary>
        DistinctValues,

        /// <summary><c>subsequence(sequence, start, length?)</c></summary>
        Subsequence,

        /// <summary><c>insert-before(sequence, position, inserts)</c></summary>
        InsertBefore,

        /// <summary><c>remove(sequence, position)</c></summary>
        Remove,

        /// <summary><c>index-of(sequence, value)</c></summary>
        IndexOf,

        /// <summary><c>unordered(sequence)</c></summary>
        Unordered,

        /// <summary><c>zero-or-one(sequence)</c></summary>
        ZeroOrOne,

        /// <summary><c>one-or-more(sequence)</c></summary>
        OneOrMore,

        /// <summary><c>exactly-one(sequence)</c></summary>
        ExactlyOne,

        /// <summary><c>deep-equal(a, b)</c></summary>
        DeepEqual,

        /// <summary><c>abs(number)</c></summary>
        Abs,

        /// <summary><c>round-half-to-even(number, precision?)</c></summary>
        RoundHalfToEven,

        /// <summary><c>min(sequence)</c></summary>
        Min,

        /// <summary><c>max(sequence)</c></summary>
        Max,

        /// <summary><c>avg(sequence)</c></summary>
        Average,

        /// <summary><c>upper-case(string)</c></summary>
        UpperCase,

        /// <summary><c>lower-case(string)</c></summary>
        LowerCase,

        /// <summary><c>ends-with(string, string)</c></summary>
        EndsWith,

        /// <summary><c>string-join(sequence, separator)</c></summary>
        StringJoin,

        /// <summary><c>compare(a, b)</c></summary>
        Compare,

        /// <summary><c>codepoints-to-string(sequence)</c></summary>
        CodepointsToString,

        /// <summary><c>string-to-codepoints(string)</c></summary>
        StringToCodepoints,

        /// <summary><c>matches(input, pattern, flags?)</c></summary>
        Matches,

        /// <summary><c>replace(input, pattern, replacement, flags?)</c></summary>
        Replace,

        /// <summary><c>tokenize(input, pattern, flags?)</c></summary>
        Tokenize,

        /// <summary><c>current-dateTime()</c>, <c>current-date()</c>, <c>current-time()</c></summary>
        CurrentDateTime,

        /// <summary><c>implicit-timezone()</c></summary>
        ImplicitTimezone,

        /// <summary>One of the <c>*-from-dateTime</c>, <c>*-from-date</c> and <c>*-from-time</c> family.</summary>
        ComponentOfDateTime,

        /// <summary>One of the <c>*-from-duration</c> family.</summary>
        ComponentOfDuration,

        /// <summary><c>dateTime(date, time)</c></summary>
        CombineDateAndTime,

        /// <summary><c>doc(uri)</c></summary>
        Doc,

        /// <summary><c>doc-available(uri)</c></summary>
        DocAvailable,

        /// <summary><c>unparsed-text(href, encoding?)</c></summary>
        UnparsedText,

        /// <summary><c>unparsed-text-available(href, encoding?)</c></summary>
        UnparsedTextAvailable,

        /// <summary><c>base-uri(node?)</c></summary>
        BaseUri,

        /// <summary><c>document-uri(node?)</c></summary>
        DocumentUri,

        /// <summary><c>static-base-uri()</c></summary>
        StaticBaseUri,

        /// <summary><c>resolve-uri(relative, base?)</c></summary>
        ResolveUri,

        /// <summary><c>data(sequence)</c></summary>
        Data,

        /// <summary><c>nilled(node)</c></summary>
        Nilled,

        /// <summary><c>root(node?)</c></summary>
        Root,

        /// <summary><c>collection(uri?)</c></summary>
        Collection,

        /// <summary><c>error(code?, description?, object?)</c></summary>
        Error,

        /// <summary><c>trace(value, label)</c></summary>
        Trace,

        /// <summary><c>in-scope-prefixes(element)</c></summary>
        InScopePrefixes,

        /// <summary><c>namespace-uri-for-prefix(prefix, element)</c></summary>
        NamespaceUriForPrefix,

        /// <summary><c>encode-for-uri(string)</c></summary>
        EncodeForUri,

        /// <summary><c>iri-to-uri(string)</c></summary>
        IriToUri,

        /// <summary><c>escape-html-uri(string)</c></summary>
        EscapeHtmlUri,

        /// <summary><c>codepoint-equal(a, b)</c></summary>
        CodepointEqual,

        /// <summary><c>normalize-unicode(string, form?)</c></summary>
        NormalizeUnicode,

        /// <summary><c>default-collation()</c></summary>
        DefaultCollation,

        /// <summary><c>format-dateTime</c>, <c>format-date</c> and <c>format-time</c>.</summary>
        FormatDateTime,

        /// <summary><c>adjust-dateTime-to-timezone</c> and its two relatives.</summary>
        AdjustToTimezone,

        /// <summary><c>node-name(node)</c></summary>
        NodeName,

        /// <summary><c>QName(uri, name)</c></summary>
        MakeQName,

        /// <summary><c>resolve-QName(name, element)</c></summary>
        ResolveQName,

        /// <summary>One of <c>local-name-from-QName</c>, <c>namespace-uri-from-QName</c>, <c>prefix-from-QName</c>.</summary>
        PartOfQName,
    }

    /// <summary>
    /// A call to one of the functions XPath 2.0 adds.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="FunctionCallExpr"/>, which is the XPath 1.0 core library, because these are
    /// only available where the version in force asks for them: a stylesheet written for 1.0 that calls
    /// <c>matches()</c> means an extension function, not this.
    /// </remarks>
    public sealed class Xpath2FunctionExpr : Expr
    {
        private readonly Xpath2Function m_function;
        private readonly Expr[] m_arguments;
        private readonly string m_name;

        /// <summary>
        /// The version the call was compiled against, kept for the regular-expression functions: XPath 3.0
        /// adds the non-capturing group and the <c>q</c> flag, and a 2.0 stylesheet should be told they are
        /// not there rather than quietly given them.
        /// </summary>
        private readonly XsltVersion m_version;

        /// <summary>
        /// The collation a <c>default-collation</c> in scope puts there, as a URI, or null for the code
        /// point one.
        /// </summary>
        /// <remarks>
        /// Set where the call is compiled, and resolved as it is set: what the functions that take an
        /// optional collation use where the caller supplies none, and what <c>fn:default-collation()</c>
        /// answers with.
        /// </remarks>
        internal string? DefaultCollation => m_defaultCollation;

        /// <summary>
        /// Puts a default collation in force for the call, resolved now against the caller's collations
        /// so that a URI nobody has is refused where the call is written.
        /// </summary>
        /// <param name="uri">The collation URI.</param>
        /// <param name="resolver">The caller's collations, where there are any.</param>
        internal void UseDefaultCollation(string uri, IXsltCollationResolver? resolver)
        {
            m_defaultCollation = uri;
            m_default = XPath.Collation.Resolve(uri, resolver);
        }

        private string? m_defaultCollation;
        private Collation? m_default;

        private Xpath2FunctionExpr(
            Xpath2Function function, Expr[] arguments, string name, XsltVersion version)
        {
            m_function = function;
            m_arguments = arguments;
            m_name = name;
            m_version = version;
        }

        /// <summary>
        /// The date and time families, which are one implementation apiece rather than one per name.
        /// </summary>
        /// <remarks>
        /// <c>year-from-dateTime</c>, <c>year-from-date</c> and the twenty-odd others differ only in which
        /// component they read and which type they will accept, so the name is kept and read at evaluation
        /// rather than turned into two dozen enumeration members.
        /// </remarks>
        /// <summary>
        /// The three functions that take a picture, which have two signatures rather than a range of
        /// arities: the value and the picture, or those and all three of language, calendar and place.
        /// </summary>
        private static readonly string[] s_pictureFunctions =
            { "format-dateTime", "format-date", "format-time" };

        private static readonly string[] s_dateTimeComponents =
        {
            "year-from-dateTime", "month-from-dateTime", "day-from-dateTime", "hours-from-dateTime",
            "minutes-from-dateTime", "seconds-from-dateTime", "timezone-from-dateTime",
            "year-from-date", "month-from-date", "day-from-date", "timezone-from-date",
            "hours-from-time", "minutes-from-time", "seconds-from-time", "timezone-from-time",
        };

        private static readonly string[] s_durationComponents =
        {
            "years-from-duration", "months-from-duration", "days-from-duration",
            "hours-from-duration", "minutes-from-duration", "seconds-from-duration",
        };

        /// <summary>One function's arity and the types it declares for its parameters.</summary>
        private sealed record Entry(
            Xpath2Function Function,
            int Minimum,
            int Maximum,
            FunctionParameter[] Parameters);

        /// <summary>
        /// The functions XPath 2.0 adds, with the signatures it declares for them.
        /// </summary>
        /// <remarks>
        /// The signatures are what the function conversion rules are applied against, and the reason
        /// <c>fn:abs(xs:string('1'))</c> is a type error rather than 1; see <see cref="FunctionParameter"/>.
        /// A <c>collation</c> parameter is declared <c>xs:string</c> and then checked against the collations
        /// this engine has, which is a separate refusal further down.
        /// </remarks>
        private static readonly Dictionary<string, Entry> s_registry = BuildRegistry();

        private static Dictionary<string, Entry> BuildRegistry()
        {
            Dictionary<string, Entry> registry = new(StringComparer.Ordinal);

            void Add(string name, Xpath2Function function, int minimum, int maximum, string signature)
            {
                registry.Add(name, new Entry(function, minimum, maximum, FunctionParameter.Parse(signature)));
            }

            const string Collation = ", xs:string";

            Add("empty", Xpath2Function.Empty, 1, 1, "item()*");
            Add("exists", Xpath2Function.Exists, 1, 1, "item()*");
            Add("reverse", Xpath2Function.Reverse, 1, 1, "item()*");
            Add("distinct-values", Xpath2Function.DistinctValues, 1, 2, "xs:anyAtomicType*" + Collation);
            Add("subsequence", Xpath2Function.Subsequence, 2, 3, "item()*, xs:double, xs:double?");
            Add("insert-before", Xpath2Function.InsertBefore, 3, 3, "item()*, xs:integer, item()*");
            Add("remove", Xpath2Function.Remove, 2, 2, "item()*, xs:integer");
            Add("index-of", Xpath2Function.IndexOf, 2, 3,
                "xs:anyAtomicType*, xs:anyAtomicType" + Collation);
            Add("unordered", Xpath2Function.Unordered, 1, 1, "item()*");
            Add("zero-or-one", Xpath2Function.ZeroOrOne, 1, 1, "item()*");
            Add("one-or-more", Xpath2Function.OneOrMore, 1, 1, "item()*");
            Add("exactly-one", Xpath2Function.ExactlyOne, 1, 1, "item()*");
            Add("deep-equal", Xpath2Function.DeepEqual, 2, 3, "item()*, item()*" + Collation);
            Add("abs", Xpath2Function.Abs, 1, 1, "numeric?");
            Add("round-half-to-even", Xpath2Function.RoundHalfToEven, 1, 2, "numeric?, xs:integer");
            Add("min", Xpath2Function.Min, 1, 2, "xs:anyAtomicType*" + Collation);
            Add("max", Xpath2Function.Max, 1, 2, "xs:anyAtomicType*" + Collation);
            Add("avg", Xpath2Function.Average, 1, 1, "xs:anyAtomicType*");
            Add("upper-case", Xpath2Function.UpperCase, 1, 1, "xs:string?");
            Add("lower-case", Xpath2Function.LowerCase, 1, 1, "xs:string?");
            Add("ends-with", Xpath2Function.EndsWith, 2, 3, "xs:string?, xs:string?" + Collation);
            // Both arguments are required: XPath 2.0 has no one-argument form, and a caller who wrote one
            // meant something the function cannot supply.
            // XPath 3.1 widened the first parameter from xs:string* to xs:anyAtomicType*, so string-join(1 to
            // 9) is a string of digits rather than a type error. Declared wide at every version, the
            // narrower reading having been a restriction nothing depended on.
            Add("string-join", Xpath2Function.StringJoin, 2, 2, "xs:anyAtomicType*, xs:string");
            Add("compare", Xpath2Function.Compare, 2, 3, "xs:string?, xs:string?" + Collation);
            Add("codepoints-to-string", Xpath2Function.CodepointsToString, 1, 1, "xs:integer*");
            Add("string-to-codepoints", Xpath2Function.StringToCodepoints, 1, 1, "xs:string?");
            Add("matches", Xpath2Function.Matches, 2, 3, "xs:string?, xs:string, xs:string");
            Add("replace", Xpath2Function.Replace, 3, 4, "xs:string?, xs:string, xs:string, xs:string");
            Add("tokenize", Xpath2Function.Tokenize, 2, 3, "xs:string?, xs:string, xs:string");
            Add("current-dateTime", Xpath2Function.CurrentDateTime, 0, 0, "");
            Add("current-date", Xpath2Function.CurrentDateTime, 0, 0, "");
            Add("current-time", Xpath2Function.CurrentDateTime, 0, 0, "");
            Add("implicit-timezone", Xpath2Function.ImplicitTimezone, 0, 0, "");
            Add("dateTime", Xpath2Function.CombineDateAndTime, 2, 2, "xs:date?, xs:time?");
            Add("doc", Xpath2Function.Doc, 1, 1, "xs:string?");
            Add("doc-available", Xpath2Function.DocAvailable, 1, 1, "xs:string?");
            Add("unparsed-text", Xpath2Function.UnparsedText, 1, 2, "xs:string?, xs:string");
            Add("unparsed-text-available", Xpath2Function.UnparsedTextAvailable, 1, 2, "xs:string?, xs:string");
            Add("base-uri", Xpath2Function.BaseUri, 0, 1, "node()?");
            Add("document-uri", Xpath2Function.DocumentUri, 1, 1, "node()?");
            Add("static-base-uri", Xpath2Function.StaticBaseUri, 0, 0, "");
            Add("resolve-uri", Xpath2Function.ResolveUri, 1, 2, "xs:string?, xs:string");
            Add("data", Xpath2Function.Data, 1, 1, "item()*");
            Add("nilled", Xpath2Function.Nilled, 1, 1, "node()?");
            Add("root", Xpath2Function.Root, 0, 1, "node()?");
            Add("collection", Xpath2Function.Collection, 0, 1, "xs:string?");
            Add("error", Xpath2Function.Error, 0, 3, "xs:QName?, xs:string, item()*");
            Add("trace", Xpath2Function.Trace, 2, 2, "item()*, xs:string");
            Add("in-scope-prefixes", Xpath2Function.InScopePrefixes, 1, 1, "element()");
            Add("namespace-uri-for-prefix", Xpath2Function.NamespaceUriForPrefix, 2, 2,
                "xs:string?, element()");
            Add("encode-for-uri", Xpath2Function.EncodeForUri, 1, 1, "xs:string?");
            Add("iri-to-uri", Xpath2Function.IriToUri, 1, 1, "xs:string?");
            Add("escape-html-uri", Xpath2Function.EscapeHtmlUri, 1, 1, "xs:string?");
            Add("codepoint-equal", Xpath2Function.CodepointEqual, 2, 2, "xs:string?, xs:string?");
            Add("normalize-unicode", Xpath2Function.NormalizeUnicode, 1, 2, "xs:string?, xs:string");
            Add("default-collation", Xpath2Function.DefaultCollation, 0, 0, "");

            const string Picture = ", xs:string, xs:string?, xs:string?, xs:string?";

            Add("format-dateTime", Xpath2Function.FormatDateTime, 2, 5, "xs:dateTime?" + Picture);
            Add("format-date", Xpath2Function.FormatDateTime, 2, 5, "xs:date?" + Picture);
            Add("format-time", Xpath2Function.FormatDateTime, 2, 5, "xs:time?" + Picture);

            const string Timezone = ", xs:dayTimeDuration?";

            Add("adjust-dateTime-to-timezone", Xpath2Function.AdjustToTimezone, 1, 2,
                "xs:dateTime?" + Timezone);
            Add("adjust-date-to-timezone", Xpath2Function.AdjustToTimezone, 1, 2, "xs:date?" + Timezone);
            Add("adjust-time-to-timezone", Xpath2Function.AdjustToTimezone, 1, 2, "xs:time?" + Timezone);

            Add("node-name", Xpath2Function.NodeName, 1, 1, "node()?");
            Add("QName", Xpath2Function.MakeQName, 2, 2, "xs:string?, xs:string");
            Add("resolve-QName", Xpath2Function.ResolveQName, 2, 2, "xs:string?, element()");
            Add("local-name-from-QName", Xpath2Function.PartOfQName, 1, 1, "xs:QName?");
            Add("namespace-uri-from-QName", Xpath2Function.PartOfQName, 1, 1, "xs:QName?");
            Add("prefix-from-QName", Xpath2Function.PartOfQName, 1, 1, "xs:QName?");

            return registry;
        }

        /// <summary>
        /// Creates a call to one of these functions, or returns <see langword="null"/> if the name is not one.
        /// </summary>
        /// <param name="name">The function's local name.</param>
        /// <param name="arguments">The argument expressions.</param>
        /// <returns>The compiled call, or <see langword="null"/>.</returns>
        public static Expr? TryCreate(string name, Expr[] arguments)
        {
            return TryCreate(name, arguments, XsltVersion.V20);
        }

        /// <summary>
        /// Creates a call where the language available and the behaviour in force are different versions.
        /// </summary>
        /// <remarks>
        /// Which they are inside a stylesheet: a <c>version="2.0"</c> stylesheet run by a 3.0 processor asks
        /// for 2.0 <em>behaviour</em> and not for a smaller language. So the arity a function may be called
        /// with and the regular expression syntax it accepts follow the processor, while how its arguments
        /// are converted follows the stylesheet.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        /// <param name="arguments">The argument expressions.</param>
        /// <param name="version">The version whose behaviour is in force, which settles argument conversion.</param>
        /// <param name="syntaxVersion">The version whose language is available.</param>
        /// <returns>The compiled call, or <see langword="null"/>.</returns>
        public static Expr? TryCreate(
            string name,
            Expr[] arguments,
            XsltVersion version,
            XsltVersion syntaxVersion)
        {
            return Build(name, arguments, version, syntaxVersion);
        }

        /// <summary>
        /// Returns whether a local name is one of these functions, whatever arity it is called with.
        /// </summary>
        /// <remarks>
        /// For <c>fn:function-available()</c>, which asks what the processor can do rather than whether a
        /// particular call would compile.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        public static bool IsKnown(string name)
        {
            return s_registry.ContainsKey(name)
                || Array.IndexOf(s_dateTimeComponents, name) >= 0
                || Array.IndexOf(s_durationComponents, name) >= 0;
        }

        /// <summary>
        /// Returns whether one of these functions will take a given number of arguments.
        /// </summary>
        /// <remarks>
        /// The range comes from the same entry <see cref="Build"/> checks against, the minimum through the
        /// same version adjustment — so what is reported available is what a call would be allowed to
        /// write. The component functions take one argument each and have no entry of their own until one is
        /// built, which is why they are answered here rather than looked up.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        /// <param name="arity">How many arguments the caller is asking about.</param>
        /// <param name="syntaxVersion">The version whose grammar is being read.</param>
        public static bool TakesArity(string name, int arity, XsltVersion syntaxVersion)
        {
            if (s_registry.TryGetValue(name, out Entry? entry))
            {
                return arity >= MinimumArity(name, entry, syntaxVersion) && arity <= entry.Maximum;
            }

            return arity == 1
                && (Array.IndexOf(s_dateTimeComponents, name) >= 0
                    || Array.IndexOf(s_durationComponents, name) >= 0);
        }

        /// <summary>
        /// Creates a call to one of these functions, compiled against a particular version of XPath, or
        /// returns <see langword="null"/> if the name is not one.
        /// </summary>
        /// <param name="name">The function's local name.</param>
        /// <param name="arguments">The argument expressions.</param>
        /// <param name="version">
        /// The version in force where the call was written. Under 2.0 the arguments are checked against the
        /// function's declared parameter types.
        /// </param>
        /// <returns>The compiled call, or <see langword="null"/>.</returns>
        public static Expr? TryCreate(string name, Expr[] arguments, XsltVersion version)
        {
            return Build(name, arguments, version, version);
        }

        private static Expr? Build(
            string name,
            Expr[] arguments,
            XsltVersion version,
            XsltVersion syntaxVersion)
        {
            if (!s_registry.TryGetValue(name, out Entry? entry))
            {
                // The component functions differ only in which part they read and which type they accept, so
                // the name carries the signature rather than the registry doing it two dozen times over.
                if (Array.IndexOf(s_dateTimeComponents, name) >= 0)
                {
                    entry = ComponentEntry(Xpath2Function.ComponentOfDateTime, name);
                }
                else if (Array.IndexOf(s_durationComponents, name) >= 0)
                {
                    entry = ComponentEntry(Xpath2Function.ComponentOfDuration, name);
                }
                else
                {
                    return null;
                }
            }

            // How few arguments a call may have is part of the language, so it follows the syntax version.
            int minimum = MinimumArity(name, entry, syntaxVersion);

            if (arguments.Length < minimum || arguments.Length > entry.Maximum)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"Function '{name}()' expects {minimum} to {entry.Maximum} arguments, but "
                    + $"{arguments.Length} were supplied.");
            }

            // The three date pictures have two signatures rather than a range: the value and the picture, or
            // those and all three of language, calendar and place. Three arguments is not a shorter way of
            // writing five with the rest absent — there is no such function to call.
            if (arguments.Length is 3 or 4 && Array.IndexOf(s_pictureFunctions, name) >= 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"Function '{name}()' takes two arguments or five, and was given {arguments.Length}. "
                    + "The language, the calendar and the place are supplied together or not at all.");
            }

            if (entry.Function == Xpath2Function.Collection)
            {
                // Its own class, shared with fn:uri-collection(): what it reaches is the collection
                // resolver, and it carries the base URI and the package the compiler hands doc().
                return CollectionFunctionExpr.Create(arguments, version, urisOnly: false);
            }

            return new Xpath2FunctionExpr(
                entry.Function,

                // How the arguments are converted is behaviour, so that one follows the version in force.
                CheckedArgumentExpr.Wrap(arguments, entry.Parameters, name, version),
                name,
                syntaxVersion);
        }

        /// <summary>
        /// The functions XPath 3.0 gave a form that takes the context item in place of an argument.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The point of them is that a path can end in one. <c>PRICE/data()</c> reads better than
        /// <c>data(PRICE)</c> and composes where the other does not — in <c>a/b/data()</c> the function
        /// applies to each <c>b</c> as the path reaches it, which is what makes the accessor usable as a step
        /// rather than only as a wrapper. XPath 2.0 has them as one-argument functions only.
        /// </para>
        /// <para>
        /// Kept as a widening of the declared minimum rather than as a second registry entry, because that is
        /// what it is: the same function, reachable with one fewer argument in a later language.
        /// </para>
        /// </remarks>
        private static readonly string[] s_contextFormsAddedIn30 =
            { "data", "document-uri", "nilled", "node-name" };

        /// <summary>How few arguments a function will take, which for a few of them depends on the version.</summary>
        private static int MinimumArity(string name, Entry entry, XsltVersion version)
        {
            return entry.Minimum == 1
                && version.CompareTo(XsltVersion.V30) >= 0
                && Array.IndexOf(s_contextFormsAddedIn30, name) >= 0
                ? 0
                : entry.Minimum;
        }

        private static readonly Dictionary<string, Entry> s_components = new(StringComparer.Ordinal);

        /// <summary>
        /// The entry for a component function, whose one parameter is named by the tail of the function's own
        /// name: <c>hours-from-time</c> takes an <c>xs:time</c>.
        /// </summary>
        private static Entry ComponentEntry(Xpath2Function function, string name)
        {
            lock (s_components)
            {
                if (s_components.TryGetValue(name, out Entry? cached))
                {
                    return cached;
                }

                string type = name[(name.LastIndexOf("-from-", StringComparison.Ordinal) + 6)..];
                Entry entry = new Entry(function, 1, 1, FunctionParameter.Parse($"xs:{type}?"));

                s_components[name] = entry;
                return entry;
            }
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            switch (m_function)
            {
                case Xpath2Function.Empty:
                    return XPathValue.FromBoolean(Items(0, ref context).Count == 0);

                case Xpath2Function.Exists:
                    return XPathValue.FromBoolean(Items(0, ref context).Count != 0);

                case Xpath2Function.Unordered:
                    return m_arguments[0].Evaluate(ref context);

                case Xpath2Function.Reverse:
                {
                    List<XPathValue> items = Items(0, ref context);
                    items.Reverse();
                    return XdmSequence.Concatenate(items);
                }

                case Xpath2Function.DistinctValues:
                {
                    Collation collation = Collation(1, ref context);
                    return DistinctValues(Items(0, ref context), collation);
                }

                case Xpath2Function.Subsequence:
                    return Subsequence(ref context);

                case Xpath2Function.InsertBefore:
                    return InsertBefore(ref context);

                case Xpath2Function.Remove:
                {
                    List<XPathValue> items = Items(0, ref context);
                    int at = (int)Math.Round(m_arguments[1].Evaluate(ref context).ToNumber());

                    if (at >= 1 && at <= items.Count)
                    {
                        items.RemoveAt(at - 1);
                    }

                    return XdmSequence.Concatenate(items);
                }

                case Xpath2Function.IndexOf:
                    return IndexOf(ref context, Collation(2, ref context));

                case Xpath2Function.ZeroOrOne:
                    return Cardinality(ref context, 0, 1, "zero-or-one");

                case Xpath2Function.OneOrMore:
                    return Cardinality(ref context, 1, int.MaxValue, "one-or-more");

                case Xpath2Function.ExactlyOne:
                    return Cardinality(ref context, 1, 1, "exactly-one");

                case Xpath2Function.DeepEqual:
                    return XPathValue.FromBoolean(
                        DeepEqual(Items(0, ref context), Items(1, ref context), Collation(2, ref context)));

                case Xpath2Function.Abs:
                    return Absolute(m_arguments[0].Evaluate(ref context));

                case Xpath2Function.RoundHalfToEven:
                    return RoundHalfToEven(ref context);

                case Xpath2Function.Min:
                case Xpath2Function.Max:
                {
                    Collation collation = Collation(1, ref context);
                    return Extreme(Items(0, ref context), m_function == Xpath2Function.Min, collation);
                }

                case Xpath2Function.Average:
                    return Average(Items(0, ref context));

                case Xpath2Function.UpperCase:
                    return XPathValue.FromString(Text(0, ref context).ToUpperInvariant());

                case Xpath2Function.LowerCase:
                    return XPathValue.FromString(Text(0, ref context).ToLowerInvariant());

                case Xpath2Function.EndsWith:
                    return XPathValue.FromBoolean(
                        Collation(2, ref context).EndsWith(Text(0, ref context), Text(1, ref context)));

                case Xpath2Function.StringJoin:
                    return StringJoin(ref context);

                case Xpath2Function.Compare:
                {
                    Collation collation = Collation(2, ref context);

                    XPathValue first = m_arguments[0].Evaluate(ref context);
                    XPathValue second = m_arguments[1].Evaluate(ref context);

                    // Declared xs:string?, xs:string? -> xs:integer?, so nothing in is nothing out: there is
                    // no string to be before or after the other one.
                    if (IsEmptySequence(first) || IsEmptySequence(second))
                    {
                        return XPathValue.FromSequence(XdmSequence.Empty);
                    }

                    return XPathValue.FromInteger(
                        collation.Compare(first.ToStringValue(), second.ToStringValue()));
                }

                case Xpath2Function.CodepointsToString:
                    return CodepointsToString(Items(0, ref context));

                case Xpath2Function.StringToCodepoints:
                    return StringToCodepoints(Text(0, ref context));

                case Xpath2Function.Matches:
                    return XPathValue.FromBoolean(
                        Pattern(ref context, 1, 2).IsMatch(Text(0, ref context)));

                case Xpath2Function.Replace:
                {
                    Regex pattern = Pattern(ref context, 1, 3, "replace");
                    string written = Text(2, ref context);

                    // The q flag makes the replacement literal as well as the pattern, which is the whole
                    // point of asking for a replacement that means what it says: '$1' is a dollar and a one,
                    // and a lone backslash is a backslash rather than FORX0004. .NET reads only the dollar,
                    // and doubling it is how that class spells one meaning itself.
                    string replacement = m_arguments.Length > 3 && Text(3, ref context).Contains('q')
                        ? written.Replace("$", "$$", StringComparison.Ordinal)
                        : RegexTranslator.TranslateReplacement(written, pattern);

                    return XPathValue.FromString(pattern.Replace(Text(0, ref context), replacement));
                }

                case Xpath2Function.Tokenize:
                    return Tokenize(ref context);

                case Xpath2Function.CurrentDateTime:
                    return CurrentDateTime(ref context);

                case Xpath2Function.ImplicitTimezone:
                    return XPathValue.FromDuration(new XdmDuration(
                        0,
                        (decimal)XdmDateTime.ImplicitTimezone.TotalSeconds,
                        XdmTypeCode.DayTimeDuration));

                case Xpath2Function.CombineDateAndTime:
                    return CombineDateAndTime(ref context);

                case Xpath2Function.ComponentOfDuration:
                    return ComponentOfDuration(ref context);

                case Xpath2Function.Doc:
                {
                    // No URI names no document. The argument is declared xs:string?, so the empty sequence
                    // is a value it may be given and the answer is the empty sequence — where reading its
                    // string value instead made doc(()) the same call as doc(''), which is the module the
                    // expression was written in.
                    XPathValue uri = m_arguments[0].Evaluate(ref context);

                    return IsEmptySequence(uri)
                        ? XPathValue.FromSequence(XdmSequence.Empty)
                        : XPathValue.FromNodeSet(NodeSet.Singleton(
                            LoadDocument(XdmSequence.StringValueOf(uri), ref context), Model.XdmTree.RootNode));
                }

                case Xpath2Function.DocAvailable:
                {
                    XPathValue asked = m_arguments[0].Evaluate(ref context);

                    return XPathValue.FromBoolean(
                        !IsEmptySequence(asked)
                        && IsDocumentAvailable(XdmSequence.StringValueOf(asked), ref context));
                }

                case Xpath2Function.UnparsedText:
                {
                    // Declared xs:string?, so nothing in is nothing out: fn:unparsed-text(()) is the
                    // empty sequence and reads nothing, which is also what fn:json-doc(()) rests on.
                    XPathValue href = m_arguments[0].Evaluate(ref context);

                    return IsEmptySequence(href)
                        ? href
                        : XPathValue.FromString(ReadUnparsedText(ref context));
                }

                case Xpath2Function.UnparsedTextAvailable:
                    return XPathValue.FromBoolean(IsUnparsedTextAvailable(ref context));

                case Xpath2Function.BaseUri:
                case Xpath2Function.DocumentUri:
                    return UriOfNode(ref context);

                case Xpath2Function.StaticBaseUri:
                    // Where the expression was written, which is not the same as where the
                    // transformation was started: a module read from elsewhere, or an xml:base, moves
                    // the first and leaves the second. The transformation's own is the fallback for an
                    // expression that came with no base of its own.
                    return UriOrEmpty(StaticBaseUri ?? context.Runtime?.BaseUri);

                case Xpath2Function.ResolveUri:
                    return ResolveUri(ref context);

                case Xpath2Function.Data:
                    return XdmSequence.Concatenate(Atomize(ItemsOrContext(0, ref context)));

                case Xpath2Function.Nilled:
                {
                    // Only an element is ever nilled, and only one that validation found so: anything else
                    // yields the empty sequence, and an element of a tree nothing validated answers false.
                    List<XPathValue> items = ItemsOrContext(0, ref context);
                    return items.Count == 1 && TryNode(items[0], out Model.XdmTree? nilledTree, out int nilledNode)
                        && nilledTree!.KindOf(nilledNode) == Model.NodeKind.Element
                        ? XPathValue.FromBoolean(nilledTree.IsNilled(nilledNode))
                        : XPathValue.FromSequence(XdmSequence.Empty);
                }

                case Xpath2Function.Root:
                    return Root(ref context);

                case Xpath2Function.Error:
                    throw RaiseError(ref context);

                case Xpath2Function.Trace:
                {
                    XPathValue value = m_arguments[0].Evaluate(ref context);
                    string label = Text(1, ref context);

                    context.Runtime?.WriteMessage(
                        label.Length == 0 ? value.ToStringValue() : $"{label}: {value.ToStringValue()}");

                    return value;
                }

                case Xpath2Function.InScopePrefixes:
                    return InScopePrefixes(ref context);

                case Xpath2Function.NamespaceUriForPrefix:
                    return NamespaceUriForPrefix(ref context);

                case Xpath2Function.EncodeForUri:
                    return XPathValue.FromString(EscapeUri(Text(0, ref context), Reserved: true, Html: false));

                case Xpath2Function.IriToUri:
                    return XPathValue.FromString(EscapeUri(Text(0, ref context), Reserved: false, Html: false));

                case Xpath2Function.EscapeHtmlUri:
                    return XPathValue.FromString(EscapeUri(Text(0, ref context), Reserved: false, Html: true));

                case Xpath2Function.CodepointEqual:
                {
                    XPathValue left = m_arguments[0].Evaluate(ref context);
                    XPathValue right = m_arguments[1].Evaluate(ref context);

                    return IsEmptySequence(left) || IsEmptySequence(right)
                        ? XPathValue.FromSequence(XdmSequence.Empty)
                        : XPathValue.FromBoolean(string.Equals(
                            left.ToStringValue(), right.ToStringValue(), StringComparison.Ordinal));
                }

                case Xpath2Function.NormalizeUnicode:
                    return XPathValue.FromString(NormalizeUnicode(ref context));

                case Xpath2Function.DefaultCollation:
                    // What a default-collation in scope put there, and the code point collation where
                    // nothing did — which is the fallback the specification names.
                    return XPathValue.FromString(m_defaultCollation ?? CodepointCollation);

                case Xpath2Function.FormatDateTime:
                {
                    XPathValue value = m_arguments[0].Evaluate(ref context);

                    // The place is read and not yet honoured: naming one asks for the value to be shown in
                    // that civil timezone, which wants an Olson database this engine is not wired to. It is
                    // evaluated all the same, because an argument is of its declared type whether or not the
                    // function has a use for it — format-date(…, 'en', (), 5) is XPTY0004 about the 5, and
                    // an argument nobody evaluates is an argument nobody type-checks.
                    if (m_arguments.Length > 4)
                    {
                        _ = OptionalText(4, ref context);
                    }

                    // An empty value formats to nothing rather than to an error, so a picture can be applied
                    // to something optional without guarding every use.
                    return IsEmptySequence(value)
                        ? XPathValue.FromSequence(XdmSequence.Empty)
                        : XPathValue.FromString(DateFormatting.Format(
                            value.AsDateTime(),
                            Text(1, ref context),
                            m_name,
                            OptionalText(2, ref context),
                            OptionalText(3, ref context),
                            roundsFraction: m_version < XsltVersion.V30));
                }

                case Xpath2Function.AdjustToTimezone:
                    return AdjustToTimezone(ref context);

                case Xpath2Function.NodeName:
                    return NodeName(ref context);

                case Xpath2Function.MakeQName:
                {
                    string uri = Text(0, ref context);
                    string name = Text(1, ref context);

                    // FOCA0002 rather than the FORG0001 a cast would raise: nothing is being cast here. The
                    // argument is declared an xs:string and is one, and the objection is that the text does
                    // not spell a name — which is what 'invalid lexical value' names.
                    if (!XdmQName.TrySplit(name, out string prefix, out string localName))
                    {
                        throw XsltErrors.Error(XsltErrorCode.FOCA0002, $"'{name}' is not a valid xs:QName.");
                    }

                    // A prefix stands for a namespace, so there has to be one for it to stand for.
                    if (prefix.Length != 0 && uri.Length == 0)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.FOCA0002,
                            $"'{name}' carries the prefix '{prefix}' but no namespace was given for it to "
                            + "stand for.");
                    }

                    return XPathValue.FromQName(new XdmQName(prefix, uri, localName));
                }

                case Xpath2Function.ResolveQName:
                    return ResolveQName(ref context);

                case Xpath2Function.PartOfQName:
                {
                    XPathValue argument = m_arguments[0].Evaluate(ref context);
                    if (IsEmptySequence(argument))
                    {
                        return XPathValue.FromSequence(XdmSequence.Empty);
                    }

                    XdmQName name = argument.AsQName();

                    return m_name switch
                    {
                        "local-name-from-QName" => XPathValue.FromString(name.LocalName),
                        "namespace-uri-from-QName" => XPathValue.FromAnyUri(name.NamespaceUri),

                        // A name written without a prefix has none, and the empty sequence is how that is
                        // said — an empty string would be a prefix that happens to be empty.
                        _ => name.Prefix.Length == 0
                            ? XPathValue.FromSequence(XdmSequence.Empty)
                            : XPathValue.FromString(name.Prefix),
                    };
                }

                default:
                    return ComponentOfDateTime(ref context);
            }
        }

        // ---- Documents, text and URIs --------------------------------------------------------------------

        /// <summary>The collation URI that compares by code point, which every processor must provide.</summary>
        internal const string CodepointCollation = "http://www.w3.org/2005/xpath-functions/collation/codepoint";

        /// <summary>
        /// Finds the collation an argument names, or the default one where none was supplied.
        /// </summary>
        /// <param name="index">Which argument carries the collation, if it was supplied at all.</param>
        /// <param name="context">The evaluation context.</param>
        private Collation Collation(int index, ref DynamicContext context)
        {
            return m_arguments.Length > index
                ? XPath.Collation.Resolve(Text(index, ref context), ref context)
                : m_default ?? XPath.Collation.Codepoint;
        }

        /// <summary>
        /// The base URI where the call was written, which a relative reference resolves against.
        /// </summary>
        /// <remarks>
        /// Set by the stylesheet compiler, which is the only thing that knows it — the library builds a
        /// call from values alone. Without it a relative reference resolves against wherever the
        /// transformation started, which is the principal stylesheet: not the module the call stands in,
        /// and not what an <c>xml:base</c> may have made of it.
        /// </remarks>
        internal string? StaticBaseUri { get; set; }

        /// <summary>
        /// The package the call is written in, whose whitespace declarations strip what it reads.
        /// </summary>
        /// <remarks>
        /// Set by the stylesheet compiler, as the base URI is, and for the same reason: the library builds
        /// a call from values alone and has no way to know where it was written. Whitespace stripping is
        /// local to a package (XSLT 3.0 §3.6.5), so what <c>doc()</c> gives back depends on it.
        /// </remarks>
        internal int Package { get; set; }

        private Model.XdmTree LoadDocument(string href, ref DynamicContext context)
        {
            if (context.Runtime is XsltRuntime runtime)
            {
                return runtime.LoadDocument(href, StaticBaseUri ?? runtime.BaseUri, Package);
            }

            // A static expression — a use-when, a shadow attribute — runs before any transformation, and
            // what it may read is what the compiler lends it: the module it stands in, for doc('').
            return context.DocumentLoader is Func<string, string?, Model.XdmTree> load
                ? load(href, null)
                : throw new XsltException("doc() can only be called while a transformation is running.");
        }

        private bool IsDocumentAvailable(string href, ref DynamicContext context)
        {
            // Availability is the whole question the function asks, so every way of failing to load answers
            // it rather than being reported: no resolver, nothing there, or something there that is not XML.
            try
            {
                LoadDocument(href, ref context);
                return true;
            }
            catch (XsltException)
            {
                return false;
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }

        private string ReadUnparsedText(ref DynamicContext context)
        {
            string href = Text(0, ref context);
            string? encoding = m_arguments.Length > 1 ? Text(1, ref context) : null;

            // A fragment names part of a document, and there is no part of a text file to name. Asked
            // here rather than only of the resolver, so that a lent loader is not left to know it.
            if (href.IndexOf('#') >= 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOUT1170,
                    $"unparsed-text() was given '{href}', which carries a fragment identifier.");
            }

            if (context.Runtime is XsltRuntime runtime)
            {
                return RequireXmlCharacters(runtime.LoadText(href, encoding));
            }

            // No transformation behind this one, which is where a caller evaluating an expression on
            // its own may lend a loader, as it may for doc().
            return context.TextLoader is Func<string, string?, string> load
                ? RequireXmlCharacters(ReadThrough(load, href, encoding))
                : throw new XsltException(
                    "unparsed-text() can only be called while a transformation is running, or where "
                    + "the caller supplies a text loader.");
        }

        /// <summary>Reads through a loader the caller lent, in this function's own terms.</summary>
        /// <remarks>
        /// A resource that will not open is <c>FOUT1170</c> whoever went looking for it, so whatever
        /// the loader raises becomes that. It matters more than it looks:
        /// <c>fn:unparsed-text-available()</c> is defined as this function not raising, and answers by
        /// catching what it does — so a loader failing in its own words is not a false answer but no
        /// answer at all, the exception going past the catch and out of the transformation.
        /// </remarks>
        /// <param name="load">The loader.</param>
        /// <param name="href">The reference as written.</param>
        /// <param name="encoding">The encoding the call named, or null for none.</param>
        private static string ReadThrough(
            Func<string, string?, string> load, string href, string? encoding)
        {
            try
            {
                return load(href, encoding);
            }
            catch (Exception failed) when (failed is not XsltException)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOUT1170,
                    $"unparsed-text() could not read '{href}': {failed.Message}",
                    failed);
            }
        }

        /// <summary>
        /// Refuses text holding a character XML does not permit, which is what a resource read as text may
        /// turn out to contain.
        /// </summary>
        /// <remarks>
        /// <c>fn:unparsed-text()</c> hands back a string, and a string in this data model holds XML
        /// characters and nothing else — so a file with a NUL in it cannot be read into one. Answering with
        /// it anyway produces a result that cannot be serialized, which is a failure at the far end of the
        /// transformation about something that was wrong at the near end. <c>FOUT1190</c> says so where it
        /// happened, and is what <c>unparsed-text-lines()</c> raises too, being built on this; the
        /// <c>-available</c> forms answer false, having always answered that for anything this raises.
        /// </remarks>
        /// <param name="text">The text as it was read.</param>
        /// <exception cref="XsltException">A character in it is one XML does not permit.</exception>
        private static string RequireXmlCharacters(string text)
        {
            for (int at = 0; at < text.Length; at++)
            {
                // A surrogate pair is one character above the basic plane and is allowed; half of one is an
                // encoding artefact and is not, which falls out of asking about the code point either way.
                int codepoint = char.IsHighSurrogate(text[at])
                    && at + 1 < text.Length
                    && char.IsLowSurrogate(text[at + 1])
                        ? char.ConvertToUtf32(text[at], text[++at])
                        : text[at];

                if (!IsXmlCharacter(codepoint))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOUT1190,
                        $"The text holds the character U+{codepoint:X4}, which XML does not permit, so there "
                        + "is no string for unparsed-text() to answer with.");
                }
            }

            return text;
        }

        private bool IsUnparsedTextAvailable(ref DynamicContext context)
        {
            try
            {
                ReadUnparsedText(ref context);
                return true;
            }
            catch (XsltException)
            {
                return false;
            }
        }

        /// <summary>
        /// The URI a node's document came from, which is what both <c>base-uri</c> and <c>document-uri</c>
        /// report here.
        /// </summary>
        /// <remarks>
        /// The two differ only over <c>xml:base</c>, which this engine does not track: with no base declared
        /// anywhere, a node's base URI is its document's. Where the document has no URI — a result tree
        /// fragment, or input handed over as text — both yield the empty sequence, as they must.
        /// </remarks>
        /// <summary>
        /// The root of the tree a node is in, which for a whole document is its document node.
        /// </summary>
        /// <remarks>
        /// Cheap here, and worth saying why: nodes are numbered in preorder within one tree, so the root is
        /// node zero of whichever tree the argument came from. There is no walk up the ancestors.
        /// </remarks>
        private XPathValue Root(ref DynamicContext context)
        {
            // Walked rather than answered with node zero, because not every tree here is rooted at a document
            // node: a sequence constructor with an as declaration builds parentless ones, and the root of
            // such a node is the node itself.
            if (m_arguments.Length == 0)
            {
                int self = context.RequireContextNode("fn:root()");
                return XPathValue.FromNodeSet(
                    NodeSet.Singleton(context.Tree, context.Tree.RootOf(self)));
            }

            List<XPathValue> items = Items(0, ref context);

            if (items.Count == 0)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            if (!TryNode(items[0], out Model.XdmTree? tree, out int node))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, "fn:root() takes a node, and was given a value.");
            }

            return XPathValue.FromNodeSet(NodeSet.Singleton(tree!, tree!.RootOf(node)));
        }

        private XPathValue UriOfNode(ref DynamicContext context)
        {
            Model.XdmTree tree;
            int node;

            if (m_arguments.Length == 0)
            {
                // With no argument these read the context node, so it has to be one — the URI of an integer
                // is not the empty sequence, it is a question that does not arise.
                node = context.RequireContextNode($"fn:{m_name}()");
                tree = context.Tree;
            }
            else
            {
                List<XPathValue> items = Items(0, ref context);
                if (items.Count == 0 || !TryNode(items[0], out Model.XdmTree? found, out node))
                {
                    return XPathValue.FromSequence(XdmSequence.Empty);
                }

                tree = found!;
            }

            if (m_function == Xpath2Function.DocumentUri)
            {
                // document-uri() is where the document came from and nothing else. A tree the stylesheet
                // built came from nowhere, and a node that is not the document itself has no document URI
                // of its own — an element is in a document rather than being one, so the answer for one is
                // the empty sequence and not the URI of what encloses it.
                return tree.KindOf(node) == Model.NodeKind.Root
                    ? UriOrEmpty(context.Runtime?.UriOf(tree) ?? tree.DocumentUri)
                    : XPathValue.FromSequence(XdmSequence.Empty);
            }

            // base-uri() is what a relative reference written at this node resolves against, which is the
            // stylesheet for a built tree and which xml:base is there to move.
            return UriOrEmpty(
                BaseUriOf(tree, node, context.Runtime?.BaseUriOf(tree) ?? tree.DocumentUri ?? tree.BaseUri));
        }

        /// <summary>
        /// The base URI of a node: the <c>xml:base</c> declarations above it, resolved outwards and finally
        /// against the document's own URI.
        /// </summary>
        /// <remarks>
        /// <c>xml:base</c> is how a document assembled from several places says where each part came from,
        /// so a relative reference inside a part resolves against where that part was rather than against
        /// the file it now lives in. The declarations are collected innermost-first and applied outermost-
        /// first, since each is itself relative to the one above it.
        /// </remarks>
        /// <param name="tree">The tree the node is in.</param>
        /// <param name="node">The node.</param>
        /// <param name="document">The document's own URI, or <see langword="null"/> if it has none.</param>
        internal static string? BaseUriOf(Model.XdmTree tree, int node, string? document)
        {
            // A namespace node has no base URI at all. It is not a place in the document that a relative
            // reference could be written, so the data model gives it the empty sequence rather than the
            // base URI of the element it is attached to.
            if (tree.KindOf(node) == Model.NodeKind.Namespace)
            {
                return null;
            }

            if (tree.KindOf(node) is not (Model.NodeKind.Element or Model.NodeKind.Root)
                && tree.ParentOf(node) < 0)
            {
                // An attribute, comment, text or processing-instruction node with no parent is relative to
                // nothing: it never belonged to a document, and there is no element above it to ask.
                return null;
            }

            List<string>? declared = null;

            for (int current = node; current >= 0; current = tree.ParentOf(current))
            {
                if (tree.KindOf(current) == Model.NodeKind.Element && XmlBaseOf(tree, current) is string written)
                {
                    (declared ??= new List<string>()).Add(written);
                }

                // A node parsed out of an external entity has the entity's URI for its base rather than
                // the document's, and nothing above it in the tree says otherwise.
                if (tree.EntityBaseOf(current) is string entityBase)
                {
                    document = entityBase;
                    break;
                }
            }

            if (declared is null)
            {
                return document;
            }

            string? resolved = document;

            for (int i = declared.Count - 1; i >= 0; i--)
            {
                if (!UriReference.TryParse(declared[i], out UriReference reference))
                {
                    return null;
                }

                if (reference.Scheme is not null)
                {
                    resolved = UriReference.Resolve(reference, reference);
                }
                else if (resolved is not null
                    && UriReference.TryParse(resolved, out UriReference root) && root.Scheme is not null)
                {
                    resolved = UriReference.Resolve(reference, root);
                }
                else
                {
                    // Nothing absolute to resolve against, so the relative reference stands as it is: a base
                    // URI that cannot be made absolute is still the best answer there is.
                    resolved = declared[i];
                }
            }

            return resolved;
        }

        /// <summary>The <c>xml:base</c> written on an element, if it has one.</summary>
        /// <remarks>
        /// Read by comparing names rather than by looking a fingerprint up, because looking one up allocates
        /// a fingerprint for a name the document may not contain — which would mean writing to a tree's name
        /// table while a transformation reads it.
        /// </remarks>
        internal static string? XmlBaseOf(Model.XdmTree tree, int element)
        {
            int count = tree.AttributeCountOf(element);

            for (int i = 0; i < count; i++)
            {
                int attribute = tree.AttributeAt(element, i);
                int fingerprint = tree.FingerprintOf(attribute);

                if (tree.NameTable.GetLocalName(fingerprint) == "base"
                    && tree.NameTable.GetNamespaceUri(fingerprint) == Model.XdmTree.XmlNamespaceUri)
                {
                    return tree.StringValueOf(attribute);
                }
            }

            return null;
        }

        private XPathValue ResolveUri(ref DynamicContext context)
        {
            List<XPathValue> given = Items(0, ref context);

            // Nothing to resolve resolves to nothing, and asks nothing of the base either: a base that could
            // not be used is only an error once something needs to be resolved against it.
            if (given.Count == 0)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            string relative = given[0].ToStringValue();

            if (!UriReference.TryParse(relative, out UriReference reference))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FORG0002, $"'{relative}' is not a URI reference.");
            }

            if (reference.Scheme is not null)
            {
                // A reference naming a scheme is already the answer, so it comes back as it was written —
                // the same host in the same case, and no path invented for an authority that had none.
                return XPathValue.FromAnyUri(UriReference.Resolve(reference, reference));
            }

            string? baseUri = m_arguments.Length > 1 ? Text(1, ref context) : context.Runtime?.BaseUri;

            if (baseUri is null)
            {
                // Nothing to resolve against, which the specification makes an error rather than a shrug: the
                // answer would otherwise depend on where the transformation happened to be run.
                throw XsltErrors.Error(
                    XsltErrorCode.FONS0005,
                    $"resolve-uri() cannot resolve '{relative}' because no base URI is known. Set "
                    + "XsltOptions.BaseUri, or pass the base as the second argument.");
            }

            // A base has to name a scheme, and may carry no fragment: RFC 3986 resolves against a document,
            // and a fragment names a place inside one rather than a document to be relative to.
            if (!UriReference.TryParse(baseUri, out UriReference root)
                || root.Scheme is null
                || root.Fragment is not null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FORG0002,
                    $"'{baseUri}' cannot be resolved against. A base URI names a scheme and carries no "
                    + "fragment.");
            }

            return XPathValue.FromAnyUri(UriReference.Resolve(reference, root));
        }

        /// <summary>
        /// Returns a URI, or the empty sequence where there is none to return.
        /// </summary>
        /// <remarks>
        /// An <c>xs:anyURI</c> rather than an <c>xs:string</c>, which is what these three functions are
        /// declared to return. It reads as text wherever text is wanted, and differs where the type is the
        /// question: <c>base-uri(.) cast as xs:double</c> is a type error rather than a reading of the text.
        /// </remarks>
        private static XPathValue UriOrEmpty(string? value)
        {
            return value is null
                ? XPathValue.FromSequence(XdmSequence.Empty)
                : XPathValue.FromAnyUri(value);
        }

        private static bool IsElement(XPathValue value)
        {
            return TryNode(value, out Model.XdmTree? tree, out int node)
                && tree!.KindOf(node) == Model.NodeKind.Element;
        }

        /// <summary>
        /// Reads the single node an item holds, whichever way it holds it.
        /// </summary>
        /// <remarks>
        /// One node is two shapes here: an item taken out of a sequence carries the node directly, while an
        /// expression that produced a set carries a set of one. Both are the same thing to a function that
        /// asked for a node.
        /// </remarks>
        private static bool TryNode(XPathValue value, out Model.XdmTree? tree, out int node)
        {
            if (value.Kind == XPathValueKind.Node)
            {
                tree = value.NodeTree;
                node = value.NodeId;
                return true;
            }

            if (value.Kind == XPathValueKind.NodeSet)
            {
                NodeSet nodes = value.AsNodeSet();
                if (nodes.Count != 0)
                {
                    tree = nodes.TreeAt(0);
                    node = nodes[0];
                    return true;
                }
            }

            tree = null;
            node = -1;
            return false;
        }

        private static List<XPathValue> Atomize(List<XPathValue> items)
        {
            return XdmSequence.Atomize(items);
        }

        /// <summary>
        /// Raises the error a stylesheet asked for, carrying the name it named.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The name is an <c>xs:QName</c>, and what reaches <see cref="XsltException.Code"/> is its local
        /// part: <c>fn:error(fn:QName('http://www.w3.org/2005/xqt-errors', 'err:FOCH0004'))</c> reports
        /// <c>FOCH0004</c>, the same string a caller would see from the engine raising that error itself. So
        /// a stylesheet can raise one of the specification's errors, or one of its own, and a caller catching
        /// by code cannot tell — which is the point.
        /// </para>
        /// <para>
        /// This is the one code this engine does not choose, and the one place a code is built from a string
        /// rather than from <see cref="XsltErrorCode"/>. Calling <c>fn:error()</c> with no name at all is the
        /// exception, since then the choice is the specification's: <c>FOER0000</c>.
        /// </para>
        /// </remarks>
        private XsltException RaiseError(ref DynamicContext context)
        {
            XdmQName? name = null;

            if (m_arguments.Length > 0)
            {
                XPathValue supplied = m_arguments[0].Evaluate(ref context);

                // The two-argument form accepts an empty name, which means the default one.
                if (!IsEmptySequence(supplied))
                {
                    name = supplied.AsQName();
                }
            }

            string description = m_arguments.Length > 1
                ? Text(1, ref context)
                : "An error was raised by a call to error().";

            // The third argument is the error object, which an xsl:catch reads as $err:value. It is the
            // caller's to shape: anything at all, from a code of its own to the node that was being
            // processed when the stylesheet decided to give up on it.
            XPathValue? value = m_arguments.Length > 2
                ? m_arguments[2].Evaluate(ref context)
                : null;

            if (name is null)
            {
                return new XsltException(XsltErrorCode.FOER0000, $"FOER0000: {description}") { Value = value };
            }

            // The namespace is shown only when it is not the one the specification's own errors live in,
            // where it would be noise on every message.
            string written = name.Value.NamespaceUri.Length == 0
                || name.Value.NamespaceUri == ErrorNamespace
                    ? name.Value.LocalName
                    : $"{{{name.Value.NamespaceUri}}}{name.Value.LocalName}";

            return new XsltException($"{written}: {description}", name.Value.LocalName)
            {
                CodeNamespace = name.Value.NamespaceUri,
                Value = value,
            };
        }

        /// <summary>The namespace the errors named by XPath and its function library live in.</summary>
        private const string ErrorNamespace = "http://www.w3.org/2005/xqt-errors";

        private XPathValue InScopePrefixes(ref DynamicContext context)
        {
            List<XPathValue> prefixes = new List<XPathValue>();

            if (TryElementArgument(0, ref context, out Model.XdmTree? tree, out int element))
            {
                foreach ((string prefix, string _) in tree!.InScopeNamespacesOf(element))
                {
                    prefixes.Add(XPathValue.FromString(prefix));
                }

                // Every element has xml in scope whether or not it is declared anywhere.
                prefixes.Add(XPathValue.FromString("xml"));
            }

            return XdmSequence.Concatenate(prefixes);
        }

        private XPathValue NamespaceUriForPrefix(ref DynamicContext context)
        {
            string prefix = Text(0, ref context);

            if (!TryElementArgument(1, ref context, out Model.XdmTree? tree, out int element))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            foreach ((string declared, string uri) in tree!.InScopeNamespacesOf(element))
            {
                if (string.Equals(declared, prefix, StringComparison.Ordinal))
                {
                    return XPathValue.FromAnyUri(uri);
                }
            }

            return prefix == "xml"
                ? XPathValue.FromAnyUri("http://www.w3.org/XML/1998/namespace")
                : XPathValue.FromSequence(XdmSequence.Empty);
        }

        private bool TryElementArgument(
            int index,
            ref DynamicContext context,
            out Model.XdmTree? tree,
            out int element)
        {
            tree = null;
            element = -1;

            List<XPathValue> items = Items(index, ref context);

            return items.Count != 0
                && TryNode(items[0], out tree, out element)
                && tree!.KindOf(element) == Model.NodeKind.Element;
        }

        /// <summary>
        /// Percent-encodes a URI, in the three flavours the specification distinguishes.
        /// </summary>
        /// <remarks>
        /// They differ only in what is left alone. <c>encode-for-uri</c> escapes everything that is not
        /// unreserved, because its argument is one component of a URI rather than a URI; the other two leave
        /// the reserved characters in place, because their argument already is one, and differ over whether
        /// the double quote is escaped — which <c>escape-html-uri</c> does not do, since an HTML attribute is
        /// where its result goes.
        /// </remarks>
        private static string EscapeUri(string text, bool Reserved, bool Html)
        {
            StringBuilder builder = new StringBuilder(text.Length);

            foreach (byte unit in Encoding.UTF8.GetBytes(text))
            {
                char character = (char)unit;

                bool keep = char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_' or '.' or '~'
                    || (!Reserved && !Html && "!*'();:@&=+$,/?#[]%".IndexOf(character) >= 0)

                    // escape-html-uri keeps every printable US-ASCII octet, 32 to 126 inclusive — the space
                    // among them, since what it escapes for is a browser's address bar and not a URI parser.
                    || (Html && unit is >= 0x20 and <= 0x7E);

                if (keep)
                {
                    builder.Append(character);
                }
                else
                {
                    builder.Append('%').Append(unit.ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            return builder.ToString();
        }

        private string NormalizeUnicode(ref DynamicContext context)
        {
            string text = Text(0, ref context);
            string form = m_arguments.Length > 1 ? Text(1, ref context).Trim().ToUpperInvariant() : "NFC";

            return form switch
            {
                "" => text,
                "NFC" => text.Normalize(NormalizationForm.FormC),
                "NFD" => text.Normalize(NormalizationForm.FormD),
                "NFKC" => text.Normalize(NormalizationForm.FormKC),
                "NFKD" => text.Normalize(NormalizationForm.FormKD),
                _ => throw XsltErrors.Error(
                    XsltErrorCode.FOCH0003,
                    $"normalize-unicode() was asked for the '{form}' normalization form, which is not one it "
                    + "supports."),
            };
        }

        // ---- Dates, times and durations ------------------------------------------------------------------

        /// <summary>
        /// The current moment, as whichever of the three types was asked for.
        /// </summary>
        /// <remarks>
        /// All three are required to agree within a transformation — <c>current-date()</c> must be the date
        /// part of the same instant <c>current-dateTime()</c> reports — which is why they share one reading of
        /// the clock rather than each taking their own.
        /// </remarks>
        private XPathValue CurrentDateTime(ref DynamicContext context)
        {
            // Read once per evaluation and kept, which is what makes the three agree with one another and
            // with themselves: 'current-time() eq current-time()' has to be true, and asking the operating
            // system twice cannot promise that.
            DateTimeOffset now = context.ReadClock().Now;

            XdmTypeCode type = m_name switch
            {
                "current-date" => XdmTypeCode.Date,
                "current-time" => XdmTypeCode.Time,
                _ => XdmTypeCode.DateTime,
            };

            string lexical = type switch
            {
                XdmTypeCode.Date => now.ToString("yyyy-MM-ddZ", CultureInfo.InvariantCulture),
                XdmTypeCode.Time => now.ToString("HH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
                _ => now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
            };

            return XdmDateTime.TryParse(lexical, type, out XdmDateTime value)
                ? XPathValue.FromDateTime(value)
                : throw new XsltException("The current time could not be represented.");
        }

        private XPathValue CombineDateAndTime(ref DynamicContext context)
        {
            XPathValue first = m_arguments[0].Evaluate(ref context);
            XPathValue second = m_arguments[1].Evaluate(ref context);

            // Both halves are declared optional, and a moment made of half a moment is no moment: nothing
            // in, nothing out.
            if (IsEmptySequence(first) || IsEmptySequence(second))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            XdmDateTime date = first.AsDateTime();
            XdmDateTime time = second.AsDateTime();

            // The two must agree about their timezone, or there is no one instant they describe.
            if (date.Offset is TimeSpan a && time.Offset is TimeSpan b && a != b)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FORG0008,
                    $"'{date}' and '{time}' are in different timezones, so they name no single moment.");
            }

            // Written out and read back rather than assembled from parts, so that the year is spelled the
            // one way it is spelled everywhere: a date before the common era carries its sign here too.
            string lexical = date.WithoutTimezone().As(XdmTypeCode.Date)
                + "T" + time.WithoutTimezone().As(XdmTypeCode.Time)
                + Zone(date.Offset ?? time.Offset);

            return XdmDateTime.TryParse(lexical, XdmTypeCode.DateTime, out XdmDateTime combined)
                ? XPathValue.FromDateTime(combined)
                : throw XsltErrors.Error(XsltErrorCode.FORG0001, $"'{lexical}' is not a valid xs:dateTime.");
        }

        private static string Zone(TimeSpan? offset)
        {
            if (offset is not TimeSpan value)
            {
                return string.Empty;
            }

            return value == TimeSpan.Zero
                ? "Z"
                : (value < TimeSpan.Zero ? "-" : "+")
                    + value.Duration().ToString("hh\\:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>Reads one component out of a date, a time or a date and time.</summary>
        /// <summary>
        /// Moves a value into another timezone, or takes it out of one.
        /// </summary>
        /// <remarks>
        /// With no second argument the implicit timezone is used; with an empty second argument the timezone
        /// is removed, and that is the one case where the local time is left alone rather than shifted — the
        /// value stops denoting an instant instead of denoting a different one.
        /// </remarks>
        private XPathValue AdjustToTimezone(ref DynamicContext context)
        {
            XPathValue argument = m_arguments[0].Evaluate(ref context);

            if (IsEmptySequence(argument))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            XdmDateTime value = argument.AsDateTime();

            if (m_arguments.Length == 1)
            {
                return XPathValue.FromDateTime(value.WithTimezone(XdmDateTime.ImplicitTimezone));
            }

            XPathValue timezone = m_arguments[1].Evaluate(ref context);
            if (IsEmptySequence(timezone))
            {
                return XPathValue.FromDateTime(value.WithoutTimezone());
            }

            XdmDuration offset = timezone.AsDuration();
            TimeSpan wanted = TimeSpan.FromSeconds((double)offset.Seconds);

            if (offset.Months != 0 || wanted.Ticks % TimeSpan.TicksPerMinute != 0
                || wanted > TimeSpan.FromHours(14) || wanted < TimeSpan.FromHours(-14))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FODT0003,
                    $"'{offset}' is not a timezone: a timezone is a whole number of minutes, no more than 14 "
                    + "hours from UTC.");
            }

            return XPathValue.FromDateTime(value.WithTimezone(wanted));
        }

        /// <summary>
        /// The name of a node, as an <c>xs:QName</c>.
        /// </summary>
        /// <remarks>
        /// Only the kinds that have a name answer. A text node or a comment has none, and the empty sequence
        /// is the answer rather than an empty name.
        /// </remarks>
        private XPathValue NodeName(ref DynamicContext context)
        {
            List<XPathValue> items = ItemsOrContext(0, ref context);

            if (items.Count == 0 || !TryNode(items[0], out Model.XdmTree? tree, out int node))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            Model.NodeKind kind = tree!.KindOf(node);

            if (kind is not (Model.NodeKind.Element or Model.NodeKind.Attribute
                or Model.NodeKind.ProcessingInstruction or Model.NodeKind.Namespace))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            int nameCode = tree.NameCodeOf(node);
            int fingerprint = tree.FingerprintOf(node);
            string localName = tree.NameTable.GetLocalName(fingerprint);

            // A namespace node is named by its prefix, and the default namespace's node has none (XDM §6.4).
            if (localName.Length == 0)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            return XPathValue.FromQName(new XdmQName(
                tree.NameTable.GetPrefix(nameCode),
                tree.NameTable.GetNamespaceUri(fingerprint),
                localName));
        }

        /// <summary>
        /// Resolves a lexical QName against the namespaces in scope on an element.
        /// </summary>
        /// <remarks>
        /// The runtime counterpart of what the parser does for a literal: here the bindings come from a node
        /// in the document rather than from where the expression was written, which is what lets a stylesheet
        /// read a QName out of its input.
        /// </remarks>
        private XPathValue ResolveQName(ref DynamicContext context)
        {
            XPathValue argument = m_arguments[0].Evaluate(ref context);
            if (IsEmptySequence(argument))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            string text = argument.ToStringValue();

            if (!XdmQName.TrySplit(text, out string prefix, out string localName))
            {
                // FOCA0002 rather than the FORG0001 a cast would raise: nothing is being cast here, and
                // what is wrong is the argument this function was handed (F&O 3.1 §10.2.1).
                throw XsltErrors.Error(
                    XsltErrorCode.FOCA0002, $"'{text}' is not a lexical QName, so it resolves to nothing.");
            }

            if (!TryElementArgument(1, ref context, out Model.XdmTree? tree, out int element))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, "resolve-QName() needs an element to resolve the prefix against.");
            }

            if (prefix.Length == 0)
            {
                // An unprefixed name takes the default namespace if the element declares one, which is what
                // makes it different from a name that simply has no namespace.
                foreach ((string declared, string uri) in tree!.InScopeNamespacesOf(element))
                {
                    if (declared.Length == 0)
                    {
                        return XPathValue.FromQName(new XdmQName(string.Empty, uri, localName));
                    }
                }

                return XPathValue.FromQName(new XdmQName(string.Empty, string.Empty, localName));
            }

            foreach ((string declared, string uri) in tree!.InScopeNamespacesOf(element))
            {
                if (string.Equals(declared, prefix, StringComparison.Ordinal))
                {
                    return XPathValue.FromQName(new XdmQName(prefix, uri, localName));
                }
            }

            throw XsltErrors.Error(
                XsltErrorCode.FONS0004,
                $"The prefix '{prefix}' is not bound to a namespace on the element it was resolved against.");
        }

        private XPathValue ComponentOfDateTime(ref DynamicContext context)
        {
            XPathValue argument = m_arguments[0].Evaluate(ref context);

            if (IsEmptySequence(argument))
            {
                // Every one of these functions returns the empty sequence for an empty argument.
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            XdmDateTime value = argument.AsDateTime();
            string component = m_name[..m_name.IndexOf('-')];

            switch (component)
            {
                case "year":
                    // The year as it is written, sign and all: year-from-dateTime of -1999-05-31 is
                    // -1999, not the 1998 the timeline counts it as.
                    return XPathValue.FromInteger(value.Year);

                case "month":
                    return XPathValue.FromInteger(value.Fields.Month);

                case "day":
                    return XPathValue.FromInteger(value.Fields.Day);

                case "hours":
                    return XPathValue.FromInteger(value.Fields.Hour);

                case "minutes":
                    return XPathValue.FromInteger(value.Fields.Minute);

                case "seconds":
                {
                    // Seconds are a decimal, since a time may carry a fraction of one.
                    decimal seconds = value.Fields.Second
                        + ((decimal)(value.Fields.Ticks % TimeSpan.TicksPerSecond) / TimeSpan.TicksPerSecond);

                    return XPathValue.FromDecimal(seconds);
                }

                default:
                    return value.Offset is TimeSpan offset
                        ? XPathValue.FromDuration(new XdmDuration(
                            0, (decimal)offset.TotalSeconds, XdmTypeCode.DayTimeDuration))
                        : XPathValue.FromSequence(XdmSequence.Empty);
            }
        }

        /// <summary>Reads one component out of a duration.</summary>
        /// <remarks>
        /// Each component is what is left after the larger ones have been taken out, and all of them carry the
        /// sign of the duration as a whole — so every component of a negative duration is negative or zero.
        /// </remarks>
        private XPathValue ComponentOfDuration(ref DynamicContext context)
        {
            XPathValue argument = m_arguments[0].Evaluate(ref context);

            if (IsEmptySequence(argument))
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            XdmDuration value = argument.AsDuration();
            int months = Math.Abs(value.Months);
            decimal seconds = Math.Abs(value.Seconds);
            int sign = value.IsNegative ? -1 : 1;

            switch (m_name[..m_name.IndexOf('-')])
            {
                case "years":
                    return XPathValue.FromInteger(sign * (months / 12));

                case "months":
                    return XPathValue.FromInteger(sign * (months % 12));

                case "days":
                    return XPathValue.FromInteger(sign * (long)(seconds / 86400m));

                case "hours":
                    return XPathValue.FromInteger(sign * (long)(seconds % 86400m / 3600m));

                case "minutes":
                    return XPathValue.FromInteger(sign * (long)(seconds % 3600m / 60m));

                default:
                    return XPathValue.FromDecimal(sign * (seconds % 60m));
            }
        }

        /// <summary>
        /// Whether a value holds no items, which is the same thing however it is holding them: an empty
        /// node-set and an empty sequence are both nothing, and a path that selected nothing produces the
        /// first.
        /// </summary>
        internal static bool IsEmptySequence(XPathValue value)
        {
            return value.Kind switch
            {
                XPathValueKind.Sequence => value.AsSequence().Count == 0,
                XPathValueKind.NodeSet => value.AsNodeSet().Count == 0,
                _ => false,
            };
        }

        // ---- Argument helpers ----------------------------------------------------------------------------

        private List<XPathValue> Items(int index, ref DynamicContext context)
        {
            return XdmSequence.Items(m_arguments[index].Evaluate(ref context));
        }

        /// <summary>
        /// The items of an argument, or of the context item where the argument was not written.
        /// </summary>
        /// <remarks>
        /// The 3.0 context forms of the accessors. Reading the context item where there is none is an error
        /// rather than an empty answer, which <see cref="DynamicContext.RequireContextItem"/> sees to.
        /// </remarks>
        private List<XPathValue> ItemsOrContext(int index, ref DynamicContext context)
        {
            return m_arguments.Length > index
                ? Items(index, ref context)
                : XdmSequence.Items(context.RequireContextItem($"fn:{m_name}()"));
        }

        private string Text(int index, ref DynamicContext context)
        {
            return m_arguments[index].Evaluate(ref context).ToStringValue();
        }

        /// <summary>An argument that may be left out or empty, as text, or null where it is either.</summary>
        private string? OptionalText(int index, ref DynamicContext context)
        {
            if (index >= m_arguments.Length)
            {
                return null;
            }

            XPathValue value = m_arguments[index].Evaluate(ref context);
            return IsEmptySequence(value) ? null : value.ToStringValue();
        }

        // ---- Sequence functions --------------------------------------------------------------------------

        /// <summary>The text two numbers share exactly when they are the same value.</summary>
        /// <remarks>
        /// Not the double. Past 2^53 a double stops telling consecutive integers apart, so keying by one
        /// folded 9007199254740992 and 9007199254740993 into a single value, and every pair of integers
        /// wider than that along with them. A whole number keys by its digits instead, which is exact at
        /// any size and which an xs:integer, an xs:decimal and an xs:double of the same value all reach.
        /// </remarks>
        private static string NumericKey(XPathValue value)
        {
            if (value.TypeCode == XdmTypeCode.Integer)
            {
                return value.ToBigInteger().ToString(CultureInfo.InvariantCulture);
            }

            double number = value.ToNumber();

            return !double.IsNaN(number) && !double.IsInfinity(number) && number == Math.Floor(number)
                ? new System.Numerics.BigInteger(number).ToString(CultureInfo.InvariantCulture)
                : number.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Removes duplicates, comparing items by value rather than by identity.
        /// </summary>
        /// <remarks>
        /// Numbers and strings that look alike are not the same value — <c>1</c> and <c>'1'</c> are distinct —
        /// so the key carries whether the item is numeric as well as how it reads.
        /// </remarks>
        private static XPathValue DistinctValues(List<XPathValue> items, Collation collation)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<XPathValue> distinct = new List<XPathValue>(items.Count);

            foreach (XPathValue item in items)
            {
                XPathValue atomic = Atomize(item);

                // Keyed by the value's type as well as how it reads, because 1 and '1' are not the same
                // value even though they look alike. An untyped value counts as text, which is what it is
                // until something compares it with a number.
                bool numeric = atomic.Kind == XPathValueKind.Number;

                // A name is its namespace and local name, whatever prefix it was written with: two names
                // spelled differently are one value, which is what 'eq' says of them.
                string key;

                if (numeric)
                {
                    key = "n:" + NumericKey(atomic);
                }
                else if (atomic.TypeCode == XdmTypeCode.QName)
                {
                    XdmQName name = atomic.AsQName();
                    key = "q:" + name.NamespaceUri + "}" + name.LocalName;
                }
                else
                {
                    // A string's key is the collation's, not the string: under one that ignores case,
                    // 'DATA' and 'data' are one value and have to land in one bucket.
                    key = "s:" + collation.Key(atomic.ToStringValue());
                }

                if (seen.Add(key))
                {
                    distinct.Add(atomic);
                }
            }

            return XdmSequence.Concatenate(distinct);
        }

        /// <summary>Rounds half towards positive infinity, which is what <c>fn:round</c> does.</summary>
        private static double Round(double value)
        {
            return XdmRounding.At(value, 0, halfToEven: false);
        }

        private XPathValue Subsequence(ref DynamicContext context)
        {
            List<XPathValue> items = Items(0, ref context);

            // Rounded the way fn:round rounds — halves towards positive infinity — and not the way
            // Math.Round does, which sends them to even. The specification defines this function in the
            // same words as fn:substring, so the two have to round alike: a length of 2.5 takes three
            // items as it takes three characters, and taking two was this one disagreeing with its twin.
            double start = Round(m_arguments[1].Evaluate(ref context).ToNumber());

            double end = m_arguments.Length > 2
                ? start + Round(m_arguments[2].Evaluate(ref context).ToNumber())
                : double.PositiveInfinity;

            List<XPathValue> taken = new List<XPathValue>();
            for (int i = 0; i < items.Count; i++)
            {
                // Positions are one-based, and a start below one simply includes everything from the front.
                double position = i + 1;
                if (position >= start && position < end)
                {
                    taken.Add(items[i]);
                }
            }

            return XdmSequence.Concatenate(taken);
        }

        private XPathValue InsertBefore(ref DynamicContext context)
        {
            List<XPathValue> items = Items(0, ref context);
            int at = (int)Math.Round(m_arguments[1].Evaluate(ref context).ToNumber());
            List<XPathValue> inserts = Items(2, ref context);

            at = Math.Clamp(at, 1, items.Count + 1);
            items.InsertRange(at - 1, inserts);
            return XdmSequence.Concatenate(items);
        }

        private XPathValue IndexOf(ref DynamicContext context, Collation collation)
        {
            // The positions are into the atomized sequence, so an array contributes as many places as it has
            // members: index-of([1, [5, 6], [6, 7]], 6) is (3, 4).
            List<XPathValue> items = Atomize(Items(0, ref context));
            XPathValue sought = Atomize(m_arguments[1].Evaluate(ref context));

            List<XPathValue> positions = new List<XPathValue>();
            for (int i = 0; i < items.Count; i++)
            {
                if (SameValue(items[i], sought, collation))
                {
                    positions.Add(XPathValue.FromInteger(i + 1));
                }
            }

            return XdmSequence.Concatenate(positions);
        }

        /// <summary>
        /// The error each cardinality function raises, which the specification gives a code of its own.
        /// </summary>
        private static XsltErrorCode CodeFor(string name)
        {
            return name switch
            {
                "zero-or-one" => XsltErrorCode.FORG0003,
                "one-or-more" => XsltErrorCode.FORG0004,
                _ => XsltErrorCode.FORG0005,
            };
        }

        /// <summary>Checks how many items a sequence has, which is the whole purpose of these three functions.</summary>
        private XPathValue Cardinality(ref DynamicContext context, int minimum, int maximum, string name)
        {
            XPathValue value = m_arguments[0].Evaluate(ref context);
            int count = XdmSequence.Items(value).Count;

            if (count < minimum || count > maximum)
            {
                throw XsltErrors.Error(CodeFor(name), $"{name}() was given a sequence of {count} items.");
            }

            return value;
        }

        /// <summary>
        /// Compares two sequences item by item, which is the only equality in XPath 2.0 that does not
        /// quantify: <c>deep-equal</c> asks whether the sequences <em>are</em> the same, not whether they
        /// overlap.
        /// </summary>
        internal static bool DeepEqual(List<XPathValue> left, List<XPathValue> right, Collation collation)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!DeepEqualItem(left[i], right[i], collation))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compares two items, going inside an array or a map rather than atomizing it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An array is deep-equal only to an array and a map only to a map, and neither to a sequence
        /// holding the same values — which is the point of both being items. <c>[1, 2]</c> and <c>(1, 2)</c>
        /// are different things and this is where saying so matters.
        /// </para>
        /// <para>
        /// A function that is neither raises rather than answering. Two functions cannot be compared: there
        /// is no way to ask whether two pieces of code agree on every input.
        /// </para>
        /// </remarks>
        private static bool DeepEqualItem(XPathValue left, XPathValue right, Collation collation)
        {
            if (left.Kind == XPathValueKind.Array || right.Kind == XPathValueKind.Array)
            {
                if (left.Kind != right.Kind)
                {
                    return false;
                }

                XdmArray first = left.AsArray();
                XdmArray second = right.AsArray();

                if (first.Count != second.Count)
                {
                    return false;
                }

                for (int i = 0; i < first.Count; i++)
                {
                    if (!DeepEqual(
                        XdmSequence.Items(first.Members[i]), XdmSequence.Items(second.Members[i]), collation))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (left.Kind == XPathValueKind.Map || right.Kind == XPathValueKind.Map)
            {
                if (left.Kind != right.Kind)
                {
                    return false;
                }

                XdmMap first = left.AsMap();
                XdmMap second = right.AsMap();

                if (first.Count != second.Count)
                {
                    return false;
                }

                // Equal sizes and every key of one present in the other with an equal value: the two maps
                // then have the same keys, so there is no need to walk the second as well.
                foreach (KeyValuePair<XPathValue, XPathValue> entry in first.Entries)
                {
                    if (!second.Contains(entry.Key)
                        || !DeepEqual(
                            XdmSequence.Items(entry.Value), XdmSequence.Items(second.Get(entry.Key)), collation))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (left.Kind == XPathValueKind.Function || right.Kind == XPathValueKind.Function)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOTY0015,
                    "fn:deep-equal() cannot compare functions. Two of them agreeing on every input is not a "
                    + "question that can be asked of the values.");
            }

            // Two nodes are compared as nodes — kind, name, attributes and children — and a node against
            // an atomic value is never equal; before this, both were atomized and two elements with one
            // string value passed as the same.
            if (left.Kind == XPathValueKind.Node || right.Kind == XPathValueKind.Node)
            {
                return left.Kind == right.Kind
                    && DeepEqualNodes(left.NodeTree, left.NodeId, right.NodeTree, right.NodeId, collation);
            }

            return SameValue(Atomize(left), Atomize(right), collation, nanAgreesWithNan: true);
        }

        /// <summary>
        /// Whether two nodes are deep-equal (F&amp;O §14.2.4): the same kind, the same name where they have
        /// one, the same attributes by name and value, and children that are pairwise deep-equal once
        /// comments and processing instructions are left out.
        /// </summary>
        private static bool DeepEqualNodes(
            Model.XdmTree leftTree, int left, Model.XdmTree rightTree, int right, Collation collation)
        {
            Model.NodeKind kind = leftTree.KindOf(left);

            if (kind != rightTree.KindOf(right))
            {
                return false;
            }

            switch (kind)
            {
                case Model.NodeKind.Root:
                    return DeepEqualChildren(leftTree, left, rightTree, right, collation);

                case Model.NodeKind.Element:
                {
                    if (!SameName(leftTree, left, rightTree, right))
                    {
                        return false;
                    }

                    // Each attribute of one has to be matched by name on the other, with the same value;
                    // the counts agreeing makes the matching a bijection.
                    int count = leftTree.AttributeCountOf(left);

                    if (count != rightTree.AttributeCountOf(right))
                    {
                        return false;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        int attribute = leftTree.AttributeAt(left, i);
                        bool matched = false;

                        for (int j = 0; j < count && !matched; j++)
                        {
                            int candidate = rightTree.AttributeAt(right, j);
                            matched = SameName(leftTree, attribute, rightTree, candidate)
                                && collation.AreEqual(leftTree.StringValueOf(attribute), rightTree.StringValueOf(candidate));
                        }

                        if (!matched)
                        {
                            return false;
                        }
                    }

                    return DeepEqualChildren(leftTree, left, rightTree, right, collation);
                }

                case Model.NodeKind.Attribute:
                case Model.NodeKind.Namespace:
                case Model.NodeKind.ProcessingInstruction:
                    return SameName(leftTree, left, rightTree, right)
                        && collation.AreEqual(leftTree.StringValueOf(left), rightTree.StringValueOf(right));

                default:
                    return collation.AreEqual(leftTree.StringValueOf(left), rightTree.StringValueOf(right));
            }
        }

        /// <summary>Whether two nodes have the same expanded name.</summary>
        private static bool SameName(Model.XdmTree leftTree, int left, Model.XdmTree rightTree, int right)
        {
            int leftName = leftTree.FingerprintOf(left);
            int rightName = rightTree.FingerprintOf(right);

            return leftTree.NameTable.GetLocalName(leftName) == rightTree.NameTable.GetLocalName(rightName)
                && leftTree.NameTable.GetNamespaceUri(leftName) == rightTree.NameTable.GetNamespaceUri(rightName);
        }

        /// <summary>
        /// Whether two nodes' children are pairwise deep-equal, comments and processing instructions
        /// left out of both.
        /// </summary>
        private static bool DeepEqualChildren(
            Model.XdmTree leftTree, int left, Model.XdmTree rightTree, int right, Collation collation)
        {
            int leftChild = NextCompared(leftTree, leftTree.FirstChildOf(left));
            int rightChild = NextCompared(rightTree, rightTree.FirstChildOf(right));

            while (leftChild >= 0 && rightChild >= 0)
            {
                if (!DeepEqualNodes(leftTree, leftChild, rightTree, rightChild, collation))
                {
                    return false;
                }

                leftChild = NextCompared(leftTree, leftTree.NextSiblingOf(leftChild));
                rightChild = NextCompared(rightTree, rightTree.NextSiblingOf(rightChild));
            }

            return leftChild < 0 && rightChild < 0;
        }

        /// <summary>The node itself, or the next sibling after it that takes part in the comparison.</summary>
        private static int NextCompared(Model.XdmTree tree, int node)
        {
            while (node >= 0
                && tree.KindOf(node) is Model.NodeKind.Comment or Model.NodeKind.ProcessingInstruction)
            {
                node = tree.NextSiblingOf(node);
            }

            return node;
        }

        // ---- Numeric functions ---------------------------------------------------------------------------

        private static XPathValue Absolute(XPathValue value)
        {
            // Declared numeric? -> numeric?, so nothing in gives nothing out rather than NaN.
            if (IsEmptySequence(value))
            {
                return value;
            }

            return value.TypeCode switch
            {
                XdmTypeCode.Integer => XPathValue.FromInteger(
                    System.Numerics.BigInteger.Abs(value.ToBigInteger())),
                XdmTypeCode.Decimal => XPathValue.FromDecimal(Math.Abs(value.ToDecimal())),
                XdmTypeCode.Float => XPathValue.FromFloat(Math.Abs((float)value.ToNumber())),
                _ => XPathValue.FromNumber(Math.Abs(value.ToNumber())),
            };
        }

        /// <summary>
        /// Rounds halves to the nearest even digit, which is what accountants and IEEE 754 both expect and
        /// what <c>round()</c> deliberately does not do.
        /// </summary>
        private XPathValue RoundHalfToEven(ref DynamicContext context)
        {
            XPathValue value = m_arguments[0].Evaluate(ref context);

            // Declared numeric? -> numeric?, so nothing in gives nothing out.
            if (IsEmptySequence(value))
            {
                return value;
            }

            int digits = m_arguments.Length > 1
                ? (int)Math.Round(m_arguments[1].Evaluate(ref context).ToNumber())
                : 0;

            if (value.TypeCode == XdmTypeCode.Integer && digits >= 0)
            {
                return value;
            }

            if (value.TypeCode is XdmTypeCode.Integer or XdmTypeCode.Decimal && digits is < 0 and >= -28)
            {
                // Rounding to the left of the point, in the type the value came in: an integer stays an
                // integer, so -3 to the hundreds is 0 and not a double's -0, which writes as "-0".
                decimal power = (decimal)Math.Pow(10, -digits);
                decimal rounded = Math.Round(value.ToDecimal() / power, 0, MidpointRounding.ToEven) * power;

                return value.TypeCode == XdmTypeCode.Integer
                    ? XPathValue.FromInteger((long)rounded)
                    : XPathValue.FromDecimal(rounded);
            }

            if (value.TypeCode == XdmTypeCode.Decimal && digits is >= 0 and <= 28)
            {
                return XPathValue.FromDecimal(Math.Round(value.ToDecimal(), digits, MidpointRounding.ToEven));
            }

            double number = value.ToNumber();
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                return XPathValue.FromNumber(number);
            }

            double settled = XdmRounding.At(number, Math.Clamp(digits, -400, 400), halfToEven: true);

            return value.TypeCode == XdmTypeCode.Float
                ? XPathValue.FromFloat((float)settled)
                : XPathValue.FromNumber(settled);
        }

        /// <summary>
        /// Prepares an aggregate's items: atomizes them, reads untyped values as doubles, and checks that
        /// they all belong to one family.
        /// </summary>
        /// <remarks>
        /// An aggregate over a mixture has no answer. <c>max(("a string", 1))</c> is not the larger of the
        /// two, because there is no comparison between a string and a number to be larger under, and
        /// <c>avg((xs:yearMonthDuration("P20Y"), 3))</c> is not an average of anything. XPath 2.0 raises
        /// <c>FORG0006</c> for both rather than picking a reading. Untyped values are the exception, as
        /// everywhere else: they become doubles, so an aggregate over document content works unchanged.
        /// </remarks>
        /// <param name="items">The argument's items.</param>
        /// <param name="function">The function's name, for the message.</param>
        /// <param name="numbersAndDurationsOnly">
        /// Whether the family must be one that adds up — true for <c>avg</c>, false for <c>min</c> and
        /// <c>max</c>, which will also order strings, booleans and dates.
        /// </param>
        internal static List<XPathValue> AggregateItems(
            List<XPathValue> items,
            string function,
            bool numbersAndDurationsOnly)
        {
            XdmTypeCode family = XdmTypeCode.None;

            // Atomized as a whole rather than item by item, because an array contributes its members and so
            // changes the count: sum([[1, 2], [3, 4]]) adds four numbers. Items that are atomic already —
            // which the function's signature has usually seen to — are taken as they are, and the list
            // they came in, which every caller built for the purpose, is written back into rather than
            // copied: over a thousand prices that is two arrays of a thousand values not made.
            List<XPathValue> source = NeedsAtomizing(items) ? Atomize(items) : items;

            for (int i = 0; i < source.Count; i++)
            {
                XPathValue value = source[i];

                if (value.TypeCode == XdmTypeCode.None)
                {
                    value = XPathValue.FromUntypedAtomic(value.ToStringValue());
                }

                // Untyped items become doubles, and text that is not a number says so rather than becoming
                // NaN and quietly making the whole aggregate NaN.
                value = XdmType.UntypedAsDouble(value);

                XdmTypeCode kind = FamilyOf(value);

                if (kind == XdmTypeCode.None
                    || (numbersAndDurationsOnly && kind != XdmTypeCode.Double && !IsDurationCode(kind)))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FORG0006,
                        $"fn:{function} cannot take a value of type {value.TypeCode} here.");
                }

                if (i != 0 && kind != family)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FORG0006,
                        $"fn:{function} was given a mixture of types — {family} and {kind} — and there is no "
                        + "comparison between them to aggregate over.");
                }

                family = kind;
                source[i] = value;
            }

            return source;
        }

        /// <summary>Whether any item is something atomization would open up, or refuse.</summary>
        private static bool NeedsAtomizing(List<XPathValue> items)
        {
            foreach (XPathValue item in items)
            {
                if (item.Kind is not (XPathValueKind.Boolean or XPathValueKind.Number or XPathValueKind.String))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The family an aggregate groups an item into: all four numeric types count as one, and anything
        /// with no order at all — a name, a string of bytes, a partial date — counts as none.
        /// </summary>
        private static XdmTypeCode FamilyOf(XPathValue value)
        {
            return value.TypeCode switch
            {
                XdmTypeCode.Integer or XdmTypeCode.Decimal or XdmTypeCode.Float or XdmTypeCode.Double
                    => XdmTypeCode.Double,
                // A URI orders with the strings, so min() over a mixture of the two has an answer.
                XdmTypeCode.String or XdmTypeCode.AnyUri => XdmTypeCode.String,
                XdmTypeCode.Boolean or XdmTypeCode.Date or XdmTypeCode.Time or XdmTypeCode.DateTime
                    or XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration
                    => value.TypeCode,
                _ => XdmTypeCode.None,
            };
        }

        private static bool IsDurationCode(XdmTypeCode code)
        {
            return code is XdmTypeCode.YearMonthDuration or XdmTypeCode.DayTimeDuration;
        }

        /// <summary>
        /// Whether a value is text, and so is ordered by a collation rather than by its own kind of order.
        /// </summary>
        /// <remarks>
        /// <see cref="XPathValueKind.String"/> is not the question: a date, a duration and a QName are all
        /// held that way and none of them is a string to be collated.
        /// </remarks>
        internal static bool IsText(XPathValue value)
        {
            return value.TypeCode is XdmTypeCode.String or XdmTypeCode.UntypedAtomic or XdmTypeCode.AnyUri;
        }

        private static XPathValue Extreme(List<XPathValue> items, bool wantSmallest, Collation collation)
        {
            if (items.Count == 0)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            List<XPathValue> prepared = AggregateItems(
                items, wantSmallest ? "min" : "max", numbersAndDurationsOnly: false);

            // NaN stands in no relation to anything, so a sequence holding one has no smallest or largest
            // member. The specification makes NaN the answer rather than letting it be passed over.
            foreach (XPathValue item in prepared)
            {
                if (item.Kind == XPathValueKind.Number && double.IsNaN(item.ToNumber()))
                {
                    return item;
                }
            }

            BinaryOperator wanted = wantSmallest ? BinaryOperator.LessThan : BinaryOperator.GreaterThan;
            XPathValue best = prepared[0];

            for (int i = 1; i < prepared.Count; i++)
            {
                // Two strings are ordered by the collation; anything else by the general value comparison,
                // which is where every other kind of ordering in the language already lives. Judged by the
                // type rather than the kind, because a date and a duration are also held as strings and
                // 'PT10H' before 'PT9H' is exactly the answer they must not get.
                bool better;

                if (IsText(prepared[i]) && IsText(best))
                {
                    int sign = collation.Compare(prepared[i].ToStringValue(), best.ToStringValue());
                    better = wantSmallest ? sign < 0 : sign > 0;
                }
                else
                {
                    better = XdmComparison.Value(prepared[i], best, wanted);
                }

                if (better)
                {
                    best = prepared[i];
                }
            }

            return Promoted(best, prepared);
        }

        /// <summary>
        /// Returns the chosen value at the widest numeric type the sequence held.
        /// </summary>
        /// <remarks>
        /// <c>min((1, xs:float(2), xs:decimal(3)))</c> is an <c>xs:float</c>, not the <c>xs:integer</c> that
        /// happened to be smallest: the values are brought to a common type before the question is asked, so
        /// the answer is of that type. Only <c>xs:float</c> and <c>xs:double</c> convert — an integer already
        /// <em>is</em> a decimal, which is substitution rather than promotion and changes nothing.
        /// </remarks>
        private static XPathValue Promoted(XPathValue best, List<XPathValue> prepared)
        {
            if (!XdmComparison.IsNumeric(best.TypeCode))
            {
                return best;
            }

            bool anyDouble = false;
            bool anyFloat = false;

            foreach (XPathValue item in prepared)
            {
                anyDouble |= item.TypeCode == XdmTypeCode.Double;
                anyFloat |= item.TypeCode == XdmTypeCode.Float;
            }

            if (anyDouble)
            {
                return XPathValue.FromNumber(best.ToNumber());
            }

            return anyFloat ? XPathValue.FromFloat((float)best.ToNumber()) : best;
        }

        private static XPathValue Average(List<XPathValue> items)
        {
            if (items.Count == 0)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            List<XPathValue> prepared = AggregateItems(items, "avg", numbersAndDurationsOnly: true);
            XPathValue total = prepared[0];

            // Folded through the ordinary operators, so that the result takes the type promotion gives it:
            // an average of integers is a decimal, and an average of durations is a duration.
            for (int i = 1; i < prepared.Count; i++)
            {
                total = XdmArithmetic.Apply(BinaryOperator.Add, total, prepared[i]);
            }

            return XdmArithmetic.Apply(
                BinaryOperator.Divide, total, XPathValue.FromInteger(prepared.Count));
        }

        // ---- String functions ----------------------------------------------------------------------------

        private XPathValue StringJoin(ref DynamicContext context)
        {
            List<XPathValue> items = Items(0, ref context);
            string separator = m_arguments.Length > 1 ? Text(1, ref context) : string.Empty;

            string[] parts = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                parts[i] = XdmSequence.StringValueOf(items[i]);
            }

            return XPathValue.FromString(string.Join(separator, parts));
        }

        /// <summary>
        /// Whether a code point is one XML permits in a document.
        /// </summary>
        /// <remarks>
        /// Narrower than "a code point Unicode defines". XML excludes the control characters other than tab,
        /// newline and carriage return; the surrogate range, which is an encoding artefact rather than
        /// characters; and the two non-characters at the end of the basic plane. A string cannot hold any of
        /// them, so building one is an error rather than a value nothing can serialize.
        /// </remarks>
        private static bool IsXmlCharacter(int codepoint)
        {
            return codepoint is 0x9 or 0xA or 0xD
                || (codepoint >= 0x20 && codepoint <= 0xD7FF)
                || (codepoint >= 0xE000 && codepoint <= 0xFFFD)
                || (codepoint >= 0x10000 && codepoint <= 0x10FFFF);
        }

        private static XPathValue CodepointsToString(List<XPathValue> items)
        {
            StringBuilder builder = new StringBuilder(items.Count);

            foreach (XPathValue item in items)
            {
                double number = Atomize(item).ToNumber();

                if (number is < 0 or > 0x10FFFF || !IsXmlCharacter((int)number))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOCH0001, $"{number} is not a valid XML character.");
                }

                builder.Append(char.ConvertFromUtf32((int)number));
            }

            return XPathValue.FromString(builder.ToString());
        }

        private static XPathValue StringToCodepoints(string text)
        {
            List<XPathValue> codepoints = new List<XPathValue>(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                // Surrogate pairs are one code point, not two, so they are combined rather than reported
                // as the two UTF-16 units they are stored as.
                int codepoint = char.IsHighSurrogate(text[i]) && i + 1 < text.Length
                    ? char.ConvertToUtf32(text[i], text[++i])
                    : text[i];

                codepoints.Add(XPathValue.FromInteger(codepoint));
            }

            return XdmSequence.Concatenate(codepoints);
        }

        /// <summary>
        /// Splits a string on a pattern, giving what lies between the matches.
        /// </summary>
        /// <remarks>
        /// Written out rather than handed to <see cref="Regex.Split"/>, which also returns whatever the
        /// pattern's groups captured: <c>tokenize('abracadabra', '(ab)|(a)')</c> would come back with the
        /// <c>ab</c> and <c>a</c> it split on among the pieces it split into.
        /// </remarks>
        private XPathValue Tokenize(ref DynamicContext context)
        {
            XPathValue subject = m_arguments[0].Evaluate(ref context);

            string input = IsEmptySequence(subject) ? string.Empty : subject.ToStringValue();

            // Nothing in is nothing out, and a zero-length string counts as nothing: splitting it would
            // otherwise give one empty token, which is not what "no tokens" looks like.
            if (input.Length == 0)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            Regex pattern = Pattern(ref context, 1, 2, "tokenize");

            List<XPathValue> items = new List<XPathValue>();
            int position = 0;

            foreach (Match match in pattern.Matches(input))
            {
                items.Add(XPathValue.FromString(input[position..match.Index]));
                position = match.Index + match.Length;
            }

            items.Add(XPathValue.FromString(input[position..]));
            return XdmSequence.Concatenate(items);
        }

        // ---- Regular expressions -------------------------------------------------------------------------

        /// <summary>
        /// Compiles the pattern a regular-expression function was given.
        /// </summary>
        /// <param name="context">The evaluation context.</param>
        /// <param name="patternIndex">Which argument holds the pattern.</param>
        /// <param name="flagsIndex">Which argument holds the flags, if it was supplied.</param>
        /// <param name="needsWidth">
        /// The function's name where a zero-length match would not terminate, and <see langword="null"/>
        /// where it is harmless — <c>matches</c> is content to say that an empty pattern matches.
        /// </param>
        private Regex Pattern(
            ref DynamicContext context,
            int patternIndex,
            int flagsIndex,
            string? needsWidth = null)
        {
            string pattern = Text(patternIndex, ref context);
            string flags = m_arguments.Length > flagsIndex ? Text(flagsIndex, ref context) : string.Empty;

            Regex compiled = RegexTranslator.Translate(pattern, flags, m_version);

            if (needsWidth is not null)
            {
                RegexTranslator.RequireWidth(compiled, pattern, needsWidth);
            }

            return compiled;
        }

        // ---- Value comparison ----------------------------------------------------------------------------

        /// <summary>
        /// Reduces an item to its atomic value, which for a node is its typed value: its string-value,
        /// untyped, unless the node was validated.
        /// </summary>
        private static XPathValue Atomize(XPathValue item)
        {
            return item.Kind is XPathValueKind.Node or XPathValueKind.NodeSet
                ? XdmSequence.TypedValueAsOne(item, "The function")
                : item;
        }

        /// <summary>
        /// Compares two atomic values as <c>eq</c> would, with values it cannot compare counted as distinct.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <c>eq</c> operator and not the general comparison, so an untyped value is read as a string
        /// rather than as whatever it is being compared against: <c>xs:untypedAtomic('P1Y')</c> is not a
        /// duration here however much it looks like one.
        /// </para>
        /// <para>
        /// Where <c>eq</c> is not defined between the two types the answer is <em>false</em> rather than an
        /// error, which is what <c>fn:deep-equal</c> and <c>fn:index-of</c> both ask for: they are looking
        /// for equal values, and a date is not equal to a string by being incomparable with it. That is the
        /// one place these differ from writing <c>eq</c> out.
        /// </para>
        /// </remarks>
        private static bool SameValue(
            XPathValue left, XPathValue right, Collation collation, bool nanAgreesWithNan = false)
        {
            // Two pieces of text are compared by the collation, which is the reason these functions take
            // one. Judged by the type rather than the kind, a date and a duration being held as text too.
            if (IsText(left) && IsText(right))
            {
                return collation.AreEqual(left.ToStringValue(), right.ToStringValue());
            }

            // The one place the two callers part company. fn:deep-equal asks whether two sequences are the
            // same, and a sequence holding NaN is the same as itself, so it says so outright; fn:index-of
            // asks 'eq', which no NaN satisfies — not even against the NaN it was handed to look for.
            if (nanAgreesWithNan && IsNan(left) && IsNan(right))
            {
                return true;
            }

            try
            {
                return XdmComparison.Value(left, right, BinaryOperator.Equal);
            }
            catch (XsltException)
            {
                return false;
            }
        }

        /// <summary>Whether a value is a floating-point NaN, rather than text that reads as one.</summary>
        private static bool IsNan(XPathValue value)
        {
            return value.TypeCode is XdmTypeCode.Double or XdmTypeCode.Float
                && double.IsNaN(value.ToNumber());
        }
    }
}
