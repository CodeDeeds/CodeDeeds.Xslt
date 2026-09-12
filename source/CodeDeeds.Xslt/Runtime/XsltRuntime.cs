using CodeDeeds.Xslt.Compiler;
using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Runtime
{
    /// <summary>
    /// The state of one transformation in progress.
    /// </summary>
    /// <remarks>
    /// Everything that changes while a transformation runs lives here rather than on the compiled stylesheet,
    /// which is what allows a single compiled stylesheet to be used by several threads at once. A runtime is
    /// created per transformation and is not itself thread-safe.
    /// </remarks>
    public sealed class XsltRuntime
    {
        /// <summary>
        /// The deepest chain of template invocations allowed before the transformation is abandoned. A
        /// recursive template with no terminating case would otherwise exhaust the stack.
        /// </summary>
        private const int MaximumCallDepth = 2000;

        private readonly CompiledStylesheet m_stylesheet;
        private readonly Dictionary<XdmTree, int[]> m_fingerprintMaps = new();
        private readonly TemplateIndex m_templates;
        private Dictionary<XdmTree, TemplateIndex>? m_templatesByTree;
        private readonly XPathValue[] m_globals;

        /// <summary>How far each global has got; see the <c>Global*</c> constants.</summary>
        private readonly byte[] m_globalState;

        /// <summary>Whether xsl:global-context-item said the globals are to see no context item.</summary>
        private bool m_globalContextAbsent;

        /// <summary>A global that has not been evaluated yet.</summary>
        private const byte GlobalPending = 0;

        /// <summary>A global being evaluated now, which is how a definition that reaches itself is caught.</summary>
        private const byte GlobalEvaluating = 1;

        /// <summary>A global whose value is settled, whether evaluated here or supplied by the caller.</summary>
        private const byte GlobalReady = 2;
        private readonly Dictionary<XdmTree, Dictionary<string, List<int>>[]> m_keyIndexes = new();

        /// <summary>The elements of each tree by xml:id, built when id() first asks about the tree.</summary>
        private readonly Dictionary<XdmTree, Dictionary<string, int>> m_idIndexes = new();

        /// <summary>The elements of a tree by their <c>xml:id</c>, indexed once per tree.</summary>
        /// <param name="tree">The tree.</param>
        internal Dictionary<string, int> IdIndexOf(XdmTree tree)
        {
            if (!m_idIndexes.TryGetValue(tree, out Dictionary<string, int>? ids))
            {
                ids = IdExpr.BuildIndex(tree);
                m_idIndexes.Add(tree, ids);
            }

            return ids;
        }

        /// <summary>
        /// Which keys are being indexed now, which is how a key defined in terms of itself is caught.
        /// </summary>
        /// <remarks>
        /// Kept per key rather than per key and document. Circularity is a property of the definitions and
        /// not of what they are applied to, so a key whose <c>use</c> reaches itself is circular whichever
        /// document is being indexed at the time.
        /// </remarks>
        private readonly bool[] m_keysIndexing;
        private readonly Dictionary<XdmTree, int> m_treeIds = new();
        /// <summary>
        /// The documents already read, by the reference that named them and the stripping they were read
        /// with. One file read by two packages that strip differently is two documents.
        /// </summary>
        private readonly Dictionary<(string Reference, int Scope), XdmTree> m_documents = new();
        private readonly XsltOptions m_options;
        private int m_callDepth;

        /// <summary>Whether the processor claims XSLT 3.0, which decides the codes a later specification renamed.</summary>
        private bool Implements30 => m_options.Version.CompareTo(XsltVersion.V30) >= 0;
        private Template? m_tailTemplate;
        private ParameterValue[] m_tailParameters = Array.Empty<ParameterValue>();
        private UserFunction? m_tailFunction;
        private XPathValue[]? m_tailArguments;
        private int m_currentPrecedence = int.MaxValue;
        private int m_currentMode = CompiledStylesheet.DefaultMode;

        /// <summary>The template now running, which xsl:next-match starts its search after.</summary>
        private TemplateRule? m_currentRule;

        /// <summary>The mode in force, which is what <c>mode="#current"</c> asks for.</summary>
        internal int CurrentMode => m_currentMode;

        /// <summary>
        /// The item being processed where it is not a node — an atomic value an <c>xsl:for-each</c> or
        /// <c>xsl:apply-templates</c> is walking — which is what <c>current()</c> answers with there.
        /// </summary>
        /// <remarks>
        /// Kept on the transformation rather than in the context, which carries the current node and
        /// would grow by an item for the rare case. The instruction walking the items sets it for each and
        /// puts back what it found, and a node being current — the context's own record — takes precedence,
        /// since an inner walk over nodes is the innermost one.
        /// </remarks>
        internal XPathValue? CurrentAtomicItem { get; set; }

        /// <summary>
        /// The tunnel parameters in force, which every template invocation passes on unchanged unless it says
        /// otherwise.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Held here rather than on <see cref="DynamicContext"/>, alongside the other things a template
        /// invocation saves and restores. That context is a struct copied at every step of every path
        /// expression, and tunnel parameters are read at exactly one place — the moment a template binds its
        /// declarations — so making every copy carry them would charge the whole engine for a feature used by
        /// a handful of templates. Safe because nothing here defers execution: a template body always runs
        /// inside the invocation that set this up.
        /// </para>
        /// <para>
        /// One array, shared by every invocation that does not extend it, because the set is immutable once
        /// built. A call site that passes no tunnel parameters therefore costs a field copy and nothing else.
        /// </para>
        /// </remarks>
        private ParameterValue[] m_tunnel = Array.Empty<ParameterValue>();

        /// <summary>
        /// Whether the caller supplied a document to transform, as against starting at a named template with
        /// nothing to read.
        /// </summary>
        /// <remarks>
        /// The tree is never absent — everything that resolves a name needs one — so a transformation with
        /// no source runs against an empty document. This is what says that document is a stand-in and not
        /// the context item, which is the difference between reading the context item and finding nothing
        /// and reading it being an error.
        /// </remarks>
        internal bool HasSourceDocument { get; }

        internal XsltRuntime(
            CompiledStylesheet stylesheet,
            XdmTree inputTree,
            OutputTarget output,
            XsltOptions options,
            bool hasSourceDocument = true)
        {
            m_stylesheet = stylesheet;
            m_options = options;
            Output = output;
            PrincipalOutput = output;
            CurrentOutputUri = options.BaseOutputUri;
            InputTree = inputTree;
            HasSourceDocument = hasSourceDocument;

            if (hasSourceDocument && options.InputUri is string inputUri)
            {
                // The source document goes into the pool under its own URI, so that doc() asked for that
                // URI answers with this tree rather than reading the file a second time. Two trees over one
                // document would be two sets of nodes, and doc(document-uri(.)) is . would be false where
                // the specification has one URI name one document for the whole transformation. It goes in
                // under the top-level package's stripping, which is what it was built with.
                m_documents[(inputUri, stylesheet.WhitespaceScopeOf(0))] = inputTree;
            }

            foreach (ModeDeclaration rules in stylesheet.ModeRules.Values)
            {
                m_hasTypedModes |= rules.Typed;
            }

            int[] map = stylesheet.Names.BuildFingerprintMap(inputTree);
            m_fingerprintMaps.Add(inputTree, map);
            m_templates = new TemplateIndex(stylesheet.Rules, map);

            m_globals = new XPathValue[stylesheet.GlobalSlotCount];
            m_globalState = new byte[stylesheet.GlobalSlotCount];
            m_keysIndexing = new bool[stylesheet.Keys.Count];
        }

        /// <summary>Gets or sets where instructions currently write.</summary>
        /// <remarks>
        /// Redirected temporarily while capturing the content of an <c>xsl:variable</c> or <c>xsl:attribute</c>.
        /// </remarks>
        public OutputTarget Output
        {
            get => m_output;
            set
            {
                // Swaps nest: a capture is put in place and the previous target put back, and the second
                // of those is told apart by being the target the top of the stack saved. A capture begun
                // exactly at a document copy's floor is at that floor still — an attribute at its top is an
                // attribute on the document — so the floor moves to depth zero of the capture; one begun
                // deeper carries no floor, and a parentless attribute captured there is what it looks like.
                if (m_outputSwaps.Count != 0 && ReferenceEquals(m_outputSwaps.Peek().Previous, value))
                {
                    (_, DocumentFloor, DocumentFloorTarget) = m_outputSwaps.Pop();
                }
                else if (m_output is not null)
                {
                    m_outputSwaps.Push((m_output, DocumentFloor, DocumentFloorTarget));

                    bool atFloor = DocumentFloor >= 0
                        && ReferenceEquals(DocumentFloorTarget, m_output)
                        && m_output.OpenElementDepth == DocumentFloor;

                    DocumentFloor = atFloor ? 0 : -1;
                    DocumentFloorTarget = atFloor ? value : null;
                }

                m_output = value;
            }
        }

        private OutputTarget m_output = null!;

        private readonly Stack<(OutputTarget Previous, int Floor, OutputTarget? FloorTarget)> m_outputSwaps = new();

        /// <summary>
        /// The element depth at which a document node is being constructed, or -1 where none is.
        /// </summary>
        /// <remarks>
        /// Set by an <c>xsl:copy</c> of a document node, whose content runs straight into whatever is already
        /// open rather than into a document node built and copied out again. Everything a document node can
        /// hold is the same either way but for one thing, and this is what catches it: an attribute or
        /// namespace node written at this depth has only the document node to attach to, and a document node
        /// carries neither.
        /// </remarks>
        internal int DocumentFloor = -1;

        /// <summary>
        /// The output the floor was measured on. A body captured into another target while a document is
        /// being copied starts a depth of its own, and the floor says nothing about it.
        /// </summary>
        internal OutputTarget? DocumentFloorTarget;

        /// <summary>
        /// Whether an <c>xsl:next-iteration</c> or <c>xsl:break</c> is unwinding to its <c>xsl:iterate</c>.
        /// </summary>
        /// <remarks>
        /// Read by <see cref="Instruction.ExecuteAll"/> after each instruction, which is how the signal
        /// travels out through however many <c>xsl:if</c> and <c>xsl:choose</c> lie between. Held here beside
        /// the tunnel parameters and for the same reason: <see cref="DynamicContext"/> is a struct copied at
        /// every focus change, and this is read once per instruction rather than carried by every copy.
        /// </remarks>
        internal LoopSignal LoopSignal;

        /// <summary>
        /// Refuses an attribute or namespace node written where only a document node could hold it.
        /// </summary>
        /// <remarks>
        /// A field read and nothing else in the ordinary case, which is what keeps <c>xsl:attribute</c> free
        /// of the cost: the floor is set only while an <c>xsl:copy</c> of a document node is running its
        /// content.
        /// </remarks>
        /// <param name="what">What was being written, for the message.</param>
        internal void RequireAnElementForAttribute(string what)
        {
            if (DocumentFloor >= 0
                && ReferenceEquals(DocumentFloorTarget, Output)
                && Output.OpenElementDepth <= DocumentFloor)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0420,
                    $"{what} cannot be written here. What is being built is a document node, and a document "
                    + "node carries neither.");
            }
        }

        /// <summary>Gets the tree being transformed.</summary>
        public XdmTree InputTree { get; }

        /// <summary>
        /// Gets where the transformation's own result goes.
        /// </summary>
        /// <remarks>
        /// The destination of the base output URI, which <c>xsl:result-document</c> names too by leaving out
        /// its <c>href</c> — so it has to stay reachable after <see cref="Output"/> has been redirected.
        /// </remarks>
        internal OutputTarget PrincipalOutput { get; }

        /// <summary>Whether an <c>xsl:result-document</c> has taken the base output URI.</summary>
        private bool m_principalClaimed;

        /// <summary>What the principal destination held when that instruction finished with it.</summary>
        private bool m_principalContentAfterClaim;

        /// <summary>
        /// Takes the base output URI for an <c>xsl:result-document</c> that named it, which is what leaving
        /// out the <c>href</c> does, and returns where to write.
        /// </summary>
        /// <remarks>
        /// The instruction's own serialization applies to the tree it builds, and that tree goes exactly
        /// where the transformation's own result would have gone. A second writer over the same destination
        /// gives both, and only one of the two may ever write — writing to both is what
        /// <c>XTDE1490</c> forbids.
        /// </remarks>
        /// <param name="settings">How that instruction asked for its result to be serialized.</param>
        /// <exception cref="XsltException">Something has already written there — <c>XTDE1490</c>.</exception>
        internal OutputTarget ClaimPrincipalResult(OutputSettings settings)
        {
            if (m_principalClaimed || PrincipalOutput.HasContent)
            {
                throw XsltErrors.Error(XsltErrorCode.XTDE1490, PrincipalTwice);
            }

            m_principalClaimed = true;

            // A result document, even the principal one: an empty XML document still gets its declaration.
            if (PrincipalOutput is OutputWriter principal)
            {
                OutputWriter claimed = principal.WithSettings(settings);
                claimed.DeclaresWhenEmpty = true;
                return claimed;
            }

            return PrincipalOutput;
        }

        /// <summary>Records what the principal destination held once the claiming instruction was done.</summary>
        internal void ReleasePrincipalResult() => m_principalContentAfterClaim = PrincipalOutput.HasContent;

        /// <summary>Checks that nothing wrote to the base output URI after an instruction claimed it.</summary>
        private void CheckPrincipalResult()
        {
            if (m_principalClaimed && PrincipalOutput.HasContent != m_principalContentAfterClaim)
            {
                throw XsltErrors.Error(XsltErrorCode.XTDE1490, PrincipalTwice);
            }
        }

        private const string PrincipalTwice =
            "This transformation writes two result trees to the base output URI. An xsl:result-document "
            + "with no href names that URI, which is also where anything written outside one goes, and a URI "
            + "may carry one final result tree only.";

        /// <summary>
        /// The group being processed by the innermost <c>xsl:for-each-group</c>, and the key it shares.
        /// </summary>
        /// <remarks>
        /// Held here rather than on the context because <c>current-group()</c> and
        /// <c>current-grouping-key()</c> are functions, not variables: they are meaningless outside a
        /// grouping body, and a body that calls a template must still see them from inside it.
        /// </remarks>
        internal (XPathValue Group, XPathValue Key)? CurrentGroup { get; set; }

        /// <summary>
        /// The URI of the result document being written, or the base output URI outside any; null where
        /// there is none to name. What current-output-uri() answers, when the output is not temporary.
        /// </summary>
        internal string? CurrentOutputUri { get; set; }

        /// <summary>How many patterns are being matched, for what a pattern may not read.</summary>
        internal int PatternDepth { get; set; }

        /// <summary>
        /// How deep the transformation is in temporary output state that no output target records: a
        /// function being called, a function item, a variable's select expression. What
        /// current-output-uri() answers nothing in.
        /// </summary>
        internal int TemporaryDepth { get; set; }

        /// <summary>The base output URI, or null where the transformation was given none.</summary>
        internal string? BaseOutputUri => m_options.BaseOutputUri;

        /// <summary>
        /// Resolves a result document's href against the base output URI, or leaves it as written where
        /// there is none to resolve against.
        /// </summary>
        internal string ResolveOutputUri(string href)
        {
            if (m_options.BaseOutputUri is not string baseOutput
                || !XPath.UriReference.TryParse(href, out XPath.UriReference reference)
                || !XPath.UriReference.TryParse(baseOutput, out XPath.UriReference against))
            {
                return href;
            }

            return XPath.UriReference.Resolve(reference, against);
        }

        /// <summary>Whether an href, resolved, names the principal result.</summary>
        internal bool IsBaseOutputUri(string resolved)
        {
            return m_options.BaseOutputUri is string baseOutput
                && string.Equals(resolved, baseOutput, StringComparison.Ordinal);
        }

        /// <summary>The group an <c>xsl:merge-action</c> is running for, or null outside one.</summary>
        internal MergeGroup? CurrentMerge { get; set; }

        /// <summary>
        /// How deep the call stack was when <see cref="CurrentMerge"/> was set.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What tells apart "inside the merge action" from "inside something the merge action called".
        /// <c>current-merge-group()</c> belongs to the action's own sequence constructor and to nothing it
        /// invokes, so a template or function reached from the action must not see it — which is a narrower
        /// scope than <c>current-group()</c> has, where a called template deliberately does see the group.
        /// </para>
        /// <para>
        /// Compared against the depth rather than cleared on the way into a call, and the reason is
        /// mechanical: clearing it would mean another local in <see cref="InvokeTemplate"/>, whose frame a
        /// thousand-deep recursion pays for once per level and which has about three per cent of headroom.
        /// A field written once per merge group costs that nothing.
        /// </para>
        /// </remarks>
        internal int CurrentMergeDepth { get; set; } = -1;

        /// <summary>How many template and function invocations are on the stack.</summary>
        internal int CallDepth => m_callDepth;

        /// <summary>
        /// The captured groups of the match <c>xsl:analyze-string</c> is currently handling, or
        /// <see langword="null"/> outside a matching branch.
        /// </summary>
        internal string[]? RegexGroups { get; set; }

        /// <summary>
        /// The substring <c>xsl:analyze-string</c> is currently handling, or <see langword="null"/> outside
        /// either of its branches.
        /// </summary>
        /// <remarks>
        /// The one thing the current item can be that is not a node, and the reason it lives here rather
        /// than on the context: <see cref="DynamicContext"/> is copied at every focus change, and a field
        /// that one instruction in the language ever writes is not worth widening every copy of it by.
        /// <see cref="RegexGroups"/> is held here for the same reason and is read by the same branches.
        /// </remarks>
        internal string? CurrentSubstring { get; set; }

        /// <summary>Gets or sets the sink that receives <c>xsl:message</c> output.</summary>
        public TextWriter? MessageWriter { get; set; }

        /// <summary>
        /// Where the result documents go, for a transformation <c>fn:transform</c> is running.
        /// </summary>
        /// <remarks>
        /// Set only there. It is what makes <c>xsl:result-document</c> build a value rather than write
        /// through a resolver, the caller having asked for the documents themselves.
        /// </remarks>
        internal Compiler.TransformResults? Results { get; init; }

        /// <summary>
        /// What templates are first applied to, as a value rather than as an expression to evaluate.
        /// </summary>
        /// <remarks>
        /// <see cref="XsltOptions.InitialMatchSelection"/> is a string because a caller outside the engine
        /// has no way to build a sequence; <c>fn:transform</c> is inside it and is handed one, so the items
        /// are what it has and there is nothing to parse.
        /// </remarks>
        internal XPath.XPathValue? InitialSelection { get; init; }

        /// <summary>The parameters the transformation's entry point is called with, where any were given.</summary>
        internal Compiler.ParameterValue[]? InitialParameters { get; init; }

        /// <summary>
        /// Returns the slot-to-fingerprint mapping for a tree, computing it on first use.
        /// </summary>
        /// <remarks>
        /// Usually there is only the input tree, but a result tree fragment carries its own name table, so a
        /// path navigating into one needs its own mapping.
        /// </remarks>
        /// <param name="tree">The tree to resolve names against.</param>
        public int[] GetFingerprintMap(XdmTree tree)
        {
            // A mapping shorter than the table is one made before xsl:evaluate gave a name its first slot,
            // and is built again; the slots it had keep their places, so a context still holding the old
            // array is not wrong, only unable to see the new ones.
            if (m_fingerprintMaps.TryGetValue(tree, out int[]? map) && map.Length == m_stylesheet.Names.Count)
            {
                return map;
            }

            map = m_stylesheet.Names.BuildFingerprintMap(tree);
            m_fingerprintMaps[tree] = map;
            return map;
        }

        /// <summary>
        /// Gets the template index for a tree, building one the first time that tree is dispatched against.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <see cref="TemplateIndex"/> buckets rules by the fingerprint their name test resolves to, and a
        /// fingerprint means something only within one tree's name table. Dispatching a node of a document
        /// loaded by <c>document()</c> against the input tree's index therefore finds nothing in the name
        /// buckets, and the node silently falls through to the built-in rules — the template that should have
        /// matched simply does not run. So each tree gets its own index.
        /// </para>
        /// <para>
        /// The input tree is nearly always the one being dispatched against, so it is checked first and costs
        /// a reference comparison; anything else goes through the dictionary.
        /// </para>
        /// </remarks>
        private TemplateIndex IndexFor(XdmTree tree)
        {
            if (ReferenceEquals(tree, InputTree))
            {
                return m_templates;
            }

            m_templatesByTree ??= new Dictionary<XdmTree, TemplateIndex>();

            if (!m_templatesByTree.TryGetValue(tree, out TemplateIndex? index))
            {
                index = new TemplateIndex(m_stylesheet.Rules, GetFingerprintMap(tree));
                m_templatesByTree.Add(tree, index);
            }

            return index;
        }

        /// <summary>
        /// Ensures a global variable has been evaluated, evaluating it on first use.
        /// </summary>
        /// <remarks>
        /// Globals may refer to one another in any order, so they cannot simply be evaluated in declaration
        /// order. Evaluating on demand resolves the dependencies naturally and makes a circular definition
        /// detectable rather than silently producing an empty value.
        /// </remarks>
        /// <param name="slot">The global's slot.</param>
        /// <param name="context">The context to evaluate in.</param>
        /// <exception cref="XsltException">The global's definition is circular.</exception>
        public void EnsureGlobal(int slot, ref DynamicContext context)
        {
            if (m_globalState[slot] == 2)
            {
                return;
            }

            GlobalVariable? global = null;
            foreach (GlobalVariable candidate in m_stylesheet.Globals)
            {
                if (candidate.Slot == slot)
                {
                    global = candidate;
                    break;
                }
            }

            if (global is null)
            {
                m_globalState[slot] = GlobalReady;
                return;
            }

            // Reaching an abstract declaration means no package supplied the component it stands for. The
            // specification gives that its own code rather than letting the absence surface as an empty
            // sequence failing the declared type, which says nothing about what actually went wrong.
            if (global.IsAbstract)
            {
                throw AbstractComponent("variable", "$" + global.Name.LocalName);
            }

            if (m_globalState[slot] == GlobalEvaluating)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0640,
                    $"The global variable '${global.Name.LocalName}' is defined in terms of itself. A "
                    + "circularity in a stylesheet has no value to settle on, whichever way round it is "
                    + "evaluated.");
            }

            m_globalState[slot] = GlobalEvaluating;

            // Globals see no template's local variables, and their context item is the global one: the root
            // of the source document, or nothing where there is no source document — an xsl:copy in one is
            // then XTTE0945, as it is in a template reached with no focus. A library's globals have none
            // either way: the global context item is the top-level package's (§2.3.2). They may declare local variables
            // of their own, though, so the frame is the one their body was compiled against rather than
            // none at all.
            DynamicContext globalContext = new DynamicContext(
                InputTree,
                HasSourceDocument && !global.InLibrary && !m_globalContextAbsent
                    ? XdmTree.RootNode
                    : DynamicContext.NotANode,
                GetFingerprintMap(InputTree))
            {
                Globals = m_globals,
                Runtime = this,
                Locals = global.FrameSize == 0
                    ? Array.Empty<XPathValue>()
                    : new XPathValue[global.FrameSize],
            };

            m_globals[slot] = VariableInstruction.Evaluate(
                global.Select,
                global.Body,
                ref globalContext,
                this,
                global.Type,
                global.IsParameter
                    ? DefaultValueCode(global.Select, global.Body)
                    : XsltErrorCode.XTTE0570,
                global.BaseUri);

            m_globalState[slot] = GlobalReady;
        }

        /// <summary>
        /// Installs the values the caller supplied through <see cref="XsltOptions.Parameters"/>.
        /// </summary>
        /// <remarks>
        /// A name the stylesheet does not declare as a parameter is ignored, which is what the specification
        /// asks for and what lets one set of values serve several stylesheets: a caller need not know which of
        /// them reads what. Every declaration of a name is bound rather than only the first, because a
        /// parameter declared again in an importing module is the same parameter and not another one.
        /// </remarks>
        private void BindSuppliedParameters()
        {
            if (m_options.Parameters is not { Count: > 0 } supplied)
            {
                return;
            }

            foreach (KeyValuePair<string, object?> entry in supplied)
            {
                ExpandedName name = StylesheetParameters.ParseName(entry.Key);
                XPathValue value = StylesheetParameters.Convert(entry.Key, entry.Value);

                foreach (GlobalVariable global in m_stylesheet.Globals)
                {
                    if (global.IsParameter && global.Name.Equals(name))
                    {
                        // A declared type applies to what the caller supplied as much as to a default the
                        // stylesheet wrote: a parameter declared xs:integer is an integer however it arrived.
                        m_globals[global.Slot] = XdmTypeConversion.Apply(value, global.Type, XsltErrorCode.XTTE0590);
                        m_globalState[global.Slot] = GlobalReady;
                    }
                }
            }
        }

        /// <summary>
        /// Loads a document named by <c>document()</c>, reusing one already loaded for the same URI.
        /// </summary>
        /// <remarks>
        /// Caching is not just an optimisation here. XSLT requires two calls naming the same document to yield
        /// the same nodes, otherwise comparing or counting across them would be meaningless.
        /// </remarks>
        /// <param name="href">The document reference as written.</param>
        /// <param name="baseUri">The stylesheet's base URI, which a relative reference resolves against.</param>
        /// <returns>The loaded document.</returns>
        /// <exception cref="XsltException">No document resolver is configured, or the document is unavailable.</exception>
        public XdmTree LoadDocument(string href, string? baseUri, int package = 0)
        {
            XdmTree tree = LoadDocument(href, baseUri, out int selected, package);

            if (selected != XdmTree.RootNode)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTRE1160,
                    $"'{href}' names a fragment of a document, and only document() follows one.");
            }

            return tree;
        }

        /// <summary>
        /// Loads a document, following a fragment identifier to the node it names.
        /// </summary>
        /// <remarks>
        /// A fragment identifier selects nodes inside the document by rules the document's media type lays
        /// down, and what this engine reads is XML: a shorthand pointer, a bare name, names the element
        /// with that ID (XPointer §3.2), which a document type declaration or an <c>xml:id</c> says. Any
        /// other form is refused (XTRE1160) rather than answered with the whole document, which would
        /// answer a narrower question with a wider answer. The document's identity is what stands before
        /// the <c>#</c>, so two references into one document load it once.
        /// </remarks>
        /// <param name="href">The reference, with or without a fragment identifier.</param>
        /// <param name="baseUri">The stylesheet's base URI, which a relative reference resolves against.</param>
        /// <param name="selected">The node the reference names: the root, or the element an ID names, or -1 where no element has it.</param>
        /// <param name="package">
        /// The package the call was written in, which decides what whitespace the document is stripped of:
        /// the declarations are local to a package (XSLT 3.0 §3.6.5).
        /// </param>
        /// <returns>The loaded document.</returns>
        public XdmTree LoadDocument(string href, string? baseUri, out int selected, int package = 0)
        {
            selected = XdmTree.RootNode;
            int hash = href.IndexOf('#');

            if (hash < 0)
            {
                return LoadWholeDocument(href, baseUri, package);
            }

            string fragment = href[(hash + 1)..];

            if (!PackageVersion.IsNcName(fragment))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTRE1160,
                    $"document('{href}') names a fragment of a document by a form this engine does not "
                    + "follow: only a bare name, an NCName, which names the element with that ID.");
            }

            XdmTree tree = LoadWholeDocument(href[..hash], baseUri, package);
            selected = IdIndexOf(tree).TryGetValue(fragment, out int element) ? element : -1;
            return tree;
        }

        /// <summary>
        /// Whether a document could be read as a stream, which is what <c>fn:stream-available()</c> asks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This engine streams nothing: it builds a tree and transforms that. But the question the function
        /// asks is not "will you stream it" — it is whether the document is there and begins as XML, so
        /// that a stylesheet can pick a route before committing to one. Answering false to everything would
        /// be answering a question nobody asked, and the suite's own tests for the function carry no
        /// streaming dependency: they expect an answer about the document.
        /// </para>
        /// <para>
        /// Nothing is available without a document resolver, as nothing is readable without one.
        /// </para>
        /// </remarks>
        /// <param name="href">The reference, as the call wrote it.</param>
        /// <param name="baseUri">What a relative reference resolves against.</param>
        internal bool StartsAsXml(string href, string? baseUri)
        {
            if (m_options.DocumentResolver is null)
            {
                return false;
            }

            ResolvedResource? resolved;

            try
            {
                resolved = m_options.DocumentResolver.Resolve(href, baseUri);
            }
            catch (Exception error)
                when (error is XsltException or IOException or UnauthorizedAccessException or UriFormatException)
            {
                return false;
            }

            if (resolved is null)
            {
                return false;
            }

            using (resolved.Reader)
            {
                return XdmTreeBuilder.StartsAsXml(resolved.Reader, m_options.EntityResolver, resolved.Uri);
            }
        }

        private XdmTree LoadWholeDocument(string href, string? baseUri, int package)
        {
            // Keyed on the reference as written together with what it resolves against: the same
            // reference from two places in a stylesheet whose base URIs differ names two documents, and
            // xml:base is how a stylesheet gives two places different base URIs. And on what the reading
            // package strips, because that is part of what the document is: two packages stripping
            // differently read two different documents from one file, and two stripping nothing read one.
            int scope = m_stylesheet.WhitespaceScopeOf(package);
            (string, int) asWritten =
                (baseUri is null ? href : string.Concat(baseUri, "\n", href), scope);

            if (m_documents.TryGetValue(asWritten, out XdmTree? cached))
            {
                return cached;
            }

            if (m_options.DocumentResolver is null)
            {
                throw new XsltException(
                    $"This stylesheet calls document('{href}'), but no document resolver was configured. "
                    + "Set XsltOptions.DocumentResolver to allow documents to be loaded.");
            }

            ResolvedResource? resolved = m_options.DocumentResolver.Resolve(href, baseUri)
                ?? throw XsltErrors.Error(XsltErrorCode.FODC0002, $"The document '{href}' could not be found.");

            // Two spellings of one document are one document: what the reference resolved to may already
            // have been read under another, and the nodes have to be the same nodes.
            if (m_documents.TryGetValue((resolved.Uri, scope), out cached))
            {
                resolved.Reader.Dispose();
                m_documents[asWritten] = cached;
                return cached;
            }

            XdmTree tree;
            try
            {
                tree = XdmTreeBuilder.FromXml(
                    resolved.Reader,
                    null,
                    m_stylesheet.WhitespaceIn(package),
                    false,
                    m_options.EntityResolver,
                    resolved.Uri);
            }
            catch (System.Xml.XmlException exception)
            {
                // A document that will not parse is a document that could not be retrieved, as far as the
                // stylesheet is concerned, and the specification gives that a code. Letting XmlException out
                // instead reports a failure of this engine for what is a property of the document.
                throw XsltErrors.Error(
                    XsltErrorCode.FODC0002,
                    $"The document '{href}' could not be read: {exception.Message}",
                    exception);
            }
            finally
            {
                resolved.Reader.Dispose();
            }

            if (m_resultDocuments is not null && m_resultDocuments.Contains(resolved.Uri))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE1500,
                    $"The document '{resolved.Uri}' was written by this transformation and is now being read "
                    + "from it. Whether the read sees what the write put there would depend on an order "
                    + "nothing settles, so the two together are refused.");
            }

            m_documentsRead.Add(resolved.Uri);

            // Keyed on the reference as written, so that repeating it in the stylesheet hits the cache, and
            // also on the resolved identity, so two spellings of one document share a tree.
            m_documents[asWritten] = tree;
            m_documents.TryAdd((resolved.Uri, scope), tree);
            m_documentUris[tree] = resolved.Uri;
            return tree;
        }

        /// <summary>The base URI the caller supplied, which relative references resolve against.</summary>
        internal string? BaseUri => m_options.BaseUri;

        /// <summary>
        /// The one reading of the clock this transformation makes, which every <c>current-*</c> call in it
        /// shares.
        /// </summary>
        /// <remarks>
        /// One per transformation and not one per expression, because the requirement is that the whole
        /// execution scope agree: two templates asking the time must be told the same time, or a stylesheet
        /// stamping a date on every page could straddle midnight.
        /// </remarks>
        internal Clock Clock { get; } = new Clock();

        private readonly Dictionary<XdmTree, string> m_documentUris = new();

        /// <summary>
        /// The URI a tree was loaded from, or <see langword="null"/> where it has none.
        /// </summary>
        /// <remarks>
        /// A result tree fragment has none, and neither does input handed over as text — which is why
        /// <c>document-uri()</c> and <c>base-uri()</c> both return the empty sequence rather than inventing
        /// something.
        /// </remarks>
        /// <param name="tree">The tree to identify.</param>
        internal string? UriOf(XdmTree tree)
        {
            if (m_documentUris.TryGetValue(tree, out string? uri))
            {
                return uri;
            }

            // The input's own URI, which is not the stylesheet's. Answering with the stylesheet's had every
            // reference inside the input resolving from the wrong directory, and document-uri() naming a
            // file the document was never in.
            return ReferenceEquals(tree, InputTree) ? m_options.InputUri : null;
        }

        /// <summary>
        /// The base URI a tree's nodes are relative to, before any <c>xml:base</c> inside it.
        /// </summary>
        /// <remarks>
        /// Where a tree came from is its own URI. A tree the stylesheet built came from nowhere and has
        /// none, but its nodes are still relative to something: a node in a result tree takes its base URI
        /// from the stylesheet element that created it, so a relative reference written into a result is
        /// relative to the stylesheet rather than to wherever the result is later saved. A built tree that
        /// remembers which element that was says so itself; one that does not is relative to the stylesheet.
        /// </remarks>
        /// <param name="tree">The tree.</param>
        internal string? BaseUriOf(XdmTree tree) => UriOf(tree) ?? tree.BaseUri ?? m_stylesheet.BaseUri;

        /// <summary>
        /// Reads a resource as text, which is <c>unparsed-text()</c>.
        /// </summary>
        /// <remarks>
        /// Through the document resolver, because reading a file as text is reading data by another name, and
        /// a caller who has allowed documents to be loaded has already answered the question this asks. Only
        /// the parsing differs: the bytes are taken as characters and nothing is required to be XML.
        /// </remarks>
        /// <param name="href">The reference as written.</param>
        /// <param name="encoding">The encoding named by the caller, if any.</param>
        internal string LoadText(string href, string? encoding)
        {
            if (m_options.DocumentResolver is null)
            {
                throw new XsltException(
                    $"This stylesheet calls unparsed-text('{href}'), but no document resolver was configured. "
                    + "Set XsltOptions.DocumentResolver to allow resources to be read.");
            }

            if (href.IndexOf('#') >= 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.FOUT1170,
                    $"unparsed-text() was given '{href}', which carries a fragment identifier. A fragment "
                    + "names part of a document, and there is no part of a text file to name.");
            }

            if (encoding is not null)
            {
                // The resolver hands over a reader, which has already settled the encoding. Saying which one
                // was wanted cannot change that, so a stylesheet naming one is told rather than ignored.
                try
                {
                    System.Text.Encoding.GetEncoding(encoding);
                }
                catch (ArgumentException)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.FOUT1170, $"'{encoding}' is not an encoding this platform knows.");
                }
            }

            ResolvedResource? resolved = m_options.DocumentResolver.Resolve(href, m_options.BaseUri)
                ?? throw XsltErrors.Error(
                    XsltErrorCode.FOUT1170, $"unparsed-text() could not read '{href}'.");

            try
            {
                return resolved.Reader.ReadToEnd();
            }
            finally
            {
                resolved.Reader.Dispose();
            }
        }

        /// <summary>
        /// Returns a small stable number identifying a tree within this transformation.
        /// </summary>
        /// <remarks>
        /// Combined with a node's index it gives <c>generate-id()</c> an identifier that is unique across every
        /// document in play, including result tree fragments, without needing anything stored on the nodes.
        /// </remarks>
        /// <param name="tree">The tree to identify.</param>
        public int GetTreeId(XdmTree tree)
        {
            if (m_treeIds.TryGetValue(tree, out int id))
            {
                return id;
            }

            id = m_treeIds.Count;
            m_treeIds.Add(tree, id);
            return id;
        }

        /// <summary>
        /// Resolves a key name written as a run-time string to its index.
        /// </summary>
        /// <param name="name">The key name, which may carry a prefix.</param>
        /// <param name="namespaces">The namespace bindings in scope where the call is written, which the prefix is resolved in.</param>
        /// <param name="package">The package the call is written in, whose keys are the ones in view.</param>
        /// <exception cref="XsltException">No key of that name is declared.</exception>
        public int ResolveKeyIndex(string name, IReadOnlyDictionary<string, string>? namespaces, int package = 0)
        {
            int colon = name.IndexOf(':');
            string prefix = colon < 0 ? string.Empty : name[..colon];
            string localName = colon < 0 ? name : name[(colon + 1)..];
            string? uri = string.Empty;

            // A prefix means what it means where the call is written, and one bound nowhere names no key.
            if (prefix.Length != 0 && (namespaces is null || !namespaces.TryGetValue(prefix, out uri)))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE1260,
                    $"The key name '{name}' uses the prefix '{prefix}', which is bound to no namespace where "
                    + "the call is written.");
            }

            ExpandedName wanted = new ExpandedName(uri ?? string.Empty, localName);

            foreach (KeyDefinition key in m_stylesheet.Keys)
            {
                if (key.Name == wanted && key.Package == package)
                {
                    return key.Index;
                }
            }

            // With the specification's code, which is also what lets a pattern report it rather than read
            // it as a non-match: a key nothing declares is the stylesheet's mistake, not the data's.
            throw XsltErrors.Error(XsltErrorCode.XTDE1260, $"No xsl:key named '{name}' is declared.");
        }

        /// <summary>Whether a key was declared <c>composite</c>, which decides how a lookup value is read.</summary>
        /// <param name="keyIndex">The key's index.</param>
        public bool KeyIsComposite(int keyIndex)
        {
            return m_stylesheet.Keys[keyIndex].Composite;
        }

        /// <summary>
        /// Looks up the nodes a key associates with a value, building the key's index on first use.
        /// </summary>
        /// <remarks>
        /// Indexes are built per document and per key, and only when something actually asks for one, so a
        /// stylesheet that declares keys it does not use on a given input pays nothing for them.
        /// </remarks>
        /// <param name="keyIndex">The key's index.</param>
        /// <param name="value">The value to look up.</param>
        /// <param name="context">The context, which supplies the document being indexed.</param>
        /// <returns>The matching nodes in document order, or <see langword="null"/> if there are none.</returns>
        public List<int>? LookupKey(int keyIndex, string value, ref DynamicContext context)
        {
            XdmTree tree = context.Tree;

            if (!m_keyIndexes.TryGetValue(tree, out Dictionary<string, List<int>>[]? perKey))
            {
                perKey = new Dictionary<string, List<int>>[m_stylesheet.Keys.Count];
                m_keyIndexes.Add(tree, perKey);
            }

            Dictionary<string, List<int>>? index = perKey[keyIndex];
            if (index is null)
            {
                KeyDefinition definition = m_stylesheet.Keys[keyIndex];

                if (m_keysIndexing[keyIndex])
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE0640,
                        $"The key '{definition.Name.LocalName}' is defined in terms of itself: indexing a "
                        + "document against it asks for the key that is being built. A key definition may "
                        + "not reach itself, directly or through another key.");
                }

                m_keysIndexing[keyIndex] = true;

                try
                {
                    index = BuildKeyIndex(definition, tree);
                }
                finally
                {
                    m_keysIndexing[keyIndex] = false;
                }

                perKey[keyIndex] = index;
            }

            return index.TryGetValue(value, out List<int>? nodes) ? nodes : null;
        }

        /// <summary>
        /// Indexes a document against one key by walking it once, in document order.
        /// </summary>
        /// <remarks>
        /// Walking in preorder means each bucket comes out already in document order, so no sorting is needed
        /// afterwards. Attributes are visited alongside their element because a key's pattern may match them.
        /// </remarks>
        /// <summary>What each accumulator has been computed to be, per document.</summary>
        private readonly Dictionary<XdmTree, AccumulatorValues?[]> m_accumulators = new();

        /// <summary>
        /// The values of one accumulator over one document, computing them on first use.
        /// </summary>
        /// <remarks>
        /// Per document and per accumulator, because a stylesheet that declares six and reads one over a
        /// document it opened once should walk that document once, not six times and not at all for the five.
        /// </remarks>
        internal AccumulatorValues AccumulatorValuesFor(int index, XdmTree tree)
        {
            if (!m_accumulators.TryGetValue(tree, out AccumulatorValues?[]? perAccumulator))
            {
                perAccumulator = new AccumulatorValues?[m_stylesheet.Accumulators.Count];
                m_accumulators.Add(tree, perAccumulator);
            }

            if (perAccumulator[index] is AccumulatorValues built)
            {
                return built;
            }

            // An accumulator whose rule reads itself has no first value to start from, and the recursion is
            // not survivable: it exhausts the stack, which cannot be caught and takes the process with it.
            // Kept per accumulator rather than per document, because circularity is a property of the
            // definitions and not of what they are applied to.
            m_accumulatorsBuilding ??= new bool[m_stylesheet.Accumulators.Count];

            if (m_accumulatorsBuilding[index])
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE3400,
                    $"The accumulator '{m_stylesheet.Accumulators[index].Name.LocalName}' is defined in "
                    + "terms of itself, directly or through another accumulator.");
            }

            m_accumulatorsBuilding[index] = true;

            try
            {
                return perAccumulator[index] = BuildAccumulator(m_stylesheet.Accumulators[index], tree);
            }
            finally
            {
                m_accumulatorsBuilding[index] = false;
            }
        }

        /// <summary>Which accumulators are being computed now, which is how one reaching itself is caught.</summary>
        private bool[]? m_accumulatorsBuilding;

        /// <summary>Which accumulators apply to a document, for the documents something said it about.</summary>
        /// <remarks>
        /// Absent means all of them: what <c>doc()</c> and <c>document()</c> read, and every tree the
        /// stylesheet builds for itself, carry every accumulator without being asked. Only a document made
        /// available by something with a <c>use-accumulators</c> to say is entered here, and then with
        /// exactly what it said.
        /// </remarks>
        private Dictionary<XdmTree, AccumulatorSet>? m_applicable;

        /// <summary>
        /// Records which accumulators apply to a document that has just been made available.
        /// </summary>
        /// <remarks>
        /// The set belongs to the document rather than to whatever is reading it: an
        /// <c>xsl:source-document</c> that names one accumulator does not widen again inside a template it
        /// applies. Recorded on first availability and left alone after that — a document already read is
        /// handed back from the cache rather than read again, so the accumulators it was first read for are
        /// the ones it has.
        /// </remarks>
        /// <param name="tree">The document.</param>
        /// <param name="applicable">Which accumulators apply to it.</param>
        internal void MakeAvailable(XdmTree tree, AccumulatorSet applicable)
        {
            m_applicable ??= new Dictionary<XdmTree, AccumulatorSet>();

            if (!m_applicable.ContainsKey(tree))
            {
                m_applicable.Add(tree, applicable);
            }
        }

        /// <summary>Whether one accumulator applies to a document.</summary>
        /// <param name="index">The accumulator's index.</param>
        /// <param name="tree">The document.</param>
        internal bool AppliesTo(int index, XdmTree tree)
        {
            return m_applicable is null
                || !m_applicable.TryGetValue(tree, out AccumulatorSet? applicable)
                || applicable.Contains(index);
        }

        /// <summary>
        /// Walks a document once, recording what an accumulator was worth as each node began and ended.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The flat tree numbers nodes in preorder, so the starts come out simply by counting upwards. The
        /// ends need the other order, and a stack of open nodes gives it: a node's end is reached when the
        /// walk passes the last id in its subtree, which <see cref="XdmTree.SubtreeEndOf"/> already knows.
        /// So one pass produces both orders.
        /// </para>
        /// <para>
        /// A rule's <c>$value</c> is what the accumulator was worth before the rule fired, which is what
        /// makes a rule a rewriting rather than an assignment — and what lets <c>$value + 1</c> be the whole
        /// of a counter.
        /// </para>
        /// </remarks>
        private AccumulatorValues BuildAccumulator(AccumulatorDefinition accumulator, XdmTree tree)
        {
            AccumulatorValues values = new AccumulatorValues(tree.NodeCount);

            DynamicContext context = new DynamicContext(tree, XdmTree.RootNode, GetFingerprintMap(tree))
            {
                Globals = m_globals,
                Runtime = this,
                Locals = accumulator.FrameSize == 0
                    ? Array.Empty<XPathValue>()
                    : new XPathValue[accumulator.FrameSize],
            };

            XPathValue value = accumulator.InitialValue is null
                ? XPathValue.FromString(string.Empty)
                : XdmTypeConversion.Apply(
                    accumulator.InitialValue.Evaluate(ref context),
                    accumulator.Type,
                    XsltErrorCode.XTTE0570);

            // The nodes whose start has been passed and whose end has not, innermost last, with the id the
            // walk has to reach before each one ends.
            List<int> open = new List<int>();

            for (int node = 0; node < tree.NodeCount; node++)
            {
                while (open.Count > 0 && tree.SubtreeEndOf(open[^1]) < node)
                {
                    int closing = open[^1];
                    open.RemoveAt(open.Count - 1);

                    value = Fire(accumulator, closing, atEnd: true, value, ref context);
                    values.SetAfter(closing, value);
                }

                value = Fire(accumulator, node, atEnd: false, value, ref context);
                values.SetBefore(node, value);
                open.Add(node);
            }

            for (int i = open.Count - 1; i >= 0; i--)
            {
                value = Fire(accumulator, open[i], atEnd: true, value, ref context);
                values.SetAfter(open[i], value);
            }

            return values;
        }

        /// <summary>Applies the last rule of a phase that matches a node, or leaves the value alone.</summary>
        private XPathValue Fire(
            AccumulatorDefinition accumulator,
            int node,
            bool atEnd,
            XPathValue value,
            ref DynamicContext context)
        {
            // Taken from the end: when more than one rule of a phase matches a node, the last of them is the
            // one that fires, with neither a priority nor an error to say otherwise.
            for (int i = accumulator.Rules.Length - 1; i >= 0; i--)
            {
                AccumulatorRule rule = accumulator.Rules[i];
                if (rule.AtEnd != atEnd)
                {
                    continue;
                }

                bool matched = false;
                foreach (Pattern pattern in rule.Patterns)
                {
                    DynamicContext testing = context;
                    testing.Node = node;

                    if (pattern.Matches(node, ref testing))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    continue;
                }

                DynamicContext inner = context;
                inner.Node = node;
                inner.CurrentNode = node;
                inner.CurrentTree = inner.Tree;
                inner.Position = 1;
                inner.Size = 1;
                inner.Locals[rule.ValueSlot] = value;

                return XdmTypeConversion.Apply(
                    VariableInstruction.Evaluate(rule.Select, rule.Body, ref inner, this),
                    accumulator.Type,
                    XsltErrorCode.XTTE0570);
            }

            return value;
        }

        private Dictionary<string, List<int>> BuildKeyIndex(KeyDefinition key, XdmTree tree)
        {
            Dictionary<string, List<int>> index = new(StringComparer.Ordinal);

            DynamicContext context = new DynamicContext(tree, XdmTree.RootNode, GetFingerprintMap(tree))
            {
                Globals = m_globals,
                Runtime = this,
            };

            for (int node = 0; node < tree.NodeCount; node++)
            {
                IndexNode(key, node, index, ref context);

                int attributeCount = tree.AttributeCountOf(node);
                for (int i = 0; i < attributeCount; i++)
                {
                    IndexNode(key, tree.AttributeAt(node, i), index, ref context);
                }
            }

            return index;
        }

        private void IndexNode(
            KeyDefinition key,
            int node,
            Dictionary<string, List<int>> index,
            ref DynamicContext context)
        {
            // Each declaration of the name files the node it matches under the values it reads (§20.2.1);
            // a node several declarations match is filed by each of them.
            foreach (KeyRule rule in key.Rules)
            {
                bool matches = false;
                foreach (Pattern pattern in rule.Patterns)
                {
                    DynamicContext patternContext = context;
                    patternContext.Node = node;

                    if (pattern.Matches(node, ref patternContext))
                    {
                        matches = true;
                        break;
                    }
                }

                if (!matches || (rule.Use is null && rule.Body is null))
                {
                    continue;
                }

                DynamicContext useContext = context;
                useContext.Node = node;
                useContext.Position = 1;
                useContext.Size = 1;

                XPathValue value;

                if (rule.Use is not null)
                {
                    value = rule.Use.Evaluate(ref useContext);
                }
                else
                {
                    // A body's own local variables need a frame, exactly as a global's do. What it produces
                    // is kept as a sequence rather than built into a tree: the values are what the node is
                    // filed under, and a tree would file every node under one string.
                    useContext.Locals = rule.FrameSize == 0
                        ? Array.Empty<XPathValue>()
                        : new XPathValue[rule.FrameSize];

                    useContext.FrameBase = 0;
                    value = VariableInstruction.CaptureSequence(rule.Body, ref useContext, this);
                }

                // Filed under the identity of each value — or, for a composite key, of all of them at once,
                // which is why a composite key indexes a node under one entry however many values it read.
                // The identity is typed, so that a lookup finds a value by eq and not by spelling.
                List<string> identities = new();
                KeyDefinition.Identities(value, key.Composite, identities, rule.BackwardsCompatible);

                foreach (string identity in identities)
                {
                    Add(index, identity, node);
                }
            }
        }

        private static void Add(Dictionary<string, List<int>> index, string value, int node)
        {
            if (!index.TryGetValue(value, out List<int>? nodes))
            {
                nodes = new List<int>();
                index.Add(value, nodes);
            }

            // Nodes arrive in document order, so a node filed twice under one value — by two of its own
            // values that are equal, or by two declarations of the key — is filed once.
            if (nodes.Count == 0 || nodes[^1] != node)
            {
                nodes.Add(node);
            }
        }

        /// <summary>
        /// Finds a declared attribute set by name.
        /// </summary>
        /// <param name="name">The set's expanded name.</param>
        /// <returns>The set, or <see langword="null"/> if no such set is declared.</returns>
        internal AttributeSet? FindAttributeSet(ExpandedName name)
        {
            return m_stylesheet.AttributeSets.TryGetValue(name, out AttributeSet? set) ? set : null;
        }

        /// <summary>Writes an <c>xsl:message</c>.</summary>
        /// <param name="message">The message text.</param>
        public void WriteMessage(string message)
        {
            MessageWriter?.WriteLine(message);
        }

        /// <summary>
        /// Applies the best-matching template to a node, falling back to the built-in rule for its kind when
        /// no template matches.
        /// </summary>
        /// <param name="node">The node to process.</param>
        /// <param name="mode">The mode in force.</param>
        /// <param name="parameters">Parameters supplied by the call site.</param>
        /// <param name="context">The context, already positioned on <paramref name="node"/>.</param>
        internal void ApplyTemplates(
            int node,
            int mode,
            ParameterValue[] parameters,
            ref DynamicContext context)
        {
            if (m_hasTypedModes)
            {
                RequireTyped(node, mode, ref context);
            }

            TemplateIndex index = IndexFor(context.Tree);
            TemplateRule? rule = index.Find(node, mode, ref context, int.MaxValue);

            if (rule is TemplateRule chosen
                && m_stylesheet.ModeRules.TryGetValue(mode, out ModeDeclaration failing)
                && failing.FailOnMultipleMatch
                && index.HasRivalOfEqualRank(node, mode, ref context, chosen))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0540,
                    "More than one template rule of the same precedence and priority matches this node, and "
                    + "the mode says that is a failure rather than the later rule winning.");
            }

            if (rule is null)
            {
                ApplyBuiltInRule(node, mode, parameters, ref context);
                return;
            }

            InvokeRule(rule.Value, parameters, mode, ref context);
        }

        /// <summary>
        /// Applies the template that would have matched the current node if the running template's module, and
        /// everything importing it, did not exist — that is, <c>xsl:apply-imports</c>.
        /// </summary>
        /// <remarks>
        /// This is how a stylesheet overrides an imported template and still calls through to it, so the
        /// override can wrap rather than replace what it inherited.
        /// </remarks>
        /// <param name="context">The context, positioned on the node being processed.</param>
        /// <summary>
        /// Hands the node to the template that would have matched had the running one not existed, which is
        /// <c>xsl:next-match</c>.
        /// </summary>
        /// <param name="parameters">Parameters for the template found.</param>
        /// <param name="context">The context, positioned on the node.</param>
        private HashSet<string>? m_resultDocuments;

        /// <summary>Every document this transformation has read, by the URI it was read under.</summary>
        /// <remarks>
        /// So that writing to one can be refused. A transformation that reads a document and then writes to
        /// the same URI has no defined answer — whether the read saw what the write put there depends on
        /// the order the processor chose — and the specification refuses it rather than settling an order
        /// (XTDE1500).
        /// </remarks>
        private readonly HashSet<string> m_documentsRead = new(StringComparer.Ordinal);

        /// <summary>
        /// Finds where a secondary result document should be written.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two things are refused here. A stylesheet cannot write anywhere at all unless the caller supplied
        /// a resolver, and it cannot write the same document twice — the second write would either destroy
        /// the first or interleave with it, and neither is what the stylesheet meant.
        /// </para>
        /// <para>
        /// A stream resolver wins over a text one where both are supplied, being the only one through which
        /// the document's own <c>encoding</c> can reach its bytes.
        /// </para>
        /// </remarks>
        /// <param name="href">The destination the stylesheet asked for.</param>
        internal ResultDestination ResolveResultDocument(string href)
        {
            if (m_options.ResultStreamResolver is null && m_options.ResultResolver is null)
            {
                throw new XsltException(
                    $"This stylesheet uses xsl:result-document href=\"{href}\", but no result resolver was "
                    + "configured. Set XsltOptions.ResultStreamResolver, or XsltOptions.ResultResolver, to "
                    + "say where secondary results may go.");
            }

            m_resultDocuments ??= new HashSet<string>(StringComparer.Ordinal);

            // Resolved against the base output URI where there is one, so that two spellings of one place
            // are one document, and the resolver is told where the document goes rather than what the
            // stylesheet wrote.
            href = ResolveOutputUri(href);

            if (m_documentsRead.Contains(href))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE1500,
                    $"The document '{href}' was read by this transformation and is now being written to it. "
                    + "Whether the read saw what the write put there would depend on an order nothing "
                    + "settles, so the two together are refused.");
            }

            if (!m_resultDocuments.Add(href))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE1490,
                    $"The result document '{href}' was written more than once by one transformation, and a "
                    + "URI may carry one final result tree only.");
            }

            string? against = m_options.BaseOutputUri ?? m_options.BaseUri;

            return m_options.ResultStreamResolver is IXsltResultStreamResolver streams
                ? new ResultDestination(streams.Resolve(href, against))
                : new ResultDestination(m_options.ResultResolver!.Resolve(href, against));
        }

        /// <summary>
        /// Calls a function declared by <c>xsl:function</c> and returns its value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A function gets its own variable frame, as a template does, but the context node stays where the
        /// caller was: a function is called from inside an expression and has no node of its own to move to.
        /// </para>
        /// <para>
        /// Tunnel parameters do not cross into a function. A stylesheet function is meant to be a function of
        /// its arguments — that is what allows a call to be lifted out of a loop, evaluated once, or skipped —
        /// and letting a tunnelled value in through the side would make two calls with identical arguments
        /// return different answers. Templates it invokes therefore start from an empty set. The
        /// specification never settled this corner; see the note in ConformanceNotes.md.
        /// </para>
        /// </remarks>
        /// <param name="function">The function to call.</param>
        /// <param name="arguments">The argument values, already evaluated in the caller's context.</param>
        /// <param name="context">The caller's context.</param>
        /// <summary>
        /// What each deterministic function has already answered, by function and by argument values.
        /// </summary>
        /// <remarks>
        /// Per run rather than per stylesheet: a compiled stylesheet is safe to share between threads, and a
        /// cache on it would be state two transformations wrote to at once. It also holds nodes from the
        /// document being transformed, which the next run has no business seeing.
        /// </remarks>
        private readonly Dictionary<UserFunction, Dictionary<string, XPathValue>> m_functionResults = new();

        /// <summary>
        /// Reduces a call's arguments to a key, or answers that they cannot be one.
        /// </summary>
        /// <remarks>
        /// A node is keyed by identity — which tree and which node — because that is what makes two calls the
        /// same call for a function that navigates. An atomic value is keyed by its type and its string, so
        /// that 1 and "1" stay apart. A map, an array or a function item has no cheap identity of that sort,
        /// and rather than invent one this declines the cache and lets the call run: memoization is an
        /// optimization everywhere except for node identity, and a function taking a map is not the case
        /// where identity is being promised.
        /// </remarks>
        private bool TryKeyArguments(XPathValue[] arguments, out string key)
        {
            System.Text.StringBuilder built = new();

            foreach (XPathValue argument in arguments)
            {
                foreach (XPathValue item in XdmSequence.Items(argument))
                {
                    // A QName is keyed by the name it is and not by the name it is written with: two
                    // elements called x:alpha in different namespaces have one lexical form between them,
                    // and a cache keyed on that would answer the second call with the first one's result.
                    string? part = item.Kind switch
                    {
                        XPathValueKind.Node => $"n{GetTreeId(item.NodeTree)}:{item.NodeId}",
                        XPathValueKind.Map or XPathValueKind.Array or XPathValueKind.Function => null,
                        _ when item.TypeCode == XdmTypeCode.QName => Keyed(item.AsQName()),
                        _ => $"{(int)item.TypeCode}:{XdmSequence.StringValueOf(item)}",
                    };

                    if (part is null)
                    {
                        key = string.Empty;
                        return false;
                    }

                    built.Append(part.Length).Append(':').Append(part).Append(',');
                }

                // The end of one argument, so that f((1,2), ()) and f((1), (2)) are different calls.
                built.Append(';');
            }

            key = built.ToString();
            return true;
        }

        /// <summary>A QName written so that two names differ here exactly where the names differ.</summary>
        /// <param name="name">The name.</param>
        private static string Keyed(XdmQName name)
        {
            return "q" + name.NamespaceUri.Length + ":" + name.NamespaceUri + name.LocalName;
        }

        internal XPathValue InvokeFunction(
            UserFunction function,
            XPathValue[] arguments,
            ref DynamicContext context)
        {
            // An abstract declaration names a function without defining one, so the body is empty and
            // calling it would return nothing. Reaching it means no package supplied the function.
            if (function.IsAbstract)
            {
                throw AbstractComponent("function", function.Name.LocalName + "()");
            }

            // Before the stack guard, because answering from the cache is what stops the recursion growing
            // the stack at all — which is the difference between fib(92) finishing and not.
            Dictionary<string, XPathValue>? remembered = null;
            string memoKey = string.Empty;

            if (function.Deterministic && TryKeyArguments(arguments, out memoKey))
            {
                if (!m_functionResults.TryGetValue(function, out remembered))
                {
                    remembered = new Dictionary<string, XPathValue>(StringComparer.Ordinal);
                    m_functionResults.Add(function, remembered);
                }

                if (remembered.TryGetValue(memoKey, out XPathValue already))
                {
                    return already;
                }
            }

            // The same guard as InvokeTemplate, and needed here first: a function call goes through more stack
            // per level — the expression evaluating the call, the call itself, the body's expression — so the
            // stack runs out well before any depth counter notices.
            if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                throw new XsltException(
                    $"Calls nested too deeply while calling '{function.Name.LocalName}()'. A function with "
                    + "no terminating case will do this.");
            }

            if (++m_callDepth > MaximumCallDepth)
            {
                m_callDepth--;
                throw new XsltException(
                    $"Calls nested more than {MaximumCallDepth} deep while calling "
                    + $"'{function.Name.LocalName}()'. A function with no terminating case will do this.");
            }

            ParameterValue[] callerTunnel = m_tunnel;
            TemplateRule? callerRule = SuspendCurrentRule();
            string? callerSubstring = CurrentSubstring;
            string[]? callerGroups = RegexGroups;
            int callerMode = m_currentMode;
            XPathValue? callerAtomic = CurrentAtomicItem;
            m_tunnel = Array.Empty<ParameterValue>();

            // A function processes nothing, so current() in one is an error rather than the caller's item.
            CurrentAtomicItem = null;

            // A function is not called in any mode, so mode="#current" inside one is the unnamed mode
            // (§6.6.2, erratum XT.E19): the current mode is a template rule's, and the call left it behind.
            m_currentMode = CompiledStylesheet.DefaultMode;

            // A function called from inside an xsl:analyze-string branch is not processing that substring,
            // so current() there has nothing to read rather than the caller's piece of text, and
            // regex-group() in it is the empty string (§17.2).
            CurrentSubstring = null;
            RegexGroups = null;

            try
            {
                DynamicContext inner = context;

                // A stylesheet function has no focus of its own — the context item, position and size are
                // absent inside one. What a function sees has to come through its parameters, so that the
                // same arguments give the same answer wherever it was called from.
                inner.Node = DynamicContext.NotANode;
                inner.AtomicItem = default;
                inner.Position = 0;
                inner.Size = 0;

                // And no current node either, which is a separate thing: the focus is what '.' reads and the
                // current node is what current() reads, and a function that inherited the caller's would
                // give two calls with identical arguments different answers.
                inner.CurrentNode = DynamicContext.NotANode;

                // What the caller was promised, which is the first function's declared type whatever
                // function a chain of calls made in place of one another ends in.
                XdmSequenceType? promised = function.ResultType;
                XPathValue answer;

                while (true)
                {
                    inner.Locals = function.FrameSize == 0
                        ? Array.Empty<XPathValue>()
                        : new XPathValue[function.FrameSize];
                    inner.FrameBase = 0;

                    for (int i = 0; i < arguments.Length; i++)
                    {
                        // An argument the parameter's type cannot take: XSLT 2.0's own code for it, or XPath's
                        // from 3.0, which dropped the XSLT one. The processor's version decides, as it does
                        // for every code a later specification renamed.
                        inner.Locals[function.ParameterSlots[i]] = XdmTypeConversion.Apply(
                            arguments[i],
                            i < function.ParameterTypes.Length ? function.ParameterTypes[i] : null,
                            Implements30 ? XsltErrorCode.XPTY0004 : XsltErrorCode.XTTE0790);
                    }

                    // A body that is one xsl:sequence yields its value untouched, which is how a function
                    // returns something other than text. A declared return type also makes the body a
                    // sequence constructor rather than a tree, for the same reason it does on a variable.
                    XPathValue result = function.DirectResult is not null
                        ? function.DirectResult.Evaluate(ref inner)
                        : VariableInstruction.Evaluate(
                            null,
                            function.Body,
                            ref inner,
                            this,
                            function.ResultType ?? XdmSequenceType.AnyItem(XdmOccurrence.ZeroOrMore),
                            XsltErrorCode.XTTE0780,
                            function.BaseUri);

                    if (m_tailFunction is null)
                    {
                        answer = XdmTypeConversion.Apply(result, function.ResultType, XsltErrorCode.XTTE0780);
                        break;
                    }

                    // The body ended in a call whose answer is this call's answer, and handed it back rather
                    // than making it: what it returned is a placeholder. The call is made here, in this
                    // invocation's place, with the frame the body has finished with given up — which is what
                    // lets a function recurse for as long as it likes, provided that is the last thing it
                    // does. The stack guard above is not repeated because the stack does not grow.
                    function = m_tailFunction;
                    arguments = m_tailArguments!;
                    m_tailFunction = null;
                    m_tailArguments = null;

                    if (function.IsAbstract)
                    {
                        throw AbstractComponent("function", function.Name.LocalName + "()");
                    }

                    if (function.Deterministic
                        && TryKeyArguments(arguments, out string key)
                        && m_functionResults.TryGetValue(function, out Dictionary<string, XPathValue>? known)
                        && known.TryGetValue(key, out answer))
                    {
                        break;
                    }
                }

                // A chain that ended in a different function has had that function's promise checked, and
                // still owes the caller the first one's.
                if (!ReferenceEquals(promised, function.ResultType))
                {
                    answer = XdmTypeConversion.Apply(answer, promised, XsltErrorCode.XTTE0780);
                }

                // Recorded only on the way out, so a function that reaches itself does not record a partial
                // answer — and so that one which throws records nothing at all.
                remembered?.TryAdd(memoKey, answer);

                return answer;
            }
            finally
            {
                ResumeCurrentRule(callerRule);
                CurrentSubstring = callerSubstring;
                RegexGroups = callerGroups;
                m_currentMode = callerMode;
                CurrentAtomicItem = callerAtomic;
                m_tunnel = callerTunnel;
                m_callDepth--;
            }
        }

        /// <summary>
        /// Runs the rule an item-based search found, or the built-in rule where it found none.
        /// </summary>
        /// <remarks>
        /// The built-in rule for an item that is not a node is to write its string value, there being no
        /// children to recurse into. That is what running out of rules means here.
        /// </remarks>
        private void ContinueOverItem(
            TemplateRule? found,
            ParameterValue[] parameters,
            ref DynamicContext context)
        {
            if (found is null)
            {
                Output.WriteText(context.RequireContextItem("a built-in rule").ToStringValue());
                return;
            }

            InvokeRule(found.Value, parameters, m_currentMode, ref context);
        }

        internal void ApplyNextMatch(ParameterValue[] parameters, ref DynamicContext context)
        {
            RequireCurrentRule("xsl:next-match");
            RequireContextItemForRule("xsl:next-match", ref context);

            // The context item need not be a node: a predicate pattern matches an atomic value, so a rule
            // that matched one can contain an xsl:next-match, and the node-keyed search would ask the tree
            // for the kind of a node that is not there.
            if (context.Node < 0)
            {
                ContinueOverItem(
                    IndexFor(context.Tree).FindForItem(
                        context.RequireContextItem("xsl:next-match"),
                        m_currentMode,
                        ref context,
                        m_currentRule!.Value),
                    parameters,
                    ref context);

                return;
            }

            TemplateRule? rule = IndexFor(context.Tree)
                .FindAfter(context.Node, m_currentMode, ref context, m_currentRule!.Value);

            if (rule is null)
            {
                ApplyBuiltInRule(context.Node, m_currentMode, parameters, ref context);
                return;
            }

            InvokeRule(rule.Value, parameters, m_currentMode, ref context);
        }

        /// <summary>
        /// Refuses an instruction that continues the current template rule where there is none.
        /// </summary>
        /// <remarks>
        /// Both instructions mean "carry on down the list of rules that matched this node", and outside a
        /// rule there is no such list. A named template reached by <c>xsl:call-template</c> keeps the rule
        /// that called it — that is not this case — but one that is the transformation's entry point, or a
        /// body running inside <c>xsl:for-each</c>, <c>xsl:for-each-group</c> or a function, has none.
        /// </remarks>
        /// <param name="instruction">The instruction, named in the message.</param>
        /// <summary>
        /// Refuses <c>xsl:next-match</c> or <c>xsl:apply-imports</c> where there is no context item.
        /// </summary>
        /// <remarks>
        /// The same code as having no current template rule, and the specification names both in the one
        /// error for the same reason: each instruction means "carry on down the list of rules that matched
        /// <em>this</em>", and with nothing in focus there is no this. A named template declaring
        /// <c>xsl:context-item use="absent"</c> is how a stylesheet reaches it.
        /// </remarks>
        /// <param name="instruction">The instruction, named in the message.</param>
        private void RequireContextItemForRule(string instruction, ref DynamicContext context)
        {
            if (!context.HasContextItem)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0560,
                    $"{instruction} continues the search for a rule matching the context item, and there is "
                    + "no context item here.");
            }
        }

        private void RequireCurrentRule(string instruction)
        {
            if (m_currentRule is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0560,
                    $"{instruction} continues the template rule that matched the current node, and there is "
                    + "no current template rule here.");
            }
        }

        /// <summary>
        /// Puts the current template rule aside for the duration of a body that has none.
        /// </summary>
        /// <remarks>
        /// <c>xsl:for-each</c>, <c>xsl:for-each-group</c> and a stylesheet function all move the focus to
        /// something the rules did not choose, so the rule that was running does not follow them in. The
        /// caller restores what this returns.
        /// </remarks>
        internal TemplateRule? SuspendCurrentRule()
        {
            TemplateRule? current = m_currentRule;
            m_currentRule = null;
            return current;
        }

        /// <summary>Restores what <see cref="SuspendCurrentRule"/> put aside.</summary>
        /// <param name="rule">What it returned.</param>
        internal void ResumeCurrentRule(TemplateRule? rule) => m_currentRule = rule;

        internal void ApplyImports(ParameterValue[] parameters, ref DynamicContext context)
        {
            RequireCurrentRule("xsl:apply-imports");
            RequireContextItemForRule("xsl:apply-imports", ref context);

            if (context.Node < 0)
            {
                ContinueOverItem(
                    IndexFor(context.Tree).FindForItem(
                        context.RequireContextItem("xsl:apply-imports"),
                        m_currentMode,
                        ref context,
                        after: null,
                        m_currentPrecedence),
                    parameters,
                    ref context);

                return;
            }

            TemplateRule? rule = IndexFor(context.Tree).Find(
                context.Node, m_currentMode, ref context, m_currentPrecedence);

            if (rule is null)
            {
                ApplyBuiltInRule(context.Node, m_currentMode, parameters, ref context);
                return;
            }

            InvokeRule(rule.Value, parameters, m_currentMode, ref context);
        }

        /// <summary>Builds the error for reaching a component that was declared abstract.</summary>
        /// <remarks>
        /// A method of its own, and not for tidiness. Building the message inside
        /// <see cref="InvokeTemplate"/> put its interpolation locals in that method's frame, which every
        /// nested invocation then pays for — and the stack guard there measures the real stack, so a deeply
        /// recursive stylesheet that fitted before stopped fitting. The same shape as the note about
        /// <c>stackalloc</c> in the performance record: the cost belongs to the whole method, not to the
        /// branch it is written in.
        /// </remarks>
        /// <param name="kind">What sort of component it is, for the message.</param>
        /// <param name="name">What the component is called.</param>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static XsltException AbstractComponent(string kind, string name)
        {
            return XsltErrors.Error(
                XsltErrorCode.XTDE3052,
                $"The {kind} '{name}' is declared abstract, so it names a component without defining one, "
                + "and no package using this one has supplied it.");
        }

        /// <summary>Builds the error for reaching an abstract template.</summary>
        /// <remarks>
        /// Takes the template rather than its name for the reason above: working out the name is itself a
        /// local, and a local in <see cref="InvokeTemplate"/> is one every level of a recursion pays for.
        /// This is not theoretical — writing the name out at the call site cost a stylesheet recursing a
        /// thousand deep the room to finish.
        /// </remarks>
        /// <param name="template">The template that was reached.</param>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static XsltException AbstractTemplate(Template template)
        {
            return AbstractComponent("template", template.Name?.LocalName ?? "(matching)");
        }

        /// <summary>
        /// Runs a template, giving it a fresh variable frame and binding its parameters.
        /// </summary>
        /// <remarks>
        /// Each invocation allocates its own frame rather than carving one out of a shared stack. A shared
        /// stack would have to grow, and growing it would leave contexts already captured by outer frames
        /// pointing at the old array. The IL backend removes the question entirely by turning variables into
        /// real IL locals.
        /// </remarks>
        /// <param name="template">The template to run.</param>
        /// <param name="parameters">Parameters supplied by the call site.</param>
        /// <param name="context">The caller's context, which supplies the context node.</param>
        /// <param name="mode">
        /// The mode in force for the invocation, which is what <c>xsl:apply-imports</c>, <c>xsl:next-match</c>
        /// and <c>mode="#current"</c> read. It belongs to the invocation and not to the template: one template
        /// may serve several modes, and <c>xsl:call-template</c> changes the mode not at all.
        /// </param>
        /// <param name="asRule">
        /// Whether this invocation makes the template the current template rule. True where a pattern chose
        /// it and false where a name did: <c>xsl:call-template</c> leaves the current rule exactly as it
        /// found it, so a named helper called from a matched template can still say
        /// <c>xsl:apply-imports</c> and mean the rule that called it.
        /// </param>
        /// <summary>
        /// Runs a template rule the search chose, which becomes the current template rule for as long as it
        /// runs — and so what an <c>xsl:next-match</c> inside it carries on from.
        /// </summary>
        /// <param name="rule">The rule chosen: one pattern of one template in one mode.</param>
        /// <param name="parameters">The parameters to pass.</param>
        /// <param name="mode">The mode it was reached in.</param>
        /// <param name="context">The context, positioned on what matched.</param>
        internal void InvokeRule(
            TemplateRule rule, ParameterValue[] parameters, int mode, ref DynamicContext context)
        {
            InvokeTemplate(rule.Template, parameters, mode, ref context, rule);
        }

        internal void InvokeTemplate(
            Template template,
            ParameterValue[] parameters,
            int mode,
            ref DynamicContext context,
            TemplateRule? asRule = null)
        {
            // An abstract template names a component without defining one, so reaching it means no using
            // package supplied it. Its body is empty, so running it would quietly produce nothing.
            if (template.Visibility == Visibility.Abstract)
            {
                throw AbstractTemplate(template);
            }

            // The depth limit alone is not a guard. It was calibrated against a frame size that every feature
            // added here changes, and being wrong about it is not survivable: a stack overflow cannot be
            // caught and takes the process with it. Asking the runtime how much stack is left trips before
            // that happens and is not a guess about frame sizes.
            if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                throw new XsltException(
                    "Template invocations nested too deeply; the stylesheet is probably recursing without a "
                    + "terminating case.");
            }

            if (++m_callDepth > MaximumCallDepth)
            {
                m_callDepth--;
                throw new XsltException(
                    $"Template invocations nested more than {MaximumCallDepth} deep; the stylesheet is probably "
                    + "recursing without a terminating case.");
            }

            // xsl:apply-imports needs to know which module the running template came from, and which mode it
            // is running in, neither of which is otherwise recoverable once execution is under way.
            int callerPrecedence = m_currentPrecedence;
            int callerMode = m_currentMode;
            TemplateRule? callerRule = m_currentRule;
            ParameterValue[] callerTunnel = m_tunnel;
            m_currentMode = mode;
            m_tunnel = ExtendTunnel(parameters);

            if (asRule is not null)
            {
                m_currentPrecedence = template.ImportPrecedence;
                m_currentRule = asRule;
            }

            try
            {
                DynamicContext inner = context;

                while (true)
                {
                    inner.Locals = template.FrameSize == 0
                        ? Array.Empty<XPathValue>()
                        : new XPathValue[template.FrameSize];
                    inner.FrameBase = 0;

                    if (template.ContextItem.Says)
                    {
                        ApplyContextItemDeclaration(template, ref inner);
                    }

                    foreach (TemplateParameter parameter in template.Parameters)
                    {
                        // A tunnel parameter is matched against the tunnel set and an ordinary one against the
                        // call site. The two are separate namespaces, so one template may declare both under the
                        // same name and see two different values.
                        ParameterValue[] source = parameter.Tunnel ? m_tunnel : parameters;
                        int supplied = FindParameter(source, parameter.Name, parameter.Tunnel);

                        if (supplied < 0 && parameter.Required)
                        {
                            string kind = parameter.Tunnel ? "tunnel " : string.Empty;
                            string owner = template.Name is ExpandedName named
                                ? $"template '{named.LocalName}'"
                                : "the template that matched";

                            throw new XsltException(
                                $"No value was supplied for the required {kind}parameter "
                                + $"'{parameter.Name.LocalName}' of {owner}. A required parameter has no default "
                                + "to fall back on.");
                        }

                        // A supplied value was evaluated in the caller's context; a default is evaluated here, in
                        // the callee's, so that it can refer to parameters bound before it. Either way the
                        // declared type applies: what the caller passed is checked as much as what the default
                        // produced, and the code says which of the two was wrong.
                        inner.Locals[parameter.Slot] = supplied >= 0
                            ? XdmTypeConversion.Apply(source[supplied].Value, parameter.Type, XsltErrorCode.XTTE0590)
                            : VariableInstruction.Evaluate(
                                parameter.Select,
                                parameter.Body,
                                ref inner,
                                this,
                                parameter.Type,
                                DefaultValueCode(parameter.Select, parameter.Body),
                                parameter.BaseUri);
                    }

                    if (template.ResultType is XdmSequenceType wanted)
                    {
                        // A template declaring its result type produces a sequence, checked against the
                        // type — XTTE0505 — and written as a sequence is. A call the body ends in is made
                        // here, inside the capture, so that the whole of what the template produced is what
                        // the type is checked against.
                        //
                        // The capture stands for the output it replaces. Declaring a result type does not
                        // put the transformation into temporary output state — it is a check on what the
                        // body produced, not a variable to produce it into — so an xsl:result-document
                        // inside such a template is writing a document of the transformation's own, exactly
                        // as it would be without the type.
                        SequenceCaptureTarget capture = new SequenceCaptureTarget
                        {
                            StandsForFinalOutput = Output.IsFinalOutput,
                        };
                        OutputTarget previous = Output;
                        Output = capture;

                        try
                        {
                            Instruction.ExecuteAll(template.Body, ref inner, this);

                            while (m_tailTemplate is not null)
                            {
                                Template next = m_tailTemplate;
                                ParameterValue[] nextParameters = m_tailParameters;
                                m_tailTemplate = null;
                                m_tailParameters = Array.Empty<ParameterValue>();
                                InvokeTemplate(next, nextParameters, mode, ref inner);
                            }
                        }
                        finally
                        {
                            Output = previous;
                        }

                        SequenceWriter.Write(
                            XdmTypeConversion.Apply(capture.Finish(), wanted, XsltErrorCode.XTTE0505), this);
                    }
                    else
                    {
                        Instruction.ExecuteAll(template.Body, ref inner, this);
                    }

                    if (m_tailTemplate is null)
                    {
                        break;
                    }

                    // The body ended in an xsl:call-template, which handed the call back rather than making
                    // it. It is made here, in this invocation's place, with the frame the body has finished
                    // with given up — so a template that recurses as the last thing it does is a loop rather
                    // than a stack, for as many iterations as it likes. The context is the one the call was
                    // made in, which is this one: a body may copy the context but not disturb it. The mode
                    // and the current rule stay as they are, as they do for any call by name; the tunnel is
                    // extended from this invocation's, which is the one the call was made from. The stack
                    // guard above is not repeated because the stack does not grow.
                    template = m_tailTemplate;
                    parameters = m_tailParameters;
                    m_tailTemplate = null;
                    m_tailParameters = Array.Empty<ParameterValue>();

                    if (template.Visibility == Visibility.Abstract)
                    {
                        throw AbstractTemplate(template);
                    }

                    m_tunnel = ExtendTunnel(parameters);
                }
            }
            finally
            {
                m_currentPrecedence = callerPrecedence;
                m_currentMode = callerMode;
                m_currentRule = callerRule;
                m_tunnel = callerTunnel;
                m_callDepth--;
            }
        }

        /// <summary>
        /// Records an <c>xsl:call-template</c> that is the last thing the running template does, for the
        /// invocation running that template to make in its own place once the body has returned.
        /// </summary>
        /// <param name="target">The template called.</param>
        /// <param name="parameters">The parameters, already evaluated in the caller's frame.</param>
        internal void DeferTailCall(Template target, ParameterValue[] parameters)
        {
            m_tailTemplate = target;
            m_tailParameters = parameters;
        }

        /// <summary>
        /// Records a call that is the whole answer of the running function body, for the invocation running
        /// that body to make in its own place once the body has returned.
        /// </summary>
        /// <param name="function">The function called.</param>
        /// <param name="arguments">The arguments, already evaluated in the caller's frame.</param>
        internal void DeferTailCall(UserFunction function, XPathValue[] arguments)
        {
            m_tailFunction = function;
            m_tailArguments = arguments;
        }

        /// <summary>
        /// The code for a parameter whose default does not fit the type the parameter declared.
        /// </summary>
        /// <remarks>
        /// Two different complaints. A default that was <em>written</em> and does not fit the declared type
        /// is a type error in the stylesheet, <c>XTTE0600</c>. A parameter with no default at all has the
        /// empty sequence for one, and where the declared type does not admit that, the specification does
        /// not call the declaration wrong — it says the parameter is required after all, so what is wrong is
        /// the call that left it out. It is a dynamic error because whether it happens depends on the caller.
        /// <para>
        /// That second one is <c>XTDE0610</c> at 2.0 and <c>XTDE0700</c> at 3.0, which dropped the first code
        /// in favour of the one it already had for a required parameter nobody supplied — the two being the
        /// same complaint reached two ways. Which a caller hears follows the processor: the suite pairs these
        /// tests over one <c>version="2.0"</c> stylesheet and wants a code apiece.
        /// </para>
        /// </remarks>
        private XsltErrorCode DefaultValueCode(Expr? select, Instruction[]? body)
        {
            if (select is not null || body is { Length: > 0 })
            {
                return XsltErrorCode.XTTE0600;
            }

            return m_options.Version.CompareTo(XsltVersion.V30) >= 0
                ? XsltErrorCode.XTDE0700
                : XsltErrorCode.XTDE0610;
        }

        /// <summary>
        /// Works out the tunnel parameters a called template receives: the ones this template was given,
        /// overridden by any the call site supplied with <c>tunnel="yes"</c>.
        /// </summary>
        /// <remarks>
        /// Nothing is ever removed. A parameter enters the set where it is first passed and stays until the
        /// invocation that added it returns, which is what lets a template deep in a chain read a value that
        /// no template between it and the source ever mentioned.
        /// </remarks>
        /// <param name="parameters">Everything the call site supplied, tunnel and ordinary alike.</param>
        private ParameterValue[] ExtendTunnel(ParameterValue[] parameters)
        {
            int added = 0;
            foreach (ParameterValue parameter in parameters)
            {
                if (parameter.Tunnel)
                {
                    added++;
                }
            }

            if (added == 0)
            {
                // The ordinary case, and the one worth keeping free: pass on the set unchanged.
                return m_tunnel;
            }

            List<ParameterValue> extended = new List<ParameterValue>(m_tunnel.Length + added);
            extended.AddRange(m_tunnel);

            foreach (ParameterValue parameter in parameters)
            {
                if (!parameter.Tunnel)
                {
                    continue;
                }

                int existing = -1;
                for (int i = 0; i < extended.Count; i++)
                {
                    if (extended[i].Name.Equals(parameter.Name))
                    {
                        existing = i;
                        break;
                    }
                }

                if (existing >= 0)
                {
                    extended[existing] = parameter;
                }
                else
                {
                    extended.Add(parameter);
                }
            }

            return extended.ToArray();
        }

        private static int FindParameter(ParameterValue[] parameters, ExpandedName name, bool tunnel)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].Tunnel == tunnel && parameters[i].Name.Equals(name))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Applies templates to every child of a node, in document order.
        /// </summary>
        /// <remarks>
        /// The sibling chain is counted before it is walked so that <c>last()</c> is known, which avoids
        /// collecting the children into a list. Two passes over a linked chain cost far less than an
        /// allocation on a path taken once per element in the document.
        /// </remarks>
        /// <param name="node">The parent whose children are processed.</param>
        /// <param name="mode">The mode in force.</param>
        /// <param name="parameters">Parameters supplied by the call site.</param>
        /// <param name="context">The context supplying the tree.</param>
        internal void ApplyTemplatesToChildren(
            int node,
            int mode,
            ParameterValue[] parameters,
            ref DynamicContext context)
        {
            int size = 0;
            for (int child = context.Tree.FirstChildOf(node); child >= 0;
                child = context.Tree.NextSiblingOf(child))
            {
                size++;
            }

            int position = 0;
            for (int child = context.Tree.FirstChildOf(node); child >= 0;
                child = context.Tree.NextSiblingOf(child))
            {
                DynamicContext inner = context;
                inner.Node = child;
                inner.CurrentNode = child;
                inner.CurrentTree = inner.Tree;
                inner.Position = ++position;
                inner.Size = size;
                ApplyTemplates(child, mode, parameters, ref inner);
            }
        }

        /// <summary>
        /// Applies the built-in template rule for a node's kind: containers recurse into their children, text
        /// and attributes copy their value, and comments and processing instructions produce nothing.
        /// </summary>
        private void ApplyBuiltInRule(
            int node,
            int mode,
            ParameterValue[] parameters,
            ref DynamicContext context)
        {
            ModeDeclaration rules = m_stylesheet.ModeRules.TryGetValue(mode, out ModeDeclaration declared)
                ? declared
                : ModeDeclaration.Default;

            if (rules.WarnOnNoMatch)
            {
                m_options.MessageWriter?.WriteLine(
                    $"No template rule matched {Describe(context.Tree, node)} in this mode.");
            }

            if (rules.OnNoMatch != OnNoMatch.TextOnlyCopy)
            {
                ApplyDeclaredBuiltInRule(node, mode, parameters, rules.OnNoMatch, ref context);
                return;
            }

            switch (context.Tree.KindOf(node))
            {
                case NodeKind.Root:
                case NodeKind.Element:
                {
                    // The built-in rule is an xsl:apply-templates over the children, and like any other
                    // template it passes the tunnel parameters on. It has to take in the ones the call site
                    // supplied as well: nothing else will, because a node with no template of its own never
                    // reaches InvokeTemplate, and the value would be lost on the way past.
                    //
                    // The ordinary parameters travel with them. A node the stylesheet wrote no rule for is
                    // not a reason for a parameter to stop: what the caller meant was for the templates it
                    // eventually reaches to have it, and an intervening wrapper element is not something the
                    // stylesheet chose to put in the way.
                    ParameterValue[] callerTunnel = m_tunnel;
                    m_tunnel = ExtendTunnel(parameters);

                    try
                    {
                        ApplyTemplatesToChildren(node, mode, parameters, ref context);
                    }
                    finally
                    {
                        m_tunnel = callerTunnel;
                    }

                    return;
                }

                case NodeKind.Text:
                case NodeKind.Attribute:
                    Output.WriteText(context.Tree.StringValueOf(node));
                    return;

                default:
                    return;
            }
        }

        /// <summary>
        /// Applies one of the five built-in rule sets XSLT 3.0 added to the one that was always there.
        /// </summary>
        /// <remarks>
        /// The three that go on down do so through the attributes as well as the children, which
        /// <see cref="ApplyTemplatesToChildren"/> does not: an attribute is not a child, and a mode copying
        /// a document shallowly has to reach them or the copy loses them. The two that copy write the node
        /// themselves rather than going through <c>xsl:copy</c>, there being no sequence constructor here to
        /// run inside it.
        /// </remarks>
        private void ApplyDeclaredBuiltInRule(
            int node,
            int mode,
            ParameterValue[] parameters,
            OnNoMatch onNoMatch,
            ref DynamicContext context)
        {
            if (onNoMatch == OnNoMatch.Fail)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0555,
                    $"No template rule matched {Describe(context.Tree, node)}, and the mode it was reached "
                    + "in is declared on-no-match=\"fail\".");
            }

            // deep-skip leaves out the node and everything under it — except that the built-in rule for a
            // document node applies templates to its children whatever the mode says (§6.7.1), or nothing in
            // such a mode could ever be reached from the top.
            if (onNoMatch == OnNoMatch.DeepSkip && context.Tree.KindOf(node) != NodeKind.Root)
            {
                return;
            }

            if (onNoMatch == OnNoMatch.DeepCopy)
            {
                NodeCopier.CopyDeep(context.Tree, node, Output, runtime: this);
                return;
            }

            NodeKind kind = context.Tree.KindOf(node);
            bool container = kind is NodeKind.Root or NodeKind.Element;

            if (!container)
            {
                // Nothing to descend into, so the two shallow rules differ only in whether the node itself
                // is written — which for a leaf is the whole of what "shallow" could mean.
                if (onNoMatch == OnNoMatch.ShallowCopy)
                {
                    NodeCopier.CopyShallow(context.Tree, node, Output);
                }

                return;
            }

            bool copying = onNoMatch == OnNoMatch.ShallowCopy;
            bool element = kind == NodeKind.Element;

            if (copying && element)
            {
                XdmTree tree = context.Tree;
                int nameCode = tree.NameCodeOf(node);
                int fingerprint = tree.FingerprintOf(node);

                if (Output.OpenElementDepth == 0)
                {
                    Output.NoteSourceOfNext(tree, node, this, withAttributes: false);
                }

                Output.StartElement(
                    tree.NameTable.GetPrefix(nameCode),
                    tree.NameTable.GetNamespaceUri(fingerprint),
                    tree.NameTable.GetLocalName(fingerprint));

                Output.MarkOwnNamespaces(root: true);

                foreach ((string prefix, string uri) in tree.InScopeNamespacesOf(node))
                {
                    Output.WriteNamespaceDeclaration(prefix, uri);
                }
            }

            // A document node copied shallowly is a document node: where the copy lands in a sequence it
            // is one item, with the base URI of the document it copies, as xsl:copy would make it.
            bool document = copying && kind == NodeKind.Root;

            if (document)
            {
                if (Output.OpenElementDepth == 0)
                {
                    Output.NoteSourceOfNext(context.Tree, node, this, withAttributes: false);
                }

                Output.StartDocumentCopy();
                Output.NoteDocumentCopiedFrom(context.Tree);
            }

            ParameterValue[] callerTunnel = m_tunnel;
            m_tunnel = ExtendTunnel(parameters);

            try
            {
                ApplyTemplatesToAttributes(node, mode, parameters, ref context);
                ApplyTemplatesToChildren(node, mode, parameters, ref context);
            }
            finally
            {
                m_tunnel = callerTunnel;
            }

            if (copying && element)
            {
                Output.EndElement();
            }
            else if (document)
            {
                Output.EndDocumentCopy();
            }
        }

        /// <summary>
        /// Puts a template's <c>xsl:context-item</c> declaration into effect on the context its body runs in.
        /// </summary>
        /// <remarks>
        /// <c>use="absent"</c> takes the focus away rather than checking for it: what the template promises
        /// is that it does not read its surroundings, so the caller having a context item is not an error —
        /// the body simply cannot see it, and reading it is <c>XPDY0002</c> as it would be anywhere else.
        /// The other two are checks, and the codes say which of the two things went wrong: nothing where one
        /// was required, or something of the wrong type.
        /// </remarks>
        private static void ApplyContextItemDeclaration(Template template, ref DynamicContext inner)
        {
            ContextItemDeclaration declared = template.ContextItem;

            if (declared.Use == ContextItemUse.Absent)
            {
                inner.Node = DynamicContext.NotANode;
                inner.AtomicItem = default;
                return;
            }

            if (!inner.HasContextItem)
            {
                if (declared.Use == ContextItemUse.Required)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTTE3090,
                        $"The template '{template.Name?.LocalName ?? "(matched)"}' declares that it needs a "
                        + "context item, and it was invoked without one.");
                }

                return;
            }

            if (declared.Type is null)
            {
                return;
            }

            XPathValue item = inner.Node >= 0
                ? XPathValue.FromNode(inner.Tree, inner.Node)
                : inner.AtomicItem;

            // Matched rather than converted, which is the whole difference between this and an as on a
            // parameter. A context item is something the template was handed, not something it is asking to
            // have made for it: an element where xs:string was declared is the wrong item, and atomizing it
            // into one would answer a question the declaration did not ask.
            if (!declared.Type.Matches(item))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE0590,
                    $"The context item at a call of '{template.Name?.LocalName ?? "(matched)"}' does not "
                    + $"match the declared type {declared.Type}.");
            }
        }

        /// <summary>
        /// Puts <c>xsl:global-context-item</c> into effect on the context the globals are evaluated in.
        /// </summary>
        /// <remarks>
        /// The same three cases as a template's, one level up: a stylesheet may say it needs a source
        /// document, that it needs none, or what kind of document it needs. The codes differ from the
        /// template ones because a caller can act on these and cannot act on those — this is about what was
        /// handed to the transformation.
        /// </remarks>
        private void ApplyGlobalContextItem(ref DynamicContext context)
        {
            ContextItemDeclaration declared = m_stylesheet.GlobalContextItem;

            if (!declared.Says)
            {
                return;
            }

            if (declared.Use == ContextItemUse.Absent)
            {
                context.Node = DynamicContext.NotANode;
                context.AtomicItem = default;

                // Recorded as well as applied here: a global variable is evaluated in a context of its own,
                // built where it is forced, and what the stylesheet said about the context item has to reach
                // that one too. Without it a global could name the source document the stylesheet had just
                // declared it would not be given.
                m_globalContextAbsent = true;
                return;
            }

            if (!HasSourceDocument)
            {
                if (declared.Use == ContextItemUse.Required)
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE3086,
                        "The stylesheet declares that it needs a source document, and the transformation was "
                        + "started without one.");
                }

                return;
            }

            if (declared.Type is not null)
            {
                // XTTE0590 and not XTTE3086: the mismatch is between the type the stylesheet declared and
                // what the caller handed it, which is the caller's mistake and reported to the caller.
                XdmTypeConversion.Apply(
                    XPathValue.FromNode(InputTree, XdmTree.RootNode),
                    declared.Type,
                    XsltErrorCode.XTTE0590);
            }
        }

        /// <summary>
        /// Applies templates to a sequence of items, one at a time.
        /// </summary>
        /// <remarks>
        /// The path <c>xsl:apply-templates</c> takes where its selection is not all nodes, and the one the
        /// built-in rule for an array takes over that array's members. Sorting is left out rather than
        /// reimplemented: an <c>xsl:sort</c> over a mixed sequence is a combination the suite does not
        /// exercise and this engine has no way to test, so doing it by guesswork would be a claim rather
        /// than a feature.
        /// </remarks>
        /// <param name="items">The items, in the order they are to be processed.</param>
        /// <param name="mode">The mode in force.</param>
        /// <param name="parameters">Parameters supplied by the call site.</param>
        /// <param name="context">The context each item in turn becomes the focus of.</param>
        internal void ApplyTemplatesToSequence(
            List<XPathValue> items,
            int mode,
            ParameterValue[] parameters,
            ref DynamicContext context)
        {
            XPathValue? outerCurrent = CurrentAtomicItem;

            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    DynamicContext inner = context.WithItem(items[i]);
                    inner.Position = i + 1;
                    inner.Size = items.Count;

                    if (items[i].Kind == XPathValueKind.Node)
                    {
                        inner.CurrentNode = inner.Node;
                        inner.CurrentTree = inner.Tree;
                        CurrentAtomicItem = null;
                        ApplyTemplates(inner.Node, mode, parameters, ref inner);
                        continue;
                    }

                    // The atomic value is the current item, and the node that was is not.
                    inner.CurrentNode = DynamicContext.NotANode;
                    CurrentAtomicItem = items[i];
                    ApplyTemplatesToItem(items[i], mode, parameters, ref inner);
                }
            }
            finally
            {
                CurrentAtomicItem = outerCurrent;
            }
        }

        /// <summary>
        /// Applies templates to an item that is not a node.
        /// </summary>
        /// <remarks>
        /// XSLT 3.0 lets <c>xsl:apply-templates</c> be used over a sequence of anything, which only a
        /// predicate pattern can match. Where nothing matches, the built-in rule for an atomic value is to
        /// write it — the same answer the built-in rule gives a text node, and for the same reason: there is
        /// nothing else it could usefully do with a value it was handed.
        /// </remarks>
        internal void ApplyTemplatesToItem(
            XPathValue item,
            int mode,
            ParameterValue[] parameters,
            ref DynamicContext context)
        {
            TemplateIndex index = IndexFor(context.Tree);

            if (index.FindForItem(item, mode, ref context) is not TemplateRule rule)
            {
                // An array is the one item that is a container, and the built-in rule for a container is to
                // go into it: templates are applied to the members, which is what ?* gives, and the rule is
                // the same whatever the mode says about no match. So xsl:apply-templates over array{$data}
                // reaches the four elements in it rather than asking an array for a string value it has
                // not got.
                if (item.Kind == XPathValueKind.Array)
                {
                    List<XPathValue> members = new List<XPathValue>();

                    foreach (XPathValue member in item.AsArray().Members)
                    {
                        members.AddRange(XdmSequence.Items(member));
                    }

                    ApplyTemplatesToSequence(members, mode, parameters, ref context);
                    return;
                }

                // The built-in rule for an item that is not a node, by the mode's on-no-match: the item
                // itself under deep-copy and shallow-copy, its string value under text-only-copy, nothing
                // under either skip, and the error under fail.
                ModeDeclaration rules = m_stylesheet.ModeRules.TryGetValue(mode, out ModeDeclaration declared)
                    ? declared
                    : ModeDeclaration.Default;

                switch (rules.OnNoMatch)
                {
                    case OnNoMatch.Fail:
                        throw XsltErrors.Error(
                            XsltErrorCode.XTDE0555,
                            "No template rule matched an item that is not a node, and the mode it was "
                            + "reached in is declared on-no-match=\"fail\".");

                    case OnNoMatch.DeepSkip:
                    case OnNoMatch.ShallowSkip:
                        return;

                    case OnNoMatch.DeepCopy:
                    case OnNoMatch.ShallowCopy:
                        SequenceWriter.Write(item, this);
                        return;

                    default:
                        Output.WriteText(item.ToStringValue());
                        return;
                }
            }

            if (m_stylesheet.ModeRules.TryGetValue(mode, out ModeDeclaration failing)
                && failing.FailOnMultipleMatch
                && index.HasRivalOfEqualRankForItem(item, mode, ref context, rule))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0540,
                    "More than one template rule of the same precedence and priority matches this item, and "
                    + "the mode says that is a failure rather than the later rule winning.");
            }

            InvokeRule(rule, parameters, mode, ref context);
        }

        /// <summary>Applies templates to a node's attributes, which are not among its children.</summary>
        private void ApplyTemplatesToAttributes(
            int node,
            int mode,
            ParameterValue[] parameters,
            ref DynamicContext context)
        {
            if (context.Tree.KindOf(node) != NodeKind.Element)
            {
                return;
            }

            int count = context.Tree.AttributeCountOf(node);

            for (int i = 0; i < count; i++)
            {
                int attribute = context.Tree.AttributeAt(node, i);

                DynamicContext inner = context;
                inner.Node = attribute;
                inner.CurrentNode = attribute;
                inner.CurrentTree = inner.Tree;
                inner.Position = i + 1;
                inner.Size = count;
                ApplyTemplates(attribute, mode, parameters, ref inner);
            }
        }

        /// <summary>Names a node in a message, by kind and by name where it has one.</summary>
        private static string Describe(XdmTree tree, int node)
        {
            NodeKind kind = tree.KindOf(node);

            return kind switch
            {
                NodeKind.Root => "the document node",
                NodeKind.Element => $"the element '{NameIn(tree, node)}'",
                NodeKind.Attribute => $"the attribute '{NameIn(tree, node)}'",
                NodeKind.Namespace => $"the namespace node '{NameIn(tree, node)}'",
                NodeKind.Text => "a text node",
                NodeKind.Comment => "a comment",
                _ => "a processing instruction",
            };
        }

        /// <summary>The local name of a named node, which is enough to recognise it in a message.</summary>
        private static string NameIn(XdmTree tree, int node)
        {
            return tree.NameTable.GetLocalName(tree.FingerprintOf(node));
        }

        /// <summary>
        /// Runs the transformation, from wherever the caller said to start.
        /// </summary>
        /// <remarks>
        /// <para>
        /// XSLT has two ways in and this decides between them. Ordinarily the root of the input tree is
        /// processed and the patterns decide what happens; where the caller named an
        /// <see cref="XsltOptions.InitialTemplate"/>, that template is called instead. The second is what a
        /// stylesheet that generates rather than transforms needs: there is no input to match against, so
        /// there is nothing for a pattern to be about.
        /// </para>
        /// <para>
        /// A transformation started at a template may still have a source document, and then that document
        /// is the context item — a caller supplying one has supplied something for the template to read.
        /// Without one there is no context item at all, which is not the same as an empty one: reading it is
        /// then an error, and a good many stylesheets are written to rely on that.
        /// </para>
        /// </remarks>
        internal void Run()
        {
            DynamicContext context = new DynamicContext(
                InputTree,
                HasSourceDocument ? XdmTree.RootNode : DynamicContext.NotANode,
                GetFingerprintMap(InputTree))
            {
                Globals = m_globals,
                Runtime = this,
            };

            // A source document the transformation starts on by applying templates is made available by
            // the mode it starts in, so that mode's xsl:mode says which accumulators apply — and a mode
            // nobody declared has said nothing, which is none. Started at a named template instead, the
            // document is merely the global context item, handed over by the caller with nothing said about
            // it at all, and then every accumulator applies as it does to anything doc() reads. Before the
            // globals, because a global may be the first thing to ask.
            if (HasSourceDocument && m_options.InitialTemplate is null)
            {
                MakeAvailable(
                    InputTree,
                    m_stylesheet.ModeAccumulators.TryGetValue(StartingMode(), out AccumulatorSet? applicable)
                        ? applicable
                        : AccumulatorSet.None);
            }

            // Checked before the globals are forced, because what the globals are allowed to read is exactly
            // what this settles: a stylesheet declaring use="absent" is one whose globals cannot name the
            // source document, and one declaring a type is entitled to be told before it starts.
            ApplyGlobalContextItem(ref context);

            // Everything the caller supplied is installed before a single default is evaluated. A global
            // declared above a parameter may still refer to it, and evaluating in declaration order would
            // reach the parameter through that reference and take its default instead.
            BindSuppliedParameters();

            // Forcing the globals up front keeps the on-demand check off the hot path once execution starts.
            foreach (GlobalVariable global in m_stylesheet.Globals)
            {
                // An abstract declaration names a variable without defining one, so there is nothing here to
                // compute. Forcing it anyway made a package that declares an abstract component fail before
                // it started, whether or not anything ever reached the component.
                if (m_globalState[global.Slot] == GlobalReady || global.IsAbstract)
                {
                    continue;
                }

                if (global.Required)
                {
                    throw new XsltException(
                        $"No value was supplied for the required stylesheet parameter "
                        + $"'{global.Name.LocalName}'. A required parameter has no default to fall back on.");
                }

                try
                {
                    EnsureGlobal(global.Slot, ref context);
                }
                catch (XsltException error) when (error.Code == nameof(XsltErrorCode.XTDE3052))
                {
                    // Forcing up front is an optimisation, and an optimisation may not decide whether a
                    // transformation fails. This global reads an abstract component, so it cannot be
                    // computed — but nothing has asked for it yet, and a package is entitled to leave a
                    // component unsupplied as long as nothing reaches it. Put the slot back the way it was
                    // and let the demand, if it ever comes, raise the error.
                    m_globalState[global.Slot] = GlobalPending;
                }
            }

            // XSLT 2.0 let a transformation begin at a named template or in a named mode and not at both:
            // the two are different ways in and it settled no order between them. 3.0 dropped the error,
            // a mode being useful there for what the template's own xsl:apply-templates does.
            if (m_options.InitialTemplate is not null
                && m_options.InitialMode is not null
                && m_options.Version.CompareTo(XsltVersion.V30) < 0)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0047,
                    "The transformation was told to start at a named template and in a named mode, and "
                    + "XSLT 2.0 takes one way in or the other.");
            }

            if (m_options.InitialTemplate is string named)
            {
                ExpandedName name = StylesheetParameters.ParseName(named);

                Template template = m_stylesheet.NamedTemplates.TryGetValue(name, out Template? found)
                    ? found
                    : throw XsltErrors.Error(
                        XsltErrorCode.XTDE0040,
                        $"The transformation was told to start at a template named '{named}', and the "
                        + "stylesheet declares no template of that name.");

                // A package's private template is not a way into it. The caller is outside the package by
                // definition, so what it may start at is what the package said it offers.
                if (template.Visibility is not (Visibility.Public or Visibility.Final))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE0040,
                        $"The template named '{named}' is not public, so it is not a way into this package. "
                        + "Declare it visibility=\"public\" to make it an entry point.");
                }

                RequireInitialParameters(template);
                InvokeTemplate(template, StartingParameters, StartingMode(), ref context);
                CheckPrincipalResult();
                return;
            }

            // A selection handed over as a value, which is what fn:transform() has: its caller wrote the
            // items rather than an expression, and there is nothing here to parse them from.
            if (InitialSelection is XPath.XPathValue chosen)
            {
                ApplyToItems(XdmSequence.Items(chosen), StartingMode(), ref context);
                CheckPrincipalResult();
                return;
            }

            if (m_options.InitialMatchSelection is string selection)
            {
                ApplyToInitialSelection(selection, ref context);
                CheckPrincipalResult();
                return;
            }

            if (!HasSourceDocument)
            {
                // Naming a mode is asking for templates to be applied in it, and with no source document
                // there is nothing to apply them to. A mode nobody declares is refused first, as its own
                // error, which is what asking for the starting mode does.
                if (m_options.InitialMode is not null)
                {
                    StartingMode();

                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE0044,
                        "The transformation was told to start in a mode and given no source document to "
                        + "apply templates to in it.");
                }

                // XSLT 3.0's default entry point. With nothing to apply templates to and no template named
                // by the caller, a stylesheet may still say where to begin by declaring one called
                // xsl:initial-template — which is how a stylesheet that generates rather than transforms is
                // written, and it needs no arrangement between the stylesheet and whoever runs it.
                if (!m_stylesheet.NamedTemplates.TryGetValue(InitialTemplateName, out Template? start))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE0040,
                        "A transformation with no source document has nothing to apply templates to. Name "
                        + "a template to start at through XsltOptions.InitialTemplate, or declare one "
                        + "called xsl:initial-template for the transformation to begin at.");
                }

                // The same rule as for a template the caller named: a package's private template is not a
                // way into it, however it is called.
                if (start.Visibility is not (Visibility.Public or Visibility.Final))
                {
                    throw XsltErrors.Error(
                        XsltErrorCode.XTDE0040,
                        "The template named xsl:initial-template is not public, so it is not a way into this "
                        + "package. Declare it visibility=\"public\" to make it the entry point.");
                }

                RequireInitialParameters(start);
                InvokeTemplate(start, StartingParameters, StartingMode(), ref context);
                CheckPrincipalResult();
                return;
            }

            ApplyTemplates(XdmTree.RootNode, StartingMode(), StartingParameters, ref context);
            CheckPrincipalResult();
        }

        /// <summary>What the entry point is called with: what the caller supplied, or nothing.</summary>
        private ParameterValue[] StartingParameters =>
            InitialParameters ?? (m_startingParameters ??= SuppliedTemplateParameters());

        /// <summary>
        /// Refuses an entry point whose required parameters nothing supplied.
        /// </summary>
        /// <remarks>
        /// The specification gives this its own code because it is the caller's mistake rather than the
        /// stylesheet's: a template reached from inside the stylesheet is checked where the call is written,
        /// and one the transformation starts at has no call to check. XSLT 2.0 has no way for a caller to
        /// supply a template parameter at all, so there every required parameter on the way in is this
        /// error — and 3.0, which gave the caller a way, renamed the code from <c>XTDE0060</c> to
        /// <c>XTDE0700</c>. The processor's version decides which is reported, as it does for every code a
        /// later specification renamed.
        /// </remarks>
        /// <param name="template">The template the transformation is about to start at.</param>
        private void RequireInitialParameters(Template template)
        {
            ParameterValue[] supplied = StartingParameters;

            foreach (TemplateParameter parameter in template.Parameters)
            {
                if (!parameter.Required || FindParameter(supplied, parameter.Name, parameter.Tunnel) >= 0)
                {
                    continue;
                }

                throw XsltErrors.Error(
                    Implements30 ? XsltErrorCode.XTDE0700 : XsltErrorCode.XTDE0060,
                    $"The transformation starts at a template that declares '{parameter.Name.LocalName}' "
                    + "required, and no value for it was supplied.");
            }
        }

        private ParameterValue[]? m_startingParameters;

        /// <summary>
        /// The parameters the caller supplied for the way in, tunnel ones among them.
        /// </summary>
        /// <remarks>
        /// A transformation started at a named template is a call, and XSLT 3.0 §2.3 lets the caller supply
        /// its arguments. They are named and converted exactly as a stylesheet parameter is, by the same
        /// rules and under the same {uri}local names, because they arrive from the same place and the
        /// caller's prefixes are no more the stylesheet's here than there.
        /// <para>
        /// XSLT 2.0 had no way to supply them, so a 2.0 processor has nowhere to put what the caller
        /// offered and passes it over — and a template parameter declared <c>required="yes"</c> is then
        /// unsupplied, which is the error 2.0 names for exactly this (<c>XTDE0060</c>). The suite holds both
        /// halves: initial-template-002 supplies two parameters and reads them back, and 002a is the same
        /// invocation declared XSLT20 and expecting the refusal.
        /// </para>
        /// </remarks>
        private ParameterValue[] SuppliedTemplateParameters()
        {
            if (!Implements30
                || (m_options.TemplateParameters is not { Count: > 0 }
                    && m_options.TunnelParameters is not { Count: > 0 }))
            {
                return Array.Empty<ParameterValue>();
            }

            List<ParameterValue> supplied = new List<ParameterValue>();

            foreach ((IReadOnlyDictionary<string, object?>? given, bool tunnel) in
                new[] { (m_options.TemplateParameters, false), (m_options.TunnelParameters, true) })
            {
                foreach (KeyValuePair<string, object?> entry in
                    given ?? (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(0))
                {
                    supplied.Add(new ParameterValue(
                        StylesheetParameters.ParseName(entry.Key),
                        StylesheetParameters.Convert(entry.Key, entry.Value),
                        tunnel));
                }
            }

            return supplied.ToArray();
        }

        /// <summary>Whether any mode is declared typed, which is the only case the check below can fail.</summary>
        private readonly bool m_hasTypedModes;

        /// <summary>
        /// Refuses an element or attribute in a mode declared <c>typed="yes"</c>: this engine reads no schema,
        /// so every node it holds is untyped, and a typed mode takes none of them (§6.6.2).
        /// </summary>
        private void RequireTyped(int node, int mode, ref DynamicContext context)
        {
            if (m_stylesheet.ModeRules.TryGetValue(mode, out ModeDeclaration rules)
                && rules.Typed
                && context.Tree.KindOf(node) is NodeKind.Element or NodeKind.Attribute)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTTE3100,
                    $"Templates were applied to {Describe(context.Tree, node)} in a mode declared typed, and "
                    + "the node is untyped — as every node is here, this engine validating nothing.");
            }
        }

        /// <summary>
        /// Applies templates, in the starting mode, to the items the caller's initial match selection
        /// evaluates to (§2.3.4).
        /// </summary>
        /// <remarks>
        /// The expression is compiled against the principal module — its namespaces, functions and decimal
        /// formats — and evaluated with the source document as the context item where there is one. Each
        /// item is then processed as <c>xsl:apply-templates</c> would process it, with its position among
        /// them: a node by the template rules, an atomic value by the rules 3.0 lets match one.
        /// </remarks>
        private void ApplyToInitialSelection(string selection, ref DynamicContext context)
        {
            int mode = StartingMode();

            DynamicStaticContext scope = new DynamicStaticContext(
                m_stylesheet.Names,
                m_stylesheet.Version,
                new Dictionary<string, string>(m_stylesheet.PrincipalNamespaces),
                string.Empty,
                m_stylesheet.BaseUri,
                new List<ExpandedName>(),
                m_stylesheet.Functions,
                m_stylesheet.DecimalFormats,

                // The code point collation, and not whatever the stylesheet put in scope: this expression
                // was written by the caller rather than in the stylesheet, so there is no point in it for a
                // default-collation to have been declared at.
                Collation.CodepointUri);

            Expr compiled;

            try
            {
                compiled = XPathParser.Parse(selection, scope);
            }
            catch (XsltException failed) when (failed.Code is null)
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XPST0003,
                    $"The initial match selection '{selection}' is not an expression: {failed.Message}",
                    failed);
            }

            // Compiling may have given a name its first slot, which the mapping the context carries does
            // not reach; the runtime rebuilds its copy when it is behind.
            context.FingerprintMap = GetFingerprintMap(context.Tree);

            ApplyToItems(XdmSequence.Items(compiled.Evaluate(ref context)), mode, ref context);
        }

        /// <summary>
        /// Applies templates to each item of a selection in turn, each with its position among them.
        /// </summary>
        /// <param name="items">The items the transformation begins on.</param>
        /// <param name="mode">The mode to apply templates in.</param>
        /// <param name="context">The context to start from.</param>
        private void ApplyToItems(List<XPathValue> items, int mode, ref DynamicContext context)
        {
            for (int i = 0; i < items.Count; i++)
            {
                DynamicContext inner = context.WithItem(items[i]);
                inner.Position = i + 1;
                inner.Size = items.Count;

                if (items[i].Kind == XPathValueKind.Node)
                {
                    inner.CurrentNode = inner.Node;
                    inner.CurrentTree = inner.Tree;
                    ApplyTemplates(inner.Node, mode, StartingParameters, ref inner);
                    continue;
                }

                inner.CurrentNode = DynamicContext.NotANode;
                CurrentAtomicItem = items[i];
                ApplyTemplatesToItem(items[i], mode, StartingParameters, ref inner);
                CurrentAtomicItem = null;
            }
        }

        /// <summary>The name XSLT 3.0 reserves for the template a transformation begins at by default.</summary>
        private static readonly ExpandedName InitialTemplateName =
            new ExpandedName(StylesheetCompiler.XsltNamespace, "initial-template");

        /// <summary>The mode the transformation starts in, which is the unnamed one unless the caller said.</summary>
        private int StartingMode()
        {
            if (m_options.InitialMode is not string named)
            {
                return m_stylesheet.InitialMode;
            }

            // #unnamed is the mode a template with no mode attribute belongs to, and #default the package's
            // default mode — that same one, unless the xsl:package said default-mode. Naming either has to
            // mean the same as not naming one, or a caller could not ask for it.
            if (named == "#default")
            {
                return m_stylesheet.InitialMode;
            }

            if (named == "#unnamed")
            {
                return CompiledStylesheet.DefaultMode;
            }

            ExpandedName name = StylesheetParameters.ParseName(named);

            if (!m_stylesheet.Modes.TryGetValue(name, out int mode))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0045,
                    $"The transformation was told to start in a mode named '{named}', and no template "
                    + "declares that mode.");
            }

            // Not every mode is a way in (§2.3.4): the unnamed mode and the package's default mode always
            // are, and otherwise a mode has to be public or final — or, where the package declares no modes,
            // named by one of its template rules.
            if (mode != m_stylesheet.InitialMode && !m_stylesheet.EligibleInitialModes.Contains(mode))
            {
                throw XsltErrors.Error(
                    XsltErrorCode.XTDE0045,
                    $"The transformation was told to start in the mode '{named}', which is not a way into "
                    + "this stylesheet: a transformation may start in the unnamed mode, in the package's "
                    + "default mode, in a mode declared public or final, or — where the package declares no "
                    + "modes — in one its template rules name.");
            }

            return mode;
        }
    }
}
