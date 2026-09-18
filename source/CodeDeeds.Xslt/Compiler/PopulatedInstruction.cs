using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>
    /// Whether what a sequence constructor produced counts as something, for the three XSLT 3.0
    /// instructions that ask.
    /// </summary>
    /// <remarks>
    /// One definition serving <c>xsl:where-populated</c>, <c>xsl:on-empty</c> and <c>xsl:on-non-empty</c>,
    /// because the specification gives them one. The part worth knowing is what does <em>not</em> count: an
    /// element carrying attributes and nothing else is not populated. That is the whole point of the
    /// feature — a <c>&lt;div class="x"/&gt;</c> wrapped around nothing is exactly the empty markup a
    /// stylesheet is trying not to write.
    /// </remarks>
    internal static class Populated
    {
        /// <summary>
        /// Whether <c>xsl:where-populated</c> leaves an item out: a document or element with no children,
        /// any other node whose string value is zero-length, an atomic value that casts to a zero-length
        /// string, an empty map, or an array with nothing in it that is not itself deemed empty.
        /// </summary>
        /// <remarks>
        /// Not the same question <c>xsl:on-empty</c> asks, which counts an element as something whatever
        /// it holds and looks only for zero-length text nodes, empty document nodes and blank values —
        /// see <see cref="Any"/>. The specification defines the two separately, and an element with no
        /// children is the case they part on.
        /// </remarks>
        public static bool DeemedEmpty(XPathValue item)
        {
            switch (item.Kind)
            {
                case XPathValueKind.Node:
                {
                    XdmTree tree = item.NodeTree;
                    int node = item.NodeId;

                    return tree.KindOf(node) is NodeKind.Element or NodeKind.Root
                        ? tree.FirstChildOf(node) < 0
                        : tree.StringValueOf(node).Length == 0;
                }

                case XPathValueKind.NodeSet:
                    return false;

                case XPathValueKind.Map:
                    return item.AsMap().Count == 0;

                case XPathValueKind.Array:
                {
                    foreach (XPathValue member in item.AsArray().Members)
                    {
                        foreach (XPathValue flattened in XdmSequence.Items(member))
                        {
                            if (!DeemedEmpty(flattened))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                }

                default:
                    return item.TypeCode is XdmTypeCode.String
                        or XdmTypeCode.AnyUri
                        or XdmTypeCode.UntypedAtomic
                        or XdmTypeCode.HexBinary
                        or XdmTypeCode.Base64Binary
                        && item.ToStringValue().Length == 0;
            }
        }

        /// <summary>
        /// Whether a result is worth keeping, which is what <c>xsl:where-populated</c> asks.
        /// </summary>
        /// <remarks>
        /// Stricter than <see cref="Any"/> by one rule, and that rule is the whole point of the
        /// instruction: a single element or document node with <em>no children</em> does not count, however
        /// many attributes it carries. A <c>&lt;div class="x"/&gt;</c> wrapped around nothing is exactly the
        /// empty markup a stylesheet is trying not to write.
        /// </remarks>
        /// <param name="value">What the sequence constructor produced.</param>
        public static bool Test(XPathValue value)
        {
            List<XPathValue> items = XdmSequence.Items(value);

            if (items.Count != 1)
            {
                return Any(value);
            }

            XPathValue only = items[0];

            if (only.Kind != XPathValueKind.Node)
            {
                return only.ToStringValue().Length != 0;
            }

            XdmTree tree = only.NodeTree;
            int node = only.NodeId;

            return tree.KindOf(node) switch
            {
                NodeKind.Element or NodeKind.Root => tree.FirstChildOf(node) >= 0,
                NodeKind.Text => tree.StringValueOf(node).Length != 0,
                _ => true,
            };
        }

        /// <summary>
        /// Whether anything at all was produced, which is what <c>xsl:on-empty</c> and
        /// <c>xsl:on-non-empty</c> ask about their siblings.
        /// </summary>
        /// <remarks>
        /// An empty element counts here where it does not for <see cref="Test"/>, and the difference is
        /// which question is being asked. <c>xsl:where-populated</c> is asking about the wrapper it just
        /// built and whether there is anything inside it; these two are asking whether their neighbours
        /// wrote anything, and an empty element is something a neighbour wrote.
        /// </remarks>
        /// <param name="value">What the rest of the sequence constructor produced.</param>
        public static bool Any(XPathValue value)
        {
            foreach (XPathValue item in XdmSequence.Items(value))
            {
                // A zero-length text node is what an xsl:value-of over nothing leaves behind, and is
                // indistinguishable in the result from having written nothing at all.
                // And a document node stands for its children, so one with none is nothing too: what a
                // parameter holds when its content copied a node that was not there.
                bool blank = item.Kind == XPathValueKind.Node
                    ? item.NodeTree.KindOf(item.NodeId) switch
                    {
                        NodeKind.Text => item.NodeTree.StringValueOf(item.NodeId).Length == 0,
                        NodeKind.Root => item.NodeTree.FirstChildOf(item.NodeId) < 0,
                        _ => false,
                    }
                    : item.Kind != XPathValueKind.NodeSet && item.ToStringValue().Length == 0;

                if (!blank)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// <c>xsl:where-populated</c>, which throws away what it produced if what it produced was nothing much.
    /// </summary>
    /// <remarks>
    /// The instruction exists so that a stylesheet can wrap markup around content that may not be there
    /// without testing twice for it. Written out longhand the alternative is an <c>xsl:if</c> whose test
    /// repeats the selection the body is about to make, which is both the duplication and the second
    /// traversal this removes.
    /// </remarks>
    internal sealed class WherePopulatedInstruction : Instruction
    {
        private readonly Instruction[] m_body;

        /// <summary>Initializes a where-populated instruction.</summary>
        /// <param name="body">What it produces, if it produces anything.</param>
        public WherePopulatedInstruction(Instruction[] body)
        {
            m_body = body;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            // Item by item, as the specification writes it: the result is what the body produced with every
            // item deemed empty left out, so an empty element beside a full one goes and the full one stays.
            XPathValue produced = VariableInstruction.CaptureSequence(m_body, ref context, runtime);

            foreach (XPathValue item in XdmSequence.Items(produced))
            {
                if (!Populated.DeemedEmpty(item))
                {
                    SequenceWriter.Write(item, runtime);
                }
            }
        }
    }

    /// <summary>
    /// <c>xsl:assert</c>, which refuses the transformation when something the stylesheet believes turns out
    /// not to be so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <c>xsl:message terminate="yes"</c> written shorter, though that is what it does. The difference
    /// is what it says to the reader: an assertion is a claim about what the stylesheet expects to be true,
    /// so it may be turned off wholesale by a processor that trusts its input, and a message may not. This
    /// processor checks them always, having no mechanism to turn them off — which is the opposite of the
    /// specification's default and is left to the processor to choose.
    /// </para>
    /// <para>
    /// What happens when one fails is defined by reference (§23.2): the effect is an
    /// <c>xsl:message</c> with the same <c>select</c>, the same <c>error-code</c>, the same content and
    /// <c>terminate="yes"</c>, except that the code where none is named is <c>XTMM9001</c> rather than
    /// <c>XTMM9000</c>. So that is what this holds and runs, rather than a second rendering of the same
    /// rules: the message is written to whoever is reading messages, its content travels with the error for
    /// an <c>xsl:catch</c> to read, and a message that cannot be built does not itself stop the
    /// transformation.
    /// </para>
    /// </remarks>
    internal sealed class AssertInstruction : Instruction
    {
        private readonly Expr m_test;
        private readonly MessageInstruction m_failure;

        /// <summary>Initializes an assert instruction.</summary>
        /// <param name="test">What has to be true.</param>
        /// <param name="failure">The terminating message a failure is defined to be.</param>
        public AssertInstruction(Expr test, MessageInstruction failure)
        {
            m_test = test;
            m_failure = failure;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            bool held;

            try
            {
                held = m_test.EvaluateAsBoolean(ref context);
            }
            catch (XsltException)
            {
                // "If the effective boolean value is false, or if a dynamic error occurs during evaluation
                // of the expression, then the assertion fails" — and the note is explicit that the
                // instruction then fails with XTMM9001 rather than with what the expression raised. An
                // assertion is a claim that something holds, and an expression that cannot be evaluated has
                // not shown that it does.
                held = false;
            }

            if (!held)
            {
                m_failure.Execute(ref context, runtime);
            }
        }
    }

    /// <summary>
    /// <c>xsl:fork</c>, which says that its branches could be evaluated in one pass over the input.
    /// </summary>
    /// <remarks>
    /// A promise about streamability and nothing else. A streaming processor uses it to make several passes
    /// over a document it can only read once; one that has the whole document in memory, as this engine
    /// does, already has that freedom and has nothing to arrange. So the branches run in order and their
    /// results are concatenated, which is what the specification says the result is either way.
    /// </remarks>
    internal sealed class ForkInstruction : Instruction
    {
        private readonly Instruction[] m_body;

        /// <summary>Initializes a fork instruction.</summary>
        /// <param name="body">The branches.</param>
        public ForkInstruction(Instruction[] body)
        {
            m_body = body;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            ExecuteAll(m_body, ref context, runtime);
        }
    }

    /// <summary>
    /// <c>xsl:source-document</c>, which reads a document and processes it with the content.
    /// </summary>
    /// <remarks>
    /// The streaming counterpart of binding <c>doc($href)</c> to a variable, and without streaming that is
    /// exactly what it amounts to: the document becomes the context item for the body. What it buys a
    /// streaming processor is that the document need never exist in memory at all — which is the one thing
    /// this engine cannot offer, since its whole model is a tree of integer-indexed nodes.
    /// </remarks>
    internal sealed class SourceDocumentInstruction : Instruction
    {
        private readonly AttributeValueTemplate m_href;
        private readonly Instruction[] m_body;
        private readonly string? m_baseUri;
        private readonly AccumulatorSet m_accumulators;

        /// <summary>Initializes a source-document instruction.</summary>
        /// <param name="href">Where to read the document from.</param>
        /// <param name="body">What to do with it.</param>
        /// <param name="baseUri">
        /// The base URI of the element that wrote the href, which a relative one resolves against — the same
        /// rule <c>document()</c> follows, and for the same reason: the reference was written in a stylesheet
        /// module and means what it means there, not what it would mean from wherever the run started.
        /// </param>
        /// <param name="accumulators">Which accumulators apply to the document this makes available.</param>
        public SourceDocumentInstruction(
            AttributeValueTemplate href,
            Instruction[] body,
            string? baseUri = null,
            AccumulatorSet? accumulators = null)
        {
            m_href = href;
            m_body = body;
            m_baseUri = baseUri;
            m_accumulators = accumulators ?? AccumulatorSet.None;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            // A fragment identifier is followed here as document() follows one: a bare name names the
            // element with that ID, which an xml:id gives without any schema or document type declaration,
            // and that element is what the body processes. Without the fragment it is the document node,
            // which is the ordinary case and what the name of the instruction suggests.
            string href = m_href.Evaluate(ref context);
            XdmTree document = runtime.LoadDocument(href, m_baseUri, out int selected);

            if (selected < 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTRE1160,
                    $"'{href}' names an element by its ID, and the document holds no element with it. "
                    + "There is nothing for xsl:source-document to process.");
            }

            runtime.MakeAvailable(document, m_accumulators);

            DynamicContext inner = context.WithItem(XPathValue.FromNode(document, selected));
            inner.CurrentNode = inner.Node;
            inner.CurrentTree = inner.Tree;
            inner.Position = 1;
            inner.Size = 1;

            ExecuteAll(m_body, ref inner, runtime);
        }
    }

    /// <summary>One piece of a sequence constructor that holds an <c>xsl:on-empty</c> or <c>xsl:on-non-empty</c>.</summary>
    /// <param name="Body">The instructions of this piece.</param>
    /// <param name="When">
    /// Null for an ordinary run of instructions, true for an <c>xsl:on-non-empty</c>, false for an
    /// <c>xsl:on-empty</c>.
    /// </param>
    internal readonly record struct ConditionalSegment(Instruction[] Body, bool? When);

    /// <summary>
    /// A sequence constructor holding an <c>xsl:on-empty</c> or an <c>xsl:on-non-empty</c>, which cannot be
    /// run straight through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Those two instructions ask about their <em>siblings</em>: what the rest of this sequence constructor
    /// produced. So the rest has to be run first, and run into a buffer, before either of them can be
    /// answered — which is why a constructor containing one is compiled to this rather than to a flat list.
    /// Constructors without one are untouched and pay nothing.
    /// </para>
    /// <para>
    /// Each ordinary run is buffered separately so the pieces can be written back in the order they were
    /// written, with the conditional ones taking their places between: <c>&lt;ul&gt;</c> holding an
    /// <c>xsl:on-non-empty</c> that writes a heading, followed by the <c>xsl:apply-templates</c> that may
    /// write nothing, has to put the heading before the items and not after them.
    /// </para>
    /// </remarks>
    internal sealed class ConditionalSequenceInstruction : Instruction
    {
        private readonly ConditionalSegment[] m_segments;

        /// <summary>Initializes a conditional sequence.</summary>
        /// <param name="segments">The pieces, in the order they were written.</param>
        public ConditionalSequenceInstruction(ConditionalSegment[] segments)
        {
            m_segments = segments;
        }

        /// <inheritdoc/>
        public override void Execute(ref DynamicContext context, XsltRuntime runtime)
        {
            XPathValue[] produced = new XPathValue[m_segments.Length];
            bool populated = false;

            // Every segment is evaluated in the order it was written, the conditional ones included. They
            // have to be: an xsl:on-non-empty may name a variable declared just above it and rebound just
            // below, and deferring it to the second pass would read the later value. What is conditional is
            // whether the result is kept, not whether it is computed.
            for (int i = 0; i < m_segments.Length; i++)
            {
                produced[i] = VariableInstruction.CaptureSequence(m_segments[i].Body, ref context, runtime);

                if (m_segments[i].When is null)
                {
                    populated |= Populated.Any(produced[i]);
                }
            }

            // An empty result is replaced by what the xsl:on-empty says, whole: the zero-length strings and
            // text nodes that made it empty are not written beside the replacement, where they would still
            // stand between atomic values and put spaces where the suite's on-empty-114b has none.
            for (int i = 0; i < m_segments.Length; i++)
            {
                if (m_segments[i].When is bool wanted ? wanted != populated : !populated)
                {
                    continue;
                }

                SequenceWriter.Write(produced[i], runtime);
            }
        }
    }

    /// <summary>
    /// Writes a captured sequence into the output it was captured away from.
    /// </summary>
    /// <remarks>
    /// Shared by every instruction that has to buffer before it can decide — <c>xsl:try</c> to be able to
    /// roll back, the three populated instructions to be able to ask whether there was anything. Where the
    /// target holds a sequence the value goes in whole, so a document node stays one item rather than being
    /// flattened into its children.
    /// </remarks>
    internal static class SequenceWriter
    {
        /// <summary>Writes a value to the runtime's current output.</summary>
        public static void Write(XPathValue value, XsltRuntime runtime)
        {
            if (runtime.Output.TryAppendValue(value))
            {
                return;
            }

            // An array contributes its members, and an atomic value is written as one, which is what puts a
            // space between it and an atomic value written just before.
            foreach (XPathValue item in XdmSequence.ContentItems(value))
            {
                if (item.Kind == XPathValueKind.Node)
                {
                    // A text node written with output escaping disabled keeps that on its way straight to
                    // the result, which is what a template's declared result is on. Held in a variable or
                    // handed back by a function it goes through the copier and loses it, as the
                    // specification allows — the suite's doe-0182 against its doe-0184 and doe-0186.
                    if (item.NodeTree.IsRawText(item.NodeId))
                    {
                        runtime.Output.WriteRawText(item.NodeTree.StringValueOf(item.NodeId));
                        continue;
                    }

                    // And inside a copied element too, which is where an xsl:try buffer holds one: the try
                    // is between the instruction and the serializer and nothing else is, so the escaping it
                    // switched off is still off. Only a tree that was built as a stand-in for the final
                    // output carries the mark that deep — see SequenceCaptureTarget.WriteRawText.
                    NodeCopier.CopyDeep(item.NodeTree, item.NodeId, runtime.Output, rawText: true);
                    continue;
                }

                if (item.Kind == XPathValueKind.NodeSet)
                {
                    NodeSet nodes = item.AsNodeSet();
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        NodeCopier.CopyDeep(nodes.TreeAt(i), nodes[i], runtime.Output);
                    }

                    continue;
                }

                if (item.Kind is XPathValueKind.Map or XPathValueKind.Function)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE0450,
                        "A map or a function item cannot be written into the content of a node.");
                }

                runtime.Output.WriteAtomic(XdmSequence.StringValueOf(item));
            }
        }
    }
}
