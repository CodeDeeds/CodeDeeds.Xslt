using System.Collections;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// A call to <c>fn:environment-variable()</c> or <c>fn:available-environment-variables()</c>: the
    /// process's environment, where the caller has said a stylesheet may read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The specification lets a processor decide whether environment variables are visible at all, and
    /// this one leaves the decision to the caller through
    /// <see cref="XsltOptions.EnvironmentVariablesEnabled"/>, off by default: an environment carries
    /// tokens and connection strings as often as it carries locale settings, and a stylesheet is data as
    /// often as it is code. Not enabled, both functions answer nothing, which is the answer the
    /// specification gives a processor that provides no access. Enabled, they read the environment as it
    /// stood when the transformation first asked, so that it is the same environment for the whole
    /// transformation, which the specification requires of them.
    /// </para>
    /// <para>
    /// The argument is evaluated whether or not the environment is visible: an error in it is the
    /// caller's, and a name that is not a string is refused either way.
    /// </para>
    /// </remarks>
    internal sealed class EnvironmentVariableExpr : Expr
    {
        private readonly Expr? m_name;

        private EnvironmentVariableExpr(Expr? name)
        {
            m_name = name;
        }

        /// <summary>
        /// Whether the environment is visible where no transformation is running to say: a static
        /// expression, a <c>use-when</c>, answered before any transformation starts.
        /// </summary>
        /// <remarks>
        /// Set by the stylesheet compiler from the options it was given. A running transformation answers
        /// for itself, from its own options, and those are the same options.
        /// </remarks>
        internal bool EnabledStatically { get; set; }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_name is null ? Array.Empty<Expr>() : new[] { m_name };

        /// <summary>Creates the call.</summary>
        /// <param name="name">The function's local name, which says which of the two this is.</param>
        /// <param name="arguments">The compiled arguments: the variable's name, or nothing.</param>
        /// <param name="version">The version in force, which settles how the argument is converted.</param>
        /// <exception cref="XsltException"><c>XPST0017</c> where the argument count is wrong.</exception>
        public static Expr Create(string name, Expr[] arguments, XsltVersion version)
        {
            if (name == "available-environment-variables")
            {
                if (arguments.Length != 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPST0017,
                        $"'{name}()' takes no argument, and was given {arguments.Length}.");
                }

                return new EnvironmentVariableExpr(null);
            }

            if (arguments.Length != 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'{name}()' takes one argument, the variable's name, and was given {arguments.Length}.");
            }

            Expr[] variable = CheckedArgumentExpr.Wrap(
                arguments, FunctionParameter.Parse("xs:string"), name, version);

            return new EnvironmentVariableExpr(variable[0]);
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            // The argument first, whatever the answer will be: an error in it happened before the
            // question of visibility arose.
            string? wanted = m_name is null ? null : XdmSequence.StringValueOf(m_name.Evaluate(ref context));

            bool enabled = context.Runtime is XsltRuntime runtime
                ? runtime.EnvironmentVariablesEnabled
                : EnabledStatically;

            if (!enabled)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            IReadOnlyDictionary<string, string> environment = context.Runtime?.EnvironmentVariables ?? Snapshot();

            if (wanted is not null)
            {
                return environment.TryGetValue(wanted, out string? value)
                    ? XPathValue.FromString(value)
                    : XPathValue.FromSequence(XdmSequence.Empty);
            }

            // In name order, so that the sequence is the same one on every run and every platform, which
            // a dictionary's own order is not.
            string[] names = environment.Keys.ToArray();
            Array.Sort(names, StringComparer.Ordinal);

            XPathValue[] items = new XPathValue[names.Length];

            for (int index = 0; index < names.Length; index++)
            {
                items[index] = XPathValue.FromString(names[index]);
            }

            return XdmSequence.Concatenate(items);
        }

        /// <summary>
        /// The process's environment as it stands now.
        /// </summary>
        /// <remarks>
        /// Compared the way the platform compares names: without regard to case on Windows, where
        /// <c>Path</c> and <c>PATH</c> are one variable, and exactly elsewhere.
        /// </remarks>
        internal static IReadOnlyDictionary<string, string> Snapshot()
        {
            Dictionary<string, string> variables = new(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string name && entry.Value is string value)
                {
                    variables[name] = value;
                }
            }

            return variables;
        }
    }
}
