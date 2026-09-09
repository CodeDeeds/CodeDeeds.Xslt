using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// Reads what a caller supplied through <see cref="XsltOptions.Parameters"/> into the values a
    /// transformation binds its stylesheet parameters to.
    /// </summary>
    /// <remarks>
    /// The whole of the boundary between a .NET caller and the XPath data model lives here, which is why it
    /// is a class of its own rather than a couple of helpers on the runtime: what a caller may pass, and what
    /// each thing becomes, is the contract, and it should be readable in one place.
    /// </remarks>
    internal static class StylesheetParameters
    {
        /// <summary>
        /// Reads a parameter name as the caller wrote it.
        /// </summary>
        /// <remarks>
        /// A local name on its own, or an expanded name in the <c>{uri}local</c> notation. A prefix is refused
        /// rather than guessed at: the prefixes in scope belong to the stylesheet, and the caller has no way
        /// to know them — a name that looks bound would be resolved against nothing.
        /// </remarks>
        /// <param name="name">The key from the caller's dictionary.</param>
        public static ExpandedName ParseName(string name)
        {
            if (name.Length != 0 && name[0] == '{')
            {
                int close = name.IndexOf('}');
                if (close < 0)
                {
                    throw new XsltException(
                        $"The parameter name '{name}' opens with '{{' and never closes it. A name in a "
                        + "namespace is written {uri}local.");
                }

                return new ExpandedName(name[1..close], name[(close + 1)..]);
            }

            if (name.IndexOf(':') >= 0)
            {
                throw new XsltException(
                    $"The parameter name '{name}' carries a prefix. Prefixes here would have nothing to "
                    + "resolve them against, since they are the caller's and not the stylesheet's; write "
                    + "{uri}local instead.");
            }

            return new ExpandedName(string.Empty, name);
        }

        /// <summary>
        /// Converts a value a caller supplied into an XPath value.
        /// </summary>
        /// <remarks>
        /// Deliberately a closed list. Falling back on <see cref="object.ToString"/> for anything unrecognized
        /// would turn a caller's mistake — passing an object where a string was meant — into a stylesheet that
        /// runs and produces something nobody asked for.
        /// </remarks>
        /// <param name="name">The parameter's name, for the error message.</param>
        /// <param name="value">What the caller supplied.</param>
        public static XPathValue Convert(string name, object? value)
        {
            switch (value)
            {
                case null:
                    // Nothing is a value in XPath 2.0: the empty sequence. It reads as an empty string and
                    // counts zero, which is what a caller passing null means by it.
                    return XPathValue.FromSequence(XdmSequence.Empty);

                case string text:
                    return XPathValue.FromString(text);

                case bool flag:
                    return XPathValue.FromBoolean(flag);

                case int number:
                    return XPathValue.FromInteger(number);

                case long number:
                    return XPathValue.FromInteger(number);

                case short number:
                    return XPathValue.FromInteger(number);

                case sbyte number:
                    return XPathValue.FromInteger(number);

                case byte number:
                    return XPathValue.FromInteger(number);

                case ushort number:
                    return XPathValue.FromInteger(number);

                case uint number:
                    return XPathValue.FromInteger(number);

                case ulong number when number <= long.MaxValue:
                    return XPathValue.FromInteger((long)number);

                case double number:
                    return XPathValue.FromNumber(number);

                case float number:
                    return XPathValue.FromFloat(number);

                case decimal number:
                    return XPathValue.FromDecimal(number);

                case XdmTree tree:
                    // The document node, so a path written against the parameter starts where it would in a
                    // document the stylesheet had loaded itself.
                    return XPathValue.FromNodeSet(NodeSet.Singleton(tree, XdmTree.RootNode));

                case XPathValue built:
                    return built;

                default:
                    throw new XsltException(
                        $"The value supplied for parameter '{name}' is a {value.GetType().Name}, which has no "
                        + "XPath counterpart. Supply a string, a boolean, a number, an XdmTree, or null for "
                        + "the empty sequence; a date or duration is passed as its lexical form and read with "
                        + "xs:dateTime() or its relatives in the stylesheet.");
            }
        }
    }
}
