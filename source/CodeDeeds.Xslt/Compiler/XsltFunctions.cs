using System.Globalization;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// XSLT's <c>generate-id()</c> function.
    /// </summary>
    /// <remarks>
    /// Returns a string that identifies a node uniquely and consistently for the duration of a transformation.
    /// The identifier is built from the node's position in its tree, which the flat document model already
    /// provides as an integer, so no bookkeeping is needed to keep it stable.
    /// <para>
    /// It is what makes grouping possible in XSLT 1.0: comparing a node's identity against the first node a
    /// key returns is how a stylesheet asks "is this the first item in its group?", which has no other
    /// expression in the language.
    /// </para>
    /// </remarks>
    /// <summary>
    /// XSLT's <c>format-number()</c> function.
    /// </summary>
    /// <remarks>
    /// The pattern is parsed once at compile time when it is a literal, which it nearly always is; a computed
    /// pattern is parsed on each call.
    /// </remarks>
    internal sealed class FormatNumberExpr : Expr
    {
        private readonly Expr m_value;
        private readonly Expr m_pattern;
        private readonly DecimalFormat m_format;
        private readonly XsltVersion m_version;

        /// <summary>
        /// The version whose function library this call belongs to, which settles which code an unreadable
        /// picture carries.
        /// </summary>
        /// <remarks>
        /// Not the same as <see cref="m_version"/>, and deliberately narrower in what it decides. How the
        /// picture is <em>read</em> is behaviour — XSLT 1.0 formats through <c>DecimalFormat</c> and 2.0
        /// through an algorithm that replaced it — so that follows the version the stylesheet claims. What
        /// the failure is <em>called</em> is a question about which specification defines the function, and
        /// that is the processor's library.
        /// </remarks>
        private readonly XsltVersion m_syntaxVersion;

        private readonly NumberPattern? m_parsed;

        /// <summary>Initializes a call to <c>format-number()</c>.</summary>
        /// <param name="value">The number to format.</param>
        /// <param name="pattern">The pattern to format it with.</param>
        /// <param name="format">The symbols named by the chosen <c>xsl:decimal-format</c>.</param>
        /// <param name="version">
        /// The version in force, which decides between 1.0's <c>DecimalFormat</c> behaviour and the algorithm
        /// 2.0 replaced it with.
        /// </param>
        /// <param name="syntaxVersion">
        /// The version whose library defines the function, which decides the code an
        /// unreadable picture carries. Defaults to <paramref name="version"/>, which is right wherever the
        /// two are not being told apart.
        /// </param>
        public FormatNumberExpr(
            Expr value,
            Expr pattern,
            DecimalFormat format,
            XsltVersion version,
            XsltVersion? syntaxVersion = null)
        {
            m_value = value;
            m_pattern = pattern;
            m_format = format;
            m_version = version;
            m_syntaxVersion = syntaxVersion ?? version;

            if (pattern is StringLiteralExpr literal)
            {
                try
                {
                    m_parsed = NumberPattern.Parse(literal.Value, format, version, m_syntaxVersion);
                }
                catch (XsltException)
                {
                    // Left for evaluation to raise, so that FODF1310 is the dynamic error it is specified to
                    // be: a bad picture in a branch that never runs must not stop the stylesheet compiling.
                }
            }
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_value, m_pattern };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            // Both arguments are judged before the picture is read, so that a call wrong in two ways reports
            // the type error the specification puts first rather than whichever is noticed first.
            XPathValue value = EvaluateNumber(ref context);
            Require(value, true);

            NumberPattern? pattern = m_parsed;

            if (pattern is null)
            {
                XPathValue picture = m_pattern.Evaluate(ref context);
                Require(picture, false);
                pattern = ReadPicture(picture.ToStringValue());
            }

            // Where a number is expected, backwards compatibility converts the first item with fn:number
            // (XPath 2.0 §3.1.5), which reads what xs:double writes: a price written '1e1' formats as ten.
            return XPathValue.FromString(
                pattern.Format(
                    value,
                    m_version.IsBackwardsCompatible ? XdmType.FirstItemAsDoubleOrNaN(value) : NumberOf(value),
                    m_format));
        }

        /// <summary>
        /// Reads the value as the number it is from 2.0 on, where an untyped one is cast to
        /// <c>xs:double</c>.
        /// </summary>
        /// <remarks>
        /// The function conversion rules for a parameter declared <c>xs:numeric?</c>: a number is itself,
        /// nothing is NaN, and untyped content — which is what a node atomizes to — is cast, so
        /// <c>1e1</c> is ten and what is no number is <c>FORG0001</c>, as it is for <c>round()</c> and
        /// every other function given the same node. It had been read by XPath 1.0's grammar and
        /// answered NaN for both. <see cref="Require"/> has refused everything else by now.
        /// </remarks>
        /// <param name="value">The first argument, already checked.</param>
        /// <exception cref="XsltException">Untyped content that is not an <c>xs:double</c>.</exception>
        private static double NumberOf(XPathValue value)
        {
            switch (value.Kind)
            {
                case XPathValueKind.Node:
                    return XdmType.UntypedTextAsDouble(value.NodeTree.StringValueOf(value.NodeId));

                case XPathValueKind.NodeSet:
                {
                    NodeSet nodes = value.AsNodeSet();

                    return nodes.Count == 0
                        ? double.NaN
                        : XdmType.UntypedTextAsDouble(nodes.TreeAt(0).StringValueOf(nodes[0]));
                }

                case XPathValueKind.String when value.TypeCode == XdmTypeCode.UntypedAtomic:
                    return XdmType.UntypedTextAsDouble(value.ToStringValue());

                case XPathValueKind.Sequence when value.AsSequence().Count == 1:
                    return NumberOf(value.AsSequence()[0]);

                default:
                    return value.ToNumber();
            }
        }

        /// <summary>
        /// Reads the picture, giving the failure the code the language in force names it by.
        /// </summary>
        /// <remarks>
        /// One complaint with two codes. XSLT 2.0 has a <c>format-number</c> of its own and calls a picture
        /// it cannot read <c>XTDE1310</c>; XPath 3.0 moved the function into the core library, where the
        /// same failure is <c>FODF1310</c>. Which one a caller hears is a question about which specification
        /// defines the function being called, so it follows the processor's library rather than the version
        /// the stylesheet claims — the suite pairs these tests over one <c>version="2.0"</c> stylesheet and
        /// wants the two codes from the two processors.
        /// </remarks>
        /// <param name="picture">The picture text.</param>
        /// <summary>
        /// Evaluates the number, reaching a single selected node as the node itself rather than as a
        /// node-set built to hold it.
        /// </summary>
        /// <remarks>
        /// <c>format-number(price, …)</c>, once per element written, is the usual call, and the node-set
        /// and its array were two allocations per call for a value the node already is. What the number is
        /// read from is the same either way: the node's string value, typed as nothing.
        /// </remarks>
        private XPathValue EvaluateNumber(ref DynamicContext context)
        {
            if (!m_value.ReturnsNodeSet || m_value.MaySpanDocuments)
            {
                return m_value.Evaluate(ref context);
            }

            List<int> nodes = NodeListPool.Rent();

            try
            {
                XdmTree tree = m_value.EvaluateNodes(ref context, nodes);

                return nodes.Count == 1
                    ? XPathValue.FromNode(tree, nodes[0])
                    : XPathValue.FromNodeSet(NodeSet.FromOrderedNodes(tree, nodes));
            }
            finally
            {
                NodeListPool.Return(nodes);
            }
        }

        private NumberPattern ReadPicture(string picture)
        {
            try
            {
                return NumberPattern.Parse(picture, m_format, m_version, m_syntaxVersion);
            }
            catch (XsltException error)
                when (error.Code == nameof(XsltErrorCode.FODF1310)
                    && m_syntaxVersion.CompareTo(XsltVersion.V30) < 0)
            {
                throw XsltErrors.Error(XsltErrorCode.XTDE1310, error.Message);
            }
        }

        /// <summary>
        /// Refuses an argument of the wrong type, which from 2.0 is an error rather than something to convert.
        /// </summary>
        /// <remarks>
        /// XPath 1.0 has one type per position and coerces anything into it, so a string formats as NaN there
        /// and this check is not made. From 2.0 the signature is <c>xs:numeric?</c> and <c>xs:string</c>, and
        /// only untyped values — which is what a node atomizes to — are still converted.
        /// </remarks>
        private void Require(XPathValue value, bool numeric)
        {
            if (m_version.IsBackwardsCompatible)
            {
                return;
            }

            bool wrong = value.TypeCode switch
            {
                XdmTypeCode.None or XdmTypeCode.UntypedAtomic => false,
                XdmTypeCode.Double or XdmTypeCode.Float or XdmTypeCode.Decimal or XdmTypeCode.Integer
                    => !numeric,
                XdmTypeCode.String or XdmTypeCode.AnyUri => numeric,
                _ => true,
            };

            if (wrong)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"format-number's {(numeric ? "first" : "second")} argument is "
                        + $"{(numeric ? "the number to format" : "the picture to format it by")}, and an "
                        + $"{value.TypeCode} is not one.");
            }
        }
    }

    /// <summary>
    /// XSLT's <c>document()</c> function, which pulls in an external document as a node-set.
    /// </summary>
    /// <remarks>
    /// A node-set argument names one document per member, and the results are unioned. Each distinct URI is
    /// loaded once per transformation, as the specification requires: two calls naming the same document must
    /// return the same nodes, or comparing them would be meaningless.
    /// </remarks>
    internal sealed class DocumentExpr : Expr
    {
        private readonly Expr m_uri;
        private readonly Expr? m_base;
        private readonly string? m_baseUri;
        private readonly int m_package;

        /// <summary>Initializes a call to <c>document()</c>.</summary>
        /// <param name="uri">The expression naming the document or documents.</param>
        /// <param name="baseUri">The stylesheet's base URI, which relative references resolve against.</param>
        /// <param name="baseNode">
        /// The second argument, where the call has one: a node whose own base URI replaces the stylesheet's.
        /// </param>
        /// <param name="package">
        /// The package the call is written in, whose whitespace declarations strip what it reads.
        /// </param>
        public DocumentExpr(Expr uri, string? baseUri, Expr? baseNode = null, int package = 0)
        {
            m_uri = uri;
            m_baseUri = baseUri;
            m_base = baseNode;
            m_package = package;
        }

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => true;

        /// <summary>
        /// The one expression that introduces a second document, and so the root of every node-set that spans
        /// documents.
        /// </summary>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children =>
            m_base is null ? new[] { m_uri } : new[] { m_uri, m_base };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XsltRuntime runtime = context.Runtime
                ?? throw new XsltException("document() can only be used during a transformation.");

            // Every item of the argument names a document, and it makes no difference how they arrived: one
            // string, a node-set of them, or a sequence mixing the two. Reading anything that was not a
            // node-set as a single string was reading document(('a.xml','b.xml')) as one URI with a space in
            // it, which no resolver was ever going to find.
            // A second argument settles the base for every item; without one each item answers for itself,
            // and a node answers with its own base URI rather than the stylesheet's. That is what lets a
            // catalogue be read: document(@file) over a list of relative references means each one relative
            // to the catalogue it was written in, which is rarely where the stylesheet is.
            string? stated = m_base is null ? null : BaseUri(ref context);
            NodeSet? result = null;

            foreach (XPathValue item in XdmSequence.Items(m_uri.Evaluate(ref context)))
            {
                // Told what to resolve against, the call resolves against that and nothing else.
                // A parentless text node, comment or processing instruction has no base URI of its own —
                // nothing constructed it in a document — so a relative reference in one, or one told to
                // resolve against one, has nothing to resolve against (XTDE1162).
                bool orphan = item.Kind == XPathValueKind.Node && IsOrphanText(item);
                string? baseUri = m_base is not null ? stated : orphan ? null : BaseUriOfItem(item, ref context);
                string reference = XdmSequence.StringValueOf(item);

                if ((m_base is not null || orphan) && baseUri is null
                    && !Uri.IsWellFormedUriString(reference, UriKind.Absolute))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE1162,
                        $"'{reference}' is a relative reference, and the node document() was told to resolve "
                        + "it against has no base URI.");
                }

                XdmTree document = runtime.LoadDocument(reference, baseUri, out int selected, m_package);

                if (selected < 0)
                {
                    // A fragment naming an ID no element has selects nothing.
                    continue;
                }

                if (result is null)
                {
                    result = new NodeSet(document, 1);
                    result.Add(selected);
                }
                else
                {
                    result.Add(document, selected);
                }
            }

            if (result is null)
            {
                return XPathValue.FromNodeSet(new NodeSet(context.Tree, 0));
            }

            // One root node per distinct document named. Repeated URIs collapse of their own accord, because
            // each is loaded once and returns the same tree, and the union then removes the duplicate.
            result.SortAndDeduplicate();
            return XPathValue.FromNodeSet(result);
        }

        /// <summary>
        /// What a relative reference in the first argument resolves against.
        /// </summary>
        /// <remarks>
        /// Ordinarily the stylesheet's own base URI, which is what a reference written in a stylesheet means.
        /// A second argument replaces it with the base URI of the <em>first node</em> that argument selects,
        /// which is how a stylesheet resolves a reference the way some other document would have — a
        /// catalogue that lists its entries by relative URI is read by loading the catalogue and then asking
        /// for each entry relative to it.
        /// </remarks>
        /// <param name="context">The context to evaluate the second argument in.</param>
        private string? BaseUri(ref DynamicContext context)
        {
            foreach (XPathValue item in XdmSequence.Items(m_base!.Evaluate(ref context)))
            {
                if (item.Kind != XPathValueKind.Node)
                {
                    continue;
                }

                // The same walk fn:base-uri does, so xml:base is honoured here exactly as it is there
                // rather than by a second reading of the same attribute. A parentless text node, comment or
                // processing instruction has no base URI at all — nothing constructed it in a document — and
                // a relative reference is then XTDE1162 rather than quietly the stylesheet's.
                NodeKind kind = item.NodeTree.KindOf(item.NodeId);

                if (kind is NodeKind.Text or NodeKind.Comment or NodeKind.ProcessingInstruction
                    && item.NodeTree.ParentOf(item.NodeId) < 0)
                {
                    return null;
                }

                return Xpath2FunctionExpr.BaseUriOf(
                    item.NodeTree, item.NodeId, context.Runtime?.BaseUriOf(item.NodeTree));
            }

            // Nothing selected, so there is no node to take a base from and the stylesheet's own stands.
            return m_baseUri;
        }

        /// <summary>What one item of the first argument resolves its reference against.</summary>
        /// <remarks>
        /// A node carries where it came from, and a reference written in a document means what it means
        /// there. Anything else is a string that arrived from wherever the expression got it, and the
        /// stylesheet is the only base there is for one of those.
        /// </remarks>
        /// <param name="item">The item naming a document.</param>
        /// <param name="context">The context, for the runtime that knows where a tree came from.</param>
        /// <summary>Whether a node is a text node, comment or processing instruction with no parent.</summary>
        private static bool IsOrphanText(XPathValue item)
        {
            NodeKind kind = item.NodeTree.KindOf(item.NodeId);

            return kind is NodeKind.Text or NodeKind.Comment or NodeKind.ProcessingInstruction
                && item.NodeTree.ParentOf(item.NodeId) < 0;
        }

        private string? BaseUriOfItem(XPathValue item, ref DynamicContext context)
        {
            return item.Kind == XPathValueKind.Node
                ? Xpath2FunctionExpr.BaseUriOf(
                    item.NodeTree, item.NodeId, context.Runtime?.BaseUriOf(item.NodeTree)) ?? m_baseUri
                : m_baseUri;
        }
    }

    /// <summary>
    /// <c>fn:function-lookup</c>, which finds a function by a name and an arity that are values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else that names a function names it in the source, where what it means can be settled on
    /// the spot. This one is handed a <c>xs:QName</c> and an integer while the transformation runs, so the
    /// answer has to be reachable from a compiled expression — which is why the library dispatch was pulled
    /// out into <see cref="FunctionLibrary"/>, whose every input is a value.
    /// </para>
    /// <para>
    /// The functions XSLT adds are the exception and cannot be built that way: <c>system-property</c> and
    /// its like are compiled against the namespaces in scope where they were written, and this expression
    /// runs long after. So they are built <em>here</em>, at the call site, one item per name and arity, and
    /// handed over ready. There are eleven names and none takes more than three arguments, so the table is
    /// small and bounded — and each item carries the scope of the <c>function-lookup</c> that asked for it,
    /// which is the static context the specification says to use.
    /// </para>
    /// </remarks>
    internal sealed class FunctionLookupExpr : Expr
    {
        private readonly Expr m_name;
        private readonly Expr m_arity;
        private readonly IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> m_declared;
        private readonly IReadOnlyDictionary<(ExpandedName Name, int Arity), XPathValue> m_host;
        private readonly XsltVersion m_version;
        private readonly XsltVersion m_syntaxVersion;
        private readonly bool m_legacySyntax;

        /// <summary>Initializes a call to <c>function-lookup()</c>.</summary>
        /// <param name="name">The expression giving the function's name.</param>
        /// <param name="arity">The expression giving how many arguments it takes.</param>
        /// <param name="declared">The functions the stylesheet declares, by name and arity.</param>
        /// <param name="host">The XSLT functions, built here and ready to hand back.</param>
        /// <param name="version">The version in force where the call was written.</param>
        /// <param name="syntaxVersion">The version whose library is being read.</param>
        /// <param name="legacySyntax">Whether the 1.0 library alone is in force.</param>
        public FunctionLookupExpr(
            Expr name,
            Expr arity,
            IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> declared,
            IReadOnlyDictionary<(ExpandedName Name, int Arity), XPathValue> host,
            XsltVersion version,
            XsltVersion syntaxVersion,
            bool legacySyntax)
        {
            m_name = name;
            m_arity = arity;
            m_declared = declared;
            m_host = host;
            m_version = version;
            m_syntaxVersion = syntaxVersion;
            m_legacySyntax = legacySyntax;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_name, m_arity };

        /// <inheritdoc/>
        /// <remarks>
        /// Which function comes back is not known until the name is, and <c>fn:position</c> and
        /// <c>fn:last</c> are among the ones it may be. So every lookup is taken to read the focus, which
        /// costs a rewrite forgone where it did not and a wrong answer nowhere.
        /// </remarks>
        internal override bool ReadsFocusPosition => true;

        /// <summary>
        /// Builds the argument list a function item's body reads: one variable per slot, from zero upwards.
        /// </summary>
        /// <param name="arity">How many.</param>
        internal static Expr[] Placeholders(int arity)
        {
            Expr[] placeholders = new Expr[arity];
            for (int i = 0; i < arity; i++)
            {
                placeholders[i] = new VariableReferenceExpr(i, isGlobal: false, $"arg{i + 1}");
            }

            return placeholders;
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue named = XdmSequence.RequireSingleItem(
                m_name.Evaluate(ref context), "the name fn:function-lookup() asks about");

            if (named.TypeCode != XdmTypeCode.QName)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004, "fn:function-lookup() takes an xs:QName, and was given a value.");
            }

            XdmQName qualified = named.AsQName();
            long asked = XdmSequence
                .RequireSingleItem(m_arity.Evaluate(ref context), "the arity fn:function-lookup() asks about")
                .ToInteger();

            if (asked < 0 || asked > 64)
            {
                // Nothing here takes sixty-four arguments, and a wild number is a question with no answer
                // rather than an error: the function is defined to give the empty sequence for a name and
                // arity it cannot find.
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            int arity = (int)asked;
            ExpandedName name = new ExpandedName(qualified.NamespaceUri, qualified.LocalName);

            if (m_host.TryGetValue((name, arity), out XPathValue ready))
            {
                return ready;
            }

            Expr[] placeholders = Placeholders(arity);
            Expr? body;

            if (m_declared.TryGetValue((name, arity), out UserFunction? declared))
            {
                body = new UserFunctionCallExpr(declared, placeholders);
            }
            else
            {
                try
                {
                    body = FunctionLibrary.TryCreate(
                        qualified.NamespaceUri,
                        qualified.LocalName,
                        placeholders,
                        m_version,
                        m_syntaxVersion,
                        m_legacySyntax);
                }
                catch (XsltException)
                {
                    // A library that has the name and refuses the arity, or a type with no constructor. The
                    // question was still "is there such a function", and the answer is no.
                    body = null;
                }
            }

            return body is null
                ? XPathValue.FromSequence(XdmSequence.Empty)
                : XPathValue.FromFunction(
                    new XdmNamedFunction(qualified, arity, body) { DeclaredTypes = body.DeclaredSignature });
        }
    }

    /// <summary>
    /// XSLT's <c>system-property()</c> function, which reports what the processor is.
    /// </summary>
    /// <remarks>
    /// A name the processor does not recognise yields an empty string rather than an error, which is what
    /// makes the function usable for feature probing: a stylesheet can ask about a property it may not get.
    /// </remarks>
    internal static class SystemProperty
    {
        /// <summary>What a processor reports as <c>xsl:version</c>.</summary>
        /// <remarks>
        /// §18.2.2 asks for "the version of XSLT implemented by the processor", which is the version the
        /// caller asked for rather than the one the stylesheet claims of itself — this engine holds both
        /// languages and is told which to be. It is the same rule the rest of the vocabulary follows, and it
        /// is what a stylesheet asks when it guards a 3.0 construct with a <c>use-when</c>: the answer has to
        /// be the processor's, or the guard closes over a construct that would have worked.
        /// </remarks>
        /// <param name="implemented">The version this processor was asked to be.</param>
        public static double VersionOf(XsltVersion implemented) => implemented.Number;

        /// <summary>The value reported for <c>xsl:vendor</c>.</summary>
        public const string Vendor = "CodeDeeds";

        /// <summary>
        /// The value reported for <c>xsl:vendor-url</c>.
        /// </summary>
        /// <remarks>
        /// The repository, which is where this engine is published and so the address a stylesheet asking
        /// who wrote it is asking for. It was reported as an empty string — the specified answer for a
        /// property the processor does not provide — for as long as there was no address to give.
        /// </remarks>
        public const string VendorUrl = "https://github.com/CodeDeeds/CodeDeeds.Xslt";

        /// <summary>
        /// The version of XPath this engine offers, reported as <c>xsl:xpath-version</c>.
        /// </summary>
        /// <remarks>
        /// 3.1, because maps, arrays, <c>||</c>, <c>fn:sort</c> and <c>fn:apply</c> are all 3.1 and all
        /// here. It is one number and cannot say "3.1 apart from <c>fn:function-lookup</c>", so the question
        /// is which number misleads less, and 3.0 would disown a good deal that works. What the number
        /// cannot express, <c>function-available()</c> can: it answers for one name and one arity, and it
        /// says no to that one honestly.
        /// </remarks>
        public const string XPathVersion = "3.1";

        /// <summary>
        /// The properties whose answer is fixed for this engine, and what each is.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every one is a plain string from XSLT 2.0 on, and the <c>supports-</c> answers are the engine's
        /// own omissions written down where a stylesheet can read them: no schema to validate against, no
        /// streaming, no namespace axis — and one thing it has, <c>xsl:evaluate</c>, which a caller may
        /// switch off and is then told is not there. Saying so is the point of the
        /// function — a stylesheet that asks can take another route, and one that is told "yes" and then
        /// fails has been misled.
        /// </para>
        /// <para>
        /// <c>product-version</c> is the assembly's, in three parts, which is a package version as the suite's
        /// package-version-010 needs it to be when it sets one from the other.
        /// </para>
        /// </remarks>
        private static readonly string s_productVersion =
            typeof(Xslt).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        private static readonly Dictionary<string, string> s_fixed = new(StringComparer.Ordinal)
        {
            ["vendor"] = Vendor,
            ["vendor-url"] = VendorUrl,
            ["product-name"] = "CodeDeeds.Xslt",
            ["product-version"] = s_productVersion,
            ["is-schema-aware"] = "no",
            ["supports-serialization"] = "yes",
            ["supports-backwards-compatibility"] = "yes",
            ["supports-namespace-axis"] = "no",
            ["supports-streaming"] = "no",
            ["supports-dynamic-evaluation"] = "yes",
            ["supports-higher-order-functions"] = "yes",
            ["xpath-version"] = XPathVersion,
            ["xsd-version"] = "1.0",
        };

        /// <summary>
        /// Returns the value of a system property, or an empty string when it is not one this engine reports.
        /// </summary>
        /// <param name="namespaceUri">The property name's namespace URI.</param>
        /// <param name="localName">The property name's local part.</param>
        /// <param name="version">
        /// The version in force. XSLT 1.0 makes <c>xsl:version</c> a number and 2.0 makes every property a
        /// string, and a stylesheet written for either compares the answer the way its own version says to.
        /// </param>
        /// <param name="implemented">
        /// The version this processor was asked to be, which is what <c>xsl:version</c> reports. The two
        /// versions are different questions and a stylesheet may ask both: what it says of itself decides
        /// how it reads the answer, and what the processor is decides the answer.
        /// </param>
        /// <param name="dynamicEvaluation">Whether <c>xsl:evaluate</c> is switched on, which is what
        /// <c>xsl:supports-dynamic-evaluation</c> answers.</param>
        public static XPathValue Lookup(
            string namespaceUri,
            string localName,
            XsltVersion version,
            XsltVersion implemented,
            bool dynamicEvaluation = true,
            bool schemaAware = false)
        {
            if (localName == "supports-dynamic-evaluation" && !dynamicEvaluation)
            {
                return XPathValue.FromString("no");
            }

            if (localName == "is-schema-aware" && namespaceUri == StylesheetCompiler.XsltNamespace)
            {
                return XPathValue.FromString(schemaAware ? "yes" : "no");
            }

            if (namespaceUri != StylesheetCompiler.XsltNamespace)
            {
                return XPathValue.FromString(string.Empty);
            }

            // The one property whose type depends on the version in force: XSLT 1.0 made it a number and 2.0
            // made every property a string, so a stylesheet written for either compares it the way its own
            // version says to.
            if (localName == "version")
            {
                double number = VersionOf(implemented);

                return version.IsBackwardsCompatible
                    ? XPathValue.FromNumber(number)
                    : XPathValue.FromString(number.ToString("0.0", CultureInfo.InvariantCulture));
            }

            return XPathValue.FromString(
                s_fixed.TryGetValue(localName, out string? answer) ? answer : string.Empty);
        }

        /// <summary>
        /// The names of every property this engine answers, which is what <c>available-system-properties()</c>
        /// returns: <c>xsl:version</c> and the fixed ones, each as a QName.
        /// </summary>
        public static XPathValue AvailableNames()
        {
            List<XPathValue> names = new List<XPathValue>(s_fixed.Count + 1)
            {
                XPathValue.FromQName(new XdmQName("xsl", StylesheetCompiler.XsltNamespace, "version")),
            };

            foreach (string name in s_fixed.Keys)
            {
                names.Add(XPathValue.FromQName(new XdmQName("xsl", StylesheetCompiler.XsltNamespace, name)));
            }

            return XdmSequence.Concatenate(names);
        }
    }

    /// <summary>
    /// A function whose argument is a qualified name that was not written as a literal, and so has to be
    /// resolved while the transformation runs.
    /// </summary>
    /// <remarks>
    /// A prefix means whatever it meant where the call was written, so the bindings in scope there are
    /// captured at compile time; there is nothing at run time that could resolve them. Each of the three
    /// functions that take a name this way — <c>system-property()</c>, <c>element-available()</c> and
    /// <c>function-available()</c> — answers rather than fails when asked about something it does not know,
    /// which is what makes them usable for probing, so an unbound prefix yields that same answer.
    /// </remarks>
    internal abstract class QualifiedNameProbeExpr : Expr
    {
        private readonly Expr m_name;
        private readonly Dictionary<string, string> m_prefixes;

        /// <summary>Initializes a call taking a qualified name.</summary>
        /// <param name="name">The expression giving the name.</param>
        /// <param name="prefixes">The namespace bindings in scope where the call was written.</param>
        protected QualifiedNameProbeExpr(Expr name, Dictionary<string, string> prefixes)
        {
            m_name = name;
            m_prefixes = prefixes;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_name };

        /// <summary>Gets the answer for a name this function knows nothing about.</summary>
        protected abstract XPathValue Unknown { get; }

        /// <summary>Answers for one resolved name.</summary>
        /// <param name="namespaceUri">The name's namespace URI, empty when it was written without a prefix.</param>
        /// <param name="localName">The name's local part.</param>
        protected abstract XPathValue Lookup(string namespaceUri, string localName);

        /// <summary>
        /// Gets the code for an argument that is not a name this function could look anything up under.
        /// </summary>
        /// <remarks>
        /// Each of these functions asks whether something exists, and the answer for a name that could not
        /// exist is not "no" — the question was malformed. The specification gives every one of them its own
        /// code for it.
        /// </remarks>
        protected abstract XsltErrorCode BadName { get; }

        /// <summary>
        /// Answers for one resolved name, with the context to hand.
        /// </summary>
        /// <remarks>
        /// Exists for <c>function-available()</c>, whose second argument is an expression and so cannot be
        /// read from the parameterless overload. Everything else here asks a question the context has no
        /// part in, and answers through that one.
        /// </remarks>
        /// <param name="namespaceUri">The name's namespace URI, empty when it was written without a prefix.</param>
        /// <param name="localName">The name's local part.</param>
        /// <param name="context">The context the call is being evaluated in.</param>
        protected virtual XPathValue Lookup(
            string namespaceUri,
            string localName,
            ref DynamicContext context)
        {
            return Lookup(namespaceUri, localName);
        }

        /// <summary>
        /// Gets the namespace a name written without a prefix belongs to.
        /// </summary>
        /// <remarks>
        /// None, for everything but a function. A name written without a prefix in a <em>function call</em>
        /// means the default function namespace, so <c>function-available('translate')</c> asks about
        /// <c>fn:translate</c> — which is what makes <c>Q{}abs</c> a different question, since that names a
        /// function in no namespace and there is no such thing here.
        /// </remarks>
        protected virtual string DefaultNamespace => string.Empty;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            string qualifiedName = m_name.Evaluate(ref context).ToStringValue().Trim();

            // The braced form, which XPath 3.0 lets a name be written in and which these functions take as
            // readily as any other string. It carries its own namespace, so no prefix has to be in scope —
            // and Q{} is how a name says it is in no namespace at all.
            if (qualifiedName.StartsWith("Q{", StringComparison.Ordinal))
            {
                int close = qualifiedName.IndexOf('}');

                if (close < 0 || !XdmQName.TrySplit(qualifiedName[(close + 1)..], out string braced, out string named)
                    || braced.Length != 0)
                {
                    throw XsltErrors.Error(
                        BadName, $"'{qualifiedName}' is not a name, so nothing can be looked up under it.");
                }

                return Lookup(qualifiedName[2..close], named, ref context);
            }

            if (!XdmQName.TrySplit(qualifiedName, out string prefix, out string localName))
            {
                throw XsltErrors.Error(
                    BadName, $"'{qualifiedName}' is not a name, so nothing can be looked up under it.");
            }

            if (prefix.Length == 0)
            {
                return Lookup(DefaultNamespace, localName, ref context);
            }

            return m_prefixes.TryGetValue(prefix, out string? uri)
                ? Lookup(uri, localName, ref context)
                : throw XsltErrors.Error(
                    BadName,
                    $"'{qualifiedName}' uses the prefix '{prefix}', which nothing in scope where the call "
                    + "was written binds.");
        }
    }

    /// <summary>
    /// A call to <c>system-property()</c> whose argument is not a literal.
    /// </summary>
    internal sealed class SystemPropertyExpr : QualifiedNameProbeExpr
    {
        private readonly XsltVersion m_version;
        private readonly XsltVersion m_implemented;
        private readonly bool m_dynamicEvaluation;
        private readonly bool m_schemaAware;

        /// <summary>Initializes a call to <c>system-property()</c>.</summary>
        /// <param name="name">The expression giving the property name.</param>
        /// <param name="prefixes">The namespace bindings in scope where the call was written.</param>
        /// <param name="version">The version in force, which decides the type of <c>xsl:version</c>.</param>
        /// <param name="implemented">The version this processor was asked to be, which is what it reports.</param>
        /// <param name="dynamicEvaluation">Whether <c>xsl:evaluate</c> is switched on.</param>
        /// <param name="schemaAware">Whether the processor was asked to be schema-aware.</param>
        public SystemPropertyExpr(
            Expr name,
            Dictionary<string, string> prefixes,
            XsltVersion version,
            XsltVersion implemented,
            bool dynamicEvaluation = true,
            bool schemaAware = false)
            : base(name, prefixes)
        {
            m_version = version;
            m_implemented = implemented;
            m_dynamicEvaluation = dynamicEvaluation;
            m_schemaAware = schemaAware;
        }

        /// <inheritdoc/>
        protected override XPathValue Unknown => XPathValue.FromString(string.Empty);
        /// <inheritdoc/>
        protected override XsltErrorCode BadName => XsltErrorCode.XTDE1390;

        /// <inheritdoc/>
        protected override XPathValue Lookup(string namespaceUri, string localName)
        {
            return SystemProperty.Lookup(
                namespaceUri, localName, m_version, m_implemented, m_dynamicEvaluation, m_schemaAware);
        }
    }

    /// <summary>
    /// A call to <c>type-available()</c> whose argument is not a literal.
    /// </summary>
    internal sealed class TypeAvailableExpr : QualifiedNameProbeExpr
    {
        private readonly bool m_implements30;
        private readonly SchemaComponents? m_schemas;

        /// <summary>Initializes a call to <c>type-available()</c>.</summary>
        /// <param name="name">The expression giving the type name.</param>
        /// <param name="prefixes">The namespace bindings in scope where the call was written.</param>
        /// <param name="implements30">Whether this processor implements XSLT 3.0.</param>
        /// <param name="schemas">The schema components in scope, or null for a processor with none.</param>
        public TypeAvailableExpr(
            Expr name, Dictionary<string, string> prefixes, bool implements30, SchemaComponents? schemas = null)
            : base(name, prefixes)
        {
            m_implements30 = implements30;
            m_schemas = schemas;
        }

        /// <inheritdoc/>
        protected override XPathValue Unknown => XPathValue.FromBoolean(false);
        /// <inheritdoc/>
        protected override XsltErrorCode BadName => XsltErrorCode.XTDE1428;

        /// <inheritdoc/>
        protected override XPathValue Lookup(string namespaceUri, string localName)
        {
            return XPathValue.FromBoolean(
                Availability.IsTypeAvailable(namespaceUri, localName, m_implements30)
                || m_schemas?.IsTypeAvailable(namespaceUri, localName) == true);
        }
    }

    /// <summary>
    /// A call to <c>element-available()</c> whose argument is not a literal.
    /// </summary>
    internal sealed class ElementAvailableExpr : QualifiedNameProbeExpr
    {
        private readonly bool m_implements30;
        private readonly bool m_dynamicEvaluation;

        /// <summary>Initializes a call to <c>element-available()</c>.</summary>
        /// <param name="name">The expression giving the element name.</param>
        /// <param name="prefixes">The namespace bindings in scope where the call was written.</param>
        /// <param name="implements30">Whether the processor claims XSLT 3.0.</param>
        /// <param name="dynamicEvaluation">Whether <c>xsl:evaluate</c> is switched on.</param>
        public ElementAvailableExpr(
            Expr name, Dictionary<string, string> prefixes, bool implements30 = true, bool dynamicEvaluation = true)
            : base(name, prefixes)
        {
            m_implements30 = implements30;
            m_dynamicEvaluation = dynamicEvaluation;
            m_default = prefixes.TryGetValue(string.Empty, out string? declared) ? declared : string.Empty;
        }

        private readonly string m_default;

        /// <summary>
        /// The namespace a name written without a prefix is in, which here is the default namespace.
        /// </summary>
        /// <remarks>
        /// The one this function asks about is an element's, so a name with no prefix means what an element
        /// name with no prefix would mean where the call was written: whatever <c>xmlns</c> last declared.
        /// Everything else that takes a name this way is asking about something an element declaration says
        /// nothing about, and reads a bare name as being in no namespace.
        /// </remarks>
        protected override string DefaultNamespace => m_default;

        /// <inheritdoc/>
        protected override XPathValue Unknown => XPathValue.FromBoolean(false);
        /// <inheritdoc/>
        protected override XsltErrorCode BadName => XsltErrorCode.XTDE1440;

        /// <inheritdoc/>
        protected override XPathValue Lookup(string namespaceUri, string localName)
        {
            return XPathValue.FromBoolean(
                Availability.IsElementAvailable(namespaceUri, localName, m_implements30, m_dynamicEvaluation));
        }
    }

    /// <summary>
    /// A call to <c>function-available()</c> whose argument is not a literal.
    /// </summary>
    internal sealed class FunctionAvailableExpr : QualifiedNameProbeExpr
    {
        private readonly Expr? m_arity;
        private readonly XsltVersion m_version;
        private readonly XsltVersion m_syntaxVersion;
        private readonly IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> m_declared;

        /// <summary>Initializes a call to <c>function-available()</c>.</summary>
        /// <param name="name">The expression giving the function name.</param>
        /// <param name="prefixes">The namespace bindings in scope where the call was written.</param>
        /// <param name="declared">The functions the stylesheet declares, by name and arity.</param>
        /// <param name="version">The version the question is asked under.</param>
        /// <param name="syntaxVersion">
        /// The version this processor implements, which is what decides whether a later language's
        /// function is one a call may be written to at all. The two are different questions and the
        /// answer needs both: a version="2.0" stylesheet on a 3.0 processor may call fn:snapshot(), and
        /// must be told so.
        /// </param>
        /// <param name="arity">The second argument, or null where the call gave one argument.</param>
        public FunctionAvailableExpr(
            Expr name,
            Dictionary<string, string> prefixes,
            IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> declared,
            XsltVersion version,
            XsltVersion syntaxVersion,
            Expr? arity = null,
            bool staticContext = false)
            : base(name, prefixes)
        {
            m_declared = declared;
            m_version = version;
            m_syntaxVersion = syntaxVersion;
            m_arity = arity;
            m_staticContext = staticContext;
        }

        /// <summary>Whether the call stands in a static expression, whose library is the narrower one.</summary>
        private readonly bool m_staticContext;

        /// <summary>
        /// Whether a name is one this call has to answer "no" for, whatever the processor implements.
        /// </summary>
        /// <remarks>
        /// A <c>function-available()</c> written in a <c>use-when</c> is asking what <em>that expression</em>
        /// may call, not what the stylesheet at large may: the two static contexts are different, and this is
        /// the one place where the answer depends on where the question was written.
        /// </remarks>
        private bool Barred(string namespaceUri, string localName)
        {
            return m_staticContext
                && (namespaceUri.Length == 0 || namespaceUri == XdmType.FunctionNamespace)
                && Availability.IsBarredFromStaticExpressions(
                    localName, m_version.CompareTo(XsltVersion.V30) >= 0);
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children =>
            m_arity is null ? base.Children : new List<Expr>(base.Children) { m_arity };

        /// <inheritdoc/>
        protected override XPathValue Unknown => XPathValue.FromBoolean(false);
        /// <inheritdoc/>
        protected override XsltErrorCode BadName => XsltErrorCode.XTDE1400;

        /// <inheritdoc/>
        protected override string DefaultNamespace => XdmType.FunctionNamespace;

        /// <inheritdoc/>
        protected override XPathValue Lookup(string namespaceUri, string localName)
        {
            return XPathValue.FromBoolean(
                !Barred(namespaceUri, localName)
                && Availability.IsFunctionAvailable(
                    namespaceUri, localName, m_version, m_syntaxVersion));
        }

        /// <summary>
        /// Answers for a resolved name, having first sent an unprefixed one to the function namespace.
        /// </summary>
        /// <remarks>
        /// A name written without a prefix in a function call means the default function namespace, so
        /// <c>function-available('translate')</c> asks about <c>fn:translate</c> and not about a
        /// namespace-less function of that name. Which is why <c>Q{}abs</c> is a different question and gets
        /// a different answer: it says outright that the function is in no namespace, and no function here
        /// is.
        /// </remarks>
        /// <param name="namespaceUri">The name's namespace URI, empty when it was written without a prefix.</param>
        /// <param name="localName">The name's local part.</param>
        /// <param name="context">The context the call is being evaluated in.</param>
        protected override XPathValue Lookup(
            string namespaceUri,
            string localName,
            ref DynamicContext context)
        {
            ExpandedName name = new ExpandedName(namespaceUri, localName);

            if (m_arity is null)
            {
                // A stylesheet function is available whatever arity it was declared at, so any entry under
                // the name will do. Asked before the library, because a stylesheet may declare a function
                // in a namespace the library knows nothing about, which is the usual case.
                foreach ((ExpandedName declared, int _) in m_declared.Keys)
                {
                    if (declared.Equals(name))
                    {
                        return XPathValue.FromBoolean(true);
                    }
                }

                return Lookup(namespaceUri, localName);
            }

            long asked = XdmSequence
                .RequireSingleItem(m_arity.Evaluate(ref context), "the arity fn:function-available() asks about")
                .ToInteger();

            return XPathValue.FromBoolean(
                asked >= 0
                && asked <= int.MaxValue
                && !Barred(namespaceUri, localName)
                && (m_declared.ContainsKey((name, (int)asked))
                    || Availability.IsFunctionAvailable(
                        namespaceUri, localName, (int)asked, m_version, m_syntaxVersion)));
        }
    }

    /// <summary>
    /// What <c>element-available()</c> and <c>function-available()</c> answer.
    /// </summary>
    /// <remarks>
    /// These two functions are how a stylesheet finds out what it is running on, so the answers have to be
    /// drawn from what this engine actually implements rather than from what XSLT 1.0 defines. <c>id()</c>
    /// was reported unavailable while DTD processing was prohibited and it had no ID attributes to find;
    /// a document type declaration is read now, and it answers for what one declares and for <c>xml:id</c>.
    /// </remarks>
    internal static class Availability
    {
        /// <summary>
        /// The name of the one function this compiler builds that no library has.
        /// </summary>
        /// <remarks>
        /// It is an XPath function and belongs in a library by rights, but it needs the namespaces in scope
        /// where the call was written, and a library is given values rather than a stylesheet.
        /// </remarks>
        public const string FunctionLookup = "function-lookup";

        /// <summary>
        /// The XSLT elements that may appear in a template body. Only these count for
        /// <c>element-available()</c>: a declaration such as <c>xsl:template</c> is not an instruction, and
        /// neither is <c>xsl:sort</c> or <c>xsl:when</c>, which belong to the instruction that contains them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The answer does not depend on the version the stylesheet declares. The question is what this engine
        /// can instantiate, and that is the same whichever version is in force — a 1.0 stylesheet asking about
        /// <c>xsl:for-each-group</c> is asking whether writing one would work, and here it would.
        /// </para>
        /// <para>
        /// <c>xsl:result-document</c> is reported available even when no <c>XsltOptions.ResultResolver</c> is
        /// configured. The instruction is implemented; whether a particular run may write anywhere is the
        /// caller's decision, made after the stylesheet was compiled and changeable through
        /// <see cref="Xslt.With"/> without recompiling it.
        /// </para>
        /// </remarks>
        private static readonly HashSet<string> s_instructions = new(StringComparer.Ordinal)
        {
            // XSLT 1.0.
            "apply-imports",
            "apply-templates",
            "attribute",
            "call-template",
            "choose",
            "comment",
            "copy",
            "copy-of",
            "element",
            "fallback",
            "for-each",
            "if",
            "message",
            "number",
            "processing-instruction",
            "text",
            "value-of",
            "variable",

            // XSLT 2.0. Absent from here: xsl:function and xsl:character-map, which are declarations rather
            // than instructions.
            "analyze-string",
            "document",
            "for-each-group",
            "namespace",
            "next-match",
            "perform-sort",
            "result-document",
            "sequence",

        };

        /// <summary>
        /// The functions XSLT adds to the XPath core library that this engine implements, with how many
        /// arguments each will take. They live in no namespace, so they are named without a prefix.
        /// </summary>
        /// <remarks>
        /// The arities are written out here and read from the libraries elsewhere, because these are the
        /// functions whose calls this compiler builds by hand rather than from a registry — there is no
        /// entry to ask. Where a range is a single number the two bounds are the same.
        /// </remarks>
        private static readonly Dictionary<string, (int Least, int Most)> s_functions =
            new(StringComparer.Ordinal)
            {
                ["current"] = (0, 0),
                ["current-group"] = (0, 0),
                ["current-merge-group"] = (0, 1),
                ["current-merge-key"] = (0, 0),
                ["current-output-uri"] = (0, 0),
                ["accumulator-before"] = (1, 1),
                ["accumulator-after"] = (1, 1),
                ["copy-of"] = (0, 1),
                ["snapshot"] = (0, 1),
                ["current-grouping-key"] = (0, 0),
                ["regex-group"] = (1, 1),
                ["document"] = (1, 2),
                ["element-available"] = (1, 1),
                ["format-number"] = (2, 3),
                ["function-available"] = (1, 2),
                ["generate-id"] = (0, 1),
                ["id"] = (1, 2),
                ["element-with-id"] = (1, 2),
                ["idref"] = (1, 2),
                ["key"] = (2, 3),
                ["system-property"] = (1, 1),
                ["available-system-properties"] = (0, 0),
                ["stream-available"] = (1, 1),
                ["transform"] = (1, 1),
                ["type-available"] = (1, 1),
                ["unparsed-text"] = (1, 2),
                ["unparsed-text-available"] = (1, 2),
                ["unparsed-entity-uri"] = (1, 2),
                ["unparsed-entity-public-id"] = (1, 2),
            };

        /// <summary>
        /// The XSLT functions above whose calls this compiler builds only where it is a 3.0 processor.
        /// </summary>
        /// <remarks>
        /// The table above says what a call may be written as; this says when. Both are read here rather
        /// than one being inferred from the other, because the question is the same one the build site asks
        /// -- <c>Implements30</c>, which is about the processor and not about what the stylesheet says of
        /// itself -- and a stylesheet told a function is available and then refused the call has been
        /// misled about the one thing this function exists to answer.
        /// </remarks>
        private static readonly HashSet<string> s_functionsAddedIn30 = new(StringComparer.Ordinal)
        {
            "current-merge-group",
            "current-merge-key",
            "current-output-uri",
            "accumulator-before",
            "accumulator-after",
            "copy-of",
            "snapshot",
            "available-system-properties",
            "stream-available",
            "transform",
        };

        /// <summary>
        /// The XSLT functions a static expression may not call.
        /// </summary>
        /// <remarks>
        /// A <c>use-when</c> and a shadow attribute are answered while the stylesheet is being prepared, so
        /// every function that reads the source document or the state of a running transformation has
        /// nothing to read (XSLT 3.0 §3.11.2). Asked for by name it is <c>XPST0017</c>, and
        /// <c>function-available()</c> written in the same expression has to agree — that is the whole of
        /// what the suite's use-when-0407 checks.
        /// </remarks>
        private static readonly HashSet<string> s_barredFromStatic = new(StringComparer.Ordinal)
        {
            "current",
            "current-group",
            "current-grouping-key",
            "current-merge-group",
            "current-merge-key",
            "current-output-uri",
            "regex-group",
            "key",
            "unparsed-entity-uri",
            "unparsed-entity-public-id",
            "accumulator-before",
            "accumulator-after",
        };

        /// <summary>
        /// Whether a function of the XSLT library is out of reach of a static expression.
        /// </summary>
        /// <remarks>
        /// <c>generate-id()</c> is the one the version decides. It was XSLT's own, and about the document
        /// being transformed, until 3.0 made it <c>fn:generate-id</c> — a function of the node it is given
        /// and of nothing else, which a static expression may call as readily as any other.
        /// </remarks>
        /// <param name="localName">The function's local name, in the XSLT or the function namespace.</param>
        /// <param name="implements30">Whether the processor claims XSLT 3.0.</param>
        public static bool IsBarredFromStaticExpressions(string localName, bool implements30)
        {
            return s_barredFromStatic.Contains(localName)
                || (localName == "generate-id" && !implements30);
        }

        /// <summary>Returns whether a name denotes an instruction this engine implements.</summary>
        /// <param name="namespaceUri">The element name's namespace URI.</param>
        /// <param name="localName">The element name's local part.</param>
        /// <param name="implements30">Whether the processor claims XSLT 3.0, which is when its instructions
        /// are there to be asked about.</param>
        /// <param name="dynamicEvaluation">Whether <c>xsl:evaluate</c> is switched on; switched off, it is
        /// not available, which is what the specification says of the statically disabled feature.</param>
        public static bool IsElementAvailable(
            string namespaceUri, string localName, bool implements30 = true, bool dynamicEvaluation = true)
        {
            // Anything outside the XSLT namespace would be an extension element, and EXSLT's exsl:document
            // is the one implemented. Answered whatever version is in force: EXSLT is not a version of XSLT,
            // and a 1.0 stylesheet asking is the whole reason the element is here.
            if (namespaceUri != StylesheetCompiler.XsltNamespace)
            {
                return namespaceUri == ExsltFunctionExpr.CommonNamespace
                    && localName == ExsltFunctionExpr.DocumentElement;
            }

            // XSLT 3.0 widened the question from the instructions to every element the specification defines
            // (§24.2.2): xsl:catch is available where xsl:try is. Below 3.0 it is the instructions alone, so
            // a stylesheet asking a 2.0 processor about xsl:try is told no, and the fallback it wrote for
            // that answer is what it gets.
            if (implements30)
            {
                return XsltElements.Find(localName) is not null && (dynamicEvaluation || localName != "evaluate");
            }

            return s_instructions.Contains(localName);
        }

        /// <summary>
        /// The types every processor has in scope without a schema at XSLT 2.0.
        /// </summary>
        /// <remarks>
        /// §3.13: the primitive atomic types except <c>xs:NOTATION</c>, <c>xs:integer</c>, and the five
        /// XPath adds. <c>xs:anyType</c> and <c>xs:anySimpleType</c> are on the list too and are answered
        /// before this set is asked, being the two names no table of values holds. Everything else — the
        /// other derived types, the list types, <c>xs:NOTATION</c> itself — waits for a schema-aware
        /// processor, which this is not. XSLT 3.0 dropped the distinction and gives every processor the
        /// whole of XML Schema Part 2, which is why this is the 2.0 list and not a list.
        /// </remarks>
        private static readonly HashSet<string> s_typesInScopeAt20 = new(StringComparer.Ordinal)
        {
            "string", "boolean", "decimal", "double", "float", "date", "time", "dateTime", "duration",
            "QName", "anyURI", "gDay", "gMonthDay", "gMonth", "gYearMonth", "gYear", "base64Binary",
            "hexBinary", "integer",
            "yearMonthDuration", "dayTimeDuration", "anyAtomicType", "untyped", "untypedAtomic",
        };

        /// <summary>
        /// Returns whether a name denotes a type in scope, which is what <c>type-available()</c> answers.
        /// </summary>
        /// <remarks>
        /// A different question from whether a value of it can be built, which is what the table of types
        /// holds and what <see cref="IsFunctionAvailable(string, string)"/> asks: <c>xs:anyType</c> is in
        /// scope and constructs nothing, and at 2.0 <c>xs:int</c> constructs something and is not in scope.
        /// Which types are in scope is the processor's version to say — 2.0 gives a basic processor a short
        /// list and the rest to a schema-aware one, where 3.0 gives every processor all of XML Schema Part
        /// 2 — so the answer follows the version this engine implements, as the rest of the vocabulary does.
        /// </remarks>
        /// <param name="namespaceUri">The type name's namespace URI.</param>
        /// <param name="localName">The type name's local part.</param>
        /// <param name="implements30">Whether this processor implements XSLT 3.0.</param>
        public static bool IsTypeAvailable(string namespaceUri, string localName, bool implements30)
        {
            // Only the schema namespace has anything in it: a stylesheet cannot define a type here, and
            // there is no schema to have defined any.
            if (namespaceUri != XdmType.SchemaNamespace)
            {
                return false;
            }

            // Neither is an atomic type and neither has a constructor, so neither is in the table of types
            // a value can be built as — but both are named type definitions every processor has in scope,
            // which is the question being asked.
            if (localName is "anyType" or "anySimpleType")
            {
                return true;
            }

            if (!XdmType.TryGet(localName, out _) && !XdmType.TryGetList(localName, out _))
            {
                return false;
            }

            return implements30 || s_typesInScopeAt20.Contains(localName);
        }

        /// <summary>
        /// The functions XSLT adds, with the arities each takes, for anything that has to enumerate them.
        /// </summary>
        public static IReadOnlyDictionary<string, (int Least, int Most)> XsltFunctions => s_functions;

        /// <summary>
        /// Returns whether a name denotes a function this engine implements.
        /// </summary>
        /// <param name="namespaceUri">The function name's namespace URI.</param>
        /// <param name="localName">The function name's local part.</param>
        public static bool IsFunctionAvailable(
            string namespaceUri, string localName, XsltVersion version, XsltVersion syntaxVersion)
        {
            bool thirty = version.CompareTo(XsltVersion.V30) >= 0
                || syntaxVersion.CompareTo(XsltVersion.V30) >= 0;

            // Most built-in schema types are constructible by calling the name, so they are functions too.
            if (namespaceUri == XdmType.SchemaNamespace)
            {
                return (XdmType.TryGet(localName, out _) || XdmType.TryGetList(localName, out _))
                    && XdmType.HasConstructor(localName)
                    && (thirty || !XdmType.ConstructorAddedInThree(localName));
            }

            if (namespaceUri == MapArrayFunctionExpr.MapNamespace
                || namespaceUri == MapArrayFunctionExpr.ArrayNamespace)
            {
                return thirty && MapArrayFunctionExpr.TakesArity(namespaceUri, localName, -1);
            }

            if (namespaceUri == Xpath30FunctionExpr.MathNamespace)
            {
                return thirty && Xpath30FunctionExpr.TakesArityInMath(localName, -1);
            }

            if (namespaceUri == ExsltFunctionExpr.CommonNamespace)
            {
                return ExsltFunctionExpr.TakesArity(namespaceUri, localName, -1);
            }

            // The core library lives in the function namespace, and that is where a name written without a
            // prefix has already been sent, so fn:doc and doc ask the same question. Any other namespace
            // names an extension function, and the EXSLT Common module just above is the only one this
            // engine provides — a name that says outright it is in no namespace, which only Q{} can say,
            // therefore names nothing here.
            return namespaceUri == XdmType.FunctionNamespace
                && (s_functions.ContainsKey(localName)
                    || localName == FunctionLookup
                    || FunctionCallExpr.IsKnown(localName)
                    || Xpath2FunctionExpr.IsKnown(localName)
                    || HigherOrderFunctionExpr.TakesArity(localName, -1)
                    || Xpath30FunctionExpr.TakesArity(localName, 1)
                    || Xpath30FunctionExpr.TakesArity(localName, 2)
                    || JsonFunctionExpr.TakesArity(localName, 1)
                    || NodeBuildingFunctionExpr.TakesArity(localName, 1)
                    || NodeBuildingFunctionExpr.TakesArity(localName, 2));
        }

        /// <summary>
        /// Returns whether a function of this name will take a given number of arguments.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What <c>function-available()</c>'s second argument asks. Every answer comes from the same table
        /// the call itself is built against, so the two cannot come apart: a name reported available at an
        /// arity is one a call may be written at, and one reported unavailable is one that would be refused.
        /// </para>
        /// <para>
        /// A name may be in more than one library at more than one arity — <c>fn:round</c> takes one
        /// argument in the 2.0 library and two in the 3.0 one, and <c>fn:string-join</c> the reverse — so
        /// this is a union across them and not a lookup in the first that has the name.
        /// </para>
        /// </remarks>
        /// <param name="namespaceUri">The name's namespace, empty where it was written without a prefix.</param>
        /// <param name="localName">The name's local part.</param>
        /// <param name="arity">How many arguments the caller is asking about.</param>
        /// <param name="version">The version the question is asked under.</param>
        public static bool IsFunctionAvailable(
            string namespaceUri,
            string localName,
            int arity,
            XsltVersion version,
            XsltVersion syntaxVersion)
        {
            // The libraries a later language brought are reachable when either version is 3.0: the
            // processor's, because that is what the engine reads, or the stylesheet's own claim, because a
            // 3.0 stylesheet on a 2.0 processor is processed forwards-compatibly and its calls are built.
            // Exactly the test FunctionLibrary.Core makes before consulting them, and made here for the
            // same reason the arities are read from the same tables: the answer and the call cannot come
            // apart.
            bool thirty = version.CompareTo(XsltVersion.V30) >= 0
                || syntaxVersion.CompareTo(XsltVersion.V30) >= 0;
            // A type is constructible by calling its name with one argument, so most built-in schema types
            // are functions of arity one. Reported here rather than left to type-available(), because a
            // stylesheet asking whether it can write xs:integer($x) is asking about a function - and the
            // four types that have no constructor are exactly where the two questions come apart.
            if (namespaceUri == XdmType.SchemaNamespace)
            {
                // The three list types have constructors too, and they are held apart from the atomic
                // ones because nothing may be an instance of a list type. A cast is what a constructor is,
                // and the cast to one is defined here, so the function is there to be called. Those three,
                // and xs:error and xs:numeric, are 3.0's and are not there before it.
                return arity == 1
                    && (XdmType.TryGet(localName, out _) || XdmType.TryGetList(localName, out _))
                    && XdmType.HasConstructor(localName)
                    && (thirty || !XdmType.ConstructorAddedInThree(localName));
            }

            if (namespaceUri == MapArrayFunctionExpr.MapNamespace
                || namespaceUri == MapArrayFunctionExpr.ArrayNamespace)
            {
                return thirty && MapArrayFunctionExpr.TakesArity(namespaceUri, localName, arity);
            }

            if (namespaceUri == Xpath30FunctionExpr.MathNamespace)
            {
                return thirty && Xpath30FunctionExpr.TakesArityInMath(localName, arity);
            }

            if (namespaceUri == ExsltFunctionExpr.CommonNamespace)
            {
                return ExsltFunctionExpr.TakesArity(namespaceUri, localName, arity);
            }

            if (namespaceUri != XdmType.FunctionNamespace)
            {
                return false;
            }

            if (s_functions.TryGetValue(localName, out (int Least, int Most) xslt)
                && arity >= xslt.Least
                && arity <= xslt.Most)
            {
                // Only where the compiler would build the call. These are XSLT's own functions, built by
                // hand rather than from a library, and the build site asks about the processor alone.
                return !s_functionsAddedIn30.Contains(localName)
                    || syntaxVersion.CompareTo(XsltVersion.V30) >= 0;
            }

            // Answered on its own because it is built by the compiler rather than by a library: it needs the
            // namespaces in scope where it was written, and no library has those. Kept out of the table above
            // for the same reason - what that table drives is building every one of those functions, and
            // building this one from inside that loop would ask it to build itself.
            if (localName == FunctionLookup)
            {
                return thirty && arity == 2;
            }

            return FunctionCallExpr.TakesArity(localName, arity, version)
                || Xpath2FunctionExpr.TakesArity(localName, arity, version)
                || (thirty
                    && (HigherOrderFunctionExpr.TakesArity(localName, arity)
                        || Xpath30FunctionExpr.TakesArity(localName, arity)
                        || JsonFunctionExpr.TakesArity(localName, arity)
                        || NodeBuildingFunctionExpr.TakesArity(localName, arity)));
        }
    }

    /// <summary>
    /// <c>fn:stream-available</c>, which answers whether a document could be read as a stream.
    /// </summary>
    /// <remarks>
    /// The answer is about the document rather than about the processor: whether it is there and begins as
    /// XML. See <see cref="XsltRuntime.StartsAsXml"/> for why that is the question, and for what a
    /// document that goes wrong later answers.
    /// </remarks>
    internal sealed class StreamAvailableExpr : Expr
    {
        private readonly Expr m_uri;
        private readonly string? m_baseUri;

        /// <summary>Initializes a call.</summary>
        /// <param name="uri">The document to ask about.</param>
        /// <param name="baseUri">The base URI where the call is written, which a relative one resolves against.</param>
        public StreamAvailableExpr(Expr uri, string? baseUri)
        {
            m_uri = uri;
            m_baseUri = baseUri;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_uri };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            List<XPathValue> items = XdmSequence.Items(m_uri.Evaluate(ref context));

            // Declared xs:string?, and nothing is available at no URI at all.
            if (items.Count != 1 || context.Runtime is not XsltRuntime runtime)
            {
                return XPathValue.FromBoolean(false);
            }

            return XPathValue.FromBoolean(
                runtime.StartsAsXml(XdmSequence.StringValueOf(items[0]), m_baseUri ?? runtime.BaseUri));
        }
    }

    /// <summary>
    /// A call to an extension function that no implementation was registered for.
    /// </summary>
    /// <remarks>
    /// Rejecting the call where it is written would defeat the point of <c>function-available()</c>: the whole
    /// pattern is to guard a call with a test that keeps it from ever being made, and a stylesheet that fails
    /// to compile never gets to run its guard. So the error waits until the call is actually evaluated, which
    /// is what XSLT prescribes for an unavailable extension function.
    /// </remarks>
    internal sealed class UnavailableFunctionExpr : Expr
    {
        private readonly string m_name;
        private readonly string m_namespaceUri;
        private readonly Expr[] m_arguments;

        /// <summary>Initializes a call to an unavailable extension function.</summary>
        /// <param name="name">The function name as written, including its prefix.</param>
        /// <param name="namespaceUri">The namespace the prefix resolves to.</param>
        /// <param name="arguments">The argument expressions, kept so that they are still compiled.</param>
        public UnavailableFunctionExpr(string name, string namespaceUri, Expr[] arguments)
        {
            m_name = name;
            m_namespaceUri = namespaceUri;
            m_arguments = arguments;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            throw XsltErrors.Error(
                XsltErrorCode.XTDE1425,
                $"No implementation is available for the extension function '{m_name}()' in namespace "
                + $"'{m_namespaceUri}'. Guard the call with function-available('{m_name}') to supply a "
                + "fallback for processors that do not provide it.");
        }
    }

    /// <summary>
    /// The functions that read what an enclosing instruction is doing: <c>current-group()</c>,
    /// <c>current-grouping-key()</c> and <c>regex-group()</c>.
    /// </summary>
    /// <remarks>
    /// Each is only meaningful inside the instruction that gives it a value, and each yields nothing rather
    /// than failing outside one — which is what lets a template be called from inside a grouping body and
    /// from outside it without knowing which.
    /// </remarks>
    internal sealed class ContextualFunctionExpr : Expr
    {
        private readonly string m_name;
        private readonly Expr? m_argument;

        /// <summary>Initializes one of the contextual functions.</summary>
        /// <param name="name">Which function was written.</param>
        /// <param name="argument">The group number, for <c>regex-group()</c>.</param>
        /// <summary>Initializes a call.</summary>
        /// <param name="name">The function's name.</param>
        /// <param name="argument">Its argument, where it takes one.</param>
        /// <param name="refuses">
        /// Whether asking for a group that is not there is an error, which XSLT 3.0 made it and 2.0
        /// answered with nothing. It covers both halves of the same change: no current group at all,
        /// and a group made by group-starting-with or group-ending-with, which has no key.
        /// </param>
        public ContextualFunctionExpr(string name, Expr? argument, bool refuses = true)
        {
            m_name = name;
            m_argument = argument;
            m_refuses = refuses;
        }

        private readonly bool m_refuses;

        /// <summary>
        /// The group being merged, or the error for asking where no merge action is running.
        /// </summary>
        /// <remarks>
        /// Narrower than <c>current-group()</c>, deliberately. The grouping functions stay readable in a
        /// called template, which is what lets one template serve callers on both sides of a grouping; the
        /// merge functions belong to the merge action's own sequence constructor and to nothing it invokes.
        /// So the depth is compared as well as the group being present: a template or a function reached
        /// from the action runs one level down, and there the answer is an error rather than nothing.
        /// </remarks>
        /// <param name="runtime">The transformation in progress.</param>
        /// <param name="code">The code for this particular function.</param>
        /// <param name="called">The function's name, for the message.</param>
        private static MergeGroup InsideAMerge(XsltRuntime runtime, XsltErrorCode code, string called)
        {
            return runtime.CurrentMerge is MergeGroup merged
                && runtime.CallDepth == runtime.CurrentMergeDepth
                    ? merged
                    : throw XsltErrors.Error(
                        code,
                        $"'{called}()' was called where no xsl:merge-action is being evaluated. It reads "
                        + "the group a merge is visiting, and outside the action there is none — a template "
                        + "or function the action calls is already outside it.");
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Not a node set even for current-group(): a group of atomic values is a sequence, and a caller
        /// asking for nodes converts what it gets, as it does for a variable holding nodes.
        /// </remarks>
        public override bool ReturnsNodeSet => false;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children =>
            m_argument is null ? Array.Empty<Expr>() : new[] { m_argument };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XsltRuntime? runtime = context.Runtime;

            if (runtime is null)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            switch (m_name)
            {
                case "current-group":
                    // Outside a grouping body there is no group. XSLT 3.0 makes asking for one an error;
                    // 2.0 answered with the empty sequence, and a 2.0 processor goes on doing so.
                    if (runtime.CurrentGroup is { } inGroup)
                    {
                        return inGroup.Group;
                    }

                    return m_refuses
                        ? throw XsltErrors.Error(
                            XsltErrorCode.XTDE1061,
                            "current-group() is called outside any xsl:for-each-group, where there is no "
                            + "current group.")
                        : XPathValue.FromSequence(XdmSequence.Empty);

                case "current-output-uri":
                    // The base output URI, or the result document being written — and nothing at all in
                    // temporary output state, in a pattern, or in a function item called dynamically, none of
                    // which is writing anywhere (§20.3).
                    return runtime.Output.IsFinalOutput
                        && runtime.PatternDepth == 0
                        && runtime.TemporaryDepth == 0
                        && runtime.CurrentOutputUri is string outputUri
                        ? XPathValue.FromAnyUri(outputUri)
                        : XPathValue.FromSequence(XdmSequence.Empty);

                case "current-grouping-key":
                    // A group made by group-starting-with or group-ending-with has no key, which is the same
                    // error as having no group at all — and under 2.0 the same empty answer.
                    if (runtime.CurrentGroup is { } keyed
                        && (!m_refuses || !Xpath2FunctionExpr.IsEmptySequence(keyed.Key)))
                    {
                        return keyed.Key;
                    }

                    return m_refuses
                        ? throw XsltErrors.Error(
                            XsltErrorCode.XTDE1071,
                            "current-grouping-key() is called where there is no current grouping key: "
                            + "outside any xsl:for-each-group, or in a group that was not made by a key.")
                        : XPathValue.FromSequence(XdmSequence.Empty);

                case "current-merge-group":
                {
                    MergeGroup merged = InsideAMerge(runtime, XsltErrorCode.XTDE3480, "current-merge-group");

                    // With no argument the whole group; with one, only what that named source contributed,
                    // which is how an action tells apart items that merged to the same key.
                    if (m_argument is null)
                    {
                        return merged.Group;
                    }

                    string wanted = m_argument.Evaluate(ref context).ToStringValue();

                    foreach ((ExpandedName name, XPathValue items) in merged.BySource)
                    {
                        if (name.LocalName == wanted)
                        {
                            return items;
                        }
                    }

                    // Not an empty answer. A name no source carries is a question about a source that does
                    // not exist, and answering "nothing" would read as a source that contributed nothing —
                    // which is a thing that happens and means something else. A name that is not a name at
                    // all lands here too, and gets the same code.
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE3490,
                        $"current-merge-group('{wanted}') names a source, and the xsl:merge it is in has "
                        + "none of that name.");
                }

                case "current-merge-key":
                    return InsideAMerge(runtime, XsltErrorCode.XTDE3510, "current-merge-key").Key;

                default:
                {
                    int index = m_argument is null
                        ? 0
                        : (int)Math.Round(m_argument.Evaluate(ref context).ToNumber());

                    // Empty inside a pattern wherever the pattern is written (§17.2): the pattern is not
                    // processing the substring, and the groups are not its to read.
                    string[]? groups = runtime.PatternDepth > 0 ? null : runtime.RegexGroups;

                    // A group that did not participate, or a number outside the pattern, is the empty string.
                    return XPathValue.FromString(
                        groups is not null && index >= 0 && index < groups.Length
                            ? groups[index]
                            : string.Empty);
                }
            }
        }
    }

    /// <summary>
    /// A call to <c>unparsed-entity-uri()</c> or <c>unparsed-entity-public-id()</c>: what the document's
    /// type declaration declared the entity to be.
    /// </summary>
    /// <remarks>
    /// The entities are read off the tree, which recorded them when the declaration was parsed. The
    /// specification's answer for an entity that is not there is a zero-length string; the URI is the
    /// system identifier resolved against the base of the entity that declared it, which the tree settled
    /// where it knew its base and the transformation supplies otherwise. The functions check what they are
    /// given first — a node in a tree rooted at a document node, or a focus that is one — since a
    /// stylesheet asking the question in the wrong place is wrong whatever the answer would have been.
    /// </remarks>
    internal sealed class UnparsedEntityExpr : Expr
    {
        private readonly Expr m_name;
        private readonly Expr? m_node;
        private readonly bool m_uri;

        /// <summary>Initializes the call.</summary>
        /// <param name="name">The entity name.</param>
        /// <param name="node">The node whose document is asked about, or null for the context node.</param>
        /// <param name="uri">Whether the URI is asked for rather than the public identifier.</param>
        public UnparsedEntityExpr(Expr name, Expr? node, bool uri)
        {
            m_name = name;
            m_node = node;
            m_uri = uri;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_node is null ? new[] { m_name } : new[] { m_name, m_node };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            string function = m_uri ? "unparsed-entity-uri()" : "unparsed-entity-public-id()";
            XsltErrorCode code = m_uri ? XsltErrorCode.XTDE1370 : XsltErrorCode.XTDE1380;
            string name = m_name.Evaluate(ref context).ToStringValue();

            XdmTree tree;
            int node;

            if (m_node is not null)
            {
                List<XPathValue> items = XdmSequence.Items(m_node.Evaluate(ref context));

                if (items.Count != 1 || items[0].Kind != XPathValueKind.Node)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        $"The second argument of '{function}' must be one node.");
                }

                tree = items[0].NodeTree;
                node = items[0].NodeId;
            }
            else
            {
                // No context node, or a context item that is not one: each function has a code of its own
                // for that, rather than the general one for a missing focus.
                if (context.Node < 0)
                {
                    throw XsltErrors.Error(
                        code,
                        $"'{function}' asks about the context node's document, and there is no context node here.");
                }

                node = context.Node;
                tree = context.Tree;
            }

            if (tree.KindOf(tree.RootOf(node)) != NodeKind.Root)
            {
                throw XsltErrors.Error(
                    code,
                    $"'{function}' asks about a node in a tree whose root is not a document node, and only "
                    + "a document has entities.");
            }

            if (tree.UnparsedEntities is null || !tree.UnparsedEntities.TryGetValue(name, out UnparsedEntity entity))
            {
                return m_uri ? XPathValue.FromAnyUri(string.Empty) : XPathValue.FromString(string.Empty);
            }

            if (!m_uri)
            {
                return XPathValue.FromString(entity.PublicId ?? string.Empty);
            }

            // Resolved when the declaration was read where the document's base was known then, and against
            // what the transformation knows of the tree where it was not.
            string uri = entity.SystemId;

            if (UriReference.TryParse(uri, out UriReference reference)
                && reference.Scheme is null
                && context.Runtime?.BaseUriOf(tree) is string baseUri)
            {
                uri = DocumentTypeDeclaration.ResolveAgainst(uri, baseUri);
            }

            return XPathValue.FromAnyUri(uri);
        }
    }

    /// <summary>
    /// <c>current()</c>: the item the innermost instruction is processing.
    /// </summary>
    /// <remarks>
    /// An <em>item</em> since XSLT 2.0, not a node: inside <c>xsl:matching-substring</c> it is the piece of
    /// text the branch was given, which no tree holds. Everywhere else it is still a node, so the node-set
    /// form is the one that has to stay cheap.
    /// </remarks>
    internal sealed class CurrentExpr : Expr
    {
        private readonly XsltVersion m_version;

        /// <summary>Initializes a call to <c>current()</c>.</summary>
        /// <param name="version">
        /// The version this was compiled against, which decides whether the current item is worth asking
        /// for as a node before it is asked for as a value — XSLT 1.0 had no instruction that made it
        /// anything else, and a stylesheet written for it seldom uses one.
        /// </param>
        public CurrentExpr(XsltVersion version)
        {
            m_version = version;
        }

        /// <summary>
        /// False. The current item may be an atomic value under every version this engine compiles for,
        /// the backwards-compatible ones included, so nothing here can be promised to be a node.
        /// </summary>
        /// <remarks>
        /// It was once true under backwards-compatible behaviour, XSLT 1.0 having had no instruction that
        /// made the current item anything but a node. A <c>version="1.0"</c> stylesheet on this processor
        /// can still write <c>xsl:for-each select="(3, 0)"</c>, and everything that took the promise then
        /// refused <c>current() = 3</c> as having no current item, and read <c>[current()]</c> as a
        /// boolean where it is a position. What the promise bought, a comparison that builds nothing, is
        /// kept by <see cref="UsuallyReturnsNodeSet"/>, which promises nothing.
        /// </remarks>
        public override bool ReturnsNodeSet => false;

        /// <inheritdoc/>
        internal override bool UsuallyReturnsNodeSet => m_version.IsBackwardsCompatible;

        /// <inheritdoc/>
        internal override XdmTree? TryEvaluateOneNode(ref DynamicContext context, out int node)
        {
            node = context.CurrentNode;
            return node < 0 ? null : context.CurrentTree;
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            if (context.CurrentNode >= 0)
            {
                return XPathValue.FromNodeSet(NodeSet.Singleton(context.CurrentTree, context.CurrentNode));
            }

            // An atomic value being walked is the current item as much as a node is: what the instruction
            // walking it recorded, since the context keeps only a node.
            if (context.Runtime?.CurrentAtomicItem is XPathValue atomic)
            {
                return atomic;
            }

            return XPathValue.FromString(RequireASubstring(ref context));
        }

        /// <inheritdoc/>
        public override XdmTree EvaluateNodes(ref DynamicContext context, List<int> output)
        {
            if (context.CurrentNode < 0)
            {
                // There being a current item that is not a node is one complaint, and there being none at
                // all is another: only the second is XTDE1360.
                if (context.Runtime?.CurrentAtomicItem is null)
                {
                    RequireASubstring(ref context);
                }

                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    "Nodes were required here, and the item being processed is an atomic value rather than "
                    + "a node.");
            }

            output.Add(context.CurrentNode);
            return context.CurrentTree;
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            if (context.CurrentNode >= 0)
            {
                return true;
            }

            // The same three answers Evaluate gives, in the same order. An atomic value being walked was
            // left out here, so 'test="current()"' inside 'xsl:for-each select="(0, 1)"' was refused as
            // having no current item where the select beside it read the number.
            return context.Runtime?.CurrentAtomicItem is XPathValue atomic
                ? atomic.ToBoolean()
                : RequireASubstring(ref context).Length != 0;
        }

        /// <summary>
        /// Returns the current item where it is not a node, and refuses the call where there is none.
        /// </summary>
        /// <remarks>
        /// The only current item XSLT 2.0 makes that is not a node is the piece of text an
        /// <c>xsl:analyze-string</c> branch was given, which the runtime holds alongside the regex groups
        /// those same branches read. Anywhere else with no current node there is nothing being processed at
        /// all: a stylesheet function sees only its arguments, which is what lets two calls with the same
        /// arguments be relied on to give the same answer.
        /// </remarks>
        private static string RequireASubstring(ref DynamicContext context)
        {
            return context.Runtime?.CurrentSubstring
                ?? throw XsltErrors.Error(
                    XsltErrorCode.XTDE1360,
                    "current() reads the item being processed, and there is none here. A stylesheet function "
                    + "processes nothing — what it sees comes through its arguments.");
        }
    }

    internal sealed class GenerateIdExpr : Expr
    {
        private readonly Expr? m_argument;

        /// <summary>
        /// Whether the argument is statically a node-set, whose first node is read from a pooled list.
        /// Settled here, the call being made once for each candidate where a stylesheet compares
        /// identities.
        /// </summary>
        private readonly bool m_argumentIsNodes;

        /// <summary>
        /// Whether the argument is one node wherever it can be and cannot promise it, <c>.</c> or
        /// <c>current()</c>, which is asked for that node with nothing rented to ask it.
        /// </summary>
        private readonly bool m_argumentIsUsuallyANode;

        /// <summary>Initializes a call to <c>generate-id()</c>.</summary>
        /// <param name="argument">The node-set to identify, or <see langword="null"/> for the context node.</param>
        public GenerateIdExpr(Expr? argument)
        {
            m_argument = argument;
            m_argumentIsNodes = argument is not null && argument.ReturnsNodeSet;
            m_argumentIsUsuallyANode = argument is not null
                && !argument.ReturnsNodeSet && argument.UsuallyReturnsNodeSet;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children =>
            m_argument is null ? Array.Empty<Expr>() : new[] { m_argument };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XdmTree tree = context.Tree;
            int node;

            if (m_argument is null)
            {
                node = context.Node;
            }
            else if (TryReadFirstNode(ref context, ref tree, out node))
            {
                if (node < 0)
                {
                    // An empty node-set has no identity, so the result is the empty string.
                    return XPathValue.FromString(string.Empty);
                }
            }
            else if (!ReadFirstNodeOfValue(m_argument.Evaluate(ref context), ref tree, out node))
            {
                return XPathValue.FromString(string.Empty);
            }

            // Must be an XML name, so it cannot start with a digit.
            int treeId = context.Runtime?.GetTreeId(tree) ?? 0;
            return XPathValue.FromString($"id{treeId}n{node}");
        }

        /// <summary>
        /// Reads the first node of an argument that had to be evaluated to learn what it is.
        /// </summary>
        /// <remarks>
        /// Apart from <see cref="Evaluate"/> so that the way most calls take, through
        /// <see cref="TryReadFirstNode"/>, stays the size it was for the JIT to weigh; this is the way a
        /// variable and a filter over one take, and the one an atomic value is refused on.
        /// </remarks>
        /// <param name="value">The argument's value.</param>
        /// <param name="tree">Set to the node's tree, where there is a node.</param>
        /// <param name="node">The first node, where there is one.</param>
        /// <returns><see langword="false"/> where the argument selected nothing, which has no identity.</returns>
        /// <exception cref="XsltException">The first item is an atomic value.</exception>
        private static bool ReadFirstNodeOfValue(XPathValue value, ref XdmTree tree, out int node)
        {
            if (value.Kind == XPathValueKind.NodeSet)
            {
                // A node-set that was not promised, which is what a variable holds and what a filter
                // over one gives: its first node is read where it stands, and not from the node-set
                // laid out as a list of items, eighty bytes for every generate-id($nodes[1]).
                NodeSet found = value.AsNodeSet();

                if (found.Count == 0)
                {
                    node = -1;
                    return false;
                }

                tree = found.TreeAt(0);
                node = found.FirstNode();
                return true;
            }

            // 3.0 declares the argument node()?, so what arrives may be one node or the empty sequence
            // rather than a node-set. Both are asked the same question and the empty one has no answer.
            List<XPathValue> items = XdmSequence.Items(value);

            if (items.Count == 0)
            {
                node = -1;
                return false;
            }

            // An atomic value has no identity to name, and no tree to be looked up in: what came of
            // asking was a null reference, where the declared type refuses the argument.
            if (items[0].Kind != XPathValueKind.Node)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    "generate-id() identifies a node, and what it was given is an atomic value.");
            }

            tree = items[0].NodeTree;
            node = items[0].NodeId;
            return true;
        }

        /// <summary>
        /// Reads the first node of an argument that is nodes, from a pooled list and without the node-set.
        /// </summary>
        /// <remarks>
        /// An argument that is statically a node-set is read so always. One that is only usually a node,
        /// <c>.</c> or <c>current()</c>, is asked for the one, with no list rented to ask it:
        /// <c>generate-id(.) = generate-id(current())</c> is how a 1.0 stylesheet says "this very node",
        /// once for each candidate, and what each side names is a node unless atomic values are being
        /// walked or filtered.
        /// </remarks>
        /// <param name="context">The evaluation context.</param>
        /// <param name="tree">Set to the node's tree, where there is a node.</param>
        /// <param name="node">The first node, or a negative number where the argument selected none.</param>
        /// <returns>
        /// <see langword="false"/> where the argument has to be evaluated as a value to learn what it is.
        /// </returns>
        private bool TryReadFirstNode(ref DynamicContext context, ref XdmTree tree, out int node)
        {
            node = -1;

            if (m_argumentIsUsuallyANode)
            {
                XdmTree? one = m_argument!.TryEvaluateOneNode(ref context, out int only);

                if (one is null)
                {
                    return false;
                }

                tree = one;
                node = only;
                return true;
            }

            if (!m_argumentIsNodes)
            {
                return false;
            }

            List<int> nodes = NodeListPool.Rent();

            try
            {
                XdmTree found = m_argument!.EvaluateNodes(ref context, nodes);

                if (nodes.Count != 0)
                {
                    tree = found;
                    node = nodes[0];
                }

                return true;
            }
            finally
            {
                NodeListPool.Return(nodes);
            }
        }
    }

    /// <summary>
    /// <c>fn:copy-of()</c>, which returns its argument with every node in it replaced by a copy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point of it is identity, not content: the copy is equal to the original under
    /// <c>deep-equal()</c> and is never the same node under <c>is</c>. That is what makes it the way to put
    /// a node into a sequence without its provenance coming too — a copy has no parent, no siblings and no
    /// document to be found in, so nothing downstream can navigate out of it into the tree it came from.
    /// </para>
    /// <para>
    /// Each node is copied into a capture of its own rather than all of them into one, because a copy is
    /// made per item: two adjacent text nodes copied into a single capture would be flushed as one, and the
    /// result would be a sequence one item shorter than the argument.
    /// </para>
    /// </remarks>
    internal sealed class CopyOfFunctionExpr : Expr
    {
        private readonly Expr? m_select;

        /// <summary>Initializes a call.</summary>
        /// <param name="select">What to copy, or null for the context item.</param>
        public CopyOfFunctionExpr(Expr? select)
        {
            m_select = select;
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children =>
            m_select is null ? Array.Empty<Expr>() : new[] { m_select };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue value = m_select is not null ? m_select.Evaluate(ref context) : ContextItem(ref context);

            List<XPathValue> items = XdmSequence.Items(value);
            List<XPathValue> copies = new List<XPathValue>(items.Count);

            foreach (XPathValue item in items)
            {
                // An atomic value has no identity to give it a new one of, so it is itself. The same goes
                // for a map, an array or a function item: what a copy would change about them is the nodes
                // inside, which is a depth this engine does not need to reach for the tests it can answer.
                if (item.Kind != XPathValueKind.Node)
                {
                    copies.Add(item);
                    continue;
                }

                // §18.3 defines the function as an xsl:copy-of with copy-namespaces="yes",
                // copy-accumulators="yes" and validation="preserve", so it is subject to what that
                // instruction is subject to — here, that an attribute holding a QName cannot be copied
                // away from the element whose namespace nodes give the prefix in it a meaning.
                Instructions.RefuseStrandedQNames(
                    item.NodeTree, item.NodeId, copyNamespaces: true, preserveTypes: true);

                // The copy answers for its accumulators as the original does, which is what tells fn:copy-of
                // apart from a copy made by xsl:copy-of without copy-accumulators="yes".
                SequenceCaptureTarget capture = new SequenceCaptureTarget();
                NodeCopier.CopyDeep(item.NodeTree, item.NodeId, capture, copyAccumulators: true, runtime: context.Runtime);
                copies.AddRange(XdmSequence.Items(capture.Finish()));
            }

            return XdmSequence.Concatenate(copies);
        }

        /// <summary>The context item, which the no-argument form copies.</summary>
        internal static XPathValue ContextItem(ref DynamicContext context, string function)
        {
            if (context.HasAtomicItem)
            {
                return context.AtomicItem;
            }

            if (context.Node < 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPDY0002,
                    $"{function}() with no argument is about the context item, and here there is none.");
            }

            return XPathValue.FromNode(context.Tree, context.Node);
        }

        /// <summary>The context item, which the no-argument form copies.</summary>
        private static XPathValue ContextItem(ref DynamicContext context)
        {
            return ContextItem(ref context, "copy-of");
        }
    }

    /// <summary>
    /// <c>fn:snapshot()</c>, which copies a node together with the ancestors it hangs from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The difference from <c>fn:copy-of()</c> is the whole of it. A copy has no provenance, which is what
    /// makes it safe to hand on; a snapshot keeps just enough of one to still answer questions about where
    /// the node sat — its ancestors, and their attributes and namespaces. What it leaves behind is the
    /// siblings, so a snapshot of one row of a large table is that row and its context, not the table.
    /// </para>
    /// <para>
    /// That is why the two exist side by side: a streaming processor can afford a snapshot of the node it is
    /// looking at, because the ancestors are the part of the document it still has in hand, and cannot
    /// afford to keep the siblings it has already passed.
    /// </para>
    /// </remarks>
    internal sealed class SnapshotFunctionExpr : Expr
    {
        private readonly Expr? m_select;

        /// <summary>Initializes a call.</summary>
        /// <param name="select">What to snapshot, or null for the context item.</param>
        public SnapshotFunctionExpr(Expr? select)
        {
            m_select = select;
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children =>
            m_select is null ? Array.Empty<Expr>() : new[] { m_select };

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue value = m_select is not null
                ? m_select.Evaluate(ref context)
                : CopyOfFunctionExpr.ContextItem(ref context, "snapshot");

            List<XPathValue> items = XdmSequence.Items(value);
            List<XPathValue> taken = new List<XPathValue>(items.Count);

            foreach (XPathValue item in items)
            {
                taken.Add(item.Kind == XPathValueKind.Node ? Snapshot(item) : item);
            }

            return XdmSequence.Concatenate(taken);
        }

        /// <summary>Builds one node's snapshot and returns the copy of that node within it.</summary>
        /// <remarks>
        /// The ancestors are written outermost first, each contributing its name, its namespaces and its
        /// attributes but only the one child that leads on — so the built tree is a single path with the
        /// node's own subtree hanging off the end of it. Which is what makes the copy findable: it is
        /// reached by taking the first child as many times as there were ancestors.
        /// </remarks>
        private static XPathValue Snapshot(XPathValue item)
        {
            XdmTree source = item.NodeTree;
            int node = item.NodeId;

            // An attribute or a namespace node hangs from its element, which is the innermost ancestor copied.
            bool hanging = XdmTree.IsAttribute(node);
            int owner = hanging ? source.ParentOf(node) : node;

            if (hanging && owner < 0)
            {
                return ParentlessCopy(source, node);
            }

            // A node with no parent has no ancestors to keep, so its snapshot is a copy of it — a parentless
            // copy, as the original is, rather than one hung under a document node the original never had.
            if (!hanging && source.KindOf(node) != NodeKind.Root && source.ParentOf(node) < 0)
            {
                SequenceCaptureTarget capture = new SequenceCaptureTarget();
                NodeCopier.CopyDeep(source, node, capture, copyAccumulators: true);
                return capture.Finish();
            }

            List<int> ancestors = new List<int>();
            for (int up = hanging ? owner : source.ParentOf(node); up >= 0; up = source.ParentOf(up))
            {
                if (source.KindOf(up) == NodeKind.Element)
                {
                    ancestors.Add(up);
                }
            }

            ancestors.Reverse();

            ResultTreeBuilder builder = new ResultTreeBuilder();

            foreach (int ancestor in ancestors)
            {
                StartCopy(source, ancestor, builder);
            }

            if (!hanging)
            {
                NodeCopier.CopyDeep(source, node, builder, copyAccumulators: true);
            }
            else if (source.KindOf(node) == NodeKind.Attribute)
            {
                // A namespace node needs no copying of its own: its element's copy carries every namespace
                // in scope, the one asked for among them.
                NodeCopier.CopyShallow(source, node, builder, copyAccumulators: true);
            }

            for (int i = 0; i < ancestors.Count; i++)
            {
                builder.EndElement();
            }

            XdmTree built = builder.Finish();

            if (hanging)
            {
                // The node hangs from the last ancestor written, which is its own element.
                int element = Descend(built, ancestors.Count);
                int found = source.KindOf(node) == NodeKind.Namespace
                    ? built.NamespaceNodeOf(element, source.NameTable.GetLocalName(source.FingerprintOf(node)))
                    : built.FindAttribute(element, built.NameTable.LookupFingerprint(
                        source.NameTable.GetNamespaceUri(source.FingerprintOf(node)),
                        source.NameTable.GetLocalName(source.FingerprintOf(node))));

                return found < 0
                    ? XPathValue.FromNode(built, element)
                    : XPathValue.FromNode(built, found);
            }

            // A document node is the one case where the copy is the built root itself: copying it writes its
            // children into the root that is already there rather than opening anything of its own.
            return XPathValue.FromNode(
                built,
                source.KindOf(node) == NodeKind.Root
                    ? XdmTree.RootNode
                    : Descend(built, ancestors.Count + 1));
        }

        /// <summary>
        /// A copy of an attribute or namespace node that has no parent: a tree of one node, as the original
        /// is.
        /// </summary>
        private static XPathValue ParentlessCopy(XdmTree source, int node)
        {
            int fingerprint = source.FingerprintOf(node);
            string local = source.NameTable.GetLocalName(fingerprint);
            XdmTreeBuilder builder = new XdmTreeBuilder();
            int copy = source.KindOf(node) == NodeKind.Namespace
                ? builder.AddParentlessNamespaceNode(local, source.StringValueOf(node))
                : builder.AddParentlessAttribute(
                    source.NameTable.GetPrefix(source.NameCodeOf(node)),
                    source.NameTable.GetNamespaceUri(fingerprint),
                    local,
                    source.StringValueOf(node));

            return XPathValue.FromNode(builder.Finish(), copy);
        }

        /// <summary>Takes the first child of a built snapshot's root the given number of times.</summary>
        private static int Descend(XdmTree tree, int depth)
        {
            int node = XdmTree.RootNode;

            for (int i = 0; i < depth; i++)
            {
                node = tree.FirstChildOf(node);
            }

            return node;
        }

        /// <summary>Opens a copy of one ancestor: its name, its namespaces and its attributes.</summary>
        private static void StartCopy(XdmTree tree, int element, OutputTarget output)
        {
            int nameCode = tree.NameCodeOf(element);
            int fingerprint = tree.FingerprintOf(element);

            output.StartElement(
                tree.NameTable.GetPrefix(nameCode),
                tree.NameTable.GetNamespaceUri(fingerprint),
                tree.NameTable.GetLocalName(fingerprint));

            output.NoteCopiedFrom(tree, element);

            foreach ((string prefix, string uri) in tree.InScopeNamespacesOf(element))
            {
                output.WriteNamespaceDeclaration(prefix, uri);
            }

            int count = tree.AttributeCountOf(element);
            for (int i = 0; i < count; i++)
            {
                NodeCopier.CopyShallow(tree, tree.AttributeAt(element, i), output);
            }
        }
    }
}
