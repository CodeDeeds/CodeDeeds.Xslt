using CodeDeeds.Xslt.Runtime;

namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// An array written out, as <c>[1, 2, 3]</c>.
    /// </summary>
    /// <remarks>
    /// Each expression between the commas is one member, so <c>[(1, 2), 3]</c> has two members and not three.
    /// That is the difference between an array and a sequence, and writing it is where the difference is
    /// easiest to see.
    /// </remarks>
    public sealed class SquareArrayExpr : Expr
    {
        private readonly Expr[] m_members;

        /// <summary>Initializes an array constructor.</summary>
        /// <param name="members">One expression per member.</param>
        public SquareArrayExpr(Expr[] members)
        {
            m_members = members;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children => m_members;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            XPathValue[] members = new XPathValue[m_members.Length];

            for (int i = 0; i < m_members.Length; i++)
            {
                members[i] = m_members[i].Evaluate(ref context);
            }

            return XPathValue.FromArray(
                members.Length == 0 ? XdmArray.Empty : new XdmArray(members));
        }
    }

    /// <summary>
    /// An array written with braces, as <c>array { 1, 2, 3 }</c>.
    /// </summary>
    /// <remarks>
    /// The braces hold one expression, and every <em>item</em> it evaluates to is a member — so
    /// <c>array { (1, 2), 3 }</c> has three members where <c>[(1, 2), 3]</c> has two. The square form takes
    /// one expression per member and the curly form takes one sequence in total, which is the whole
    /// difference between them and the reason both exist.
    /// </remarks>
    public sealed class CurlyArrayExpr : Expr
    {
        private readonly Expr? m_content;

        /// <summary>Initializes a curly array constructor.</summary>
        /// <param name="content">The expression whose items become the members, or nothing.</param>
        public CurlyArrayExpr(Expr? content)
        {
            m_content = content;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children =>
            m_content is null ? Array.Empty<Expr>() : new[] { m_content };

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            if (m_content is null)
            {
                return XPathValue.FromArray(XdmArray.Empty);
            }

            List<XPathValue> items = XdmSequence.Items(m_content.Evaluate(ref context));
            return XPathValue.FromArray(items.Count == 0 ? XdmArray.Empty : new XdmArray(items.ToArray()));
        }
    }

    /// <summary>
    /// A map written out, as <c>map { 'a': 1, 'b': 2 }</c>.
    /// </summary>
    /// <remarks>
    /// A key written twice is an error rather than one entry quietly winning: the two spellings <c>1</c> and
    /// <c>1.0</c> are the same key, so a duplicate is more likely to be a mistake than an intention.
    /// </remarks>
    public sealed class CurlyMapExpr : Expr
    {
        private readonly Expr[] m_keys;
        private readonly Expr[] m_values;

        /// <summary>Initializes a map constructor.</summary>
        /// <param name="keys">The key expressions.</param>
        /// <param name="values">The value expressions, one per key.</param>
        public CurlyMapExpr(Expr[] keys, Expr[] values)
        {
            m_keys = keys;
            m_values = values;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children
        {
            get
            {
                foreach (Expr key in m_keys)
                {
                    yield return key;
                }

                foreach (Expr value in m_values)
                {
                    yield return value;
                }
            }
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            List<KeyValuePair<XPathValue, XPathValue>> entries =
                new List<KeyValuePair<XPathValue, XPathValue>>(m_keys.Length);

            for (int i = 0; i < m_keys.Length; i++)
            {
                List<XPathValue> key = XdmSequence.Items(m_keys[i].Evaluate(ref context));

                if (key.Count != 1)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        $"A map key is one atomic value, and this one is {key.Count} items.");
                }

                entries.Add(new KeyValuePair<XPathValue, XPathValue>(key[0], m_values[i].Evaluate(ref context)));
            }

            // A map constructor written down has its own code for a key said twice, distinct from the one
            // map:merge(…, 'reject') raises and from the one a JSON document with a repeated field raises:
            // three ways of arriving at the same shape, and a stylesheet reacting to one of them wants to
            // know which it was.
            return XPathValue.FromMap(
                XdmMap.Build(entries, "reject", XsltErrorCode.XQDY0137));
        }
    }

    /// <summary>How a lookup names what it wants.</summary>
    public enum LookupKind : byte
    {
        /// <summary>A name or a number written out, as <c>?a</c> or <c>?1</c>.</summary>
        Literal = 0,

        /// <summary>Every value of a map, or every member of an array, written <c>?*</c>.</summary>
        Wildcard = 1,

        /// <summary>A key worked out at run time, written <c>?(expr)</c>.</summary>
        Computed = 2,
    }

    /// <summary>
    /// The lookup operator, as <c>$m?name</c>, <c>$a?1</c>, <c>$m?('a')</c> or <c>$m?*</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It applies to each item of its operand and joins what comes back, so <c>$maps?name</c> reads the same
    /// entry out of every map in a sequence. An item that is neither a map nor an array is a type error: the
    /// operator asks a question only those two can answer.
    /// </para>
    /// <para>
    /// A name after <c>?</c> is a string key and a number is an integer one, whatever the operand turns out
    /// to be — <c>?1</c> reads the member of an array and the entry under the integer 1 of a map.
    /// </para>
    /// </remarks>
    public sealed class LookupExpr : Expr
    {
        private readonly Expr m_operand;
        private readonly LookupKind m_kind;
        private readonly XPathValue m_literal;
        private readonly Expr? m_key;

        /// <summary>Initializes a lookup.</summary>
        /// <param name="operand">What is being looked in.</param>
        /// <param name="kind">How the key is named.</param>
        /// <param name="literal">The key, where it was written out.</param>
        /// <param name="key">The key expression, where it is computed.</param>
        public LookupExpr(Expr operand, LookupKind kind, XPathValue literal, Expr? key)
        {
            m_operand = operand;
            m_kind = kind;
            m_literal = literal;
            m_key = key;
        }

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children =>
            m_key is null ? new[] { m_operand } : new[] { m_operand, m_key };

        /// <inheritdoc/>
        public override bool MaySpanDocuments => true;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            List<XPathValue> keys = new List<XPathValue>();

            if (m_kind == LookupKind.Computed)
            {
                keys.AddRange(XdmSequence.Items(m_key!.Evaluate(ref context)));
            }
            else if (m_kind == LookupKind.Literal)
            {
                keys.Add(m_literal);
            }

            List<XPathValue> found = new List<XPathValue>();

            foreach (XPathValue item in XdmSequence.Items(m_operand.Evaluate(ref context)))
            {
                LookIn(item, keys, found);
            }

            return XdmSequence.Concatenate(found);
        }

        private void LookIn(XPathValue item, List<XPathValue> keys, List<XPathValue> found)
        {
            switch (item.Kind)
            {
                case XPathValueKind.Map:
                {
                    XdmMap map = item.AsMap();

                    if (m_kind == LookupKind.Wildcard)
                    {
                        foreach (KeyValuePair<XPathValue, XPathValue> entry in map.Entries)
                        {
                            found.AddRange(XdmSequence.Items(entry.Value));
                        }

                        return;
                    }

                    foreach (XPathValue key in keys)
                    {
                        found.AddRange(XdmSequence.Items(map.Get(key)));
                    }

                    return;
                }

                case XPathValueKind.Array:
                {
                    XdmArray array = item.AsArray();

                    if (m_kind == LookupKind.Wildcard)
                    {
                        foreach (XPathValue member in array.Members)
                        {
                            found.AddRange(XdmSequence.Items(member));
                        }

                        return;
                    }

                    foreach (XPathValue key in keys)
                    {
                        found.AddRange(XdmSequence.Items(array.Get(AsPosition(key))));
                    }

                    return;
                }

                default:
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        "Only a map or an array can be looked into with '?', and this is neither.");
            }
        }

        private static long AsPosition(XPathValue key)
        {
            // An integer, not a number that happens to be whole: an array is looked into by position, and
            // '?(1.0)' names an xs:decimal where a position is an xs:integer.
            if (key.TypeCode != XdmTypeCode.Integer)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPTY0004,
                    "An array is looked into by position, so the key is an xs:integer and this is "
                    + $"an {key.TypeCode}.");
            }

            return key.ToInteger();
        }
    }
}
