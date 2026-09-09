using System.Runtime.CompilerServices;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A function written out where it is used, as <c>function($x as xs:integer) as xs:integer { $x * 2 }</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Evaluating this does not call anything. It produces a <em>value</em> — a closure over the variables in
    /// scope here — and calling happens later, wherever that value ends up.
    /// </para>
    /// <para>
    /// The parameters are range variables, the same store <c>for</c> and <c>every</c> bind into, because they
    /// have the same lifetime: they exist inside one expression and the parser knows their slots by how deeply
    /// the bindings nest. Using the stylesheet's own frame instead would collide with the outer variables the
    /// body reads, which are addressed in that frame and have to keep meaning what they meant.
    /// </para>
    /// </remarks>
    public sealed class InlineFunctionExpr : Expr
    {
        private readonly int m_parameterBase;
        private readonly string[] m_parameterNames;
        private readonly XdmSequenceType?[] m_parameterTypes;
        private readonly XdmSequenceType? m_resultType;
        private readonly Expr m_body;
        private readonly int m_slotsNeeded;

        /// <summary>Initializes an inline function.</summary>
        /// <param name="parameterBase">The first range-variable slot the parameters occupy.</param>
        /// <param name="parameterNames">The parameter names, for diagnostics.</param>
        /// <param name="parameterTypes">The declared parameter types, with nothing where none was declared.</param>
        /// <param name="resultType">The declared result type, or <see langword="null"/>.</param>
        /// <param name="body">The body, which is the whole of what the function does.</param>
        /// <param name="slotsNeeded">How many range-variable slots the body can reach, parameters included.</param>
        public InlineFunctionExpr(
            int parameterBase,
            string[] parameterNames,
            XdmSequenceType?[] parameterTypes,
            XdmSequenceType? resultType,
            Expr body,
            int slotsNeeded)
        {
            m_parameterBase = parameterBase;
            m_parameterNames = parameterNames;
            m_parameterTypes = parameterTypes;
            m_resultType = resultType;
            m_body = body;
            m_slotsNeeded = slotsNeeded;
        }

        /// <summary>Gets how many parameters the function declares.</summary>
        public int Arity => m_parameterNames.Length;

        /// <summary>The types this function was written with, which every item made from it carries.</summary>
        internal XdmFunctionSignature Signature => m_signature ??=
            new XdmFunctionSignature(m_parameterTypes, m_resultType);

        private XdmFunctionSignature? m_signature;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_body };

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return XPathValue.FromFunction(
                new XdmInlineFunction(this, CapturedContext.Capture(ref context, m_slotsNeeded)));
        }

        /// <summary>
        /// Runs the body with the arguments bound to the parameters.
        /// </summary>
        /// <remarks>
        /// The previous occupants of the slots are put back afterwards, for the reason <c>for</c> does the
        /// same: one closure can be re-entered while it is running, which is exactly what happens when its
        /// body folds over a sequence with itself.
        /// </remarks>
        /// <param name="arguments">The argument values, one per parameter.</param>
        /// <param name="captured">The context the closure was created in.</param>
        /// <param name="caller">The context of the call, which supplies only the runtime.</param>
        internal XPathValue Run(
            XPathValue[] arguments,
            CapturedContext captured,
            ref DynamicContext caller)
        {
            // A closure recurses through the higher-order functions rather than by naming itself, so nothing
            // counts the depth. The stack still runs out, and this is what turns that into a message.
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                throw new XsltException(
                    "Calls nested too deeply inside an inline function. A function that folds over its own "
                    + "result with no terminating case will do this.");
            }

            DynamicContext inner = captured.Restore(ref caller);
            XPathValue[] slots = captured.RangeVariables;
            XPathValue[] saved = arguments.Length == 0
                ? Array.Empty<XPathValue>()
                : new XPathValue[arguments.Length];

            for (int i = 0; i < arguments.Length; i++)
            {
                saved[i] = slots[m_parameterBase + i];
                slots[m_parameterBase + i] = XdmTypeConversion.Apply(arguments[i], m_parameterTypes[i]);
            }

            try
            {
                return XdmTypeConversion.Apply(m_body.Evaluate(ref inner), m_resultType);
            }
            finally
            {
                for (int i = 0; i < saved.Length; i++)
                {
                    slots[m_parameterBase + i] = saved[i];
                }
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return "function(" + string.Join(", ", Array.ConvertAll(m_parameterNames, name => "$" + name))
                + ") { … }";
        }
    }

    /// <summary>
    /// A call to a function that is not known until the expression runs, as <c>$f(1, 2)</c>.
    /// </summary>
    /// <remarks>
    /// The target may be any function item, which includes a map and an array: <c>$m('a')</c> reads an entry
    /// and <c>$a(2)</c> a member, both being ordinary calls of a one-argument function rather than a separate
    /// piece of syntax that happens to look like one.
    /// </remarks>
    public sealed class DynamicCallExpr : Expr
    {
        private readonly Expr m_target;
        private readonly Expr[] m_arguments;

        /// <summary>Initializes a dynamic call.</summary>
        /// <param name="target">The expression producing the function to call.</param>
        /// <param name="arguments">The argument expressions.</param>
        public DynamicCallExpr(Expr target, Expr[] arguments)
        {
            m_target = target;
            m_arguments = arguments;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children
        {
            get
            {
                yield return m_target;

                foreach (Expr argument in m_arguments)
                {
                    yield return argument;
                }
            }
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue target = XdmSequence.RequireSingleItem(
                m_target.Evaluate(ref context), "the function to call");

            XdmFunction function = target.AsFunction();

            // The arguments are evaluated in the caller's context, before anything is bound: they mean what
            // they mean where they are written, which for a closure is a different place entirely.
            XPathValue[] values = m_arguments.Length == 0
                ? Array.Empty<XPathValue>()
                : new XPathValue[m_arguments.Length];

            for (int i = 0; i < m_arguments.Length; i++)
            {
                values[i] = m_arguments[i].Evaluate(ref context);
            }

            return function.Call(values, ref context);
        }
    }

    /// <summary>
    /// A call with a <c>?</c> in place of one or more arguments, which produces a function rather than
    /// calling one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// XPath 3.1's partial application. <c>serialize(?, $output)</c> is a function of one argument that
    /// serializes whatever it is given with the options <c>$output</c> held <em>now</em> — which is the point
    /// of it, and why the supplied arguments are evaluated here rather than on each later call.
    /// </para>
    /// <para>
    /// A named call and a dynamic one reduce to the same thing: something that evaluates to a function item,
    /// and a list of argument slots some of which are open. So <c>f(?, 3)</c> compiles to this over the same
    /// function item <c>f#2</c> would have produced.
    /// </para>
    /// </remarks>
    public sealed class PartialApplicationExpr : Expr
    {
        private readonly Expr m_target;
        private readonly Expr?[] m_arguments;

        /// <summary>Initializes a partial application.</summary>
        /// <param name="target">The expression producing the function to apply.</param>
        /// <param name="arguments">
        /// The argument expressions, with <see langword="null"/> where a <c>?</c> left the place open.
        /// </param>
        public PartialApplicationExpr(Expr target, Expr?[] arguments)
        {
            m_target = target;
            m_arguments = arguments;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children
        {
            get
            {
                yield return m_target;

                foreach (Expr? argument in m_arguments)
                {
                    if (argument is not null)
                    {
                        yield return argument;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XdmFunction function = XdmSequence
                .RequireSingleItem(m_target.Evaluate(ref context), "the function to apply")
                .AsFunction();

            XPathValue?[] supplied = new XPathValue?[m_arguments.Length];

            for (int i = 0; i < m_arguments.Length; i++)
            {
                supplied[i] = m_arguments[i]?.Evaluate(ref context);
            }

            if (m_arguments.Length != function.Arity)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"{function.Describe()} takes {function.Arity} argument"
                    + (function.Arity == 1 ? string.Empty : "s")
                    + $", and this partial application supplies {m_arguments.Length} places.");
            }

            return XPathValue.FromFunction(new XdmPartialFunction(function, supplied));
        }
    }
}
