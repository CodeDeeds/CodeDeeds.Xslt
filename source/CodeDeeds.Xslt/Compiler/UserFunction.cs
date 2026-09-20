using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// A function declared by <c>xsl:function</c> and called from XPath like any other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The nearest thing XSLT 1.0 offers is a named template, which differs in two ways that matter: a
    /// template is called by an instruction rather than from inside an expression, and it produces result
    /// content rather than a value. A function can be used in a predicate, which is what makes it worth
    /// having.
    /// </para>
    /// <para>
    /// The name must be in a namespace — the specification requires it, so that a stylesheet can never
    /// shadow a function from the core library or be broken by one added to a later version of it.
    /// </para>
    /// </remarks>
    internal sealed class UserFunction
    {
        /// <summary>Initializes a function declaration.</summary>
        /// <param name="name">The function's expanded name.</param>
        /// <param name="arity">How many parameters it declares.</param>
        public UserFunction(ExpandedName name, int arity)
        {
            Name = name;
            Arity = arity;
        }

        /// <summary>The function's expanded name.</summary>
        public ExpandedName Name { get; }

        /// <summary>
        /// How many parameters the function takes. Part of its identity: two functions of one name and
        /// different arities are two functions, as they are in the core library.
        /// </summary>
        public int Arity { get; }

        /// <summary>The slots the parameters occupy in the function's frame.</summary>
        public int[] ParameterSlots { get; set; } = Array.Empty<int>();

        /// <summary>The declared type of each parameter, or <see langword="null"/> where none was declared.</summary>
        public XdmSequenceType?[] ParameterTypes { get; set; } = Array.Empty<XdmSequenceType?>();

        /// <summary>The declared return type, from the function's own <c>as</c> attribute.</summary>
        public XdmSequenceType? ResultType { get; set; }

        /// <summary>How many slots the frame needs.</summary>
        public int FrameSize { get; set; }

        /// <summary>The body, compiled.</summary>
        public Instruction[] Body { get; set; } = Array.Empty<Instruction>();

        /// <summary>The base URI of the declaration, which a tree the body builds takes.</summary>
        public string? BaseUri { get; set; }

        /// <summary>
        /// Whether the declaration names the function without defining it, leaving that to a using package.
        /// </summary>
        /// <remarks>
        /// An abstract function has an empty body, so calling one would quietly return nothing — and where
        /// the declaration says <c>as="xs:integer"</c> that surfaces as a type error about an empty
        /// sequence, which describes the symptom rather than the cause. A package supplying the function is
        /// what the declaration is waiting for, and reaching it without one has its own code.
        /// </remarks>
        public bool IsAbstract { get; set; }

        /// <summary>The function this one overrides, which is what <c>xsl:original</c> means in its body.</summary>
        public UserFunction? Original { get; set; }

        /// <summary>
        /// The body when it is a single <c>xsl:sequence</c>, whose value can be returned as it is.
        /// </summary>
        /// <remarks>
        /// This is what lets a function return a typed value rather than a tree of text. A body that builds
        /// content is captured as a result tree instead, which is right for a function that constructs
        /// elements and wrong only for one that could have said <c>xsl:sequence</c> and did not.
        /// </remarks>
        public Expr? DirectResult { get; set; }

        /// <summary>
        /// Whether the same arguments must give back the very same result, so a call may be answered from
        /// what an earlier one returned.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Set by <c>new-each-time="no"</c>, which is a promise about node identity rather than about speed:
        /// a function returning <c>&lt;e&gt;{$n}&lt;/e&gt;</c> ordinarily builds a new element per call, so
        /// ten calls with seven distinct arguments give ten distinct nodes and a union of them counts ten.
        /// Under <c>new-each-time="no"</c> the union counts seven. That is observable, so it cannot be done
        /// by choosing to memoize and not done by choosing not to — the attribute is what decides it.
        /// </para>
        /// <para>
        /// Also set by <c>cache="yes"</c>, which asks for the memoization outright. There the identity is a
        /// side effect rather than the point, and the point is that a naive recursion over a large argument
        /// is exponential without it: <c>fib(92)</c> written the obvious way does not finish in this
        /// lifetime and finishes instantly memoized.
        /// </para>
        /// </remarks>
        public bool Deterministic { get; set; }

        /// <summary>
        /// The visibility the function's own package settled on: what it declared, or what an
        /// <c>xsl:expose</c> gave it, or private.
        /// </summary>
        /// <remarks>
        /// Read by <c>xsl:evaluate</c>, whose target expression may call a function only if this is public
        /// or final. A package boundary is settled elsewhere and does not read it.
        /// </remarks>
        public Visibility Visibility { get; set; } = Visibility.Private;
    }

    /// <summary>A call to a function declared by <c>xsl:function</c>.</summary>
    internal sealed class UserFunctionCallExpr : Expr
    {
        private readonly UserFunction m_function;
        private readonly Expr[] m_arguments;
        private bool m_last;

        /// <summary>Initializes a call.</summary>
        /// <param name="function">The function being called.</param>
        /// <param name="arguments">The argument expressions, one per parameter.</param>
        public UserFunctionCallExpr(UserFunction function, Expr[] arguments)
        {
            m_function = function;
            m_arguments = arguments;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        internal override bool NeedsTheTransformation => true;

        /// <inheritdoc/>
        internal override XdmFunctionSignature? DeclaredSignature =>
            new XdmFunctionSignature(m_function.ParameterTypes, m_function.ResultType);

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return InvokeInTemporaryState(ref context, inPlace: m_last);
        }

        /// <inheritdoc/>
        public override bool EvaluateAsBoolean(ref DynamicContext context)
        {
            // Asked for as a boolean, the value is about to be read, so the call has to be made here: only
            // a value handed on untouched can stand in for the function body's own.
            return InvokeInTemporaryState(ref context, inPlace: false).ToBoolean();
        }

        /// <inheritdoc/>
        public override XdmTree EvaluateNodes(ref DynamicContext context, List<int> output)
        {
            NodeSet nodes = InvokeInTemporaryState(ref context, inPlace: false).AsNodeSet();
            for (int i = 0; i < nodes.Count; i++)
            {
                output.Add(nodes[i]);
            }

            return nodes.Tree;
        }

        /// <summary>
        /// Makes the call in temporary output state, which is where a function is evaluated whatever its
        /// body writes to (§2.3.2).
        /// </summary>
        /// <remarks>
        /// One place for it, because the call has three ways in and the state is not the caller's to
        /// forget: asked for as a boolean or as nodes the call was made without it, so a function reading
        /// <c>current-output-uri()</c> answered with the URI in an <c>xsl:if</c> and with nothing in a
        /// <c>select</c>.
        /// </remarks>
        private XPathValue InvokeInTemporaryState(ref DynamicContext context, bool inPlace)
        {
            XsltRuntime? runtime = context.Runtime;

            if (runtime is null)
            {
                return Invoke(ref context, inPlace);
            }

            runtime.TemporaryDepth++;

            try
            {
                return Invoke(ref context, inPlace);
            }
            finally
            {
                runtime.TemporaryDepth--;
            }
        }

        /// <inheritdoc/>
        internal override void MarkTailPosition()
        {
            m_last = true;
        }

        /// <summary>Evaluates the arguments and makes the call, or hands it back to be made in place.</summary>
        /// <param name="context">The caller's context.</param>
        /// <param name="inPlace">
        /// Whether the call is the whole answer of the function body it ends, and so can be made by the
        /// invocation running that body once the body has returned — in the body's own place on the stack
        /// rather than beneath it. The value returned then is a placeholder the invocation never reads.
        /// </param>
        private XPathValue Invoke(ref DynamicContext context, bool inPlace)
        {
            XsltRuntime runtime = context.Runtime
                ?? throw new XsltException(
                    $"'{m_function.Name.LocalName}()' can only be called during a transformation.");

            XPathValue[] values = EvaluateArguments(ref context);

            if (inPlace)
            {
                runtime.DeferTailCall(m_function, values);
                return default;
            }

            return runtime.InvokeFunction(m_function, values, ref context);
        }

        /// <summary>Evaluates the arguments of the call.</summary>
        /// <remarks>
        /// <para>
        /// In the caller's context, before the frame is switched: they mean what they mean where they were
        /// written, not inside the function.
        /// </para>
        /// <para>
        /// Never inlined. This has returned before the call is made, but the frame it would be inlined into
        /// stays on the stack for as long as the function runs, and would hold the room for every temporary
        /// of the argument expressions for all that time. Profile-guided compilation did that, twice over
        /// — once for each place the call is made from, with and without a transformation to tell that
        /// it is in temporary output state — and the frame came to a kilobyte
        /// for each level of a recursive function.
        /// </para>
        /// </remarks>
        /// <param name="context">The caller's context.</param>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private XPathValue[] EvaluateArguments(ref DynamicContext context)
        {
            XPathValue[] values = new XPathValue[m_arguments.Length];
            for (int i = 0; i < m_arguments.Length; i++)
            {
                values[i] = m_arguments[i].Evaluate(ref context);
            }

            return values;
        }
    }
}
