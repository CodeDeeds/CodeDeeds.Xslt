using CodeDeeds.Xslt.Model;
using CodeDeeds.Xslt.Runtime;
using CodeDeeds.Xslt.XPath;

namespace CodeDeeds.Xslt.Compiler
{
    /// <summary>A global <c>xsl:variable</c> or <c>xsl:param</c>, declared at the top level of a stylesheet.</summary>
    internal sealed class GlobalVariable
    {
        /// <summary>Initializes a global variable.</summary>
        public GlobalVariable(
            ExpandedName name,
            int slot,
            Expr? select,
            Instruction[]? body,
            bool isParameter = false,
            bool required = false,
            XdmSequenceType? type = null)
        {
            Name = name;
            Slot = slot;
            Select = select;
            Body = body;
            IsParameter = isParameter;
            Required = required;
            Type = type;
        }

        /// <summary>The variable's expanded name.</summary>
        public ExpandedName Name { get; }

        /// <summary>The slot in global storage that holds the value.</summary>
        public int Slot { get; }

        /// <summary>
        /// Whether the declaration names the variable without defining it, leaving that to a using package.
        /// </summary>
        /// <remarks>
        /// An abstract component has no value to compute, so it is neither forced with the others nor
        /// readable: reaching one means no package supplied it, and the specification calls that
        /// <c>XTDE3052</c> rather than letting the absence show up as an empty sequence failing a type.
        /// </remarks>
        public bool IsAbstract { get; init; }

        /// <summary>
        /// Whether the variable belongs to a library package, whose globals have no context item: the
        /// global context item is the top-level package's (§2.3.2).
        /// </summary>
        public bool InLibrary { get; init; }

        /// <summary>The base URI of the declaration, which a tree built from its content takes (§9.4).</summary>
        public string? BaseUri { get; init; }

        /// <summary>
        /// How many slots the body's own local variables need.
        /// </summary>
        /// <remarks>
        /// A global's body is a sequence constructor like any other, so it may declare local variables — and
        /// those need a frame to live in, exactly as a template's do. Leaving it at zero and building the
        /// body anyway is what made a local variable inside a global crash rather than run.
        /// </remarks>
        public int FrameSize { get; set; }

        /// <summary>The value expression, if declared with a <c>select</c>.</summary>
        public Expr? Select { get; }

        /// <summary>The value content, if declared with a body.</summary>
        public Instruction[]? Body { get; }

        /// <summary>
        /// Whether this is an <c>xsl:param</c> rather than an <c>xsl:variable</c>, and so something the
        /// caller may supply a value for.
        /// </summary>
        public bool IsParameter { get; }

        /// <summary>
        /// Whether this is an <c>xsl:param required="yes"</c>, which the caller must supply a value for.
        /// </summary>
        public bool Required { get; }

        /// <summary>
        /// The declared type, if the declaration had an <c>as</c> attribute.
        /// </summary>
        /// <remarks>
        /// It settles what the content amounts to as well as checking it. Without one, everything a
        /// sequence constructor produces is built into a single document node; with one, the result is a
        /// sequence — which is the difference between a global whose value is the text <c>1 2 3</c> and one
        /// whose value is three integers.
        /// </remarks>
        public XdmSequenceType? Type { get; }
    }

    /// <summary>
    /// A stylesheet after compilation: everything needed to run a transformation, and nothing that changes
    /// while one runs.
    /// </summary>
    /// <remarks>
    /// Immutable, and therefore safe to share between concurrent transformations. All per-transformation state
    /// — the output target, variable storage, and the name index resolved against the input tree — belongs to
    /// <see cref="XsltRuntime"/> instead.
    /// </remarks>
    internal sealed class CompiledStylesheet
    {
        /// <summary>The mode identifier used by templates that declare no mode.</summary>
        public const int DefaultMode = -1;

        /// <summary>
        /// Stands for <c>mode="#current"</c> on <c>xsl:apply-templates</c>: not a mode of its own, but a
        /// request for whichever one is in force when the instruction runs.
        /// </summary>
        public const int CurrentMode = -2;

        /// <summary>Initializes a compiled stylesheet.</summary>
        public CompiledStylesheet(
            NameSlotTable names,
            IReadOnlyList<TemplateRule> rules,
            IReadOnlyList<GlobalVariable> globals,
            int globalSlotCount,
            OutputMethod outputMethod,
            IReadOnlyList<KeyDefinition> keys,
            OutputSettings outputSettings,
            WhitespaceControl whitespace,
            IReadOnlyDictionary<ExpandedName, AttributeSet> attributeSets,
            IReadOnlyDictionary<ExpandedName, Template> namedTemplates,
            IReadOnlyDictionary<ExpandedName, int> modes)
        {
            AttributeSets = attributeSets;
            Names = names;
            Rules = rules;
            Globals = globals;
            GlobalSlotCount = globalSlotCount;
            OutputMethod = outputMethod;
            Keys = keys;
            OutputSettings = outputSettings;
            Whitespace = whitespace;
            NamedTemplates = namedTemplates;
            Modes = modes;
        }

        /// <summary>The templates that have a name, by which a caller may start the transformation at one.</summary>
        public IReadOnlyDictionary<ExpandedName, Template> NamedTemplates { get; }

        /// <summary>The modes the stylesheet declares, by the index its rules are recorded under.</summary>
        public IReadOnlyDictionary<ExpandedName, int> Modes { get; }

        /// <summary>
        /// What <c>xsl:mode</c> said about each mode, by index.
        /// </summary>
        /// <remarks>
        /// Only the modes a stylesheet declared something about are in here. A mode exists as soon as a
        /// template rule names it, so absence means <see cref="ModeDeclaration.Default"/> rather than
        /// meaning there is no such mode.
        /// </remarks>
        public IReadOnlyDictionary<int, ModeDeclaration> ModeRules { get; init; }
            = new Dictionary<int, ModeDeclaration>();

        /// <summary>
        /// Which accumulators apply to the document a transformation begins on, by the mode it begins in.
        /// </summary>
        /// <remarks>
        /// Only the modes whose <c>use-accumulators</c> narrowed it; a mode saying nothing means every
        /// accumulator, which is what a missing entry means here.
        /// </remarks>
        public IReadOnlyDictionary<int, AccumulatorSet> ModeAccumulators { get; init; }
            = new Dictionary<int, AccumulatorSet>();

        /// <summary>What <c>xsl:global-context-item</c> says the stylesheet is meant to be run against.</summary>
        public ContextItemDeclaration GlobalContextItem { get; init; } = ContextItemDeclaration.Default;

        /// <summary>
        /// The mode the transformation starts in where the caller names none.
        /// </summary>
        /// <remarks>
        /// The unnamed mode, unless XSLT 3.0's <c>default-mode</c> on the outermost element says otherwise.
        /// A stylesheet that writes every rule in a named mode would otherwise be unable to start: the rule
        /// for the root is in that mode too, so the unnamed mode reaches nothing and the built-in rules run
        /// the whole document into text.
        /// </remarks>
        public int InitialMode { get; init; } = DefaultMode;

        /// <summary>
        /// The named modes a transformation may start in besides the unnamed one and <see cref="InitialMode"/>
        /// (§2.3.4): declared public or final in the top-level package, accepted into it as such, or — where
        /// the package declares no modes — named by one of its template rules.
        /// </summary>
        public IReadOnlySet<int> EligibleInitialModes { get; init; } = new HashSet<int>();

        /// <summary>The stylesheet's functions, which an initial match selection may call.</summary>
        internal IReadOnlyDictionary<(ExpandedName Name, int Arity), UserFunction> Functions { get; init; }
            = new Dictionary<(ExpandedName Name, int Arity), UserFunction>();

        /// <summary>The top-level package's decimal formats, for an initial match selection.</summary>
        internal IReadOnlyDictionary<ExpandedName, DecimalFormat> DecimalFormats { get; init; }
            = new Dictionary<ExpandedName, DecimalFormat>();

        /// <summary>The namespaces in scope on the principal module's outermost element.</summary>
        internal IReadOnlyDictionary<string, string> PrincipalNamespaces { get; init; }
            = new Dictionary<string, string>();

        /// <summary>The version the principal module claims.</summary>
        internal XsltVersion Version { get; init; }

        /// <summary>The accumulators declared, indexed by AccumulatorDefinition.Index.</summary>
        public IReadOnlyList<AccumulatorDefinition> Accumulators { get; init; }
            = Array.Empty<AccumulatorDefinition>();

        /// <summary>The attribute sets declared by <c>xsl:attribute-set</c>, by expanded name.</summary>
        public IReadOnlyDictionary<ExpandedName, AttributeSet> AttributeSets { get; }

        /// <summary>The keys declared by <c>xsl:key</c>, indexed by <see cref="KeyDefinition.Index"/>.</summary>
        public IReadOnlyList<KeyDefinition> Keys { get; }

        /// <summary>The serialization options requested by <c>xsl:output</c>.</summary>
        public OutputSettings OutputSettings { get; }

        /// <summary>
        /// Which source whitespace the top-level package strips, per <c>xsl:strip-space</c>.
        /// </summary>
        /// <remarks>
        /// The one that strips the source document, the top-level package's declarations being the only
        /// ones that reach it. What a document read by <c>doc()</c> or <c>document()</c> is stripped by
        /// depends on where the call was written: see <see cref="WhitespaceIn"/>.
        /// </remarks>
        public WhitespaceControl Whitespace { get; }

        /// <summary>What each package strips, for the documents read by calls written in it.</summary>
        public IReadOnlyDictionary<int, WhitespaceControl> PackageWhitespace { get; init; } =
            new Dictionary<int, WhitespaceControl>();

        /// <summary>
        /// The whitespace control a document read from one package is built with.
        /// </summary>
        /// <remarks>
        /// Whitespace stripping is local to a package (XSLT 3.0 §3.6.5), so a library that declares nothing
        /// preserves everything however much the package using it strips, and one that declares something
        /// keeps it to itself. A package with no declarations answers with the shared control that strips
        /// nothing, which is what lets two such packages share one copy of a document they both read.
        /// </remarks>
        /// <param name="package">The package the call was written in.</param>
        public WhitespaceControl WhitespaceIn(int package)
        {
            return PackageWhitespace.TryGetValue(package, out WhitespaceControl? control) && !control.IsEmpty
                ? control
                : WhitespaceControl.PreserveAll;
        }

        /// <summary>
        /// A number identifying the stripping a package applies, for keying documents already read.
        /// </summary>
        /// <remarks>
        /// Every package that strips nothing shares one, so a URI read from two such packages is read once
        /// and the two see the same nodes. A package that strips something has its own, because what it
        /// reads is a different document from what anyone else reads.
        /// </remarks>
        /// <param name="package">The package the call was written in.</param>
        public int WhitespaceScopeOf(int package)
        {
            return ReferenceEquals(WhitespaceIn(package), WhitespaceControl.PreserveAll) ? -1 : package;
        }

        /// <summary>The slot table mapping every tested name to an index.</summary>
        public NameSlotTable Names { get; }

        /// <summary>Every pattern in the stylesheet, paired with the template it selects.</summary>
        public IReadOnlyList<TemplateRule> Rules { get; }

        /// <summary>The global variables and parameters, in declaration order.</summary>
        public IReadOnlyList<GlobalVariable> Globals { get; }

        /// <summary>The number of slots global storage must provide.</summary>
        public int GlobalSlotCount { get; }

        /// <summary>The serialization method requested by <c>xsl:output</c>.</summary>
        public OutputMethod OutputMethod { get; }

        /// <summary>
        /// The stylesheet's own base URI, which <c>xml:base</c> on its outermost element may have moved.
        /// </summary>
        /// <remarks>
        /// It is the base URI of every node the stylesheet builds. A node in a result tree gets its base URI
        /// from the stylesheet element that created it, so a relative reference written into a result is
        /// relative to the stylesheet rather than to whatever the result is later saved as.
        /// </remarks>
        public string? BaseUri { get; init; }
    }
}
