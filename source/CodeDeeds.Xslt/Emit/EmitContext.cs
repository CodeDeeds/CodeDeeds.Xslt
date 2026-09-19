using System.Reflection;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Emit
{
    /// <summary>
    /// The signature every compiled expression is emitted with.
    /// </summary>
    /// <param name="context">The evaluation context, passed by reference so nothing is copied or allocated.</param>
    /// <param name="constants">
    /// Values the emitted code needs but IL cannot embed — strings, interpreter nodes, and any other object
    /// reference. Passed as a real parameter because a ref struct cannot be boxed, which rules out reaching
    /// the method through reflection.
    /// </param>
    /// <returns>The expression's value.</returns>
    internal delegate XPathValue CompiledExpressionDelegate(ref DynamicContext context, object[] constants);

    /// <summary>
    /// The state shared while one expression is being compiled: the emitter, and the pool of object constants
    /// the emitted code will need at run time.
    /// </summary>
    internal sealed class EmitContext
    {
        /// <summary>The argument index of the evaluation context.</summary>
        public const int ContextArgument = 0;

        /// <summary>The argument index of the constant pool.</summary>
        public const int ConstantsArgument = 1;

        private static readonly MethodInfo s_evaluate =
            typeof(Expr).GetMethod(nameof(Expr.Evaluate))
            ?? throw new InvalidOperationException("Expr.Evaluate could not be located.");

        private static readonly MethodInfo s_evaluateAsBoolean =
            typeof(Expr).GetMethod(nameof(Expr.EvaluateAsBoolean))
            ?? throw new InvalidOperationException("Expr.EvaluateAsBoolean could not be located.");

        private readonly List<object> m_constants = new();

        /// <summary>Initializes a compilation context.</summary>
        /// <param name="il">The emitter to write into.</param>
        public EmitContext(ILBuilder il)
        {
            IL = il;
        }

        /// <summary>Gets the emitter.</summary>
        public ILBuilder IL { get; }

        /// <summary>Gets the constants the compiled method will be invoked with.</summary>
        public object[] Constants => m_constants.ToArray();

        /// <summary>Adds a value to the constant pool.</summary>
        /// <param name="value">The value the emitted code needs.</param>
        /// <returns>Its index in the pool.</returns>
        public int AddConstant(object value)
        {
            m_constants.Add(value);
            return m_constants.Count - 1;
        }

        /// <summary>Pushes the evaluation context, by reference.</summary>
        public void LoadContext()
        {
            IL.LoadArgument(ContextArgument);
        }

        /// <summary>Pushes a field of the evaluation context.</summary>
        /// <param name="fieldName">The field to read, such as <c>Node</c> or <c>Position</c>.</param>
        public void LoadContextField(string fieldName)
        {
            FieldInfo field = typeof(DynamicContext).GetField(fieldName)
                ?? throw new InvalidOperationException($"DynamicContext has no field '{fieldName}'.");

            LoadContext();
            IL.LoadField(field);
        }

        /// <summary>Pushes an object from the constant pool.</summary>
        /// <param name="value">The value to push.</param>
        /// <param name="type">The type to present it as.</param>
        public void LoadConstant(object value, Type type)
        {
            IL.LoadArgument(ConstantsArgument);
            IL.LoadInt(AddConstant(value));
            IL.LoadElementReference();
            IL.CastClass(type);
        }

        /// <summary>
        /// Emits a call back into an expression's interpreted implementation, leaving its value on the stack.
        /// </summary>
        /// <remarks>
        /// This is what every expression node compiles to until it is taught to emit something better. It
        /// means the backend produces correct code from the outset and each specialisation can be added — and
        /// verified against the interpreter — on its own, rather than the whole backend having to work before
        /// any of it can be tested.
        /// </remarks>
        /// <param name="expression">The node to defer to.</param>
        public void EmitInterpreterFallback(Expr expression)
        {
            LoadConstant(expression, typeof(Expr));
            LoadContext();
            IL.Call(s_evaluate, virtualCall: true);
        }

        /// <summary>
        /// Emits a call back into an expression's interpreted implementation for its effective boolean
        /// value, leaving a raw <see cref="bool"/> on the stack.
        /// </summary>
        /// <remarks>
        /// For an expression whose <see cref="Expr.EvaluateAsBoolean"/> does less than evaluating it and
        /// converting the result — a path, which answers by existence. An expression with no such route
        /// gains nothing here over <see cref="EmitInterpreterFallback"/> and the conversion after it.
        /// </remarks>
        /// <param name="expression">The node to defer to.</param>
        public void EmitInterpreterBooleanFallback(Expr expression)
        {
            LoadConstant(expression, typeof(Expr));
            LoadContext();
            IL.Call(s_evaluateAsBoolean, virtualCall: true);
        }
    }
}
