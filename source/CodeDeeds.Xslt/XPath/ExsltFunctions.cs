using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>Which function of the EXSLT Common module a call is.</summary>
    internal enum ExsltCommonFunction : byte
    {
        /// <summary><c>exsl:node-set</c>.</summary>
        NodeSet,

        /// <summary><c>exsl:object-type</c>.</summary>
        ObjectType,
    }

    /// <summary>
    /// A call to one of the two functions of the EXSLT Common module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXSLT is not a W3C specification. It is the extension library XSLT 1.0 processors converged on, and
    /// its Common module exists because XSLT 1.0 had a type a stylesheet could build and could not then look
    /// inside: the result tree fragment. <c>exsl:node-set()</c> turned one into a node-set, and every
    /// stylesheet that had to work on what it had just built called it.
    /// </para>
    /// <para>
    /// XSLT 2.0 removed the restriction, so here the function is very nearly the identity. That is not why
    /// it is implemented. A 1.0 stylesheet does not call it unguarded: it asks
    /// <c>function-available('exsl:node-set')</c> first and writes a different branch for the answer no, and
    /// those branches are not always merely worse at navigating. The DocBook 1.79.1 stylesheets count the
    /// elements of a title page with it and, told the function is unavailable, assume the count is one and
    /// emit an <c>fo:block</c> around nothing — nine of them in the suite's docbook-002, which is the whole
    /// of why that test's element count came out at 628 against the 619 it asks for. Answering the question
    /// truthfully is what makes the guarded branch reachable.
    /// </para>
    /// <para>
    /// Both functions are available whatever version is in force. What they do is defined in terms of the
    /// XPath 1.0 type system and every one of those types still exists, so there is no version at which the
    /// answers would have to change — and a 2.0 stylesheet importing a 1.0 one inherits its calls.
    /// </para>
    /// </remarks>
    internal sealed class ExsltFunctionExpr : Expr
    {
        /// <summary>The namespace the EXSLT Common module is named by.</summary>
        public const string CommonNamespace = "http://exslt.org/common";

        private readonly ExsltCommonFunction m_function;
        private readonly Expr m_argument;

        private ExsltFunctionExpr(ExsltCommonFunction function, Expr argument)
        {
            m_function = function;
            m_argument = argument;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => new[] { m_argument };

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => m_function == ExsltCommonFunction.NodeSet;

        /// <summary>
        /// Whether the nodes may come from more than one tree, which for <c>exsl:node-set()</c> they may:
        /// its argument is whatever the caller had, and a non-node argument makes a tree of its own.
        /// </summary>
        public override bool MaySpanDocuments => m_function == ExsltCommonFunction.NodeSet;

        /// <summary>
        /// Creates a call, or returns <see langword="null"/> where the name is not one of these.
        /// </summary>
        /// <param name="namespaceUri">The namespace the call's prefix resolved to.</param>
        /// <param name="localName">The function's local name.</param>
        /// <param name="arguments">The compiled arguments.</param>
        public static Expr? TryCreate(string namespaceUri, string localName, Expr[] arguments)
        {
            if (namespaceUri != CommonNamespace || Find(localName) is not ExsltCommonFunction function)
            {
                return null;
            }

            if (arguments.Length != 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'exsl:{localName}()' takes one argument, and was given {arguments.Length}.");
            }

            return new ExsltFunctionExpr(function, arguments[0]);
        }

        /// <summary>The function a name denotes, or nothing where the module has no such name.</summary>
        /// <remarks>
        /// One table, read both by <see cref="TryCreate"/> and by <see cref="TakesArity"/>, so that what
        /// <c>function-available()</c> reports and what a call may be written cannot come apart.
        /// </remarks>
        /// <param name="localName">The function's local name.</param>
        private static ExsltCommonFunction? Find(string localName)
        {
            return localName switch
            {
                "node-set" => ExsltCommonFunction.NodeSet,
                "object-type" => ExsltCommonFunction.ObjectType,
                _ => null,
            };
        }

        /// <summary>Whether a function of this name will take a given number of arguments.</summary>
        /// <param name="namespaceUri">The name's namespace URI.</param>
        /// <param name="localName">The function's local name.</param>
        /// <param name="arity">How many arguments the caller is asking about, or -1 for any.</param>
        public static bool TakesArity(string namespaceUri, string localName, int arity)
        {
            return namespaceUri == CommonNamespace && Find(localName) is not null && arity is -1 or 1;
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue value = m_argument.Evaluate(ref context);

            return m_function == ExsltCommonFunction.NodeSet
                ? AsNodeSet(value, ref context)
                : XPathValue.FromString(TypeOf(value));
        }

        /// <summary>
        /// The EXSLT reading of a value as a node-set: nodes stay as they are, and anything else becomes a
        /// single text node holding its string.
        /// </summary>
        /// <remarks>
        /// An empty sequence is returned as it came. It is already a node-set with nothing in it by every
        /// question that can be asked of it, and making one would mean naming a tree for it to belong to
        /// that no node of it is in.
        /// </remarks>
        /// <param name="value">The value the argument produced.</param>
        /// <param name="context">The context, for the tree an empty node-set would be said to belong to.</param>
        private static XPathValue AsNodeSet(XPathValue value, ref DynamicContext context)
        {
            if (value.Kind == XPathValueKind.NodeSet)
            {
                return value;
            }

            if (value.Kind == XPathValueKind.Node)
            {
                return XPathValue.FromNodeSet(NodeSet.Singleton(value.NodeTree, value.NodeId));
            }

            if (value.Kind == XPathValueKind.Sequence)
            {
                List<XPathValue> items = XdmSequence.Items(value);

                if (items.Count == 0 || IsAllNodes(items))
                {
                    return items.Count == 0
                        ? value
                        : XPathValue.FromNodeSet(
                            NodeSet.Of(value, context.Tree, XsltErrorCode.XPTY0004, "exsl:node-set()"));
                }
            }

            XdmTreeBuilder builder = new XdmTreeBuilder();
            builder.AddText(value.ToStringValue(), keepEmpty: true);
            int text = builder.FirstTopLevelNode;

            return XPathValue.FromNodeSet(NodeSet.Singleton(builder.Finish(), text));
        }

        /// <summary>Whether every item of a sequence is a node.</summary>
        /// <param name="items">The items to look at.</param>
        private static bool IsAllNodes(List<XPathValue> items)
        {
            foreach (XPathValue item in items)
            {
                if (item.Kind != XPathValueKind.Node)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// What <c>exsl:object-type()</c> answers.
        /// </summary>
        /// <remarks>
        /// EXSLT names six answers and this engine can give five of them. The sixth is <c>RTF</c>, for a
        /// result tree fragment, and there are none: what an XSLT 1.0 processor would hand back as one is
        /// here an ordinary document node, so it answers <c>node-set</c>. That is the same answer a
        /// stylesheet gets from any 2.0 processor, and it is the one that makes the usual test — treat it as
        /// nodes if it is nodes — come out right.
        /// </remarks>
        /// <param name="value">The value to describe.</param>
        private static string TypeOf(XPathValue value)
        {
            switch (value.Kind)
            {
                case XPathValueKind.NodeSet:
                case XPathValueKind.Node:
                    return "node-set";

                case XPathValueKind.Boolean:
                    return "boolean";

                case XPathValueKind.Number:
                    return "number";

                case XPathValueKind.String:
                    // Every atomic value that is not a number or a boolean arrives as this kind, a date and
                    // a URI included, and XPath 1.0 had one name for all of them.
                    return "string";

                case XPathValueKind.Sequence:
                    // A sequence of nodes is what a 1.0 stylesheet would have called a node-set. One holding
                    // anything else is a value XPath 1.0 has no name for at all, which is what external is
                    // there for: it means a type this processor has and the module does not describe.
                    return IsAllNodes(XdmSequence.Items(value)) ? "node-set" : "external";

                default:
                    // A map, an array or a function item.
                    return "external";
            }
        }
    }
}
