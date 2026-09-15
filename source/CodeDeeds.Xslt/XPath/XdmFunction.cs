using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// The types a function item was declared with: what it takes, and what it gives back.
    /// </summary>
    /// <remarks>
    /// Only a function whose types the stylesheet actually wrote has one — an <c>xsl:function</c> and an
    /// inline function. A call into the standard library is compiled from a signature of another kind
    /// entirely, and a partial application is a new function nobody declared, so both answer null and are
    /// judged by their arity alone.
    /// </remarks>
    internal sealed class XdmFunctionSignature
    {
        /// <summary>Initializes a signature.</summary>
        /// <param name="parameters">The declared argument types, with null where none was written.</param>
        /// <param name="result">The declared result type, or null where none was written.</param>
        public XdmFunctionSignature(IReadOnlyList<XdmSequenceType?> parameters, XdmSequenceType? result)
        {
            Parameters = parameters;
            Result = result;
        }

        /// <summary>The declared argument types, with null where none was written.</summary>
        public IReadOnlyList<XdmSequenceType?> Parameters { get; }

        /// <summary>The declared result type, or null where none was written.</summary>
        public XdmSequenceType? Result { get; }
    }

    /// <summary>
    /// A function item, which XPath 3.0 adds: a function that is itself a value, and so can be held in a
    /// variable, passed to another function and returned from one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the piece that makes the rest of XPath 3.1 work rather than a feature on its own.
    /// <c>fn:filter</c>, <c>fn:sort</c>, <c>map:for-each</c> and the folds all take a function as an
    /// argument, and none of them can exist until a function can be one.
    /// </para>
    /// <para>
    /// A function item is an <em>item</em>, like a map and an array and unlike an atomic value: it has no
    /// string-value, cannot be atomized, and cannot be a map key. Maps and arrays are themselves function
    /// items — <c>$m('a')</c> and <c>$m?a</c> mean the same thing — which is why
    /// <see cref="XPathValue.AsFunction"/> answers for all three.
    /// </para>
    /// <para>
    /// Not subclassable outside this assembly: <see cref="Invoke"/> is internal, because invoking one needs a
    /// <see cref="DynamicContext"/> and that is a <see langword="ref"/> struct threaded through the whole
    /// evaluator rather than something a caller can hand over.
    /// </para>
    /// </remarks>
    public abstract class XdmFunction
    {
        /// <summary>
        /// Gets the function's name, or <see langword="null"/> where it has none.
        /// </summary>
        /// <remarks>
        /// An inline function is anonymous, and so is the function a map or an array stands for. That is what
        /// <c>fn:function-name</c> reports as the empty sequence.
        /// </remarks>
        public abstract XdmQName? FunctionName { get; }

        /// <summary>Gets how many arguments the function takes, which is part of its identity.</summary>
        public abstract int Arity { get; }

        /// <summary>
        /// Gets the types the function was declared with, or <see langword="null"/> where none are recorded.
        /// </summary>
        /// <remarks>
        /// What <c>instance of function(…) as …</c> is answered against, and what says whether a coercion
        /// has anything to do. Null is not "no types" but "not recorded", and is answered by admitting the
        /// item wherever its arity fits.
        /// </remarks>
        internal virtual XdmFunctionSignature? Signature => null;

        /// <summary>Gets the name to put in a message, which is the QName where there is one.</summary>
        internal string Describe()
        {
            if (FunctionName is not XdmQName name)
            {
                return $"an anonymous function of {Arity} argument" + (Arity == 1 ? string.Empty : "s");
            }

            string written = name.Prefix.Length == 0 ? name.LocalName : name.Prefix + ":" + name.LocalName;
            return $"{written}#{Arity}";
        }

        /// <summary>Calls the function.</summary>
        /// <param name="arguments">The argument values, one per parameter.</param>
        /// <param name="context">The context of the call.</param>
        /// <returns>The function's result.</returns>
        internal abstract XPathValue Invoke(XPathValue[] arguments, ref DynamicContext context);

        /// <summary>
        /// Calls the function, having first checked that it takes the number of arguments supplied.
        /// </summary>
        /// <param name="arguments">The argument values.</param>
        /// <param name="context">The context of the call.</param>
        /// <exception cref="XsltException"><c>XPTY0004</c> where the count does not match the arity.</exception>
        internal XPathValue Call(XPathValue[] arguments, ref DynamicContext context)
        {
            if (arguments.Length != Arity)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"{Describe()} takes {Arity} argument" + (Arity == 1 ? string.Empty : "s")
                    + $", and was called with {arguments.Length}.");
            }

            // Counted so that what a dynamic call may not read — current-output-uri(), which is about the
            // instruction being executed and a function item is executing none — knows it is inside one.
            XsltRuntime? runtime = context.Runtime;

            if (runtime is null)
            {
                return Invoke(arguments, ref context);
            }

            runtime.TemporaryDepth++;

            try
            {
                return Invoke(arguments, ref context);
            }
            finally
            {
                runtime.TemporaryDepth--;
            }
        }
    }

    /// <summary>
    /// A copy of everything a closure has to remember, taken where the closure is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="DynamicContext"/> is a <see langword="ref"/> struct and cannot be stored in a field, which
    /// is the whole reason this exists. It holds the same information in a class, so that an inline function
    /// evaluated inside a loop can be called after the loop has moved on and still see what it was written
    /// among.
    /// </para>
    /// <para>
    /// The two variable stores are <em>copied</em> rather than referenced, and that is not defensive
    /// programming but the difference between a closure and a bug. Both are reused: a <c>for</c> puts back
    /// what was in its slot when it finishes, and a template's frame is written again on the next call. A
    /// closure holding the array would see whatever happened to be there when it was called, so
    /// <c>for $x in 1 to 3 return function() { $x }</c> would answer 3 three times.
    /// </para>
    /// </remarks>
    internal sealed class CapturedContext
    {
        private readonly XdmTree m_tree;
        private readonly int[] m_fingerprintMap;
        private readonly int m_currentNode;
        private readonly XdmTree m_currentTree;
        private readonly XPathValue[] m_locals;
        private readonly int m_frameBase;
        private readonly XPathValue[] m_globals;
        private readonly XPathValue[] m_rangeVariables;
        private readonly XsltRuntime? m_runtime;

        private CapturedContext(
            ref DynamicContext context,
            XPathValue[] locals,
            XPathValue[] rangeVariables)
        {
            m_tree = context.Tree;
            m_fingerprintMap = context.FingerprintMap;
            m_currentNode = context.CurrentNode;
            m_currentTree = context.CurrentTree;
            m_locals = locals;
            m_frameBase = context.FrameBase;
            m_globals = context.Globals;
            m_rangeVariables = rangeVariables;
            m_runtime = context.Runtime;
        }

        /// <summary>Takes a copy of the context a closure is being created in.</summary>
        /// <param name="context">The context to capture.</param>
        /// <param name="rangeSlots">
        /// How many range-variable slots the closure's body needs, counting the parameters it will be called
        /// with. Sized here so that an invocation never has to grow the array and lose the copy.
        /// </param>
        public static CapturedContext Capture(ref DynamicContext context, int rangeSlots)
        {
            XPathValue[] range = new XPathValue[Math.Max(rangeSlots, context.RangeVariables?.Length ?? 0)];
            context.RangeVariables?.CopyTo(range, 0);

            return new CapturedContext(
                ref context,
                context.Locals.Length == 0 ? context.Locals : (XPathValue[])context.Locals.Clone(),
                range);
        }

        /// <summary>
        /// Rebuilds the context the closure's body is evaluated in.
        /// </summary>
        /// <remarks>
        /// The focus is deliberately absent, which XPath 3.0 requires of an inline function's body: <c>.</c>
        /// inside one raises <c>XPDY0002</c> rather than quietly meaning whatever the caller was positioned
        /// on. A function that wants a node is given it as an argument.
        /// </remarks>
        /// <param name="caller">The context of the call, which supplies the runtime and nothing else.</param>
        public DynamicContext Restore(ref DynamicContext caller)
        {
            DynamicContext inner = new DynamicContext(m_tree, DynamicContext.NotANode, m_fingerprintMap)
            {
                CurrentNode = m_currentNode,
                CurrentTree = m_currentTree,
                Position = 0,
                Size = 0,
                Locals = m_locals,
                FrameBase = m_frameBase,
                Globals = m_globals,
                RangeVariables = m_rangeVariables,
                Runtime = m_runtime ?? caller.Runtime,
            };

            return inner;
        }

        /// <summary>Gets the range-variable store, which an invocation writes its arguments into.</summary>
        public XPathValue[] RangeVariables => m_rangeVariables;
    }

    /// <summary>
    /// A function written where it is used, as <c>function($x) { $x * 2 }</c>.
    /// </summary>
    /// <remarks>
    /// A closure: it remembers the variables in scope where it was written, not where it is called. That is
    /// what makes <c>function($y) { $y + $offset }</c> worth writing at all, and it is why creating one costs
    /// a copy of the variable stores while calling one costs nothing.
    /// </remarks>
    internal sealed class XdmInlineFunction : XdmFunction
    {
        private readonly InlineFunctionExpr m_definition;
        private readonly CapturedContext m_captured;

        /// <summary>Initializes a closure over a definition and the context it was written in.</summary>
        /// <param name="definition">The inline function as it was written.</param>
        /// <param name="captured">The context it was written in.</param>
        public XdmInlineFunction(InlineFunctionExpr definition, CapturedContext captured)
        {
            m_definition = definition;
            m_captured = captured;
        }

        /// <inheritdoc/>
        public override XdmQName? FunctionName => null;

        /// <inheritdoc/>
        public override int Arity => m_definition.Arity;

        /// <inheritdoc/>
        internal override XdmFunctionSignature? Signature => m_definition.Signature;

        /// <inheritdoc/>
        internal override XPathValue Invoke(XPathValue[] arguments, ref DynamicContext context)
        {
            return m_definition.Run(arguments, m_captured, ref context);
        }
    }

    /// <summary>
    /// A function item made from a name: <c>fn:concat#3</c>, <c>my:total#1</c>, <c>xs:date#1</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The call it stands for is compiled once, exactly as if it had been written out, with a variable
    /// reference in each argument position. Invoking it means handing those variables an array of values, so
    /// a reference to a built-in, to a type constructor and to an <c>xsl:function</c> are one mechanism —
    /// whatever the parser would have built for a call of that name is what runs.
    /// </para>
    /// <para>
    /// A reference to a function that reads the focus, such as <c>fn:name#0</c> or
    /// <c>accumulator-before#1</c>, carries the focus of the place the reference was written, as the
    /// specification says, and reads that wherever it is later called. Every other reference is a constant,
    /// made once when the expression is compiled.
    /// </para>
    /// </remarks>
    internal sealed class XdmNamedFunction : XdmFunction
    {
        private readonly XdmQName m_name;
        private readonly int m_arity;
        private readonly Expr m_body;
        private readonly CapturedFocus? m_focus;

        /// <summary>Initializes a named function item.</summary>
        /// <param name="name">The function's name.</param>
        /// <param name="arity">How many arguments it takes.</param>
        /// <param name="body">
        /// The call, compiled over <see cref="VariableReferenceExpr"/> arguments in slots 0 upwards.
        /// </param>
        public XdmNamedFunction(XdmQName name, int arity, Expr body)
        {
            m_name = name;
            m_arity = arity;
            m_body = body;
        }

        /// <summary>Initializes a named function item that reads the focus it was made in.</summary>
        /// <param name="name">The function's name.</param>
        /// <param name="arity">How many arguments it takes.</param>
        /// <param name="body">The call, compiled as above.</param>
        /// <param name="focus">The dynamic context where the reference was written.</param>
        public XdmNamedFunction(XdmQName name, int arity, Expr body, ref DynamicContext focus)
            : this(name, arity, body)
        {
            m_focus = new CapturedFocus(ref focus);
        }

        /// <inheritdoc/>
        public override XdmQName? FunctionName => m_name;

        /// <inheritdoc/>
        public override int Arity => m_arity;

        /// <inheritdoc/>
        internal override XdmFunctionSignature? Signature => DeclaredTypes;

        /// <summary>The types the function was declared with, where the reference could find them.</summary>
        internal XdmFunctionSignature? DeclaredTypes { get; init; }

        /// <inheritdoc/>
        internal override XPathValue Invoke(XPathValue[] arguments, ref DynamicContext context)
        {
            DynamicContext inner = context;
            m_focus?.Restore(ref inner);

            // The arguments are the frame, so the variable references compiled into the body read them. A
            // fresh array per call is what the callers pass, so nothing here has to copy it.
            inner.Locals = arguments;
            inner.FrameBase = 0;
            inner.RangeVariables = null;

            // The regex group set is no part of what a function item carries, and a call made through one
            // does not borrow the caller's either: regex-group#1 called from inside another
            // xsl:analyze-string answers with a zero-length string rather than with that instruction's
            // groups (§5.3.4). A function the stylesheet declares is already called with none, this being
            // the one way a built-in that reads them can be reached without going through that.
            XsltRuntime? runtime = inner.Runtime;

            if (runtime is null)
            {
                return m_body.Evaluate(ref inner);
            }

            string[]? groups = runtime.RegexGroups;
            runtime.RegexGroups = null;

            try
            {
                return m_body.Evaluate(ref inner);
            }
            finally
            {
                runtime.RegexGroups = groups;
            }
        }
    }

    /// <summary>
    /// A function with some of its arguments already supplied, which is what <c>f(?, 3)</c> denotes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixed arguments are values rather than expressions, and that is the whole of what makes partial
    /// application worth having: they are evaluated once, where the <c>?</c> was written, and not again on
    /// each call. So <c>serialize(?, $output)</c> reads <c>$output</c> now and gives back something that
    /// serializes anything handed to it afterwards.
    /// </para>
    /// <para>
    /// The result is anonymous, whatever it was made from: XPath 3.1 gives the name only to the function
    /// that was written, and applying some of its arguments makes another function that nobody named. So
    /// <c>fn:function-name()</c> of it is the empty sequence, and its arity is how many places were left
    /// open.
    /// </para>
    /// </remarks>
    internal sealed class XdmPartialFunction : XdmFunction
    {
        private readonly XdmFunction m_function;
        private readonly XPathValue?[] m_supplied;
        private readonly int m_arity;

        /// <summary>Initializes a partially applied function.</summary>
        /// <param name="function">The function being applied.</param>
        /// <param name="supplied">
        /// One entry per parameter: the value given for it, or <see langword="null"/> where a <c>?</c> left
        /// it open.
        /// </param>
        public XdmPartialFunction(XdmFunction function, XPathValue?[] supplied)
        {
            m_function = function;
            m_supplied = supplied;

            foreach (XPathValue? value in supplied)
            {
                if (value is null)
                {
                    m_arity++;
                }
            }
        }

        /// <inheritdoc/>
        public override XdmQName? FunctionName => null;

        /// <inheritdoc/>
        public override int Arity => m_arity;

        /// <inheritdoc/>
        internal override XPathValue Invoke(XPathValue[] arguments, ref DynamicContext context)
        {
            XPathValue[] full = new XPathValue[m_supplied.Length];
            int taken = 0;

            for (int i = 0; i < full.Length; i++)
            {
                full[i] = m_supplied[i] ?? arguments[taken++];
            }

            return m_function.Call(full, ref context);
        }
    }

    /// <summary>
    /// A map seen as the function it also is: <c>$m('a')</c> asks the same question as <c>$m?a</c>.
    /// </summary>
    /// <remarks>
    /// XPath 3.1 does not merely allow this, it defines a map <em>as</em> a function from key to value. The
    /// consequence worth having is that every higher-order function takes one: <c>fn:for-each(('a','b'), $m)</c>
    /// looks up two keys without a wrapper being written.
    /// </remarks>
    internal sealed class XdmMapFunction : XdmFunction
    {
        private readonly XdmMap m_map;

        /// <summary>Initializes a map's function view.</summary>
        /// <param name="map">The map.</param>
        public XdmMapFunction(XdmMap map)
        {
            m_map = map;
        }

        /// <inheritdoc/>
        public override XdmQName? FunctionName => null;

        /// <inheritdoc/>
        public override int Arity => 1;

        /// <inheritdoc/>
        internal override XPathValue Invoke(XPathValue[] arguments, ref DynamicContext context)
        {
            return m_map.Get(XdmSequence.RequireSingleItem(arguments[0], "a map key"));
        }
    }

    /// <summary>
    /// A function item the engine makes rather than the stylesheet: a library function's result, over a
    /// delegate.
    /// </summary>
    /// <remarks>
    /// What <c>fn:random-number-generator()</c> hands back under <c>next</c> and <c>permute</c>. Anonymous,
    /// as the specification has them, and with no declared types, so it is judged by its arity alone. The
    /// delegate is given the arguments and nothing else: a function made this way closes over values the
    /// engine already holds, and has no focus to read.
    /// </remarks>
    internal sealed class XdmNativeFunction : XdmFunction
    {
        private readonly int m_arity;
        private readonly Func<XPathValue[], XPathValue> m_body;

        /// <summary>Initializes a function item over a delegate.</summary>
        /// <param name="arity">How many arguments it takes.</param>
        /// <param name="body">What it does with them.</param>
        public XdmNativeFunction(int arity, Func<XPathValue[], XPathValue> body)
        {
            m_arity = arity;
            m_body = body;
        }

        /// <inheritdoc/>
        public override XdmQName? FunctionName => null;

        /// <inheritdoc/>
        public override int Arity => m_arity;

        /// <inheritdoc/>
        internal override XPathValue Invoke(XPathValue[] arguments, ref DynamicContext context)
        {
            return m_body(arguments);
        }
    }

    /// <summary>An array seen as the function from position to member, so that <c>$a(2)</c> is written.</summary>
    internal sealed class XdmArrayFunction : XdmFunction
    {
        private readonly XdmArray m_array;

        /// <summary>Initializes an array's function view.</summary>
        /// <param name="array">The array.</param>
        public XdmArrayFunction(XdmArray array)
        {
            m_array = array;
        }

        /// <inheritdoc/>
        public override XdmQName? FunctionName => null;

        /// <inheritdoc/>
        public override int Arity => 1;

        /// <inheritdoc/>
        internal override XPathValue Invoke(XPathValue[] arguments, ref DynamicContext context)
        {
            // An array called as a function is an array looked up by position, and a position is an
            // xs:integer: [1,2,3](1.1) is a type error rather than a question about the first member.
            return m_array.Get(MapArrayFunctionExpr.AsPosition(arguments[0]));
        }
    }
    /// <summary>
    /// The focus of a dynamic context — the context item, its position and size, and the current node —
    /// kept on the heap, which the context itself, being a ref struct, cannot be.
    /// </summary>
    /// <remarks>
    /// Only the focus. The frame, the globals and the runtime are the caller's when the function is invoked,
    /// and the same ones in any case.
    /// </remarks>
    internal sealed class CapturedFocus
    {
        private readonly XdmTree m_tree;
        private readonly int[] m_fingerprintMap;
        private readonly int m_node;
        private readonly XPathValue m_atomicItem;
        private readonly int m_currentNode;
        private readonly XdmTree m_currentTree;
        private readonly int m_position;
        private readonly int m_size;
        private readonly XsltRuntime? m_runtime;
        private readonly XPathValue[] m_globals;

        /// <summary>Takes the focus of a context.</summary>
        /// <param name="context">The context.</param>
        public CapturedFocus(ref DynamicContext context)
        {
            m_tree = context.Tree;
            m_fingerprintMap = context.FingerprintMap;
            m_node = context.Node;
            m_atomicItem = context.AtomicItem;
            m_currentNode = context.CurrentNode;
            m_currentTree = context.CurrentTree;
            m_position = context.Position;
            m_size = context.Size;
            m_runtime = context.Runtime;
            m_globals = context.Globals;
        }

        /// <summary>Gives a context this focus.</summary>
        /// <param name="context">The context.</param>
        public void Restore(ref DynamicContext context)
        {
            context.Tree = m_tree;
            context.FingerprintMap = m_fingerprintMap;
            context.Node = m_node;
            context.AtomicItem = m_atomicItem;
            context.CurrentNode = m_currentNode;
            context.CurrentTree = m_currentTree;
            context.Position = m_position;
            context.Size = m_size;

            // And the transformation the reference was made in, with the global variables belonging to it.
            // A function item can be carried out of its transformation — fn:transform() hands one back —
            // and a body that calls a function the stylesheet declares still belongs to that stylesheet.
            if (m_runtime is not null)
            {
                context.Runtime = m_runtime;
                context.Globals = m_globals;
            }
        }
    }

    /// <summary>
    /// A function item wrapped so that it has the type it was asked for: XPath 3.1's function coercion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where a function item is supplied for a declared function type it does not already satisfy, the
    /// specification does not refuse it — it wraps it. The wrapper <em>is</em> of the required type; calling
    /// it converts each argument by the function conversion rules on the way in, calls the function inside,
    /// and converts the result on the way out.
    /// </para>
    /// <para>
    /// So the check moves from where the item was passed to where it is called, which is the point:
    /// <c>string-length#1</c> handed to a parameter of type <c>function(xs:string) as xs:string</c> is
    /// accepted, and fails when it is called and returns an integer. A function that is never called never
    /// fails at all.
    /// </para>
    /// </remarks>
    internal sealed class XdmCoercedFunction : XdmFunction
    {
        private readonly XdmFunction m_function;
        private readonly XdmSequenceType[] m_parameters;
        private readonly XdmSequenceType m_result;

        /// <summary>Wraps a function item in the type it was asked for.</summary>
        /// <param name="function">The function being coerced.</param>
        /// <param name="parameters">The argument types the wrapper has.</param>
        /// <param name="result">The result type the wrapper has.</param>
        public XdmCoercedFunction(
            XdmFunction function, XdmSequenceType[] parameters, XdmSequenceType result)
        {
            m_function = function;
            m_parameters = parameters;
            m_result = result;
        }

        /// <inheritdoc/>
        public override XdmQName? FunctionName => m_function.FunctionName;

        /// <inheritdoc/>
        public override int Arity => m_function.Arity;

        /// <inheritdoc/>
        internal override XdmFunctionSignature? Signature => new XdmFunctionSignature(m_parameters, m_result);

        /// <inheritdoc/>
        internal override XPathValue Invoke(XPathValue[] arguments, ref DynamicContext context)
        {
            XPathValue[] converted = new XPathValue[arguments.Length];

            for (int i = 0; i < arguments.Length; i++)
            {
                converted[i] = XdmTypeConversion.Apply(arguments[i], m_parameters[i]);
            }

            // The arity was checked when this wrapper was called, and the two share it.
            return XdmTypeConversion.Apply(m_function.Invoke(converted, ref context), m_result);
        }
    }

    /// <summary>
    /// A reference to a function that reads where it was written, evaluated there so that the context can
    /// travel with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which is what the specification asks of <c>name#0</c> handed to another function, and of
    /// <c>../accumulator-before#1</c> bound to a parameter: the item reads the node it was made on, however
    /// far it is carried before being called. The reference is not a constant, as every other named function
    /// reference is, because the focus is not.
    /// </para>
    /// <para>
    /// A reference to a function the stylesheet declares is made here for a second reason: its body is run
    /// by the transformation, so the item has to carry that transformation with it. What
    /// <c>fn:transform()</c> hands back may be called long after the transformation that made it ended.
    /// </para>
    /// </remarks>
    internal sealed class FocusCapturingFunctionExpr : Expr
    {
        private readonly XdmQName m_name;
        private readonly int m_arity;
        private readonly Expr m_body;

        /// <summary>Initializes the reference.</summary>
        /// <param name="name">The function's name.</param>
        /// <param name="arity">How many arguments it takes.</param>
        /// <param name="body">The call, compiled over the argument slots.</param>
        public FocusCapturingFunctionExpr(XdmQName name, int arity, Expr body)
        {
            m_name = name;
            m_arity = arity;
            m_body = body;
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            return XPathValue.FromFunction(new XdmNamedFunction(m_name, m_arity, m_body, ref context)
            {
                DeclaredTypes = m_body.DeclaredSignature,
            });
        }
    }

    /// <summary>A call that is refused whenever it is made, with a fixed error.</summary>
    /// <remarks>
    /// The body of a named function reference that the specification allows to exist but not to be called,
    /// which is <c>current#0</c>: the reference is a value like any other, and the error is the call's.
    /// </remarks>
    internal sealed class RefusedCallExpr : Expr
    {
        private readonly XsltErrorCode m_code;
        private readonly string m_message;

        /// <summary>Initializes the refusal.</summary>
        /// <param name="code">The error code the call raises.</param>
        /// <param name="message">What it says.</param>
        public RefusedCallExpr(XsltErrorCode code, string message)
        {
            m_code = code;
            m_message = message;
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            throw XsltErrors.Error(m_code, m_message);
        }
    }
}
