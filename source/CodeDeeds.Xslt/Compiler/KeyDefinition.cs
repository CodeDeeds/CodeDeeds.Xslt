using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// One <c>xsl:key</c> declaration: which nodes it files, and under what.
    /// </summary>
    /// <remarks>
    /// Several declarations may share a name, and then they are one key (§20.2.1): a node any of them
    /// matches is filed under the values that declaration reads from it, and a lookup finds what every one
    /// of them filed. So a definition holds a list of these rather than one match and one use.
    /// </remarks>
    internal sealed class KeyRule
    {
        /// <summary>Initializes a rule.</summary>
        /// <param name="tree">The module the <c>xsl:key</c> element is in.</param>
        /// <param name="element">The <c>xsl:key</c> element.</param>
        public KeyRule(XdmTree tree, int element)
        {
            Tree = tree;
            Element = element;
        }

        /// <summary>The module the declaration was read from.</summary>
        public XdmTree Tree { get; }

        /// <summary>The <c>xsl:key</c> element the rule was read from.</summary>
        public int Element { get; }

        /// <summary>The match patterns; a node matching any of them is indexed.</summary>
        public Pattern[] Patterns { get; set; } = Array.Empty<Pattern>();

        /// <summary>The <c>use</c> expression giving the node's key values, if written as an attribute.</summary>
        public Expr? Use { get; set; }

        /// <summary>
        /// The sequence constructor giving the key values, if written as content. XSLT 2.0 added the form;
        /// the values are what it produces, as a sequence rather than a tree.
        /// </summary>
        public Instruction[]? Body { get; set; }

        /// <summary>How many local variable slots the body needs.</summary>
        public int FrameSize { get; set; }

        /// <summary>
        /// Whether the declaration is read with backwards-compatible behaviour, under which every value it
        /// files a node under is converted to a string first (§20.2.1).
        /// </summary>
        public bool BackwardsCompatible { get; set; }
    }

    /// <summary>
    /// A key: the declarations of one name in one package, and what they agree about.
    /// </summary>
    internal sealed class KeyDefinition
    {
        /// <summary>Initializes a key definition.</summary>
        /// <param name="name">The key's name.</param>
        /// <param name="index">The key's position in the stylesheet's key list.</param>
        /// <param name="package">The package that declared it, keys being local to one (§3.5.3).</param>
        public KeyDefinition(ExpandedName name, int index, int package = 0)
        {
            Name = name;
            Index = index;
            Package = package;
        }

        /// <summary>The package the key belongs to.</summary>
        public int Package { get; }

        /// <summary>The key's name.</summary>
        public ExpandedName Name { get; }

        /// <summary>Where the key stands in the stylesheet's list, which is how the runtime addresses it.</summary>
        public int Index { get; }

        /// <summary>The declarations, in the order they were read.</summary>
        public List<KeyRule> Rules { get; } = new();

        /// <summary>
        /// Whether the key is composite: each node filed once under the whole sequence of its values, so
        /// that a lookup has to supply all of them (§20.2.1). Every declaration of a name has to agree.
        /// </summary>
        public bool Composite { get; set; }

        /// <summary>
        /// The identity a key value is filed and found under: what two values that are equal under
        /// <c>eq</c> share, and two that are not do not.
        /// </summary>
        /// <remarks>
        /// A key matches by value (§20.2.2), not by string: the integer 4 finds a node filed under
        /// <c>string-length()</c> and the string '4' does not, a date finds a date in any time zone, and
        /// an untyped value is a string. So the index holds a typed spelling — the same one
        /// <c>xsl:for-each-group</c> groups by — under which every numeric type spells a number the same
        /// way and an untyped atomic value spells as the string it is. A node contributes its string value
        /// as an untyped one. <c>NaN</c> is equal to nothing, itself included, and has no identity at all.
        /// </remarks>
        /// <param name="item">One key value.</param>
        /// <param name="asString">Whether the value is a string first, as under backwards-compatible behaviour.</param>
        /// <returns>The identity, or <see langword="null"/> for a value nothing can be found under.</returns>
        public static string? Identity(XPathValue item, bool asString = false)
        {
            if (asString)
            {
                return "s:" + XdmSequence.StringValueOf(item);
            }

            if (item.Kind == XPathValueKind.Number && double.IsNaN(item.ToNumber()))
            {
                return null;
            }

            return ForEachGroupInstruction.KeyIdentity(item);
        }

        /// <summary>The identity of a composite key's whole sequence of values.</summary>
        /// <param name="identities">The identities of the values, in order.</param>
        public static string CompositeIdentity(IReadOnlyList<string> identities)
        {
            System.Text.StringBuilder joined = new System.Text.StringBuilder().Append(identities.Count);

            foreach (string identity in identities)
            {
                joined.Append(':').Append(identity.Length).Append(':').Append(identity);
            }

            return joined.ToString();
        }

        /// <summary>
        /// The identities of a value's items, as a lookup or an index reads them: each item's own, or for a
        /// composite key the one identity of all of them together.
        /// </summary>
        /// <param name="value">The key value or values.</param>
        /// <param name="composite">Whether the key is composite.</param>
        /// <param name="output">Where the identities go. A composite value with a <c>NaN</c> in it adds none.</param>
        /// <param name="asStrings">
        /// Whether every value is a string first, which is what XSLT 1.0 keys were and what backwards-
        /// compatible behaviour keeps: <c>key('k', 1.0)</c> then finds a node filed under '1'.
        /// </param>
        public static void Identities(XPathValue value, bool composite, List<string> output, bool asStrings = false)
        {
            if (!composite)
            {
                foreach (XPathValue item in XdmSequence.Items(value))
                {
                    if (Identity(item, asStrings) is string identity)
                    {
                        output.Add(identity);
                    }
                }

                return;
            }

            List<string> parts = new();

            foreach (XPathValue item in XdmSequence.Items(value))
            {
                if (Identity(item, asStrings) is not string identity)
                {
                    return;
                }

                parts.Add(identity);
            }

            output.Add(CompositeIdentity(parts));
        }
    }

    /// <summary>
    /// A call to <c>key()</c>.
    /// </summary>
    internal sealed class KeyExpr : Expr
    {
        private readonly int m_keyIndex;
        private readonly Expr? m_nameExpression;
        private readonly Expr m_value;
        private readonly Expr? m_document;

        /// <summary>Initializes a key lookup.</summary>
        /// <param name="keyIndex">The key, where its name was a literal; -1 where it is computed.</param>
        /// <param name="nameExpression">The name expression, where the name is computed.</param>
        /// <param name="value">The values to look up.</param>
        /// <param name="document">
        /// The third argument, if written: a node whose subtree is searched, in whichever tree it is in.
        /// </param>
        public KeyExpr(int keyIndex, Expr? nameExpression, Expr value, Expr? document = null)
        {
            m_keyIndex = keyIndex;
            m_nameExpression = nameExpression;
            m_value = value;
            m_document = document;
        }

        /// <summary>The package the call is written in, which is where a computed name is looked up.</summary>
        public int Package { get; init; }

        /// <summary>
        /// Whether the call is read with backwards-compatible behaviour, under which the values looked up
        /// are strings first (§20.2.2), as every key value was in XSLT 1.0.
        /// </summary>
        public bool BackwardsCompatible { get; init; }

        /// <summary>
        /// The namespace bindings in scope where the call is written, which a computed name's prefix is
        /// resolved in (§20.2.2).
        /// </summary>
        public IReadOnlyDictionary<string, string>? Namespaces { get; init; }

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => true;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children
        {
            get
            {
                if (m_nameExpression is not null)
                {
                    yield return m_nameExpression;
                }

                yield return m_value;

                if (m_document is not null)
                {
                    yield return m_document;
                }
            }
        }

        /// <inheritdoc/>
        public override bool MaySpanDocuments => m_document is not null;

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            List<int> nodes = NodeListPool.Rent();
            try
            {
                XdmTree tree = EvaluateNodes(ref context, nodes);
                return XPathValue.FromNodeSet(NodeSet.FromOrderedNodes(tree, nodes));
            }
            finally
            {
                NodeListPool.Return(nodes);
            }
        }

        /// <inheritdoc/>
        public override XdmTree EvaluateNodes(ref DynamicContext context, List<int> output)
        {
            XsltRuntime runtime = context.Runtime
                ?? throw new XsltException("key() can only be used during a transformation.");

            int keyIndex = m_keyIndex;
            if (keyIndex < 0)
            {
                string name = m_nameExpression!.Evaluate(ref context).ToStringValue();
                keyIndex = runtime.ResolveKeyIndex(name, Namespaces, Package);
            }

            // Two arguments search the tree the context node is in, so there has to be one. A stylesheet
            // function has none, and answering with the source document there would search a tree nothing in
            // the call named.
            if (m_document is null && context.Node < 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE1270,
                    "key() with two arguments searches the tree the context node is in, and there is no "
                    + "context node here. A stylesheet function has none; name the tree with a third "
                    + "argument.");
            }

            // And that tree has to be a document. A key indexes a document, so a tree rooted at anything else
            // has no index to consult — which is the shape a parentless node has, and what an as declaration
            // on a variable produces.
            if (m_document is null)
            {
                RequireDocumentRoot(context.Tree, context.Node, "The context node of a two-argument key()");
            }

            XPathValue value = m_value.Evaluate(ref context);
            int before = output.Count;

            // The third argument names a node, and the search is of the subtree under it (§20.2.2): the
            // lookups run over the document that node is in, and only what stands at or below the node is
            // kept.
            DynamicContext searched = context;
            int top = -1;

            if (m_document is not null)
            {
                NodeSet within = NodeSet.Of(
                    m_document.Evaluate(ref context), context.Tree, XsltErrorCode.XPTY0004, "key()'s third argument");

                if (within.Count == 0)
                {
                    return context.Tree;
                }

                RequireDocumentRoot(
                    within.TreeAt(0), within[0], "The third argument of key()");

                searched = context.SwitchTree(within.TreeAt(0), within[0]);
                top = within[0];
            }

            List<string> identities = new();
            KeyDefinition.Identities(value, runtime.KeyIsComposite(keyIndex), identities, BackwardsCompatible);

            foreach (string identity in identities)
            {
                List<int>? found = runtime.LookupKey(keyIndex, identity, ref searched);

                if (found is null)
                {
                    continue;
                }

                if (top < 0 || top == XdmTree.RootNode)
                {
                    output.AddRange(found);
                    continue;
                }

                foreach (int node in found)
                {
                    if (InSubtree(searched.Tree, node, top))
                    {
                        output.Add(node);
                    }
                }
            }

            // A single lookup is already ordered; only a union over several values can interleave.
            if (identities.Count > 1 && output.Count - before > 1)
            {
                output.Sort(before, output.Count - before, Comparer<int>.Default);
                Deduplicate(output, before);
            }

            return searched.Tree;
        }

        /// <summary>Whether a node stands at or below another: the subtree the third argument names.</summary>
        private static bool InSubtree(XdmTree tree, int node, int top)
        {
            if (XdmTree.IsAttribute(top))
            {
                return node == top;
            }

            int element = XdmTree.IsAttribute(node) ? tree.ParentOf(node) : node;
            return element >= top && element <= tree.SubtreeEndOf(top);
        }

        private static void RequireDocumentRoot(XdmTree tree, int node, string what)
        {
            if (tree.KindOf(tree.RootOf(node)) == NodeKind.Root)
            {
                return;
            }

            throw XsltErrors.Error(
                XsltErrorCode.XTDE1270,
                $"{what} is in a tree whose root is not a document node, and a key indexes a document. A "
                + "node built by a sequence constructor with an 'as' declaration is its own root, so there "
                + "is no document under it to have been indexed.");
        }

        private static void Deduplicate(List<int> nodes, int start)
        {
            int write = start + 1;
            for (int read = write; read < nodes.Count; read++)
            {
                if (nodes[read] != nodes[write - 1])
                {
                    nodes[write++] = nodes[read];
                }
            }

            nodes.RemoveRange(write, nodes.Count - write);
        }
    }

    /// <summary>
    /// A call to <c>id()</c> or <c>element-with-id()</c>: the elements an ID attribute names.
    /// </summary>
    /// <remarks>
    /// An attribute is of type ID where the document's type declaration says so, or where it is
    /// <c>xml:id</c>, which is one by its own specification wherever it is written; a schema could say so
    /// too, and this engine reads none. Each argument string is a whitespace-separated list of IDs, and the answer is
    /// the elements carrying any of them, in document order, none twice. The two functions differ only for
    /// an ID on an attribute a schema typed, which does not arise.
    /// </remarks>
    internal sealed class IdExpr : Expr
    {
        private readonly string m_name;
        private readonly Expr m_values;
        private readonly Expr? m_node;

        /// <summary>Initializes a lookup.</summary>
        /// <param name="name">Which of the two functions was called, for messages.</param>
        /// <param name="values">The IDs, as strings each holding one or more.</param>
        /// <param name="node">The second argument, naming the document to search, or null for the context node's.</param>
        public IdExpr(string name, Expr values, Expr? node)
        {
            m_name = name;
            m_values = values;
            m_node = node;
        }

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => true;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => m_node is not null;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children
        {
            get
            {
                yield return m_values;

                if (m_node is not null)
                {
                    yield return m_node;
                }
            }
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            if (AnchorOf(m_name, m_node, ref context) is not { } where)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            XdmTree tree = where.Tree;

            Dictionary<string, int> ids = context.Runtime?.IdIndexOf(tree) ?? BuildIndex(tree);
            List<int> found = new();

            foreach (XPathValue item in XdmSequence.Items(m_values.Evaluate(ref context)))
            {
                foreach (string id in item.ToStringValue().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (ids.TryGetValue(id, out int element) && !found.Contains(element))
                    {
                        found.Add(element);
                    }
                }
            }

            found.Sort();
            return XPathValue.FromNodeSet(NodeSet.FromOrderedNodes(tree, found));
        }

        /// <summary>
        /// The tree and node a lookup starts from: the second argument's first node, or the context node,
        /// which has to be in a document (FODC0001) — a parentless node is its own root, and there is no
        /// document under it to have IDs in.
        /// </summary>
        internal static (XdmTree Tree, int Anchor)? AnchorOf(string name, Expr? node, ref DynamicContext context)
        {
            XdmTree tree;
            int anchor;

            if (node is not null)
            {
                NodeSet named = NodeSet.Of(
                    node.Evaluate(ref context), context.Tree, XsltErrorCode.XPTY0004, $"{name}()'s second argument");

                // Declared node(), which is one node and not a sequence of them: an empty one is a type
                // error rather than a search of nothing that finds nothing.
                if (named.Count == 0)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XPTY0004,
                        $"{name}()'s second argument names the document to search and is declared node(), "
                        + "so it has to be one node; the empty sequence is not one.");
                }

                tree = named.TreeAt(0);
                anchor = named[0];
            }
            else
            {
                anchor = context.RequireContextNode($"fn:{name}()");
                tree = context.Tree;
            }

            if (tree.KindOf(tree.RootOf(anchor)) != NodeKind.Root)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FODC0001,
                    $"{name}() searches the document its node is in, and the node given is not in a "
                    + "document: its tree is rooted at an element, as a node built with an 'as' declaration is.");
            }

            return (tree, anchor);
        }

        /// <summary>
        /// Indexes a tree's elements by their ID attributes. Where two elements claim one ID the first in
        /// document order keeps it, as <c>id()</c> is defined to answer.
        /// </summary>
        /// <param name="tree">The tree.</param>
        public static Dictionary<string, int> BuildIndex(XdmTree tree)
        {
            Dictionary<string, int> ids = new(StringComparer.Ordinal);

            for (int node = 0; node < tree.NodeCount; node++)
            {
                if (tree.KindOf(node) != NodeKind.Element)
                {
                    continue;
                }

                int count = tree.AttributeCountOf(node);

                for (int i = 0; i < count; i++)
                {
                    int attribute = tree.AttributeAt(node, i);

                    if (tree.IsIdAttribute(attribute))
                    {
                        ids.TryAdd(tree.StringValueOf(attribute).Trim(), node);
                    }
                }
            }

            return ids;
        }
    }

    /// <summary>
    /// A call to <c>idref()</c>: the attributes of type IDREF or IDREFS that refer to any of the IDs given.
    /// </summary>
    /// <remarks>
    /// The inverse of <c>id()</c>, and answered from the same declaration: an attribute is IDREF or
    /// IDREFS where the document's type declaration typed it so, and there is no <c>xml:idref</c> to be
    /// one without it. Each argument string is one ID — not a list, as id() takes — and the answer is every
    /// such attribute any of whose tokens is one of them, in document order. A schema could type an
    /// element IDREF as well, which does not arise here.
    /// </remarks>
    internal sealed class IdrefExpr : Expr
    {
        private readonly Expr m_values;
        private readonly Expr? m_node;

        /// <summary>Initializes a lookup.</summary>
        /// <param name="values">The IDs, as strings each holding one or more.</param>
        /// <param name="node">The second argument, naming the document to search, or null for the context node's.</param>
        public IdrefExpr(Expr values, Expr? node)
        {
            m_values = values;
            m_node = node;
        }

        /// <inheritdoc/>
        public override bool ReturnsNodeSet => true;

        /// <inheritdoc/>
        public override bool MaySpanDocuments => m_node is not null;

        /// <inheritdoc/>
        internal override IEnumerable<Expr> Children
        {
            get
            {
                yield return m_values;

                if (m_node is not null)
                {
                    yield return m_node;
                }
            }
        }

        /// <inheritdoc/>
        public override XPathValue Evaluate(ref DynamicContext context)
        {
            if (IdExpr.AnchorOf("idref", m_node, ref context) is not { } where)
            {
                return XPathValue.FromSequence(XdmSequence.Empty);
            }

            XdmTree tree = where.Tree;
            HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);

            foreach (XPathValue item in XdmSequence.Items(m_values.Evaluate(ref context)))
            {
                // Each string is one IDREF value, not a list as id() takes: one that is not an NCName is not
                // an IDREF value and refers to nothing.
                string id = item.ToStringValue();

                if (PackageVersion.IsNcName(id))
                {
                    wanted.Add(id);
                }
            }

            List<int> found = new List<int>();

            if (wanted.Count != 0)
            {
                foreach (int entry in tree.DeclaredIdrefAttributes)
                {
                    int attribute = XdmTree.AttributeIdBase + entry;

                    foreach (string token in tree.StringValueOf(attribute).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (wanted.Contains(token))
                        {
                            found.Add(attribute);
                            break;
                        }
                    }
                }
            }

            return XPathValue.FromNodeSet(NodeSet.FromOrderedNodes(tree, found));
        }
    }
}
