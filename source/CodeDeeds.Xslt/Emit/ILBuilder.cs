using System.Reflection;
using System.Reflection.Emit;

namespace CodeDeeds.Xslt.Emit
{
    /// <summary>
    /// Wraps an <see cref="ILGenerator"/> and tracks how deep the evaluation stack is at every point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A method whose stack does not balance fails at run time as
    /// <c>InvalidProgramException: Common Language Runtime detected an invalid program</c> — with no offset, no
    /// opcode, and no indication of which of several hundred emitted instructions was wrong. Finding a codegen
    /// bug from that alone is close to hopeless.
    /// </para>
    /// <para>
    /// So the depth is maintained here instead, and checked where it can actually be verified: every path
    /// reaching a label must arrive with the same depth, and a method must return with exactly what its
    /// signature calls for. A violation throws immediately, naming the operation that caused it, while the
    /// emitter is still on the stack.
    /// </para>
    /// <para>
    /// Only semantic operations are exposed rather than raw opcodes, because each one has to declare its own
    /// effect on the stack; there is deliberately no general "emit any opcode" escape hatch.
    /// </para>
    /// </remarks>
    internal sealed class ILBuilder
    {
        private readonly ILGenerator m_il;
        private readonly string m_methodName;
        private readonly Dictionary<Label, int> m_depthAtLabel = new();
        private readonly Dictionary<Label, string> m_labelNames = new();
        private int m_depth;
        private int m_maximumDepth;
        private bool m_reachable = true;

        /// <summary>Initializes a builder over a method's generator.</summary>
        /// <param name="il">The generator to emit into.</param>
        /// <param name="methodName">The method's name, used in diagnostics.</param>
        public ILBuilder(ILGenerator il, string methodName)
        {
            m_il = il;
            m_methodName = methodName;
        }

        /// <summary>Gets the current depth of the evaluation stack.</summary>
        public int Depth => m_depth;

        /// <summary>Gets the greatest depth reached so far.</summary>
        public int MaximumDepth => m_maximumDepth;

        /// <summary>Declares a local variable.</summary>
        /// <param name="type">The local's type.</param>
        /// <returns>The declared local.</returns>
        public LocalBuilder DeclareLocal(Type type)
        {
            return m_il.DeclareLocal(type);
        }

        /// <summary>Defines a label.</summary>
        /// <param name="name">A name for the label, used in diagnostics.</param>
        /// <returns>The new label.</returns>
        public Label DefineLabel(string name)
        {
            Label label = m_il.DefineLabel();
            m_labelNames[label] = name;
            return label;
        }

        /// <summary>
        /// Marks the position of a label, checking that every path arriving here agrees on the stack depth.
        /// </summary>
        /// <param name="label">The label to mark.</param>
        /// <exception cref="InvalidOperationException">Branches to this label disagree about the stack depth.</exception>
        public void MarkLabel(Label label)
        {
            if (m_depthAtLabel.TryGetValue(label, out int expected))
            {
                if (m_reachable && expected != m_depth)
                {
                    throw Failure(
                        $"label '{Describe(label)}' is reached with stack depth {m_depth} by fall-through "
                        + $"but {expected} by an earlier branch");
                }

                m_depth = expected;
            }
            else
            {
                m_depthAtLabel[label] = m_depth;
            }

            // Code after an unconditional branch is only reachable once something jumps back to it.
            m_reachable = true;
            m_il.MarkLabel(label);
        }

        /// <summary>Loads an argument by index.</summary>
        /// <param name="index">The zero-based argument index.</param>
        public void LoadArgument(int index)
        {
            switch (index)
            {
                case 0: m_il.Emit(OpCodes.Ldarg_0); break;
                case 1: m_il.Emit(OpCodes.Ldarg_1); break;
                case 2: m_il.Emit(OpCodes.Ldarg_2); break;
                case 3: m_il.Emit(OpCodes.Ldarg_3); break;
                default: m_il.Emit(OpCodes.Ldarg, index); break;
            }

            Push(1, "ldarg");
        }

        /// <summary>Loads a local's value.</summary>
        /// <param name="local">The local to load.</param>
        public void LoadLocal(LocalBuilder local)
        {
            m_il.Emit(OpCodes.Ldloc, local);
            Push(1, "ldloc");
        }

        /// <summary>Loads the address of a local.</summary>
        /// <param name="local">The local whose address is wanted.</param>
        public void LoadLocalAddress(LocalBuilder local)
        {
            m_il.Emit(OpCodes.Ldloca, local);
            Push(1, "ldloca");
        }

        /// <summary>Stores the top of the stack into a local.</summary>
        /// <param name="local">The destination local.</param>
        public void StoreLocal(LocalBuilder local)
        {
            Pop(1, "stloc");
            m_il.Emit(OpCodes.Stloc, local);
        }

        /// <summary>Loads a 32-bit integer constant.</summary>
        /// <param name="value">The value to load.</param>
        public void LoadInt(int value)
        {
            switch (value)
            {
                case -1: m_il.Emit(OpCodes.Ldc_I4_M1); break;
                case 0: m_il.Emit(OpCodes.Ldc_I4_0); break;
                case 1: m_il.Emit(OpCodes.Ldc_I4_1); break;
                case 2: m_il.Emit(OpCodes.Ldc_I4_2); break;
                case 3: m_il.Emit(OpCodes.Ldc_I4_3); break;
                case 4: m_il.Emit(OpCodes.Ldc_I4_4); break;
                case 5: m_il.Emit(OpCodes.Ldc_I4_5); break;
                case 6: m_il.Emit(OpCodes.Ldc_I4_6); break;
                case 7: m_il.Emit(OpCodes.Ldc_I4_7); break;
                case 8: m_il.Emit(OpCodes.Ldc_I4_8); break;
                default:
                    if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
                    {
                        m_il.Emit(OpCodes.Ldc_I4_S, (sbyte)value);
                    }
                    else
                    {
                        m_il.Emit(OpCodes.Ldc_I4, value);
                    }

                    break;
            }

            Push(1, "ldc.i4");
        }

        /// <summary>Loads a double constant.</summary>
        /// <param name="value">The value to load.</param>
        public void LoadDouble(double value)
        {
            m_il.Emit(OpCodes.Ldc_R8, value);
            Push(1, "ldc.r8");
        }

        /// <summary>Loads a string constant.</summary>
        /// <param name="value">The value to load.</param>
        public void LoadString(string value)
        {
            m_il.Emit(OpCodes.Ldstr, value);
            Push(1, "ldstr");
        }

        /// <summary>Loads a field from the instance or address on the stack.</summary>
        /// <param name="field">The field to load.</param>
        public void LoadField(FieldInfo field)
        {
            Pop(1, "ldfld");
            m_il.Emit(OpCodes.Ldfld, field);
            Push(1, "ldfld");
        }

        /// <summary>Loads an element from an object array on the stack.</summary>
        public void LoadElementReference()
        {
            Pop(2, "ldelem.ref");
            m_il.Emit(OpCodes.Ldelem_Ref);
            Push(1, "ldelem.ref");
        }

        /// <summary>Loads an element from an <see cref="int"/> array on the stack.</summary>
        public void LoadElementInt()
        {
            Pop(2, "ldelem.i4");
            m_il.Emit(OpCodes.Ldelem_I4);
            Push(1, "ldelem.i4");
        }

        /// <summary>Casts the reference on the stack, throwing at run time if it does not fit.</summary>
        /// <param name="type">The type to cast to.</param>
        public void CastClass(Type type)
        {
            m_il.Emit(OpCodes.Castclass, type);
        }

        /// <summary>
        /// Calls a method, adjusting the tracked depth by its actual signature.
        /// </summary>
        /// <param name="method">The method to call.</param>
        /// <param name="virtualCall">Whether to emit <c>callvirt</c> rather than <c>call</c>.</param>
        public void Call(MethodInfo method, bool virtualCall = false)
        {
            int arguments = method.GetParameters().Length + (method.IsStatic ? 0 : 1);
            Pop(arguments, $"call {method.Name}");

            m_il.Emit(virtualCall ? OpCodes.Callvirt : OpCodes.Call, method);

            if (method.ReturnType != typeof(void))
            {
                Push(1, $"call {method.Name}");
            }
        }

        /// <summary>Emits an arithmetic or comparison operation that consumes two values and produces one.</summary>
        /// <param name="opCode">The opcode to emit.</param>
        /// <param name="name">A name for diagnostics.</param>
        public void BinaryOperation(OpCode opCode, string name)
        {
            Pop(2, name);
            m_il.Emit(opCode);
            Push(1, name);
        }

        /// <summary>Emits an operation that replaces the top of the stack with another single value.</summary>
        /// <param name="opCode">The opcode to emit.</param>
        /// <param name="name">A name for diagnostics.</param>
        public void UnaryOperation(OpCode opCode, string name)
        {
            Pop(1, name);
            m_il.Emit(opCode);
            Push(1, name);
        }

        /// <summary>Discards the top of the stack.</summary>
        public void Pop()
        {
            Pop(1, "pop");
            m_il.Emit(OpCodes.Pop);
        }

        /// <summary>Duplicates the top of the stack.</summary>
        public void Duplicate()
        {
            Pop(1, "dup");
            Push(2, "dup");
            m_il.Emit(OpCodes.Dup);
        }

        /// <summary>Branches unconditionally.</summary>
        /// <param name="label">The destination.</param>
        public void Branch(Label label)
        {
            RecordBranch(label, "br");
            m_il.Emit(OpCodes.Br, label);
            m_reachable = false;
        }

        /// <summary>Branches when the value on the stack is false or zero.</summary>
        /// <param name="label">The destination.</param>
        public void BranchIfFalse(Label label)
        {
            Pop(1, "brfalse");
            RecordBranch(label, "brfalse");
            m_il.Emit(OpCodes.Brfalse, label);
        }

        /// <summary>Branches when the value on the stack is true or non-zero.</summary>
        /// <param name="label">The destination.</param>
        public void BranchIfTrue(Label label)
        {
            Pop(1, "brtrue");
            RecordBranch(label, "brtrue");
            m_il.Emit(OpCodes.Brtrue, label);
        }

        /// <summary>Branches on a comparison of the two values on the stack.</summary>
        /// <param name="opCode">The branch opcode.</param>
        /// <param name="label">The destination.</param>
        /// <param name="name">A name for diagnostics.</param>
        public void BranchComparing(OpCode opCode, Label label, string name)
        {
            Pop(2, name);
            RecordBranch(label, name);
            m_il.Emit(opCode, label);
        }

        /// <summary>
        /// Returns from the method, checking that the stack holds exactly what the signature promises.
        /// </summary>
        /// <param name="returnsValue">Whether the method has a return value.</param>
        /// <exception cref="InvalidOperationException">The stack does not match the signature.</exception>
        public void Return(bool returnsValue)
        {
            int expected = returnsValue ? 1 : 0;
            if (m_depth != expected)
            {
                throw Failure(
                    $"return leaves {m_depth} value(s) on the stack, but the signature requires {expected}");
            }

            m_il.Emit(OpCodes.Ret);
            m_depth = 0;
            m_reachable = false;
        }

        private void RecordBranch(Label label, string operation)
        {
            if (m_depthAtLabel.TryGetValue(label, out int expected))
            {
                if (expected != m_depth)
                {
                    throw Failure(
                        $"{operation} to '{Describe(label)}' with stack depth {m_depth}, "
                        + $"but another path reaches it with {expected}");
                }

                return;
            }

            m_depthAtLabel[label] = m_depth;
        }

        private void Push(int count, string operation)
        {
            m_depth += count;
            if (m_depth > m_maximumDepth)
            {
                m_maximumDepth = m_depth;
            }
        }

        private void Pop(int count, string operation)
        {
            if (m_depth < count)
            {
                throw Failure($"{operation} needs {count} value(s) but the stack holds {m_depth}");
            }

            m_depth -= count;
        }

        private string Describe(Label label)
        {
            return m_labelNames.TryGetValue(label, out string? name) ? name : "unnamed";
        }

        private InvalidOperationException Failure(string message)
        {
            return new InvalidOperationException(
                $"Code generation for '{m_methodName}' is inconsistent: {message}. "
                + "This is a bug in the XSLT compiler, not in the stylesheet.");
        }
    }
}
