using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The XPath 3.0 functions that make nodes out of text.</summary>
    internal enum NodeBuildingFunction : byte
    {
        /// <summary><c>fn:parse-xml</c>.</summary>
        ParseXml,

        /// <summary><c>fn:parse-xml-fragment</c>.</summary>
        ParseXmlFragment,

        /// <summary><c>fn:analyze-string</c>.</summary>
        AnalyzeString,
    }

    /// <summary>
    /// A call to one of the functions that build a tree from a string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three functions with one thing in common: each answers with nodes that were not in any input document,
    /// which before XPath 3.0 only <c>document()</c> and a result tree fragment could do.
    /// </para>
    /// <para>
    /// <c>fn:analyze-string</c> is <c>xsl:analyze-string</c> with the two branches replaced by a fixed
    /// structure — so the same regular expression translation and the same refusal of a zero-width pattern
    /// serve both, and a stylesheet can now do the analysis inside an expression rather than as an
    /// instruction.
    /// </para>
    /// </remarks>
    internal sealed class NodeBuildingFunctionExpr : Expr
    {
        private readonly NodeBuildingFunction m_function;
        private readonly Expr[] m_arguments;

        private NodeBuildingFunctionExpr(NodeBuildingFunction function, Expr[] arguments)
        {
            m_function = function;
            m_arguments = arguments;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => true;

        /// <summary>Always: every one of these answers with a tree of its own making.</summary>
        public override bool MaySpanDocuments => true;

        /// <summary>
        /// Creates a call, or returns <see langword="null"/> where the name is not one of these.
        /// </summary>
        /// <param name="name">The function's local name.</param>
        /// <param name="arguments">The compiled arguments.</param>
        /// <param name="version">The version in force, for the argument type checks.</param>
        public static Expr? TryCreate(string name, Expr[] arguments, XsltVersion version)
        {
            if (Find(name)
                is not (NodeBuildingFunction function, int least, int most, string signature))
            {
                return null;
            }

            if (arguments.Length < least || arguments.Length > most)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'fn:{name}()' takes " + (least == most ? $"{least}" : $"{least} to {most}")
                    + $" arguments, and was given {arguments.Length}.");
            }

            return new NodeBuildingFunctionExpr(
                function,
                CheckedArgumentExpr.Wrap(arguments, FunctionParameter.Parse(signature), name, version));
        }

        /// <summary>The function a name denotes, with how many arguments it takes and their types.</summary>
        /// <remarks>
        /// One table, read both by <see cref="TryCreate"/> and by <see cref="TakesArity"/>, so that what
        /// <c>fn:function-available()</c> reports and what a call may be written cannot come apart.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        private static (NodeBuildingFunction Function, int Least, int Most, string Signature)? Find(
            string name)
        {
            return name switch
            {
                "parse-xml" => (NodeBuildingFunction.ParseXml, 1, 1, "xs:string?"),
                "parse-xml-fragment" => (NodeBuildingFunction.ParseXmlFragment, 1, 1, "xs:string?"),
                "analyze-string" =>
                    (NodeBuildingFunction.AnalyzeString, 2, 3, "xs:string?, xs:string, xs:string"),
                _ => null,
            };
        }

        /// <summary>Whether a function of this name will take a given number of arguments.</summary>
        /// <param name="name">The function's local name.</param>
        /// <param name="arity">How many arguments the caller is asking about.</param>
        public static bool TakesArity(string name, int arity)
        {
            return Find(name) is (NodeBuildingFunction _, int least, int most, string _)
                && arity >= least
                && arity <= most;
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            if (m_function == NodeBuildingFunction.AnalyzeString)
            {
                return Analyze(ref context);
            }

            XPathValue text = m_arguments[0].Evaluate(ref context);

            // Declared xs:string? -> document-node()?, so nothing in is nothing out rather than an empty
            // document, which is not a thing XML has.
            if (Xpath2FunctionExpr.IsEmptySequence(text))
            {
                return text;
            }

            bool fragment = m_function == NodeBuildingFunction.ParseXmlFragment;

            try
            {
                XdmTree tree = XdmTreeBuilder.FromXml(
                    text.ToStringValue(), fragment, context.Runtime is null ? null : context.Tree.NameTable);

                return XPathValue.FromNodeSet(NodeSet.Singleton(tree, XdmTree.RootNode));
            }
            catch (XmlException exception)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FODC0006,
                    $"fn:parse-xml{(fragment ? "-fragment" : string.Empty)}() was given text that is not "
                    + $"well-formed: {exception.Message}",
                    exception);
            }
        }

        /// <summary>
        /// Builds the <c>analyze-string-result</c> element, whose children alternate between what matched
        /// and what did not.
        /// </summary>
        private XPathValue Analyze(ref DynamicContext context)
        {
            XPathValue value = m_arguments[0].Evaluate(ref context);

            // An absent input is the zero-length string here rather than nothing: the result is declared as
            // an element and not an optional one, so there is always a result element to build.
            string input = Xpath2FunctionExpr.IsEmptySequence(value) ? string.Empty : value.ToStringValue();

            string pattern = m_arguments[1].Evaluate(ref context).ToStringValue();
            string flags = m_arguments.Length > 2
                ? m_arguments[2].Evaluate(ref context).ToStringValue()
                : string.Empty;

            // fn:analyze-string exists only at 3.0, so its patterns are read by 3.0's rules by construction.
            Regex regex = RegexTranslator.Translate(pattern, flags, XsltVersion.V30);

            // Walking the matches of a pattern that occupies no width would never move past the first
            // position, which is why every function that walks them refuses one.
            RegexTranslator.RequireWidth(regex, pattern, "analyze-string");

            XdmTreeBuilder builder = new XdmTreeBuilder();
            builder.StartElement(string.Empty, JsonTreeBuilder.XPathFunctionsNamespace, "analyze-string-result");

            int position = 0;

            foreach (Match match in regex.Matches(input))
            {
                if (match.Index > position)
                {
                    Wrap(builder, "non-match", input[position..match.Index]);
                }

                builder.StartElement(string.Empty, JsonTreeBuilder.XPathFunctionsNamespace, "match");
                WriteGroups(builder, match);
                builder.EndElement();

                position = match.Index + match.Length;
            }

            if (position < input.Length)
            {
                Wrap(builder, "non-match", input[position..]);
            }

            builder.EndElement();

            // The result is the element, not the document node holding it, which the signature says: this is
            // the one of the three that answers with an element.
            XdmTree tree = builder.Finish();
            return XPathValue.FromNodeSet(NodeSet.Singleton(tree, XdmTree.RootNode + 1));
        }

        private static void Wrap(XdmTreeBuilder builder, string name, string text)
        {
            builder.StartElement(string.Empty, JsonTreeBuilder.XPathFunctionsNamespace, name);
            builder.AddText(text);
            builder.EndElement();
        }

        /// <summary>
        /// Writes a match's text with its capturing groups marked, nested as they are in the pattern.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The nesting has to be rebuilt rather than read off. A <see cref="Match"/> reports where each group
        /// landed and nothing about which group is inside which, so the spans are sorted — earliest first,
        /// and the longer of two that start together first, since that is the one that contains the other —
        /// and a stack turns them back into a tree.
        /// </para>
        /// <para>
        /// A group that did not participate contributes nothing, which is what separates <c>(a)|(b)</c>
        /// matching <c>a</c> from it matching an empty <c>b</c> as well.
        /// </para>
        /// </remarks>
        private static void WriteGroups(XdmTreeBuilder builder, Match match)
        {
            List<(int Number, int Start, int End)> spans = new List<(int, int, int)>();

            for (int i = 1; i < match.Groups.Count; i++)
            {
                Group group = match.Groups[i];

                // Only the groups the regular expression numbers, so a named group that .NET also numbers
                // separately does not appear twice.
                if (group.Success && int.TryParse(
                    group.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                {
                    spans.Add((number, group.Index, group.Index + group.Length));
                }
            }

            spans.Sort((left, right) =>
            {
                int order = left.Start.CompareTo(right.Start);
                if (order != 0)
                {
                    return order;
                }

                // The longer of two groups starting together contains the other, so it opens first.
                order = right.End.CompareTo(left.End);
                return order != 0 ? order : left.Number.CompareTo(right.Number);
            });

            int position = match.Index;
            int end = match.Index + match.Length;
            Stack<int> open = new Stack<int>();

            foreach ((int number, int start, int stop) in spans)
            {
                while (open.Count > 0 && open.Peek() <= start)
                {
                    position = Close(builder, match, open, position);
                }

                position = Text(builder, match, position, start);

                builder.StartElement(string.Empty, JsonTreeBuilder.XPathFunctionsNamespace, "group");
                builder.AddAttribute(
                    string.Empty, string.Empty, "nr", number.ToString(CultureInfo.InvariantCulture));

                open.Push(stop);
            }

            while (open.Count > 0)
            {
                position = Close(builder, match, open, position);
            }

            Text(builder, match, position, end);
        }

        /// <summary>Closes the innermost group, writing whatever text is still inside it first.</summary>
        private static int Close(XdmTreeBuilder builder, Match match, Stack<int> open, int position)
        {
            position = Text(builder, match, position, open.Pop());
            builder.EndElement();
            return position;
        }

        /// <summary>Writes the text between two offsets of the subject, and returns the later one.</summary>
        private static int Text(XdmTreeBuilder builder, Match match, int from, int to)
        {
            if (to > from)
            {
                // The offsets are into the whole subject; the match knows where it starts in it.
                builder.AddText(match.Value.Substring(from - match.Index, to - from));
            }

            return Math.Max(from, to);
        }
    }
}
