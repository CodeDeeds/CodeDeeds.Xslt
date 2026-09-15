using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>The functions of the <c>map:</c> and <c>array:</c> namespaces that this engine has.</summary>
    internal enum MapArrayFunction : byte
    {
        /// <summary><c>map:size</c>.</summary>
        MapSize,

        /// <summary><c>map:keys</c>.</summary>
        MapKeys,

        /// <summary><c>map:contains</c>.</summary>
        MapContains,

        /// <summary><c>map:get</c>.</summary>
        MapGet,

        /// <summary><c>map:put</c>.</summary>
        MapPut,

        /// <summary><c>map:entry</c>.</summary>
        MapEntry,

        /// <summary><c>map:remove</c>.</summary>
        MapRemove,

        /// <summary><c>map:merge</c>.</summary>
        MapMerge,

        /// <summary><c>map:for-each</c>.</summary>
        MapForEach,

        /// <summary><c>map:find</c>.</summary>
        MapFind,

        /// <summary><c>array:size</c>.</summary>
        ArraySize,

        /// <summary><c>array:get</c>.</summary>
        ArrayGet,

        /// <summary><c>array:put</c>.</summary>
        ArrayPut,

        /// <summary><c>array:append</c>.</summary>
        ArrayAppend,

        /// <summary><c>array:head</c>.</summary>
        ArrayHead,

        /// <summary><c>array:tail</c>.</summary>
        ArrayTail,

        /// <summary><c>array:reverse</c>.</summary>
        ArrayReverse,

        /// <summary><c>array:join</c>.</summary>
        ArrayJoin,

        /// <summary><c>array:subarray</c>.</summary>
        ArraySubarray,

        /// <summary><c>array:remove</c>.</summary>
        ArrayRemove,

        /// <summary><c>array:insert-before</c>.</summary>
        ArrayInsertBefore,

        /// <summary><c>array:flatten</c>.</summary>
        ArrayFlatten,

        /// <summary><c>array:for-each</c>.</summary>
        ArrayForEach,

        /// <summary><c>array:filter</c>.</summary>
        ArrayFilter,

        /// <summary><c>array:fold-left</c>.</summary>
        ArrayFoldLeft,

        /// <summary><c>array:fold-right</c>.</summary>
        ArrayFoldRight,

        /// <summary><c>array:for-each-pair</c>.</summary>
        ArrayForEachPair,

        /// <summary><c>array:sort</c>.</summary>
        ArraySort,
    }

    /// <summary>
    /// A call to one of the map or array functions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two namespaces are <c>http://www.w3.org/2005/xpath-functions/map</c> and the same ending in
    /// <c>/array</c>. XSLT 3.0 binds <c>map</c> and <c>array</c> to them without being asked; this engine does
    /// not claim 3.0, so a stylesheet declares the prefixes itself.
    /// </para>
    /// <para>
    /// Both libraries are complete: the ten functions XPath 3.1 puts in <c>map:</c> and the eighteen it puts
    /// in <c>array:</c>. The ones taking a function among their arguments waited on function items, and
    /// arrived with them.
    /// </para>
    /// </remarks>
    internal sealed class MapArrayFunctionExpr : Expr
    {
        private readonly MapArrayFunction m_function;
        private readonly Expr[] m_arguments;

        private MapArrayFunctionExpr(MapArrayFunction function, Expr[] arguments)
        {
            m_function = function;
            m_arguments = arguments;
        }

        /// <summary>The namespace the map functions are in.</summary>
        public const string MapNamespace = "http://www.w3.org/2005/xpath-functions/map";

        /// <summary>The namespace the array functions are in.</summary>
        public const string ArrayNamespace = "http://www.w3.org/2005/xpath-functions/array";

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_arguments;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <summary>
        /// Builds a call, or refuses the name.
        /// </summary>
        /// <param name="uri">Which of the two namespaces the name is in.</param>
        /// <param name="name">The local name.</param>
        /// <param name="arguments">The compiled arguments.</param>
        /// <exception cref="XsltException">There is no such function, or it takes a different number.</exception>
        public static Expr Create(string uri, string name, Expr[] arguments)
        {
            bool isMap = uri == MapNamespace;
            string prefix = isMap ? "map" : "array";

            (MapArrayFunction Function, int Least, int Most)? found = isMap
                ? MapFunction(name)
                : ArrayFunction(name);

            if (found is not (MapArrayFunction function, int least, int most))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017, $"There is no function '{prefix}:{name}()'.");
            }

            if (arguments.Length < least || arguments.Length > most)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0017,
                    $"'{prefix}:{name}()' takes "
                    + (least == most ? $"{least}" : $"{least} to {most}")
                    + $" arguments, and was given {arguments.Length}.");
            }

            return new MapArrayFunctionExpr(function, arguments);
        }

        /// <summary>
        /// Returns whether a <c>map:</c> or <c>array:</c> function will take a given number of arguments.
        /// </summary>
        /// <remarks>
        /// Read by <c>function-available()</c>, from the same tables <see cref="Create"/> checks against.
        /// An arity of -1 asks only whether the name is one of these functions at all, which is what the
        /// one-argument form of that function wants.
        /// </remarks>
        /// <param name="uri">The namespace, which says which of the two libraries to look in.</param>
        /// <param name="name">The function's local name.</param>
        /// <param name="arity">How many arguments the caller is asking about, or -1 for any.</param>
        public static bool TakesArity(string uri, string name, int arity)
        {
            (MapArrayFunction, int, int)? found = uri switch
            {
                MapNamespace => MapFunction(name),
                ArrayNamespace => ArrayFunction(name),
                _ => null,
            };

            return found is (_, int least, int most) && (arity < 0 || (arity >= least && arity <= most));
        }

        private static (MapArrayFunction, int, int)? MapFunction(string name)
        {
            return name switch
            {
                "size" => (MapArrayFunction.MapSize, 1, 1),
                "keys" => (MapArrayFunction.MapKeys, 1, 1),
                "contains" => (MapArrayFunction.MapContains, 2, 2),
                "get" => (MapArrayFunction.MapGet, 2, 2),
                "put" => (MapArrayFunction.MapPut, 3, 3),
                "entry" => (MapArrayFunction.MapEntry, 2, 2),
                "remove" => (MapArrayFunction.MapRemove, 2, 2),
                "merge" => (MapArrayFunction.MapMerge, 1, 2),
                "for-each" => (MapArrayFunction.MapForEach, 2, 2),
                "find" => (MapArrayFunction.MapFind, 2, 2),
                _ => null,
            };
        }

        private static (MapArrayFunction, int, int)? ArrayFunction(string name)
        {
            return name switch
            {
                "size" => (MapArrayFunction.ArraySize, 1, 1),
                "get" => (MapArrayFunction.ArrayGet, 2, 2),
                "put" => (MapArrayFunction.ArrayPut, 3, 3),
                "append" => (MapArrayFunction.ArrayAppend, 2, 2),
                "head" => (MapArrayFunction.ArrayHead, 1, 1),
                "tail" => (MapArrayFunction.ArrayTail, 1, 1),
                "reverse" => (MapArrayFunction.ArrayReverse, 1, 1),
                "join" => (MapArrayFunction.ArrayJoin, 1, 1),
                "subarray" => (MapArrayFunction.ArraySubarray, 2, 3),
                "remove" => (MapArrayFunction.ArrayRemove, 2, 2),
                "insert-before" => (MapArrayFunction.ArrayInsertBefore, 3, 3),
                "flatten" => (MapArrayFunction.ArrayFlatten, 1, 1),
                "for-each" => (MapArrayFunction.ArrayForEach, 2, 2),
                "filter" => (MapArrayFunction.ArrayFilter, 2, 2),
                "fold-left" => (MapArrayFunction.ArrayFoldLeft, 3, 3),
                "fold-right" => (MapArrayFunction.ArrayFoldRight, 3, 3),
                "for-each-pair" => (MapArrayFunction.ArrayForEachPair, 3, 3),
                "sort" => (MapArrayFunction.ArraySort, 1, 3),
                _ => null,
            };
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            switch (m_function)
            {
                case MapArrayFunction.MapSize:
                    return XPathValue.FromInteger(Map(0, ref context).Count);

                case MapArrayFunction.MapKeys:
                    return XdmSequence.Concatenate(new List<XPathValue>(Map(0, ref context).Keys));

                case MapArrayFunction.MapContains:
                    return XPathValue.FromBoolean(
                        Map(0, ref context).Contains(Key(1, ref context)));

                case MapArrayFunction.MapGet:
                    return Map(0, ref context).Get(Key(1, ref context));

                case MapArrayFunction.MapPut:
                    return XPathValue.FromMap(Map(0, ref context)
                        .Put(Key(1, ref context), m_arguments[2].Evaluate(ref context)));

                case MapArrayFunction.MapEntry:
                    return XPathValue.FromMap(
                        XdmMap.Entry(Key(0, ref context), m_arguments[1].Evaluate(ref context)));

                case MapArrayFunction.MapRemove:
                    return XPathValue.FromMap(Map(0, ref context)
                        .Remove(XdmSequence.Items(m_arguments[1].Evaluate(ref context))));

                case MapArrayFunction.MapMerge:
                    return Merge(ref context);

                case MapArrayFunction.MapForEach:
                {
                    XdmMap map = Map(0, ref context);
                    XdmFunction action = Function(1, ref context);
                    List<XPathValue> results = new List<XPathValue>(map.Count);

                    // The entries arrive in no particular order, a map being unordered, so a caller that
                    // wants an order sorts what comes back rather than expecting one here.
                    foreach (KeyValuePair<XPathValue, XPathValue> entry in map.Entries)
                    {
                        results.AddRange(XdmSequence.Items(
                            action.Call(new[] { entry.Key, entry.Value }, ref context)));
                    }

                    return XdmSequence.Concatenate(results);
                }

                case MapArrayFunction.MapFind:
                {
                    List<XPathValue> found = new List<XPathValue>();
                    XPathValue key = Key(1, ref context);

                    foreach (XPathValue item in XdmSequence.Items(m_arguments[0].Evaluate(ref context)))
                    {
                        Find(item, key, found);
                    }

                    return XPathValue.FromArray(
                        found.Count == 0 ? XdmArray.Empty : new XdmArray(found.ToArray()));
                }

                case MapArrayFunction.ArraySize:
                    return XPathValue.FromInteger(Array(0, ref context).Count);

                case MapArrayFunction.ArrayGet:
                    return Array(0, ref context).Get(Position(1, ref context));

                case MapArrayFunction.ArrayPut:
                    return XPathValue.FromArray(Array(0, ref context)
                        .Put(Position(1, ref context), m_arguments[2].Evaluate(ref context)));

                case MapArrayFunction.ArrayAppend:
                    return XPathValue.FromArray(
                        Array(0, ref context).Append(m_arguments[1].Evaluate(ref context)));

                case MapArrayFunction.ArrayHead:
                    return Array(0, ref context).Get(1);

                case MapArrayFunction.ArrayTail:
                    return Subarray(Array(0, ref context), 2, long.MaxValue);

                case MapArrayFunction.ArrayReverse:
                {
                    XdmArray array = Array(0, ref context);
                    XPathValue[] members = new XPathValue[array.Count];

                    for (int i = 0; i < members.Length; i++)
                    {
                        members[i] = array.Members[members.Length - 1 - i];
                    }

                    return XPathValue.FromArray(new XdmArray(members));
                }

                case MapArrayFunction.ArrayJoin:
                {
                    List<XPathValue> members = new List<XPathValue>();

                    foreach (XPathValue item in XdmSequence.Items(m_arguments[0].Evaluate(ref context)))
                    {
                        members.AddRange(AsArray(item).Members);
                    }

                    return XPathValue.FromArray(new XdmArray(members.ToArray()));
                }

                case MapArrayFunction.ArraySubarray:
                {
                    XdmArray array = Array(0, ref context);
                    long start = Position(1, ref context);

                    if (m_arguments.Length == 2)
                    {
                        return Subarray(array, start, long.MaxValue);
                    }

                    long length = m_arguments[2].Evaluate(ref context).ToInteger();
                    if (length < 0)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.FOAY0002, $"A subarray cannot have a length of {length}.");
                    }

                    return Subarray(array, start, length);
                }

                case MapArrayFunction.ArrayRemove:
                {
                    XdmArray array = Array(0, ref context);
                    HashSet<long> dropped = new HashSet<long>();

                    foreach (XPathValue item in XdmSequence.Items(m_arguments[1].Evaluate(ref context)))
                    {
                        long at = item.ToInteger();
                        if (at < 1 || at > array.Count)
                        {
                            throw XsltErrors.Error(
                                XsltErrorCode.FOAY0001,
                                $"An array of {array.Count} has no member {at} to remove.");
                        }

                        dropped.Add(at);
                    }

                    List<XPathValue> kept = new List<XPathValue>(array.Count - dropped.Count);
                    for (int i = 0; i < array.Count; i++)
                    {
                        if (!dropped.Contains(i + 1))
                        {
                            kept.Add(array.Members[i]);
                        }
                    }

                    return XPathValue.FromArray(new XdmArray(kept.ToArray()));
                }

                case MapArrayFunction.ArrayInsertBefore:
                {
                    XdmArray array = Array(0, ref context);
                    long at = m_arguments[1].Evaluate(ref context).ToInteger();

                    // One past the end is allowed here, which is what makes it possible to insert at the end.
                    if (at < 1 || at > array.Count + 1)
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.FOAY0001,
                            $"An array of {array.Count} has no position {at} to insert before.");
                    }

                    List<XPathValue> members = new List<XPathValue>(array.Members);
                    members.Insert((int)(at - 1), m_arguments[2].Evaluate(ref context));
                    return XPathValue.FromArray(new XdmArray(members.ToArray()));
                }

                case MapArrayFunction.ArrayForEach:
                {
                    XdmArray array = Array(0, ref context);
                    XdmFunction action = Function(1, ref context);
                    XPathValue[] members = new XPathValue[array.Count];

                    for (int i = 0; i < members.Length; i++)
                    {
                        members[i] = action.Call(new[] { array.Members[i] }, ref context);
                    }

                    return XPathValue.FromArray(
                        members.Length == 0 ? XdmArray.Empty : new XdmArray(members));
                }

                case MapArrayFunction.ArrayFilter:
                {
                    XdmArray array = Array(0, ref context);
                    XdmFunction test = Function(1, ref context);
                    List<XPathValue> kept = new List<XPathValue>(array.Count);

                    foreach (XPathValue member in array.Members)
                    {
                        if (test.Call(new[] { member }, ref context).ToBoolean())
                        {
                            kept.Add(member);
                        }
                    }

                    return XPathValue.FromArray(
                        kept.Count == 0 ? XdmArray.Empty : new XdmArray(kept.ToArray()));
                }

                case MapArrayFunction.ArrayFoldLeft:
                case MapArrayFunction.ArrayFoldRight:
                {
                    XdmArray array = Array(0, ref context);
                    XPathValue total = m_arguments[1].Evaluate(ref context);
                    XdmFunction combine = Function(2, ref context);
                    bool leftwards = m_function == MapArrayFunction.ArrayFoldLeft;

                    for (int i = 0; i < array.Count; i++)
                    {
                        XPathValue member = array.Members[leftwards ? i : array.Count - 1 - i];

                        total = leftwards
                            ? combine.Call(new[] { total, member }, ref context)
                            : combine.Call(new[] { member, total }, ref context);
                    }

                    return total;
                }

                case MapArrayFunction.ArrayForEachPair:
                {
                    XdmArray first = Array(0, ref context);
                    XdmArray second = Array(1, ref context);
                    XdmFunction action = Function(2, ref context);

                    // As long as the shorter of the two, which is what makes zipping arrays of unequal
                    // length a truncation rather than an error.
                    XPathValue[] members = new XPathValue[Math.Min(first.Count, second.Count)];

                    for (int i = 0; i < members.Length; i++)
                    {
                        members[i] = action.Call(
                            new[] { first.Members[i], second.Members[i] }, ref context);
                    }

                    return XPathValue.FromArray(
                        members.Length == 0 ? XdmArray.Empty : new XdmArray(members));
                }

                case MapArrayFunction.ArraySort:
                {
                    XdmArray array = Array(0, ref context);

                    Collation collation = m_arguments.Length >= 2
                        ? Collation.Resolve(m_arguments[1].Evaluate(ref context).ToStringValue(), ref context)
                        : Collation.Codepoint;

                    XdmFunction? key = m_arguments.Length >= 3 ? Function(2, ref context) : null;

                    List<XPathValue> sorted = HigherOrderFunctionExpr.Sort(
                        new List<XPathValue>(array.Members), key, collation, ref context);

                    return XPathValue.FromArray(
                        sorted.Count == 0 ? XdmArray.Empty : new XdmArray(sorted.ToArray()));
                }

                default:
                    return Flatten(m_arguments[0].Evaluate(ref context));
            }
        }

        /// <summary>
        /// Collects every value bound to a key, looking inside maps and arrays however deeply they nest.
        /// </summary>
        /// <remarks>
        /// The recursion is the point of <c>map:find</c>. Where <c>map:get</c> asks one map one question,
        /// this searches a whole structure — which is what makes it usable on parsed JSON, where the map
        /// holding what you want is several levels down and you do not want to write the path to it.
        /// </remarks>
        private static void Find(XPathValue item, XPathValue key, List<XPathValue> found)
        {
            switch (item.Kind)
            {
                case XPathValueKind.Map:
                {
                    XdmMap map = item.AsMap();

                    if (map.Contains(key))
                    {
                        found.Add(map.Get(key));
                    }

                    foreach (KeyValuePair<XPathValue, XPathValue> entry in map.Entries)
                    {
                        foreach (XPathValue inner in XdmSequence.Items(entry.Value))
                        {
                            Find(inner, key, found);
                        }
                    }

                    return;
                }

                case XPathValueKind.Array:
                {
                    foreach (XPathValue member in item.AsArray().Members)
                    {
                        foreach (XPathValue inner in XdmSequence.Items(member))
                        {
                            Find(inner, key, found);
                        }
                    }

                    return;
                }

                default:
                    // Anything else contributes nothing rather than being an error: map:find() searches what
                    // it is given, and a sequence of mixed items is a thing a stylesheet may well hold.
                    return;
            }
        }

        private XdmFunction Function(int index, ref DynamicContext context)
        {
            return XdmSequence
                .RequireSingleItem(m_arguments[index].Evaluate(ref context), "the function to apply")
                .AsFunction();
        }

        /// <summary>Every item, with any array replaced by its members, however deeply they nest.</summary>
        private static XPathValue Flatten(XPathValue value)
        {
            List<XPathValue> items = new List<XPathValue>();

            foreach (XPathValue item in XdmSequence.Items(value))
            {
                if (item.Kind == XPathValueKind.Array)
                {
                    items.AddRange(XdmSequence.Items(item.AsArray().Flatten()));
                }
                else
                {
                    items.Add(item);
                }
            }

            return XdmSequence.Concatenate(items);
        }

        private static XPathValue Subarray(XdmArray array, long start, long length)
        {
            if (start < 1 || start > array.Count + 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOAY0001, $"An array of {array.Count} does not start a subarray at {start}.");
            }

            long available = array.Count - start + 1;
            if (length > available)
            {
                if (length != long.MaxValue)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOAY0001,
                        $"An array of {array.Count} has no {length} members from position {start}.");
                }

                length = available;
            }

            XPathValue[] members = new XPathValue[length];
            for (long i = 0; i < length; i++)
            {
                members[i] = array.Members[(int)(start - 1 + i)];
            }

            return XPathValue.FromArray(new XdmArray(members));
        }

        private XPathValue Merge(ref DynamicContext context)
        {
            string duplicates = "use-first";

            if (m_arguments.Length == 2)
            {
                XPathValue options = m_arguments[1].Evaluate(ref context);
                XPathValue chosen = AsMap(options).Get(XPathValue.FromString("duplicates"));

                if (chosen.Kind != XPathValueKind.Sequence || XdmSequence.Items(chosen).Count != 0)
                {
                    duplicates = chosen.ToStringValue();
                    if (duplicates is not ("use-first" or "use-last" or "use-any" or "combine" or "reject"))
                    {
                        throw XsltErrors.Error(
                            XsltErrorCode.FOJS0005,
                            $"'{duplicates}' is not one of the ways map:merge() handles a repeated key.");
                    }
                }
            }

            List<KeyValuePair<XPathValue, XPathValue>> entries =
                new List<KeyValuePair<XPathValue, XPathValue>>();

            foreach (XPathValue item in XdmSequence.Items(m_arguments[0].Evaluate(ref context)))
            {
                entries.AddRange(AsMap(item).Entries);
            }

            return XPathValue.FromMap(XdmMap.Build(entries, duplicates));
        }

        private XdmMap Map(int index, ref DynamicContext context)
        {
            return AsMap(m_arguments[index].Evaluate(ref context));
        }

        private XdmArray Array(int index, ref DynamicContext context)
        {
            return AsArray(m_arguments[index].Evaluate(ref context));
        }

        /// <summary>A position argument, which is declared <c>xs:integer</c> and is held to it.</summary>
        /// <remarks>
        /// Exactly one item, and an integer. The function conversion rules promote an integer outwards
        /// to a decimal and a double and never inwards, so <c>array:get([1,2,3], 1.2)</c> is a type
        /// error rather than a question about the first member — which is what it became when the
        /// argument was simply read as a number and truncated. Untyped text converts, being untyped.
        /// </remarks>
        /// <param name="index">Which argument holds the position.</param>
        /// <param name="context">The context to evaluate it in.</param>
        private long Position(int index, ref DynamicContext context)
        {
            return AsPosition(m_arguments[index].Evaluate(ref context));
        }

        /// <summary>Reads a value as the one integer a position has to be.</summary>
        /// <param name="value">The value given.</param>
        internal static long AsPosition(XPathValue value)
        {
            List<XPathValue> items = XdmSequence.Items(value);

            if (items.Count != 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"A position is one integer, and this is {items.Count} items.");
            }

            XPathValue one = items[0];

            if (one.Kind is XPathValueKind.Node or XPathValueKind.NodeSet
                || one.TypeCode == XdmTypeCode.UntypedAtomic)
            {
                one = XdmType.TryGet("integer", out XdmType.BuiltInType integer)
                    ? XdmType.Cast(one, integer)
                    : one;
            }

            return one.TypeCode == XdmTypeCode.Integer
                ? one.ToInteger()
                : throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"A position is an xs:integer, and this one is {one.TypeCode}.");
        }

        private XPathValue Key(int index, ref DynamicContext context)
        {
            List<XPathValue> items = XdmSequence.Items(m_arguments[index].Evaluate(ref context));

            if (items.Count != 1)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    $"A map key is one atomic value, and this one is {items.Count} items.");
            }

            return items[0];
        }

        private static XdmMap AsMap(XPathValue value)
        {
            if (value.Kind == XPathValueKind.Map)
            {
                return value.AsMap();
            }

            throw XsltErrors.Error(XsltErrorCode.XPTY0004, "A map was expected here.");
        }

        private static XdmArray AsArray(XPathValue value)
        {
            if (value.Kind == XPathValueKind.Array)
            {
                return value.AsArray();
            }

            throw XsltErrors.Error(XsltErrorCode.XPTY0004, "An array was expected here.");
        }
    }
}
