using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The core-library functions that take a function as an argument, or ask one about itself.</summary>
    internal enum HigherOrderFunction : byte
    {
        /// <summary><c>fn:function-arity</c>.</summary>
        FunctionArity,

        /// <summary><c>fn:function-name</c>.</summary>
        FunctionName,

        /// <summary><c>fn:for-each</c>.</summary>
        ForEach,

        /// <summary><c>fn:filter</c>.</summary>
        Filter,

        /// <summary><c>fn:fold-left</c>.</summary>
        FoldLeft,

        /// <summary><c>fn:fold-right</c>.</summary>
        FoldRight,

        /// <summary><c>fn:for-each-pair</c>.</summary>
        ForEachPair,

        /// <summary><c>fn:sort</c>.</summary>
        Sort,

        /// <summary><c>fn:apply</c>.</summary>
        Apply,
    }

    /// <summary>
    /// A call to one of the functions that only exist once a function can be a value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are what function items are <em>for</em>. <c>fn:filter</c> and <c>fn:sort</c> do in one call what
    /// XSLT 1.0 needs a named template and a recursion for, and the folds do what it cannot express at all.
    /// </para>
    /// <para>
    /// Kept apart from the rest of the 2.0 library because they are gated differently: a stylesheet at
    /// <c>version="2.0"</c> that writes <c>filter(...)</c> means an extension function of its own, and should
    /// be told so rather than quietly given this one.
    /// </para>
    /// </remarks>
    internal sealed class HigherOrderFunctionExpr : Expr
    {
        private readonly HigherOrderFunction m_function;
        private readonly Expr[] m_arguments;
        private readonly string m_name;

        private HigherOrderFunctionExpr(HigherOrderFunction function, Expr[] arguments, string name)
        {
            m_function = function;
            m_arguments = arguments;
            m_name = name;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <summary>
        /// The functions here, with how many arguments each will take.
        /// </summary>
        /// <param name="name">The function's local name.</param>
        private static (HigherOrderFunction Function, int Least, int Most)? Find(string name)
        {
            return name switch
            {
                "function-arity" => (HigherOrderFunction.FunctionArity, 1, 1),
                "function-name" => (HigherOrderFunction.FunctionName, 1, 1),
                "for-each" => (HigherOrderFunction.ForEach, 2, 2),
                "filter" => (HigherOrderFunction.Filter, 2, 2),
                "fold-left" => (HigherOrderFunction.FoldLeft, 3, 3),
                "fold-right" => (HigherOrderFunction.FoldRight, 3, 3),
                "for-each-pair" => (HigherOrderFunction.ForEachPair, 3, 3),
                "sort" => (HigherOrderFunction.Sort, 1, 3),
                "apply" => (HigherOrderFunction.Apply, 2, 2),
                _ => null,
            };
        }

        /// <summary>
        /// Returns whether one of these functions will take a given number of arguments.
        /// </summary>
        /// <remarks>
        /// Read by <c>function-available()</c>, from the same table <see cref="TryCreate"/> uses. An arity
        /// of -1 asks only whether the name is one of these at all.
        /// </remarks>
        /// <param name="name">The function's local name.</param>
        /// <param name="arity">How many arguments the caller is asking about, or -1 for any.</param>
        public static bool TakesArity(string name, int arity)
        {
            return Find(name) is (_, int least, int most)
                && (arity < 0 || (arity >= least && arity <= most));
        }

        /// <summary>
        /// Creates a call, or returns <see langword="null"/> where the name is not one of these.
        /// </summary>
        /// <param name="name">The function's local name.</param>
        /// <param name="arguments">The compiled arguments.</param>
        /// <exception cref="XsltException"><c>XPST0017</c> where the argument count is wrong.</exception>
        public static Expr? TryCreate(string name, Expr[] arguments)
        {
            if (Find(name) is not (HigherOrderFunction function, int least, int most))
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

            return new HigherOrderFunctionExpr(function, arguments, name);
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            switch (m_function)
            {
                case HigherOrderFunction.FunctionArity:
                    return XPathValue.FromInteger(Function(0, ref context).Arity);

                case HigherOrderFunction.FunctionName:
                    return Function(0, ref context).FunctionName is XdmQName named
                        ? XPathValue.FromQName(named)
                        : XPathValue.FromSequence(XdmSequence.Empty);


                case HigherOrderFunction.ForEach:
                {
                    List<XPathValue> items = Items(0, ref context);
                    XdmFunction action = Function(1, ref context);
                    List<XPathValue> results = new List<XPathValue>(items.Count);

                    foreach (XPathValue item in items)
                    {
                        results.AddRange(XdmSequence.Items(Call(action, ref context, item)));
                    }

                    return XdmSequence.Concatenate(results);
                }

                case HigherOrderFunction.Filter:
                {
                    List<XPathValue> items = Items(0, ref context);
                    XdmFunction test = Function(1, ref context);
                    List<XPathValue> kept = new List<XPathValue>(items.Count);

                    foreach (XPathValue item in items)
                    {
                        if (Holds(test, ref context, item))
                        {
                            kept.Add(item);
                        }
                    }

                    return XdmSequence.Concatenate(kept);
                }

                case HigherOrderFunction.FoldLeft:
                {
                    List<XPathValue> items = Items(0, ref context);
                    XPathValue total = m_arguments[1].Evaluate(ref context);
                    XdmFunction combine = Function(2, ref context);

                    // The accumulator comes first, which is the whole difference from fold-right and the
                    // reason both exist: '(((zero, a), b), c)' rather than '(a, (b, (c, zero)))'.
                    foreach (XPathValue item in items)
                    {
                        total = Call(combine, ref context, total, item);
                    }

                    return total;
                }

                case HigherOrderFunction.FoldRight:
                {
                    List<XPathValue> items = Items(0, ref context);
                    XPathValue total = m_arguments[1].Evaluate(ref context);
                    XdmFunction combine = Function(2, ref context);

                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        total = Call(combine, ref context, items[i], total);
                    }

                    return total;
                }

                case HigherOrderFunction.ForEachPair:
                {
                    List<XPathValue> first = Items(0, ref context);
                    List<XPathValue> second = Items(1, ref context);
                    XdmFunction action = Function(2, ref context);

                    // It stops at the shorter of the two rather than erroring or padding, so zipping a
                    // sequence with its own tail is how a stylesheet compares neighbours.
                    int pairs = Math.Min(first.Count, second.Count);
                    List<XPathValue> results = new List<XPathValue>(pairs);

                    for (int i = 0; i < pairs; i++)
                    {
                        results.AddRange(XdmSequence.Items(Call(action, ref context, first[i], second[i])));
                    }

                    return XdmSequence.Concatenate(results);
                }

                case HigherOrderFunction.Apply:
                {
                    XdmFunction target = Function(0, ref context);
                    XPathValue second = m_arguments[1].Evaluate(ref context);

                    if (XdmSequence.RequireSingleItem(second, "the arguments of fn:apply()")
                        is { Kind: XPathValueKind.Array } packed)
                    {
                        IReadOnlyList<XPathValue> members = packed.AsArray().Members;
                        XPathValue[] arguments = new XPathValue[members.Count];

                        for (int i = 0; i < arguments.Length; i++)
                        {
                            arguments[i] = members[i];
                        }

                        if (arguments.Length != target.Arity)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.FOAP0001,
                                $"fn:apply() was given {arguments.Length} argument"
                                + (arguments.Length == 1 ? string.Empty : "s")
                                + $" for {target.Describe()}, which takes {target.Arity}.");
                        }

                        return target.Invoke(arguments, ref context);
                    }

                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        "fn:apply() takes the arguments as an array, and was given something else.");
                }

                default:
                {
                    List<XPathValue> items = Items(0, ref context);

                    Collation collation = m_arguments.Length >= 2
                        ? Collation.Resolve(m_arguments[1].Evaluate(ref context).ToStringValue(), ref context)
                        : Collation.Codepoint;

                    XdmFunction? key = m_arguments.Length >= 3 ? Function(2, ref context) : null;
                    return XdmSequence.Concatenate(Sort(items, key, collation, ref context));
                }
            }
        }

        /// <summary>
        /// Sorts items by a key, keeping the order of items whose keys are equal.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Stability is required rather than incidental: sorting twice is how a stylesheet sorts by two
        /// things, and it only works if the second sort leaves the first one's order alone where its own key
        /// does not decide. <see cref="Array.Sort{T}(T[], Comparison{T})"/> is not stable, so the original
        /// position goes into the comparison as the last tie-break.
        /// </para>
        /// <para>
        /// Keys are computed once per item, before any comparing starts. A key function may be arbitrarily
        /// expensive, and a sort would otherwise call it O(n log n) times instead of n.
        /// </para>
        /// </remarks>
        /// <param name="items">The items to sort.</param>
        /// <param name="key">The key function, or <see langword="null"/> to sort by the items themselves.</param>
        /// <param name="context">The context calls are made in.</param>
        internal static List<XPathValue> Sort(
            List<XPathValue> items,
            XdmFunction? key,
            Collation collation,
            ref DynamicContext context)
        {
            Collation ordering = collation;

            (List<XPathValue> Key, int Position, XPathValue Item)[] decorated =
                new (List<XPathValue>, int, XPathValue)[items.Count];

            for (int i = 0; i < items.Count; i++)
            {
                XPathValue keyed = key is null
                    ? items[i]
                    : key.Call(new[] { items[i] }, ref context);

                decorated[i] = (XdmSequence.Atomize(XdmSequence.Items(keyed)), i, items[i]);
            }

            try
            {
                Array.Sort(decorated, (left, right) =>
                {
                    int order = CompareKeys(left.Key, right.Key, ordering);
                    return order != 0 ? order : left.Position.CompareTo(right.Position);
                });
            }
            catch (InvalidOperationException wrapped) when (wrapped.InnerException is XsltException error)
            {
                // Array.Sort catches whatever a comparison threw and reports that a sort failed. What the
                // caller asked about is the values, so the type error the comparison raised is what it hears
                // — sorting an integer against a string is XPTY0004, not an internal fault.
                throw error;
            }

            List<XPathValue> sorted = new List<XPathValue>(items.Count);
            foreach ((List<XPathValue> _, int _, XPathValue item) in decorated)
            {
                sorted.Add(item);
            }

            return sorted;
        }

        /// <summary>
        /// Orders two key sequences, item by item, with a shorter sequence coming first where they agree so
        /// far.
        /// </summary>
        private static int CompareKeys(List<XPathValue> left, List<XPathValue> right, Collation collation)
        {
            int shared = Math.Min(left.Count, right.Count);

            for (int i = 0; i < shared; i++)
            {
                // Two strings are ordered by the collation, which is the whole reason these functions take
                // one — judged by the type rather than the kind, since a date and a duration are also held
                // as strings. Anything else goes through the value comparison, in two calls rather than one
                // returning a sign, because that is the shape it has: NaN answers false both ways and so is
                // equal to everything, which keeps a sort from depending on where an unorderable value
                // happened to start.
                if (Xpath2FunctionExpr.IsText(left[i]) && Xpath2FunctionExpr.IsText(right[i]))
                {
                    int sign = collation.Compare(left[i].ToStringValue(), right[i].ToStringValue());

                    if (sign != 0)
                    {
                        return sign;
                    }

                    continue;
                }

                if (XdmComparison.Value(left[i], right[i], BinaryOperator.LessThan))
                {
                    return -1;
                }

                if (XdmComparison.Value(right[i], left[i], BinaryOperator.LessThan))
                {
                    return 1;
                }
            }

            return left.Count.CompareTo(right.Count);
        }

        private List<XPathValue> Items(int index, ref DynamicContext context)
        {
            return XdmSequence.Items(m_arguments[index].Evaluate(ref context));
        }

        private XdmFunction Function(int index, ref DynamicContext context)
        {
            return XdmSequence
                .RequireSingleItem(m_arguments[index].Evaluate(ref context), $"the function fn:{m_name}() uses")
                .AsFunction();
        }

        private static XPathValue Call(XdmFunction function, ref DynamicContext context, XPathValue argument)
        {
            return function.Call(new[] { argument }, ref context);
        }

        private static XPathValue Call(
            XdmFunction function, ref DynamicContext context, XPathValue first, XPathValue second)
        {
            return function.Call(new[] { first, second }, ref context);
        }

        private static bool Holds(XdmFunction test, ref DynamicContext context, XPathValue item)
        {
            return Call(test, ref context, item).ToBoolean();
        }
    }
}
