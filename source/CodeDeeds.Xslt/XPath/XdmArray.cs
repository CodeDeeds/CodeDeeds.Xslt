namespace CodeDeeds.Xslt.XPath
{
    /// <summary>
    /// An array, which XPath 3.1 adds: an ordered list of members, where a member is any sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An array is an <em>item</em>, and that is the whole point of it. A sequence cannot nest — <c>(1, (2,
    /// 3))</c> is three items, not two — so before arrays there was no way to hold a list of lists. An array
    /// is one item however many members it has, so <c>[(1, 2), 3]</c> keeps its shape: two members, the first
    /// of two items.
    /// </para>
    /// <para>
    /// A member is a sequence rather than an item, which is why <c>array:get</c> can hand back several items
    /// or none at all.
    /// </para>
    /// <para>
    /// Immutable, for the reason <see cref="XdmMap"/> is.
    /// </para>
    /// </remarks>
    public sealed class XdmArray
    {
        private readonly XPathValue[] m_members;

        /// <summary>Initializes an array over its members.</summary>
        /// <param name="members">The members, each of which is a sequence.</param>
        public XdmArray(XPathValue[] members)
        {
            m_members = members;
        }

        /// <summary>The array with no members.</summary>
        public static XdmArray Empty { get; } = new XdmArray(Array.Empty<XPathValue>());

        /// <summary>Gets how many members the array has.</summary>
        public int Count => m_members.Length;

        /// <summary>Gets the members in order.</summary>
        public IReadOnlyList<XPathValue> Members => m_members;

        /// <summary>
        /// Returns a member by its position, counting from one as everything else in XPath does.
        /// </summary>
        /// <param name="position">The position, from 1 to <see cref="Count"/>.</param>
        /// <exception cref="XsltException">The position is outside the array.</exception>
        public XPathValue Get(long position)
        {
            if (position < 1 || position > m_members.Length)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOAY0001,
                    $"An array of {m_members.Length} "
                    + (m_members.Length == 1 ? "member has" : "members has")
                    + $" no member {position}.");
            }

            return m_members[position - 1];
        }

        /// <summary>Returns this array with one member replaced.</summary>
        /// <param name="position">Which member, from one.</param>
        /// <param name="value">What to put there.</param>
        public XdmArray Put(long position, XPathValue value)
        {
            if (position < 1 || position > m_members.Length)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOAY0001, $"An array of {m_members.Length} has no member {position}.");
            }

            XPathValue[] copy = (XPathValue[])m_members.Clone();
            copy[position - 1] = value;
            return new XdmArray(copy);
        }

        /// <summary>Returns this array with one more member at the end.</summary>
        /// <param name="value">The member to add.</param>
        public XdmArray Append(XPathValue value)
        {
            XPathValue[] copy = new XPathValue[m_members.Length + 1];
            m_members.CopyTo(copy, 0);
            copy[^1] = value;
            return new XdmArray(copy);
        }

        /// <summary>
        /// Returns every item of every member, in order, which is what an array flattens to.
        /// </summary>
        /// <remarks>
        /// Nested arrays flatten too, since the specification defines the operation to recurse: what comes
        /// back holds no arrays at all.
        /// </remarks>
        public XPathValue Flatten()
        {
            List<XPathValue> items = new List<XPathValue>();
            FlattenInto(this, items);
            return XdmSequence.Concatenate(items);
        }

        private static void FlattenInto(XdmArray array, List<XPathValue> output)
        {
            foreach (XPathValue member in array.m_members)
            {
                foreach (XPathValue item in XdmSequence.Items(member))
                {
                    if (item.Kind == XPathValueKind.Array)
                    {
                        FlattenInto(item.AsArray(), output);
                    }
                    else
                    {
                        output.Add(item);
                    }
                }
            }
        }
    }
}
